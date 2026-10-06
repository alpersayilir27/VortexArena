using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using VortexArena.App.Admin;
using VortexArena.Core.UI;

// Factory methods share element names (Image / Button). Aliases are required because
// `Image x = Image(...)` gives CS0119 — simple name lookup finds the member group BEFORE the type.
using UiImage = UnityEngine.UI.Image;
using UiButton = UnityEngine.UI.Button;
using UiSelectable = UnityEngine.UI.Selectable;

namespace VortexArena.App.Editor
{
    /// <summary>
    /// Side-column player card (CSS <c>.pcard</c>) — <c>Resources/UI/AdminPlayerRow.prefab</c>.
    /// <para>⚠️ Only the card's HEIGHT is fixed; the width comes from the column at runtime
    /// (<see cref="AdminPlayerRow.Place"/> stretches it), so every part is either edge-anchored or
    /// measured in <c>Bind</c>. A hard-coded x would drift the moment the column resizes.</para>
    /// </summary>
    public static partial class GirdapUiBuilder
    {
        private const string RowPrefabName = "AdminPlayerRow";

        /// <summary>CSS <c>.col</c> width — the authoring width only; see the class note.</summary>
        private const float RowWidth = 400f;

        /// <summary>CSS <c>.pcard --c</c> / <c>padding: 1px</c> (the border ring).</summary>
        private const float RowChamfer = 12f;

        private const float RowBorder = 1f;

        /// <summary>CSS <c>.pcard</c> grid first column, and the slant of its clip-path.</summary>
        private const float RowNumWidth = 44f;

        /// <inheritdoc cref="RowNumWidth"/>
        private const float RowNumSlant = 10f;

        /// <summary>CSS <c>.pcard .body</c> side padding.</summary>
        private const float RowPadX = 12f;

        // CSS `.pcard .body` is a 6 px-gap flex column; the kit forbids layout groups, so the stops
        // are precomputed from the card top (border 1 px + padding-top 9 px).
        private const float RowTopY = 9f;
        private const float RowTopH = 28f;

        /// <summary>Name text box. ⚠️ Taller than the row: TMP Ellipsis also truncates VERTICALLY,
        /// and Saira Condensed's line box at 24 px is ~38 px — in a 28 px rect the name vanishes.
        /// Midline alignment keeps it visually on the row.</summary>
        private const float RowNameH = 40f;
        private const float RowHpY = 43f;
        private const float RowHpH = 20f;
        private const float RowTeleY = 69f;
        private const float RowTeleH = 18f;
        private const float RowActY = 93f;
        private const float RowActH = 32f;

        /// <summary>CSS <c>.pc-hp b</c> fixed column + the <c>.pc-hp</c> gap.</summary>
        private const float RowHpNumWidth = 38f;

        /// <inheritdoc cref="RowHpNumWidth"/>
        private const float RowHpBarX = 46f;

        /// <summary>CSS <c>.hp</c> height, tick, gap and <c>skewX(-24deg)</c>.</summary>
        private const float RowHpBarHeight = 12f;

        /// <inheritdoc cref="RowHpBarHeight"/>
        private const float RowHpSegment = 9f;

        /// <inheritdoc cref="RowHpBarHeight"/>
        private const float RowHpGap = 3f;

        /// <inheritdoc cref="RowHpBarHeight"/>
        private const float RowHpSkew = 24f;

        /// <summary>CSS <c>.ctrl i</c>: 9×14 tick at <c>skewX(-12deg)</c>.</summary>
        private const float RowCtrlWidth = 9f;

        /// <inheritdoc cref="RowCtrlWidth"/>
        private const float RowCtrlHeight = 14f;

        /// <inheritdoc cref="RowCtrlWidth"/>
        private const float RowCtrlSkew = 12f;

        private const float RowTeleIcon = 16f;

        /// <summary>CSS <c>.pc-act</c>: four equal columns, 4 px gap, <c>.btn.sm</c> metrics.</summary>
        private const int RowActColumns = 4;

        /// <inheritdoc cref="RowActColumns"/>
        private const float RowActGap = 4f;

        /// <inheritdoc cref="RowActColumns"/>
        private const float RowActChamfer = 5f;

        /// <inheritdoc cref="RowActColumns"/>
        private const float RowActFont = 14f;

        [MenuItem("Tools/VortexArena/UI/Girdap/Yalnız PlayerRow")]
        public static void BuildPlayerRowOnly()
        {
            BuildAssets();
            BuildPlayerRow();
            AssetDatabase.SaveAssets();
        }

        static partial void BuildPlayerRow()
        {
            GameObject root = LoadPrefabContents(RowPrefabName)
                              ?? NewRoot(RowPrefabName, RowWidth, AdminPlayerRow.Height);

            // The card surface is a UiShape child now (chamfer + gradient + glow); the flat root
            // Image would draw an opaque rectangle over its corners.
            var legacy = root.GetComponent<UiImage>();
            if (legacy != null)
            {
                Object.DestroyImmediate(legacy);
            }

            var rowRect = (RectTransform)root.transform;
            rowRect.sizeDelta = new Vector2(RowWidth, AdminPlayerRow.Height);
            RowClearChildren(rowRect);

            AdminPlayerRow row = root.GetComponent<AdminPlayerRow>();
            if (row == null)
            {
                row = root.AddComponent<AdminPlayerRow>();
            }

            // Whole card selects: the surface absorbs the raycast and the click bubbles up to this
            // Button. Transition None because the root carries no Graphic of its own.
            UiButton select = root.GetComponent<UiButton>();
            if (select == null)
            {
                select = root.AddComponent<UiButton>();
            }

            select.transition = UiSelectable.Transition.None;

            UiShape surface = Shape(rowRect, "Surface");
            Stretch(surface.rectTransform);
            // Outline/fill are re-applied per bind (team wash, selection, alert); this is only the
            // teamless look a freshly instantiated card shows before its first Bind.
            surface.Chamfer(RowChamfer)
                .Outline(RowBorder, Girdap.CardBd)
                .Fill(new Color(Girdap.PanelB.r, Girdap.PanelB.g, Girdap.PanelB.b, 0.95f));
            surface.raycastTarget = true;

            RectTransform content = Node(rowRect, "Content");
            Stretch(content, RowBorder, RowBorder, RowBorder, RowBorder);
            var dim = content.gameObject.AddComponent<CanvasGroup>();

            UiShape plate = RowNumberPlate(content, out TextMeshProUGUI number);

            RectTransform body = Node(content, "Body");
            Stretch(body, RowNumWidth);

            RectTransform top = Node(body, "Top");
            StretchTop(top, RowPadX, RowTopY, RowPadX, RowTopH);
            RowTopRow(top, out TextMeshProUGUI playerName, out TextMeshProUGUI id, out UiChip chip);

            RectTransform hp = Node(body, "Hp");
            StretchTop(hp, RowPadX, RowHpY, RowPadX, RowHpH);
            RowHpRow(hp, out TextMeshProUGUI hpValue, out UiSegmentBar bar);

            RectTransform tele = Node(body, "Tele");
            StretchTop(tele, RowPadX, RowTeleY, RowPadX, RowTeleH);
            RowTeleRow(tele, out TextMeshProUGUI kdCaption, out TextMeshProUGUI kdValue,
                out UiImage battery, out TextMeshProUGUI batteryValue, out UiSegmentBar ctrlLeft,
                out UiSegmentBar ctrlRight, out UiImage bodyState);

            RectTransform act = Node(body, "Act");
            StretchTop(act, RowPadX, RowActY, RowPadX, RowActH);
            UiButtonStyle pov = RowAction(act, "Pov", "POV", UiButtonKind.Normal, 0);
            UiButtonStyle measure = RowAction(act, "Measure", "ÖLÇ", UiButtonKind.Normal, 1);
            UiButtonStyle team = RowAction(act, "Team", "MAVİ", UiButtonKind.TextBlue, 2);
            UiButtonStyle kick = RowAction(act, "Kick", "AT", UiButtonKind.TextBad, 3);

            row.EditorWire(surface, select, dim, plate, number, top, playerName, id, chip,
                hpValue, bar, tele, kdCaption, kdValue, battery, batteryValue, ctrlLeft,
                ctrlRight, bodyState, pov, measure, team, kick);

            SaveAndUnload(root, RowPrefabName);
        }

        private static void RowClearChildren(RectTransform parent)
        {
            var doomed = new List<GameObject>(parent.childCount);
            for (int i = 0; i < parent.childCount; i++)
            {
                doomed.Add(parent.GetChild(i).gameObject);
            }

            foreach (GameObject go in doomed)
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>Jersey number plate (CSS <c>.pcard .num</c>): team gradient, bottom-right corner
        /// cut, number pinned to the top.</summary>
        private static UiShape RowNumberPlate(RectTransform parent, out TextMeshProUGUI number)
        {
            UiShape plate = Shape(parent, "Num");
            StretchLeft(plate.rectTransform, 0f, 0f, RowNumWidth, 0f);
            // Chamfer is one px short of the card's: the plate sits inside the border ring.
            plate.Chamfer(RowChamfer - RowBorder, 0f, 0f, 0f)
                .Slant(0f, RowNumSlant)
                .Fill(Girdap.TeamHi(""), Girdap.TeamLo(""), UiGradientMode.Vertical);

            // CSS `place-items: start center` with padding-top 11, padding-right 8.
            number = Text(plate.transform, "Label", "", GirdapFont.ChakraBold, 23f, Color.white,
                TextAlignmentOptions.Top);
            Place(number.rectTransform, 0f, 11f, RowNumWidth - 8f, 26f);
            return plate;
        }

        private static void RowTopRow(RectTransform parent, out TextMeshProUGUI playerName,
            out TextMeshProUGUI id, out UiChip chip)
        {
            playerName = Text(parent, "Name", "", GirdapFont.SairaBold, 24f, Girdap.Text,
                TextAlignmentOptions.MidlineLeft);
            PlaceMiddleLeft(playerName.rectTransform, 0f, 160f, RowNameH);
            // CSS `text-overflow: ellipsis`; the width itself is measured in Bind.
            playerName.overflowMode = TextOverflowModes.Ellipsis;

            id = Text(parent, "Id", "", GirdapFont.ChakraSemiBold, 13f, Girdap.Faint,
                TextAlignmentOptions.MidlineLeft);
            PlaceMiddleLeft(id.rectTransform, 168f, 40f, RowTopH);

            chip = Chip(parent, "Chip", "HAZIR", UiChipKind.Good);
            // CSS `margin-left: auto` — right pivot so UiChip.Set grows the badge leftwards.
            RectTransform cr = chip.Root;
            cr.anchorMin = new Vector2(1f, 0.5f);
            cr.anchorMax = new Vector2(1f, 0.5f);
            cr.pivot = new Vector2(1f, 0.5f);
            cr.anchoredPosition = Vector2.zero;
        }

        private static void RowHpRow(RectTransform parent, out TextMeshProUGUI hpValue,
            out UiSegmentBar bar)
        {
            hpValue = Text(parent, "Value", "100", GirdapFont.ChakraBold, 20f, Girdap.Text,
                TextAlignmentOptions.MidlineLeft);
            PlaceMiddleLeft(hpValue.rectTransform, 0f, RowHpNumWidth, RowHpH);

            bar = SegmentBar(parent, "Bar");
            RectTransform br = bar.rectTransform;
            br.anchorMin = new Vector2(0f, 0.5f);
            br.anchorMax = new Vector2(1f, 0.5f);
            br.pivot = new Vector2(0.5f, 0.5f);
            br.offsetMin = new Vector2(RowHpBarX, -RowHpBarHeight * 0.5f);
            br.offsetMax = new Vector2(0f, RowHpBarHeight * 0.5f);
            bar.SetMetrics(RowHpSegment, RowHpGap, RowHpSkew);
            bar.SetTrack(Girdap.Track);
            bar.SetFillColors(Girdap.GoA, Girdap.GoB);
        }

        /// <summary>K/D · battery · controller ticks · body icon. Every part is middle-left anchored;
        /// <c>Bind</c> walks them left to right because an unknown token shrinks.</summary>
        private static void RowTeleRow(RectTransform parent, out TextMeshProUGUI kdCaption,
            out TextMeshProUGUI kdValue, out UiImage battery, out TextMeshProUGUI batteryValue,
            out UiSegmentBar ctrlLeft, out UiSegmentBar ctrlRight, out UiImage bodyState)
        {
            kdCaption = RowTeleText(parent, "KdLabel", "K/D", Girdap.Faint);
            kdValue = RowTeleText(parent, "KdValue", "0/0", Girdap.Muted);

            battery = Icon(parent, "BatteryIcon", "Battery", RowTeleIcon, Girdap.Muted);
            PlaceMiddleLeft(battery.rectTransform, 0f, RowTeleIcon, RowTeleIcon);
            batteryValue = RowTeleText(parent, "BatteryValue", "%100", Girdap.Muted);

            ctrlLeft = RowCtrlTick(parent, "CtrlLeft");
            ctrlRight = RowCtrlTick(parent, "CtrlRight");

            bodyState = Icon(parent, "BodyIcon", "Body", RowTeleIcon, Girdap.Muted);
            PlaceMiddleLeft(bodyState.rectTransform, 0f, RowTeleIcon, RowTeleIcon);
        }

        private static TextMeshProUGUI RowTeleText(RectTransform parent, string name, string text,
            Color color)
        {
            TextMeshProUGUI tmp = Text(parent, name, text, GirdapFont.SairaSemiBold, 16f, color,
                TextAlignmentOptions.MidlineLeft);
            PlaceMiddleLeft(tmp.rectTransform, 0f, 40f, RowTeleH);
            return tmp;
        }

        /// <summary>One controller tick. A <see cref="UiSegmentBar"/> with a single segment is the
        /// only kit part that can SKEW a quad — <c>UiShape.Slant</c> makes a trapezoid, not CSS's
        /// parallelogram.</summary>
        private static UiSegmentBar RowCtrlTick(RectTransform parent, string name)
        {
            UiSegmentBar tick = SegmentBar(parent, name);
            PlaceMiddleLeft(tick.rectTransform, 0f, RowCtrlWidth, RowCtrlHeight);
            tick.SetMetrics(RowCtrlWidth, 0f, RowCtrlSkew);
            tick.SetTrack(Color.clear);
            tick.SetFillColors(Girdap.Muted, Girdap.Muted);
            return tick;
        }

        /// <summary>One of the four <c>.btn.sm</c> actions, anchored to its grid column so the row
        /// follows the card's runtime width.</summary>
        private static UiButtonStyle RowAction(RectTransform parent, string name, string label,
            UiButtonKind kind, int column)
        {
            UiButtonStyle style = Button(parent, name, label, kind, 0f, 0f, 80f, RowActH,
                fontSize: RowActFont, chamfer: RowActChamfer);

            // Equal columns with a 4 px gap, exact at any width: anchoring column i at
            // i/4..(i+1)/4 needs +i px on the left and i - (gap × 3 / 4) px on the right.
            float trim = RowActGap * (RowActColumns - 1) / RowActColumns;
            var rt = (RectTransform)style.transform;
            rt.anchorMin = new Vector2(column / (float)RowActColumns, 0.5f);
            rt.anchorMax = new Vector2((column + 1) / (float)RowActColumns, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(column * RowActGap - column * trim, -RowActH * 0.5f);
            rt.offsetMax = new Vector2(column * RowActGap - (column + 1) * trim, RowActH * 0.5f);
            return style;
        }
    }
}
