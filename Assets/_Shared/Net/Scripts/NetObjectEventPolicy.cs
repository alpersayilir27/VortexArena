namespace VortexArena.Net
{
    /// <summary>Who may raise an object event (§10.10)? Carried on the wire as
    /// <c>ArenaProtocol.OBJECT_EVENT_POLICY_*</c>.
    /// <para>⚠️ Serialized: a new value is APPENDED (Unity stores the numeric index).</para></summary>
    public enum NetObjectEventPolicy
    {
        /// <summary>Anyone may raise it.</summary>
        Anyone,

        /// <summary>Only the object's owner (<c>object_state.owner</c>).</summary>
        Owner
    }
}
