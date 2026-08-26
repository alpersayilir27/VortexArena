namespace VortexArena.Core.Combat
{
    /// <summary>How an item GETS INTO the hand — one of the three independent axes of
    /// <see cref="ItemDefinition"/> ("how it arrives" / "what arrives" / "what happens when released").
    /// <para>⚠️ <b>Serialized: a new value is APPENDED.</b> Unity stores the numeric index, and
    /// <see cref="DistanceGrab"/> must stay at 0 — every asset written before this field existed reads
    /// 0, which is exactly today's weapon behaviour. Reordering silently turns the whole arsenal into
    /// something else.</para>
    /// <para>⚠️ "Cannot be grabbed by ray" is expressed by the ISDK component's ABSENCE on the prefab,
    /// never by a filter: an empty candidate list still lets the interactor hover, and
    /// <c>Select()</c> then drops the press off the queue — the press is silently eaten. This enum is
    /// the RULE; the editor guard compares it against the prefab.</para></summary>
    public enum ItemGrabPath
    {
        /// <summary>ISDK distance grab (aim ray + reticle) — the default, and what every existing
        /// asset reads.</summary>
        DistanceGrab,

        /// <summary>A <see cref="GripSocket"/> on the prefab: the hand must enter its accept radius.</summary>
        ProximitySocket,

        /// <summary>Carried on the left wrist (<see cref="WristHolster"/>), taken with the right hand.</summary>
        WristHolster,

        /// <summary>Cannot be taken at all.</summary>
        None
    }
}
