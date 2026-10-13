#nullable enable
using VortexArena.Protocol;

namespace VortexArena.Server.Core.Modes;

/// <summary>Co-op wave defence (§10.5 <c>zombie</c>): players are ONE team, zombies arrive wave by wave
/// and hit in melee. Ends on time ("dayanıldı") or when every connected player is down at the same
/// moment ("kayıp"); there is no winner.</summary>
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

    /// <summary>Server-side BOUND on the attack distance (m, horizontal) — not a validation: both poses
    /// are client-reported and one interp buffer old, so this only drops the absurd (§10.10). Twice the
    /// client's reach (1.2 m) to cover latency and the pose interval.</summary>
    private const float AttackReachBound = 2.5f;

    private const float AttackCooldownSeconds = 1.5f;

    /// <summary>Absolute ceiling of zombies standing at once, whatever the difficulty, the pace and the
    /// player count produce.</summary>
    /// <remarks>⚠️ Stays UNDER <see cref="ArenaProtocol.OBJECT_MAX_ENTRIES_PER_PACKET"/> (32): an object
    /// pose over that ceiling does not spill, it is DROPPED (§6.8) and the choice is not fair — the 33rd
    /// zombie would simply freeze. The two spare slots are the event section's share of the byte
    /// budget.</remarks>
    private const int ConcurrentCapMax = 30;

    /// <summary>How many of the last used spawn points are excluded from the next draw (§10.5).</summary>
    private const int SpawnRecency = 3;

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

        public WaveTable(string id, int totalBase, int totalPerWave, int standingBase,
            int standingPerWave, int standingCap, float spawnInterval, float waveBreak, int damageBase,
            int damagePerWave, int damageCap, int emptyFloorMin)
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
        }
    }

    private static readonly WaveTable EasyTable = new(MatchDifficulty.Easy, 6, 3, 4, 1, 16,
        1.6f, 8f, 12, 4, 32, 2);

    private static readonly WaveTable NormalTable = new(MatchDifficulty.Normal, 8, 4, 6, 2, 24,
        1.0f, 6f, 15, 4, 40, 3);

    private static readonly WaveTable HardTable = new(MatchDifficulty.Hard, 12, 6, 10, 3, 30,
        0.6f, 5f, 20, 5, 50, 4);

    /// <summary>Pace of a map that sets none (§10.5) — exactly the base numbers of the wave table.</summary>
    private const float DefaultPace = 1f;

    /// <summary>Pace clamp: a mis-typed export must neither stall the waves nor flood the hall. Mirrors
    /// the MapDefinition.zombiePace range.</summary>
    private const float MinPace = 0.25f;
    private const float MaxPace = 2f;

    /// <summary>How long a broken zombie stays in the world before <c>object_despawn</c> — the death
    /// animation's length. ⚠️ Removing it at once would cut the animation on every headset.</summary>
    private const float DespawnDelaySeconds = 3f;

    /// <summary>Pause reason shown while there is no object host; it rides <c>modeState</c>
    /// (§10.1) and the HUD prints it verbatim.</summary>
    private const string HostMissingNotice = "Yönetici bağlantısı yok — zombiler bekliyor";

    /// <summary>One zombie's mode-side state. Kept here because <c>object_state</c> alone cannot answer
    /// "which wave is it from" or "has its cooldown expired" — the core times nothing per object.</summary>
    private sealed class ZombieState
    {
        public int SpawnNetId;
        public int Wave;

        /// <summary>Accepted attack counter (<c>payload a</c>): the client plays the attack animation
        /// from its CHANGE, so it is monotonic and never rolled back.</summary>
        public int AttackCounter;

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

    /// <summary>Seconds left of the between-waves break; <c>0</c> = not in a break.</summary>
    private float _breakTimer;

    /// <summary>Ring of the last <see cref="SpawnRecency"/> spawn points used — excluded from the next
    /// draw so a wave does not pour out of one door. <c>0</c> = unused slot.
    /// <para>⚠️ "Pick the point farthest from the players" is impossible here: distance is metres and the
    /// server has none (§10.10).</para></summary>
    private readonly int[] _recentSpawns = new int[SpawnRecency];
    private int _recentWrite;

    private bool _lost;

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
    /// penalty treat the whole squad as one team (§10.5). Revive is <c>standstill</c> — the player
    /// stands back up where they fell, which can be in the middle of a pack, hence the 3 s spawn
    /// protection (§10.4).</summary>
    public ModeRules Rules => new()
    {
        Teams = TeamMode.None,
        Allies = true,
        Scoring = ScoreKind.PlayerAndShared,
        Revive = ReviveAnchor.StandStill,
        Weapons = WeaponSource.WeaponCanvas,
        RespawnDelay = 0f,
        FireWhilePaused = false,
        SpawnProtectionSeconds = 3f
    };

    public int DefaultRoundSeconds => 300;

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
            _waveTotal = 0;
            _standingCap = 0;
            _playerFactor = 1f;
            Array.Clear(_recentSpawns, 0, _recentSpawns.Length);
            _recentWrite = 0;
            _lost = false;
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
                              "dayanıldı, tüm oyuncular aynı anda ölürse kayıp.");
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
        }

        for (var i = 0; i < leftovers.Count; i++) director.DespawnObject(leftovers[i]);
    }

    /// <summary>Two end conditions (§10.5): the clock ("dayanıldı") and every connected player being
    /// down at the same moment ("kayıp"). There is no winner either way (<c>shared</c> scoring).</summary>
    /// <remarks>⚠️ Both are asked only in <see cref="Phase.Playing"/>: the clock is deliberately zeroed
    /// while we hold the mode pause, and a paused roster is revived by the core on the way back — judging
    /// either there would end the match while it is waiting for an admin.</remarks>
    public bool IsMatchOver(MatchDirector director, out MatchOutcome outcome)
    {
        outcome = MatchOutcome.Draw;
        if (director.CurrentPhase != Phase.Playing) return false;
        if (director.TimeRemaining <= 0f) return true;
        if (!AllPlayersDown(director)) return false;

        lock (_gate) _lost = true;
        PushModeState(director);
        return true;
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
            TickWaveLocked(director, deltaSeconds);
        }

        PushModeState(director);
    }

    /// <summary>A zombie reported a melee hit. ⚠️ ALWAYS returns <c>true</c> for its own event: an attack
    /// is never cosmetic, so it is never relayed — the truth is the <c>object_state</c> whose payload
    /// carries the attack counter (§10.10). A rejected attack produces nothing at all, on purpose.</summary>
    /// <remarks>The gates in order (§10.5): the object is ours and alive · its cooldown has expired ·
    /// a target id is present · the server's reach BOUND holds · the director's player gates accept the
    /// damage (phase, connected player, alive, calibrated, not spawn protected). ⚠️ The event carries NO
    /// damage number — it comes from the wave table here, so a tampered client cannot choose it.</remarks>
    public bool OnObjectEvent(MatchDirector director, int playerId, int netId, string kind, ObjectEventMsg msg)
    {
        if (msg.name != EventAttack || kind != ZombieKind) return false;

        int targetId = msg.i != null && msg.i.Length > 0 ? msg.i[0] : 0;
        float damage;
        string payload;

        lock (_gate)
        {
            if (!_zombies.TryGetValue(netId, out var zombie) || zombie.Dead) return true;
            if (targetId <= 0) return true;

            var now = DateTime.UtcNow;
            if (now < zombie.NextAttackAt) return true;

            // ⚠️ A MISSING pose passes: the first attack can beat the first 0x09 packet, and rejecting it
            // would make the opening of every wave feel dead. The bound only drops what it can measure.
            if (director.TryGetObjectToPlayerDistance(netId, targetId, out float meters) &&
                meters > AttackReachBound)
            {
                return true;
            }

            damage = DamageOfWave(zombie.Wave);
            if (!director.DamagePlayerFromObject(targetId, damage, netId)) return true;

            zombie.NextAttackAt = now.AddSeconds(AttackCooldownSeconds);
            zombie.AttackCounter++;
            payload = PayloadOf(zombie);
        }

        director.SetObjectPayload(netId, payload);
        return true;
    }

    /// <summary>A shot landed on a zombie. Breaking a network object is normally a world state with no
    /// score (§10.10) — here it is also a KILL, so the shared ledger is written from this hook.</summary>
    public void OnObjectHit(MatchDirector director, int attackerId, int netId, string kind, float damage,
        bool broken)
    {
        if (!broken || kind != ZombieKind) return;

        lock (_gate)
        {
            if (!_zombies.TryGetValue(netId, out var zombie) || zombie.Dead) return;

            zombie.Dead = true;
            zombie.DeadSeconds = 0f;
        }

        // Both channels at once (§10.2 <c>shared</c>): the last hit's owner and the squad total.
        director.AddSharedScore(attackerId, 1);
        PushModeState(director);
    }

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

        if (_breakTimer > 0f)
        {
            _breakTimer = MathF.Max(0f, _breakTimer - deltaSeconds);
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
            // The wave is over only once the last CORPSE is gone: the despawn delay is the death
            // animation's window, and the break must not start on top of it.
            if (_zombies.Count == 0) _breakTimer = _waveBreak;
            return;
        }

        _spawnTimer -= deltaSeconds;
        if (_spawnTimer > 0f) return;

        _spawnTimer = _spawnInterval;
        if (StandingCountLocked() >= _standingCap) return;

        SpawnZombieLocked(director);
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
        _playerFactor = PlayerFactorOf(ConnectedCount(director));
        _waveTotal = WaveTotalLocked(_wave);
        _standingCap = ConcurrentCapLocked(_wave);
        Console.WriteLine($"[zombie] dalga {_wave} ({_table.Id}) — {_waveTotal} zombi, en çok " +
                          $"{_standingCap} ayakta, hasar {DamageOfWave(_wave):0}, " +
                          $"oyuncu çarpanı ×{_playerFactor:0.##}.");
    }

    /// <summary>Spawns one zombie OWNED by the host but not held — i.e. straight into the flight window
    /// (§10.10), which is what opens the <c>0x09</c> pose gate for its brain.</summary>
    /// <remarks>⚠️ The spawn POSE is meaningless (identity): the server does not know where the spawn
    /// point is, so the point travels as a <c>netId</c> in the payload and the brain resolves the
    /// transform itself. Every other headset hides the zombie until its first streamed pose.</remarks>
    private void SpawnZombieLocked(MatchDirector director)
    {
        int host = director.ObjectHostId;
        if (host == 0) return;

        int spawnPoint = PickSpawnPointLocked();
        var zombie = new ZombieState { SpawnNetId = spawnPoint, Wave = _wave };

        int netId = director.SpawnObject(ZombieKind, default, owner: host, rightHand: false,
            payload: PayloadOf(zombie), inHand: false);
        if (netId == 0) return; // rejected (unknown kind / dynamic id pool exhausted) — already logged

        _zombies[netId] = zombie;
        _spawnedThisWave++;
        _recentSpawns[_recentWrite] = spawnPoint;
        _recentWrite = (_recentWrite + 1) % _recentSpawns.Length;
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

    /// <summary>Damage of one hit (§10.5) — from the difficulty table, NOT from the event, so a tampered
    /// client cannot choose it. Pace does not touch it.</summary>
    private float DamageOfWave(int wave) =>
        Math.Min(_table.DamageCap, _table.DamageBase + _table.DamagePerWave * (Math.Max(1, wave) - 1));

    /// <summary>§10.5: the server writes the WHOLE payload on every write, so no key can go missing
    /// between two states.</summary>
    private static string PayloadOf(ZombieState zombie) =>
        $"sp:{zombie.SpawnNetId};w:{zombie.Wave};a:{zombie.AttackCounter}";

    /// <summary>Is every connected player down at the same moment (the mode's only fail state)?</summary>
    /// <remarks>An empty roster is NOT a loss: an admin map preview has nobody to lose, and its exit is
    /// the operator's <c>end_match</c>/<c>abort_match</c>.
    /// <para>An uncalibrated player can never be damaged (§10.6), so they can never die and the run
    /// cannot be lost while they are connected — the safe direction.</para></remarks>
    private static bool AllPlayersDown(MatchDirector director)
    {
        bool any = false;
        foreach (var player in director.ConnectedPlayers())
        {
            any = true;
            if (player.Alive) return false;
        }

        return any;
    }

    /// <summary>§10.5 <c>w:..;left:..;next:..;rem:..;fm:..;diff:..[;over:lost]</c>. ⚠️ Not pushed while we
    /// hold the pause: the reason text lives in the same field there (§10.1) and would be
    /// overwritten.</summary>
    /// <remarks><c>rem</c> and <c>fm</c> are the BRAIN's input, not HUD decoration (§10.5 multi-floor
    /// rule): the storey quota is computed from them, and <c>rem:0</c> is what drops the empty-storey
    /// minimum to 0 so a wave cannot stall on an unvisited floor.</remarks>
    private void PushModeState(MatchDirector director)
    {
        string state;
        lock (_gate)
        {
            if (_paused) return;

            int next = (int)MathF.Ceiling(_breakTimer);
            int remaining = Math.Max(0, _waveTotal - _spawnedThisWave);
            state = $"w:{_wave};left:{StandingCountLocked()};next:{next};rem:{remaining};" +
                    $"fm:{_table.EmptyFloorMin};diff:{_table.Id}";
            if (_lost) state += ";over:lost";
        }

        director.SetModeState(state);
    }
}
