using TMPro;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.Combat;
using VortexArena.Net;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Uzak oyuncu hayaleti prefab sürücüsü: RemotePlayerRegistry'den interpolasyonlu
    /// arena-uzayı pozunu okur, ArenaSpace ile dünyaya çevirip baş/el transformlarına
    /// uygular. İlk poz gelene dek görseller gizli kalır. Ad etiketi kameraya döner;
    /// takım rengi MaterialPropertyBlock ile _BaseColor'a yazılır (URP Lit).
    /// RemotePlayerSpawner tarafından Instantiate + Initialize ile kurulur.
    /// <para>
    /// Snapshot'taki alive bayrağı okunur — ölü oyuncu <b>hayalet gövdeye</b> döner (yarı saydam,
    /// iki yüzü de çizilen <c>VortexArena/AvatarGhost</c>; dost mavi, düşman kırmızı), ad
    /// etiketine " (ölü)" eklenir ve vuruş kutuları kapatılır (ölüye ateş edilemez). <b>Aynı
    /// hayalet görünümü kalibresiz oyuncuda da kullanılır</b> ve orada turuncuya nabız atar —
    /// kalibresizlik ölümü EZER.
    /// </para>
    /// <para>
    /// <b>Elde tutulan eşya</b> (§6.6): snapshot'tan gelen <c>itemL</c>/<c>itemR</c> baytları
    /// <see cref="NetItemCatalog"/> ile prefaba çözülür ve ilgili elin pozundan sürülür. Örnekler
    /// yalnız durum DEĞİŞİNCE kurulur/yıkılır; kare başına yapılan iş yalnız transform yazmaktır.
    /// Kurulan örnek bir <b>görseldir</b>, çalışan bir silah değil — oyun bileşenleri
    /// <see cref="SterilizeVisual"/>'da sökülür.
    /// </para>
    /// </summary>
    public class RemoteAvatar : MonoBehaviour
    {
        [Header("Görseller")]
        [Tooltip("Karakterli avatarda kullanılmaz; etiket/eski kapsül yolu için kafa transformu.")]
        [SerializeField] private Transform head;
        [Tooltip("Gövde kapsülü; kafanın BodyDropMeters altında, yalnız yaw döner (opsiyonel).")]
        [SerializeField] private Transform body;
        [SerializeField] private Transform handL;
        [SerializeField] private Transform handR;
        [SerializeField] private TMP_Text nameLabel;
        [SerializeField] private Renderer[] teamRenderers;

        [Tooltip("Karakter mesh'i — canlı+kalibreli oyuncuda buna HİÇ dokunulmaz. Takım rengi " +
                 "buraya YAZILMAZ (düşmanı işaretlemek duvar arkası avantaj olurdu); yalnız " +
                 "hayalet durumunda gizlenir ya da hayalet materyaline çevrilir.")]
        [SerializeField] private Renderer[] bodyRenderers;

        [Header("Hayalet gövde (ölü / kalibresiz)")]
        [Tooltip("Yarı saydam hayalet materyali (VortexArena/AvatarGhost). BOŞSA hayalet " +
                 "görünümü hiç uygulanmaz — ölü oyuncu canlıdan ayırt edilemez.")]
        [SerializeField] private Material ghostMaterial;

        [Tooltip("Ayrı hayalet gövde alt ağacı (Starter robot). BOŞSA karakterin KENDİ mesh'i " +
                 "hayalet materyaliyle çizilir; ikisi de geçerli kurulumdur.")]
        [SerializeField] private GameObject ghostRoot;

        [Header("Karakter")]
        [Tooltip("Bağlıysa gövde ağdan gelen Movement SDK iskeletiyle çizilir; boşsa eski " +
                 "kafa/el/kapsül yolu kullanılır.")]
        [SerializeField] private ArenaNetCharacterBehaviour character;

        [Tooltip("YEREL oyuncuyla aynı takımdayken görünen dost göstergesi (kafanın üstündeki küp).")]
        [SerializeField] private GameObject friendMarker;

        [Tooltip("İlk poz gelene dek gizlenecek görsel kök. Boşsa teamRenderers listesi kullanılır.")]
        [SerializeField] private GameObject visualRoot;

        [Header("Vuruş kutuları")]
        [Tooltip("Kafa + gövde collider'ları; boş bırakılırsa çocuklardan otomatik toplanır.")]
        [SerializeField] private Collider[] hitColliders;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Color TeamRedColor = new Color(0.85f, 0.20f, 0.20f);
        private static readonly Color TeamBlueColor = new Color(0.20f, 0.40f, 0.90f);
        private static readonly Color NeutralColor = new Color(0.6f, 0.6f, 0.6f);

        private const float CameraRetryIntervalSeconds = 1f;

        /// <summary>Gövde merkezinin kafa merkezinden aşağı ofseti (metre).</summary>
        private const float BodyDropMeters = 0.55f;

        /// <summary>
        /// Ölü avatarın renk çarpanı — YALNIZ eski kapsül yolundaki <see cref="teamRenderers"/>
        /// içindir. Karakter mesh'i karartılmaz, hayalete döner (<see cref="ApplyBodyVisual"/>).
        /// </summary>
        private const float DeadColorScale = 0.35f;

        /// <summary>Dost göstergesinin kafa merkezinin üstündeki yüksekliği (metre).</summary>
        private const float FriendMarkerHeightMeters = 0.32f;

        /// <summary>Ad etiketinin kafa merkezinin üstündeki yüksekliği (metre) — göstergenin üstünde.</summary>
        private const float NameLabelHeightMeters = 0.5f;

        /// <summary>
        /// Çift ellide (<c>FLAG_GRIP_LINKED</c>) boş elin kabzaya yapıştırılma yarıçapı (metre).
        /// <para>
        /// ⚠️ Bu bir <b>güzellik ayarı değil paket kaybı emniyetidir</b> (§6.6):
        /// <c>FLAG_GRIP_LINKED</c> UDP'de kaybolabilir ya da bayat kalabilir; oyuncu silahı
        /// gerçekten bıraktığı o ~50 ms'lik pencerede koşulsuz yapıştırma kolu arenanın öbür
        /// ucuna uzatırdı. Telde gelen el pozu bu yarıçaptan uzaktaysa GERÇEK poz kullanılır.
        /// </para>
        /// </summary>
        private const float SecondaryGripSnapRadius = 0.25f;

        /// <summary>
        /// Ölü avatarın elinde eşya çizilir mi. <b>Hayır</b> — telde karşılığı olmayan bir
        /// SUNUM kararıdır: ölen oyuncunun silahı elinde durursa avatar hâlâ tehditmiş gibi
        /// okunur (ölü avatarın vuruş kutuları da kapalıdır, yani yanlış okuma bedava değil).
        /// </summary>
        private const bool DrawItemsWhileDead = false;

        /// <summary>
        /// Eşya prefabında silah geometrisini taşıyan çocuğun adı (<c>WeaponKitBuilder</c> bunu
        /// kökte kurar; yerelde <c>Weapon.modelPivot</c> aynı düğüme bağlanır).
        /// </summary>
        private const string ModelPivotChildName = "Model";

        private const string DeadLabelSuffix = " (ölü)";
        private const string UncalibratedLabelSuffix = " (KALİBRESİZ)";

        /// <summary>Kalibresiz avatarın nabız hızı (saniyedeki tam gidiş-dönüş).</summary>
        private const float UncalibratedPulseHz = 1.6f;

        /// <summary>Nabzın takım rengiyle beyaz arasındaki en yüksek karışım oranı.</summary>
        private const float UncalibratedPulseAmount = 0.85f;

        /// <summary>
        /// Kalibresiz karakterin nabız rengi. Takım renklerinden (kırmızı/mavi) bilerek uzak bir
        /// turuncu: bu bir takım işareti DEĞİL, "bu avatarın konumu yalan" uyarısıdır.
        /// </summary>
        private static readonly Color UncalibratedTint = new Color(1f, 0.45f, 0.1f);

        /// <summary>
        /// Hayalet gövdenin taban alfası. Shader kenar parlamasını bunun ÜSTÜNE ekler, yani
        /// silüet bu değerden her zaman daha opaktır.
        /// </summary>
        private const float GhostBaseAlpha = 0.28f;

        /// <summary>Hayalet gövdenin dost rengi (yerel oyuncuyla AYNI takım).</summary>
        private static readonly Color GhostFriendColor = new Color(0.20f, 0.45f, 0.90f);

        /// <summary>
        /// Hayalet gövdenin düşman rengi. ⚠️ Takımsız modda (FFA) ve admin gözlemcide HERKES
        /// budur ve bu bilinçlidir: "dost değil" ile "takım yok" aynı cevabı verir, takımsız
        /// oyunda karşındaki herkes gerçekten düşmandır.
        /// </summary>
        private static readonly Color GhostEnemyColor = new Color(0.90f, 0.20f, 0.20f);

        /// <summary>Bu avatarın temsil ettiği uzak oyuncunun id'si.</summary>
        public int PlayerId { get; private set; }

        /// <summary>Son snapshot'taki canlılık bayrağı (kayıt yoksa true).</summary>
        public bool IsAlive { get; private set; } = true;

        /// <summary>Sunucuya göre bu oyuncunun hizalaması geçerli mi (§10.6; roster'dan gelir).</summary>
        public bool IsCalibrated { get; private set; } = true;

        // GC üretmemek için alan olarak tutulur (SetInfo her lobby_state'te çağrılabilir).
        private MaterialPropertyBlock _propertyBlock;

        // Camera.main her karede aranmaz — önbellek, null ise 1 sn'de bir yenilenir.
        private Camera _mainCamera;
        private float _cameraRetryTimer;

        private bool _visible = true;

        /// <summary>Bu uzak oyuncu YEREL oyuncuyla aynı takımda mı (dost göstergesini sürer).</summary>
        private bool _isFriendly;

        // Ad/numara/renk SetInfo'da saklanır; ölüm görünümü bunların üstüne uygulanır.
        private string _displayName = "";

        /// <summary>Forma numarası (§2); 0 = atanmamış → etikette basılmaz.</summary>
        private int _number;

        private Color _teamColor = NeutralColor;

        // ── Elde tutulan eşya (§6.6) ────────────────────────────────────────────────────
        // Katalog statik önbellekli ama aramayı kare başına yapmamak için burada tutulur.
        private NetItemCatalog _itemCatalog;

        // Çizilen örneklerin kabı: görünürlük (ölü/gizli avatar) tek kökten kapatılır, örnekler
        // YIKILMAZ — durum değişmedikçe yeniden Instantiate etmemek asıl amaç.
        private Transform _itemsRoot;

        // ⚠️ PASİF kuluçka kökü: yeni örnek ÖNCE buraya kurulur (kök aktif değil → prefabın
        // Awake'i HİÇ koşmaz), sterilize edildikten sonra _itemsRoot'a taşınır. Aktif bir köke
        // kurup sonra bileşen sökmek, Awake'in bir kez çalışmasını (ses, fizik, abonelik)
        // engellemezdi.
        private Transform _itemStagingRoot;

        // Çizilmekte olan durum — kare başına gelen durumla karşılaştırılır.
        private byte _shownItemL;
        private byte _shownItemR;
        private bool _shownGripLinked;

        // Etkin ana el (yalnız _shownGripLinked iken anlamlı). Telde gelenden SAPABİLİR: ana
        // işaretlenen slot boşsa diğer slot ana el sayılır (aşağıda gerekçesi).
        private bool _shownPrimaryRight;

        private ItemDefinition _itemDefL;
        private ItemDefinition _itemDefR;
        private Transform _itemInstanceL;
        private Transform _itemInstanceR;

        // HoldMode ↔ GRIP_LINKED çelişkisi durum başına BİR kez loglanır (20 Hz'de log seli olurdu).
        private bool _holdModeMismatchWarned;

        // ── Geri tepme (§6.4/6.5 atış olayından türetilir) ──────────────────────────────
        /// <summary>
        /// Bir elin geri tepme durumu. Telde geri tepme DİYE BİR ŞEY YOKTUR: yerelin eğrisi
        /// (<c>Weapon.Update</c>) burada, gelen atış olayından tetiklenerek yeniden üretilir —
        /// tek bayt bile eklenmeden.
        /// <para>
        /// Alanlar tek tek değil bir yapıda tutulur çünkü sol/sağ İKİ kopya gerekiyor; yapı
        /// <c>ref</c> ile geçirilir, yani kare başına kutulama/allocation olmaz.
        /// </para>
        /// </summary>
        private struct RecoilSlot
        {
            /// <summary>Örneğin <c>Model</c> çocuğu — YALNIZ örnek kurulurken aranır.</summary>
            public Transform Pivot;

            public Vector3 BasePosition;
            public Quaternion BaseRotation;

            public float Kick;
            public float KickBack;

            /// <summary>Son atışın tanımından gelen toparlanma hızı (derece/sn).</summary>
            public float RecoverSpeed;

            /// <summary>
            /// Bu kare transform yazılacak mı. Sıfıra dönen son kare de YAZILIR (pivot tam tabana
            /// otursun), sonrasında bayrak düşer — hareketsiz silahta boşuna transform trafiği yok.
            /// </summary>
            public bool Settling;
        }

        private RecoilSlot _recoilL;
        private RecoilSlot _recoilR;

        // "Örnekte Model çocuğu yok" uyarısı oyuncu başına BİR kez (olay yolu 53-160/sn, spam yasak).
        private bool _modelPivotWarned;

        /// <summary>Bağsız <see cref="character"/> uyarısı örnek başına bir kez (LateUpdate 72/sn).</summary>
        private bool _characterWarned;

        // ── Hayalet gövde ───────────────────────────────────────────────────────────────
        /// <summary>Ayrı hayalet gövdesinin renderer'ları; <see cref="ghostRoot"/> boşsa null.</summary>
        private Renderer[] _ghostRenderers;

        /// <summary>Hayaleti canlı iskeletten süren köprü; hayaletle birlikte açılıp kapanır.</summary>
        private GhostPoseDriver _ghostDriver;

        // Materyal takasının geri alınabilmesi için her bodyRenderer'ın ÖZGÜN dizisi ve aynı
        // UZUNLUKTA hayalet dizisi. ⚠️ Uzunluk birebir korunmalı: alt mesh sayısından fazla
        // materyal SON alt mesh'i bir kez daha çizer, eksik olan hiç çizilmez.
        private Material[][] _bodyOriginalMaterials;
        private Material[][] _bodyGhostMaterials;

        // Uygulanmış hayalet durumu — kare başına gereksiz materyal/renderer trafiği olmasın.
        private bool _ghostApplied;
        private bool _ghostStateKnown;

        /// <summary>Kurulumsuz hayalet uyarısı örnek başına bir kez.</summary>
        private bool _ghostSetupWarned;

        private void Awake()
        {
            // Prefabda liste bağlanmadıysa çocuk collider'ları vuruş kutusu sayılır.
            if (hitColliders == null || hitColliders.Length == 0)
            {
                hitColliders = GetComponentsInChildren<Collider>(true);
            }

            _itemCatalog = NetItemCatalog.Load();

            CacheGhostTargets();
        }

        /// <summary>
        /// Hayalet hedeflerini BİR kez toplar: ayrı hayalet gövdesi varsa onun renderer'ları,
        /// yoksa karakterin kendi mesh'inin materyal dizileri.
        /// <para>⚠️ <c>sharedMaterials</c> her çağrıda YENİ dizi döndürür — bu yüzden bir kez
        /// okunup saklanır; durum değişiminde okumak kare başına çöp üretmese de, takas edilen
        /// diziyi geri koyabilmek için özgün dizinin saklanması zaten şart.</para>
        /// </summary>
        private void CacheGhostTargets()
        {
            if (ghostRoot != null)
            {
                _ghostRenderers = ghostRoot.GetComponentsInChildren<Renderer>(true);
                SetRenderersEnabled(_ghostRenderers, false); // hayalet kapalı doğar

                // Görünmeyen hayaletin pozunu sürmek boşuna iş — sürücü hayaletle birlikte açılır.
                _ghostDriver = ghostRoot.GetComponentInChildren<GhostPoseDriver>(true);
                if (_ghostDriver != null)
                {
                    _ghostDriver.enabled = false;
                }
            }

            if (_ghostRenderers != null && _ghostRenderers.Length > 0)
            {
                return; // ayrı hayalet gövdesi var: karakterin materyallerine HİÇ dokunulmaz
            }

            if (bodyRenderers == null || bodyRenderers.Length == 0 || ghostMaterial == null)
            {
                return;
            }

            _bodyOriginalMaterials = new Material[bodyRenderers.Length][];
            _bodyGhostMaterials = new Material[bodyRenderers.Length][];

            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                Renderer target = bodyRenderers[i];
                if (target == null)
                {
                    continue;
                }

                Material[] original = target.sharedMaterials;
                _bodyOriginalMaterials[i] = original;

                var ghosts = new Material[original.Length];
                for (int m = 0; m < ghosts.Length; m++)
                {
                    ghosts[m] = ghostMaterial;
                }

                _bodyGhostMaterials[i] = ghosts;
            }
        }

        /// <summary>Spawner kurar; poz okumaları bu id ile yapılır.</summary>
        public void Initialize(int playerId)
        {
            if (PlayerId != playerId)
            {
                // Aynı örnek başka bir oyuncuya devrediliyor: eski oyuncunun eşyası kalmasın.
                ClearHeldItems();

                // ⚠️ Eski oyuncunun iskeleti ve kökü de kalmamalı — hepsi mandallı durum, kendi
                // kendine düzelmez ve devralan avatar bir kare önceki oyuncunun gövdesiyle çizilirdi.
                RemoteSkeletonRegistry.Instance?.Forget(PlayerId);
            }

            PlayerId = playerId;

            // Gövde ağdan gelen iskeletle sürülür: bu avatar hiçbir zaman input authority'ye sahip
            // olmaz (kendi gövdemizi LocalBodyAvatar çiziyor). Sensör kaynağı da burada kapanır.
            if (character != null)
            {
                character.Initialize(playerId, hasInputAuthority: false);
            }
        }

        /// <summary>Ad etiketini, forma numarasını ve takım rengini günceller ("red"/"blue"/diğer=gri).
        /// <para><paramref name="number"/> 0 ise numara BASILMAZ (atanmamış ya da admin): adlar
        /// benzersiz olmadığı için ayırt edici alan numaradır, uydurma bir sayı göstermek onu
        /// güvenilmez kılardı (§2).</para></summary>
        public void SetInfo(string displayName, int number, string team)
        {
            _displayName = displayName ?? "";
            _number = number;
            _teamColor = team == "red" ? TeamRedColor : team == "blue" ? TeamBlueColor : NeutralColor;

            ApplyLabelText();
            ApplyTeamColor();
        }

        /// <summary>
        /// Kalibrasyon durumunu uygular (§10.6; <c>lobby_state</c>'ten gelir). Kalibresiz avatar
        /// <b>parlar</b>, etiketine " (KALİBRESİZ)" eklenir ve <b>vuruş kutuları kapanır</b> —
        /// sunucu zaten hasarı reddediyor, istemcide de kapatmak atıcının "vurdum ama olmadı"
        /// hissini yaşamasını engeller.
        /// </summary>
        public void SetCalibrated(bool calibrated)
        {
            if (IsCalibrated == calibrated)
            {
                return;
            }

            IsCalibrated = calibrated;
            ApplyLabelText();
            ApplyTeamColor();
            ApplyBodyVisual();
            RefreshColliders();
        }

        /// <summary>
        /// Bu uzak oyuncu YEREL oyuncuyla aynı takımda mı — kafanın üstündeki dost göstergesi
        /// buna göre açılır.
        /// <para>
        /// Takım rengi karakter mesh'ine UYGULANMAZ: herkes aynı modeli kullanıyor ve ayrımı
        /// yapan tek şey bu göstergedir. Düşmanda hiçbir işaret olmaması bilinçlidir — düşmanı
        /// da işaretlemek arenada duvar arkasından okunabilen bir avantaj üretirdi.
        /// </para>
        /// </summary>
        public void SetFriendly(bool friendly)
        {
            if (_isFriendly == friendly)
            {
                return;
            }

            _isFriendly = friendly;
            RefreshFriendMarker();

            // Hayaletin rengi dost/düşman bilgisidir — takım değişimi onu da tazelemeli.
            ApplyBodyVisual();
        }

        /// <summary>Göstergeyi kafanın üstünde tutar — kafa KEMİĞİNE bağlanmaz, çünkü IK her
        /// karede kemiği yeniden yerleştiriyor ve gösterge onunla birlikte eğilmemeli.</summary>
        private void UpdateFriendMarker(in Pose headWorld)
        {
            if (friendMarker == null || !friendMarker.activeSelf)
            {
                return;
            }

            friendMarker.transform.position = headWorld.position + Vector3.up * FriendMarkerHeightMeters;
        }

        private void RefreshFriendMarker()
        {
            if (friendMarker != null)
            {
                friendMarker.SetActive(_visible && _isFriendly);
            }
        }

        /// <summary>Ad etiketi; ölüyken " (ölü)", kalibresizken " (KALİBRESİZ)" eki taşır.</summary>
        private void ApplyLabelText()
        {
            if (nameLabel != null)
            {
                string suffix = !IsCalibrated ? UncalibratedLabelSuffix : IsAlive ? "" : DeadLabelSuffix;
                string prefix = _number > 0 ? _number + " · " : "";
                nameLabel.text = prefix + _displayName + suffix;
            }
        }

        /// <summary>
        /// Takım rengini MaterialPropertyBlock ile yazar; ölüyken karartır, kalibresizken parlatır.
        /// <para>
        /// ⚠️ Parlama <c>_BaseColor</c> nabzıyla yapılır, emission ile DEĞİL:
        /// <see cref="MaterialPropertyBlock"/> shader keyword'ü açamaz, bu yüzden paylaşılan
        /// materyalde <c>_EMISSION</c> önceden açık olmadıkça <c>_EmissionColor</c> yazmak sessizce
        /// hiçbir şey yapmazdı. İkinci bir materyal örneği yaratmak da Quest'te SRP batch'ini bozar.
        /// </para>
        /// </summary>
        private void ApplyTeamColor()
        {
            if (teamRenderers == null)
            {
                return;
            }

            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            Color color;
            if (!IsCalibrated)
            {
                // Kalibresiz durum ölümü EZER: operatörün ve diğer oyuncuların görmesi gereken
                // şey "bu adamın hizalaması bozuk", ölü olup olmadığı değil.
                float pulse = Mathf.PingPong(Time.time * UncalibratedPulseHz, 1f) * UncalibratedPulseAmount;
                color = Color.Lerp(_teamColor, Color.white, pulse);
            }
            else if (IsAlive)
            {
                color = _teamColor;
            }
            else
            {
                color = new Color(_teamColor.r * DeadColorScale, _teamColor.g * DeadColorScale, _teamColor.b * DeadColorScale, _teamColor.a);
            }

            for (int i = 0; i < teamRenderers.Length; i++)
            {
                Renderer target = teamRenderers[i];
                if (target == null)
                {
                    continue;
                }

                target.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(BaseColorId, color);
                target.SetPropertyBlock(_propertyBlock);
            }
        }

        /// <summary>
        /// Gövde görünümü: canlı + kalibreli = <b>hiç dokunulmaz</b> (karakter olduğu gibi çizilir),
        /// ölü ya da kalibresiz = <b>hayalet</b>.
        /// <para>
        /// ⚠️ <b>Neden materyal takası, alfa DEĞİL:</b> karakterin materyali URP Lit ve OPAK
        /// (<c>_Surface: 0</c>, <c>_ZWrite: 1</c>) — opak malzemede <c>_BaseColor.a</c> yazmanın
        /// görsel karşılığı yoktur. Saydamlık ancak saydam bir shader'la gelir; renk çarpanıyla
        /// karartmak ise sahada "ölü mü canlı mı" sorusunu cevaplamıyordu.
        /// </para>
        /// <para>
        /// İki kurulum da geçerlidir ve kod tarafı ikisinde de aynıdır: <see cref="ghostRoot"/>
        /// bağlıysa karakterin mesh'i kapanıp AYRI hayalet gövdesi açılır, bağlı değilse
        /// karakterin kendi mesh'i hayalet materyaliyle çizilir. Yani hayalet modelini değiştirmek
        /// bir prefab işidir, kod işi değil.
        /// </para>
        /// <para>
        /// ⚠️ <b>Takım rengi hâlâ karaktere yazılmaz</b> — hayaletin rengi takım değil
        /// <b>dost/düşman</b> bilgisidir (<see cref="_isFriendly"/>) ve yalnız zaten tehdit
        /// olmayan (ölü/kalibresiz) bir gövdede görünür.
        /// </para>
        /// </summary>
        private void ApplyBodyVisual()
        {
            // ⚠️ Görünürlük de karara girer: hayalet ayrı bir alt ağaçtaysa visualRoot onu
            // kapatmayabilir (kardeş olabilir), o hâlde poz gelmeden havada asılı kalırdı.
            bool ghost = _visible && (!IsCalibrated || !IsAlive);

            if (_ghostStateKnown && ghost == _ghostApplied)
            {
                // Durum aynı: yalnız renk tazelenir — kalibresiz nabız her kare buraya uğrar.
                if (ghost)
                {
                    ApplyGhostColor();
                }

                return;
            }

            _ghostStateKnown = true;
            _ghostApplied = ghost;

            if (_ghostRenderers != null && _ghostRenderers.Length > 0)
            {
                SetRenderersEnabled(bodyRenderers, !ghost);
                SetRenderersEnabled(_ghostRenderers, ghost);

                if (_ghostDriver != null)
                {
                    _ghostDriver.enabled = ghost;
                }
            }
            else if (_bodyGhostMaterials != null)
            {
                ApplyBodyMaterials(ghost);
            }
            else if (ghost)
            {
                WarnMissingGhostSetup();
            }

            if (ghost)
            {
                ApplyGhostColor();
            }
            else
            {
                ClearGhostColor();
            }
        }

        /// <summary>
        /// Hayaletin rengi: dost mavi, düşman kırmızı; kalibresizken turuncuya nabız atar.
        /// <para>Kalibresizlik ölümü EZER — operatörün ve diğer oyuncuların görmesi gereken şey
        /// "bu adamın hizalaması bozuk", ölü olup olmadığı değil.</para>
        /// </summary>
        private void ApplyGhostColor()
        {
            Color color = _isFriendly ? GhostFriendColor : GhostEnemyColor;

            if (!IsCalibrated)
            {
                float pulse = Mathf.PingPong(Time.time * UncalibratedPulseHz, 1f) * UncalibratedPulseAmount;
                color = Color.Lerp(color, UncalibratedTint, pulse);
            }

            color.a = GhostBaseAlpha;
            WriteBaseColor(GhostTargets, color);
        }

        /// <summary>Hayalet rengin yazılacağı renderer'lar: ayrı gövde varsa o, yoksa karakterin
        /// kendi mesh'i (o hâlde üstünde zaten hayalet materyali duruyordur).</summary>
        private Renderer[] GhostTargets =>
            _ghostRenderers != null && _ghostRenderers.Length > 0 ? _ghostRenderers : bodyRenderers;

        /// <summary>
        /// Hayaletten çıkışta property block SÖKÜLÜR (boşaltılmaz): karakterin özgün materyali
        /// dokusuyla çizilsin ve renderer SRP Batcher'a geri girsin — property block'lu bir
        /// renderer batcher dışında kalır.
        /// </summary>
        private void ClearGhostColor()
        {
            ClearPropertyBlocks(_ghostRenderers);
            ClearPropertyBlocks(bodyRenderers);
        }

        /// <summary>Karakterin kendi mesh'ini hayalet materyaline çevirir ya da geri alır.
        /// Yalnız ayrı hayalet gövdesi YOKKEN çağrılır.</summary>
        private void ApplyBodyMaterials(bool ghost)
        {
            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                Renderer target = bodyRenderers[i];
                if (target == null)
                {
                    continue;
                }

                Material[] materials = ghost ? _bodyGhostMaterials[i] : _bodyOriginalMaterials[i];
                if (materials != null)
                {
                    target.sharedMaterials = materials;
                }
            }
        }

        /// <summary>
        /// Hayalet istendi ama uygulanacak hiçbir hedef yok — örnek başına bir kez HATA basar.
        /// <para>⚠️ Uyarı değil <b>hata</b>: bu durumda ölü oyuncu canlıdan ayırt edilemez, yani
        /// bileşen sessizce hiçbir şey yapmaz. Düzeltilmek istenen hatanın ta kendisi bu.</para>
        /// </summary>
        private void WarnMissingGhostSetup()
        {
            if (_ghostSetupWarned)
            {
                return;
            }

            _ghostSetupWarned = true;
            Debug.LogError(
                $"[RemoteAvatar] Oyuncu {PlayerId}: hayalet görünümü kurulmamış — RemoteAvatar " +
                "prefabında 'ghostMaterial' (M_AvatarGhost) bağlanmalı ya da 'ghostRoot' bir " +
                "hayalet gövdesi göstermeli. Ölü/kalibresiz oyuncu canlıdan ayırt edilemiyor.", this);
        }

        private void WriteBaseColor(Renderer[] targets, in Color color)
        {
            if (targets == null)
            {
                return;
            }

            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            for (int i = 0; i < targets.Length; i++)
            {
                Renderer target = targets[i];
                if (target == null)
                {
                    continue;
                }

                target.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(BaseColorId, color);
                target.SetPropertyBlock(_propertyBlock);
            }
        }

        private static void ClearPropertyBlocks(Renderer[] targets)
        {
            if (targets == null)
            {
                return;
            }

            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null)
                {
                    targets[i].SetPropertyBlock(null);
                }
            }
        }

        private static void SetRenderersEnabled(Renderer[] targets, bool enabled)
        {
            if (targets == null)
            {
                return;
            }

            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null)
                {
                    targets[i].enabled = enabled;
                }
            }
        }

        /// <summary>Ölü/görünmez/kalibresiz avatara ateş edilemez: vuruş kutuları kapatılır.</summary>
        private void RefreshColliders()
        {
            if (hitColliders == null)
            {
                return;
            }

            bool enable = _visible && IsAlive && IsCalibrated;
            for (int i = 0; i < hitColliders.Length; i++)
            {
                if (hitColliders[i] != null)
                {
                    hitColliders[i].enabled = enable;
                }
            }
        }

        private void LateUpdate()
        {
            RemotePlayerRegistry registry = RemotePlayerRegistry.Instance;
            if (registry == null ||
                !registry.GetInterpolatedPose(PlayerId, out Pose headPose, out Pose handLPose, out Pose handRPose))
            {
                SetVisible(false); // ilk poz gelene dek gizli
                return;
            }

            SetVisible(true);
            UpdateAlive(registry);

            // Nabız yalnız KALİBRESİZKEN sürülür — kalibreli avatarda her kare MPB yazmak
            // boşuna iş olurdu (renk değişmiyor, durum olay tabanlı tazeleniyor).
            if (!IsCalibrated)
            {
                ApplyTeamColor();
                ApplyBodyVisual();
            }

            // Pozlar arena uzayında — sahnedeki origin'e göre dünyaya çevir.
            Pose headWorld = ArenaSpace.ArenaToWorld(headPose);
            Pose handLWorld = ArenaSpace.ArenaToWorld(handLPose);
            Pose handRWorld = ArenaSpace.ArenaToWorld(handRPose);

            // §6.6: eşya durumu okunur (örnek kurulumu YALNIZ değişimde) ve eşyalar HAM el
            // pozundan sürülür — yapıştırma düzeltmesinden ÖNCE, çünkü eşyanın yeri ana elin
            // fiziksel pozundan gelir.
            UpdateHeldItems(registry);
            ApplyItemPoses(handLWorld, handRWorld);

            // Geri tepme, kavramadan SONRA sürülür ve onunla yarışmaz: ApplyGrip örneğin KÖK
            // dünya pozunu yazar, buradaki eğri ise örneğin 'Model' ÇOCUĞUNUN yerel TRS'ini.
            // Yerel silah da tam bu yüzden aynı pivotu kullanıyor (Weapon sınıf yorumu).
            TickRecoil(ref _recoilL);
            TickRecoil(ref _recoilR);

            // ⚠️ Poz kanalına DOKUNULMAZ (§6.2: ham poz fiziksel gerçektir) — yapıştırma yalnız
            // GÖSTERİME giden kopyayı değiştirir; hand*Pose/hand*World olduğu gibi kalır.
            Pose displayHandL = handLWorld;
            Pose displayHandR = handRWorld;
            ApplySecondaryGripSnap(ref displayHandL, ref displayHandR);

            // ⚠️ Karakter bağlıysa GÖVDEYE HİÇ DOKUNULMAZ: iskelet ağdan geliyor ve
            // ArenaNetCharacterBehaviour onu kendi kadansında uyguluyor (§6.10). Buradan kafa/el
            // transformlarına yazmak, retarget edilmiş kemiklerin üstüne ikinci bir sürücü koymak
            // olurdu. Bu döngünün gövdeyle ilgili tek işi kalmadı — eşya, etiket ve işaretçi.
            if (character == null)
            {
                // ⚠️ Bu yol bir "kapsül avatarı" DEĞİLDİR ve öyle sayılmamalıdır: prefabda
                // head/handL/handR/body alanları boş olabilir (öyleydiler) ve o hâlde aşağıdaki
                // dört çağrının hepsi sessiz no-op olur — gövde dünya orijininde T-pozunda donar,
                // sahada "oyuncu hiç görünmüyor" diye okunur. Bu yüzden önce AÇIKÇA bağırılır.
                WarnMissingCharacter();

                // Kök yine de doğru yere taşınır: yanlış yerde görünmeyen bir avatar yerine, yanlış
                // POZDA ama doğru YERDE duran bir avatar teşhis edilebilir bir hatadır.
                transform.SetPositionAndRotation(
                    headWorld.position - Vector3.up * BodyDropMeters,
                    Quaternion.identity);

                Apply(head, headWorld);
                Apply(handL, displayHandL);
                Apply(handR, displayHandR);
                ApplyBody(headWorld);
            }

            UpdateFriendMarker(headWorld);
            UpdateLabel(headWorld);
        }

        /// <summary>
        /// Gövde kapsülü kafanın altında durur ve yalnız YAW döner (öne eğilmez);
        /// böylece oyuncu eğildiğinde gövde yatmaz, vuruş kutusu makul kalır.
        /// </summary>
        private void ApplyBody(in Pose headWorldPose)
        {
            if (body == null)
            {
                return;
            }

            Vector3 forward = headWorldPose.rotation * Vector3.forward;
            forward.y = 0f;
            Quaternion yaw = forward.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(forward, Vector3.up)
                : body.rotation;

            body.SetPositionAndRotation(headWorldPose.position - Vector3.up * BodyDropMeters, yaw);
        }

        /// <summary>
        /// <see cref="character"/> bağlı değilse örnek başına bir kez HATA basar.
        /// <para>⚠️ Uyarı değil <b>hata</b>: bu durumda uzak oyuncunun gövdesini süren hiçbir şey
        /// yoktur ve eksiklik sahada "ağ bozuk" diye okunur — oysa tek eksik prefab bağıdır.
        /// Sessiz kalmak, teşhisi ağ katmanına yönlendirip saatler yakar.</para>
        /// </summary>
        private void WarnMissingCharacter()
        {
            if (_characterWarned)
            {
                return;
            }

            _characterWarned = true;
            Debug.LogError(
                $"[RemoteAvatar] Oyuncu {PlayerId}: 'character' alanı boş — uzak gövdeyi çizen " +
                "hiçbir bileşen yok (T-pozunda donar). RemoteAvatar.prefab'daki Character objesine " +
                "ArenaNetCharacterBehaviour + NetworkCharacterRetargeter kurulmalı.", this);
        }

        /// <summary>Snapshot alive bayrağını okur; değiştiyse görünüm + collider'ları tazeler.</summary>
        private void UpdateAlive(RemotePlayerRegistry registry)
        {
            bool alive = registry.IsAlive(PlayerId);
            if (alive == IsAlive)
            {
                return;
            }

            IsAlive = alive;
            ApplyLabelText();
            ApplyTeamColor();
            ApplyBodyVisual();
            RefreshColliders();
            RefreshHeldItemVisibility();
        }

        /// <summary>
        /// §6.6: uzak oyuncunun elindeki eşya durumunu okur ve örnekleri <b>yalnız durum
        /// değiştiğinde</b> kurar/yıkar. Durum insan hızında değişir; kare başına
        /// Instantiate/Destroy yapmak Quest'te bedava değildir (GC + sahne hiyerarşisi trafiği).
        /// </summary>
        private void UpdateHeldItems(RemotePlayerRegistry registry)
        {
            if (!registry.TryGetHeldItems(PlayerId, out byte itemL, out byte itemR, out bool gripLinked, out bool primaryRight))
            {
                itemL = 0;
                itemR = 0;
                gripLinked = false;
                primaryRight = false;
            }

            byte wantL = itemL;
            byte wantR = itemR;

            if (gripLinked)
            {
                // Bayat/kayıp bayrak toleransı: ana el işaretlenen slot BOŞ ama diğeri doluysa ana
                // el çevrilir — yoksa o tik'te tüfek hiç çizilmezdi (iki bayt ile bir bayrak farklı
                // paketlerden gelmiş olabilir).
                if (primaryRight && itemR == 0 && itemL != 0)
                {
                    primaryRight = false;
                }
                else if (!primaryRight && itemL == 0 && itemR != 0)
                {
                    primaryRight = true;
                }

                // ⚠️ GRIP_LINKED = TEK örnek, ana elin pozundan (§6.6). Aynı id iki slotta
                // gelse bile ikinci örnek KURULMAZ — ikinci örnek kurmak "aynı id iki slotta"yı
                // çift tabanca ile karıştırmak olurdu; ayrımı yalnız bu bayrak taşır.
                if (primaryRight)
                {
                    wantL = 0;
                }
                else
                {
                    wantR = 0;
                }
            }

            if (wantL == _shownItemL && wantR == _shownItemR &&
                gripLinked == _shownGripLinked && primaryRight == _shownPrimaryRight)
            {
                return; // hiçbir şey değişmedi: kare başına tek karşılaştırma
            }

            if (wantL != _shownItemL)
            {
                _shownItemL = wantL;
                _itemDefL = Resolve(wantL);
                RebuildItemInstance(ref _itemInstanceL, ref _recoilL, _itemDefL);
            }

            if (wantR != _shownItemR)
            {
                _shownItemR = wantR;
                _itemDefR = Resolve(wantR);
                RebuildItemInstance(ref _itemInstanceR, ref _recoilR, _itemDefR);
            }

            _shownGripLinked = gripLinked;
            _shownPrimaryRight = primaryRight;
            _holdModeMismatchWarned = false;
            WarnOnHoldModeMismatch();
        }

        /// <summary>
        /// Eşyanın kendi <c>HoldMode</c>'u ile telden gelen <c>GRIP_LINKED</c> çelişirse
        /// <b>telde geleni esas alırız</b> (durumun sahibi atıcı istemcidir, §6.2) — ama tek elli
        /// bir eşyanın çift elli bildirilmesi bir içerik/kod hatasının işareti olduğu için bir
        /// kez loglanır (aksi hâlde çelişki sahada sessizce yanlış duruş olarak görünürdü).
        /// </summary>
        private void WarnOnHoldModeMismatch()
        {
            if (!_shownGripLinked || _holdModeMismatchWarned)
            {
                return;
            }

            ItemDefinition primary = _shownPrimaryRight ? _itemDefR : _itemDefL;
            if (primary == null || primary.IsTwoHanded)
            {
                return;
            }

            _holdModeMismatchWarned = true;
            Debug.LogWarning(
                $"[RemoteAvatar] Oyuncu {PlayerId}: '{primary.DisplayName}' tek elli (HoldMode) ama " +
                "GRIP_LINKED ile geldi — telde gelen esas alındı, duruş yanlış görünebilir (§6.6).");
        }

        private ItemDefinition Resolve(byte netItemId)
        {
            return netItemId == 0 || _itemCatalog == null ? null : _itemCatalog.FindByNetItemId(netItemId);
        }

        /// <summary>
        /// Bir elin eşya örneğini yeniler: eski varsa yıkılır, yeni tanım varsa kurulur.
        /// Yalnız durum değişiminde çağrılır (allocation burada meşrudur).
        /// </summary>
        private void RebuildItemInstance(ref Transform instance, ref RecoilSlot recoil, ItemDefinition definition)
        {
            // Eski örneğin pivotu ve birikmiş kick'i sıfırlanır: bayat geri tepme yeni silaha
            // taşınırsa eldeki tüfek hiç ateş edilmeden sarsılmış görünürdü.
            recoil = default;

            if (instance != null)
            {
                // Destroy kare sonuna ertelenir; o kare hâlâ ÇİZİLİR. Elden bırakılan eşyanın bir
                // kare daha bayat pozda görünmemesi için önce kapatılır.
                instance.gameObject.SetActive(false);
                Destroy(instance.gameObject);
                instance = null;
            }

            if (definition == null || definition.Prefab == null)
            {
                return;
            }

            EnsureItemRoots();

            // Pasif kuluçka kökünde kurulur → prefabın hiçbir Awake'i çalışmaz.
            GameObject spawned = Instantiate(definition.Prefab, _itemStagingRoot);
            SterilizeVisual(spawned);

            // Sterilize edildikten sonra görünür kaba taşınır (kap ölü/gizli avatarda pasiftir).
            spawned.transform.SetParent(_itemsRoot, false);
            instance = spawned.transform;

            CacheRecoilPivot(ref recoil, instance);
        }

        /// <summary>
        /// Geri tepmenin uygulanacağı <c>Model</c> çocuğunu ve TABAN yerel TRS'ini saklar —
        /// yerelde <c>Weapon.Awake</c>'in yaptığının aynısı.
        /// <para>
        /// ⚠️ Arama YALNIZ burada, yani örnek kurulurken yapılır: atış olayı yolu 53-160/sn ve
        /// geri tepme her karede sürülüyor; kare/olay başına <c>Find</c> kabul edilemez.
        /// Bulunamazsa pivot sessizce null kalır (o silahta geri tepme görselleşmez, sunumun
        /// geri kalanı bozulmaz) ve oyuncu başına tek uyarı basılır.
        /// </para>
        /// </summary>
        private void CacheRecoilPivot(ref RecoilSlot recoil, Transform instance)
        {
            // Önce doğrudan çocuk (WeaponKitBuilder onu kökte kurar), yoksa derin arama.
            Transform pivot = instance.Find(ModelPivotChildName);
            if (pivot == null)
            {
                pivot = FindChildByName(instance, ModelPivotChildName);
            }

            if (pivot == null)
            {
                if (!_modelPivotWarned)
                {
                    _modelPivotWarned = true;
                    Debug.LogWarning(
                        $"[RemoteAvatar] Oyuncu {PlayerId}: '{instance.name}' örneğinde " +
                        $"'{ModelPivotChildName}' çocuğu yok — uzak silah geri tepmeyecek.");
                }

                return;
            }

            recoil.Pivot = pivot;
            recoil.BasePosition = pivot.localPosition;
            recoil.BaseRotation = pivot.localRotation;
        }

        // GetComponentsInChildren KULLANILMAZ (dizi ayırır): elle özyineleme allocation'sızdır ve
        // bu arama eşya değişiminde bir kez koşar. İLK eşleşme alınır — eşyanın tek gövdesi vardır.
        private static Transform FindChildByName(Transform parent, string childName)
        {
            int count = parent.childCount;
            for (int i = 0; i < count; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == childName)
                {
                    return child;
                }

                Transform found = FindChildByName(child, childName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// Kurulan örneği <b>salt görsele</b> indirger.
        /// <para>
        /// ⚠️ <b>Bu adım atlanamaz:</b> uzak kopyada çalışan bir silah kendi sesini çalar, fizik
        /// yapar, kavranabilir olur, hatta ateş edip <c>hit_report</c> üretir — sessizce çalışan
        /// bir uzak silah teşhis edilmesi en zor hatalardan biridir.
        /// </para>
        /// <para>
        /// Sökme sırası: MonoBehaviour'lar TERS sırada (bir bileşen <c>[RequireComponent]</c> ile
        /// bir başkasına dayanıyorsa — ör. <c>ShellEjector</c> → <c>Weapon</c> — bağımlı olan
        /// sonra eklenmiştir, önce o gider), ardından fizik ve ses. <c>DestroyImmediate</c>
        /// kullanılıyor çünkü <c>Destroy</c> kare sonuna ertelenir: aynı karede sökülen bir
        /// bağımlılık hâlâ "var" sayılır ve "can't remove component" hatası basılırdı. Örnek pasif
        /// kökte durduğu için hiçbir callback'in ortasında değiliz.
        /// </para>
        /// </summary>
        private static void SterilizeVisual(GameObject instance)
        {
            // Tüm oyun mantığı: Weapon, WeaponAudio, WeaponAnimator, WeaponReloadGesture,
            // ShellEjector, Meta'nın Grabbable/GrabInteractable/DistanceGrabInteractable/
            // OneGrab-TwoGrabFreeTransformer/MoveTowardsTargetProvider, MetaXRAudioSource…
            // Tek tek tipe göre değil TOPTAN sökülür: prefaba yeni bir bileşen eklendiğinde bu
            // liste güncellenmeyi bekleyemez (unutulan bileşen = sahada çalışan uzak silah).
            MonoBehaviour[] behaviours = instance.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = behaviours.Length - 1; i >= 0; i--)
            {
                if (behaviours[i] != null)
                {
                    DestroyImmediate(behaviours[i]);
                }
            }

            Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
            for (int i = colliders.Length - 1; i >= 0; i--)
            {
                if (colliders[i] != null)
                {
                    DestroyImmediate(colliders[i]);
                }
            }

            Rigidbody[] bodies = instance.GetComponentsInChildren<Rigidbody>(true);
            for (int i = bodies.Length - 1; i >= 0; i--)
            {
                if (bodies[i] != null)
                {
                    DestroyImmediate(bodies[i]);
                }
            }

            // AudioSource MonoBehaviour DEĞİL: playOnAwake ile kendi kendine ses çalabilir.
            AudioSource[] audioSources = instance.GetComponentsInChildren<AudioSource>(true);
            for (int i = audioSources.Length - 1; i >= 0; i--)
            {
                if (audioSources[i] != null)
                {
                    DestroyImmediate(audioSources[i]);
                }
            }

            // Parçacık sistemleri (namlu alevi/duman/kovan) BIRAKILIR — onları tetikleyen bileşen
            // gitti, kendiliğinden oynamasınlar diye yalnız playOnAwake kapatılır. Yıkılmıyorlar
            // çünkü uzak atış sunumu (RemoteShotFx) çizilen eşyanın namlusunu kullanır.
            ParticleSystem[] particles = instance.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i] != null)
                {
                    ParticleSystem.MainModule main = particles[i].main;
                    main.playOnAwake = false;
                }
            }
        }

        private void EnsureItemRoots()
        {
            if (_itemsRoot == null)
            {
                var root = new GameObject("HeldItems");
                root.transform.SetParent(transform, false);
                _itemsRoot = root.transform;
                RefreshHeldItemVisibility();
            }

            if (_itemStagingRoot == null)
            {
                var staging = new GameObject("HeldItemStaging");
                staging.transform.SetParent(transform, false);
                staging.SetActive(false); // pasif kalır: burada kurulan hiçbir şey Awake görmez
                _itemStagingRoot = staging.transform;
            }
        }

        /// <summary>
        /// Eşyaları ilgili elin pozundan sürer (duruş telde gitmez, §6.6 — kanonik kavrama her
        /// istemcinin APK'sında).
        /// <para>
        /// ⚠️ Telden gelen el pozu <b>kumanda anchor'ının</b> pozudur; kavrama ise AVUÇtan
        /// hesaplanır — dönüşüm <see cref="HandGripPivot"/> ile burada da yapılır. Yerel uçta
        /// yapılıp uzakta atlanırsa aynı silah iki ekranda birkaç santim kaymış çizilir.
        /// </para>
        /// <para>
        /// <c>GRIP_LINKED</c> iken TEK örnek vardır ve iki elli çözümle sürülür: ana el
        /// (<c>FLAG_PRIMARY_RIGHT</c>) eşyayı taşır, öteki elin avuç konumu eşyanın YÖNELİMİNİ
        /// çeker. Çözücü yerelin kullandığının <b>aynısıdır</b> (<see cref="ItemGripSolver"/>);
        /// <c>aimBlend</c> burada sabit <c>1</c>'dir — yumuşatma telin kendi interpolasyonundan
        /// geliyor, ikinci bir zaman sabiti uzak duruşu yerelden geciktirirdi.
        /// </para>
        /// <para>Eşya el KEMİĞİNİN çocuğu yapılmaz, dünya pozu yazılır: karakterli avatarda el
        /// kemiği IK'nın türettiği bir sonuçtur (kol hedefe yetişmeyebilir) ve eşyanın yeri telden
        /// gelen el pozudur — atışın bildirildiği poz da odur.</para>
        /// </summary>
        private void ApplyItemPoses(in Pose handLWorld, in Pose handRWorld)
        {
            Pose palmL = HandGripPivot.Resolve(handLWorld, false);
            Pose palmR = HandGripPivot.Resolve(handRWorld, true);

            if (_shownGripLinked)
            {
                Transform item = _shownPrimaryRight ? _itemInstanceR : _itemInstanceL;
                ItemDefinition definition = _shownPrimaryRight ? _itemDefR : _itemDefL;
                if (item == null || definition == null)
                {
                    return;
                }

                ApplyGrip(item, _shownPrimaryRight ? palmR : palmL, definition,
                    true, (_shownPrimaryRight ? palmL : palmR).position);
                return;
            }

            if (_itemInstanceL != null && _itemDefL != null)
            {
                ApplyGrip(_itemInstanceL, palmL, _itemDefL, false, Vector3.zero);
            }

            if (_itemInstanceR != null && _itemDefR != null)
            {
                ApplyGrip(_itemInstanceR, palmR, _itemDefR, false, Vector3.zero);
            }
        }

        /// <summary>Kavrama matematiğinin TEK uygulaması <see cref="ItemGripSolver"/>'dadır; burası
        /// yalnız sonucu transforma yazar (ikinci bir kavrama matematiği iki uçta iki ayrı duruş
        /// demek olurdu).</summary>
        private static void ApplyGrip(Transform item, in Pose palm, ItemDefinition definition,
            bool hasSecondary, in Vector3 secondaryPalmPosition)
        {
            ItemGripSolver.Solve(definition, palm, hasSecondary, secondaryPalmPosition, 1f,
                out Vector3 position, out Quaternion rotation);

            item.SetPositionAndRotation(position, rotation);
        }

        /// <summary>
        /// §6.6 "Çift ellide boş el": <c>GRIP_LINKED</c> iken ANA OLMAYAN elin gösterim pozu
        /// eşyanın <c>secondaryGrip</c> noktasına çekilir — ama yalnız gerçek poz o noktaya
        /// <see cref="SecondaryGripSnapRadius"/> kadar yakınsa (paket kaybı emniyeti).
        /// <para>
        /// ⚠️ <b>Rolü artık son rötuştur:</b> silah zaten ikinci ele bakıyor
        /// (<see cref="ApplyItemPoses"/> iki elli çözümü koşuyor), burası yalnız boş elin
        /// GÖRSELİNİ soketin tam üstüne oturtur. Yani duruşu bu metot BELİRLEMEZ; kaldırılırsa
        /// silah doğru durur ama boş el birkaç santim yanında yüzer.
        /// </para>
        /// <para>⚠️ <c>secondaryGrip</c>, <c>primaryGrip</c> ile <b>AYNI UZAYDA DEĞİLDİR</b>
        /// (<see cref="ItemDefinition"/>): <c>primaryGrip</c> "el → eşya", <c>secondaryGrip</c> ise
        /// ön kabza noktasının <b>eşyaya göre</b> yerel pozudur, yani "eşya → el". Bu yüzden ikinci
        /// elin hedefi ters bileşimle DEĞİL <b>düz ileri yönde</b> bulunur: eşyanın dünya pozu ana
        /// elden türetilmiştir, ön kabza da eşyanın üstünde sabit bir noktadır. İki alanın uzayı
        /// farklı olduğu için burada işaret/ters çevirme hatası sessizce yanlış duruş üretir.</para>
        /// </summary>
        private void ApplySecondaryGripSnap(ref Pose displayHandL, ref Pose displayHandR)
        {
            if (!_shownGripLinked)
            {
                return;
            }

            Transform item = _shownPrimaryRight ? _itemInstanceR : _itemInstanceL;
            ItemDefinition definition = _shownPrimaryRight ? _itemDefR : _itemDefL;
            if (item == null || definition == null)
            {
                return;
            }

            // TransformPoint yerine elle bileşim: eşya örneğinin ölçeği bugün 1 ama avatar kökünün
            // ölçeği 1 olmasa bile kavrama ofseti METREdir, ölçeklenmemesi gerekir.
            Quaternion itemRotation = item.rotation;
            Vector3 handPosition = item.position + itemRotation * definition.SecondaryGripPosition;
            Quaternion handRotation = itemRotation * definition.SecondaryGripRotation;

            if (_shownPrimaryRight)
            {
                if (IsWithinSnapRadius(displayHandL.position, handPosition))
                {
                    displayHandL = new Pose(handPosition, handRotation);
                }
            }
            else if (IsWithinSnapRadius(displayHandR.position, handPosition))
            {
                displayHandR = new Pose(handPosition, handRotation);
            }
        }

        private static bool IsWithinSnapRadius(in Vector3 actual, in Vector3 target)
        {
            return (actual - target).sqrMagnitude <= SecondaryGripSnapRadius * SecondaryGripSnapRadius;
        }

        /// <summary>§6.6: o elde ÇİZİLMİŞ eşya örneğinin kök transformu; o elde eşya yoksa null.
        /// <para>Uzak atış sunumu (RemoteShotFx) namluyu bununla bulur — ada/mesafeye dayalı sahne
        /// aramasıyla değil. GRIP_LINKED (çift elli) durumda TEK örnek vardır ve hangi el sorulursa
        /// sorulsun O örnek döner: silahı iki el birden tutuyordur.</para></summary>
        public Transform GetHeldItemVisual(bool rightHand)
        {
            // ⚠️ Gizli/ölü avatarda da referans DÖNER: görünürlük çağıranın kararıdır. Burada null
            // döndürmek "namlu yok" sanılıp gereksiz el-pozu fallback'ine düşürürdü.
            return ResolveSlotIsRight(rightHand) ? _itemInstanceR : _itemInstanceL;
        }

        /// <summary>
        /// Bir olayın "hangi el" bilgisini ÇİZİLEN slota çevirir: <c>GRIP_LINKED</c> iken tek örnek
        /// vardır ve o ANA elin slotunda durur, aksi hâlde elin kendi slotu.
        /// <para>Namlu (<see cref="GetHeldItemVisual"/>) ile geri tepme (<see cref="ApplyShotRecoil"/>)
        /// bu tek yardımcıyı paylaşır: ikisi ayrı yazılsaydı çift elli tutuşta biri diğerinden
        /// farklı örneği seçebilir ve alev bir silahtan, sarsıntı ötekinden çıkardı.</para>
        /// </summary>
        private bool ResolveSlotIsRight(bool rightHand)
        {
            return _shownGripLinked ? _shownPrimaryRight : rightHand;
        }

        /// <summary>
        /// §6.4/6.5: gelen atış olayında bu avatarın silahını geri tepmeye sokar — yerelin
        /// (<c>Weapon</c>) eğrisinin BİREBİR aynısı, telde tek bayt yer kaplamadan.
        /// <para>
        /// ⚠️ Sürücü neden burada, ayrı bir bileşende değil: uzak örnek
        /// <see cref="SterilizeVisual"/>'dan geçiyor ve orada TÜM MonoBehaviour'lar toptan
        /// sökülüyor (bilinçli) — örneğe eklenen her bileşen bir sonraki kurulumda yok olurdu.
        /// </para>
        /// <para>
        /// İki elle tutuşta çarpan <see cref="Weapon.DefaultTwoHandRecoilMultiplier"/>'dır:
        /// prefabdaki alan telde gitmez (o const'un yorumuna bak), ama tutuşun çift elli olduğu
        /// bilgisi <c>FLAG_GRIP_LINKED</c> ile zaten geliyor.
        /// </para>
        /// </summary>
        public void ApplyShotRecoil(bool rightHand, WeaponDefinition definition)
        {
            if (definition == null)
            {
                return; // silah olmayan eşya (bomba vb.) ya da katalogda çözülemeyen id
            }

            if (ResolveSlotIsRight(rightHand))
            {
                AddKick(ref _recoilR, definition);
            }
            else
            {
                AddKick(ref _recoilL, definition);
            }
        }

        /// <summary>Yerel <c>Weapon.Fire</c>'daki birikme + tavan kuralının aynısı.</summary>
        private void AddKick(ref RecoilSlot recoil, WeaponDefinition definition)
        {
            if (recoil.Pivot == null)
            {
                return; // o elde eşya çizilmemiş ya da prefabda Model çocuğu yok
            }

            float scale = _shownGripLinked ? Weapon.DefaultTwoHandRecoilMultiplier : 1f;

            recoil.Kick = Mathf.Min(recoil.Kick + definition.KickDegrees * scale, definition.KickDegrees * 4f);
            recoil.KickBack = Mathf.Min(recoil.KickBack + definition.KickBackMeters * scale, definition.KickBackMeters * 3f);
            recoil.RecoverSpeed = definition.RecoilRecoverSpeed;
            recoil.Settling = true;
        }

        /// <summary>
        /// Geri tepmeyi söndürür ve pivota uygular — yerel <c>Weapon.Update</c>'in aynısı
        /// (geri dönüş hızı ötelemede 0.02 katsayısıyla yavaşlatılır).
        /// <para>Hareketsiz silahta hiçbir şey yazılmaz: bayrak, sıfıra dönen SON kareyi de
        /// kapsadığı için pivot tam tabana oturur, sonrasında döngü durur.</para>
        /// </summary>
        private static void TickRecoil(ref RecoilSlot recoil)
        {
            if (!recoil.Settling || recoil.Pivot == null)
            {
                return;
            }

            recoil.Kick = Mathf.MoveTowards(recoil.Kick, 0f, recoil.RecoverSpeed * Time.deltaTime);
            recoil.KickBack = Mathf.MoveTowards(recoil.KickBack, 0f, recoil.RecoverSpeed * 0.02f * Time.deltaTime);

            Quaternion rotation = recoil.BaseRotation * Quaternion.Euler(-recoil.Kick, 0f, 0f);
            recoil.Pivot.localRotation = rotation;
            recoil.Pivot.localPosition = recoil.BasePosition + rotation * (Vector3.back * recoil.KickBack);

            recoil.Settling = recoil.Kick > 0f || recoil.KickBack > 0f;
        }

        /// <summary>
        /// Eşyalar gizli/ölü avatarda görünmez olur — örnekler YIKILMAZ, yalnız kap kapanır
        /// (durum değişmediği hâlde canlanmada yeniden Instantiate etmemek için).
        /// </summary>
        private void RefreshHeldItemVisibility()
        {
            if (_itemsRoot != null)
            {
                _itemsRoot.gameObject.SetActive(_visible && (IsAlive || DrawItemsWhileDead));
            }
        }

        /// <summary>Örnekleri ve durumu sıfırlar (avatar başka bir oyuncuya devredilirse).</summary>
        private void ClearHeldItems()
        {
            if (_itemInstanceL != null)
            {
                Destroy(_itemInstanceL.gameObject);
                _itemInstanceL = null;
            }

            if (_itemInstanceR != null)
            {
                Destroy(_itemInstanceR.gameObject);
                _itemInstanceR = null;
            }

            // Örnekler gitti: pivot referansları da gitmeli (yıkılmış transform'a yazılmaz).
            _recoilL = default;
            _recoilR = default;

            _itemDefL = null;
            _itemDefR = null;
            _shownItemL = 0;
            _shownItemR = 0;
            _shownGripLinked = false;
            _shownPrimaryRight = false;
            _holdModeMismatchWarned = false;

            // Avatar başka bir oyuncuya devredildi: uyarı kotası da yeni oyuncu için sıfırlanır.
            _modelPivotWarned = false;
        }

        private static void Apply(Transform target, in Pose worldPose)
        {
            if (target != null)
            {
                target.SetPositionAndRotation(worldPose.position, worldPose.rotation);
            }
        }

        /// <summary>
        /// Ad etiketini kafanın üstünde tutar ve kameraya döndürür (etiket ters bakmasın diye
        /// etiket→kamera tersi).
        /// <para>Konum her karede YAZILIR: karakterli avatarda etiket bir kafa objesinin çocuğu
        /// değildir (kafa artık IK'nın sürdüğü bir kemiktir ve etiket onunla eğilmemeli).</para>
        /// </summary>
        private void UpdateLabel(in Pose headWorld)
        {
            if (nameLabel == null)
            {
                return;
            }

            nameLabel.transform.position = headWorld.position + Vector3.up * NameLabelHeightMeters;

            if (_mainCamera == null)
            {
                _cameraRetryTimer -= Time.deltaTime;
                if (_cameraRetryTimer > 0f)
                {
                    return;
                }

                _cameraRetryTimer = CameraRetryIntervalSeconds;
                _mainCamera = Camera.main;
                if (_mainCamera == null)
                {
                    return;
                }
            }

            Transform label = nameLabel.transform;
            Vector3 direction = label.position - _mainCamera.transform.position;
            if (direction.sqrMagnitude < 1e-6f)
            {
                return;
            }

            label.rotation = Quaternion.LookRotation(direction);
        }

        private void SetVisible(bool visible)
        {
            if (_visible == visible)
            {
                return;
            }

            _visible = visible;

            if (visualRoot != null)
            {
                // Karakterli avatar: tek kök kapatılır (mesh listesi tutmak gerekmez).
                visualRoot.SetActive(visible);
            }
            else if (teamRenderers != null)
            {
                for (int i = 0; i < teamRenderers.Length; i++)
                {
                    if (teamRenderers[i] != null)
                    {
                        teamRenderers[i].enabled = visible;
                    }
                }
            }

            if (nameLabel != null)
            {
                nameLabel.enabled = visible;
            }

            RefreshFriendMarker();
            RefreshColliders();
            RefreshHeldItemVisibility();

            // Hayalet ayrı bir alt ağaç olabilir (visualRoot onu kapatmaz) — görünürlük kararı
            // oraya da taşınmalı, yoksa poz gelmeden hayalet havada asılı kalır.
            ApplyBodyVisual();
        }
    }
}
