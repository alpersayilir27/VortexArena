using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// <b>İsabet göstergesinin (<see cref="HitMarker"/>) görünüm ayarları</b> — boy, saydamlık,
    /// ömür, kontur, materyal ve (istenirse) görünümün tamamının yerini alan bir prefab.
    ///
    /// <para><b>Nerede yaşar:</b> <c>Assets/_Shared/Data/Resources/HitMarkerStyle.asset</c>.
    /// <see cref="WeaponCatalog"/> ile aynı gerekçe: <see cref="HitMarker"/> kendini önyükleyen bir
    /// tekildir, sahnede/prefabda referansı yoktur — ayarı ancak <c>Resources.Load</c> ile
    /// bulabilir. ⚠️ <c>Resources/</c> altından çıkarılırsa ya da adı değişirse gösterge
    /// <b>çalışmayı sürdürür</b> ama koddaki varsayılanlara düşer, yani yaptığın ayarlar
    /// sessizce yok sayılır.</para>
    ///
    /// <para><b>Asset zorunlu değildir:</b> yoksa aşağıdaki alan başlangıç değerleri kullanılır
    /// (<see cref="ScriptableObject.CreateInstance{T}"/> ile bellekte bir örnek açılır). Yani
    /// varsayılanların TEK doğruluk kaynağı bu dosyadaki başlangıç değerleridir — asset onların
    /// bir kopyasıdır, ikinci bir tablo değil.</para>
    ///
    /// <para><b>Play kipinde canlı ayarlanır:</b> sayılar, renkler ve eğriler her karede buradan
    /// okunur → Inspector'da oynattığın değer anında ekrana yansır. ⚠️ <see cref="MarkerPrefab"/>
    /// ve <see cref="LineMaterial"/> havuz düğümü kurulurken bağlandığı için onların değişimi
    /// bir sonraki isabette (prefab için: havuz o düğümü yeniden kurduğunda) geçerli olur.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "HitMarkerStyle", menuName = "VortexArena/Hit Marker Style")]
    public class HitMarkerStyle : ScriptableObject
    {
        /// <summary>Resources.Load anahtarı (asset dosya adıyla birebir).</summary>
        private const string ResourcePath = "HitMarkerStyle";

        private static HitMarkerStyle _cached;
        private static bool _loadAttempted;

        // ------------------------------------------------------------------ görünüm

        [Header("Görünüm")]
        [Tooltip("İşaretin rengi. ALFA = saydamlık (0 görünmez, 1 tam opak).\n\n" +
                 "⚠️ Kırmızı/mavi seçme: ikisi de takım rengidir ve gövdenin üstünde çizilen renkli " +
                 "bir X takım okumasını bozar.")]
        [SerializeField] private Color color = new Color(1f, 0.97f, 0.88f, 0.95f);

        [Tooltip("1 metre mesafedeki boy (m) — işaretin kenar uzunluğu.\n\n" +
                 "Boy mesafeyle ölçeklenir: 0.034 → 1 m'de 3.4 cm, 10 m'de 34 cm. Böylece işaret " +
                 "her mesafede ekranda aynı büyüklükte görünür. Sabit metre kullanılsaydı yakın " +
                 "hedefte ekranı kaplar, uzakta hiç okunmazdı.")]
        [SerializeField] private float sizeAtOneMeter = 0.034f;

        [Tooltip("Boyun alt sınırı (m) — burnunun dibindeki isabette işaret nokta olmasın.")]
        [SerializeField] private float minSizeMeters = 0.04f;

        [Tooltip("Boyun üst sınırı (m) — arenanın öbür ucundaki isabette işaret pano olmasın.")]
        [SerializeField] private float maxSizeMeters = 0.2f;

        [Tooltip("Çizgi kalınlığı, boyun oranı olarak (0.08 → boyun %8'i).")]
        [SerializeField] private float thicknessOfSize = 0.08f;

        [Tooltip("İşaretin ekranda kalma süresi (sn).")]
        [SerializeField] private float lifetimeSeconds = 0.3f;

        [Tooltip("İşaretin yüzeyden göze doğru kaldırıldığı mesafe (m).\n\n" +
                 "⚠️ 0 YAPMA: isabet noktası tam gövdenin yüzeyindedir, kaldırılmayan işaret " +
                 "derinlik testinde yüzeyle didişip parça parça kaybolur.")]
        [SerializeField] private float surfaceLiftMeters = 0.02f;

        // ------------------------------------------------------------------ zaman eğrileri

        [Header("Zaman eğrileri  (yatay eksen: ömrün 0 → 1'i)")]
        [Tooltip("Saydamlık çarpanı. Rengin alfası bununla ÇARPILIR — yani rengi %50 saydam " +
                 "yaparsan eğrinin tepesi de %50'de kalır.\n\n" +
                 "Varsayılan: bir süre tam parlaklık, sonra hızlanarak sönme. Baştan sönen bir " +
                 "işaret kısa ömürde hiç tam parlaklık göstermez ve soluk bir hayalet gibi durur.")]
        [SerializeField] private AnimationCurve alphaOverLife = DefaultAlphaCurve();

        [Tooltip("Boy çarpanı. Varsayılan: küçükten açılır, sönerken hafifçe genişler — hızlı " +
                 "ateşte üst üste binen isabetler böylece ayrı ayrı okunur.")]
        [SerializeField] private AnimationCurve sizeOverLife = DefaultSizeCurve();

        // ------------------------------------------------------------------ kontur

        [Header("Kontur  (X'in dışına çizilen ikinci, kalın X)")]
        [Tooltip("Kontur rengi. ⚠️ ALFA 0 → kontur hiç çizilmez.\n\n" +
                 "Açık zeminde ve beyaz gövdede işaretin kaybolmasını engeller.")]
        [SerializeField] private Color outlineColor = new Color(0f, 0f, 0f, 0.5f);

        [Tooltip("Kontur kalınlığı, ana çizginin katı olarak (2.2 → iki katından biraz fazla). " +
                 "1 ve altı → kontur ana çizginin altında kalır, yani görünmez.")]
        [SerializeField] private float outlineThicknessScale = 2.2f;

        // ------------------------------------------------------------------ serbest görünüm

        [Header("Serbest görünüm")]
        [Tooltip("Çizgi materyali. Boşsa çalışma anında Sprites/Default üretilir.\n\n" +
                 "Parlama (glow) istiyorsan buraya additive harmanlayan bir materyal bağla — " +
                 "renk çizginin vertex renginden geldiği için materyalin rengine dokunulmaz.")]
        [SerializeField] private Material lineMaterial;

        [Tooltip("BOŞ DEĞİLSE X hiç çizilmez; bunun bir örneği açılır ve görünümün tamamı senindir " +
                 "(parçacık, shader, animasyon, ışık...).\n\n" +
                 "HitMarker yalnız YERİ, BOYU, DÖNÜŞÜ ve ÖMRÜ yönetir — renk, saydamlık ve sönme " +
                 "prefabın kendi işidir (yukarıdaki renk/kontur alanları bu yolda okunmaz).\n\n" +
                 "Prefab kameranın dönüşünü aynen alır (ekrana paralel): varsayılan Unity Quad'ı " +
                 "bu hâlde kameraya bakar. 1 birim = 1 metre olacak şekilde kur; ölçek yukarıdaki " +
                 "boy alanlarından gelir. İçindeki parçacık sistemleri her isabette baştan oynatılır.")]
        [SerializeField] private GameObject markerPrefab;

        [Tooltip("İşaret kameraya döndürülsün mü. Prefab yolunda kapatılabilir (dünyada sabit " +
                 "duran bir efekt için); prosedürel X'te kapatmak anlamsızdır (X kenardan " +
                 "bakıldığında çizgiye iner).")]
        [SerializeField] private bool faceCamera = true;

        // ------------------------------------------------------------------ okuma

        /// <summary>İşaretin rengi; alfası saydamlıktır.</summary>
        public Color Color => color;

        /// <summary>Kontur rengi; alfası 0 ise kontur çizilmez.</summary>
        public Color OutlineColor => outlineColor;

        /// <summary>Kontur çizilecek mi (renk görünür + kalınlık ana çizgiyi aşıyor mu).</summary>
        public bool HasOutline => outlineColor.a > 0.001f && outlineThicknessScale > 1f;

        /// <summary>Kontur kalınlığının ana çizgiye oranı.</summary>
        public float OutlineThicknessScale => Mathf.Max(1f, outlineThicknessScale);

        /// <summary>Ömür (sn) — sıfıra bölünmeyi engellemek için tabanı vardır.</summary>
        public float LifetimeSeconds => Mathf.Max(0.02f, lifetimeSeconds);

        /// <summary>Yüzeyden göze doğru kaldırma (m).</summary>
        public float SurfaceLiftMeters => Mathf.Max(0f, surfaceLiftMeters);

        /// <summary>Çizgi materyali (null olabilir → çalışma anında üretilir).</summary>
        public Material LineMaterial => lineMaterial;

        /// <summary>Görünümün tamamının yerini alan prefab (null olabilir → prosedürel X).</summary>
        public GameObject MarkerPrefab => markerPrefab;

        /// <summary>İşaret kameraya döndürülsün mü.</summary>
        public bool FaceCamera => faceCamera;

        /// <summary>
        /// Verilen mesafedeki boy (m): açısal boy, alt/üst sınıra kırpılı. Sınırlar ters
        /// girilmişse (min &gt; max) sıra düzeltilir — yoksa <c>Clamp</c> sessizce min'i döndürür.
        /// </summary>
        public float SizeAt(float distanceMeters)
        {
            float min = Mathf.Max(0.001f, Mathf.Min(minSizeMeters, maxSizeMeters));
            float max = Mathf.Max(min, Mathf.Max(minSizeMeters, maxSizeMeters));
            return Mathf.Clamp(distanceMeters * Mathf.Max(0f, sizeAtOneMeter), min, max);
        }

        /// <summary>Boydan türeyen çizgi kalınlığı (m).</summary>
        public float ThicknessFor(float sizeMeters) =>
            Mathf.Max(0.0005f, sizeMeters * Mathf.Max(0f, thicknessOfSize));

        /// <summary>
        /// Ömrün <paramref name="t"/> (0→1) anındaki saydamlık çarpanı.
        /// ⚠️ Eğri boşsa (asset elle yazılmış ve alan eksik) koddaki varsayılan eğri kullanılır —
        /// boş bir <see cref="AnimationCurve"/> her yerde 0 döndürür, yani işaret hiç görünmezdi.
        /// </summary>
        public float AlphaAt(float t) => Evaluate(alphaOverLife, t, DefaultAlpha(t));

        /// <summary>Ömrün <paramref name="t"/> (0→1) anındaki boy çarpanı (aynı boş-eğri emniyeti).</summary>
        public float SizeScaleAt(float t) => Evaluate(sizeOverLife, t, DefaultSizeScale(t));

        private static float Evaluate(AnimationCurve curve, float t, float fallback) =>
            curve != null && curve.length > 0 ? curve.Evaluate(t) : fallback;

        // ------------------------------------------------------------------ varsayılanlar

        // ⚠️ Eğrilerin varsayılanı İKİ yerde yaşıyor: bir eğri nesnesi olarak (Inspector'da
        // görünen hâli) ve bir formül olarak (eğri boşaltılırsa devreye giren yedek). İkisi
        // birbirinin kopyası değil, aynı şeklin iki temsili — formül tarafı asset bozulduğunda
        // göstergenin tümden kaybolmasını engelliyor.

        private static AnimationCurve DefaultAlphaCurve() =>
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.45f, 1f), new Keyframe(1f, 0f));

        private static AnimationCurve DefaultSizeCurve() =>
            new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.18f, 1f), new Keyframe(1f, 1.12f));

        /// <summary>Kısa bir tam parlaklık payı, sonra <c>1 − u²</c> ile hızlanan sönme.</summary>
        private static float DefaultAlpha(float t)
        {
            const float hold = 0.45f;
            if (t <= hold)
            {
                return 1f;
            }

            float u = (t - hold) / (1f - hold);
            return 1f - u * u;
        }

        /// <summary>Küçükten açılma, ardından ömrün sonuna kadar hafif genişleme.</summary>
        private static float DefaultSizeScale(float t)
        {
            const float pop = 0.18f;
            return t < pop
                ? Mathf.Lerp(0.5f, 1f, Mathf.SmoothStep(0f, 1f, t / pop))
                : Mathf.Lerp(1f, 1.12f, (t - pop) / (1f - pop));
        }

        // ------------------------------------------------------------------ yükleme

        /// <summary>
        /// Ayarı Resources'tan yükler; sonuç tek sefer önbelleklenir. Asset yoksa <c>null</c> döner
        /// ve <b>uyarı basılmaz</b> — asset isteğe bağlıdır, gösterge onsuz da çalışır (çağıran
        /// koddaki varsayılanlara düşer).
        /// </summary>
        public static HitMarkerStyle Load()
        {
            if (_cached != null)
            {
                return _cached;
            }

            if (_loadAttempted)
            {
                return null;
            }

            _loadAttempted = true;
            _cached = Resources.Load<HitMarkerStyle>(ResourcePath);
            return _cached;
        }
    }
}
