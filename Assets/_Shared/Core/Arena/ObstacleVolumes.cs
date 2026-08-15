using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// "Bu nokta bir <b>iç engelin</b> içinde mi" sorusunun <b>tek</b> cevabı
    /// (<see cref="ArenaLayers.ObstacleName"/> layer'ı, Docs/ArenaNet-Protokol.md §10.9).
    ///
    /// <para><b>Neden ayrı bir sınıf:</b> aynı soruyu üç sistem soruyor —
    /// <c>ObstacleViolationProbe</c> (kafa/el engelin içinde mi → ceza + ateş kapısı),
    /// <c>ArenaCombat.IsMuzzleBlocked</c> (namlu) ve <c>ArenaCombat.IsWeaponBlocked</c> (silahın
    /// gövdesi, <see cref="OverlapsBox"/>). Layer maskesi, konvekslik süzgeci, hata satırı ve
    /// "ClosestPoint içeride noktanın kendisini döner" bilgisi çağıran başına kopyalansaydı biri
    /// kaçınılmaz olarak sapardı; belirti de "engel bazen çalışmıyor" olurdu.</para>
    ///
    /// <para>⚠️ <b>Engel collider'ı KONVEKS olmak zorundadır</b> (Box/Sphere/Capsule ya da
    /// <c>MeshCollider</c> + <c>Convex</c>). Sebep hayati: <see cref="Collider.ClosestPoint"/>
    /// non-convex bir <c>MeshCollider</c>'da <b>girdi noktasını AYNEN döndürür</b> → her nokta
    /// "içeride" okunur → o sahnedeki <b>herkes anında ölmeye başlar</b>. Böyle bir collider burada
    /// kalıcı olarak <b>yok sayılır</b> (açık başarısızlık: bir hata satırı + hiç ceza yok) ve
    /// editör tarafında ayrıca taranır (<c>Engel Hacimlerini Denetle</c>).</para>
    ///
    /// <para><b>İki kullanım biçimi vardır ve tamponları AYRIDIR:</b>
    /// <see cref="Sample"/> + <see cref="Contains(Vector3,int)"/> bir kez sorgulayıp <b>çok nokta</b>
    /// test etmek içindir (gövde ölçümü: tek physics sorgusu, 20+ nokta) ve önbelleği
    /// <b>son <see cref="Sample"/> çağıranındır</b>; <see cref="ContainsPoint"/> tek atışlıktır ve o
    /// önbelleğe hiç dokunmaz — atış yolu gövde ölçümünün turunu bozamaz.</para>
    /// </summary>
    public static class ObstacleVolumes
    {
        /// <summary>Aday engel tavanı. Aşılırsa fazlası yok sayılır — bir oyuncunun etrafında aynı
        /// anda sekizden çok engel olması sahne kurulumu hatasıdır.</summary>
        public const int MaxCandidates = 8;

        /// <summary>Tek nokta sorgusunun yarıçapı (m). Sıfır yarıçaplı küre bazı sürücülerde hiç
        /// çakışma bildirmiyor; kesin kararı zaten <see cref="Collider.ClosestPoint"/> veriyor,
        /// bu yarıçap yalnız aday toplamak için.</summary>
        private const float PointQueryRadius = 0.01f;

        private static readonly Collider[] Candidates = new Collider[MaxCandidates];
        private static readonly Bounds[] CandidateBounds = new Bounds[MaxCandidates];
        private static readonly Collider[] PointCandidates = new Collider[MaxCandidates];
        private static readonly Collider[] BoxCandidates = new Collider[MaxCandidates];
        private static readonly Collider[] ClearanceCandidates = new Collider[MaxCandidates];

        /// <summary>Konveks olmadığı için elenen collider'lar — uyarı bir kez basılsın diye.</summary>
        private static readonly HashSet<int> Rejected = new HashSet<int>();

        /// <summary>
        /// "İçeride" diyen son collider — yalnız <b>teşhis</b> içindir (hangi engel tetikledi).
        /// Kural yazarken buna bakma: cevap <see cref="Contains(Vector3,int)"/>'in dönüşüdür.
        /// </summary>
        public static Collider LastHit { get; private set; }

        /// <summary>
        /// Verilen küre içindeki engel adaylarını toplar ve sınırlayıcı kutularını <b>tur başına bir
        /// kez</b> okur (<see cref="Collider.bounds"/> her erişimde native'e iner). Dönüş, sonraki
        /// <see cref="Contains(Vector3,int)"/> çağrılarına verilecek aday sayısıdır.
        /// </summary>
        public static int Sample(Vector3 center, float radius)
        {
            int mask = ArenaLayers.ObstacleMask;
            if (mask == 0)
            {
                return 0; // layer tanımsız — ArenaLayers zaten bir kez bağırdı
            }

            int count = Physics.OverlapSphereNonAlloc(center, radius, Candidates, mask,
                QueryTriggerInteraction.Ignore);
            if (count > MaxCandidates)
            {
                count = MaxCandidates;
            }

            int kept = 0;
            for (int i = 0; i < count; i++)
            {
                Collider collider = Candidates[i];
                if (collider == null || !IsUsable(collider))
                {
                    continue;
                }

                Candidates[kept] = collider;
                CandidateBounds[kept] = collider.bounds;
                kept++;
            }

            return kept;
        }

        /// <summary>
        /// Nokta, <see cref="Sample"/>'ın topladığı adaylardan <b>herhangi birinin</b> içinde mi
        /// (birlik semantiği): iki kutunun ek yerinde duran kafa aksi hâlde "hiçbirinin tam içinde
        /// değil" diye kaçardı.
        /// </summary>
        public static bool Contains(Vector3 point, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (!CandidateBounds[i].Contains(point))
                {
                    continue; // ucuz AABB elemesi — noktaların çoğu buradan döner
                }

                Collider collider = Candidates[i];
                if (collider == null || !IsPointInside(collider, point))
                {
                    continue;
                }

                LastHit = collider;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Tek nokta için sorgu + test (kendi tamponu). Atış yolu bunu kullanır: atış başına bir
        /// physics sorgusu, 600 RPM'de saniyede on sorgu.
        /// </summary>
        public static bool ContainsPoint(Vector3 point)
        {
            int mask = ArenaLayers.ObstacleMask;
            if (mask == 0)
            {
                return false;
            }

            int count = Physics.OverlapSphereNonAlloc(point, PointQueryRadius, PointCandidates, mask,
                QueryTriggerInteraction.Ignore);
            if (count > MaxCandidates)
            {
                count = MaxCandidates;
            }

            for (int i = 0; i < count; i++)
            {
                Collider collider = PointCandidates[i];
                if (collider == null || !IsUsable(collider) || !IsPointInside(collider, point))
                {
                    continue;
                }

                LastHit = collider;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Noktanın en yakın engel <b>YÜZEYİNE</b> uzaklığı (m), <paramref name="maxDistance"/> ile
        /// tavanlanmış: o yarıçapta hiç engel yoksa <paramref name="maxDistance"/> döner, nokta bir
        /// engelin <b>içindeyse</b> <c>0</c>.
        ///
        /// <para><b>Neden ayrı bir soru:</b> <see cref="Contains(Vector3,int)"/> "içeride mi" der ve
        /// bu, <b>görüşün</b> kapatılması için geç bir cevaptır — kameranın kırpma düzlemi göz
        /// noktasının birkaç santim ÖNÜNDEDİR, yani geometri göz henüz dışarıdayken kırpılmaya
        /// başlar ve katı cismin içi okunur. Kırpmadan önce karar verebilmenin tek yolu yüzeye olan
        /// gerçek uzaklıktır (tüketicisi <c>ObstacleViolationProbe</c>'un karartma kapısı).</para>
        ///
        /// <para>⚠️ Ölçüm <b>yalnız dışarıdan</b> anlamlıdır: <see cref="Collider.ClosestPoint"/>
        /// içerideki bir nokta için noktanın kendisini döner, yani içeride sonuç kaçınılmaz olarak
        /// <c>0</c>'dır. Bu bir kayıp değil, aranan cevaptır — içerisi zaten en yakın hâldir.</para>
        ///
        /// <para>⚠️ <see cref="LastHit"/>'e <b>yazmaz</b>: o alan "içeride diyen son collider"
        /// teşhisidir ve her karede koşan bir yakınlık ölçümü onu sürekli ezerdi.</para>
        ///
        /// <para>Kendi tamponu vardır: <see cref="Sample"/>'ın önbelleğine dokunmaz (gövde ölçümünün
        /// turu bozulamaz — sınıf özetindeki "tamponlar AYRIDIR" sözleşmesi).</para>
        /// </summary>
        public static float DistanceToSurface(Vector3 point, float maxDistance)
        {
            int mask = ArenaLayers.ObstacleMask;
            if (mask == 0)
            {
                return maxDistance; // layer tanımsız — ArenaLayers zaten bir kez bağırdı
            }

            int count = Physics.OverlapSphereNonAlloc(point, maxDistance, ClearanceCandidates, mask,
                QueryTriggerInteraction.Ignore);
            if (count > MaxCandidates)
            {
                count = MaxCandidates;
            }

            float nearest = maxDistance;
            for (int i = 0; i < count; i++)
            {
                Collider collider = ClearanceCandidates[i];
                if (collider == null || !IsUsable(collider))
                {
                    continue;
                }

                float distance = Vector3.Distance(collider.ClosestPoint(point), point);
                if (distance < nearest)
                {
                    nearest = distance;
                    if (nearest <= 0f)
                    {
                        break; // içerideyiz — daha yakını yok
                    }
                }
            }

            return nearest;
        }

        /// <summary>
        /// Yönlendirilmiş bir <b>kutu</b> herhangi bir engelle kesişiyor mu. Tek tüketicisi silahın
        /// gövde testidir: "namlu içeride mi" sorusu tek noktadır, "silahın herhangi bir parçası
        /// değiyor mu" ise bir <b>hacim</b> sorusudur ve nokta örneklemesiyle cevaplanamaz (dipçikle
        /// duvara değen silah hiçbir örnek noktasını içeride bulmayabilir).
        /// <para>⚠️ Buradaki testin <see cref="Contains(Vector3,int)"/>'ten farkı, <b>konveks olmayan
        /// collider'ları da doğru yanıtlamasıdır</b>: kutu-mesh kesişimi <c>ClosestPoint</c>'e
        /// dayanmaz, yani o API'nin "her nokta içeride" yalanı buraya bulaşmaz. Bu yüzden konvekslik
        /// süzgeci uygulanmaz — kural olarak layer konveks olmalıdır, ama olmayan bir collider
        /// burada <b>sessizce yanlış cevap vermek yerine</b> doğru cevap verir.</para>
        /// </summary>
        /// <param name="center">Kutunun DÜNYA merkezi.</param>
        /// <param name="halfExtents">Kutunun yarı ölçüleri (dünya birimi).</param>
        /// <param name="rotation">Kutunun dünya rotasyonu.</param>
        public static bool OverlapsBox(Vector3 center, Vector3 halfExtents, Quaternion rotation)
        {
            int mask = ArenaLayers.ObstacleMask;
            if (mask == 0)
            {
                return false;
            }

            int count = Physics.OverlapBoxNonAlloc(center, halfExtents, BoxCandidates, rotation, mask,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count && i < MaxCandidates; i++)
            {
                if (BoxCandidates[i] == null)
                {
                    continue;
                }

                LastHit = BoxCandidates[i];
                return true;
            }

            return false;
        }

        /// <summary>
        /// Konveks collider'da <b>içerideki</b> bir nokta için <see cref="Collider.ClosestPoint"/>
        /// noktanın KENDİSİNİ döner. ⚠️ Tersi ölçülemez: içeriden yüzey mesafesi bu API ile
        /// alınamaz, o yüzden "merkez yüzeye şu kadar uzak" gibi bir derinlik hesabı yazılamaz —
        /// derinlik ancak <b>birden çok nokta</b> örnekleyerek yaklaşıklanır (kafa küresi böyle
        /// çalışıyor).
        /// </summary>
        private static bool IsPointInside(Collider collider, Vector3 point) =>
            (collider.ClosestPoint(point) - point).sqrMagnitude <= 1e-8f;

        /// <summary>
        /// ⚠️ <b>Non-convex <see cref="MeshCollider"/> KULLANILAMAZ</b> (gerekçe sınıf özetinde).
        /// Böyle bir collider kalıcı olarak yok sayılır ve bir kez rapor edilir — açık
        /// başarısızlık, sessiz katliam değil.
        /// </summary>
        private static bool IsUsable(Collider collider)
        {
            if (collider is not MeshCollider mesh || mesh.convex)
            {
                return true;
            }

            if (Rejected.Add(mesh.GetInstanceID()))
            {
                Debug.LogError(
                    $"[ObstacleVolumes] '{mesh.name}' objesi '{ArenaLayers.ObstacleName}' layer'ında " +
                    "ama collider'ı KONVEKS DEĞİL (MeshCollider + Convex kapalı). Bu obje engel " +
                    "hesabından ÇIKARILDI — nokta-içeride testi non-convex mesh'te her zaman " +
                    "'içeride' der ve tüm oyuncuları anında öldürürdü. Convex işaretle ya da kaba bir " +
                    "Box/Capsule collider kullan.", mesh);
            }

            return false;
        }
    }
}
