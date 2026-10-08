using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using VortexArena.Core.UI;

// `Image x = Image(...)` gives CS0119 — simple name lookup finds the member group before the type.
using UiImage = UnityEngine.UI.Image;
using UiObject = UnityEngine.Object;

namespace VortexArena.App.Editor
{
    /// <summary>
    /// The patty's cooking gauge as a world-space Lokanta card
    /// (<c>Assets/Modes/Burger/UI/LokantaThermometer.prefab</c>), nested into every patty prefab that
    /// carries <c>BurgerPattyThermometer</c>.
    /// <para>
    /// ⚠️ The canvas settings are COPIED from the customer bubble's canvas, not typed here: both are
    /// world-space panels in the same arena, and a hand-written sorting value is exactly the trap
    /// Yapma-Listesi warns about ("Oyun içi dünya paneline ... sıralamasını elle 0'a çekme").
    /// </para>
    /// <para>
    /// ⚠️ Replaces the old shader quad. The gauge was a <c>MaterialPropertyBlock</c> on a Quad, which
    /// cannot carry text — the player had to learn a colour code instead of reading "HAZIR!".
    /// </para>
    /// </summary>
    public static partial class GirdapUiBuilder
    {
        public const string LkThermoPrefab = LkUiDir + "LokantaThermometer.prefab";

        private const string LkThermoName = "LokantaThermometer";
        private const string LkThermoType = "BurgerPattyThermometer";

        // 100x200 at 0.0005 = 5x10 cm over the patty.
        private const float LkThermoW = 100f;
        private const float LkThermoH = 200f;
        private const float LkThermoScale = 0.0005f;

        private const float LkTubeX = 32f;
        private const float LkTubeY = 12f;
        private const float LkTubeW = 26f;
        private const float LkTubeH = 118f;
        private const float LkTubeWall = 3f;

        private const float LkZoneX = 74f;
        private const float LkZoneW = 14f;

        private const float LkStatusRowY = 166f;
        private const float LkStatusRowH = 26f;

        [MenuItem("Tools/VortexArena/UI/Girdap/Yalnız Aşçı termometre")]
        public static void BuildLokantaThermometerMenu()
        {
            BuildAssets();
            BuildLokantaThermometer();
            AssetDatabase.SaveAssets();
        }

        public static void BuildLokantaThermometer()
        {
            EnsureFolder(LkUiDir);

            GameObject root = NewRoot(LkThermoName, LkThermoW, LkThermoH);
            try
            {
                var rect = (RectTransform)root.transform;
                rect.localScale = Vector3.one * LkThermoScale;

                LkApplyBubbleCanvas(root);
                root.AddComponent<CanvasGroup>();

                UiShape card = LkCard(root.transform, "Card", 30f, Lokanta.Cream);
                Stretch((RectTransform)card.transform.parent);

                LkBuildThermoParts(root.transform);

                PrefabUtility.SaveAsPrefabAsset(root, LkThermoPrefab);
            }
            finally
            {
                UiObject.DestroyImmediate(root);
            }

            BindLokantaThermometer();
        }

        // ------------------------------------------------------------------ parts

        /// <summary>Draws the gauge. Nothing is returned: the fields are bound by PATH after the prefab
        /// is instantiated into the patty, so there is one list of node paths, not two.</summary>
        private static void LkBuildThermoParts(Transform parent)
        {
            UiShape tube = Shape(parent, "Tube");
            Place(tube.rectTransform, LkTubeX, LkTubeY, LkTubeW, LkTubeH);
            tube.Radius(13f, 13f, 0f, 0f).Outline(LkTubeWall, Lokanta.Ink).Fill(Lokanta.Light);

            // The mercury's own parent IS the usable column, so the runtime reads the height off the
            // prefab instead of carrying a second copy of the tube's size.
            RectTransform track = Node(tube.transform, "MercuryTrack");
            Stretch(track, LkTubeWall, LkTubeWall, LkTubeWall, LkTubeWall);

            UiShape mercury = Shape(track, "Mercury");
            LkBottomBar(mercury.rectTransform, 0f);
            mercury.Radius(4f).Fill(Lokanta.MercuryRawA, Lokanta.MercuryRawB, UiGradientMode.Vertical);

            Color shine = Lokanta.Light;
            shine.a = 0.55f;
            UiShape gloss = Shape(tube.transform, "Shine");
            Place(gloss.rectTransform, 4f, 10f, 4f, LkTubeH - 26f);
            gloss.Radius(2f).Fill(shine);

            UiShape bulb = Shape(parent, "Bulb");
            Place(bulb.rectTransform, LkTubeX - 9f, LkTubeY + LkTubeH - 14f, 44f, 44f);
            bulb.Radius(22f).Outline(LkTubeWall, Lokanta.Ink).Fill(Lokanta.MercuryRawB);

            // Same vertical range as the mercury column, so a normalised span maps 1:1.
            RectTransform zoneTrack = Node(parent, "ZoneTrack");
            Place(zoneTrack, LkZoneX, LkTubeY + LkTubeWall, LkZoneW, LkTubeH - LkTubeWall * 2f);

            UiShape zone = Shape(zoneTrack, "Zone");
            LkBottomBar(zone.rectTransform, 0f);
            zone.Radius(5f).Outline(LkTubeWall, Lokanta.Ink).Fill(Lokanta.Green);

            RectTransform status = Node(parent, "Status");
            Place(status, 0f, LkStatusRowY, LkThermoW, LkStatusRowH);

            UiImage icon = Icon(status, "Icon", "Flame", 16f, Lokanta.Flame);
            PlaceMiddleLeft(icon.rectTransform, 14f, 16f, 16f);

            TextMeshProUGUI label = LkText(status, "Label", "ÇİĞ", GirdapFont.FredokaBold, 14f,
                Lokanta.Ink, TextAlignmentOptions.Center);
            Stretch(label.rectTransform, 32f, 0f, 6f, 0f);
        }

        /// <summary>Full-width bar growing UP from its parent's bottom edge.</summary>
        private static void LkBottomBar(RectTransform rt, float height)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, height);
        }

        // ----------------------------------------------------------------- canvas

        /// <summary>Clones the customer bubble's Canvas onto <paramref name="root"/>.</summary>
        private static void LkApplyBubbleCanvas(GameObject root)
        {
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LkCustomerPrefab);
            if (prefab == null)
            {
                Debug.LogWarning($"[Lokanta] {LkCustomerPrefab} yok — termometre tuvali varsayılan "
                                 + "dünya uzayı ayarıyla kaldı.");
                return;
            }

            Transform bubble = LkFindDeep(prefab.transform, LkBubbleNode);
            var source = bubble != null ? bubble.GetComponent<Canvas>() : null;
            if (source == null)
            {
                Debug.LogWarning($"[Lokanta] '{LkBubbleNode}' üzerinde Canvas yok — termometre tuvali "
                                 + "varsayılan dünya uzayı ayarıyla kaldı.");
                return;
            }

            canvas.renderMode = source.renderMode;
            canvas.overrideSorting = source.overrideSorting;
            canvas.sortingLayerID = source.sortingLayerID;
            canvas.sortingOrder = source.sortingOrder;
            canvas.additionalShaderChannels = source.additionalShaderChannels;
        }

        // ---------------------------------------------------------------- binding

        /// <summary>Nests the gauge prefab into every patty prefab under <c>Assets/Modes/Burger</c>
        /// and drops the old shader quad.</summary>
        public static void BindLokantaThermometer()
        {
            var gauge = AssetDatabase.LoadAssetAtPath<GameObject>(LkThermoPrefab);
            if (gauge == null)
            {
                Debug.LogError($"[Lokanta] {LkThermoPrefab} üretilemedi — bağlama atlandı.");
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Modes/Burger" });
            var done = new List<string>();

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (path == LkThermoPrefab)
                {
                    continue;
                }

                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null || LkFindComponentDeep(asset, LkThermoType) == null)
                {
                    continue;
                }

                if (LkBindThermoInto(path, gauge))
                {
                    done.Add(System.IO.Path.GetFileName(path));
                }
            }

            Debug.Log("[Lokanta] Termometre üretildi: " + LkThermoName
                      + "/{Card/{Shadow,Plate}, Tube/{MercuryTrack/Mercury, Shine}, Bulb,"
                      + " ZoneTrack/Zone, Status/{Icon,Label}}"
                      + $" · {LkThermoW}x{LkThermoH} @ {LkThermoScale}"
                      + " · bağlanan: uiRoot, uiGroup, mercury, mercuryShape, bulbShape, cardShape,"
                      + " zone, label, icon, iconFlame/Check/Warn/X (gauge boşaltıldı)"
                      + " · prefablar: " + (done.Count > 0 ? string.Join(", ", done) : "yok"));
        }

        private static bool LkBindThermoInto(string path, GameObject gauge)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Component comp = LkFindComponentDeep(contents, LkThermoType);
                if (comp == null)
                {
                    return false;
                }

                var so = new SerializedObject(comp);

                // Old quad first: its renderer is what `gauge` pointed at, and the field is the only
                // handle on it once the child is gone.
                SerializedProperty old = so.FindProperty("gauge");
                if (old != null)
                {
                    LkDropGauge(old.objectReferenceValue as Renderer, comp.gameObject);
                    old.objectReferenceValue = null;
                }

                // A re-run finds the field already cleared, so the quad is hunted by component too —
                // otherwise the orphaned MeshRenderer keeps drawing the old shader forever.
                LkDropGauge(comp.GetComponent<Renderer>(), comp.gameObject);

                // ⚠️ The quad's SIZE was the host node's scale (0.05 x 0.10 non-uniform). The canvas
                // brings its own scale and a non-uniform parent would both shrink it and skew the
                // billboard, so the host is normalised.
                comp.transform.localScale = Vector3.one;

                // Idempotent: the previous run's instance is replaced, not stacked.
                Transform stale = comp.transform.Find(LkThermoName);
                if (stale != null)
                {
                    UiObject.DestroyImmediate(stale.gameObject);
                }

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(gauge);
                instance.name = LkThermoName;
                instance.transform.SetParent(comp.transform, false);

                Transform ui = instance.transform;
                HudSet(so, "uiRoot", ui);
                HudSet(so, "uiGroup", instance.GetComponent<CanvasGroup>());
                HudSet(so, "cardShape", LkChild<UiShape>(ui, "Card/Plate"));
                HudSet(so, "mercury", LkChild<RectTransform>(ui, "Tube/MercuryTrack/Mercury"));
                HudSet(so, "mercuryShape", LkChild<UiShape>(ui, "Tube/MercuryTrack/Mercury"));
                HudSet(so, "bulbShape", LkChild<UiShape>(ui, "Bulb"));
                HudSet(so, "zone", LkChild<RectTransform>(ui, "ZoneTrack/Zone"));
                HudSet(so, "label", LkChild<TextMeshProUGUI>(ui, "Status/Label"));
                HudSet(so, "icon", LkChild<UiImage>(ui, "Status/Icon"));
                HudSet(so, "iconFlame", LoadIcon("Flame"));
                HudSet(so, "iconCheck", LoadIcon("Check"));
                HudSet(so, "iconWarn", LoadIcon("Warn"));
                HudSet(so, "iconX", LoadIcon("X"));
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(contents, path);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>
        /// Drops the old shader quad. ⚠️ It may BE the component's own object (that is how
        /// <c>NO_patty</c> is built), in which case only the mesh parts go — destroying the object
        /// would take the thermometer with it.
        /// </summary>
        private static void LkDropGauge(Renderer renderer, GameObject host)
        {
            if (renderer == null)
            {
                return;
            }

            if (renderer.gameObject != host)
            {
                UiObject.DestroyImmediate(renderer.gameObject);
                return;
            }

            var filter = renderer.GetComponent<MeshFilter>();
            UiObject.DestroyImmediate(renderer);
            if (filter != null)
            {
                UiObject.DestroyImmediate(filter);
            }
        }

        private static T LkChild<T>(Transform root, string path) where T : Component
        {
            Transform child = root.Find(path);
            if (child == null)
            {
                Debug.LogError($"[Lokanta] {LkThermoName}/{path} yok.");
                return null;
            }

            var comp = child.GetComponent<T>();
            if (comp == null)
            {
                Debug.LogError($"[Lokanta] {LkThermoName}/{path} üzerinde {typeof(T).Name} yok.");
            }

            return comp;
        }

        /// <summary>First component with this type name anywhere under <paramref name="root"/>.</summary>
        private static Component LkFindComponentDeep(GameObject root, string typeName)
        {
            Component[] parts = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] != null && parts[i].GetType().Name == typeName)
                {
                    return parts[i];
                }
            }

            return null;
        }
    }
}
