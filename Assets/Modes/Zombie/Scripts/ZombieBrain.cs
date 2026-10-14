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
    /// <para>⚠️ The TYPE comes from the server's payload (<c>t</c>, §10.5) and nothing here guesses it:
    /// speed, reach, cooldown, stop distance and placement all hang off it, and the view reads the same
    /// key — a second rule would make a body move like a brute and look like a walker.</para>
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

        // ------------------------------------------------------------------ per-type numbers

        /// <summary>A brute's and the Dev's bodies are wide: the normal 1 m stop walks them INTO the
        /// player and the melee then swings from inside the chest.</summary>
        private const float BruteStoppingDistance = 1.3f;

        private const float DevStoppingDistance = 1.8f;

        /// <summary>A runner's spawn door is kept this far from the nearest player (m) while a better
        /// door exists: at the cap it crosses a short gap before the squad can turn.</summary>
        private const float RunnerSpawnMin = 10f;

        /// <summary>The screamer stops this far from its target and drives the pack instead of hitting;
        /// it picks the chase back up past <see cref="ScreamerResumeDistance"/> (m).</summary>
        private const float ScreamerStandoffDistance = 7f;

        private const float ScreamerResumeDistance = 8.5f;

        /// <summary>A scream enrages the bodies this far around the screamer (m, flat, same storey).</summary>
        private const float EnrageRadius = 8f;

        private const float EnrageSeconds = 6f;

        private const float EnrageSpeedScale = 1.25f;

        /// <summary>The Dev stands still while it screams (s) — the summoned walkers appear in the same
        /// moment and the roar has to be readable.</summary>
        private const float DevScreamHoldSeconds = 1.5f;

        /// <summary>A summoned walker comes out this far from the Dev (m).</summary>
        private const float SummonRadiusMin = 1.5f;

        private const float SummonRadiusMax = 3f;

        private const int SummonPlaceAttempts = 8;

        /// <summary>The Dev's own spawn band (m): far enough not to materialise on the squad, close
        /// enough that it is seen coming.</summary>
        private const float DevSpawnMin = 6f;

        private const float DevSpawnMax = 15f;

        /// <summary>No chaseable player on the Dev's storey for this long (s) → it moves to the busiest
        /// one. The boss must not spend a run alone in a corridor.</summary>
        private const float DevStrandedSeconds = 8f;

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

        /// <summary>Type from the payload (§10.5) — the server's choice, re-read on every state because
        /// the payload may arrive after the object does.</summary>
        private string _type = ZombieKinds.TypeWalker;

        /// <summary>Last accepted scream counter; the scream fires on its CHANGE, like the attack.</summary>
        private int _screamCounter;

        /// <summary>Player we are walking at right now — a screamer hands it to the bodies it enrages, so
        /// the pack converges instead of scattering.</summary>
        private int _targetId;

        /// <summary>Enraged by a scream until this time, chasing <see cref="_forcedTargetId"/>
        /// (<c>0</c> = normal targeting).</summary>
        private float _enrageUntil;
        private int _forcedTargetId;

        /// <summary>Screamer: holding its distance instead of closing in. Hysteresis, not a threshold —
        /// one distance would make it step in and out of the stand-off every frame.</summary>
        private bool _standoff;

        /// <summary>Dev: time its storey went empty; <c>0</c> = there is someone to chase.</summary>
        private float _devAloneSince;

        // string == is ordinal here; `using System` is deliberately absent (it would make Random
        // ambiguous with UnityEngine.Random all over this file).
        private bool IsDev => _type == ZombieKinds.TypeDev;

        private bool IsScreamer => _type == ZombieKinds.TypeScreamer;

        private void Awake()
        {
            _net = GetComponent<NetObject>();
            _type = ZombieKinds.TypeOf(_net);
            _screamCounter = ZombieKinds.PayloadInt(_net, ZombieKinds.PayloadScream);

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
            CheckScream(origin);
        }

        private void ApplyOwnership()
        {
            _type = ZombieKinds.TypeOf(_net);

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
            agent.stoppingDistance = StoppingDistance();
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

        /// <summary>Walking speed for this body: the type's pace in this wave, raised while a scream is
        /// on us. ⚠️ Comes from the SHARED rule (<see cref="ZombieKinds.SpeedOf"/>) — the view stretches
        /// the clip to the same number, and a body moving faster than its clip skates.</summary>
        private float WaveSpeed()
        {
            float speed = ZombieKinds.SpeedOf(_type,
                ZombieKinds.PayloadInt(_net, ZombieKinds.PayloadWave, 1));

            if (Time.time < _enrageUntil)
            {
                speed = Mathf.Min(ZombieKinds.SpeedCap, speed * EnrageSpeedScale);
            }

            return speed;
        }

        private float StoppingDistance()
        {
            if (IsDev)
            {
                return DevStoppingDistance;
            }

            return _type == ZombieKinds.TypeBrute ? BruteStoppingDistance : stoppingDistance;
        }

        private float SpawnHold() =>
            ZombieKinds.IsCrawlerType(_type) ? crawlerSpawnHoldSeconds : spawnHoldSeconds;

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

            // The boss and the walkers it summons ignore the spawn doors entirely: one has to come out
            // in front of the squad, the others next to the body that called them.
            if (IsDev && TryPlaceDev())
            {
                return;
            }

            int summoner = ZombieKinds.PayloadInt(_net, ZombieKinds.PayloadSummoner);
            if (summoner > 0 && TryPlaceNearSummoner(summoner))
            {
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
                _targetId = 0;

                if (IsDev)
                {
                    HandleDevStranded();
                }
                else if (ArenaFloors.Count > 1)
                {
                    HandleStranded();
                }
                else
                {
                    agent.isStopped = true;
                }

                return;
            }

            _targetId = targetId;
            _devAloneSince = 0f;

            if (_wandering)
            {
                _wandering = false;
                agent.speed = WaveSpeed();
            }

            // ⚠️ Next stranding draws a FRESH delay: a stale one has expired for every chaser at once and
            // the pack would relocate in the same frame.
            _relocateCheckAt = 0f;

            Vector3 flat = targetFloor - transform.position;
            flat.y = 0f;
            float distance = flat.magnitude;

            if (IsScreamer && UpdateStandoff(distance))
            {
                // Holds its ground and keeps facing the target: the scream is a crowd signal, and a
                // screamer in the melee dies before it can give one.
                if (!agent.isStopped)
                {
                    agent.isStopped = true;
                }

                FaceTarget(flat);
            }
            else if (_retargetTimer <= 0f)
            {
                _retargetTimer = retargetSeconds;
                agent.speed = WaveSpeed();
                agent.isStopped = false;
                agent.SetDestination(targetFloor);
            }

            if (Time.time < _cooldownUntil)
            {
                return;
            }

            float reach = ZombieKinds.ReachOf(_type);
            if (distance > reach)
            {
                return;
            }

            _attackTargetId = targetId;
            _attackAt = Time.time + attackWindupSeconds;
            agent.isStopped = true;

            // Turn to the target: the lunge comes from the view and would otherwise swing sideways.
            FaceTarget(flat);
        }

        private void FaceTarget(in Vector3 flat)
        {
            if (flat.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            }
        }

        /// <summary>Screamer hysteresis: <c>true</c> = hold the current spot.</summary>
        private bool UpdateStandoff(float distance)
        {
            if (_standoff)
            {
                if (distance > ScreamerResumeDistance)
                {
                    _standoff = false;
                }
            }
            else if (distance <= ScreamerStandoffDistance)
            {
                _standoff = true;
            }

            // Inside reach the stand-off is off anyway: the target walked into it, and a screamer that
            // refuses to hit back is a free kill.
            return _standoff && distance > ZombieKinds.ReachOf(_type);
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
            _cooldownUntil = Time.time + ZombieKinds.CooldownOf(_type);

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

            // A scream overrides the nearest-target rule for its duration: the point of the enrage is
            // that the pack converges on ONE player instead of each picking its own.
            int forced = Time.time < _enrageUntil ? _forcedTargetId : 0;

            for (int i = 0; i < SeenPlayers.Count; i++)
            {
                Seen seen = SeenPlayers[i];
                if (!seen.Chaseable || (multiFloor && seen.Floor != myFloor) ||
                    (forced > 0 && seen.Id != forced))
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

            if (playerId == 0 && forced > 0)
            {
                // The forced target went down, upstairs or into spawn protection: back to normal
                // targeting, or the enrage would leave the body standing for its whole duration.
                _forcedTargetId = 0;
                return TryFindTarget(out playerId, out floorPosition);
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

        // ------------------------------------------------------------------ scream

        /// <summary>The scream counter moved (§10.5): a screamer sets the bodies around it off, the Dev
        /// roars in place while the server drops its walkers in.</summary>
        /// <remarks>⚠️ The counter is followed on EVERY state, ours or not, Live or snapshot — only the
        /// reaction is Live-only. A snapshot that is not absorbed leaves the brain behind, and the next
        /// live state then replays a scream that is long over.</remarks>
        private void CheckScream(NetStateOrigin origin)
        {
            int counter = ZombieKinds.PayloadInt(_net, ZombieKinds.PayloadScream, _screamCounter);
            if (counter <= _screamCounter)
            {
                return;
            }

            _screamCounter = counter;

            if (origin != NetStateOrigin.Live || !_net.IsMine || _net.IsBroken)
            {
                return;
            }

            if (IsDev)
            {
                _holdUntil = Mathf.Max(_holdUntil, Time.time + DevScreamHoldSeconds);
                _attackAt = 0f;
                _attackTargetId = 0;
                return;
            }

            if (IsScreamer)
            {
                SpreadEnrage();
            }
        }

        /// <summary>Enrages every other body of ours within <see cref="EnrageRadius"/> on OUR storey.
        /// The Dev is immune: a boss that sprints is not readable, and its pace is its whole
        /// silhouette.</summary>
        private void SpreadEnrage()
        {
            int myFloor = MyFloor();
            Vector3 position = transform.position;

            for (int i = 0; i < LiveBrains.Count; i++)
            {
                ZombieBrain brain = LiveBrains[i];
                if (brain == null || brain == this || brain.IsDev)
                {
                    continue;
                }

                if (ArenaFloors.Count > 1 && brain.MyFloor() != myFloor)
                {
                    continue;
                }

                float dx = brain.transform.position.x - position.x;
                float dz = brain.transform.position.z - position.z;
                if (dx * dx + dz * dz > EnrageRadius * EnrageRadius)
                {
                    continue;
                }

                brain.Enrage(_targetId);
            }
        }

        private void Enrage(int targetId)
        {
            _enrageUntil = Time.time + EnrageSeconds;
            _forcedTargetId = targetId;

            if (agent != null && agent.enabled && agent.isOnNavMesh)
            {
                agent.speed = WaveSpeed();
            }
        }

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

        /// <summary>The Dev has nobody to chase: it wanders, and after
        /// <see cref="DevStrandedSeconds"/> moves itself to the storey with the most players — it is
        /// OUTSIDE the quota, so nothing else would ever relocate it.</summary>
        private void HandleDevStranded()
        {
            Wander();

            float now = Time.time;
            if (_devAloneSince <= 0f)
            {
                _devAloneSince = now;
                return;
            }

            if (now - _devAloneSince < DevStrandedSeconds)
            {
                return;
            }

            _devAloneSince = 0f;

            if (!TryPlaceDev())
            {
                return;
            }

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

                // ⚠️ The Dev is not part of the wave's distribution: counting it would push a normal
                // zombie off the storey the boss is fighting on.
                if (brain == null || brain.IsDev)
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

        // ------------------------------------------------------------------ dev + summons

        /// <summary>Puts the Dev on the storey with the MOST players, inside its own band; <c>false</c> =
        /// nobody to go to (then the server's spawn door stands).</summary>
        private bool TryPlaceDev()
        {
            if (!CollectSeen() || !TryBusiestFloor(out int floor) ||
                !TryPickDevPoint(floor, out Vector3 point))
            {
                return false;
            }

            MoveTo(point, floor, false, true);
            return true;
        }

        /// <summary>Storey holding the most chaseable players; <c>false</c> = none anywhere.</summary>
        private static bool TryBusiestFloor(out int floor)
        {
            floor = 0;
            int floors = ArenaFloors.Count;

            if (floors <= 1)
            {
                for (int i = 0; i < SeenPlayers.Count; i++)
                {
                    if (SeenPlayers[i].Chaseable)
                    {
                        return true;
                    }
                }

                return false;
            }

            EnsureFloorBuffers(floors);

            for (int i = 0; i < SeenPlayers.Count; i++)
            {
                Seen seen = SeenPlayers[i];
                if (seen.Chaseable && seen.Floor >= 0 && seen.Floor < floors)
                {
                    s_playersOnFloor[seen.Floor]++;
                }
            }

            int best = 0;
            for (int f = 0; f < floors; f++)
            {
                if (s_playersOnFloor[f] > best)
                {
                    best = s_playersOnFloor[f];
                    floor = f;
                }
            }

            return best > 0;
        }

        /// <summary>Spawn point on <paramref name="floor"/> inside the Dev's band, else the farthest one.
        /// ⚠️ Not remembered in the recency ring: the boss spawns once, and burning a door for the whole
        /// wave would make the walkers come from somewhere else for no reason.</summary>
        private static bool TryPickDevPoint(int floor, out Vector3 point)
        {
            point = Vector3.zero;

            CollectCandidates(floor);
            if (CandidateBuffer.Count == 0)
            {
                return false;
            }

            ValidBuffer.Clear();
            float farthest = -1f;
            int fallback = -1;

            for (int i = 0; i < CandidateBuffer.Count; i++)
            {
                float nearest = NearestPlayerDistance(CandidateBuffer[i].Position, floor);
                if (nearest >= DevSpawnMin && nearest <= DevSpawnMax)
                {
                    ValidBuffer.Add(CandidateBuffer[i]);
                }

                if (nearest < float.MaxValue && nearest > farthest)
                {
                    farthest = nearest;
                    fallback = i;
                }
            }

            if (ValidBuffer.Count > 0)
            {
                point = ValidBuffer[Random.Range(0, ValidBuffer.Count)].Position;
                return true;
            }

            if (fallback < 0)
            {
                fallback = Random.Range(0, CandidateBuffer.Count);
            }

            point = CandidateBuffer[fallback].Position;
            return true;
        }

        /// <summary>A walker the Dev summoned (<c>n</c>, §10.5) comes out right next to it, not at a
        /// door; <c>false</c> = the Dev is already gone, so the normal placement runs.</summary>
        private bool TryPlaceNearSummoner(int summonerNetId)
        {
            if (!NetObjectRegistry.TryGet(summonerNetId, out NetObject dev) || dev == null)
            {
                return false;
            }

            Vector3 origin = dev.transform.position;
            int floor = ArenaFloors.Count > 1 ? ArenaFloors.FloorAt(origin.y) : 0;

            for (int attempt = 0; attempt < SummonPlaceAttempts; attempt++)
            {
                Vector2 direction = Random.insideUnitCircle.normalized;
                if (direction.sqrMagnitude < 0.0001f)
                {
                    continue;
                }

                Vector3 candidate = origin + new Vector3(direction.x, 0f, direction.y) *
                    Random.Range(SummonRadiusMin, SummonRadiusMax);

                if (!TrySample(candidate, out Vector3 sampled) ||
                    (ArenaFloors.Count > 1 && ArenaFloors.FloorAt(sampled.y) != floor))
                {
                    continue;
                }

                MoveTo(sampled, floor, false, true);
                return true;
            }

            return false;
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

            // A runner wants the far end of the band while one exists: at the speed cap a near door
            // puts it on the squad before anyone can turn around. Dropped with the first relax step.
            if (relax == 0 && _type == ZombieKinds.TypeRunner && nearest < RunnerSpawnMin)
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
