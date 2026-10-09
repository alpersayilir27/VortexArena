using TMPro;
using UnityEditor;
using UnityEngine;
using VortexArena.Core.UI;

// `Image x = Image(...)` gives CS0119 — simple name lookup finds the member group before the type.
using UiImage = UnityEngine.UI.Image;

namespace VortexArena.App.Editor
{
    /// <summary>
    /// Player calibration card (<c>Docs/Gelistirici/Arayuz/oyuncu-hud.html</c>, <c>.ccard</c> +
    /// <c>.chint</c>): built from scratch on every run — it has no variants, so a fresh
    /// <see cref="NewRoot"/> + <see cref="SavePrefab"/> cannot lose overrides.
    /// </summary>
    public static partial class GirdapUiBuilder
    {
        private const string CalPrefab = "CalibrationOverlay";

        // CSS px, 1 px = 1 canvas unit; 720 px at 0.0006 ≈ 0.43 m wide.
        private const float CalRootW = 720f;
        private const float CalRootH = 176f;
        private const float CalScale = 0.0006f;

        private const float CalCardH = 124f;
        private const float CalChamfer = 14f;

        private const float CalLabelY = 18f;
        private const float CalLabelH = 16f;
        private const float CalRuleW = 90f;
        private const float CalRuleH = 2f;
        private const float CalRuleGap = 16f;

        private const float CalRowTop = 52f;
        private const float CalRowH = 48f;
        private const float CalIconSize = 34f;
        private const float CalIconGap = 12f;
        private const float CalRowMaxW = 672f;

        private const float CalHintY = 136f;
        private const float CalHintH = 40f;
        private const float CalHintPad = 16f;

        // Eye level, above the death/connection cards at -0.32 so they never overlap; the yaw-only
        // follow keeps the card out of the way while the player bends to the floor mark.
        private const float CalFollowDistance = 1.2f;
        private const float CalFollowVerticalOffset = -0.08f;

        [MenuItem("Tools/VortexArena/UI/Girdap/Yalnız Kalibrasyon")]
        public static void BuildCalibrationOnly()
        {
            BuildAssets();
            BuildCalibration();
            AssetDatabase.SaveAssets();
        }

        static partial void BuildCalibration()
        {
            GameObject root = NewRoot(CalPrefab, CalRootW, CalRootH);

            var rootRect = (RectTransform)root.transform;
            rootRect.localScale = Vector3.one * CalScale;

            // World-space VR canvas: the card IS the canvas, so every CSS px maps 1:1. No
            // CanvasScaler — a world-space canvas has no reference resolution to scale against.
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var follow = root.AddComponent<HudFollow>();
            var overlay = root.AddComponent<CalibrationOverlay>();

            var followWiring = new SerializedObject(follow);
            HudSetFloat(followWiring, "distance", CalFollowDistance);
            HudSetFloat(followWiring, "verticalOffset", CalFollowVerticalOffset);
            followWiring.ApplyModifiedPropertiesWithoutUndo();

            var wiring = new SerializedObject(overlay);
            HudSet(wiring, "canvas", canvas);
            CalBuildCard(root.transform, wiring);
            HudSet(wiring, "hint", CalBuildHint(root.transform, "Hint"));
            HudSetFloat(wiring, "iconSize", CalIconSize);
            HudSetFloat(wiring, "iconGap", CalIconGap);
            HudSetFloat(wiring, "rowMaxWidth", CalRowMaxW);
            wiring.ApplyModifiedPropertiesWithoutUndo();

            SavePrefab(root, CalPrefab);
        }

        // ----------------------------------------------------------------- .ccard

        private static void CalBuildCard(Transform parent, SerializedObject wiring)
        {
            RectTransform card = Node(parent, "Card");
            PlaceTopCenter(card, 0f, CalRootW, CalCardH);

            Color border = Girdap.Warn;
            border.a = 0.65f;
            UiShape frame = Shape(card, "Frame");
            Stretch(frame.rectTransform);
            frame.Chamfer(CalChamfer)
                .Outline(1f, border, Girdap.Rgba(170, 205, 255, 0.14f))
                .Fill(Girdap.Hex(0x141424), Girdap.Hex(0x080A1A), UiGradientMode.Vertical);

            UiStripes stripes = Stripes(card, "Stripes");
            Stretch(stripes.rectTransform);
            stripes.Stripes(-55f, 10f, 30f, new Color(1f, 1f, 1f, 0.035f)).Drift(4f); // CSS drift 4s
            VhudChamfer(stripes, CalChamfer);

            UiImage rule = Image(card, "Rule", null, Girdap.Warn);
            StretchBottom(rule.rectTransform, 0f, 0f, 0f, 3f);

            Color labelColor = Girdap.Text;
            labelColor.a = 0.8f;
            TextMeshProUGUI label = Text(card, "Label", "KALİBRASYON", GirdapFont.ChakraSemiBold,
                14f, labelColor, TextAlignmentOptions.Center, 0.5f);
            float labelW = Mathf.Ceil(label.GetPreferredValues(label.text).x);
            PlaceTopCenter(label.rectTransform, CalLabelY, labelW, CalLabelH);

            Color flank = Girdap.Warn;
            flank.a = 0.8f;
            float ruleOffset = labelW * 0.5f + CalRuleGap + CalRuleW * 0.5f;
            float ruleY = CalLabelY + (CalLabelH - CalRuleH) * 0.5f; // centred on the label's midline

            UiImage ruleLeft = Image(card, "RuleLeft", null, flank);
            PlaceTopCenter(ruleLeft.rectTransform, ruleY, CalRuleW, CalRuleH);
            ruleLeft.rectTransform.anchoredPosition += new Vector2(-ruleOffset, 0f);

            UiImage ruleRight = Image(card, "RuleRight", null, flank);
            PlaceTopCenter(ruleRight.rectTransform, ruleY, CalRuleW, CalRuleH);
            ruleRight.rectTransform.anchoredPosition += new Vector2(ruleOffset, 0f);

            // Icon + title are CENTRE-anchored: the runtime centres the row by hand, so it only
            // writes anchoredPosition.x.
            float rowDy = CalRowTop + CalRowH * 0.5f - CalCardH * 0.5f;
            UiImage icon = Icon(card, "Icon", "Warn", CalIconSize, Girdap.Warn);
            PlaceCenter(icon.rectTransform, 0f, rowDy, CalIconSize, CalIconSize);

            TextMeshProUGUI titleText = Text(card, "Title", "Lütfen kalibre alınız",
                GirdapFont.SairaBold, 40f, Girdap.Warn, TextAlignmentOptions.MidlineLeft);
            PlaceCenter(titleText.rectTransform, 0f, rowDy, CalRowMaxW - CalIconSize - CalIconGap,
                CalRowH);

            HudSet(wiring, "frame", frame);
            HudSet(wiring, "rule", rule);
            HudSet(wiring, "ruleLeft", ruleLeft);
            HudSet(wiring, "ruleRight", ruleRight);
            HudSet(wiring, "icon", icon);
            HudSet(wiring, "title", titleText);
        }

        // ----------------------------------------------------------------- .chint

        /// <summary>Hint line: StatusPlate root + Body (plate · text), Body off until a line is set.
        /// No icon — the card's own icon already carries the tone.</summary>
        private static StatusPlate CalBuildHint(Transform parent, string name)
        {
            RectTransform rootRt = Node(parent, name);
            PlaceTopCenter(rootRt, CalHintY, CalRootW, CalHintH);

            RectTransform body = Node(rootRt, "Body");
            PlaceCenter(body, 0f, 0f, CalRootW, CalHintH);

            UiShape plate = Shape(body, "Plate");
            Stretch(plate.rectTransform);
            plate.Chamfer(6f).Outline(1f, Girdap.Rgba(255, 197, 61, 0.45f))
                .Fill(Girdap.PlateA, Girdap.PlateB, UiGradientMode.Vertical);

            TextMeshProUGUI text = Text(body, "Text", "", GirdapFont.ChakraSemiBold, 20f,
                Girdap.Warn, TextAlignmentOptions.MidlineLeft, 0.02f);
            Stretch(text.rectTransform, CalHintPad, 0f, CalHintPad, 0f);

            var plateComp = rootRt.gameObject.AddComponent<StatusPlate>();
            var so = new SerializedObject(plateComp);
            HudSet(so, "body", body.gameObject);
            HudSet(so, "plate", body);
            HudSet(so, "text", text);
            HudSetFloat(so, "padLeft", CalHintPad);
            HudSetFloat(so, "padRight", CalHintPad);
            HudSetFloat(so, "maxWidth", CalRootW);
            so.ApplyModifiedPropertiesWithoutUndo();

            body.gameObject.SetActive(false);
            return plateComp;
        }
    }
}
