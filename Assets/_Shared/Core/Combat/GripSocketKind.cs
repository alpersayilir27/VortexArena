namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Which hand the grip point is for: the grip or the foregrip.
    /// <para>
    /// ⚠️ <b>Serialized enum</b> — Unity stores a numeric index. A new value is only appended at
    /// the END; inserting at the start/middle silently shifts the recorded values (what was
    /// Primary becomes Secondary and the grip is read from the wrong field).
    /// </para>
    /// </summary>
    public enum GripSocketKind
    {
        /// <summary>Main hand (trigger hand).</summary>
        Primary,

        /// <summary>Foregrip — only meaningful in <see cref="ItemHoldMode.TwoHand"/>.</summary>
        Secondary
    }
}
