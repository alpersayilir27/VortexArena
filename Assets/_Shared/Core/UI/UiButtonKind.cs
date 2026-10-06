namespace VortexArena.Core.UI
{
    /// <summary>
    /// Visual role of a themed button (CSS <c>.btn</c> modifiers).
    /// <c>Text*</c> kinds keep the Normal surface and only recolor the label.
    /// <para>⚠️ Serialized — new values go to the END (Unity stores the numeric index).</para>
    /// </summary>
    public enum UiButtonKind
    {
        Normal = 0,
        Plate = 1,
        On = 2,
        Go = 3,
        Danger = 4,
        Confirm = 5,
        WarnFill = 6,

        /// <summary>Inside a <c>.seg</c> group: transparent surface, no border.</summary>
        Seg = 7,

        /// <summary>Selected item of a <c>.seg</c> group.</summary>
        SegOn = 8,

        TextGood = 9,
        TextBad = 10,
        TextRed = 11,
        TextBlue = 12,

        /// <summary>Idle panel tab (<c>.tab</c>): faint wash, muted label; active tab = <see cref="SegOn"/>.</summary>
        Tab = 13
    }
}
