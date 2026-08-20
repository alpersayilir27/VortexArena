using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.ProBuilder.MeshOperations;
using VortexArena.Core.Arena;

namespace VortexArena.Core.Editor
{
    /// <summary>Turns a venue's dimensions file (<see cref="ArenaDimensions"/>) into the scene's
    /// dimension mesh: one <c>Plane</c> polygon, a prism per column and two calibration anchors
    /// (<c>anchor_a</c> / <c>anchor_b</c>) under the <c>&lt;Venue&gt;_DimensionMesh</c> root.</summary>
    /// <remarks>
    /// The mesh is not played geometry but it DOES ship: the <c>anchor_a</c>/<c>anchor_b</c> cubes
    /// are the scene's calibration anchors and <see cref="ArenaCalibrator"/> looks for them at
    /// runtime. Only the anchors are drawn in game; <see cref="ArenaDimensionMesh"/> hides the plane
    /// and columns in <c>Awake</c>. Arena art is built on top of the mesh; no wall is generated
    /// (arena walls belong to the environment).
    /// <para>The mesh is built UNDER the scene's <see cref="ArenaBoundary"/> at local zero, scale 1,
    /// so fitting the arena into an existing environment means moving/rotating ONE object (the
    /// boundary instance) with the mesh and its anchors following. The two must coincide anyway
    /// (the boundary reads the same file). Without a boundary the mesh goes to the scene root at the
    /// world origin, unrotated.</para>
    /// <para>⚠️ The mesh's scale is never changed, and under a rotated root the size is NOT read
    /// from the world-axis selection box: a 12×12 plane under a root rotated 48.72° measures
    /// <c>12 × (cos θ + sin θ)</c> = 16.93 in that box and the tool looks like it broke the scale.
    /// The dimensions file is where the size is read; reading back references the mesh's OWN root,
    /// so a moved/rotated mesh still converts correctly.</para>
    /// <para>⚠️ Idempotent: an existing mesh of the same venue is deleted and regenerated, no second
    /// copy accumulates. Nothing outside the mesh is touched.</para>
    /// <para>⚠️ No <c>EditorUtility.DisplayDialog</c>: a modal dialog locks Unity's main thread and
    /// times out CLI invocations. The result is reported with <c>Debug.Log</c>.</para>
    /// </remarks>
    public class DimensionMeshBuilder : EditorWindow
    {
        private const string VenuesRoot = "Assets/Arenas/Venues";
        private const string SharedMaterialPath = "Assets/Materials/M_Mekan.mat";

        // Anchors are painted with the team materials (A red, B blue) to avoid new assets: telling
        // the two points apart AT A GLANCE matters more than the A→B order itself, the operator
        // confirms from the lit marker.
        private const string MarkAMaterialPath = "Assets/Materials/M_TeamRed.mat";
        private const string MarkBMaterialPath = "Assets/Materials/M_TeamBlue.mat";

        /// <summary>Edge length of the calibration anchor cube (m).</summary>
        private const float MarkSize = 0.12f;

        [SerializeField] private TextAsset dimensionsJson;

        /// <summary>Build result, used by the caller (window / another tool) to report.</summary>
        public sealed class Result
        {
            /// <summary>Root of the generated mesh; null on failure.</summary>
            public GameObject Root;

            /// <summary>Venue name (derived from the path).</summary>
            public string VenueName;

            /// <summary>Generated column count.</summary>
            public int ColumnCount;

            /// <summary>Vertex count of the plane ring.</summary>
            public int PlanePointCount;

            /// <summary>XZ size of the generated plane (m) = bounding box of the file's ring, which
            /// is also its size in the mesh's LOCAL space (the mesh is unrotated in its own root; a
            /// rotated root reads larger in the world-axis box — see class summary).</summary>
            public Vector2 PlaneLocalSize;

            /// <summary>Whether calibration anchors were generated (file had the points).</summary>
            public bool HasCalibration;

            /// <summary>Whether the build happened.</summary>
            public bool Success;

            /// <summary>Reason on failure.</summary>
            public string Error;

            /// <summary>Recovered situations that still need attention.</summary>
            public readonly List<string> Warnings = new List<string>();
        }

        // --------------------------------------------------------------- window

        [MenuItem("Tools/VortexArena/Arena/JSON'dan DimensionMesh Üret", false, 2)]
        private static void Open()
        {
            var window = GetWindow<DimensionMeshBuilder>(true, "Boyut Maketi Üret", true);
            window.minSize = new Vector2(430f, 210f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Kaynak", EditorStyles.boldLabel);
            dimensionsJson = (TextAsset)EditorGUILayout.ObjectField(
                new GUIContent("Boyut dosyası (JSON)",
                    "Mekanın ArenaDimensions JSON'u: taban halkası + kolonlar (metre, arena yerel XZ)."),
                dimensionsJson, typeof(TextAsset), false);

            if (dimensionsJson != null)
            {
                EditorGUILayout.HelpBox(
                    "Mekan: " + ResolveVenueName(AssetDatabase.GetAssetPath(dimensionsJson)) +
                    "\nKök obje: " + ArenaDimensionMesh.RootNameFor(
                        ResolveVenueName(AssetDatabase.GetAssetPath(dimensionsJson))),
                    MessageType.None);
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Maket taban + kolonlar + kalibrasyon işaretçileri (anchor_a kırmızı, anchor_b " +
                "mavi) üretir; duvar üretmez. Build'e GİRER çünkü işaretçileri kalibrasyon " +
                "çalışma anında kullanır — ama taban/kolon görseli oyunda çizilmez. Köşeleri " +
                "ProBuilder ile, işaretçileri sürükleyerek düzeltip 'DimensionMesh'i JSON'a " +
                "Çevir' ile aynı dosyaya geri yazabilirsin.\n\n" +
                "Sahnede ArenaBoundary varsa maket ONUN ALTINA, yerel sıfırda kurulur: arenayı " +
                "bir environment'ın üstüne oturtmak için yalnız VA_ArenaBoundary örneğini " +
                "taşırsın/döndürürsün, maket ve kalibrasyon işaretçileri onu izler. Muhafaza " +
                "yoksa sahne köküne, dünya orijininde ve dönüşsüz kurulur. Geri okuma maketin " +
                "kendi kökünü referans aldığı için taşınmış/döndürülmüş maketten de doğru çevirir.",
                MessageType.Info);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(dimensionsJson == null))
            {
                if (GUILayout.Button("Üret", GUILayout.Height(28f)))
                {
                    Result result = Build(dimensionsJson);
                    if (result.Success)
                    {
                        Debug.Log(
                            $"[DimensionMesh] '{result.VenueName}' maketi üretildi: " +
                            $"{result.PlanePointCount} köşeli taban + {result.ColumnCount} kolon" +
                            (result.HasCalibration ? " + A/B kalibrasyon işaretçileri" : string.Empty) +
                            $". Taban ölçüsü: {result.PlaneLocalSize.x:0.###} × " +
                            $"{result.PlaneLocalSize.y:0.###} m.",
                            result.Root);
                        Selection.activeGameObject = result.Root;
                    }
                    else
                    {
                        Debug.LogError("[DimensionMesh] Üretilemedi: " + result.Error);
                    }

                    for (int i = 0; i < result.Warnings.Count; i++)
                    {
                        Debug.LogWarning("[DimensionMesh] " + result.Warnings[i]);
                    }
                }
            }
        }

        // ---------------------------------------------------------------- build

        /// <summary>Builds the mesh from the dimensions file. Throws NO exception — failures come
        /// back as <see cref="Result.Success"/> <c>false</c>.</summary>
        public static Result Build(TextAsset json)
        {
            var result = new Result();

            if (json == null)
            {
                result.Error = "Boyut dosyası verilmedi.";
                return result;
            }

            string assetPath = AssetDatabase.GetAssetPath(json);
            result.VenueName = ResolveVenueName(assetPath);

            ArenaDimensions plan = ArenaDimensions.FromTextAsset(json, out string parseError);
            if (plan == null)
            {
                result.Error = $"Boyut dosyası okunamadı ('{json.name}'): {parseError}";
                return result;
            }

            Material material = ResolveMaterial();
            if (material == null)
            {
                result.Error = "Materyal çözülemedi (URP/Lit shader yok?).";
                return result;
            }

            if (Polygon2D.IsSelfIntersecting(plan.plane))
            {
                result.Warnings.Add(
                    "Taban halkası KENDİ KENDİNİ KESİYOR — köşeler büyük ihtimalle yanlış sırada " +
                    "yazılmış. Üretilen şekil ölçülen odayı temsil etmeyebilir.");
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("VortexArena Boyut Maketi");

            // ------------------------------------------------------------- root
            // Built UNDER the boundary at local zero (reason in the class summary): the boundary
            // instance is the ONE object carrying the arena's placement, mesh and anchors follow it.
            // Without a boundary the fallback is the scene root.
            DestroyExisting(result.VenueName);

            Transform anchorParent = FindBoundaryParent();

            var root = new GameObject(ArenaDimensionMesh.RootNameFor(result.VenueName));
            Undo.RegisterCreatedObjectUndo(root, "Boyut Maketi Kökü");
            root.transform.SetParent(anchorParent, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            if (anchorParent == null)
            {
                result.Warnings.Add(
                    "Sahnede ArenaBoundary yok — maket sahne köküne, dünya orijininde kuruldu. " +
                    "Muhafazayı 'Template Temellerini Yükle' ile kurup maketi yeniden üretirsen " +
                    "arenayı tek objeyi taşıyarak yerleştirebilirsin.");
            }

            // The mesh MUST ship (the anchors live under it). The tag is reset explicitly: reusing
            // an 'EditorOnly' tagged root from an old scene would silently drop the mesh from the
            // build and leave the arena unalignable on site.
            root.tag = "Untagged";

            var marker = Undo.AddComponent<ArenaDimensionMesh>(root);
            marker.Configure(result.VenueName, json, plan.defaultColumnHeight);
            EditorUtility.SetDirty(marker);

            result.Root = root;

            // ----------------------------------------------------------- plane
            // The plane pivot equals the root: ring coordinates are already in the plan's own space
            // and the plane is a single piece, nothing to offset.
            GameObject plane = CreatePolygon(
                ArenaDimensionMesh.PlaneName,
                plan.plane,
                Vector2.zero,
                0f,
                material,
                root.transform,
                result.Warnings);

            if (plane == null)
            {
                // No half-built mesh is left behind: a root without a plane fails on read back and
                // just sits in the scene as a useless skeleton.
                Undo.DestroyObjectImmediate(root);
                result.Root = null;
                result.Error = "Taban çokgeni üretilemedi (ProBuilder üçgenlemesi düştü) — " +
                               "köşe sırasını ve kendi kendini kesen kenarları gözden geçir.";
                Undo.CollapseUndoOperations(undoGroup);
                return result;
            }

            var planeMarker = plane.AddComponent<DimensionPolygon>();
            planeMarker.SetKind(DimensionPolygon.PolygonKind.Plane);
            EditorUtility.SetDirty(planeMarker);
            result.PlanePointCount = plan.plane.Length;

            Rect planeBounds = Polygon2D.Bounds(plan.plane);
            result.PlaneLocalSize = new Vector2(planeBounds.width, planeBounds.height);

            // --------------------------------------------------------- columns
            ArenaDimensions.Column[] columns = plan.columns;
            if (columns != null && columns.Length > 0)
            {
                var group = new GameObject(ArenaDimensionMesh.ColumnsGroupName);
                Undo.RegisterCreatedObjectUndo(group, "Kolon Grubu");
                group.transform.SetParent(root.transform, false);

                for (int i = 0; i < columns.Length; i++)
                {
                    ArenaDimensions.Column column = columns[i];
                    string columnName = string.IsNullOrWhiteSpace(column.name) ? $"Kolon_{i:00}" : column.name;

                    if (Polygon2D.IsSelfIntersecting(column.points))
                    {
                        result.Warnings.Add($"'{columnName}' halkası kendi kendini kesiyor — köşe sırasını gözden geçir.");
                    }

                    // Pivot at the centroid so dragging a column with the Move tool feels natural.
                    Vector2 pivot = Polygon2D.Centroid(column.points);
                    GameObject prism = CreatePolygon(
                        columnName,
                        column.points,
                        pivot,
                        Mathf.Max(0.01f, plan.HeightOf(column)),
                        material,
                        group.transform,
                        result.Warnings);

                    if (prism == null)
                    {
                        // The plane is mandatory, a column is not: the rest stays usable.
                        result.Warnings.Add($"'{columnName}' üretilemedi — atlandı.");
                        continue;
                    }

                    var prismMarker = prism.AddComponent<DimensionPolygon>();
                    prismMarker.SetKind(DimensionPolygon.PolygonKind.Column);
                    EditorUtility.SetDirty(prismMarker);
                    result.ColumnCount++;
                }
            }

            // --------------------------------------------- calibration anchors
            // The floor marks are a measurement too, built as geometry so they can be read back into
            // the file. A file without points generates nothing — a made up pair would claim the
            // tape on site was laid there.
            if (plan.HasCalibration)
            {
                CreateMark(ArenaCalibrator.AnchorAName, DimensionAnchor.AnchorKind.A,
                    plan.calibration.a, MarkAMaterialPath, material, root.transform);
                CreateMark(ArenaCalibrator.AnchorBName, DimensionAnchor.AnchorKind.B,
                    plan.calibration.b, MarkBMaterialPath, material, root.transform);
                result.HasCalibration = true;
            }
            else
            {
                result.Warnings.Add(
                    "Dosyada kalibrasyon noktası yok ('calibration' alanı boş ya da iki nokta " +
                    $"{ArenaDimensions.MinCalibrationSpan:0.##} m'den yakın) — işaretçiler " +
                    "üretilmedi. Zemin bandının yerini bu alana yaz, yoksa arena sahada elle " +
                    "hizalanan işaretçilere kalır.");
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(root.scene);

            result.Success = true;
            return result;
        }

        /// <summary>Creates a ProBuilder polygon from a ring under <paramref name="parent"/>.</summary>
        /// <remarks>
        /// <paramref name="pivot"/> is subtracted from the ring coordinates and written as the
        /// object's local position, so dragging the object moves the polygon as a whole.
        /// <para>⚠️ With <paramref name="extrude"/>, the direction ProBuilder extrudes depends on the
        /// ring's winding, so the result is measured and the object is offset to sit with its BOTTOM
        /// face on the parent's y=0 plane: read back uses the "lowest horizontal face" rule, and a
        /// prism floating or sunk below the floor would make the mesh unreadable.</para>
        /// <para>⚠️ ProBuilder's result IS checked: on a failed triangulation
        /// <c>CreateShapeFromPolygon</c> throws nothing and leaves an EMPTY mesh — an object with the
        /// right name and no geometry, noticed only days later. A failed polygon is therefore
        /// deleted and <c>null</c> returned.</para>
        /// </remarks>
        /// <returns>The created object; <c>null</c> when triangulation failed.</returns>
        private static GameObject CreatePolygon(
            string name,
            Vector2[] ring,
            Vector2 pivot,
            float extrude,
            Material material,
            Transform parent,
            List<string> warnings)
        {
            var points = new List<Vector3>(ring.Length);
            for (int i = 0; i < ring.Length; i++)
            {
                points.Add(new Vector3(ring[i].x - pivot.x, 0f, ring[i].y - pivot.y));
            }

            ProBuilderMesh mesh = ProBuilderMesh.Create();
            mesh.gameObject.name = name;

            UnityEngine.ProBuilder.ActionResult shape = mesh.CreateShapeFromPolygon(points, extrude, false);
            if (!shape || mesh.faceCount == 0)
            {
                warnings.Add($"'{name}' çokgeni üretilemedi (ProBuilder: {shape.notification}).");
                Object.DestroyImmediate(mesh.gameObject);
                return null;
            }

            mesh.SetMaterial(mesh.faces, material);
            mesh.ToMesh();
            mesh.Refresh();

            Undo.RegisterCreatedObjectUndo(mesh.gameObject, "Boyut Çokgeni");
            mesh.transform.SetParent(parent, false);
            mesh.transform.localRotation = Quaternion.identity;
            mesh.transform.localScale = Vector3.one;

            float bottom = 0f;
            var filter = mesh.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                bottom = filter.sharedMesh.bounds.min.y;
            }

            mesh.transform.localPosition = new Vector3(pivot.x, -bottom, pivot.y);
            return mesh.gameObject;
        }

        /// <summary>Creates one calibration anchor: a small cube standing at the plan point.</summary>
        /// <remarks>
        /// ⚠️ The cube's CENTRE sits on the point, not its base: read back takes the transform as-is,
        /// so the Inspector position must equal the file's point. Half of it below the plane is
        /// deliberate — the point is on the floor.
        /// <para>⚠️ The same contract holds at runtime: this cube IS the scene's calibration anchor
        /// and <c>ArenaCalibrator.PlaceMarkerAtFloor</c> places it directly on the floor point. One
        /// contract (transform position = floor point) on both sides; split in two, the mesh cube
        /// and the aligned anchor would never coincide.</para>
        /// </remarks>
        private static void CreateMark(
            string name,
            DimensionAnchor.AnchorKind kind,
            Vector2 point,
            string materialPath,
            Material fallbackMaterial,
            Transform parent)
        {
            var mark = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mark.name = name;
            Undo.RegisterCreatedObjectUndo(mark, "Kalibrasyon İşaretçisi");

            // Colliders are not the mesh's job: free-roam has no physical collision anyway, so only
            // a box catching raycasts (weapon aim included) would remain.
            var collider = mark.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }

            mark.transform.SetParent(parent, false);
            mark.transform.localPosition = new Vector3(point.x, 0f, point.y);
            mark.transform.localRotation = Quaternion.identity;
            mark.transform.localScale = Vector3.one * MarkSize;

            var renderer = mark.GetComponent<Renderer>();
            if (renderer != null)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                renderer.sharedMaterial = material != null ? material : fallbackMaterial;
            }

            var anchor = mark.AddComponent<DimensionAnchor>();
            anchor.SetKind(kind);
            EditorUtility.SetDirty(anchor);
        }

        /// <summary>Boundary transform to parent the mesh to; <c>null</c> without an
        /// <see cref="ArenaBoundary"/> (mesh goes to the scene root).</summary>
        /// <remarks>⚠️ The boundary is mandatory and unique per arena scene; a second one already
        /// means two different measurements. Even so this falls back to the first and proceeds:
        /// refusing to build would dead-end a scene halfway through setup.</remarks>
        private static Transform FindBoundaryParent()
        {
            ArenaBoundary boundary =
                Object.FindFirstObjectByType<ArenaBoundary>(FindObjectsInactive.Include);
            return boundary != null ? boundary.transform : null;
        }

        /// <summary>Deletes a previously generated mesh of the same venue (idempotency).</summary>
        /// <remarks>Matched by VENUE NAME only, never by position: the mesh may have been moved or
        /// rotated by hand, and a position based match would miss it and leave a second copy.
        /// Another venue's mesh is never touched.</remarks>
        private static void DestroyExisting(string venueName)
        {
            ArenaDimensionMesh[] existing =
                Object.FindObjectsByType<ArenaDimensionMesh>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < existing.Length; i++)
            {
                ArenaDimensionMesh candidate = existing[i];
                if (candidate == null)
                {
                    continue;
                }

                if (string.Equals(candidate.VenueName, venueName, System.StringComparison.OrdinalIgnoreCase))
                {
                    Undo.DestroyObjectImmediate(candidate.gameObject);
                }
            }
        }

        // -------------------------------------------------------------- helpers

        /// <summary>Derives the venue name from the dimensions file path:
        /// <c>Assets/Arenas/Venues/&lt;Venue&gt;/…</c> → <c>&lt;Venue&gt;</c>.</summary>
        /// <remarks>⚠️ The venue comes from the path, not from inside the file — same rule as
        /// <c>MapDefinition</c>, to avoid a second, forgettable source of truth. Outside
        /// <c>Venues/</c> it falls back to the first part of the file name.</remarks>
        public static string ResolveVenueName(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return "Arena";
            }

            string normalized = assetPath.Replace('\\', '/');
            if (normalized.StartsWith(VenuesRoot + "/", System.StringComparison.OrdinalIgnoreCase))
            {
                string tail = normalized.Substring(VenuesRoot.Length + 1);
                int slash = tail.IndexOf('/');
                if (slash > 0)
                {
                    return tail.Substring(0, slash);
                }
            }

            string fileName = Path.GetFileNameWithoutExtension(normalized);
            int suffix = fileName.IndexOf("_dimensions", System.StringComparison.OrdinalIgnoreCase);
            return suffix > 0 ? fileName.Substring(0, suffix) : fileName;
        }

        /// <summary>Shared venue material: <c>Assets/Materials/M_Mekan.mat</c> when it exists,
        /// otherwise created from URP/Lit and written there. Null when the project is not URP (the
        /// caller reports the error).</summary>
        public static Material ResolveMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(SharedMaterialPath);
            if (existing != null)
            {
                return existing;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("[DimensionMesh] URP/Lit shader bulunamadı — proje gerçekten URP mi?");
                return null;
            }

            string folder = Path.GetDirectoryName(SharedMaterialPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(folder) && !AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder("Assets", "Materials");
            }

            var created = new Material(shader) { color = new Color(0.75f, 0.75f, 0.72f) };
            AssetDatabase.CreateAsset(created, SharedMaterialPath);
            AssetDatabase.SaveAssets();
            return created;
        }
    }
}
