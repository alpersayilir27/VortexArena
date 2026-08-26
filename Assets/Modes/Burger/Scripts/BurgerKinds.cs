using System;

namespace VortexArena.Modes.Burger
{
    /// <summary>The client's ONE vocabulary for the Burger mode: kind names, event names, stage numbers
    /// and payload keys.
    /// <para>⚠️ Mirrors the <c>burger</c> table of <c>Docs/ArenaNet-Protokol.md</c> §10.5 one to one. A
    /// literal typed at a call site instead of a constant here is silently rejected by the server
    /// (<c>kinds[].events[]</c> validation) and looks like "the interaction does nothing".</para></summary>
    internal static class BurgerKinds
    {
        // ------------------------------------------------------------------- kinds

        public const string BunWhole = "bun_whole";
        public const string BunBottom = "bun_bottom";
        public const string BunTop = "bun_top";
        public const string Patty = "patty";
        public const string Cheese = "cheese";
        public const string Bacon = "bacon";
        public const string Lettuce = "lettuce";
        public const string Onion = "onion";
        public const string Pickle = "pickle";
        public const string Tomato = "tomato";
        public const string Sauce = "sauce";

        public const string Board = "board";
        public const string Knife = "knife";
        public const string Spatula = "spatula";
        public const string Customer = "customer";

        /// <summary>Dispenser kinds are <c>dispenser_&lt;ingredient&gt;</c>.</summary>
        public const string DispenserPrefix = "dispenser_";

        // ------------------------------------------------------------------- events

        public const string EventTake = "take";
        public const string EventCut = "cut";
        public const string EventGrill = "grill";
        public const string EventServe = "serve";

        // ------------------------------------------------------------------- stages

        public const int CustomerWalking = 0;
        public const int CustomerWaiting = 1;
        public const int CustomerHappy = 2;
        public const int CustomerUnhappy = 3;

        public const int PattyRaw = 0;
        public const int PattyCooked = 1;
        public const int PattyBurnt = 2;

        // ------------------------------------------------------------------- timing

        /// <summary>⚠️ Mirror of the server's <c>BurgerMode</c> constants: the customer's walk is not on
        /// the wire (§10.5), only the stage is. If the two drift apart the customer appears to order
        /// before reaching the counter (or stands still after arriving).</summary>
        public const float CustomerWalkSeconds = 6f;

        /// <inheritdoc cref="CustomerWalkSeconds"/>
        public const float CustomerLeaveSeconds = 4f;

        // ------------------------------------------------------------------- payload keys (§10.10 `s`)

        public const string PayloadSlot = "slot";
        public const string PayloadRecipe = "r";

        /// <summary>Can this kind appear in a recipe? ⚠️ <see cref="BunWhole"/> is NOT one — it is cut
        /// into two halves before it can be stacked.</summary>
        public static bool IsIngredient(string kind)
        {
            if (string.IsNullOrEmpty(kind))
            {
                return false;
            }

            return string.Equals(kind, BunBottom, StringComparison.Ordinal) ||
                   string.Equals(kind, BunTop, StringComparison.Ordinal) ||
                   string.Equals(kind, Patty, StringComparison.Ordinal) ||
                   string.Equals(kind, Cheese, StringComparison.Ordinal) ||
                   string.Equals(kind, Bacon, StringComparison.Ordinal) ||
                   string.Equals(kind, Lettuce, StringComparison.Ordinal) ||
                   string.Equals(kind, Onion, StringComparison.Ordinal) ||
                   string.Equals(kind, Pickle, StringComparison.Ordinal) ||
                   string.Equals(kind, Tomato, StringComparison.Ordinal) ||
                   string.Equals(kind, Sauce, StringComparison.Ordinal);
        }
    }
}
