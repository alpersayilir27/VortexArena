using TMPro;
using UnityEditor;
using UnityEngine;
using VortexArena.App;
using VortexArena.Core.UI;

// Alias kept identical to the other result builder: the binding arrays are UnityEngine.Object[].
using UiObject = UnityEngine.Object;

namespace VortexArena.App.Editor
{
    /// <summary>
    /// Zombie run's match end screen ("kan damlası" theme): outcome card + "EKİP RAPORU" table, built
    /// into <c>Assets/Modes/Zombie/UI/ZombieResultOverlay.prefab</c>.
    /// <para>
    /// ⚠️ <b>Generated from scratch, not a prefab VARIANT</b> (Yemek-Kitabı 13.2 yol B): the layout is
    /// its own (six value cells and row badges), so there is no override set left to inherit. The root
    /// component and every binding stay the shared ones — no second logic path, no <c>modeId</c> branch.
    /// </para>
    /// <para>
    /// ⚠️ <b>The file is written over IN PLACE</b>: <c>ZOMBIE.asset</c> links this path by GUID. The
    /// root's local fileID does change, so the mode asset's reference is re-checked at the end.
    /// </para>
    /// <para>
    /// ⚠️ Cell NAMES are the contract (<c>MatchResultOverlay.Row.Bind</c>):
    /// <c>Bg·Rank·Name·Tag·Small·Score·Kills·Deaths·Headshots·Revives·Badge0..2</c>. A renamed node is
    /// silently never filled. The extra cells are drawn only while the mode publishes its per-player
    /// tokens, which is the runtime's own gate.
    /// </para>
    /// </summary>
    public static partial class GirdapUiBuilder
    {
        public const string ZbUiDir = "Assets/Modes/Zombie/UI/";

        public const string ZbResultPrefab = ZbUiDir + "ZombieResultOverlay.prefab";

        private const string ZbResultName = "ZombieResultOverlay";

        // The card IS the canvas (like the generic screen and the diner one), so every px maps 1:1.
        private const float ZrW = 1400f;
        private const float ZrH = 860f;
        private const float ZrPad = 40f;

        // ---- result card
        private const float ZrTitleY = 64f;
        private const float ZrTitleH = 140f;
        private const float ZrWaveLabelY = 226f;
        private const float ZrWaveY = 262f;
        private const float ZrWaveH = 196f;
        private const float ZrScoreLabelY = 470f;
        private const float ZrScoreY = 504f;
        private const float ZrScoreH = 150f;
        private const float ZrPillsY = 684f;
        private const float ZrPillsH = 88f;
        private const float ZrPillW = 280f;
        private const float ZrPillGap = 28f;
        private const float ZrLabelH = 36f;

        // ---- scoreboard
        private const float ZrHeadY = 36f;
        private const float ZrHeadH = 60f;
        private const float ZrBlockW = ZrW - 2f * ZrPad; // blocks are STACKED: six cells need the width
        private const float ZrThRowH = 36f;
        private const float ZrRowH = 56f;
        private const float ZrRowGap = 8f;
        private const float ZrRowsTop = ZrThRowH + 8f;
        private const int ZrRowsPerBlock = 4;
        private const float ZrBlockH = ZrRowsTop + ZrRowsPerBlock * (ZrRowH + ZrRowGap);
        private const float ZrBlock0Y = 112f;
        private const float ZrBlockGapY = 16f;
        private const float ZrFootH = 96f;
        private const float ZrFootY = ZrH - 20f - ZrFootH;
        private const float ZrFootWhoW = 200f;

        // Value cells: five numbers right-aligned at the row's end, badges in the gap before them.
        private const float ZrCellW = 100f;
        private const float ZrCellGap = 8f;
        private const float ZrCellsRight = 16f;
        private const int ZrCellCount = 5;
        private const float ZrCellsX =
            ZrBlockW - ZrCellsRight - ZrCellCount * (ZrCellW + ZrCellGap);
        private const float ZrNameW = 380f;
        private const float ZrBadgeW = 104f;
        private const float ZrBadgeH = 26f;
        private const float ZrBadgeGap = 4f;
        private const float ZrBadgeX = MatchResultOverlay.NameCellX + ZrNameW + 8f;

        [MenuItem("Tools/VortexArena/UI/Girdap/Yalnız Zombi sonuç ekranı")]
        public static void BuildZombieResultMenu()
        {
            BuildAssets();
            BuildZombieResult();
            AssetDatabase.SaveAssets();
        }

        public static void BuildZombieResult()
        {
            EnsureFolder(ZbUiDir);

            GameObject root = NewRoot(ZbResultName, ZrW, ZrH);
            try
            {
                // Shared with the diner screen on purpose: the chrome rules (Canvas sorting copied by
                // hand, MatchResultOverlay skipped) must not exist twice.
                LkAdoptResultChrome(root);
                var overlay = root.AddComponent<MatchResultOverlay>();

                RectTransform resultPanel = Node(root.transform, "ResultPanel");
                Stretch(resultPanel);
                RectTransform boardPanel = Node(root.transform, "ScoreboardPanel");
                Stretch(boardPanel);

                var wiring = new SerializedObject(overlay);
                ResultSet(wiring, "resultPanel", resultPanel.gameObject);
                ResultSet(wiring, "scoreboardPanel", boardPanel.gameObject);

                ZrBuildCard(resultPanel, wiring);
                ZrBuildBoard(boardPanel, wiring);
                ZrBindSkin(wiring);
                wiring.ApplyModifiedPropertiesWithoutUndo();

                boardPanel.gameObject.SetActive(false);

                // Written over the existing file: deleting it first would mint a new GUID and cut
                // ZOMBIE.asset's link.
                PrefabUtility.SaveAsPrefabAsset(root, ZbResultPrefab);
            }
            finally
            {
                UiObject.DestroyImmediate(root);
            }

            ZrVerifyModeLink();
        }

        // ------------------------------------------------------------------ result card

        private static void ZrBuildCard(RectTransform panel, SerializedObject wiring)
        {
            ZrCardShell(panel);

            TextMeshProUGUI title = Text(panel, "Title", "DAYANDINIZ", GirdapFont.RubikWetPaint,
                110f, Kan.BoneWarm, TextAlignmentOptions.Center);
            PlaceTopCenter(title.rectTransform, ZrTitleY, ZrW - 120f, ZrTitleH);
            TextShadow(title, Kan.BloodShadow, 0.05f);

            ZrCaption(panel, "WaveLabel", "ULAŞILAN DALGA", ZrWaveLabelY);
            TextMeshProUGUI wave = Text(panel, "Wave", "7", GirdapFont.RubikWetPaint, 170f,
                Kan.BloodHi, TextAlignmentOptions.Center);
            PlaceTopCenter(wave.rectTransform, ZrWaveY, ZrW - 240f, ZrWaveH);
            TextShadow(wave, Kan.BloodShadow, 0.04f);

            ZrCaption(panel, "ScoreLabel", "EKİP SKORU", ZrScoreLabelY);
            TextMeshProUGUI score = Text(panel, "Score", "1240", GirdapFont.RubikWetPaint, 120f,
                Kan.BoneBright, TextAlignmentOptions.Center);
            PlaceTopCenter(score.rectTransform, ZrScoreY, ZrW - 240f, ZrScoreH);

            float total = ZrPillW * 2f + ZrPillGap;
            float x = (ZrW - total) * 0.5f;
            TextMeshProUGUI self = ZrPill(panel, "Self", "PUANIN", "180", x);
            TextMeshProUGUI rank = ZrPill(panel, "Rank", "SIRA", "1.", x + ZrPillW + ZrPillGap);

            ResultSet(wiring, "resultTitleText", title);
            ResultSet(wiring, "coopWaveText", wave);
            ResultSet(wiring, "coopSharedScoreText", score);
            ResultSet(wiring, "coopSelfScoreText", self);
            ResultSet(wiring, "coopRankText", rank);

            // Said by the big numbers above; an unbound target is simply not drawn.
            ResultSet(wiring, "resultWinnerText", null);
            ResultSet(wiring, "resultScoreText", null);
            ResultSet(wiring, "resultScorebug", null);
            ResultSet(wiring, "coopHappyText", null);
            ResultSet(wiring, "coopUnhappyText", null);
        }

        /// <summary>Rusted plate with a blood outline; both panels wear the same shell.</summary>
        private static void ZrCardShell(RectTransform panel)
        {
            UiShape plate = Shape(panel, "Card");
            Stretch(plate.rectTransform);
            plate.Radius(Kan.PlateRadius * 2f)
                .Outline(Kan.Border1 + 1f, Kan.Border)
                .Fill(Kan.PlateA, Kan.PlateB, UiGradientMode.Vertical)
                .InnerGlow(60f, Kan.InnerBlood)
                .Glow(30f, Kan.Drop, new Vector2(0f, -10f));

            UiShape rule = Shape(panel, "Rule");
            Place(rule.rectTransform, ZrPad, ZrTitleY + ZrTitleH + 6f, ZrW - 2f * ZrPad, 3f);
            rule.Fill(Kan.Divider).Antialias(false);
        }

        private static void ZrCaption(Transform parent, string name, string text, float y)
        {
            TextMeshProUGUI label = Text(parent, name, text, GirdapFont.SairaBold, 24f, Kan.Label,
                TextAlignmentOptions.Center, 0.18f);
            PlaceTopCenter(label.rectTransform, y, ZrW - 240f, ZrLabelH);
        }

        /// <summary>Caption over a number on a rusted pill; returns the NUMBER.</summary>
        private static TextMeshProUGUI ZrPill(Transform parent, string name, string caption,
            string sample, float x)
        {
            RectTransform root = Node(parent, name);
            Place(root, x, ZrPillsY, ZrPillW, ZrPillsH);

            UiShape plate = Shape(root, "Plate");
            Stretch(plate.rectTransform);
            plate.Radius(Kan.ClockRadius)
                .Outline(Kan.Border1, Kan.Border)
                .Fill(Kan.ClockA, Kan.ClockB, UiGradientMode.Vertical);

            TextMeshProUGUI label = Text(root, "Label", caption, GirdapFont.SairaBold, 18f,
                Kan.Label, TextAlignmentOptions.Center, 0.16f);
            Place(label.rectTransform, 0f, 10f, ZrPillW, 24f);

            TextMeshProUGUI value = Text(root, "Value", sample, GirdapFont.SairaExtraBold, 44f,
                Kan.Bone, TextAlignmentOptions.Center);
            Place(value.rectTransform, 0f, 34f, ZrPillW, ZrPillsH - 38f);
            return value;
        }

        // ------------------------------------------------------------------- scoreboard

        private static void ZrBuildBoard(RectTransform panel, SerializedObject wiring)
        {
            ZrCardShell(panel);
            ZrBoardHead(panel, wiring);
            ZrBoardBody(panel, wiring);
            ZrBoardFoot(panel, wiring);
        }

        /// <summary>Head: title left, DALGA and EKİP SKORU pills right.</summary>
        private static void ZrBoardHead(RectTransform panel, SerializedObject wiring)
        {
            TextMeshProUGUI title = Text(panel, "Title", "EKİP RAPORU", GirdapFont.RubikWetPaint,
                48f, Kan.BoneWarm, TextAlignmentOptions.MidlineLeft);
            Place(title.rectTransform, ZrPad, ZrHeadY, 600f, ZrHeadH);
            TextShadow(title, Kan.BloodShadow, 0.05f);

            const float pillW = 240f;
            const float gap = 16f;
            float x = ZrW - ZrPad - (pillW * 2f + gap);

            TextMeshProUGUI wave = ZrHeadPill(panel, "Wave", "DALGA", "7", x, pillW);
            TextMeshProUGUI shared = ZrHeadPill(panel, "Shared", "EKİP SKORU", "1240",
                x + pillW + gap, pillW);

            ResultSet(wiring, "boardWaveText", wave);
            ResultSet(wiring, "boardSharedScoreText", shared);
            ResultSet(wiring, "boardHeadlineText", null);
            ResultSet(wiring, "boardScorebug", null);
            ResultSet(wiring, "boardHappyText", null);
            ResultSet(wiring, "boardUnhappyText", null);
        }

        private static TextMeshProUGUI ZrHeadPill(Transform parent, string name, string caption,
            string sample, float x, float w)
        {
            RectTransform root = Node(parent, name);
            Place(root, x, ZrHeadY, w, ZrHeadH);

            UiShape plate = Shape(root, "Plate");
            Stretch(plate.rectTransform);
            plate.Radius(Kan.ClockRadius)
                .Outline(Kan.Border1, Kan.Border)
                .Fill(Kan.ClockA, Kan.ClockB, UiGradientMode.Vertical);

            TextMeshProUGUI label = Text(root, "Label", caption, GirdapFont.SairaBold, 16f,
                Kan.Label, TextAlignmentOptions.MidlineLeft, 0.16f);
            PlaceMiddleLeft(label.rectTransform, 16f, w - 100f, ZrHeadH);

            TextMeshProUGUI value = Text(root, "Value", sample, GirdapFont.SairaExtraBold, 34f,
                Kan.Bone, TextAlignmentOptions.MidlineRight);
            PlaceMiddleLeft(value.rectTransform, w - 96f, 80f, ZrHeadH);
            return value;
        }

        /// <summary>Two STACKED full-width blocks (the ranking's halves); the row template lives in
        /// the upper one.</summary>
        private static void ZrBoardBody(RectTransform panel, SerializedObject wiring)
        {
            var blocks = new UiObject[MatchResultOverlay.BlockCount];
            var titles = new UiObject[MatchResultOverlay.BlockCount];
            var scoreHeaders = new UiObject[MatchResultOverlay.BlockCount];
            var rowParents = new UiObject[MatchResultOverlay.BlockCount];
            var combatCells = new UiObject[MatchResultOverlay.BlockCount * 4];
            GameObject template = null;

            for (int b = 0; b < MatchResultOverlay.BlockCount; b++)
            {
                RectTransform block = Node(panel, "Block" + b);
                Place(block, ZrPad, ZrBlock0Y + b * (ZrBlockH + ZrBlockGapY), ZrBlockW, ZrBlockH);

                TextMeshProUGUI who = ZrHeader(block, "ThWho", "OYUNCU",
                    MatchResultOverlay.NameCellX, ZrNameW, TextAlignmentOptions.MidlineLeft);

                TextMeshProUGUI points = ZrHeader(block, "ThScore", "PUAN", ZrCellX(0), ZrCellW,
                    TextAlignmentOptions.MidlineRight);
                TextMeshProUGUI kills = ZrHeader(block, "ThKills", "ÖLDÜRME", ZrCellX(1), ZrCellW,
                    TextAlignmentOptions.MidlineRight);
                TextMeshProUGUI head = ZrHeader(block, "ThHeadshots", "KAFADAN", ZrCellX(2),
                    ZrCellW, TextAlignmentOptions.MidlineRight);
                TextMeshProUGUI revives = ZrHeader(block, "ThRevives", "KALDIRMA", ZrCellX(3),
                    ZrCellW, TextAlignmentOptions.MidlineRight);
                TextMeshProUGUI downs = ZrHeader(block, "ThDowns", "DÜŞME", ZrCellX(4), ZrCellW,
                    TextAlignmentOptions.MidlineRight);

                RectTransform rows = Node(block, "Rows");
                Place(rows, 0f, ZrRowsTop, ZrBlockW, ZrBlockH - ZrRowsTop);
                if (b == 0)
                {
                    template = ZrRowTemplate(rows);
                }

                blocks[b] = block.gameObject;

                // Only the LEFT block's title is rewritten at runtime (soloBlockTitle); the lower
                // block keeps the header the builder wrote, so both halves read the same.
                titles[b] = b == 0 ? who : null;
                scoreHeaders[b] = points;
                rowParents[b] = rows;

                // The four cells that are dropped when the mode publishes no per-player tallies.
                combatCells[b * 4] = kills.gameObject;
                combatCells[b * 4 + 1] = downs.gameObject;
                combatCells[b * 4 + 2] = head.gameObject;
                combatCells[b * 4 + 3] = revives.gameObject;
            }

            ResultSetArray(wiring, "boardBlocks", blocks);
            ResultSetArray(wiring, "boardBlockPlates", new UiObject[MatchResultOverlay.BlockCount]);
            ResultSetArray(wiring, "boardBlockTitles", titles);
            ResultSetArray(wiring, "boardBlockScoreHeaders", scoreHeaders);
            ResultSetArray(wiring, "boardBlockCombatCells", combatCells);
            ResultSetArray(wiring, "boardRowParents", rowParents);
            ResultSet(wiring, "boardRowTemplate", template);
            ResultSet(wiring, "boardTeamSummaryText", null);
            ResultSet(wiring, "boardTeamSummary2Text", null);

            // Legacy column table has no counterpart in this skin; emptied so the runtime's
            // visibility pass has nothing to touch.
            ResultSetArray(wiring, "boardColumns", new UiObject[0]);
            ResultSetArray(wiring, "boardColumnRoots", new UiObject[0]);
            ResultSetArray(wiring, "boardColumnHeaders", new UiObject[0]);
        }

        private static float ZrCellX(int index) => ZrCellsX + index * (ZrCellW + ZrCellGap);

        private static TextMeshProUGUI ZrHeader(Transform parent, string name, string text, float x,
            float w, TextAlignmentOptions align)
        {
            TextMeshProUGUI label = Text(parent, name, text, GirdapFont.SairaBold, 16f, Kan.Label,
                align, 0.14f);
            Place(label.rectTransform, x, 0f, w, ZrThRowH);
            return label;
        }

        /// <summary>Row template, cloned per player at runtime.</summary>
        private static GameObject ZrRowTemplate(Transform rows)
        {
            RectTransform row = Node(rows, "Row");
            Place(row, 0f, 0f, ZrBlockW, ZrRowH);
            row.gameObject.AddComponent<CanvasGroup>(); // a departed player's row dims as a whole

            UiShape bg = Shape(row, "Bg");
            Stretch(bg.rectTransform);
            bg.Radius(Kan.ClockRadius).Outline(2f, Kan.Border).Fill(Kan.ClockA, Kan.ClockB,
                UiGradientMode.Horizontal);

            TextMeshProUGUI rank = Text(row, "Rank", "1", GirdapFont.SairaExtraBold, 24f, Kan.Label,
                TextAlignmentOptions.Center);
            Place(rank.rectTransform, 8f, 0f, 40f, ZrRowH);

            TextMeshProUGUI name = Text(row, "Name", "Oyuncu", GirdapFont.SairaExtraBold, 28f,
                Kan.Bone, TextAlignmentOptions.MidlineLeft);
            Place(name.rectTransform, MatchResultOverlay.NameCellX, 0f, ZrNameW, ZrRowH);

            // "SEN" badge; the runtime places it after the measured name width.
            RectTransform tag = Node(row, "Tag");
            UiShape tagBg = Shape(tag, "Bg");
            Stretch(tagBg.rectTransform);
            tagBg.Radius(13f).Fill(Kan.Blood, Kan.BloodLo, UiGradientMode.Vertical);
            TextMeshProUGUI tagLabel = Text(tag, "Label", "SEN", GirdapFont.SairaBold, 14f,
                Kan.BoneWarm, TextAlignmentOptions.Center, 0.12f);
            Stretch(tagLabel.rectTransform);
            float tagW = Mathf.Ceil(tagLabel.GetPreferredValues(tagLabel.text).x) + 18f;
            Place(tag, MatchResultOverlay.NameCellX, (ZrRowH - 26f) * 0.5f, tagW, 26f);

            TextMeshProUGUI small = Text(row, "Small", "#12", GirdapFont.SairaBold, 15f, Kan.Label,
                TextAlignmentOptions.MidlineLeft);
            Place(small.rectTransform, MatchResultOverlay.NameCellX, 0f, 160f, ZrRowH);

            for (int i = 0; i < MatchResultOverlay.BadgeCount; i++)
            {
                ZrBadge(row, i);
            }

            ZrValueCell(row, "Score", 0, GirdapFont.SairaExtraBold, 30f, Kan.BoneWarm);
            ZrValueCell(row, "Kills", 1, GirdapFont.SairaBold, 26f, Kan.Bone);
            ZrValueCell(row, "Headshots", 2, GirdapFont.SairaBold, 26f, Kan.Bone);
            ZrValueCell(row, "Revives", 3, GirdapFont.SairaBold, 26f, Kan.Bone);
            ZrValueCell(row, "Deaths", 4, GirdapFont.SairaBold, 26f, Kan.Bone);

            row.gameObject.SetActive(false); // an active template draws an empty row under the table
            return row.gameObject;
        }

        private static void ZrValueCell(Transform row, string name, int index, GirdapFont font,
            float size, Color color)
        {
            TextMeshProUGUI value = Text(row, name, "0", font, size, color,
                TextAlignmentOptions.MidlineRight);
            Place(value.rectTransform, ZrCellX(index), 0f, ZrCellW, ZrRowH);
        }

        /// <summary>Badge slot (<c>Badge0..2</c>) with its <c>Label</c> child; inactive until the
        /// runtime hands it a category.</summary>
        private static void ZrBadge(Transform row, int index)
        {
            RectTransform badge = Node(row, "Badge" + index);
            Place(badge, ZrBadgeX + index * (ZrBadgeW + ZrBadgeGap), (ZrRowH - ZrBadgeH) * 0.5f,
                ZrBadgeW, ZrBadgeH);

            UiShape bg = Shape(badge, "Bg");
            Stretch(bg.rectTransform);
            bg.Radius(ZrBadgeH * 0.5f).Outline(2f, Kan.BloodHi).Fill(Kan.BloodLo);

            TextMeshProUGUI label = Text(badge, "Label", "Nişancı", GirdapFont.SairaBold, 13f,
                Kan.BoneWarm, TextAlignmentOptions.Center, 0.06f);
            Stretch(label.rectTransform, 6f, 0f, 6f, 0f);

            badge.gameObject.SetActive(false);
        }

        /// <summary>Footer: "SEN" plate + five stat boxes (PUAN · SIRA · KAFADAN · KALDIRMA · EKİP
        /// SKORU). Labels are static; the runtime only writes the values.</summary>
        private static void ZrBoardFoot(RectTransform panel, SerializedObject wiring)
        {
            RectTransform foot = Node(panel, "Foot");
            Place(foot, ZrPad, ZrFootY, ZrBlockW, ZrFootH);

            UiShape plate = Shape(foot, "Plate");
            Stretch(plate.rectTransform);
            plate.Radius(Kan.ClockRadius)
                .Outline(Kan.Border1, Kan.Border)
                .Fill(Kan.PlateA, Kan.PlateB, UiGradientMode.Vertical);

            UiShape who = Shape(foot, "Who");
            Place(who.rectTransform, 0f, 0f, ZrFootWhoW, ZrFootH);
            who.Radius(Kan.ClockRadius, 0f, 0f, Kan.ClockRadius)
                .Fill(Kan.Blood, Kan.BloodLo, UiGradientMode.Vertical);
            TextMeshProUGUI whoLabel = Text(who.transform, "Label", "SEN",
                GirdapFont.RubikWetPaint, 40f, Kan.BoneWarm, TextAlignmentOptions.Center);
            Stretch(whoLabel.rectTransform);

            RectTransform stats = Node(foot, "Stats");
            Place(stats, ZrFootWhoW, 0f, ZrBlockW - ZrFootWhoW, ZrFootH);

            string[] labels = { "PUAN", "SIRA", "KAFADAN", "KALDIRMA", "EKİP SKORU" };
            string[] samples = { "180", "1.", "9", "2", "1240" };
            var values = new UiObject[labels.Length];
            float cellW = (ZrBlockW - ZrFootWhoW) / labels.Length;

            for (int i = 0; i < labels.Length; i++)
            {
                RectTransform cell = Node(stats, "Stat" + i);
                Place(cell, i * cellW, 0f, cellW, ZrFootH);

                TextMeshProUGUI caption = Text(cell, "Label", labels[i], GirdapFont.SairaBold, 15f,
                    Kan.Label, TextAlignmentOptions.Center, 0.2f);
                Place(caption.rectTransform, 0f, 16f, cellW, 18f);

                TextMeshProUGUI value = Text(cell, "Value", samples[i], GirdapFont.SairaExtraBold,
                    40f, Kan.Bone, TextAlignmentOptions.Center);
                Place(value.rectTransform, 0f, 38f, cellW, 46f);
                values[i] = value;
            }

            ResultSet(wiring, "footStatsGroup", stats.gameObject);
            ResultSetArray(wiring, "footStatValues", values);
            ResultSet(wiring, "boardSelfScoreText", values[0]);
            ResultSet(wiring, "boardRankText", values[1]);
            ResultSet(wiring, "boardMatchSummaryText", null);
            ResultSet(wiring, "footWhoPlate", null); // no teams: the team gradient has nothing to say
        }

        // ------------------------------------------------------------------ skin + words

        private static void ZrBindSkin(SerializedObject wiring)
        {
            HudSetFloat(wiring, "rowHeight", ZrRowH);
            HudSetFloat(wiring, "rowGap", ZrRowGap);
            HudSetFloat(wiring, "rowsTop", ZrRowsTop);

            ResultSetColor(wiring, "rowFillA", Kan.ClockA);
            ResultSetColor(wiring, "rowFillB", Kan.ClockB);
            ResultSetColor(wiring, "rowOutline", Kan.Border);
            HudSetFloat(wiring, "rowOutlineWidth", 2f);
            ResultSetColor(wiring, "rowTextColor", Kan.Bone);
            ResultSetColor(wiring, "rowSmallColor", Kan.Label);
            ResultSetColor(wiring, "rowRankColor", Kan.Label);

            ResultSetColor(wiring, "ownRowFillA", Kan.Blood);
            ResultSetColor(wiring, "ownRowFillB", Kan.BloodLo);
            ResultSetColor(wiring, "ownRowOutline", Kan.BloodHi);
            HudSetFloat(wiring, "ownRowOutlineWidth", 3f);
            ResultSetColor(wiring, "ownRowGlow", Kan.InnerBlood);
            HudSetFloat(wiring, "ownRowGlowWidth", 30f);
            ResultSetColor(wiring, "ownRowTextColor", Kan.BoneWarm);
            ResultSetColor(wiring, "ownRowSmallColor", Kan.BoneWarm);
            ResultSetColor(wiring, "ownRowRankColor", Kan.BoneWarm);

            HudSetFloat(wiring, "leftRowAlpha", 0.5f);

            // The title IS the outcome word here, so it carries the won/lost tone.
            LkSetBool(wiring, "tintTitleByOutcome", true);

            LkSetString(wiring, "coopTitle", "OYUN BİTTİ");
            LkSetString(wiring, "runSurvivedTitle", "DAYANDINIZ");
            LkSetString(wiring, "runRescuedTitle", "KURTULDUNUZ");
            LkSetString(wiring, "runLostTitle", "YENİLDİNİZ");
            LkSetString(wiring, "coopScoreHeader", "PUAN");
            LkSetString(wiring, "soloBlockTitle", "OYUNCU");
            LkSetString(wiring, "coopBlockSummaryFormat", "");
            LkSetString(wiring, "badgeTopKillerLabel", "En çok öldüren");
            LkSetString(wiring, "badgeMarksmanLabel", "Nişancı");
            LkSetString(wiring, "badgeRescuerLabel", "Kurtarıcı");

            ResultSetColor(wiring, "coopColor", Kan.BloodHi);
            ResultSetColor(wiring, "wonColor", Kan.BoneWarm);
            ResultSetColor(wiring, "lostColor", Kan.BloodHi);
        }

        // ------------------------------------------------------------------- mode link

        /// <summary>
        /// Re-points every Zombie mode definition at the rebuilt prefab. ⚠️ The GUID survives an
        /// overwrite but the ROOT's local fileID does not, so a reference saved against the old root
        /// would resolve to nothing — and a missing result screen is silent (the generic one opens).
        /// </summary>
        private static void ZrVerifyModeLink()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ZbResultPrefab);
            if (prefab == null)
            {
                Debug.LogError($"[Kan] {ZbResultPrefab} kaydedilemedi.");
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:ScriptableObject",
                new[] { "Assets/Modes/Zombie" });
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

            Debug.Log("[Kan] Zombi sonuç ekranı üretildi: " + ZbResultName
                      + "/{ResultPanel/{Card, Rule, Title, WaveLabel, Wave, ScoreLabel, Score,"
                      + " Self, Rank}, ScoreboardPanel/{Card, Title, Wave, Shared,"
                      + " Block0/{ThWho,ThScore,ThKills,ThHeadshots,ThRevives,ThDowns,Rows/Row},"
                      + " Block1/{…}, Foot/{Who,Stats/Stat0..4}}}"
                      + $" · kart {ZrW}x{ZrH} · satır {ZrRowH}+{ZrRowGap} (üst {ZrRowsTop})"
                      + " · satır hücreleri: Bg, Rank, Name, Tag, Small, Score, Kills, Headshots,"
                      + " Revives, Deaths, Badge0..2"
                      + " · bağlanan: resultPanel, scoreboardPanel, resultTitleText, coopWaveText,"
                      + " coopSharedScoreText, coopSelfScoreText, coopRankText, boardWaveText,"
                      + " boardSharedScoreText, boardBlocks, boardBlockTitles[0],"
                      + " boardBlockScoreHeaders, boardBlockCombatCells[8], boardRowParents,"
                      + " boardRowTemplate, footStatsGroup, footStatValues[5], boardSelfScoreText,"
                      + " boardRankText · deri: satır/kendi satırı renkleri, sonuç kelimeleri,"
                      + " rozet adları · " + state);
        }
    }
}
