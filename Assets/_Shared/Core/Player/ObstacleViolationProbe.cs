using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.Combat;

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
    /// (yalnız KAFA → tel bayrağı + ceza + karartma) ve <see cref="IsBodyBlocked"/> (kafa <b>ya da
    /// bir el</b> → yalnız ateş kapısı, tele gitmez).</para>
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

        /// <summary>
        /// <b>Değme bandının</b> karartma tavanı: kabuk yüzeye değiyor ama kafa içeride değil.
        /// ⚠️ Bu bandın varlık sebebi kalibrasyon sapmasıdır — birkaç santimlik hizalama hatası ani
        /// bir siyah değil hafif bir kararma üretsin (dış duvarların layer'a alınmamasıyla aynı
        /// gerekçe).
        /// </summary>
        private const float GrazeFadeAlpha = 0.35f;

        /// <summary>Karartmanın hedefe varma süresi (sn) — "anında ama sarsmadan".</summary>
        private const float FadeInSeconds = 0.2f;

        /// <summary>Karartmanın kalkma süresi (sn). Girişten uzun tutulmaz: geri çekilen oyuncuyu
        /// bir an daha kör bırakmanın kimseye faydası yok.</summary>
        private const float FadeOutSeconds = 0.25f;

        /// <summary>Değme bandının rengi — kırmızı, "geri çekil" demek için.</summary>
        private static readonly Color WarnColor = new Color(0.55f, 0.04f, 0.04f);

        /// <summary>İhlal karartmasının rengi: görüşün kapanması bir uyarı değil, <b>görülecek bir
        /// şey olmadığı</b> içindir.</summary>
        private static readonly Color BlackoutColor = new Color(0.02f, 0.01f, 0.01f);

        /// <summary><see cref="ScreenFade"/> kaynak kimliği.</summary>
        private const string FadeSourceId = "obstacle";

        /// <summary>Titreşim nabzının frekansı (Hz) — sürekli titreşim uyarı olmaktan çıkar.</summary>
        private const float HapticPulseHz = 2f;

        /// <summary>Rig bulunamadığında iki arama arasındaki en kısa süre (sn).</summary>
        private const float RigSearchIntervalSeconds = 0.5f;

        /// <summary>Kafa kabuğunun nokta sayısı (merkez + ±x/±y/±z).</summary>
        private const int HeadSampleCount = 7;

        // ------------------------------------------------------------------ durum

        public static ObstacleViolationProbe Instance { get; private set; }

        /// <summary>
        /// Yerel oyuncunun kafası şu an bir iç engelin içinde mi — <b>telin taşıdığı tek bilgi</b>
        /// (<c>gripFlags</c> bit5, §6.3). Okuyucusu <c>PlayerPoseTracker</c>'dır.
        /// </summary>
        public static bool IsViolating { get; private set; }

        /// <summary>
        /// Kafa kabuğunun ne kadarı geometrinin içinde (0..1): yedi noktanın içeride olanlarının
        /// oranı. ⚠️ <b>İhlalin ŞİDDETİ DEĞİLDİR</b> — karartma içerideyken koşulsuz tam siyahtır;
        /// bu değer yalnız <b>değme bandını</b> (ihlalden önceki hafif kararma) sürer. İkisini tek
        /// rampaya bağlamak "içerideyim ama görüyorum" demektir.
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

            /// <summary>Kabuk yüzeye değiyor ama kafa içeride sayılmıyor (ceza yok).</summary>
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

        /// <summary>Çizilen karartma (yumuşatılmış).</summary>
        private float _fadeAlpha;

        private bool _hapticsOn;

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
            _sampleAccumulator += Time.unscaledDeltaTime;
            if (_sampleAccumulator < SampleInterval)
            {
                // Karartma her KAREDE bildirilir (hakemin kalp atışı sözleşmesi), ölçüm 20 Hz.
                ReportPresentation();
                return;
            }

            float elapsed = _sampleAccumulator;
            _sampleAccumulator = 0f;

            Evaluate(elapsed);
            ReportPresentation();
        }

        /// <summary>Bir ölçüm turu: geniş faz → kafa kabuğu → eşik + histerezis.</summary>
        private void Evaluate(float elapsed)
        {
            Transform head = ResolveHead();
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

            // ⚠️ EŞİK ile RAMPA ayrı şeylerdir. Giriş: merkez içeride ya da kabuğun üçte biri
            // içeride. Çıkış: HİÇBİR nokta içeride değil — histerezis ikinci bir eşikten değil
            // buradan gelir, çünkü "girdim" ile "tamamen çıktım" doğal olarak farklı sorulardır.
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
            return _rig != null ? _rig.centerEyeAnchor : null;
        }

        // ------------------------------------------------------------------ sunum

        /// <summary>
        /// Karartma + titreşim. <b>İki durum vardır ve gerekçeleri farklıdır:</b>
        /// <list type="number">
        /// <item><b>Kafa içeride → KOŞULSUZ TAM SİYAH.</b> Derinliğe bağlanmaz: gözler katı bir
        /// cismin içindeyken görülecek meşru bir şey yoktur ve o an duvar arkasını görmek tam olarak
        /// istismarın kendisidir. Bu kuralın anti-hile ayağı budur — ceza değil.</item>
        /// <item><b>Kabuk değiyor → hafif kararma</b> (<see cref="GrazeFadeAlpha"/>'ya kadar
        /// rampalı). Gerekçesi kalibrasyon sapmasıdır, ceza değildir: birkaç santimlik hizalama
        /// hatası oyuncuyu aniden kör etmesin.</item>
        /// </list>
        /// <para>⚠️ <b>Hasarın kırmızısı BURADA değildir.</b> <see cref="ScreenFade"/> hakemi "en
        /// yüksek alfa kazanır" diyor; siyah 1.0'dayken hiçbir kırmızı onu geçemez. Can kaybının
        /// görselini <see cref="DamageVignette"/> quad'ın ÜSTÜNDE ayrı bir katman olarak çizer.</para>
        /// <para>Ölüyken susulur: ölüm ekranı zaten kendi sunumunu yapıyor.</para>
        /// </summary>
        private void ReportPresentation()
        {
            bool alive = ArenaCombat.IsAlive;
            float target = 0f;
            Color color = WarnColor;

            if (alive)
            {
                if (IsViolating)
                {
                    target = 1f;
                    color = BlackoutColor;
                }
                else if (HeadInsideLevel > 0f)
                {
                    target = HeadInsideLevel * GrazeFadeAlpha;
                }
            }

            float seconds = target > _fadeAlpha ? FadeInSeconds : FadeOutSeconds;
            _fadeAlpha = Mathf.MoveTowards(_fadeAlpha, target, Time.unscaledDeltaTime / seconds);
            FadeAlpha = _fadeAlpha;

            ScreenFade.Report(FadeSourceId, _fadeAlpha, color);

            bool pulse = alive && IsViolating &&
                         Mathf.Repeat(Time.unscaledTime * HapticPulseHz, 1f) < 0.5f;
            SetHaptics(pulse);
        }

        private void SetHaptics(bool on)
        {
            if (on == _hapticsOn)
            {
                return;
            }

            _hapticsOn = on;
            float amplitude = on ? 0.5f : 0f;
            OVRInput.SetControllerVibration(0.6f, amplitude, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(0.6f, amplitude, OVRInput.Controller.RTouch);
        }
    }
}
