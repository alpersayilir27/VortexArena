namespace VortexArena.Core.Audio
{
    /// <summary>Game type filter of a <see cref="ModeAudioRegistry"/> rule; <see cref="Any"/> = no
    /// restriction.
    /// <para>⚠️ Serialized enum: new values are appended at the END — inserting shifts the mapping in
    /// the existing asset.</para></summary>
    public enum ModeAudioGameType
    {
        /// <summary>No restriction: the rule fits every game type.</summary>
        Any = 0,

        /// <summary>Only <see cref="VortexArena.Core.GameType.QuickBattle"/>.</summary>
        QuickBattle = 1,

        /// <summary>Only <see cref="VortexArena.Core.GameType.Kids"/>.</summary>
        Kids = 2
    }
}
