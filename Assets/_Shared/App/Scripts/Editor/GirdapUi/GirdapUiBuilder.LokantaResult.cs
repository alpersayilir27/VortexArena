using TMPro;
using UnityEditor;
using UnityEngine;
using VortexArena.App;
using VortexArena.Core.UI;

// `Image x = Image(...)` gives CS0119 — simple name lookup finds the member group before the type.
using UiImage = UnityEngine.UI.Image;
using UiObject = UnityEngine.Object;

namespace VortexArena.App.Editor
{
    /// <summary>
    /// Burger mode's match end screen (<c>Docs/Gelistirici/Arayuz/asci.html</c> frames 4-5):
    /// shift result card and "GÜNÜN HESABI" scoreboard, built into
    /// <c>Assets/Modes/Burger/UI/BurgerResultOverlay.prefab</c>.
    /// <para>
    /// ⚠️ <b>Generated from scratch, not a prefab VARIANT.</b> The skin is now DATA on
    /// <see cref="MatchResultOverlay"/> (row colours, co-op text targets, five footer boxes), so
    /// there is no override set left to inherit — the variant's value was exactly that set.
    /// The root component and every binding stay the shared ones, which is what Yapma-Listesi's
    /// "kopya yapma" rule protects: no second logic path, no <c>modeId</c> branch.
    /// </para>
    /// <para>
    /// ⚠️ <b>The file is written over IN PLACE</b> (never deleted first): <c>BURGER.asset</c> links
    /// this path by GUID. The root's local fileID does change, so the mode asset's reference is
    /// re-checked and repaired at the end of the run.
    /// </para>
    /// <para>
    /// ⚠️ Root chrome (world-space Canvas, scaler, <c>HudFollow</c>) is COPIED from the generic
    /// screen instead of typed here — a hand-written sorting value drops the panel behind the
    /// blackout quad.
    /// </para>
    /// </summary>
    public static partial class GirdapUiBuilder
    {
        public const string LkResultPrefab = LkUiDir + "BurgerResultOverlay.prefab";

        private const string LkResultName = "BurgerResultOverlay";
        private const string LkGenericResultPrefab = PrefabDir + ResultPrefab + ".prefab";

        // The card IS the canvas (like the generic screen), so every CSS px maps 1:1.
        private const float LkResW = 1400f;
        private const float LkResH = 860f;
        private const float LkResRadius = 34f;
        private const float LkResOutline = 6f;
        private const float LkResShadow = 10f;
        private const float LkCheckerInset = 10f;
        private const float LkCheckerH = 22f;

        // Result card
        private const float LkTitleY = 64f;
        private const float LkTitleH = 64f;
        private const float LkBigY = 150f;
        private const float LkBigH = 300f;
        private const float LkScoreLabelY = 452f;
        private const float LkScoreLabelH = 44f;
        private const float LkPillsY = 570f;
        private const float LkPillsH = 84f;
        private const float LkPillGap = 28f;
        private const float LkStarSize = 150f;
        private const float LkDecorSize = 156f; // mockup art at 1.3x

        // Scoreboard
        private const float LkBoardPad = 40f;
        private const float LkHeadY = 48f;
        private const float LkResHeadH = 70f;
        private const float LkHeadPillH = 64f;
        private const float LkBlockGap = 28f;
        private const float LkBlockY = LkHeadY + LkResHeadH + 18f;
        private const float LkBlockW = (LkResW - 2f * LkBoardPad - LkBlockGap) / 2f;
        private const float LkThRowH = 36f;
        private const float LkFootH = 96f;
        private const float LkFootY = LkResH - 20f - LkFootH;
        private const float LkFootW = LkResW - 2f * LkBoardPad;
        private const float LkFootWhoW = 200f;
        private const float LkBlockH = LkFootY - LkBlockY - 16f;

        // Row metrics written into the overlay (`.brow`: 66 tall, 10 gap, header row + 10).
        private const float LkResRowH = 66f;
        private const float LkResRowGap = 10f;
        private const float LkRowsTop = LkThRowH + 10f;
        private const float LkResScoreCellW = 110f;
        private const float LkScoreIcon = 24f;

        [MenuItem("Tools/VortexArena/UI/Girdap/Yalnız Aşçı sonuç ekranı")]
        public static void BuildLokantaResultMenu()
        {
            BuildAssets();
            BuildLokantaResult();
            AssetDatabase.SaveAssets();
        }

        public static void BuildLokantaResult()
        {
            EnsureFolder(LkUiDir);

            GameObject root = NewRoot(LkResultName, LkResW, LkResH);
            try
            {
                LkAdoptResultChrome(root);
                var overlay = root.AddComponent<MatchResultOverlay>();

                RectTransform resultPanel = Node(root.transform, "ResultPanel");
                Stretch(resultPanel);
                RectTransform boardPanel = Node(root.transform, "ScoreboardPanel");
                Stretch(boardPanel);

                var wiring = new SerializedObject(overlay);
                ResultSet(wiring, "resultPanel", resultPanel.gameObject);
                ResultSet(wiring, "scoreboardPanel", boardPanel.gameObject);

                LkBuildResultCard(resultPanel, wiring);
                LkBuildResultBoard(boardPanel, wiring);
                LkBindResultSkin(wiring);
                wiring.ApplyModifiedPropertiesWithoutUndo();

                boardPanel.gameObject.SetActive(false);

                // Written over the existing file: deleting it first would mint a new GUID and cut
                // BURGER.asset's link.
                PrefabUtility.SaveAsPrefabAsset(root, LkResultPrefab);
            }
            finally
            {
                UiObject.DestroyImmediate(root);
            }

            LkVerifyModeLink();
        }

        // -------------------------------------------------------------- result card

        private static void LkBuildResultCard(RectTransform panel, SerializedObject wiring)
        {
            LkResultCardShell(panel);

            RectTransform title = LkResultRibbon(panel, "Title", "VARDİYA BİTTİ!", LkTitleH, 36f,
                false, "DinerBurger", 28f, out TextMeshProUGUI titleLabel);
            PlaceTopCenter(title, LkTitleY, title.sizeDelta.x, LkTitleH);

            TextMeshProUGUI big = LkText(panel, "Big", "120", GirdapFont.FredokaBold, 300f,
                Color.white, TextAlignmentOptions.Center);
            PlaceTopCenter(big.rectTransform, LkBigY, LkResW - 120f, LkBigH);
            // ⚠️ The underlay offset is em-relative, not px: the mockup's 18 px shadow under a
            // 300 px number is 18/300 em — a raw 18 clamps to the shader's maximum.
            TextOutline(big, Lokanta.Ink, 0.2f, Lokanta.Ink, 18f / LkBigH);

            RectTransform label = LkResultRibbon(panel, "ScoreLabel", "EKİP SKORU", LkScoreLabelH,
                20f, true, null, 0f, out TextMeshProUGUI _);
            PlaceTopCenter(label, LkScoreLabelY, label.sizeDelta.x, LkScoreLabelH);

            // Pills row — widths are fixed (mockup), the block is centred as a whole.
            const float happyW = 260f;
            const float selfW = 220f;
            float total = happyW * 2f + selfW + LkStarSize + 3f * LkPillGap;
            float x = (LkResW - total) * 0.5f;

            TextMeshProUGUI happy = LkCountPill(panel, "Happy", LkMood.Happy, "MUTLU", x, happyW);
            x += happyW + LkPillGap;
            TextMeshProUGUI sad = LkCountPill(panel, "Sad", LkMood.Sad, "MUTSUZ", x, happyW);
            x += happyW + LkPillGap;
            TextMeshProUGUI self = LkSelfPill(panel, x, selfW);
            x += selfW + LkPillGap;
            TextMeshProUGUI rank = LkStarBadge(panel, x);

            LkCardDecor(panel, "DinerBurger", "DinerBurger", -8f, 70f, true);
            LkCardDecor(panel, "DinerFries", "DinerFries", 8f, 90f, false);

            ResultSet(wiring, "resultTitleText", titleLabel);
            ResultSet(wiring, "coopSharedScoreText", big);
            ResultSet(wiring, "coopHappyText", happy);
            ResultSet(wiring, "coopUnhappyText", sad);
            ResultSet(wiring, "coopSelfScoreText", self);
            ResultSet(wiring, "coopRankText", rank);
        }

        /// <summary>Cream card + checker strip; both panels wear the same shell.</summary>
        private static void LkResultCardShell(RectTransform panel)
        {
            UiShape plate = LkCard(panel, "Card", LkResRadius, Lokanta.Cream, LkResOutline,
                LkResShadow);
            Stretch((RectTransform)plate.transform.parent);

            Transform card = plate.transform.parent;
            UiImage checker = Image(card, "Checker", LkSprite("Lk_Checker"), Color.white,
                UiImage.Type.Tiled);
            StretchTop(checker.rectTransform, LkCheckerInset, LkCheckerInset, LkCheckerInset,
                LkCheckerH);

            UiShape bar = Shape(card, "CheckerBar");
            StretchTop(bar.rectTransform, LkCheckerInset, LkCheckerInset + LkCheckerH,
                LkCheckerInset, 3f);
            bar.Fill(Lokanta.Must2).Antialias(false);
        }

        /// <summary>Mood pill: face · number · caption. Returns the number.</summary>
        private static TextMeshProUGUI LkCountPill(Transform parent, string name, LkMood mood,
            string caption, float x, float w)
        {
            UiShape plate = LkPill(parent, name, LkPillsH, Lokanta.Paper, Lokanta.Outline, 5f);
            var root = (RectTransform)plate.transform.parent;
            Place(root, x, LkPillsY, w, LkPillsH);

            UiImage face = LkFace(root, "Face", mood, 52f);
            PlaceMiddleLeft(face.rectTransform, 24f, 52f, 52f);

            TextMeshProUGUI value = LkText(root, "Value", "0", GirdapFont.FredokaBold, 48f,
                Lokanta.Ink, TextAlignmentOptions.MidlineLeft);
            PlaceMiddleLeft(value.rectTransform, 86f, 80f, LkPillsH);

            TextMeshProUGUI label = LkText(root, "Label", caption, GirdapFont.NunitoExtraBold, 15f,
                LkInk(0.75f), TextAlignmentOptions.MidlineLeft, 0.18f);
            PlaceMiddleLeft(label.rectTransform, 172f, w - 184f, LkPillsH);
            return value;
        }

        /// <summary>Own-contribution pill (mustard): caption over the number.</summary>
        private static TextMeshProUGUI LkSelfPill(Transform parent, float x, float w)
        {
            UiShape plate = LkPill(parent, "Self", LkPillsH, Lokanta.MustHi, Lokanta.Outline, 5f);
            plate.Fill(Lokanta.MustHi, Lokanta.Must, UiGradientMode.Vertical);
            var root = (RectTransform)plate.transform.parent;
            Place(root, x, LkPillsY, w, LkPillsH);

            TextMeshProUGUI label = LkText(root, "Label", "SEN", GirdapFont.NunitoExtraBold, 15f,
                LkInk(0.75f), TextAlignmentOptions.Center, 0.18f);
            StretchTop(label.rectTransform, 0f, 16f, 0f, 16f);

            TextMeshProUGUI value = LkText(root, "Value", "40", GirdapFont.FredokaBold, 48f,
                Lokanta.Ink, TextAlignmentOptions.Center);
            Place(value.rectTransform, 0f, 30f, w, LkPillsH - 34f);
            return value;
        }

        /// <summary>Rank star: "1." over "SIRA". Returns the rank text.</summary>
        private static TextMeshProUGUI LkStarBadge(Transform parent, float x)
        {
            UiImage star = Image(parent, "Star", LkSprite("Lk_Star"), Color.white);
            star.preserveAspect = true;
            Place(star.rectTransform, x, LkPillsY + (LkPillsH - LkStarSize) * 0.5f, LkStarSize,
                LkStarSize);

            TextMeshProUGUI rank = LkText(star.transform, "Rank", "1.", GirdapFont.FredokaBold, 44f,
                Lokanta.Ink, TextAlignmentOptions.Center);
            Place(rank.rectTransform, 0f, 38f, LkStarSize, 50f);

            TextMeshProUGUI label = LkText(star.transform, "Label", "SIRA",
                GirdapFont.NunitoExtraBold, 13f, LkInk(0.75f), TextAlignmentOptions.Center, 0.18f);
            Place(label.rectTransform, 0f, 88f, LkStarSize, 16f);
            return rank;
        }

        /// <summary>Cartoon decoration tilted into a bottom corner.</summary>
        private static void LkCardDecor(Transform parent, string name, string sprite, float angle,
            float inset, bool left)
        {
            UiImage art = Image(parent, name, LkSprite(sprite), Color.white);
            art.preserveAspect = true;

            if (left)
            {
                PlaceBottomLeft(art.rectTransform, inset, 60f, LkDecorSize, LkDecorSize);
            }
            else
            {
                PlaceBottomRight(art.rectTransform, inset, 60f, LkDecorSize, LkDecorSize);
            }

            art.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        // --------------------------------------------------------------- scoreboard

        private static void LkBuildResultBoard(RectTransform panel, SerializedObject wiring)
        {
            LkResultCardShell(panel);
            LkBoardHead(panel, wiring);
            LkBoardBody(panel, wiring);
            LkBoardFoot(panel, wiring);
        }

        /// <summary>Head row: "GÜNÜN HESABI" ribbon left, customer + team pills right.</summary>
        private static void LkBoardHead(RectTransform panel, SerializedObject wiring)
        {
            RectTransform title = LkResultRibbon(panel, "Title", "GÜNÜN HESABI", LkResHeadH, 36f, false,
                "DinerBurger", 30f, out TextMeshProUGUI _);
            Place(title, LkBoardPad, LkHeadY, title.sizeDelta.x, LkResHeadH);

            const float moodW = 150f;
            const float sharedW = 210f;
            const float gap = 16f;
            float x = LkResW - LkBoardPad - (moodW * 2f + sharedW + 2f * gap);
            float y = LkHeadY + (LkResHeadH - LkHeadPillH) * 0.5f;

            TextMeshProUGUI happy = LkHeadPill(panel, "Happy", LkMood.Happy, x, moodW, y);
            x += moodW + gap;
            TextMeshProUGUI sad = LkHeadPill(panel, "Sad", LkMood.Sad, x, moodW, y);
            x += moodW + gap;

            UiShape plate = LkPill(panel, "Shared", LkHeadPillH, Lokanta.MustHi, Lokanta.Outline, 5f);
            plate.Fill(Lokanta.MustHi, Lokanta.Must, UiGradientMode.Vertical);
            var shared = (RectTransform)plate.transform.parent;
            Place(shared, x, y, sharedW, LkHeadPillH);

            TextMeshProUGUI label = LkText(shared, "Label", "EKİP", GirdapFont.NunitoExtraBold, 14f,
                LkInk(0.75f), TextAlignmentOptions.MidlineLeft, 0.18f);
            PlaceMiddleLeft(label.rectTransform, 18f, 60f, LkHeadPillH);

            TextMeshProUGUI value = LkText(shared, "Value", "120", GirdapFont.FredokaBold, 34f,
                Lokanta.Ink, TextAlignmentOptions.MidlineLeft);
            PlaceMiddleLeft(value.rectTransform, 84f, sharedW - 100f, LkHeadPillH);

            ResultSet(wiring, "boardHappyText", happy);
            ResultSet(wiring, "boardUnhappyText", sad);
            ResultSet(wiring, "boardSharedScoreText", value);
        }

        private static TextMeshProUGUI LkHeadPill(Transform parent, string name, LkMood mood,
            float x, float w, float y)
        {
            UiShape plate = LkPill(parent, name, LkHeadPillH, Lokanta.Paper, Lokanta.Outline, 5f);
            var root = (RectTransform)plate.transform.parent;
            Place(root, x, y, w, LkHeadPillH);

            UiImage face = LkFace(root, "Face", mood, 38f);
            PlaceMiddleLeft(face.rectTransform, 16f, 38f, 38f);

            TextMeshProUGUI value = LkText(root, "Value", "0", GirdapFont.FredokaBold, 34f,
                Lokanta.Ink, TextAlignmentOptions.MidlineLeft);
            PlaceMiddleLeft(value.rectTransform, 62f, w - 74f, LkHeadPillH);
            return value;
        }

        /// <summary>Two columns of rows; the row template lives in the left one.</summary>
        private static void LkBoardBody(RectTransform panel, SerializedObject wiring)
        {
            var blocks = new UiObject[MatchResultOverlay.BlockCount];
            var titles = new UiObject[MatchResultOverlay.BlockCount];
            var scoreHeaders = new UiObject[MatchResultOverlay.BlockCount];
            var rowParents = new UiObject[MatchResultOverlay.BlockCount];
            GameObject template = null;

            for (int b = 0; b < MatchResultOverlay.BlockCount; b++)
            {
                RectTransform block = Node(panel, "Block" + b);
                Place(block, LkBoardPad + b * (LkBlockW + LkBlockGap), LkBlockY, LkBlockW,
                    LkBlockH);

                // .bth-row — column captions; the right block's caption is never rewritten (its
                // boardBlockTitles slot stays empty) so both columns keep the same header.
                TextMeshProUGUI who = LkText(block, "ThWho", "OYUNCULAR",
                    GirdapFont.NunitoExtraBold, 14f, LkInk(0.7f),
                    TextAlignmentOptions.MidlineLeft, 0.16f);
                Place(who.rectTransform, MatchResultOverlay.NameCellX, 0f,
                    LkBlockW - MatchResultOverlay.NameCellX - LkResScoreCellW, LkThRowH);

                TextMeshProUGUI score = LkText(block, "ThScore", "KATKI",
                    GirdapFont.NunitoExtraBold, 14f, LkInk(0.7f),
                    TextAlignmentOptions.MidlineRight, 0.16f);
                Place(score.rectTransform, LkBlockW - LkResScoreCellW - 16f, 0f, LkResScoreCellW,
                    LkThRowH);

                RectTransform rows = Node(block, "Rows");
                Place(rows, 0f, LkRowsTop, LkBlockW, LkBlockH - LkRowsTop);
                if (b == 0)
                {
                    template = LkRowTemplate(rows);
                }

                TextMeshProUGUI summary = LkText(block, "Summary", "2 oyuncu · 120 katkı · 12 burger",
                    GirdapFont.NunitoBold, 18f, LkInk(0.6f), TextAlignmentOptions.MidlineLeft);
                Place(summary.rectTransform, 8f, LkRowsTop + 4f, LkBlockW - 16f, 26f);

                blocks[b] = block.gameObject;
                titles[b] = b == 0 ? who : null;
                scoreHeaders[b] = score;
                rowParents[b] = rows;
                ResultSet(wiring, b == 0 ? "boardTeamSummaryText" : "boardTeamSummary2Text",
                    summary);
            }

            ResultSetArray(wiring, "boardBlocks", blocks);
            ResultSetArray(wiring, "boardBlockPlates", new UiObject[MatchResultOverlay.BlockCount]);
            ResultSetArray(wiring, "boardBlockTitles", titles);
            ResultSetArray(wiring, "boardBlockScoreHeaders", scoreHeaders);
            ResultSetArray(wiring, "boardBlockCombatCells", new UiObject[0]);
            ResultSetArray(wiring, "boardRowParents", rowParents);
            ResultSet(wiring, "boardRowTemplate", template);

            // Legacy column table has no counterpart in this skin; emptied so the runtime's
            // visibility pass has nothing to touch.
            ResultSetArray(wiring, "boardColumns", new UiObject[0]);
            ResultSetArray(wiring, "boardColumnRoots", new UiObject[0]);
            ResultSetArray(wiring, "boardColumnHeaders", new UiObject[0]);
        }

        /// <summary>
        /// <c>.brow</c> template, cloned per player at runtime. ⚠️ Cell NAMES are the contract
        /// (<c>MatchResultOverlay.Row.Bind</c>): <c>Bg·Rank·Name·Tag·Small·Score·ScoreIcon</c>. The
        /// combat cells are absent on purpose — nobody shoots in a co-op shift.
        /// </summary>
        private static GameObject LkRowTemplate(Transform rows)
        {
            RectTransform row = Node(rows, "Row");
            Place(row, 0f, 0f, LkBlockW, LkResRowH);
            row.gameObject.AddComponent<CanvasGroup>(); // a departed player's row dims as a whole

            UiShape drop = Shape(row, "Shadow");
            Stretch(drop.rectTransform, 0f, 4f, 0f, -4f);
            drop.Radius(18f).Fill(Lokanta.Ink);

            UiShape bg = Shape(row, "Bg");
            Stretch(bg.rectTransform);
            bg.Radius(18f).Outline(3f, Lokanta.Ink).Fill(Lokanta.Paper);

            UiShape badge = Shape(row, "RankBadge");
            Place(badge.rectTransform, 8f, (LkResRowH - 40f) * 0.5f, 40f, 40f);
            badge.Radius(20f).Outline(3f, Lokanta.Ink).Fill(Lokanta.Must);

            TextMeshProUGUI rank = LkText(row, "Rank", "1", GirdapFont.FredokaBold, 20f, Lokanta.Ink,
                TextAlignmentOptions.Center);
            Place(rank.rectTransform, 8f, (LkResRowH - 40f) * 0.5f, 40f, 40f);

            TextMeshProUGUI name = LkText(row, "Name", "Ayşe", GirdapFont.FredokaBold, 30f,
                Lokanta.Ink, TextAlignmentOptions.MidlineLeft);
            Place(name.rectTransform, MatchResultOverlay.NameCellX, 0f, 300f, LkResRowH);

            // "SEN" badge; the runtime places it after the measured name width.
            RectTransform tag = Node(row, "Tag");
            UiShape tagBg = Shape(tag, "Bg");
            Stretch(tagBg.rectTransform);
            tagBg.Radius(13f).Outline(3f, Lokanta.Ink).Fill(Lokanta.Must);
            TextMeshProUGUI tagLabel = LkText(tag, "Label", "SEN", GirdapFont.NunitoExtraBold, 13f,
                Lokanta.Ink, TextAlignmentOptions.Center, 0.12f);
            Stretch(tagLabel.rectTransform);
            float tagW = Mathf.Ceil(tagLabel.GetPreferredValues(tagLabel.text).x) + 18f;
            Place(tag, MatchResultOverlay.NameCellX, (LkResRowH - 26f) * 0.5f, tagW, 26f);

            TextMeshProUGUI small = LkText(row, "Small", "#12", GirdapFont.NunitoBold, 15f,
                LkInk(0.55f), TextAlignmentOptions.MidlineLeft);
            Place(small.rectTransform, MatchResultOverlay.NameCellX, 0f, 200f, LkResRowH);

            float scoreX = LkBlockW - 16f - LkResScoreCellW;
            UiImage icon = Image(row, "ScoreIcon", LkSprite("DinerBurger"), Color.white);
            icon.preserveAspect = true;
            Place(icon.rectTransform, scoreX - LkScoreIcon - 8f, (LkResRowH - LkScoreIcon) * 0.5f,
                LkScoreIcon, LkScoreIcon);

            TextMeshProUGUI score = LkText(row, "Score", "6", GirdapFont.FredokaBold, 34f,
                Lokanta.Ink, TextAlignmentOptions.MidlineRight);
            Place(score.rectTransform, scoreX, 0f, LkResScoreCellW, LkResRowH);

            row.gameObject.SetActive(false); // an active template draws an empty row under the table
            return row.gameObject;
        }

        /// <summary>Footer: "SEN" plate + five stat boxes (KATKI · SIRA · MUTLU · MUTSUZ · EKİP
        /// SKORU). Labels are static; the runtime only writes the values.</summary>
        private static void LkBoardFoot(RectTransform panel, SerializedObject wiring)
        {
            UiShape plate = LkCard(panel, "Foot", 24f, Lokanta.Paper, Lokanta.Outline, 5f);
            var foot = (RectTransform)plate.transform.parent;
            Place(foot, LkBoardPad, LkFootY, LkFootW, LkFootH);

            UiShape who = Shape(foot, "Who");
            Place(who.rectTransform, 0f, 0f, LkFootWhoW, LkFootH);
            who.Radius(24f, 0f, 0f, 24f)
                .Fill(Lokanta.MustHi, Lokanta.Must, UiGradientMode.Vertical);
            TextMeshProUGUI whoLabel = LkText(who.transform, "Label", "SEN", GirdapFont.FredokaBold,
                40f, Lokanta.Ink, TextAlignmentOptions.Center);
            Stretch(whoLabel.rectTransform);

            UiShape divider = Shape(foot, "Divider");
            Place(divider.rectTransform, LkFootWhoW, 0f, 4f, LkFootH);
            divider.Fill(Lokanta.Ink).Antialias(false);

            RectTransform stats = Node(foot, "Stats");
            Place(stats, LkFootWhoW + 4f, 0f, LkFootW - LkFootWhoW - 4f, LkFootH);

            string[] labels = { "KATKI", "SIRA", "MUTLU", "MUTSUZ", "EKİP SKORU" };
            string[] samples = { "40", "1.", "9", "2", "120" };
            var values = new UiObject[labels.Length];
            float cellW = (LkFootW - LkFootWhoW - 4f) / labels.Length;

            for (int i = 0; i < labels.Length; i++)
            {
                RectTransform cell = Node(stats, "Stat" + i);
                Place(cell, i * cellW, 0f, cellW, LkFootH);

                TextMeshProUGUI caption = LkText(cell, "Label", labels[i],
                    GirdapFont.NunitoExtraBold, 14f, LkInk(0.7f), TextAlignmentOptions.Center, 0.2f);
                Place(caption.rectTransform, 0f, 18f, cellW, 16f);

                bool mood = i == 2 || i == 3;
                if (mood)
                {
                    UiImage face = LkFace(cell, "Face", i == 2 ? LkMood.Happy : LkMood.Sad, 34f);
                    Place(face.rectTransform, cellW * 0.5f - 58f, 40f, 34f, 34f);
                }

                TextMeshProUGUI value = LkText(cell, "Value", samples[i], GirdapFont.FredokaBold,
                    42f, Lokanta.Ink,
                    mood ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center);
                Place(value.rectTransform, mood ? cellW * 0.5f - 16f : 0f, 36f,
                    mood ? 80f : cellW, 44f);
                values[i] = value;
            }

            ResultSet(wiring, "footStatsGroup", stats.gameObject);
            ResultSetArray(wiring, "footStatValues", values);

            // ⚠️ Left unbound on purpose (the field stays empty on a fresh root): footWhoPlate is
            // painted with the TEAM gradient at runtime and this shift has no teams, the slab/
            // scorebug parts belong to the Girdap layout, and the composite head/score lines are
            // said by the ribbons and pills here.
            ResultSet(wiring, "boardSelfScoreText", values[0]);
            ResultSet(wiring, "boardRankText", values[1]);
        }

        // -------------------------------------------------------------- skin + words

        private static void LkBindResultSkin(SerializedObject wiring)
        {
            HudSetFloat(wiring, "rowHeight", LkResRowH);
            HudSetFloat(wiring, "rowGap", LkResRowGap);
            HudSetFloat(wiring, "rowsTop", LkRowsTop);

            ResultSetColor(wiring, "rowFillA", Lokanta.Paper);
            ResultSetColor(wiring, "rowFillB", Lokanta.Paper);
            ResultSetColor(wiring, "rowOutline", Lokanta.Ink);
            HudSetFloat(wiring, "rowOutlineWidth", 3f);
            ResultSetColor(wiring, "rowTextColor", Lokanta.Ink);
            ResultSetColor(wiring, "rowSmallColor", LkInk(0.55f));
            ResultSetColor(wiring, "rowRankColor", Lokanta.Ink);

            ResultSetColor(wiring, "ownRowFillA", Lokanta.MustSoft);
            ResultSetColor(wiring, "ownRowFillB", Lokanta.Must);
            ResultSetColor(wiring, "ownRowOutline", Lokanta.Ink);
            HudSetFloat(wiring, "ownRowOutlineWidth", 3f);
            ResultSetColor(wiring, "ownRowGlow", Color.clear);
            HudSetFloat(wiring, "ownRowGlowWidth", 0f);
            ResultSetColor(wiring, "ownRowTextColor", Lokanta.Ink);
            ResultSetColor(wiring, "ownRowSmallColor", LkInk(0.55f));
            ResultSetColor(wiring, "ownRowRankColor", Lokanta.Ink);

            HudSetFloat(wiring, "leftRowAlpha", 0.5f);
            LkSetBool(wiring, "tintTitleByOutcome", false);

            LkSetString(wiring, "coopTitle", "VARDİYA BİTTİ!");
            LkSetString(wiring, "coopScoreHeader", "KATKI");
            LkSetString(wiring, "soloBlockTitle", "OYUNCULAR");
            LkSetString(wiring, "coopBlockSummaryFormat", "{0} oyuncu · {1} katkı · {2} burger");
            ResultSetColor(wiring, "coopColor", Lokanta.Must);
        }

        // ------------------------------------------------------------------ chrome

        /// <summary>Copies the generic screen's root components (Canvas + scaler + HudFollow) onto
        /// the new root. <see cref="MatchResultOverlay"/> is skipped: its serialized references
        /// point INTO the generic prefab and would cross-link two assets.</summary>
        private static void LkAdoptResultChrome(GameObject root)
        {
            var generic = AssetDatabase.LoadAssetAtPath<GameObject>(LkGenericResultPrefab);
            if (generic == null)
            {
                Debug.LogWarning($"[Lokanta] {LkGenericResultPrefab} yok — aşçı sonuç ekranı "
                                 + "tuval/HudFollow ayarlarını alamadı.");
                return;
            }

            Component[] parts = generic.GetComponents<Component>();
            for (int i = 0; i < parts.Length; i++)
            {
                Component part = parts[i];
                if (part == null || part is Transform || part is CanvasRenderer ||
                    part is MatchResultOverlay)
                {
                    continue;
                }

                System.Type type = part.GetType();
                Component clone = root.GetComponent(type);
                if (clone == null)
                {
                    clone = root.AddComponent(type);
                }

                LkCopySerialized(part, clone);

                // ⚠️ A Canvas's sorting does NOT come through SerializedObject (m_SortingOrder is
                // hidden): copied by hand, or the panel silently changes layer.
                if (part is Canvas source && clone is Canvas target)
                {
                    target.renderMode = source.renderMode;
                    target.overrideSorting = source.overrideSorting;
                    target.sortingLayerID = source.sortingLayerID;
                    target.sortingOrder = source.sortingOrder;
                    target.additionalShaderChannels = source.additionalShaderChannels;
                }
            }
        }

        // ----------------------------------------------------------------- helpers

        /// <summary>Ribbon sized to its text, with an optional sprite before the label.</summary>
        private static RectTransform LkResultRibbon(Transform parent, string name, string text,
            float h, float size, bool must, string icon, float iconSize,
            out TextMeshProUGUI label)
        {
            RectTransform root = LkRibbon(parent, name, text, 100f, h, GirdapFont.FredokaBold, size,
                must);
            label = root.Find("Label").GetComponent<TextMeshProUGUI>();

            float pad = h * 0.55f;
            float textW = Mathf.Ceil(label.GetPreferredValues(label.text).x);
            float iconW = string.IsNullOrEmpty(icon) ? 0f : iconSize + 12f;
            root.sizeDelta = new Vector2(textW + iconW + 2f * pad, h);

            if (iconW > 0f)
            {
                UiImage art = Image(root, "Icon", LkSprite(icon), Color.white);
                art.preserveAspect = true;
                PlaceMiddleLeft(art.rectTransform, pad, iconSize, iconSize);
                Stretch(label.rectTransform, pad + iconW, 0f, pad, 0f);
            }

            return root;
        }

        private static Color LkInk(float alpha)
        {
            Color ink = Lokanta.Ink;
            ink.a = alpha;
            return ink;
        }

        private static void LkSetBool(SerializedObject so, string field, bool value)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[Lokanta] MatchResultOverlay.{field} alanı yok — yazılamadı.");
                return;
            }

            property.boolValue = value;
        }

        private static void LkSetString(SerializedObject so, string field, string value)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[Lokanta] MatchResultOverlay.{field} alanı yok — yazılamadı.");
                return;
            }

            property.stringValue = value;
        }

        // ---------------------------------------------------------------- mode link

        /// <summary>
        /// Re-points every Burger mode definition at the rebuilt prefab. ⚠️ The GUID survives an
        /// overwrite but the ROOT's local fileID does not, so a reference saved against the old root
        /// would resolve to nothing — and a missing result screen is silent (the generic one opens).
        /// </summary>
        private static void LkVerifyModeLink()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LkResultPrefab);
            if (prefab == null)
            {
                Debug.LogError($"[Lokanta] {LkResultPrefab} kaydedilemedi.");
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:ScriptableObject",
                new[] { "Assets/Modes/Burger" });
            string state = "mod bağı bulunamadı";

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (asset == null)
                {
                    continue;
                }

                var so = new SerializedObject(asset);
                SerializedProperty property = so.FindProperty("resultScreenPrefab");
                if (property == null)
                {
                    continue;
                }

                string file = System.IO.Path.GetFileName(path);
                if (property.objectReferenceValue == prefab)
                {
                    state = file + " bağı sağlam";
                    continue;
                }

                property.objectReferenceValue = prefab;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
                state = file + " bağı yenilendi";
            }

            Debug.Log("[Lokanta] Aşçı sonuç ekranı üretildi: " + LkResultName
                      + "/{ResultPanel/{Card/{Shadow,Plate,Checker,CheckerBar}, Title, Big,"
                      + " ScoreLabel, Happy, Sad, Self, Star, DinerBurger, DinerFries},"
                      + " ScoreboardPanel/{Card, Title, Happy, Sad, Shared,"
                      + " Block0/{ThWho,ThScore,Rows/Row,Summary}, Block1/{…},"
                      + " Foot/{Who,Divider,Stats/Stat0..4}}}"
                      + $" · kart {LkResW}x{LkResH} · satır {LkResRowH}+{LkResRowGap} (üst {LkRowsTop})"
                      + " · bağlanan: resultPanel, scoreboardPanel, resultTitleText,"
                      + " coopSharedScoreText, coopHappyText, coopUnhappyText, coopSelfScoreText,"
                      + " coopRankText, boardHappyText, boardUnhappyText, boardSharedScoreText,"
                      + " boardSelfScoreText, boardRankText, boardBlocks, boardBlockTitles[0],"
                      + " boardBlockScoreHeaders, boardRowParents, boardRowTemplate,"
                      + " boardTeamSummaryText(+2), footStatsGroup, footStatValues[5]"
                      + " · deri: satır/kendi satırı renkleri, leftRowAlpha,"
                      + " tintTitleByOutcome=false, coopTitle, coopBlockSummaryFormat"
                      + " · " + state);
        }
    }
}
