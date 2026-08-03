using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// Free-roam arena guard for a physical play space. The player moves 1:1 with their real body;
    /// this component watches the HMD position in the arena's local space and fades the screen —
    /// gently as the player nears the edge, to black once they step outside — plus shows a warning.
    /// Attach to an object positioned inside the arena, aligned with the arena's rotation.
    /// <para>
    /// <b>Arena ölçüsünün TEK kaynağı <see cref="dimensionsJson"/>'dur</b> (boyut dosyası,
    /// <see cref="ArenaDimensions"/> olarak çözülür). Alan dikdörtgen bile olsa dört köşeli bir
    /// <c>plane</c> halkası olarak yazılır — "dikdörtgense şu hızlı yol" ayrımı YOKTUR, aynı
    /// ölçünün iki ayrı ifadesi birbirinden sapıyordu. Sahnedeki <see cref="ArenaObstacle"/>'lar
    /// plana ek olarak hesaba girer.
    /// </para>
    /// <para>
    /// ⚠️ Boyut dosyası yoksa/okunamıyorsa muhafaza <b>kendini kapatır</b> — gerekçe
    /// <see cref="ResolvePlan"/>'de.
    /// </para>
    /// <para>
    /// ⚠️ <b>Yarı saydam muhafaza duvarı KALDIRILDI ve geri eklenmez.</b> Eskiden kenara
    /// yaklaşıldıkça belirginleşen bir duvar geometrisi vardı; arenanın gerçek duvarları
    /// environment sanatından geldiği için görevi göz zaten yapıyor. Mekanizma sanat duvarına
    /// TAŞINAMAZ da: alfa yazımı yalnız Transparent malzemede iş görür (gerçek duvarlar opak) ve
    /// Renderer'ı alfa düşünce kapatırdı — oyuncu uzaktayken duvar tümden kaybolurdu. Yaklaşma
    /// uyarısı bu yüzden karartma quad'ına taşındı (<see cref="warnFadeAlpha"/>): HMD'ye bağlı
    /// olduğu için arena geometrisinden tümden bağımsızdır.
    /// </para>
    /// <para>
    /// ⚠️ Bu bileşen <b>arena uzayının origin'i DEĞİLDİR</b>: ağ koordinatlarının sıfırı
    /// <b>dünya orijinidir</b> (<see cref="ArenaSpace"/> — arena uzayı dünya uzayıyla çakışıktır).
    /// Muhafaza objesini büyütmek ya da kaydırmak oyuncuların ağ konumunu etkilemez.
    /// </para>
    /// </summary>
    public class ArenaBoundary : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("HMD transform (CenterEyeAnchor). Falls back to Camera.main.")]
        [SerializeField] private Transform head;
        [Tooltip("Quad parented to the HMD used for the approach/out-of-bounds fade.")]
        [SerializeField] private Renderer fadeRenderer;
        [SerializeField] private TextMesh warningText;

        [Header("Arena size (meters)")]
        [Tooltip("Boyut dosyası (JSON, TextAsset) — arena ölçüsünün TEK kaynağı, ZORUNLUDUR. " +
                 "Dosya MEKAN başınadır: bir işletmenin tüm sahneleri aynı dosyayı gösterir. " +
                 "İşletmenin ölçüsü şeritmetreyle alınıp doğrudan bu dosyaya yazılır; alan " +
                 "dikdörtgen olsa bile dört köşeli bir 'plane' halkası olarak girilir. Boşsa " +
                 "muhafaza devre dışı kalır. " +
                 "Örnek: Assets/Arenas/Venues/VortexAntep/Data/VortexAntep_dimensions.json")]
        [SerializeField] private TextAsset dimensionsJson;

        [Header("Warning behaviour")]
        [Tooltip("Distance from the edge (m) where the approach fade starts.")]
        [SerializeField] private float warnDistance = 1f;
        [Tooltip("Fade alpha reached exactly AT the boundary (approach warning ceiling).")]
        [SerializeField] private float warnFadeAlpha = 0.35f;
        [Tooltip("Meters past the boundary at which the blackout is fully opaque.")]
        [SerializeField] private float fadeOutsideDistance = 0.3f;
        [SerializeField] private float maxFadeAlpha = 0.96f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock propertyBlock;

        /// <summary>True while the HMD is outside the allowed area.</summary>
        public bool IsOutOfBounds { get; private set; }

        /// <summary>
        /// Arena yarı ölçüleri (metre, X/Z) — admin kuş bakışı kadrajı bunu okur. Plandaki taban
        /// halkasının sınırlayıcı kutusundan gelir; plan yoksa <see cref="Vector2.zero"/>.
        /// </summary>
        public Vector2 HalfExtents
        {
            get
            {
                EnsurePlan();
                if (activePlan == null)
                {
                    return Vector2.zero;
                }

                Rect bounds = activePlan.LocalBounds();
                return new Vector2(bounds.width * 0.5f, bounds.height * 0.5f);
            }
        }

        /// <summary>
        /// Arena alanının YEREL uzaydaki merkezi (XZ, metre) = taban halkasının sınırlayıcı
        /// kutusunun merkezi. Plan yoksa <see cref="Vector2.zero"/>.
        /// <para>
        /// ⚠️ Kadrajlarken <see cref="HalfExtents"/> tek başına yetmez: ölçü genellikle bir köşeden
        /// alınır (plan sıfırı o köşedir), yani kutu bu transformun tam merkezinde DEĞİLDİR.
        /// </para>
        /// </summary>
        public Vector2 LocalCenter
        {
            get
            {
                EnsurePlan();
                return activePlan != null ? activePlan.LocalBounds().center : Vector2.zero;
            }
        }

        /// <summary>
        /// Mekanın iki kalibrasyon noktasını DÜNYA uzayında verir (zemin seviyesinde, bu
        /// transformun düzleminde). Dosyada nokta yoksa ya da ikisi birbirine çok yakınsa
        /// <c>false</c> döner.
        /// <para>
        /// ⚠️ Planı okuyan tek yer bu bileşendir; <c>ArenaCalibrator</c> boyut dosyasını kendi
        /// ayrıştırmaz, işaretçilerini buradan konumlandırır. Aksi hâlde aynı JSON iki kere
        /// çözülür ve ikisi birbirinden sapabilirdi.
        /// </para>
        /// </summary>
        public bool TryGetCalibrationMarks(out Vector3 worldA, out Vector3 worldB)
        {
            worldA = Vector3.zero;
            worldB = Vector3.zero;

            EnsurePlan();
            if (activePlan == null || !activePlan.HasCalibration)
            {
                return false;
            }

            worldA = LocalToWorld(activePlan.calibration.a);
            worldB = LocalToWorld(activePlan.calibration.b);
            return true;
        }

        // Gözlemci (admin) kipi: görsel muhafaza susar.
        private bool spectatorMode;

        // Plan önbelleği: JSON ayrıştırma ve halka dizileri kare başına yeniden kurulmasın
        // (Update her karede, gizmo her repaint'te çalışıyor — tahsis GC baskısı demek).
        private ArenaDimensions activePlan;      // çözülmüş plan (null = muhafaza devre dışı)
        private Vector2[] cachedPlane;           // activePlan.plane (hızlı erişim)
        private Vector2[][] cachedColumns;       // muhafazaya giren kolon halkaları
        private TextAsset cachedJsonSource;      // plan hangi TextAsset'ten çözüldü
        // ⚠️ Bir kez çözüldü mü: eksik/geçersiz bir JSON'da activePlan null kalır, bu bayrak olmasa
        // kare başına yeniden ayrıştırılır ve hata log'u sel olurdu.
        private bool planResolved;

        /// <summary>Muhafaza hesabına giren, döndürülmüş bir engel dikdörtgeni (yerel XZ).</summary>
        private struct ObstacleRect
        {
            public Vector2 Center;
            public Vector2 HalfSize;
            public float SinYaw;
            public float CosYaw;
        }

        private void Awake()
        {
            propertyBlock = new MaterialPropertyBlock();
            if (head == null && Camera.main != null)
                head = Camera.main.transform;
            if (warningText != null)
                warningText.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            // Alanlar çalışma anında (ya da devre dışıyken) değiştirilmiş olabilir: her etkinleşmede
            // sıfırdan çözülür, yoksa bayat bir plan taşınırdı.
            planResolved = false;
            ResolvePlan();
        }

        /// <summary>
        /// Gözlemci (admin) kipi. Görsel muhafazayı susturur: karartma quad'ı ve alan-dışı uyarısı
        /// kapanır, <see cref="IsOutOfBounds"/> false'a kilitlenir.
        /// <para>
        /// Gerekçe: admin masaüstündedir, HMD'si yoktur; kafası (kapatılmış rig'in
        /// CenterEyeAnchor'ı) sabit durduğu için muhafaza mantığı anlamsız veri üretir. Bileşen
        /// kapatılmak yerine susturulur ki <see cref="HalfExtents"/> / <see cref="LocalCenter"/>
        /// (kuş bakışı kadrajı) okunmaya devam edebilsin.
        /// </para>
        /// </summary>
        public void SetSpectatorMode(bool on)
        {
            spectatorMode = on;
            if (!on)
                return; // bir sonraki Update gerçek duruma göre yeniden çizer

            propertyBlock ??= new MaterialPropertyBlock();
            IsOutOfBounds = false;
            if (fadeRenderer != null)
                SetAlpha(fadeRenderer, 0f);
            if (warningText != null && warningText.gameObject.activeSelf)
                warningText.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (spectatorMode)
            {
                return; // muhafaza susuyor
            }

            EnsurePlan();
            if (activePlan == null)
            {
                // Plansız = muhafaza devre dışı (gerekçe ResolvePlan'de). Alan-dışı durumu ve
                // karartma sıfırlanır, uyarı yazısı kapanır — ölçüyü bilmeden "kenara ne kadar
                // yakınız" sorusunun cevabı yok, ekranı rastgele karartmak yanlış bilgi verirdi.
                IsOutOfBounds = false;
                if (fadeRenderer != null)
                    SetAlpha(fadeRenderer, 0f);
                if (warningText != null && warningText.gameObject.activeSelf)
                    warningText.gameObject.SetActive(false);
                return;
            }

            if (head == null)
                return;

            Vector3 local = transform.InverseTransformPoint(head.position);
            float edgeDistance = EdgeDistance(new Vector2(local.x, local.z));
            IsOutOfBounds = edgeDistance < 0f;

            if (fadeRenderer != null)
                SetAlpha(fadeRenderer, FadeAlphaFor(edgeDistance));
            if (warningText != null && warningText.gameObject.activeSelf != IsOutOfBounds)
                warningText.gameObject.SetActive(IsOutOfBounds);
        }

        /// <summary>
        /// Karartma alfası: içeride yaklaşma rampası, dışarıda tam karartmaya giden rampa.
        /// <para>
        /// İki dal sınırda (<paramref name="edgeDistance"/> = 0) aynı değeri —
        /// <see cref="warnFadeAlpha"/> — verdiği için geçiş SÜREKLİDİR. Bu, kaldırılan yarı saydam
        /// duvarın yerini alan tek uyarı kanalıdır: oyuncu sınırı geçmeden önce uyarılmalı, aksi
        /// hâlde gerçek duvara çarptıktan sonra haberi olurdu.
        /// </para>
        /// </summary>
        private float FadeAlphaFor(float edgeDistance)
        {
            if (edgeDistance >= 0f)
            {
                float warn = warnDistance > 0f ? Mathf.Clamp01(1f - edgeDistance / warnDistance) : 0f;
                return warn * warnFadeAlpha;
            }

            float outside = fadeOutsideDistance > 0f
                ? Mathf.Clamp01(-edgeDistance / fadeOutsideDistance)
                : 1f;
            return Mathf.Lerp(warnFadeAlpha, maxFadeAlpha, outside);
        }

        // -------------------------------------------------------------- mesafe hesabı

        /// <summary>
        /// Verilen YEREL XZ noktasının "en yakın tehlikeye" işaretli uzaklığı: <b>artı</b> = güvenli
        /// alanın içinde ve o kadar metre payı var, <b>eksi</b> = dışarıda (ya da bir engelin
        /// içinde) ve o kadar metre içeri girmiş. Karartma ve uyarı bu tek sayıdan türer.
        /// <para>
        /// ⚠️ Yalnız plan varken çağrılır — çağıran (<c>Update</c>) plansız durumu zaten erken
        /// çıkışla eliyor.
        /// </para>
        /// </summary>
        private float EdgeDistance(Vector2 point)
        {
            float distance = Polygon2D.SignedDistance(cachedPlane, point);

            if (cachedColumns != null)
            {
                for (int i = 0; i < cachedColumns.Length; i++)
                {
                    distance = Mathf.Min(distance, Polygon2D.ObstacleDistance(cachedColumns[i], point));
                }
            }

            // Sahnedeki engeller her karede yeniden okunur: taşınabilir objelerdir, önbelleklenirse
            // sessizce eski yerlerinde uyarı üretirler. Liste indeksle gezilir (foreach bir
            // arayüz numaralandırıcısı kutulardı).
            IReadOnlyList<ArenaObstacle> obstacles = ArenaObstacle.All;
            for (int i = 0; i < obstacles.Count; i++)
            {
                ArenaObstacle obstacle = obstacles[i];
                if (obstacle == null)
                {
                    continue;
                }

                obstacle.GetLocalRect(transform, out Vector2 center, out Vector2 size, out float yaw);
                distance = Mathf.Min(distance, DistanceToRect(point, MakeRect(center, size, yaw)));
            }

            return distance;
        }

        /// <summary>
        /// Noktanın döndürülmüş bir engel dikdörtgenine işaretli uzaklığı: dışarıdaysa + (kutuya
        /// olan mesafe), içerideyse − (en yakın yüzeye olan derinlik). Sınır hesabıyla aynı işaret
        /// sözleşmesini kullanır, böylece ikisi tek bir <c>Mathf.Min</c> ile birleşebilir.
        /// <para>
        /// ⚠️ Yalnız <see cref="ArenaObstacle"/> için kalmıştır — plandaki kolonlar artık çokgendir
        /// ve <see cref="Polygon2D.ObstacleDistance"/> ile ölçülür. Sahneye elle konan dekorun
        /// gösterimi ise dikdörtgen kaldı: taşınabilir bir objenin ölçüsünü tek bir alandan
        /// (<c>Size</c>) okumak, ona ayrıca bir köşe listesi yazdırmaktan basit.
        /// </para>
        /// </summary>
        private static float DistanceToRect(Vector2 point, in ObstacleRect rect)
        {
            // Noktayı dikdörtgenin kendi eksenine taşı (yaw kadar ters döndür).
            Vector2 delta = point - rect.Center;
            Vector2 localPoint = new Vector2(
                delta.x * rect.CosYaw - delta.y * rect.SinYaw,
                delta.x * rect.SinYaw + delta.y * rect.CosYaw);

            float dx = Mathf.Abs(localPoint.x) - rect.HalfSize.x;
            float dy = Mathf.Abs(localPoint.y) - rect.HalfSize.y;

            if (dx > 0f || dy > 0f)
            {
                float outsideX = Mathf.Max(dx, 0f);
                float outsideY = Mathf.Max(dy, 0f);
                return Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY);
            }

            // İçerideyiz: en yakın yüzeye olan mesafe (negatif).
            return Mathf.Max(dx, dy);
        }

        private static ObstacleRect MakeRect(Vector2 center, Vector2 size, float yaw)
        {
            // Ters dönüş açısı saklanır (nokta dikdörtgenin eksenine taşınacak).
            float radians = -yaw * Mathf.Deg2Rad;
            return new ObstacleRect
            {
                Center = center,
                HalfSize = new Vector2(Mathf.Abs(size.x) * 0.5f, Mathf.Abs(size.y) * 0.5f),
                SinYaw = Mathf.Sin(radians),
                CosYaw = Mathf.Cos(radians)
            };
        }

        /// <summary>
        /// Önbelleği yalnız <b>kaynak referansı değiştiyse</b> (ya da hiç çözülmediyse) tazeler.
        /// <para>
        /// ⚠️ Ayrıştırma/hata burada değil <see cref="ResolvePlan"/> içinde: bu metot her karede
        /// (Update) ve her repaint'te (gizmo) çağrılıyor, koşul olmasa JSON kare başına
        /// ayrıştırılırdı.
        /// </para>
        /// </summary>
        private void EnsurePlan()
        {
            if (!planResolved || cachedJsonSource != dimensionsJson)
            {
                ResolvePlan();
            }
        }

        /// <summary>
        /// Planı tek kaynaktan — <see cref="dimensionsJson"/> — çözer. Kolon halkaları burada bir
        /// kez toplanır: kaynak çalışma anında değişmez, ama <c>Update</c> her karede koşar.
        /// <para>
        /// ⚠️ Dosya bağlanmamışsa ya da ayrıştırılamıyorsa <b>açık başarısızlık</b> seçilir:
        /// konsola bir kez hata basılır ve muhafaza tümden susar. Gerekçe: ölçüsü bilinmeyen bir
        /// arenada zaten doğru bir muhafaza üretilemez; kapalı başarısızlık (ör. her karede ekranı
        /// karartmak) işletmede oyunu tümden oynanamaz kılardı. Bu bir KURULUM hatasıdır ve
        /// editörde/QA'da yakalanır — sahadaki oturumu düşürmemesi gerekir.
        /// </para>
        /// </summary>
        private void ResolvePlan()
        {
            cachedJsonSource = dimensionsJson;
            planResolved = true;

            activePlan = null;
            cachedPlane = null;
            cachedColumns = null;

            activePlan = ArenaDimensions.FromTextAsset(dimensionsJson, out string error);

            if (activePlan == null)
            {
                string reason = string.IsNullOrEmpty(error) ? string.Empty : " — " + error;
                Debug.LogError(
                    $"[ArenaBoundary] '{name}': boyut dosyası (dimensionsJson) bağlanmamış ya da " +
                    $"okunamadı{reason}. Muhafaza DEVRE DIŞI. Arena ölçüsünün tek kaynağı bu dosyadır.",
                    this);
                return;
            }

            cachedPlane = activePlan.plane;

            ArenaDimensions.Column[] columns = activePlan.columns;
            if (columns == null || columns.Length == 0)
            {
                return;
            }

            // Parse geçersiz halkalı kolonları zaten ayıkladı; burada yalnız halkalar toplanır.
            cachedColumns = new Vector2[columns.Length][];
            for (int i = 0; i < columns.Length; i++)
            {
                cachedColumns[i] = columns[i].points;
            }
        }

        // ------------------------------------------------------------------ gizmo

        /// <summary>
        /// Seçiliyken planı çizer: taban halkası + kolon prizmaları (yerel uzayda hesaplanıp
        /// dünyaya taşınır). Yamuk bir arenayı elle ayarlarken bu çizim şarttır — plan sayı
        /// listesidir, sahnede karşılığını görmeden köşe taşımak körlemedir.
        /// <para>
        /// ⚠️ Plan yoksa HİÇBİR ŞEY çizilmez: uydurulmuş bir kutu, muhafazanın aslında devre dışı
        /// olduğunu gizlerdi. Sebebi konsoldaki hata satırıdır.
        /// </para>
        /// <para>
        /// ⚠️ Sınırın ÜST kenarı artık çizilmez: duvar yüksekliği alanı kaldırıldı (duvar üretimi
        /// de duvar göstergesi de yok), okuyanı olmayan bir ölçü bayatlardı.
        /// </para>
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            EnsurePlan();

            if (activePlan == null)
            {
                return;
            }

            Vector2[] ring = activePlan.plane;
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                Gizmos.DrawLine(LocalToWorld(ring[j]), LocalToWorld(ring[i]));
            }

            // Kalibrasyon noktaları: zemin bandının nereye çekileceğini sahnede göstermenin tek
            // yolu bu — işaretçi objeleri sahnede KAPALI durur (yalnız kalibrasyon sırasında
            // açılırlar), yani gizmo olmadan yerleri gözle denetlenemez.
            if (activePlan.HasCalibration)
            {
                Vector3 markA = LocalToWorld(activePlan.calibration.a);
                Vector3 markB = LocalToWorld(activePlan.calibration.b);
                Gizmos.color = new Color(0.35f, 1f, 0.45f, 0.9f);
                Gizmos.DrawLine(markA, markB);
                Gizmos.DrawWireSphere(markA, 0.12f);
                Gizmos.DrawWireSphere(markB, 0.2f); // B daha büyük: A→B yönü gizmodan okunabilsin
            }

            ArenaDimensions.Column[] columns = activePlan.columns;
            if (columns == null)
            {
                return;
            }

            Gizmos.color = new Color(0.95f, 0.55f, 0.15f, 0.9f);

            for (int i = 0; i < columns.Length; i++)
            {
                Vector2[] footprint = columns[i].points;
                if (!Polygon2D.IsValid(footprint))
                {
                    continue;
                }

                float height = activePlan.HeightOf(columns[i]);
                for (int k = 0, m = footprint.Length - 1; k < footprint.Length; m = k++)
                {
                    Gizmos.DrawLine(LocalToWorld(footprint[m]), LocalToWorld(footprint[k]));
                    Gizmos.DrawLine(LocalToWorld(footprint[m], height), LocalToWorld(footprint[k], height));
                    Gizmos.DrawLine(LocalToWorld(footprint[k]), LocalToWorld(footprint[k], height));
                }
            }
        }

        private Vector3 LocalToWorld(Vector2 localPoint, float height = 0f)
        {
            return transform.TransformPoint(new Vector3(localPoint.x, height, localPoint.y));
        }

        private void SetAlpha(Renderer target, float alpha)
        {
            target.GetPropertyBlock(propertyBlock);
            Color color = target.sharedMaterial != null && target.sharedMaterial.HasProperty(BaseColorId)
                ? target.sharedMaterial.GetColor(BaseColorId)
                : Color.white;
            color.a = alpha;
            propertyBlock.SetColor(BaseColorId, color);
            target.SetPropertyBlock(propertyBlock);
            target.enabled = alpha > 0.001f;
        }
    }
}
