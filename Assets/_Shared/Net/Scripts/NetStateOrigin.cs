namespace VortexArena.Net
{
    /// <summary>Where a network object state came from — tells presentation whether effects may play.</summary>
    public enum NetStateOrigin
    {
        /// <summary>object_state — a real change just happened; presentation MAY play effects.</summary>
        Live,

        /// <summary>world_state — a full snapshot (scene load, late joiner, round reset); presentation
        /// applies it SILENTLY: a late joiner must not see an explosion that happened before they joined.</summary>
        Snapshot
    }
}
