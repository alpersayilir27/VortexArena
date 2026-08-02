using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VortexArena.Core.Arena;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// <c>Tools &gt; VortexArena &gt; DimensionMesh'i JSON'a Çevir</c> — sahnedeki ölçü maketini
    /// okuyup <see cref="ArenaDimensionMesh.SourceJson"/>'un <b>ÜSTÜNE</b> yazar.
    /// <para>
    /// <b>Ne için:</b> ölçü yanlış alınmışsa köşeler ProBuilder ile sahnede düzeltilir; gerçek
    /// ölçüyü tek doğruluk kaynağına (boyut dosyası) geri yazan adım budur. Hedef dosya
    /// kullanıcıya sorulmaz — maketin kökündeki işaretçiden gelir, yani maket hangi dosyadan
    /// üretildiyse ona döner.
    /// </para>
    /// <para>
    /// ✔ <b>Gidiş-dönüş kayıpsız ve simetriktir:</b> JSON'daki tek halka → tek mesh → tek halka.
    /// Şemada birleştirme olmadığı için "yazdığın gibi geri gelmeyen alan" diye bir durum yoktur;
    /// dokunulmamış bir maketi çevirmek dosyayı (kayan nokta yuvarlamasına kadar) aynı bırakır.
    /// </para>
    /// <para>
    /// ⚠️ <b>Ayak izi ALT yüzden okunur.</b> Yatay yüzler Y seviyesine göre gruplanır ve en alt
    /// grup alınır (düz tabanda tek grup, prizmada alt + üst). Bir kolonun üst yüzü alttan farklı
    /// düzenlenmişse kazanan ALT yüzdür — muhafaza zaten zemindeki ayak izini önemsiyor.
    /// </para>
    /// <para>
    /// ⚠️ <b>Kenarlar köşe İNDEKSİYLE değil KONUMLA anahtarlanır.</b> ProBuilder sert normaller
    /// için köşeleri yüz başına ayırıyor; indeksle bakan bir sınır tespiti her yüzün her kenarını
    /// "yalnız bir kez geçmiş" sanar ve tüm mesh'i sınır olarak çıkarır.
    /// </para>
    /// </summary>
    public static class DimensionMeshReader
    {
        /// <summary>Köşe konumlarını anahtarlarken kullanılan çözünürlük (metre) — 0.1 mm.</summary>
        private const float WeldResolution = 1e-4f;

        /// <summary>Yatay sayılmak için yüz normalinin |y| eşiği.</summary>
        private const float HorizontalNormalThreshold = 0.9f;

        /// <summary>Aynı Y seviyesinde sayılmak için tolerans (metre).</summary>
        private const float LevelTolerance = 0.01f;

        /// <summary>Doğrusal (collinear) köşe ayıklama eşiği — ardışık iki kenarın çapraz çarpımı.</summary>
        private const float CollinearEpsilon = 1e-3f;

        [MenuItem("Tools/VortexArena/DimensionMesh'i JSON'a Çevir")]
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

        /// <summary>
        /// Maketi okuyup kaynak dosyaya yazar. Exception FIRLATMAZ.
        /// <para>
        /// ⚠️ Yazmadan önce sonuç <see cref="ArenaDimensions.Parse"/> ile geri okunur;
        /// ayrıştırılamıyorsa dosyaya <b>DOKUNULMAZ</b> — bozuk bir yazım o mekanın tüm
        /// sahnelerini ölçüsüz bırakırdı.
        /// </para>
        /// </summary>
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

            // Kendi yazdığımızı geri okumak: JsonUtility sessizce boş bir alan üretirse burada
            // yakalanır ve dosyaya hiç dokunulmaz.
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

        // ---------------------------------------------------------------- çıkarım

        /// <summary>
        /// Maketin geometrisini plana çevirir. Başarısızlıkta <c>null</c> döner.
        /// </summary>
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
                defaultColumnHeight = target.DefaultColumnHeight
            };
        }

        /// <summary>
        /// Bir mesh'in ayak izi halkasını <paramref name="root"/>'un yerel XZ uzayında çıkarır ve
        /// yüksekliğini (Y aralığı) verir.
        /// <para>
        /// Noktalar dünya üstünden geçirilir
        /// (<c>root.InverseTransformPoint(mesh.TransformPoint(v))</c>) — böylece kolonu sahnede
        /// sürüklemek ve döndürmek doğru sonuç verir.
        /// </para>
        /// </summary>
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

            // 1) Tüm köşeleri kök uzayına taşı.
            var local = new Vector3[vertices.Length];
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < vertices.Length; i++)
            {
                local[i] = root.InverseTransformPoint(meshTransform.TransformPoint(vertices[i]));
                minY = Mathf.Min(minY, local[i].y);
                maxY = Mathf.Max(maxY, local[i].y);
            }

            height = maxY - minY;

            // 2) Yatay üçgenleri seç ve en alt seviyeyi bul.
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

            // 3) En alt seviyedeki yatay üçgenlerin kenarlarını topla; yalnız BİR KEZ geçen kenar
            //    sınırdır. Kenarlar konumla anahtarlanır (bkz. sınıf başlığı).
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
                    continue; // iç kenar: iki üçgen paylaşıyor
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

            // 4) Halkayı yürü.
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

        /// <summary>Üçgen yatay mı (normalinin |y| bileşeni eşiğin üstünde); öyleyse Y seviyesi.</summary>
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
                return false; // dejenere üçgen
            }

            if (Mathf.Abs(normal.y / magnitude) < HorizontalNormalThreshold)
            {
                level = 0f;
                return false;
            }

            level = (a.y + b.y + c.y) / 3f;
            return true;
        }

        /// <summary>Noktayı XZ'ye izdüşürüp toleransla kaynaştırır; kaynaşmış köşe indeksini döner.</summary>
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
                return; // dejenere kenar (kaynaştırma iki köşeyi birleştirdi)
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

        /// <summary>
        /// Sınır kenarlarından kapalı halkayı yürür. Başlangıç, sözlük sırasına göre en küçük
        /// köşedir (dış halka üstünde olduğu garanti).
        /// </summary>
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
                    return null; // yürüyüş kapanmadı: güvenlik freni
                }

                previous = current;
                current = next;
            }

            return ring.Count >= Polygon2D.MinPoints ? ring.ToArray() : null;
        }

        /// <summary>
        /// Doğrusal ara köşeleri ayıklar — bir kenar üstünde duran ek köşeler ölçüye bilgi
        /// katmaz, dosyayı okunmaz yapar.
        /// </summary>
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

        // -------------------------------------------------------------- yardımcı

        /// <summary>
        /// Hedef maketi bulur: önce seçimde (ya da seçimin atalarında), sonra sahnede tek olan.
        /// Birden fazla varsa hangisinin yazılacağı belirsizdir — iş yapılmaz.
        /// </summary>
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
