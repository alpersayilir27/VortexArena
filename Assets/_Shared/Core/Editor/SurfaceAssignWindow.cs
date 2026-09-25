using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core.Combat;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// <c>Tools &gt; VortexArena &gt; Arena &gt; Yüzey Atama</c> — shows which surface the selected
    /// objects resolve to (and WHY), then binds them by hand: a <see cref="SurfaceTag"/> on the object
    /// or the material into a <see cref="SurfaceDefinition"/>.
    ///
    /// <para>Assignment is deliberately MANUAL: a guessed material→surface binding is invisible until
    /// someone shoots that wall, so nothing here is applied automatically.</para>
    ///
    /// <para><b>NO modal dialogs:</b> <c>EditorUtility.DisplayDialog</c> locks the main thread and
    /// breaks Unity CLI verification. Feedback is an in-window <c>HelpBox</c>.</para>
    /// </summary>
    public class SurfaceAssignWindow : EditorWindow
    {
        private const string MenuPath = "Tools/VortexArena/Arena/Yüzey Atama";

        /// <summary>Private field names on the library asset, read through SerializedObject — the
        /// tool must not force a runtime API open just to list definitions.</summary>
        private const string DefinitionsField = "definitions";
        private const string DefaultSurfaceField = "defaultSurface";
        private const string MaterialsField = "materials";
        private const string TagSurfaceField = "surface";

        private sealed class ObjectRow
        {
            public GameObject Go;
            public string Surface;
            public string Reason;
            public bool HasCollider;
            public SurfaceTag Tag;
        }

        private sealed class MaterialRow
        {
            public Material Material;
            public SurfaceDefinition Bound;
            public bool OnMultiMaterialRenderer;
        }

        private SurfaceLibrary library;
        private SurfaceDefinition defaultSurface;
        private readonly List<SurfaceDefinition> definitions = new List<SurfaceDefinition>();
        private readonly Dictionary<Material, SurfaceDefinition> map = new Dictionary<Material, SurfaceDefinition>();

        private readonly List<ObjectRow> objects = new List<ObjectRow>();
        private readonly List<MaterialRow> materials = new List<MaterialRow>();

        private string[] surfaceLabels = new string[0];
        private int surfaceIndex;
        private Vector2 scroll;
        private string message;

        [MenuItem(MenuPath, false, 6)]
        private static void Open()
        {
            var window = GetWindow<SurfaceAssignWindow>(false, "Yüzey Atama", true);
            window.minSize = new Vector2(520f, 380f);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Yüzey Atama");
            Selection.selectionChanged += HandleSelectionChanged;
            ReloadLibrary();
            Analyze();
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= HandleSelectionChanged;
        }

        private void HandleSelectionChanged()
        {
            Analyze();
            Repaint();
        }

        // ------------------------------------------------------------------ library

        private void ReloadLibrary()
        {
            definitions.Clear();
            map.Clear();
            defaultSurface = null;
            library = null;

            string[] guids = AssetDatabase.FindAssets("t:" + nameof(SurfaceLibrary));
            if (guids.Length > 0)
            {
                library = AssetDatabase.LoadAssetAtPath<SurfaceLibrary>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            if (library == null)
            {
                surfaceLabels = new string[0];
                return;
            }

            var so = new SerializedObject(library);
            defaultSurface = so.FindProperty(DefaultSurfaceField).objectReferenceValue as SurfaceDefinition;

            SerializedProperty list = so.FindProperty(DefinitionsField);
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue is SurfaceDefinition definition)
                {
                    definitions.Add(definition);
                }
            }

            // Same "first binding wins" rule as SurfaceLibrary.Resolve, so the preview cannot
            // disagree with the game.
            for (int i = 0; i < definitions.Count; i++)
            {
                Material[] bound = definitions[i].Materials;
                if (bound == null)
                {
                    continue;
                }

                for (int m = 0; m < bound.Length; m++)
                {
                    if (bound[m] != null && !map.ContainsKey(bound[m]))
                    {
                        map.Add(bound[m], definitions[i]);
                    }
                }
            }

            surfaceLabels = new string[definitions.Count];
            for (int i = 0; i < definitions.Count; i++)
            {
                surfaceLabels[i] = definitions[i].SurfaceId;
            }

            surfaceIndex = Mathf.Clamp(surfaceIndex, 0, Mathf.Max(0, definitions.Count - 1));
        }

        private SurfaceDefinition Chosen =>
            surfaceIndex >= 0 && surfaceIndex < definitions.Count ? definitions[surfaceIndex] : null;

        // ------------------------------------------------------------------ analysis

        private void Analyze()
        {
            objects.Clear();
            materials.Clear();

            GameObject[] selection = Selection.gameObjects;
            for (int i = 0; i < selection.Length; i++)
            {
                GameObject go = selection[i];
                if (go == null)
                {
                    continue;
                }

                objects.Add(BuildRow(go));
                CollectMaterials(go);
            }
        }

        private ObjectRow BuildRow(GameObject go)
        {
            var row = new ObjectRow
            {
                Go = go,
                HasCollider = go.GetComponentInChildren<Collider>(true) != null,
                Tag = go.GetComponentInParent<SurfaceTag>(),
            };

            if (row.Tag != null && row.Tag.Surface != null)
            {
                row.Surface = row.Tag.Surface.SurfaceId;
                row.Reason = "etiket";
                return row;
            }

            Material material = FirstMaterial(go);
            if (material != null && map.TryGetValue(material, out SurfaceDefinition mapped))
            {
                row.Surface = mapped.SurfaceId;
                row.Reason = "materyal: " + material.name;
                return row;
            }

            row.Surface = defaultSurface != null ? defaultSurface.SurfaceId : "(tanımsız)";
            row.Reason = "varsayılan";
            return row;
        }

        /// <summary>Mirrors <c>SurfaceLibrary.FindMaterial</c>: self → child → parent, first material
        /// only. A different order here would preview a surface the game never picks.</summary>
        private static Material FirstMaterial(GameObject go)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null)
            {
                renderer = go.GetComponentInChildren<Renderer>(true);
            }

            if (renderer == null)
            {
                renderer = go.GetComponentInParent<Renderer>();
            }

            return renderer != null ? renderer.sharedMaterial : null;
        }

        private void CollectMaterials(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                Material[] shared = renderers[r].sharedMaterials;
                bool multi = shared.Length > 1;

                for (int m = 0; m < shared.Length; m++)
                {
                    Material material = shared[m];
                    if (material == null)
                    {
                        continue;
                    }

                    MaterialRow existing = Find(material);
                    if (existing != null)
                    {
                        existing.OnMultiMaterialRenderer |= multi;
                        continue;
                    }

                    map.TryGetValue(material, out SurfaceDefinition bound);
                    materials.Add(new MaterialRow
                    {
                        Material = material,
                        Bound = bound,
                        OnMultiMaterialRenderer = multi,
                    });
                }
            }
        }

        private MaterialRow Find(Material material)
        {
            for (int i = 0; i < materials.Count; i++)
            {
                if (materials[i].Material == material)
                {
                    return materials[i];
                }
            }

            return null;
        }

        // ---------------------------------------------------------------------- gui

        private void OnGUI()
        {
            if (library == null)
            {
                EditorGUILayout.HelpBox(
                    "SurfaceLibrary asset'i bulunamadı (Assets/_Shared/Data/Resources/SurfaceLibrary.asset).",
                    MessageType.Error);
                if (GUILayout.Button("Yeniden tara"))
                {
                    ReloadLibrary();
                    Analyze();
                }

                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                surfaceIndex = EditorGUILayout.Popup(
                    new GUIContent("Yüzey", "Atama düğmelerinin kullanacağı yüzey tanımı."),
                    surfaceIndex, surfaceLabels);

                if (GUILayout.Button(new GUIContent("Yenile", "Kütüphaneyi ve seçimi yeniden oku."),
                        GUILayout.Width(70f)))
                {
                    ReloadLibrary();
                    Analyze();
                }
            }

            EditorGUILayout.Space(4f);

            if (objects.Count == 0)
            {
                EditorGUILayout.HelpBox("Sahnede bir obje seç.", MessageType.Info);
                return;
            }

            DrawTagButtons();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawObjects();
            EditorGUILayout.Space(6f);
            DrawMaterials();
            EditorGUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(message))
            {
                EditorGUILayout.HelpBox(message, MessageType.Info);
            }
        }

        private void DrawTagButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(Chosen == null))
            {
                if (GUILayout.Button(new GUIContent("Seçili objelere etiket koy",
                        "Materyalden bağımsız, yalnız bu objeler için geçerli override.")))
                {
                    ApplyTag();
                }
            }

            if (GUILayout.Button(new GUIContent("Etiketi kaldır", "Çözüm yeniden materyale düşer.")))
            {
                RemoveTag();
            }
        }

        private void DrawObjects()
        {
            EditorGUILayout.LabelField($"Seçili obje ({objects.Count})", EditorStyles.boldLabel);

            for (int i = 0; i < objects.Count; i++)
            {
                ObjectRow row = objects[i];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(row.Go.name, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"Yüzey: {row.Surface}  ({row.Reason})");

                    if (!row.HasCollider)
                    {
                        EditorGUILayout.HelpBox(
                            "Collider yok — mermi bu objenin içinden geçer, hiçbir efekt çıkmaz.",
                            MessageType.Warning);
                    }
                }
            }
        }

        private void DrawMaterials()
        {
            EditorGUILayout.LabelField($"Materyal ({materials.Count})", EditorStyles.boldLabel);

            for (int i = 0; i < materials.Count; i++)
            {
                MaterialRow row = materials[i];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.ObjectField(row.Material, typeof(Material), false);
                    }

                    EditorGUILayout.LabelField(row.Bound != null
                        ? "Bağlı yüzey: " + row.Bound.SurfaceId
                        : "Bağlı yüzey: yok (varsayılana düşer)");

                    if (row.OnMultiMaterialRenderer)
                    {
                        EditorGUILayout.HelpBox(
                            "Çok materyalli renderer — yalnız ilk materyal sayılır. Gerekirse objeyi böl " +
                            "ya da etiket koy.",
                            MessageType.Warning);
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(Chosen == null))
                        {
                            if (GUILayout.Button(new GUIContent(
                                    $"Bu materyali '{(Chosen != null ? Chosen.SurfaceId : "-")}' yüzeyine bağla",
                                    "Materyali kullanan HER arenayı etkiler.")))
                            {
                                BindMaterial(row.Material, Chosen);
                            }
                        }

                        using (new EditorGUI.DisabledScope(row.Bound == null))
                        {
                            if (GUILayout.Button(new GUIContent("Bağı kaldır", "Materyal varsayılana düşer."),
                                    GUILayout.Width(100f)))
                            {
                                BindMaterial(row.Material, null);
                            }
                        }

                        if (GUILayout.Button(new GUIContent("Kullanan objeleri seç",
                                "Açık sahnelerde bu materyali kullanan renderer'ları seçer."),
                                GUILayout.Width(160f)))
                        {
                            SelectUsers(row.Material);
                        }
                    }
                }
            }
        }

        // ------------------------------------------------------------------- actions

        private void ApplyTag()
        {
            SurfaceDefinition surface = Chosen;
            if (surface == null)
            {
                return;
            }

            GameObject[] selection = Selection.gameObjects;
            for (int i = 0; i < selection.Length; i++)
            {
                GameObject go = selection[i];
                if (go == null)
                {
                    continue;
                }

                // Own tag only: GetComponentInParent would rewrite a parent's tag instead of tagging
                // the object the operator picked.
                var tag = go.GetComponent<SurfaceTag>();
                if (tag == null)
                {
                    tag = Undo.AddComponent<SurfaceTag>(go);
                }

                var so = new SerializedObject(tag);
                so.FindProperty(TagSurfaceField).objectReferenceValue = surface;
                so.ApplyModifiedProperties();

                EditorSceneManager.MarkSceneDirty(go.scene);
            }

            message = $"{selection.Length} objeye '{surface.SurfaceId}' etiketi kondu.";
            Analyze();
        }

        private void RemoveTag()
        {
            GameObject[] selection = Selection.gameObjects;
            int removed = 0;

            for (int i = 0; i < selection.Length; i++)
            {
                GameObject go = selection[i];
                if (go == null)
                {
                    continue;
                }

                var tag = go.GetComponent<SurfaceTag>();
                if (tag == null)
                {
                    continue;
                }

                Scene scene = go.scene;
                Undo.DestroyObjectImmediate(tag);
                EditorSceneManager.MarkSceneDirty(scene);
                removed++;
            }

            message = $"{removed} etiket kaldırıldı.";
            Analyze();
        }

        /// <summary>Binds the material to one definition and strips it from the others: a material
        /// listed twice makes the effect depend on list order (SurfaceLibrary warns and keeps the
        /// first). <c>target</c> null = unbind only.</summary>
        private void BindMaterial(Material material, SurfaceDefinition target)
        {
            if (material == null)
            {
                return;
            }

            for (int i = 0; i < definitions.Count; i++)
            {
                SurfaceDefinition definition = definitions[i];
                bool keep = definition == target;

                Undo.RecordObject(definition, "Yüzey materyal bağı");
                var so = new SerializedObject(definition);
                SerializedProperty list = so.FindProperty(MaterialsField);

                bool present = false;
                for (int m = list.arraySize - 1; m >= 0; m--)
                {
                    if (list.GetArrayElementAtIndex(m).objectReferenceValue != material)
                    {
                        continue;
                    }

                    if (keep && !present)
                    {
                        present = true;
                        continue;
                    }

                    // Object-reference arrays: the first delete only NULLS the element, so it is
                    // nulled first and then removed.
                    list.GetArrayElementAtIndex(m).objectReferenceValue = null;
                    list.DeleteArrayElementAtIndex(m);
                }

                if (keep && !present)
                {
                    list.InsertArrayElementAtIndex(list.arraySize);
                    list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = material;
                }

                // WithoutUndo: Undo.RecordObject above already holds the entry; applying with undo
                // would need two ctrl+Z per definition.
                if (so.ApplyModifiedPropertiesWithoutUndo())
                {
                    EditorUtility.SetDirty(definition);
                }
            }

            message = target != null
                ? $"'{material.name}' → '{target.SurfaceId}'."
                : $"'{material.name}' bağı kaldırıldı.";

            ReloadLibrary();
            Analyze();
        }

        /// <summary>Selects every renderer in the OPEN scenes using the material — the operator has to
        /// see where a binding will land before making it.</summary>
        private static void SelectUsers(Material material)
        {
            var found = new List<Object>();

            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded)
                {
                    continue;
                }

                GameObject[] roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++)
                {
                    Renderer[] renderers = roots[r].GetComponentsInChildren<Renderer>(true);
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        Material[] shared = renderers[i].sharedMaterials;
                        for (int m = 0; m < shared.Length; m++)
                        {
                            if (shared[m] == material)
                            {
                                found.Add(renderers[i].gameObject);
                                break;
                            }
                        }
                    }
                }
            }

            Selection.objects = found.ToArray();
        }
    }
}
