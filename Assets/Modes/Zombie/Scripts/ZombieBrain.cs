using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using VortexArena.Core.Arena;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Modes.Zombie
{
    /// <summary>The zombie's BRAIN: walks it on the NavMesh, picks a target, times the melee and streams
    /// the pose (<c>0x09</c>). Runs on ONE machine only — the owner's, i.e. the object host admin
    /// (§10.10) — and does nothing at all on every other headset.</summary>
    /// <remarks>⚠️ Why the client: the server has no NavMesh, no colliders and no metres (§10.10); it
    /// owns the wave machine, the damage number and the health, and this component owns the movement.
    /// <para>⚠️ <b><c>NetObjectPoseSender</c> must NOT be put on this prefab.</b> That component
    /// measures "it stopped" and then sends <c>object_rest</c>, which ENDS ownership — a standing zombie
    /// would hand its own brain back and freeze for the rest of the match. Pose streaming is done here,
    /// without the rest contract.</para>
    /// <para>⚠️ The agent is DISABLED while the object is not ours: an enabled agent moves the transform
    /// and would fight the interpolated stream (<see cref="ZombieView"/>).</para>
    /// <para>Multi-floor arenas (<see cref="ArenaFloors"/>): the zombie only chases players on ITS
    /// storey, spawn points are spread over the storeys that hold players, and a zombie left alone on
    /// its storey relocates — the NavMesh has no link between floors, so without this a wave would
    /// pile up under an empty ceiling.</para></remarks>
    [RequireComponent(typeof(NetObject))]
    [DisallowMultipleComponent]
    public class ZombieBrain : MonoBehaviour
    {
        private static readonly float PoseSendInterval = 1f / ArenaProtocol.OBJECT_POSE_RATE_HZ;

        [Tooltip("Boş bırakılırsa aynı objeden alınır.")]
        [SerializeField] private NavMeshAgent agent;

        [Tooltip("Hedefe bu kadar kalınca durur (m) — vuruş mesafesinin biraz altı.")]
        [SerializeField] private float stoppingDistance = 1f;

        [Tooltip("Vuruş kararı ile olayın gönderilmesi arasındaki hazırlık (sn).")]
        [SerializeField] private float attackWindupSeconds = 0.4f;

        [Tooltip("Hedef kaç saniyede bir yeniden seçilir.")]
        [SerializeField] private float retargetSeconds = 0.25f;

        [Tooltip("Doğum noktasına oturtmak için NavMesh arama yarıçapı (m).")]
        [SerializeField] private float navSampleRadius = 4f;

        [Tooltip("Çok katlı arenada doğum noktası bu kattaki oyunculardan en az bu kadar uzak seçilir (m).")]
        [SerializeField] private float minSpawnDistance = 2.5f;

        [Tooltip("Çok katlı arenada katında hedef kalmayan zombi bu kadar bekleyince oyuncu olan bir " +
                 "kata taşınır (sn).")]
        [SerializeField] private float strandedRelocateSeconds = 6f;

        /// <summary>Multi-floor sample radius cap (m): storeys are ~3 m apart, so a wider search lands
        /// the point on the floor above.</summary>
        private const float MultiFloorSampleRadius = 1.5f;

        /// <summary>One player visible to the brain this frame.</summary>
        /// <remarks><see cref="Chaseable"/> false = spawn protected: no target, but still keeps a spawn
        /// point away from them.</remarks>
        private struct Seen
        {
            public int Id;
            public int Floor;
            public Vector3 Head;
            public bool Chaseable;
        }

        // Shared buffers: filled and read inside ONE call, so every brain can use the same lists
        // instead of allocating per frame.
        private static readonly List<Seen> SeenPlayers = new List<Seen>();
        private static readonly List<int> OccupiedBuffer = new List<int>();
        private static readonly List<Vector3> CandidateBuffer = new List<Vector3>();
        private static readonly List<Vector3> FarEnoughBuffer = new List<Vector3>();

        /// <summary>Round-robin cursor over the occupied storeys, shared by every brain: successive
        /// zombies of a wave then land on different floors instead of all picking the first one.</summary>
        private static int s_floorCursor;

        private NetObject _net;
        private readonly List<int> _playerIds = new List<int>();

        private float _poseTimer;
        private float _retargetTimer;

        /// <summary>Unscaled time the local cooldown expires; mirrors the server's (§10.5).</summary>
        private float _cooldownUntil;

        /// <summary>Unscaled time the pending attack event goes out; <c>0</c> = no attack in progress.</summary>
        private float _attackAt;

        private int _attackTargetId;

        /// <summary>We are the owner but the agent could not be placed — the zombie stands still and only
        /// streams its pose, so the others see a standing body instead of nothing at all.</summary>
        private bool _navMissing;

        /// <summary>Seconds our storey has been empty while somebody is chaseable elsewhere (multi-floor
        /// arenas only).</summary>
        private float _strandedFor;

        /// <summary>Did we already warp onto the spawn point for THIS ownership? ⚠️ Warping again on every
        /// state message would teleport the zombie back to its door mid-wave.</summary>
        private bool _placed;

        private void Awake()
        {
            _net = GetComponent<NetObject>();

            if (agent == null)
            {
                agent = GetComponent<NavMeshAgent>();
            }

            if (agent != null)
            {
                agent.enabled = false;
            }
        }

        private void OnEnable()
        {
            _net.OwnerChanged += HandleOwnerChanged;
            _net.StateChanged += HandleStateChanged;
            ApplyOwnership();
        }

        private void OnDisable()
        {
            _net.OwnerChanged -= HandleOwnerChanged;
            _net.StateChanged -= HandleStateChanged;
        }

        private void HandleOwnerChanged(NetObject _, int previousOwner)
        {
            // Ownership can move to another admin mid-match (§10.10): the new brain must start from where
            // the body IS, not from the spawn door, so placement is only redone from scratch when it has
            // never happened on this machine.
            ApplyOwnership();
        }

        private void HandleStateChanged(NetObject _, NetStateOrigin origin)
        {
            ApplyOwnership();
        }

        private void ApplyOwnership()
        {
            bool mine = _net.IsMine && !_net.IsBroken;

            if (agent == null)
            {
                return;
            }

            if (!mine)
            {
                if (agent.enabled)
                {
                    agent.enabled = false;
                }

                _attackAt = 0f;
                return;
            }

            agent.speed = ZombieKinds.SpeedOfWave(ZombieKinds.PayloadInt(_net, ZombieKinds.PayloadWave, 1));
            agent.stoppingDistance = stoppingDistance;

            if (!agent.enabled)
            {
                agent.enabled = true;
            }

            if (!_placed)
            {
                Place();
            }
        }

        /// <summary>Puts the body on its spawn point and onto the NavMesh; in a multi-floor arena the
        /// point is re-picked on a storey that holds players. The spawn point travels as a
        /// <c>netId</c> in the payload (§10.5) — the spawn pose on the wire is meaningless, because the
        /// server does not know where its own scene objects are.</summary>
        private void Place()
        {
            _placed = true;

            Vector3 target = transform.position;

            // ⚠️ The spawn door is only for the FIRST brain. On a hand-over (§10.10) the previous owner's
            // stream already put the body where it belongs, and warping it home would teleport the zombie
            // back to the fence in the middle of a wave. "Have we ever seen a pose" is the one available
            // discriminator — the server relays even our own poses back.
            RemoteObjectRegistry streams = ArenaClient.Instance != null
                ? ArenaClient.Instance.RemoteObjects
                : null;
            bool fresh = streams == null || !streams.IsStreaming(_net.NetId);

            int spawnNetId = ZombieKinds.PayloadInt(_net, ZombieKinds.PayloadSpawn);
            NetObject serverPoint = null;
            if (fresh && spawnNetId > 0 && NetObjectRegistry.TryGet(spawnNetId, out NetObject spawnPoint))
            {
                serverPoint = spawnPoint;
                target = spawnPoint.transform.position;
            }

            // ⚠️ The server picks the spawn point BLIND — it knows neither metres nor storeys (§10.10).
            // On a multi-floor arena that means every zombie of the wave can come out on an empty
            // storey, and the NavMesh has no way up.
            if (fresh && ArenaFloors.Count > 1)
            {
                CollectSeen();
                if (CollectOccupiedFloors() &&
                    TryPickSpawnPoint(NextOccupiedFloor(), serverPoint, out Vector3 spread))
                {
                    target = spread;
                }
            }

            if (TrySample(target, out Vector3 sampled))
            {
                agent.Warp(sampled);
            }
            else
            {
                transform.position = target;
            }

            // ⚠️ Zombies arrive from OUTSIDE the play area, so "is it on the NavMesh" is the only sane
            // check here — a boundary test would reject every legitimate spawn.
            _navMissing = !agent.isOnNavMesh;
            if (_navMissing)
            {
                Debug.LogWarning("[Zombie] NavMesh bulunamadı — zombi olduğu yerde duruyor. " +
                                 "Arena sahnesinde NavMesh bake'i (dış yollar dahil) eksik.", this);
            }
        }

        private void Update()
        {
            if (_net == null || _net.NetId <= 0 || !_net.IsMine)
            {
                return;
            }

            if (_net.IsBroken)
            {
                if (agent != null && agent.enabled)
                {
                    agent.enabled = false;
                }

                return;
            }

            StreamPose();

            if (_navMissing || agent == null || !agent.enabled || !agent.isOnNavMesh)
            {
                return;
            }

            if (_attackAt > 0f)
            {
                FinishAttack();
                return;
            }

            Hunt();
        }

        /// <summary>Walks toward the nearest chaseable player ON OUR STOREY and starts a melee when
        /// close enough; with nobody on it, relocates to a storey that has players.</summary>
        private void Hunt()
        {
            _retargetTimer -= Time.deltaTime;

            if (!TryFindTarget(out int targetId, out Vector3 targetFloor))
            {
                agent.isStopped = true;

                if (ArenaFloors.Count > 1)
                {
                    HandleStranded();
                }

                return;
            }

            _strandedFor = 0f;

            if (_retargetTimer <= 0f)
            {
                _retargetTimer = retargetSeconds;
                agent.isStopped = false;
                agent.SetDestination(targetFloor);
            }

            if (Time.time < _cooldownUntil)
            {
                return;
            }

            Vector3 flat = targetFloor - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > ZombieKinds.AttackReach * ZombieKinds.AttackReach)
            {
                return;
            }

            _attackTargetId = targetId;
            _attackAt = Time.time + attackWindupSeconds;
            agent.isStopped = true;

            // Turn to the target: the lunge comes from the view and would otherwise swing sideways.
            if (flat.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            }
        }

        /// <summary>Windup over: the attack goes out as an <c>object_event</c>. ⚠️ No damage number rides
        /// it — the server reads that from the wave table, so a tampered client cannot pick it
        /// (§10.5).</summary>
        private void FinishAttack()
        {
            if (Time.time < _attackAt)
            {
                return;
            }

            _attackAt = 0f;
            _cooldownUntil = Time.time + ZombieKinds.AttackCooldownSeconds;

            if (_attackTargetId > 0)
            {
                NetObjectSync.SendEvent(_net.NetId, ZombieKinds.EventAttack, new[] { _attackTargetId });
            }

            _attackTargetId = 0;
        }

        /// <summary>Nearest player who is alive, calibrated, not spawn protected and ON OUR STOREY;
        /// their HEAD pose projected onto our own floor level (the agent walks on a plane, the head does
        /// not).</summary>
        /// <remarks>⚠️ Floor filter only in a multi-floor arena: there the NavMesh has no link between
        /// storeys, so a target upstairs is a path the agent can never finish.</remarks>
        private bool TryFindTarget(out int playerId, out Vector3 floorPosition)
        {
            playerId = 0;
            floorPosition = Vector3.zero;

            if (!CollectSeen())
            {
                return false;
            }

            bool multiFloor = ArenaFloors.Count > 1;
            int myFloor = multiFloor ? ArenaFloors.FloorAt(transform.position.y) : 0;
            float best = float.MaxValue;

            for (int i = 0; i < SeenPlayers.Count; i++)
            {
                Seen seen = SeenPlayers[i];
                if (!seen.Chaseable || (multiFloor && seen.Floor != myFloor))
                {
                    continue;
                }

                Vector3 world = seen.Head;
                world.y = transform.position.y;

                float distance = (world - transform.position).sqrMagnitude;
                if (distance >= best)
                {
                    continue;
                }

                best = distance;
                playerId = seen.Id;
                floorPosition = world;
            }

            return playerId > 0;
        }

        /// <summary>Fills <see cref="SeenPlayers"/> with every alive, calibrated player that has a pose;
        /// <c>false</c> = nobody (or no connection).</summary>
        private bool CollectSeen()
        {
            SeenPlayers.Clear();

            ArenaClient client = ArenaClient.Instance;
            RemotePlayerRegistry remotes = client != null ? client.Remotes : null;
            if (remotes == null)
            {
                return false;
            }

            remotes.GetActivePlayerIds(_playerIds);

            for (int i = 0; i < _playerIds.Count; i++)
            {
                int id = _playerIds[i];
                if (!ZombieRoster.CanBeChased(id) || !remotes.IsAlive(id))
                {
                    continue;
                }

                if (!remotes.GetInterpolatedPose(id, out Pose head, out _, out _))
                {
                    continue;
                }

                Vector3 world = ArenaSpace.ArenaToWorld(head.position);
                SeenPlayers.Add(new Seen
                {
                    Id = id,
                    Floor = FloorOf(id, world),
                    Head = world,
                    Chaseable = !remotes.IsSpawnProtected(id),
                });
            }

            return SeenPlayers.Count > 0;
        }

        /// <summary>Storey a player stands on.</summary>
        /// <remarks>The roster lags a hop by a few frames, so a head already below its level takes the
        /// storey it is actually on (<c>ArenaCombat.TryScoreBody</c> does the same).</remarks>
        private static int FloorOf(int playerId, in Vector3 headWorld)
        {
            int floor = FloorState.PlayerFloor(playerId);
            if (headWorld.y < ArenaFloors.HeightOf(floor))
            {
                floor = ArenaFloors.FloorAt(headWorld.y);
            }

            return floor;
        }

        /// <summary>Our storey is empty: after <see cref="strandedRelocateSeconds"/> the body is warped
        /// to a spawn point on a storey that holds players. Multi-floor arenas only.</summary>
        /// <remarks>Reads the player buffer <see cref="TryFindTarget"/> has just filled.</remarks>
        private void HandleStranded()
        {
            if (!CollectOccupiedFloors())
            {
                _strandedFor = 0f;
                return;
            }

            _strandedFor += Time.deltaTime;
            if (_strandedFor < strandedRelocateSeconds)
            {
                return;
            }

            _strandedFor = 0f;

            // No server-point preference here: that point is what left us stranded.
            if (!TryPickSpawnPoint(NextOccupiedFloor(), null, out Vector3 point) ||
                !TrySample(point, out Vector3 sampled))
            {
                return;
            }

            agent.Warp(sampled);

            // The windup was aimed at a target we are no longer next to.
            _attackAt = 0f;
            _attackTargetId = 0;
        }

        /// <summary>Fills <see cref="OccupiedBuffer"/> with the distinct storeys (ascending) holding a
        /// chaseable player; <c>false</c> = nobody anywhere.</summary>
        private static bool CollectOccupiedFloors()
        {
            OccupiedBuffer.Clear();

            for (int i = 0; i < SeenPlayers.Count; i++)
            {
                Seen seen = SeenPlayers[i];
                if (!seen.Chaseable || OccupiedBuffer.Contains(seen.Floor))
                {
                    continue;
                }

                OccupiedBuffer.Add(seen.Floor);
            }

            OccupiedBuffer.Sort();
            return OccupiedBuffer.Count > 0;
        }

        /// <summary>Next occupied storey in round robin.</summary>
        private static int NextOccupiedFloor()
        {
            if (s_floorCursor < 0)
            {
                s_floorCursor = 0;
            }

            int index = s_floorCursor % OccupiedBuffer.Count;

            // Wrapped well below int.MaxValue: an overflow would turn the cursor negative.
            s_floorCursor = (s_floorCursor + 1) % 1000000;
            return OccupiedBuffer[index];
        }

        /// <summary>Picks a spawn point on <paramref name="floor"/>, at least
        /// <see cref="minSpawnDistance"/> (horizontal) from the players there; <c>false</c> = that storey
        /// has no spawn point at all.</summary>
        /// <remarks><paramref name="preferred"/> (the server's choice) wins when it is on the storey and
        /// far enough; otherwise a random far-enough point, and with none the farthest one — standing a
        /// zombie on top of a player is worse than a short walk.</remarks>
        private bool TryPickSpawnPoint(int floor, NetObject preferred, out Vector3 point)
        {
            point = Vector3.zero;

            if (preferred != null)
            {
                Vector3 position = preferred.transform.position;
                if (ArenaFloors.FloorAt(position.y) == floor &&
                    NearestPlayerDistance(position, floor) >= minSpawnDistance)
                {
                    point = position;
                    return true;
                }
            }

            CandidateBuffer.Clear();
            foreach (NetObject candidate in NetObjectRegistry.All)
            {
                if (candidate == null || candidate.Kind == null ||
                    candidate.Kind.Kind != ZombieKinds.SpawnPoint)
                {
                    continue;
                }

                Vector3 position = candidate.transform.position;
                if (ArenaFloors.FloorAt(position.y) == floor)
                {
                    CandidateBuffer.Add(position);
                }
            }

            FarEnoughBuffer.Clear();
            Vector3 farthest = Vector3.zero;
            float farthestDistance = -1f;

            for (int i = 0; i < CandidateBuffer.Count; i++)
            {
                Vector3 position = CandidateBuffer[i];
                float nearest = NearestPlayerDistance(position, floor);
                if (nearest >= minSpawnDistance)
                {
                    FarEnoughBuffer.Add(position);
                }
                else if (nearest > farthestDistance)
                {
                    farthestDistance = nearest;
                    farthest = position;
                }
            }

            if (FarEnoughBuffer.Count > 0)
            {
                point = FarEnoughBuffer[Random.Range(0, FarEnoughBuffer.Count)];
                return true;
            }

            if (farthestDistance >= 0f)
            {
                point = farthest;
                return true;
            }

            return false;
        }

        /// <summary>Horizontal (XZ) distance to the nearest player on <paramref name="floor"/>;
        /// <c>float.MaxValue</c> when the storey is empty.</summary>
        /// <remarks>Spawn protected players count too: they are about to start playing.</remarks>
        private static float NearestPlayerDistance(in Vector3 position, int floor)
        {
            float best = float.MaxValue;

            for (int i = 0; i < SeenPlayers.Count; i++)
            {
                Seen seen = SeenPlayers[i];
                if (seen.Floor != floor)
                {
                    continue;
                }

                float dx = seen.Head.x - position.x;
                float dz = seen.Head.z - position.z;
                float distance = Mathf.Sqrt(dx * dx + dz * dz);
                if (distance < best)
                {
                    best = distance;
                }
            }

            return best;
        }

        /// <summary>Puts a point onto the NavMesh; <c>false</c> = nothing within the search radius.</summary>
        /// <remarks>⚠️ The radius is capped in a multi-floor arena
        /// (<see cref="MultiFloorSampleRadius"/>): storeys are ~3 m apart and the default 4 m search
        /// happily returns a point on the floor above.</remarks>
        private bool TrySample(in Vector3 target, out Vector3 position)
        {
            float radius = ArenaFloors.Count > 1
                ? Mathf.Min(navSampleRadius, MultiFloorSampleRadius)
                : navSampleRadius;

            if (NavMesh.SamplePosition(target, out NavMeshHit hit, radius, NavMesh.AllAreas))
            {
                position = hit.position;
                return true;
            }

            position = target;
            return false;
        }

        /// <summary>Streams the pose at <c>OBJECT_POSE_RATE_HZ</c> — the only thing every other headset
        /// knows about this zombie.</summary>
        private void StreamPose()
        {
            _poseTimer -= Time.deltaTime;
            if (_poseTimer > 0f)
            {
                return;
            }

            _poseTimer = PoseSendInterval;

            ArenaClient client = ArenaClient.Instance;
            UdpStateChannel channel = client != null ? client.UdpChannel : null;
            if (channel == null)
            {
                return;
            }

            transform.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);
            channel.SendObjectPose(_net.NetId, ArenaSpace.WorldToArena(new Pose(position, rotation)));
        }
    }
}
