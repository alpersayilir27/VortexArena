#nullable enable
using VortexArena.Protocol;

namespace VortexArena.Server.Core.Modes;

/// <summary>Co-op wave defence (§10.5 <c>zombie</c>): players are ONE team, zombies arrive wave by wave
/// and hit in melee. Ends on time (<c>over:survived</c>), with the Last Stand's Dev killed
/// (<c>over:rescued</c>) or with the squad down (<c>over:lost</c>); there is no winner TEAM — the result
/// travels in <c>modeState</c>.</summary>
/// <remarks>⚠️ A zombie is a NETWORK OBJECT (§10.10), not a player: the server spawns it, holds its
/// health, breaks it and writes its score, while its POSE is streamed by its owner — and that owner is
/// the admin build (§10.10 "Sunucu-atanmış sahiplik"), because the brain needs NavMesh and the server
/// knows no metres. Shooting a zombie is today's <c>hit_report{targetNetId}</c> path; no new damage
/// gate exists.
/// <para>⚠️ <b>This mode holds its OWN lock</b>, unlike every other mode: <see cref="OnObjectEvent"/>
/// and <see cref="OnObjectHit"/> run on WS connection threads CONCURRENTLY with
/// <see cref="OnTick"/> — the zombie table and the attack cooldowns cannot live in an unguarded
/// collection. Lock order is always this gate → the director's (the director raises hooks outside its
/// own lock), never the reverse.</para>
/// <para>⚠️ The wave machine advances only in <see cref="Phase.Playing"/>: the core ticks a mode during
/// the countdown and during a mode pause as well (§10.1).</para></remarks>
public sealed class ZombieMode : IGameMode
{
    /// <summary>Dynamically spawned zombie (§10.5). ⚠️ The literal must match the Unity
    /// <c>NetObjectKind</c> asset exported into <c>maps.json</c>, or every spawn is rejected.</summary>
    private const string ZombieKind = "zombie";

    /// <summary>Scene-baked spawn marker; no visual, no health, only its <c>netId</c> matters.</summary>
    private const string SpawnKind = "zombie_spawn";

    private const string EventAttack = "attack";

    // ---- Balance (§10.5 "Dalga tablosu"). Playtest numbers: the rationale is in the doc. ----

    /// <summary>Absolute ceiling of zombies standing at once, whatever the difficulty, the pace and the
    /// player count produce.</summary>
    /// <remarks>⚠️ Stays UNDER <see cref="ArenaProtocol.OBJECT_MAX_ENTRIES_PER_PACKET"/> (32): an object
    /// pose over that ceiling does not spill, it is DROPPED (§6.8) and the choice is not fair — the 33rd
    /// zombie would simply freeze. The two spare slots are the event section's share of the byte
    /// budget.</remarks>
    private const int ConcurrentCapMax = 30;

    /// <summary>How many of the last used spawn points are excluded from the next draw (§10.5).</summary>
    private const int SpawnRecency = 3;

    // ---- Zombie types (§10.5 "Zombi türleri"). The SERVER picks the type; the brain reads it from
    // the payload for the model, the animation and the speed. ----

    private const string TypeWalker = "walker";
    private const string TypeCrawler = "crawler";
    private const string TypeRunner = "runner";
    private const string TypeBrute = "brute";
    private const string TypeScreamer = "screamer";
    private const string TypeDev = "dev";

    /// <summary>Special wave ids published as <c>modeState wx</c> (§10.5).</summary>
    private const string SpecialSwarm = "swarm";
    private const string SpecialHeavy = "heavy";
    private const string SpecialScream = "scream";

    private const int IdxWalker = 0;
    private const int IdxCrawler = 1;
    private const int IdxRunner = 2;
    private const int IdxBrute = 3;
    private const int IdxScreamer = 4;
    private const int IdxDev = 5;

    /// <summary>Spawnable types, i.e. everything but the Dev (which only the Last Stand spawns).</summary>
    private const int DeckSize = IdxDev;

    /// <summary>One zombie type's server-side numbers (§10.5). Speed and behaviour are NOT here: those
    /// live in the brain, which has metres.</summary>
    private readonly struct ZombieType
    {
        public readonly string Id;

        /// <summary>Base max HP, before the difficulty's <see cref="WaveTable.TypeScale"/>.</summary>
        public readonly float Hp;

        /// <summary>Multiplier on the wave damage; ignored when <see cref="FlatDamage"/> is set.</summary>
        public readonly float DamageMul;

        /// <summary>Damage independent of the wave (the Dev); <c>0</c> = use <see cref="DamageMul"/>.</summary>
        public readonly float FlatDamage;

        /// <summary>Damage ceiling of this type; <c>0</c> = none.</summary>
        public readonly float DamageCap;

        public readonly float Cooldown;

        /// <summary>Server-side BOUND on the attack distance (m, horizontal) — not a validation: both
        /// poses are client-reported and one interp buffer old, so this only drops the absurd (§10.10).
        /// Twice the client's reach to cover latency and the pose interval.</summary>
        public readonly float ReachBound;

        public readonly int KillPoints;

        /// <summary>How many of this type may stand at once; <c>0</c> = no per-type cap.</summary>
        public readonly int StandingCap;

        public ZombieType(string id, float hp, float damageMul, float flatDamage, float damageCap,
            float cooldown, float reachBound, int killPoints, int standingCap)
        {
            Id = id;
            Hp = hp;
            DamageMul = damageMul;
            FlatDamage = flatDamage;
            DamageCap = damageCap;
            Cooldown = cooldown;
            ReachBound = reachBound;
            KillPoints = killPoints;
            StandingCap = standingCap;
        }
    }

    // ⚠️ Order must match the Idx* constants: the composition deck is indexed by them.
    private static readonly ZombieType[] Types =
    {
        new(TypeWalker, 100f, 1f, 0f, 0f, 1.5f, 2.5f, 1, 0),
        new(TypeCrawler, 60f, 0.6f, 0f, 0f, 1.5f, 2.5f, 1, 0),
        new(TypeRunner, 60f, 0.75f, 0f, 0f, 1.5f, 2.5f, 2, 0),
        new(TypeBrute, 400f, 1.6f, 0f, 60f, 1.5f, 2.5f, 5, 3),
        new(TypeScreamer, 80f, 0.5f, 0f, 0f, 1.5f, 2.5f, 3, 2),
        new(TypeDev, 2500f, 0f, 45f, 0f, 2f, 3f, 25, 1)
    };

    /// <summary>Dev HP is per player: <c>Hp × max(DevMinPlayers, n)</c> (§10.5) — a two-player floor, or
    /// a solo run would face a boss sized for nobody.</summary>
    private const int DevMinPlayers = 2;

    /// <summary>Gap between two tries at a rejected Dev spawn (no host, id pool full, cap 30).</summary>
    private const float DevRetrySeconds = 1f;

    // ---- Scoring + team rules (§10.5) ----

    /// <summary>Extra point when the killing hit was a headshot (<c>hit_report.zone</c>, §5.1).</summary>
    private const int HeadshotBonus = 1;

    /// <summary>Points for lifting a downed teammate — worth more than a kill: the squad survives on it.</summary>
    private const int RevivePoints = 3;

    /// <summary>HP a teammate-lifted player stands up with (§10.4); the wave break gives full HP.</summary>
    private const float TeammateReviveHp = 50f;

    /// <summary>Calibrated players a match needs before a team loss is possible (§10.5).</summary>
    /// <remarks>⚠️ Below it nobody can lift anybody (the revive condition needs a teammate), so a solo run
    /// would be unlosable-by-design turned into instantly lost.</remarks>
    private const int LossMinPlayers = 2;

    /// <summary>Nobody alive for this long = loss (§10.5).</summary>
    /// <remarks>⚠️ Without the grace the single frame where everyone is down — the normal moment a
    /// teammate runs over to lift someone — would end the match.</remarks>
    private const float LossGraceSeconds = 2f;

    /// <summary>Final <c>modeState over:</c> values (§10.5); <c>Rescued</c> (the Last Stand's Dev kill)
    /// is a win like <c>Survived</c>.</summary>
    private const string OverSurvived = "survived";
    private const string OverRescued = "rescued";
    private const string OverLost = "lost";

    // ---- Last Stand (§10.5 "Son Direniş") ----

    /// <summary>Time left that opens the Last Stand; the short value guards a short round, where 90 s
    /// would be most of the match.</summary>
    private const float FinalStandSeconds = 90f;
    private const float FinalStandShortSeconds = 60f;
    private const int FinalStandShortRound = 300;

    /// <summary>While the Dev lives it screams and summons on this period.</summary>
    private const float DevSummonInterval = 15f;
    private const int DevSummonCount = 3;

    /// <summary>A screamer's first scream, then its period (§10.5) — in normal waves too.</summary>
    private const float ScreamFirstSeconds = 6f;
    private const float ScreamInterval = 10f;

    /// <summary>Delay between the Dev's death and the end of the match: the collapse of every other
    /// zombie has to be seen before the result screen covers it.</summary>
    private const float RescueDelaySeconds = 3f;

    /// <summary>Total multipliers of the special waves (§10.5): fewer but faster, fewer but heavier.</summary>
    private const float SwarmTotalMul = 0.8f;
    private const float HeavyTotalMul = 0.7f;

    /// <summary>Player-count scaling of a wave (§10.5): <c>1 + PlayerFactorStep × (n − 4)</c>, clamped.
    /// 4 players is the reference group the difficulty table is written for.</summary>
    private const float PlayerFactorStep = 0.15f;
    private const float PlayerFactorMin = 0.55f;
    private const float PlayerFactorMax = 1.9f;

    /// <summary>One difficulty's wave table (§10.5). <c>d</c> = wave number, 1-based; damage uses
    /// <c>d−1</c>, so wave 1 deals the base damage.</summary>
    private readonly struct WaveTable
    {
        public readonly string Id;
        public readonly int TotalBase;
        public readonly int TotalPerWave;
        public readonly int StandingBase;
        public readonly int StandingPerWave;
        public readonly int StandingCap;
        public readonly float SpawnInterval;
        public readonly float WaveBreak;
        public readonly int DamageBase;
        public readonly int DamagePerWave;
        public readonly int DamageCap;

        /// <summary>Zombies an EMPTY storey keeps even with nobody on it (<c>modeState fm</c>); the brain
        /// reads it (§10.5 multi-floor rule), the server only publishes it.</summary>
        public readonly int EmptyFloorMin;

        /// <summary>Shift of the EFFECTIVE wave the composition is read from (§10.5): difficulty moves
        /// not only the numbers but WHEN each type arrives.</summary>
        public readonly int WaveShift;

        /// <summary>Multiplier on every type's max HP and on the Dev's damage (§10.5).</summary>
        public readonly float TypeScale;

        public WaveTable(string id, int totalBase, int totalPerWave, int standingBase,
            int standingPerWave, int standingCap, float spawnInterval, float waveBreak, int damageBase,
            int damagePerWave, int damageCap, int emptyFloorMin, int waveShift, float typeScale)
        {
            Id = id;
            TotalBase = totalBase;
            TotalPerWave = totalPerWave;
            StandingBase = standingBase;
            StandingPerWave = standingPerWave;
            StandingCap = standingCap;
            SpawnInterval = spawnInterval;
            WaveBreak = waveBreak;
            DamageBase = damageBase;
            DamagePerWave = damagePerWave;
            DamageCap = damageCap;
            EmptyFloorMin = emptyFloorMin;
            WaveShift = waveShift;
            TypeScale = typeScale;
        }
    }

    // ⚠️ The wave break is a BREATHER, not dead air: everyone down stands back up with full HP in that
    // window (§10.4) and weapons get swapped — shortening it breaks both.
    private static readonly WaveTable EasyTable = new(MatchDifficulty.Easy, 6, 3, 4, 1, 16,
        1.6f, 15f, 12, 4, 32, 2, -1, 0.7f);

    private static readonly WaveTable NormalTable = new(MatchDifficulty.Normal, 8, 4, 6, 2, 24,
        1.0f, 12f, 15, 4, 40, 3, 0, 1f);

    private static readonly WaveTable HardTable = new(MatchDifficulty.Hard, 12, 6, 10, 3, 30,
        0.6f, 10f, 20, 5, 50, 4, 1, 1.3f);

    /// <summary>First part of the between-waves gap (§10.5): the "wave cleared" phase the HUD shows the
    /// next wave's new weapons in. ⚠️ NOT divided by the map pace — it is a reading window, not pressure.</summary>
    private const float ClearSeconds = 5f;

    /// <summary>Pace of a map that sets none (§10.5) — exactly the base numbers of the wave table.</summary>
    private const float DefaultPace = 1f;

    /// <summary>Pace clamp: a mis-typed export must neither stall the waves nor flood the hall. Mirrors
    /// the MapDefinition.zombiePace range.</summary>
    private const float MinPace = 0.25f;
    private const float MaxPace = 2f;

    /// <summary>How long a broken zombie stays in the world before <c>object_despawn</c> — the death
    /// animation plus the time the corpse ragdoll needs to settle. ⚠️ Removing it at once would cut the
    /// animation on every headset; shortening it makes bodies vanish mid-fall.
    /// <para>Purely visual: the wave ends on the last STANDING zombie, so the corpses run this timer out
    /// inside the wave gap.</para></summary>
    private const float DespawnDelaySeconds = 4.5f;

    /// <summary>Pause reason shown while there is no object host; it rides <c>modeState</c>
    /// (§10.1) and the HUD prints it verbatim.</summary>
    private const string HostMissingNotice = "Yönetici bağlantısı yok — zombiler bekliyor";

    /// <summary>One zombie's mode-side state. Kept here because <c>object_state</c> alone cannot answer
    /// "which wave is it from" or "has its cooldown expired" — the core times nothing per object.</summary>
    private sealed class ZombieState
    {
        public int SpawnNetId;
        public int Wave;

        /// <summary>Zombie type (<c>payload t</c>, §10.5): damage, reach, cooldown and kill points are
        /// all read from it.</summary>
        public string Type = TypeWalker;

        /// <summary>Max HP written into the object table at spawn — kept here because the Dev's bar
        /// needs the ceiling and no wire field carries one.</summary>
        public float MaxHp;

        /// <summary>Accepted attack counter (<c>payload a</c>): the client plays the attack animation
        /// from its CHANGE, so it is monotonic and never rolled back.</summary>
        public int AttackCounter;

        /// <summary>Scream counter (<c>payload c</c>), same change-driven contract as
        /// <see cref="AttackCounter"/>; only screamers and the Dev have one.</summary>
        public int ScreamCounter;

        /// <summary>Seconds to the next scream; <c>0</c> = this type never screams. A countdown rather
        /// than a stamp so the host pause (which stops the tick) also stops the screams.</summary>
        public float ScreamTimer;

        /// <summary>netId of the Dev that summoned this zombie (<c>payload n</c>); <c>0</c> = none.</summary>
        public int SummonerNetId;

        /// <summary>UTC instant the next attack may be accepted.</summary>
        public DateTime NextAttackAt;

        public bool Dead;

        /// <summary>Seconds since death (despawn delay).</summary>
        public float DeadSeconds;
    }

    private readonly object _gate = new();
    private readonly Dictionary<int, ZombieState> _zombies = new();
    private readonly List<int> _spawnPoints = new();
    private readonly Random _rng = new();

    /// <summary>Scratch lists, reused so a 10 Hz tick allocates nothing.</summary>
    private readonly List<int> _despawnScratch = new();
    private readonly List<int> _adoptScratch = new();

    private int _wave;
    private int _spawnedThisWave;
    private float _spawnTimer;

    /// <summary>Wave size and standing cap of the RUNNING wave: both are snapshotted at wave start
    /// (player factor), so the numbers cannot shift mid-wave.</summary>
    private int _waveTotal;
    private int _standingCap;

    /// <summary>Player-count factor of the running wave (§10.5), snapshotted at wave start.</summary>
    private float _playerFactor = 1f;

    /// <summary>The difficulty table of this match, resolved once at match start (§10.5).</summary>
    private WaveTable _table = NormalTable;

    /// <summary>The map's pace and the two intervals it produces, resolved once at match start.</summary>
    private float _pace = DefaultPace;
    private float _spawnInterval = NormalTable.SpawnInterval;
    private float _waveBreak = NormalTable.WaveBreak;

    /// <summary>Seconds left of the WHOLE between-waves gap (clear phase + break); <c>0</c> = not in a
    /// break. ⚠️ Everything downstream (<c>next</c>, the revive window, the music) reads this one total:
    /// the clear phase is a part of the gap, not a state beside it.</summary>
    private float _breakTimer;

    /// <summary>Seconds left of the clear phase (<c>modeState clr</c>), running inside
    /// <see cref="_breakTimer"/>; <c>0</c> = the countdown part.</summary>
    private float _clearTimer;

    /// <summary>Zombies of the running wave still to be spawned, per type (§10.5 "Dalga dizilimi");
    /// indexed by the <c>Idx*</c> constants. The spawn picker draws from it, so the composition cannot
    /// drift from the wave total.</summary>
    private readonly int[] _deck = new int[DeckSize];

    /// <summary>Special wave id of the running wave (<c>modeState wx</c>); <c>""</c> = ordinary.</summary>
    private string _specialId = "";

    /// <summary>Last Stand open? (§10.5) Once set it never clears: no new wave starts after it.</summary>
    private bool _finalStand;

    /// <summary>The Dev's netId and max HP (<c>modeState dev</c>); <c>0</c> = not spawned yet.</summary>
    private int _devNetId;
    private float _devMaxHp;

    /// <summary>Player count the Dev's HP is sized for — snapshotted when the Last Stand opens.</summary>
    private int _devPlayers;

    private float _devRetryTimer;
    private bool _devDead;

    /// <summary>Seconds left between the Dev's death and <c>over:rescued</c>.</summary>
    private float _rescueTimer;

    /// <summary>Walkers the Dev's scream still owes us. ⚠️ Deferred on purpose: the scream pass walks
    /// the zombie dictionary, and spawning inside that walk would invalidate the enumerator.</summary>
    private int _devSummonPending;

    /// <summary>Ring of the last <see cref="SpawnRecency"/> spawn points used — excluded from the next
    /// draw so a wave does not pour out of one door. <c>0</c> = unused slot.
    /// <para>⚠️ "Pick the point farthest from the players" is impossible here: distance is metres and the
    /// server has none (§10.10).</para></summary>
    private readonly int[] _recentSpawns = new int[SpawnRecency];
    private int _recentWrite;

    /// <summary>Per-player headshot kills / teammate revives of this match — published only in the FINAL
    /// modeState (<c>zs</c> tokens, §10.5), because the result screen's numbers are not a per-tick field.</summary>
    private readonly Dictionary<int, int> _headshots = new();
    private readonly Dictionary<int, int> _revives = new();

    /// <summary>Result key of the match: <c>""</c> until it ends, then <see cref="OverSurvived"/>,
    /// <see cref="OverRescued"/> or <see cref="OverLost"/>.</summary>
    private string _over = "";

    /// <summary>When the squad went fully down; <c>null</c> = somebody is alive (§10.5 loss rule).</summary>
    /// <remarks>A wall-clock stamp rather than a counted-down timer: the check lives in
    /// <see cref="IsMatchOver"/>, which gets no delta.</remarks>
    private DateTime? _allDownSince;

    /// <summary>Do WE hold the mode pause right now (§10.1)?</summary>
    private bool _paused;

    /// <summary>Match time left when we opened the pause — the core zeroes the clock there and sets it
    /// to the full round on the way back, so the remainder is only knowable here (TournamentMode's
    /// pattern).</summary>
    private float _savedRemaining;

    /// <summary>Is the next <see cref="OnRoundStart"/> a RESUME (write the saved clock back) rather than
    /// the match's first round (where the saved value is 0 and would end the match)?</summary>
    private bool _resuming;

    public string ModeId => "zombie";

    /// <summary>Zombie family (§11): the <c>start_match</c> gate rejects a map from any other family.</summary>
    public string GameType => MapTable.ZombieGameType;

    /// <summary>Co-op shape: no red/blue but everyone is an ally, so friendly fire and the teamkill
    /// penalty treat the whole squad as one team (§10.5). Revive is <c>teammate</c> — only a living
    /// teammate (or the wave break) lifts the downed player, who stands back up where they fell, which
    /// can be in the middle of a pack, hence the 3 s spawn protection (§10.4).</summary>
    public ModeRules Rules => new()
    {
        Teams = TeamMode.None,
        Allies = true,
        Scoring = ScoreKind.PlayerAndShared,
        Revive = ReviveAnchor.Teammate,
        Weapons = WeaponSource.WeaponCanvas,
        RespawnDelay = 0f,
        FireWhilePaused = false,
        SpawnProtectionSeconds = 3f
    };

    public int DefaultRoundSeconds => 600;

    /// <summary>No score limit: a co-op run is bounded by the clock (or by everyone dying), and a target
    /// score would end it early with no winner to declare.</summary>
    public int DefaultScoreLimit => ArenaProtocol.SCORE_LIMIT_UNLIMITED;

    /// <summary>The wave count + shared total is what the group reads at the end; the core's
    /// <c>MATCH_END_SECONDS</c> valve would wipe it mid-read (§10.1).</summary>
    public bool HoldsResultForOperator => true;

    /// <summary>Zero state from <c>start_match</c> on, so the HUD's wave counter is on screen during
    /// loading and the countdown instead of appearing with the first spawn.</summary>
    /// <remarks>⚠️ Carries the <c>normal</c> table's numbers, NOT the operator's pick: this is read at
    /// <c>start_match</c>, before <see cref="OnMatchStart"/> hands us the match (and with it the
    /// difficulty). The real values arrive with the first push once the countdown ends, and nothing acts
    /// on <c>fm</c>/<c>diff</c> before the first wave.</remarks>
    public string InitialModeState =>
        $"w:0;left:0;next:0;rem:0;fm:{NormalTable.EmptyFloorMin};diff:{NormalTable.Id}";

    public void OnMatchStart(MatchDirector director)
    {
        lock (_gate)
        {
            _zombies.Clear();
            _spawnPoints.Clear();
            _wave = 0;
            _spawnedThisWave = 0;
            _spawnTimer = 0f;
            _breakTimer = 0f;
            _clearTimer = 0f;
            _waveTotal = 0;
            _standingCap = 0;
            _playerFactor = 1f;
            Array.Clear(_deck, 0, _deck.Length);
            _specialId = "";
            _finalStand = false;
            _devNetId = 0;
            _devMaxHp = 0f;
            _devPlayers = DevMinPlayers;
            _devRetryTimer = 0f;
            _devDead = false;
            _rescueTimer = 0f;
            _devSummonPending = 0;
            Array.Clear(_recentSpawns, 0, _recentSpawns.Length);
            _recentWrite = 0;
            _over = "";
            _allDownSince = null;
            _headshots.Clear();
            _revives.Clear();
            _paused = false;
            _resuming = false;
            _savedRemaining = 0f;

            // Difficulty is the operator's pick, pace is map data (§10.5): both read at setup, never on
            // the tick.
            _table = TableOf(director.Difficulty);
            _pace = ResolvePace(director);
            _spawnInterval = _table.SpawnInterval / _pace;
            _waveBreak = _table.WaveBreak / _pace;

            foreach (var netId in director.ObjectIdsOfKind(SpawnKind)) _spawnPoints.Add(netId);
        }

        PushModeState(director);

        if (_spawnPoints.Count == 0)
        {
            Console.WriteLine($"[zombie] UYARI: haritada '{SpawnKind}' türünde doğum noktası yok — zombi " +
                              "çıkmayacak. Sahnedeki noktaların bake'i ve maps.json eksik olabilir.");
        }
        else
        {
            Console.WriteLine($"[zombie] maç başladı — {director.RoundSeconds} sn, zorluk {_table.Id}, " +
                              $"{_spawnPoints.Count} doğum noktası, tempo ×{_pace:0.##}; süre bitince " +
                              $"dayanıldı, {LossMinPlayers} kalibreli oyuncudan hiçbiri " +
                              $"{LossGraceSeconds:0} sn boyunca ayakta değilse kayıp.");
        }

        // No host = no brain: pause before the first wave rather than spawning zombies nobody drives.
        SyncHostPause(director);
    }

    /// <summary>Resume path only (§10.1): the core restarted its clock at <c>roundSeconds</c> and
    /// cleared <c>modeState</c>, so both are written back. ⚠️ The wave machine is NOT reset — the run
    /// continues where the pause caught it.</summary>
    public void OnRoundStart(MatchDirector director)
    {
        bool resuming;
        lock (_gate)
        {
            resuming = _resuming;
            _resuming = false;
        }

        if (!resuming) return;

        director.SetTimeRemaining(_savedRemaining);
        PushModeState(director);
    }

    /// <summary>Clears the field: <see cref="Phase.Finished"/> gets no ticks, so anything left standing
    /// would walk through the whole held result screen (its brain does not read the phase).</summary>
    public void OnMatchEnd(MatchDirector director)
    {
        // The hook runs outside the lock; a new match/staging may already have replaced this one.
        if (director.CurrentPhase != Phase.Finished) return;

        // Own list, not the tick's scratch: this hook runs on another thread and would overwrite a
        // scan that is in progress.
        List<int> leftovers;
        lock (_gate)
        {
            leftovers = new List<int>(_zombies.Keys);
            _zombies.Clear();
            _breakTimer = 0f;
            _clearTimer = 0f;
        }

        for (var i = 0; i < leftovers.Count; i++) director.DespawnObject(leftovers[i]);
    }

    /// <summary>Three end conditions (§10.5): the Dev killed in the Last Stand (<c>over:rescued</c>),
    /// the clock running out with the squad standing (<c>over:survived</c>) and the squad being down
    /// (<c>over:lost</c>). There is no winner TEAM either way (<c>shared</c> scoring) — the result travels
    /// in <c>modeState</c>.</summary>
    /// <remarks>⚠️ All are asked only in <see cref="Phase.Playing"/>: the clock is deliberately zeroed
    /// while we hold the mode pause, and a paused roster is revived by the core on the way back — judging
    /// either there would end the match while it is waiting for an admin.</remarks>
    public bool IsMatchOver(MatchDirector director, out MatchOutcome outcome)
    {
        outcome = MatchOutcome.Draw;
        if (director.CurrentPhase != Phase.Playing) return false;

        bool devDead;
        bool rescued;
        lock (_gate)
        {
            devDead = _devDead;
            rescued = _devDead && _rescueTimer <= 0f;
        }

        if (rescued)
        {
            SetOver(OverRescued);
            PushModeState(director);
            return true;
        }

        if (director.TimeRemaining <= 0f)
        {
            SetOver(devDead ? OverRescued : OverSurvived);
            PushModeState(director);
            return true;
        }

        // ⚠️ Once the Dev is down the run is already won: a wipe inside the rescue window must not turn
        // it into a loss.
        if (devDead) return false;

        if (!IsTeamDown(director)) return false;

        SetOver(OverLost);
        PushModeState(director);
        return true;
    }

    /// <summary>Writes the outcome token and closes the clear phase with it: a gap that was running when
    /// the clock ran out must not keep announcing a wave the squad will never see.</summary>
    private void SetOver(string over)
    {
        lock (_gate)
        {
            _over = over;
            _clearTimer = 0f;
        }
    }

    public void OnTick(MatchDirector director, float deltaSeconds)
    {
        // Runs during the countdown and during our own pause too (§10.1) — the host check belongs to
        // both, the wave machine to neither.
        SyncHostPause(director);
        if (director.CurrentPhase != Phase.Playing) return;

        lock (_gate)
        {
            TickDeadLocked(director, deltaSeconds);
            TickScreamsLocked(director, deltaSeconds);
            TickWaveLocked(director, deltaSeconds);
        }

        PushModeState(director);
    }

    /// <summary>A zombie reported a melee hit. ⚠️ ALWAYS returns <c>true</c> for its own event: an attack
    /// is never cosmetic, so it is never relayed — the truth is the <c>object_state</c> whose payload
    /// carries the attack counter (§10.10). A rejected attack produces nothing at all, on purpose.</summary>
    /// <remarks>The gates in order (§10.5): the object is ours and alive · its cooldown has expired ·
    /// the server's reach BOUND holds · the director's player gates accept the damage (phase, connected
    /// player, alive, calibrated, not spawn protected). ⚠️ The event carries NO damage number — it comes
    /// from the wave table here, so a tampered client cannot choose it. An empty target list is a WHIFF:
    /// it spends the cooldown and bumps the counter (everyone plays the swing) but deals no damage.</remarks>
    public bool OnObjectEvent(MatchDirector director, int playerId, int netId, string kind, ObjectEventMsg msg)
    {
        if (msg.name != EventAttack || kind != ZombieKind) return false;

        int targetId = msg.i != null && msg.i.Length > 0 ? msg.i[0] : 0;
        float damage;

        lock (_gate)
        {
            if (!_zombies.TryGetValue(netId, out var zombie) || zombie.Dead) return true;

            var now = DateTime.UtcNow;
            if (now < zombie.NextAttackAt) return true;

            var type = TypeOf(zombie.Type);

            // Whiff: the target stepped out of reach during the wind-up. The swing is still spent, so
            // the cooldown and the counter run — only the damage is missing.
            if (targetId <= 0)
            {
                zombie.NextAttackAt = now.AddSeconds(type.Cooldown);
                zombie.AttackCounter++;
                director.SetObjectPayload(netId, PayloadOf(zombie));
                return true;
            }

            // ⚠️ A MISSING pose passes: the first attack can beat the first 0x09 packet, and rejecting it
            // would make the opening of every wave feel dead. The bound only drops what it can measure.
            if (director.TryGetObjectToPlayerDistance(netId, targetId, out float meters) &&
                meters > type.ReachBound)
            {
                return true;
            }

            damage = DamageOfLocked(zombie, type);
            if (!director.DamagePlayerFromObject(targetId, damage, netId)) return true;

            zombie.NextAttackAt = now.AddSeconds(type.Cooldown);
            zombie.AttackCounter++;

            // Written under the gate: the tick's scream writer shares this payload, and a stale
            // string landing last would roll a counter back on the wire.
            director.SetObjectPayload(netId, PayloadOf(zombie));
        }

        return true;
    }

    /// <summary>A shot landed on a zombie. Breaking a network object is normally a world state with no
    /// score (§10.10) — here it is also a KILL, so the shared ledger is written from this hook.</summary>
    /// <remarks>⚠️ Only the 7-arg overload is implemented: the core calls this one and the 6-arg default
    /// body would make the same kill score twice.
    /// <para><c>kills</c> is credited separately (<see cref="MatchDirector.AddKillCredit"/>): a broken
    /// zombie raises no <c>kill_event</c>, so nothing else would count it.</para></remarks>
    public void OnObjectHit(MatchDirector director, int attackerId, int netId, string kind, float damage,
        bool broken, int zone)
    {
        if (!broken || kind != ZombieKind) return;

        int points;
        List<int>? collapse = null;
        lock (_gate)
        {
            if (!_zombies.TryGetValue(netId, out var zombie) || zombie.Dead) return;

            zombie.Dead = true;
            zombie.DeadSeconds = 0f;

            points = KillPoints(zombie.Type);
            if (zone == ArenaProtocol.HIT_ZONE_HEAD)
            {
                points += HeadshotBonus;
                if (attackerId > 0) Bump(_headshots, attackerId);
            }

            if (netId == _devNetId && !_devDead)
            {
                _devDead = true;
                _rescueTimer = RescueDelaySeconds;

                // Every other zombie drops with him — no points, it is not anybody's kill.
                collapse = new List<int>();
                foreach (var pair in _zombies)
                {
                    if (pair.Value.Dead) continue;
                    pair.Value.Dead = true;
                    pair.Value.DeadSeconds = 0f;
                    collapse.Add(pair.Key);
                }
            }
        }

        if (collapse != null)
        {
            for (var i = 0; i < collapse.Count; i++) director.BreakObject(collapse[i]);
            Console.WriteLine($"[zombie] Dev öldürüldü (#{attackerId}) — {collapse.Count} zombi çöktü, " +
                              $"{RescueDelaySeconds:0} sn sonra kurtarıldı.");
        }

        // Both channels at once (§10.2 "shared"): the last hit's owner and the squad total.
        director.AddSharedScore(attackerId, points);
        director.AddKillCredit(attackerId);
        PushModeState(director);
    }

    /// <summary>A teammate lifted a downed player (§10.4): the reviver gets <see cref="RevivePoints"/>
    /// and the revive is counted for the result screen.</summary>
    /// <remarks><paramref name="byPlayerId"/> is already validated by the director; <c>0</c> = the wave
    /// break stood them up, which rewards nobody.</remarks>
    public void OnPlayerRevived(MatchDirector director, int playerId, int byPlayerId)
    {
        if (byPlayerId <= 0) return;

        lock (_gate) Bump(_revives, byPlayerId);
        director.AddSharedScore(byPlayerId, RevivePoints);
    }

    /// <summary>Revive HP (§10.4): a teammate lift is half health, the wave break is full.</summary>
    /// <remarks>The partial revive is what makes lifting a risk rather than a reset — and the break is
    /// full HP because nobody is there to carry the downed player then.</remarks>
    public float ReviveHp(MatchDirector director, int playerId, int byPlayerId) =>
        byPlayerId > 0 ? TeammateReviveHp : ArenaProtocol.PLAYER_MAX_HP;

    /// <summary>The brain moved to another admin, or there is none (§10.10).</summary>
    /// <remarks>⚠️ With no host the core writes <c>owner = 0</c> on every zombie, so a returning host
    /// does NOT inherit them through the hand-over (there is nothing left to hand over) — the mode
    /// re-assigns them here. Without this the run resumes with zombies standing frozen forever.</remarks>
    public void OnObjectHostChanged(MatchDirector director, int hostPlayerId)
    {
        if (hostPlayerId != 0) AdoptZombies(director, hostPlayerId);
        SyncHostPause(director);
    }

    // ---------------------------------------------------------------- wave machine

    private void TickWaveLocked(MatchDirector director, float deltaSeconds)
    {
        if (_spawnPoints.Count == 0) return;

        TryStartFinalStandLocked(director);
        if (_finalStand)
        {
            TickFinalStandLocked(director, deltaSeconds);
            return;
        }

        if (_breakTimer > 0f)
        {
            _breakTimer = MathF.Max(0f, _breakTimer - deltaSeconds);
            _clearTimer = MathF.Max(0f, _clearTimer - deltaSeconds);
            if (_breakTimer > 0f) return;

            StartWaveLocked(director);
            return;
        }

        if (_wave == 0)
        {
            StartWaveLocked(director);
            return;
        }

        if (_spawnedThisWave >= _waveTotal)
        {
            // ⚠️ STANDING count, not the table size: waiting for the corpses to despawn would push the
            // "wave cleared" cue 4.5 s past the kill that earned it. They clear during the gap.
            if (StandingCountLocked() == 0)
            {
                _clearTimer = ClearSeconds;
                _breakTimer = ClearSeconds + _waveBreak;
            }

            return;
        }

        TrySpawnFromDeckLocked(director, deltaSeconds, _standingCap);
    }

    /// <summary>One spawn slot of the running wave: waits out the interval, honours the standing cap and
    /// draws a type from the deck.</summary>
    /// <remarks>⚠️ When every remaining type sits at its own standing cap the slot is NOT consumed (§10.5):
    /// resetting the interval there would silently skip spawns and the wave could not finish.</remarks>
    private void TrySpawnFromDeckLocked(MatchDirector director, float deltaSeconds, int standingCap)
    {
        _spawnTimer -= deltaSeconds;
        if (_spawnTimer > 0f) return;

        if (StandingCountLocked() >= standingCap)
        {
            _spawnTimer = _spawnInterval;
            return;
        }

        int type = PickTypeLocked();
        if (type < 0) return;

        _spawnTimer = _spawnInterval;
        SpawnZombieLocked(director, type);
    }

    /// <summary>Draws a type from the deck, weighted by how many of it are still owed (§10.5): a type at
    /// its standing cap is skipped. <c>-1</c> = nothing may spawn right now.</summary>
    private int PickTypeLocked()
    {
        var total = 0;
        for (var i = 0; i < _deck.Length; i++)
        {
            if (_deck[i] > 0 && !AtTypeCapLocked(i)) total += _deck[i];
        }

        if (total == 0) return -1;

        int roll = _rng.Next(total);
        for (var i = 0; i < _deck.Length; i++)
        {
            if (_deck[i] <= 0 || AtTypeCapLocked(i)) continue;
            roll -= _deck[i];
            if (roll < 0) return i;
        }

        return -1;
    }

    private bool AtTypeCapLocked(int typeIndex)
    {
        int cap = Types[typeIndex].StandingCap;
        if (cap <= 0) return false;

        var standing = 0;
        foreach (var zombie in _zombies.Values)
        {
            if (!zombie.Dead && string.Equals(zombie.Type, Types[typeIndex].Id, StringComparison.Ordinal))
            {
                standing++;
            }
        }

        return standing >= cap;
    }

    private bool DeckEmptyLocked()
    {
        for (var i = 0; i < _deck.Length; i++)
        {
            if (_deck[i] > 0) return false;
        }

        return true;
    }

    /// <summary>Fills the deck from the EFFECTIVE wave (§10.5 "Dalga dizilimi") and returns the total it
    /// sums to; also sets <see cref="_specialId"/>.</summary>
    /// <remarks>⚠️ At least one regular zombie always remains: a small wave made only of brutes and
    /// screamers would hit both per-type standing caps and stall on them.</remarks>
    private int BuildDeckLocked(int wave, int total)
    {
        int e = Math.Max(1, wave + _table.WaveShift);
        int weightWalker, weightCrawler, weightRunner, brutes, screamers;
        var mul = 1f;
        var special = "";

        // Weights split the REGULAR part only (§10.5); they need not sum to 100 — brutes and screamers
        // are counts taken off the total first.
        switch (e)
        {
            case 1: weightWalker = 100; weightCrawler = 0; weightRunner = 0; brutes = 0; screamers = 0; break;
            case 2: weightWalker = 85; weightCrawler = 15; weightRunner = 0; brutes = 0; screamers = 0; break;
            case 3: weightWalker = 70; weightCrawler = 10; weightRunner = 20; brutes = 0; screamers = 0; break;
            case 4: weightWalker = 65; weightCrawler = 10; weightRunner = 25; brutes = 1; screamers = 0; break;
            case 5:
                weightWalker = 30; weightCrawler = 0; weightRunner = 70; brutes = 0; screamers = 0;
                mul = SwarmTotalMul;
                special = SpecialSwarm;
                break;
            case 6: weightWalker = 55; weightCrawler = 10; weightRunner = 25; brutes = 2; screamers = 1; break;
            case 7: weightWalker = 50; weightCrawler = 10; weightRunner = 30; brutes = 2; screamers = 1; break;
            case 8:
                weightWalker = 100; weightCrawler = 0; weightRunner = 0; brutes = 4; screamers = 0;
                mul = HeavyTotalMul;
                special = SpecialHeavy;
                break;
            default:
                weightWalker = 45; weightCrawler = 10; weightRunner = 35;
                brutes = 2 + (e - 9) / 2;
                screamers = 2;
                break;
        }

        // Special rotation overrides the 9+ shape every third wave (§10.5).
        if (e >= 11 && (e - 11) % 3 == 0)
        {
            switch ((e - 11) / 3 % 3)
            {
                case 0:
                    weightWalker = 30; weightCrawler = 0; weightRunner = 70; brutes = 0; screamers = 0;
                    mul = SwarmTotalMul;
                    special = SpecialSwarm;
                    break;
                case 1:
                    weightWalker = 100; weightCrawler = 0; weightRunner = 0;
                    brutes = Math.Max(4, brutes + 2);
                    screamers = 0;
                    mul = HeavyTotalMul;
                    special = SpecialHeavy;
                    break;
                default:
                    screamers = 3;
                    special = SpecialScream;
                    break;
            }
        }

        _specialId = special;

        int scaled = Math.Max(1, (int)MathF.Round(total * mul, MidpointRounding.AwayFromZero));
        while (brutes + screamers > scaled - 1)
        {
            if (screamers > 0) screamers--;
            else if (brutes > 0) brutes--;
            else break;
        }

        int regular = Math.Max(0, scaled - brutes - screamers);
        float weightSum = weightWalker + weightCrawler + weightRunner;
        int crawlers = Math.Min(regular,
            (int)MathF.Round(regular * weightCrawler / weightSum, MidpointRounding.AwayFromZero));
        int walkers, runners;
        if (weightRunner == 0)
        {
            walkers = regular - crawlers; // no runner wave: rounding slack must not create one
            runners = 0;
        }
        else
        {
            walkers = Math.Min(regular - crawlers,
                (int)MathF.Round(regular * weightWalker / weightSum, MidpointRounding.AwayFromZero));
            runners = regular - walkers - crawlers; // the remainder, so the split always sums
        }

        Array.Clear(_deck, 0, _deck.Length);
        _deck[IdxWalker] = walkers;
        _deck[IdxCrawler] = crawlers;
        _deck[IdxRunner] = runners;
        _deck[IdxBrute] = brutes;
        _deck[IdxScreamer] = screamers;
        return walkers + crawlers + runners + brutes + screamers;
    }

    // ---------------------------------------------------------------- Last Stand (§10.5)

    /// <summary>Opens the Last Stand once the clock is short enough: the wave break is dropped, no new
    /// wave will start and the Dev spawns.</summary>
    private void TryStartFinalStandLocked(MatchDirector director)
    {
        if (_finalStand || _wave < 1) return;

        float threshold = director.RoundSeconds < FinalStandShortRound
            ? FinalStandShortSeconds
            : FinalStandSeconds;
        if (director.TimeRemaining > threshold) return;

        _finalStand = true;
        _breakTimer = 0f;
        _clearTimer = 0f;
        _spawnTimer = 0f;
        _devPlayers = Math.Max(DevMinPlayers, ConnectedCount(director)); // frozen like a wave's count
        SpawnDevLocked(director);
        Console.WriteLine($"[zombie] Son Direniş — dalga {_wave} dizilimi sonsuz sürüyor, " +
                          $"Dev doğdu (can {_devMaxHp:0}).");
    }

    /// <summary>Endless spawning at the running wave's cap and composition; the deck is refilled when it
    /// empties. After the Dev's death nothing spawns — only the rescue delay runs down.</summary>
    private void TickFinalStandLocked(MatchDirector director, float deltaSeconds)
    {
        if (_devDead)
        {
            _rescueTimer = MathF.Max(0f, _rescueTimer - deltaSeconds);
            return;
        }

        // A Dev rejected at spawn (no host yet / id pool) is retried: without him the Last Stand has no
        // way to end in a rescue.
        if (_devNetId == 0)
        {
            _devRetryTimer -= deltaSeconds;
            if (_devRetryTimer <= 0f) SpawnDevLocked(director);
        }

        if (DeckEmptyLocked()) BuildDeckLocked(_wave, WaveTotalLocked(_wave));
        TrySpawnFromDeckLocked(director, deltaSeconds, _standingCap);
    }

    private void SpawnDevLocked(MatchDirector director)
    {
        // Retried at most once a second: a rejected spawn logs, and 10 Hz would flood the log.
        _devRetryTimer = DevRetrySeconds;

        // The absolute cap holds for him too — a 31st object eats the packet's spare entry (§10.5).
        if (StandingCountLocked() >= ConcurrentCapMax) return;

        float hp = Types[IdxDev].Hp * _devPlayers * _table.TypeScale;

        int netId = SpawnZombieLocked(director, IdxDev, hpOverride: hp);
        if (netId == 0) return;

        _devNetId = netId;
        _devMaxHp = hp;
    }

    /// <summary>The Dev's and the screamers' scream clocks (§10.5). The Dev's scream also summons
    /// walkers, which is why the spawns are deferred past the dictionary walk.</summary>
    private void TickScreamsLocked(MatchDirector director, float deltaSeconds)
    {
        foreach (var pair in _zombies)
        {
            var zombie = pair.Value;
            if (zombie.Dead || zombie.ScreamTimer <= 0f) continue;

            zombie.ScreamTimer -= deltaSeconds;
            if (zombie.ScreamTimer > 0f) continue;

            zombie.ScreamCounter++;
            bool isDev = string.Equals(zombie.Type, TypeDev, StringComparison.Ordinal);
            zombie.ScreamTimer = isDev ? DevSummonInterval : ScreamInterval;
            if (isDev) _devSummonPending += DevSummonCount;

            director.SetObjectPayload(pair.Key, PayloadOf(zombie));
        }

        // ⚠️ Only the ABSOLUTE cap applies here, not the wave's: the summon is the Dev's pressure, and
        // a full wave cap would silently swallow it.
        for (var i = 0; i < _devSummonPending; i++)
        {
            if (StandingCountLocked() >= ConcurrentCapMax) break;
            SpawnZombieLocked(director, IdxWalker, _devNetId);
        }

        _devSummonPending = 0;
    }

    /// <summary>Opens a wave and FREEZES its numbers: the player count is snapshotted here, once (§10.5).
    /// ⚠️ Re-counting every tick would move a running wave's target — a leaver would leave it unfinishable
    /// and a joiner would empty it at a stroke.</summary>
    private void StartWaveLocked(MatchDirector director)
    {
        _wave++;
        _spawnedThisWave = 0;
        _spawnTimer = 0f;
        _breakTimer = 0f;
        _clearTimer = 0f;
        _playerFactor = PlayerFactorOf(ConnectedCount(director));
        _standingCap = ConcurrentCapLocked(_wave);
        _waveTotal = BuildDeckLocked(_wave, WaveTotalLocked(_wave));
        Console.WriteLine($"[zombie] dalga {_wave} ({_table.Id}) — {_waveTotal} zombi, en çok " +
                          $"{_standingCap} ayakta, hasar {DamageOfWave(_wave):0}, " +
                          $"oyuncu çarpanı ×{_playerFactor:0.##}; dizilim " +
                          $"{_deck[IdxWalker]}/{_deck[IdxCrawler]}/{_deck[IdxRunner]}/" +
                          $"{_deck[IdxBrute]}/{_deck[IdxScreamer]}" +
                          (_specialId.Length > 0 ? $", özel dalga {_specialId}" : "") + ".");
    }

    /// <summary>Spawns one zombie OWNED by the host but not held — i.e. straight into the flight window
    /// (§10.10), which is what opens the <c>0x09</c> pose gate for its brain.</summary>
    /// <remarks>⚠️ The spawn POSE is meaningless (identity): the server does not know where the spawn
    /// point is, so the point travels as a <c>netId</c> in the payload and the brain resolves the
    /// transform itself. Every other headset hides the zombie until its first streamed pose.</remarks>
    /// <returns>The new object's <c>netId</c>, or <c>0</c> when there is no host or the spawn was
    /// rejected.</returns>
    private int SpawnZombieLocked(MatchDirector director, int typeIndex, int summonerNetId = 0,
        float hpOverride = 0f)
    {
        int host = director.ObjectHostId;
        if (host == 0) return 0;

        var type = Types[typeIndex];
        int spawnPoint = PickSpawnPointLocked();
        float maxHp = hpOverride > 0f ? hpOverride : type.Hp * _table.TypeScale;
        var zombie = new ZombieState
        {
            SpawnNetId = spawnPoint,
            Wave = _wave,
            Type = type.Id,
            MaxHp = maxHp,
            SummonerNetId = summonerNetId,
            ScreamTimer = typeIndex switch
            {
                IdxDev => DevSummonInterval,
                IdxScreamer => ScreamFirstSeconds,
                _ => 0f
            }
        };

        // ⚠️ maxHp is written per OBJECT: the catalog's value is the walker's, every other type would
        // otherwise die in one magazine (or never).
        int netId = director.SpawnObject(ZombieKind, default, owner: host, rightHand: false,
            payload: PayloadOf(zombie), inHand: false, maxHp: maxHp);
        if (netId == 0) return 0; // rejected (unknown kind / dynamic id pool exhausted) — already logged

        _zombies[netId] = zombie;

        // Only deck draws count against the wave: the Dev and his summons are extra (§10.5).
        if (summonerNetId == 0 && typeIndex < _deck.Length)
        {
            _spawnedThisWave++;
            if (_deck[typeIndex] > 0) _deck[typeIndex]--;
        }

        _recentSpawns[_recentWrite] = spawnPoint;
        _recentWrite = (_recentWrite + 1) % _recentSpawns.Length;
        return netId;
    }

    /// <summary>A random spawn point, never one of the last <see cref="SpawnRecency"/> used (§10.5) —
    /// otherwise a whole wave can pour out of a single door.</summary>
    /// <remarks>⚠️ With fewer than <c>SpawnRecency + 1</c> points only the LAST one is excluded: a wider
    /// window would leave no legal point and the draw could never finish.</remarks>
    private int PickSpawnPointLocked()
    {
        if (_spawnPoints.Count == 1) return _spawnPoints[0];

        int window = _spawnPoints.Count > SpawnRecency ? SpawnRecency : 1;

        int chosen;
        do
        {
            chosen = _spawnPoints[_rng.Next(_spawnPoints.Count)];
        } while (IsRecentlyUsed(chosen, window));

        return chosen;
    }

    /// <summary>Is the point among the last <paramref name="window"/> used? Walks the ring backwards from
    /// the newest entry.</summary>
    private bool IsRecentlyUsed(int spawnPoint, int window)
    {
        for (var i = 1; i <= window; i++)
        {
            int index = (_recentWrite - i + _recentSpawns.Length) % _recentSpawns.Length;
            if (_recentSpawns[index] == spawnPoint) return true;
        }

        return false;
    }

    private void TickDeadLocked(MatchDirector director, float deltaSeconds)
    {
        _despawnScratch.Clear();

        foreach (var pair in _zombies)
        {
            if (!pair.Value.Dead) continue;

            pair.Value.DeadSeconds += deltaSeconds;
            if (pair.Value.DeadSeconds >= DespawnDelaySeconds) _despawnScratch.Add(pair.Key);
        }

        for (var i = 0; i < _despawnScratch.Count; i++)
        {
            _zombies.Remove(_despawnScratch[i]);
            director.DespawnObject(_despawnScratch[i]);
        }
    }

    private int StandingCountLocked()
    {
        int standing = 0;
        foreach (var zombie in _zombies.Values)
        {
            if (!zombie.Dead) standing++;
        }

        return standing;
    }

    // ---------------------------------------------------------------- host pause

    /// <summary>Opens/lifts the mode pause from the object host's presence (§10.10). Idempotent: called
    /// from the tick as well as from the host hook, because a host lost during loading/countdown cannot
    /// be paused for yet (<see cref="MatchDirector.TryPauseForMode"/> works only from
    /// <see cref="Phase.Playing"/>).</summary>
    private void SyncHostPause(MatchDirector director)
    {
        int host = director.ObjectHostId;

        if (host == 0)
        {
            if (_paused || director.CurrentPhase != Phase.Playing) return;

            _savedRemaining = director.TimeRemaining;
            if (!director.TryPauseForMode(HostMissingNotice)) return;

            _paused = true;
            // ⚠️ TryPauseForMode ZEROES the clock, and this clock is the MATCH's: a HUD sitting at 00:00
            // through the whole wait and jumping back up on resume reads as a bug.
            director.SetTimeRemaining(_savedRemaining);
            Console.WriteLine("[zombie] nesne sahibi yok — maç duraklatıldı; " +
                              $"kalan süre {_savedRemaining:0} sn, dalga {_wave} korunuyor.");
            return;
        }

        if (!_paused) return;
        // A refusal is retried on the next tick: an operator pause may be holding the phase.
        if (!director.TryStartRound()) return;

        _paused = false;
        _resuming = true;
        Console.WriteLine($"[zombie] nesne sahibi #{host} — maç kaldığı süreden sürüyor (dalga {_wave}).");
    }

    /// <summary>Writes the new host as the owner of every live zombie (§10.10). Unchanged owners are
    /// no-ops, so calling it on a normal hand-over costs nothing.</summary>
    private void AdoptZombies(MatchDirector director, int hostPlayerId)
    {
        _adoptScratch.Clear();
        lock (_gate)
        {
            foreach (var pair in _zombies)
            {
                if (!pair.Value.Dead) _adoptScratch.Add(pair.Key);
            }
        }

        for (var i = 0; i < _adoptScratch.Count; i++)
        {
            director.SetObjectOwner(_adoptScratch[i], hostPlayerId);
        }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Wave size: the difficulty table's <c>TotalBase + TotalPerWave × d</c> scaled by the
    /// player factor. Never below 1 — a wave of zero zombies ends the moment it starts and the run would
    /// race through its waves.</summary>
    private int WaveTotalLocked(int wave)
    {
        int table = _table.TotalBase + _table.TotalPerWave * Math.Max(1, wave);
        int scaled = (int)MathF.Round(table * _playerFactor, MidpointRounding.AwayFromZero);
        return Math.Max(1, scaled);
    }

    /// <summary>Zombies standing at once: the table's <c>StandingBase + StandingPerWave × d</c> capped by
    /// the difficulty, then scaled by the player factor and the map pace (in that order, §10.5).</summary>
    /// <remarks>⚠️ Never below 1 and never above the difficulty cap (nor
    /// <see cref="ConcurrentCapMax"/>): a cap rounded down to 0 would leave the arena empty for the whole
    /// wave, which then never ends (a wave ends when its zombies die).</remarks>
    private int ConcurrentCapLocked(int wave)
    {
        int table = Math.Min(_table.StandingCap,
            _table.StandingBase + _table.StandingPerWave * Math.Max(1, wave));
        int scaled = (int)MathF.Round(table * _playerFactor * _pace, MidpointRounding.AwayFromZero);
        return Math.Clamp(scaled, 1, Math.Min(_table.StandingCap, ConcurrentCapMax));
    }

    /// <summary>The difficulty's table (§10.5); an unknown/empty pick reads as <c>normal</c>.</summary>
    private static WaveTable TableOf(string difficulty) =>
        MatchDifficulty.Normalize(difficulty) switch
        {
            MatchDifficulty.Easy => EasyTable,
            MatchDifficulty.Hard => HardTable,
            _ => NormalTable
        };

    /// <summary>Wave scaling from the connected player count (§10.5); <c>n</c> is at least 1, so an empty
    /// roster does not produce a 0.55× wave the first joiner then walks into.</summary>
    private static float PlayerFactorOf(int players) =>
        Math.Clamp(1f + PlayerFactorStep * (Math.Max(1, players) - 4), PlayerFactorMin, PlayerFactorMax);

    private static int ConnectedCount(MatchDirector director)
    {
        var count = 0;
        foreach (var _ in director.ConnectedPlayers()) count++;
        return count;
    }

    /// <summary>The map's pace (§10.5), clamped; the mode's default when the map sets none.</summary>
    private static float ResolvePace(MatchDirector director)
    {
        // <= 0 = old export with no field, or a scene outside the catalog: the map asks for nothing.
        float pace = director.CurrentMap?.zombiePace ?? 0f;
        return pace > 0f ? Math.Clamp(pace, MinPace, MaxPace) : DefaultPace;
    }

    /// <summary>A wave's base damage (§10.5) — from the difficulty table, NOT from the event, so a
    /// tampered client cannot choose it. Pace does not touch it; the type multiplier is applied in
    /// <see cref="DamageOfLocked"/>.</summary>
    private float DamageOfWave(int wave) =>
        Math.Min(_table.DamageCap, _table.DamageBase + _table.DamagePerWave * (Math.Max(1, wave) - 1));

    /// <summary>§10.5: the server writes the WHOLE payload on every write, so no key can go missing
    /// between two states. <c>c</c> and <c>n</c> only exist where they mean something.</summary>
    private static string PayloadOf(ZombieState zombie)
    {
        string payload = $"sp:{zombie.SpawnNetId};w:{zombie.Wave};a:{zombie.AttackCounter}" +
                         $";t:{zombie.Type}";
        if (string.Equals(zombie.Type, TypeScreamer, StringComparison.Ordinal) ||
            string.Equals(zombie.Type, TypeDev, StringComparison.Ordinal))
        {
            payload += $";c:{zombie.ScreamCounter}";
        }

        if (zombie.SummonerNetId != 0) payload += $";n:{zombie.SummonerNetId}";
        return payload;
    }

    /// <summary>The type's row (§10.5); an unknown id reads as a walker, never as nothing.</summary>
    private static ZombieType TypeOf(string type)
    {
        for (var i = 0; i < Types.Length; i++)
        {
            if (string.Equals(Types[i].Id, type, StringComparison.Ordinal)) return Types[i];
        }

        return Types[IdxWalker];
    }

    /// <summary>Damage of one hit (§10.5): the wave table scaled by the type, or the type's flat damage
    /// scaled by the difficulty (the Dev). Never below 1 — a rounded-down 0 would make a type harmless.</summary>
    /// <remarks>⚠️ The wave damage is ALREADY per-difficulty, so it gets no second difficulty
    /// multiplier; only the flat branch does.</remarks>
    private float DamageOfLocked(ZombieState zombie, in ZombieType type)
    {
        if (type.FlatDamage > 0f)
        {
            return MathF.Max(1f,
                MathF.Round(type.FlatDamage * _table.TypeScale, MidpointRounding.AwayFromZero));
        }

        float value = MathF.Round(DamageOfWave(zombie.Wave) * type.DamageMul,
            MidpointRounding.AwayFromZero);
        if (type.DamageCap > 0f) value = MathF.Min(type.DamageCap, value);
        return MathF.Max(1f, value);
    }

    /// <summary>Team loss (§10.5): at least <see cref="LossMinPlayers"/> calibrated players in the match
    /// and NONE of them alive for <see cref="LossGraceSeconds"/> without a break.</summary>
    /// <remarks>Only calibrated participants are counted: an uncalibrated headset is not playing, so it
    /// may neither count as alive nor keep the squad from losing (it used to do the latter — it can never
    /// be damaged, §10.6).
    /// <para>A solo run NEVER loses: with nobody to lift them the player's revive falls back to the
    /// standstill rule (§10.4), so "everybody down" is a normal five seconds there.</para></remarks>
    private bool IsTeamDown(MatchDirector director)
    {
        var counted = 0;
        var anyAlive = false;
        foreach (var player in director.ConnectedPlayers())
        {
            if (!player.MatchParticipant || !player.Calibrated) continue;
            counted++;
            if (player.Alive) anyAlive = true;
        }

        if (anyAlive || counted < LossMinPlayers)
        {
            lock (_gate) _allDownSince = null;
            return false;
        }

        var now = DateTime.UtcNow;
        lock (_gate)
        {
            _allDownSince ??= now;
            return (now - _allDownSince.Value).TotalSeconds >= LossGraceSeconds;
        }
    }

    /// <summary>Kill points by zombie type (§10.5); an unknown type is worth a walker, never 0.</summary>
    private static int KillPoints(string type) => TypeOf(type).KillPoints;

    private static void Bump(Dictionary<int, int> tally, int playerId) =>
        tally[playerId] = tally.TryGetValue(playerId, out var count) ? count + 1 : 1;

    /// <summary>§10.5
    /// <c>w:..;left:..;next:..;rem:..;fm:..;diff:..;fs:..[;dev:id/hp][;wx:..][;clr:..][;over:..][;zs&lt;id&gt;:h/r]</c>.
    /// ⚠️ Not pushed while we hold the pause: the reason text lives in the same field there (§10.1) and
    /// would be overwritten.</summary>
    /// <remarks><c>rem</c> and <c>fm</c> are the BRAIN's input, not HUD decoration (§10.5 multi-floor
    /// rule): the storey quota is computed from them, and <c>rem:0</c> is what drops the empty-storey
    /// minimum to 0 so a wave cannot stall on an unvisited floor.
    /// <para>The per-player <c>zs</c> tokens ride ONLY the final state: the result screen reads them once,
    /// and a 10 Hz field has no room for a row per player. A player with both counts at 0 is omitted —
    /// the reader treats a missing token as <c>0/0</c>.</para></remarks>
    private void PushModeState(MatchDirector director)
    {
        string state;
        lock (_gate)
        {
            if (_paused) return;

            // In the Last Stand there is no next wave and no remaining count: spawning is endless, and a
            // counting-down `next` would promise a wave that never comes.
            int next = _finalStand ? 0 : (int)MathF.Ceiling(_breakTimer);
            int remaining = _finalStand ? 0 : Math.Max(0, _waveTotal - _spawnedThisWave);
            state = $"w:{_wave};left:{StandingCountLocked()};next:{next};rem:{remaining};" +
                    $"fm:{_table.EmptyFloorMin};diff:{_table.Id};fs:{(_finalStand ? 1 : 0)}";
            if (_devNetId != 0)
            {
                state += $";dev:{_devNetId}/" +
                         $"{(int)MathF.Round(_devMaxHp, MidpointRounding.AwayFromZero)}";
            }

            if (_specialId.Length > 0 && _wave > 0 && _breakTimer <= 0f) state += $";wx:{_specialId}";

            // Only while it runs: its absence is what tells the HUD the countdown part has begun.
            // ⚠️ Never beside `over`: the result screen would get a "dalga tamamlandı" layer on top.
            if (!_finalStand && _clearTimer > 0f && _over.Length == 0)
            {
                state += $";clr:{(int)MathF.Ceiling(_clearTimer)}";
            }

            if (_over.Length > 0) state += $";over:{_over}{PlayerTokensLocked()}";
        }

        director.SetModeState(state);
    }

    /// <summary><c>zs&lt;playerId&gt;:&lt;headshots&gt;/&lt;revives&gt;</c> for every player with a
    /// non-zero count (§10.5).</summary>
    private string PlayerTokensLocked()
    {
        if (_headshots.Count == 0 && _revives.Count == 0) return "";

        var tokens = new System.Text.StringBuilder();
        foreach (var playerId in PlayersWithTalliesLocked())
        {
            _headshots.TryGetValue(playerId, out var heads);
            _revives.TryGetValue(playerId, out var revives);
            tokens.Append($";zs{playerId}:{heads}/{revives}");
        }

        return tokens.ToString();
    }

    private IEnumerable<int> PlayersWithTalliesLocked()
    {
        var seen = new HashSet<int>(_headshots.Keys);
        seen.UnionWith(_revives.Keys);
        return seen;
    }
}
