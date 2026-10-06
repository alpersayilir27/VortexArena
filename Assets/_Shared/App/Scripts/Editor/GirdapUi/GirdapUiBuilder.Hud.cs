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

namespace VortexArena.App.Editor
{
    /// <summary>
    /// Admin HUD (<c>plan/arayuz-yenileme/index.html</c>): vignette, scorebug, team columns, the two
    /// feeds and the match bar.
    /// <para>⚠️ <b>Edited IN PLACE:</b> <c>AdminHud.prefab</c> nests the stats and preferences panel
    /// prefabs; saving a fresh root over it would drop those instances.</para>
    /// </summary>
    public static partial class GirdapUiBuilder
    {
        // ---- CSS `.hud` geometry ----
        private const float HudEdge = 24f;        // `.hud .tl/.tr` inset and `.mbar { bottom }`
        private const float HudColumnTop = 136f;  // `.col { top }`
        private const float HudColumnWidth = 400f;
        private const float HudHeadHeight = 38f;  // `.col-head`
        private const float HudRowGap = 8f;       // `.col { gap }`
        private const int HudMaxRows = 5;

        // ---- `.scorebug` ----
        private const float SbHeight = 76f;
        private const float SbTeamWidth = 170f;
        private const float SbScoreWidth = 104f;
        private const float SbClockWidth = 208f;
        private const float SbSlant = 26f;

        // ---- `.mbar` ----
        private const float BarHeight = 74f;      // 9 px padding + 56 px button + 9 px
        private const float BarPad = 9f;
        private const float BarGap = 8f;
        private const float BarButtonHeight = 56f;
        private const float BarButtonMinWidth = 152f;
        private const float BarIcon = 22f;        // `.mbar .btn .ic`
        private const float BarSepSlot = 13f;     // 6 px margin + 1 px line + 6 px margin
        private const float SegGap = 4f;          // `.seg { gap }`
        private const float SegPad = 5f;          // `.seg { padding }`
        private const float SegItemHeight = 40f;  // `.seg .btn`

        /// <summary>Both feeds start level with the columns and stop 16 px short of them.</summary>
        private const float FeedInset = HudEdge + HudColumnWidth + 16f;

        [MenuItem("Tools/VortexArena/UI/Girdap/Yalnız Hud")]
        public static void BuildHudOnly()
        {
            BuildAssets();
            BuildHud();
            AssetDatabase.SaveAssets();
        }

        static partial void BuildHud()
        {
            GameObject root = LoadPrefabContents("AdminHud");
            if (root == null)
            {
                Debug.LogError($"[Girdap] {PrefabDir}AdminHud.prefab yok — HUD yerinde düzenlenir, " +
                               "sıfırdan üretilmez (iç içe paneller kaybolur).");
                return;
            }

            var hud = root.GetComponent<AdminHud>();
            if (hud == null)
            {
                Debug.LogError("[Girdap] AdminHud.prefab kökünde AdminHud bileşeni yok.");
                PrefabUtility.UnloadPrefabContents(root);
                return;
            }

            var floors = root.GetComponent<AdminFloorControls>();
            if (floors == null)
            {
                // On the ROOT, because AdminHud's runtime fallback probes GetComponent there — a copy
                // on the bar would make it add a second, unwired one.
                floors = root.AddComponent<AdminFloorControls>();
            }

            Stretch((RectTransform)root.transform);
            HudClearChildren(root.transform);

            var hudSo = new SerializedObject(hud);
            Transform parent = root.transform;

            HudVignette(parent);
            HudPreferencesButton(parent, hudSo);
            HudScorebug(parent, hudSo);
            HudStatsChip(parent, hudSo);
            HudOptionalLines(parent, hudSo);
            HudCameraSeg(parent, hudSo);
            HudColumn(parent, hudSo, "red", "KIRMIZI", false);
            HudColumn(parent, hudSo, "blue", "MAVİ", true);
            HudKillFeed(parent, hudSo);
            HudViolationFeed(parent, hudSo);
            HudMatchBar(parent, floors);

            HudSet(hudSo, "rowPrefab", HudLoadComponent<AdminPlayerRow>("AdminPlayerRow"));
            HudSetInt(hudSo, "maxRowsPerColumn", HudMaxRows);
            HudSetFloat(hudSo, "rowGap", HudRowGap);
            HudSetFloat(hudSo, "headerHeight", HudHeadHeight);
            hudSo.ApplyModifiedPropertiesWithoutUndo();

            // Last, so the full-screen panels draw OVER the HUD.
            HudNestPanel(parent, "AdminStatsPanel");
            AdminPreferencesPanel prefs = HudNestPanel(parent, "AdminPreferencesPanel")
                ?.GetComponent<AdminPreferencesPanel>();

            var controls = root.GetComponentInChildren<AdminMatchControls>(true);
            if (controls != null && prefs != null)
            {
                var controlsSo = new SerializedObject(controls);
                HudSet(controlsSo, "preferences", prefs);
                controlsSo.ApplyModifiedPropertiesWithoutUndo();
            }

            SaveAndUnload(root, "AdminHud");
        }

        private static void HudClearChildren(Transform parent)
        {
            var children = new List<Transform>(parent.childCount);
            for (int i = 0; i < parent.childCount; i++)
            {
                children.Add(parent.GetChild(i));
            }

            for (int i = 0; i < children.Count; i++)
            {
                Object.DestroyImmediate(children[i].gameObject);
            }
        }

        // ------------------------------------------------------------------- vignette

        /// <summary>
        /// CSS <c>.hud .arena::after</c>: four edge gradients so the HUD parts separate from the live
        /// arena behind the canvas. ⚠️ No sprite — <c>Fade*</c> belongs to the old kit.
        /// </summary>
        private static void HudVignette(Transform parent)
        {
            Color edge = Girdap.Rgba(4, 7, 16, 0.82f);          // CSS 180deg layer
            Color side = Girdap.Rgba(4, 7, 16, 0.62f);          // CSS 90deg layer
            Color fade = Girdap.Rgba(4, 7, 16, 0f);

            UiShape top = Shape(parent, "VignetteTop");
            StretchTop(top.rectTransform, 0f, 0f, 0f, 173f);    // transparent at 16%
            top.Chamfer(0f).Fill(edge, fade, UiGradientMode.Vertical).Antialias(false);

            UiShape bottom = Shape(parent, "VignetteBottom");
            StretchBottom(bottom.rectTransform, 0f, 0f, 0f, 194f); // opaque again from 82%
            bottom.Chamfer(0f).Fill(fade, edge, UiGradientMode.Vertical).Antialias(false);

            UiShape left = Shape(parent, "VignetteLeft");
            StretchLeft(left.rectTransform, 0f, 0f, 480f, 0f);  // transparent at 25%
            left.Chamfer(0f).Fill(side, fade, UiGradientMode.Horizontal).Antialias(false);

            UiShape right = Shape(parent, "VignetteRight");
            HudStretchRight(right.rectTransform, 480f);
            right.Chamfer(0f).Fill(fade, side, UiGradientMode.Horizontal).Antialias(false);
        }

        /// <summary>Full height, fixed width, pinned to the right edge (no sibling helper exists).</summary>
        private static void HudStretchRight(RectTransform rt, float w)
        {
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.offsetMin = new Vector2(-w, 0f);
            rt.offsetMax = Vector2.zero;
        }

        // ------------------------------------------------------------- top left / right

        private static void HudPreferencesButton(Transform parent, SerializedObject hud)
        {
            float width = HudButtonWidth("TERCİHLER", GirdapFont.SairaBold, 20f, 0.07f, 16f, 48f,
                true, "P");
            UiButtonStyle style = Button(parent, "PreferencesButton", "TERCİHLER",
                UiButtonKind.Plate, HudEdge, HudEdge, width, 48f, icon: "Sliders", kbd: "P");
            HudSet(hud, "preferencesButton", style.TargetButton);
        }

        private static void HudCameraSeg(Transform parent, SerializedObject hud)
        {
            float freeWidth = HudButtonWidth("SERBEST", GirdapFont.SairaBold, 20f, 0.07f, 16f,
                SegItemHeight, false, "2");
            float topWidth = HudButtonWidth("KUŞ BAKIŞI", GirdapFont.SairaBold, 20f, 0.07f, 16f,
                SegItemHeight, false, "3");
            float total = SegPad * 2f + freeWidth + SegGap + topWidth;

            RectTransform seg = Node(parent, "CameraSeg");
            PlaceRight(seg, HudEdge, HudEdge, total, SegPad * 2f + SegItemHeight);

            UiShape bg = Shape(seg, "Bg");
            Stretch(bg.rectTransform);
            bg.Chamfer(8f).Outline(1f, Girdap.SegBd).Fill(Girdap.SegBg);

            UiButtonStyle free = Button(seg, "ModeFree", "SERBEST", UiButtonKind.Seg, SegPad,
                SegPad, freeWidth, SegItemHeight, kbd: "2");
            UiButtonStyle topDown = Button(seg, "ModeTopDown", "KUŞ BAKIŞI", UiButtonKind.SegOn,
                SegPad + freeWidth + SegGap, SegPad, topWidth, SegItemHeight, kbd: "3");

            // ⚠️ Enum-indexed: slot 0 is POV and stays EMPTY (entered from a card or the keyboard).
            // Shifting the array would bind SERBEST/KUŞ BAKIŞI to the wrong mode.
            SerializedProperty array = hud.FindProperty("modeButtons");
            array.arraySize = 3;
            array.GetArrayElementAtIndex(0).objectReferenceValue = null;
            array.GetArrayElementAtIndex(1).objectReferenceValue = free;
            array.GetArrayElementAtIndex(2).objectReferenceValue = topDown;
        }

        // ---------------------------------------------------------------- scorebug

        private static void HudScorebug(Transform parent, SerializedObject hud)
        {
            float total = SbTeamWidth * 2f + SbScoreWidth * 2f + SbClockWidth;
            RectTransform bug = Node(parent, "Scorebug");
            PlaceTopCenter(bug, HudEdge, total, SbHeight);

            // CSS `filter: drop-shadow(0 8px 22px …)` — one glow behind the whole band instead of per
            // segment, which would show as five overlapping shadows.
            UiShape shadow = Shape(bug, "Shadow");
            Stretch(shadow.rectTransform);
            shadow.Chamfer(0f).Fill(Girdap.Rgba(0, 0, 0, 0f))
                .Glow(22f, Girdap.Rgba(0, 0, 0, 0.6f), new Vector2(0f, -8f)).Antialias(false);

            HudTeamPlate(bug, "TeamRed", 0f, SbSlant, 0f, Girdap.RedHi, Girdap.RedLo, "KIRMIZI",
                16f, 0f);
            TextMeshProUGUI redScore = HudScoreCell(bug, "ScoreRed", SbTeamWidth, Girdap.Red);
            HudClock(bug, SbTeamWidth + SbScoreWidth, hud);
            TextMeshProUGUI blueScore = HudScoreCell(bug,
                "ScoreBlue", SbTeamWidth + SbScoreWidth + SbClockWidth, Girdap.Blue);
            HudTeamPlate(bug, "TeamBlue", SbTeamWidth + SbScoreWidth * 2f + SbClockWidth, 0f,
                SbSlant, Girdap.BlueHi, Girdap.BlueLo, "MAVİ", 0f, 16f);

            HudSet(hud, "scoreRedText", redScore);
            HudSet(hud, "scoreBlueText", blueScore);
        }

        /// <summary>CSS <c>.sb-team</c>: team gradient + diagonal highlight stripes + slanted corner.</summary>
        private static void HudTeamPlate(Transform parent, string name, float x, float slantLeft,
            float slantRight, Color hi, Color lo, string label, float padLeft, float padRight)
        {
            UiShape plate = Shape(parent, name);
            Place(plate.rectTransform, x, 0f, SbTeamWidth, SbHeight);
            plate.Chamfer(0f).Slant(slantLeft, slantRight)
                .Fill(hi, lo, UiGradientMode.Vertical).Antialias(false);

            UiStripes stripes = Stripes(plate.transform, "Stripes");
            Stretch(stripes.rectTransform);
            // No fluent Slant on UiStripes — the polygon fields live on the shared base.
            stripes.SlantLeft = slantLeft;
            stripes.SlantRight = slantRight;
            // CSS `repeating-linear-gradient(-55deg, StripeInk 0 6px, transparent 6px 17px)`.
            stripes.Stripes(-55f, 6f, 17f, Girdap.StripeInk);

            TextMeshProUGUI text = Text(plate.transform, "Label", label, GirdapFont.ChakraBold, 27f,
                Color.white, TextAlignmentOptions.Center, 0.12f);
            Stretch(text.rectTransform, padLeft, 0f, padRight, 0f);
        }

        /// <summary>CSS <c>.sb-score</c> + its <c>inset 0 -4px 0</c> team underline.</summary>
        private static TextMeshProUGUI HudScoreCell(Transform parent, string name, float x,
            Color underline)
        {
            UiShape cell = Shape(parent, name);
            Place(cell.rectTransform, x, 0f, SbScoreWidth, SbHeight);
            cell.Chamfer(0f).Fill(Girdap.ScoreA, Girdap.ScoreB, UiGradientMode.Vertical)
                .Antialias(false);

            HudEdgeBar(cell.transform, "Underline", true, 4f, underline);

            TextMeshProUGUI text = Text(cell.transform, "Value", "0", GirdapFont.ChakraBold, 56f,
                Girdap.Text, TextAlignmentOptions.Center);
            Stretch(text.rectTransform);
            return text;
        }

        private static void HudClock(Transform parent, float x, SerializedObject hud)
        {
            UiShape cell = Shape(parent, "Clock");
            Place(cell.rectTransform, x, 0f, SbClockWidth, SbHeight);
            cell.Chamfer(0f).Fill(Girdap.ClockA, Girdap.ClockB, UiGradientMode.Vertical)
                .Antialias(false);

            HudEdgeBar(cell.transform, "Topline", false, 3f, Girdap.Acc);

            TextMeshProUGUI text = Text(cell.transform, "Value", "", GirdapFont.ChakraBold, 48f,
                Girdap.AccHi, TextAlignmentOptions.Center, 0.04f);
            Stretch(text.rectTransform);
            HudSet(hud, "clockText", text);
        }

        /// <summary>CSS <c>box-shadow: inset 0 ±Npx 0 C</c> — a flush bar on one edge.</summary>
        private static UiShape HudEdgeBar(Transform parent, string name, bool bottom, float h,
            Color color)
        {
            UiShape bar = Shape(parent, name);
            RectTransform rt = bar.rectTransform;
            rt.anchorMin = new Vector2(0f, bottom ? 0f : 1f);
            rt.anchorMax = new Vector2(1f, bottom ? 0f : 1f);
            rt.pivot = new Vector2(0.5f, bottom ? 0f : 1f);
            rt.offsetMin = new Vector2(0f, bottom ? 0f : -h);
            rt.offsetMax = new Vector2(0f, bottom ? h : 0f);
            bar.Chamfer(0f).Fill(color).Antialias(false);
            return bar;
        }

        /// <summary>CSS <c>.sb-chip</c>: the İSTATİSTİK button under the scorebug.</summary>
        private static void HudStatsChip(Transform parent, SerializedObject hud)
        {
            const float width = 196f;
            const float height = 32f;
            const float icon = 16f;
            const float gap = 8f;

            RectTransform chip = Node(parent, "StatsChip");
            PlaceTopCenter(chip, 100f, width, height);

            UiShape bg = Shape(chip, "Bg");
            Stretch(bg.rectTransform);
            bg.Chamfer(0f).Slant(14f, 14f)
                .Fill(Girdap.ClockA, Girdap.ChipBgB, UiGradientMode.Vertical);
            bg.raycastTarget = true;

            float textWidth = HudTextWidth("İSTATİSTİK", GirdapFont.ChakraSemiBold, 14f, 0.16f);
            float kbdWidth = HudKbdWidth("I");
            float cursor = (width - (icon + gap + textWidth + gap + kbdWidth)) * 0.5f;

            UiImage iconImage = Icon(chip, "Icon", "Chart", icon, Girdap.AccHi);
            PlaceMiddleLeft(iconImage.rectTransform, cursor, icon, icon);
            cursor += icon + gap;

            TextMeshProUGUI label = Text(chip, "Label", "İSTATİSTİK", GirdapFont.ChakraSemiBold,
                14f, Girdap.AccHi, TextAlignmentOptions.MidlineLeft, 0.16f);
            PlaceMiddleLeft(label.rectTransform, cursor, textWidth, height);
            cursor += textWidth + gap;

            RectTransform kbd = Kbd(chip, "Kbd", "I", 0f, 0f);
            PlaceMiddleLeft(kbd, cursor, kbdWidth, 22f);
            HudTintKbd(kbd, Girdap.AccHi);

            var button = chip.gameObject.AddComponent<UiButton>();
            button.targetGraphic = bg;

            HudSet(hud, "statsChipButton", button);
            HudSet(hud, "chipText", label);
        }

        /// <summary>
        /// FFA leaderboard and co-op counters. ⚠️ The mockup has NO slot for them — they are built
        /// inactive under the chip and <see cref="AdminHud"/> only shows them while they carry text,
        /// so a team match looks exactly like the design and FFA/co-op keep their numbers.
        /// </summary>
        private static void HudOptionalLines(Transform parent, SerializedObject hud)
        {
            TextMeshProUGUI leaderboard = Text(parent, "LeaderboardLine", "",
                GirdapFont.SairaBold, 20f, Girdap.Muted, TextAlignmentOptions.Center, 0.05f);
            PlaceTopCenter(leaderboard.rectTransform, 140f, 900f, 26f);
            leaderboard.gameObject.SetActive(false);

            TextMeshProUGUI customers = Text(parent, "CustomerCountsLine", "",
                GirdapFont.SairaBold, 20f, Girdap.Muted, TextAlignmentOptions.Center, 0.05f);
            PlaceTopCenter(customers.rectTransform, 170f, 900f, 26f);
            customers.gameObject.SetActive(false);

            HudSet(hud, "leaderboardText", leaderboard);
            HudSet(hud, "customerCountsText", customers);
        }

        // ----------------------------------------------------------------- columns

        /// <summary>
        /// One team column (CSS <c>.col</c>): header + the runtime row area + the overflow box.
        /// <paramref name="prefix"/> picks the <see cref="AdminHud"/> field family ("red"/"blue").
        /// </summary>
        private static void HudColumn(Transform parent, SerializedObject hud, string prefix,
            string title, bool right)
        {
            bool red = prefix == "red";
            RectTransform column = Node(parent, red ? "RedColumn" : "BlueColumn");
            if (right)
            {
                PlaceRight(column, HudEdge, HudColumnTop, HudColumnWidth, 1080f - HudColumnTop);
            }
            else
            {
                Place(column, HudEdge, HudColumnTop, HudColumnWidth, 1080f - HudColumnTop);
            }

            RectTransform head = Node(column, "Head");
            Place(head, 0f, 0f, HudColumnWidth, HudHeadHeight);

            UiShape headBg = Shape(head, "Bg");
            Stretch(headBg.rectTransform);
            // CSS `linear-gradient(90deg, PlateB, PlateFade)`.
            headBg.Chamfer(0f).Fill(Girdap.PlateB, Girdap.PlateFade, UiGradientMode.Horizontal)
                .Antialias(false);
            HudEdgeBar(headBg.transform, "Underline", true, 2f, red ? Girdap.Red : Girdap.Blue);

            // Width is written at runtime: "OYUNCULAR" (FFA) and "MAVİ" are not the same width.
            UiShape plate = Shape(head, "Plate");
            Place(plate.rectTransform, 0f, 0f, 0f, HudHeadHeight);
            plate.Chamfer(0f).Slant(0f, 14f)
                .Fill(Girdap.TeamHi(prefix), Girdap.TeamLo(prefix), UiGradientMode.Vertical)
                .Antialias(false);

            TextMeshProUGUI plateLabel = Text(plate.transform, "Label", title,
                GirdapFont.ChakraBold, 19f, Color.white, TextAlignmentOptions.MidlineLeft, 0.12f);
            PlaceMiddleLeft(plateLabel.rectTransform, 14f, 0f, HudHeadHeight);

            TextMeshProUGUI count = Text(head, "Count", "", GirdapFont.SairaBold, 17f,
                Girdap.Muted, TextAlignmentOptions.MidlineLeft, 0.05f);
            PlaceMiddleLeft(count.rectTransform, 0f, 200f, HudHeadHeight);

            UiChip calibration = Chip(head, "CalibChip", "KALİBRESİZ", UiChipKind.Warn, "Warn");
            RectTransform chipRect = calibration.Root;
            chipRect.anchorMin = new Vector2(1f, 0.5f);
            chipRect.anchorMax = new Vector2(1f, 0.5f);
            chipRect.pivot = new Vector2(1f, 0.5f);
            chipRect.anchoredPosition = new Vector2(-8f, 0f);
            calibration.gameObject.SetActive(false);

            // CSS `.col-more`. Positioned at runtime: its top depends on how many cards are drawn.
            RectTransform overflow = Node(column, "Overflow");
            Place(overflow, 0f, 0f, HudColumnWidth, 33f);

            UiShape overflowBg = Shape(overflow, "Bg");
            Stretch(overflowBg.rectTransform);
            overflowBg.Chamfer(6f).Outline(1f, Girdap.EdgeSoft).Fill(Girdap.SegBg);

            TextMeshProUGUI overflowLabel = Text(overflow, "Label", "", GirdapFont.SairaSemiBold,
                17f, Girdap.Muted, TextAlignmentOptions.MidlineLeft);
            PlaceMiddleLeft(overflowLabel.rectTransform, 12f, HudColumnWidth - 24f, 33f);
            overflow.gameObject.SetActive(false);

            HudSet(hud, prefix + "Column", column);
            HudSet(hud, prefix + "TeamPlate", plate);
            HudSet(hud, prefix + "TeamLabel", plateLabel);
            HudSet(hud, prefix + "Header", count);
            HudSet(hud, prefix + "CalibChip", calibration);
            HudSet(hud, prefix + "OverflowBox", overflow);
            HudSet(hud, prefix + "Overflow", overflowLabel);
        }

        // ------------------------------------------------------------------- feeds

        /// <summary>CSS <c>.kfeed</c>: right-aligned column of plates, newest on top.</summary>
        private static void HudKillFeed(Transform parent, SerializedObject hud)
        {
            RectTransform feed = Node(parent, "KillFeed");
            PlaceRight(feed, FeedInset, HudColumnTop, 1000f, 500f);

            var view = feed.gameObject.AddComponent<AdminKillFeedView>();
            var viewSo = new SerializedObject(view);
            HudSet(viewSo, "template", HudKillFeedTemplate(feed));
            HudSetInt(viewSo, "maxRows", AdminRoster.KillFeedMaxLines);
            HudSetFloat(viewSo, "rowGap", 6f);
            viewSo.ApplyModifiedPropertiesWithoutUndo();

            HudSet(hud, "killFeed", view);
        }

        private static AdminKillFeedRow HudKillFeedTemplate(Transform parent)
        {
            RectTransform row = Node(parent, "RowTemplate");
            PlaceRight(row, 0f, 0f, 200f, AdminKillFeedRow.Height);
            row.gameObject.AddComponent<CanvasGroup>();

            UiShape killer = HudKfSegment(row, "Killer");
            TextMeshProUGUI killerLabel = HudKfLabel(killer.transform, GirdapFont.SairaBold, 20f,
                Color.white, 0f);

            UiShape weapon = HudKfSegment(row, "Weapon");
            TextMeshProUGUI weaponLabel = HudKfLabel(weapon.transform, GirdapFont.ChakraSemiBold,
                13f, Girdap.AccHi, 0.14f);

            UiShape victim = HudKfSegment(row, "Victim");
            UiImage skull = Icon(victim.transform, "Icon", "Skull", 16f, Girdap.DeadRedFg);
            PlaceMiddleLeft(skull.rectTransform, 0f, 16f, 16f);
            TextMeshProUGUI victimLabel = HudKfLabel(victim.transform, GirdapFont.SairaBold, 20f,
                Girdap.DeadRedFg, 0f);

            UiShape sentence = HudKfSegment(row, "Sentence");
            TextMeshProUGUI sentenceLabel = HudKfLabel(sentence.transform,
                GirdapFont.SairaSemiBold, 18f, Girdap.Text, 0f);

            // CSS `.kf.new::before`: a 3 px accent rule along the plate's bottom edge.
            UiShape underline = Shape(row, "NewLine");
            PlaceBottomLeft(underline.rectTransform, 0f, 0f, 200f, 3f);
            underline.Chamfer(0f).Fill(Girdap.Acc).Antialias(false);

            var component = row.gameObject.AddComponent<AdminKillFeedRow>();
            var so = new SerializedObject(component);
            HudSet(so, "root", row);
            HudSet(so, "fade", row.GetComponent<CanvasGroup>());
            HudSet(so, "killerShape", killer);
            HudSet(so, "killerLabel", killerLabel);
            HudSet(so, "weaponShape", weapon);
            HudSet(so, "weaponLabel", weaponLabel);
            HudSet(so, "victimShape", victim);
            HudSet(so, "victimIcon", skull);
            HudSet(so, "victimLabel", victimLabel);
            HudSet(so, "sentenceShape", sentence);
            HudSet(so, "sentenceLabel", sentenceLabel);
            HudSet(so, "newUnderline", underline);
            so.ApplyModifiedPropertiesWithoutUndo();

            row.gameObject.SetActive(false);
            return component;
        }

        private static UiShape HudKfSegment(Transform parent, string name)
        {
            UiShape shape = Shape(parent, name);
            Place(shape.rectTransform, 0f, 0f, 100f, AdminKillFeedRow.Height);
            shape.Chamfer(0f).Fill(Girdap.KfMidA, Girdap.KfMidB, UiGradientMode.Vertical)
                .Antialias(false);
            return shape;
        }

        private static TextMeshProUGUI HudKfLabel(Transform parent, GirdapFont font, float size,
            Color color, float spacingEm)
        {
            TextMeshProUGUI text = Text(parent, "Label", "", font, size, color,
                TextAlignmentOptions.MidlineLeft, spacingEm);
            PlaceMiddleLeft(text.rectTransform, 0f, 100f, AdminKillFeedRow.Height);
            return text;
        }

        /// <summary>CSS <c>.vfeed</c>: left-aligned column, newest (and only live) row on top.</summary>
        private static void HudViolationFeed(Transform parent, SerializedObject hud)
        {
            RectTransform feed = Node(parent, "ViolationFeed");
            Place(feed, FeedInset, HudColumnTop, 1000f, 500f);

            var view = feed.gameObject.AddComponent<AdminViolationFeedView>();
            var viewSo = new SerializedObject(view);
            HudSet(viewSo, "template", HudViolationTemplate(feed));
            HudSetInt(viewSo, "maxRows", AdminRoster.ViolationFeedMaxLines);
            HudSetFloat(viewSo, "rowGap", 6f);
            viewSo.ApplyModifiedPropertiesWithoutUndo();

            HudSet(hud, "violationFeed", view);
        }

        private static AdminViolationFeedRow HudViolationTemplate(Transform parent)
        {
            RectTransform row = Node(parent, "RowTemplate");
            Place(row, 0f, 0f, 300f, AdminViolationFeedRow.Height);

            UiShape bg = Shape(row, "Bg");
            Stretch(bg.rectTransform);
            bg.Chamfer(7f).Outline(1f, Girdap.EdgeSoft).Fill(Girdap.SegBg);

            // CSS hazard band: 58 px wide, pinned to the right edge of the live row.
            UiStripes stripes = Stripes(bg.transform, "Stripes");
            HudStretchRight(stripes.rectTransform, 58f);
            stripes.Stripes(-45f, 9f, 18f, Girdap.StripeHazard);
            stripes.gameObject.SetActive(false);

            UiImage icon = Icon(row, "Icon", "Warn", 24f, Girdap.ConfirmFg);
            PlaceMiddleLeft(icon.rectTransform, 12f, 24f, 24f);

            TextMeshProUGUI nameLabel = Text(row, "Name", "", GirdapFont.SairaBold, 18f,
                Girdap.Text, TextAlignmentOptions.MidlineLeft);
            PlaceMiddleLeft(nameLabel.rectTransform, 12f, 100f, AdminViolationFeedRow.Height);

            TextMeshProUGUI restLabel = Text(row, "Rest", "", GirdapFont.SairaSemiBold, 18f,
                Girdap.Muted, TextAlignmentOptions.MidlineLeft);
            PlaceMiddleLeft(restLabel.rectTransform, 120f, 200f, AdminViolationFeedRow.Height);

            var component = row.gameObject.AddComponent<AdminViolationFeedRow>();
            var so = new SerializedObject(component);
            HudSet(so, "root", row);
            HudSet(so, "shape", bg);
            HudSet(so, "stripes", stripes);
            HudSet(so, "icon", icon);
            HudSet(so, "nameLabel", nameLabel);
            HudSet(so, "restLabel", restLabel);
            so.ApplyModifiedPropertiesWithoutUndo();

            row.gameObject.SetActive(false);
            return component;
        }

        // ---------------------------------------------------------------- match bar

        private static void HudMatchBar(Transform parent, AdminFloorControls floors)
        {
            float startWidth = HudBarWidth("BAŞLAT", "");
            float pauseWidth = HudBarWidth("DURAKLAT", "DEVAM");
            float endWidth = HudBarWidth("BİTİR", "BİTİR?");
            float abortWidth = HudBarWidth("İPTAL", "");

            float x = BarPad;
            float startX = x;
            x += startWidth + BarGap;
            float pauseX = x;
            x += pauseWidth + BarGap;
            float endX = x;
            x += endWidth + BarGap;
            float sepX = x;
            x += BarSepSlot + BarGap;
            float abortX = x;
            x += abortWidth;
            float baseWidth = x + BarPad;

            RectTransform bar = Node(parent, "MatchBar");
            PlaceBottomCenter(bar, HudEdge, baseWidth, BarHeight);

            UiShape bg = Shape(bar, "Bg");
            Stretch(bg.rectTransform);
            bg.Chamfer(14f).Outline(1f, Girdap.EdgeBar)
                .Fill(Girdap.PlateA, Girdap.PlateB, UiGradientMode.Vertical);

            UiButtonStyle start = HudBarButton(bar, "StartButton", "BAŞLAT", "Play", startX,
                startWidth, UiButtonKind.Go);
            UiButtonStyle pause = HudBarButton(bar, "PauseButton", "DURAKLAT", "Pause", pauseX,
                pauseWidth, UiButtonKind.Normal);
            UiButtonStyle end = HudBarButton(bar, "EndButton", "BİTİR", "Stop", endX, endWidth,
                UiButtonKind.Normal);
            HudSeparator(bar, sepX);
            UiButtonStyle abort = HudBarButton(bar, "AbortButton", "İPTAL", "X", abortX,
                abortWidth, UiButtonKind.Danger);

            var controls = bar.gameObject.AddComponent<AdminMatchControls>();

            TextMeshProUGUI note = Text(parent, "ArmedNote", "BİTİR: onaylamak için tekrar bas.",
                GirdapFont.SairaSemiBold, 17f, Girdap.Warn, TextAlignmentOptions.Center);
            PlaceBottomCenter(note.rectTransform, HudEdge + BarHeight + 8f, 700f, 24f);
            note.gameObject.SetActive(false);

            var so = new SerializedObject(controls);
            HudSet(so, "startButton", start);
            HudSet(so, "pauseButton", pause);
            HudSet(so, "endButton", end);
            HudSet(so, "abortButton", abort);
            HudSet(so, "startIcon", HudIconOf(start));
            HudSet(so, "pauseIcon", HudIconOf(pause));
            HudSet(so, "endIcon", HudIconOf(end));
            HudSet(so, "abortIcon", HudIconOf(abort));
            HudSet(so, "playSprite", LoadIcon("Play"));
            HudSet(so, "pauseSprite", LoadIcon("Pause"));
            HudSet(so, "armedNote", note);
            so.ApplyModifiedPropertiesWithoutUndo();

            HudFloorGroup(bar, floors, baseWidth, abortX + abortWidth);
        }

        /// <summary>
        /// Floor selector group: separator + "KAT" + the selector box. ⚠️ Built but left EMPTY — the
        /// floor count comes from the loaded scene, so <see cref="AdminFloorControls"/> clones the
        /// template and widens the bar at runtime.
        /// </summary>
        private static void HudFloorGroup(RectTransform bar, AdminFloorControls floors,
            float baseWidth, float groupX)
        {
            float labelWidth = 6f + HudTextWidth("KAT", GirdapFont.ChakraSemiBold, 12f, 0.2f) + 4f;
            float segmentX = BarGap + BarSepSlot + BarGap + labelWidth + BarGap;

            RectTransform group = Node(bar, "FloorGroup");
            Place(group, groupX, BarPad, 0f, BarButtonHeight);

            HudSeparator(group, BarGap);

            TextMeshProUGUI label = Text(group, "Label", "KAT", GirdapFont.ChakraSemiBold, 12f,
                Girdap.Faint, TextAlignmentOptions.MidlineLeft, 0.2f);
            PlaceMiddleLeft(label.rectTransform, BarGap + BarSepSlot + BarGap + 6f,
                labelWidth - 10f, BarButtonHeight);

            RectTransform segment = Node(group, "Segment");
            PlaceMiddleLeft(segment, segmentX, 0f, BarButtonHeight);

            // `.mbar .seg .btn` overrides the transparent seg item back to the normal surface.
            UiButtonStyle template = HudBarButton(segment, "FloorTemplate", "Zemin", null, 0f,
                92f, UiButtonKind.Normal, GirdapFont.SairaBold, 20f, 0.04f);
            RectTransform templateRect = (RectTransform)template.transform;
            PlaceMiddleLeft(templateRect, 0f, 92f, BarButtonHeight);
            template.gameObject.SetActive(false);

            group.gameObject.SetActive(false);

            var so = new SerializedObject(floors);
            HudSet(so, "matchBar", bar);
            HudSetFloat(so, "baseWidth", baseWidth);
            HudSet(so, "floorGroup", group);
            HudSet(so, "floorSegment", segment);
            HudSet(so, "buttonTemplate", template);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>CSS <c>.mbar .btn</c>: 56 px tall, 152 px min width, 22 px icon, Chakra 18/.12em.</summary>
        private static UiButtonStyle HudBarButton(Transform parent, string name, string label,
            string icon, float x, float width, UiButtonKind kind,
            GirdapFont font = GirdapFont.ChakraBold, float size = 18f, float spacingEm = 0.12f)
        {
            UiButtonStyle style = Button(parent, name, label, kind, x, BarPad, width,
                BarButtonHeight, icon: icon, fontSize: size, font: font);

            // Button() hardcodes `.btn`'s .07em; the bar and its selector override it, and the label
            // must be re-measured afterwards or the icon sits off center.
            TMP_Text text = style.Label;
            if (text != null)
            {
                text.characterSpacing = Girdap.Spacing(spacingEm);
            }

            HudCenterContent(style, width, BarButtonHeight);
            return style;
        }

        /// <summary>Re-centers the icon+label block; mirrors <c>AdminMatchControls.Center</c>.</summary>
        private static void HudCenterContent(UiButtonStyle style, float width, float height)
        {
            TMP_Text text = style.Label;
            if (text == null)
            {
                return;
            }

            UiImage icon = HudIconOf(style);
            float labelWidth = Mathf.Ceil(text.GetPreferredValues(text.text).x);
            float cursor = (width - (labelWidth + (icon != null ? BarIcon + BarGap : 0f))) * 0.5f;

            if (icon != null)
            {
                PlaceMiddleLeft(icon.rectTransform, cursor, BarIcon, BarIcon);
                cursor += BarIcon + BarGap;
            }

            PlaceMiddleLeft(text.rectTransform, cursor, labelWidth, height);
        }

        private static UiImage HudIconOf(UiButtonStyle style)
        {
            Transform icon = style.transform.Find("Icon");
            return icon != null ? icon.GetComponent<UiImage>() : null;
        }

        private static float HudBarWidth(string label, string alternate)
        {
            float text = HudTextWidth(label, GirdapFont.ChakraBold, 18f, 0.12f);
            if (!string.IsNullOrEmpty(alternate))
            {
                // The box must fit BOTH labels: it keeps its width while the state changes.
                text = Mathf.Max(text, HudTextWidth(alternate, GirdapFont.ChakraBold, 18f, 0.12f));
            }

            return Mathf.Max(BarButtonMinWidth, 32f + BarIcon + BarGap + text);
        }

        /// <summary>
        /// CSS <c>.mbar .sep</c>: a 1 px rule that fades out at both ends. Two stacked halves, because
        /// a <see cref="UiShape"/> gradient has two stops, not three.
        /// </summary>
        private static void HudSeparator(Transform parent, float x)
        {
            Color line = Girdap.SepLine;
            Color fade = new Color(line.r, line.g, line.b, 0f);

            UiShape top = Shape(parent, "SepTop");
            PlaceMiddleLeft(top.rectTransform, x + 6f, 1f, 24f);
            top.rectTransform.anchoredPosition = new Vector2(x + 6f, 12f);
            top.Chamfer(0f).Fill(fade, line, UiGradientMode.Vertical).Antialias(false);

            UiShape bottom = Shape(parent, "SepBottom");
            PlaceMiddleLeft(bottom.rectTransform, x + 6f, 1f, 24f);
            bottom.rectTransform.anchoredPosition = new Vector2(x + 6f, -12f);
            bottom.Chamfer(0f).Fill(line, fade, UiGradientMode.Vertical).Antialias(false);
        }

        // ---------------------------------------------------------------- utilities

        private static GameObject HudNestPanel(Transform parent, string prefabName)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + prefabName + ".prefab");
            if (asset == null)
            {
                Debug.LogError($"[Girdap] {prefabName}.prefab yok — HUD'a iç içe panel eklenemedi.");
                return null;
            }

            // ⚠️ InstantiatePrefab, not Instantiate: only this keeps a NESTED prefab instance, so a
            // later panel rebuild still reaches the HUD.
            var instance = PrefabUtility.InstantiatePrefab(asset, parent) as GameObject;
            if (instance == null)
            {
                return null;
            }

            instance.name = prefabName;
            if (instance.transform is RectTransform rect)
            {
                Stretch(rect);
            }

            return instance;
        }

        private static T HudLoadComponent<T>(string prefabName) where T : Component
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + prefabName + ".prefab");
            if (asset == null)
            {
                Debug.LogError($"[Girdap] {prefabName}.prefab yok.");
                return null;
            }

            return asset.GetComponent<T>();
        }

        /// <summary>Width of a `.btn` box: padding + icon + gap + label + gap + keycap.</summary>
        private static float HudButtonWidth(string label, GirdapFont font, float size,
            float spacingEm, float padding, float height, bool icon, string kbd)
        {
            // Mirrors Button()'s own derivation, so the padding comes out at the CSS value.
            float gap = height >= 48f ? 8f : height >= 36f ? 6f : 5f;
            float iconSize = height >= 48f ? 20f : 16f;

            float width = HudTextWidth(label, font, size, spacingEm);
            if (icon)
            {
                width += iconSize + gap;
            }

            if (!string.IsNullOrEmpty(kbd))
            {
                width += HudKbdWidth(kbd) + gap;
            }

            return padding * 2f + width;
        }

        /// <summary>Keycap width; same rule as <c>Kbd()</c>.</summary>
        private static float HudKbdWidth(string key)
        {
            return Mathf.Max(22f, HudTextWidth(key, GirdapFont.ChakraBold, 12f, 0f) + 10f);
        }

        /// <summary>
        /// Measures text with the theme's settings. A throwaway TMP object: <c>GetPreferredValues</c>
        /// works detached, and there is no other way to size a box before the prefab exists.
        /// </summary>
        private static float HudTextWidth(string text, GirdapFont font, float size, float spacingEm)
        {
            var go = new GameObject("__GirdapMeasure");
            var tmp = go.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset asset = LoadFontAsset(font);
            if (asset != null)
            {
                tmp.font = asset;
            }

            tmp.fontSize = size;
            tmp.characterSpacing = Girdap.Spacing(spacingEm);
            tmp.richText = false;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.enableAutoSizing = false;

            float width = Mathf.Ceil(tmp.GetPreferredValues(text ?? "").x);
            Object.DestroyImmediate(go);
            return width;
        }

        /// <summary>Recolors a keycap: CSS <c>kbd</c> borrows <c>currentColor</c> at .6 alpha, and
        /// <c>Kbd()</c> assumes the default text color.</summary>
        private static void HudTintKbd(RectTransform kbd, Color color)
        {
            color.a = 0.6f;

            var shape = kbd.GetComponentInChildren<UiShape>(true);
            if (shape != null)
            {
                shape.Outline(1f, color);
            }

            var label = kbd.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.color = color;
            }
        }

        private static void HudSet(SerializedObject so, string field, Object value)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[Girdap] {so.targetObject.GetType().Name}.{field} alanı yok.");
                return;
            }

            property.objectReferenceValue = value;
        }

        private static void HudSetFloat(SerializedObject so, string field, float value)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[Girdap] {so.targetObject.GetType().Name}.{field} alanı yok.");
                return;
            }

            property.floatValue = value;
        }

        private static void HudSetInt(SerializedObject so, string field, int value)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[Girdap] {so.targetObject.GetType().Name}.{field} alanı yok.");
                return;
            }

            property.intValue = value;
        }
    }
}
