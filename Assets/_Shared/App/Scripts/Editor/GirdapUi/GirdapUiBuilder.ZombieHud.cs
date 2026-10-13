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
    /// head-locked strip · big between-waves countdown · short toast, built into
    /// <c>Assets/Modes/Zombie/UI/ZombieHud.prefab</c>.
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
        private const float ZbHealthY = ZbRowY + ZbCellH + 10f;
        private const float ZbHealthH = 44f;
        private const float ZbStatusY = ZbHealthY + ZbHealthH + 12f;
        private const float ZbStatusH = 44f;
        private const float ZbContentH = ZbStatusY + ZbStatusH; // 312

        // Fallback rect for a re-run after the legacy strip is gone AND this one was deleted by hand;
        // matches what the copied Lokanta strip carried.
        private const float ZbFallbackH = 176f;

        // ---- centre countdown (.za-cd)
        private const float ZbNoticeTitleY = 190f;
        private const float ZbNoticeSubY = 210f;

        // ---- toast (.zt.za)
        private const float ZbToastW = 780f;
        private const float ZbToastH = 128f;

        /// <summary>Top margin the big toast line gets while the small line is shown.</summary>
        private const float ZbToastSmallBand = 44f;

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

                // Drops the legacy strip and anything left from an earlier layout.
                VhudKeepOnly(t, ZbStripName, ZbNoticeName, ZbToastName);
                VhudKeepOnly(strip);
                VhudKeepOnly(notice);
                VhudKeepOnly(toast);

                strip.sizeDelta = new Vector2(ZbStripW, ZbContentH);

                RectTransform clock = ZbBuildClock(strip, out TextMeshProUGUI time,
                    out GameObject alarmPlate);
                ZbCells cells = ZbBuildRow(strip);
                ZbBuildHealth(strip, out TextMeshProUGUI healthText, out UiImage healthFill);
                StatusPlate status = ZbBuildStatus(strip);
                ZbNotice centre = ZbBuildNotice(notice);
                ZbToast toastParts = ZbBuildToast(toast);

                ZbBindHud(root, clock, time, alarmPlate, cells, healthText, healthFill, status,
                    centre, toastParts);
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
            public TextMeshProUGUI standing;
            public TextMeshProUGUI squad;
            public TextMeshProUGUI self;
        }

        /// <summary>CSS <c>.za .row</c>: DALGA (blood cell) · AYAKTA · EKİP · SEN on one rusted plate.</summary>
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

            RectTransform standing = Node(row, "CellStanding");
            Place(standing, x, 0f, ZbCellW, ZbCellH);
            cells.standing = ZbCellContent(standing, "AYAKTA", "14", GirdapFont.SairaBold,
                GirdapFont.SairaExtraBold, 68f, Kan.Label, Kan.Alive, false);
            x += ZbCellW;

            ZbDivider(row, "DividerStanding", x);
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
        /// The player's own health — zombies damage players, and the diner strip this HUD was copied
        /// from carries none.
        /// <para>⚠️ <see cref="HealthStrip"/> is deliberately NOT used: it paints itself from the
        /// <see cref="Girdap"/> threshold palette, which would put a second, foreign red on a blood
        /// themed strip. The base's <c>healthFill</c>/<c>healthText</c> pair is skin-free.</para>
        /// </summary>
        private static void ZbBuildHealth(Transform parent, out TextMeshProUGUI value,
            out UiImage fill)
        {
            RectTransform health = Node(parent, "Health");
            PlaceTopCenter(health, ZbHealthY, ZbStripW, ZbHealthH);

            UiShape plate = Shape(health, "Plate");
            Stretch(plate.rectTransform);
            plate.Radius(Kan.ClockRadius)
                .Outline(Kan.Border1, Kan.Border)
                .Fill(Kan.PlateA, Kan.PlateB, UiGradientMode.Vertical);

            value = Text(health, "Value", "CAN 100", GirdapFont.SairaExtraBold, 28f, Kan.Bone,
                TextAlignmentOptions.MidlineLeft);
            PlaceMiddleLeft(value.rectTransform, 16f, 170f, ZbHealthH);

            const float trackX = 190f;
            const float trackH = 18f;
            float trackW = ZbStripW - trackX - 16f;

            UiShape track = Shape(health, "Track");
            PlaceMiddleLeft(track.rectTransform, trackX, trackW, trackH);
            track.Radius(4f).Outline(2f, Kan.Border).Fill(Kan.BloodLo);

            // ⚠️ Image.type = Filled needs a SPRITE; with a null sprite the component falls back to a
            // plain quad and fillAmount is ignored (the bar would never move).
            fill = Image(track.transform, "Fill", LoadSprite("HealthBar_Fill.png"), Kan.BloodHi,
                UiImage.Type.Filled);
            fill.fillMethod = UiImage.FillMethod.Horizontal;
            fill.fillOrigin = (int)UiImage.OriginHorizontal.Left;
            fill.fillAmount = 1f;
            Stretch(fill.rectTransform, 3f, 3f, 3f, 3f);
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

        // ------------------------------------------------------- centre countdown

        private struct ZbNotice
        {
            public GameObject root;
            public GameObject dim;
            public TextMeshProUGUI title;
            public TextMeshProUGUI number;
            public TextMeshProUGUI sub;
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

            notice.gameObject.SetActive(false);
            return new ZbNotice
            {
                root = notice.gameObject,
                dim = dim.gameObject,
                title = title,
                number = number,
                sub = sub
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

            TextMeshProUGUI small = Text(plate.transform, "Small", "YENİ SİLAH AÇILDI",
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
            GameObject alarmPlate, ZbCells cells, TextMeshProUGUI healthText, UiImage healthFill,
            StatusPlate status, ZbNotice notice, ZbToast toast)
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
            HudSet(so, "healthText", healthText);
            HudSet(so, "healthFill", healthFill);

            // The centre notice is the ONE element the base draws the number into; the title and the
            // subtitle around it are the mode's (OnCenterNoticeRefreshed).
            HudSet(so, "centerNoticeRoot", notice.root);
            HudSet(so, "centerNoticeText", notice.number);
            HudSet(so, "centerNoticeDim", notice.dim);

            // No teams, no segment health strip, no legacy score line here; an unassigned base field
            // is simply not drawn.
            HudSet(so, "healthStrip", null);
            HudSet(so, "statusText", null);
            HudSet(so, "scoreText", null);

            HudSet(so, "waveText", cells.wave);
            HudSet(so, "standingText", cells.standing);
            HudSet(so, "squadKillsText", cells.squad);
            HudSet(so, "selfKillsText", cells.self);
            HudSet(so, "clockAlarmPlate", alarmPlate);
            HudSet(so, "noticeTitleText", notice.title);
            HudSet(so, "noticeSubText", notice.sub);
            HudSet(so, "toastRoot", toast.root);
            HudSet(so, "toastSmallText", toast.small);
            HudSet(so, "toastBigText", toast.big);
            HudSetFloat(so, "toastSmallBand", ZbToastSmallBand);
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log("[Kan] Zombi HUD üretildi: " + ZbStripName
                      + "/{Clock/{Plate,AlarmPlate,Time}, Row/{CellWave,CellStanding,CellSquad,"
                      + "CellSelf}, Health, Status} · " + ZbNoticeName + "/{Dim,Title,Number,Sub} · "
                      + ZbToastName + "/Plate/{Small,Big}"
                      + $" · şerit {ZbStripW}x{ZbContentH}"
                      + " · bağlanan: timeText, timeFrame, statusPlate, healthText, healthFill,"
                      + " centerNoticeRoot/Text/Dim, waveText, standingText, squadKillsText,"
                      + " selfKillsText, clockAlarmPlate, noticeTitleText, noticeSubText,"
                      + " toastRoot, toastSmallText, toastBigText"
                      + " (healthStrip/statusText/scoreText boşaltıldı).");
        }
    }
}

