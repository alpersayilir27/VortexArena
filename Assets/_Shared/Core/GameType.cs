namespace VortexArena.Core
{
    /// <summary>
    /// Game type — the operator's FIRST choice, which groups the maps (§11). The round type
    /// (<c>modeId</c>) sits UNDER it: a game type is picked first, then a round type within it.
    /// <para>⚠ SERIALIZED by <see cref="ModeDefinition"/> and <c>MapDefinition</c> — new values are
    /// appended at the END.</para>
    /// </summary>
    public enum GameType
    {
        /// <summary>Competitive arena play; wire name <c>"quickbattle"</c>.</summary>
        QuickBattle,

        /// <summary>Children's games; wire name <c>"kids"</c>.</summary>
        Kids
    }

    /// <summary>Wire names of <see cref="GameType"/> (§11).</summary>
    public static class GameTypeIds
    {
        public const string QuickBattle = "quickbattle";
        public const string Kids = "kids";

        /// <summary>Enum → wire name.</summary>
        public static string ToWire(GameType type) => type == GameType.Kids ? Kids : QuickBattle;
    }
}
