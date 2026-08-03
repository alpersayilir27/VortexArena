using Meta.XR.Movement.Networking;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Net;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Yerel oyuncunun kendi gövdesi: aşağı bakınca kendi kollarını, bileklerini ve gövdesini görür.
    /// <para>
    /// <b>Uzak avatarlarla AYNI prefab, AYNI retarget config, AYNI kod yolu.</b> Tek fark
    /// <see cref="ArenaNetCharacterBehaviour.HasInputAuthority"/>'dir: burada <c>true</c>, yani
    /// gövde Meta Movement SDK'nın body tracking'inden çözülür ve sonucu ağa akar. Uzak tarafta
    /// <c>false</c> olur ve aynı prefab gelen iskeleti uygular. Böylece "kendi gördüğüm gövde" ile
    /// "başkalarının gördüğü gövde" tek doğruluk kaynağıdır.
    /// </para>
    /// <para>
    /// <b>Neden kendini önyükleyen kalıcı tekil</b> (<c>WeaponGranter</c> deseni): sahneye elle
    /// konsaydı her yeni arena bir kurulum adımı doğururdu.
    /// </para>
    /// <para>
    /// ⚠️ <b>Avatar SAHNE KÖKÜNDE durur, rig'in ALTINA KONMAZ.</b> SDK kök eklemi
    /// <c>SetLocalPositionAndRotation</c> ile yazıyor; dolu bir ebeveyn dönüşümü ikinci kez
    /// uygulanırdı (<c>Docs/Sistem-Ozeti.md</c> §7, "retarget avatarı hareket eden kökün altına
    /// konmaz"). Yerel gövdede kök zaten izleme uzayından geliyor, yani kalibrasyonla rig hareket
    /// edince gövde kendiliğinden onunla gelir.
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
    /// ⚠️ <b>Execution order 100'den BÜYÜK olmak zorunda:</b> <see cref="firstPersonOffset"/>
    /// <c>LateUpdate</c>'te uygulanıyor ve iskeleti ağa serileştiren
    /// <c>NetworkCharacterHandler</c> <c>[DefaultExecutionOrder(100)]</c>. Daha küçük bir sırada
    /// ofset gönderime de karışır, yani başkaları oyuncuyu kaymış görür.
    /// </remarks>
    [DefaultExecutionOrder(30000)]
    public class LocalBodyAvatar : MonoBehaviour
    {
        /// <summary>Prefabın <c>Resources</c> altındaki adı (önyükleme bunu yükler).
        /// ⚠️ Ad ve konum DEĞİŞMEZ — <c>Resources.Load</c> ile yükleniyor, taşınırsa oyuncu kendi
        /// gövdesini sessizce hiç görmez.</summary>
        private const string PrefabResourceName = "LocalBodyAvatar";

        /// <summary>Rig/oturum bulunamadığında iki arama arasındaki en kısa süre (sn).</summary>
        private const float RigSearchIntervalSeconds = 0.5f;

        /// <summary>
        /// Arena kalibrasyonu tamamlandıktan sonra gövde kalibrasyonuna kadar beklenen süre (sn).
        /// <para>⚠️ Gecikme <b>zorunludur</b>: oyuncu arena kalibrasyonunu kumandanın ucunu zemin
        /// işaretine değdirerek yapıyor, yani o anda EĞİLMİŞ durumda. <c>Calibrate()</c> gövde
        /// oranlarını o andaki T-poza sabitliyor — eğilmiş bir oyuncudan alınan ölçü maçın kalanı
        /// boyunca yanlış boy demektir.</para>
        /// </summary>
        private const float BodyCalibrationDelaySeconds = 3f;

        public static LocalBodyAvatar Instance { get; private set; }

        [Tooltip("Ağ köprüsü + SDK sürücüsü. Boşsa alt ağaçtan aranır.")]
        [SerializeField] private ArenaNetCharacterBehaviour character;

        [Tooltip("Gövde oranını sabitleyen SDK bileşeni. Boşsa alt ağaçtan aranır.")]
        [SerializeField] private NetworkCharacterRetargeter retargeter;

        [Tooltip("İlk poz/oturum gelene dek gizlenecek görsel kök. Boşsa karakterin kendisi kullanılır.")]
        [SerializeField] private GameObject visualRoot;

        [Tooltip("Gövdenin birinci şahısta kaydırılması (metre, gövdenin YAW uzayı: -Z geri, " +
                 "+Y yukarı). YALNIZ yerel görünümü etkiler, ağa gitmez.")]
        [SerializeField] private Vector3 firstPersonOffset = new Vector3(0f, 0f, -0.06f);

        private OVRCameraRig _rig;
        private float _rigSearchTime = float.NegativeInfinity;

        /// <summary>Gövde çiziliyor mu. ⚠️ Kurulumdan sonra görünürlük <b>renderer düzeyinde</b>
        /// yönetilir, obje kapatılarak DEĞİL — gerekçe <see cref="SetBodyVisible"/>.</summary>
        private bool _bodyVisible;

        /// <summary>Karakterin renderer'ları; kurulumda bir kez toplanır.</summary>
        private Renderer[] _bodyRenderers;

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
                // Yerel görsel yok diye oyun durmaz: tek satır uyarı, sessizce devam.
                Debug.LogWarning($"[LocalBodyAvatar] 'Resources/{PrefabResourceName}' prefabı bulunamadı; " +
                                 "yerel gövde avatarı çizilmeyecek.");
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
                // ⚠️ Uyarı değil HATA: bu durumda oyuncu kendi gövdesini HİÇ görmez ve eksiklik
                // sahada "izleme çalışmıyor" diye okunur — oysa tek eksik prefab bağıdır. Sessiz
                // kalmak teşhisi Meta SDK'sına/sensöre yönlendirip saatler yakar.
                Debug.LogError("[LocalBodyAvatar] ArenaNetCharacterBehaviour / NetworkCharacterRetargeter " +
                               "bulunamadı; yerel gövde çizilmeyecek. Resources/LocalBodyAvatar.prefab " +
                               "içindeki Character objesine bu bileşenler kurulmalı.", this);
                enabled = false;
                return;
            }

            // ⚠️ Kurulumdan ÖNCE tüm alt ağaç PASİF durur (görünürlük değil, tümden kapalı) ve bu
            // tek meşru kapatmadır: kurulmamış bir retargeter hem T-pozunda bir manken çizer, hem
            // de her karede "Ownership is None" hatası basar. Admin'de rig hiç gelmediği için burada
            // kapalı kalır — o da doğrusudur.
            if (visualRoot != null)
            {
                visualRoot.SetActive(false);
            }

            _bodyVisible = false;
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

            // Gövde YALNIZ gerçekten sürülürken çizilir ve bunun İKİ ayrı koşulu var: etkin bir rig
            // (sahne değişimi, gözlemcinin kapattığı rig) ve retargeter'ın o kare bir poz gerçekten
            // UYGULAMIŞ olması.
            // ⚠️ İkincisi "kurulmuş olmak" ile aynı şey DEĞİLDİR: SDK'nın CharacterRetargeter.Update'i
            // sensör geçerli veri üretene dek hiçbir şey uygulamadan döner — o pencerede karakter
            // prefabdan geldiği yerde, yani DÜNYA ORİJİNİNDE ve T-pozunda durur. Kurulur kurulmaz
            // görünür yapmak, oyuncunun haritanın ortasında kendi kopyasını görmesi demekti.
            OVRCameraRig rig = ResolveRig();
            SetBodyVisible(rig != null && retargeter.RetargeterValid);

            TickSourceProviderCheck();

            // ⚠️ Rapor kalibrasyondan ÖNCE tiklenir ve bu sıra kasıtlıdır: uygulanan ölçek
            // Calibrate()'in ölçtüğü orandan bir kare SONRA yazılıyor (SDK onu retarget
            // döngüsünde tazeliyor), yani aynı karede okumak kalibrasyon ÖNCESİ değeri basardı.
            TickScaleReport();
            TickBodyCalibration();
        }

        /// <summary>
        /// Gövdeyi birinci şahıs için kaydırır — <b>yalnız görüntüde</b>.
        /// <para>Retarget çıktısı gövdeyi izlenen kafanın altına oturtur; modelin oranları
        /// oyuncununkine birebir oturmadığında sonuç "gövde kameraya göre bir tık ileride" olur ve
        /// oyuncu aşağı bakınca kendi göğsünün içini görür. Ayarı tek bir ofsettir
        /// (<see cref="firstPersonOffset"/>).</para>
        /// <para>
        /// ⚠️ <b>Neden <c>LateUpdate</c> ve neden geri alınmıyor:</b> ağa giden kök gönderim anında
        /// okunuyor (<c>NetworkCharacterHandler</c>, order 100); bu sınıf 30000'de olduğu için
        /// ofset o okumadan SONRA basılır ve tele hiç girmez. Geri almak da gerekmez — SDK kökü
        /// HER KARE yeniden yazıyor (<c>CharacterRetargeter.LateUpdate</c> → <c>ApplyPose</c>),
        /// yani ofset birikmez. ⚠️ Aynısı kemik <b>ölçeği</b> için geçerli DEĞİLDİR: onu SDK
        /// yazmadığı için <see cref="LocalAvatarBoneHider"/> gizlemeyi her kare elle geri almak
        /// zorundadır.
        /// </para>
        /// <para>Ofset gövdenin tam rotasyonunda değil <b>yaw</b>'ında uygulanır: oyuncu
        /// eğildiğinde "geri" yönü yukarı/aşağı kaymasın.</para>
        /// </summary>
        private void LateUpdate()
        {
            if (!_initialized || firstPersonOffset == Vector3.zero)
            {
                return;
            }

            Transform root = retargeter.transform;
            Vector3 forward = root.rotation * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f)
            {
                return;
            }

            root.position += Quaternion.LookRotation(forward, Vector3.up) * firstPersonOffset;
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
            // sahipliği None kalır → karakter T-pozunda donar). SetActive(true) eksik Awake'leri
            // kendi çağrısı içinde senkron koşturur, yani bu satırdan sonra karakter kurulmaya
            // hazırdır. ⚠️ Bu, objenin SON kez etkinleştirilmesidir — bir daha KAPATILMAZ.
            if (visualRoot != null)
            {
                visualRoot.SetActive(true);
            }

            _bodyRenderers = visualRoot != null
                ? visualRoot.GetComponentsInChildren<Renderer>(true)
                : System.Array.Empty<Renderer>();
            _bodyVisible = true;

            character.Initialize(client.PlayerId, hasInputAuthority: true);
            _sourceProviderGrace = SourceProviderGraceSeconds;

            // Kurulum bitti ama retargeter henüz TEK bir poz uygulamadı — gövde şu an prefabdan
            // geldiği yerde, dünya orijininde duruyor. Görünürlük burada da doğru değere çekilir
            // ki ilk Update'e kadar geçen karede oyuncu kendi kopyasını haritada görmesin.
            SetBodyVisible(retargeter.RetargeterValid);

            // İlk gövde ölçüsü de gecikmeli alınır: oyuncu bağlandığı anda ayakta olmayabilir.
            _calibrationGeneration = ArenaCalibrator.CalibrationGeneration;
            _calibrationCountdown = BodyCalibrationDelaySeconds;
            _calibrationPending = true;
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
            // gelirse kalibrasyon oturumun geri kalanı boyunca HİÇ yapılmamış olur: karakterin
            // oranları oyuncununkine sabitlenmez ve gövde birinci şahısta kameraya göre yanlış
            // yerde durur. Bu yüzden bayrak koşul sağlanana dek AÇIK kalır ve her karede yeniden
            // denenir.
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
        /// oyuncuyla aynı boyda OLAMAZ — ve bunun birinci şahıstaki görünümü "gövde kameraya göre
        /// yanlış yerde / aşağı bakınca gövdenin içi" olur. Sınıra dayanmış bir ölçek gözle
        /// yanlış oranlardan ayırt EDİLEMEZ, bu yüzden tahmin edilmez, ölçülür.
        /// </para>
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
                    "gövde kameraya göre kaymış görünür. Resources/LocalBodyAvatar.prefab içindeki " +
                    "NetworkCharacterRetargeter > Scale Range genişletilmeli.", this);
                return;
            }

            Debug.Log(line, this);
        }

        /// <summary>
        /// Gövdeyi gizler/gösterir — <b>yalnız renderer'ları kapatarak</b>.
        /// <para>
        /// ⚠️ <b>Obje KAPATILMAZ</b> (<c>SetActive(false)</c>) ve bu bir üslup tercihi değildir:
        /// karakterin üstündeki sensör kaynağı bir <c>OVRBody</c>'dir ve objeyi kapatmak onun
        /// <c>OnDisable</c>'ını çalıştırır — açık son örnek de kapanınca <c>StopBodyTracking</c>
        /// çağrılır. Geri açıldığında <c>OnEnable</c> yeniden başlatmayı dener ve
        /// <b>başaramazsa kendini KALICI olarak kapatır</b>, bir daha denemez. Yani rig'in bir an
        /// kaybolduğu her harita geçişi, gövdeyi oturumun geri kalanı boyunca sessizce
        /// öldürebilecek bir kumar olurdu. Renderer kapatmak aynı görsel sonucu verir ve hiçbir
        /// yaşam döngüsü olayını tetiklemez.
        /// </para>
        /// <para>Tek meşru <c>SetActive(false)</c> kurulumdan ÖNCEDİR (<see cref="Awake"/>) — orada
        /// sensör zaten hiç açılmamıştır, dolayısıyla kapatılacak bir izleme de yoktur.</para>
        /// </summary>
        private void SetBodyVisible(bool visible)
        {
            if (_bodyVisible == visible || _bodyRenderers == null)
            {
                return;
            }

            _bodyVisible = visible;
            for (int i = 0; i < _bodyRenderers.Length; i++)
            {
                if (_bodyRenderers[i] != null)
                {
                    _bodyRenderers[i].enabled = visible;
                }
            }
        }

        /// <summary>
        /// Kurulumdan sonra gerçekten <b>çizilebilir bir gövde</b> oluştu mu — oluşmadıysa tek bir
        /// eyleme dönük hata basar.
        /// <para>⚠️ Ölçüt sensörün açık olması DEĞİL, retargeter'ın poz uygulamasıdır: gövdeyi
        /// çizen kapı odur (<see cref="Update"/>). Yalnız "sağlayıcı açık mı" diye bakılsaydı,
        /// açık kalıp geçerli veri üretmeyen bir sensör <b>hiç uyarı basmadan</b> görünmez bir
        /// gövdeye yol açardı.</para>
        /// <para>Gerekçe: <c>OVRBody</c> başlatamadığında kendi uyarısını basıp susuyor ve o satır
        /// "gövdem neden yok" sorusuna bağlanmıyor; bağı burada açıkça kuruyoruz. Süre tanınmasının
        /// sebebi <see cref="SourceProviderGraceSeconds"/>'da.</para>
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
                $"[LocalBodyAvatar] {cause} — yerel gövde çizilmeyecek ve ağa gövde akmayacak. Sık " +
                "görülen iki sebep: (1) editörden Link ile koşuluyor ve Meta Quest Link " +
                "uygulamasında ilgili geliştirici çalışma zamanı özelliği kapalı, (2) cihazda " +
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
