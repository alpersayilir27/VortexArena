using Meta.XR.Movement.Networking;
using UnityEngine;
using VortexArena.Net;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Yerel oyuncunun gövdesi — <b>yalnız ağ kaynağı</b>: hiç çizilmez, başkalarının gördüğü
    /// gövdeyi üretir.
    /// <para>
    /// ⚠️ <b>Oyuncu kendi gövdesinden HİÇBİR ŞEY görmez</b> — gövde de kol da el de çizilmez.
    /// Oyuncunun gözlükte gördüğü eller rig'in <b>sentetik elleridir</b>
    /// (<c>VA_CameraRig</c> → <c>OVRHandVisualLeft/Right</c>, ISDK <c>SyntheticHand</c>) ve bu
    /// sınıfın onlarla hiçbir ilgisi yoktur.
    /// </para>
    /// <para>
    /// ⚠️ <b>Görünmezlik yalnız ÇİZİMDEDİR, telde tam gövde gider:</b> Renderer'lar kapatılır,
    /// hiçbir kemiğe dokunulmaz — ağa giden iskelet kemiklerin canlı transformlarından okunuyor.
    /// </para>
    /// <para>
    /// <b>Uzak avatarla AYNI FBX, AYNI retarget config, AYNI kod yolu</b> (prefablar ayrıdır:
    /// <c>Avatars/Resources/LocalBodyAvatar.prefab</c> ve <c>App/Prefabs/RemoteAvatar.prefab</c>).
    /// Tek fark
    /// <see cref="ArenaNetCharacterBehaviour.HasInputAuthority"/>'dir: burada <c>true</c>, yani
    /// gövde Meta Movement SDK'nın body tracking'inden çözülür ve sonucu ağa akar. Uzak tarafta
    /// <c>false</c> olur ve aynı prefab gelen iskeleti uygular.
    /// </para>
    /// <para>
    /// ⚠️ <b>Obje neden var ve neden silinemez:</b> başkalarının gördüğü gövde tam olarak buradan
    /// çıkıyor. Görünmez olması "gereksiz" demek değildir — bu obje yıkılırsa oyuncu ağa hiç
    /// iskelet göndermez ve <b>diğer oyuncular onu göremez</b>. Artık yerelde tek bir pikseli bile
    /// çizilmediği için bu refleks daha da tehlikelidir: silenin ekranında hiçbir şey değişmez,
    /// bedeli yalnız <b>başkalarının</b> ekranında görülür.
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
    /// <para>
    /// ⚠️ <b>Gövde oranı burada KALİBRE EDİLMEZ</b> — <c>CharacterRetargeter.Calibrate()</c> hiç
    /// çağrılmaz ve o yol geri gelmez: gönderenin gövde ORANLARINI değiştirmek, blob'un eklem
    /// uzunluğu sıkıştırmasıyla (<c>SerializationCompressionType.High</c>) uyuşmaz ve uzak avatarı
    /// bozuk duruşlara sokar. Oyuncular arası boy farkı bunun yerine tek bir üniform çarpanla
    /// taşınır (<c>BodyScaleState</c> ölçer, <c>bodyScale</c> ile gider, §10.8) ve YALNIZ uzak
    /// avatara uygulanır. Bu sınıfın oradaki tek işi <see cref="EyeAnchor"/>'ı sunmaktır.
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

        public static LocalBodyAvatar Instance { get; private set; }

        [Tooltip("Ağ köprüsü + SDK sürücüsü. Boşsa alt ağaçtan aranır.")]
        [SerializeField] private ArenaNetCharacterBehaviour character;

        [Tooltip("Gövdeyi sensörden çözen SDK bileşeni. Boşsa alt ağaçtan aranır.")]
        [SerializeField] private NetworkCharacterRetargeter retargeter;

        [Tooltip("Gövdenin görsel kökü. Boşsa karakterin kendisi kullanılır.")]
        [SerializeField] private GameObject visualRoot;

        [Tooltip("Karakterin GÖZ hizası — kafa kemiğinin altında, iki gözün arasında duran boş " +
                 "işaretçi. Gövde ölçümünün referansıdır (§10.8): oyuncunun gözü ile buranın " +
                 "yüksekliği oranlanır. Boşsa ölçüm hiç yapılmaz.")]
        [SerializeField] private Transform eyeAnchor;

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

        /// <summary>
        /// Karakterin göz hizası (prefabta kafa kemiğinin altındaki işaretçi) — gövde ölçümünün
        /// referansı (§10.8). Bağlı değilse <c>null</c>; ölçen taraf o hâlde ölçmez ve bağırır.
        /// <para>⚠️ Ölçüm bunun DÜNYA konumunu okur ve o konumun ölçek-1 referansı olması
        /// <see cref="ArenaNetCharacterBehaviour"/>'ın yerel karakteri hiç ölçeklememesine bağlıdır
        /// — yoksa ikinci ölçüm çarpanı 1'e yaklaştırırdı.</para>
        /// </summary>
        public Transform EyeAnchor => eyeAnchor;

        /// <summary>
        /// Gövde gerçekten çözülüyor mu (kurulmuş + retargeter geçerli). Ölçümün ön koşuludur:
        /// pozu olmayan bir iskeletin göz hizası anlamsızdır.
        /// </summary>
        public bool IsBodyPoseValid => _initialized && retargeter != null && retargeter.RetargeterValid;

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
            // daha KAPATILMAZ (gerekçe HideAllRenderers'da).
            if (visualRoot != null)
            {
                visualRoot.SetActive(true);
            }

            HideAllRenderers();

            character.Initialize(client.PlayerId, hasInputAuthority: true);
            _sourceProviderGrace = SourceProviderGraceSeconds;
        }

        /// <summary>
        /// Gövdeyi görsel olarak tümden susturur: <b>alt ağaçtaki her Renderer kapanır, istisna
        /// yoktur.</b> Oyuncu kendi gövdesinden hiçbir şey görmez; gördüğü eller rig'in sentetik
        /// elleridir ve bu sınıfın onlarla ilgisi yoktur.
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
        /// <para>Tek çağrı yeter: gövdeye sonradan renderer eklenmiyor. Prefabda da hepsi kapalı
        /// gelir; buradaki geçiş yalnız garantidir.</para>
        /// </summary>
        private void HideAllRenderers()
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
                    renderers[i].enabled = false;
                }
            }
        }

        /// <summary>
        /// Kurulumdan sonra gerçekten <b>ağa akan bir gövde</b> oluştu mu — oluşmadıysa tek bir
        /// eyleme dönük hata basar.
        /// <para>⚠️ Ölçüt sensörün açık olması DEĞİL, retargeter'ın poz uygulamasıdır: ağa giden
        /// iskeleti üreten kapı odur. Yalnız "sağlayıcı açık mı" diye bakılsaydı, açık kalıp
        /// geçerli veri üretmeyen bir sensör <b>hiç uyarı basmadan</b> oyuncuyu diğerlerinin
        /// ekranında görünmez bırakırdı.</para>
        /// <para>⚠️ Arıza <b>yerelde HİÇBİR iz bırakmaz</b> ve bu yüzden bu satır tek sinyaldir:
        /// oyuncu ellerini rig'den gördüğü için ekranında her şey normal görünür, oysa başkalarının
        /// ekranından tümden silinmiştir. Kendi başına fark edilmesi imkânsız olan bir arıza için
        /// sebep tahmine bırakılmaz, açıkça yazılır.</para>
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
                "göremeyecek. ⚠️ Oyuncunun KENDİ ekranında hiçbir belirti olmaz (eller rig'den " +
                "geliyor); bu satır tek uyarıdır. Sık görülen iki sebep: (1) editörden Link ile " +
                "koşuluyor ve Meta Quest " +
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
