namespace VortexArena.Core.Audio
{
    /// <summary>Local mix channels (<see cref="AudioMix"/>).</summary>
    /// <remarks>⚠️ The values are used as INDICES (the mix array and the admin panel's serialized
    /// arrays) → a new channel is APPENDED, never inserted.</remarks>
    public enum AudioChannel
    {
        /// <summary>The map's ambience loop.</summary>
        Ambience = 0,

        /// <summary>Weapon SFX (fire, magazine, dry fire) — local and remote shots alike.</summary>
        Weapons = 1,

        /// <summary>Announcement/voiceover clips.</summary>
        Voiceover = 2,

        /// <summary>The map's music loop — a channel of its own, separate from ambience.</summary>
        Music = 3,
    }
}
