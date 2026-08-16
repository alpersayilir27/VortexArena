using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VortexArena.Core.Player;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// <c>Tools &gt; VortexArena &gt; Arena &gt; HMD Katmanlarını Kur</c> — <c>VA_CameraRig.prefab</c>
    /// içindeki <c>CenterEyeAnchor</c>'a <b>iki uyarı yazısını</b> (engelin içi + alanın dışı) ve
    /// hasar vinyetini kurar. İdempotenttir, tekrar çalıştırmak güvenlidir.
    ///
    /// <para><b>Neden rig prefabında:</b> altyapı prefabı tüm arenalarda örnek olarak duruyor, yani
    /// buraya konan bir katman <b>her arenaya bedavaya</b> gider ve yeni arena bir kurulum adımı
    /// daha doğurmaz. Aynı sebeple <b>kafaya kilitlidir</b>: yazı <c>CenterEyeAnchor</c>'ın çocuğu
    /// olduğu için oyuncunun baktığı yerde, görüşün tam ortasında durur — takip/yumuşatma yapan bir
    /// bileşene ihtiyaç yoktur (<c>HudFollow</c>'un tembel takibi HUD panelleri içindir; ihlal
    /// uyarısı okunana kadar kaçmamalıdır).</para>
    ///
    /// <para><b>Neden araç, neden elle YAML değil:</b> vinyetin materyali <b>bu araç tarafından
    /// üretiliyor</b> (shader import edilmeden GUID'i bilinemez) ve rig prefabı tüm arenaların
    /// altyapısıdır — elle bozulan bir satır hepsini birden düşürür. ⚠️ Yazının <b>fontu da burada
    /// bağlanır</b>: <c>AddComponent&lt;TextMesh&gt;</c> font atamaz ve fontsuz <c>TextMesh</c> hiç
    /// mesh üretmez, yani uyarı sessizce hiç çizilmez.</para>
    ///
    /// <para><b>Çizim sırası prefabdaki MESAFEDEN gelir</b> (yazılar 0.42 · vinyet 0.44 · karartma
    /// quad'ı 0.5) — sayıları değiştirirken sıra korunmalıdır. Vinyet ayrıca <c>Overlay</c>
    /// kuyruğundadır, yani mesafeye ek olarak her şeyin üstünde çizilir; gerekçesi
    /// <see cref="DamageVignette"/>'de.</para>
    ///
    /// <para>⚠️ <b>İki yazı aynı anda açık olabilir</b> (muhafaza sahnedeki <c>ArenaObstacle</c>'ları
    /// da alan dışı sayar → <see cref="VortexArena.Core.Arena.ArenaBoundary"/>), bu yüzden
    /// üst üste değil <b>dikey olarak istiflenirler</b>: engel uyarısı merkezin biraz üstünde,
    /// alan-dışı uyarısı biraz altında. Ayrım <see cref="WarningStackOffsetY"/> kadardır — o
    /// mesafede ~1.6°, yani ikisi de hâlâ "tam karşıda".</para>
    /// </summary>
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

        /// <summary>Uyarı yazılarının kameradan uzaklığı (m) — karartma quad'ından YAKIN olmalı.</summary>
        private const float WarningZ = 0.42f;

        /// <summary>
        /// İki uyarı yazısının merkeze göre dikey ayrımı (m, <see cref="WarningZ"/> mesafesinde).
        /// İkisi de aynı anda açık olabildiği için üst üste binmemeleri gerekir; 1.2 cm bu mesafede
        /// ~1.6°, yani ayrım okunurluğu kurtarır ama yazıyı görüşün ortasından çıkarmaz.
        /// </summary>
        private const float WarningStackOffsetY = 0.012f;

        /// <summary>
        /// Yazının ölçek çarpanı. ⚠️ <b>Boyut buradan DEĞİL</b> <see cref="WarningCharacterSize"/>'dan
        /// ayarlanır; bu sabit yalnız <c>TextMesh</c>'in piksel uzayını metreye indiren sözleşmedir
        /// ve iki yazıda da aynıdır (<c>ObstacleWarningOverlay</c> nabız atarken bunu taban alır).
        /// </summary>
        private const float WarningScale = 0.01f;

        /// <summary>
        /// Yazının büyüklüğü. Satır yüksekliği ≈ <c>FontSize × CharacterSize / 10 × Scale</c> =
        /// 0.0072 m; <see cref="WarningZ"/> mesafesinde ~1°'lik satır demektir (öncesi ~0.5°'ydi,
        /// yani okunabilirlik sınırının altında).
        /// <para>⚠️ Büyütme <b>karakter boyundan</b> yapılır, <see cref="WarningFontSize"/>'dan
        /// değil: atlas bu mesafede zaten ~6× fazla örneklenmiş (0.05 mm/texel ≈ Quest 3'ün ekran
        /// pikselinin altıda biri), yani font boyunu büyütmek yalnız atlas belleği harcardı.</para>
        /// </summary>
        private const float WarningCharacterSize = 0.1f;

        /// <summary>Fontun rasterleştirme boyu (px) — görünen büyüklüğü değil, keskinliği belirler.</summary>
        private const int WarningFontSize = 72;

        /// <summary>
        /// ⚠️ ASCII: yerleşik (builtin) font kullanılıyor — Türkçe karakterler bu fontta
        /// çizilmeyebilir. Aynı sözleşme alan-dışı uyarısı için de geçerlidir.
        /// </summary>
        private const string WarningMessage = "DUVARIN ICINDESIN!\nOYUN ALANINA DON";

        /// <summary>Alan-dışı uyarısı — sürücüsü sahnedeki <c>ArenaBoundary</c>'dir.</summary>
        private const string BoundaryWarningMessage = "OYUN ALANINA GERI DONUN!";

        /// <summary>Engel uyarısının rengi (kehribar).</summary>
        private static readonly Color WarningColor = new Color(1f, 0.85f, 0.2f, 1f);

        /// <summary>Alan-dışı uyarısının rengi (kırmızı) — iki ihlal bakışta ayrılsın.</summary>
        private static readonly Color BoundaryWarningColor = new Color(1f, 0.25f, 0.2f, 1f);

        /// <summary>Vinyetin kameradan uzaklığı (m).</summary>
        private const float VignetteZ = 0.44f;

        /// <summary>Vinyet quad'ının kenarı (m): 0.44 m'de ~130° kapsar, yani FOV'un tamamı.</summary>
        private const float VignetteSize = 1.9f;

        /// <summary>
        /// Denetim toleransı. ⚠️ <c>Mathf.Approximately</c> DEĞİL: o, büyüklüğe göre ölçeklenen
        /// bir epsilon kullanıyor ve burada karşılaştırılan sayılar (0.012 m, 0.01 ölçek) çok
        /// küçük — elle santim mertebesinde kaydırılmış bir yazı "eşit" sayılırdı.
        /// </summary>
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

        // ------------------------------------------------------------------ denetim

        /// <summary>
        /// Rig prefabındaki üç ekran katmanı bu araçtan çıkmış hâliyle duruyor mu — <b>HİÇBİR ŞEY
        /// YAZMAZ</b> (build hazırlık panelinin okuduğu denetim; yazma tetiğini kullanıcı çeker).
        /// <para>
        /// ⚠️ Prefab <b>salt okunur</b> yüklenir (<see cref="AssetDatabase.LoadAssetAtPath"/>);
        /// <c>LoadPrefabContents</c> KULLANILMAZ: pencere her odaklandığında koşan bir denetim için
        /// pahalıdır ve rig'i sahneye açtığı için her seferinde OVR uyarıları basar.
        /// </para>
        /// </summary>
        /// <param name="detail">İlk uyuşmazlık (ya da güncelse kısa özet).</param>
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

        /// <summary>
        /// Bir uyarı yazısının kurulumu <see cref="ConfigureWarningText"/>'in yazdığıyla aynı mı.
        /// Fontsuz/ölçüsü kaymış yazı hata basmaz, yalnız okunmaz olur — bu yüzden ölçüler tek tek
        /// karşılaştırılır.
        /// </summary>
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

        // ------------------------------------------------------------------ uyarı yazıları

        /// <summary>
        /// Engelin içindeyken çıkan uyarı. Sürücüsü kendi bileşenidir
        /// (<see cref="ObstacleWarningOverlay"/>), bu yüzden objesi <b>açık</b> bırakılır —
        /// görünürlüğü Renderer'dan yönetiliyor.
        /// </summary>
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

        /// <summary>
        /// Alanın dışındayken çıkan uyarı. ⚠️ Burada <b>bileşen yoktur</b>: objeyi sahnedeki
        /// <c>ArenaBoundary</c> açıp kapatıyor (<c>TemplateBasicsLoader</c> bağlıyor), bu yüzden
        /// obje <b>kapalı</b> kurulur — açık bırakılırsa uyarı arenaya girer girmez bir kare
        /// boyunca çakar.
        /// </summary>
        private static void ConfigureBoundaryWarning(Transform anchor, Font font)
        {
            TextMesh text = ConfigureWarningText(anchor, BoundaryWarningObjectName,
                BoundaryWarningMessage, BoundaryWarningColor, -WarningStackOffsetY, font);

            text.gameObject.SetActive(false);
        }

        /// <summary>
        /// İki uyarının ortak gövdesi: kafaya kilitli, görüşün ortasında duran, aynı boyda yazı.
        /// <para>⚠️ <b>Font ve materyal açıkça bağlanır</b>: <c>AddComponent&lt;TextMesh&gt;</c> font
        /// atamıyor ve fontsuz <c>TextMesh</c> hiç mesh üretmiyor — uyarı hata vermeden, sessizce
        /// hiç çizilmemiş olurdu.</para>
        /// </summary>
        private static TextMesh ConfigureWarningText(Transform anchor, string objectName,
            string message, Color color, float localY, Font font)
        {
            Transform existing = anchor.Find(objectName);
            GameObject go = existing != null ? existing.gameObject : new GameObject(objectName);
            if (existing == null)
            {
                go.transform.SetParent(anchor, false);
            }

            // Yazı kafanın ÇOCUĞUDUR: konum/dönüş sıfırlanınca oyuncunun baktığı yerde, görüşün tam
            // ortasında kalır. ⚠️ Her koşuda geri yazılır — elle kaydırılmış bir örnek, uyarıyı
            // gözün kenarına iter ve bunu kimse fark etmez.
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

        /// <summary>
        /// Yerleşik (builtin) font. ⚠️ Adı Unity sürümüyle değişti: 2022.2'den beri
        /// <c>LegacyRuntime.ttf</c>, öncesinde <c>Arial.ttf</c>. Bulunamayan ad <b>null döner,
        /// hata basmaz</b> — bu yüzden ikisi de denenir ve sonuç sessizce boş bırakılmaz.
        /// </summary>
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
