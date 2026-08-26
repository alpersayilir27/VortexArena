namespace VortexArena.Net
{
    /// <summary>Which match phase accepts an object event (§10.10)? Carried on the wire as
    /// <c>ArenaProtocol.OBJECT_PHASE_GATE_*</c>.
    /// <para>⚠️ Serialized: a new value is APPENDED (Unity stores the numeric index). The stricter gate
    /// is index 0 on purpose — a default-constructed rule must not open the lobby.</para></summary>
    public enum NetObjectPhaseGate
    {
        /// <summary>Only while the match is <c>playing</c>.</summary>
        Playing,

        /// <summary>Any phase — the lobby too.</summary>
        Any
    }
}
