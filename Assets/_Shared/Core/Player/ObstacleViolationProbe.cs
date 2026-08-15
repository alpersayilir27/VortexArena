using UnityEngine;
using VortexArena.Core.Arena;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// YEREL oyuncunun <b>kafasının</b> bir iç engelin içinde olup olmadığını ölçer
    /// (Docs/ArenaNet-Protokol.md §10.9). Tek kural vardır: <b>kafa içeride</b>.
    ///
    /// <para><b>Ölçer, cezalandırmaz.</b> Sonucu <see cref="IsViolating"/>'ten okunur;
    /// <c>PlayerPoseTracker</c> onu poz paketine bir bit olarak koyar (§6.2) ve canı
    /// <b>sunucu</b> eritir. Bu sınıf can/skor/ölüm bilmez.</para>
    ///
    /// <para><b>İKİ ayrı çıktı üretir ve karıştırılmamalıdır:</b> <see cref="IsViolating"/>
    /// (yalnız KAFA → tel bayrağı + ceza) ve <see cref="IsBodyBlocked"/> (kafa <b>ya da
    /// bir el</b> → yalnız ateş kapısı, tele gitmez).</para>
    ///
    /// <para>⚠️ <b>Karartma ve titreşim bu ikisinin de değil, GÖRÜŞÜN çıktısıdır</b> — kafa
    /// geometriye değiyor (<see cref="HeadInsideLevel"/> &gt; 0) <b>ya da</b> göz noktası bir engel
    /// yüzeyine kameranın kırpma mesafesinden yakın: ceza eşiğini beklemezler, gerekçesi
    /// <see cref="ReportPresentation"/>'da. ⚠️ <b>Ayrıca maç fazından ve canlılıktan da
    /// bağımsızdırlar</b> — aynı gerekçe: görüşün katı cismin içinde olması her durumda aynı
    /// şeydir. Faz/canlılık kapıları <b>yalnız cezaya</b> aittir ve onları sunucu uygular.</para>
    ///
    /// <para><b>Neden ceza yalnız kafaya bakıyor:</b> cezanın işi görüşün katı geometrinin içine
    /// girmesini cezalandırmaktır — el, kol ve gövde bunu yapmaz. Ölçülen kütlenin oranını
    /// yargılayan bir kural burada
    /// <b>YOKTUR ve geri eklenmez</b>: Quest'te alt gövde sensörle ölçülmez, üst gövdeden ÜRETİLİR
    /// (OVRBody FullBody = generative legs) ve üretilmiş bir uzuv oyuncu siperin arkasında dururken
    /// siperin İÇİNDE çözülebilir — yani oran kuralı kaçınılmaz olarak dokunulmamış bir cisimden
    /// ceza üretir.</para>
    ///
    /// <para><b>Kafa merkezi kemikten değil HMD'den gelir:</b> gözlük oyuncunun gerçek kafasının
    /// yerini iskeletten daha iyi bilir ve kural body tracking hiç çalışmasa da işler.</para>
    ///
    /// <para>Nokta-içeride testinin kendisi <b>bu sınıfta değil</b>
    /// <see cref="ObstacleVolumes"/>'dedir (konvekslik şartının gerekçesi de orada): aynı soruyu
    /// atış yolu da soruyor ve iki kopya kaçınılmaz olarak sapardı.</para>
    ///
    /// <para><b>Neden kendini önyükleyen tekil</b> (<c>WeaponGranter</c>/<c>PlayerCombatState</c>
    /// deseni): sahneye ya da prefaba elle konsaydı her yeni arena bir kurulum adımı doğururdu ve
    /// unutulan bir arena sessizce cezasız kalırdı.</para>
    /// </summary>
    /// <remarks>
    /// ⚠️ Execution order geç (30100): HMD anchor'ı rig'in kendi döngüsünde yazılıyor, erken okuyan
    /// kare bir kare bayat bir kafa konumu ölçer.
    /// </remarks>
    [DefaultExecutionOrder(30100)]
    public class ObstacleViolationProbe : MonoBehaviour
    {
        // ------------------------------------------------------------------ ayarlar

        /// <summary>Ölçüm kadansı — poz gönderimiyle (20 Hz) aynı: bayrak paket çıkarken taze olsun.</summary>
        private const float SampleInterval = 1f / 20f;

        /// <summary>Kafa küresinin yarıçapı (m) — kabuğun yedi noktası bundan türer.</summary>
        private const float HeadRadius = 0.11f;

        /// <summary>Göz hizasından kafa merkezine geri ofset (m): HMD gözlerin önündedir.</summary>
        private const float HeadCenterBackOffset = 0.06f;

        /// <summary>
        /// Aday toplama küresinin yarıçapı (m): kafa kabuğu <b>ve iki el</b> bu kürenin içinde
        /// kalmalı — tamamen uzatılmış bir kol kafa merkezinden ~0.8 m'dir.
        /// </summary>
        private const float QueryRadius = 1.2f;

        /// <summary>El küresinin yarıçapı (m): avuç + parmaklar kabaca bu kadar yer kaplar.</summary>
        private const float HandRadius = 0.07f;

        /// <summary>
        /// İhlalin sayılması için kesintisiz geçmesi gereken süre (sn). Tek karelik bir izleme
        /// sıçraması ceza başlatmasın diye.
        /// </summary>
        private const float MinViolationSeconds = 0.15f;

        /// <summary>
        /// Merkez noktası dışarıdayken ihlal saymak için gereken kabuk noktası sayısı. Merkez zaten
        /// içerideyse tek başına yeter — bu eşik <b>ikinci</b> kapıdır (kafa yandan girdiğinde
        /// merkez son giren nokta olur).
        /// </summary>
        private const int EnterPointCount = 3;

        /// <summary>Karartmanın kalkma süresi (sn). ⚠️ <b>Girişin karşılığı YOKTUR</b> — karartma
        /// temasta kademesiz iner (gerekçe <see cref="ReportPresentation"/>); rampa yalnız çıkışta
        /// vardır ve orada da konfor içindir: sınırda gidip gelen kafa, ölçüm kadansıyla siyah/açık
        /// arasında çırpınırdı.</summary>
        private const float FadeOutSeconds = 0.25f;

        /// <summary>Karartmanın rengi: görüşün kapanması bir uyarı değil, <b>görülecek bir
        /// şey olmadığı</b> içindir.</summary>
        private static readonly Color BlackoutColor = new Color(0.02f, 0.01f, 0.01f);

        /// <summary><see cref="ScreenFade"/> kaynak kimliği.</summary>
        private const string FadeSourceId = "obstacle";

        /// <summary><see cref="ControllerHaptics"/> kaynak kimliği. ⚠️ Nabzın frekansı ve genliği
        /// burada DEĞİL hakemde durur: aynı hissi isteyen ikinci kaynak (muhafaza) ile üst üste
        /// bindiğinde iki ayrı sayı iki ayrı faz demek olurdu.</summary>
        private const string HapticSourceId = "obstacle";

        /// <summary>Rig bulunamadığında iki arama arasındaki en kısa süre (sn).</summary>
        private const float RigSearchIntervalSeconds = 0.5f;

        /// <summary>Kafa kabuğunun nokta sayısı (merkez + ±x/±y/±z).</summary>
        private const int HeadSampleCount = 7;

        /// <summary>
        /// Kırpma düzleminin <b>KÖŞESİ</b>, merkezinin kaç katı uzaklıktadır. Kırpma göz noktasından
        /// <c>nearClipPlane</c> kadar ileride başlar ama düzlem bir dikdörtgendir: 90° görüş açısı ve
        /// kare en-boy oranında köşe <c>near·√3 ≈ 1.73×</c> uzağa düşer. Karartma köşeyi de kapatmak
        /// zorundadır — ⚠️ çevresel görüşte kalan bir şerit hilenin tamamını geri verir.
        /// </summary>
        private const float NearPlaneCornerFactor = 1.8f;

        /// <summary>
        /// Stereo payı (m): her göz, ölçümün yapıldığı <b>merkez</b> göz noktasından yarım IPD kadar
        /// yandadır, yani yan duvara merkezden bu kadar daha yakındır.
        /// </summary>
        private const float StereoEyeMargin = 0.035f;

        /// <summary>Kamera çözülemezse varsayılan kırpma mesafesi (m).</summary>
        private const float DefaultNearClip = 0.05f;

        /// <summary>
        /// Görüş açıklığının alt sınırı (m): kırpma düzlemi ne kadar küçük olursa olsun karartma bir
        /// izleme titremesi kadar erken gelmelidir.
        /// </summary>
        private const float MinEyeClearance = 0.05f;

        /// <summary>
        /// Görüş açıklığının üst sınırı (m) = <see cref="HeadRadius"/>. ⚠️ <b>Tavan bir emniyet
        /// değil, bir SÖZLEŞMEDİR:</b> karartmanın kapısı en fazla "kafam cisme değiyor" anıdır.
        /// Bundan büyük bir açıklık, oyuncunun bloğun <b>yanından</b> geçerken ekranını karartırdı;
        /// yani kırpma mesafesi tek başına büyütülerek çözülemez — kameranın
        /// <c>nearClipPlane</c>'i de bu tavanın altında kalacak kadar küçük tutulur
        /// (<c>VA_CameraRig</c>).
        /// </summary>
        private const float MaxEyeClearance = HeadRadius;

        // ------------------------------------------------------------------ durum

        public static ObstacleViolationProbe Instance { get; private set; }

        /// <summary>
        /// Yerel oyuncunun kafası şu an bir iç engelin içinde mi — <b>telin taşıdığı tek bilgi</b>
        /// (<c>gripFlags</c> bit5, §6.3). Okuyucusu <c>PlayerPoseTracker</c>'dır.
        /// </summary>
        public static bool IsViolating { get; private set; }

        /// <summary>
        /// Kafa kabuğunun ne kadarı geometrinin içinde (0..1): yedi noktanın içeride olanlarının
        /// oranı. ⚠️ <b>Bir ŞİDDET ölçüsü DEĞİLDİR ve alfaya çevrilmez</b> — karartmanın kapılarından
        /// biri bu değerin sıfırdan büyük olup olmadığıdır, büyüklüğü değil. Oranı rampaya bağlamak
        /// "içerideyim ama görüyorum" demektir. Kalan tüketicisi teşhistir (Dev penceresi).
        /// </summary>
        public static float HeadInsideLevel { get; private set; }

        /// <summary>
        /// Çizilen karartmanın o karedeki değeri (0..1). Uyarı yazısı buna bakar: yazı karartmayla
        /// birlikte gelsin, ondan önce belirmesin.
        /// </summary>
        public static float FadeAlpha { get; private set; }

        /// <summary>
        /// Oyuncunun <b>ölçülen</b> parçalarından biri (kafa ya da bir el) bir engelin içinde mi —
        /// <b>ateş kapısı</b> (<c>PlayerCombatState.CanFire</c>, §10.9).
        ///
        /// <para><see cref="IsViolating"/>'ten üç farkı var ve üçü de bilinçli:</para>
        /// <list type="number">
        /// <item><b>Elleri de sayar.</b> Ceza yalnız kafaya bakar (görüş kuralı), ateş kapısı ise
        /// "gövdemi göstermeden ateş ediyor muyum" sorusudur — bloğun içinde durup silahı dışarı
        /// uzatan oyuncu tam olarak bunu yapıyor.</item>
        /// <item><b>Bekleme süresi yok</b> (<see cref="MinViolationSeconds"/> uygulanmaz): ateşi bir
        /// kare fazla engellemek, bir kare fazla izin vermekten iyidir.</item>
        /// <item><b>Tele GİTMEZ.</b> Bayrak (<c>FLAG_IN_OBSTACLE</c>) yalnız
        /// <see cref="IsViolating"/>'i taşır — el kuralı cezaya karışmaz, protokolde karşılığı
        /// yoktur.</item>
        /// </list>
        /// <para>⚠️ <b>İzlenmeyen el hiç sorulmaz:</b> rig, izlenmeyen elin anchor'ını rig orijinine
        /// yazıyor (<see cref="ControllerTracking"/>) ve oyuncunun ayağının dibi pekâlâ bir engelin
        /// içinde olabilir — kumandası kapanan oyuncu sebepsizce ateş edemez hâle gelirdi.</para>
        /// </summary>
        public static bool IsBodyBlocked { get; private set; }

        /// <summary>Ölçümün son durumu (teşhis).</summary>
        public enum Trigger
        {
            /// <summary>Kafa hiçbir engele değmiyor.</summary>
            None,

            /// <summary>Kabuk yüzeye değiyor ama kafa içeride sayılmıyor: <b>karartma var, ceza
            /// yok</b>.</summary>
            Grazing,

            /// <summary>Kafa içeride — ihlal.</summary>
            Inside
        }

        /// <summary>Son ölçümün sonucu (teşhis).</summary>
        public static Trigger LastTrigger { get; private set; }

        /// <summary>Son "içeride" cevabını veren engelin adı (teşhis); yoksa boş.</summary>
        public static string LastTriggerCollider =>
            ObstacleVolumes.LastHit != null ? ObstacleVolumes.LastHit.name : "";

        private float _sampleAccumulator;
        private float _violationHold;

        private OVRCameraRig _rig;
        private float _rigSearchTime = float.NegativeInfinity;

        /// <summary>HMD kamerası — görüş açıklığı onun kırpma mesafesinden türer.</summary>
        private Camera _headCamera;

        /// <summary>Çizilen karartma (yumuşatılmış).</summary>
        private float _fadeAlpha;

        // ------------------------------------------------------------------ önyükleme

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var go = new GameObject("[ObstacleViolationProbe]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<ObstacleViolationProbe>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            Instance = null;
            ClearState();
        }

        // ------------------------------------------------------------------ döngü

        private void LateUpdate()
        {
            // ⚠️ Kafa her KAREDE çözülür: karartmanın kapısı da (görüş açıklığı) onu okuyor ve o
            // kapı 20 Hz'e bırakılamaz — gerekçe ReportPresentation'da.
            Transform head = ResolveHead();

            _sampleAccumulator += Time.unscaledDeltaTime;
            if (_sampleAccumulator >= SampleInterval)
            {
                float elapsed = _sampleAccumulator;
                _sampleAccumulator = 0f;
                Evaluate(head, elapsed);
            }

            // Karartma her KAREDE bildirilir (hakemin kalp atışı sözleşmesi), ceza ölçümü 20 Hz.
            ReportPresentation(head);
        }

        /// <summary>Bir ölçüm turu: geniş faz → kafa kabuğu → eşik + histerezis.</summary>
        private void Evaluate(Transform head, float elapsed)
        {
            if (head == null)
            {
                // Oyuncu değiliz (admin) ya da rig henüz yok: ölçüm yapılamaz, ihlal de yoktur.
                ClearState();
                return;
            }

            Vector3 headCenter = head.position - head.forward * HeadCenterBackOffset;

            int count = ObstacleVolumes.Sample(headCenter, QueryRadius);
            if (count == 0)
            {
                // ⚠️ YAYGIN DURUM ve erken çıkışın tek sebebi bu: yakında engel yokken kare başına
                // tek physics sorgusu yapılır, nokta testi hiç koşmaz.
                // (Layer tanımsızsa Sample de 0 döner; ArenaLayers zaten bir kez bağırdı.)
                HeadInsideLevel = 0f;
                IsBodyBlocked = false;
                LastTrigger = Trigger.None;
                SetViolation(false, elapsed);
                return;
            }

            bool centerInside = EvaluateHead(headCenter, count, out int inside);
            HeadInsideLevel = inside / (float)HeadSampleCount;

            // ⚠️ Bu eşik YALNIZ CEZAYA aittir; karartma onu beklemez (gerekçe: ReportPresentation).
            // Giriş: merkez içeride ya da kabuğun üçte biri içeride. Çıkış: HİÇBİR nokta içeride
            // değil — histerezis ikinci bir eşikten değil buradan gelir, çünkü "girdim" ile
            // "tamamen çıktım" doğal olarak farklı sorulardır.
            bool rule = IsViolating
                ? inside > 0
                : centerInside || inside >= EnterPointCount;

            LastTrigger = rule ? Trigger.Inside
                : inside > 0 ? Trigger.Grazing
                : Trigger.None;

            // Ateş kapısı cezadan AYRI hesaplanır (gerekçe: IsBodyBlocked).
            IsBodyBlocked = rule || IsHandInside(false, count) || IsHandInside(true, count);

            SetViolation(rule, elapsed);
        }

        /// <summary>
        /// Bir elin küçük kabuğu (merkez + ±x/±y/±z) engelin içinde mi. İzlenmeyen el
        /// <b>sorulmaz</b> — gerekçe <see cref="IsBodyBlocked"/>'ta.
        /// </summary>
        private bool IsHandInside(bool right, int count)
        {
            if (!ControllerTracking.IsValid(right))
            {
                return false;
            }

            Transform anchor = _rig != null
                ? (right ? _rig.rightHandAnchor : _rig.leftHandAnchor)
                : null;

            if (anchor == null)
            {
                return false;
            }

            Vector3 center = anchor.position;
            return ObstacleVolumes.Contains(center, count) ||
                   ObstacleVolumes.Contains(center + Vector3.right * HandRadius, count) ||
                   ObstacleVolumes.Contains(center - Vector3.right * HandRadius, count) ||
                   ObstacleVolumes.Contains(center + Vector3.up * HandRadius, count) ||
                   ObstacleVolumes.Contains(center - Vector3.up * HandRadius, count) ||
                   ObstacleVolumes.Contains(center + Vector3.forward * HandRadius, count) ||
                   ObstacleVolumes.Contains(center - Vector3.forward * HandRadius, count);
        }

        /// <summary>
        /// Histerezis + minimum süre. Girişte <see cref="MinViolationSeconds"/> beklenir, çıkışta
        /// beklenmez: cezayı geciktirmek oyuncu lehinedir, cezayı bırakmayı geciktirmek değildir.
        /// </summary>
        private void SetViolation(bool rule, float elapsed)
        {
            if (!rule)
            {
                _violationHold = 0f;
                IsViolating = false;
                return;
            }

            if (IsViolating)
            {
                return;
            }

            _violationHold += elapsed;
            if (_violationHold >= MinViolationSeconds)
            {
                IsViolating = true;
            }
        }

        private void ClearState()
        {
            IsViolating = false;
            IsBodyBlocked = false;
            HeadInsideLevel = 0f;
            LastTrigger = Trigger.None;
            _violationHold = 0f;
        }

        // ------------------------------------------------------------------ kural

        /// <summary>
        /// Kafa küresinin yedi noktasını (merkez + ±x/±y/±z) sınar; dönüş <b>merkezin</b> içeride
        /// olup olmadığıdır, <paramref name="inside"/> ise içerideki nokta sayısı.
        /// <para>⚠️ "Merkezin yüzeye uzaklığı ≥ yarıçap" hesabı KULLANILAMAZ:
        /// <see cref="Collider.ClosestPoint"/> içerideki bir nokta için noktanın kendisini döner,
        /// yani <b>içeriden yüzey mesafesi ölçülemez</b>. Derinliğe dair elimizdeki tek şey bu nokta
        /// sayısıdır.</para>
        /// </summary>
        private static bool EvaluateHead(Vector3 center, int count, out int inside)
        {
            inside = 0;

            bool centerInside = ObstacleVolumes.Contains(center, count);
            if (centerInside) inside++;

            if (ObstacleVolumes.Contains(center + Vector3.right * HeadRadius, count)) inside++;
            if (ObstacleVolumes.Contains(center - Vector3.right * HeadRadius, count)) inside++;
            if (ObstacleVolumes.Contains(center + Vector3.up * HeadRadius, count)) inside++;
            if (ObstacleVolumes.Contains(center - Vector3.up * HeadRadius, count)) inside++;
            if (ObstacleVolumes.Contains(center + Vector3.forward * HeadRadius, count)) inside++;
            if (ObstacleVolumes.Contains(center - Vector3.forward * HeadRadius, count)) inside++;

            return centerInside;
        }

        // ------------------------------------------------------------------ kaynaklar

        /// <summary>
        /// HMD transformu. ⚠️ <b>Aynı zamanda "oyuncu muyuz" kapısıdır</b>: admin gözlemcide rig
        /// kapalı olduğu için burası kalıcı olarak <c>null</c> döner ve sonda hiç çalışmaz
        /// (<see cref="LocalBodyAvatar"/> ile aynı kapı — Core, <c>AppSession</c>'ı göremez).
        /// <para>Arama kısılır: rig hiç yokken her karede sahne geneli tip araması yapılmasın.</para>
        /// </summary>
        private Transform ResolveHead()
        {
            if (_rig != null && _rig.isActiveAndEnabled && _rig.centerEyeAnchor != null)
            {
                return _rig.centerEyeAnchor;
            }

            if (Time.unscaledTime - _rigSearchTime < RigSearchIntervalSeconds)
            {
                return null;
            }

            _rigSearchTime = Time.unscaledTime;
            _rig = FindFirstObjectByType<OVRCameraRig>();
            _headCamera = null; // rig değişti: kamera da yeniden çözülmeli (harita geçişi)
            return _rig != null ? _rig.centerEyeAnchor : null;
        }

        /// <summary>
        /// Görüşün kapanacağı yarıçap (m): göz noktası bir engel yüzeyine bundan yakınsa ekran
        /// kapanır.
        /// <para>⚠️ <b>Kameranın kendi kırpma mesafesinden TÜRETİLİR, sabit yazılmaz:</b> kırpmayı
        /// yapan ile kırpmadan önce kararmayı isteyen aynı sayıya bakmalıdır. İki ayrı yerde
        /// yazılsaydı biri değiştiğinde sızıntı sessizce geri gelirdi — ve belirtisi yine
        /// "engelin içini görüyorum" olurdu.</para>
        /// </summary>
        private float EyeClearance()
        {
            if (_headCamera == null && _rig != null && _rig.centerEyeAnchor != null)
            {
                _headCamera = _rig.centerEyeAnchor.GetComponent<Camera>();
            }

            float near = _headCamera != null ? _headCamera.nearClipPlane : DefaultNearClip;
            return Mathf.Clamp(
                near * NearPlaneCornerFactor + StereoEyeMargin, MinEyeClearance, MaxEyeClearance);
        }

        // ------------------------------------------------------------------ sunum

        /// <summary>
        /// Karartma + titreşim.
        ///
        /// <para><b>Karartmanın ve TİTREŞİMİN kapısı TEMAS ya da YAKINLIKTIR</b> — ikisi de aynı
        /// soruyu sorar: <i>gözler katı bir cismin içini görebiliyor mu?</i>
        /// <list type="number">
        /// <item><b>Temas:</b> kafa kabuğunun yedi noktasından <b>herhangi biri</b> geometrinin
        /// içinde (<see cref="HeadInsideLevel"/> &gt; 0).</item>
        /// <item><b>Görüş açıklığı:</b> göz noktası bir engel yüzeyine <see cref="EyeClearance"/>
        /// metreden yakın. ⚠️ <b>İkincisi olmadan birincisi GEÇ KALIR ve bu bir ayar değil, bir
        /// geometri gerçeğidir:</b> kamera geometriyi göz noktasında değil, onun
        /// <c>nearClipPlane</c> kadar ÖNÜNDEKİ düzlemde kırpar. Kabuk noktaları ise kafa
        /// merkezinden (gözün ~6 cm ARKASI) dünya eksenlerinde uzanır, yani bakış yönünde göz
        /// noktasını en fazla birkaç santim geçer — köşegen yönde hiç geçmez. Aradaki bant tam
        /// olarak "duvar kırpıldı ama ekran hâlâ açık" bandıdır ve blokların içi oradan
        /// okunur.</item>
        /// </list>
        /// Kapı <see cref="IsViolating"/> DEĞİLDİR ve
        /// oraya geri bağlanmaz: ceza eşiği (nokta sayısı + 0.15 sn) bilerek toleranslıdır ve o
        /// tolerans görüşe uygulandığında oyuncunun kafasını bloğun içine <b>görecek kadar</b>
        /// sokmasına izin verir — istismarın kendisi tam olarak budur. Ceza *"bu adam duvarda mı
        /// duruyor"* sorusudur, karartma *"gözler katı bir cismin içinde mi"*; ikincisinde
        /// tereddüt edilecek bir şey yoktur.</para>
        ///
        /// <para>⚠️ <b>Rampa da, değme bandı da (kısmi kararma) yoktur:</b> ikisi de birkaç kare
        /// boyunca yarı saydam bir perde çizer, yani duvarın öbür yüzü <b>okunabilir</b> kalır.
        /// Kalibrasyon sapmasının bedeli olan ani kararma bilinçli olarak kabul edilir; dış duvar,
        /// zemin ve tavan zaten <c>Obstacle</c> layer'ında değildir.</para>
        ///
        /// <para>Rampa yalnız <b>çıkışta</b> vardır (<see cref="FadeOutSeconds"/>): sınırda gidip
        /// gelen kafa, ölçüm kadansıyla (20 Hz) siyah/açık arasında çırpınırdı ve VR'da bu bir
        /// strobe demektir.</para>
        ///
        /// <para>⚠️ <b>Hasarın kırmızısı BURADA değildir.</b> <see cref="ScreenFade"/> hakemi "en
        /// yüksek alfa kazanır" diyor; siyah 1.0'dayken hiçbir kırmızı onu geçemez. Can kaybının
        /// görselini <see cref="DamageVignette"/> quad'ın ÜSTÜNDE ayrı bir katman olarak çizer.</para>
        /// <para>⚠️ <b>Kapıda faz da canlılık da YOKTUR ve geri eklenmez:</b> karartma hangi
        /// harita, hangi mod, hangi faz (lobi · yükleme · geri sayım · oyun · maç sonu) olursa
        /// olsun ve oyuncu ölü de olsa çalışır. Gerekçe eşik tartışmasının aynısı: gözlerin katı
        /// bir cismin içinde olması her durumda aynı şeydir ve maç başlamadan ya da ölüyken duvarın
        /// öbür yüzünü okumak da istismarın kendisidir. Ölüyken susturmak ayrıca canlanmayı
        /// engelleyen kapıyla (engelin içinde canlanma yok, §10.9) çelişirdi: oyuncu neden
        /// canlanmadığını göremezdi. Faz/canlılık <b>yalnız cezanın</b> kapısıdır ve orası
        /// sunucudadır (<c>MatchDirector.TickObstacleLocked</c>) — burada ikinci bir kopyası
        /// tutulmaz.</para>
        /// </summary>
        private void ReportPresentation(Transform head)
        {
            bool contact = HeadInsideLevel > 0f || IsEyeAgainstGeometry(head);

            if (contact)
            {
                _fadeAlpha = 1f;
            }
            else
            {
                _fadeAlpha = Mathf.MoveTowards(
                    _fadeAlpha, 0f, Time.unscaledDeltaTime / FadeOutSeconds);
            }

            FadeAlpha = _fadeAlpha;

            ScreenFade.Report(FadeSourceId, _fadeAlpha, BlackoutColor);

            // Titreşim karartmayla AYNI kapıdan (temas) beslenir: kararan ekran tek başına "ne oldu"
            // sorusunu doğuruyor, nabız ona "duvardasın, geri çekil" cevabını veriyor.
            // ⚠️ Motor DOĞRUDAN sürülmez: aynı titreşimi isteyen ikinci bir kaynak var (arena
            // sınırının dışına çıkma, ArenaBoundary) ve ikisi aynı anda doğru olabiliyor —
            // hakem ScreenFade'deki sözleşmenin aynısını uyguluyor.
            // ⚠️ Bildirim KOŞULSUZDUR (temas yokken de 0 bildirilir): hakem yalnız bir bildirim
            // geldiğinde yeniden hesaplıyor, yani her karede bildiren bu tekil onun kalp atışıdır.
            ControllerHaptics.ReportPulse(HapticSourceId, contact);
        }

        /// <summary>
        /// Göz noktası bir engel yüzeyine <see cref="EyeClearance"/>'tan yakın mı — karartmanın
        /// <b>kırpmayı önceleyen</b> kapısı.
        /// <para>⚠️ <b>Ceza ölçümünün kadansına (20 Hz) bağlanamaz</b> ve her karede koşar: 50 ms,
        /// hızlı dönen bir kafanın açıklık bandını tümden geçmesine yeter ve o karelerde ekran açık
        /// kalır. Maliyeti kare başına <b>bir</b> küçük küre sorgusudur (açıklık ≤ 11 cm), yani
        /// gövde ölçümünün 1.2 m'lik sorgusunun yanında ihmal edilebilir.</para>
        /// <para>⚠️ Sonuç bir ORAN değil, bir <b>eşiktir</b>: mesafeyi alfaya çeviren bir rampa
        /// YOKTUR ve eklenmez — yarı saydam bir perde duvarın öbür yüzünü okunabilir bırakır
        /// (gerekçenin tamamı yukarıda).</para>
        /// </summary>
        private bool IsEyeAgainstGeometry(Transform head)
        {
            if (head == null)
            {
                return false; // oyuncu değiliz (admin) ya da rig henüz yok
            }

            float clearance = EyeClearance();
            return ObstacleVolumes.DistanceToSurface(head.position, clearance) < clearance;
        }
    }
}
