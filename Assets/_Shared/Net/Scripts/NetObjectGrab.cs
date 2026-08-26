namespace VortexArena.Net
{
    /// <summary>Can this KIND be picked up (§10.10)? Carried on the wire as
    /// <c>ArenaProtocol.OBJECT_GRAB_*</c>.
    /// <para>⚠️ Serialized: a new value is APPENDED — Unity stores the numeric index, so reordering
    /// silently turns every asset's grab rule into a different one.</para></summary>
    public enum NetObjectGrab
    {
        /// <summary>Default — cannot be picked up.</summary>
        None,

        /// <summary>A free object goes to the first asker; there is no stealing (§10.10).</summary>
        Anyone
    }
}
