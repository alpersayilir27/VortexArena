using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core.Arena;
using VortexArena.Core.Combat;

namespace VortexArena.Core.Editor
{
    /// <summary>Treats the arena folder tree as the single source of truth and SYNCS every registry
    /// to it: Build Settings, <c>GameCatalog.maps</c>, non-empty <c>ModeDefinition.maps</c> lists and
    /// <c>Server/config/maps.json</c>. The same sync also runs the weapon kit (WD assets, WPN prefab
    /// wiring, FX/indicator prefabs, <c>WeaponCatalog</c>), the random weapon pools derived from that
    /// catalog (<see cref="SyncModeLoadouts"/>) and the net item catalog — everything table-derived
    /// that ships, under one button.</summary>
    /// <remarks>
    /// <b>Why sync, not append:</b> an append-only tool leaves a deleted arena's row as "Missing" in
    /// Build Settings and the catalog lists; the APK build then aborts with "scene missing from
    /// disk" and states the cause nowhere. Extra entries are removed, missing ones are reported.
    /// <para><b>Expected layout</b> (box folder name = scene name = MapDefinition name):
    /// <c>Assets/Arenas/Venues/&lt;Venue&gt;/Scenes/&lt;Scene&gt;/&lt;Scene&gt;.unity</c> +
    /// <c>…/&lt;Scene&gt;/Data/&lt;Scene&gt;.asset</c>. All three names being equal guarantees that
    /// code finding the scene also finds its MapDefinition — a second free-form name inevitably
    /// drifted from the scene name.</para>
    /// <para>⚠️ The catalog is not picked by hand, it is resolved from the project: at runtime it is
    /// found via <c>Resources.Load&lt;GameCatalog&gt;("GameCatalog")</c>, so exactly one asset is
    /// correct. More than one catalog is a PROJECT ERROR — nothing is written.</para>
    /// <para>⚠️ No <c>EditorUtility.DisplayDialog</c> (CLI timeout trap): the export is called in its
    /// dialogless variant too, results go to <c>Debug.Log</c> and the window report.</para>
    /// </remarks>
    public class BuildElementsConfigurator : EditorWindow
    {
        /// <summary>Root of playable arenas — venue folders sit one level below.</summary>
        private const string VenuesRoot = "Assets/Arenas/Venues";

        /// <summary>Root of reference templates — these scenes NEVER enter Build Settings.</summary>
        private const string TemplateRoot = "Assets/Arenas/Template/";

        /// <summary>Whole arena tree: Build Settings rows with this prefix are scanned.</summary>
        private const string ArenasRoot = "Assets/Arenas/";

        /// <summary>Name of the folder collecting the boxes (<c>&lt;Venue&gt;/Scenes</c>).</summary>
        private const string ScenesFolderName = "Scenes";

        /// <summary>Folders allowed at a venue root; anything else is misplaced.</summary>
        private static readonly string[] AllowedVenueFolders = { "Art", "Data", "Prefabs", ScenesFolderName };

        /// <summary>Slack for the boundary/mesh alignment checks (position m · scale component). Not
        /// zero tolerance: millimetre drift is normal in hand placement and warning on every open
        /// would make the whole report unreadable.</summary>
        private const float AlignmentTolerance = 0.01f;

        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private List<string> selectedModeIds = new List<string>();

        [NonSerialized] private string[] availableModeIds = Array.Empty<string>();
        [NonSerialized] private ScanResult scan;
        [NonSerialized] private string loadedForScenePath = null;
        [NonSerialized] private List<string> lastReport;
        [NonSerialized] private List<BuildReadiness.ReadinessRow> readiness;
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

        /// <summary>Refreshes the mode list and the venue scan; WRITES to no asset.</summary>
        private void Refresh()
        {
            availableModeIds = CollectModeIds(ResolveCatalog(null));
            scan = Scan();

            // ⚠️ Readiness checks are collected HERE, not in OnGUI: OnGUI runs several times per
            // frame and the checks read prefabs/assets. Refresh runs on every window focus, which
            // keeps the list fresh enough.
            readiness = BuildReadiness.Collect();

            SyncActiveSceneFields();
            Repaint();
        }

        /// <summary>Fills the form fields from the active scene's MapDefinition when the scene
        /// changed.</summary>
        /// <remarks>⚠️ Never refills for the same scene: rereading on every window focus would
        /// silently revert a mode selection the user has not saved yet.</remarks>
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
            DrawReadiness();
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

        /// <summary>Readiness rows checked before a build, in run order.</summary>
        /// <remarks>⚠️ These checks only read. The only writer is "Hepsini Çalıştır" and it runs all
        /// rows (HMD overlays only when stale — reserializing the shared rig prefab every run would
        /// be merge noise). Buttonless rows just report state; a remaining ✗ is a human step the tool
        /// cannot fix (grip, fire audio, netItemId).</remarks>
        private void DrawReadiness()
        {
            EditorGUILayout.LabelField(
                new GUIContent(
                    "Hazırlık",
                    "Her satır bir kayıt yerinin güncel olup olmadığını SALT OKUR. Hepsini Çalıştır " +
                    "bunların hepsini koşar; kalan ✗ insan adımıdır. Ne zaman gerektiğini öğrenmek " +
                    "için satırın üstüne gel."),
                EditorStyles.boldLabel);

            if (readiness == null || readiness.Count == 0)
            {
                return;
            }

            // ⚠️ The action runs AFTER the loop: the tool calls Refresh() at the end and replaces
            // the list — run mid-loop, the number of controls drawn changes within the frame and
            // GUILayout reports a layout/repaint mismatch.
            BuildReadiness.ReadinessRow pending = default;
            bool hasPending = false;

            List<BuildReadiness.ReadinessRow> rows = readiness;
            for (int i = 0; i < rows.Count; i++)
            {
                BuildReadiness.ReadinessRow row = rows[i];

                using (new EditorGUILayout.HorizontalScope())
                {
                    // ⚠️ The tooltip goes on EVERY part of the row so "when do I need this" is
                    // readable wherever the mouse lands; the visible "?" announces that a tooltip
                    // exists at all.
                    EditorGUILayout.LabelField(
                        new GUIContent(row.Ok ? "✓" : "✗", row.Tooltip), GUILayout.Width(16f));
                    EditorGUILayout.LabelField(
                        new GUIContent(row.Title, row.Tooltip), EditorStyles.boldLabel, GUILayout.Width(210f));
                    EditorGUILayout.LabelField(
                        new GUIContent("?", row.Tooltip), EditorStyles.miniLabel, GUILayout.Width(12f));
                    EditorGUILayout.LabelField(
                        new GUIContent(row.Detail, row.Tooltip), EditorStyles.miniLabel);

                    if (!string.IsNullOrEmpty(row.ActionLabel) && row.Action != null &&
                        GUILayout.Button(row.ActionLabel, GUILayout.Width(90f)))
                    {
                        pending = row;
                        hasPending = true;
                    }
                }
            }

            if (!hasPending)
            {
                return;
            }

            pending.Action();
            RunAndLog(new List<string> { "hazırlık: " + pending.Title + " çalıştırıldı" });
        }

        /// <summary>⚠️ ONE button. Two buttons ("configure all" + "sync only") made the user decide
        /// which one applies, and with a non-box active scene the first was disabled and silently
        /// postponed the sync too. This single button is always enabled; without a box scene only
        /// the MapDefinition step is skipped.</summary>
        private void DrawButtons()
        {
            if (GUILayout.Button(
                    new GUIContent(
                        "Hepsini Çalıştır",
                        "Build almadan önce basılacak tek düğme. Aktif sahne bir arena kutusuysa önce " +
                        "onu kaydedip MapDefinition'ını yazar, sonra HER durumda tüm kayıt yerlerini " +
                        "eşitler (yukarıdaki satırlar). Sahne açık olmasa da çalışır — silinmiş " +
                        "arenanın Build Settings/katalog kalıntısını temizlemenin yolu budur."),
                    GUILayout.Height(32f)))
            {
                RunEverything();
            }

            EditorGUILayout.HelpBox(
                "Build öncesi tek adım: yukarıdaki satırların hepsini bu düğme koşar. Aktif sahne bir " +
                "arena kutusu değilse MapDefinition adımı atlanır, eşitleme yine tam koşar. Kalan ✗ " +
                "satırlar aracın düzeltemeyeceği insan adımlarıdır — satırın üstüne gel, ne zaman " +
                "gerektiği yazıyor.",
                MessageType.Info);
        }

        /// <summary>Runs every readiness row in order.</summary>
        /// <remarks>⚠️ <see cref="ConfigureActiveScene"/> already calls <see cref="SyncAll"/> at its
        /// end, so it is not called a SECOND time in a box scene (it would redo the work and
        /// duplicate the report).
        /// <para>⚠️ HMD overlays are installed only when STALE: writing to the shared rig prefab on
        /// every run would produce a prefab diff even when nothing changed.</para></remarks>
        private void RunEverything()
        {
            var report = new List<string>();

            Scene scene = SceneManager.GetActiveScene();
            bool activeSceneIsBox =
                TryParseBoxScene(scene.path, out _, out _, out string boxName, out string sceneName) &&
                string.Equals(boxName, sceneName, StringComparison.Ordinal);

            if (activeSceneIsBox)
            {
                report.AddRange(ConfigureActiveScene(displayName, selectedModeIds.ToArray()));
            }
            else
            {
                report.Add("aktif sahne bir arena kutusu değil — MapDefinition'a dokunulmadı, " +
                           "yalnız eşitleme koşuldu.");
                SyncAll(report);
            }

            // ⚠️ Exception swallowed (same pattern as SyncWeaponKit): a contract drift in the rig
            // prefab must not swallow the arena sync's report.
            try
            {
                if (!HmdOverlayBuilder.IsRigUpToDate(out string hmdDetail))
                {
                    HmdOverlayBuilder.BuildOverlays();
                    report.Add("HMD katmanları kuruldu (bayattı: " + hmdDetail + ")");
                }
            }
            catch (Exception e)
            {
                report.Add("HMD katmanları HATA: " + e.Message);
                Debug.LogException(e);
            }

            RunAndLog(report);
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

        // ---------------------------------------------------------------- scan

        /// <summary>A scan finding — neither errors nor warnings stop the work, all are
        /// reported.</summary>
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

        /// <summary>Everything known about one box folder
        /// (<c>&lt;Venue&gt;/Scenes/&lt;Scene&gt;</c>).</summary>
        private sealed class BoxRecord
        {
            public string Venue;
            public string BoxPath;
            public string BoxName;
            public string ScenePath = string.Empty;
            public string SceneName = string.Empty;
            public string MapPath = string.Empty;
            public MapDefinition Map;

            /// <summary>Whether the scene name and MapDefinition's <c>sceneName</c> drifted.</summary>
            public bool SceneNameMismatch;

            /// <summary>Layout is correct — this box ENTERS the Build Settings/catalog sync.</summary>
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

            /// <summary>Scene paths present in Build Settings (for drawing the layout table).</summary>
            public readonly HashSet<string> BuildScenePaths =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            /// <summary>Boxes entering the sync — scan order preserved.</summary>
            public readonly List<BoxRecord> ValidBoxes = new List<BoxRecord>();
        }

        /// <summary>Reads the venue tree and produces the findings. READ ONLY — it runs on every
        /// window focus, so it writes to no asset; <see cref="SyncAll"/> does the fixing.</summary>
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

            // ⚠️ Only the box's DIRECT children: the lightmap folder shares the scene's name and a
            // recursive scan would mistake its files for scenes.
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
                    $"'{box.MapPath}' YOK — sahneyi aç ve 'Hepsini Çalıştır' ile modlarını seç."));
                return box;
            }

            // ⚠️ A MapDefinition is never auto-created: an empty `supportedModeIds` means
            // "unrestricted", so a generated lobby map would silently be playable in every mode.

            if (!string.Equals(box.Map.SceneName, box.SceneName, StringComparison.Ordinal))
            {
                box.SceneNameMismatch = true;
                box.Issues.Add(new ScanIssue(false,
                    $"'{box.MapPath}'.sceneName = '{box.Map.SceneName}' ≠ '{box.SceneName}' — " +
                    "senkronizasyonda düzeltilir (dosya sistemi otoritedir)."));
            }

            return box;
        }

        /// <summary>MapDefinitions inside the venue tree that are not in their expected box.</summary>
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

                // Assets/Arenas/Venues/<Venue>/Scenes/<Scene>/Data/<Scene>.asset
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

        // ------------------------------------------------------------- sync

        /// <summary>Syncs the folder tree into Build Settings, <c>GameCatalog.maps</c> and non-empty
        /// <c>ModeDefinition.maps</c> lists, then runs the weapon kit and the
        /// <c>ModeDefinition.loadout</c> pools derived from it. Writes what it did line by line into
        /// <paramref name="report"/>. Throws NO exception.</summary>
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

            FixSceneNames(current, report, false);

            var maps = new List<MapDefinition>();
            for (int i = 0; i < current.ValidBoxes.Count; i++)
            {
                if (current.ValidBoxes[i].Map != null)
                {
                    maps.Add(current.ValidBoxes[i].Map);
                }
            }

            SyncBuildSettings(current, report, false);

            GameCatalog catalog = ResolveCatalog(report);
            if (catalog != null)
            {
                SyncCatalogMaps(catalog, maps, report, false);
                SyncModeMaps(catalog, maps, report, false);
            }

            AssetDatabase.SaveAssets();

            SyncWeaponKit(report);

            // ⚠️ AFTER the kit: the pool's source is WeaponCatalog, which SyncWeaponKit just wrote.
            // Run first, a new weapon would only reach the pool on the SECOND sync.
            if (catalog != null)
            {
                SyncModeLoadouts(catalog, report, false);
                AssetDatabase.SaveAssets();
            }

            ServerConfigExportResult export = ServerConfigExporter.Export(false);
            report.Add("export: " + (export != null ? export.Summary : "sonuç alınamadı"));
            if (export != null && export.Warnings != null)
            {
                for (int i = 0; i < export.Warnings.Count; i++)
                {
                    report.Add("export uyarısı: " + export.Warnings[i]);
                }
            }

            // Health checks look at the ACTIVE SCENE; outside a box what they measure (boundary,
            // dimension mesh) is meaningless for that scene.
            string activePath = SceneManager.GetActiveScene().path;
            if (TryParseBoxScene(activePath, out _, out _, out string boxName, out string sceneName) &&
                string.Equals(boxName, sceneName, StringComparison.Ordinal))
            {
                RunHealthChecks(report);
            }
        }

        /// <summary>Weapon kit + net item catalog — runs on EVERY sync, with no separate
        /// button/menu.</summary>
        /// <remarks>
        /// <b>Why here:</b> "build elements" are not only arenas; WPN prefabs, WD assets, the
        /// weapon/item catalogs and the front-grip indicator are table-derived content that ships
        /// too. A separate menu item meant "a tool someone forgets to run", surfacing in the field as
        /// "the weapon cannot be grabbed / the item is not drawn".
        /// <para>⚠️ The kit run is idempotent: an unchanged asset is written with the same content and
        /// produces no diff. Gaps needing a human step (unauthored grip, unassigned fire audio,
        /// unassigned <c>netItemId</c>) are NOT fixed here — they go to the report and the readiness
        /// rows.</para>
        /// <para>⚠️ Exception swallowed: a contract drift in the weapon kit must not stop the arena
        /// sync — the error enters the report as a line.</para>
        /// </remarks>
        private static void SyncWeaponKit(List<string> report)
        {
            try
            {
                report.Add(WeaponKitBuilder.BuildAll());
            }
            catch (Exception e)
            {
                report.Add("silah kiti HATA: " + e.Message + " (ayrıntı konsolda)");
                Debug.LogException(e);
            }

            try
            {
                report.Add(NetItemIdGuard.Rebuild());
            }
            catch (Exception e)
            {
                report.Add("net eşya kataloğu HATA: " + e.Message + " (ayrıntı konsolda)");
                Debug.LogException(e);
            }
        }

        /// <summary>The file system is authoritative: <c>sceneName</c> fields that drifted from the
        /// scene name are written back.</summary>
        /// <remarks>⚠️ <paramref name="dryRun"/> = no writing, identical report, so the readiness row
        /// and the sync share one body; a second implementation of the check would silently drift
        /// from the sync. The return value counts CHANGE lines in the report (warnings do not
        /// count).</remarks>
        private static int FixSceneNames(ScanResult current, List<string> report, bool dryRun)
        {
            int changes = 0;

            for (int i = 0; i < current.ValidBoxes.Count; i++)
            {
                BoxRecord box = current.ValidBoxes[i];
                if (box.Map == null || !box.SceneNameMismatch)
                {
                    continue;
                }

                if (!dryRun)
                {
                    var mapObject = new SerializedObject(box.Map);
                    mapObject.FindProperty("sceneName").stringValue = box.SceneName;
                    mapObject.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(box.Map);
                }

                report.Add($"düzeltildi: {box.MapPath}.sceneName = {box.SceneName}");
                changes++;
            }

            return changes;
        }

        /// <summary>
        /// Rebuilds the Build Settings list.
        /// <para>⚠️ Order is preserved, never re-sorted: <c>Boot.unity</c> must stay at index 0 (the
        /// app opens it). Non-arena rows keep their order at the front, arena rows follow, and new
        /// arenas are appended Ordinal alphabetically.</para>
        /// <para>⚠️ Template scenes NEVER enter the list: they would open a row for a venue that does
        /// not exist at server startup.</para>
        /// <para>⚠️ In <paramref name="dryRun"/> <c>EditorBuildSettings.scenes</c> is not assigned but
        /// the counting still happens, so the readiness row and the sync read from one body.</para>
        /// </summary>
        private static int SyncBuildSettings(ScanResult current, List<string> report, bool dryRun)
        {
            int changes = 0;
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
                    changes++;
                    continue;
                }

                string path = Normalize(entry.path);
                bool onDisk = AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null;

                if (!path.StartsWith(ArenasRoot, StringComparison.Ordinal))
                {
                    if (!onDisk)
                    {
                        report.Add($"kaldırıldı (Build Settings): {path} — diskte yok");
                        changes++;
                        continue;
                    }

                    outside.Add(new EditorBuildSettingsScene(path, entry.enabled));
                    continue;
                }

                if (path.StartsWith(TemplateRoot, StringComparison.Ordinal))
                {
                    report.Add($"kaldırıldı (Build Settings): {path} — şablon");
                    changes++;
                    continue;
                }

                if (!onDisk)
                {
                    report.Add($"kaldırıldı (Build Settings): {path} — diskte yok");
                    changes++;
                    continue;
                }

                if (!targets.ContainsKey(path))
                {
                    report.Add($"kaldırıldı (Build Settings): {path} — mekan ağacında değil");
                    changes++;
                    continue;
                }

                if (!placed.Add(path))
                {
                    report.Add($"kaldırıldı (Build Settings): {path} — yinelenen satır");
                    changes++;
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
                changes++;
            }

            var final = new List<EditorBuildSettingsScene>(outside.Count + arena.Count);
            final.AddRange(outside);
            final.AddRange(arena);
            if (!dryRun)
            {
                EditorBuildSettings.scenes = final.ToArray();
            }

            report.Add($"Build Settings: {final.Count} sahne ({outside.Count} arena dışı + {arena.Count} arena)");
            return changes;
        }

        /// <summary><c>GameCatalog.maps</c> = every scanned map. Existing order is kept and new ones
        /// are appended; null and no-longer-scanned (deleted/moved) references are REMOVED — a
        /// leftover "Missing" row would draw as an empty entry in the admin map picker.</summary>
        /// <remarks>⚠️ Nothing is written in <paramref name="dryRun"/>; the return value counts
        /// removed + added references (which already covers "the array to write differs from the
        /// current one").</remarks>
        private static int SyncCatalogMaps(GameCatalog catalog, List<MapDefinition> maps, List<string> report, bool dryRun)
        {
            int changes = 0;
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
                    changes++;
                    continue;
                }

                if (!known.Contains(map))
                {
                    report.Add($"kaldırıldı (GameCatalog.maps): {AssetDatabase.GetAssetPath(map)} — mekan ağacında değil");
                    changes++;
                    continue;
                }

                if (ordered.Contains(map))
                {
                    report.Add($"kaldırıldı (GameCatalog.maps): {map.SceneName} — yinelenen kayıt");
                    changes++;
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
                    changes++;
                }
            }

            if (!dryRun)
            {
                WriteArray(prop, ordered);
                catalogObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(catalog);
            }

            return changes;
        }

        /// <summary>Syncs each mode's NON-EMPTY <c>maps</c> list with the maps supporting that
        /// mode.</summary>
        /// <remarks>
        /// ⚠️ An empty list means "unrestricted" (GameCatalog.MapsForMode falls back to every map in
        /// the catalog), so two rules bind: an empty list is left untouched and a non-empty list is
        /// NEVER emptied — with an empty target set the mode would silently accept all maps. In that
        /// case the list stays as-is and a warning is reported.
        /// <para>Null elements are cleaned regardless: a "Missing" entry is neither a restriction nor
        /// a map, only an empty row in the admin picker.</para>
        /// <para>⚠️ No list is written in <paramref name="dryRun"/>; the return value counts change
        /// lines in the report (the WARNING for "no supporting map" does not count).</para>
        /// </remarks>
        private static int SyncModeMaps(GameCatalog catalog, List<MapDefinition> maps, List<string> report, bool dryRun)
        {
            int changes = 0;
            ModeDefinition[] modes = catalog.Modes;
            if (modes == null)
            {
                return changes;
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
                        changes++;
                        continue;
                    }

                    if (kept.Contains(map))
                    {
                        report.Add($"kaldırıldı ({mode.ModeId}.maps): {map.SceneName} — yinelenen kayıt");
                        changed = true;
                        changes++;
                        continue;
                    }

                    kept.Add(map);
                }

                if (kept.Count == 0)
                {
                    // The list is (now) empty = unrestricted; writing the target set here would
                    // restrict the mode unintentionally.
                    if (changed && !dryRun)
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
                    if (changed && !dryRun)
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
                        changes++;
                    }
                }

                for (int i = 0; i < target.Count; i++)
                {
                    if (!final.Contains(target[i]))
                    {
                        final.Add(target[i]);
                        report.Add($"eklendi ({mode.ModeId}.maps): {target[i].SceneName}");
                        changed = true;
                        changes++;
                    }
                }

                if (!changed || dryRun)
                {
                    continue;
                }

                WriteArray(prop, final);
                modeObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(mode);
            }

            return changes;
        }

        /// <summary>Syncs the <c>loadout</c> list of random-granting modes
        /// (<see cref="ModeWeaponSource.RandomGrant"/>) with <c>WeaponCatalog</c>: missing weapons
        /// added, null / duplicate / uncatalogued references removed, existing order kept.</summary>
        /// <remarks>
        /// ⚠️ An empty list IS filled here — the opposite of <c>maps</c> (where empty = unrestricted).
        /// In <c>WeaponGranter.PickFromLoadout</c> an empty pool means NO weapon at all, not
        /// "unrestricted": the mode becomes unplayable. The two fields must therefore differ.
        /// <para>⚠️ Only <c>RandomGrant</c> modes are touched. In
        /// <see cref="ModeWeaponSource.WeaponCanvas"/> modes the ARENA decides which weapon stands
        /// in the scene and <c>loadout</c> is never read; writing there would inflate an unread
        /// list.</para>
        /// <para><b>Why sync, not a hand written list:</b> as the arsenal grows a hand written pool
        /// inevitably goes stale, and its only symptom is "some weapons never show up" — nothing is
        /// logged and no build breaks. The catalog is the pool's single source of truth; there is no
        /// per-mode weapon restriction (if one is wanted, a field to carry it is designed first — a
        /// hand trimmed list does not stand in for it, this run writes it back).</para>
        /// <para>⚠️ No pool is written in <paramref name="dryRun"/>; the <c>loadout: … (değişmedi)</c>
        /// status line is still reported and does not count as a CHANGE — it is the readiness row's
        /// detail.</para>
        /// </remarks>
        private static int SyncModeLoadouts(GameCatalog catalog, List<string> report, bool dryRun)
        {
            int changes = 0;
            ModeDefinition[] modes = catalog.Modes;
            if (modes == null)
            {
                return changes;
            }

            var weaponCatalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(WeaponKitBuilder.CatalogPath);
            if (weaponCatalog == null)
            {
                report.Add("UYARI: '" + WeaponKitBuilder.CatalogPath + "' okunamadı — mod loadout'ları " +
                           "eşitlenmedi (rastgele silah veren modlar eski havuzda kalır).");
                return changes;
            }

            var pool = new List<WeaponDefinition>();
            WeaponDefinition[] definitions = weaponCatalog.Definitions;
            for (int i = 0; definitions != null && i < definitions.Length; i++)
            {
                if (definitions[i] != null && !pool.Contains(definitions[i]))
                {
                    pool.Add(definitions[i]);
                }
            }

            if (pool.Count == 0)
            {
                report.Add("UYARI: WeaponCatalog boş — mod loadout'ları eşitlenmedi. Dolu bir listeyi " +
                           "boşaltmak rastgele silah veren modu oynanamaz hâle getirirdi.");
                return changes;
            }

            var poolSet = new HashSet<WeaponDefinition>(pool);

            for (int m = 0; m < modes.Length; m++)
            {
                ModeDefinition mode = modes[m];
                if (mode == null || mode.Weapons != ModeWeaponSource.RandomGrant)
                {
                    continue;
                }

                var modeObject = new SerializedObject(mode);
                SerializedProperty prop = modeObject.FindProperty("loadout");
                if (prop == null || !prop.isArray)
                {
                    report.Add($"UYARI: '{mode.ModeId}' modunda 'loadout' alanı yok (sözleşme kayması?).");
                    continue;
                }

                var final = new List<WeaponDefinition>();
                bool changed = false;

                for (int i = 0; i < prop.arraySize; i++)
                {
                    var weapon = prop.GetArrayElementAtIndex(i).objectReferenceValue as WeaponDefinition;
                    if (weapon == null)
                    {
                        report.Add($"kaldırıldı ({mode.ModeId}.loadout): boş/Missing referans");
                        changed = true;
                        changes++;
                        continue;
                    }

                    if (final.Contains(weapon))
                    {
                        report.Add($"kaldırıldı ({mode.ModeId}.loadout): {weapon.name} — yinelenen kayıt");
                        changed = true;
                        changes++;
                        continue;
                    }

                    if (!poolSet.Contains(weapon))
                    {
                        report.Add($"kaldırıldı ({mode.ModeId}.loadout): {weapon.name} — WeaponCatalog'da yok");
                        changed = true;
                        changes++;
                        continue;
                    }

                    final.Add(weapon);
                }

                for (int i = 0; i < pool.Count; i++)
                {
                    if (!final.Contains(pool[i]))
                    {
                        final.Add(pool[i]);
                        report.Add($"eklendi ({mode.ModeId}.loadout): {pool[i].name}");
                        changed = true;
                        changes++;
                    }
                }

                if (changed && !dryRun)
                {
                    WriteArray(prop, final);
                    modeObject.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(mode);
                }

                // A definition without a prefab STAYS in the list but never enters the pool
                // (WeaponGranter filters it). Removing it would silently bring it back once the
                // prefab is wired; the real gap is the prefab link.
                int usable = 0;
                for (int i = 0; i < final.Count; i++)
                {
                    if (final[i].Prefab != null)
                    {
                        usable++;
                    }
                    else
                    {
                        report.Add($"UYARI ({mode.ModeId}.loadout): {final[i].name} prefabsız — " +
                                   "havuzda sayılmaz.");
                    }
                }

                report.Add($"loadout: {mode.ModeId} = {usable}/{pool.Count} silah" +
                           (changed ? " (güncellendi)" : " (değişmedi)"));
            }

            return changes;
        }

        // ------------------------------------------------------ readiness checks

        /// <summary>Whether Build Settings + <c>GameCatalog.maps</c> + the modes' map lists are up to
        /// date.</summary>
        /// <remarks>⚠️ READ ONLY. The writer is <see cref="SyncAll"/> and both share the SAME body
        /// (<c>dryRun</c>) — the check logic is never reimplemented here, it would silently drift
        /// from what the sync actually does.
        /// <para>⚠️ Exceptions are NOT swallowed — <c>BuildReadiness.Check</c> already does.</para>
        /// </remarks>
        internal static bool IsArenaRegistryUpToDate(out string detail)
        {
            ScanResult current = Scan();
            var probe = new List<string>();

            int changes = FixSceneNames(current, probe, true);
            changes += SyncBuildSettings(current, probe, true);

            GameCatalog catalog = ResolveCatalog(null);
            if (catalog == null)
            {
                detail = "GameCatalog çözülemedi.";
                return false;
            }

            var maps = new List<MapDefinition>();
            for (int i = 0; i < current.ValidBoxes.Count; i++)
            {
                if (current.ValidBoxes[i].Map != null)
                {
                    maps.Add(current.ValidBoxes[i].Map);
                }
            }

            changes += SyncCatalogMaps(catalog, maps, probe, true);
            changes += SyncModeMaps(catalog, maps, probe, true);

            // A scan ERROR counts as ✗ too: a misplaced box never enters the sync, so "no
            // difference" would not mean "everything is fine".
            string firstError = FirstScanError(current);
            if (changes == 0 && firstError == null)
            {
                detail = "Build Settings + GameCatalog + mod harita listeleri güncel.";
                return true;
            }

            detail = changes == 0
                ? firstError
                : DifferenceSummary(probe, changes);
            return false;
        }

        /// <summary>Whether the <c>loadout</c> pools of random-granting modes match
        /// <c>WeaponCatalog</c>.</summary>
        /// <remarks>⚠️ READ ONLY. The writer is <see cref="SyncAll"/> and both share the SAME body
        /// (<c>dryRun</c>); the check logic is never reimplemented here.
        /// <para>⚠️ Exceptions are NOT swallowed — <c>BuildReadiness.Check</c> already does.</para>
        /// </remarks>
        internal static bool AreModeLoadoutsUpToDate(out string detail)
        {
            GameCatalog catalog = ResolveCatalog(null);
            if (catalog == null)
            {
                detail = "GameCatalog çözülemedi.";
                return false;
            }

            var probe = new List<string>();
            int changes = SyncModeLoadouts(catalog, probe, true);

            if (changes != 0)
            {
                detail = DifferenceSummary(probe, changes);
                return false;
            }

            var status = new List<string>();
            for (int i = 0; i < probe.Count; i++)
            {
                if (probe[i].StartsWith("loadout:", StringComparison.Ordinal))
                {
                    status.Add(probe[i]);
                }
            }

            detail = status.Count > 0 ? string.Join(" · ", status) : "rastgele silah veren mod yok.";
            return true;
        }

        /// <summary>Picks the first two CHANGE lines from the dry-run report.</summary>
        /// <remarks>⚠️ Only change prefixes are taken: warning and status lines do not enter the
        /// counter, so showing them in the detail would mislead.</remarks>
        private static string DifferenceSummary(List<string> probe, int changes)
        {
            var picked = new List<string>(2);
            for (int i = 0; i < probe.Count && picked.Count < 2; i++)
            {
                string line = probe[i];
                if (line.StartsWith("kaldırıldı", StringComparison.Ordinal) ||
                    line.StartsWith("eklendi", StringComparison.Ordinal) ||
                    line.StartsWith("düzeltildi", StringComparison.Ordinal))
                {
                    picked.Add(line);
                }
            }

            return string.Join(" · ", picked) + $" (toplam {changes} fark)";
        }

        /// <summary>First ERROR finding of the scan; <c>null</c> when there is none.</summary>
        private static string FirstScanError(ScanResult current)
        {
            for (int i = 0; i < current.Issues.Count; i++)
            {
                if (current.Issues[i].IsError)
                {
                    return current.Issues[i].Text;
                }
            }

            for (int v = 0; v < current.Venues.Count; v++)
            {
                VenueRecord venue = current.Venues[v];
                for (int i = 0; i < venue.Issues.Count; i++)
                {
                    if (venue.Issues[i].IsError)
                    {
                        return venue.Issues[i].Text;
                    }
                }

                for (int b = 0; b < venue.Boxes.Count; b++)
                {
                    BoxRecord box = venue.Boxes[b];
                    for (int i = 0; i < box.Issues.Count; i++)
                    {
                        if (box.Issues[i].IsError)
                        {
                            return box.Issues[i].Text;
                        }
                    }
                }
            }

            return null;
        }

        // -------------------------------------------------------- active scene

        /// <summary>Writes/updates the active scene's MapDefinition, then syncs every registry.
        /// Throws NO exception — what it did is returned line by line.</summary>
        /// <remarks>⚠️ The asset name is not taken from the user, it is derived from the scene name:
        /// a second free-form name was a source of truth that could drift from it.</remarks>
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

            // Writing the MapDefinition before the scene hits disk would produce a record that does
            // not reflect the scene's latest changes.
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

        /// <summary>Points checked before treating a scene as ready. None stops the work — all are
        /// reported, because they are the manual part of the setup.</summary>
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

                // ⚠️ The boundary's POSITION/ROTATION is not checked: the default is the world
                // origin, but playing a zone inside an existing environment means moving/rotating it
                // deliberately — distance cannot tell intent apart. SCALE is an error in every case:
                // the dimensions file is in metres and TransformPoint applies scale too, so anything
                // off 1 silently builds the boundary, anchors and framing at the wrong size.
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

                // ⚠️ The rule is INVERTED here: the mesh MUST ship, because the calibration anchors
                // (anchor_a/anchor_b) sit under it and are needed at runtime. An 'EditorOnly' tag
                // strips the root with all its children — the arena silently becomes unalignable on
                // site. The visual branch is already removed by DimensionMeshBuildStripper.
                if (maquette.CompareTag("EditorOnly"))
                {
                    report.Add($"SAĞLIK: '{maquette.name}' maketi 'EditorOnly' ETİKETLİ — build'e " +
                               "girmez ve kalibrasyon işaretçileri onunla birlikte silinir. " +
                               "Etiketi 'Untagged' yap.");
                }

                // With a boundary present the mesh must sit UNDER it at local identity (that is how
                // "JSON'dan DimensionMesh Üret" builds it): if they drift, the visible size and the
                // place the boundary/anchors are actually built silently differ. Without a boundary
                // the mesh may sit anywhere — read back references the mesh's own root.
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

            // ⚠️ There is no "leftover Wall_*" check here and none is added: the arena's real walls
            // carry that name in the environment art too. A name based warning would false-alarm on
            // every open and make the whole health report unreadable.
        }

        // -------------------------------------------------------------- helpers

        /// <summary>Finds the project's ONE <c>GameCatalog</c> asset; null when missing or
        /// duplicated.</summary>
        /// <remarks>More than one catalog is a project error: <c>Resources.Load</c> gives no
        /// guarantee which one it returns at runtime.</remarks>
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

        /// <summary>Splits
        /// <c>Assets/Arenas/Venues/&lt;Venue&gt;/Scenes/&lt;Box&gt;/&lt;Scene&gt;.unity</c> into its
        /// parts.</summary>
        /// <remarks>Box name == scene name is checked separately by the caller (a mismatch means a
        /// broken layout, but the path still parses).</remarks>
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

        /// <summary>Scene files DIRECTLY under the box (subfolders are not walked).</summary>
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

        /// <summary>Writes a reference list into a serialized array (size + elements).</summary>
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
