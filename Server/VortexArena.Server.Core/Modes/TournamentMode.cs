#nullable enable
namespace VortexArena.Server.Core.Modes;

/// <summary>Round based team elimination (a bombless "Search &amp; Destroy"): no revive within a
/// round, a team wiped out ends the round and gives the winner +1 round; the match ends at
/// <see cref="MatchDirector.ScoreLimit"/> rounds.</summary>
/// <remarks>Differs from the TDM default at a SINGLE point (§10.5): <see cref="ReviveAnchor.None"/>.
/// <para>⚠️ The round concept is not a rule field and never enters <see cref="ModeRules"/> — rounds are
/// this class's internal state, unknown to the core (§10.1). Their only wire traces are
/// <c>modeState</c> and the <c>health_update</c>s the mode asks for when a round CLOSES.</para>
/// <para>Score means something else here: <c>scoreRed</c>/<c>scoreBlue</c> count rounds won. The
/// ROUND has no clock of its own: <c>roundSeconds</c> bounds the whole MATCH, as in every other mode —
/// a round is decided by elimination.</para></remarks>
public sealed class TournamentMode : IGameMode
{
    /// <summary>Interval of the "who is missing" console line while the regroup drags on.</summary>
    /// <remarks>⚠️ NOT a timeout — it never starts the round, only prints diagnostics. The regroup ends
    /// only when everyone is in their base, or the operator kicks / aborts (§10.1); the server window
    /// is the operator's only view of who is stuck.</remarks>
    private const double RegroupReportIntervalSeconds = 30.0;

    /// <summary>Where the between-rounds flow currently is.</summary>
    /// <remarks>⚠️ Not a copy of the core's pause reason: while the phase is not <c>Playing</c> the mode
    /// can be ticked in three situations (the match's first countdown, our regroup, our countdown) with
    /// different work in each. We set the pause, so we remember our own decision.</remarks>
    private enum RoundStage
    {
        /// <summary>Flow not running: the match's first countdown, or mid-round.</summary>
        None,

        /// <summary>Our own pause — waiting for everyone to return to their base.</summary>
        Regroup,

        /// <summary>Regroup over, round countdown running (still cancellable).</summary>
        Countdown
    }

    private RoundStage _stage;

    private int _round;

    /// <summary>Is the round in combat — the elimination scan runs only then; <c>false</c> during the
    /// countdown so an unstarted round cannot end on "everyone is dead".</summary>
    private bool _roundLive;

    private bool _matchOver;
    private MatchOutcome _outcome = MatchOutcome.Draw;

    /// <summary>Seconds left of the whole MATCH; carried across rounds because the core resets its clock
    /// at every <c>playing</c> entry.</summary>
    /// <remarks>⚠️ Only counted down in <c>playing</c> (the core's own clock does that) — the regroup and
    /// the countdown between rounds do NOT burn match time, or a slow return would eat the match.
    /// <para>The match ends on whichever fills first: this clock or the round limit. With
    /// <c>scoreLimit</c> unlimited the clock is the only end condition left (besides
    /// <c>end_match</c>).</para></remarks>
    private float _matchRemaining;

    /// <summary>Time of the next "waiting for regroup" console line (see
    /// <see cref="RegroupReportIntervalSeconds"/>).</summary>
    private DateTime _nextRegroupReportAt;

    public string ModeId => "tournament";

    /// <summary>The tournament rule shape.</summary>
    /// <remarks><c>Revive = None</c>: no revive within a round (§10.4). <c>RespawnDelay = 0</c> is its
    /// complement — a client countdown to a revive that never happens would be a lie.
    /// <para>Everything else is deliberately the default: two teams, team score, friendly fire off,
    /// weapon standing in the scene (so magazine/reserve accounting works — reload is disabled under
    /// <c>RandomGrant</c>).</para></remarks>
    public ModeRules Rules => new()
    {
        Revive = ReviveAnchor.None,
        RespawnDelay = 0f
    };

    /// <summary>The length (seconds) of the whole MATCH — rounds are not time-boxed.</summary>
    /// <remarks>Longer than the other modes' default on purpose: a free-roam round ends by elimination
    /// in tens of seconds, so this has to cover a whole best-of.</remarks>
    public int DefaultRoundSeconds => 600;

    /// <summary>ROUNDS needed to win the match → best-of-7.</summary>
    /// <remarks>The operator can override it (§5.2) or choose unlimited
    /// (<c>ArenaProtocol.SCORE_LIMIT_UNLIMITED</c>): then neither win limit nor round cap applies and
    /// rounds run until the match clock expires (or the operator's <c>end_match</c>).</remarks>
    public int DefaultScoreLimit => 4;

    public void OnMatchStart(MatchDirector director)
    {
        // The mode instance is reused across matches for the server's lifetime → reset state HERE.
        // (OnMatchStart runs once per match; rounds do not trigger it.)
        _round = 1;
        _roundLive = false;
        _matchOver = false;
        _outcome = MatchOutcome.Draw;
        _stage = RoundStage.None;

        // ⚠️ The operator's duration is the MATCH's, never a round's: a free-roam round is decided by
        // elimination in tens of seconds, and a per-round clock that restarts forever would leave an
        // unlimited match with NO end condition at all.
        var limit = director.ScoreLimit;
        _matchRemaining = director.RoundSeconds;

        Console.WriteLine($"[tournament] maç başladı — MAÇ süresi {director.RoundSeconds} sn; " +
                          (limit > 0
                              ? $"{limit} tur galibiyet (en fazla {MaxRounds(limit)} tur) — " +
                                "hangisi önce dolarsa maç orada biter."
                              : "SINIRSIZ tur — süre dolunca çok tur alan kazanır."));
    }

    public void OnRoundStart(MatchDirector director)
    {
        _roundLive = true;
        _stage = RoundStage.None;

        // The core restarted its clock at roundSeconds; here that clock is the MATCH's, so the remaining
        // budget is written back over it.
        director.SetTimeRemaining(_matchRemaining);

        director.SetModeState($"round:{_round}");
        Console.WriteLine($"[tournament] tur {_round} başladı " +
                          $"(kırmızı {director.ScoreRed} : mavi {director.ScoreBlue}).");
    }

    // ⚠️ OnKill is deliberately unimplemented: points come from ROUNDS, not kills, and the server
    // already counts individual K/D (§10.2). Elimination is checked in OnTick — see EvaluateRound.

    public void OnTick(MatchDirector director, float deltaSeconds)
    {
        if (_matchOver) return;

        if (director.CurrentPhase == Phase.Playing)
        {
            if (_roundLive) EvaluateRound(director);
            return;
        }

        // Not Playing: three situations reach here with different work — told apart by our own stage
        // (see RoundStage).
        switch (_stage)
        {
            case RoundStage.Regroup:
                TickRegroup(director);
                break;
            case RoundStage.Countdown:
                TickCountdownWatch(director);
                break;
            // RoundStage.None: only the match's FIRST countdown (from the loading gate, not a
            // regroup). Polling the regroup here would print "TOPLANMA" on the HUD before round one.
        }
    }

    /// <summary>⚠️ <c>TimeRemaining &lt;= 0</c> is not answered here directly (unlike TDM/FFA): the clock
    /// running out first closes the RUNNING round, and the match can only be decided after that. Every
    /// decision is made in <see cref="EndRound"/>; this method just carries it.</summary>
    public bool IsMatchOver(MatchDirector director, out MatchOutcome outcome)
    {
        outcome = _outcome;
        return _matchOver;
    }

    // ---------------------------------------------------------------- round flow

    /// <summary>Measures whether the round is over (10 Hz).</summary>
    /// <remarks>⚠️ Tick and not <see cref="OnKill"/>, because a team is also emptied by disconnects,
    /// where <c>OnKill</c> never fires. Two triggers would mean two code paths and a rule forgotten in
    /// one; a single scan is one source of truth, and its cost is negligible.</remarks>
    private void EvaluateRound(MatchDirector director)
    {
        int redOnline = 0, blueOnline = 0;
        int redAlive = 0, blueAlive = 0;
        // "Fit to fight" = alive AND calibrated: an uncalibrated player neither fires nor takes damage
        // (§10.6), so they do not count as being on the field.
        int redFit = 0, blueFit = 0;

        foreach (var player in director.ConnectedPlayers())
        {
            var isRed = player.Team == "red";
            var isBlue = player.Team == "blue";
            if (!isRed && !isBlue) continue; // a late joiner with no team assigned — not part of the round

            if (isRed) redOnline++; else blueOnline++;
            if (!player.Alive) continue;
            if (isRed) redAlive++; else blueAlive++;
            if (!player.Calibrated) continue;
            if (isRed) redFit++; else blueFit++;
        }

        // One-sided match (admin map preview or a team fully dropped): do not advance, else the server
        // would hand out a round per second in an empty arena. Way out is abort_match — same decision
        // as "a match with no players" (§10.1).
        if (redOnline == 0 || blueOnline == 0) return;

        // ELIMINATION ignores calibration: an uncalibrated player is not dead and keeps their team
        // standing. That round simply goes to time, where the comparison below leaves them out.
        if (redAlive == 0 && blueAlive == 0)
        {
            EndRound(director, "", "karşılıklı eleme");
            return;
        }
        if (blueAlive == 0)
        {
            EndRound(director, "red", "mavi takım elendi");
            return;
        }
        if (redAlive == 0)
        {
            EndRound(director, "blue", "kırmızı takım elendi");
            return;
        }

        if (director.TimeRemaining > 0f) return;

        // The MATCH clock ran out mid-round → this round is the last, and is closed normally (more
        // players standing wins, a tie scores nothing); EndRound then decides the match.
        var winner = redFit > blueFit ? "red" : blueFit > redFit ? "blue" : "";
        EndRound(director, winner, $"maç süresi doldu ({redFit} - {blueFit} savaşabilir ayakta)");
    }

    /// <summary>Closes the round: writes the point, checks for match end, otherwise moves to the
    /// regroup.</summary>
    private void EndRound(MatchDirector director, string winnerTeam, string reason)
    {
        _roundLive = false;
        if (winnerTeam.Length > 0) director.AddScore(winnerTeam, 1);

        // Carried over: the core restarts its clock at every round, so what is left of the MATCH is only
        // knowable here (see _matchRemaining).
        _matchRemaining = director.TimeRemaining;

        Console.WriteLine($"[tournament] tur {_round} bitti — {reason}; " +
                          $"{(winnerTeam.Length > 0 ? winnerTeam + " +1" : "puan yok")} " +
                          $"(kırmızı {director.ScoreRed} : mavi {director.ScoreBlue}).");

        // ⚠️ One gate for both the win limit and the round cap, since both derive from the same number —
        // leaving either open would silently end an "unlimited" match.
        var limit = director.ScoreLimit;
        if (limit > 0)
        {
            if (director.ScoreRed >= limit)
            {
                Decide(MatchOutcome.Team("red"), "skor limiti");
                return;
            }
            if (director.ScoreBlue >= limit)
            {
                Decide(MatchOutcome.Team("blue"), "skor limiti");
                return;
            }

            // Round cap: drawn rounds can keep anyone from reaching the limit, and a LIMITED match must
            // not run forever. At the cap the higher score wins, equal is a draw.
            if (_round >= MaxRounds(limit))
            {
                var red = director.ScoreRed;
                var blue = director.ScoreBlue;
                Decide(red > blue ? MatchOutcome.Team("red")
                    : blue > red ? MatchOutcome.Team("blue")
                    : MatchOutcome.Draw, "tur tavanı");
                return;
            }
        }

        // The MATCH clock, checked AFTER the limit so a round that both reaches the limit and runs the
        // clock out is reported as the win it is. With unlimited rounds this is the only gate left.
        if (_matchRemaining <= 0f)
        {
            var red = director.ScoreRed;
            var blue = director.ScoreBlue;
            Decide(red > blue ? MatchOutcome.Team("red")
                : blue > red ? MatchOutcome.Team("blue")
                : MatchOutcome.Draw, "maç süresi doldu");
            return;
        }

        // The result rides the SAME broadcast that opens the regroup (§10.1): match_state already
        // carries the new score, and a message of its own would be a second sender for one fact.
        // ⚠️ Built BEFORE the increment — it names the round that just CLOSED. The number is not
        // decoration: without it two rounds won by the same team are the same string and the client's
        // latch swallows the second.
        var result = $"roundend:{(winnerTeam.Length > 0 ? winnerTeam : "draw")}:{_round}";

        _round++;

        // Regroup: everyone physically walks to their own base (free-roam — nobody is teleported, §10.4).
        if (!director.TryPauseForMode(result))
        {
            // abort_match / an operator pause may have come in between: the round flow is out of our
            // hands, so log it rather than fail silently.
            Console.WriteLine("[tournament] toplanmaya geçilemedi (faz değişmiş) — tur akışı durdu.");
            return;
        }

        // ⚠️ TryPauseForMode ZEROES the clock — right where that clock belongs to the round, a lie here:
        // the HUD would sit at 00:00 through the whole regroup and jump back up when the round opens,
        // which reads as a bug.
        director.SetTimeRemaining(_matchRemaining);

        // Full health the MOMENT the round closes, not when the next one opens: the eliminated player
        // walks to their base during the regroup, and leaving them on the death screen for that walk
        // makes "the round is over" indistinguishable from "I am still dead". The return value needs no
        // handling — the core refreshes the roster at the playing gate anyway, so a miss here only costs
        // the early timing.
        director.TryReviveRosterForMode();

        _stage = RoundStage.Regroup;
        ScheduleRegroupReport();
    }

    /// <summary>The between-rounds regroup (phase <c>paused</c>/<c>mode</c>): the new round's countdown
    /// starts once everyone is in their own base zone and has sent <c>set_ready{true}</c>.</summary>
    /// <remarks>No new protocol message — the <c>ready</c> flag already means "I am ready" at the
    /// loading gate (§10.1). "Am I in the base" is the client's decision; the server keeps the ledger
    /// rather than refereeing (§10.3, same contract as <c>reviveAnchor</c>).
    /// <para>⚠️ There is NO forced start: opening a round with players missing defeats the point of
    /// waiting for the full roster (someone counted as eliminated is on the field). A stuck headset can
    /// hold the match indefinitely — the way out is the operator's <c>kick</c> or <c>abort_match</c>; a
    /// kicked player also leaves <see cref="CountInBase"/>'s total, so the round starts on that
    /// tick.</para></remarks>
    private void TickRegroup(MatchDirector director)
    {
        var (ready, total) = CountInBase(director);

        // ⚠️ This is also what OVERWRITES the roundend state one tick after EndRound opened the pause —
        // the client latches that value on arrival rather than polling for it (§10.1).
        director.SetModeState($"regroup:{ready}/{total}"); // broadcasts only if it CHANGED

        if (total == 0) return; // nobody left — wait for the operator's abort_match

        if (ready < total)
        {
            ReportRegroupWaiting(director, ready, total);
            return;
        }

        if (!director.TryStartRound()) return;

        _stage = RoundStage.Countdown;
        Console.WriteLine($"[tournament] tur {_round} geri sayımı başlıyor (herkes tabanında).");
    }

    /// <summary>Periodic diagnostic line naming who is missing — the operator's only view from the
    /// server window, sourced from the same flag as the roster's ready state.</summary>
    private void ReportRegroupWaiting(MatchDirector director, int ready, int total)
    {
        var now = DateTime.UtcNow;
        if (now < _nextRegroupReportAt) return;

        _nextRegroupReportAt = now.AddSeconds(RegroupReportIntervalSeconds);

        var missing = string.Join(", ", director.ConnectedPlayers()
            .Where(p => !p.Ready)
            .Select(p => p.Name));
        Console.WriteLine($"[tournament] toplanma bekleniyor ({ready}/{total}) — " +
                          $"tabanına dönmeyenler: {missing}");
    }

    /// <summary>Schedules the first diagnostic line (on every entry into the regroup).</summary>
    private void ScheduleRegroupReport() =>
        _nextRegroupReportAt = DateTime.UtcNow.AddSeconds(RegroupReportIntervalSeconds);

    /// <summary>Countdown watchdog: the regroup condition holds throughout the countdown, so one player
    /// leaving their base cancels it and returns to the regroup (counter restarts).</summary>
    /// <remarks>Measured "once on entry", a player could touch the base for a second and leave, and the
    /// round would catch them mid-field; the rule is "WAIT in the base", not "drop by".
    /// <para>⚠️ The cancellation has no exception: the countdown is always opened by the regroup
    /// condition (no forced start), so it is always undoable.</para></remarks>
    private void TickCountdownWatch(MatchDirector director)
    {
        var (ready, total) = CountInBase(director);
        if (total == 0 || ready >= total) return;

        // Counter ran out on this tick → the round already started, so it returns false and we undo
        // nothing.
        if (!director.TryCancelCountdownForMode($"regroup:{ready}/{total}")) return;

        _stage = RoundStage.Regroup;
        ScheduleRegroupReport();

        Console.WriteLine($"[tournament] geri sayım İPTAL — tabanından çıkan var ({ready}/{total}); " +
                          "toplanmaya dönüldü.");
    }

    /// <summary>How many players are in their base (<c>ready</c>) out of the connected total.</summary>
    /// <remarks>Here <c>ready</c> means "I am in my own base right now" and the client updates it in
    /// both directions (§10.1) — gate and watchdog read the same counter.</remarks>
    private static (int ready, int total) CountInBase(MatchDirector director)
    {
        var total = 0;
        var ready = 0;
        foreach (var player in director.ConnectedPlayers())
        {
            total++;
            if (player.Ready) ready++;
        }

        return (ready, total);
    }

    private void Decide(MatchOutcome outcome, string reason)
    {
        _outcome = outcome;
        _matchOver = true;
        Console.WriteLine($"[tournament] maç kararı ({reason}) — {_round}. turda bitti.");
    }

    /// <summary>Best-of cap: <c>2 × limit − 1</c> rounds; no limit (unlimited match) = no cap, the match
    /// clock ends it.</summary>
    /// <remarks>⚠️ May also be called limitless just to produce text — the decision gate in
    /// <see cref="EndRound"/> is already closed by <c>limit &gt; 0</c>.</remarks>
    private static int MaxRounds(int scoreLimit) =>
        scoreLimit > 0 ? 2 * scoreLimit - 1 : int.MaxValue;
}
