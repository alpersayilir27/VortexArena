namespace VortexArena.Core.Audio
{
    /// <summary>Triggers for announcement sounds that vary by mode/map. Which clip plays for which
    /// trigger lives in <see cref="ModeAudioRegistry"/>; <see cref="GameAudio"/> is the only player.
    /// <para>⚠️ Serialized enum: new values are appended at the END — inserting shifts the mapping in
    /// the existing asset.</para></summary>
    public enum ModeAudioEvent
    {
        /// <summary>Phase moved to <c>playing</c>: match start in single-round modes (tdm, ffa) and
        /// EVERY round start in round-based ones (tournament) — the same transition.</summary>
        RoundStart = 0,

        /// <summary><see cref="ModeAudioRegistry.Rule.WarningSeconds"/> left until the round ends.
        /// <para>Writing a rule for this trigger is how a mode is declared "round-based" — the core
        /// does not interpret <c>modeState</c>.</para></summary>
        RoundEndWarning = 1,

        /// <summary><see cref="ModeAudioRegistry.Rule.WarningSeconds"/> left until the match ends.
        /// Takes over when no rule matches <see cref="RoundEndWarning"/>.</summary>
        MatchEndWarning = 2,

        /// <summary>A round ended AND another follows: the mode asked to pause (phase
        /// <c>playing</c> → <c>paused</c>, <c>phaseReason == "mode"</c>). Start of the between-rounds
        /// gathering in round-based modes.
        /// <para>⚠️ The round that ENDS THE MATCH does not fire this trigger: there the phase goes
        /// straight to <c>finished</c> and the match result (<see cref="GameSoundId"/>) takes over
        /// the announcement — there is no next round to return to positions for.</para></summary>
        RoundEnd = 3
    }
}
