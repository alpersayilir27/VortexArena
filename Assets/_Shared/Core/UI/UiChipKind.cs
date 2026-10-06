namespace VortexArena.Core.UI
{
    /// <summary>
    /// Badge variant (CSS <c>.chip</c> modifiers). <see cref="Good"/> and <see cref="Plain"/> have
    /// no surface at all — text only, no padding.
    /// <para>⚠️ Serialized — new values go to the END (Unity stores the numeric index).</para>
    /// </summary>
    public enum UiChipKind
    {
        Good = 0,
        Plain = 1,
        Warn = 2,
        Bad = 3,
        Dead = 4,

        /// <summary>CSS base <c>.chip</c>: Control surface, muted label, 8 px side padding.</summary>
        Neutral = 5
    }
}
