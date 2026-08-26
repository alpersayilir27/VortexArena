#nullable enable
using VortexArena.Protocol;

namespace VortexArena.Server.Core.Modes;

/// <summary>Implemented by every game mode and plugged into the MatchDirector.</summary>
/// <remarks>ModeId matches start_match.modeId and the Unity mode box key exactly (e.g. "tdm"); a new
/// modId is recorded in Docs/ArenaNet-Protokol.md.
/// <para>All hooks are called OUTSIDE the MatchDirector's lock, so the director's public API
/// (ScoreRed/AddScore/TimeRemaining/ConnectedPlayers…) is free to use — it takes its own lock. Do no
/// blocking work inside a hook (the match tick is 10 Hz).</para>
/// <para>⚠️ New hooks get a DEFAULT BODY so existing modes stay untouched; a dead hook every mode must
/// implement empty is a tax on every new mode. For the same reason a hook with no consumer is not
/// added — adding it later is free.</para></remarks>
public interface IGameMode
{
    /// <summary>Key matching start_match.modeId.</summary>
    string ModeId { get; }

    /// <summary>The mode's SHAPE (§10.5): team mode, score channel, friendly fire, revive, weapon
    /// source.</summary>
    /// <remarks>The MatchDirector configures itself from it and sends it via load_match.rules;
    /// <see cref="ModeRules.TeamDefault"/> gives today's TDM behaviour.</remarks>
    ModeRules Rules { get; }

    /// <summary>Game family this mode belongs to (§11 maps.json gameType). Default: quick battle.</summary>
    string GameType => "quickbattle";

    /// <summary>Default MATCH duration for load_match.roundSeconds — a default, not a lock: the admin
    /// can override it per match (§5.2).</summary>
    /// <remarks>⚠️ The name is the wire field's, not a promise about rounds: a round-based mode spends
    /// this budget across ALL its rounds (§10.5).</remarks>
    int DefaultRoundSeconds { get; }

    /// <summary>Default score limit for load_match.scoreLimit (overridable the same way).</summary>
    int DefaultScoreLimit { get; }

    /// <summary>true keeps the <c>finished</c> result screen up until the operator acts: the core's
    /// <c>MATCH_END_SECONDS</c> auto-return to the lobby is switched OFF (§10.1).</summary>
    /// <remarks>For a mode whose scoreboard is the match's product (round based play, §10.5) that timer
    /// wipes the table exactly as the referee reads it out. The exit stays what it always was —
    /// <c>return_to_lobby</c> / <c>abort_match</c> / a new <c>start_match</c>.</remarks>
    bool HoldsResultForOperator => false;

    /// <summary>Called ONCE per match, on the first transition to Live.</summary>
    /// <remarks>⚠️ In a round based mode (<c>tournament</c>) later Live entries do NOT retrigger it —
    /// "match started" and "round started" are separate events (see <see cref="OnRoundStart"/>).</remarks>
    void OnMatchStart(MatchDirector director);

    /// <summary>Called on EVERY entry into Live, including the first round right after
    /// <see cref="OnMatchStart"/>; the moment damage is enabled.</summary>
    /// <remarks>Modes with no round concept ignore it.</remarks>
    void OnRoundStart(MatchDirector director) { }

    /// <summary>true moves the phase to End (time up / score limit); the winner comes back in
    /// <see cref="MatchOutcome"/> per the mode's score channel
    /// (<see cref="ModeRules.Scoring"/>).</summary>
    bool IsMatchOver(MatchDirector director, out MatchOutcome outcome);

    /// <summary>Scoring after a validated kill; write ONLY through the director's ledger (AddScore /
    /// AddPlayerScore).</summary>
    void OnKill(MatchDirector director, int killerId, int victimId, string weaponId) { }

    /// <summary>Called on the 10 Hz match tick, Live phase only; the MatchDirector runs the
    /// clock.</summary>
    void OnTick(MatchDirector director, float deltaSeconds) { }

    /// <summary>Called after every validated hit, kill or not — for damage based
    /// scoring/statistics.</summary>
    void OnHitApplied(MatchDirector director, int attackerId, int targetId, float damage, bool killed) { }

    /// <summary>An <c>object_event</c> that passed every gate (§10.10). <paramref name="kind"/> is the
    /// object's kind; meaning comes from the <c>kind</c> + <c>msg.name</c> pair.</summary>
    /// <remarks>⚠️ The RETURN VALUE answers ONE question — is the event relayed? <c>true</c> = "I handled
    /// it, do not relay"; <c>false</c> = cosmetic, so the same <c>object_event</c> goes out to everyone.
    /// Relaying a handled event would announce one fact twice and the client would play the presentation
    /// twice.
    /// <para>Writes go through the director (<c>SetObjectStage</c>/<c>SetObjectFlags</c>/
    /// <c>SetObjectPayload</c>/<c>SpawnObject</c>/<c>DespawnObject</c>) — the mode never touches the
    /// table — and each of those broadcasts its own <c>object_state</c>, because ONE event may change
    /// several objects.</para></remarks>
    bool OnObjectEvent(MatchDirector director, int playerId, int netId, string kind, ObjectEventMsg msg) => false;
}
