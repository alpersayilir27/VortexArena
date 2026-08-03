using Meta.XR.Movement.Networking;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Net;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Yerel oyuncunun gövdesi — hem <b>ağ kaynağı</b>, hem oyuncunun gözlükte gördüğü
    /// <b>ellerin</b> kaynağı.
    /// <para>
    /// ⚠️ <b>Oyuncu kendi gövdesini, kollarını ve bacaklarını görmez; yalnız ellerini görür</b> —
    /// ve o eller <b>kendi karakterinin elleridir</b>: gövde meshinden kesilmiş ayrı bir el meshi
    /// (<see cref="firstPersonHands"/>) aynı iskeletten, aynı karede sürülür. Yani oyuncunun
    /// gördüğü el ile başkalarının gördüğü el aynı modeldir. Rig'in ISDK el görselleri
    /// ("hayalet el") tam bu yüzden çizilmez — <see cref="ControllerModelHider"/> onları söndürür.
    /// </para>
    /// <para>
    /// ⚠️ <b>Kesim yalnız ÇİZİMDEDİR, telde tam gövde gider:</b> el meshi gövdeyle aynı kemiklere
    /// bağlıdır ve hiçbir kemiğe dokunulmaz. Gövde renderer'ı kapalı, el renderer'ı açıktır.
    /// </para>
    /// <para>
    /// <b>Uzak avatarlarla AYNI prefab, AYNI retarget config, AYNI kod yolu.</b> Tek fark
    /// <see cref="ArenaNetCharacterBehaviour.HasInputAuthority"/>'dir: burada <c>true</c>, yani
    /// gövde Meta Movement SDK'nın body tracking'inden çözülür ve sonucu ağa akar. Uzak tarafta
    /// <c>false</c> olur ve aynı prefab gelen iskeleti uygular.
    /// </para>
    /// <para>
    /// ⚠️ <b>Obje neden hâlâ var ve neden silinemez:</b> başkalarının gördüğü gövde tam olarak
    /// buradan çıkıyor. Gizli olması "gereksiz" demek değildir — bu obje yıkılırsa oyuncu ağa
    /// hiç iskelet göndermez ve <b>diğer oyuncular onu göremez</b>.
    /// </para>
    /// <para>
    /// <b>Neden kendini önyükleyen kalıcı tekil</b> (<c>WeaponGranter</c> deseni): sahneye elle
    /// konsaydı her yeni arena bir kurulum adımı doğururdu.
    /// </para>
    /// <para>
    /// ⚠️ <b>Avatar SAHNE KÖKÜNDE durur, rig'in ALTINA KONMAZ.</b> SDK kök eklemi
    /// <c>SetLocalPositionAndRotation</c> ile yazıyor; dolu bir ebeveyn dönüşümü ikinci kez
    /// uygulanırdı (<c>Docs/Sistem-Ozeti.md</c> §7, "retarget avatarı hareket eden kökün altına
    /// konmaz").
    /// </para>
    /// <para>
    /// ⚠️ <b>Admin'de çizilmez ve bunun için rol kontrolü YAPILMAZ</b>: <c>AppSession</c>
    /// <c>VortexArena.App</c> asmdef'indedir, bağımlılık yönü App → Core, yani Core onu göremez.
    /// Kapı şudur: etkin bir <see cref="OVRCameraRig"/> bulunamazsa gövde kurulmaz — admin
    /// gözlemcide <c>AdminSpectator</c> rig'i kapattığı için bu kapı kendiliğinden doğru davranır
    /// (aynı gerekçe <c>WeaponGranter.ResolveRig</c>'de de geçerli).
    /// </para>
    /// <para>
    /// ⚠️ Bu avatara <b>collider konmaz</b>: <c>Weapon</c>'daki atış raycast'i maskesizdir
    /// (<c>Physics.Raycast(...)</c>, layer mask yok) — kendi gövden kendi atışını yerdi.
    /// </para>
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Execution order 100'den BÜYÜK olmak zorunda:</b> <c>Calibrate()</c> o karenin
    /// UYGULANMIŞ pozunu ölçer, yani SDK'nın retarget döngüsünden ve iskeleti ağa serileştiren
    /// <c>NetworkCharacterHandler</c>'dan (<c>[DefaultExecutionOrder(100)]</c>) sonra çağrılmalıdır.
    /// </remarks>
    [DefaultExecutionOrder(30000)]
    public class LocalBodyAvatar : MonoBehaviour
    {
        /// <summary>Prefabın <c>Resources</c> altındaki adı (önyükleme bunu yükler).
        /// ⚠️ Ad ve konum DEĞİŞMEZ — <c>Resources.Load</c> ile yükleniyor, taşınırsa oyuncu ağa
        /// gövde göndermez ve kimse onu göremez.</summary>
        private const string PrefabResourceName = "LocalBodyAvatar";

        /// <summary>Rig/oturum bulunamadığında iki arama arasındaki en kısa süre (sn).</summary>
        private const float RigSearchIntervalSeconds = 0.5f;

        /// <summary>
        /// Arena kalibrasyonu tamamlandıktan sonra gövde kalibrasyonuna kadar beklenen süre (sn).
        /// <para>⚠️ Gecikme <b>zorunludur</b>: oyuncu arena kalibrasyonunu kumandanın ucunu zemin
        /// işaretine değdirerek yapıyor, yani o anda EĞİLMİŞ durumda. <c>Calibrate()</c> gövde
        /// oranlarını o andaki poza sabitliyor — eğilmiş bir oyuncudan alınan ölçü maçın kalanı
        /// boyunca yanlış boy demektir.</para>
        /// </summary>
        private const float BodyCalibrationDelaySeconds = 3f;

        public static LocalBodyAvatar Instance { get; private set; }

        [Tooltip("Ağ köprüsü + SDK sürücüsü. Boşsa alt ağaçtan aranır.")]
        [SerializeField] private ArenaNetCharacterBehaviour character;

        [Tooltip("Gövde oranını sabitleyen SDK bileşeni. Boşsa alt ağaçtan aranır.")]
        [SerializeField] private NetworkCharacterRetargeter retargeter;

        [Tooltip("Gövdenin görsel kökü. Boşsa karakterin kendisi kullanılır.")]
        [SerializeField] private GameObject visualRoot;

        [Tooltip("Oyuncunun gözlükte gördüğü eller: gövde meshinden kesilmiş el meshi. " +
                 "Tools > VortexArena > Avatars > Build First-Person Hands Mesh üretir ve bağlar.")]
        [SerializeField] private SkinnedMeshRenderer firstPersonHands;

        [Tooltip("Gövde oranını oyuncunun boyuna sabitle (SDK Calibrate()). KAPALI olmalı — " +
                 "gerekçe koddaki açıklamada.")]
        [SerializeField] private bool calibrateBodyProportions;

        private OVRCameraRig _rig;
        private float _rigSearchTime = float.NegativeInfinity;

        private bool _initialized;

        /// <summary>"Sensör başlamadı" hatası bir kez basılır (Update 72/sn).</summary>
        private bool _sourceProviderWarned;

        /// <summary>
        /// Sensörün başlaması için tanınan süre (sn). Anında bakılmaz: <c>OVRBody</c> izin
        /// verilmemişse kendini kapatıp <c>PermissionGranted</c>'ı bekliyor ve izin diyalogu
        /// cevaplanınca kendini geri açıyor — hemen hata basmak bu meşru yolu yalancı çıkarırdı.
        /// </summary>
        private const float SourceProviderGraceSeconds = 5f;

        private float _sourceProviderGrace = SourceProviderGraceSeconds;

        /// <summary>Son görülen <see cref="ArenaCalibrator.CalibrationGeneration"/>; değişmesi
        /// "arena yeniden hizalandı, gövde oranı yeniden ölçülmeli" demektir.</summary>
        private int _calibrationGeneration = -1;

        /// <summary>Bekleyen bir gövde kalibrasyonu var mı. Süre dolmasıyla BİRLİKTE değil, ondan
        /// AYRI tutulur: süre dolduğunda sensör hâlâ hazır olmayabilir ve o hâlde beklemeye devam
        /// edilir (gerekçe <see cref="TickBodyCalibration"/>).</summary>
        private bool _calibrationPending;

        /// <summary>Gövde kalibrasyonuna kalan süre (sn); yalnız <see cref="_calibrationPending"/>
        /// iken anlamlıdır.</summary>
        private float _calibrationCountdown;

        /// <summary>Kalibrasyondan sonra uygulanan ölçeğin bir kez raporlanması bekleniyor mu.</summary>
        private bool _scaleReportPending;

        /// <summary>Ölçeğin <c>ScaleRange</c> sınırına "dayanmış" sayılması için tolerans.</summary>
        private const float ScaleClampEpsilon = 0.005f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var prefab = Resources.Load<GameObject>(PrefabResourceName);
            if (prefab == null)
            {
                // Yerel çizim zaten yok; kayıp olan UZAK görünürlüktür — bu yüzden uyarı da onu söyler.
                Debug.LogWarning($"[LocalBodyAvatar] 'Resources/{PrefabResourceName}' prefabı bulunamadı; " +
                                 "ağa gövde gitmeyecek, yani diğer oyuncular bu oyuncuyu göremeyecek.");
                return;
            }

            // ⚠️ Parent VERİLMEZ (gerekçe sınıf özetinde).
            GameObject instance = Instantiate(prefab);
            instance.name = prefab.name;
            DontDestroyOnLoad(instance);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (character == null)
            {
                character = GetComponentInChildren<ArenaNetCharacterBehaviour>(true);
            }

            if (retargeter == null)
            {
                retargeter = GetComponentInChildren<NetworkCharacterRetargeter>(true);
            }

            if (visualRoot == null && character != null)
            {
                visualRoot = character.gameObject;
            }

            if (character == null || retargeter == null)
            {
                // ⚠️ Uyarı değil HATA: bu durumda oyuncunun gövdesi ağa HİÇ gitmez, yani diğer
                // oyuncular onu göremez — ve eksiklik sahada "ağ bozuk" diye okunur, oysa tek eksik
                // prefab bağıdır. Sessiz kalmak teşhisi ağ katmanına yönlendirip saatler yakar.
                Debug.LogError("[LocalBodyAvatar] ArenaNetCharacterBehaviour / NetworkCharacterRetargeter " +
                               "bulunamadı; ağa gövde gitmeyecek. Resources/LocalBodyAvatar.prefab " +
                               "içindeki Character objesine bu bileşenler kurulmalı.", this);
                enabled = false;
                return;
            }

            // ⚠️ Kurulumdan ÖNCE tüm alt ağaç PASİF durur (renderer değil, tümden kapalı) ve bu tek
            // meşru kapatmadır: kurulmamış bir retargeter her karede "Ownership is None" hatası
            // basar. Admin'de rig hiç gelmediği için burada kapalı kalır — o da doğrusudur.
            if (visualRoot != null)
            {
                visualRoot.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (!_initialized)
            {
                TryInitialize();
                return;
            }

            TickSourceProviderCheck();

            // ⚠️ Rapor kalibrasyondan ÖNCE tiklenir ve bu sıra kasıtlıdır: uygulanan ölçek
            // Calibrate()'in ölçtüğü orandan bir kare SONRA yazılıyor (SDK onu retarget
            // döngüsünde tazeliyor), yani aynı karede okumak kalibrasyon ÖNCESİ değeri basardı.
            TickScaleReport();
            TickBodyCalibration();
        }

        /// <summary>
        /// Gövdeyi ancak <b>her iki koşul</b> sağlanınca kurar: etkin bir rig (yani rol gerçekten
        /// oyuncu) ve sunucudan alınmış bir <c>playerId</c>.
        /// <para>⚠️ <c>playerId</c> beklenir çünkü gövdenin ağa akan blob'u onunla etiketleniyor
        /// (§6.9); kimliksiz gönderilen bir kare sunucuda sahipsiz kalırdı.</para>
        /// </summary>
        private void TryInitialize()
        {
            if (ResolveRig() == null)
            {
                return;
            }

            ArenaClient client = ArenaClient.Instance;
            if (client == null || client.PlayerId <= 0)
            {
                return;
            }

            _initialized = true;

            // ⚠️ SIRA ÖNEMLİ — obje önce AKTİF edilir, sonra kurulur. Buraya kadar pasifti; pasif
            // objede Awake hiç koşmaz ve kurulumun ihtiyaç duyduğu bileşenler çözülmemiş olur (SDK
            // sahipliği None kalır → gövde ağa hiç gitmez). SetActive(true) eksik Awake'leri kendi
            // çağrısı içinde senkron koşturur. ⚠️ Bu, objenin SON kez etkinleştirilmesidir — bir
            // daha KAPATILMAZ (gerekçe ApplyFirstPersonVisibility'de).
            if (visualRoot != null)
            {
                visualRoot.SetActive(true);
            }

            ApplyFirstPersonVisibility();

            character.Initialize(client.PlayerId, hasInputAuthority: true);
            _sourceProviderGrace = SourceProviderGraceSeconds;

            // İlk gövde ölçüsü de gecikmeli alınır: oyuncu bağlandığı anda ayakta olmayabilir.
            _calibrationGeneration = ArenaCalibrator.CalibrationGeneration;
            _calibrationCountdown = BodyCalibrationDelaySeconds;
            _calibrationPending = true;
        }

        /// <summary>
        /// Birinci şahıs görünümünü kurar: <b>gövdenin tamamı söner, yalnız el meshi çizilir.</b>
        /// Oyuncu kendi gövdesini, kollarını ve bacaklarını hiçbir zaman görmez.
        /// <para>
        /// ⚠️ <b>Ayrım renderer bazındadır ve el meshi ADIYLA değil REFERANSLA seçilir</b>
        /// (<see cref="firstPersonHands"/>): araç onu prefaba bağlıyor, yani ad değişse bile
        /// doğru renderer açık kalır.
        /// </para>
        /// <para>
        /// ⚠️ <b>Obje KAPATILMAZ</b> (<c>SetActive(false)</c>) ve bu bir üslup tercihi değildir:
        /// karakterin üstündeki sensör kaynağı bir <c>OVRBody</c>'dir ve objeyi kapatmak onun
        /// <c>OnDisable</c>'ını çalıştırır — açık son örnek de kapanınca <c>StopBodyTracking</c>
        /// çağrılır. Geri açıldığında <c>OnEnable</c> yeniden başlatmayı dener ve
        /// <b>başaramazsa kendini KALICI olarak kapatır</b>, bir daha denemez. Kapatılan bir gövde
        /// ağa da akmaz, yani oyuncu diğerlerinin ekranından kaybolurdu. Renderer kapatmak aynı
        /// görsel sonucu verir ve hiçbir yaşam döngüsü olayını tetiklemez.
        /// </para>
        /// <para>
        /// ⚠️ <b>Kemik gizleme/ölçekleme ile YAPILMAZ ve o yol geri getirilmez:</b> ağa giden
        /// iskelet kemiklerin canlı transformlarından okunuyor ve okuma <c>localScale</c>'i de
        /// kapsıyor (<c>SkeletonJobs.GetPoseJob</c>) — sıfırlanan bir kemik uzak tarafta gövdeyi
        /// çökertir. Renderer kapatmak transformlara hiç dokunmaz, yani telde tam gövde gider.
        /// </para>
        /// <para>Tek çağrı yeter: gövdeye sonradan renderer eklenmiyor.</para>
        /// </summary>
        private void ApplyFirstPersonVisibility()
        {
            if (visualRoot == null)
            {
                return;
            }

            Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].enabled = renderers[i] == firstPersonHands;
                }
            }

            if (firstPersonHands == null)
            {
                // ⚠️ Uyarı değil HATA: oyuncunun ekranında çizilen TEK şey elleridir, yani bu
                // eksiklik "hiçbir şey görünmüyor" olarak yaşanır ve izleme arızasıyla karıştırılır.
                // Oysa tek eksik, üretilmemiş/bağlanmamış bir mesh asset'idir.
                Debug.LogError(
                    "[LocalBodyAvatar] Birinci şahıs el meshi bağlı değil — oyuncu kendi ellerini " +
                    "GÖRMEYECEK (gövde zaten çizilmiyor). Tools > VortexArena > Avatars > " +
                    "Build First-Person Hands Mesh çalıştırılmalı: araç meshi üretip " +
                    "Resources/LocalBodyAvatar.prefab'a bağlar.", this);
                return;
            }

            // Renderer prefabda KAPALI gelir (admin'de ve kurulum tamamlanmadan çizilmesin diye);
            // açmak yukarıdaki döngünün işi. Objenin de kapatılmış olma ihtimaline karşı bu kapı:
            // kapalı objede renderer'ı açmak sessizce hiçbir şey çizmezdi.
            if (!firstPersonHands.gameObject.activeSelf)
            {
                firstPersonHands.gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// Arena hizalaması değiştiyse gövde oranını yeniden ölçtürür (gecikmeli).
        /// <para>⚠️ <b>İki kalibrasyon KARIŞTIRILMAZ:</b> <see cref="ArenaCalibrator"/> rig'i
        /// fiziksel arenaya hizalar (bizim, sunucu-otoriter durum §10.6);
        /// <c>CharacterRetargeter.Calibrate()</c> karakterin gövde oranını oyuncununkine sabitler
        /// (SDK'nın, tamamen yerel). Buradaki tek bağ şudur: arena yeniden hizalanınca oyuncu
        /// zaten eğilip doğrulmuştur, yani boy ölçüsünü tazelemek için doğru an odur.</para>
        /// </summary>
        private void TickBodyCalibration()
        {
            // ⚠️ VARSAYILAN KAPALI — açmadan önce aşağıdaki bedeli oku.
            //
            // UZAK GÖVDEYİ BOZAR. İskelet blob'u SerializationCompressionType.High ile kodlanıyor
            // ve o kip eklemleri "joint lengths" ile sıkıştırıyor. Calibrate() gönderenin gövde
            // ORANLARINI değiştirdiği için, alıcının hedef iskeleti artık gönderenin kodladığı
            // uzunluklarla uyuşmaz — sonuç, uzak avatarda rastgele bozuk duruşlardır. Kapalıyken
            // herkes prefabın oranlarını kullanır ve iki uç eşleşir.
            //
            // ⚠️ Anahtarın İKİ ucu birden var ve ters yönde çekiyorlar: açmak oyuncunun kendi
            // ellerini gerçek eline yaklaştırır (gövde oranı oyuncununkine sabitlenir), ama aynı
            // hamle uzak avatarı bozar. Uzak taraf tercih edilir — yerelde el birkaç santim kayabilir,
            // uzakta ise gövde tümden bozuk duruşlara girer.
            if (!calibrateBodyProportions)
            {
                return;
            }

            int generation = ArenaCalibrator.CalibrationGeneration;
            if (generation != _calibrationGeneration)
            {
                _calibrationGeneration = generation;
                _calibrationCountdown = BodyCalibrationDelaySeconds;
                _calibrationPending = true;
            }

            if (!_calibrationPending)
            {
                return;
            }

            if (_calibrationCountdown > 0f)
            {
                _calibrationCountdown -= Time.unscaledDeltaTime;
                return;
            }

            // ⚠️ Sürenin dolması YETMEZ: SDK'nın Calibrate()'i geçerli bir poz yoksa SESSİZCE döner
            // (hiçbir şey yapmaz, hiçbir şey da basmaz). Tek atışlık bir çağrı tam bu pencereye denk
            // gelirse kalibrasyon oturumun geri kalanı boyunca HİÇ yapılmamış olur. Bu yüzden bayrak
            // koşul sağlanana dek AÇIK kalır ve her karede yeniden denenir.
            if (!retargeter.RetargeterValid)
            {
                return;
            }

            _calibrationPending = false;
            retargeter.Calibrate();
            _scaleReportPending = true;
        }

        /// <summary>
        /// Kalibrasyondan sonra karaktere uygulanan gövde ölçeğini <b>bir kez</b> raporlar.
        /// <para>
        /// Sebep: ölçek <c>SkeletonRetargeter.ScaleRange</c> ile <b>kelepçelenir</b> (varsayılan
        /// 0.8–1.2). Oyuncunun boyu modelin boyundan bu aralığın dışında farklıysa karakter
        /// oyuncuyla aynı boyda OLAMAZ. Bunun iki sonucu var: <b>diğer oyuncular</b> onu yanlış
        /// boyda görür, <b>kendisi</b> de ellerini gerçek elinden kaymış görür (kol uzunluğu
        /// tutmayınca el meshi gerçek elin durduğu yere erişemez). Sınıra dayanmış bir ölçek gözle
        /// yanlış oranlardan ayırt EDİLEMEZ, bu yüzden tahmin edilmez, ölçülür.
        /// </para>
        /// <para>Yalnız <see cref="calibrateBodyProportions"/> açıkken anlamlıdır: bayrak
        /// <c>Calibrate()</c>'ten sonra kalkıyor.</para>
        /// </summary>
        private void TickScaleReport()
        {
            if (!_scaleReportPending || !retargeter.RetargeterValid)
            {
                return;
            }

            var skeleton = retargeter.SkeletonRetargeter;
            if (skeleton == null || !skeleton.IsInitialized)
            {
                return;
            }

            _scaleReportPending = false;

            float scale = skeleton.RootScale.x;
            Vector2 range = skeleton.ScaleRange;
            string line = $"[LocalBodyAvatar] Gövde ölçeği {scale:F3} " +
                          $"(izin verilen aralık {range.x:F2}–{range.y:F2}).";

            if (scale <= range.x + ScaleClampEpsilon || scale >= range.y - ScaleClampEpsilon)
            {
                Debug.LogWarning(
                    line + " Değer aralığın SINIRINDA: karakter oyuncunun boyuna yetişemiyor, yani " +
                    "diğer oyuncular bu oyuncuyu yanlış boyda görür. " +
                    "Resources/LocalBodyAvatar.prefab içindeki NetworkCharacterRetargeter > " +
                    "Scale Range genişletilmeli.", this);
                return;
            }

            Debug.Log(line, this);
        }

        /// <summary>
        /// Kurulumdan sonra gerçekten <b>ağa akan bir gövde</b> oluştu mu — oluşmadıysa tek bir
        /// eyleme dönük hata basar.
        /// <para>⚠️ Ölçüt sensörün açık olması DEĞİL, retargeter'ın poz uygulamasıdır: ağa giden
        /// iskeleti üreten kapı odur. Yalnız "sağlayıcı açık mı" diye bakılsaydı, açık kalıp
        /// geçerli veri üretmeyen bir sensör <b>hiç uyarı basmadan</b> oyuncuyu diğerlerinin
        /// ekranında görünmez bırakırdı.</para>
        /// <para>⚠️ Arıza <b>iki şeyi birden</b> götürür: oyuncu kendi ellerini kaybeder (çizilen
        /// tek şey onlar) ve başkalarının ekranından tümden silinir. İlki hemen fark edilir, ikincisi
        /// hiç fark edilmeyebilir — bu yüzden sebep tahmine bırakılmaz, açıkça yazılır.</para>
        /// <para>Gerekçe: <c>OVRBody</c> başlatamadığında kendi uyarısını basıp susuyor ve o satır
        /// bu soruya bağlanmıyor; bağı burada açıkça kuruyoruz. Süre tanınmasının sebebi
        /// <see cref="SourceProviderGraceSeconds"/>'da.</para>
        /// </summary>
        private void TickSourceProviderCheck()
        {
            if (_sourceProviderWarned || retargeter.RetargeterValid)
            {
                return;
            }

            _sourceProviderGrace -= Time.unscaledDeltaTime;
            if (_sourceProviderGrace > 0f)
            {
                return;
            }

            _sourceProviderWarned = true;

            // İki farklı arıza aynı belirtiyi veriyor ama çözümleri ayrı — hangisi olduğu söylenir.
            string cause = character.IsSourceProviderRunning
                ? "Body tracking açık ama geçerli bir gövde pozu hiç üretmedi"
                : "Body tracking hiç başlamadı (sebebi konsolda bunun üstündeki [OVRBody] satırı söyler)";

            Debug.LogError(
                $"[LocalBodyAvatar] {cause} — ağa gövde akmayacak, yani diğer oyuncular bu oyuncuyu " +
                "göremeyecek. Sık görülen iki sebep: (1) editörden Link ile koşuluyor ve Meta Quest " +
                "Link uygulamasında ilgili geliştirici çalışma zamanı özelliği kapalı, (2) cihazda " +
                "BODY_TRACKING izni verilmemiş. Düzelttikten sonra oyunu yeniden başlat.", this);
        }

        /// <summary>Etkin rig'i bulur. Referans önbelleğe alınır ama null'a düşünce (sahne değişimi,
        /// gözlemcinin kapattığı rig) yeniden aranır.
        /// <para>⚠️ Arama <b>kısılır</b>: rig hiç yokken (admin gözlemci — <c>AdminSpectator</c> rig'i
        /// kapatır) bu kapı kalıcı olarak boş döner ve kısılmasaydı her karede bir sahne geneli tip
        /// araması yapılırdı. Rig insan zaman ölçeğinde gelir; saniyede birkaç deneme yeter.</para></summary>
        private OVRCameraRig ResolveRig()
        {
            if (_rig != null && _rig.isActiveAndEnabled)
            {
                return _rig;
            }

            if (Time.unscaledTime - _rigSearchTime < RigSearchIntervalSeconds)
            {
                return null;
            }

            _rigSearchTime = Time.unscaledTime;
            _rig = FindFirstObjectByType<OVRCameraRig>();
            return _rig;
        }
    }
}
