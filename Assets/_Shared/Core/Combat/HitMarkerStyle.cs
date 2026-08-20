using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Look settings of the hit marker (<see cref="HitMarker"/>) — size, transparency, lifetime,
    /// outline, material and optionally a prefab replacing the whole look.
    /// <para>Lives at <c>Assets/_Shared/Data/Resources/HitMarkerStyle.asset</c>. Same rationale as
    /// <see cref="WeaponCatalog"/>: <see cref="HitMarker"/> self-bootstraps with no scene/prefab
    /// reference, so <c>Resources.Load</c> is the only way in. ⚠️ Moved out or renamed, the marker
    /// KEEPS WORKING but falls back to the code defaults — your settings are silently ignored.</para>
    /// <para>The asset is optional: without it the field initial values below are used (in-memory
    /// instance). So the defaults' SINGLE source of truth is this file; the asset is a copy of
    /// them, not a second table.</para>
    /// <para>Tunable live in Play mode — numbers, colors and curves are read every frame. ⚠️
    /// <see cref="MarkerPrefab"/> and <see cref="LineMaterial"/> bind while a pool node is built,
    /// so they take effect on the next hit (prefab: when the pool rebuilds that node).</para>
    /// </summary>
    [CreateAssetMenu(fileName = "HitMarkerStyle", menuName = "VortexArena/Hit Marker Style")]
    public class HitMarkerStyle : ScriptableObject
    {
        /// <summary>Resources.Load key (identical to the asset file name).</summary>
        private const string ResourcePath = "HitMarkerStyle";

        private static HitMarkerStyle _cached;
        private static bool _loadAttempted;

        // ------------------------------------------------------------------ look

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

        // ------------------------------------------------------------------ time curves

        [Header("Zaman eğrileri  (yatay eksen: ömrün 0 → 1'i)")]
        [Tooltip("Saydamlık çarpanı. Rengin alfası bununla ÇARPILIR — yani rengi %50 saydam " +
                 "yaparsan eğrinin tepesi de %50'de kalır.\n\n" +
                 "Varsayılan: bir süre tam parlaklık, sonra hızlanarak sönme. Baştan sönen bir " +
                 "işaret kısa ömürde hiç tam parlaklık göstermez ve soluk bir hayalet gibi durur.")]
        [SerializeField] private AnimationCurve alphaOverLife = DefaultAlphaCurve();

        [Tooltip("Boy çarpanı. Varsayılan: küçükten açılır, sönerken hafifçe genişler — hızlı " +
                 "ateşte üst üste binen isabetler böylece ayrı ayrı okunur.")]
        [SerializeField] private AnimationCurve sizeOverLife = DefaultSizeCurve();

        // ------------------------------------------------------------------ outline

        [Header("Kontur  (X'in dışına çizilen ikinci, kalın X)")]
        [Tooltip("Kontur rengi. ⚠️ ALFA 0 → kontur hiç çizilmez.\n\n" +
                 "Açık zeminde ve beyaz gövdede işaretin kaybolmasını engeller.")]
        [SerializeField] private Color outlineColor = new Color(0f, 0f, 0f, 0.5f);

        [Tooltip("Kontur kalınlığı, ana çizginin katı olarak (2.2 → iki katından biraz fazla). " +
                 "1 ve altı → kontur ana çizginin altında kalır, yani görünmez.")]
        [SerializeField] private float outlineThicknessScale = 2.2f;

        // ------------------------------------------------------------------ custom look

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

        // ------------------------------------------------------------------ reading

        /// <summary>Marker color; its alpha is the transparency.</summary>
        public Color Color => color;

        /// <summary>Outline color; the outline is not drawn when its alpha is 0.</summary>
        public Color OutlineColor => outlineColor;

        /// <summary>Whether the outline is drawn (color visible + thickness exceeds the main line).</summary>
        public bool HasOutline => outlineColor.a > 0.001f && outlineThicknessScale > 1f;

        /// <summary>Ratio of the outline thickness to the main line.</summary>
        public float OutlineThicknessScale => Mathf.Max(1f, outlineThicknessScale);

        /// <summary>Lifetime (s) — it has a floor to prevent division by zero.</summary>
        public float LifetimeSeconds => Mathf.Max(0.02f, lifetimeSeconds);

        /// <summary>Lift from the surface toward the eye (m).</summary>
        public float SurfaceLiftMeters => Mathf.Max(0f, surfaceLiftMeters);

        /// <summary>Line material (may be null → generated at runtime).</summary>
        public Material LineMaterial => lineMaterial;

        /// <summary>Prefab that replaces the whole look (may be null → procedural X).</summary>
        public GameObject MarkerPrefab => markerPrefab;

        /// <summary>Whether the marker is turned toward the camera.</summary>
        public bool FaceCamera => faceCamera;

        /// <summary>
        /// Size at the given distance (m): angular size, clamped. Inverted bounds (min &gt; max)
        /// are reordered — otherwise <c>Clamp</c> silently returns min.
        /// </summary>
        public float SizeAt(float distanceMeters)
        {
            float min = Mathf.Max(0.001f, Mathf.Min(minSizeMeters, maxSizeMeters));
            float max = Mathf.Max(min, Mathf.Max(minSizeMeters, maxSizeMeters));
            return Mathf.Clamp(distanceMeters * Mathf.Max(0f, sizeAtOneMeter), min, max);
        }

        /// <summary>Line thickness derived from the size (m).</summary>
        public float ThicknessFor(float sizeMeters) =>
            Mathf.Max(0.0005f, sizeMeters * Mathf.Max(0f, thicknessOfSize));

        /// <summary>
        /// Transparency multiplier at lifetime <paramref name="t"/> (0→1).
        /// ⚠️ An empty curve (hand-written asset missing the field) falls back to the code default:
        /// an empty <see cref="AnimationCurve"/> returns 0 everywhere, hiding the marker entirely.
        /// </summary>
        public float AlphaAt(float t) => Evaluate(alphaOverLife, t, DefaultAlpha(t));

        /// <summary>Size multiplier at moment <paramref name="t"/> (0→1) of the lifetime (same
        /// empty-curve safety net).</summary>
        public float SizeScaleAt(float t) => Evaluate(sizeOverLife, t, DefaultSizeScale(t));

        private static float Evaluate(AnimationCurve curve, float t, float fallback) =>
            curve != null && curve.length > 0 ? curve.Evaluate(t) : fallback;

        // ------------------------------------------------------------------ defaults

        // ⚠️ The curve defaults live in TWO places: a curve object (shown in the Inspector) and a
        // formula (fallback when the curve is emptied) — two representations of the same shape, not
        // copies. The formula keeps the marker from vanishing when the asset is broken.

        private static AnimationCurve DefaultAlphaCurve() =>
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.45f, 1f), new Keyframe(1f, 0f));

        private static AnimationCurve DefaultSizeCurve() =>
            new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.18f, 1f), new Keyframe(1f, 1.12f));

        /// <summary>A short full-brightness hold, then a fade accelerating with <c>1 − u²</c>.</summary>
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

        /// <summary>Pops open from small, then widens slightly until the end of the lifetime.</summary>
        private static float DefaultSizeScale(float t)
        {
            const float pop = 0.18f;
            return t < pop
                ? Mathf.Lerp(0.5f, 1f, Mathf.SmoothStep(0f, 1f, t / pop))
                : Mathf.Lerp(1f, 1.12f, (t - pop) / (1f - pop));
        }

        // ------------------------------------------------------------------ loading

        /// <summary>
        /// Loads the settings from Resources, cached once. Returns <c>null</c> with NO warning when
        /// the asset is absent — it is optional and the caller falls back to the code defaults.
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
