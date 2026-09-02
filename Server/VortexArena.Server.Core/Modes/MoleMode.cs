#nullable enable
using VortexArena.Protocol;

namespace VortexArena.Server.Core.Modes;

/// <summary>Whack-a-mole (§10.5 <c>mole</c>): moles pop out of the arena's holes in red or blue,
/// smashing YOUR OWN colour scores for your team, hitting the other one takes from it. Ends on time
/// only; the higher score wins.</summary>
/// <remarks>The Kids family's first COMPETITIVE round: the family invariants hold (no weapon, so no
/// damage; no revive, because nothing dies; the result screen waits for the operator) and the score
/// channel is what differs — red against blue.
/// <para>⚠️ The mole is NOT an object of its own: a hole is one <c>netId</c> and its stage says whether
/// a mole stands in it. Ownership of the timers is therefore the hole's, and one hole can never hold
/// two moles.</para></remarks>
public sealed class MoleMode : IGameMode
{
    /// <summary>Kind of a hole in the ground (§10.5) — the mode's ONLY scene object.</summary>
    private const string HoleKind = "mole_hole";

    private const string EventWhack = "whack";

    private const int StageHidden = 0;
    private const int StageUp = 1;
    private const int StageSquashed = 2;

    /// <summary>How long a mole stays up and hittable. ⚠️ The client's rise animation plays INSIDE this
    /// window (§10.5): an animation longer than the window would still show a mole the server has
    /// already taken down.</summary>
    private const float MoleUpSeconds = 2f;

    /// <summary>Gap between pop ATTEMPTS; an attempt is skipped while the cap is full.</summary>
    private const float PopIntervalSeconds = 1.5f;

    /// <summary>How long the squashed mole stays visible before the hole empties.</summary>
    private const float SquashedSeconds = 0.6f;

    /// <summary>Moles standing at once: one per player, within these bounds. Tied to the roster because
    /// the field feels empty for a crowd and unfair for one child at the same fixed number.</summary>
    private const int MinConcurrentUp = 2;
    private const int MaxConcurrentUp = 6;

    /// <summary>Points for the right colour; the wrong one takes the same amount away.</summary>
    private const int WhackPoints = 10;

    /// <summary>Colours are drawn from a shuffled EQUAL deck rather than per-pop randomness, which over
    /// a short shift can hand one team most of the targets.</summary>
    private const int DeckHalfSize = 8;

    /// <summary>What one hole is doing. Kept mode-side because <c>stage</c> alone cannot answer "how long
    /// has it been standing" — the core does not time an object's stage.</summary>
    private sealed class HoleState
    {
        public int Stage;

        /// <summary>Pop counter (§10.5) — the nonce a <c>whack</c> must carry. Monotonic per hole, so a
        /// swing that left the client before the mole went down can never match the NEXT one.</summary>
        public int Nonce;

        public string Color = "";

        /// <summary>Seconds spent in <see cref="Stage"/>.</summary>
        public float Timer;
    }

    private readonly List<int> _holes = new();
    private readonly Dictionary<int, HoleState> _state = new();
    private readonly Dictionary<int, (int Correct, int Wrong)> _counts = new();
    private readonly List<string> _deck = new();
    private readonly Random _rng = new();

    private float _popTimer;

    /// <summary>Hidden holes, reused every pop attempt so a 10 Hz tick allocates nothing.</summary>
    private readonly List<int> _candidates = new();

    public string ModeId => "mole";

    /// <summary>Kids family — startable ONLY on a <c>gameType:"kids"</c> map (§11).</summary>
    public string GameType => MapTable.KidsGameType;

    /// <summary>Two teams and a team score, but NO base and NO revive: nothing dies here, and the base
    /// was the vehicle of <c>reviveAnchor:"base"</c>. <c>Weapons = None</c> is what switches damage off
    /// (§10.5) — the hammer is an item, not a weapon.</summary>
    public ModeRules Rules => new()
    {
        Teams = TeamMode.TwoTeams,
        Scoring = ScoreKind.Team,
        Revive = ReviveAnchor.None,
        Weapons = WeaponSource.None,
        RespawnDelay = 0f
    };

    public int DefaultRoundSeconds => 300;

    /// <summary>No score limit: the round is bounded by the clock, and a target score would end a
    /// children's game early — at a moment nobody in the hall is watching for.</summary>
    public int DefaultScoreLimit => ArenaProtocol.SCORE_LIMIT_UNLIMITED;

    /// <summary>The scoreboard is what the operator reads out; the auto-return would wipe it (§10.1).</summary>
    public bool HoldsResultForOperator => true;

    public void OnMatchStart(MatchDirector director)
    {
        _holes.Clear();
        _state.Clear();
        _counts.Clear();
        _deck.Clear();
        _popTimer = 0f;

        foreach (var netId in director.ObjectIdsOfKind(HoleKind))
        {
            _holes.Add(netId);
            _state[netId] = new HoleState();
            // The previous match may have left a stage/payload behind; the mode owns the reset because
            // a scene object's stage is not cleared by staging.
            director.SetObjectPayload(netId, "");
            director.SetObjectStage(netId, StageHidden);
        }

        PushModeState(director);

        if (_holes.Count == 0)
        {
            Console.WriteLine($"[mole] UYARI: haritada '{HoleKind}' türünde delik yok — köstebek " +
                              "çıkmayacak. Sahnedeki deliklerin bake'i ve maps.json eksik olabilir.");
            return;
        }

        Console.WriteLine($"[mole] maç başladı — {director.RoundSeconds} sn, {_holes.Count} delik; " +
                          "skor limiti yok, süre bitince yüksek skor kazanır.");
    }

    /// <summary>Time is the ONLY end condition (§10.5); the leading team wins, a tie is a draw.</summary>
    public bool IsMatchOver(MatchDirector director, out MatchOutcome outcome)
    {
        if (director.TimeRemaining > 0f)
        {
            outcome = MatchOutcome.Draw;
            return false;
        }

        var red = director.ScoreRed;
        var blue = director.ScoreBlue;
        outcome = red > blue ? MatchOutcome.Team("red")
            : blue > red ? MatchOutcome.Team("blue")
            : MatchOutcome.Draw;
        return true;
    }

    public void OnTick(MatchDirector director, float deltaSeconds)
    {
        TickHoles(director, deltaSeconds);
        TickPop(director, deltaSeconds);
    }

    /// <summary>Ages every standing/squashed mole and empties the hole when its window is up.</summary>
    /// <remarks>A mole nobody hit goes down with NO penalty: punishing a miss would make the fastest
    /// child the only one who dares swing.</remarks>
    private void TickHoles(MatchDirector director, float deltaSeconds)
    {
        for (int i = 0; i < _holes.Count; i++)
        {
            int netId = _holes[i];
            if (!_state.TryGetValue(netId, out var hole) || hole.Stage == StageHidden) continue;

            hole.Timer += deltaSeconds;
            float window = hole.Stage == StageUp ? MoleUpSeconds : SquashedSeconds;
            if (hole.Timer < window) continue;

            hole.Stage = StageHidden;
            hole.Timer = 0f;
            director.SetObjectStage(netId, StageHidden);
        }
    }

    private void TickPop(MatchDirector director, float deltaSeconds)
    {
        _popTimer -= deltaSeconds;
        if (_popTimer > 0f) return;

        _popTimer = PopIntervalSeconds;
        if (_holes.Count == 0) return;

        if (StandingCount() >= ConcurrentCap(director)) return;

        _candidates.Clear();
        for (int i = 0; i < _holes.Count; i++)
        {
            if (_state.TryGetValue(_holes[i], out var hole) && hole.Stage == StageHidden)
            {
                _candidates.Add(_holes[i]);
            }
        }

        if (_candidates.Count == 0) return;

        int chosen = _candidates[_rng.Next(_candidates.Count)];
        var state = _state[chosen];

        state.Nonce++;
        state.Color = DrawColor();
        state.Stage = StageUp;
        state.Timer = 0f;

        // ⚠️ Payload BEFORE stage: the client reads the payload on every state, and a stage arriving
        // first would announce a standing mole whose colour is still the previous pop's.
        director.SetObjectPayload(chosen, $"n:{state.Nonce};c:{state.Color}");
        director.SetObjectStage(chosen, StageUp);
    }

    /// <summary>One mole per player, clamped — see <see cref="MinConcurrentUp"/>.</summary>
    private static int ConcurrentCap(MatchDirector director)
    {
        int players = 0;
        foreach (var _ in director.ConnectedPlayers()) players++;
        return Math.Clamp(players, MinConcurrentUp, MaxConcurrentUp);
    }

    private int StandingCount()
    {
        int standing = 0;
        foreach (var hole in _state.Values)
        {
            if (hole.Stage == StageUp) standing++;
        }

        return standing;
    }

    /// <summary>Next colour from the shuffled equal deck; refilled when it runs out.</summary>
    private string DrawColor()
    {
        if (_deck.Count == 0)
        {
            for (int i = 0; i < DeckHalfSize; i++)
            {
                _deck.Add("red");
                _deck.Add("blue");
            }

            for (int i = _deck.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (_deck[i], _deck[j]) = (_deck[j], _deck[i]);
            }
        }

        int last = _deck.Count - 1;
        string color = _deck[last];
        _deck.RemoveAt(last);
        return color;
    }

    /// <summary>A hammer landed on a hole. ⚠️ ALWAYS returns <c>true</c>: a whack is never cosmetic, so
    /// it is never relayed — the truth is the <c>object_state</c> the writes below publish (§10.10).
    /// A rejected swing produces nothing at all, on purpose (§10.5: silently dropped, no penalty).</summary>
    public bool OnObjectEvent(MatchDirector director, int playerId, int netId, string kind, ObjectEventMsg msg)
    {
        if (msg.name != EventWhack || kind != HoleKind) return false;
        if (!_state.TryGetValue(netId, out var hole) || hole.Stage != StageUp) return true;

        // Stale swing: the mole went down, or a new pop already turned the counter over.
        int nonce = msg.i != null && msg.i.Length > 0 ? msg.i[0] : -1;
        if (nonce != hole.Nonce) return true;

        string team = TeamOf(director, playerId);
        // No team = no score owner (an admin or a player who just left); the swing does nothing.
        if (team != "red" && team != "blue") return true;

        bool correct = hole.Color == team;
        Score(director, playerId, team, correct);

        var counts = _counts.TryGetValue(playerId, out var current) ? current : (0, 0);
        _counts[playerId] = correct ? (counts.Item1 + 1, counts.Item2) : (counts.Item1, counts.Item2 + 1);

        hole.Stage = StageSquashed;
        hole.Timer = 0f;
        director.SetObjectPayload(netId,
            $"n:{hole.Nonce};c:{hole.Color};by:{playerId};ok:{(correct ? 1 : 0)}");
        director.SetObjectStage(netId, StageSquashed);

        PushModeState(director);
        return true;
    }

    /// <summary>Writes both channels (§10.5). ⚠️ The TEAM score is floored at 0 while the PLAYER score is
    /// not: a negative team score cannot be explained to a children's audience, but a wrong hit that
    /// left no trace on its own contributor would be invisible. The two channels are therefore not each
    /// other's sum.</summary>
    private static void Score(MatchDirector director, int playerId, string team, bool correct)
    {
        if (correct)
        {
            director.AddScore(team, WhackPoints);
            director.AddPlayerScore(playerId, WhackPoints);
            return;
        }

        int current = team == "red" ? director.ScoreRed : director.ScoreBlue;
        director.AddScore(team, -Math.Min(WhackPoints, Math.Max(0, current)));
        director.AddPlayerScore(playerId, -WhackPoints);
    }

    private static string TeamOf(MatchDirector director, int playerId)
    {
        foreach (var player in director.ConnectedPlayers())
        {
            if (player.PlayerId == playerId) return player.Team;
        }

        return "";
    }

    /// <summary>Per-player counters onto <c>modeState</c> (§10.5 <c>p&lt;id&gt;:&lt;doğru&gt;/&lt;yanlış&gt;</c>).</summary>
    private void PushModeState(MatchDirector director)
    {
        if (_counts.Count == 0)
        {
            director.SetModeState("");
            return;
        }

        var parts = new List<string>(_counts.Count);
        foreach (var pair in _counts)
        {
            parts.Add($"p{pair.Key}:{pair.Value.Correct}/{pair.Value.Wrong}");
        }

        director.SetModeState(string.Join(";", parts));
    }
}
