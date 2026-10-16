using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.Core.UI;

// `Image x = Image(...)` gives CS0119 — simple name lookup finds the member group before the type.
using UiImage = UnityEngine.UI.Image;

namespace VortexArena.App.Editor
{
    /// <summary>
    /// Zombie mode's in-match HUD (<c>Docs/Gelistirici/Arayuz/zombi.html</c>, "kan damlası" theme):
    /// head-locked strip (clock · scores · can paneli · status · team) · big between-waves countdown
    /// · short toast · DEV barı, built
    /// into <c>Assets/Modes/Zombie/UI/ZombieHud.prefab</c>.
    /// <para>
    /// ⚠️ <b>Replaces the Lokanta strip copied into this prefab.</b> The new strip ADOPTS the removed
    /// node's RectTransform and its chrome (nested Canvas with its sorting override,
    /// <see cref="HeadLockedHud"/>), so the panel keeps sitting exactly where the player learned to
    /// look even though it is twice as tall.
    /// </para>
    /// <para>
    /// ⚠️ The Zombie assembly is NOT referenced from here (editor asmdef, same as the player HUD):
    /// <c>ZombieClientController</c>'s own fields are written by NAME through
    /// <see cref="SerializedObject"/> on the <see cref="ModeHudBase"/> component. A renamed field
    /// unbinds silently — rename it in both places.
    /// </para>
    /// </summary>
    public static partial class GirdapUiBuilder
    {
        private const string ZbHudPrefab = "Assets/Modes/Zombie/UI/ZombieHud.prefab";
        private const string ZbStripName = "ZombieStrip";
        private const string ZbNoticeName = "WaveNotice";
        private const string ZbToastName = "Toast";
        private const string ZbBossName = "BossBar";
        private const string ZbHealthName = "HealthPanel";

        /// <summary>The node the strip is grown from; a Lokanta-era copy sits under this name.</summary>
        private const string ZbLegacyStrip = "LokantaStrip";

        // ---- strip (.zs.za) — y is measured downward like `Place`
        private const float ZbCellW = 150f;
        private const float ZbCellH = 124f;
        private const float ZbDividerW = 2f;
        private const float ZbStripW = ZbCellW * 4f + ZbDividerW * 3f; // 606
        private const float ZbClockW = 240f;
        private const float ZbClockH = 68f;
        private const float ZbRowY = ZbClockH + 10f;

        // ---- can paneli (.zhp) — the player's own health, a full-width row under the scores
        private const float ZbHpW = ZbStripW;
        private const float ZbHpH = 104f;
        private const float ZbHpTextX = 18f;
        private const float ZbHpTextW = 140f;
        private const float ZbHpTrackX = 162f;
        private const float ZbHpTrackW = ZbHpW - ZbHpTrackX - 18f; // 426
        private const float ZbHpTrackH = 34f;

        private const float ZbHpY = ZbRowY + ZbCellH + 12f; // 214
        private const float ZbStatusY = ZbHpY + ZbHpH + 12f; // 330
        private const float ZbStatusH = 44f;

        // ---- team strip (.zs team list): header + one row per OTHER player
        private const int ZbTeamRows = 8;
        private const float ZbTeamRowH = 28f;
        private const float ZbTeamHeadH = 30f;
        private const float ZbTeamH = ZbTeamHeadH + ZbTeamRowH * ZbTeamRows; // 254
        private const float ZbTeamY = ZbStatusY + ZbStatusH + 12f; // 386
        private const float ZbTeamNameW = 330f;
        private const float ZbContentH = ZbTeamY + ZbTeamH; // 640

        /// <summary>Strip height the adopted head-lock aim is tuned for; the pivot is CENTRED, so
        /// anything taller is pushed back down to keep the clock + row block on the gaze line.</summary>
        private const float ZbAimH = 524f;

        // Fallback rect for a re-run after the legacy strip is gone AND this one was deleted by hand;
        // matches what the copied Lokanta strip carried.
        private const float ZbFallbackH = 176f;

        // ---- centre countdown (.za-cd)
        private const float ZbNoticeTitleY = 190f;
        private const float ZbNoticeSubY = 210f;

        /// <summary>The clear phase's weapon line, under the sub line (CSS <c>.zcd .u</c>).</summary>
        private const float ZbNoticeUnlockY = 290f;

        // ---- toast (.zt.za)
        private const float ZbToastW = 780f;
        private const float ZbToastH = 128f;

        /// <summary>Top margin the big toast line gets while the small line is shown.</summary>
        private const float ZbToastSmallBand = 44f;

        // ---- DEV barı (final stand boss bar)
        private const float ZbBossW = 720f;
        private const float ZbBossH = 124f;
        private const float ZbBossTrackH = 22f;

        /// <summary>Pitch of the boss bar's head lock: above the strip, so both are readable at once.</summary>
        private const float ZbBossPitch = 18f;

        /// <summary>Gap between the track's border and the bars inside it.</summary>
        private const float ZbHpBarInset = 3f;

        /// <summary>Segment lines over the bar — one per 10%, so the ninth is the last.</summary>
        private const int ZbHpTicks = 9;

        private const float ZbHpTickW = 2f;

        /// <summary>⚠️ A WHITE sprite, not <c>HealthBar_Fill.png</c>: an Image tint MULTIPLIES the
        /// sprite, and that one is green — a red tint on it comes out brown.</summary>
        private const string ZbHpBarSprite = "RoundedRect_8.png";

        /// <summary>Damage trail behind the fill (bone, slightly see-through).</summary>
        private static readonly Color ZbHpTrail = ZbFade(Kan.BoneWarm, 0.8f);

        private static readonly Color ZbHpTick = ZbFade(Kan.BloodLo, 0.6f);

        /// <summary>Vertical darkening over the bar: BloodHi reads as Blood at the bottom.</summary>
        private static readonly Color ZbHpShade = ZbFade(Kan.BloodLo, 0.55f);

        /// <summary>Fully transparent — a glow-only shape still needs a fill colour.</summary>
        private static readonly Color ZbHpClear = ZbFade(Kan.BloodLo, 0f);

        private static readonly Color ZbHpGlow = ZbFade(Kan.BloodHi, 0.65f);

        private static Color ZbFade(Color c, float a)
        {
            return new Color(c.r, c.g, c.b, a);
        }

        [MenuItem("Tools/VortexArena/UI/Girdap/Yalnız Zombi HUD")]
        public static void BuildZombieHudMenu()
        {
            BuildAssets();
            BuildZombieHud();
            AssetDatabase.SaveAssets();
        }

        public static void BuildZombieHud()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ZbHudPrefab) == null)
            {
                Debug.LogWarning($"[Kan] {ZbHudPrefab} yok — zombi HUD'ı üretilemedi.");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(ZbHudPrefab);
            try
            {
                Transform t = root.transform;
                Transform legacy = t.Find(ZbLegacyStrip);

                RectTransform strip = VhudClaim(t, ZbStripName);
                PlaceCenter(strip, 0f, 0f, ZbStripW, ZbFallbackH);
                if (legacy != null)
                {
                    // Shared with the Lokanta builder on purpose: the adoption rules (sorting override
                    // copied by hand, visual components skipped) must not exist twice.
                    LkAdoptRect((RectTransform)legacy, strip);
                    LkAdoptChrome(legacy, strip);
                }

                RectTransform notice = VhudClaim(t, ZbNoticeName);
                RectTransform toast = VhudClaim(t, ZbToastName);
                RectTransform boss = VhudClaim(t, ZbBossName);

                // Drops the legacy strip and anything left from an earlier layout — including the
                // health panel's old root node, whose own HeadLockedHud would keep drawing it.
                VhudKeepOnly(t, ZbStripName, ZbNoticeName, ZbToastName, ZbBossName);
                VhudKeepOnly(strip);
                VhudKeepOnly(notice);
                VhudKeepOnly(toast);
                VhudKeepOnly(boss);

                strip.sizeDelta = new Vector2(ZbStripW, ZbContentH);

                // Pivot is centred, so extra height would lift the TOP edge too: give it back as a
                // downward shift, otherwise the clock + row block climbs off the gaze line.
                strip.anchoredPosition -= new Vector2(0f, (ZbContentH - ZbAimH) * 0.5f);

                RectTransform clock = ZbBuildClock(strip, out TextMeshProUGUI time,
                    out GameObject alarmPlate);
                ZbCells cells = ZbBuildRow(strip);
                UiImage healthFill = ZbBuildHealthPanel(strip);
                StatusPlate status = ZbBuildStatus(strip);
                ZbTeam team = ZbBuildTeamStrip(strip);
                ZbNotice centre = ZbBuildNotice(notice);
                ZbToast toastParts = ZbBuildToast(toast);
                ZbBoss bossParts = ZbBuildBossBar(boss);

                ZbBindHud(root, clock, time, alarmPlate, cells, healthFill, status,
                    team, centre, toastParts, bossParts);
                PrefabUtility.SaveAsPrefabAsset(root, ZbHudPrefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ------------------------------------------------------------------- clock

        /// <summary>CSS <c>.za .clock</c>: rusted plate with the remaining time; the red alarm plate on
        /// top is raised by the controller in the last seconds.</summary>
        private static RectTransform ZbBuildClock(Transform parent, out TextMeshProUGUI time,
            out GameObject alarmPlate)
        {
            RectTransform clock = Node(parent, "Clock");
            PlaceTopCenter(clock, 0f, ZbClockW, ZbClockH);

            UiShape plate = Shape(clock, "Plate");
            Stretch(plate.rectTransform);
            plate.Radius(Kan.ClockRadius)
                .Outline(Kan.Border1, Kan.Border)
                .Fill(Kan.ClockA, Kan.ClockB, UiGradientMode.Vertical);

            UiShape alarm = Shape(clock, "AlarmPlate");
            Stretch(alarm.rectTransform);
            alarm.Radius(Kan.ClockRadius)
                .Outline(Kan.Border1, Kan.BloodHi)
                .Fill(Kan.Blood, Kan.BloodLo, UiGradientMode.Vertical);
            alarm.gameObject.SetActive(false);
            alarmPlate = alarm.gameObject;

            time = Text(clock, "Time", "3:12", GirdapFont.BarlowMedium, 46f, Kan.Bone,
                TextAlignmentOptions.Center);
            Stretch(time.rectTransform, 20f, 0f, 20f, 0f);
            return clock;
        }

        // --------------------------------------------------------------------- row

        /// <summary>The row's four value targets; grouping them keeps the binder's signature readable.</summary>
        private struct ZbCells
        {
            public TextMeshProUGUI wave;
            public TextMeshProUGUI remaining;
            public TextMeshProUGUI squad;
            public TextMeshProUGUI self;
        }

        /// <summary>CSS <c>.za .row</c>: DALGA (blood cell) · SÜRE · EKİP · SEN on one rusted plate.</summary>
        private static ZbCells ZbBuildRow(Transform parent)
        {
            RectTransform row = Node(parent, "Row");
            PlaceTopCenter(row, ZbRowY, ZbStripW, ZbCellH);

            UiShape plate = Shape(row, "Plate");
            Stretch(plate.rectTransform);
            plate.Radius(Kan.PlateRadius)
                .Outline(Kan.Border1, Kan.Border)
                .Fill(Kan.PlateA, Kan.PlateB, UiGradientMode.Vertical)
                .InnerGlow(40f, Kan.InnerBlood)
                .Glow(30f, Kan.Drop, new Vector2(0f, -10f));

            var cells = new ZbCells();
            float x = 0f;

            // DALGA — the only cell with its own fill; its left corners follow the plate's radius.
            UiShape wave = Shape(row, "CellWave");
            Place(wave.rectTransform, x, 0f, ZbCellW, ZbCellH);
            wave.Radius(Kan.PlateRadius - Kan.Border1, 0f, 0f, Kan.PlateRadius - Kan.Border1)
                .Fill(Kan.BloodHi, Kan.BloodLo, UiGradientMode.Vertical);
            cells.wave = ZbCellContent(wave.transform, "DALGA", "4", GirdapFont.RubikWetPaint,
                GirdapFont.RubikWetPaint, 84f, Kan.BoneWarm, Kan.BoneWarm, true);
            x += ZbCellW;

            ZbDivider(row, "DividerWave", x);
            x += ZbDividerW;

            // SÜRE, not a zombie count: a wave ends on its CLOCK, and the number the squad plans around
            // is how long the pressure lasts. ⚠️ Smaller value font than the other cells — `m:ss` is four
            // glyphs in a 150 px cell.
            RectTransform remaining = Node(row, "CellRemaining");
            Place(remaining, x, 0f, ZbCellW, ZbCellH);
            cells.remaining = ZbCellContent(remaining, "SÜRE", "2:30", GirdapFont.SairaBold,
                GirdapFont.SairaExtraBold, 56f, Kan.Label, Kan.Alive, false);
            x += ZbCellW;

            ZbDivider(row, "DividerRemaining", x);
            x += ZbDividerW;

            RectTransform squad = Node(row, "CellSquad");
            Place(squad, x, 0f, ZbCellW, ZbCellH);
            cells.squad = ZbCellContent(squad, "EKİP", "87", GirdapFont.SairaBold,
                GirdapFont.SairaExtraBold, 68f, Kan.Label, Kan.Bone, false);
            x += ZbCellW;

            ZbDivider(row, "DividerSquad", x);
            x += ZbDividerW;

            RectTransform self = Node(row, "CellSelf");
            Place(self, x, 0f, ZbCellW, ZbCellH);
            cells.self = ZbCellContent(self, "SEN", "23", GirdapFont.SairaBold,
                GirdapFont.SairaExtraBold, 68f, Kan.Label, Kan.Bone, false);

            return cells;
        }

        /// <summary>Label (26 px) over the value; returns the VALUE, which is the only runtime target.</summary>
        private static TextMeshProUGUI ZbCellContent(Transform cell, string label, string sample,
            GirdapFont labelFont, GirdapFont valueFont, float valueSize, Color labelColor,
            Color valueColor, bool drop)
        {
            TextMeshProUGUI labelText = Text(cell, "Label", label, labelFont, 26f, labelColor,
                TextAlignmentOptions.Center, 0.06f);
            Place(labelText.rectTransform, 0f, 10f, ZbCellW, 34f);

            TextMeshProUGUI value = Text(cell, "Value", sample, valueFont, valueSize, valueColor,
                TextAlignmentOptions.Center);
            Place(value.rectTransform, 0f, 44f, ZbCellW, ZbCellH - 44f);

            if (drop)
            {
                // CSS `text-shadow: 0 3px 0 rgba(0,0,0,.5)`; the offset is em-ish, not px, and one
                // preset asset serves both lines (it is keyed by font + colour only).
                TextShadow(labelText, Kan.TextDrop, 0.05f);
                TextShadow(value, Kan.TextDrop, 0.05f);
            }

            return value;
        }

        private static void ZbDivider(Transform parent, string name, float x)
        {
            UiShape divider = Shape(parent, name);
            Place(divider.rectTransform, x, 0f, ZbDividerW, ZbCellH);
            divider.Fill(Kan.Divider).Antialias(false);
        }

        // ------------------------------------------------------------------ health

        /// <summary>
        /// CSS <c>.zhp</c>: the player's own health as a full-width strip row right under the scores —
        /// big number, thick segmented bar, damage trail, low-health pulse. It is a row of its own and
        /// not a fifth cell: health is the readout the player glances at mid-fight and a cell-sized
        /// line is not found in time.
        /// <para>⚠️ <see cref="HealthStrip"/> is deliberately NOT used: it paints itself from the
        /// <see cref="Girdap"/> threshold palette, which would put a second, foreign red on a blood
        /// themed panel.</para>
        /// <para>⚠️ The number is written by <c>ZombieHealthPanel</c>, not by the base's
        /// <c>healthText</c>: the base writes "CAN 72" as one line, and here the label and the number
        /// are two sizes in two fonts. Binding both would give the field two writers.</para>
        /// </summary>
        private static UiImage ZbBuildHealthPanel(Transform parent)
        {
            RectTransform panel = Node(parent, ZbHealthName);
            PlaceTopCenter(panel, ZbHpY, ZbHpW, ZbHpH);

            // First child: the only layer the low-health pulse fades in, so it must sit behind the
            // plate. Visible in the prefab, zeroed by the component on the first frame.
            UiShape glow = Shape(panel, "Glow");
            Stretch(glow.rectTransform);
            glow.Radius(Kan.PlateRadius).Fill(ZbHpClear).Glow(40f, ZbHpGlow);

            UiShape plate = Shape(panel, "Plate");
            Stretch(plate.rectTransform);
            plate.Radius(Kan.PlateRadius)
                .Outline(Kan.Border1, Kan.Border)
                .Fill(Kan.PlateA, Kan.PlateB, UiGradientMode.Vertical)
                .InnerGlow(40f, Kan.InnerBlood)
                .Glow(30f, Kan.Drop, new Vector2(0f, -10f));

            TextMeshProUGUI label = Text(plate.transform, "Label", "CAN", GirdapFont.SairaBold, 22f,
                Kan.BoneWarm, TextAlignmentOptions.Left, 0.14f);
            Place(label.rectTransform, ZbHpTextX, 6f, ZbHpTextW, 24f);

            TextMeshProUGUI value = Text(plate.transform, "Value", "100", GirdapFont.RubikWetPaint,
                72f, Kan.Bone, TextAlignmentOptions.Left);
            Place(value.rectTransform, ZbHpTextX, 30f, ZbHpTextW, 66f);
            TextShadow(value, Kan.TextDrop, 0.05f);

            UiShape track = Shape(plate.transform, "Track");
            PlaceMiddleLeft(track.rectTransform, ZbHpTrackX, ZbHpTrackW, ZbHpTrackH);
            track.Radius(Kan.PlateRadius).Outline(Kan.Border1, Kan.Border).Fill(Kan.BloodLo);

            // Before the fill: the trail is what shows THROUGH the ground the fill just gave up.
            UiImage trail = ZbHpBar(track.transform, "Trail", ZbHpTrail);
            UiImage fill = ZbHpBar(track.transform, "Fill", Kan.BloodHi);

            UiShape shade = Shape(track.transform, "Shade");
            Stretch(shade.rectTransform, ZbHpBarInset, ZbHpBarInset, ZbHpBarInset, ZbHpBarInset);
            shade.Radius(Kan.PlateRadius - ZbHpBarInset)
                .Fill(ZbHpClear, ZbHpShade, UiGradientMode.Vertical);

            ZbHpTickMarks(track.transform);

            // Pulse ring over the track, same deal as the glow: alpha comes from the component.
            UiShape pulse = Shape(track.transform, "Pulse");
            Stretch(pulse.rectTransform);
            pulse.Radius(Kan.PlateRadius)
                .Fill(ZbHpClear)
                .Outline(Kan.Border1, Kan.BloodHi)
                .Glow(18f, ZbHpGlow);

            ZbBindHealthPanel(panel.gameObject, value, fill, trail, glow, pulse);
            return fill;
        }

        /// <summary>One bar layer inside the track (trail or fill).
        /// ⚠️ <c>Image.type = Filled</c> needs a SPRITE; with a null sprite the component falls back to
        /// a plain quad and <c>fillAmount</c> is ignored (the bar would never move).</summary>
        private static UiImage ZbHpBar(Transform track, string name, Color color)
        {
            UiImage bar = Image(track, name, LoadSprite(ZbHpBarSprite), color, UiImage.Type.Filled);
            bar.fillMethod = UiImage.FillMethod.Horizontal;
            bar.fillOrigin = (int)UiImage.OriginHorizontal.Left;
            bar.fillAmount = 1f;
            Stretch(bar.rectTransform, ZbHpBarInset, ZbHpBarInset, ZbHpBarInset, ZbHpBarInset);
            return bar;
        }

        /// <summary>Thin dark lines over the bar, one per 10% — the eye reads "iki dilim kaldı"
        /// faster than it reads a bar length.</summary>
        private static void ZbHpTickMarks(Transform track)
        {
            RectTransform ticks = Node(track, "Ticks");
            Stretch(ticks, ZbHpBarInset, ZbHpBarInset, ZbHpBarInset, ZbHpBarInset);

            float inner = ZbHpTrackW - ZbHpBarInset * 2f;
            float step = inner / (ZbHpTicks + 1);
            for (int i = 1; i <= ZbHpTicks; i++)
            {
                UiShape tick = Shape(ticks, "Tick" + i);
                Place(tick.rectTransform, step * i - ZbHpTickW * 0.5f, 0f, ZbHpTickW,
                    ZbHpTrackH - ZbHpBarInset * 2f);
                tick.Fill(ZbHpTick).Antialias(false);
            }
        }

        /// <summary>
        /// Adds the mode's own <c>ZombieHealthPanel</c> and writes its fields by name.
        /// ⚠️ The type is resolved by STRING: this editor assembly does not reference the Zombie
        /// assembly (same reason the base's fields go through <see cref="SerializedObject"/>), so a
        /// renamed type or field unbinds silently — rename it in both places.
        /// </summary>
        private static void ZbBindHealthPanel(GameObject panel, TextMeshProUGUI value, UiImage fill,
            UiImage trail, UiShape glow, UiShape pulse)
        {
            System.Type type = System.Type.GetType(
                "VortexArena.Modes.Zombie.ZombieHealthPanel, VortexArena.Modes.Zombie");
            if (type == null)
            {
                Debug.LogError("[Kan] ZombieHealthPanel tipi bulunamadı — can paneli canlanmaz.");
                return;
            }

            Component comp = panel.GetComponent(type);
            if (comp == null)
            {
                comp = panel.AddComponent(type);
            }

            var so = new SerializedObject(comp);
            HudSet(so, "source", fill);
            HudSet(so, "trail", trail);
            HudSet(so, "valueText", value);
            HudSet(so, "glow", glow);
            HudSet(so, "pulse", pulse);
            HudSetFloat(so, "trailSeconds", 0.6f);
            HudSetFloat(so, "lowFraction", 0.3f);
            HudSetFloat(so, "pulseHz", 1.4f);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ status

        /// <summary><see cref="StatusPlate"/> in the skin's clothes: rusted plate, bone words.</summary>
        private static StatusPlate ZbBuildStatus(Transform parent)
        {
            RectTransform rootRt = Node(parent, "Status");
            PlaceTopCenter(rootRt, ZbStatusY, ZbStripW, ZbStatusH);

            // Body is what StatusPlate resizes to the text, so the plate STRETCHES to it.
            RectTransform body = Node(rootRt, "Body");
            PlaceCenter(body, 0f, 0f, ZbStripW, ZbStatusH);

            UiShape plate = Shape(body, "Plate");
            Stretch(plate.rectTransform);
            plate.Radius(Kan.ClockRadius)
                .Outline(Kan.Border1, Kan.Border)
                .Fill(Kan.ClockA, Kan.ClockB, UiGradientMode.Vertical);

            UiImage icon = Icon(body, "Icon", "Warn", 22f, Kan.Label);
            PlaceMiddleLeft(icon.rectTransform, 14f, 22f, 22f);
            TextMeshProUGUI text = Text(body, "Text", "", GirdapFont.SairaBold, 24f, Kan.Label,
                TextAlignmentOptions.MidlineLeft);
            Stretch(text.rectTransform, 44f, 0f, 16f, 0f);

            var comp = rootRt.gameObject.AddComponent<StatusPlate>();
            var so = new SerializedObject(comp);
            HudSet(so, "body", body.gameObject);
            HudSet(so, "plate", body);
            HudSet(so, "icon", icon);
            HudSet(so, "text", text);
            HudSetFloat(so, "padLeft", 14f);
            HudSetFloat(so, "padRight", 16f);
            HudSetFloat(so, "gap", 8f);
            HudSetFloat(so, "iconSize", 22f);
            HudSetFloat(so, "maxWidth", ZbStripW);
            so.ApplyModifiedPropertiesWithoutUndo();
            body.gameObject.SetActive(false);
            return comp;
        }

        // -------------------------------------------------------------- team strip

        private struct ZbTeam
        {
            public GameObject root;
            public Object[] rows;
            public Object[] names;
            public Object[] states;
        }

        /// <summary>EKİP list: one row per OTHER player with their name and state (AYAKTA · DÜŞTÜ ·
        /// KALDIRILIYOR %NN). Rows are built EMPTY and fixed in place — the controller only writes
        /// text and toggles them, so there is no layout group to re-flow (Yapma-Listesi).</summary>
        private static ZbTeam ZbBuildTeamStrip(Transform parent)
        {
            RectTransform team = Node(parent, "Team");
            PlaceTopCenter(team, ZbTeamY, ZbStripW, ZbTeamH);

            UiShape plate = Shape(team, "Plate");
            Stretch(plate.rectTransform);
            plate.Radius(Kan.PlateRadius)
                .Outline(Kan.Border1, Kan.Border)
                .Fill(Kan.PlateA, Kan.PlateB, UiGradientMode.Vertical);

            TextMeshProUGUI head = Text(team, "Head", "EKİP", GirdapFont.SairaBold, 22f, Kan.Label,
                TextAlignmentOptions.MidlineLeft, 0.14f);
            Place(head.rectTransform, 16f, 4f, ZbStripW - 32f, ZbTeamHeadH - 4f);

            var rows = new Object[ZbTeamRows];
            var names = new Object[ZbTeamRows];
            var states = new Object[ZbTeamRows];

            for (int i = 0; i < ZbTeamRows; i++)
            {
                RectTransform rowRt = Node(team, "Row" + i);
                Place(rowRt, 0f, ZbTeamHeadH + i * ZbTeamRowH, ZbStripW, ZbTeamRowH);

                TextMeshProUGUI name = Text(rowRt, "Name", "OYUNCU", GirdapFont.SairaBold, 22f,
                    Kan.Bone, TextAlignmentOptions.MidlineLeft);
                Place(name.rectTransform, 16f, 0f, ZbTeamNameW, ZbTeamRowH);

                TextMeshProUGUI state = Text(rowRt, "State", "AYAKTA", GirdapFont.SairaExtraBold,
                    22f, Kan.Bone, TextAlignmentOptions.MidlineRight, 0.08f);
                Place(state.rectTransform, ZbTeamNameW + 16f, 0f,
                    ZbStripW - ZbTeamNameW - 32f, ZbTeamRowH);

                rowRt.gameObject.SetActive(false);
                rows[i] = rowRt.gameObject;
                names[i] = name;
                states[i] = state;
            }

            team.gameObject.SetActive(false);
            return new ZbTeam
            {
                root = team.gameObject,
                rows = rows,
                names = names,
                states = states
            };
        }

        // ------------------------------------------------------- centre countdown

        private struct ZbNotice
        {
            public GameObject root;
            public GameObject dim;
            public TextMeshProUGUI title;
            public TextMeshProUGUI number;
            public TextMeshProUGUI sub;
            public TextMeshProUGUI unlocks;
        }

        /// <summary>
        /// CSS <c>.za-cd</c>: "DALGA N" · the big number · HAZIRLAN, in the middle of the view.
        /// <para>⚠️ Its own <see cref="HeadLockedHud"/> — the strip's head lock is the documented
        /// exception to <see cref="HudFollow"/>, and this one is the same trade for the same reason:
        /// the number is up for a few seconds and must be where the player is already looking. It is
        /// NOT a permanent panel, so the "do not widen the exception" rule still holds.</para>
        /// </summary>
        private static ZbNotice ZbBuildNotice(RectTransform notice)
        {
            Stretch(notice);
            ZbHeadLock(notice, 0f);

            // First child: the veil has to render BEHIND the words.
            UiImage dim = Image(notice, "Dim", null, Kan.Dim);
            PlaceCenter(dim.rectTransform, 0f, 0f, 4000f, 2800f);
            dim.gameObject.SetActive(false);

            TextMeshProUGUI title = Text(notice, "Title", "DALGA 5", GirdapFont.RubikWetPaint, 96f,
                Kan.BloodHi, TextAlignmentOptions.Center);
            PlaceCenter(title.rectTransform, 0f, -ZbNoticeTitleY, 1200f, 130f);
            TextShadow(title, Kan.BloodShadow, 0.06f);

            TextMeshProUGUI number = Text(notice, "Number", "3", GirdapFont.RubikWetPaint, 280f,
                Kan.BoneBright, TextAlignmentOptions.Center);
            PlaceCenter(number.rectTransform, 0f, 0f, 900f, 340f);
            TextShadow(number, Kan.BloodLo, 0.035f);

            TextMeshProUGUI sub = Text(notice, "Sub", "HAZIRLAN", GirdapFont.SairaBold, 40f,
                Kan.Label, TextAlignmentOptions.Center, 0.18f);
            PlaceCenter(sub.rectTransform, 0f, ZbNoticeSubY, 900f, 60f);

            // Only the clear phase fills this; the countdown leaves it empty (the controller clears it).
            TextMeshProUGUI unlocks = Text(notice, "Unlocks", "P90 · AK-47", GirdapFont.SairaBold, 56f,
                Kan.BoneBright, TextAlignmentOptions.Center, 0.04f);
            PlaceCenter(unlocks.rectTransform, 0f, ZbNoticeUnlockY, 1600f, 80f);

            notice.gameObject.SetActive(false);
            return new ZbNotice
            {
                root = notice.gameObject,
                dim = dim.gameObject,
                title = title,
                number = number,
                sub = sub,
                unlocks = unlocks
            };
        }

        // ------------------------------------------------------------------- toast

        private struct ZbToast
        {
            public GameObject root;
            public TextMeshProUGUI small;
            public TextMeshProUGUI big;
        }

        /// <summary>CSS <c>.zt.za</c>: blood plate slightly above the view centre.</summary>
        private static ZbToast ZbBuildToast(RectTransform toast)
        {
            Stretch(toast);
            ZbHeadLock(toast, 10f);

            UiShape plate = Shape(toast, "Plate");
            PlaceCenter(plate.rectTransform, 0f, 0f, ZbToastW, ZbToastH);
            plate.Radius(Kan.ClockRadius)
                .Fill(Kan.BloodHi, Kan.Blood, UiGradientMode.Vertical)
                .Glow(30f, Kan.Drop, new Vector2(0f, -10f));

            TextMeshProUGUI small = Text(plate.transform, "Small", "DALGA 6",
                GirdapFont.RubikWetPaint, 26f, Kan.BoneWarm, TextAlignmentOptions.Center, 0.14f);
            Place(small.rectTransform, 0f, 14f, ZbToastW, 34f);

            // Stretched + centred: the controller reserves the small line's band with `margin`, so a
            // single-line toast needs no second layout.
            TextMeshProUGUI big = Text(plate.transform, "Big", "DALGA 5 BAŞLADI",
                GirdapFont.RubikWetPaint, 48f, Kan.BoneWarm, TextAlignmentOptions.Center);
            Stretch(big.rectTransform, 24f, 0f, 24f, 0f);

            toast.gameObject.SetActive(false);
            return new ZbToast
            {
                root = toast.gameObject,
                small = small,
                big = big
            };
        }

        // ---------------------------------------------------------------- DEV barı

        private struct ZbBoss
        {
            public GameObject root;
            public TextMeshProUGUI title;
            public TextMeshProUGUI time;
            public UiImage fill;
        }

        /// <summary>The final stand's boss bar: DEV · its health · the clock left, above the strip. Its
        /// own head lock for the same reason the centre notice has one — it is up for the last minute
        /// and a half of the run and must be where the player is already looking.</summary>
        private static ZbBoss ZbBuildBossBar(RectTransform boss)
        {
            Stretch(boss);
            ZbHeadLock(boss, ZbBossPitch);

            UiShape plate = Shape(boss, "Plate");
            PlaceCenter(plate.rectTransform, 0f, 0f, ZbBossW, ZbBossH);
            plate.Radius(Kan.PlateRadius)
                .Outline(Kan.Border1, Kan.BloodHi)
                .Fill(Kan.PlateA, Kan.PlateB, UiGradientMode.Vertical)
                .Glow(30f, Kan.Drop, new Vector2(0f, -10f));

            TextMeshProUGUI title = Text(plate.transform, "Title", "DEV", GirdapFont.RubikWetPaint,
                44f, Kan.BloodHi, TextAlignmentOptions.Center, 0.06f);
            Place(title.rectTransform, 0f, 8f, ZbBossW, 56f);
            TextShadow(title, Kan.BloodShadow, 0.05f);

            UiShape track = Shape(plate.transform, "Track");
            Place(track.rectTransform, 24f, 64f, ZbBossW - 48f, ZbBossTrackH);
            track.Radius(4f).Outline(2f, Kan.Border).Fill(Kan.BloodLo);

            // ⚠️ Filled Image needs a SPRITE (same trap as the health bar): with a null sprite
            // fillAmount is ignored and the bar never moves.
            UiImage fill = Image(track.transform, "Fill", LoadSprite("HealthBar_Fill.png"),
                Kan.BloodHi, UiImage.Type.Filled);
            fill.fillMethod = UiImage.FillMethod.Horizontal;
            fill.fillOrigin = (int)UiImage.OriginHorizontal.Left;
            fill.fillAmount = 1f;
            Stretch(fill.rectTransform, 3f, 3f, 3f, 3f);

            TextMeshProUGUI time = Text(plate.transform, "Time", "SON DİRENİŞ · 01:30",
                GirdapFont.SairaBold, 24f, Kan.Label, TextAlignmentOptions.Center, 0.14f);
            Place(time.rectTransform, 0f, 90f, ZbBossW, 30f);

            boss.gameObject.SetActive(false);
            return new ZbBoss
            {
                root = boss.gameObject,
                title = title,
                time = time,
                fill = fill
            };
        }

        /// <summary>Adds (or re-uses) a head lock aiming <paramref name="pitch"/> degrees above the gaze
        /// axis at the strip's distance — same apparent size as the strip at any angle.</summary>
        private static void ZbHeadLock(RectTransform rt, float pitch)
        {
            var headLock = rt.GetComponent<HeadLockedHud>();
            if (headLock == null)
            {
                headLock = rt.gameObject.AddComponent<HeadLockedHud>();
            }

            var so = new SerializedObject(headLock);
            HudSetFloat(so, "distance", 1f);
            HudSetFloat(so, "pitchDegrees", pitch);
            HudSetFloat(so, "yawDegrees", 0f);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ----------------------------------------------------------------- wiring

        private static void ZbBindHud(GameObject root, RectTransform clock, TextMeshProUGUI time,
            GameObject alarmPlate, ZbCells cells, UiImage healthFill,
            StatusPlate status, ZbTeam team, ZbNotice notice, ZbToast toast, ZbBoss boss)
        {
            var hud = root.GetComponent<ModeHudBase>();
            if (hud == null)
            {
                Debug.LogError($"[Kan] {ZbHudPrefab} kökünde ModeHudBase yok — bağlama yapılamadı.");
                return;
            }

            var so = new SerializedObject(hud);
            HudSet(so, "timeText", time);
            HudSet(so, "timeFrame", clock.gameObject);
            HudSet(so, "statusPlate", status);
            HudSet(so, "healthFill", healthFill);

            // The centre notice is the ONE element the base draws the number into; the title and the
            // subtitle around it are the mode's (OnCenterNoticeRefreshed).
            HudSet(so, "centerNoticeRoot", notice.root);
            HudSet(so, "centerNoticeText", notice.number);
            HudSet(so, "centerNoticeDim", notice.dim);

            // No teams, no segment health strip, no legacy score line here; an unassigned base field
            // is simply not drawn. healthText stays EMPTY on purpose — the panel writes the number
            // itself, and the base's "CAN 72" would fight it for the same label.
            HudSet(so, "healthText", null);
            HudSet(so, "healthStrip", null);
            HudSet(so, "statusText", null);
            HudSet(so, "scoreText", null);

            HudSet(so, "waveText", cells.wave);
            HudSet(so, "remainingText", cells.remaining);
            HudSet(so, "squadKillsText", cells.squad);
            HudSet(so, "selfKillsText", cells.self);
            HudSet(so, "clockAlarmPlate", alarmPlate);
            HudSet(so, "teamStripRoot", team.root);
            HudSetArray(so, "teamRows", team.rows);
            HudSetArray(so, "teamNameTexts", team.names);
            HudSetArray(so, "teamStateTexts", team.states);
            HudSet(so, "noticeTitleText", notice.title);
            HudSet(so, "noticeSubText", notice.sub);
            HudSet(so, "noticeUnlockText", notice.unlocks);
            HudSet(so, "toastRoot", toast.root);
            HudSet(so, "toastSmallText", toast.small);
            HudSet(so, "toastBigText", toast.big);
            HudSetFloat(so, "toastSmallBand", ZbToastSmallBand);
            HudSet(so, "bossRoot", boss.root);
            HudSet(so, "bossFill", boss.fill);
            HudSet(so, "bossTitleText", boss.title);
            HudSet(so, "bossTimeText", boss.time);
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log("[Kan] Zombi HUD üretildi: " + ZbStripName
                      + "/{Clock/{Plate,AlarmPlate,Time}, Row/{CellWave,CellRemaining,CellSquad,"
                      + "CellSelf}, " + ZbHealthName + "/{Glow,Plate/{Label,Value,Track/{Trail,Fill,"
                      + "Shade,Ticks,Pulse}}}, Status, Team/{Head,Row0..7/{Name,State}}} · "
                      + ZbNoticeName + "/{Dim,Title,Number,Sub,Unlocks} · "
                      + ZbToastName + "/Plate/{Small,Big} · "
                      + ZbBossName + "/Plate/{Title,Track/Fill,Time}"
                      + $" · şerit {ZbStripW}x{ZbContentH} (takım şeridi {ZbStripW}x{ZbTeamH})"
                      + $" · DEV barı {ZbBossW}x{ZbBossH}"
                      + $" · can paneli {ZbHpW}x{ZbHpH} (şerit içinde)"
                      + " · bağlanan: timeText, timeFrame, statusPlate, healthFill,"
                      + " centerNoticeRoot/Text/Dim, waveText, remainingText, squadKillsText,"
                      + " selfKillsText, clockAlarmPlate, teamStripRoot, teamRows[8],"
                      + " teamNameTexts[8], teamStateTexts[8], noticeTitleText, noticeSubText,"
                      + " noticeUnlockText,"
                      + " toastRoot, toastSmallText, toastBigText, bossRoot, bossFill,"
                      + " bossTitleText, bossTimeText"
                      + " · ZombieHealthPanel: source, trail, valueText, glow, pulse"
                      + " (healthText/healthStrip/statusText/scoreText boşaltıldı).");
        }
    }
}

