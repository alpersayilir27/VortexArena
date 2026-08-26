namespace VortexArena.Core.Combat
{
    /// <summary>What happens when the item LEAVES the hand — the third of
    /// <see cref="ItemDefinition"/>'s three independent axes.
    /// <para>⚠️ <b>Serialized: a new value is APPENDED</b>, and <see cref="Return"/> must stay at 0 —
    /// assets written before this field existed read 0, which is today's weapon behaviour.</para>
    /// <para>This axis, not an ownership rule, is what says "a knocked-off object goes back": the
    /// server always frees a disconnected owner's object where it last was (§10.10), and an item that
    /// wants to be re-seated says so here.</para></summary>
    public enum ItemReleaseMode
    {
        /// <summary>Back to where it belongs: the clone disappears and the frame returns, or the world
        /// object is re-seated on its socket. The default.</summary>
        Return,

        /// <summary>The rigidbody is set free (throwable prop).</summary>
        Physics
    }
}
