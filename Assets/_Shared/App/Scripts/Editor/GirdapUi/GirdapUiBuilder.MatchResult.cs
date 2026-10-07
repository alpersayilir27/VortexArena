using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using VortexArena.App;
using VortexArena.Core.UI;

// Factory methods share element names (Image / Text). The alias is required because
// `Image x = Image(...)` gives CS0119 — simple name lookup finds the member group BEFORE the type.
using UiImage = UnityEngine.UI.Image;

namespace VortexArena.App.Editor
{
    /// <summary>
    /// Player's match end screen (<c>Docs/Gelistirici/Arayuz/mac-sonu.html</c>): result card
    /// (<c>.rcard</c> + <c>.slab</c>) and scoreboard (<c>.sc-*</c>).
    /// <para>
    /// ⚠️ <b>Edited IN PLACE.</b> Two mode variants (kids' modes) inherit this prefab, so nodes they
    /// override (<c>Title</c>, <c>Header0..5</c>, <c>Column0..5</c>, <c>Headline</c>,
    /// <c>StatsPanel</c>, <c>MatchSummary</c>) are re-parented and restyled, never deleted —
    /// a deleted node turns the variant's override into a dangling one and its art disappears.
    /// </para>
    /// </summary>
    public static partial class GirdapUiBuilder
    {
        private const string ResultPrefab = "MatchResultOverlay";

        // --- .rcard: 1400x860 at (260,110), --c: 30px, 1 px gradient border
        private const float ResultCardW = 1400f;
        private const float ResultCardH = 860f;
        private const float ResultChamfer = 30f;

        // Inside Fill (the card inset by its 1 px border).
        private const float ResultFillW = ResultCardW - 2f;
        private const float ResultFillH = ResultCardH - 2f;

        private const float ResultSlabH = 429f; // .slab 430 − border
        private const float ResultHeadH = 104f; // .sc-head
        private const float ResultFootH = 116f; // .sc-foot
        private const float ResultFootGap = 16f; // .sc-foot margin-top

        // .sc-body: padding 0 32, two 1fr blocks with a 24 px gap
        private const float ResultBodyPad = 32f;
        private const float ResultBlockGap = 24f;
        private const float ResultBlockW = (ResultFillW - 2f * ResultBodyPad - ResultBlockGap) / 2f;
        private const float ResultBlockH = ResultFillH - ResultHeadH - ResultFootH - ResultFootGap;
        private const float ResultHeadPlateH = 64f; // .sc-th

        // .sc-th / .sc-row grid: 56px 1fr 88px 68px 68px 96px
        private const float ResultCellRank = 56f;
        private const float ResultCellScore = 88f;
        private const float ResultCellKill = 68f;
        private const float ResultCellKd = 96f;

        private const float ResultCellNameW = ResultBlockW - ResultCellRank - ResultCellScore -
                                              2f * ResultCellKill - ResultCellKd;

        private const float ResultColScoreX = ResultCellRank + ResultCellNameW;
        private const float ResultColKillsX = ResultColScoreX + ResultCellScore;
        private const float ResultColDeathsX = ResultColKillsX + ResultCellKill;
        private const float ResultColKdX = ResultColDeathsX + ResultCellKill;

        private static readonly Color ResultBdA = Girdap.Rgba(125, 227, 255, 0.55f);
        private static readonly Color ResultBdB = Girdap.Rgba(170, 205, 255, 0.14f);
        private static readonly Color ResultBgA = Girdap.Hex(0x111A31);
        private static readonly Color ResultBgB = Girdap.Hex(0x080E1D);
        private static readonly Color ResultDots = new Color(1f, 1f, 1f, 0.045f);
        private static readonly Color ResultRowA = Girdap.Hex(0xAACDFF, 0.095f);
        private static readonly Color ResultRowB = Girdap.Hex(0xAACDFF, 0.03f);
        private static readonly Color ResultRowLine = Girdap.Hex(0xAACDFF, 0.08f);
        private static readonly Color ResultFootA = Girdap.Hex(0xAACDFF, 0.09f);
        private static readonly Color ResultFootB = Girdap.Hex(0xAACDFF, 0.03f);
        private static readonly Color ResultSlabA = Girdap.Hex(0x0C1828);
        private static readonly Color ResultSlabB = Girdap.Hex(0x0A1422);

        /// <summary>Nodes the two mode variants override — never destroyed, only re-parented.</summary>
        private static readonly HashSet<string> ResultKeptNodes = new HashSet<string>
        {
            "ResultPanel", "Card", "Fill", "Title", "TitleText", "WinnerText", "ScoreText",
            "ScoreboardPanel", "StatsPanel", "Headline", "TeamSummary", "Divider", "MatchSummary",
            "Header0", "Header1", "Header2", "Header3", "Header4", "Header5",
            "IconKills", "IconDeaths",
            "Column0", "Column1", "Column2", "Column3", "Column4", "Column5"
        };

        [MenuItem("Tools/VortexArena/UI/Girdap/Yalnız MatchResult")]
        public static void BuildMatchResultOnly()
        {
            BuildAssets();
            BuildMatchResult();
            AssetDatabase.SaveAssets();
        }

        static partial void BuildMatchResult()
        {
            GameObject root = LoadPrefabContents(ResultPrefab);
            if (root == null)
            {
                root = NewRoot(ResultPrefab, ResultCardW, ResultCardH);
                root.AddComponent<Canvas>();
                root.AddComponent<UnityEngine.UI.CanvasScaler>();
                root.AddComponent<MatchResultOverlay>();
                Debug.LogWarning("[Girdap] MatchResultOverlay.prefab yoktu — HudFollow elle eklenmeli.");
            }

            var overlay = root.GetComponent<MatchResultOverlay>();
            if (overlay == null)
            {
                overlay = root.AddComponent<MatchResultOverlay>();
            }

            // World-space VR canvas: the card IS the canvas, so every CSS px maps 1:1.
            ((RectTransform)root.transform).sizeDelta = new Vector2(ResultCardW, ResultCardH);
            ResultPrune(root.transform);

            RectTransform resultPanel = ResultClaim(root.transform, root.transform, "ResultPanel");
            Stretch(resultPanel);
            RectTransform boardPanel = ResultClaim(root.transform, root.transform, "ScoreboardPanel");
            Stretch(boardPanel);

            var wiring = new SerializedObject(overlay);
            ResultSet(wiring, "resultPanel", resultPanel.gameObject);
            ResultSet(wiring, "scoreboardPanel", boardPanel.gameObject);

            ResultBuildCard(resultPanel, wiring);
            ResultBuildBoard(boardPanel, wiring);

            // Field initializers do not re-run on an existing prefab: the old UiKit colors would
            // survive forever otherwise.
            ResultSetColor(wiring, "wonColor", Girdap.Good);
            ResultSetColor(wiring, "lostColor", Girdap.Bad);
            ResultSetColor(wiring, "drawColor", Girdap.Acc);
            ResultSetColor(wiring, "coopColor", Girdap.Acc);
            ResultSetColor(wiring, "redTeamColor", Girdap.RedInk);
            ResultSetColor(wiring, "blueTeamColor", Girdap.BlueInk);
            wiring.ApplyModifiedPropertiesWithoutUndo();

            boardPanel.gameObject.SetActive(false);
            SaveAndUnload(root, ResultPrefab);
        }

        // ------------------------------------------------------------- result card

        private static void ResultBuildCard(RectTransform panel, SerializedObject wiring)
        {
            Transform scope = panel;
            ResultSurfaceBg(panel, "Bg");

            RectTransform card = ResultClaimSurface(scope, panel, "Card");
            Stretch(card);
            UiImage dots = Image(card, "Dots", LoadSprite("Dots_16.png"), ResultDots,
                UiImage.Type.Tiled);
            Stretch(dots.rectTransform);

            RectTransform fill = ResultClaimSurface(scope, card, "Fill");
            Stretch(fill, 1f, 1f, 1f, 1f);

            // .slab — full width, 430 tall, tone-coloured glow + stripes + bottom rule
            RectTransform slab = Node(fill, "Slab");
            StretchTop(slab, 0f, 0f, 0f, ResultSlabH);

            UiShape slabBg = Shape(slab, "Bg");
            Stretch(slabBg.rectTransform);
            slabBg.Fill(ResultSlabA, ResultSlabB, UiGradientMode.Vertical);
            slabBg.ChamferTopLeft = ResultChamfer - 1f;

            Color glow = Girdap.Good;
            glow.a = 0.55f;
            UiImage slabGlow = Image(slab, "Glow", LoadSprite("Radial_256.png"), glow);
            // radial-gradient(ellipse 72% 95% at 50% 118%): radii in px, centre below the slab.
            PlaceBottomCenter(slabGlow.rectTransform,
                -(0.18f * ResultSlabH) - 0.95f * ResultSlabH, 2f * 0.72f * ResultFillW,
                2f * 0.95f * ResultSlabH);

            UiStripes slabStripes = Stripes(slab, "Stripes");
            Stretch(slabStripes.rectTransform);
            slabStripes.Stripes(-55f, 10f, 30f, new Color(1f, 1f, 1f, 0.04f)).Drift(4f); // CSS: animation drift 4s
            slabStripes.ChamferTopLeft = ResultChamfer - 1f;

            UiImage underline = Image(slab, "Underline", null, Girdap.Good);
            StretchBottom(underline.rectTransform, 0f, 0f, 0f, 3f);

            // .slab small — label flanked by two 150x2 rules
            Color labelColor = Girdap.Text;
            labelColor.a = 0.8f;
            TextMeshProUGUI title = ResultClaimLabel(scope, slab, "Title", "MAÇ SONUCU",
                GirdapFont.ChakraSemiBold, 24f, labelColor, TextAlignmentOptions.Center, 0.5f);
            float titleW = Mathf.Ceil(title.GetPreferredValues(title.text).x);
            ResultTopCenter(title.rectTransform, 0f, 66f, titleW, 24f);

            UiShape ruleLeft = Shape(slab, "RuleLeft");
            ResultTopCenter(ruleLeft.rectTransform, -(titleW * 0.5f + 24f + 75f), 77f, 150f, 2f);
            Color fade = Girdap.Good;
            fade.a = 0f;
            ruleLeft.Fill(fade, Girdap.Good, UiGradientMode.Horizontal).Antialias(false);

            UiShape ruleRight = Shape(slab, "RuleRight");
            ResultTopCenter(ruleRight.rectTransform, titleW * 0.5f + 24f + 75f, 77f, 150f, 2f);
            ruleRight.Fill(Girdap.Good, fade, UiGradientMode.Horizontal).Antialias(false);

            // .slab b — 286 px word, white → tone vertex gradient
            TextMeshProUGUI word = ResultClaimLabel(scope, slab, "TitleText", "KAZANDIN",
                GirdapFont.SairaExtraBold, 286f, Color.white, TextAlignmentOptions.Center, 0.01f);
            ResultTopCenter(word.rectTransform, 0f, 94f, ResultFillW - 60f, 269f);
            word.enableVertexGradient = true;
            word.colorGradient = new VertexGradient(Color.white, Color.white, Girdap.Good,
                Girdap.Good);
            // CSS `drop-shadow(0 0 44px rgba(tone, .55))`; the overlay recolours it per result.
            Color wordGlow = Girdap.Good;
            wordGlow.a = 0.55f;
            TextGlow(word, wordGlow);

            // .rlow — winner line + score plate, centred in the lower half
            RectTransform low = Node(fill, "RLow");
            StretchBottom(low, 0f, 0f, 0f, ResultFillH - ResultSlabH);

            TextMeshProUGUI winner = ResultClaimLabel(scope, low, "WinnerText",
                "<color=#FF857C>KIRMIZI</color> TAKIM KAZANDI", GirdapFont.ChakraBold, 44f,
                Girdap.Text, TextAlignmentOptions.Center, 0.14f, true);
            ResultTopCenter(winner.rectTransform, 0f, 109f, ResultFillW - 60f, 44f);

            RectTransform bug = ResultScoreBug(low, "Scorebug", 132f, 420f, 180f, 42f, 100f, 46f,
                40f, 0f, "KIRMIZI TAKIM", "MAVİ TAKIM", out TextMeshProUGUI red,
                out TextMeshProUGUI blue);
            ResultTopCenter(bug, 0f, 189f, bug.sizeDelta.x, 132f);

            TextMeshProUGUI scoreLine = ResultClaimLabel(scope, low, "ScoreText",
                "SENİN SKORUN 6", GirdapFont.ChakraBold, 44f, Girdap.Text,
                TextAlignmentOptions.Center, 0.07f, true);
            ResultTopCenter(scoreLine.rectTransform, 0f, 189f, ResultFillW - 60f, 132f);
            scoreLine.gameObject.SetActive(false); // the plate is the team-mode default

            ResultSet(wiring, "resultTitleText", word);
            ResultSet(wiring, "resultWinnerText", winner);
            ResultSet(wiring, "resultScoreText", scoreLine);
            ResultSet(wiring, "resultScorebug", bug.gameObject);
            ResultSet(wiring, "resultScoreRedText", red);
            ResultSet(wiring, "resultScoreBlueText", blue);
            ResultSet(wiring, "resultSlabGlow", slabGlow);
            ResultSet(wiring, "resultSlabUnderline", underline);
            ResultSet(wiring, "resultSlabRuleLeft", ruleLeft);
            ResultSet(wiring, "resultSlabRuleRight", ruleRight);
        }

        /// <summary>
        /// <c>.scorebug</c>: team plate · score · score · team plate, the outer bottom corners pulled
        /// in by <paramref name="slant"/>. Team plate widths are measured, not guessed — the CSS
        /// <c>md</c> variant sizes them from the label.
        /// </summary>
        private static RectTransform ResultScoreBug(Transform parent, string name, float h,
            float teamW, float scoreW, float teamSize, float scoreSize, float slant, float padOuter,
            float padInner, string redName, string blueName, out TextMeshProUGUI red,
            out TextMeshProUGUI blue)
        {
            RectTransform root = Node(parent, name);

            float redW = teamW;
            float blueW = teamW;

            TextMeshProUGUI redLabel = Text(root, "RedLabel", redName, GirdapFont.ChakraBold,
                teamSize, Color.white, TextAlignmentOptions.Center, 0.12f);
            TextMeshProUGUI blueLabel = Text(root, "BlueLabel", blueName, GirdapFont.ChakraBold,
                teamSize, Color.white, TextAlignmentOptions.Center, 0.12f);

            if (teamW <= 0f)
            {
                // Auto width (.scorebug.md): label + outer pad + inner pad.
                redW = Mathf.Ceil(redLabel.GetPreferredValues(redLabel.text).x) + padOuter + padInner;
                blueW = Mathf.Ceil(blueLabel.GetPreferredValues(blueLabel.text).x) + padOuter + padInner;
            }

            UiShape redPlate = Shape(root, "RedPlate");
            Place(redPlate.rectTransform, 0f, 0f, redW, h);
            redPlate.Fill(Girdap.RedHi, Girdap.RedLo, UiGradientMode.Vertical);
            redPlate.SlantLeft = slant;

            UiStripes redStripes = Stripes(redPlate.transform, "Stripes");
            Stretch(redStripes.rectTransform);
            redStripes.Stripes(-55f, 6f, 17f, new Color(1f, 1f, 1f, 0.075f)).Drift(2.4f);
            redStripes.SlantLeft = slant;

            UiShape redScore = Shape(root, "RedScore");
            Place(redScore.rectTransform, redW, 0f, scoreW, h);
            redScore.Fill(Girdap.ScoreA, Girdap.ScoreB, UiGradientMode.Vertical);
            UiImage redRule = Image(redScore.transform, "Rule", null, Girdap.Red);
            StretchBottom(redRule.rectTransform, 0f, 0f, 0f, 4f);

            UiShape blueScore = Shape(root, "BlueScore");
            Place(blueScore.rectTransform, redW + scoreW, 0f, scoreW, h);
            blueScore.Fill(Girdap.ScoreA, Girdap.ScoreB, UiGradientMode.Vertical);
            UiImage blueRule = Image(blueScore.transform, "Rule", null, Girdap.Blue);
            StretchBottom(blueRule.rectTransform, 0f, 0f, 0f, 4f);

            UiShape bluePlate = Shape(root, "BluePlate");
            Place(bluePlate.rectTransform, redW + 2f * scoreW, 0f, blueW, h);
            bluePlate.Fill(Girdap.BlueHi, Girdap.BlueLo, UiGradientMode.Vertical);
            bluePlate.SlantRight = slant;

            UiStripes blueStripes = Stripes(bluePlate.transform, "Stripes");
            Stretch(blueStripes.rectTransform);
            blueStripes.Stripes(-55f, 6f, 17f, new Color(1f, 1f, 1f, 0.075f)).Drift(2.4f);
            blueStripes.SlantRight = slant;

            // Labels sit above the plates: claimed last so they are the top siblings.
            redLabel.transform.SetAsLastSibling();
            blueLabel.transform.SetAsLastSibling();
            Place(redLabel.rectTransform, padOuter, 0f, redW - padOuter - padInner, h);
            Place(blueLabel.rectTransform, redW + 2f * scoreW + padInner, 0f,
                blueW - padOuter - padInner, h);

            red = Text(root, "RedScoreText", "14", GirdapFont.ChakraBold, scoreSize, Color.white,
                TextAlignmentOptions.Center);
            Place(red.rectTransform, redW, 0f, scoreW, h);

            blue = Text(root, "BlueScoreText", "11", GirdapFont.ChakraBold, scoreSize, Color.white,
                TextAlignmentOptions.Center);
            Place(blue.rectTransform, redW + scoreW, 0f, scoreW, h);

            Place(root, 0f, 0f, redW + 2f * scoreW + blueW, h);
            return root;
        }

        // -------------------------------------------------------------- scoreboard

        private static void ResultBuildBoard(RectTransform panel, SerializedObject wiring)
        {
            Transform scope = panel;
            ResultSurfaceBg(panel, "Bg");

            RectTransform card = ResultClaimSurface(scope, panel, "StatsPanel");
            Stretch(card);
            UiImage dots = Image(card, "Dots", LoadSprite("Dots_16.png"), ResultDots,
                UiImage.Type.Tiled);
            Stretch(dots.rectTransform);

            RectTransform fill = ResultClaimSurface(scope, card, "Fill");
            Stretch(fill, 1f, 1f, 1f, 1f);

            // Legacy column table: kept, wired and hidden — a variant skinned around it only has to
            // clear boardRowTemplate, and the runtime restores the columns' visibility itself.
            var columns = new Object[6];
            for (int c = 0; c < 6; c++)
            {
                RectTransform col = ResultClaim(scope, fill, "Column" + c);
                col.gameObject.SetActive(false);
                columns[c] = col.GetComponent<TextMeshProUGUI>();
            }

            ResultSetArray(wiring, "boardColumns", columns);

            ResultBoardHead(scope, fill, wiring);
            ResultBoardBody(scope, fill, wiring);
            ResultBoardFoot(scope, fill, wiring);
        }

        /// <summary>.sc-head — accent mark + title on the left, winner line + score plate right.</summary>
        private static void ResultBoardHead(Transform scope, RectTransform fill,
            SerializedObject wiring)
        {
            // .sc-head h2::before — a 24x28 skewed plate of accent bars.
            UiStripes mark = Stripes(fill, "TitleMark");
            Place(mark.rectTransform, ResultBodyPad, (ResultHeadH - 28f) * 0.5f, 24f, 28f);
            mark.Stripes(90f, 5f, 9f, Girdap.Acc);
            mark.SlantLeft = 10f;
            mark.SlantRight = -10f;

            TextMeshProUGUI title = ResultClaimLabel(scope, fill, "Title", "SKOR TABLOSU",
                GirdapFont.ChakraBold, 38f, Girdap.Text, TextAlignmentOptions.MidlineLeft, 0.14f);
            Place(title.rectTransform, ResultBodyPad + 24f + 18f, 0f,
                Mathf.Ceil(title.GetPreferredValues(title.text).x), ResultHeadH);

            RectTransform bug = ResultScoreBug(fill, "Scorebug", ResultHeadPlateH, 0f, 88f, 24f, 44f,
                22f, 42f, 26f, "KIRMIZI", "MAVİ", out TextMeshProUGUI red,
                out TextMeshProUGUI blue);
            float bugW = bug.sizeDelta.x;
            PlaceRight(bug, ResultBodyPad, (ResultHeadH - ResultHeadPlateH) * 0.5f, bugW,
                ResultHeadPlateH);

            TextMeshProUGUI headline = ResultClaimLabel(scope, fill, "Headline",
                "<color=#FF857C>KIRMIZI</color> TAKIM KAZANDI", GirdapFont.ChakraBold, 22f,
                Girdap.Text, TextAlignmentOptions.MidlineRight, 0.14f, true);
            PlaceRight(headline.rectTransform, ResultBodyPad + bugW + 24f, 0f, 700f, ResultHeadH);

            ResultSet(wiring, "boardHeadlineText", headline);
            ResultSet(wiring, "boardScorebug", bug.gameObject);
            ResultSet(wiring, "boardScoreRedText", red);
            ResultSet(wiring, "boardScoreBlueText", blue);
        }

        /// <summary>.sc-body — one block per team; the left block reuses the overridable nodes.</summary>
        private static void ResultBoardBody(Transform scope, RectTransform fill,
            SerializedObject wiring)
        {
            var blocks = new Object[MatchResultOverlay.BlockCount];
            var plates = new Object[MatchResultOverlay.BlockCount];
            var titles = new Object[MatchResultOverlay.BlockCount];
            var scoreHeaders = new Object[MatchResultOverlay.BlockCount];
            var combat = new Object[MatchResultOverlay.BlockCount * 3];
            var rowParents = new Object[MatchResultOverlay.BlockCount];
            GameObject template = null;

            for (int b = 0; b < MatchResultOverlay.BlockCount; b++)
            {
                bool left = b == 0;
                RectTransform block = Node(fill, left ? "Block0" : "Block1");
                Place(block, ResultBodyPad + b * (ResultBlockW + ResultBlockGap), ResultHeadH,
                    ResultBlockW, ResultBlockH);

                UiShape plate = Shape(block, "Head");
                Place(plate.rectTransform, 0f, 0f, ResultBlockW, ResultHeadPlateH);
                plate.Fill(left ? Girdap.RedHi : Girdap.BlueHi, left ? Girdap.RedLo : Girdap.BlueLo,
                    UiGradientMode.Horizontal, 0.46f);
                plate.SlantRight = 18f;

                // Left block's header cells are the variant-overridden Header0..5 objects.
                TextMeshProUGUI name = ResultCell(scope, block, left, 0, "KIRMIZI TAKIM",
                    GirdapFont.ChakraBold, 25f, 0.12f);
                Place(name.rectTransform, 18f, 0f, ResultColScoreX - 18f, ResultHeadPlateH);
                if (!left)
                {
                    name.text = "MAVİ TAKIM";
                }

                // Header1 is spanned by the team name in CSS (grid-column 1/3): kept, never drawn.
                TextMeshProUGUI spanned = ResultCell(scope, block, left, 1, "",
                    GirdapFont.ChakraSemiBold, 15f, 0.14f);
                Place(spanned.rectTransform, ResultCellRank, 0f, ResultCellNameW, ResultHeadPlateH);
                spanned.gameObject.SetActive(false);

                TextMeshProUGUI score = ResultCell(scope, block, left, 2, "SKOR",
                    GirdapFont.ChakraSemiBold, 15f, 0.14f);
                Place(score.rectTransform, ResultColScoreX, 0f, ResultCellScore, ResultHeadPlateH);

                TextMeshProUGUI kills = ResultCell(scope, block, left, 3, "",
                    GirdapFont.ChakraSemiBold, 15f, 0.14f);
                Place(kills.rectTransform, ResultColKillsX, 0f, ResultCellKill, ResultHeadPlateH);
                ResultHeaderIcon(scope, kills.transform, left, "IconKills", "Target");

                TextMeshProUGUI deaths = ResultCell(scope, block, left, 4, "",
                    GirdapFont.ChakraSemiBold, 15f, 0.14f);
                Place(deaths.rectTransform, ResultColDeathsX, 0f, ResultCellKill, ResultHeadPlateH);
                ResultHeaderIcon(scope, deaths.transform, left, "IconDeaths", "Skull");

                TextMeshProUGUI kd = ResultCell(scope, block, left, 5, "K/D",
                    GirdapFont.ChakraSemiBold, 15f, 0.14f);
                Place(kd.rectTransform, ResultColKdX, 0f, ResultCellKd, ResultHeadPlateH);

                RectTransform rows = Node(block, "Rows");
                Place(rows, 0f, MatchResultOverlay.RowsTop, ResultBlockW,
                    ResultBlockH - MatchResultOverlay.RowsTop);
                if (left)
                {
                    template = ResultRowTemplate(rows);
                }

                TextMeshProUGUI sum = left
                    ? ResultClaimLabel(scope, block, "TeamSummary",
                        "4 oyuncu · 23 öldürme · 11 ölüm", GirdapFont.SairaSemiBold, 22f,
                        Girdap.Muted, TextAlignmentOptions.MidlineLeft)
                    : ResultLabel(block, "TeamSummaryB", "4 oyuncu · 19 öldürme · 17 ölüm",
                        GirdapFont.SairaSemiBold, 22f, Girdap.Muted,
                        TextAlignmentOptions.MidlineLeft);
                Place(sum.rectTransform, 4f, MatchResultOverlay.RowsTop + 4f, ResultBlockW - 8f, 26f);

                blocks[b] = block.gameObject;
                plates[b] = plate;
                titles[b] = name;
                scoreHeaders[b] = score;
                combat[b * 3] = kills.gameObject;
                combat[b * 3 + 1] = deaths.gameObject;
                combat[b * 3 + 2] = kd.gameObject;
                rowParents[b] = rows;

                if (left)
                {
                    ResultSetArray(wiring, "boardColumnHeaders", new Object[]
                    {
                        name, spanned, score, kills, deaths, kd
                    });
                    ResultSet(wiring, "boardTeamSummaryText", sum);
                }
                else
                {
                    ResultSet(wiring, "boardTeamSummary2Text", sum);
                }
            }

            ResultSetArray(wiring, "boardBlocks", blocks);
            ResultSetArray(wiring, "boardBlockPlates", plates);
            ResultSetArray(wiring, "boardBlockTitles", titles);
            ResultSetArray(wiring, "boardBlockScoreHeaders", scoreHeaders);
            ResultSetArray(wiring, "boardBlockCombatCells", combat);
            ResultSetArray(wiring, "boardRowParents", rowParents);
            ResultSet(wiring, "boardRowTemplate", template);
        }

        /// <summary>One header cell: <c>HeaderN</c> on the left block (kept for the variants), a fresh
        /// node on the right one.</summary>
        private static TextMeshProUGUI ResultCell(Transform scope, Transform parent, bool kept,
            int index, string text, GirdapFont font, float size, float spacing)
        {
            return kept
                ? ResultClaimLabel(scope, parent, "Header" + index, text, font, size, Color.white,
                    TextAlignmentOptions.MidlineLeft, spacing)
                : ResultLabel(parent, "HeaderB" + index, text, font, size, Color.white,
                    TextAlignmentOptions.MidlineLeft, spacing);
        }

        private static void ResultHeaderIcon(Transform scope, Transform cell, bool kept, string name,
            string icon)
        {
            UiImage image;
            if (kept)
            {
                RectTransform rt = ResultClaim(scope, cell, name);
                image = rt.GetComponent<UiImage>();
                if (image == null)
                {
                    image = rt.gameObject.AddComponent<UiImage>();
                }

                image.sprite = LoadIcon(icon);
                image.color = Color.white;
                image.raycastTarget = false;
                image.preserveAspect = true;
            }
            else
            {
                image = Icon(cell, name, icon, 22f, Color.white);
            }

            Place(image.rectTransform, 0f, (ResultHeadPlateH - 22f) * 0.5f, 22f, 22f);
        }

        /// <summary>
        /// .sc-row template, cloned per player at runtime. ⚠️ Inactive in the prefab: an active
        /// template would draw an empty row under every table.
        /// </summary>
        private static GameObject ResultRowTemplate(Transform rows)
        {
            RectTransform row = Node(rows, "Row");
            Place(row, 0f, 0f, ResultBlockW, MatchResultOverlay.RowHeight);
            row.gameObject.AddComponent<CanvasGroup>(); // .sc-row.left dims the whole row

            UiShape bg = Shape(row, "Bg");
            Stretch(bg.rectTransform);
            bg.Fill(ResultRowA, ResultRowB, UiGradientMode.Horizontal);

            UiImage line = Image(bg.transform, "Line", null, ResultRowLine);
            StretchBottom(line.rectTransform, 0f, 0f, 0f, 1f);

            float h = MatchResultOverlay.RowHeight;

            TextMeshProUGUI rank = ResultLabel(row, "Rank", "1", GirdapFont.ChakraBold, 22f,
                Girdap.Faint, TextAlignmentOptions.Center);
            Place(rank.rectTransform, 0f, 0f, ResultCellRank, h);

            TextMeshProUGUI name = ResultLabel(row, "Name", "Ayşe", GirdapFont.SairaBold, 40f,
                Girdap.Text, TextAlignmentOptions.MidlineLeft);
            Place(name.rectTransform, MatchResultOverlay.NameCellX, 0f, ResultCellNameW, h);

            // .tag — accent badge, placed after the name at runtime (width measured here).
            RectTransform tag = Node(row, "Tag");
            UiShape tagBg = Shape(tag, "Bg");
            Stretch(tagBg.rectTransform);
            tagBg.Chamfer(5f).Fill(Girdap.Acc);
            TextMeshProUGUI tagLabel = ResultLabel(tag, "Label", "SEN", GirdapFont.ChakraBold, 15f,
                Girdap.OnFg, TextAlignmentOptions.Center, 0.1f);
            Stretch(tagLabel.rectTransform);
            float tagW = Mathf.Ceil(tagLabel.GetPreferredValues(tagLabel.text).x) + 18f;
            Place(tag, MatchResultOverlay.NameCellX, (h - 28f) * 0.5f, tagW, 28f);

            TextMeshProUGUI small = ResultLabel(row, "Small", "#12", GirdapFont.ChakraSemiBold, 17f,
                Girdap.Faint, TextAlignmentOptions.MidlineLeft);
            Place(small.rectTransform, MatchResultOverlay.NameCellX, 0f, 240f, h);

            TextMeshProUGUI score = ResultLabel(row, "Score", "6", GirdapFont.ChakraBold, 32f,
                Girdap.Text, TextAlignmentOptions.MidlineLeft);
            Place(score.rectTransform, ResultColScoreX, 0f, ResultCellScore, h);

            TextMeshProUGUI kills = ResultLabel(row, "Kills", "6", GirdapFont.ChakraSemiBold, 32f,
                Girdap.Muted, TextAlignmentOptions.MidlineLeft);
            Place(kills.rectTransform, ResultColKillsX, 0f, ResultCellKill, h);

            TextMeshProUGUI deaths = ResultLabel(row, "Deaths", "2", GirdapFont.ChakraSemiBold, 32f,
                Girdap.Muted, TextAlignmentOptions.MidlineLeft);
            Place(deaths.rectTransform, ResultColDeathsX, 0f, ResultCellKill, h);

            TextMeshProUGUI kd = ResultLabel(row, "Kd", "3.00", GirdapFont.ChakraSemiBold, 32f,
                Girdap.Muted, TextAlignmentOptions.MidlineLeft);
            Place(kd.rectTransform, ResultColKdX, 0f, ResultCellKd, h);

            row.gameObject.SetActive(false);
            return row.gameObject;
        }

        /// <summary>.sc-foot — "SEN" team plate + four stat boxes; the single summary line is the
        /// fallback for the modes without combat stats.</summary>
        private static void ResultBoardFoot(Transform scope, RectTransform fill,
            SerializedObject wiring)
        {
            RectTransform divider = ResultClaim(scope, fill, "Divider");
            StretchBottom(divider, 0f, ResultFootH, 0f, 1f);
            UiImage dividerImage = divider.GetComponent<UiImage>();
            if (dividerImage == null)
            {
                dividerImage = divider.gameObject.AddComponent<UiImage>();
            }

            dividerImage.sprite = null;
            dividerImage.color = Girdap.Line;
            dividerImage.raycastTarget = false;

            RectTransform foot = Node(fill, "Foot");
            StretchBottom(foot, 0f, 0f, 0f, ResultFootH);

            UiShape bg = Shape(foot, "Bg");
            Stretch(bg.rectTransform);
            bg.Fill(ResultFootA, ResultFootB, UiGradientMode.Vertical);
            bg.ChamferBottomRight = ResultChamfer - 1f;

            UiShape who = Shape(foot, "Who");
            Place(who.rectTransform, 0f, 0f, 210f, ResultFootH);
            who.Fill(Girdap.RedHi, Girdap.RedLo, UiGradientMode.Vertical);
            who.SlantRight = 30f;

            TextMeshProUGUI whoLabel = ResultLabel(who.transform, "Label", "SEN",
                GirdapFont.ChakraBold, 44f, Color.white, TextAlignmentOptions.Center, 0.1f);
            Place(whoLabel.rectTransform, 0f, 0f, 210f - 22f, ResultFootH);

            RectTransform stats = Node(foot, "Stats");
            Stretch(stats, 210f, 0f, 0f, 0f);

            string[] labels = { "ÖLDÜRME", "ÖLÜM", "K/D", "SKOR" };
            string[] samples = { "6", "2", "3.00", "6" };
            var values = new Object[labels.Length];
            float cellW = (ResultFillW - 210f) / labels.Length;

            for (int i = 0; i < labels.Length; i++)
            {
                RectTransform cell = Node(stats, "Stat" + i);
                Place(cell, i * cellW, 0f, cellW, ResultFootH);

                TextMeshProUGUI caption = ResultLabel(cell, "Label", labels[i],
                    GirdapFont.ChakraSemiBold, 15f, Girdap.Muted, TextAlignmentOptions.Center, 0.2f);
                Place(caption.rectTransform, 0f, 21f, cellW, 15f);

                TextMeshProUGUI value = ResultLabel(cell, "Value", samples[i], GirdapFont.ChakraBold,
                    50f, Girdap.Text, TextAlignmentOptions.Center);
                Place(value.rectTransform, 0f, 44f, cellW, 50f);
                TextGlow(value, Girdap.Glow); // CSS `text-shadow: 0 0 26px var(--glow)`
                values[i] = value;
            }

            TextMeshProUGUI summary = ResultClaimLabel(scope, fill, "MatchSummary",
                "SKOR: 6", GirdapFont.ChakraBold, 40f, Girdap.Text, TextAlignmentOptions.Center,
                0.07f, true);
            PlaceBottomLeft(summary.rectTransform, 210f, 0f, ResultFillW - 210f, ResultFootH);
            summary.gameObject.SetActive(false);

            ResultSet(wiring, "footStatsGroup", stats.gameObject);
            ResultSetArray(wiring, "footStatValues", values);
            ResultSet(wiring, "footWhoPlate", who);
            ResultSet(wiring, "boardMatchSummaryText", summary);
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Card shell: chamfered 1 px gradient border + vertical fill. A SIBLING of the
        /// panel surface, not its child — a variant's own panel sprite must still draw on top.</summary>
        private static UiShape ResultSurfaceBg(Transform parent, string name)
        {
            UiShape shape = Shape(parent, name);
            Stretch(shape.rectTransform);
            shape.Chamfer(ResultChamfer)
                .Outline(1f, ResultBdA, ResultBdB)
                .Fill(ResultBgA, ResultBgB, UiGradientMode.Vertical);
            return shape;
        }

        /// <summary>Top-centre placement with a horizontal offset (CSS centred flex item).</summary>
        private static void ResultTopCenter(RectTransform rt, float cx, float y, float w, float h)
        {
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(cx, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        /// <summary>Destroys every node this builder generated last run; the variant-overridden ones
        /// (<see cref="ResultKeptNodes"/>) survive and are re-parented instead.</summary>
        private static void ResultPrune(Transform parent)
        {
            var children = new List<Transform>(parent.childCount);
            for (int i = 0; i < parent.childCount; i++)
            {
                children.Add(parent.GetChild(i));
            }

            for (int i = 0; i < children.Count; i++)
            {
                if (ResultKeptNodes.Contains(children[i].name))
                {
                    ResultPrune(children[i]);
                    continue;
                }

                Object.DestroyImmediate(children[i].gameObject);
            }
        }

        private static Transform ResultFind(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == name)
                {
                    return child;
                }

                Transform hit = ResultFind(child, name);
                if (hit != null)
                {
                    return hit;
                }
            }

            return null;
        }

        /// <summary>Finds a kept node anywhere under <paramref name="scope"/> and re-parents it;
        /// creates it when the prefab never had it.</summary>
        private static RectTransform ResultClaim(Transform scope, Transform parent, string name)
        {
            Transform found = ResultFind(scope, name);
            if (found == null)
            {
                return Node(parent, name);
            }

            var rt = found as RectTransform;
            if (rt == null)
            {
                rt = found.gameObject.AddComponent<RectTransform>();
            }

            rt.SetParent(parent, false);
            rt.SetAsLastSibling();
            rt.localScale = Vector3.one;
            rt.gameObject.SetActive(true);
            return rt;
        }

        /// <summary>Kept surface node (Card / Fill / StatsPanel): its Image stays — a variant
        /// overrides the sprite on it — but renders nothing by default.</summary>
        private static RectTransform ResultClaimSurface(Transform scope, Transform parent, string name)
        {
            RectTransform rt = ResultClaim(scope, parent, name);
            UiImage image = rt.GetComponent<UiImage>();
            if (image != null)
            {
                image.sprite = null;
                image.color = Color.clear;
                image.raycastTarget = false;
            }

            return rt;
        }

        private static TextMeshProUGUI ResultClaimLabel(Transform scope, Transform parent,
            string name, string text, GirdapFont font, float size, Color color,
            TextAlignmentOptions align, float spacingEm = 0f, bool rich = false)
        {
            RectTransform rt = ResultClaim(scope, parent, name);
            var tmp = rt.GetComponent<TextMeshProUGUI>();
            if (tmp == null)
            {
                tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            }

            return ResultStyle(tmp, text, font, size, color, align, spacingEm, rich);
        }

        private static TextMeshProUGUI ResultLabel(Transform parent, string name, string text,
            GirdapFont font, float size, Color color, TextAlignmentOptions align,
            float spacingEm = 0f, bool rich = false)
        {
            return ResultStyle(Text(parent, name, text, font, size, color, align, spacingEm), text,
                font, size, color, align, spacingEm, rich);
        }

        private static TextMeshProUGUI ResultStyle(TextMeshProUGUI tmp, string text, GirdapFont font,
            float size, Color color, TextAlignmentOptions align, float spacingEm, bool rich)
        {
            TMP_FontAsset asset = LoadFontAsset(font);
            if (asset != null)
            {
                tmp.font = asset;
            }

            tmp.text = text ?? "";
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.characterSpacing = Girdap.Spacing(spacingEm);
            tmp.fontStyle = FontStyles.Normal;

            // Team colours and the departed-row alpha arrive as tags; everything else must treat a
            // player name containing "<b>" as plain text.
            tmp.richText = rich;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.enableAutoSizing = false;
            tmp.enableVertexGradient = false;
            tmp.raycastTarget = false;
            return tmp;
        }

        // --------------------------------------------------------------- wiring

        private static void ResultSet(SerializedObject so, string field, Object value)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[Girdap] MatchResultOverlay.{field} alanı yok — bağlanamadı.");
                return;
            }

            property.objectReferenceValue = value;
        }

        private static void ResultSetArray(SerializedObject so, string field, Object[] values)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[Girdap] MatchResultOverlay.{field} dizisi yok — bağlanamadı.");
                return;
            }

            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        private static void ResultSetColor(SerializedObject so, string field, Color value)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[Girdap] MatchResultOverlay.{field} rengi yok — yazılamadı.");
                return;
            }

            property.colorValue = value;
        }
    }
}
