using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VortexArena.Core.Arena;

namespace VortexArena.Core.Editor
{
    /// <summary>Reads the scene's dimension mesh and writes OVER
    /// <see cref="ArenaDimensionMesh.SourceJson"/>.</summary>
    /// <remarks>
    /// Purpose: when the measurement was wrong the corners are fixed in the scene with ProBuilder,
    /// and this step writes the real measurement back to the single source of truth. The target file
    /// is not asked for — it comes from the mesh root, so it returns to the file it was generated
    /// from.
    /// <para>Calibration points travel too: the <c>anchor_a</c>/<c>anchor_b</c> markers are dragged
    /// into place and written back to the file's <c>calibration</c> field — the floor marks are a
    /// measurement as well. Without markers the file's values are kept, never cleared.</para>
    /// <para>✔ The round trip is lossless and symmetric: one ring in JSON → one mesh → one ring. The
    /// schema has no union, so no field comes back different; converting an untouched mesh leaves
    /// the file identical up to float rounding.</para>
    /// <para>⚠️ The footprint is read from the BOTTOM face: horizontal faces are grouped by Y level
    /// and the lowest group wins (one group on a flat plane, bottom + top on a prism). If a column's
    /// top face was edited differently, the bottom wins — the boundary cares about the footprint on
    /// the floor.</para>
    /// <para>⚠️ Edges are keyed by POSITION, not vertex index: ProBuilder splits vertices per face
    /// for hard normals, so index based boundary detection would see every edge of every face as
    /// "traversed once" and return the whole mesh as boundary.</para>
    /// </remarks>
    public static class DimensionMeshReader
    {
        /// <summary>Resolution for keying vertex positions (m) — 0.1 mm.</summary>
        private const float WeldResolution = 1e-4f;

        /// <summary>|y| threshold of the face normal to count as horizontal.</summary>
        private const float HorizontalNormalThreshold = 0.9f;

        /// <summary>Tolerance for counting as the same Y level (m).</summary>
        private const float LevelTolerance = 0.01f;

        /// <summary>Collinear vertex cull threshold — cross product of two consecutive edges.</summary>
        private const float CollinearEpsilon = 1e-3f;

        [MenuItem("Tools/VortexArena/Arena/DimensionMesh'i JSON'a Çevir", false, 3)]
        private static void ConvertSelected()
        {
            ArenaDimensionMesh target = ResolveTarget(out string targetError);
            if (target == null)
            {
                Debug.LogError("[DimensionMesh] " + targetError);
                return;
            }

            var warnings = new List<string>();
            bool written = Write(target, warnings, out string error);

            for (int i = 0; i < warnings.Count; i++)
            {
                Debug.LogWarning("[DimensionMesh] " + warnings[i], target);
            }

            if (!written)
            {
                Debug.LogError("[DimensionMesh] Yazılamadı: " + error, target);
                return;
            }

            Debug.Log(
                $"[DimensionMesh] '{target.VenueName}' maketi " +
                $"'{AssetDatabase.GetAssetPath(target.SourceJson)}' dosyasına yazıldı.",
                target.SourceJson);
        }

        /// <summary>Reads the mesh and writes the source file. Throws NO exception.</summary>
        /// <remarks>⚠️ The result is parsed back with <see cref="ArenaDimensions.Parse"/> before
        /// writing; if it does not parse the file is left untouched — a broken write would leave
        /// every scene of that venue without dimensions.</remarks>
        public static bool Write(ArenaDimensionMesh target, List<string> warnings, out string error)
        {
            error = null;

            if (target == null)
            {
                error = "Maket verilmedi.";
                return false;
            }

            if (target.SourceJson == null)
            {
                error = $"'{target.name}' maketinde kaynak boyut dosyası bağlı değil (sourceJson boş).";
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(target.SourceJson);
            if (string.IsNullOrEmpty(assetPath))
            {
                error = "Kaynak boyut dosyasının asset yolu çözülemedi.";
                return false;
            }

            ArenaDimensions plan = Extract(target, warnings, out error);
            if (plan == null)
            {
                return false;
            }

            string json = plan.ToJson();

            // Read back what we just produced: a field JsonUtility silently emptied is caught here
            // and the file is never touched.
            if (ArenaDimensions.Parse(json, out string verifyError) == null)
            {
                error = "Üretilen JSON doğrulanamadı, dosyaya YAZILMADI: " + verifyError;
                return false;
            }

            try
            {
                File.WriteAllText(assetPath, json);
            }
            catch (Exception exception)
            {
                error = "Dosya yazılamadı: " + exception.Message;
                return false;
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            return true;
        }

        // ---------------------------------------------------------------- extract

        /// <summary>Converts the mesh geometry into a plan; <c>null</c> on failure.</summary>
        public static ArenaDimensions Extract(ArenaDimensionMesh target, List<string> warnings, out string error)
        {
            error = null;

            Transform root = target.transform;
            DimensionPolygon[] polygons = target.GetComponentsInChildren<DimensionPolygon>(true);

            DimensionPolygon planePolygon = null;
            var columnPolygons = new List<DimensionPolygon>();

            for (int i = 0; i < polygons.Length; i++)
            {
                if (polygons[i].Kind == DimensionPolygon.PolygonKind.Plane)
                {
                    if (planePolygon != null)
                    {
                        warnings?.Add(
                            $"Birden çok taban çokgeni var ('{planePolygon.name}', '{polygons[i].name}') — " +
                            "ilki kullanıldı. Taban TEK halkadır.");
                        continue;
                    }

                    planePolygon = polygons[i];
                }
                else
                {
                    columnPolygons.Add(polygons[i]);
                }
            }

            if (planePolygon == null)
            {
                error = $"'{target.name}' altında taban çokgeni (DimensionPolygon.Plane) yok.";
                return null;
            }

            Vector2[] planeRing = ExtractFootprint(planePolygon.transform, root, out float _, out string planeError);
            if (planeRing == null)
            {
                error = $"Taban okunamadı ('{planePolygon.name}'): {planeError}";
                return null;
            }

            if (Polygon2D.IsSelfIntersecting(planeRing))
            {
                warnings?.Add("Okunan taban halkası kendi kendini kesiyor — köşe düzenlemesini gözden geçir.");
            }

            var columns = new List<ArenaDimensions.Column>(columnPolygons.Count);
            for (int i = 0; i < columnPolygons.Count; i++)
            {
                DimensionPolygon polygon = columnPolygons[i];
                Vector2[] ring = ExtractFootprint(polygon.transform, root, out float height, out string columnError);
                if (ring == null)
                {
                    warnings?.Add($"'{polygon.name}' okunamadı ve ATLANDI: {columnError}");
                    continue;
                }

                if (height <= 0.001f)
                {
                    warnings?.Add($"'{polygon.name}' yüksekliği sıfır görünüyor — prizma düzleşmiş olabilir.");
                }

                columns.Add(new ArenaDimensions.Column
                {
                    name = polygon.name,
                    height = height,
                    points = ring
                });
            }

            return new ArenaDimensions
            {
                name = target.VenueName,
                plane = planeRing,
                columns = columns.ToArray(),
                calibration = ExtractCalibration(target, root, warnings),
                defaultColumnHeight = target.DefaultColumnHeight
            };
        }

        /// <summary>Reads the mesh's <see cref="DimensionAnchor"/> markers in root space.</summary>
        /// <remarks>⚠️ Without markers the points are NOT zeroed, the source file's values are kept:
        /// converting a mesh generated without anchors would otherwise silently erase the venue's
        /// floor mark measurement. The gap is reported as a warning.</remarks>
        private static ArenaDimensions.CalibrationMarks ExtractCalibration(
            ArenaDimensionMesh target,
            Transform root,
            List<string> warnings)
        {
            DimensionAnchor[] anchors = target.GetComponentsInChildren<DimensionAnchor>(true);

            Transform a = null;
            Transform b = null;
            for (int i = 0; i < anchors.Length; i++)
            {
                if (anchors[i].Kind == DimensionAnchor.AnchorKind.A)
                {
                    if (a == null) a = anchors[i].transform;
                    else warnings?.Add($"Birden çok A işaretçisi var ('{a.name}', '{anchors[i].name}') — ilki kullanıldı.");
                }
                else
                {
                    if (b == null) b = anchors[i].transform;
                    else warnings?.Add($"Birden çok B işaretçisi var ('{b.name}', '{anchors[i].name}') — ilki kullanıldı.");
                }
            }

            if (a == null || b == null)
            {
                warnings?.Add(
                    "Makette kalibrasyon işaretçisi (DimensionAnchor A/B) yok — dosyadaki " +
                    "'calibration' değerleri OLDUĞU GİBİ korundu. İşaretçileri üretmek için " +
                    "maketi 'JSON'dan DimensionMesh Üret' ile yeniden kur.");
                return ReadCalibrationFromSource(target);
            }

            Vector3 localA = root.InverseTransformPoint(a.position);
            Vector3 localB = root.InverseTransformPoint(b.position);

            var marks = new ArenaDimensions.CalibrationMarks
            {
                a = new Vector2(localA.x, localA.z),
                b = new Vector2(localB.x, localB.z)
            };

            if ((marks.b - marks.a).magnitude < ArenaDimensions.MinCalibrationSpan)
            {
                warnings?.Add(
                    $"Kalibrasyon noktaları birbirine çok yakın ({(marks.b - marks.a).magnitude:0.##} m < " +
                    $"{ArenaDimensions.MinCalibrationSpan:0.##} m) — bu çift yön tanımlamaz ve " +
                    "kalibratör noktaları YOK sayar.");
            }

            return marks;
        }

        /// <summary>Calibration points from the source file; empty pair when unreadable.</summary>
        private static ArenaDimensions.CalibrationMarks ReadCalibrationFromSource(ArenaDimensionMesh target)
        {
            ArenaDimensions existing = ArenaDimensions.FromTextAsset(target.SourceJson, out string _);
            return existing != null ? existing.calibration : default;
        }

        /// <summary>Extracts a mesh's footprint ring in <paramref name="root"/>'s local XZ space and
        /// its height (Y range).</summary>
        /// <remarks>Points go through world space
        /// (<c>root.InverseTransformPoint(mesh.TransformPoint(v))</c>) so dragging and rotating a
        /// column in the scene stays correct.</remarks>
        private static Vector2[] ExtractFootprint(
            Transform meshTransform,
            Transform root,
            out float height,
            out string error)
        {
            height = 0f;
            error = null;

            var filter = meshTransform.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null)
            {
                error = "MeshFilter/mesh yok.";
                return null;
            }

            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            if (vertices.Length == 0 || triangles.Length < 3)
            {
                error = "Mesh boş.";
                return null;
            }

            // 1) Move every vertex into root space.
            var local = new Vector3[vertices.Length];
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < vertices.Length; i++)
            {
                local[i] = root.InverseTransformPoint(meshTransform.TransformPoint(vertices[i]));
                minY = Mathf.Min(minY, local[i].y);
                maxY = Mathf.Max(maxY, local[i].y);
            }

            height = maxY - minY;

            // 2) Pick horizontal triangles and find the lowest level.
            float lowestLevel = float.MaxValue;
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                if (!IsHorizontal(local, triangles, t, out float level))
                {
                    continue;
                }

                lowestLevel = Mathf.Min(lowestLevel, level);
            }

            if (lowestLevel == float.MaxValue)
            {
                error = "Yatay yüz bulunamadı (mesh tümden dik mi?).";
                return null;
            }

            // 3) Collect edges of the lowest-level horizontal triangles; an edge traversed ONCE is a
            //    boundary edge. Edges are keyed by position (see class summary).
            var welded = new List<Vector2>();
            var lookup = new Dictionary<long, int>();
            var edgeCounts = new Dictionary<long, int>();
            var edgePairs = new Dictionary<long, (int A, int B)>();

            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                if (!IsHorizontal(local, triangles, t, out float level) ||
                    Mathf.Abs(level - lowestLevel) > LevelTolerance)
                {
                    continue;
                }

                int i0 = Weld(local[triangles[t]], welded, lookup);
                int i1 = Weld(local[triangles[t + 1]], welded, lookup);
                int i2 = Weld(local[triangles[t + 2]], welded, lookup);

                AddEdge(i0, i1, edgeCounts, edgePairs);
                AddEdge(i1, i2, edgeCounts, edgePairs);
                AddEdge(i2, i0, edgeCounts, edgePairs);
            }

            var adjacency = new Dictionary<int, List<int>>();
            foreach (KeyValuePair<long, int> entry in edgeCounts)
            {
                if (entry.Value != 1)
                {
                    continue; // interior edge: shared by two triangles
                }

                (int a, int b) = edgePairs[entry.Key];
                AddNeighbour(adjacency, a, b);
                AddNeighbour(adjacency, b, a);
            }

            if (adjacency.Count < Polygon2D.MinPoints)
            {
                error = $"Sınır kenarı yetersiz (bulunan köşe: {adjacency.Count}).";
                return null;
            }

            // 4) Walk the ring.
            Vector2[] ring = WalkRing(adjacency, welded);
            if (ring == null)
            {
                error = "Sınır kapalı bir halkaya yürünemedi (mesh delikli ya da parçalı olabilir).";
                return null;
            }

            Vector2[] simplified = DropCollinear(ring);
            if (!Polygon2D.IsValid(simplified))
            {
                error = "Doğrusal köşeler ayıklandıktan sonra geçerli bir çokgen kalmadı.";
                return null;
            }

            return simplified;
        }

        /// <summary>Whether the triangle is horizontal (|y| of its normal above the threshold); if
        /// so, its Y level.</summary>
        private static bool IsHorizontal(Vector3[] local, int[] triangles, int t, out float level)
        {
            Vector3 a = local[triangles[t]];
            Vector3 b = local[triangles[t + 1]];
            Vector3 c = local[triangles[t + 2]];

            Vector3 normal = Vector3.Cross(b - a, c - a);
            float magnitude = normal.magnitude;
            if (magnitude < 1e-9f)
            {
                level = 0f;
                return false; // degenerate triangle
            }

            if (Mathf.Abs(normal.y / magnitude) < HorizontalNormalThreshold)
            {
                level = 0f;
                return false;
            }

            level = (a.y + b.y + c.y) / 3f;
            return true;
        }

        /// <summary>Projects the point to XZ and welds it within tolerance; returns the welded
        /// vertex index.</summary>
        private static int Weld(Vector3 point, List<Vector2> welded, Dictionary<long, int> lookup)
        {
            long key = QuantizeKey(point.x, point.z);
            if (lookup.TryGetValue(key, out int index))
            {
                return index;
            }

            index = welded.Count;
            welded.Add(new Vector2(point.x, point.z));
            lookup[key] = index;
            return index;
        }

        private static long QuantizeKey(float x, float z)
        {
            long qx = (long)Mathf.Round(x / WeldResolution);
            long qz = (long)Mathf.Round(z / WeldResolution);
            return (qx << 32) ^ (qz & 0xFFFFFFFFL);
        }

        private static void AddEdge(
            int a,
            int b,
            Dictionary<long, int> counts,
            Dictionary<long, (int A, int B)> pairs)
        {
            if (a == b)
            {
                return; // degenerate edge (welding merged both vertices)
            }

            int low = Mathf.Min(a, b);
            int high = Mathf.Max(a, b);
            long key = ((long)low << 32) | (uint)high;

            counts.TryGetValue(key, out int count);
            counts[key] = count + 1;
            pairs[key] = (low, high);
        }

        private static void AddNeighbour(Dictionary<int, List<int>> adjacency, int from, int to)
        {
            if (!adjacency.TryGetValue(from, out List<int> list))
            {
                list = new List<int>(2);
                adjacency[from] = list;
            }

            list.Add(to);
        }

        /// <summary>Walks the closed ring from the boundary edges, starting at the
        /// lexicographically smallest vertex (guaranteed to be on the outer ring).</summary>
        private static Vector2[] WalkRing(Dictionary<int, List<int>> adjacency, List<Vector2> welded)
        {
            int start = -1;
            foreach (int index in adjacency.Keys)
            {
                if (start < 0)
                {
                    start = index;
                    continue;
                }

                Vector2 candidate = welded[index];
                Vector2 best = welded[start];
                if (candidate.x < best.x || (Mathf.Approximately(candidate.x, best.x) && candidate.y < best.y))
                {
                    start = index;
                }
            }

            if (start < 0)
            {
                return null;
            }

            var ring = new List<Vector2>(adjacency.Count);
            var used = new HashSet<long>();

            int current = start;
            int previous = -1;

            while (true)
            {
                ring.Add(welded[current]);

                if (!adjacency.TryGetValue(current, out List<int> neighbours))
                {
                    return null;
                }

                int next = -1;
                for (int i = 0; i < neighbours.Count; i++)
                {
                    int candidate = neighbours[i];
                    if (candidate == previous)
                    {
                        continue;
                    }

                    long edgeKey = ((long)Mathf.Min(current, candidate) << 32) | (uint)Mathf.Max(current, candidate);
                    if (used.Contains(edgeKey))
                    {
                        continue;
                    }

                    next = candidate;
                    used.Add(edgeKey);
                    break;
                }

                if (next < 0 || next == start)
                {
                    break;
                }

                if (ring.Count > adjacency.Count + 1)
                {
                    return null; // walk never closed: safety brake
                }

                previous = current;
                current = next;
            }

            return ring.Count >= Polygon2D.MinPoints ? ring.ToArray() : null;
        }

        /// <summary>Drops collinear vertices — extra points sitting on an edge add no information
        /// and only make the file unreadable.</summary>
        private static Vector2[] DropCollinear(Vector2[] ring)
        {
            var kept = new List<Vector2>(ring.Length);

            for (int i = 0; i < ring.Length; i++)
            {
                Vector2 previous = ring[(i - 1 + ring.Length) % ring.Length];
                Vector2 current = ring[i];
                Vector2 next = ring[(i + 1) % ring.Length];

                Vector2 incoming = current - previous;
                Vector2 outgoing = next - current;
                float cross = (incoming.x * outgoing.y) - (incoming.y * outgoing.x);

                if (Mathf.Abs(cross) > CollinearEpsilon)
                {
                    kept.Add(current);
                }
            }

            return kept.Count >= Polygon2D.MinPoints ? kept.ToArray() : ring;
        }

        // -------------------------------------------------------------- helpers

        /// <summary>Resolves the target mesh: the selection (or its ancestors) first, then the only
        /// one in the scene. With more than one the target is ambiguous, so nothing is done.</summary>
        private static ArenaDimensionMesh ResolveTarget(out string error)
        {
            error = null;

            GameObject[] selection = Selection.gameObjects;
            for (int i = 0; i < selection.Length; i++)
            {
                if (selection[i] == null)
                {
                    continue;
                }

                var fromSelection = selection[i].GetComponentInParent<ArenaDimensionMesh>(true);
                if (fromSelection != null)
                {
                    return fromSelection;
                }
            }

            ArenaDimensionMesh[] all =
                UnityEngine.Object.FindObjectsByType<ArenaDimensionMesh>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (all.Length == 0)
            {
                error = "Sahnede ölçü maketi (ArenaDimensionMesh) yok.";
                return null;
            }

            if (all.Length > 1)
            {
                error = $"Sahnede {all.Length} maket var — hangisinin yazılacağı belirsiz. Birini seçip tekrar dene.";
                return null;
            }

            return all[0];
        }
    }
}
