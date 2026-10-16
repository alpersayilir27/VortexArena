using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using VortexArena.Core.Arena;
using VortexArena.Core.Combat;
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

        /// <summary>Wanted free space around the spawn spot (m): the body spawns LYING (~1.5 m long)
        /// and a marker set against a wall pokes the model through it during the stand-up.</summary>
        private const float SpawnEdgeClearance = 0.9f;

        /// <summary>Ring offsets searched around the marker (m) — also the spawn jitter: two zombies
        /// from the same door must not overlap, but the spot has to stay in the doorway.</summary>
        private static readonly float[] SpawnRingRadii = { 0.5f, 0.9f, 1.3f };

        private const int SpawnRingDirections = 8;

        /// <summary>Two spots this close count as the same one (m).</summary>
        private const float SpawnSpotMerge = 0.2f;

        /// <summary>NavMesh search radius of a ring sample (m) — short on purpose: a wide search pulls
        /// the offset back onto the marker's own tile and the ring stops spreading.</summary>
        private const float SpawnSpotSampleRadius = 0.6f;

        /// <summary>Height a spot may differ from the marker (m): past this it is a ramp or the mesh
        /// under a balcony, not the marker's own floor.</summary>
        private const float SpawnSpotRise = 0.5f;

        /// <summary>Rise facing: axes tested and how far down each one free space is measured (m) — the
        /// lying body is ~1.5 m long, so anything past that buys nothing.</summary>
        private const int RiseFacingAxes = 8;

        private const float RiseFacingProbe = 1.6f;

        /// <summary>Free lengths this close count as a tie, and the nearest player breaks it.</summary>
        private const float RiseFacingTie = 0.15f;

        /// <summary>Avoidance priority band: every zombie gets its own number inside it, because equal
        /// priorities make the local avoidance treat the pair as one blob and they walk through
        /// each other.</summary>
        private const int AvoidancePriorityBase = 40;

        private const int AvoidancePrioritySpread = 20;

        /// <summary>Separation: bodies closer than the two radii plus this skin (m) push each other
        /// apart; the push is <see cref="SeparationStrength"/> m/s per metre of overlap, never more
        /// than <see cref="SeparationSpeedCap"/>.</summary>
        /// <remarks>⚠️ Local avoidance alone does not open a stack that is already overlapping — it
        /// steers, it does not separate.</remarks>
        private const float SeparationSkin = 0.1f;

        private const float SeparationStrength = 2f;

        private const float SeparationSpeedCap = 0.6f;

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

        /// <summary>The screamer stands still for its scream (s) — ⚠️ the length of <c>ZA_Scream</c>, or it
        /// slides off mid-cry; then it charges (<see cref="_charging"/>).</summary>
        private const float ScreamerScreamHoldSeconds = 2.8f;

        /// <summary>A scream enrages the bodies this far around the screamer (m, flat, same storey).</summary>
        private const float EnrageRadius = 8f;

        private const float EnrageSeconds = 6f;

        private const float EnrageSpeedScale = 1.25f;

        /// <summary>Stagger: how much pace a round costs and for how long. A leg hit costs the most —
        /// it is the one zone with a reason to slow a body down, and it gives the squad something to
        /// aim at besides the head.
        /// <para>⚠️ Short on purpose: a longer stagger plus a burst of fire pins a zombie in place,
        /// and the wave then never reaches the squad.</para></summary>
        private const float LegStaggerScale = 0.35f;

        private const float LegStaggerSeconds = 0.45f;

        private const float BodyStaggerScale = 0.7f;

        private const float BodyStaggerSeconds = 0.25f;

        private const float BossStaggerScale = 0.85f;

        private const float BossStaggerSeconds = 0.2f;

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

        // ------------------------------------------------------------------ targeting

        /// <summary>Target score is recomputed at a random interval in this band (s). ⚠️ RANDOM per
        /// brain: one shared tick would run every path query of the wave in the same frame.</summary>
        private const float RetargetMin = 0.5f;

        private const float RetargetMax = 1f;

        /// <summary>A freshly picked target is kept at least this long (s), and a rival only wins below
        /// <see cref="SwitchFactor"/> of its score — without both, two equally close players leave the
        /// body turning in place instead of walking.</summary>
        private const float TargetHoldSeconds = 2f;

        private const float SwitchFactor = 0.7f;

        /// <summary>A target we cannot see is worth this much more walking; every chaser over the
        /// storey's fair share costs this many metres.</summary>
        private const float BlindPenalty = 1.5f;

        private const float CrowdPenaltyMeters = 2f;

        /// <summary>A player who fired near us just now wins ties: the loud member of the squad is the
        /// one the wave should converge on.</summary>
        private const float ShooterBonus = 0.8f;

        private const float ShooterRadius = 15f;

        private const float ShooterSeconds = 3f;

        /// <summary>An unreachable target stays a candidate but only wins when nothing reachable
        /// exists; a partial path pays twice the gap it could not cover.</summary>
        private const float InvalidPathPenalty = 10f;

        private const float InvalidPathFactor = 2f;

        private const float PartialGapFactor = 2f;

        /// <summary>Per-FRAME ceiling for the WHOLE wave: path queries and LOS rays are the two costs
        /// that scale with zombies × players. A brain that finds the budget spent keeps its current
        /// target and asks again next frame — it never falls back to a worse answer.</summary>
        private const int PathBudgetPerFrame = 6;

        private const int RayBudgetPerFrame = 12;

        /// <summary>LOS answer is reused this long (s) per brain+player pair.</summary>
        private const float LosCacheSeconds = 0.5f;

        /// <summary>Eye height of the LOS ray (m, scaled by the body); a crawler looks along the
        /// floor.</summary>
        private const float EyeHeight = 1.6f;

        private const float CrawlerEyeHeight = 0.4f;

        /// <summary>A hit nearer than (distance − this) blocks the view; the margin keeps the wall right
        /// behind the player from counting as cover.</summary>
        private const float LosSkin = 0.3f;

        /// <summary>Attack re-check at the end of the windup: the reach plus the step a player can take
        /// during it. Past that the swing goes out as a MISS.</summary>
        private const float AttackEscapeSlack = 0.4f;

        /// <summary>Stuck watchdog: with a path longer than the stop distance + <see cref="StuckSlack"/>,
        /// <see cref="StuckSampleSeconds"/> must move the body at least <see cref="StuckProgress"/>
        /// metres.</summary>
        private const float StuckSampleSeconds = 8f;

        private const float StuckSlack = 0.5f;

        private const float StuckProgress = 0.5f;

        /// <summary>How strongly an idle wander leans toward the nearest player: at 1 it would walk a
        /// straight line at someone it is not allowed to chase.</summary>
        private const float WanderBias = 0.6f;

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

        /// <summary>A spot around the chosen marker with its distance to the nearest NavMesh edge.</summary>
        private struct SpawnSpot
        {
            public Vector3 Position;
            public float Clearance;
        }

        // Shared buffers: filled and read inside ONE call, so every brain can use the same lists
        // instead of allocating per frame.
        private static readonly List<Seen> SeenPlayers = new List<Seen>();
        private static readonly List<SpawnCandidate> CandidateBuffer = new List<SpawnCandidate>();
        private static readonly List<SpawnCandidate> ValidBuffer = new List<SpawnCandidate>();
        private static readonly List<SpawnSpot> SpotBuffer = new List<SpawnSpot>();

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

        /// <summary>LOS ray hits and path corners, shared: both are read inside one call.</summary>
        /// <remarks>⚠️ A truncated corner list only UNDERSTATES a very long path, which is harmless for
        /// a comparison between candidates.</remarks>
        private static readonly RaycastHit[] LosHits = new RaycastHit[8];

        private static readonly Vector3[] PathCorners = new Vector3[64];

        /// <summary>Attack with no target id = a miss (§10.5): the server applies the cooldown and bumps
        /// the counter everyone animates, but deals no damage.</summary>
        private static readonly int[] WhiffIds = new int[0];

        // Per-frame query budgets, shared by every brain on this machine.
        private static int s_budgetFrame = -1;
        private static int s_pathBudget;
        private static int s_rayBudget;

        /// <summary>One cached line-of-sight answer.</summary>
        private struct LosEntry
        {
            public int Id;
            public float Until;
            public bool Visible;
        }

        private NetObject _net;
        private readonly List<int> _playerIds = new List<int>();
        private readonly List<LosEntry> _los = new List<LosEntry>();

        /// <summary>One path object per brain: <c>NavMesh.CalculatePath</c> writes into it, so a shared
        /// one would be overwritten mid-comparison.</summary>
        private NavMeshPath _path;

        private readonly int[] _attackIds = new int[1];

        /// <summary>Agent radius as the prefab/agent type carries it, before the per-type scale: the
        /// scale is re-applied on every state, and scaling the already scaled value would grow the
        /// body a little further each time.</summary>
        private float _baseRadius;

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

        /// <summary>Screamer after its first scream: no more stand-off — it runs at the speed cap at
        /// <see cref="_chargeTargetId"/>, the nearest player when the scream ended.</summary>
        private bool _charging;
        private int _chargeTargetId;

        /// <summary>Dev: time its storey went empty; <c>0</c> = there is someone to chase.</summary>
        private float _devAloneSince;

        /// <summary>Score of <see cref="_targetId"/> and the time it was acquired — the hysteresis pair.</summary>
        private float _targetScore = float.MaxValue;
        private float _targetSince;

        /// <summary>Time of the next full score pass; drawn randomly so the wave spreads its path
        /// queries over the frames.</summary>
        private float _targetAt;

        /// <summary>Stuck watchdog: next sample time and the position at the last sample.</summary>
        private float _stuckAt;
        private Vector3 _stuckPos;

        /// <summary>Stagger: a round briefly slows the body. Time it expires and the factor
        /// <see cref="WaveSpeed"/> multiplies into the pace while it runs.</summary>
        private float _staggerUntil;
        private float _staggerScale = 1f;

        /// <summary>Is the stagger currently reflected in <c>agent.speed</c>? The speed is only written
        /// at a few points (retarget, wander, hunt) — without this the slowdown would start and end
        /// whenever the next of those happens to run, up to a retarget period late.</summary>
        private bool _staggerApplied;

        private ZombieReactionTuning _tuning;

        /// <summary>Direction of the last RELAYED hit report (§5.2) — the knockback travels along it,
        /// which is away from whoever fired.</summary>
        private Vector3 _lastHitDir;

        /// <summary>Burst accumulator: when the window opened and how much health has been lost in it.
        /// ⚠️ Measured on the hp the SERVER reports, not on hit reports: the admin never sees a damage
        /// number, and hp is the one signal that is authoritative anyway.</summary>
        private float _damageAt;
        private float _damageAccum;
        private float _lastSeenHp;

        /// <summary>Crippled (payload <c>cr</c>): the body crawls from here on — the crawler's pace and
        /// its eye height, while look, size and reach stay the type's own.</summary>
        private bool _crippled;

        /// <summary>Knockback in progress: direction (flat), speed and when it ends.</summary>
        private Vector3 _knockDir;
        private float _knockSpeed;
        private float _knockUntil;

        // string == is ordinal here; `using System` is deliberately absent (it would make Random
        // ambiguous with UnityEngine.Random all over this file).
        private bool IsDev => _type == ZombieKinds.TypeDev;

        private bool IsScreamer => _type == ZombieKinds.TypeScreamer;

        private void Awake()
        {
            _net = GetComponent<NetObject>();
            _type = ZombieKinds.TypeOf(_net);
            _screamCounter = ZombieKinds.PayloadInt(_net, ZombieKinds.PayloadScream);
            _tuning = ZombieReactionTuning.Live;

            if (agent == null)
            {
                agent = GetComponent<NavMeshAgent>();
            }

            if (agent != null)
            {
                _baseRadius = agent.radius;
                agent.enabled = false;
            }
        }

        private void OnEnable()
        {
            _net.OwnerChanged += HandleOwnerChanged;
            _net.StateChanged += HandleStateChanged;
            ArenaCombat.ObjectHit += HandleObjectHit;
            ApplyOwnership();
        }

        private void OnDisable()
        {
            _net.OwnerChanged -= HandleOwnerChanged;
            _net.StateChanged -= HandleStateChanged;
            ArenaCombat.ObjectHit -= HandleObjectHit;
            SetLive(false);
        }

        /// <summary>A round landed on this body: it loses pace for a moment. Owner side only — the
        /// agent exists nowhere else, and the others see the slowdown through the pose stream.</summary>
        /// <remarks>⚠️ Never during the spawn hold or an attack windup: both are LOCKED poses (the
        /// view is playing a stand-up or a lunge), and slowing the agent there slides the body out from
        /// under its own animation.</remarks>
        private void HandleObjectHit(ArenaCombat.ObjectHitInfo info)
        {
            if (info.NetId != _net.NetId || !_net.IsMine || _net.IsBroken)
            {
                return;
            }

            // Remembered even mid-windup: the burst stagger reads it from the hp drop, which arrives in
            // a separate message and carries no direction of its own.
            if (info.Direction.sqrMagnitude > 1e-6f)
            {
                _lastHitDir = info.Direction;
            }

            if (Time.time < _holdUntil || _attackAt > 0f)
            {
                return;
            }

            if (IsDev || _type == ZombieKinds.TypeBrute)
            {
                // A boss staggered like a walker is a boss the squad can pin against a wall.
                _staggerScale = BossStaggerScale;
                _staggerUntil = Time.time + BossStaggerSeconds;
            }
            else if ((HitZone)info.Zone == HitZone.Leg)
            {
                _staggerScale = LegStaggerScale;
                _staggerUntil = Time.time + LegStaggerSeconds;
            }
            else
            {
                _staggerScale = BodyStaggerScale;
                _staggerUntil = Time.time + BodyStaggerSeconds;
            }

            UpdateStagger();
        }

        /// <summary>Writes the stagger into the agent when it starts and takes it out when it ends.</summary>
        private void UpdateStagger()
        {
            bool active = Time.time < _staggerUntil;
            if (active == _staggerApplied)
            {
                return;
            }

            _staggerApplied = active;

            if (agent != null && agent.enabled)
            {
                agent.speed = _wandering ? WaveSpeed() * WanderSpeedScale : WaveSpeed();
            }
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
            CheckCripple();
            AccumulateDamage();
        }

        /// <summary>Crippled (§10.5 <c>cr</c>): the leg is gone, so this body now moves like a crawler.
        /// ⚠️ Only the PACE and the eye height change — the look, the size and the reach stay the
        /// type's, because the server still calls it a walker.</summary>
        private void CheckCripple()
        {
            if (_crippled || ZombieKinds.PayloadInt(_net, ZombieKinds.PayloadCrippled) != 1)
            {
                return;
            }

            _crippled = true;

            if (agent != null && agent.enabled)
            {
                agent.speed = WaveSpeed();
            }
        }

        /// <summary>Burst stagger: enough damage in a short window stops the body, cancels a swing and
        /// shoves it back a step. ⚠️ Fed by the hp the server reports, the only damage signal the admin
        /// has (hit reports carry no number off the shooter's headset).</summary>
        private void AccumulateDamage()
        {
            float hp = _net.Hp;

            if (!_net.IsMine || _net.IsBroken || _lastSeenHp <= 0f)
            {
                _lastSeenHp = hp;
                return;
            }

            float drop = _lastSeenHp - hp;
            _lastSeenHp = hp;

            if (drop <= 0.01f)
            {
                return;
            }

            float now = Time.time;
            if (now - _damageAt > _tuning.StaggerWindowSeconds)
            {
                // The window opens on the FIRST round of the burst, not on the last: it is "this much
                // damage this fast", not a rolling average.
                _damageAt = now;
                _damageAccum = 0f;
            }

            _damageAccum += drop;

            if (_damageAccum < StaggerThreshold())
            {
                return;
            }

            _damageAccum = 0f;
            Stagger();
        }

        /// <summary>Damage that staggers this body. The Dev never staggers — a boss the squad can pin
        /// against a wall is not a boss.</summary>
        private float StaggerThreshold()
        {
            if (IsDev)
            {
                return float.MaxValue;
            }

            return _type == ZombieKinds.TypeBrute
                ? _tuning.BruteStaggerDamage
                : _tuning.StaggerDamage;
        }

        private void Stagger()
        {
            // The spawn hold is a LOCKED pose (the view is playing a stand-up): shoving the body there
            // slides it out from under its own animation.
            if (Time.time < _holdUntil)
            {
                return;
            }

            // ⚠️ Cancelled BEFORE the windup ends, which is also before the event goes out: the attack
            // animation plays off the server's accepted counter (§10.5), so nothing is cut mid-swing —
            // the swing simply never happens, and the cooldown postpones the next one.
            if (_attackAt > 0f)
            {
                _attackAt = 0f;
                _attackTargetId = 0;
                _cooldownUntil = Time.time + _tuning.StaggerSeconds;
            }

            _staggerScale = _tuning.StaggerSpeedScale;
            _staggerUntil = Time.time + _tuning.StaggerSeconds;

            // Forces the write: a zone stagger may already be running, and UpdateStagger only touches
            // the agent when the ON/OFF state itself changes.
            _staggerApplied = false;
            UpdateStagger();

            StartKnockback();
        }

        private void StartKnockback()
        {
            Vector3 flat = _lastHitDir;
            flat.y = 0f;

            if (flat.sqrMagnitude < 1e-6f)
            {
                return;
            }

            _knockDir = flat.normalized;
            _knockSpeed = Mathf.Min(_tuning.KnockbackMeters / _tuning.KnockbackSeconds,
                _tuning.KnockbackSpeedCap);
            _knockUntil = Time.time + _tuning.KnockbackSeconds;
        }

        /// <summary>Slides the body back along the shot while the knockback lasts.</summary>
        /// <remarks>⚠️ Through <c>agent.Move</c>, never <c>transform.position</c>: Move keeps the body
        /// ON the NavMesh, so a push next to a ledge can never drop it off the mesh.
        /// <para>⚠️ Above the wave's 1,2 m/s pace on purpose (capped at
        /// <see cref="ZombieReactionTuning.KnockbackSpeedCap"/>): the speed cap exists so a zombie
        /// cannot outrun the squad, and this displacement moves AWAY from the players.</para></remarks>
        private void UpdateKnockback()
        {
            if (Time.time >= _knockUntil)
            {
                return;
            }

            agent.Move(_knockDir * (_knockSpeed * Time.deltaTime));
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
            ApplyBodySize();
            _wandering = false;

            // First score pass at a random offset: taking ownership of a whole wave at once would
            // otherwise line every brain's first pass up in one frame.
            if (_targetAt <= 0f)
            {
                _targetAt = Time.time + Random.Range(0f, RetargetMax);
            }

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

        /// <summary>Agent width and crowd behaviour for this type. ⚠️ The scale comes from the SHARED
        /// rule (<see cref="ZombieKinds.ScaleOf"/>) the view uses: a brute drawn at 1.3 and steered at
        /// 1.0 walks its shoulders through doorframes and into the bodies next to it.</summary>
        private void ApplyBodySize()
        {
            if (_baseRadius > 0f)
            {
                agent.radius = _baseRadius * ZombieKinds.ScaleOf(_type, _net.NetId);
            }

            // ⚠️ Equal priorities make the local avoidance merge the pair instead of steering around
            // it, so every body gets its own number out of the band.
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            agent.avoidancePriority = AvoidancePriorityBase +
                Mathf.Abs(_net.NetId) % AvoidancePrioritySpread;
        }

        /// <summary>Walking speed for this body: the type's pace in this wave, raised while a scream is
        /// on us. ⚠️ Comes from the SHARED rule (<see cref="ZombieKinds.SpeedOf"/>) — the view stretches
        /// the clip to the same number, and a body moving faster than its clip skates.</summary>
        private float WaveSpeed()
        {
            // A crippled body drags itself: the crawler's pace, so the view's crawl cycle does not skate.
            // A charging screamer runs: the cap is the run clip's pace, so the view does not skate.
            float speed = _charging && !_crippled
                ? ZombieKinds.SpeedCap
                : ZombieKinds.SpeedOf(_crippled ? ZombieKinds.TypeCrawler : _type,
                    ZombieKinds.PayloadInt(_net, ZombieKinds.PayloadWave, 1));

            if (Time.time < _enrageUntil)
            {
                speed = Mathf.Min(ZombieKinds.SpeedCap, speed * EnrageSpeedScale);
            }

            // ⚠️ Multiplied in HERE, not written over agent.speed somewhere else: the pace is set from
            // half a dozen places (retarget, wander, scream, relocate) and any of them would wipe a
            // separately written slowdown within a frame.
            if (Time.time < _staggerUntil)
            {
                speed *= _staggerScale;
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

        /// <summary>Warps onto <paramref name="point"/>; <paramref name="spread"/> first looks for a spot
        /// around it with room to stand up in. <paramref name="hold"/> freezes the body for
        /// <see cref="SpawnHold"/> so the view's spawn animation has room — a hand-over must NOT hold,
        /// the body there is already mid-walk and would stall in front of the squad.</summary>
        private void MoveTo(in Vector3 point, int floor, bool spread, bool hold)
        {
            Vector3 destination = spread && TryClearSpawnSpot(point, floor, out Vector3 clear)
                ? clear
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

            if (hold)
            {
                // Facing is decided HERE and then stays: the stand-up holds the body still and nothing
                // rotates it until the first chase step, so a bad yaw lies in a wall for ~3 s.
                ApplyRiseFacing();
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

            RefreshBudgets();

            if (_navMissing || agent == null || !agent.enabled || !agent.isOnNavMesh)
            {
                return;
            }

            UpdateKnockback();

            // ⚠️ Before the attack windup too: a zombie that spawns inside reach would otherwise hit
            // on the first frame, while the view is still fading it in.
            if (Time.time < _holdUntil)
            {
                if (!agent.isStopped)
                {
                    agent.isStopped = true;
                }

                // ⚠️ A held body makes no progress BY DESIGN: sampling through the hold would relocate
                // every zombie that just spawned.
                _stuckAt = 0f;
                return;
            }

            if (_attackAt > 0f)
            {
                _stuckAt = 0f;
                FinishAttack();
                return;
            }

            UpdateStagger();
            Hunt();
            Separate();
        }

        /// <summary>Pushes overlapping bodies apart by hand, on top of the agents' own avoidance.</summary>
        /// <remarks>⚠️ Local avoidance only STEERS: two bodies that are already inside each other
        /// (fresh spawns, a doorway, a crowd around one player) stay merged without this nudge.
        /// <para>O(n²) over the brains on this machine — the wave cap keeps that in the tens — and the
        /// loop allocates nothing, so it can run every frame.</para></remarks>
        private void Separate()
        {
            // An attack is a locked pose: shoving the body mid-windup makes the swing come from the side.
            if (_attackAt > 0f || !agent.enabled || !agent.isOnNavMesh)
            {
                return;
            }

            Vector3 position = transform.position;
            int myFloor = MyFloor();
            float myRadius = agent.radius;
            Vector3 push = Vector3.zero;

            for (int i = 0; i < LiveBrains.Count; i++)
            {
                ZombieBrain other = LiveBrains[i];
                if (other == null || other == this || other.agent == null || !other.agent.enabled)
                {
                    continue;
                }

                Vector3 theirs = other.transform.position;
                if (ArenaFloors.Count > 1 && ArenaFloors.FloorAt(theirs.y) != myFloor)
                {
                    continue;
                }

                float dx = position.x - theirs.x;
                float dz = position.z - theirs.z;
                float limit = myRadius + other.agent.radius + SeparationSkin;
                float squared = dx * dx + dz * dz;
                if (squared >= limit * limit)
                {
                    continue;
                }

                float distance = Mathf.Sqrt(squared);
                Vector3 away = distance > 0.0001f
                    ? new Vector3(dx / distance, 0f, dz / distance)
                    // Exactly stacked: any direction will do, as long as the two pick different ones.
                    : DirectionOf(_net.NetId);

                push += away * (limit - distance);
            }

            if (push.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Vector3 velocity = push * SeparationStrength;
            if (velocity.magnitude > SeparationSpeedCap)
            {
                velocity = velocity.normalized * SeparationSpeedCap;
            }

            agent.Move(velocity * Time.deltaTime);
        }

        /// <summary>Stable flat direction out of a netId.</summary>
        private static Vector3 DirectionOf(int netId)
        {
            float angle = netId * 2.39996f;
            return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
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
                    // ⚠️ A single-floor arena has no quota to fall back on: with everybody protected or
                    // downed the zombies would stand in a row, which reads as a frozen game. They drift
                    // toward the squad instead.
                    Wander(true);
                }

                CheckStuck();
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

            if (IsScreamer && !_charging && UpdateStandoff(distance))
            {
                // Holds its ground and keeps facing the target: the scream is a crowd signal, and a
                // screamer in the melee dies before it can give one.
                if (!agent.isStopped)
                {
                    agent.isStopped = true;
                }

                FaceTarget(flat);

                // The stand-off is a deliberate stop, not a blocked path.
                _stuckAt = 0f;
            }
            else
            {
                if (_retargetTimer <= 0f)
                {
                    _retargetTimer = retargetSeconds;
                    agent.speed = WaveSpeed();
                    agent.isStopped = false;
                    agent.SetDestination(targetFloor);
                }

                CheckStuck();
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

            int target = _attackTargetId;
            _attackTargetId = 0;

            if (target <= 0)
            {
                return;
            }

            // ⚠️ Re-checked AFTER the windup: those 0.4 s are the player's chance to step out, so a
            // swing into thin air goes out WITHOUT a target id and the server reads it as a miss. The
            // cooldown runs either way — a missed swing that cost nothing would be a free attack.
            if (IsStillInReach(target))
            {
                _attackIds[0] = target;
                NetObjectSync.SendEvent(_net.NetId, ZombieKinds.EventAttack, _attackIds);
                return;
            }

            NetObjectSync.SendEvent(_net.NetId, ZombieKinds.EventAttack, WhiffIds);
        }

        /// <summary>Is the windup's target still hittable (alive, chaseable, unprotected, our storey) and
        /// inside reach + <see cref="AttackEscapeSlack"/>?</summary>
        private bool IsStillInReach(int playerId)
        {
            if (!CollectSeen() ||
                !TryValidTarget(playerId, ArenaFloors.Count > 1, MyFloor(), out Vector3 point))
            {
                return false;
            }

            float dx = point.x - transform.position.x;
            float dz = point.z - transform.position.z;
            float reach = ZombieKinds.ReachOf(_type) + AttackEscapeSlack;
            return dx * dx + dz * dz <= reach * reach;
        }

        /// <summary>The player this body walks at: the lowest SCORE among the chaseable players on our
        /// storey (path length, cover, crowding and who just fired — numbers in
        /// <c>Docs/ArenaNet-Protokol.md</c> §10.5), with hysteresis on top. The returned point is the
        /// target's head projected onto our own floor level: the agent walks on a plane.</summary>
        /// <remarks>⚠️ Floor filter only in a multi-floor arena: there the NavMesh has no link between
        /// storeys, so a target upstairs is a path the agent can never finish.
        /// <para>⚠️ The score is recomputed only every <see cref="RetargetMin"/>–<see cref="RetargetMax"/>
        /// seconds and only inside the frame's query budget; in between (and when the budget is spent)
        /// the current target is returned unchanged.</para></remarks>
        private bool TryFindTarget(out int playerId, out Vector3 floorPosition)
        {
            playerId = 0;
            floorPosition = Vector3.zero;

            if (!CollectSeen())
            {
                _targetId = 0;
                return false;
            }

            bool multiFloor = ArenaFloors.Count > 1;
            int myFloor = MyFloor();
            float now = Time.time;

            // A charging screamer keeps its pick until that player drops out (down, protected,
            // upstairs), then takes the next nearest — no score, no path budget.
            if (_charging)
            {
                if (_chargeTargetId > 0 &&
                    TryValidTarget(_chargeTargetId, multiFloor, myFloor, out floorPosition))
                {
                    playerId = _chargeTargetId;
                    return true;
                }

                if (TryNearestTarget(multiFloor, myFloor, out playerId, out floorPosition))
                {
                    _chargeTargetId = playerId;
                    return true;
                }

                _chargeTargetId = 0;
                _targetId = 0;
                return false;
            }

            // A scream overrides the score for its duration: the point of the enrage is that the pack
            // converges on ONE player instead of each picking its own.
            int forced = now < _enrageUntil ? _forcedTargetId : 0;
            if (forced > 0)
            {
                if (TryValidTarget(forced, multiFloor, myFloor, out floorPosition))
                {
                    playerId = forced;
                    return true;
                }

                // The forced target went down, upstairs or into spawn protection: back to the score, or
                // the enrage would leave the body standing for its whole duration.
                _forcedTargetId = 0;
            }

            bool keep = _targetId > 0 && TryValidTarget(_targetId, multiFloor, myFloor, out floorPosition);
            if (!keep)
            {
                // Dead, downed, protected or upstairs: the switch is IMMEDIATE — the hold below only
                // exists to stop a body dithering between two live players.
                _targetId = 0;
                _targetScore = float.MaxValue;
            }

            if (keep && (now - _targetSince < TargetHoldSeconds || now < _targetAt ||
                         IsWithinReach(floorPosition)))
            {
                playerId = _targetId;
                return true;
            }

            if (s_pathBudget <= 0)
            {
                // Budget spent by the rest of the wave this frame: keep what we have, ask again next
                // frame. Only a body with no target at all falls back to flat distance.
                if (keep)
                {
                    playerId = _targetId;
                    return true;
                }

                return TryNearestTarget(multiFloor, myFloor, out playerId, out floorPosition);
            }

            _targetAt = now + Random.Range(RetargetMin, RetargetMax);

            int fairShare = FairShare(multiFloor, myFloor);
            float bestScore = float.MaxValue;

            // Last pass's score stands in when the budget runs out before the current target is
            // re-scored: comparing a rival against float.MaxValue would switch on anything.
            float currentScore = keep ? _targetScore : float.MaxValue;
            int best = 0;
            Vector3 bestPoint = Vector3.zero;

            for (int i = 0; i < SeenPlayers.Count; i++)
            {
                Seen seen = SeenPlayers[i];
                if (!seen.Chaseable || (multiFloor && seen.Floor != myFloor))
                {
                    continue;
                }

                if (s_pathBudget <= 0)
                {
                    break;
                }

                Vector3 point = FloorPointOf(seen.Head);
                float score = ScoreOf(seen, point, fairShare);

                if (seen.Id == _targetId)
                {
                    currentScore = score;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    best = seen.Id;
                    bestPoint = point;
                }
            }

            if (best == 0)
            {
                if (keep)
                {
                    playerId = _targetId;
                    return true;
                }

                _targetId = 0;
                return false;
            }

            // A rival has to be clearly better: an equally good one would swap back next pass.
            if (keep && best != _targetId && bestScore >= currentScore * SwitchFactor)
            {
                playerId = _targetId;
                _targetScore = currentScore;
                return true;
            }

            if (best != _targetId)
            {
                _targetSince = now;
            }

            _targetId = best;
            _targetScore = bestScore;
            playerId = best;
            floorPosition = bestPoint;
            return true;
        }

        /// <summary>Score of one candidate: walking distance, doubled-ish while it is out of sight, plus
        /// the crowding penalty, minus a bonus for the player who just fired at us. Lowest wins.</summary>
        private float ScoreOf(in Seen seen, in Vector3 point, int fairShare)
        {
            float score = PathLength(point) * (HasLineOfSight(seen) ? 1f : BlindPenalty);

            // Fair share: a player already chased by more than their share of the storey's bodies is
            // worth less, so a wave spreads over the squad instead of dog-piling one headset.
            int extra = Mathf.Max(0, ChasersOf(seen.Id) + 1 - fairShare);
            score += CrowdPenaltyMeters * extra;

            if (ZombieShotMemory.FiredNear(seen.Id, transform.position, ShooterRadius, ShooterSeconds))
            {
                score *= ShooterBonus;
            }

            return score;
        }

        /// <summary>Walking length to <paramref name="target"/> (m); consumes one path budget.</summary>
        /// <remarks>⚠️ Not flat distance: the point of the whole score is that a player behind a wall is
        /// FAR. An unreachable one still gets a number (<see cref="InvalidPathPenalty"/>) so a body with
        /// nowhere to walk still picks somebody and keeps moving.</remarks>
        private float PathLength(in Vector3 target)
        {
            Vector3 from = transform.position;
            float dx = target.x - from.x;
            float dz = target.z - from.z;
            float flat = Mathf.Sqrt(dx * dx + dz * dz);

            _path ??= new NavMeshPath();
            s_pathBudget--;

            if (!NavMesh.CalculatePath(from, target, agent.areaMask, _path) ||
                _path.status == NavMeshPathStatus.PathInvalid)
            {
                return flat * InvalidPathFactor + InvalidPathPenalty;
            }

            int count = _path.GetCornersNonAlloc(PathCorners);
            float length = 0f;
            for (int i = 1; i < count; i++)
            {
                length += Vector3.Distance(PathCorners[i - 1], PathCorners[i]);
            }

            if (_path.status == NavMeshPathStatus.PathPartial && count > 0)
            {
                length += PartialGapFactor * Vector3.Distance(PathCorners[count - 1], target);
            }

            return length;
        }

        /// <summary>Can this body see the player's head? Cached per player for
        /// <see cref="LosCacheSeconds"/>.</summary>
        /// <remarks>⚠️ With the frame's ray budget spent the cached (or default) answer is VISIBLE: an
        /// invented wall would send the whole wave the long way round.</remarks>
        private bool HasLineOfSight(in Seen seen)
        {
            float now = Time.time;

            for (int i = 0; i < _los.Count; i++)
            {
                if (_los[i].Id != seen.Id)
                {
                    continue;
                }

                if (now < _los[i].Until)
                {
                    return _los[i].Visible;
                }

                _los.RemoveAt(i);
                break;
            }

            if (s_rayBudget <= 0)
            {
                return true;
            }

            s_rayBudget--;
            bool visible = CastLineOfSight(seen.Head);
            _los.Add(new LosEntry { Id = seen.Id, Until = now + LosCacheSeconds, Visible = visible });
            return visible;
        }

        /// <summary>One eye → head ray. ⚠️ Player and zombie hitboxes sit on <c>Default</c> like the
        /// walls do, so they are skipped explicitly — otherwise the body in front of the target (or our
        /// own) would read as cover and nobody would ever be "seen".</summary>
        private bool CastLineOfSight(in Vector3 head)
        {
            Vector3 eye = transform.position;
            // A crippled body is on the ground too: its eye line has to come from there, or it "sees"
            // over the cover it is lying behind.
            eye.y += ZombieKinds.IsCrawlerType(_type) || _crippled
                ? CrawlerEyeHeight
                : EyeHeight * ZombieKinds.ScaleOf(_type, _net.NetId);

            Vector3 delta = head - eye;
            float distance = delta.magnitude;
            if (distance <= LosSkin)
            {
                return true;
            }

            int count = Physics.RaycastNonAlloc(eye, delta / distance, LosHits, distance,
                ArenaLayers.AreaTargetMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                if (LosHits[i].distance >= distance - LosSkin)
                {
                    continue;
                }

                Collider hit = LosHits[i].collider;
                if (hit == null || hit.transform.IsChildOf(transform) ||
                    hit.GetComponentInParent<VortexArena.Core.Player.RemoteHitBox>() != null)
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        /// <summary>How many OTHER bodies of ours are already walking at this player.</summary>
        private int ChasersOf(int playerId)
        {
            int count = 0;

            for (int i = 0; i < LiveBrains.Count; i++)
            {
                ZombieBrain brain = LiveBrains[i];
                if (brain != null && brain != this && brain._targetId == playerId)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Bodies per player on this storey, rounded up — the line above which a target counts
        /// as crowded.</summary>
        private static int FairShare(bool multiFloor, int floor)
        {
            int zombies = 0;
            int players = 0;

            for (int i = 0; i < LiveBrains.Count; i++)
            {
                ZombieBrain brain = LiveBrains[i];
                if (brain != null && (!multiFloor || brain.MyFloor() == floor))
                {
                    zombies++;
                }
            }

            for (int i = 0; i < SeenPlayers.Count; i++)
            {
                Seen seen = SeenPlayers[i];
                if (seen.Chaseable && (!multiFloor || seen.Floor == floor))
                {
                    players++;
                }
            }

            return players > 0 ? Mathf.CeilToInt(zombies / (float)players) : 1;
        }

        /// <summary>Is <paramref name="playerId"/> still a legal target (chaseable, our storey)?
        /// Reads the buffer <see cref="CollectSeen"/> filled, which already excludes the dead.</summary>
        private bool TryValidTarget(int playerId, bool multiFloor, int floor, out Vector3 floorPosition)
        {
            for (int i = 0; i < SeenPlayers.Count; i++)
            {
                Seen seen = SeenPlayers[i];
                if (seen.Id != playerId)
                {
                    continue;
                }

                if (seen.Chaseable && (!multiFloor || seen.Floor == floor))
                {
                    floorPosition = FloorPointOf(seen.Head);
                    return true;
                }

                break;
            }

            floorPosition = Vector3.zero;
            return false;
        }

        /// <summary>Flat-nearest pick — the budget-exhausted fallback for a body that has no target at
        /// all; standing still would read worse than walking at the wrong player.</summary>
        private bool TryNearestTarget(bool multiFloor, int floor, out int playerId,
            out Vector3 floorPosition)
        {
            playerId = 0;
            floorPosition = Vector3.zero;
            float best = float.MaxValue;

            for (int i = 0; i < SeenPlayers.Count; i++)
            {
                Seen seen = SeenPlayers[i];
                if (!seen.Chaseable || (multiFloor && seen.Floor != floor))
                {
                    continue;
                }

                Vector3 point = FloorPointOf(seen.Head);
                float distance = (point - transform.position).sqrMagnitude;
                if (distance >= best)
                {
                    continue;
                }

                best = distance;
                playerId = seen.Id;
                floorPosition = point;
            }

            if (playerId > 0)
            {
                _targetId = playerId;
                _targetSince = Time.time;
                _targetScore = float.MaxValue;
            }

            return playerId > 0;
        }

        /// <summary>A head pose dropped onto our own floor level.</summary>
        private Vector3 FloorPointOf(in Vector3 head)
        {
            Vector3 point = head;
            point.y = transform.position.y;
            return point;
        }

        private bool IsWithinReach(in Vector3 floorPoint)
        {
            float dx = floorPoint.x - transform.position.x;
            float dz = floorPoint.z - transform.position.z;
            float reach = ZombieKinds.ReachOf(_type);
            return dx * dx + dz * dz <= reach * reach;
        }

        private static void RefreshBudgets()
        {
            if (s_budgetFrame == Time.frameCount)
            {
                return;
            }

            s_budgetFrame = Time.frameCount;
            s_pathBudget = PathBudgetPerFrame;
            s_rayBudget = RayBudgetPerFrame;
        }

        // ------------------------------------------------------------------ stuck watchdog

        /// <summary>Walking but getting nowhere (another body in a doorway, a NavMesh island, a target
        /// behind geometry the agent cannot round): the body is warped to a spawn point on its own
        /// storey instead of pushing against the wall for the rest of the run.</summary>
        /// <remarks>⚠️ Only while there IS a path to walk: a stopped agent, a windup, the screamer's
        /// stand-off and every spawn hold reset the sample, or a deliberate stop would relocate the
        /// body.</remarks>
        private void CheckStuck()
        {
            if (agent.isStopped || agent.pathPending || !agent.hasPath ||
                agent.remainingDistance <= agent.stoppingDistance + StuckSlack)
            {
                _stuckAt = 0f;
                return;
            }

            float now = Time.time;
            Vector3 position = transform.position;

            if (_stuckAt <= 0f)
            {
                _stuckAt = now + StuckSampleSeconds;
                _stuckPos = position;
                return;
            }

            if (now < _stuckAt)
            {
                return;
            }

            _stuckAt = now + StuckSampleSeconds;

            bool moved = (position - _stuckPos).sqrMagnitude >= StuckProgress * StuckProgress;
            _stuckPos = position;

            if (moved)
            {
                return;
            }

            Relocate();
        }

        /// <summary>Warps a stuck body back onto a spawn door of its own storey (the Dev onto its own
        /// band) with the usual stand-up hold.</summary>
        private void Relocate()
        {
            _stuckAt = 0f;

            bool placed;
            if (IsDev)
            {
                placed = TryPlaceDev();
            }
            else
            {
                CollectSeen();
                int floor = MyFloor();
                placed = TryPickSpawnPoint(floor, null, out Vector3 point);
                if (placed)
                {
                    MoveTo(point, floor, true, true);
                }
            }

            if (!placed)
            {
                return;
            }

            Debug.Log("[Zombie] Zombi takıldı — doğum noktasına taşındı.", this);

            _attackAt = 0f;
            _attackTargetId = 0;
            _wandering = false;
            _wanderAt = 0f;
            agent.speed = WaveSpeed();
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

                // Cries in place, then charges the player nearest at the END of the cry: the target is
                // re-picked once the hold is over.
                _holdUntil = Mathf.Max(_holdUntil, Time.time + ScreamerScreamHoldSeconds);
                _attackAt = 0f;
                _attackTargetId = 0;
                _charging = true;
                _standoff = false;
                _chargeTargetId = 0;
                _retargetTimer = 0f;
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

        /// <summary>Slow random walk inside <see cref="wanderRadius"/> on our own storey;
        /// <paramref name="biased"/> leans the step toward the nearest player (protected or downed ones
        /// too) so an idle wave still drifts where the squad is.</summary>
        private void Wander(bool biased = false)
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

            Vector2 direction = Random.insideUnitCircle.normalized;
            if (biased && TryNearestDirection(out Vector2 toward))
            {
                Vector2 leaning = direction + toward * WanderBias;
                if (leaning.sqrMagnitude > 0.0001f)
                {
                    direction = leaning.normalized;
                }
            }

            // At least a few strides: a shuffle of a few centimetres is a start-stop jerk.
            Vector2 offset = direction *
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

        /// <summary>Flat unit direction to the nearest player on our storey, chaseable or not;
        /// <c>false</c> = nobody there.</summary>
        /// <remarks>Reads the buffer <see cref="CollectSeen"/> filled, and falls back to the DOWNED
        /// players (which that buffer drops): with the whole squad on the floor they are the only thing
        /// left to drift toward.</remarks>
        private bool TryNearestDirection(out Vector2 direction)
        {
            direction = Vector2.zero;
            float best = float.MaxValue;
            bool multiFloor = ArenaFloors.Count > 1;
            int myFloor = MyFloor();

            for (int i = 0; i < SeenPlayers.Count; i++)
            {
                Seen seen = SeenPlayers[i];
                if (multiFloor && seen.Floor != myFloor)
                {
                    continue;
                }

                Lean(seen.Head, ref best, ref direction);
            }

            if (best < float.MaxValue)
            {
                return true;
            }

            ArenaClient client = ArenaClient.Instance;
            RemotePlayerRegistry remotes = client != null ? client.Remotes : null;
            if (remotes == null)
            {
                return false;
            }

            for (int i = 0; i < _playerIds.Count; i++)
            {
                int id = _playerIds[i];
                if (!ZombieRoster.CanBeChased(id) ||
                    !remotes.GetInterpolatedPose(id, out Pose head, out _, out _))
                {
                    continue;
                }

                Vector3 world = ArenaSpace.ArenaToWorld(head.position);
                if (multiFloor && FloorOf(id, world) != myFloor)
                {
                    continue;
                }

                Lean(world, ref best, ref direction);
            }

            return best < float.MaxValue;
        }

        private void Lean(in Vector3 point, ref float best, ref Vector2 direction)
        {
            float dx = point.x - transform.position.x;
            float dz = point.z - transform.position.z;
            float distance = dx * dx + dz * dz;
            if (distance >= best || distance < 0.0001f)
            {
                return;
            }

            best = distance;
            direction = new Vector2(dx, dz).normalized;
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

            // Only while the wave CLOCK runs: once it is out the wave closes on its last standing
            // zombie, and a floor nobody visits would hold that zombie forever.
            int emptyMin = ZombieWaveState.WaveLeft > 0 ? ZombieWaveState.FloorMin : 0;
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

        /// <summary>Spawn spot around <paramref name="point"/>: the marker itself plus a ring of offsets
        /// (<see cref="SpawnRingRadii"/>), scored by how far the NavMesh edge is. <c>false</c> = nothing
        /// landed on the mesh, use the marker as it came.</summary>
        /// <remarks>⚠️ A spot is only accepted when it stays on the SAME storey and a COMPLETE path runs
        /// back to the marker: an offset through a wall drops the zombie into the next room, and 1.5 m of
        /// NavMesh search reaches over a 3 m thin floor.
        /// <para>The pick stays random among the spots that are roomy enough — a deterministic "best"
        /// would send every zombie of the door to the same tile. Spawn-time only; the ring costs ~25
        /// NavMesh queries.</para></remarks>
        private bool TryClearSpawnSpot(in Vector3 point, int floor, out Vector3 spot)
        {
            spot = point;
            SpotBuffer.Clear();

            // The ring is measured from the SNAPPED marker: a marker that sits off the mesh (outside
            // the fence) would otherwise put its whole ring off the mesh too.
            Vector3 origin = TrySample(point, out Vector3 snapped) ? snapped : point;

            AddSpawnSpot(origin, origin.y, floor);

            // Random start angle: the ring order itself must not favour one side of the door.
            float turn = Random.value * Mathf.PI * 2f;
            float step = Mathf.PI * 2f / SpawnRingDirections;

            for (int r = 0; r < SpawnRingRadii.Length; r++)
            {
                for (int d = 0; d < SpawnRingDirections; d++)
                {
                    float angle = turn + d * step;
                    Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) *
                        SpawnRingRadii[r];
                    AddSpawnSpot(origin + offset, origin.y, floor);
                }
            }

            if (SpotBuffer.Count == 0)
            {
                return false;
            }

            // Roomy and nobody standing there · roomy · roomiest left (narrow balconies never reach the
            // clearance, and standing the body in the corner there is still the best we have).
            return TryTakeSpot(origin, SpawnEdgeClearance, true, out spot) ||
                   TryTakeSpot(origin, SpawnEdgeClearance, false, out spot) ||
                   TryTakeRoomiestSpot(origin, out spot);
        }

        /// <summary>Samples one candidate onto the NavMesh and files it with its edge distance.</summary>
        private static void AddSpawnSpot(in Vector3 candidate, float baseHeight, int floor)
        {
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit sample, SpawnSpotSampleRadius,
                    NavMesh.AllAreas))
            {
                return;
            }

            Vector3 position = sample.position;

            // Same storey AND same level: a ring sample may land on a ramp or on the mesh under a
            // balcony, and a zombie dropped there spawns somewhere nobody expects.
            if (Mathf.Abs(position.y - baseHeight) > SpawnSpotRise ||
                (ArenaFloors.Count > 1 && ArenaFloors.FloorAt(position.y) != floor))
            {
                return;
            }

            for (int i = 0; i < SpotBuffer.Count; i++)
            {
                if ((SpotBuffer[i].Position - position).sqrMagnitude <
                    SpawnSpotMerge * SpawnSpotMerge)
                {
                    return;
                }
            }

            // No edge found at all = open floor, which is as clear as it gets.
            float clearance = NavMesh.FindClosestEdge(position, out NavMeshHit edge, NavMesh.AllAreas)
                ? edge.distance
                : SpawnEdgeClearance;

            SpotBuffer.Add(new SpawnSpot { Position = position, Clearance = clearance });
        }

        /// <summary>First spot (from a random index on) that clears <paramref name="minClearance"/>, is
        /// optionally free of other bodies and has a path back to <paramref name="origin"/>.</summary>
        private bool TryTakeSpot(in Vector3 origin, float minClearance, bool uncrowded, out Vector3 spot)
        {
            spot = origin;
            int count = SpotBuffer.Count;
            int start = Random.Range(0, count);

            for (int i = 0; i < count; i++)
            {
                SpawnSpot candidate = SpotBuffer[(start + i) % count];
                if (candidate.Clearance < minClearance ||
                    (uncrowded && HasZombieNear(candidate.Position)) ||
                    !HasPathBack(candidate.Position, origin))
                {
                    continue;
                }

                spot = candidate.Position;
                return true;
            }

            return false;
        }

        /// <summary>Last resort: the reachable spot with the largest edge distance.</summary>
        private static bool TryTakeRoomiestSpot(in Vector3 origin, out Vector3 spot)
        {
            spot = origin;

            // Path checks are the expensive part, so they run only on the current best, and a spot that
            // fails one drops out instead of being re-tested.
            while (true)
            {
                float best = -1f;
                int index = -1;
                for (int i = 0; i < SpotBuffer.Count; i++)
                {
                    if (SpotBuffer[i].Clearance > best)
                    {
                        best = SpotBuffer[i].Clearance;
                        index = i;
                    }
                }

                if (index < 0)
                {
                    return false;
                }

                SpawnSpot candidate = SpotBuffer[index];
                if (HasPathBack(candidate.Position, origin))
                {
                    spot = candidate.Position;
                    return true;
                }

                SpotBuffer.RemoveAt(index);
            }
        }

        private static bool HasPathBack(in Vector3 from, in Vector3 to)
        {
            if ((from - to).sqrMagnitude < 0.0001f)
            {
                return true;
            }

            s_pathScratch ??= new NavMeshPath();
            return NavMesh.CalculatePath(from, to, NavMesh.AllAreas, s_pathScratch) &&
                   s_pathScratch.status == NavMeshPathStatus.PathComplete;
        }

        /// <summary>Turns the body onto the axis with the most free NavMesh around it before the
        /// stand-up starts; a near-tie goes to the axis that looks at the nearest player.</summary>
        /// <remarks>⚠️ The zombie spawns LYING (~1.5 m of body): facing a wall pushes the model through
        /// it for the whole hold, and this rotation is also what the pose stream carries to the other
        /// headsets.</remarks>
        private void ApplyRiseFacing()
        {
            if (!agent.isOnNavMesh)
            {
                return;
            }

            Vector3 origin = transform.position;
            Vector3 toPlayer = NearestPlayerDirection(origin);

            float bestFree = -1f;
            float bestFacing = float.MinValue;
            Vector3 bestDirection = transform.forward;
            float step = Mathf.PI / RiseFacingAxes;

            // Half a turn is enough: an axis is tested in BOTH directions, and the sign is picked
            // afterwards.
            for (int i = 0; i < RiseFacingAxes; i++)
            {
                float angle = i * step;
                Vector3 axis = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                float free = Mathf.Min(FreeLength(origin, axis), FreeLength(origin, -axis));

                Vector3 direction = Vector3.Dot(axis, toPlayer) < 0f ? -axis : axis;
                float facing = Vector3.Dot(direction, toPlayer);

                bool better = free > bestFree + RiseFacingTie ||
                              (free > bestFree - RiseFacingTie && facing > bestFacing);
                if (!better)
                {
                    continue;
                }

                bestFree = Mathf.Max(bestFree, free);
                bestFacing = facing;
                bestDirection = direction;
            }

            transform.rotation = Quaternion.LookRotation(bestDirection, Vector3.up);
        }

        /// <summary>How far the NavMesh runs from <paramref name="origin"/> along
        /// <paramref name="direction"/>, capped at <see cref="RiseFacingProbe"/>.</summary>
        private static float FreeLength(in Vector3 origin, in Vector3 direction)
        {
            Vector3 target = origin + direction * RiseFacingProbe;
            return NavMesh.Raycast(origin, target, out NavMeshHit hit, NavMesh.AllAreas)
                ? Mathf.Min(hit.distance, RiseFacingProbe)
                : RiseFacingProbe;
        }

        /// <summary>Flat direction to the nearest player; zero when nobody is visible.</summary>
        private Vector3 NearestPlayerDirection(in Vector3 origin)
        {
            CollectSeen();

            float best = float.MaxValue;
            Vector3 direction = Vector3.zero;

            for (int i = 0; i < SeenPlayers.Count; i++)
            {
                Vector3 flat = SeenPlayers[i].Head - origin;
                flat.y = 0f;
                float distance = flat.sqrMagnitude;
                if (distance < best && distance > 0.0001f)
                {
                    best = distance;
                    direction = flat.normalized;
                }
            }

            return direction;
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

                // The player obstacles only make sense where agents run: this machine (§10.10).
                ZombiePlayerObstacles.Ensure();
            }
            else
            {
                LiveBrains.Remove(this);

                // ⚠️ Our target is cleared too: a dead brain left in the chaser count would keep
                // pushing the others off a player nobody is chasing any more.
                _targetId = 0;
                _stuckAt = 0f;
                _los.Clear();

                if (LiveBrains.Count == 0)
                {
                    ZombiePlayerObstacles.Release();
                }
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
