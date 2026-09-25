using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// <c>Tools &gt; VortexArena &gt; Arena &gt; Sahne Bütçesini Ölç</c> — compares the Quest render
    /// load of a tenant's arena scenes without opening them by hand.
    ///
    /// <para>Scenes are loaded ADDITIVELY and closed again; scenes the user already has open are
    /// measured in place and left alone. Nothing is ever saved.</para>
    ///
    /// <para><b>NO modal dialogs:</b> <c>EditorUtility.DisplayDialog</c> locks the main thread and
    /// breaks Unity CLI verification. Feedback is in-window <c>HelpBox</c> + console.</para>
    /// </summary>
    public class SceneBudgetWindow : EditorWindow
    {
        private const string MenuPath = "Tools/VortexArena/Arena/Sahne Bütçesini Ölç";
        private const string VenuesRoot = "Assets/Arenas/Venues";
        private const string AllTenantsLabel = "Hepsi";

        // Quest 3 budget per scene: beyond these a frame no longer fits 72 Hz on the headset.
        private const long TrisWarn = 1_000_000;
        private const int RenderersWarn = 1500;
        private const int TerrainsWarn = 1;

        /// <summary>Below this renderer count missing static flags are not worth reporting.</summary>
        private const int StaticFlagCheckMin = 200;

        private const int TopMeshCount = 10;
        private const int TopMeshInLog = 5;

        private static readonly CultureInfo Numbers = CultureInfo.GetCultureInfo("tr-TR");
        private static readonly Color WarnColor = new Color(1f, 0.45f, 0.4f);

        [SerializeField] private Vector2 scroll;
        [SerializeField] private int tenantIndex;

        // Rebuilt on demand — losing these to a domain reload is harmless.
        [NonSerialized] private string[] tenantLabels = Array.Empty<string>();
        [NonSerialized] private List<SceneBudget> results = new List<SceneBudget>();
        [NonSerialized] private string message;

        [MenuItem(MenuPath, false, 5)]
        private static void Open()
        {
            var window = GetWindow<SceneBudgetWindow>(false, "Sahne Bütçesi", true);
            window.minSize = new Vector2(720f, 420f);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Sahne Bütçesi");
            RefreshTenants();
        }

        private void RefreshTenants()
        {
            if (!AssetDatabase.IsValidFolder(VenuesRoot))
            {
                tenantLabels = Array.Empty<string>();
                return;
            }

            string[] folders = AssetDatabase.GetSubFolders(VenuesRoot);
            var labels = new List<string>(folders.Length + 1) { AllTenantsLabel };
            for (int i = 0; i < folders.Length; i++)
            {
                labels.Add(FolderName(folders[i]));
            }

            tenantLabels = labels.ToArray();
            tenantIndex = Mathf.Clamp(tenantIndex, 0, tenantLabels.Length - 1);
        }

        private static string FolderName(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash >= 0 ? path.Substring(slash + 1) : path;
        }

        // ----------------------------------------------------------------------- gui

        private void OnGUI()
        {
            if (tenantLabels.Length == 0)
            {
                EditorGUILayout.HelpBox($"{VenuesRoot} bulunamadı.", MessageType.Warning);
                if (GUILayout.Button("Yenile"))
                {
                    RefreshTenants();
                }

                return;
            }

            DrawToolbar();
            EditorGUILayout.LabelField(
                "Kamera taşıyan kök objeler (oyuncu rig'i) sayılmaz.", EditorStyles.miniLabel);

            if (!string.IsNullOrEmpty(message))
            {
                EditorGUILayout.HelpBox(message, MessageType.Info);
            }

            EditorGUILayout.Space(2f);
            DrawHeaderRow();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int i = 0; i < results.Count; i++)
            {
                DrawRow(results[i]);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal();
            tenantIndex = EditorGUILayout.Popup("Tenant", tenantIndex, tenantLabels,
                GUILayout.Width(340f));

            if (GUILayout.Button("Ölç", GUILayout.Width(80f)))
            {
                Measure();
            }

            using (new EditorGUI.DisabledScope(results.Count == 0))
            {
                if (GUILayout.Button("Konsola yaz", GUILayout.Width(110f)))
                {
                    LogMarkdown();
                }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private const float NameWidth = 210f;
        private const float NumWidth = 88f;
        private const float RendererWidth = 110f;
        private const float LightWidth = 120f;

        private void DrawHeaderRow()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            Head("Sahne", NameWidth);
            Head("Üçgen (LOD0)", NumWidth);
            Head("Vertex", NumWidth);
            Head("Renderer (LOD0)", RendererWidth);
            Head("Static", NumWidth);
            Head("Skinned", NumWidth);
            Head("Materyal", NumWidth);
            Head("Terrain", NumWidth);
            Head("Particle", NumWidth);
            Head("Işık RT/Mix/Gölge", LightWidth);
            GUILayout.Label("Uyarı", EditorStyles.miniBoldLabel);
            EditorGUILayout.EndHorizontal();
        }

        private static void Head(string text, float width) =>
            GUILayout.Label(text, EditorStyles.miniBoldLabel, GUILayout.Width(width));

        private void DrawRow(SceneBudget b)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent(b.Name, b.Path), GUILayout.Width(NameWidth));

            if (b.Failed)
            {
                GUI.color = WarnColor;
                GUILayout.Label("Açılamadı");
                GUI.color = Color.white;
                EditorGUILayout.EndHorizontal();
                return;
            }

            Cell(Num(b.TrisEffective), NumWidth, b.TrisEffective > TrisWarn);
            Cell(Num(b.VertsEffective), NumWidth, false);
            Cell($"{Num(b.MeshRenderersEffective)}/{Num(b.MeshRenderersTotal)}", RendererWidth,
                b.MeshRenderersEffective > RenderersWarn);
            Cell(Num(b.StaticFlagged), NumWidth, b.MissingStaticFlags);
            Cell($"{Num(b.SkinnedActive)} · {Num(b.SkinnedTris)}", NumWidth, false);
            Cell(Num(b.UniqueMaterials), NumWidth, false);
            Cell(Num(b.Terrains), NumWidth, b.Terrains > TerrainsWarn);
            Cell(Num(b.ParticleSystems), NumWidth, false);
            Cell($"{b.LightsRealtime}/{b.LightsMixed}/{b.LightsCastingShadows}", LightWidth, false);

            string warnings = b.WarningText();
            if (!string.IsNullOrEmpty(warnings))
            {
                GUI.color = WarnColor;
                GUILayout.Label(warnings);
                GUI.color = Color.white;
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel++;
            b.Expanded = EditorGUILayout.Foldout(b.Expanded, "En ağır mesh'ler", true);
            if (b.Expanded)
            {
                EditorGUILayout.LabelField(
                    $"Tüm LOD'lar dahil üçgen: {Num(b.TrisAllLods)}" +
                    $" · renderer: {Num(b.MeshRenderersActive)}" +
                    (b.Terrains > 0
                        ? $" · terrain yükseklik üçgeni (yaklaşık): {Num(b.TerrainHeightTrisApprox)}"
                        : string.Empty));

                for (int i = 0; i < b.TopMeshes.Count; i++)
                {
                    MeshStat m = b.TopMeshes[i];
                    EditorGUILayout.LabelField($"{m.Name} ×{m.Count} = {Num(m.Triangles)} üçgen");
                }

                if (b.TopMeshes.Count == 0)
                {
                    EditorGUILayout.LabelField("—");
                }
            }

            EditorGUI.indentLevel--;
        }

        private static void Cell(string text, float width, bool warn)
        {
            if (warn)
            {
                GUI.color = WarnColor;
            }

            GUILayout.Label(text, GUILayout.Width(width));
            GUI.color = Color.white;
        }

        private static string Num(long value) => value.ToString("N0", Numbers);

        // ------------------------------------------------------------------- measure

        private void Measure()
        {
            message = null;
            results.Clear();

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                message = "Play modunda ölçüm yapılmaz.";
                return;
            }

            List<string> scenePaths = CollectScenePaths();
            if (scenePaths.Count == 0)
            {
                message = tenantIndex == 0
                    ? "Hiçbir tenant'ta sahne yok."
                    : "Bu tenant'ta sahne yok.";
                return;
            }

            try
            {
                for (int i = 0; i < scenePaths.Count; i++)
                {
                    string path = scenePaths[i];
                    EditorUtility.DisplayProgressBar("Sahne Bütçesi",
                        System.IO.Path.GetFileNameWithoutExtension(path),
                        (float)i / scenePaths.Count);
                    results.Add(MeasureScene(path));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private List<string> CollectScenePaths()
        {
            var paths = new List<string>();
            string[] tenants = tenantIndex == 0
                ? AssetDatabase.GetSubFolders(VenuesRoot)
                : new[] { $"{VenuesRoot}/{tenantLabels[tenantIndex]}" };

            for (int t = 0; t < tenants.Length; t++)
            {
                string scenesFolder = $"{tenants[t]}/Scenes";
                if (!AssetDatabase.IsValidFolder(scenesFolder))
                {
                    continue;
                }

                string[] guids = AssetDatabase.FindAssets("t:SceneAsset", new[] { scenesFolder });
                for (int g = 0; g < guids.Length; g++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[g]);
                    if (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) &&
                        !paths.Contains(path))
                    {
                        paths.Add(path);
                    }
                }
            }

            paths.Sort(StringComparer.OrdinalIgnoreCase);
            return paths;
        }

        /// <summary>Measures one scene, opening it additively only if it is not already open.</summary>
        private static SceneBudget MeasureScene(string path)
        {
            var budget = new SceneBudget
            {
                Path = path,
                Name = System.IO.Path.GetFileNameWithoutExtension(path)
            };

            Scene scene = SceneManager.GetSceneByPath(path);
            bool alreadyOpen = scene.IsValid() && scene.isLoaded;

            if (!alreadyOpen)
            {
                try
                {
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Sahne bütçesi] '{path}' açılamadı: {e.Message}");
                    budget.Failed = true;
                    return budget;
                }
            }

            if (!scene.IsValid() || !scene.isLoaded)
            {
                budget.Failed = true;
                return budget;
            }

            try
            {
                Collect(scene, budget);
            }
            finally
            {
                if (!alreadyOpen)
                {
                    // removeScene: true → the user's scene list is left exactly as it was found.
                    EditorSceneManager.CloseScene(scene, true);
                }
            }

            return budget;
        }

        private static void Collect(Scene scene, SceneBudget budget)
        {
            var materials = new HashSet<Material>();
            var meshStats = new Dictionary<string, MeshStat>();

            GameObject[] roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
            {
                GameObject root = roots[r];

                // Camera roots are skipped: the player rig is identical in every scene and the Meta
                // hand prefab carries hundreds of editor-only debug spheres.
                if (root.GetComponentInChildren<Camera>(true) != null)
                {
                    continue;
                }

                var lodCulled = new HashSet<Renderer>();
                LODGroup[] groups = root.GetComponentsInChildren<LODGroup>(true);
                for (int g = 0; g < groups.Length; g++)
                {
                    LOD[] lods = groups[g].GetLODs();
                    for (int l = 1; l < lods.Length; l++)
                    {
                        Renderer[] renderers = lods[l].renderers;
                        for (int i = 0; i < renderers.Length; i++)
                        {
                            if (renderers[i] != null)
                            {
                                lodCulled.Add(renderers[i]);
                            }
                        }
                    }
                }

                MeshRenderer[] meshRenderers = root.GetComponentsInChildren<MeshRenderer>(true);
                for (int i = 0; i < meshRenderers.Length; i++)
                {
                    MeshRenderer mr = meshRenderers[i];
                    budget.MeshRenderersTotal++;
                    if (!mr.enabled || !mr.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    budget.MeshRenderersActive++;
                    if (GameObjectUtility.GetStaticEditorFlags(mr.gameObject) != 0)
                    {
                        budget.StaticFlagged++;
                    }

                    AddMaterials(materials, mr);

                    var filter = mr.GetComponent<MeshFilter>();
                    Mesh mesh = filter != null ? filter.sharedMesh : null;
                    long tris = Triangles(mesh);
                    budget.TrisAllLods += tris;

                    if (lodCulled.Contains(mr))
                    {
                        continue;
                    }

                    // Only one LOD level draws at a time: renderers count like triangles, LOD0 only.
                    budget.MeshRenderersEffective++;
                    budget.TrisEffective += tris;
                    budget.VertsEffective += mesh != null ? mesh.vertexCount : 0;
                    if (mesh != null && tris > 0)
                    {
                        AddMeshStat(meshStats, mesh.name, tris);
                    }
                }

                SkinnedMeshRenderer[] skinned =
                    root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                for (int i = 0; i < skinned.Length; i++)
                {
                    SkinnedMeshRenderer smr = skinned[i];
                    if (!smr.enabled || !smr.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    budget.SkinnedActive++;
                    budget.SkinnedTris += Triangles(smr.sharedMesh);
                    AddMaterials(materials, smr);
                }

                Terrain[] terrains = root.GetComponentsInChildren<Terrain>(true);
                for (int i = 0; i < terrains.Length; i++)
                {
                    Terrain terrain = terrains[i];
                    if (!terrain.enabled || !terrain.gameObject.activeInHierarchy ||
                        terrain.terrainData == null)
                    {
                        continue;
                    }

                    budget.Terrains++;
                    long res = terrain.terrainData.heightmapResolution;
                    budget.TerrainHeightTrisApprox += res * res * 2L;
                }

                ParticleSystem[] particles = root.GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < particles.Length; i++)
                {
                    if (particles[i].gameObject.activeInHierarchy)
                    {
                        budget.ParticleSystems++;
                    }
                }

                Light[] lights = root.GetComponentsInChildren<Light>(true);
                for (int i = 0; i < lights.Length; i++)
                {
                    Light light = lights[i];
                    if (!light.enabled || !light.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    if (light.lightmapBakeType == LightmapBakeType.Realtime)
                    {
                        budget.LightsRealtime++;
                    }
                    else if (light.lightmapBakeType == LightmapBakeType.Mixed)
                    {
                        budget.LightsMixed++;
                    }

                    if (light.shadows != LightShadows.None)
                    {
                        budget.LightsCastingShadows++;
                    }
                }
            }

            budget.UniqueMaterials = materials.Count;
            budget.TopMeshes = SortTop(meshStats);
        }

        private static void AddMaterials(HashSet<Material> set, Renderer renderer)
        {
            Material[] shared = renderer.sharedMaterials;
            for (int i = 0; i < shared.Length; i++)
            {
                if (shared[i] != null)
                {
                    set.Add(shared[i]);
                }
            }
        }

        private static void AddMeshStat(Dictionary<string, MeshStat> stats, string name, long tris)
        {
            if (!stats.TryGetValue(name, out MeshStat stat))
            {
                stat = new MeshStat { Name = name };
                stats[name] = stat;
            }

            stat.Count++;
            stat.Triangles += tris;
        }

        private static List<MeshStat> SortTop(Dictionary<string, MeshStat> stats)
        {
            var list = new List<MeshStat>(stats.Values);
            list.Sort((a, b) => b.Triangles.CompareTo(a.Triangles));
            if (list.Count > TopMeshCount)
            {
                list.RemoveRange(TopMeshCount, list.Count - TopMeshCount);
            }

            return list;
        }

        /// <summary>Triangle count from the index buffer — <c>mesh.triangles</c> throws on a
        /// Read/Write-disabled mesh and allocates a full copy on the rest.</summary>
        private static long Triangles(Mesh mesh)
        {
            if (mesh == null)
            {
                return 0;
            }

            long total = 0;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                if (mesh.GetTopology(sub) == MeshTopology.Triangles)
                {
                    total += mesh.GetIndexCount(sub) / 3;
                }
            }

            return total;
        }

        // ------------------------------------------------------------------- console

        private void LogMarkdown()
        {
            var text = new StringBuilder();
            text.AppendLine("| Sahne | Üçgen (LOD0) | Vertex | Renderer (LOD0) | Static | Skinned | " +
                            "Materyal | Terrain | Particle | Işık RT/Mix/Gölge | Uyarı |");
            text.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");

            for (int i = 0; i < results.Count; i++)
            {
                SceneBudget b = results[i];
                if (b.Failed)
                {
                    text.AppendLine($"| {b.Name} | — | — | — | — | — | — | — | — | — | Açılamadı |");
                    continue;
                }

                text.AppendLine($"| {b.Name} | {Num(b.TrisEffective)} | {Num(b.VertsEffective)} | " +
                                $"{Num(b.MeshRenderersEffective)}/{Num(b.MeshRenderersTotal)} | " +
                                $"{Num(b.StaticFlagged)} | {Num(b.SkinnedActive)} · " +
                                $"{Num(b.SkinnedTris)} | {Num(b.UniqueMaterials)} | " +
                                $"{Num(b.Terrains)} | {Num(b.ParticleSystems)} | " +
                                $"{b.LightsRealtime}/{b.LightsMixed}/{b.LightsCastingShadows} | " +
                                $"{b.WarningText()} |");
            }

            for (int i = 0; i < results.Count; i++)
            {
                SceneBudget b = results[i];
                if (b.Failed || b.TopMeshes.Count == 0)
                {
                    continue;
                }

                text.AppendLine();
                text.AppendLine($"**{b.Name}** — en ağır mesh'ler");
                int listed = Mathf.Min(TopMeshInLog, b.TopMeshes.Count);
                for (int m = 0; m < listed; m++)
                {
                    MeshStat stat = b.TopMeshes[m];
                    text.AppendLine($"- {stat.Name} ×{stat.Count} = {Num(stat.Triangles)}");
                }
            }

            // Single Log call: the table must stay copyable as one block.
            Debug.Log($"[Sahne bütçesi]\n{text}");
        }

        // --------------------------------------------------------------------- model

        private class MeshStat
        {
            public string Name;
            public int Count;
            public long Triangles;
        }

        private class SceneBudget
        {
            public string Name;
            public string Path;
            public bool Failed;
            public bool Expanded;

            public long TrisEffective;
            public long VertsEffective;
            public long TrisAllLods;
            public int MeshRenderersEffective;
            public int MeshRenderersActive;
            public int MeshRenderersTotal;
            public int StaticFlagged;
            public int SkinnedActive;
            public long SkinnedTris;
            public int UniqueMaterials;
            public int Terrains;
            public long TerrainHeightTrisApprox;
            public int ParticleSystems;
            public int LightsRealtime;
            public int LightsMixed;
            public int LightsCastingShadows;
            public List<MeshStat> TopMeshes = new List<MeshStat>();

            public bool MissingStaticFlags =>
                MeshRenderersActive > StaticFlagCheckMin && StaticFlagged == 0;

            public string WarningText()
            {
                var parts = new List<string>();
                if (TrisEffective > TrisWarn)
                {
                    parts.Add("1M+ üçgen");
                }

                if (MeshRenderersEffective > RenderersWarn)
                {
                    parts.Add("1500+ renderer");
                }

                if (Terrains > TerrainsWarn)
                {
                    parts.Add("çoklu terrain");
                }

                if (MissingStaticFlags)
                {
                    parts.Add("static flag yok");
                }

                return string.Join(" · ", parts);
            }
        }
    }
}
