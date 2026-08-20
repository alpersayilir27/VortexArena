using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VortexArena.Core.Player;

namespace VortexArena.Core.Editor
{
    /// <summary>Installs the two warning texts (inside an obstacle + out of bounds) and the damage
    /// vignette onto <c>CenterEyeAnchor</c> inside <c>VA_CameraRig.prefab</c>. Idempotent.</summary>
    /// <remarks>
    /// <b>Why in the rig prefab:</b> the infrastructure prefab is instanced in every arena, so an
    /// overlay put here reaches all of them for free and adds no per-arena setup step. Same reason
    /// it is head locked: as a child of <c>CenterEyeAnchor</c> the text sits dead centre of the view
    /// with no follow/smoothing component (<c>HudFollow</c>'s lazy follow is for HUD panels; a
    /// violation warning must not drift away before it is read).
    /// <para><b>Why a tool and not hand written YAML:</b> the vignette material is generated here
    /// (a shader's GUID is unknown before import) and the rig prefab is every arena's
    /// infrastructure, so one broken line takes them all down. ⚠️ The font is bound here too:
    /// <c>AddComponent&lt;TextMesh&gt;</c> assigns none and a fontless <c>TextMesh</c> generates no
    /// mesh, so the warning would silently never draw.</para>
    /// <para>Draw order comes from the DISTANCE in the prefab (texts 0.42 · vignette 0.44 · fade quad
    /// 0.5) — keep that order when changing numbers. The vignette is additionally in the
    /// <c>Overlay</c> queue; reason in <see cref="DamageVignette"/>.</para>
    /// <para>⚠️ Both texts can be on at once (the boundary counts scene <c>ArenaObstacle</c>s as out
    /// of bounds → <see cref="VortexArena.Core.Arena.ArenaBoundary"/>), so they are stacked
    /// vertically instead of overlapping: obstacle warning slightly above centre, out-of-bounds
    /// slightly below, separated by <see cref="WarningStackOffsetY"/> (~1.6° at that distance, so
    /// both stay "straight ahead").</para>
    /// </remarks>
    internal static class HmdOverlayBuilder
    {
        private const string MenuPath = "Tools/VortexArena/Arena/HMD Katmanlarını Kur";

        private const string RigPrefabPath = "Assets/_Shared/App/Prefabs/VA_CameraRig.prefab";
        private const string VignetteMaterialPath = "Assets/_Shared/Materials/M_DamageVignette.mat";
        private const string VignetteShaderName = "VortexArena/ScreenVignette";

        private const string AnchorName = "CenterEyeAnchor";
        private const string WarningObjectName = "ObstacleWarningText";
        private const string BoundaryWarningObjectName = "BoundaryWarningText";
        private const string VignetteObjectName = "DamageVignette";

        /// <summary>Distance of the warning texts from the camera (m) — must be NEARER than the fade
        /// quad.</summary>
        private const float WarningZ = 0.42f;

        /// <summary>Vertical offset of each warning from centre (m, at <see cref="WarningZ"/>).
        /// Both can be on at once, so they must not overlap; 1.2 cm is ~1.6° at that distance —
        /// enough to read them apart without leaving the centre of view.</summary>
        private const float WarningStackOffsetY = 0.012f;

        /// <summary>Scale factor of the text. ⚠️ Size is tuned from
        /// <see cref="WarningCharacterSize"/>, NOT here; this constant only maps <c>TextMesh</c>
        /// pixel space to metres and is identical for both texts (<c>ObstacleWarningOverlay</c>
        /// pulses around it).</summary>
        private const float WarningScale = 0.01f;

        /// <summary>Visible size of the text: line height ≈
        /// <c>FontSize × CharacterSize / 10 × Scale</c> = 0.0072 m, i.e. ~1° of line at
        /// <see cref="WarningZ"/> (below ~0.5° it is under the readability limit).</summary>
        /// <remarks>⚠️ Scale up via character size, not <see cref="WarningFontSize"/>: at this
        /// distance the atlas is already ~6× oversampled (0.05 mm/texel ≈ a sixth of a Quest 3
        /// screen pixel), so a bigger font size would only burn atlas memory.</remarks>
        private const float WarningCharacterSize = 0.1f;

        /// <summary>Font rasterization size (px) — drives sharpness, not visible size.</summary>
        private const int WarningFontSize = 72;

        /// <summary>⚠️ ASCII only: the builtin font is used and may not draw Turkish characters.
        /// Same contract for the out-of-bounds warning.</summary>
        private const string WarningMessage = "DUVARIN ICINDESIN!\nOYUN ALANINA DON";

        /// <summary>Out-of-bounds warning — driven by the scene's <c>ArenaBoundary</c>.</summary>
        private const string BoundaryWarningMessage = "OYUN ALANINA GERI DONUN!";

        /// <summary>Obstacle warning colour (amber).</summary>
        private static readonly Color WarningColor = new Color(1f, 0.85f, 0.2f, 1f);

        /// <summary>Out-of-bounds warning colour (red) — the two violations read apart at a
        /// glance.</summary>
        private static readonly Color BoundaryWarningColor = new Color(1f, 0.25f, 0.2f, 1f);

        /// <summary>Distance of the vignette from the camera (m).</summary>
        private const float VignetteZ = 0.44f;

        /// <summary>Edge of the vignette quad (m): covers ~130° at 0.44 m, i.e. the whole FOV.</summary>
        private const float VignetteSize = 1.9f;

        /// <summary>Check tolerance. ⚠️ Not <c>Mathf.Approximately</c>: its epsilon scales with
        /// magnitude and the numbers compared here (0.012 m, 0.01 scale) are tiny — a text nudged by
        /// a centimetre would count as "equal".</summary>
        private const float CheckEpsilon = 1e-4f;

        [MenuItem(MenuPath, false, 40)]
        internal static void BuildOverlays()
        {
            Material vignetteMaterial = LoadOrCreateVignetteMaterial();
            if (vignetteMaterial == null)
            {
                return;
            }

            Font font = LoadBuiltinFont();
            if (font == null)
            {
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(RigPrefabPath);
            if (root == null)
            {
                Debug.LogError($"[HmdOverlay] Prefab açılamadı: {RigPrefabPath}");
                return;
            }

            try
            {
                Transform anchor = FindByName(root.transform, AnchorName);
                if (anchor == null)
                {
                    Debug.LogError($"[HmdOverlay] '{AnchorName}' bulunamadı: {RigPrefabPath}. " +
                                   "Rig prefabı beklenen yapıda değil.");
                    return;
                }

                ConfigureObstacleWarning(anchor, font);
                ConfigureBoundaryWarning(anchor, font);
                ConfigureVignette(anchor, vignetteMaterial);

                PrefabUtility.SaveAsPrefabAsset(root, RigPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[HmdOverlay] Kuruldu: {RigPrefabPath} → {AnchorName}/{WarningObjectName} + " +
                      $"{AnchorName}/{BoundaryWarningObjectName} + {AnchorName}/{VignetteObjectName}.");
        }

        // ------------------------------------------------------------------ check

        /// <summary>Whether the rig prefab's three overlays still match this tool's output —
        /// <b>WRITES NOTHING</b> (read by the build readiness panel; the user pulls the write
        /// trigger).</summary>
        /// <remarks>⚠️ The prefab is loaded read-only (<see cref="AssetDatabase.LoadAssetAtPath"/>);
        /// <c>LoadPrefabContents</c> is NOT used: it is expensive for a check that runs on every
        /// window focus and it opens the rig into a scene, spamming OVR warnings each time.</remarks>
        /// <param name="detail">First mismatch, or a short summary when up to date.</param>
        internal static bool IsRigUpToDate(out string detail)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
            if (root == null)
            {
                detail = $"Rig prefabı bulunamadı: {RigPrefabPath}";
                return false;
            }

            Transform anchor = FindByName(root.transform, AnchorName);
            if (anchor == null)
            {
                detail = $"'{AnchorName}' yok — rig prefabı beklenen yapıda değil.";
                return false;
            }

            if (!CheckWarningText(anchor, WarningObjectName, WarningStackOffsetY, out detail))
            {
                return false;
            }

            Transform obstacle = anchor.Find(WarningObjectName);
            if (obstacle.GetComponent<ObstacleWarningOverlay>() == null)
            {
                detail = $"{WarningObjectName} üstünde {nameof(ObstacleWarningOverlay)} yok — " +
                         "engel uyarısını hiçbir şey sürmez.";
                return false;
            }

            if (!CheckWarningText(anchor, BoundaryWarningObjectName, -WarningStackOffsetY, out detail))
            {
                return false;
            }

            Transform vignette = anchor.Find(VignetteObjectName);
            if (vignette == null)
            {
                detail = $"{VignetteObjectName} yok — hasar vinyeti hiç çizilmez.";
                return false;
            }

            if (vignette.GetComponent<DamageVignette>() == null)
            {
                detail = $"{VignetteObjectName} üstünde {nameof(DamageVignette)} bileşeni yok.";
                return false;
            }

            var vignetteRenderer = vignette.GetComponent<MeshRenderer>();
            if (vignetteRenderer == null || vignetteRenderer.sharedMaterial == null)
            {
                detail = $"{VignetteObjectName} materyalsiz — vinyet Quest'te pembe çizilir " +
                         "(materyali bu araç üretir).";
                return false;
            }

            detail = "3 katman güncel (engel uyarısı · sınır uyarısı · hasar vinyeti).";
            return true;
        }

        /// <summary>Whether a warning text still matches what <see cref="ConfigureWarningText"/>
        /// writes.</summary>
        /// <remarks>A fontless or resized text logs no error, it just becomes unreadable, so every
        /// measure is compared one by one.</remarks>
        private static bool CheckWarningText(Transform anchor, string objectName, float localY, out string detail)
        {
            Transform go = anchor.Find(objectName);
            if (go == null)
            {
                detail = $"{objectName} yok — uyarı hiç çizilmez.";
                return false;
            }

            var text = go.GetComponent<TextMesh>();
            if (text == null)
            {
                detail = $"{objectName} üstünde TextMesh yok.";
                return false;
            }

            if (text.font == null)
            {
                detail = $"{objectName}.font BOŞ — fontsuz TextMesh hiç mesh üretmez.";
                return false;
            }

            if (!Same(text.characterSize, WarningCharacterSize))
            {
                detail = $"{objectName}.characterSize {text.characterSize}, beklenen {WarningCharacterSize}";
                return false;
            }

            if (text.fontSize != WarningFontSize)
            {
                detail = $"{objectName}.fontSize {text.fontSize}, beklenen {WarningFontSize}";
                return false;
            }

            var expectedPosition = new Vector3(0f, localY, WarningZ);
            if (!Same(go.localPosition, expectedPosition))
            {
                detail = $"{objectName}.localPosition {go.localPosition}, beklenen {expectedPosition}";
                return false;
            }

            var expectedScale = Vector3.one * WarningScale;
            if (!Same(go.localScale, expectedScale))
            {
                detail = $"{objectName}.localScale {go.localScale}, beklenen {expectedScale}";
                return false;
            }

            if (Quaternion.Angle(go.localRotation, Quaternion.identity) > 0.01f)
            {
                detail = $"{objectName} döndürülmüş — yazı görüşün ortasına dik bakmıyor.";
                return false;
            }

            detail = null;
            return true;
        }

        private static bool Same(float a, float b)
        {
            return Mathf.Abs(a - b) <= CheckEpsilon;
        }

        private static bool Same(Vector3 a, Vector3 b)
        {
            return Same(a.x, b.x) && Same(a.y, b.y) && Same(a.z, b.z);
        }

        // ------------------------------------------------------------------ warning texts

        /// <summary>Warning shown while inside an obstacle. Driven by its own component
        /// (<see cref="ObstacleWarningOverlay"/>), so the object is left ACTIVE — visibility is
        /// handled through the Renderer.</summary>
        private static void ConfigureObstacleWarning(Transform anchor, Font font)
        {
            TextMesh text = ConfigureWarningText(anchor, WarningObjectName, WarningMessage,
                WarningColor, WarningStackOffsetY, font);

            GameObject go = text.gameObject;
            ObstacleWarningOverlay overlay = go.GetComponent<ObstacleWarningOverlay>();
            if (overlay == null)
            {
                overlay = go.AddComponent<ObstacleWarningOverlay>();
            }

            AssignReference(overlay, "warningText", text);
        }

        /// <summary>Warning shown while out of bounds. ⚠️ It carries NO component: the scene's
        /// <c>ArenaBoundary</c> toggles the object (wired by <c>TemplateBasicsLoader</c>), so it is
        /// created INACTIVE — left active it would flash for one frame on entering the
        /// arena.</summary>
        private static void ConfigureBoundaryWarning(Transform anchor, Font font)
        {
            TextMesh text = ConfigureWarningText(anchor, BoundaryWarningObjectName,
                BoundaryWarningMessage, BoundaryWarningColor, -WarningStackOffsetY, font);

            text.gameObject.SetActive(false);
        }

        /// <summary>Shared body of both warnings: head locked text of the same size at the centre of
        /// view.</summary>
        /// <remarks>⚠️ Font and material are assigned explicitly:
        /// <c>AddComponent&lt;TextMesh&gt;</c> assigns no font and a fontless <c>TextMesh</c>
        /// generates no mesh — the warning would silently never draw.</remarks>
        private static TextMesh ConfigureWarningText(Transform anchor, string objectName,
            string message, Color color, float localY, Font font)
        {
            Transform existing = anchor.Find(objectName);
            GameObject go = existing != null ? existing.gameObject : new GameObject(objectName);
            if (existing == null)
            {
                go.transform.SetParent(anchor, false);
            }

            // The text is a CHILD of the head: zeroed position/rotation keeps it dead centre of the
            // view. ⚠️ Rewritten on every run — a hand nudged instance would push the warning to the
            // edge of vision and nobody would notice.
            go.transform.localPosition = new Vector3(0f, localY, WarningZ);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * WarningScale;

            TextMesh text = go.GetComponent<TextMesh>();
            if (text == null)
            {
                text = go.AddComponent<TextMesh>();
            }

            text.text = message;
            text.font = font;
            text.characterSize = WarningCharacterSize;
            text.fontSize = WarningFontSize;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = color;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = font.material;
                StripLighting(renderer);
            }

            return text;
        }

        // ------------------------------------------------------------------ damage vignette

        private static void ConfigureVignette(Transform anchor, Material material)
        {
            Transform existing = anchor.Find(VignetteObjectName);
            GameObject go;

            if (existing != null)
            {
                go = existing.gameObject;
            }
            else
            {
                // Builtin Quad mesh instead of building one by hand; its collider is removed (a
                // screen overlay has no business in physics and the shot ray is unmasked).
                go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = VignetteObjectName;
                go.transform.SetParent(anchor, false);

                var collider = go.GetComponent<Collider>();
                if (collider != null)
                {
                    Object.DestroyImmediate(collider);
                }
            }

            go.transform.localPosition = new Vector3(0f, 0f, VignetteZ);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(VignetteSize, VignetteSize, 1f);

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                Debug.LogError($"[HmdOverlay] '{VignetteObjectName}' üstünde MeshRenderer yok.");
                return;
            }

            renderer.sharedMaterial = material;
            renderer.enabled = false; // nothing to draw at alpha 0; the component decides on frame one
            StripLighting(renderer);

            DamageVignette vignette = go.GetComponent<DamageVignette>();
            if (vignette == null)
            {
                vignette = go.AddComponent<DamageVignette>();
            }

            AssignReference(vignette, "vignetteRenderer", renderer);
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Vignette material. ⚠️ Generated by the tool: a shader's GUID is unknown before
        /// import, so a hand written <c>.mat</c> would open with a silently empty shader
        /// reference.</summary>
        /// <remarks>The material is an ASSET referenced from the prefab — created at runtime with
        /// <c>Shader.Find</c> the shader would be stripped from the build and the vignette would
        /// draw pink on Quest (same trap as <c>M_BaseZoneXRay</c>).</remarks>
        private static Material LoadOrCreateVignetteMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(VignetteMaterialPath);
            if (existing != null)
            {
                return existing;
            }

            Shader shader = Shader.Find(VignetteShaderName);
            if (shader == null)
            {
                Debug.LogError($"[HmdOverlay] Shader bulunamadı: {VignetteShaderName}. " +
                               "Assets/_Shared/Shaders/ScreenVignette.shader import edilmiş mi?");
                return null;
            }

            var material = new Material(shader) { name = "M_DamageVignette" };
            AssetDatabase.CreateAsset(material, VignetteMaterialPath);
            Debug.Log($"[HmdOverlay] Vinyet materyali üretildi: {VignetteMaterialPath}");
            return material;
        }

        /// <summary>Builtin font. ⚠️ Its name changed with the Unity version:
        /// <c>LegacyRuntime.ttf</c> since 2022.2, <c>Arial.ttf</c> before. A missing name returns
        /// null without an error, so both are tried and the result is never left silently
        /// empty.</summary>
        private static Font LoadBuiltinFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                        ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

            if (font == null)
            {
                Debug.LogError("[HmdOverlay] Yerleşik font bulunamadı (LegacyRuntime.ttf / Arial.ttf) — " +
                               "uyarı yazıları fontsuz kalırdı, kurulum iptal edildi.");
            }

            return font;
        }

        /// <summary>Screen overlays stay out of lighting: no shadows, no probes.</summary>
        private static void StripLighting(Renderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        /// <summary>Wires a <c>[SerializeField] private</c> field.</summary>
        /// <remarks>A missing field is not passed over silently: the component recovers via
        /// <c>GetComponent</c> in <c>Awake</c>, but a broken link is a rename bug and must be
        /// visible.</remarks>
        private static void AssignReference(Object target, string fieldName, Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogWarning($"[HmdOverlay] {target.GetType().Name}.{fieldName} alanı bulunamadı — " +
                                 "bileşen kendi renderer'ını çalışma anında çözecek.");
                return;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Transform FindByName(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindByName(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
