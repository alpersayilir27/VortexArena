namespace VortexArena.Core.Combat
{
    /// <summary>WHAT arrives in the hand — the second of <see cref="ItemDefinition"/>'s three
    /// independent axes.
    /// <para>⚠️ <b>Serialized: a new value is APPENDED</b>, and <see cref="PerViewerClone"/> must stay
    /// at 0 — assets written before this field existed read 0, which is today's weapon/bomb
    /// behaviour.</para></summary>
    public enum ItemInstancing
    {
        /// <summary>The scene object stays frozen and a CLONE goes into the hand; each remote viewer
        /// builds its own clone from the <c>itemL</c>/<c>itemR</c> byte (§6.6). The default.</summary>
        PerViewerClone,

        /// <summary>ONE instance exists: the object ITSELF goes into the hand and ownership is handed
        /// over (§10.10).
        /// <para>⚠️ Its <c>itemL</c>/<c>itemR</c> byte stays <b>0</b> and the remote avatar builds NO
        /// copy — the remote hand draws the network object's own instance. Putting the same fact in
        /// both the byte and the object state is two sources of truth that diverge under
        /// latency.</para></summary>
        WorldSingle
    }
}
