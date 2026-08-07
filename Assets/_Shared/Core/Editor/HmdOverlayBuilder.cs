using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VortexArena.Core.Player;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// <c>Tools &gt; VortexArena &gt; Arena &gt; HMD Katmanlarını Kur</c> — <c>VA_CameraRig.prefab</c>
    /// içindeki <c>CenterEyeAnchor</c>'a engel uyarı yazısını ve hasar vinyetini kurar.
    /// İdempotenttir, tekrar çalıştırmak güvenlidir.
    ///
    /// <para><b>Neden rig prefabında:</b> altyapı prefabı tüm arenalarda örnek olarak duruyor, yani
    /// buraya konan bir katman <b>her arenaya bedavaya</b> gider ve yeni arena bir kurulum adımı
    /// daha doğurmaz. İkisi de kendi renderer'ının sahibi olduğu için sahne tarafında bağlanacak
    /// bir alan da yoktur (karartma quad'ından farkı budur: onun sürücüsü <c>ArenaBoundary</c>,
    /// yani sahnede yaşayan başka bir objedir).</para>
    ///
    /// <para><b>Neden araç, neden elle YAML değil:</b> vinyetin materyali <b>bu araç tarafından
    /// üretiliyor</b> (shader import edilmeden GUID'i bilinemez) ve rig prefabı tüm arenaların
    /// altyapısıdır — elle bozulan bir satır hepsini birden düşürür.</para>
    ///
    /// <para><b>Çizim sırası prefabdaki MESAFEDEN gelir</b> (yazı 0.42 · vinyet 0.44 · karartma
    /// quad'ı 0.5) — sayıları değiştirirken sıra korunmalıdır. Vinyet ayrıca <c>Overlay</c>
    /// kuyruğundadır, yani mesafeye ek olarak her şeyin üstünde çizilir; gerekçesi
    /// <see cref="DamageVignette"/>'de.</para>
    /// </summary>
    internal static class HmdOverlayBuilder
    {
        private const string MenuPath = "Tools/VortexArena/Arena/HMD Katmanlarını Kur";

        private const string RigPrefabPath = "Assets/_Shared/App/Prefabs/VA_CameraRig.prefab";
        private const string VignetteMaterialPath = "Assets/_Shared/Materials/M_DamageVignette.mat";
        private const string VignetteShaderName = "VortexArena/ScreenVignette";

        private const string AnchorName = "CenterEyeAnchor";
        private const string WarningObjectName = "ObstacleWarningText";
        private const string VignetteObjectName = "DamageVignette";

        /// <summary>Uyarı yazısının kameradan uzaklığı (m) — karartma quad'ından YAKIN olmalı.</summary>
        private const float WarningZ = 0.42f;

        /// <summary>Vinyetin kameradan uzaklığı (m).</summary>
        private const float VignetteZ = 0.44f;

        /// <summary>Vinyet quad'ının kenarı (m): 0.44 m'de ~130° kapsar, yani FOV'un tamamı.</summary>
        private const float VignetteSize = 1.9f;

        /// <summary>
        /// ⚠️ ASCII: yerleşik (builtin) font kullanılıyor ve rig'deki sınır uyarısı da aynı
        /// sözleşmeyi izliyor — Türkçe karakterler bu fontta çizilmeyebilir.
        /// </summary>
        private const string WarningMessage = "DUVARIN ICINDESIN!\nOYUN ALANINA DON";

        [MenuItem(MenuPath, false, 40)]
        private static void BuildOverlays()
        {
            Material vignetteMaterial = LoadOrCreateVignetteMaterial();
            if (vignetteMaterial == null)
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

                ConfigureWarning(anchor);
                ConfigureVignette(anchor, vignetteMaterial);

                PrefabUtility.SaveAsPrefabAsset(root, RigPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[HmdOverlay] Kuruldu: {RigPrefabPath} → {AnchorName}/{WarningObjectName} + " +
                      $"{AnchorName}/{VignetteObjectName}.");
        }

        // ------------------------------------------------------------------ uyarı yazısı

        private static void ConfigureWarning(Transform anchor)
        {
            Transform existing = anchor.Find(WarningObjectName);
            GameObject go = existing != null ? existing.gameObject : new GameObject(WarningObjectName);
            if (existing == null)
            {
                go.transform.SetParent(anchor, false);
            }

            go.transform.localPosition = new Vector3(0f, 0f, WarningZ);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * 0.01f;

            // Sınır uyarısıyla aynı ölçüler: aynı mesafede aynı boyda okunsun.
            TextMesh text = go.GetComponent<TextMesh>();
            if (text == null)
            {
                text = go.AddComponent<TextMesh>();
            }

            text.text = WarningMessage;
            text.characterSize = 0.05f;
            text.fontSize = 72;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = new Color(1f, 0.85f, 0.2f, 1f);

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                StripLighting(renderer);
            }

            ObstacleWarningOverlay overlay = go.GetComponent<ObstacleWarningOverlay>();
            if (overlay == null)
            {
                overlay = go.AddComponent<ObstacleWarningOverlay>();
            }

            AssignReference(overlay, "warningText", text);
        }

        // ------------------------------------------------------------------ hasar vinyeti

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
                // Yerleşik Quad mesh'i: elle mesh kurmak yerine primitive üretilir, collider'ı
                // atılır (ekran katmanının fizikte hiçbir işi yok ve atış ışını maskesizdir).
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
            renderer.enabled = false; // alfa 0'da çizilmez; bileşen ilk karede zaten karar verir
            StripLighting(renderer);

            DamageVignette vignette = go.GetComponent<DamageVignette>();
            if (vignette == null)
            {
                vignette = go.AddComponent<DamageVignette>();
            }

            AssignReference(vignette, "vignetteRenderer", renderer);
        }

        // ------------------------------------------------------------------ yardımcılar

        /// <summary>
        /// Vinyet materyali. ⚠️ <b>Araç üretir</b>: shader import edilmeden GUID'i bilinemediği için
        /// elle yazılmış bir <c>.mat</c> dosyası sessizce boş shader referansıyla açılırdı.
        /// <para>Materyal bir <b>asset</b>tir ve prefabtan referanslanır — çalışma anında
        /// <c>Shader.Find</c> ile üretilseydi shader build'den strip edilir ve vinyet Quest'te
        /// pembe çizilirdi (<c>M_BaseZoneXRay</c> ile aynı tuzak).</para>
        /// </summary>
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

        /// <summary>Ekran katmanı ışıklandırmaya girmez: gölge yok, probe yok.</summary>
        private static void StripLighting(Renderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        /// <summary>
        /// <c>[SerializeField] private</c> bir alanı bağlar. Alan gerçekten yoksa sessiz kalmaz:
        /// bileşen <c>Awake</c>'te <c>GetComponent</c> ile kendini kurtarıyor ama bağın kopması bir
        /// yeniden adlandırma hatasıdır ve görünmelidir.
        /// </summary>
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
