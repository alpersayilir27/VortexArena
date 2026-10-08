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
    /// Burger mode's in-match HUD strip (<c>Docs/Gelistirici/Arayuz/asci.html</c> <c>.bhud</c>):
    /// shift clock · co-op score band · status line, built into
    /// <c>Assets/Modes/Burger/UI/BurgerHud.prefab</c>.
    /// <para>
    /// ⚠️ <b>Replaces the nested <c>HealthHud</c> instance.</b> Burger has no health and no teams, so
    /// the Girdap strip was switched off piece by piece and reskinned — a dead weight that drifted
    /// every time the shared strip changed. The Lokanta strip ADOPTS the removed instance's
    /// RectTransform and its chrome (nested Canvas, head lock), so the panel keeps sitting exactly
    /// where the player learned to look.
    /// </para>
    /// </summary>
    public static partial class GirdapUiBuilder
    {
        private const string LkHudPrefab = LkUiDir + "BurgerHud.prefab";
        private const string LkStripName = "LokantaStrip";

        // .bhud — 560 wide, stacked from the top; y is measured downward like `Place`.
        private const float LkStripW = 560f;
        private const float LkClockW = 220f;
        private const float LkClockH = 46f;
        private const float LkRibbonW = 96f;
        private const float LkRibbonH = 30f;
        private const float LkBandH = 64f;
        private const float LkStatusH = 40f;
        private const float LkCellInset = Lokanta.Outline; // keeps the band's Ink frame visible
        private const float LkTeamW = 150f;
        private const float LkScoreCellW = 110f;
        private const float LkDividerW = 3f;

        private const float LkBandY = LkClockH + 14f;
        private const float LkStatusY = LkBandY + LkBandH + 12f;
        private const float LkContentH = LkStatusY + LkStatusH;

        // Fallback rect for the (impossible-to-recover) case of a re-run after HealthHud is gone AND
        // the strip was deleted by hand; matches what HealthHud.prefab's root carried.
        private const float LkFallbackH = 142f;

        static partial void BuildLokanta()
        {
            BuildLokantaHud();
            BuildLokantaBubble();
            BuildLokantaThermometer();
            BuildLokantaResult();
        }

        [MenuItem("Tools/VortexArena/UI/Girdap/Yalnız Aşçı HUD")]
        public static void BuildLokantaHudMenu()
        {
            BuildAssets();
            BuildLokantaHud();
            AssetDatabase.SaveAssets();
        }

        public static void BuildLokantaHud()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(LkHudPrefab) == null)
            {
                Debug.LogWarning($"[Lokanta] {LkHudPrefab} yok — aşçı HUD'ı üretilemedi.");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(LkHudPrefab);
            try
            {
                Transform t = root.transform;
                Transform legacy = t.Find(StripPrefab);
                Transform legacyScore = t.Find("Score");

                RectTransform strip = VhudClaim(t, LkStripName);
                PlaceCenter(strip, 0f, 0f, LkStripW, LkFallbackH);
                if (legacy != null)
                {
                    LkAdoptRect((RectTransform)legacy, strip);
                    LkAdoptChrome(legacy, strip);
                }

                VhudKeepOnly(strip); // idempotent: the whole content is rebuilt every run

                if (legacyScore != null)
                {
                    Object.DestroyImmediate(legacyScore.gameObject);
                }

                if (legacy != null)
                {
                    // Fine on a nested-prefab instance root: inside LoadPrefabContents it is a plain
                    // object graph, not a live instance that would need PrefabUtility bookkeeping.
                    Object.DestroyImmediate(legacy.gameObject);
                }

                strip.sizeDelta = new Vector2(LkStripW, LkContentH);

                RectTransform clock = LkBuildClock(strip, out TextMeshProUGUI time,
                    out GameObject lastPlate, out UiShape ribbonPlate);
                LkBand band = LkBuildBand(strip);
                StatusPlate status = LkBuildStatus(strip);

                LkBindHud(root, clock, time, lastPlate, ribbonPlate, band, status);
                PrefabUtility.SaveAsPrefabAsset(root, LkHudPrefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ------------------------------------------------------------------- clock

        /// <summary>CSS <c>.bclock</c>: cream pill with the "VARDİYA" ribbon and the tabular time.</summary>
        private static RectTransform LkBuildClock(Transform parent, out TextMeshProUGUI time,
            out GameObject lastPlate, out UiShape ribbonPlate)
        {
            UiShape plate = LkPill(parent, "Clock", LkClockH, Lokanta.Cream);
            var clock = (RectTransform)plate.transform.parent;
            PlaceTopCenter(clock, 0f, LkClockW, LkClockH);

            // Last-10-seconds skin: a red plate ON TOP of the cream one, raised by the controller.
            UiShape last = Shape(clock, "LastPlate");
            Stretch(last.rectTransform);
            last.Radius(LkClockH * 0.5f).Outline(Lokanta.Outline, Lokanta.Ink).Fill(Lokanta.Red);
            last.gameObject.SetActive(false);
            lastPlate = last.gameObject;

            RectTransform ribbon = LkRibbon(clock, "Ribbon", "VARDİYA", LkRibbonW, LkRibbonH,
                GirdapFont.FredokaBold, 14f);
            PlaceMiddleLeft(ribbon, 8f, LkRibbonW, LkRibbonH);
            ribbonPlate = ribbon.Find("Bg").GetComponent<UiShape>();

            const float timeX = 8f + LkRibbonW + 12f;
            time = LkText(clock, "Time", "02:14", GirdapFont.FredokaBold, 28f, Lokanta.Ink,
                TextAlignmentOptions.MidlineRight);
            PlaceMiddleLeft(time.rectTransform, timeX, LkClockW - timeX - 16f, LkClockH);
            return clock;
        }

        // -------------------------------------------------------------------- band

        /// <summary>The band's four text targets; grouping them keeps the binder's signature readable.</summary>
        private struct LkBand
        {
            public TextMeshProUGUI shared;
            public TextMeshProUGUI self;
            public TextMeshProUGUI happy;
            public TextMeshProUGUI unhappy;
        }

        /// <summary>CSS <c>.bband</c>: EKİP · shared total · own contribution · customer faces.</summary>
        private static LkBand LkBuildBand(Transform parent)
        {
            UiShape plate = LkCard(parent, "Band", Lokanta.CardRadius, Lokanta.Paper);
            var band = (RectTransform)plate.transform.parent;
            PlaceTopCenter(band, LkBandY, LkStripW, LkBandH);

            float cellH = LkBandH - LkCellInset * 2f;
            float x = LkCellInset;

            // Team — the only cell with its own corner radius; the rest hide under the Paper card.
            UiShape team = Shape(band, "Team");
            Place(team.rectTransform, x, LkCellInset, LkTeamW, cellH);
            team.Radius(18f, 0f, 0f, 18f)
                .Fill(Lokanta.RedHi, Lokanta.Red2, UiGradientMode.Vertical);
            TextMeshProUGUI teamLabel = LkText(team.transform, "Label", "EKİP",
                GirdapFont.FredokaBold, 26f, Lokanta.Light, TextAlignmentOptions.Center, 0.04f);
            Stretch(teamLabel.rectTransform);
            TextShadow(teamLabel, Lokanta.Ink);
            x += LkTeamW;

            LkDivider(band, "DividerTeam", x, cellH);
            x += LkDividerW;

            UiShape shared = Shape(band, "Shared");
            Place(shared.rectTransform, x, LkCellInset, LkScoreCellW, cellH);
            shared.Fill(Lokanta.Paper);
            TextMeshProUGUI sharedValue = LkText(shared.transform, "Value", "120",
                GirdapFont.FredokaBold, 40f, Lokanta.Ink, TextAlignmentOptions.Center);
            Stretch(sharedValue.rectTransform);
            x += LkScoreCellW;

            LkDivider(band, "DividerShared", x, cellH);
            x += LkDividerW;

            UiShape self = Shape(band, "Self");
            Place(self.rectTransform, x, LkCellInset, LkScoreCellW, cellH);
            self.Fill(Lokanta.Cream2);
            Color labelInk = Lokanta.Ink;
            labelInk.a = 0.75f;
            TextMeshProUGUI selfLabel = LkText(self.transform, "Label", "SEN",
                GirdapFont.NunitoExtraBold, 11f, labelInk, TextAlignmentOptions.Center, 0.2f);
            StretchTop(selfLabel.rectTransform, 0f, 6f, 0f, 12f);
            TextMeshProUGUI selfValue = LkText(self.transform, "Value", "40",
                GirdapFont.FredokaBold, 30f, Lokanta.Ink, TextAlignmentOptions.Center);
            Place(selfValue.rectTransform, 0f, 18f, LkScoreCellW, cellH - 18f);
            x += LkScoreCellW;

            LkDivider(band, "DividerSelf", x, cellH);
            x += LkDividerW;

            float customersW = LkStripW - LkCellInset - x;
            UiShape customers = Shape(band, "Customers");
            Place(customers.rectTransform, x, LkCellInset, customersW, cellH);
            customers.Radius(0f, 18f, 18f, 0f).Fill(Lokanta.Paper);

            const float faceSize = 28f;
            const float countW = 38f;
            const float faceGap = 6f;
            const float pairGap = 16f;
            float block = (faceSize + faceGap + countW) * 2f + pairGap;
            float cx = Mathf.Max(0f, (customersW - block) * 0.5f);

            UiImage happyFace = LkFace(customers.transform, "FaceHappy", LkMood.Happy, faceSize);
            PlaceMiddleLeft(happyFace.rectTransform, cx, faceSize, faceSize);
            cx += faceSize + faceGap;
            TextMeshProUGUI happy = LkText(customers.transform, "HappyCount", "0",
                GirdapFont.FredokaBold, 30f, Lokanta.Ink, TextAlignmentOptions.MidlineLeft);
            PlaceMiddleLeft(happy.rectTransform, cx, countW, cellH);
            cx += countW + pairGap;

            UiImage sadFace = LkFace(customers.transform, "FaceSad", LkMood.Sad, faceSize);
            PlaceMiddleLeft(sadFace.rectTransform, cx, faceSize, faceSize);
            cx += faceSize + faceGap;
            TextMeshProUGUI unhappy = LkText(customers.transform, "UnhappyCount", "0",
                GirdapFont.FredokaBold, 30f, Lokanta.Ink, TextAlignmentOptions.MidlineLeft);
            PlaceMiddleLeft(unhappy.rectTransform, cx, countW, cellH);

            return new LkBand
            {
                shared = sharedValue,
                self = selfValue,
                happy = happy,
                unhappy = unhappy
            };
        }

        private static void LkDivider(Transform parent, string name, float x, float h)
        {
            UiShape divider = Shape(parent, name);
            Place(divider.rectTransform, x, LkCellInset, LkDividerW, h);
            divider.Fill(Lokanta.Ink).Antialias(false);
        }

        // ------------------------------------------------------------------ status

        /// <summary><see cref="StatusPlate"/> in the skin's clothes: cream pill, Ink icon and words.</summary>
        private static StatusPlate LkBuildStatus(Transform parent)
        {
            RectTransform rootRt = Node(parent, "Status");
            PlaceTopCenter(rootRt, LkStatusY, LkStripW, LkStatusH);

            // Body is what StatusPlate resizes to the text, so the pill STRETCHES to it.
            RectTransform body = Node(rootRt, "Body");
            PlaceCenter(body, 0f, 0f, LkStripW, LkStatusH);
            UiShape plate = LkPill(body, "Pill", LkStatusH, Lokanta.Cream, Lokanta.Outline, 4f);
            Stretch((RectTransform)plate.transform.parent);

            UiImage icon = Icon(body, "Icon", "Warn", 20f, Lokanta.Ink);
            PlaceMiddleLeft(icon.rectTransform, 14f, 20f, 20f);
            TextMeshProUGUI text = LkText(body, "Text", "", GirdapFont.FredokaBold, 20f, Lokanta.Ink,
                TextAlignmentOptions.MidlineLeft);
            Stretch(text.rectTransform, 40f, 0f, 16f, 0f);

            var comp = rootRt.gameObject.AddComponent<StatusPlate>();
            var so = new SerializedObject(comp);
            HudSet(so, "body", body.gameObject);
            HudSet(so, "plate", body);
            HudSet(so, "icon", icon);
            HudSet(so, "text", text);
            HudSetFloat(so, "padLeft", 14f);
            HudSetFloat(so, "padRight", 16f);
            HudSetFloat(so, "gap", 8f);
            HudSetFloat(so, "iconSize", 20f);
            HudSetFloat(so, "maxWidth", LkStripW);
            so.ApplyModifiedPropertiesWithoutUndo();
            body.gameObject.SetActive(false);
            return comp;
        }

        // ------------------------------------------------------------------ wiring

        private static void LkBindHud(GameObject root, RectTransform clock, TextMeshProUGUI time,
            GameObject lastPlate, UiShape ribbonPlate, LkBand band, StatusPlate status)
        {
            var hud = root.GetComponent<ModeHudBase>();
            if (hud == null)
            {
                Debug.LogError($"[Lokanta] {LkHudPrefab} kökünde ModeHudBase yok — bağlama yapılamadı.");
                return;
            }

            var so = new SerializedObject(hud);
            HudSet(so, "timeText", time);
            HudSet(so, "timeFrame", clock.gameObject);
            HudSet(so, "statusPlate", status);

            // Burger has no health, no teams and no legacy score line any more; an unassigned base
            // field is simply not drawn.
            HudSet(so, "healthStrip", null);
            HudSet(so, "healthText", null);
            HudSet(so, "healthFill", null);
            HudSet(so, "statusText", null);
            HudSet(so, "scoreText", null);
            HudSet(so, "customerCountsText", null);

            HudSet(so, "sharedScoreText", band.shared);
            HudSet(so, "selfScoreText", band.self);
            HudSet(so, "happyCountText", band.happy);
            HudSet(so, "unhappyCountText", band.unhappy);
            HudSet(so, "clockLastPlate", lastPlate);
            HudSet(so, "clockRibbonPlate", ribbonPlate);
            HudSet(so, "clockTimeText", time);
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log("[Lokanta] Aşçı HUD üretildi: " + LkStripName
                      + "/{Clock/{Ribbon,LastPlate,Time}, Band/{Team,Shared,Self,Customers}, Status}"
                      + $" · şerit {LkStripW}x{LkContentH}"
                      + " · bağlanan: timeText, timeFrame, statusPlate, sharedScoreText,"
                      + " selfScoreText, happyCountText, unhappyCountText, clockLastPlate,"
                      + " clockRibbonPlate, clockTimeText (healthStrip/healthText/healthFill/"
                      + "statusText/scoreText/customerCountsText boşaltıldı).");
        }

        // ----------------------------------------------------------------- adoption

        private static void LkAdoptRect(RectTransform from, RectTransform to)
        {
            to.anchorMin = from.anchorMin;
            to.anchorMax = from.anchorMax;
            to.pivot = from.pivot;
            to.anchoredPosition = from.anchoredPosition;
            to.sizeDelta = from.sizeDelta;
            to.localScale = from.localScale;
            to.localRotation = from.localRotation;
        }

        /// <summary>
        /// Moves the removed instance's non-visual components (nested Canvas with its sorting
        /// override, <see cref="HeadLockedHud"/>) onto the new strip.
        /// ⚠️ Copied, never re-typed by hand: a hand-written <c>sortingOrder</c> drops the panel behind
        /// the blackout quad (Yapma-Listesi, "Arayüz (Girdap)").
        /// </summary>
        private static void LkAdoptChrome(Transform from, Transform to)
        {
            Component[] parts = from.GetComponents<Component>();
            for (int i = 0; i < parts.Length; i++)
            {
                Component part = parts[i];
                if (part == null || part is Transform || part is CanvasRenderer || part is Graphic)
                {
                    continue;
                }

                System.Type type = part.GetType();
                Component clone = to.gameObject.GetComponent(type);
                if (clone == null)
                {
                    clone = to.gameObject.AddComponent(type);
                }

                LkCopySerialized(part, clone);

                // ⚠️ A nested Canvas's sorting does NOT come through SerializedObject (m_SortingOrder
                // is hidden): copied by hand, or the panel silently changes layer.
                if (part is Canvas source && clone is Canvas target)
                {
                    target.overrideSorting = source.overrideSorting;
                    target.sortingLayerID = source.sortingLayerID;
                    target.sortingOrder = source.sortingOrder;
                    target.additionalShaderChannels = source.additionalShaderChannels;
                }
            }
        }

        // Top-level property copy; CopyFromSerializedProperty recurses into nested values itself.
        private static void LkCopySerialized(Component from, Component to)
        {
            var src = new SerializedObject(from);
            var dst = new SerializedObject(to);
            SerializedProperty it = src.GetIterator();
            if (!it.NextVisible(true))
            {
                return;
            }

            do
            {
                if (it.propertyPath == "m_Script")
                {
                    continue;
                }

                dst.CopyFromSerializedProperty(it);
            }
            while (it.NextVisible(false));

            dst.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
