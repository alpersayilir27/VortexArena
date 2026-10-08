using UnityEngine;

namespace VortexArena.Core.UI
{
    /// <summary>
    /// "Lokanta" skin constants — the ONE place <c>Docs/Gelistirici/Arayuz/asci.css</c>'s tokens
    /// live in code (Burger mode HUD, order bubble, thermometer, result screen).
    /// <para>
    /// ⚠️ <b>Do not hand-pick a colour at a call site</b> (same rule as <see cref="Girdap"/>): the
    /// mockup is the single source of truth and a literal copied into a screen drifts silently.
    /// </para>
    /// <para>
    /// ⚠️ This is a SECOND palette on purpose — it skins one mode, it does not replace
    /// <see cref="Girdap"/> on admin/result screens.
    /// </para>
    /// </summary>
    public static class Lokanta
    {
        // ---------------------------------------------------------- surfaces

        public static readonly Color Ink = Girdap.Hex(0x4A2410);
        public static readonly Color Cream = Girdap.Hex(0xFFF4DC);
        public static readonly Color Cream2 = Girdap.Hex(0xFFE6B8);
        public static readonly Color Paper = Girdap.Hex(0xFFFBF2);
        public static readonly Color Light = Girdap.Hex(0xFFF8EA);

        /// <summary>Card fill while a patty is over the warn threshold.</summary>
        public static readonly Color HotCard = Girdap.Hex(0xFFD9D4);

        public static readonly Color BurntCard = Girdap.Hex(0xE6D8C4);

        // ------------------------------------------------------------ accents

        public static readonly Color Red = Girdap.Hex(0xE8584F);
        public static readonly Color Red2 = Girdap.Hex(0xC9342C);
        public static readonly Color RedHi = Girdap.Hex(0xF0675E);

        public static readonly Color Must = Girdap.Hex(0xF4B942);
        public static readonly Color Must2 = Girdap.Hex(0xE09A1B);
        public static readonly Color MustHi = Girdap.Hex(0xFFD36A);
        public static readonly Color MustSoft = Girdap.Hex(0xFFE28A);

        public static readonly Color Green = Girdap.Hex(0x5BB544);
        public static readonly Color Green2 = Girdap.Hex(0x3E8F2E);
        public static readonly Color Sky = Girdap.Hex(0x5FA8E8);

        /// <summary>Flame icon tint while a patty is actually on the grill.</summary>
        public static readonly Color Flame = Girdap.Hex(0xE8582E);

        // -------------------------------------------------- thermometer mercury
        // A/B = gradient top/bottom of the tube fill, one pair per cooking stage.

        public static readonly Color MercuryRawA = Girdap.Hex(0xE8A0A0);
        public static readonly Color MercuryRawB = Girdap.Hex(0xF5C2C2);
        public static readonly Color MercuryCookedA = Girdap.Hex(0x5A2E18);
        public static readonly Color MercuryCookedB = Girdap.Hex(0x9A5A36);
        public static readonly Color MercuryHotA = Girdap.Hex(0x5A2E18);
        public static readonly Color MercuryHotB = Girdap.Hex(0xD23A2E);
        public static readonly Color MercuryBurntA = Girdap.Hex(0x1A100A);
        public static readonly Color MercuryBurntB = Girdap.Hex(0x3A2418);

        // ------------------------------------------------- ingredient slabs
        // CSS `.bing.*`; A = gradient top, B = gradient bottom.

        public static readonly Color BunBottomA = Girdap.Hex(0xF2BE6E);
        public static readonly Color BunBottomB = Girdap.Hex(0xD1903F);
        public static readonly Color BunTopA = Girdap.Hex(0xF7C877);
        public static readonly Color BunTopB = Girdap.Hex(0xD79A48);
        public static readonly Color PattyA = Girdap.Hex(0x8A4E2E);
        public static readonly Color PattyB = Girdap.Hex(0x5A2E18);
        public static readonly Color CheeseA = Girdap.Hex(0xFFD54A);
        public static readonly Color CheeseB = Girdap.Hex(0xF0A81B);
        public static readonly Color LettuceA = Girdap.Hex(0x86DC66);
        public static readonly Color LettuceB = Girdap.Hex(0x4CAB3F);
        public static readonly Color TomatoA = Girdap.Hex(0xF25A4C);
        public static readonly Color TomatoB = Girdap.Hex(0xC7332A);
        public static readonly Color OnionA = Girdap.Hex(0xF8EEFF);
        public static readonly Color OnionB = Girdap.Hex(0xD3BEE8);
        public static readonly Color PickleA = Girdap.Hex(0xA8D45E);
        public static readonly Color PickleB = Girdap.Hex(0x6B9C34);
        public static readonly Color SauceA = Girdap.Hex(0xFFA43B);
        public static readonly Color SauceB = Girdap.Hex(0xE07314);

        /// <summary>Bacon is drawn as stripes: <see cref="BaconStripe"/> bars over this base.</summary>
        public static readonly Color Bacon = Girdap.Hex(0xB8463E);

        public static readonly Color BaconStripe = Girdap.Hex(0xE58A7A);

        /// <summary>Sesame dots on the top bun slab.</summary>
        public static readonly Color Sesame = Girdap.Hex(0xFFF1C2);

        // ------------------------------------------------------------ metrics

        /// <summary>Default Ink outline width of a card/pill.</summary>
        public const float Outline = 4f;

        /// <summary>Default drop-shadow offset (down) of a card/pill.</summary>
        public const float Shadow = 6f;

        public const float CardRadius = 22f;

        // ---------------------------------------------------- patience thresholds
        // Shared by the bubble's mood face and its patience bar colour so the two never disagree.

        public const float PatienceWarn = 0.5f;
        public const float PatienceBad = 0.2f;
    }
}
