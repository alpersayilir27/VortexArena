using UnityEngine;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// 2B çokgen matematiği — <b>sıralı köşe halkası</b> (<c>ring</c>) üstünde içerik testi,
    /// mesafe, sınırlayıcı kutu ve doğrulama. Saf hesap: sahne, asset ya da bileşen bilmez.
    /// <para>
    /// Hem çalışma anında (<see cref="ArenaBoundary"/> muhafaza mesafesi) hem editörde (maket
    /// üretimi/geri okuması) kullanılır. Arena ölçüsünün tek temsili
    /// <see cref="ArenaDimensions"/>'daki halkalar olduğu için, o halkalara sorulan her geometrik
    /// sorunun cevabı burada toplanır.
    /// </para>
    /// <para>
    /// ⚠️ <b>Halka KAPALIDIR:</b> son köşe ilk köşeye kendiliğinden bağlanır — dizide ilk noktayı
    /// sona tekrarlama. Sarım yönü (saat yönü / tersi) hiçbir metotta önemsenmez.
    /// </para>
    /// <para>
    /// ⚠️ <b>Çokgen BİRLEŞİMİ (union) bilinçli olarak YOKTUR ve eklenmez.</b> Ne taban ne kolon
    /// parçalardan birleştirilir; ikisi de tek halkadır. İçbükeylik bunun için engel değil (L
    /// şekli, yamuk, girintili duvar tek halkayla ifade edilir ve buradaki testler içbükeyde de
    /// doğrudur). Birleşimin satacağı tek şey "alanı üst üste binen dikdörtgenler olarak yaz"
    /// rahatlığıydı; bedeli düzlemsel düzenleme + dış yüz yürüyüşü kadar kenar-durumu yoğun bir
    /// kod ve onun <see cref="ArenaBoundary"/> yüzünden sahne açılışında da koşması olurdu.
    /// </para>
    /// <para>
    /// ⚠️ Metotlar <b>tahsis yapmaz</b> (<see cref="Bounds"/> dahil): muhafaza her karede
    /// çağırıyor, geçici dizi/liste GC baskısı demek.
    /// </para>
    /// </summary>
    public static class Polygon2D
    {
        /// <summary>Geçerli bir çokgen için gereken en az köşe sayısı.</summary>
        public const int MinPoints = 3;

        /// <summary>Halka kullanılabilir mi (null değil ve en az bir üçgen).</summary>
        public static bool IsValid(Vector2[] ring)
        {
            return ring != null && ring.Length >= MinPoints;
        }

        // ------------------------------------------------------------------ içerik

        /// <summary>
        /// Nokta halkanın içinde mi (ray casting: noktadan +X yönüne giden ışın kaç kenarı
        /// kesiyor — tek sayı = içeride). İçbükey çokgende de doğrudur.
        /// <para>
        /// Sınırın tam üstündeki bir nokta için sonuç tanımsızdır (kayan noktaya bağlı) —
        /// muhafaza kararları <see cref="SignedDistance"/> üstünden verildiği için bu sorun
        /// değil: orada sınır 0'dır ve iki tarafa da eşit uzaklıktadır.
        /// </para>
        /// </summary>
        public static bool Contains(Vector2[] ring, Vector2 point)
        {
            if (!IsValid(ring))
            {
                return false;
            }

            bool inside = false;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                Vector2 a = ring[i];
                Vector2 b = ring[j];

                if ((a.y > point.y) != (b.y > point.y) &&
                    point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        // ------------------------------------------------------------------ mesafe

        /// <summary>
        /// Noktanın en yakın KENAR PARÇASINA uzaklığı (işaretsiz, her zaman ≥ 0).
        /// <para>
        /// Kenar doğrularına değil <b>parçalarına</b> bakılır — köşe yakınında da doğru sonuç
        /// verir. Halka geçersizse <see cref="float.MaxValue"/> döner (çağıran
        /// <c>Mathf.Min</c> zincirinde kullanıyorsa etkisiz kalsın diye).
        /// </para>
        /// </summary>
        public static float DistanceToRing(Vector2[] ring, Vector2 point)
        {
            if (!IsValid(ring))
            {
                return float.MaxValue;
            }

            float minDistance = float.MaxValue;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                float distance = DistanceToSegment(point, ring[i], ring[j]);
                if (distance < minDistance)
                {
                    minDistance = distance;
                }
            }

            return minDistance;
        }

        /// <summary>
        /// <b>Muhafaza sözleşmesi (alan):</b> içerideyse <b>+</b> (kenara o kadar metre pay var),
        /// dışarıdaysa <b>−</b> (o kadar metre dışarı çıkılmış). Büyüklük her iki durumda da en
        /// yakın kenar parçasına olan mesafedir.
        /// </summary>
        public static float SignedDistance(Vector2[] ring, Vector2 point)
        {
            if (!IsValid(ring))
            {
                return float.MaxValue;
            }

            float distance = DistanceToRing(ring, point);
            return Contains(ring, point) ? distance : -distance;
        }

        /// <summary>
        /// <b>Muhafaza sözleşmesi (engel):</b> <see cref="SignedDistance"/>'ın işaretçe tersi —
        /// engelin dışındaysa <b>+</b> (engele o kadar metre var), içindeyse <b>−</b> (o kadar
        /// metre içine girilmiş).
        /// <para>
        /// İki ayrı sözleşmenin sebebi, muhafazanın ikisini tek bir <c>Mathf.Min</c> ile
        /// birleştirmesidir: her ikisinde de "artı = güvenli pay" demek. Alan için güvenli olan
        /// içerisi, engel için dışarısıdır.
        /// </para>
        /// </summary>
        public static float ObstacleDistance(Vector2[] ring, Vector2 point)
        {
            if (!IsValid(ring))
            {
                return float.MaxValue;
            }

            float distance = DistanceToRing(ring, point);
            return Contains(ring, point) ? -distance : distance;
        }

        /// <summary>Noktanın <c>a</c>–<c>b</c> doğru parçasına uzaklığı.</summary>
        public static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSq = ab.sqrMagnitude;
            if (lengthSq < 1e-8f)
            {
                return Vector2.Distance(point, a); // dejenere kenar (üst üste iki köşe)
            }

            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSq);
            return Vector2.Distance(point, a + ab * t);
        }

        // ------------------------------------------------------------------ ölçü

        /// <summary>
        /// Halkanın XZ sınırlayıcı kutusu. Halka geçersizse sıfır kutu döner.
        /// <para>
        /// ⚠️ Kadrajlarken kutunun <b>merkezi</b> de gerekir: ölçü genellikle bir köşeden alınır,
        /// yani kutu transformun tam merkezinde DEĞİLDİR.
        /// </para>
        /// </summary>
        public static Rect Bounds(Vector2[] ring)
        {
            if (!IsValid(ring))
            {
                return new Rect(0f, 0f, 0f, 0f);
            }

            float minX = ring[0].x, maxX = ring[0].x;
            float minY = ring[0].y, maxY = ring[0].y;

            for (int i = 1; i < ring.Length; i++)
            {
                minX = Mathf.Min(minX, ring[i].x);
                maxX = Mathf.Max(maxX, ring[i].x);
                minY = Mathf.Min(minY, ring[i].y);
                maxY = Mathf.Max(maxY, ring[i].y);
            }

            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        /// <summary>İşaretli alan (sarım yönüne göre + / −). Mutlak değeri gerçek alandır.</summary>
        public static float SignedArea(Vector2[] ring)
        {
            if (!IsValid(ring))
            {
                return 0f;
            }

            float sum = 0f;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                sum += (ring[j].x * ring[i].y) - (ring[i].x * ring[j].y);
            }

            return sum * 0.5f;
        }

        /// <summary>
        /// Halkanın ağırlık merkezi — maket üretiminde kolon prizmasının <b>pivotu</b> olur
        /// (kolonu Move tool ile sürüklemek doğal olsun diye).
        /// <para>
        /// Alan sıfıra yakınsa (dejenere halka) köşelerin aritmetik ortalamasına düşer: ağırlık
        /// merkezi formülü orada sıfıra bölerdi.
        /// </para>
        /// </summary>
        public static Vector2 Centroid(Vector2[] ring)
        {
            if (!IsValid(ring))
            {
                return Vector2.zero;
            }

            float area = SignedArea(ring);
            if (Mathf.Abs(area) < 1e-6f)
            {
                Vector2 sum = Vector2.zero;
                for (int i = 0; i < ring.Length; i++)
                {
                    sum += ring[i];
                }

                return sum / ring.Length;
            }

            float cx = 0f, cy = 0f;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                float cross = (ring[j].x * ring[i].y) - (ring[i].x * ring[j].y);
                cx += (ring[j].x + ring[i].x) * cross;
                cy += (ring[j].y + ring[i].y) * cross;
            }

            float scale = 1f / (6f * area);
            return new Vector2(cx * scale, cy * scale);
        }

        // -------------------------------------------------------------- doğrulama

        /// <summary>
        /// Halka kendi kendini kesiyor mu — <b>yalnız doğrulama içindir</b>, muhafaza bunu
        /// çağırmaz.
        /// <para>
        /// Köşeleri yanlış sırada yazılmış bir alan (kelebek/kum saati şekli) sessizce yanlış bir
        /// bölge tanımlar: <see cref="Contains"/> ona da bir cevap üretir, ama cevap ölçülen odayı
        /// temsil etmez. Üretim araçları bunu yakalayıp uyarır.
        /// </para>
        /// <para>
        /// O(n²) — köşe sayısı bir odanın köşeleri kadar (onlar mertebesinde) olduğu için editörde
        /// sorun değil.
        /// </para>
        /// </summary>
        public static bool IsSelfIntersecting(Vector2[] ring)
        {
            if (!IsValid(ring))
            {
                return false;
            }

            int count = ring.Length;
            for (int i = 0; i < count; i++)
            {
                Vector2 a1 = ring[i];
                Vector2 a2 = ring[(i + 1) % count];

                for (int j = i + 1; j < count; j++)
                {
                    // Komşu kenarlar ortak köşede zaten "kesişir" — atlanır. (i, j) = (0, n-1)
                    // çifti de komşudur: halka kapalı olduğu için son kenar ilkine bağlanıyor.
                    if (j == i + 1 || (i == 0 && j == count - 1))
                    {
                        continue;
                    }

                    if (SegmentsIntersect(a1, a2, ring[j], ring[(j + 1) % count]))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>İki doğru parçası kesişiyor mu (uç noktalar dahil, yön işaretleriyle).</summary>
        private static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
        {
            float d1 = Cross(q1, q2, p1);
            float d2 = Cross(q1, q2, p2);
            float d3 = Cross(p1, p2, q1);
            float d4 = Cross(p1, p2, q2);

            if (((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) &&
                ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f)))
            {
                return true;
            }

            // Doğrusal (collinear) durumlar: bir uç noktanın ötekinin üstünde olması.
            if (Mathf.Approximately(d1, 0f) && OnSegment(q1, q2, p1)) return true;
            if (Mathf.Approximately(d2, 0f) && OnSegment(q1, q2, p2)) return true;
            if (Mathf.Approximately(d3, 0f) && OnSegment(p1, p2, q1)) return true;
            if (Mathf.Approximately(d4, 0f) && OnSegment(p1, p2, q2)) return true;

            return false;
        }

        /// <summary><c>a</c>→<c>b</c> ile <c>a</c>→<c>p</c> çapraz çarpımı (işaret = dönüş yönü).</summary>
        private static float Cross(Vector2 a, Vector2 b, Vector2 p)
        {
            return ((b.x - a.x) * (p.y - a.y)) - ((b.y - a.y) * (p.x - a.x));
        }

        /// <summary><c>p</c> doğrusal olduğu bilinen <c>a</c>–<c>b</c> parçasının sınırları içinde mi.</summary>
        private static bool OnSegment(Vector2 a, Vector2 b, Vector2 p)
        {
            return p.x >= Mathf.Min(a.x, b.x) && p.x <= Mathf.Max(a.x, b.x) &&
                   p.y >= Mathf.Min(a.y, b.y) && p.y <= Mathf.Max(a.y, b.y);
        }
    }
}
