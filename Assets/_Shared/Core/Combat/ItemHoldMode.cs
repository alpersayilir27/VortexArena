namespace VortexArena.Core.Combat
{
    /// <summary>
    /// How many hands an item is held with (<see cref="ItemDefinition.HoldMode"/>).
    /// It is the static half of the remote rendering's "should I stick the free hand onto the
    /// grip" decision; the dynamic half is the <c>FLAG_GRIP_LINKED</c> bit coming over the wire
    /// (Docs/ArenaNet-Protokol.md §6.6).
    /// <para>
    /// ⚠️ <b>Serialized enum</b> — Unity stores a numeric index. A new value is only appended at
    /// the END; inserting at the start/middle silently shifts the values in existing
    /// <c>WD_*.asset</c> files.
    /// </para>
    /// </summary>
    public enum ItemHoldMode
    {
        OneHand,
        TwoHand
    }
}
