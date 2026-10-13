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

    private const int DamageBase = 12;
    private const int DamagePerWave = 4;
    private const int DamageCap = 32;

    /// <summary>Zombies standing at once: <c>(ConcurrentBase + wave) × pace</c>, capped.</summary>
    /// <remarks>⚠️ The cap stays UNDER <see cref="ArenaProtocol.OBJECT_MAX_ENTRIES_PER_PACKET"/> (16):
    /// an object pose over that ceiling does not spill, it is DROPPED (§6.8) and the choice is not fair —
    /// the 17th zombie would simply freeze.</remarks>
    private const int ConcurrentBase = 3;
    private const int ConcurrentCapMax = 12;

    /// <summary>Zombies in one wave: <c>WavePerWave × wave</c>.</summary>
    private const int WavePerWave = 4;

    private const float SpawnIntervalSeconds = 2f;
    private const float WaveBreakSeconds = 8f;

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

    /// <summary>The map's pace and the two intervals it produces, resolved once at match start.</summary>
    private float _pace = DefaultPace;
    private float _spawnInterval = SpawnIntervalSeconds;
    private float _waveBreak = WaveBreakSeconds;

    /// <summary>Seconds left of the between-waves break; <c>0</c> = not in a break.</summary>
    private float _breakTimer;

    /// <summary>Last spawn point used — excluded from the next draw so a wave does not pour out of one
    /// door. ⚠️ "Pick the point farthest from the players" is impossible here: distance is metres and the
    /// server has none (§10.10).</summary>
    private int _lastSpawnPoint;

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

    /// <summary>Armed co-op, so the quick-battle family (§11) — the kids maps carry no weapons.</summary>
    public string GameType => "quickbattle";

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
    public string InitialModeState => "w:0;left:0;next:0";

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
            _lastSpawnPoint = 0;
            _lost = false;
            _paused = false;
            _resuming = false;
            _savedRemaining = 0f;

            // Pace is map data (§10.5): read at setup, never on the tick.
            _pace = ResolvePace(director);
            _spawnInterval = SpawnIntervalSeconds / _pace;
            _waveBreak = WaveBreakSeconds / _pace;

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
            Console.WriteLine($"[zombie] maç başladı — {director.RoundSeconds} sn, " +
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

            StartWaveLocked();
            return;
        }

        if (_wave == 0)
        {
            StartWaveLocked();
            return;
        }

        int total = WaveTotal(_wave);
        if (_spawnedThisWave >= total)
        {
            // The wave is over only once the last CORPSE is gone: the despawn delay is the death
            // animation's window, and the break must not start on top of it.
            if (_zombies.Count == 0) _breakTimer = _waveBreak;
            return;
        }

        _spawnTimer -= deltaSeconds;
        if (_spawnTimer > 0f) return;

        _spawnTimer = _spawnInterval;
        if (StandingCountLocked() >= ConcurrentCapOf(_wave)) return;

        SpawnZombieLocked(director);
    }

    private void StartWaveLocked()
    {
        _wave++;
        _spawnedThisWave = 0;
        _spawnTimer = 0f;
        _breakTimer = 0f;
        Console.WriteLine($"[zombie] dalga {_wave} — {WaveTotal(_wave)} zombi, en çok " +
                          $"{ConcurrentCapOf(_wave)} ayakta, hasar {DamageOfWave(_wave):0}.");
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
        _lastSpawnPoint = spawnPoint;
    }

    /// <summary>A random spawn point, never the one used last (§10.5) — otherwise a whole wave can pour
    /// out of a single door. With one point in the scene that point is used every time.</summary>
    private int PickSpawnPointLocked()
    {
        if (_spawnPoints.Count == 1) return _spawnPoints[0];

        int chosen;
        do
        {
            chosen = _spawnPoints[_rng.Next(_spawnPoints.Count)];
        } while (chosen == _lastSpawnPoint);

        return chosen;
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

    private static int WaveTotal(int wave) => WavePerWave * Math.Max(1, wave);

    /// <summary><c>(ConcurrentBase + wave) × pace</c>, clamped. Never below 1 — a rounded-down cap would
    /// leave the arena empty for the whole wave, which never ends then (a wave ends when its zombies
    /// die).</summary>
    private int ConcurrentCapOf(int wave)
    {
        int baseCap = ConcurrentBase + Math.Max(1, wave);
        int paced = (int)MathF.Round(baseCap * _pace, MidpointRounding.AwayFromZero);
        return Math.Clamp(paced, 1, ConcurrentCapMax);
    }

    /// <summary>The map's pace (§10.5), clamped; the mode's default when the map sets none.</summary>
    private static float ResolvePace(MatchDirector director)
    {
        // <= 0 = old export with no field, or a scene outside the catalog: the map asks for nothing.
        float pace = director.CurrentMap?.zombiePace ?? 0f;
        return pace > 0f ? Math.Clamp(pace, MinPace, MaxPace) : DefaultPace;
    }

    private static float DamageOfWave(int wave) =>
        Math.Min(DamageCap, DamageBase + DamagePerWave * (Math.Max(1, wave) - 1));

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

    /// <summary>§10.5 <c>w:..;left:..;next:..[;over:lost]</c>. ⚠️ Not pushed while we hold the pause: the
    /// reason text lives in the same field there (§10.1) and would be overwritten.</summary>
    private void PushModeState(MatchDirector director)
    {
        string state;
        lock (_gate)
        {
            if (_paused) return;

            int next = (int)MathF.Ceiling(_breakTimer);
            state = $"w:{_wave};left:{StandingCountLocked()};next:{next}";
            if (_lost) state += ";over:lost";
        }

        director.SetModeState(state);
    }
}
