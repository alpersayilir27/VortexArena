using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.App.Admin;
using VortexArena.Core.UI;

// Factory methods share element names (Image / Button / Text). Aliases are required because
// `Image x = Image(...)` gives CS0119 — simple name lookup finds the member group BEFORE the type.
using UiImage = UnityEngine.UI.Image;
using UiButton = UnityEngine.UI.Button;

namespace VortexArena.App.Editor
{
    /// <summary>
    /// Builds the admin stats screen: the row prefab (<c>AdminStatsRow</c>) and the panel
    /// (<c>AdminStatsPanel</c>), from <c>plan/arayuz-yenileme/istatistik.html</c> + <c>tema.css</c>.
    /// <para>
    /// ⚠️ <b>The column grid lives in <see cref="Col"/> only.</b> Row cells, the header strip and
    /// the team group rows all read it; a cell placed with its own literal x drifts out of the
    /// table the first time a column width changes.
    /// </para>
    /// </summary>
    public static partial class GirdapUiBuilder
    {
        // ---------------------------------------------------------------- metrics

        private const float StatsPanelW = 1440f;
        private const float StatsPanelH = 810f;
        private const float StatsPanelChamfer = 26f;

        /// <summary>CSS <c>.panel { padding: 1px }</c> — the border ring of the card.</summary>
        private const float StatsBorder = 1f;

        private const float StatsHeadH = 72f;
        private const float StatsInfoH = 44f;
        private const float StatsFootH = 72f;

        /// <summary>CSS <c>.p-body { padding: 12px 24px }</c>.</summary>
        private const float StatsBodyPadX = 24f;

        /// <inheritdoc cref="StatsBodyPadX"/>
        private const float StatsBodyPadY = 12f;

        private const float StatsRowH = 44f;
        private const float StatsGroupH = 36f;
        private const float StatsTheadH = 30f;

        /// <summary>CSS <c>.tbl { gap: 4px }</c>.</summary>
        private const float StatsRowGap = 4f;

        /// <summary>CSS <c>.thead/.trow > * { padding: 0 8px }</c>.</summary>
        private const float StatsCellPad = 8f;

        /// <summary>Inline button strip metrics (CSS <c>.btn.sm</c>).</summary>
        private const float StatsBtnH = 32f;

        /// <inheritdoc cref="StatsBtnH"/>
        private const float StatsBtnChamfer = 5f;

        /// <inheritdoc cref="StatsBtnH"/>
        private const float StatsBtnFont = 14f;

        /// <summary>
        /// CSS <c>.thead/.tgroup/.trow { grid-template-columns: 40px 1fr 190px … }</c> resolved on
        /// the body's 1390 px content width — the <c>1fr</c> name column has
        /// <c>overflow: hidden</c>, so its automatic minimum is 0 and the leftover is exactly
        /// 124 px.
        /// </summary>
        private static readonly float[] Col =
        {
            0f, 40f, 164f, 354f, 402f, 450f, 514f, 574f, 650f, 726f, 786f, 848f, 918f, 1094f, 1390f
        };

        private static float StatsTableW => Col[Col.Length - 1];

        private static float StatsInnerW => StatsPanelW - StatsBorder * 2f;

        /// <summary>Card-local y of the table's first row (head band + info strip + body padding).</summary>
        private static float StatsTableY =>
            StatsBorder + StatsHeadH + StatsInfoH + StatsBodyPadY;

        // ------------------------------------------------------------- CSS colours
        // ⚠️ Values the palette does not name yet (row/group/strip surfaces). They come from
        // tema.css verbatim; do not round them by eye.

        private static readonly Color StatsRowBgA = Girdap.RowA;
        private static readonly Color StatsRowBgB = Girdap.RowB;
        private static readonly Color StatsRowLine = Girdap.RowLine;
        private static readonly Color StatsRedFade = Girdap.Rgba(217, 51, 51, 0.10f);
        private static readonly Color StatsRedLine = Girdap.Rgba(244, 84, 75, 0.7f);
        private static readonly Color StatsBlueFade = Girdap.Rgba(51, 102, 230, 0.12f);
        private static readonly Color StatsBlueLine = Girdap.Rgba(92, 140, 255, 0.75f);
        private static readonly Color StatsHeadTint = Girdap.Rgba(125, 227, 255, 0.10f);
        private static readonly Color StatsInfoTint = Girdap.Rgba(125, 227, 255, 0.05f);
        private static readonly Color StatsFootBg = Girdap.Rgba(4, 7, 16, 0.55f);
        private static readonly Color StatsGlow = Girdap.Rgba(125, 227, 255, 0.13f);
        private static readonly Color StatsDots = Girdap.Rgba(255, 255, 255, 0.05f);
        private static readonly Color StatsScrim = Girdap.Rgba(3, 5, 12, 0.92f);
        private static readonly Color StatsScrimCore = Girdap.Rgba(10, 16, 36, 0.74f);
        private static readonly Color StatsFieldBd = Girdap.Rgba(170, 205, 255, 0.32f);
        private static readonly Color StatsFieldBg = Girdap.Hex(0x090F1F);
        private static readonly Color StatsPlateStripe = Girdap.Rgba(255, 255, 255, 0.075f);

        // --------------------------------------------------------------- menu items

        [MenuItem("Tools/VortexArena/UI/Girdap/Yalnız StatsRow")]
        public static void BuildStatsRowOnly()
        {
            BuildAssets();
            BuildStatsRow();
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/VortexArena/UI/Girdap/Yalnız StatsPanel")]
        public static void BuildStatsPanelOnly()
        {
            BuildAssets();
            BuildStatsPanel();
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------- row

        static partial void BuildStatsRow()
        {
            GameObject root = LoadPrefabContents("AdminStatsRow")
                              ?? NewRoot("AdminStatsRow", StatsTableW, StatsRowH);
            StatsClearChildren(root);

            // Explicit top-left anchors: the panel reads this rect's height to space the list, and
            // a stretched anchor pair would report the preview canvas's height instead.
            var rect = (RectTransform)root.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(StatsTableW, StatsRowH);

            var row = root.GetComponent<AdminStatsRow>() ?? root.AddComponent<AdminStatsRow>();

            // The root's own Image is the row-wide click target (select); it is transparent, the
            // visible surface is the Bg shape below it.
            var hit = root.GetComponent<UiImage>() ?? root.AddComponent<UiImage>();
            hit.color = Color.clear;
            hit.raycastTarget = true;
            var select = root.GetComponent<UiButton>() ?? root.AddComponent<UiButton>();
            select.transition = Selectable.Transition.None;
            select.targetGraphic = hit;

            Transform t = root.transform;

            UiShape bg = Shape(t, "Bg");
            Stretch(bg.rectTransform);
            // Antialias off: rows stack 4 px apart and the feather would read as a double seam.
            bg.Chamfer(0f).Fill(StatsRowBgA, StatsRowBgB, UiGradientMode.Horizontal).Antialias(false);

            UiImage line = Image(t, "Line", null, StatsRowLine);
            StretchBottom(line.rectTransform, 0f, 0f, 0f, 1f);

            UiShape plate = Shape(t, "NumberPlate");
            Place(plate.rectTransform, Col[0], 0f, Col[1] - Col[0], StatsRowH);
            // CSS clip-path pulls the BOTTOM-RIGHT corner 9 px in — the table's team wedge.
            plate.Chamfer(0f).Slant(0f, 9f).Fill(Girdap.RedHi, Girdap.RedLo, UiGradientMode.Vertical);

            TextMeshProUGUI number = Text(t, "Number", "7", GirdapFont.ChakraBold, 17f, Color.white,
                TextAlignmentOptions.Center);
            Place(number.rectTransform, Col[0], 0f, Col[1] - Col[0] - 7f, StatsRowH);

            // --- name cell (c2) ---
            float nameX = Col[1] + StatsCellPad;
            float idW = StatsMeasure("#99", GirdapFont.ChakraSemiBold, 12f, 0f);
            float nameW = Col[2] - StatsCellPad - nameX - idW - 8f;

            TextMeshProUGUI playerName = Text(t, "Name", "Oyuncu", GirdapFont.SairaBold, 22f,
                Girdap.Text, TextAlignmentOptions.BaselineLeft);
            // Ellipsis, not overflow: the name column is 124 px and a long name would run over the
            // id and the status chip.
            playerName.overflowMode = TextOverflowModes.Ellipsis;
            Place(playerName.rectTransform, nameX, 0f, nameW, StatsRowH);

            TextMeshProUGUI id = Text(t, "Id", "#12", GirdapFont.ChakraSemiBold, 12f, Girdap.Faint,
                TextAlignmentOptions.BaselineLeft);
            Place(id.rectTransform, nameX + nameW + 8f, 0f, idW, StatsRowH);

            // --- status chip (c3) ---
            UiChip state = Chip(t, "State", "hazır", UiChipKind.Good);
            PlaceMiddleLeft(state.Root, Col[2] + StatsCellPad, 0f, UiChip.Height);

            // --- counters (c4..c7) ---
            TextMeshProUGUI kills = StatsNumberCell(t, "Kills", 3, "6");
            TextMeshProUGUI deaths = StatsNumberCell(t, "Deaths", 4, "2");
            TextMeshProUGUI kd = StatsNumberCell(t, "Kd", 5, "3.00");
            TextMeshProUGUI score = StatsNumberCell(t, "Score", 6, "6");

            // --- battery (c8) ---
            UiImage batteryIcon = Icon(t, "BatteryIcon", "Battery", 16f, Girdap.Muted);
            Place(batteryIcon.rectTransform, Col[7] + StatsCellPad, (StatsRowH - 16f) * 0.5f, 16f, 16f);

            float batteryX = Col[7] + StatsCellPad + 16f + 4f;
            TextMeshProUGUI battery = Text(t, "Battery", "%78", GirdapFont.SairaSemiBold, 17f,
                Girdap.Muted, TextAlignmentOptions.MidlineLeft);
            Place(battery.rectTransform, batteryX, 0f, Col[8] - StatsCellPad - batteryX, StatsRowH);

            // --- controllers (c9) ---
            float ctrlX = Col[8] + StatsCellPad;
            UiShape ctrlL = StatsSkewBar(t, "CtrlLeft", ctrlX, (StatsRowH - 14f) * 0.5f, 9f, 14f,
                Girdap.Muted, -12f);
            UiShape ctrlR = StatsSkewBar(t, "CtrlRight", ctrlX + 12f, (StatsRowH - 14f) * 0.5f, 9f,
                14f, Girdap.Muted, -12f);

            // --- body (c10) ---
            UiImage body = Icon(t, "BodyIcon", "Body", 16f, Girdap.Muted);
            Place(body.rectTransform, Col[9] + StatsCellPad, (StatsRowH - 16f) * 0.5f, 16f, 16f);

            // --- floor / ping (c11, c12) ---
            TextMeshProUGUI floor = StatsTextCell(t, "Floor", 10, "—");
            TextMeshProUGUI ping = StatsTextCell(t, "Ping", 11, "24 ms");

            // --- violation ledger (c13) ---
            float violX = Col[12] + StatsCellPad;
            TextMeshProUGUI violEmpty = StatsTextCell(t, "ViolationEmpty", 12, "—");

            RectTransform violRoot = Node(t, "Violations");
            Place(violRoot, violX, (StatsRowH - UiChip.Height) * 0.5f,
                Col[13] - StatsCellPad - violX, UiChip.Height);
            UiChip obstacle = Chip(violRoot, "Obstacle", "DUVAR 2", UiChipKind.Bad);
            PlaceMiddleLeft(obstacle.Root, 0f, 0f, UiChip.Height);
            UiChip outOfBounds = Chip(violRoot, "OutOfBounds", "ALAN DIŞI 1", UiChipKind.Warn);
            PlaceMiddleLeft(outOfBounds.Root, 0f, 0f, UiChip.Height);
            UiChip calibError = Chip(violRoot, "CalibrationError", "hata", UiChipKind.Bad);
            PlaceMiddleLeft(calibError.Root, 0f, 0f, UiChip.Height);

            // --- action strip (c14) ---
            float actY = (StatsRowH - StatsBtnH) * 0.5f;
            float x = Col[13] + StatsCellPad;

            UiButtonStyle calibrate = StatsSmallButton(t, "Calibrate", "KALİBRE",
                UiButtonKind.Normal, x, actY, 76f);
            x += 76f + 4f;
            UiButtonStyle measure = StatsSmallButton(t, "Measure", "ÖLÇ", UiButtonKind.Normal,
                x, actY, 48f);
            x += 48f + 4f;
            UiButtonStyle rename = StatsIconButton(t, "Rename", "Pencil", x, actY, 32f);
            // CSS `.trow .act .gap` — the separator that keeps the two destructive buttons away
            // from the three safe ones.
            x += 32f + 4f + 4f + 4f;
            UiButtonStyle reset = StatsSmallButton(t, "Reset", "SIFIRLA", UiButtonKind.TextBad,
                x, actY, 62f, hold: true);
            x += 62f + 4f;
            UiButtonStyle kick = StatsSmallButton(t, "Kick", "AT", UiButtonKind.TextBad, x, actY, 40f);

            // --- inline rename editor (covers the name + status cells) ---
            RectTransform editRoot = Node(t, "NameEdit");
            float editW = Col[3] - 4f - (Col[1] + 4f);
            Place(editRoot, Col[1] + 4f, actY, editW, StatsBtnH);

            UiShape editBg = Shape(editRoot, "Bg");
            Stretch(editBg.rectTransform);
            editBg.Chamfer(StatsBtnChamfer).Fill(Girdap.PanelB);

            float applyW = 54f;
            float cancelW = 50f;
            float inputW = editW - applyW - cancelW - 8f;
            TMP_InputField input = StatsInputField(editRoot, "Input", 0f, inputW);
            UiButtonStyle apply = StatsSmallButton(editRoot, "ApplyName", "TAMAM", UiButtonKind.Go,
                inputW + 4f, 0f, applyW);
            UiButtonStyle cancel = StatsSmallButton(editRoot, "CancelName", "İPTAL",
                UiButtonKind.Normal, inputW + applyW + 8f, 0f, cancelW);
            editRoot.gameObject.SetActive(false);

            // ---------------------------------------------------------------- wiring
            var so = new SerializedObject(row);
            StatsWire(so, "rowShape", bg);
            StatsWire(so, "selectButton", select);
            StatsWire(so, "numberPlate", plate);
            StatsWire(so, "numberText", number);
            StatsWire(so, "nameText", playerName);
            StatsWire(so, "idText", id);
            StatsWire(so, "nameEditRoot", editRoot.gameObject);
            StatsWire(so, "nameInput", input);
            StatsWire(so, "nameApplyButton", apply.TargetButton);
            StatsWire(so, "nameCancelButton", cancel.TargetButton);
            StatsWire(so, "stateChip", state);
            StatsWire(so, "killsText", kills);
            StatsWire(so, "deathsText", deaths);
            StatsWire(so, "kdText", kd);
            StatsWire(so, "scoreText", score);
            StatsWire(so, "batteryIcon", batteryIcon);
            StatsWire(so, "batteryText", battery);
            StatsWire(so, "ctrlLeftBar", ctrlL);
            StatsWire(so, "ctrlRightBar", ctrlR);
            StatsWire(so, "bodyIcon", body);
            StatsWire(so, "floorText", floor);
            StatsWire(so, "pingText", ping);
            StatsWire(so, "obstacleChip", obstacle);
            StatsWire(so, "outOfBoundsChip", outOfBounds);
            StatsWire(so, "calibrationErrorChip", calibError);
            StatsWire(so, "violationEmpty", violEmpty);
            StatsWire(so, "calibrateButton", calibrate);
            StatsWire(so, "measureButton", measure);
            StatsWire(so, "renameButton", rename);
            StatsWire(so, "resetButton", reset);
            StatsWire(so, "kickButton", kick);
            so.ApplyModifiedPropertiesWithoutUndo();

            SaveAndUnload(root, "AdminStatsRow");
        }

        // ----------------------------------------------------------------- panel

        static partial void BuildStatsPanel()
        {
            GameObject root = LoadPrefabContents("AdminStatsPanel")
                              ?? NewRoot("AdminStatsPanel", 1920f, 1080f);
            StatsClearChildren(root);

            // Full-screen root: the card sits at its CSS offset inside it, and the scrim needs the
            // whole screen.
            Stretch((RectTransform)root.transform);

            var panel = root.GetComponent<AdminStatsPanel>() ?? root.AddComponent<AdminStatsPanel>();

            RectTransform shell = Node(root.transform, "Panel");
            Stretch(shell);

            // CSS `.scrim` — a radial darkening, flattened to a solid plate plus one radial
            // highlight (a uGUI graphic carries one gradient). ⚠️ raycastTarget stays OFF: the HUD
            // behind the card must keep taking clicks while the panel is open.
            UiImage scrim = Image(shell, "Scrim", null, StatsScrim);
            Stretch(scrim.rectTransform);
            UiImage scrimCore = Image(shell, "ScrimCore", LoadSprite("Radial_256.png"), StatsScrimCore);
            // Ellipse centre at CSS `50% 42%` of the screen: 86 px above the middle at 1080.
            PlaceCenter(scrimCore.rectTransform, 0f, -86f, 2400f, 1350f);

            UiShape card = Shape(shell, "Card");
            // CSS `left: 240px; top: 135px` in a 1920x1080 stage = dead centre.
            PlaceCenter(card.rectTransform, 0f, 0f, StatsPanelW, StatsPanelH);
            // ⚠️ The card itself blocks clicks: without it a press on an empty part of the panel
            // would reach the HUD button sitting underneath.
            card.raycastTarget = true;
            card.Chamfer(StatsPanelChamfer)
                .Outline(StatsBorder, Girdap.PanelBdA, Girdap.PanelBdB)
                .Fill(Girdap.PanelA, Girdap.PanelB, UiGradientMode.Vertical);
            Transform c = card.transform;

            // Top bloom + dot grid of `.panel`'s layered background. The bloom is kept INSIDE the
            // card: uGUI has no `overflow: hidden`, so a bleeding glow would sit on the scene.
            UiImage glow = Image(c, "Glow", LoadSprite("Radial_256.png"), StatsGlow);
            Place(glow.rectTransform, 0f, 0f, StatsPanelW, 440f);

            UiImage dots = Image(c, "Dots", LoadSprite("Dots_16.png"), StatsDots, UiImage.Type.Tiled);
            Stretch(dots.rectTransform, StatsBorder, StatsBorder, StatsBorder, StatsBorder);

            StatsHead(c, out UiButtonStyle close, out GameObject scorebug,
                out TextMeshProUGUI scoreRed, out TextMeshProUGUI scoreBlue,
                out TextMeshProUGUI headline);

            StatsInfoStrip(c, out RectTransform[] infoItems, out TextMeshProUGUI[] infoValues);

            StatsTableHead(c, out GameObject[] killDeathCells);

            StatsTableBody(c, out ScrollRect scroll, out RectTransform content);

            RectTransform redGroup = StatsGroupRow(content, "GroupRed", "red",
                out TextMeshProUGUI redName, out TextMeshProUGUI redMeta,
                out TextMeshProUGUI redKills, out TextMeshProUGUI redDeaths,
                out TextMeshProUGUI redScore);
            RectTransform blueGroup = StatsGroupRow(content, "GroupBlue", "blue",
                out TextMeshProUGUI blueName, out TextMeshProUGUI blueMeta,
                out TextMeshProUGUI blueKills, out TextMeshProUGUI blueDeaths,
                out TextMeshProUGUI blueScore);

            StatsFoot(c, out UiButtonStyle restartBody, out UiButtonStyle measureAll,
                out UiButtonStyle calibrateAll, out TextMeshProUGUI hint);

            StatsToast(c, out RectTransform toast, out UiImage toastIcon,
                out TextMeshProUGUI toastText, out UiButton toastClose);

            var rowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "AdminStatsRow.prefab");
            AdminStatsRow rowComponent = rowPrefab != null
                ? rowPrefab.GetComponent<AdminStatsRow>()
                : null;
            if (rowComponent == null)
            {
                Debug.LogError("[Girdap] AdminStatsRow.prefab yok — önce StatsRow üretilmeli.");
            }

            var so = new SerializedObject(panel);
            StatsWire(so, "_root", shell.gameObject);
            StatsWire(so, "_closeButton", close);
            StatsWire(so, "_scorebugRoot", scorebug);
            StatsWire(so, "_scoreRedText", scoreRed);
            StatsWire(so, "_scoreBlueText", scoreBlue);
            StatsWire(so, "_headline", headline);
            StatsWireArray(so, "_infoItems", infoItems);
            StatsWireArray(so, "_infoValues", infoValues);
            StatsWire(so, "_rowPrefab", rowComponent);
            StatsWire(so, "_rowContainer", content);
            StatsWire(so, "_scroll", scroll);
            SerializedProperty gap = so.FindProperty("_rowGap");
            if (gap != null)
            {
                gap.floatValue = StatsRowGap;
            }

            StatsWireArray(so, "_killDeathHeaderCells", killDeathCells);
            StatsWire(so, "_redGroup", redGroup);
            StatsWire(so, "_redGroupName", redName);
            StatsWire(so, "_redGroupMeta", redMeta);
            StatsWire(so, "_redGroupKills", redKills);
            StatsWire(so, "_redGroupDeaths", redDeaths);
            StatsWire(so, "_redGroupScore", redScore);
            StatsWire(so, "_blueGroup", blueGroup);
            StatsWire(so, "_blueGroupName", blueName);
            StatsWire(so, "_blueGroupMeta", blueMeta);
            StatsWire(so, "_blueGroupKills", blueKills);
            StatsWire(so, "_blueGroupDeaths", blueDeaths);
            StatsWire(so, "_blueGroupScore", blueScore);
            StatsWire(so, "_restartBodyAllButton", restartBody);
            StatsWire(so, "_measureAllButton", measureAll);
            StatsWire(so, "_calibrateAllButton", calibrateAll);
            StatsWire(so, "_footHint", hint);
            StatsWire(so, "_popupRoot", toast.gameObject);
            StatsWire(so, "_popupShape", toast);
            StatsWire(so, "_popupIcon", toastIcon);
            StatsWire(so, "_popupText", toastText);
            StatsWire(so, "_popupCloseButton", toastClose);
            so.ApplyModifiedPropertiesWithoutUndo();

            SaveAndUnload(root, "AdminStatsPanel");
        }

        // ------------------------------------------------------------------ header

        private static void StatsHead(Transform card, out UiButtonStyle close,
            out GameObject scorebug, out TextMeshProUGUI scoreRed, out TextMeshProUGUI scoreBlue,
            out TextMeshProUGUI headline)
        {
            RectTransform head = Node(card, "Head");
            Place(head, StatsBorder, StatsBorder, StatsInnerW, StatsHeadH);

            UiShape bg = Shape(head, "Bg");
            Stretch(bg.rectTransform);
            // Top-left chamfer only: the band has to follow the card's cut corner.
            bg.Chamfer(StatsPanelChamfer, 0f, 0f, 0f)
                .Fill(StatsHeadTint, Girdap.Rgba(125, 227, 255, 0f), UiGradientMode.Vertical)
                .Antialias(false);

            UiImage line = Image(head, "Line", null, Girdap.Line);
            StretchBottom(line.rectTransform, 0f, 0f, 0f, 1f);

            StatsTitleMark(head, 28f, (StatsHeadH - 28f) * 0.5f);

            float titleTextX = 28f + 24f + 16f;
            float titleW = StatsMeasure("İSTATİSTİKLER", GirdapFont.ChakraBold, 29f, 0.14f);
            TextMeshProUGUI title = Text(head, "Title", "İSTATİSTİKLER", GirdapFont.ChakraBold, 29f,
                Girdap.Text, TextAlignmentOptions.MidlineLeft, 0.14f);
            Place(title.rectTransform, titleTextX, 0f, titleW, StatsHeadH);

            // Close button: the label slot is measured against the LONGER text ("TURA DEVAM"), so
            // the parked-round rename cannot push the icon and the keycap out of place.
            float closeLabelW = StatsMeasure("TURA DEVAM", GirdapFont.SairaBold, 20f, 0.07f);
            float kbdW = Mathf.Max(22f, StatsMeasure("Esc", GirdapFont.ChakraBold, 12f, 0f) + 10f);
            float closeW = 16f + 20f + 8f + closeLabelW + 8f + kbdW + 16f;
            close = Button(head, "Close", "TURA DEVAM", UiButtonKind.Normal,
                StatsInnerW - 16f - closeW, (StatsHeadH - 48f) * 0.5f, closeW, 48f, "X", "Esc");
            close.SetLabel("KAPAT");

            // Mini scorebug, centred the way the CSS flex spacers place it (two equal `.sp`
            // between title, bug and button).
            float redLabelW = StatsMeasure("KIRMIZI", GirdapFont.ChakraBold, 17f, 0.12f);
            float blueLabelW = StatsMeasure("MAVİ", GirdapFont.ChakraBold, 17f, 0.12f);
            float redPlateW = 30f + redLabelW + 20f;
            float bluePlateW = 20f + blueLabelW + 30f;
            float bugW = redPlateW + 128f + bluePlateW;
            float spare = Mathf.Max(0f,
                StatsInnerW - 28f - 16f - (titleTextX - 28f + titleW) - bugW - closeW - 80f) * 0.5f;
            float bugX = 28f + (titleTextX - 28f + titleW) + 20f + spare + 20f;
            float bugY = (StatsHeadH - 44f) * 0.5f;

            RectTransform bug = Node(head, "Scorebug");
            Place(bug, bugX, bugY, bugW, 44f);
            scorebug = bug.gameObject;

            StatsTeamPlate(bug, "TeamRed", "KIRMIZI", 0f, redPlateW, 30f, true);
            scoreRed = StatsScoreBox(bug, "ScoreRed", redPlateW, Girdap.Red);
            scoreBlue = StatsScoreBox(bug, "ScoreBlue", redPlateW + 64f, Girdap.Blue);
            StatsTeamPlate(bug, "TeamBlue", "MAVİ", redPlateW + 128f, bluePlateW, 20f, false);

            headline = Text(head, "Headline", "HERKES TEK", GirdapFont.ChakraBold, 24f, Girdap.AccHi,
                TextAlignmentOptions.Center, 0.12f);
            Place(headline.rectTransform, bugX, bugY, bugW, 44f);
            headline.gameObject.SetActive(false);
        }

        /// <summary>
        /// CSS <c>.p-title::before</c>: three skewed accent bars.
        /// <para>⚠️ Built with a Z ROTATION, not a skew: a uGUI rect cannot be sheared. On a 7 px
        /// wide bar the two read the same; on anything wider they would not.</para>
        /// </summary>
        private static void StatsTitleMark(Transform parent, float x, float y)
        {
            RectTransform mark = Node(parent, "Mark");
            Place(mark, x, y, 24f, 28f);

            StatsSkewBar(mark, "A", 0f, 0f, 7f, 28f, Girdap.Acc, -20f);
            StatsSkewBar(mark, "B", 11f, 0f, 5f, 28f, Girdap.Acc, -20f);
            StatsSkewBar(mark, "C", 20f, 0f, 4f, 28f, Girdap.Hex(0x7DE3FF, 0.45f), -20f);
        }

        private static void StatsTeamPlate(Transform parent, string name, string label, float x,
            float w, float padLeft, bool red)
        {
            UiShape plate = Shape(parent, name);
            Place(plate.rectTransform, x, 0f, w, 44f);
            // CSS clip-path: red cuts the bottom-LEFT corner, blue the bottom-RIGHT one.
            plate.Chamfer(0f)
                .Slant(red ? 16f : 0f, red ? 0f : 16f)
                .Fill(red ? Girdap.RedHi : Girdap.BlueHi, red ? Girdap.RedLo : Girdap.BlueLo,
                    UiGradientMode.Vertical);

            UiStripes stripes = Stripes(plate.transform, "Stripes");
            Stretch(stripes.rectTransform);
            stripes.SlantLeft = red ? 16f : 0f;
            stripes.SlantRight = red ? 0f : 16f;
            stripes.Stripes(-55f, 6f, 17f, StatsPlateStripe);

            TextMeshProUGUI text = Text(plate.transform, "Label", label, GirdapFont.ChakraBold, 17f,
                Color.white, TextAlignmentOptions.MidlineLeft, 0.12f);
            Place(text.rectTransform, padLeft, 0f, w - padLeft - 20f, 44f);
        }

        private static TextMeshProUGUI StatsScoreBox(Transform parent, string name, float x,
            Color underline)
        {
            UiShape box = Shape(parent, name);
            Place(box.rectTransform, x, 0f, 64f, 44f);
            box.Chamfer(0f).Fill(Girdap.ScoreA, Girdap.ScoreB, UiGradientMode.Vertical)
                .Antialias(false);

            // CSS `box-shadow: inset 0 -4px 0 <team>` — whose score this is.
            UiImage mark = Image(box.transform, "Underline", null, underline);
            StretchBottom(mark.rectTransform, 0f, 0f, 0f, 4f);

            TextMeshProUGUI text = Text(box.transform, "Value", "0", GirdapFont.ChakraBold, 30f,
                Color.white, TextAlignmentOptions.Center);
            Stretch(text.rectTransform);
            return text;
        }

        // -------------------------------------------------------------- info strip

        /// <summary>
        /// CSS <c>.info</c>: fixed label + live value pairs. ⚠️ The ORDER matches
        /// <c>AdminStatsPanel</c>'s <c>Info*</c> constants; a slot inserted here relabels a value
        /// there.
        /// </summary>
        private static void StatsInfoStrip(Transform card, out RectTransform[] items,
            out TextMeshProUGUI[] values)
        {
            RectTransform strip = Node(card, "Info");
            Place(strip, StatsBorder, StatsBorder + StatsHeadH, StatsInnerW, StatsInfoH);

            UiShape bg = Shape(strip, "Bg");
            Stretch(bg.rectTransform);
            bg.Chamfer(0f).Fill(StatsInfoTint).Antialias(false);

            UiImage line = Image(strip, "Line", null, Girdap.Line);
            StretchBottom(line.rectTransform, 0f, 0f, 0f, 1f);

            RectTransform list = Node(strip, "Items");
            Place(list, 28f, 0f, StatsInnerW - 56f, StatsInfoH);

            string[] labels =
            {
                "FAZ", "KALAN", "MOD", "HARİTA", "SÜRE", "SKOR LİMİTİ", "SUNUCU", "POZ AKIŞI",
                "BAĞLI ADMİN", "MÜŞTERİ"
            };
            string[] names =
            {
                "Phase", "Remaining", "Mode", "Map", "Duration", "ScoreLimit", "Server", "Snapshot",
                "Admins", "Customers"
            };

            items = new RectTransform[labels.Length];
            values = new TextMeshProUGUI[labels.Length];

            for (int i = 0; i < labels.Length; i++)
            {
                RectTransform item = Node(list, names[i]);
                Place(item, 0f, 0f, 120f, StatsInfoH);

                float labelW = StatsMeasure(labels[i], GirdapFont.ChakraSemiBold, 11f, 0.16f);
                TextMeshProUGUI label = Text(item, "Label", labels[i], GirdapFont.ChakraSemiBold,
                    11f, Girdap.Faint, TextAlignmentOptions.BaselineLeft, 0.16f);
                Place(label.rectTransform, 0f, 0f, labelW, StatsInfoH);

                TextMeshProUGUI value = Text(item, "Value", "-", GirdapFont.SairaSemiBold, 17f,
                    Girdap.Text, TextAlignmentOptions.BaselineLeft);
                // The label width is baked into this x offset — the runtime reads it back to size
                // the pair without re-measuring the label.
                Place(value.rectTransform, labelW + 7f, 0f, 60f, StatsInfoH);

                items[i] = item;
                values[i] = value;
            }
        }

        // ------------------------------------------------------------- table header

        private static void StatsTableHead(Transform card, out GameObject[] killDeathCells)
        {
            RectTransform head = Node(card, "TableHead");
            Place(head, StatsBorder + StatsBodyPadX, StatsTableY, StatsTableW, StatsTheadH);

            StatsHeadLabel(head, "Player", "OYUNCU", 1, false);
            StatsHeadLabel(head, "State", "DURUM", 2, false);
            GameObject killCell = StatsHeadIcon(head, "Target", 3);
            GameObject deathCell = StatsHeadIcon(head, "Skull", 4);
            GameObject kdCell = StatsHeadLabel(head, "Kd", "K/D", 5, true);
            StatsHeadLabel(head, "Score", "SKOR", 6, true);
            StatsHeadLabel(head, "Battery", "PİL", 7, false);
            StatsHeadLabel(head, "Controllers", "KUMANDA", 8, false);
            StatsHeadLabel(head, "Body", "GÖVDE", 9, false);
            StatsHeadLabel(head, "Floor", "KAT", 10, false);
            StatsHeadLabel(head, "Ping", "PING", 11, false);
            StatsHeadLabel(head, "Violations", "İHLAL", 12, false);

            killDeathCells = new[] { killCell, deathCell, kdCell };
        }

        private static GameObject StatsHeadLabel(Transform parent, string name, string text,
            int column, bool centered)
        {
            TextMeshProUGUI label = Text(parent, name, text, GirdapFont.ChakraSemiBold, 12f,
                Girdap.Faint, centered ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft,
                0.16f);
            float x = centered ? Col[column] : Col[column] + StatsCellPad;
            float w = centered ? Col[column + 1] - Col[column] : Col[column + 1] - x - StatsCellPad;
            Place(label.rectTransform, x, 0f, w, StatsTheadH);
            return label.gameObject;
        }

        private static GameObject StatsHeadIcon(Transform parent, string icon, int column)
        {
            UiImage image = Icon(parent, icon, icon, 16f, Girdap.Faint);
            Place(image.rectTransform, Col[column] + (Col[column + 1] - Col[column] - 16f) * 0.5f,
                (StatsTheadH - 16f) * 0.5f, 16f, 16f);
            return image.gameObject;
        }

        // --------------------------------------------------------------- table body

        private static void StatsTableBody(Transform card, out ScrollRect scroll,
            out RectTransform content)
        {
            float top = StatsTableY + StatsTheadH + StatsRowGap;
            float bottom = StatsPanelH - StatsBorder - StatsFootH - StatsBodyPadY;

            RectTransform viewport = Node(card, "Viewport");
            Place(viewport, StatsBorder + StatsBodyPadX, top, StatsTableW, bottom - top);
            viewport.gameObject.AddComponent<RectMask2D>();

            content = Node(viewport, "Rows");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = new Vector2(0f, -(bottom - top));
            content.offsetMax = Vector2.zero;

            // ScrollRect sits ON the viewport: the clipped rect and the scrolling rect are the same
            // box here, so a separate wrapper would only add a node.
            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            scroll.horizontalScrollbar = null;
            scroll.verticalScrollbar = null;
        }

        /// <summary>CSS <c>.tgroup</c>: the team band inside the one table.</summary>
        private static RectTransform StatsGroupRow(Transform parent, string name, string team,
            out TextMeshProUGUI label, out TextMeshProUGUI meta, out TextMeshProUGUI kills,
            out TextMeshProUGUI deaths, out TextMeshProUGUI score)
        {
            bool red = team == "red";

            RectTransform group = Node(parent, name);
            Place(group, 0f, 0f, StatsTableW, StatsGroupH);

            UiShape bg = Shape(group, "Bg");
            Stretch(bg.rectTransform);
            bg.Chamfer(0f)
                .Fill(red ? Girdap.Red : Girdap.Blue, red ? StatsRedFade : StatsBlueFade,
                    UiGradientMode.Horizontal, 0.68f)
                .Antialias(false);

            UiImage line = Image(group, "Line", null, red ? StatsRedLine : StatsBlueLine);
            StretchBottom(line.rectTransform, 0f, 0f, 0f, 1f);

            label = Text(group, "Name", red ? "KIRMIZI" : "MAVİ", GirdapFont.ChakraBold, 18f,
                Color.white, TextAlignmentOptions.BaselineLeft, 0.12f);
            Place(label.rectTransform, 12f, 0f, 96f, StatsGroupH);

            meta = Text(group, "Meta", "0 oyuncu", GirdapFont.SairaSemiBold, 18f, Color.white,
                TextAlignmentOptions.BaselineLeft);
            Place(meta.rectTransform, 120f, 0f, Col[3] - 120f, StatsGroupH);

            kills = StatsGroupNumber(group, "Kills", 3);
            deaths = StatsGroupNumber(group, "Deaths", 4);
            score = StatsGroupNumber(group, "Score", 6);
            return group;
        }

        private static TextMeshProUGUI StatsGroupNumber(Transform parent, string name, int column)
        {
            TextMeshProUGUI text = Text(parent, name, "0", GirdapFont.ChakraBold, 19f, Color.white,
                TextAlignmentOptions.Center);
            Place(text.rectTransform, Col[column], 0f, Col[column + 1] - Col[column], StatsGroupH);
            return text;
        }

        // ------------------------------------------------------------------- footer

        private static void StatsFoot(Transform card, out UiButtonStyle restartBody,
            out UiButtonStyle measureAll, out UiButtonStyle calibrateAll, out TextMeshProUGUI hint)
        {
            RectTransform foot = Node(card, "Foot");
            Place(foot, StatsBorder, StatsPanelH - StatsBorder - StatsFootH, StatsInnerW, StatsFootH);

            UiShape bg = Shape(foot, "Bg");
            Stretch(bg.rectTransform);
            // Bottom-right chamfer only: the band has to follow the card's cut corner.
            bg.Chamfer(0f, 0f, StatsPanelChamfer, 0f).Fill(StatsFootBg).Antialias(false);

            UiImage line = Image(foot, "Line", null, Girdap.Line);
            StretchTop(line.rectTransform, 0f, 0f, 0f, 1f);

            float y = (StatsFootH - 48f) * 0.5f;
            float right = 24f;

            calibrateAll = StatsFootButton(foot, "CalibrateAll", "TÜMÜNÜ KALİBRE ET", "Target",
                UiButtonKind.On, ref right, y);
            measureAll = StatsFootButton(foot, "MeasureAll", "TÜMÜNÜ ÖLÇEKLENDİR", "Scale",
                UiButtonKind.Normal, ref right, y);
            restartBody = StatsFootButton(foot, "RestartBodyAll", "GÖVDE YENİLE", "Refresh",
                UiButtonKind.Normal, ref right, y);

            hint = Text(foot, "Hint", "", GirdapFont.BarlowMedium, 15f, Girdap.Muted,
                TextAlignmentOptions.MidlineLeft);
            hint.textWrappingMode = TextWrappingModes.Normal; // one long sentence, two lines
            Place(hint.rectTransform, 24f, 12f, StatsInnerW - right - 34f, 48f);
            hint.gameObject.SetActive(false);
        }

        private static UiButtonStyle StatsFootButton(Transform parent, string name, string label,
            string icon, UiButtonKind kind, ref float right, float y)
        {
            float w = 16f + 20f + 8f + StatsMeasure(label, GirdapFont.SairaBold, 20f, 0.07f) + 16f;
            UiButtonStyle style = Button(parent, name, label, kind, StatsInnerW - right - w, y, w,
                48f, icon);
            right += w + 10f; // CSS `.p-foot { gap: 10px }`
            return style;
        }

        // -------------------------------------------------------------------- toast

        private static void StatsToast(Transform card, out RectTransform toast, out UiImage icon,
            out TextMeshProUGUI text, out UiButton close)
        {
            toast = Node(card, "Toast");
            // Pivot stays centred so the runtime can hug the width to the message.
            PlaceBottomCenter(toast, StatsFootH + 16f, 420f, 40f);

            UiShape bg = Shape(toast, "Bg");
            Stretch(bg.rectTransform);
            bg.Chamfer(8f).Outline(1f, Girdap.ConfirmBd)
                .Fill(Girdap.ConfirmA, Girdap.ConfirmB, UiGradientMode.Vertical);

            icon = Icon(toast, "Icon", "Warn", 20f, Girdap.ConfirmFg);
            PlaceMiddleLeft(icon.rectTransform, 14f, 20f, 20f);

            text = Text(toast, "Label", "", GirdapFont.SairaBold, 18f, Girdap.ConfirmFg,
                TextAlignmentOptions.MidlineLeft);
            PlaceMiddleLeft(text.rectTransform, 42f, 360f, 40f);

            // The whole strip dismisses it — the mockup's toast carries no X of its own.
            UiImage hit = Image(toast, "Hit", null, Color.clear);
            Stretch(hit.rectTransform);
            hit.raycastTarget = true;
            close = hit.gameObject.AddComponent<UiButton>();
            close.transition = Selectable.Transition.None;
            close.targetGraphic = hit;

            toast.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ helpers

        private static TextMeshProUGUI StatsNumberCell(Transform parent, string name, int column,
            string sample)
        {
            TextMeshProUGUI text = Text(parent, name, sample, GirdapFont.ChakraBold, 20f,
                Girdap.Text, TextAlignmentOptions.Center);
            Place(text.rectTransform, Col[column], 0f, Col[column + 1] - Col[column], StatsRowH);
            return text;
        }

        private static TextMeshProUGUI StatsTextCell(Transform parent, string name, int column,
            string sample)
        {
            float x = Col[column] + StatsCellPad;
            TextMeshProUGUI text = Text(parent, name, sample, GirdapFont.SairaSemiBold, 17f,
                Girdap.Muted, TextAlignmentOptions.MidlineLeft);
            Place(text.rectTransform, x, 0f, Col[column + 1] - StatsCellPad - x, StatsRowH);
            return text;
        }

        /// <summary>
        /// CSS <c>transform: skewX(θ)</c> as a Z rotation around the bar's centre (the theme's
        /// controller pips and title bars). ⚠️ A uGUI rect cannot be sheared; on bars this narrow
        /// the two are indistinguishable.
        /// </summary>
        private static UiShape StatsSkewBar(Transform parent, string name, float x, float y,
            float w, float h, Color color, float skewDeg)
        {
            UiShape bar = Shape(parent, name);
            RectTransform rt = bar.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x + w * 0.5f, -(y + h * 0.5f));
            rt.localEulerAngles = new Vector3(0f, 0f, skewDeg);
            bar.Chamfer(0f).Fill(color);
            return bar;
        }

        private static UiButtonStyle StatsSmallButton(Transform parent, string name, string label,
            UiButtonKind kind, float x, float y, float w, bool hold = false)
        {
            return Button(parent, name, label, kind, x, y, w, StatsBtnH, null, null, StatsBtnFont,
                GirdapFont.SairaBold, StatsBtnChamfer, hold);
        }

        /// <summary>Square icon-only inline button (CSS <c>.btn.sm.sq</c>).</summary>
        private static UiButtonStyle StatsIconButton(Transform parent, string name, string icon,
            float x, float y, float size)
        {
            UiButtonStyle style = Button(parent, name, "", UiButtonKind.Normal, x, y, size, size,
                icon, null, StatsBtnFont, GirdapFont.SairaBold, StatsBtnChamfer);

            // The Button helper centres "icon + gap + label" as a block; with an empty label the
            // gap still counts, so the glyph would sit left of centre.
            var glyph = style.transform.Find("Icon") as RectTransform;
            if (glyph != null)
            {
                PlaceMiddleLeft(glyph, (size - 16f) * 0.5f, 16f, 16f);
            }

            return style;
        }

        /// <summary>Inline name editor field (CSS <c>.select</c> surface, inline height).</summary>
        private static TMP_InputField StatsInputField(Transform parent, string name, float x,
            float w)
        {
            RectTransform field = Node(parent, name);
            Place(field, x, 0f, w, StatsBtnH);

            UiShape bg = Shape(field, "Bg");
            Stretch(bg.rectTransform);
            bg.Chamfer(StatsBtnChamfer).Outline(1f, StatsFieldBd).Fill(StatsFieldBg);
            bg.raycastTarget = true;

            RectTransform area = Node(field, "Text Area");
            Stretch(area, 8f, 4f, 8f, 4f);
            area.gameObject.AddComponent<RectMask2D>();

            TextMeshProUGUI text = Text(area, "Text", "", GirdapFont.SairaBold, 17f, Girdap.Text,
                TextAlignmentOptions.MidlineLeft);
            Stretch(text.rectTransform);

            TextMeshProUGUI placeholder = Text(area, "Placeholder", "ad", GirdapFont.SairaBold, 17f,
                Girdap.Faint, TextAlignmentOptions.MidlineLeft);
            Stretch(placeholder.rectTransform);

            var input = field.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = area;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.targetGraphic = bg;
            input.transition = Selectable.Transition.None;
            // ⚠️ Font/size/caret are NOT set through the input field's own properties: their setters
            // run UpdateLabel() on a component that has not initialised yet during prefab
            // generation. The child texts already carry the theme font from Text().
            return input;
        }

        /// <summary>
        /// Text width in px. ⚠️ Measured on a throwaway TMP rather than guessed: the inline button
        /// strip and the info pairs are packed to the pixel, and a wrong width shifts a whole row.
        /// </summary>
        private static float StatsMeasure(string text, GirdapFont font, float size, float spacingEm)
        {
            var go = new GameObject("Measure");
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
            float width = Mathf.Ceil(tmp.GetPreferredValues(text ?? "").x);
            Object.DestroyImmediate(go);
            return width;
        }

        private static void StatsClearChildren(GameObject root)
        {
            var children = new System.Collections.Generic.List<Transform>();
            foreach (Transform child in root.transform)
            {
                children.Add(child);
            }

            for (int i = 0; i < children.Count; i++)
            {
                Object.DestroyImmediate(children[i].gameObject);
            }
        }

        private static void StatsWire(SerializedObject so, string field, Object value)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[Girdap] {so.targetObject.GetType().Name}.{field} alanı yok.");
                return;
            }

            property.objectReferenceValue = value;
        }

        private static void StatsWireArray<T>(SerializedObject so, string field, T[] values)
            where T : Object
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[Girdap] {so.targetObject.GetType().Name}.{field} alanı yok.");
                return;
            }

            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }
    }
}
