namespace VortexArena.Core.Combat
{
    /// <summary>What makes a thrown item go off (Docs/ArenaNet-Protokol.md §6.4, throw contract).</summary>
    /// <remarks>⚠️ Serialized: new values go at the END (Unity stores the numeric index).</remarks>
    public enum ThrowableTrigger
    {
        /// <summary>Timed from the RELEASE instant — the bomb. Holding it starts nothing.</summary>
        Fuse = 0,

        /// <summary>First contact with static geometry — the molotov pattern.</summary>
        Impact = 1
    }
}
