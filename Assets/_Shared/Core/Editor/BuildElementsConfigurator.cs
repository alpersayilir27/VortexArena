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
    /// <c>Tools &gt; VortexArena &gt; Build &gt; Configure All Build Elements</c> — arena klasör
    /// ağacını <b>tek doğruluk kaynağı</b> sayıp kayıt yerlerini ona EŞİTLER: Build Settings,
    /// <c>GameCatalog.maps</c>, dolu <c>ModeDefinition.maps</c> listeleri ve
    /// <c>Server/config/maps.json</c>.
    /// <para>
    /// <b>Neden eşitleme, ekleme değil:</b> yalnız ekleyen bir araç silinen arenanın satırını
    /// Build Settings'te ve katalog listelerinde "Missing" olarak bırakır; APK build'i
    /// "diskte olmayan sahne var" diye iptal olur ve sebebi hiçbir yerde yazmaz. Fazlalık
    /// silinir, eksiklik rapora yazılır.
    /// </para>
    /// <para>
    /// <b>Beklenen yerleşim</b> (kutu klasörünün adı = sahne adı = MapDefinition adı):
    /// <c>Assets/Arenas/Venues/&lt;Mekan&gt;/Scenes/&lt;Sahne&gt;/&lt;Sahne&gt;.unity</c> +
    /// <c>…/&lt;Sahne&gt;/Data/&lt;Sahne&gt;.asset</c>. Üç adın da aynı olması, sahneyi bulan
    /// kodun MapDefinition'ı da bulabilmesini garanti eder — ikinci bir serbest ad (eski
    /// "Arena Id") kaçınılmaz olarak sahne adından sapıyordu.
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
        /// <summary>Oynanan arenaların kökü — mekan klasörleri bunun bir altındadır.</summary>
        private const string VenuesRoot = "Assets/Arenas/Venues";

        /// <summary>Referans şablonların kökü — buradaki sahneler ASLA Build Settings'e girmez.</summary>
        private const string TemplateRoot = "Assets/Arenas/Template/";

        /// <summary>Arena ağacının tamamı: bu önekli Build Settings satırları taramaya tabidir.</summary>
        private const string ArenasRoot = "Assets/Arenas/";

        /// <summary>Kutuların toplandığı ara klasörün adı (<c>&lt;Mekan&gt;/Scenes</c>).</summary>
        private const string ScenesFolderName = "Scenes";

        /// <summary>Mekan kökünde durmasına izin verilen klasörler; başkası yanlış yere konmuş demektir.</summary>
        private static readonly string[] AllowedVenueFolders = { "Art", "Data", "Prefabs", ScenesFolderName };

        /// <summary>
        /// Boundary/maket hiza denetimlerinin payı (konum m · ölçek bileşeni). Sıfır tolerans
        /// değil: elle yerleşimde milimetrik kayma olağan ve her açılışta uyarı basmak raporun
        /// tamamını okunmaz kılar.
        /// </summary>
        private const float AlignmentTolerance = 0.01f;

        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private List<string> selectedModeIds = new List<string>();

        [NonSerialized] private string[] availableModeIds = Array.Empty<string>();
        [NonSerialized] private ScanResult scan;
        [NonSerialized] private string loadedForScenePath = null;
        [NonSerialized] private List<string> lastReport;
        private Vector2 scroll;

        [MenuItem("Tools/VortexArena/Build/Configure All Build Elements", false, 40)]
        private static void Open()
        {
            var window = GetWindow<BuildElementsConfigurator>(true, "Build Öğelerini Yapılandır", true);
            window.minSize = new Vector2(520f, 520f);
            window.Refresh();
            window.Show();
        }

        private void OnFocus()
        {
            Refresh();
        }

        /// <summary>Mod listesini ve mekan taramasını tazeler; hiçbir asset'e YAZMAZ.</summary>
        private void Refresh()
        {
            availableModeIds = CollectModeIds(ResolveCatalog(null));
            scan = Scan();
            SyncActiveSceneFields();
            Repaint();
        }

        /// <summary>
        /// Aktif sahne değiştiyse form alanlarını o sahnenin MapDefinition'ından doldurur.
        /// <para>
        /// ⚠️ Sahne aynıyken doldurma YAPILMAZ: pencere her odaklandığında yeniden okunsaydı,
        /// kullanıcının henüz kaydetmediği mod seçimi sessizce geri alınırdı.
        /// </para>
        /// </summary>
        private void SyncActiveSceneFields()
        {
            string scenePath = SceneManager.GetActiveScene().path ?? string.Empty;
            if (string.Equals(scenePath, loadedForScenePath, StringComparison.Ordinal))
            {
                return;
            }

            loadedForScenePath = scenePath;
            displayName = string.Empty;
            selectedModeIds.Clear();

            MapDefinition map = FindMapForScenePath(scenePath);
            if (map == null)
            {
                return;
            }

            displayName = map.DisplayName ?? string.Empty;
            if (map.SupportedModeIds != null)
            {
                for (int i = 0; i < map.SupportedModeIds.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(map.SupportedModeIds[i]))
                    {
                        selectedModeIds.Add(map.SupportedModeIds[i]);
                    }
                }
            }
        }

        // --------------------------------------------------------------- pencere

        private void OnGUI()
        {
            if (scan == null)
            {
                Refresh();
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);

            DrawScanTable();
            EditorGUILayout.Space();
            DrawActiveScene();
            EditorGUILayout.Space();
            DrawButtons();
            DrawLastReport();

            EditorGUILayout.EndScrollView();
        }

        private void DrawScanTable()
        {
            EditorGUILayout.LabelField("Mekanlar", EditorStyles.boldLabel);

            if (GUILayout.Button("Yenile", GUILayout.Width(90f)))
            {
                Refresh();
            }

            for (int i = 0; i < scan.Issues.Count; i++)
            {
                DrawIssue(scan.Issues[i]);
            }

            if (scan.Venues.Count == 0)
            {
                EditorGUILayout.HelpBox($"'{VenuesRoot}' altında mekan klasörü YOK.", MessageType.Warning);
                return;
            }

            for (int v = 0; v < scan.Venues.Count; v++)
            {
                VenueRecord venue = scan.Venues[v];
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField(venue.Name, EditorStyles.boldLabel);

                for (int i = 0; i < venue.Issues.Count; i++)
                {
                    DrawIssue(venue.Issues[i]);
                }

                using (new EditorGUI.IndentLevelScope())
                {
                    for (int b = 0; b < venue.Boxes.Count; b++)
                    {
                        DrawBox(venue.Boxes[b]);
                    }
                }
            }
        }

        private void DrawBox(BoxRecord box)
        {
            string sceneCell = string.IsNullOrEmpty(box.SceneName) ? "(sahne yok)" : box.SceneName;
            string mapCell = box.Map != null ? "map ✔" : "map ✘";
            string buildCell = !string.IsNullOrEmpty(box.ScenePath) && scan.BuildScenePaths.Contains(box.ScenePath)
                ? "build ✔"
                : "build ✘";
            string modesCell = box.Map == null || box.Map.SupportedModeIds == null || box.Map.SupportedModeIds.Length == 0
                ? "kısıtsız"
                : string.Join("·", box.Map.SupportedModeIds);

            EditorGUILayout.LabelField($"{sceneCell}   —   {mapCell} · {buildCell} · {modesCell}");

            for (int i = 0; i < box.Issues.Count; i++)
            {
                DrawIssue(box.Issues[i]);
            }
        }

        private static void DrawIssue(ScanIssue issue)
        {
            EditorGUILayout.HelpBox(issue.Text, issue.IsError ? MessageType.Error : MessageType.Warning);
        }

        private void DrawActiveScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            EditorGUILayout.LabelField("Aktif sahne", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Yol", string.IsNullOrEmpty(scene.path) ? "(kaydedilmemiş)" : scene.path);

            if (!TryParseBoxScene(scene.path, out _, out _, out string boxName, out string sceneName) ||
                !string.Equals(boxName, sceneName, StringComparison.Ordinal))
            {
                EditorGUILayout.HelpBox(
                    "Aktif sahne bir arena kutusunda DEĞİL — yalnız senkronizasyon yapılabilir. " +
                    "Beklenen yol: " + VenuesRoot + "/<Mekan>/Scenes/<Sahne>/<Sahne>.unity",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Katalog anahtarı (sahne adı)", sceneName);
            displayName = EditorGUILayout.TextField(
                new GUIContent("Gösterim adı", "Admin harita seçicisinde görünen ad. Boşsa sahne adı kullanılır."),
                displayName);

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
        }

        private void DrawButtons()
        {
            Scene scene = SceneManager.GetActiveScene();
            bool activeSceneIsBox =
                TryParseBoxScene(scene.path, out _, out _, out string boxName, out string sceneName) &&
                string.Equals(boxName, sceneName, StringComparison.Ordinal);

            using (new EditorGUI.DisabledScope(!activeSceneIsBox))
            {
                if (GUILayout.Button("Hepsini Yapılandır", GUILayout.Height(28f)))
                {
                    RunAndLog(ConfigureActiveScene(displayName, selectedModeIds.ToArray()));
                }
            }

            if (GUILayout.Button("Yalnız Senkronize Et", GUILayout.Height(22f)))
            {
                var report = new List<string>();
                SyncAll(report);
                RunAndLog(report);
            }

            EditorGUILayout.HelpBox(
                "\"Yalnız Senkronize Et\" aktif sahneye DOKUNMAZ ve sahne açık olmasa da çalışır — " +
                "silinen bir arenanın Build Settings / katalog kalıntısını temizlemenin yolu budur.",
                MessageType.Info);
        }

        private void RunAndLog(List<string> report)
        {
            lastReport = report;
            for (int i = 0; i < report.Count; i++)
            {
                Debug.Log("[BuildElements] " + report[i]);
            }

            Refresh();
        }

        private void DrawLastReport()
        {
            if (lastReport == null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Son çalıştırma", EditorStyles.boldLabel);
            for (int i = 0; i < lastReport.Count; i++)
            {
                EditorGUILayout.LabelField("• " + lastReport[i], EditorStyles.wordWrappedLabel);
            }
        }

        // ---------------------------------------------------------------- tarama

        /// <summary>Bir tarama bulgusu — hata da uyarı da işi DURDURMAZ, hepsi rapora düşer.</summary>
        private sealed class ScanIssue
        {
            public ScanIssue(bool isError, string text)
            {
                IsError = isError;
                Text = text;
            }

            public bool IsError { get; }

            public string Text { get; }
        }

        /// <summary>Bir kutu klasörü (<c>&lt;Mekan&gt;/Scenes/&lt;Sahne&gt;</c>) hakkında bilinen her şey.</summary>
        private sealed class BoxRecord
        {
            public string Venue;
            public string BoxPath;
            public string BoxName;
            public string ScenePath = string.Empty;
            public string SceneName = string.Empty;
            public string MapPath = string.Empty;
            public MapDefinition Map;

            /// <summary>Sahne adı ile MapDefinition'ın <c>sceneName</c> alanı ayrışmış mı.</summary>
            public bool SceneNameMismatch;

            /// <summary>Yerleşim doğru — bu kutu Build Settings/katalog eşitlemesine GİRER.</summary>
            public bool Valid;

            public readonly List<ScanIssue> Issues = new List<ScanIssue>();
        }

        private sealed class VenueRecord
        {
            public string Name;
            public string Path;
            public readonly List<BoxRecord> Boxes = new List<BoxRecord>();
            public readonly List<ScanIssue> Issues = new List<ScanIssue>();
        }

        private sealed class ScanResult
        {
            public readonly List<VenueRecord> Venues = new List<VenueRecord>();
            public readonly List<ScanIssue> Issues = new List<ScanIssue>();

            /// <summary>Build Settings'te duran sahne yolları (yerleşim tablosunu çizmek için).</summary>
            public readonly HashSet<string> BuildScenePaths =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            /// <summary>Eşitlemeye girecek kutular — tarama sırası korunur.</summary>
            public readonly List<BoxRecord> ValidBoxes = new List<BoxRecord>();
        }

        /// <summary>
        /// Mekan ağacını okur ve bulgu listesi üretir. <b>Salt okunur</b>: pencere her
        /// odaklandığında koştuğu için hiçbir asset'e yazmaz — düzeltmeleri
        /// <see cref="SyncAll"/> yapar.
        /// </summary>
        private static ScanResult Scan()
        {
            var result = new ScanResult();

            EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes;
            for (int i = 0; i < buildScenes.Length; i++)
            {
                if (buildScenes[i] != null && !string.IsNullOrEmpty(buildScenes[i].path))
                {
                    result.BuildScenePaths.Add(Normalize(buildScenes[i].path));
                }
            }

            if (!AssetDatabase.IsValidFolder(VenuesRoot))
            {
                result.Issues.Add(new ScanIssue(true, $"'{VenuesRoot}' klasörü YOK — oynanacak arena bulunamaz."));
                return result;
            }

            string[] venuePaths = AssetDatabase.GetSubFolders(VenuesRoot);
            Array.Sort(venuePaths, StringComparer.Ordinal);

            for (int v = 0; v < venuePaths.Length; v++)
            {
                result.Venues.Add(ScanVenue(venuePaths[v], result));
            }

            ScanStrayMaps(result);
            return result;
        }

        private static VenueRecord ScanVenue(string venuePath, ScanResult result)
        {
            var venue = new VenueRecord { Path = venuePath, Name = LeafName(venuePath) };

            string[] children = AssetDatabase.GetSubFolders(venuePath);
            for (int i = 0; i < children.Length; i++)
            {
                string leaf = LeafName(children[i]);
                if (Array.IndexOf(AllowedVenueFolders, leaf) < 0)
                {
                    venue.Issues.Add(new ScanIssue(false,
                        $"'{children[i]}' mekan kökünde beklenmeyen bir klasör — kutular artık '{ScenesFolderName}/' " +
                        "altında durur (Art/Data/Prefabs mekanın paylaşılan içeriğidir)."));
                }
            }

            string scenesFolder = venuePath + "/" + ScenesFolderName;
            if (!AssetDatabase.IsValidFolder(scenesFolder))
            {
                venue.Issues.Add(new ScanIssue(false,
                    $"'{scenesFolder}' YOK — bu mekanda hiç kutu bulunamadı."));
                return venue;
            }

            string[] boxPaths = AssetDatabase.GetSubFolders(scenesFolder);
            Array.Sort(boxPaths, StringComparer.Ordinal);

            for (int i = 0; i < boxPaths.Length; i++)
            {
                BoxRecord box = ScanBox(venue.Name, boxPaths[i]);
                venue.Boxes.Add(box);
                if (box.Valid)
                {
                    result.ValidBoxes.Add(box);
                }
            }

            return venue;
        }

        private static BoxRecord ScanBox(string venueName, string boxPath)
        {
            var box = new BoxRecord { Venue = venueName, BoxPath = boxPath, BoxName = LeafName(boxPath) };

            // ⚠️ Yalnız kutunun DOĞRUDAN altına bakılır: lightmap klasörü sahneyle aynı adı
            // taşıyor ve alt ağaç taranırsa oradaki dosyalar sahne sanılır.
            List<string> scenePaths = DirectSceneFiles(boxPath);

            if (scenePaths.Count == 0)
            {
                box.Issues.Add(new ScanIssue(true, $"'{boxPath}' kutusunda hiç sahne YOK."));
                return box;
            }

            if (scenePaths.Count > 1)
            {
                box.Issues.Add(new ScanIssue(true,
                    $"'{boxPath}' kutusunda {scenePaths.Count} sahne var: " + string.Join(" · ", scenePaths) +
                    " — kutu başına TEK sahne beklenir."));
                return box;
            }

            box.ScenePath = scenePaths[0];
            box.SceneName = Path.GetFileNameWithoutExtension(box.ScenePath);

            if (!string.Equals(box.SceneName, box.BoxName, StringComparison.Ordinal))
            {
                box.Issues.Add(new ScanIssue(true,
                    $"'{box.ScenePath}': klasör adı = sahne adı ZORUNLU ('{box.BoxName}' ≠ '{box.SceneName}') — " +
                    "bu kutu senkronizasyona girmedi."));
                return box;
            }

            box.MapPath = $"{boxPath}/Data/{box.SceneName}.asset";
            box.Map = AssetDatabase.LoadAssetAtPath<MapDefinition>(box.MapPath);
            box.Valid = true;

            if (box.Map == null)
            {
                box.Issues.Add(new ScanIssue(false,
                    $"'{box.MapPath}' YOK — sahneyi aç ve 'Hepsini Yapılandır' ile modlarını seç."));
                return box;
            }

            // ⚠️ MapDefinition KENDİLİĞİNDEN ÜRETİLMEZ: boş `supportedModeIds` "kısıtsız" demek,
            // yani üretilen bir lobi haritası sessizce tüm modlarda oynanabilir hâle gelirdi.

            if (!string.Equals(box.Map.SceneName, box.SceneName, StringComparison.Ordinal))
            {
                box.SceneNameMismatch = true;
                box.Issues.Add(new ScanIssue(false,
                    $"'{box.MapPath}'.sceneName = '{box.Map.SceneName}' ≠ '{box.SceneName}' — " +
                    "senkronizasyonda düzeltilir (dosya sistemi otoritedir)."));
            }

            return box;
        }

        /// <summary>Mekan ağacında olup beklenen kutu yerinde durmayan MapDefinition'lar.</summary>
        private static void ScanStrayMaps(ScanResult result)
        {
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(MapDefinition));
            for (int i = 0; i < guids.Length; i++)
            {
                string path = Normalize(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (!path.StartsWith(VenuesRoot + "/", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] parts = path.Split('/');

                // Assets/Arenas/Venues/<Mekan>/Scenes/<Sahne>/Data/<Sahne>.asset
                bool wellPlaced =
                    parts.Length == 8 &&
                    string.Equals(parts[4], ScenesFolderName, StringComparison.Ordinal) &&
                    string.Equals(parts[6], "Data", StringComparison.Ordinal) &&
                    string.Equals(Path.GetFileNameWithoutExtension(parts[7]), parts[5], StringComparison.Ordinal);

                if (!wellPlaced)
                {
                    result.Issues.Add(new ScanIssue(false,
                        $"'{path}' beklenen yerde değil — MapDefinition'ın yeri " +
                        VenuesRoot + "/<Mekan>/Scenes/<Sahne>/Data/<Sahne>.asset'tir; " +
                        "buradaki asset hiçbir kutuya bağlanmadı."));
                }
            }
        }

        // ------------------------------------------------------------- eşitleme

        /// <summary>
        /// Klasör ağacını Build Settings, <c>GameCatalog.maps</c> ve dolu
        /// <c>ModeDefinition.maps</c> listeleriyle eşitler; ne yapıldığını satır satır
        /// <paramref name="report"/>'a yazar. Exception FIRLATMAZ.
        /// </summary>
        public static void SyncAll(List<string> report)
        {
            ScanResult current = Scan();

            for (int i = 0; i < current.Issues.Count; i++)
            {
                report.Add(Prefix(current.Issues[i]));
            }

            for (int v = 0; v < current.Venues.Count; v++)
            {
                VenueRecord venue = current.Venues[v];
                for (int i = 0; i < venue.Issues.Count; i++)
                {
                    report.Add(Prefix(venue.Issues[i]));
                }

                for (int b = 0; b < venue.Boxes.Count; b++)
                {
                    BoxRecord box = venue.Boxes[b];
                    for (int i = 0; i < box.Issues.Count; i++)
                    {
                        report.Add(Prefix(box.Issues[i]));
                    }
                }
            }

            FixSceneNames(current, report);

            var maps = new List<MapDefinition>();
            for (int i = 0; i < current.ValidBoxes.Count; i++)
            {
                if (current.ValidBoxes[i].Map != null)
                {
                    maps.Add(current.ValidBoxes[i].Map);
                }
            }

            SyncBuildSettings(current, report);

            GameCatalog catalog = ResolveCatalog(report);
            if (catalog != null)
            {
                SyncCatalogMaps(catalog, maps, report);
                SyncModeMaps(catalog, maps, report);
            }

            AssetDatabase.SaveAssets();

            ServerConfigExportResult export = ServerConfigExporter.Export(false);
            report.Add("export: " + (export != null ? export.Summary : "sonuç alınamadı"));
            if (export != null && export.Warnings != null)
            {
                for (int i = 0; i < export.Warnings.Count; i++)
                {
                    report.Add("export uyarısı: " + export.Warnings[i]);
                }
            }

            // Sağlık kontrolleri AKTİF SAHNEYE bakar; sahne bir kutuda değilse ölçtükleri şey
            // (muhafaza, ölçü maketi) o sahne için anlamsızdır.
            string activePath = SceneManager.GetActiveScene().path;
            if (TryParseBoxScene(activePath, out _, out _, out string boxName, out string sceneName) &&
                string.Equals(boxName, sceneName, StringComparison.Ordinal))
            {
                RunHealthChecks(report);
            }
        }

        /// <summary>Dosya sistemi otoritedir: sahne adı ile ayrışan <c>sceneName</c> alanları geri yazılır.</summary>
        private static void FixSceneNames(ScanResult current, List<string> report)
        {
            for (int i = 0; i < current.ValidBoxes.Count; i++)
            {
                BoxRecord box = current.ValidBoxes[i];
                if (box.Map == null || !box.SceneNameMismatch)
                {
                    continue;
                }

                var mapObject = new SerializedObject(box.Map);
                mapObject.FindProperty("sceneName").stringValue = box.SceneName;
                mapObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(box.Map);
                report.Add($"düzeltildi: {box.MapPath}.sceneName = {box.SceneName}");
            }
        }

        /// <summary>
        /// Build Settings listesini yeniden kurar.
        /// <para>
        /// ⚠️ <b>Sıra korunur, yeniden sıralama YAPILMAZ:</b> <c>Boot.unity</c> index 0'da
        /// durmak zorunda (uygulama onu açıyor). Arena dışı satırlar mevcut sıralarıyla başa,
        /// arena satırları arkaya yazılır; yeni arenalar sona Ordinal alfabetik eklenir.
        /// </para>
        /// <para>
        /// ⚠️ Şablon sahneleri listeye ASLA girmez: girseydi sunucu açılışında var olmayan bir
        /// mekan satırı açardı.
        /// </para>
        /// </summary>
        private static void SyncBuildSettings(ScanResult current, List<string> report)
        {
            var targets = new Dictionary<string, BoxRecord>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < current.ValidBoxes.Count; i++)
            {
                BoxRecord box = current.ValidBoxes[i];
                if (!string.IsNullOrEmpty(box.ScenePath))
                {
                    targets[box.ScenePath] = box;
                }
            }

            EditorBuildSettingsScene[] existing = EditorBuildSettings.scenes;
            var outside = new List<EditorBuildSettingsScene>();
            var arena = new List<EditorBuildSettingsScene>();
            var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < existing.Length; i++)
            {
                EditorBuildSettingsScene entry = existing[i];
                if (entry == null || string.IsNullOrEmpty(entry.path))
                {
                    report.Add("kaldırıldı (Build Settings): boş satır");
                    continue;
                }

                string path = Normalize(entry.path);
                bool onDisk = AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null;

                if (!path.StartsWith(ArenasRoot, StringComparison.Ordinal))
                {
                    if (!onDisk)
                    {
                        report.Add($"kaldırıldı (Build Settings): {path} — diskte yok");
                        continue;
                    }

                    outside.Add(new EditorBuildSettingsScene(path, entry.enabled));
                    continue;
                }

                if (path.StartsWith(TemplateRoot, StringComparison.Ordinal))
                {
                    report.Add($"kaldırıldı (Build Settings): {path} — şablon");
                    continue;
                }

                if (!onDisk)
                {
                    report.Add($"kaldırıldı (Build Settings): {path} — diskte yok");
                    continue;
                }

                if (!targets.ContainsKey(path))
                {
                    report.Add($"kaldırıldı (Build Settings): {path} — mekan ağacında değil");
                    continue;
                }

                if (!placed.Add(path))
                {
                    report.Add($"kaldırıldı (Build Settings): {path} — yinelenen satır");
                    continue;
                }

                arena.Add(new EditorBuildSettingsScene(path, entry.enabled));
            }

            var added = new List<string>();
            foreach (KeyValuePair<string, BoxRecord> pair in targets)
            {
                if (!placed.Contains(pair.Key))
                {
                    added.Add(pair.Key);
                }
            }

            added.Sort(StringComparer.Ordinal);
            for (int i = 0; i < added.Count; i++)
            {
                arena.Add(new EditorBuildSettingsScene(added[i], true));
                report.Add($"eklendi (Build Settings): {added[i]}");
            }

            var final = new List<EditorBuildSettingsScene>(outside.Count + arena.Count);
            final.AddRange(outside);
            final.AddRange(arena);
            EditorBuildSettings.scenes = final.ToArray();
            report.Add($"Build Settings: {final.Count} sahne ({outside.Count} arena dışı + {arena.Count} arena)");
        }

        /// <summary>
        /// <c>GameCatalog.maps</c> = taranan haritaların tamamı. Mevcut sıra korunur, yeniler sona
        /// eklenir; null ve taramada olmayan (silinmiş/taşınmış) referanslar SİLİNİR — kalan
        /// "Missing" satır admin harita seçicisinde boş bir kayıt olarak çizilirdi.
        /// </summary>
        private static void SyncCatalogMaps(GameCatalog catalog, List<MapDefinition> maps, List<string> report)
        {
            var known = new HashSet<MapDefinition>(maps);
            var ordered = new List<MapDefinition>();

            var catalogObject = new SerializedObject(catalog);
            SerializedProperty prop = catalogObject.FindProperty("maps");

            for (int i = 0; i < prop.arraySize; i++)
            {
                var map = prop.GetArrayElementAtIndex(i).objectReferenceValue as MapDefinition;
                if (map == null)
                {
                    report.Add("kaldırıldı (GameCatalog.maps): boş/Missing referans");
                    continue;
                }

                if (!known.Contains(map))
                {
                    report.Add($"kaldırıldı (GameCatalog.maps): {AssetDatabase.GetAssetPath(map)} — mekan ağacında değil");
                    continue;
                }

                if (ordered.Contains(map))
                {
                    report.Add($"kaldırıldı (GameCatalog.maps): {map.SceneName} — yinelenen kayıt");
                    continue;
                }

                ordered.Add(map);
            }

            for (int i = 0; i < maps.Count; i++)
            {
                if (!ordered.Contains(maps[i]))
                {
                    ordered.Add(maps[i]);
                    report.Add($"eklendi (GameCatalog.maps): {maps[i].SceneName}");
                }
            }

            WriteArray(prop, ordered);
            catalogObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
        }

        /// <summary>
        /// Her modun <b>DOLU</b> <c>maps</c> listesini o modu destekleyen haritalarla eşitler.
        /// <para>
        /// ⚠️ <b>Boş liste "kısıtsız" demektir</b> (GameCatalog.MapsForMode boş listeyi görünce
        /// katalogdaki tüm haritalara düşer). Bu yüzden iki kural bağlayıcıdır: boş liste
        /// dokunulmadan bırakılır ve dolu bir liste ASLA boşaltılmaz — hedef küme boşsa mod
        /// sessizce tüm haritaları kabul eder hâle gelirdi. O durumda liste olduğu gibi kalır,
        /// rapora uyarı düşer.
        /// </para>
        /// <para>
        /// Null elemanlar her hâlükârda temizlenir: "Missing" bir eleman ne kısıt ne harita,
        /// yalnız admin seçicisinde boş bir satırdır.
        /// </para>
        /// </summary>
        private static void SyncModeMaps(GameCatalog catalog, List<MapDefinition> maps, List<string> report)
        {
            ModeDefinition[] modes = catalog.Modes;
            if (modes == null)
            {
                return;
            }

            for (int m = 0; m < modes.Length; m++)
            {
                ModeDefinition mode = modes[m];
                if (mode == null)
                {
                    continue;
                }

                var modeObject = new SerializedObject(mode);
                SerializedProperty prop = modeObject.FindProperty("maps");

                var kept = new List<MapDefinition>();
                bool changed = false;
                for (int i = 0; i < prop.arraySize; i++)
                {
                    var map = prop.GetArrayElementAtIndex(i).objectReferenceValue as MapDefinition;
                    if (map == null)
                    {
                        report.Add($"kaldırıldı ({mode.ModeId}.maps): boş/Missing referans");
                        changed = true;
                        continue;
                    }

                    if (kept.Contains(map))
                    {
                        report.Add($"kaldırıldı ({mode.ModeId}.maps): {map.SceneName} — yinelenen kayıt");
                        changed = true;
                        continue;
                    }

                    kept.Add(map);
                }

                if (kept.Count == 0)
                {
                    // Liste zaten boş(aldı) = kısıtsız; hedef kümeyi buraya yazmak modu
                    // istemeden kısıtlamak olurdu.
                    if (changed)
                    {
                        WriteArray(prop, kept);
                        modeObject.ApplyModifiedPropertiesWithoutUndo();
                        EditorUtility.SetDirty(mode);
                    }

                    continue;
                }

                var target = new List<MapDefinition>();
                for (int i = 0; i < maps.Count; i++)
                {
                    if (maps[i].SupportsMode(mode.ModeId))
                    {
                        target.Add(maps[i]);
                    }
                }

                if (target.Count == 0)
                {
                    report.Add($"UYARI: '{mode.ModeId}' modunu destekleyen HİÇ harita yok — {mode.ModeId}.maps " +
                               "olduğu gibi bırakıldı. Boşaltılsaydı liste 'kısıtsız' anlamına gelir ve mod " +
                               "sessizce tüm haritaları kabul ederdi; haritaların supportedModeIds alanını kontrol et.");
                    if (changed)
                    {
                        WriteArray(prop, kept);
                        modeObject.ApplyModifiedPropertiesWithoutUndo();
                        EditorUtility.SetDirty(mode);
                    }

                    continue;
                }

                var final = new List<MapDefinition>();
                var targetSet = new HashSet<MapDefinition>(target);
                for (int i = 0; i < kept.Count; i++)
                {
                    if (targetSet.Contains(kept[i]))
                    {
                        final.Add(kept[i]);
                    }
                    else
                    {
                        report.Add($"kaldırıldı ({mode.ModeId}.maps): {kept[i].SceneName} — modu desteklemiyor");
                        changed = true;
                    }
                }

                for (int i = 0; i < target.Count; i++)
                {
                    if (!final.Contains(target[i]))
                    {
                        final.Add(target[i]);
                        report.Add($"eklendi ({mode.ModeId}.maps): {target[i].SceneName}");
                        changed = true;
                    }
                }

                if (!changed)
                {
                    continue;
                }

                WriteArray(prop, final);
                modeObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(mode);
            }
        }

        // -------------------------------------------------------- aktif sahne

        /// <summary>
        /// Aktif sahnenin MapDefinition'ını yazar/günceller, ardından tüm kayıt yerlerini
        /// eşitler. Exception FIRLATMAZ — ne yapıldığı satır satır dönülür.
        /// <para>
        /// ⚠️ Asset adı kullanıcıdan ALINMAZ, sahne adından türetilir: ikinci bir serbest ad
        /// (eski "Arena Id") sahne adından sapabilen bir doğruluk kaynağıydı.
        /// </para>
        /// </summary>
        public static List<string> ConfigureActiveScene(string displayName, string[] supportedModeIds)
        {
            var report = new List<string>();
            Scene scene = SceneManager.GetActiveScene();

            if (string.IsNullOrEmpty(scene.path))
            {
                report.Add("HATA: sahne kaydedilmemiş — MapDefinition'ın yeri sahne yolundan türetiliyor, " +
                           "önce sahneyi " + VenuesRoot + "/<Mekan>/Scenes/<Sahne>/<Sahne>.unity olarak kaydet.");
                return report;
            }

            if (!TryParseBoxScene(scene.path, out _, out string boxPath, out string boxName, out string sceneName) ||
                !string.Equals(boxName, sceneName, StringComparison.Ordinal))
            {
                report.Add($"HATA: '{scene.path}' beklenen yerleşimde değil. Beklenen: " +
                           VenuesRoot + "/<Mekan>/" + ScenesFolderName + "/<Sahne>/<Sahne>.unity " +
                           "(klasör adı = sahne adı zorunlu).");
                return report;
            }

            // Sahne diske yazılmadan MapDefinition yazmak, sahnedeki son değişiklikleri
            // yansıtmayan bir kayıt üretirdi.
            EditorSceneManager.SaveScene(scene);

            string dataFolder = boxPath + "/Data";
            EnsureFolder(dataFolder);

            string assetPath = $"{dataFolder}/{sceneName}.asset";
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

            SyncAll(report);
            return report;
        }

        /// <summary>
        /// Sahneyi yayına hazır saymadan önce bakılan noktalar. Hiçbiri işi durdurmaz — hepsi
        /// rapora satır düşer, çünkü bunlar kurulumun ELDE kalan kısmıdır.
        /// </summary>
        private static void RunHealthChecks(List<string> report)
        {
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

                // ⚠️ Boundary'nin KONUMU/DÖNÜŞÜ denetlenmez: varsayılan yerleşim dünya orijinidir
                // ama hazır bir environment'ın içinde bölge oynatmak için boundary bilinçli olarak
                // taşınır/döndürülür — mesafe, niyeti ayırt edemeyen bir sinyaldir. ÖLÇEK ise her
                // durumda hatadır: boyut dosyası metre cinsindendir ve TransformPoint ölçeği de
                // uygular — 1'den sapan ölçek muhafazayı, işaretçileri ve kadrajı sessizce yanlış
                // ölçüde kurar.
                Vector3 scale = boundary.transform.lossyScale;
                if (Mathf.Abs(scale.x - 1f) > AlignmentTolerance ||
                    Mathf.Abs(scale.y - 1f) > AlignmentTolerance ||
                    Mathf.Abs(scale.z - 1f) > AlignmentTolerance)
                {
                    report.Add($"SAĞLIK: ArenaBoundary ölçeği {scale} — 1 olmalı. Boyut " +
                               "dosyasındaki metreler bu ölçekle çarpılır; muhafaza ve " +
                               "kalibrasyon işaretçileri yanlış ölçüde kurulur.");
                }
            }

            ArenaDimensionMesh[] maquettes = UnityEngine.Object.FindObjectsByType<ArenaDimensionMesh>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < maquettes.Length; i++)
            {
                ArenaDimensionMesh maquette = maquettes[i];

                // ⚠️ Kural TERSİNE döner: maket build'e GİRMELİDİR, çünkü kalibrasyon işaretçileri
                // (anchor_a/anchor_b) onun altındadır ve çalışma anında gerekir. 'EditorOnly'
                // etiketi kökü tüm çocuklarıyla birlikte build'den siler — arena sahada sessizce
                // hizalanamaz hâle gelir. Görsel dalı zaten DimensionMeshBuildStripper ayıklıyor.
                if (maquette.CompareTag("EditorOnly"))
                {
                    report.Add($"SAĞLIK: '{maquette.name}' maketi 'EditorOnly' ETİKETLİ — build'e " +
                               "girmez ve kalibrasyon işaretçileri onunla birlikte silinir. " +
                               "Etiketi 'Untagged' yap.");
                }

                // Maket, boundary varsa onun ALTINDA ve yerel-kimlikte durmalı ("JSON'dan
                // DimensionMesh Üret" böyle kurar): ayrışırlarsa gözle görülen ölçü ile
                // muhafazanın/işaretçilerin gerçekte kurulduğu yer sessizce farklılaşır.
                // Boundary'siz (eski) sahnede maketin yeri serbesttir — geri okuma maketin
                // kendi kökünü referans alır.
                if (boundary != null &&
                    (maquette.transform.parent != boundary.transform ||
                     maquette.transform.localPosition.magnitude > AlignmentTolerance ||
                     Quaternion.Angle(maquette.transform.localRotation, Quaternion.identity) > 0.1f ||
                     (maquette.transform.localScale - Vector3.one).magnitude > AlignmentTolerance))
                {
                    report.Add($"SAĞLIK: '{maquette.name}' maketi ArenaBoundary'nin altında " +
                               "yerel-kimlikte değil — görülen ölçü ile muhafaza/işaretçiler " +
                               "ayrışabilir. 'JSON'dan DimensionMesh Üret'i yeniden çalıştır.");
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
                report?.Add("HATA: projede GameCatalog asset'i YOK — katalog eşitlenmedi. " +
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

        private static string[] CollectModeIds(GameCatalog catalog)
        {
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

            return ids.ToArray();
        }

        private static MapDefinition FindMapForScenePath(string scenePath)
        {
            if (!TryParseBoxScene(scenePath, out _, out string boxPath, out string boxName, out string sceneName) ||
                !string.Equals(boxName, sceneName, StringComparison.Ordinal))
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<MapDefinition>($"{boxPath}/Data/{sceneName}.asset");
        }

        /// <summary>
        /// <c>Assets/Arenas/Venues/&lt;Mekan&gt;/Scenes/&lt;Kutu&gt;/&lt;Sahne&gt;.unity</c> yolunu
        /// parçalarına ayırır. Kutu adı ile sahne adının EŞİT olması ayrıca çağıran tarafından
        /// kontrol edilir (eşit değilse yerleşim bozuktur ama yol yine de ayrıştırılabilir).
        /// </summary>
        private static bool TryParseBoxScene(
            string scenePath, out string venue, out string boxPath, out string boxName, out string sceneName)
        {
            venue = string.Empty;
            boxPath = string.Empty;
            boxName = string.Empty;
            sceneName = string.Empty;

            if (string.IsNullOrEmpty(scenePath))
            {
                return false;
            }

            string normalized = Normalize(scenePath);
            if (!normalized.StartsWith(VenuesRoot + "/", StringComparison.Ordinal))
            {
                return false;
            }

            string[] parts = normalized.Split('/');
            if (parts.Length != 7 || !string.Equals(parts[4], ScenesFolderName, StringComparison.Ordinal))
            {
                return false;
            }

            venue = parts[3];
            boxName = parts[5];
            boxPath = string.Join("/", parts, 0, 6);
            sceneName = Path.GetFileNameWithoutExtension(parts[6]);
            return true;
        }

        /// <summary>Kutunun DOĞRUDAN altındaki sahne dosyaları (alt klasörlere inilmez).</summary>
        private static List<string> DirectSceneFiles(string folderPath)
        {
            var result = new List<string>();
            string fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", folderPath));
            if (!Directory.Exists(fullPath))
            {
                return result;
            }

            string[] files = Directory.GetFiles(fullPath, "*.unity", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.Ordinal);
            for (int i = 0; i < files.Length; i++)
            {
                result.Add(folderPath + "/" + Path.GetFileName(files[i]));
            }

            return result;
        }

        /// <summary>Referans listesini serialize edilmiş diziye yazar (boyut + elemanlar).</summary>
        private static void WriteArray<T>(SerializedProperty arrayProp, List<T> values)
            where T : UnityEngine.Object
        {
            if (arrayProp == null || !arrayProp.isArray)
            {
                return;
            }

            arrayProp.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++)
            {
                arrayProp.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        private static string Prefix(ScanIssue issue)
        {
            return (issue.IsError ? "HATA: " : "UYARI: ") + issue.Text;
        }

        private static string Normalize(string path)
        {
            return path.Replace('\\', '/');
        }

        private static string LeafName(string path)
        {
            string normalized = Normalize(path);
            int slash = normalized.LastIndexOf('/');
            return slash < 0 ? normalized : normalized.Substring(slash + 1);
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
