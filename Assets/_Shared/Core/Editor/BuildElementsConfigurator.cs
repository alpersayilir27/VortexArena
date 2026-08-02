using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core.Arena;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// <c>Tools &gt; VortexArena &gt; Configure All Build Elements</c> — açık sahneyi oynanabilir
    /// hâle getiren <b>tüm kayıt işini tek geçişte</b> yapar: <c>MapDefinition</c> yazar,
    /// <c>GameCatalog</c>'a ve uyumlu <c>ModeDefinition.maps</c> listelerine ekler, Build
    /// Settings'e koyar ve <c>Server/config/maps.json</c>'u export eder.
    /// <para>
    /// <b>Neden tek araç:</b> bu adımlardan biri atlanınca hata sessizdir — harita admin
    /// seçicisinde görünmez ya da <c>start_match</c> "harita bu modu desteklemiyor" diye reddedilir.
    /// Adımları tek düğmeye toplamak, unutulacak bir adım bırakmamaktır.
    /// </para>
    /// <para>
    /// ⚠️ <b>Katalog seçtirilmez</b>, projeden çözülür: çalışma anında
    /// <c>Resources.Load&lt;GameCatalog&gt;("GameCatalog")</c> ile bulunuyor, yani doğru olan tek
    /// bir asset var. Birden fazla katalog bir PROJE HATASIDIR — kayıt yapılmaz.
    /// </para>
    /// <para>
    /// ⚠️ <c>EditorUtility.DisplayDialog</c> YOK (CLI timeout tuzağı): export de dialogsuz
    /// varyantıyla çağrılır, sonuç <c>Debug.Log</c> ve pencere raporuyla bildirilir.
    /// </para>
    /// </summary>
    public class BuildElementsConfigurator : EditorWindow
    {
        [SerializeField] private string arenaId = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private List<string> selectedModeIds = new List<string>();

        [NonSerialized] private string[] availableModeIds = Array.Empty<string>();
        [NonSerialized] private List<string> lastReport;
        private Vector2 scroll;

        [MenuItem("Tools/VortexArena/Configure All Build Elements")]
        private static void Open()
        {
            var window = GetWindow<BuildElementsConfigurator>(true, "Build Öğelerini Yapılandır", true);
            window.minSize = new Vector2(470f, 420f);
            window.RefreshModes();
            window.Show();
        }

        private void OnFocus()
        {
            RefreshModes();
        }

        private void RefreshModes()
        {
            GameCatalog catalog = ResolveCatalog(null);
            var ids = new List<string>();
            if (catalog != null && catalog.Modes != null)
            {
                for (int i = 0; i < catalog.Modes.Length; i++)
                {
                    ModeDefinition mode = catalog.Modes[i];
                    if (mode != null && !string.IsNullOrEmpty(mode.ModeId))
                    {
                        ids.Add(mode.ModeId);
                    }
                }
            }

            availableModeIds = ids.ToArray();
        }

        // --------------------------------------------------------------- pencere

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            Scene scene = SceneManager.GetActiveScene();
            string sceneName = scene.name;

            EditorGUILayout.LabelField("Sahne", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Aktif sahne (katalog anahtarı)", sceneName);
            EditorGUILayout.LabelField("Yol", string.IsNullOrEmpty(scene.path) ? "(kaydedilmemiş)" : scene.path);

            if (string.IsNullOrEmpty(scene.path))
            {
                EditorGUILayout.HelpBox(
                    "Sahne henüz KAYDEDİLMEMİŞ. MapDefinition'ın nereye yazılacağı sahne yolundan " +
                    "türetiliyor — önce sahneyi arena kutusuna kaydet " +
                    "(Assets/Arenas/Venues/<Mekan>/<Arena>/Scenes/).",
                    MessageType.Error);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Harita", EditorStyles.boldLabel);

            if (string.IsNullOrWhiteSpace(arenaId) && !string.IsNullOrEmpty(scene.path))
            {
                arenaId = ResolveArenaFolderName(scene.path);
            }

            arenaId = EditorGUILayout.TextField(
                new GUIContent("Arena Id (asset adı)", "MapDefinition asset'inin adı, ör. A12x12"), arenaId);
            displayName = EditorGUILayout.TextField(
                new GUIContent("Gösterim adı", "Admin harita seçicisinde görünen ad. Boşsa sahne adı kullanılır."),
                displayName);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Desteklenen modlar", EditorStyles.boldLabel);
            if (availableModeIds.Length == 0)
            {
                EditorGUILayout.HelpBox("Katalogda mod yok (ya da GameCatalog bulunamadı).", MessageType.Warning);
            }

            for (int i = 0; i < availableModeIds.Length; i++)
            {
                string modeId = availableModeIds[i];
                bool on = selectedModeIds.Contains(modeId);
                bool next = EditorGUILayout.ToggleLeft(modeId, on);
                if (next == on)
                {
                    continue;
                }

                if (next)
                {
                    selectedModeIds.Add(modeId);
                }
                else
                {
                    selectedModeIds.Remove(modeId);
                }
            }

            EditorGUILayout.HelpBox(
                selectedModeIds.Count == 0
                    ? "Hiçbiri seçili değil = KISITSIZ (harita tüm modlarda oynanabilir). " +
                      "Lobi sahnesinde yalnız 'lobby' seçilmelidir."
                    : "Seçili: " + string.Join(" · ", selectedModeIds),
                MessageType.Info);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(scene.path) || string.IsNullOrWhiteSpace(arenaId)))
            {
                if (GUILayout.Button("Hepsini Yapılandır", GUILayout.Height(28f)))
                {
                    lastReport = Configure(arenaId, displayName, selectedModeIds.ToArray());
                    for (int i = 0; i < lastReport.Count; i++)
                    {
                        Debug.Log("[BuildElements] " + lastReport[i]);
                    }
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

        // ------------------------------------------------------------ yapılandırma

        /// <summary>
        /// Tüm kayıt adımlarını sırayla koşar ve ne yapıldığını satır satır döner. Exception
        /// FIRLATMAZ.
        /// </summary>
        public static List<string> Configure(string arenaId, string displayName, string[] supportedModeIds)
        {
            var report = new List<string>();
            Scene scene = SceneManager.GetActiveScene();

            if (string.IsNullOrEmpty(scene.path))
            {
                report.Add("HATA: sahne kaydedilmemiş — hiçbir şey yapılmadı.");
                return report;
            }

            // Sahne diske yazılmadan MapDefinition yazmak, sahnedeki son değişiklikleri
            // yansıtmayan bir kayıt üretirdi.
            EditorSceneManager.SaveScene(scene);

            string sceneName = Path.GetFileNameWithoutExtension(scene.path);
            string dataFolder = ResolveDataFolder(scene.path);
            if (string.IsNullOrEmpty(dataFolder))
            {
                report.Add($"HATA: sahne bir arena kutusunda değil ('{scene.path}') — MapDefinition yazılamadı.");
                return report;
            }

            EnsureFolder(dataFolder);

            // ----------------------------------------------------- 1) MapDefinition
            string assetPath = $"{dataFolder}/{arenaId.Trim()}.asset";
            var map = AssetDatabase.LoadAssetAtPath<MapDefinition>(assetPath);
            bool created = map == null;
            if (created)
            {
                map = ScriptableObject.CreateInstance<MapDefinition>();
                AssetDatabase.CreateAsset(map, assetPath);
            }

            var mapObject = new SerializedObject(map);
            mapObject.FindProperty("sceneName").stringValue = sceneName;
            mapObject.FindProperty("displayName").stringValue =
                string.IsNullOrWhiteSpace(displayName) ? sceneName : displayName.Trim();

            SerializedProperty modesProp = mapObject.FindProperty("supportedModeIds");
            modesProp.arraySize = supportedModeIds?.Length ?? 0;
            for (int i = 0; i < modesProp.arraySize; i++)
            {
                modesProp.GetArrayElementAtIndex(i).stringValue = supportedModeIds[i];
            }

            mapObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(map);
            report.Add($"{(created ? "yazıldı" : "güncellendi")}: {assetPath} (sceneName = {sceneName})");

            // --------------------------------------------------------- 2) katalog
            GameCatalog catalog = ResolveCatalog(report);
            if (catalog != null)
            {
                var catalogObject = new SerializedObject(catalog);
                if (AppendUnique(catalogObject.FindProperty("maps"), map))
                {
                    catalogObject.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(catalog);
                    report.Add("eklendi: GameCatalog.maps");
                }
                else
                {
                    report.Add("atlandı (zaten var): GameCatalog.maps");
                }

                RegisterInModes(catalog, map, report);
            }

            // --------------------------------------------------- 3) build settings
            if (IsSceneInBuildSettings(scene.path))
            {
                report.Add("atlandı (zaten var): Build Settings");
            }
            else
            {
                var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes)
                {
                    new EditorBuildSettingsScene(scene.path, true)
                };
                EditorBuildSettings.scenes = scenes.ToArray();
                report.Add("eklendi: Build Settings");
            }

            AssetDatabase.SaveAssets();

            // ---------------------------------------------------------- 4) export
            ServerConfigExportResult export = ServerConfigExporter.Export(false);
            report.Add("export: " + (export != null ? export.Summary : "sonuç alınamadı"));
            if (export != null && export.Warnings != null)
            {
                for (int i = 0; i < export.Warnings.Count; i++)
                {
                    report.Add("export uyarısı: " + export.Warnings[i]);
                }
            }

            // -------------------------------------------------- 5) sağlık kontrolü
            RunHealthChecks(report);
            return report;
        }

        /// <summary>
        /// Haritayı destekleyen her modun <b>DOLU</b> <c>maps</c> listesine ekler.
        /// <para>
        /// ⚠️ Boş liste "katalogdaki tüm uyumlu haritalar" demektir, dokunulmaz. Dolu bir listeye
        /// eklenmezse harita admin seçicisinde GÖRÜNMEZ — sessiz hata.
        /// </para>
        /// </summary>
        private static void RegisterInModes(GameCatalog catalog, MapDefinition map, List<string> report)
        {
            ModeDefinition[] modes = catalog.Modes;
            if (modes == null)
            {
                return;
            }

            for (int i = 0; i < modes.Length; i++)
            {
                ModeDefinition mode = modes[i];
                if (mode == null || mode.Maps == null || mode.Maps.Length == 0)
                {
                    continue; // boş liste = kısıtsız
                }

                if (!map.SupportsMode(mode.ModeId))
                {
                    continue;
                }

                var modeObject = new SerializedObject(mode);
                if (AppendUnique(modeObject.FindProperty("maps"), map))
                {
                    modeObject.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(mode);
                    report.Add($"eklendi: {mode.ModeId}.maps");
                }
            }
        }

        /// <summary>
        /// Sahneyi yayına hazır saymadan önce bakılan noktalar. Hiçbiri işi durdurmaz — hepsi
        /// rapora satır düşer, çünkü bunlar kurulumun ELDE kalan kısmıdır.
        /// </summary>
        private static void RunHealthChecks(List<string> report)
        {
            SpawnPoint[] spawns =
                UnityEngine.Object.FindObjectsByType<SpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (spawns.Length == 0)
            {
                report.Add("SAĞLIK: SpawnPoint YOK — arena uzayının sıfırı tanımsız, uzak oyuncular dünya orijininde toplanır.");
            }
            else if (spawns.Length > 1)
            {
                report.Add($"SAĞLIK: {spawns.Length} SpawnPoint var — arena başına TEK nokta beklenir.");
            }

            var boundary = UnityEngine.Object.FindFirstObjectByType<ArenaBoundary>(FindObjectsInactive.Include);
            if (boundary == null)
            {
                report.Add("SAĞLIK: ArenaBoundary YOK — arena sınırsız.");
            }
            else
            {
                var serialized = new SerializedObject(boundary);
                SerializedProperty json = serialized.FindProperty("dimensionsJson");
                if (json == null || json.objectReferenceValue == null)
                {
                    report.Add("SAĞLIK: ArenaBoundary.dimensionsJson BOŞ — muhafaza kendini kapatır.");
                }
            }

            ArenaDimensionMesh[] maquettes = UnityEngine.Object.FindObjectsByType<ArenaDimensionMesh>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < maquettes.Length; i++)
            {
                ArenaDimensionMesh maquette = maquettes[i];
                if (!maquette.CompareTag(ArenaDimensionMesh.EditorOnlyTag))
                {
                    report.Add($"SAĞLIK: '{maquette.name}' maketi 'EditorOnly' ETİKETLİ DEĞİL — build'e girer.");
                }

                if (boundary != null && maquette.transform.parent != boundary.transform)
                {
                    report.Add($"SAĞLIK: '{maquette.name}' maketi ArenaBoundary'nin altında değil — " +
                               "koordinatları muhafaza uzayıyla hizalı olmayabilir.");
                }
            }

            // ⚠️ Burada "Wall_* kalıntısı" diye bir kontrol YOKTUR ve eklenmez: arenanın gerçek
            // duvarları da environment sanatında bu adı taşıyor (IceWorld). Ada bakan bir uyarı
            // her açılışta yanlış alarm verir ve sağlık raporunun tamamı okunmaz olur.
        }

        // -------------------------------------------------------------- yardımcı

        /// <summary>
        /// Projedeki TEK <c>GameCatalog</c> asset'ini bulur; bulunamazsa ya da birden fazlaysa
        /// null döner. Birden fazla katalog bir proje hatasıdır: çalışma anında
        /// <c>Resources.Load</c> hangisini döndüreceğini garanti etmez.
        /// </summary>
        private static GameCatalog ResolveCatalog(List<string> report)
        {
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(GameCatalog));
            if (guids == null || guids.Length == 0)
            {
                report?.Add("HATA: projede GameCatalog asset'i YOK — harita kataloğa eklenmedi. " +
                            "Beklenen yer: Assets/_Shared/Data/Resources/GameCatalog.asset");
                return null;
            }

            if (guids.Length > 1)
            {
                var paths = new List<string>(guids.Length);
                for (int i = 0; i < guids.Length; i++)
                {
                    paths.Add(AssetDatabase.GUIDToAssetPath(guids[i]));
                }

                report?.Add("HATA: birden fazla GameCatalog var — hiçbirine yazılmadı: " + string.Join(" · ", paths));
                return null;
            }

            string catalogPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            var catalog = AssetDatabase.LoadAssetAtPath<GameCatalog>(catalogPath);
            if (catalog == null)
            {
                report?.Add($"HATA: GameCatalog yüklenemedi ('{catalogPath}').");
                return null;
            }

            if (catalogPath.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) < 0)
            {
                report?.Add($"UYARI: GameCatalog 'Resources/' altında DEĞİL ('{catalogPath}') — " +
                            "çalışma anında yüklenemez, admin listesinde görünmez.");
            }

            return catalog;
        }

        /// <summary>Diziye referansı sonuna ekler; zaten varsa <c>false</c> döner.</summary>
        private static bool AppendUnique(SerializedProperty arrayProp, UnityEngine.Object value)
        {
            if (arrayProp == null || !arrayProp.isArray)
            {
                return false;
            }

            for (int i = 0; i < arrayProp.arraySize; i++)
            {
                if (arrayProp.GetArrayElementAtIndex(i).objectReferenceValue == value)
                {
                    return false;
                }
            }

            arrayProp.arraySize++;
            arrayProp.GetArrayElementAtIndex(arrayProp.arraySize - 1).objectReferenceValue = value;
            return true;
        }

        /// <summary>
        /// Sahne yolundan arena kutusunun <c>Data/</c> klasörünü çıkarır:
        /// <c>&lt;kutu&gt;/Scenes/X.unity</c> → <c>&lt;kutu&gt;/Data</c>.
        /// </summary>
        private static string ResolveDataFolder(string scenePath)
        {
            string normalized = scenePath.Replace('\\', '/');
            int scenesIndex = normalized.LastIndexOf("/Scenes/", StringComparison.OrdinalIgnoreCase);
            return scenesIndex < 0 ? string.Empty : normalized.Substring(0, scenesIndex) + "/Data";
        }

        /// <summary>Arena kutusunun klasör adı — arenaId için makul bir varsayılan.</summary>
        private static string ResolveArenaFolderName(string scenePath)
        {
            string normalized = scenePath.Replace('\\', '/');
            int scenesIndex = normalized.LastIndexOf("/Scenes/", StringComparison.OrdinalIgnoreCase);
            if (scenesIndex < 0)
            {
                return string.Empty;
            }

            string boxPath = normalized.Substring(0, scenesIndex);
            int slash = boxPath.LastIndexOf('/');
            return slash < 0 ? boxPath : boxPath.Substring(slash + 1);
        }

        private static bool IsSceneInBuildSettings(string scenePath)
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i] != null &&
                    string.Equals(scenes[i].path, scenePath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool EnsureFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || AssetDatabase.IsValidFolder(folderPath))
            {
                return true;
            }

            string parent = Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
            string leaf = Path.GetFileName(folderPath);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
            {
                return false;
            }

            return EnsureFolder(parent) && !string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, leaf));
        }
    }
}
