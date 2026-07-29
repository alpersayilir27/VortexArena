using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core.Arena;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// Bir <b>TestMesh</b>'i (mekanın fiziksel alanını kabaca temsil eden basit quad/blok yığını)
    /// <see cref="ArenaDimensions"/> planına çevirir, planı diske <b>JSON olarak yazar</b> ve
    /// normal üretim kapısından (<see cref="ArenaShapeBuilder.Build"/>) geçirir.
    /// <para>
    /// <b>Neden böyle:</b> arenanın ölçüsünün tek temsili boyut JSON'udur. TestMesh yolu ikinci bir
    /// geometri üreteci OLMAZ; yalnız bir <i>çıkarım</i> adımıdır — kaba bloklardan plan okur, o
    /// planı dosyaya yazar ve oradan sonrası elle yazılmış bir JSON ile birebir aynı yoldan gider.
    /// Böylece ölçü sonradan dosyada elle düzeltilip <c>Build Arena From Dimensions</c> ile
    /// yeniden çizilebilir; çalışma anında <see cref="ArenaBoundary"/>'nin okuyacağı ölçü de
    /// üretilenle aynı dosyadan gelir.
    /// </para>
    /// <para>
    /// Menü girişi: <c>Tools &gt; VortexArena &gt; Build Arena From TestMesh</c>. Sihirbaz
    /// (<see cref="ArenaTemplateWizard"/>) de aynı kapıdan geçer.
    /// </para>
    /// <para>
    /// ✔ <b>Döndürülmüş blok tuzağı ÇÖZÜLDÜ:</b> eski sürüm dünya AABB'sinin AABB'sini alıyordu ve
    /// köke göre döndürülmüş bloklar şişiyordu. Artık her parçanın ölçüsü KENDİ frame'inde
    /// okunuyor (<c>Mesh.bounds</c> × <c>lossyScale</c>) ve dönüş <see cref="ArenaDimensions.Column.yaw"/>
    /// alanında korunuyor.
    /// </para>
    /// <para>
    /// ⚠️ <c>EditorUtility.DisplayDialog</c> YOK: modal dialog Unity ana thread'ini kilitliyor ve
    /// CLI'dan çalıştırınca komut timeout veriyor. Sonuç <c>Debug.Log</c> ile bildirilir.
    /// </para>
    /// </summary>
    internal static class ArenaTestMeshBuilder
    {
        /// <summary>
        /// "Yassı" sayılmak için bir bloğun en fazla olabileceği mutlak yükseklik (metre).
        /// 25 cm: bir podyum/rampa basamağından alçak, bir duvardan çok alçak.
        /// </summary>
        private const float FlatMaxHeight = 0.25f;

        /// <summary>
        /// "Yassı" sayılmak için yüksekliğin yatay KISA kenara oranı. Büyük bir zemin quad'ı
        /// kalın modellenmiş olsa bile (ör. 12 m'lik alan için 40 cm) zemin sayılsın diye mutlak
        /// eşiğin yanında oransal bir eşik de var.
        /// </summary>
        private const float FlatHeightRatio = 0.15f;

        /// <summary>
        /// Duvar sayılmak için ayak izinin uzun/kısa kenar oranı. 4 kat: bir kolon en fazla
        /// dikdörtgen olur, duvar ise her zaman uzun bir şerittir.
        /// </summary>
        private const float WallAspectRatio = 4f;

        /// <summary>Hiç duvar sınıfı blok yoksa kullanılacak duvar yüksekliği (metre).</summary>
        private const float DefaultWallHeight = 3f;

        /// <summary>
        /// Köşe konumlarını anahtarlarken kullanılan çözünürlük (metre) — 0.1 mm.
        /// <para>
        /// ⚠️ Kenarlar köşe İNDEKSİ ile değil KONUM ile anahtarlanır: ProBuilder mesh'leri sert
        /// normaller için köşeleri yüz başına ayırıyor, indeksle bakan bir sınır tespiti her yüzün
        /// her kenarını "yalnız bir kez geçmiş" sanır ve tüm mesh sınır çıkardı.
        /// </para>
        /// </summary>
        private const float WeldResolution = 1e-4f;

        /// <summary>
        /// Doğrusal (collinear) köşe ayıklama eşiği — ardışık iki kenarın çapraz çarpımı.
        /// Bir 12×12 quad'ın kenarları üzerindeki ara köşeler böylece atılır ve plan 4 köşeye iner.
        /// </summary>
        private const float CollinearEpsilon = 1e-3f;

        private enum PieceKind
        {
            Floor,
            Wall,
            Column
        }

        /// <summary>Bir kaynak bloğun arena YEREL uzayındaki okunmuş hâli.</summary>
        private struct Piece
        {
            public string Name;
            public MeshRenderer Renderer;
            public Mesh Mesh;

            /// <summary>Parçanın KENDİ eksenlerindeki gerçek ölçüsü (dünya AABB'si DEĞİL).</summary>
            public Vector3 SizeLocal;

            /// <summary>Parça merkezinin arena yerel koordinatı.</summary>
            public Vector3 CenterLocal;

            /// <summary>Arena köküne göre Y ekseni dönüşü (derece).</summary>
            public float Yaw;

            public PieceKind Kind;
        }

        // ------------------------------------------------------------- çıkarım

        /// <summary>
        /// TestMesh'i <see cref="ArenaDimensions"/> planına çevirir. Exception FIRLATMAZ —
        /// başarısızlıkta <c>null</c> döner ve sebebi <paramref name="error"/>'a yazar; kurtarılmış
        /// ama dikkat isteyen durumlar <paramref name="warnings"/>'e düşer.
        /// <para>
        /// <b>Sınır (<c>outline</c>) zemin parçasının GERÇEK mesh sınırından çıkarılır</b>, AABB'den
        /// değil: zemin bir ProBuilder poly-shape'i olabilir (L, yamuk) ve AABB o şekli sessizce
        /// dikdörtgene düzleştirirdi.
        /// </para>
        /// <para>
        /// <b>Kolonlar</b>: zemin olmayan her parça bir <see cref="ArenaDimensions.Column"/>'dur —
        /// duvar sınıfı dahil, çünkü iç duvar da sonuçta döndürülmüş bir dikdörtgen engeldir.
        /// ⚠️ Sınır çokgeninin DIŞINDA kalanlar atlanır: çevre duvarları zaten <c>outline</c>'dan
        /// üretiliyor, atlanmazsa her duvar iki kez çizilirdi.
        /// </para>
        /// </summary>
        /// <param name="testMesh">TestMesh kökü (sahne objesi ya da prefab asset'i).</param>
        /// <param name="root">Arena kökü — <see cref="ArenaBoundary"/>'yi taşıyan transform.</param>
        /// <param name="error">Başarısızsa sebebi; başarılıysa <c>null</c>.</param>
        /// <param name="warnings">Kurtarılmış belirsizlikler (çağıran kullanıcıya gösterir).</param>
        internal static ArenaDimensions Extract(
            GameObject testMesh,
            Transform root,
            out string error,
            List<string> warnings)
        {
            error = null;

            if (testMesh == null)
            {
                error = "TestMesh verilmedi.";
                return null;
            }

            if (root == null)
            {
                error = "Arena kökü verilmedi.";
                return null;
            }

            List<Piece> pieces = ReadPieces(testMesh, root, warnings);
            if (pieces.Count == 0)
            {
                error = $"TestMesh altında kullanılabilir MeshRenderer yok ('{testMesh.name}').";
                return null;
            }

            // ----------------------------------------------------------- sınır
            int floorIndex = PickLargestFloor(pieces, warnings);

            Vector2[] outline = floorIndex >= 0
                ? ExtractOutline(pieces[floorIndex], root, out error)
                : OutlineFromOverallBox(pieces, warnings);

            if (outline == null)
            {
                return null; // error zaten dolu
            }

            // --------------------------------------------------------- kolonlar
            var columns = new List<ArenaDimensions.Column>(pieces.Count);
            int skippedOutside = 0;

            for (int i = 0; i < pieces.Count; i++)
            {
                if (i == floorIndex || pieces[i].Kind == PieceKind.Floor)
                {
                    continue; // zemin geometrisi kolon değildir
                }

                Piece piece = pieces[i];
                var center = new Vector2(piece.CenterLocal.x, piece.CenterLocal.z);
                if (!IsInsidePolygon(outline, center))
                {
                    skippedOutside++;
                    continue;
                }

                columns.Add(new ArenaDimensions.Column
                {
                    name = piece.Name,
                    center = center,
                    size = new Vector2(piece.SizeLocal.x, piece.SizeLocal.z),
                    yaw = piece.Yaw,
                    height = piece.SizeLocal.y
                });
            }

            if (skippedOutside > 0)
            {
                warnings?.Add(
                    $"{skippedOutside} parça sınır çokgeninin dışında kaldığı için kolon olarak " +
                    "yazılmadı — çevre duvarları outline'dan üretiliyor, ikinci kez çizilmemeleri için.");
            }

            // ------------------------------------------------------ duvar boyu
            float wallHeight = 0f;
            for (int i = 0; i < pieces.Count; i++)
            {
                if (pieces[i].Kind == PieceKind.Wall)
                {
                    wallHeight = Mathf.Max(wallHeight, pieces[i].SizeLocal.y);
                }
            }

            if (wallHeight <= Mathf.Epsilon)
            {
                wallHeight = DefaultWallHeight;
                warnings?.Add(
                    $"TestMesh'te duvar sınıfına giren blok yok — duvar yüksekliği {DefaultWallHeight:0.##} m " +
                    "varsayıldı; gerçek ölçü farklıysa boyut dosyasında düzelt.");
            }

            return new ArenaDimensions
            {
                name = testMesh.name,
                outline = outline,
                wallHeight = wallHeight,
                columns = columns.ToArray()
            };
        }

        /// <summary>
        /// TestMesh altındaki her <c>MeshRenderer</c>'ı okur.
        /// <para>
        /// ⚠️ Ölçü parçanın KENDİ frame'inde alınır: <c>Mesh.bounds</c> (yerel) × <c>lossyScale</c>.
        /// Dünya AABB'si kullanılsaydı döndürülmüş bir blok kendi ölçüsünden büyük görünürdü —
        /// eski sürümün tuzağı buydu, <c>yaw</c> alanı sayesinde çözüldü.
        /// </para>
        /// </summary>
        private static List<Piece> ReadPieces(GameObject testMesh, Transform root, List<string> warnings)
        {
            MeshRenderer[] sources = testMesh.GetComponentsInChildren<MeshRenderer>(true);
            var pieces = new List<Piece>(sources.Length);
            int missingMesh = 0;

            Quaternion inverseRootRotation = Quaternion.Inverse(root.rotation);

            for (int i = 0; i < sources.Length; i++)
            {
                MeshRenderer source = sources[i];
                if (source == null)
                {
                    continue;
                }

                var filter = source.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                {
                    missingMesh++;
                    continue;
                }

                Transform pieceTransform = source.transform;
                Bounds localBounds = mesh.bounds;
                Vector3 scale = pieceTransform.lossyScale;

                var sizeLocal = new Vector3(
                    Mathf.Abs(localBounds.size.x * scale.x),
                    Mathf.Abs(localBounds.size.y * scale.y),
                    Mathf.Abs(localBounds.size.z * scale.z));

                if (sizeLocal.x <= Mathf.Epsilon || sizeLocal.z <= Mathf.Epsilon)
                {
                    continue; // dejenere blok (dikey quad vb.): plana katkısı yok
                }

                Vector3 centerLocal = root.InverseTransformPoint(
                    pieceTransform.TransformPoint(localBounds.center));

                float yaw = (inverseRootRotation * pieceTransform.rotation).eulerAngles.y;

                pieces.Add(new Piece
                {
                    Name = source.gameObject.name,
                    Renderer = source,
                    Mesh = mesh,
                    SizeLocal = sizeLocal,
                    CenterLocal = centerLocal,
                    Yaw = yaw,
                    Kind = Classify(source.gameObject.name, sizeLocal)
                });
            }

            if (missingMesh > 0)
            {
                warnings?.Add($"{missingMesh} MeshRenderer'ın mesh'i yok (MeshFilter boş) — atlandı.");
            }

            return pieces;
        }

        /// <summary>
        /// En büyük yatay alana sahip zemin parçasının indeksi; zemin yoksa <c>-1</c>.
        /// Birden çok zemin varsa sınır yalnız en büyüğünden çıkar (birleştirmek, aralarındaki
        /// boşluğu sessizce zemin sayardı) ve bu bir uyarı olarak bildirilir.
        /// </summary>
        private static int PickLargestFloor(List<Piece> pieces, List<string> warnings)
        {
            int best = -1;
            float bestArea = 0f;
            int floorCount = 0;

            for (int i = 0; i < pieces.Count; i++)
            {
                if (pieces[i].Kind != PieceKind.Floor)
                {
                    continue;
                }

                floorCount++;
                float area = pieces[i].SizeLocal.x * pieces[i].SizeLocal.z;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = i;
                }
            }

            if (floorCount > 1)
            {
                warnings?.Add(
                    $"TestMesh'te {floorCount} zemin parçası var — sınır için yalnız en büyüğü " +
                    $"('{pieces[best].Name}') kullanıldı.");
            }

            return best;
        }

        // -------------------------------------------------------- sınır çıkarımı

        /// <summary>
        /// Zemin parçasının mesh'inden kapalı sınır çokgenini çıkarır: sınır kenarları → en uzun
        /// döngü → arena yerel XZ → doğrusal ara köşelerin ayıklanması.
        /// <para>
        /// Su geçirmez (kapalı katı) bir zeminin sınır kenarı YOKTUR — bir ProBuilder küpünün her
        /// kenarı iki yüz arasındadır. Bu <b>hata değil, beklenen durumdur</b>: kare/dikdörtgen
        /// alanlar tipik olarak böyle modellenir. O durumda sınır parçanın kendi yönelimli
        /// dikdörtgeninden dört köşe olarak üretilir (<see cref="OutlineFromPieceRect"/>) —
        /// gerçek çokgen (L, yamuk) isteniyorsa zemin düz bir yüzey olmalıdır.
        /// </para>
        /// </summary>
        private static Vector2[] ExtractOutline(Piece floor, Transform root, out string error)
        {
            error = null;

            List<Vector3> loop = LongestBoundaryLoop(floor.Mesh);
            if (loop != null && loop.Count >= ArenaDimensions.MinOutlinePoints)
            {
                var points = new List<Vector2>(loop.Count);
                for (int i = 0; i < loop.Count; i++)
                {
                    Vector3 local = root.InverseTransformPoint(floor.Renderer.transform.TransformPoint(loop[i]));
                    points.Add(new Vector2(local.x, local.z));
                }

                SimplifyCollinear(points);

                if (points.Count >= ArenaDimensions.MinOutlinePoints)
                {
                    return points.ToArray();
                }
            }

            return OutlineFromPieceRect(floor);
        }

        /// <summary>
        /// Sınır kenarı çıkmayan (kapalı katı) bir zemin parçasının kendi yönelimli XZ
        /// dikdörtgeninden dört köşeli outline üretir.
        /// <para>
        /// ⚠️ Köşeler parçanın KENDİ frame'inde kurulup <c>yaw</c> ile döndürülür, sonra merkeze
        /// taşınır — eksene hizalı bir kutuya düşülseydi döndürülmüş bir zemin bloğu köşegeni
        /// kadar şişerdi (eski sürümün tuzağı).
        /// </para>
        /// </summary>
        private static Vector2[] OutlineFromPieceRect(Piece floor)
        {
            float hx = floor.SizeLocal.x * 0.5f;
            float hz = floor.SizeLocal.z * 0.5f;
            Quaternion rotation = Quaternion.Euler(0f, floor.Yaw, 0f);
            var center = new Vector2(floor.CenterLocal.x, floor.CenterLocal.z);

            var corners = new Vector2[4];
            corners[0] = RotateCorner(rotation, -hx, -hz) + center;
            corners[1] = RotateCorner(rotation, hx, -hz) + center;
            corners[2] = RotateCorner(rotation, hx, hz) + center;
            corners[3] = RotateCorner(rotation, -hx, hz) + center;
            return corners;
        }

        private static Vector2 RotateCorner(Quaternion rotation, float x, float z)
        {
            Vector3 rotated = rotation * new Vector3(x, 0f, z);
            return new Vector2(rotated.x, rotated.z);
        }

        /// <summary>
        /// Mesh'in sınır kenarlarından en uzun kapalı döngüyü döndürür (mesh YEREL uzayında).
        /// <para>
        /// Sınır kenarı = üçgen listesinde <b>tam bir kez</b> geçen kenar; iki kez geçen kenar iki
        /// üçgen arasındadır, yani içtedir. Kenarlar konuma göre anahtarlanır
        /// (bkz. <see cref="WeldResolution"/>).
        /// </para>
        /// </summary>
        private static List<Vector3> LongestBoundaryLoop(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            if (vertices.Length == 0 || triangles.Length < 3)
            {
                return null;
            }

            var counts = new Dictionary<EdgeKey, int>(triangles.Length);
            var positions = new Dictionary<Vector3Int, Vector3>(vertices.Length);

            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                AddEdge(counts, positions, vertices, triangles[t], triangles[t + 1]);
                AddEdge(counts, positions, vertices, triangles[t + 1], triangles[t + 2]);
                AddEdge(counts, positions, vertices, triangles[t + 2], triangles[t]);
            }

            // Sınır kenarları + komşuluk tablosu.
            var boundary = new List<EdgeKey>();
            foreach (KeyValuePair<EdgeKey, int> entry in counts)
            {
                if (entry.Value == 1)
                {
                    boundary.Add(entry.Key);
                }
            }

            if (boundary.Count < ArenaDimensions.MinOutlinePoints)
            {
                return null;
            }

            var incident = new Dictionary<Vector3Int, List<int>>(boundary.Count * 2);
            for (int i = 0; i < boundary.Count; i++)
            {
                AddIncident(incident, boundary[i].A, i);
                AddIncident(incident, boundary[i].B, i);
            }

            var used = new bool[boundary.Count];
            List<Vector3> longest = null;

            for (int i = 0; i < boundary.Count; i++)
            {
                if (used[i])
                {
                    continue;
                }

                List<Vector3> loop = WalkLoop(boundary, incident, positions, used, i);
                if (loop != null && (longest == null || loop.Count > longest.Count))
                {
                    longest = loop;
                }
            }

            return longest;
        }

        /// <summary>
        /// <paramref name="startEdge"/>'den başlayarak sınır kenarlarını uç uca ekler. Zincir
        /// başlangıç köşesine dönerse kapalı bir döngü elde edilmiştir; dönmezse (açık zincir)
        /// döngü sayılmaz ve <c>null</c> döner.
        /// </summary>
        private static List<Vector3> WalkLoop(
            List<EdgeKey> boundary,
            Dictionary<Vector3Int, List<int>> incident,
            Dictionary<Vector3Int, Vector3> positions,
            bool[] used,
            int startEdge)
        {
            Vector3Int start = boundary[startEdge].A;
            Vector3Int current = boundary[startEdge].B;
            used[startEdge] = true;

            var loop = new List<Vector3> { positions[start], positions[current] };
            bool closed = false;

            // Güvenlik tavanı: bozuk bir mesh'te komşuluk zinciri kendini yiyebilir; editör
            // aracının sonsuz döngüye girmesi kabul edilemez.
            for (int guard = 0; guard <= boundary.Count; guard++)
            {
                if (current.Equals(start))
                {
                    closed = true;
                    break;
                }

                int next = -1;
                if (incident.TryGetValue(current, out List<int> candidates))
                {
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        if (!used[candidates[i]])
                        {
                            next = candidates[i];
                            break;
                        }
                    }
                }

                if (next < 0)
                {
                    break;
                }

                used[next] = true;
                current = boundary[next].A.Equals(current) ? boundary[next].B : boundary[next].A;
                loop.Add(positions[current]);
            }

            if (!closed)
            {
                return null;
            }

            // Son nokta başlangıcın tekrarıdır; plan KAPALI kabul edildiği için yazılmaz.
            loop.RemoveAt(loop.Count - 1);
            return loop.Count >= ArenaDimensions.MinOutlinePoints ? loop : null;
        }

        private static void AddEdge(
            Dictionary<EdgeKey, int> counts,
            Dictionary<Vector3Int, Vector3> positions,
            Vector3[] vertices,
            int i0,
            int i1)
        {
            Vector3Int k0 = Quantize(vertices[i0]);
            Vector3Int k1 = Quantize(vertices[i1]);
            if (k0.Equals(k1))
            {
                return; // dejenere kenar
            }

            positions[k0] = vertices[i0];
            positions[k1] = vertices[i1];

            var key = new EdgeKey(k0, k1);
            counts.TryGetValue(key, out int count);
            counts[key] = count + 1;
        }

        private static void AddIncident(Dictionary<Vector3Int, List<int>> incident, Vector3Int key, int edgeIndex)
        {
            if (!incident.TryGetValue(key, out List<int> list))
            {
                list = new List<int>(2);
                incident[key] = list;
            }

            list.Add(edgeIndex);
        }

        private static Vector3Int Quantize(Vector3 point)
        {
            return new Vector3Int(
                Mathf.RoundToInt(point.x / WeldResolution),
                Mathf.RoundToInt(point.y / WeldResolution),
                Mathf.RoundToInt(point.z / WeldResolution));
        }

        /// <summary>Yönden bağımsız kenar anahtarı — (v0,v1) ile (v1,v0) aynı kenardır.</summary>
        private readonly struct EdgeKey : IEquatable<EdgeKey>
        {
            public readonly Vector3Int A;
            public readonly Vector3Int B;

            public EdgeKey(Vector3Int a, Vector3Int b)
            {
                // Komşu iki üçgen ortak kenarı TERS yönde gezer; sıralamazsak ikisi ayrı anahtar
                // olur, sayaç 1'de kalır ve her iç kenar sınır sanılırdı.
                if (Compare(a, b) <= 0)
                {
                    A = a;
                    B = b;
                }
                else
                {
                    A = b;
                    B = a;
                }
            }

            private static int Compare(Vector3Int a, Vector3Int b)
            {
                if (a.x != b.x) return a.x < b.x ? -1 : 1;
                if (a.y != b.y) return a.y < b.y ? -1 : 1;
                if (a.z != b.z) return a.z < b.z ? -1 : 1;
                return 0;
            }

            public bool Equals(EdgeKey other) => A.Equals(other.A) && B.Equals(other.B);

            public override bool Equals(object obj) => obj is EdgeKey other && Equals(other);

            public override int GetHashCode() => (A.GetHashCode() * 397) ^ B.GetHashCode();
        }

        /// <summary>
        /// Aynı doğru üstündeki ara köşeleri atar (üçlü çapraz çarpımı ~0). Bir 12×12 quad,
        /// triangülasyondan gelen ara köşelerle birlikte okunuyor olabilir — sadeleştirilmezse
        /// plana onlarca gereksiz köşe ve o kadar duvar parçası girerdi.
        /// </summary>
        private static void SimplifyCollinear(List<Vector2> points)
        {
            bool removed = true;
            while (removed && points.Count > ArenaDimensions.MinOutlinePoints)
            {
                removed = false;
                for (int i = 0; i < points.Count && points.Count > ArenaDimensions.MinOutlinePoints; i++)
                {
                    Vector2 previous = points[(i - 1 + points.Count) % points.Count];
                    Vector2 current = points[i];
                    Vector2 next = points[(i + 1) % points.Count];

                    Vector2 a = current - previous;
                    Vector2 b = next - current;
                    float cross = a.x * b.y - a.y * b.x;

                    if (Mathf.Abs(cross) <= CollinearEpsilon)
                    {
                        points.RemoveAt(i);
                        removed = true;
                        i--;
                    }
                }
            }
        }

        /// <summary>
        /// Zemin parçası bulunamadığında kullanılan yedek sınır: tüm parçaların arena yerel XZ
        /// sınırlayıcı kutusundan dört köşe. Şekli düzleştirdiği için uyarıyla bildirilir.
        /// </summary>
        private static Vector2[] OutlineFromOverallBox(List<Piece> pieces, List<string> warnings)
        {
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;

            for (int i = 0; i < pieces.Count; i++)
            {
                Piece piece = pieces[i];
                minX = Mathf.Min(minX, piece.CenterLocal.x - piece.SizeLocal.x * 0.5f);
                maxX = Mathf.Max(maxX, piece.CenterLocal.x + piece.SizeLocal.x * 0.5f);
                minZ = Mathf.Min(minZ, piece.CenterLocal.z - piece.SizeLocal.z * 0.5f);
                maxZ = Mathf.Max(maxZ, piece.CenterLocal.z + piece.SizeLocal.z * 0.5f);
            }

            warnings?.Add(
                "TestMesh'te zemin parçası bulunamadı — sınır tüm blokların genel kutusundan türetildi " +
                "(dikdörtgen). Alan dikdörtgen değilse boyut dosyasındaki outline'ı elle düzelt.");

            return new[]
            {
                new Vector2(minX, minZ),
                new Vector2(maxX, minZ),
                new Vector2(maxX, maxZ),
                new Vector2(minX, maxZ)
            };
        }

        /// <summary>
        /// Ray-casting nokta-içinde testi — <c>ArenaBoundary</c>'nin sınır hesabındaki mantığın
        /// aynısı: noktadan +X yönüne giden ışın kaç kenarı kesiyor (tek sayı = içeride).
        /// </summary>
        private static bool IsInsidePolygon(Vector2[] polygon, Vector2 point)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[j];

                if ((a.y > point.y) != (b.y > point.y) &&
                    point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        /// <summary>
        /// Bir bloğun ne olduğunu belirler: <b>önce ad ipucu, sonra geometri</b>. Ad ipucu önde
        /// çünkü modelleyen kişinin niyeti ölçüden daha güvenilir bir kaynaktır (ince bir podyum
        /// ile kalın bir zemin geometrik olarak ayırt edilemez).
        /// </summary>
        private static PieceKind Classify(string name, Vector3 sizeLocal)
        {
            string lower = (name ?? string.Empty).ToLowerInvariant();

            if (lower.Contains("kolon") || lower.Contains("column") || lower.Contains("sutun") || lower.Contains("sütun"))
            {
                return PieceKind.Column;
            }

            if (lower.Contains("zemin") || lower.Contains("floor") || lower.Contains("ground") || lower.Contains("taban"))
            {
                return PieceKind.Floor;
            }

            if (lower.Contains("duvar") || lower.Contains("wall"))
            {
                return PieceKind.Wall;
            }

            float shortSide = Mathf.Min(sizeLocal.x, sizeLocal.z);
            float longSide = Mathf.Max(sizeLocal.x, sizeLocal.z);

            if (sizeLocal.y <= Mathf.Max(FlatMaxHeight, FlatHeightRatio * shortSide))
            {
                return PieceKind.Floor;
            }

            return longSide > shortSide * WallAspectRatio ? PieceKind.Wall : PieceKind.Column;
        }

        // -------------------------------------------------- çıkarım + yazım + üretim

        /// <summary>
        /// <see cref="BuildAndWrite"/> sonucu. Çağıran (menü ya da sihirbaz)
        /// <see cref="DimensionsAsset"/>'i <c>ArenaBoundary.dimensionsJson</c>'a bağlar.
        /// </summary>
        internal sealed class TestMeshResult
        {
            /// <summary>Plan çıkarıldı, dosyaya yazıldı ve geometri üretildi mi.</summary>
            public bool Success;

            /// <summary>Başarısızsa sebebi.</summary>
            public string Error;

            /// <summary>Çıkarılan plan.</summary>
            public ArenaDimensions Plan;

            /// <summary>Diske yazılan boyut dosyasının asset yolu.</summary>
            public string JsonPath;

            /// <summary>Yazılan dosyanın <c>TextAsset</c> hâli (<c>ArenaBoundary</c>'ye bağlanacak).</summary>
            public TextAsset DimensionsAsset;

            /// <summary>Üretilen geometri (duvar Renderer'ları buradan bağlanır).</summary>
            public ArenaShapeBuilder.Result Geometry;
        }

        /// <summary>
        /// TestMesh boru hattının tamamı: <see cref="Extract"/> → boyut JSON'unu diske yaz →
        /// <see cref="ArenaShapeBuilder.Build"/>.
        /// <para>
        /// ⚠️ <b>JSON diske YAZILIR, bellekte tutulmaz.</b> Çalışma anında
        /// <see cref="ArenaBoundary"/> ölçüyü o dosyadan okuyor; yazılmasaydı arena editörde doğru,
        /// build'de ölçüsüz olurdu. Dosya arena kutusunun <c>Data/</c> klasörüne girer ki
        /// referanslandığında build'e dahil olsun.
        /// </para>
        /// <para>
        /// Exception FIRLATMAZ — hata durumunda <see cref="TestMeshResult.Success"/> <c>false</c>.
        /// </para>
        /// </summary>
        /// <param name="testMesh">TestMesh kökü.</param>
        /// <param name="root">Arena kökü (<c>ArenaBoundary</c>'yi taşıyan transform).</param>
        /// <param name="dataFolder">Boyut dosyasının yazılacağı klasör (yoksa açılır).</param>
        /// <param name="fileName">Dosya adı, uzantısız (ör. sahne adı).</param>
        /// <param name="warnings">Çıkarım uyarıları buraya eklenir.</param>
        /// <param name="material">Geometri materyali; null ise ortak mekan materyali.</param>
        internal static TestMeshResult BuildAndWrite(
            GameObject testMesh,
            Transform root,
            string dataFolder,
            string fileName,
            List<string> warnings,
            Material material = null)
        {
            var result = new TestMeshResult();

            ArenaDimensions plan = Extract(testMesh, root, out string extractError, warnings);
            if (plan == null)
            {
                result.Error = extractError;
                return result;
            }

            result.Plan = plan;

            if (string.IsNullOrWhiteSpace(dataFolder) || string.IsNullOrWhiteSpace(fileName))
            {
                result.Error = "Boyut dosyasının yazılacağı klasör/ad çözülemedi.";
                return result;
            }

            string jsonPath = $"{dataFolder}/{fileName}_dimensions.json";
            if (!EnsureFolder(dataFolder))
            {
                result.Error = $"Boyut dosyası klasörü oluşturulamadı: '{dataFolder}'.";
                return result;
            }

            try
            {
                File.WriteAllText(jsonPath, plan.ToJson());
            }
            catch (Exception exception)
            {
                result.Error = $"Boyut dosyası yazılamadı ('{jsonPath}'): {exception.Message}";
                return result;
            }

            AssetDatabase.ImportAsset(jsonPath, ImportAssetOptions.ForceUpdate);
            result.JsonPath = jsonPath;
            result.DimensionsAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(jsonPath);

            if (result.DimensionsAsset == null)
            {
                result.Error = $"Yazılan boyut dosyası TextAsset olarak yüklenemedi ('{jsonPath}').";
                return result;
            }

            ArenaShapeBuilder.Result built = ArenaShapeBuilder.Build(plan, root, material);
            if (!built.Success)
            {
                result.Error = built.Error;
                return result;
            }

            result.Geometry = built;
            result.Success = true;
            return result;
        }

        /// <summary>Klasör zincirini (gerekirse üstten aşağı) oluşturur.</summary>
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

            if (!EnsureFolder(parent))
            {
                return false;
            }

            return !string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, leaf));
        }

        // --------------------------------------------------------------- menü

        /// <summary>
        /// <c>Tools &gt; VortexArena &gt; Build Arena From TestMesh</c> — seçili TestMesh'ten plan
        /// çıkarır, arena kutusunun <c>Data/</c> klasörüne <c>&lt;sahneAdı&gt;_dimensions.json</c>
        /// olarak yazar, geometriyi üretir ve <c>ArenaBoundary</c>'yi bağlar.
        /// <para>
        /// TestMesh seçimi: Project'ten bir prefab ya da hiyerarşiden bir sahne objesi. Kök
        /// <see cref="ArenaShapeBuilder.FindSelectedRoot"/> ile çözülür; çözülen kök TestMesh'in
        /// KENDİSİ (ya da onun altındaki bir obje) çıkarsa sahnedeki <c>ArenaBoundary</c>'ye
        /// düşülür — TestMesh'i seçmek onu arena kökü yapmamalı, yoksa geometri kaynak bloğun
        /// altına üretilirdi.
        /// </para>
        /// </summary>
        [MenuItem("Tools/VortexArena/Build Arena From TestMesh")]
        private static void BuildFromSelection()
        {
            GameObject testMesh = FindSelectedTestMesh();
            if (testMesh == null)
            {
                Debug.LogError(
                    "[ArenaTestMesh] Bir TestMesh seç: Project'ten prefab ya da hiyerarşiden " +
                    "MeshRenderer taşıyan bir kök.");
                return;
            }

            Transform root = ResolveRoot(testMesh);
            if (root == null)
            {
                Debug.LogError(
                    "[ArenaTestMesh] Arena kökü bulunamadı: sahnede ArenaBoundary taşıyan bir obje yok. " +
                    "TestMesh'i Project'ten (prefab olarak) seçip tekrar dene.");
                return;
            }

            if (root == testMesh.transform || root.IsChildOf(testMesh.transform))
            {
                Debug.LogError(
                    "[ArenaTestMesh] Arena kökü TestMesh'in kendisi olamaz — geometri kaynak bloğun " +
                    "altına üretilirdi. ArenaBoundary'yi taşıyan objeyi kök olarak kullan.");
                return;
            }

            // Boyut dosyası arena KUTUSUNUN Data/ klasörüne yazılır; kutuyu sahnenin yolu söyler
            // (Venues/<İşletme>/<Arena>/Scenes/<Sahne>.unity → .../Data). Kaydedilmemiş bir sahnede
            // yol yoktur, o yüzden iş yapılmaz: dosya proje kökünde yetim kalırdı.
            Scene scene = root.gameObject.scene;
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
            {
                Debug.LogError(
                    "[ArenaTestMesh] Sahne henüz kaydedilmemiş — boyut dosyasının yazılacağı arena kutusu " +
                    "belirlenemedi. Sahneyi arena kutusuna kaydedip tekrar dene.");
                return;
            }

            string dataFolder = ResolveDataFolder(scene.path);
            var warnings = new List<string>();
            TestMeshResult result = BuildAndWrite(testMesh, root, dataFolder, scene.name, warnings);

            if (!result.Success)
            {
                Debug.LogError($"[ArenaTestMesh] Üretilemedi: {result.Error}");
                return;
            }

            ArenaBoundary boundary = root.GetComponent<ArenaBoundary>();
            ArenaShapeBuilder.BindBoundary(boundary, result.DimensionsAsset, result.Geometry.WallRenderers, warnings);
            if (boundary == null)
            {
                warnings.Add("Kökte ArenaBoundary yok — boyut dosyasını ve duvarları ELLE bağla.");
            }

            Debug.Log(
                $"[ArenaTestMesh] '{testMesh.name}' → '{result.JsonPath}' → " +
                $"'{root.name}/{ArenaShapeBuilder.GeometryRootName}': {result.Plan.outline.Length} köşeli sınır + " +
                $"{result.Geometry.WallRenderers.Count} duvar + {result.Geometry.Columns.Count} kolon " +
                $"(yerel sınır {result.Geometry.LocalBounds.width:0.##}×{result.Geometry.LocalBounds.height:0.##} m).");

            for (int i = 0; i < warnings.Count; i++)
            {
                Debug.LogWarning("[ArenaTestMesh] " + warnings[i]);
            }

            Selection.activeGameObject = root.gameObject;
        }

        [MenuItem("Tools/VortexArena/Build Arena From TestMesh", true)]
        private static bool ValidateBuildFromSelection()
        {
            return FindSelectedTestMesh() != null;
        }

        /// <summary>
        /// Sahne yolundan arena kutusunun <c>Data/</c> klasörünü türetir
        /// (<c>.../&lt;Arena&gt;/Scenes/X.unity</c> → <c>.../&lt;Arena&gt;/Data</c>).
        /// </summary>
        private static string ResolveDataFolder(string scenePath)
        {
            string scenesFolder = Path.GetDirectoryName(scenePath)?.Replace('\\', '/');
            string boxFolder = Path.GetDirectoryName(scenesFolder)?.Replace('\\', '/');
            return string.IsNullOrEmpty(boxFolder) ? scenesFolder : boxFolder + "/Data";
        }

        /// <summary>
        /// Seçimdeki TestMesh: önce Project'teki prefab asset'i, yoksa sahnedeki ilk MeshRenderer
        /// taşıyan obje. Prefab önde çünkü sahne seçimi aynı zamanda arena kökü adayıdır — ikisi
        /// karışmasın.
        /// </summary>
        private static GameObject FindSelectedTestMesh()
        {
            GameObject[] selection = Selection.gameObjects;

            for (int i = 0; i < selection.Length; i++)
            {
                if (selection[i] != null && !selection[i].scene.IsValid())
                {
                    return selection[i];
                }
            }

            for (int i = 0; i < selection.Length; i++)
            {
                if (selection[i] != null && selection[i].GetComponentInChildren<MeshRenderer>(true) != null)
                {
                    return selection[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Arena kökü: <see cref="ArenaShapeBuilder.FindSelectedRoot"/>; sonuç TestMesh'in kendisi
        /// (ya da altındaki bir obje) ise sahnedeki <c>ArenaBoundary</c>'ye düşülür.
        /// </summary>
        private static Transform ResolveRoot(GameObject testMesh)
        {
            Transform root = ArenaShapeBuilder.FindSelectedRoot();
            if (root != null && root != testMesh.transform && !root.IsChildOf(testMesh.transform))
            {
                return root;
            }

            var boundary = UnityEngine.Object.FindFirstObjectByType<ArenaBoundary>(FindObjectsInactive.Include);
            return boundary != null ? boundary.transform : root;
        }
    }
}
