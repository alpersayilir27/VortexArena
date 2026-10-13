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
    /// storey — the NavMesh has no link between floors — so the whole wave is DISTRIBUTED by quota
    /// instead of by turns. Every brain on this machine registers itself, and the storey a new or a
    /// surplus zombie goes to is the one furthest below its share: the storeys holding players split the
    /// wave by player count, while the empty ones keep at most <c>fm</c> zombies (and only while the wave
    /// is still spawning, <c>rem</c> — see <see cref="ZombieWaveState"/>) so walking into an empty storey
    /// is not free. A zombie whose storey is empty wanders slowly instead of standing, and moves on at
    /// its own random interval.</para></remarks>
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

        [Tooltip("Oyuncu olan katta doğum noktası oyunculara en az bu kadar uzak seçilir (m).")]
        [SerializeField] private float spawnBandMin = 3f;

        [Tooltip("Oyuncu olan katta doğum noktası tercihen en çok bu kadar uzak seçilir (m) — " +
                 "uygun nokta yoksa bu üst sınır bırakılır.")]
        [SerializeField] private float spawnBandMax = 15f;

        [Tooltip("Katında hedef kalmayan zombinin dolaşma yarıçapı (m).")]
        [SerializeField] private float wanderRadius = 4f;

        [Tooltip("Dolaşırken yeni nokta seçme aralığı — en az (sn).")]
        [SerializeField] private float wanderIntervalMin = 3f;

        [Tooltip("Dolaşırken yeni nokta seçme aralığı — en çok (sn).")]
        [SerializeField] private float wanderIntervalMax = 7f;

        [Tooltip("Kat değiştirme denetimi aralığı — en az (sn). Her zombi kendi süresini çeker.")]
        [SerializeField] private float relocateDelayMin = 3f;

        [Tooltip("Kat değiştirme denetimi aralığı — en çok (sn).")]
        [SerializeField] private float relocateDelayMax = 10f;

        [Tooltip("Doğduktan/kat değiştirdikten sonra yerinde bekleme (sn) — ayağa kalkma " +
                 "animasyonunun okunacağı süre. ⚠️ ZombieAnimator'daki StandUp klibinden KISA olmaz.")]
        [SerializeField] private float spawnHoldSeconds = 2.8f;

        [Tooltip("Sürünen zombinin bekleme süresi (sn) — ayağa kalkmadığı için kısadır.")]
        [SerializeField] private float crawlerSpawnHoldSeconds = 1.2f;

        /// <summary>Multi-floor sample radius cap (m): storeys are ~3 m apart, so a wider search lands
        /// the point on the floor above.</summary>
        private const float MultiFloorSampleRadius = 1.5f;

        /// <summary>Share of a wave the empty storeys may hold at most — the rest belongs to the storeys
        /// that have players, or the squad never sees the wave it is fighting.</summary>
        private const float EmptyFloorShare = 0.4f;

        /// <summary>How many of the last used spawn points are avoided: the same door three times in a
        /// row reads as "the zombies always come from there".</summary>
        private const int RecentSpawnMemory = 3;

        /// <summary>A spawn point with another live zombie this close is skipped — two bodies in one
        /// doorway push each other off the NavMesh.</summary>
        private const float SpawnCrowdRadius = 1f;

        /// <summary>Jitter around the chosen spawn point (m): enough that two zombies from the same door
        /// do not overlap, short enough to stay in the doorway.</summary>
        private const float JitterMin = 0.5f;

        private const float JitterMax = 1f;

        /// <summary>Speed multiplier while wandering an empty storey: it must read as "lost", not as
        /// "hunting something I cannot see".</summary>
        private const float WanderSpeedScale = 0.6f;

        /// <summary>Chance that a wander tick keeps the zombie standing instead of picking a new
        /// destination.</summary>
        private const float WanderLingerChance = 0.35f;

        /// <summary>Shortest wander leg (m).</summary>
        private const float WanderMinStep = 1.2f;

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

        /// <summary>A scene spawn marker: the <c>netId</c> is carried along so the recency ring can name
        /// the point instead of comparing floats.</summary>
        private struct SpawnCandidate
        {
            public int NetId;
            public Vector3 Position;
        }

        // Shared buffers: filled and read inside ONE call, so every brain can use the same lists
        // instead of allocating per frame.
        private static readonly List<Seen> SeenPlayers = new List<Seen>();
        private static readonly List<SpawnCandidate> CandidateBuffer = new List<SpawnCandidate>();
        private static readonly List<SpawnCandidate> ValidBuffer = new List<SpawnCandidate>();

        /// <summary>Every brain that is ours, alive and placed — the only way to know how the wave is
        /// spread over the storeys, because the server counts no floors (§10.10).</summary>
        private static readonly List<ZombieBrain> LiveBrains = new List<ZombieBrain>();

        /// <summary>The last <see cref="RecentSpawnMemory"/> spawn point ids, shared by every brain.</summary>
        private static readonly List<int> RecentSpawns = new List<int>();

        // Per-storey scratch, resized with the arena: index = storey.
        private static int[] s_playersOnFloor = new int[0];
        private static int[] s_zombiesOnFloor = new int[0];
        private static float[] s_desiredOnFloor = new float[0];

        private static NavMeshPath s_pathScratch;

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

        /// <summary>Are we in <see cref="LiveBrains"/> right now?</summary>
        private bool _live;

        /// <summary>Stay put until this time: the body has just been warped onto a spawn point (or onto
        /// another storey) and the view is playing its stand-up. ⚠️ Moving during it would slide the
        /// zombie across the floor in a get-up pose.</summary>
        private float _holdUntil;

        /// <summary>Walking a wander destination: the agent runs slower and the hunt has to put the wave
        /// speed back.</summary>
        private bool _wandering;

        /// <summary>Time of the next wander destination / of the next storey check. ⚠️ Each brain draws
        /// its OWN delay: a shared one makes the whole wave change floors in the same frame.</summary>
        private float _wanderAt;
        private float _relocateCheckAt;

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
            SetLive(false);
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
                SetLive(false);
                return;
            }

            agent.speed = WaveSpeed();
            agent.stoppingDistance = stoppingDistance;
            _wandering = false;

            if (!agent.enabled)
            {
                agent.enabled = true;
            }

            if (!_placed)
            {
                Place();
            }

            SetLive(true);
        }

        /// <summary>Walking speed for this body. ⚠️ A crawler is slowed by the SHARED rule
        /// (<see cref="ZombieKinds.IsCrawler"/>): the view derives the crawl animation from the same
        /// <c>netId</c>, and a body that crawls at walking speed skates across the floor.</summary>
        private float WaveSpeed()
        {
            float speed = ZombieKinds.SpeedOfWave(ZombieKinds.PayloadInt(_net, ZombieKinds.PayloadWave, 1));
            return ZombieKinds.IsCrawler(_net.NetId) ? speed * ZombieKinds.CrawlerSpeedScale : speed;
        }

        private float SpawnHold() =>
            ZombieKinds.IsCrawler(_net.NetId) ? crawlerSpawnHoldSeconds : spawnHoldSeconds;

        /// <summary>Puts the body on its spawn point and onto the NavMesh; in a multi-floor arena the
        /// storey comes from the quota, not from the server. The spawn point travels as a
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

            if (!fresh)
            {
                // Stay where the body is, but still make sure the agent sits on the NavMesh.
                MoveTo(target, MyFloor(), false, false);
                return;
            }

            int spawnNetId = ZombieKinds.PayloadInt(_net, ZombieKinds.PayloadSpawn);
            NetObject serverPoint = null;
            if (spawnNetId > 0 && NetObjectRegistry.TryGet(spawnNetId, out NetObject spawnPoint))
            {
                serverPoint = spawnPoint;
                target = spawnPoint.transform.position;
            }

            int floor = 0;

            // ⚠️ The server picks the spawn point BLIND — it knows neither metres nor storeys (§10.10).
            // On a multi-floor arena that means every zombie of the wave can come out on an empty
            // storey, and the NavMesh has no way up.
            if (ArenaFloors.Count > 1)
            {
                CollectSeen();
                if (TryPickFloor(LiveBrains.Count + 1, out floor) &&
                    TryPickSpawnPoint(floor, serverPoint, out Vector3 spread))
                {
                    target = spread;
                }
                else
                {
                    // Nobody chaseable anywhere (or the storey has no marker): the server's point stands.
                    floor = ArenaFloors.FloorAt(target.y);
                }
            }
            else if (serverPoint != null)
            {
                RememberSpawn(spawnNetId);
            }

            MoveTo(target, floor, true, true);
        }

        /// <summary>Warps onto <paramref name="point"/>, jittered when the jitter stays reachable on the
        /// same storey. <paramref name="hold"/> freezes the body for <see cref="SpawnHold"/> so the
        /// view's spawn animation has room — a hand-over must NOT hold, the body there is already
        /// mid-walk and would stall in front of the squad.</summary>
        private void MoveTo(in Vector3 point, int floor, bool jitter, bool hold)
        {
            Vector3 destination = jitter && TryJitter(point, floor, out Vector3 jittered)
                ? jittered
                : point;

            if (hold)
            {
                _holdUntil = Time.time + SpawnHold();
            }

            if (TrySample(destination, out Vector3 sampled))
            {
                agent.Warp(sampled);
            }
            else
            {
                transform.position = destination;
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

                SetLive(false);
                return;
            }

            StreamPose();

            if (_navMissing || agent == null || !agent.enabled || !agent.isOnNavMesh)
            {
                return;
            }

            // ⚠️ Before the attack windup too: a zombie that spawns inside reach would otherwise hit
            // on the first frame, while the view is still fading it in.
            if (Time.time < _holdUntil)
            {
                if (!agent.isStopped)
                {
                    agent.isStopped = true;
                }

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
        /// close enough; with nobody on it, wanders and waits for its turn to change storey.</summary>
        private void Hunt()
        {
            _retargetTimer -= Time.deltaTime;

            if (!TryFindTarget(out int targetId, out Vector3 targetFloor))
            {
                if (ArenaFloors.Count > 1)
                {
                    HandleStranded();
                }
                else
                {
                    agent.isStopped = true;
                }

                return;
            }

            if (_wandering)
            {
                _wandering = false;
                agent.speed = WaveSpeed();
            }

            // ⚠️ Next stranding draws a FRESH delay: a stale one has expired for every chaser at once and
            // the pack would relocate in the same frame.
            _relocateCheckAt = 0f;

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
            int myFloor = MyFloor();
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

        private int MyFloor() => ArenaFloors.Count > 1 ? ArenaFloors.FloorAt(transform.position.y) : 0;

        // ------------------------------------------------------------------ stranded / quota

        /// <summary>Our storey has no chaseable player: the zombie WANDERS, and at its own random
        /// interval asks whether its storey is over quota — if it is, it moves to the storey furthest
        /// below its share. Multi-floor arenas only.</summary>
        /// <remarks>Reads the player buffer <see cref="TryFindTarget"/> has just filled.
        /// <para>⚠️ Wandering instead of standing still: a motionless row of zombies under an empty
        /// ceiling reads as a bug, and a quota run with nobody chaseable anywhere must not relocate
        /// anything.</para></remarks>
        private void HandleStranded()
        {
            Wander();

            float now = Time.time;
            if (_relocateCheckAt <= 0f)
            {
                _relocateCheckAt = now + Random.Range(relocateDelayMin, relocateDelayMax);
                return;
            }

            if (now < _relocateCheckAt)
            {
                return;
            }

            _relocateCheckAt = now + Random.Range(relocateDelayMin, relocateDelayMax);

            int myFloor = MyFloor();
            if (!ComputeQuota(LiveBrains.Count) || !IsSurplus(myFloor))
            {
                return;
            }

            // No server-point preference here: that point is what left us stranded.
            if (!TryBestDeficitFloor(out int floor) || floor == myFloor ||
                !TryPickSpawnPoint(floor, null, out Vector3 point))
            {
                return;
            }

            MoveTo(point, floor, true, true);

            // The windup was aimed at a target we are no longer next to.
            _attackAt = 0f;
            _attackTargetId = 0;

            _wandering = false;
            _wanderAt = 0f;
            agent.speed = WaveSpeed();
        }

        /// <summary>Slow random walk inside <see cref="wanderRadius"/> on our own storey.</summary>
        private void Wander()
        {
            float now = Time.time;
            if (now < _wanderAt)
            {
                return;
            }

            _wanderAt = now + Random.Range(wanderIntervalMin, wanderIntervalMax);

            // Sometimes just keep standing: a fixed walk-stop rhythm reads as one looping animation.
            if (_wandering && Random.value < WanderLingerChance)
            {
                _wanderAt += Random.Range(wanderIntervalMin, wanderIntervalMax);
                return;
            }

            if (!_wandering)
            {
                _wandering = true;
                agent.speed = WaveSpeed() * WanderSpeedScale;
            }

            // At least a few strides: a shuffle of a few centimetres is a start-stop jerk.
            Vector2 offset = Random.insideUnitCircle.normalized *
                             Random.Range(Mathf.Min(WanderMinStep, wanderRadius), wanderRadius);
            Vector3 candidate = transform.position + new Vector3(offset.x, 0f, offset.y);
            if (!TrySample(candidate, out Vector3 sampled) ||
                ArenaFloors.FloorAt(sampled.y) != MyFloor())
            {
                agent.isStopped = true;
                return;
            }

            agent.isStopped = false;
            agent.SetDestination(sampled);
        }

        /// <summary>Fills the per-storey quota for <paramref name="liveCount"/> zombies;
        /// <c>false</c> = nobody chaseable anywhere, so there is nothing to distribute.</summary>
        /// <remarks>Empty storeys hold <c>fm</c> zombies at most, and only while the wave is still
        /// spawning (<c>rem</c>); on top of that they never take more than
        /// <see cref="EmptyFloorShare"/> of the wave. What is left is split between the storeys that have
        /// players, proportional to how many players each one has.</remarks>
        private static bool ComputeQuota(int liveCount)
        {
            int floors = ArenaFloors.Count;
            EnsureFloorBuffers(floors);

            int totalPlayers = 0;
            for (int i = 0; i < SeenPlayers.Count; i++)
            {
                Seen seen = SeenPlayers[i];
                if (!seen.Chaseable || seen.Floor < 0 || seen.Floor >= floors)
                {
                    continue;
                }

                s_playersOnFloor[seen.Floor]++;
                totalPlayers++;
            }

            if (totalPlayers == 0)
            {
                return false;
            }

            for (int i = 0; i < LiveBrains.Count; i++)
            {
                ZombieBrain brain = LiveBrains[i];
                if (brain == null)
                {
                    continue;
                }

                int floor = ArenaFloors.FloorAt(brain.transform.position.y);
                if (floor >= 0 && floor < floors)
                {
                    s_zombiesOnFloor[floor]++;
                }
            }

            int emptyFloors = 0;
            for (int f = 0; f < floors; f++)
            {
                if (s_playersOnFloor[f] == 0)
                {
                    emptyFloors++;
                }
            }

            int emptyMin = ZombieWaveState.Remaining > 0 ? ZombieWaveState.FloorMin : 0;
            int perEmpty = emptyFloors > 0
                ? Mathf.Min(emptyMin, Mathf.FloorToInt(EmptyFloorShare * liveCount / emptyFloors))
                : 0;

            int forOccupied = Mathf.Max(0, liveCount - perEmpty * emptyFloors);

            for (int f = 0; f < floors; f++)
            {
                s_desiredOnFloor[f] = s_playersOnFloor[f] == 0
                    ? perEmpty
                    : forOccupied * (s_playersOnFloor[f] / (float)totalPlayers);
            }

            return true;
        }

        private static void EnsureFloorBuffers(int floors)
        {
            if (s_playersOnFloor.Length < floors)
            {
                s_playersOnFloor = new int[floors];
                s_zombiesOnFloor = new int[floors];
                s_desiredOnFloor = new float[floors];
            }

            for (int f = 0; f < floors; f++)
            {
                s_playersOnFloor[f] = 0;
                s_zombiesOnFloor[f] = 0;
                s_desiredOnFloor[f] = 0f;
            }
        }

        /// <summary>Storey furthest below its quota; ties go to a storey that HAS players (a zombie in
        /// front of a player is worth more than one in reserve), then to chance.</summary>
        private static bool TryBestDeficitFloor(out int floor)
        {
            floor = -1;
            float best = float.MinValue;
            int ties = 0;

            for (int f = 0; f < ArenaFloors.Count; f++)
            {
                float deficit = s_desiredOnFloor[f] - s_zombiesOnFloor[f];
                if (deficit <= 0f)
                {
                    continue;
                }

                bool occupied = s_playersOnFloor[f] > 0;
                bool bestOccupied = floor >= 0 && s_playersOnFloor[floor] > 0;

                if (deficit > best + 0.001f)
                {
                    best = deficit;
                    floor = f;
                    ties = 1;
                    continue;
                }

                if (deficit < best - 0.001f)
                {
                    continue;
                }

                if (occupied && !bestOccupied)
                {
                    floor = f;
                    ties = 1;
                    continue;
                }

                if (!occupied && bestOccupied)
                {
                    continue;
                }

                // Equal deficit and equal kind: reservoir pick, so a wave does not stack on the lowest
                // storey index.
                ties++;
                if (Random.Range(0, ties) == 0)
                {
                    floor = f;
                }
            }

            return floor >= 0;
        }

        /// <summary>Does our storey hold more zombies than its share?</summary>
        private static bool IsSurplus(int floor) =>
            floor >= 0 && floor < s_desiredOnFloor.Length &&
            s_zombiesOnFloor[floor] - s_desiredOnFloor[floor] > 0.001f;

        /// <summary>Storey a zombie should go to, counting <paramref name="liveCount"/> zombies in
        /// total; <c>false</c> = nobody chaseable anywhere.</summary>
        private static bool TryPickFloor(int liveCount, out int floor)
        {
            floor = -1;
            return ComputeQuota(liveCount) && TryBestDeficitFloor(out floor);
        }

        // ------------------------------------------------------------------ spawn point

        /// <summary>Picks a spawn point on <paramref name="floor"/>; <c>false</c> = that storey has no
        /// spawn point at all.</summary>
        /// <remarks><paramref name="preferred"/> (the server's choice) wins when it passes the same test.
        /// On a storey with players the point has to be inside the <see cref="spawnBandMin"/>–
        /// <see cref="spawnBandMax"/> band, out of the last <see cref="RecentSpawnMemory"/> used points
        /// and clear of other zombies; the conditions are dropped in that order when nothing qualifies,
        /// and the last resort is the point farthest from the players — standing a zombie on top of a
        /// player is worse than a long walk. An empty storey only avoids the recent points.</remarks>
        private bool TryPickSpawnPoint(int floor, NetObject preferred, out Vector3 point)
        {
            point = Vector3.zero;

            CollectCandidates(floor);
            if (CandidateBuffer.Count == 0 && preferred == null)
            {
                return false;
            }

            if (preferred != null)
            {
                Vector3 position = preferred.transform.position;
                if (ArenaFloors.FloorAt(position.y) == floor && IsAcceptable(position, floor, 0))
                {
                    point = position;
                    RememberSpawn(preferred.NetId);
                    return true;
                }
            }

            // 0: full test · 1: upper bound dropped · 2: recency dropped · 3: crowding dropped.
            // On an EMPTY storey level 0 and 1 are the same test: there is no player to measure a band
            // against, so only recency and crowding do any filtering there.
            for (int relax = 0; relax <= 3; relax++)
            {
                ValidBuffer.Clear();
                for (int i = 0; i < CandidateBuffer.Count; i++)
                {
                    if (IsAcceptable(CandidateBuffer[i].Position, floor, relax) &&
                        (relax >= 2 || !RecentSpawns.Contains(CandidateBuffer[i].NetId)))
                    {
                        ValidBuffer.Add(CandidateBuffer[i]);
                    }
                }

                if (ValidBuffer.Count > 0)
                {
                    SpawnCandidate pick = ValidBuffer[Random.Range(0, ValidBuffer.Count)];
                    point = pick.Position;
                    RememberSpawn(pick.NetId);
                    return true;
                }
            }

            // Farthest from the players: every condition failed, so distance is all that is left.
            float farthest = -1f;
            int index = -1;
            for (int i = 0; i < CandidateBuffer.Count; i++)
            {
                float distance = NearestPlayerDistance(CandidateBuffer[i].Position, floor);
                if (distance > farthest)
                {
                    farthest = distance;
                    index = i;
                }
            }

            if (index < 0)
            {
                return false;
            }

            point = CandidateBuffer[index].Position;
            RememberSpawn(CandidateBuffer[index].NetId);
            return true;
        }

        /// <summary>Spawn test at a relax level; recency is checked by the caller (the server's point
        /// goes through the same band and crowding test).</summary>
        private bool IsAcceptable(in Vector3 position, int floor, int relax)
        {
            float nearest = NearestPlayerDistance(position, floor);
            if (nearest < spawnBandMin)
            {
                return false;
            }

            if (relax == 0 && nearest > spawnBandMax && nearest < float.MaxValue)
            {
                return false;
            }

            return relax >= 3 || !HasZombieNear(position);
        }

        private static void CollectCandidates(int floor)
        {
            CandidateBuffer.Clear();

            foreach (NetObject candidate in NetObjectRegistry.All)
            {
                if (candidate == null || candidate.Kind == null ||
                    candidate.Kind.Kind != ZombieKinds.SpawnPoint)
                {
                    continue;
                }

                Vector3 position = candidate.transform.position;
                if (ArenaFloors.Count <= 1 || ArenaFloors.FloorAt(position.y) == floor)
                {
                    CandidateBuffer.Add(new SpawnCandidate
                    {
                        NetId = candidate.NetId,
                        Position = position,
                    });
                }
            }
        }

        /// <summary>Is another live zombie already standing on this point?</summary>
        private bool HasZombieNear(in Vector3 position)
        {
            for (int i = 0; i < LiveBrains.Count; i++)
            {
                ZombieBrain brain = LiveBrains[i];
                if (brain == null || brain == this)
                {
                    continue;
                }

                if ((brain.transform.position - position).sqrMagnitude <
                    SpawnCrowdRadius * SpawnCrowdRadius)
                {
                    return true;
                }
            }

            return false;
        }

        private static void RememberSpawn(int netId)
        {
            if (netId <= 0)
            {
                return;
            }

            RecentSpawns.Remove(netId);
            RecentSpawns.Add(netId);

            while (RecentSpawns.Count > RecentSpawnMemory)
            {
                RecentSpawns.RemoveAt(0);
            }
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

        // ------------------------------------------------------------------ navmesh helpers

        /// <summary>A point <see cref="JitterMin"/>–<see cref="JitterMax"/> away from the spawn point;
        /// <c>false</c> = no jitter, use the point itself.</summary>
        /// <remarks>⚠️ Only accepted when it stays on the SAME storey and a complete path runs back to
        /// the unjittered point: a jitter through a wall drops the zombie into the next room, and 1.5 m of
        /// NavMesh search reaches over a 3 m thin floor.</remarks>
        private bool TryJitter(in Vector3 point, int floor, out Vector3 jittered)
        {
            jittered = point;

            Vector2 direction = Random.insideUnitCircle.normalized;
            if (direction.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            float distance = Random.Range(JitterMin, JitterMax);
            Vector3 candidate = point + new Vector3(direction.x, 0f, direction.y) * distance;

            if (!TrySample(candidate, out Vector3 sampled))
            {
                return false;
            }

            if (ArenaFloors.Count > 1 && ArenaFloors.FloorAt(sampled.y) != floor)
            {
                return false;
            }

            s_pathScratch ??= new NavMeshPath();
            if (!NavMesh.CalculatePath(sampled, point, NavMesh.AllAreas, s_pathScratch) ||
                s_pathScratch.status != NavMeshPathStatus.PathComplete)
            {
                return false;
            }

            jittered = sampled;
            return true;
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

        // ------------------------------------------------------------------ registry + stream

        private void SetLive(bool value)
        {
            if (value == _live)
            {
                return;
            }

            _live = value;
            if (value)
            {
                LiveBrains.Add(this);
            }
            else
            {
                LiveBrains.Remove(this);
            }
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
