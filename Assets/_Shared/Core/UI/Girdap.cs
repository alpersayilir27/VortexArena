using TMPro;
using UnityEngine;

namespace VortexArena.Core.UI
{
    /// <summary>
    /// "Girdap" theme constants — the ONE place <c>plan/arayuz-yenileme/tema.css</c>'s
    /// <c>:root</c> tokens live in code.
    /// <para>
    /// ⚠️ <b>Do not hand-pick a color at a call site.</b> The mockups are the single source of
    /// truth; a literal copied into a screen silently drifts when a token changes and the admin UI
    /// ends up with two slightly different "reds".
    /// </para>
    /// </summary>
    public static class Girdap
    {
        // ---------------------------------------------------------- surfaces

        public static readonly Color Ink = Hex(0x070B16);
        public static readonly Color Panel = Hex(0x0E1526);
        public static readonly Color Raised = Hex(0x151F36);
        public static readonly Color Control = Hex(0x1B2845);
        public static readonly Color ControlHi = Hex(0x273961);
        public static readonly Color Line = Hex(0x2A3A5C);
        public static readonly Color Track = Hex(0x18233D);
        public static readonly Color Plate = Rgba(8, 12, 24, 0.92f);

        // ------------------------------------------------------------- text

        public static readonly Color Text = Hex(0xEAF2FF);
        public static readonly Color Muted = Hex(0xA6B6D4);
        public static readonly Color Faint = Hex(0x7385AB);

        // ------------------------------------------------------------- teams

        /// <summary>⚠️ Flat team red — identical to <c>UiKit.TeamRed</c> and the avatar material.
        /// A player shown in two different reds reads as two different players.</summary>
        public static readonly Color Red = Hex(0xD93333);

        public static readonly Color RedHi = Hex(0xF4544B);
        public static readonly Color RedLo = Hex(0xB32428);
        public static readonly Color RedInk = Hex(0xFF857C);
        public static readonly Color RedDim = Hex(0x55161D);

        /// <summary>⚠️ Flat team blue — identical to <c>UiKit.TeamBlue</c> and the avatar material.</summary>
        public static readonly Color Blue = Hex(0x3366E6);

        public static readonly Color BlueHi = Hex(0x5C8CFF);
        public static readonly Color BlueLo = Hex(0x2147B4);
        public static readonly Color BlueInk = Hex(0x8CB0FF);
        public static readonly Color BlueDim = Hex(0x162C66);

        public static readonly Color NeutralHi = Hex(0x8A97AD);
        public static readonly Color NeutralLo = Hex(0x4B566B);

        // ------------------------------------------------------------ status

        public static readonly Color Good = Hex(0x3EE6A0);
        public static readonly Color Warn = Hex(0xFFC53D);
        public static readonly Color Bad = Hex(0xFF5747);

        /// <summary>CSS <c>.hp i</c> gradient tops (bottom = Good/Warn/Bad).</summary>
        public static readonly Color HpA = Hex(0x86FBCB);

        public static readonly Color HpWarnA = Hex(0xFFE08A);
        public static readonly Color HpBadA = Hex(0xFF9383);

        /// <summary>CSS <c>.ctrl i.unk</c> — controller state not reported.</summary>
        public static readonly Color CtrlUnknown = Hex(0x47515E);

        /// <summary>CSS <c>.pcard --bd</c> — resting card border.</summary>
        public static readonly Color CardBd = Rgba(170, 205, 255, 0.22f);

        // --------------------------------------------------------- hud edges

        /// <summary>CSS rgba(170,205,255,.22) — <c>.vf</c> / <c>.col-more</c> edge.</summary>
        public static readonly Color EdgeSoft = Rgba(170, 205, 255, 0.22f);

        /// <summary>CSS rgba(170,205,255,.30) — <c>.mbar</c> edge.</summary>
        public static readonly Color EdgeBar = Rgba(170, 205, 255, 0.30f);

        /// <summary>CSS <c>.mbar .sep</c> mid color.</summary>
        public static readonly Color SepLine = Hex(0x4A5F8C);

        /// <summary>CSS <c>.sb-chip</c> fill end (start = ClockA).</summary>
        public static readonly Color ChipBgB = Hex(0x101930);

        /// <summary>CSS rgba(8,12,24,.88) — <c>.col-head</c> gradient end (start = PlateB).</summary>
        public static readonly Color PlateFade = Rgba(8, 12, 24, 0.88f);

        /// <summary>CSS rgba(255,255,255,.075) — highlight stripes on team plates.</summary>
        public static readonly Color StripeInk = Rgba(255, 255, 255, 0.075f);

        /// <summary>CSS rgba(0,0,0,.30) — hazard stripes on a live violation row.</summary>
        public static readonly Color StripeHazard = Rgba(0, 0, 0, 0.30f);

        // ------------------------------------------------------------- table

        /// <summary>CSS table row wash (top → bottom) and the hairline between rows.</summary>
        public static readonly Color RowA = Rgba(170, 205, 255, 0.085f);

        public static readonly Color RowB = Rgba(170, 205, 255, 0.03f);
        public static readonly Color RowLine = Rgba(170, 205, 255, 0.07f);

        // ------------------------------------------------------------ accent

        public static readonly Color Acc = Hex(0x7DE3FF);
        public static readonly Color AccHi = Hex(0xC9F4FF);
        public static readonly Color Glow = Rgba(125, 227, 255, 0.45f);

        // ------------------------------------------------ button border/fill pairs

        public static readonly Color BtnBdA = Rgba(170, 205, 255, 0.42f);
        public static readonly Color BtnBdB = Rgba(170, 205, 255, 0.12f);
        public static readonly Color BtnBgA = Hex(0x24345C);
        public static readonly Color BtnBgB = Hex(0x161F3A);
        public static readonly Color BtnHoverA = Hex(0x2F4274);
        public static readonly Color BtnHoverB = Hex(0x1C2A4E);

        public static readonly Color PlateA = Rgba(22, 32, 60, 0.97f);
        public static readonly Color PlateB = Rgba(8, 12, 24, 0.97f);

        public static readonly Color OnBd = Hex(0xE4F9FF);
        public static readonly Color OnA = Hex(0xC9F4FF);
        public static readonly Color OnB = Hex(0x6FD8F7);
        public static readonly Color OnFg = Hex(0x06202C);

        public static readonly Color GoBd = Hex(0xC4FFE6);
        public static readonly Color GoA = Hex(0x62F2B9);
        public static readonly Color GoB = Hex(0x1FC585);
        public static readonly Color GoFg = Hex(0x052417);

        public static readonly Color DangerBd = Hex(0xFF5747);

        /// <summary>rgba(255,87,71,.20) composited over #0A0F1F — flattened because a UiShape fill is
        /// one opaque layer, not two stacked backgrounds.</summary>
        public static readonly Color DangerA = Hex(0x3B1D27);

        /// <summary>rgba(255,87,71,.06) composited over #0A0F1F.</summary>
        public static readonly Color DangerB = Hex(0x191321);

        public static readonly Color DangerFg = Hex(0xFF5747);

        public static readonly Color ConfirmBd = Hex(0xFFC2B8);
        public static readonly Color ConfirmA = Hex(0xFF7A68);
        public static readonly Color ConfirmB = Hex(0xE33A2A);
        public static readonly Color ConfirmFg = Hex(0x260502);

        public static readonly Color WarnBd = Hex(0xFFEDB5);
        public static readonly Color WarnA = Hex(0xFFD866);
        public static readonly Color WarnB = Hex(0xF0AA18);
        public static readonly Color WarnFg = Hex(0x2A1C00);

        public static readonly Color OffBd = Rgba(170, 205, 255, 0.10f);
        public static readonly Color OffBg = Hex(0x0D1427);
        public static readonly Color OffFg = Hex(0x4A5878);

        public static readonly Color HoldBg = Hex(0xE33A2A);
        public static readonly Color HoldTrack = Hex(0x0A0F1F);

        public static readonly Color SegBd = Rgba(170, 205, 255, 0.26f);
        public static readonly Color SegBg = Rgba(8, 12, 24, 0.95f);
        public static readonly Color SegInBg = Hex(0x080D1A);
        public static readonly Color SegHover = Rgba(125, 227, 255, 0.10f);

        /// <summary>CSS <c>.tab</c> idle wash (panel tab bar).</summary>
        public static readonly Color TabBg = Rgba(170, 205, 255, 0.08f);

        /// <summary>CSS <c>.select</c> fill gradient (dropdown face and its open list).</summary>
        public static readonly Color SelectBgA = Hex(0x0A1122);

        public static readonly Color SelectBgB = Hex(0x0E1730);
        public static readonly Color SelectBd = Rgba(170, 205, 255, 0.32f);

        // --------------------------------------------------- kill feed / scorebug

        public static readonly Color DeadRedA = Hex(0x6C1D25);
        public static readonly Color DeadRedB = Hex(0x3D1016);
        public static readonly Color DeadRedFg = Hex(0xFFCFCA);
        public static readonly Color DeadBlueA = Hex(0x1C3679);
        public static readonly Color DeadBlueB = Hex(0x0F1E48);
        public static readonly Color DeadBlueFg = Hex(0xCDDCFF);
        public static readonly Color ChipDead = Hex(0x03050B);

        public static readonly Color KfMidA = Hex(0x151F38);
        public static readonly Color KfMidB = Hex(0x090F1F);
        public static readonly Color ScoreA = Hex(0x17223E);
        public static readonly Color ScoreB = Hex(0x090F1F);
        public static readonly Color ClockA = Hex(0x1C2A4C);
        public static readonly Color ClockB = Hex(0x0C1326);

        // ------------------------------------------------------------- panel

        public static readonly Color PanelA = Hex(0x121B33);
        public static readonly Color PanelB = Hex(0x090F1F);
        public static readonly Color PanelBdA = Rgba(125, 227, 255, 0.60f);
        public static readonly Color PanelBdB = Rgba(170, 205, 255, 0.16f);

        // ------------------------------------------------------------ helpers

        public static Color Hex(int rgb)
        {
            return new Color32((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF),
                (byte)(rgb & 0xFF), 0xFF);
        }

        public static Color Hex(int rgb, float a)
        {
            Color c = Hex(rgb);
            c.a = a;
            return c;
        }

        public static Color Rgba(int r, int g, int b, float a)
        {
            return new Color(r / 255f, g / 255f, b / 255f, a);
        }

        /// <summary>Team gradient top color ("red"/"blue"/other → neutral gray).</summary>
        public static Color TeamHi(string team)
        {
            return team == "red" ? RedHi : team == "blue" ? BlueHi : NeutralHi;
        }

        /// <summary>Team gradient bottom color.</summary>
        public static Color TeamLo(string team)
        {
            return team == "red" ? RedLo : team == "blue" ? BlueLo : NeutralLo;
        }

        /// <summary>Team text color on a dark surface.</summary>
        public static Color TeamInk(string team)
        {
            return team == "red" ? RedInk : team == "blue" ? BlueInk : Muted;
        }

        /// <summary>CSS <c>letter-spacing</c> in em → TMP <c>characterSpacing</c> (.12em → 12).</summary>
        public static float Spacing(float em)
        {
            return em * 100f;
        }

        private static readonly System.Globalization.CultureInfo Turkish =
            new System.Globalization.CultureInfo("tr-TR");

        /// <summary>Turkish-aware uppercase for dynamic text (CSS <c>text-transform: uppercase</c>).
        /// ⚠️ TMP's UpperCase style and <c>ToUpperInvariant</c> map i → I; player names need İ.</summary>
        public static string Upper(string s)
        {
            return string.IsNullOrEmpty(s) ? "" : s.ToUpper(Turkish);
        }

        // ------------------------------------------------------------- assets

        private static GirdapAssets assets;
        private static bool assetsMissingLogged;

        /// <summary>
        /// Lazily loaded asset container. Null (with a one-shot error) when the container has not
        /// been generated yet — the UI then renders without fonts/icons instead of throwing.
        /// </summary>
        public static GirdapAssets Assets
        {
            get
            {
                if (assets != null)
                {
                    return assets;
                }

                assets = Resources.Load<GirdapAssets>("UI/Girdap");
                if (assets == null && !assetsMissingLogged)
                {
                    assetsMissingLogged = true;
                    Debug.LogError("[Girdap] UI/Girdap.asset yok — Tools > VortexArena > UI > Girdap > Fontları ve asset kabını üret");
                }

                return assets;
            }
        }

        public static TMP_FontAsset Font(GirdapFont f)
        {
            GirdapAssets a = Assets;
            return a != null ? a.Font(f) : null;
        }

        public static Sprite Icon(string name)
        {
            GirdapAssets a = Assets;
            return a != null ? a.Icon(name) : null;
        }
    }
}
