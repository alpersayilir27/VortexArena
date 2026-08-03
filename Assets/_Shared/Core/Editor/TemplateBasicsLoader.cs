using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core.Arena;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// <c>Tools &gt; VortexArena &gt; Arena &gt; Template Temellerini Yükle</c> — aktif sahneye, arenanın ağa
    /// bağlanması için gereken altyapıyı koyar. Yeni bir arena boş bir sahneden başlar ve bu araçla
    /// donatılır (sahne kopyalayan sihirbaz kaldırıldı).
    /// <para>
    /// ⚠️ <b>Her şey PREFAB ÖRNEĞİ olarak konur — kopyalanmaz, unpack edilmez.</b> Kopya konursa
    /// rig/kalibrasyon kurulumundaki tek bir düzeltme arena sayısı kadar elle iş doğurur.
    /// </para>
    /// <para>
    /// ⚠️ <b>Idempotent:</b> aynı prefabın örneği zaten varsa atlanır. Aracı ikinci kez çalıştırmak
    /// sahneye ikinci bir rig ya da ikinci bir muhafaza koymaz.
    /// </para>
    /// <para>
    /// ⚠️ Sahneye ayrıca <c>OVRComprehensiveInteractionRig</c> ya da Building Blocks rig'i
    /// EKLENMEZ: BB kurulumu prefabı otomatik unpack eder ve ikisi de zaten <c>VA_CameraRig</c>
    /// içindedir.
    /// </para>
    /// <para>
    /// ⚠️ <b>Kalibrasyon işaretçilerini bu araç KOYMAZ</b> — onlar ölçü maketinin küpleridir ve
    /// <c>Arena &gt; JSON'dan DimensionMesh Üret</c> tarafından, boyut dosyasının
    /// <c>calibration</c> alanındaki noktalara üretilirler. Loader'ın onlara ikinci bir yerden
    /// dokunması, sahnede kimin haklı olduğu belirsiz iki kaynak üretirdi.
    /// </para>
    /// <para>
    /// ELDE kalan işler (sonuç raporu hatırlatır): ölçü maketini üret (kalibrasyon işaretçileri
    /// oradan gelir) · taban bölgelerini arenanın gerçek yerleşimine göre taşı · arena
    /// geometrisini <b>dünya orijinine göre</b> kur (zemin y=0) · environment sanatını kur ·
    /// NavMesh/ışık bake et.
    /// </para>
    /// </summary>
    public class TemplateBasicsLoader : EditorWindow
    {
        private const string PrefabRoot = "Assets/_Shared/App/Prefabs";
        private const string ArenaBoundaryPrefab = PrefabRoot + "/VA_ArenaBoundary.prefab";
        private const string CameraRigPrefab = PrefabRoot + "/VA_CameraRig.prefab";
        private const string PoseSyncPrefab = PrefabRoot + "/VA_PoseSync.prefab";
        private const string CalibrationPrefab = PrefabRoot + "/VA_CalibrationManager.prefab";
        private const string ModeHudPrefab = PrefabRoot + "/VA_ModeHud.prefab";
        private const string BaseZonePrefab = PrefabRoot + "/VA_BaseZone.prefab";

        private const string VenuesRoot = "Assets/Arenas/Venues";

        // VA_CameraRig içindeki, muhafazanın baktığı objeler. Muhafaza kendi başına yalnız
        // 'head'i çözebiliyor (Camera.main); karartma quad'ı ile uyarı yazısının fallback'i YOK,
        // bağlanmazlarsa arena sessizce uyarısız kalır — bu yüzden burada bağlanırlar.
        private const string HeadName = "CenterEyeAnchor";
        private const string FadeQuadName = "OutOfBoundsFade";
        private const string WarningTextName = "BoundaryWarningText";

        private const string TeamRedMaterial = "Assets/Materials/M_TeamRed.mat";
        private const string TeamBlueMaterial = "Assets/Materials/M_TeamBlue.mat";

        [SerializeField] private bool includeModeHud = true;
        [SerializeField] private bool includeBaseZones = true;

        private Vector2 scroll;
        [System.NonSerialized] private List<string> lastReport;

        [MenuItem("Tools/VortexArena/Arena/Template Temellerini Yükle", false, 1)]
        private static void Open()
        {
            var window = GetWindow<TemplateBasicsLoader>(true, "Template Temelleri", true);
            window.minSize = new Vector2(440f, 300f);
            window.Show();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.LabelField("Aktif sahne: " + SceneManager.GetActiveScene().name, EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Her zaman konur", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField("VA_ArenaBoundary · VA_CameraRig · VA_PoseSync · VA_CalibrationManager",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Seçime bağlı", EditorStyles.miniBoldLabel);
            includeModeHud = EditorGUILayout.ToggleLeft(
                new GUIContent("VA_ModeHud", "Lobi sahnesinde İSTENMEZ — lobide mod HUD'ı yoktur."),
                includeModeHud);
            includeBaseZones = EditorGUILayout.ToggleLeft(
                new GUIContent("Taban bölgeleri (Base_Red / Base_Blue)",
                    "Lobi sahnesinde İSTENMEZ — lobide canlanma bölgesi yoktur."),
                includeBaseZones);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Hepsi prefab ÖRNEĞİ olarak konur ve var olanlar atlanır (idempotent). " +
                "Boyut dosyası mekan klasöründen çözülüp ArenaBoundary'ye bağlanır.\n\n" +
                "Kalibrasyon işaretçileri BURADAN gelmez: 'Arena > JSON'dan DimensionMesh Üret' " +
                "ile üretilen ölçü maketinin anchor_a/anchor_b küpleridir.",
                MessageType.Info);

            EditorGUILayout.Space();
            if (GUILayout.Button("Yükle", GUILayout.Height(28f)))
            {
                lastReport = Load(includeModeHud, includeBaseZones);
                for (int i = 0; i < lastReport.Count; i++)
                {
                    Debug.Log("[TemplateBasics] " + lastReport[i]);
                }
            }

            if (lastReport != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Son çalıştırma", EditorStyles.boldLabel);
                for (int i = 0; i < lastReport.Count; i++)
                {
                    EditorGUILayout.LabelField("• " + lastReport[i], EditorStyles.wordWrappedLabel);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        // ----------------------------------------------------------------- yükleme

        /// <summary>
        /// Aktif sahneye temelleri koyar ve ne yapıldığını satır satır döner. Exception FIRLATMAZ.
        /// </summary>
        public static List<string> Load(bool modeHud, bool baseZones)
        {
            var report = new List<string>();
            Scene scene = SceneManager.GetActiveScene();

            if (!scene.IsValid())
            {
                report.Add("HATA: geçerli bir aktif sahne yok.");
                return report;
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("VortexArena Template Temelleri");

            EnsurePrefab(ArenaBoundaryPrefab, report);
            GameObject cameraRig = EnsurePrefab(CameraRigPrefab, report);
            EnsurePrefab(PoseSyncPrefab, report);
            GameObject calibration = EnsurePrefab(CalibrationPrefab, report);

            if (modeHud)
            {
                EnsurePrefab(ModeHudPrefab, report);
            }

            if (baseZones)
            {
                EnsureBaseZones(report);
            }

            WireCalibration(calibration, cameraRig, report);
            WireBoundaryToRig(cameraRig, report);
            BindDimensions(scene, report);
            ReportCalibrationMarkers(report);

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);

            report.Add("ELDE: ölçü maketini üret (kalibrasyon işaretçileri ondan gelir) · " +
                       "taban bölgelerini gerçek yerleşime göre taşı · " +
                       "arena geometrisini DÜNYA ORİJİNİNE göre kur (zemin y=0; arena uzayı = " +
                       "dünya uzayı, sahneyi kaydırmak herkesin ağ konumunu kaydırır) · " +
                       "environment sanatını kur · NavMesh/ışık bake et.");
            report.Add("Sonra 'Tools > VortexArena > Build > Configure All Build Elements' çalıştır.");
            return report;
        }

        /// <summary>
        /// Prefabın sahnede bir örneği yoksa koyar. Var olan örneği DÖNER (çağıran bağlama için
        /// kullanıyor), bulunamayan prefab için rapora hata satırı düşer.
        /// </summary>
        private static GameObject EnsurePrefab(string prefabPath, List<string> report)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (asset == null)
            {
                report.Add($"HATA: prefab bulunamadı — '{prefabPath}'. Konmadı.");
                return null;
            }

            GameObject existing = FindInstanceOf(asset);
            if (existing != null)
            {
                report.Add($"atlandı (zaten var): {asset.name}");
                return existing;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            Undo.RegisterCreatedObjectUndo(instance, "Template Temeli");
            report.Add($"kondu: {asset.name}");
            return instance;
        }

        /// <summary>
        /// Sahnede verilen prefab asset'inin bir örneği var mı. Karşılaştırma <b>asset yolu</b>
        /// üzerinden yapılır: bileşen türüne bakmak, aynı bileşeni taşıyan başka bir objeyi
        /// yanlışlıkla "zaten var" saymaya açıktı.
        /// </summary>
        private static GameObject FindInstanceOf(GameObject prefabAsset)
        {
            string assetPath = AssetDatabase.GetAssetPath(prefabAsset);
            GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();

            for (int i = 0; i < roots.Length; i++)
            {
                Transform[] all = roots[i].GetComponentsInChildren<Transform>(true);
                for (int k = 0; k < all.Length; k++)
                {
                    GameObject candidate = all[k].gameObject;
                    if (!PrefabUtility.IsAnyPrefabInstanceRoot(candidate))
                    {
                        continue;
                    }

                    GameObject source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(candidate);
                    if (source != null && AssetDatabase.GetAssetPath(source) == assetPath)
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// İki taban bölgesi koyar (<c>Base_Red</c> / <c>Base_Blue</c>) ve takımlarını yazar.
        /// Sahnede zaten <see cref="BaseZone"/> varsa hiç dokunulmaz — yerleri elle ayarlanmış
        /// olabilir.
        /// </summary>
        private static void EnsureBaseZones(List<string> report)
        {
            BaseZone[] existing =
                Object.FindObjectsByType<BaseZone>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (existing.Length > 0)
            {
                report.Add($"atlandı (zaten var): taban bölgesi ×{existing.Length}");
                return;
            }

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(BaseZonePrefab);
            if (asset == null)
            {
                report.Add($"HATA: prefab bulunamadı — '{BaseZonePrefab}'. Taban bölgeleri konmadı.");
                return;
            }

            SpawnBaseZone(asset, "Base_Red", Team.Red, TeamRedMaterial);
            SpawnBaseZone(asset, "Base_Blue", Team.Blue, TeamBlueMaterial);
            report.Add("kondu: Base_Red + Base_Blue (yerleri ELLE ayarlanır)");
        }

        /// <summary>
        /// Tek bir <c>VA_BaseZone</c> prefabından takım rengine boyanmış bir bölge üretir.
        /// <para>
        /// ⚠️ Şerit rengi <b>çalışma anında boyanmaz</b> (kimse boyamıyor), yani takım başına ayrı
        /// bir prefab tutmamanın bedeli malzemeyi burada yazmaktır. Ayrı prefab yolu seçilmedi:
        /// şerit geometrisinde yapılacak tek bir düzeltme iki dosyada elle iş doğururdu.
        /// </para>
        /// </summary>
        private static void SpawnBaseZone(GameObject asset, string name, Team team, string materialPath)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            instance.name = name;
            Undo.RegisterCreatedObjectUndo(instance, "Taban Bölgesi");

            ApplyTeamMaterial(instance, materialPath);

            var zone = instance.GetComponent<BaseZone>();
            if (zone == null)
            {
                return;
            }

            var serialized = new SerializedObject(zone);
            SerializedProperty teamProp = serialized.FindProperty("team");
            if (teamProp != null)
            {
                teamProp.enumValueIndex = (int)team;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(zone);
            }
        }

        /// <summary>Bölgenin görsel şeritlerini takım malzemesiyle boyar (prefab override'ı).</summary>
        private static void ApplyTeamMaterial(GameObject instance, string materialPath)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                return;
            }

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].sharedMaterial = material;
                EditorUtility.SetDirty(renderers[i]);
            }
        }

        /// <summary>
        /// <see cref="ArenaCalibrator"/>'ın sahneye bakan alanını bağlar. Prefab asset'inde bu alan
        /// boş durur (normaldir) — yalnız sahne örneği üstünde doldurulabilir.
        /// <para>
        /// ⚠️ <c>anchorA</c>/<c>anchorB</c> BAĞLANMAZ: kalibratör işaretçileri çalışma anında
        /// maketin <see cref="DimensionAnchor"/> küplerinden (yoksa addan) kendisi çözer. Burada
        /// bağlamak, maket yeniden üretilince ölü referansa dönen bir kopya bırakırdı.
        /// </para>
        /// </summary>
        private static void WireCalibration(
            GameObject calibration,
            GameObject cameraRig,
            List<string> report)
        {
            if (calibration == null)
            {
                return;
            }

            var calibrator = calibration.GetComponentInChildren<ArenaCalibrator>(true);
            if (calibrator == null)
            {
                report.Add("UYARI: VA_CalibrationManager altında ArenaCalibrator yok — alanlar bağlanamadı.");
                return;
            }

            var serialized = new SerializedObject(calibrator);
            if (AssignObject(serialized, "rigRoot", cameraRig != null ? cameraRig.transform : null))
            {
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(calibrator);
                report.Add("bağlandı: ArenaCalibrator.rigRoot → VA_CameraRig");
            }
        }

        /// <summary>
        /// <see cref="ArenaBoundary"/>'nin rig'e bakan alanlarını bağlar: HMD, karartma quad'ı ve
        /// alan-dışı uyarı yazısı — üçü de <c>VA_CameraRig</c>'in içindedir.
        /// <para>
        /// ⚠️ Bu adım atlanırsa muhafaza <b>sessizce</b> işlevsizleşir: <c>head</c> için
        /// <c>Camera.main</c> fallback'i var, ama karartma ve uyarı için YOK — sınır aşılır,
        /// hiçbir şey olmaz. Boş alan görülebilir bir hata üretmediği için bağlama koda alındı.
        /// </para>
        /// </summary>
        private static void WireBoundaryToRig(GameObject cameraRig, List<string> report)
        {
            var boundary = Object.FindFirstObjectByType<ArenaBoundary>(FindObjectsInactive.Include);
            if (boundary == null || cameraRig == null)
            {
                return;
            }

            Transform head = FindDescendant(cameraRig.transform, HeadName);
            Transform fade = FindDescendant(cameraRig.transform, FadeQuadName);
            Transform warning = FindDescendant(cameraRig.transform, WarningTextName);

            var serialized = new SerializedObject(boundary);
            bool wired = false;

            wired |= AssignObject(serialized, "head", head);
            wired |= AssignObject(serialized, "fadeRenderer",
                fade != null ? fade.GetComponent<Renderer>() : null);
            wired |= AssignObject(serialized, "warningText",
                warning != null ? warning.GetComponent<TextMesh>() : null);

            if (wired)
            {
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(boundary);
                report.Add("bağlandı: ArenaBoundary head/fadeRenderer/warningText → VA_CameraRig");
            }

            if (fade == null || warning == null)
            {
                report.Add($"UYARI: rig altında '{FadeQuadName}'/'{WarningTextName}' yok — " +
                           "muhafaza sınır aşımında hiçbir şey göstermez.");
            }
        }

        /// <summary>
        /// Sahnede kalibrasyon işaretçisi var mı diye BAKAR, koymaz — işaretçiler ölçü maketinden
        /// gelir ve onları üreten tek araç <c>JSON'dan DimensionMesh Üret</c>'tir.
        /// <para>
        /// Sessiz geçilmez: işaretçisiz bir arena sahada hiç hizalanmaz ve eksiklik ancak
        /// başlıkla alana çıkıldığında fark edilir.
        /// </para>
        /// </summary>
        private static void ReportCalibrationMarkers(List<string> report)
        {
            DimensionAnchor[] anchors =
                Object.FindObjectsByType<DimensionAnchor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (anchors.Length >= 2)
            {
                report.Add($"bulundu: kalibrasyon işaretçisi ×{anchors.Length} (ölçü maketinden).");
                return;
            }

            report.Add("UYARI: sahnede kalibrasyon işaretçisi yok — 'Tools > VortexArena > Arena > " +
                       "JSON'dan DimensionMesh Üret' çalıştır. İşaretçiler maketin anchor_a/anchor_b " +
                       "küpleridir ve yerleri boyut dosyasının 'calibration' alanından gelir; " +
                       "onlarsız hizalama YAPILAMAZ.");
        }

        /// <summary>Alanı yalnız DOLU bir değerle ve yalnız BOŞSA yazar (elle bağlanan korunur).</summary>
        private static bool AssignObject(SerializedObject serialized, string field, Object value)
        {
            if (value == null)
            {
                return false;
            }

            SerializedProperty property = serialized.FindProperty(field);
            if (property == null || property.objectReferenceValue != null)
            {
                return false;
            }

            property.objectReferenceValue = value;
            return true;
        }

        /// <summary>
        /// Sahnenin mekan klasöründen boyut dosyasını çözüp <see cref="ArenaBoundary"/>'ye bağlar.
        /// <para>
        /// Dosya MEKAN başınadır: <c>Venues/&lt;Mekan&gt;/Data/&lt;Mekan&gt;_dimensions.json</c>.
        /// Bulunamazsa uyarı düşer — muhafaza ölçüsüz kalırsa kendini kapatır.
        /// </para>
        /// </summary>
        private static void BindDimensions(Scene scene, List<string> report)
        {
            var boundary = Object.FindFirstObjectByType<ArenaBoundary>(FindObjectsInactive.Include);
            if (boundary == null)
            {
                report.Add("UYARI: sahnede ArenaBoundary yok — boyut dosyası bağlanamadı.");
                return;
            }

            var serialized = new SerializedObject(boundary);
            SerializedProperty property = serialized.FindProperty("dimensionsJson");
            if (property == null)
            {
                report.Add("UYARI: ArenaBoundary'de 'dimensionsJson' alanı bulunamadı.");
                return;
            }

            if (property.objectReferenceValue != null)
            {
                report.Add("atlandı (zaten bağlı): ArenaBoundary.dimensionsJson");
                return;
            }

            string venue = ResolveVenueFromScenePath(scene.path);
            if (string.IsNullOrEmpty(venue))
            {
                report.Add("UYARI: sahne bir mekan klasöründe değil — boyut dosyasını ELLE bağla " +
                           "(sahne henüz kaydedilmediyse önce kaydet).");
                return;
            }

            string jsonPath = $"{VenuesRoot}/{venue}/Data/{venue}_dimensions.json";
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(jsonPath);
            if (json == null)
            {
                report.Add($"UYARI: boyut dosyası yok — '{jsonPath}'. Muhafaza ÖLÇÜSÜZ, arena sınırsız.");
                return;
            }

            property.objectReferenceValue = json;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(boundary);
            report.Add($"bağlandı: ArenaBoundary.dimensionsJson → {jsonPath}");
        }

        /// <summary>
        /// <c>Assets/Arenas/Venues/&lt;Mekan&gt;/…</c> yolundan mekan adını çıkarır; sahne mekan
        /// klasöründe değilse boş döner.
        /// </summary>
        public static string ResolveVenueFromScenePath(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath))
            {
                return string.Empty;
            }

            string normalized = scenePath.Replace('\\', '/');
            if (!normalized.StartsWith(VenuesRoot + "/", System.StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            string tail = normalized.Substring(VenuesRoot.Length + 1);
            int slash = tail.IndexOf('/');
            return slash > 0 ? tail.Substring(0, slash) : string.Empty;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != root && string.Equals(all[i].name, name, System.StringComparison.Ordinal))
                {
                    return all[i];
                }
            }

            return null;
        }
    }
}
