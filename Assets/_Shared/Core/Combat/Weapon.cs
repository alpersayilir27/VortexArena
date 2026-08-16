using System;
using System.Collections;
using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.InputSystem;
using VortexArena.Core.Player;
using Random = UnityEngine.Random;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Tutulabilir hitscan VR silahı: dünyada durur (ISDK Grabbable + GrabInteractable),
    /// yalnız tutulurken ateş eder. Tetik, silahı FİİLEN tutan ANA elin kontrolcüsünden
    /// okunur (Grabbable pointer olayından el çözülür; çözülemezse Input System
    /// "Player/Attack" editör fallback'i). İki elle tutuş saçılım ve geri tepmeyi
    /// çarpanla düşürür; geri tepme <see cref="ModelPivot"/>'a uygulanır (kök transformu
    /// kanonik kavrama sürdüğü için onunla yarışmaz).
    /// <para>
    /// <b>KAVRAMA KANONİKTİR</b> (§6.6): silah tutulduğu sürece kökü ana elin anchor'ından +
    /// tanımdaki <b>stüdyoda yazılmış</b> kavramadan sürülür — konum
    /// <see cref="ItemDefinition.PrimaryGripPosition(bool)"/>, <b>dönüş de kayıttan</b>
    /// (<see cref="ItemDefinition.PrimaryGripRotation(bool)"/>: el nasıl tutuyorsa silah öyle
    /// durur) (<see cref="LateUpdate"/>). ISDK'nın <c>*GrabFreeTransformer</c>'ları bu yüzden
    /// <c>WPN_*</c> prefablarından kaldırıldı: kavramanın ALGILANMASI Grabbable/GrabInteractable'da,
    /// silahı TAŞIMA işi burada. Gerekçe ağdadır — duruş telde gitmez, uzak taraf silahı "elin pozu ×
    /// sabit kavrama ofseti" olarak çizer; serbest kavrama (keyfi ofset) o eşitliği bozar ve namlusundan
    /// ters tutulan bir AK karşı tarafta düzgün tutuluyor görünürdü.
    /// </para>
    /// <para>
    /// Tüm denge/his/ses değerleri <see cref="WeaponDefinition"/>'dan gelir (ZORUNLU);
    /// hasar istemci-otoriterdir: headshot çarpanı dahil burada hesaplanır ve
    /// <see cref="ArenaCombat"/> üzerinden bildirilir (protokol §10.3). Ağa hiçbir mesaj
    /// doğrudan yazılmaz — tek kapı ArenaCombat'tır.
    /// </para>
    /// <para>
    /// Şarjör bitince otomatik reload YOKTUR — dolum <see cref="TryStartReload"/> ile
    /// bilinçli başlatılır (ör. <see cref="WeaponReloadGesture"/>'ın bel-altı jesti). Rezerv
    /// muhasebesi <see cref="WeaponReserveMode"/>'a göre yürür; reload silah bırakılsa
    /// da tamamlanır. Şarjör seslerini bu sınıf ÇALMAZ (WeaponAnimator zaman çizgisi).
    /// </para>
    /// <para>
    /// İKİNCİ TUTUŞ YOLU — <b>verilen silah</b> (<see cref="GrantTo"/>): silah ISDK kavraması hiç
    /// işletilmeden, doğrudan bir kumandaya bildirilerek tutulur. İki türü vardır ve farkları
    /// <see cref="WeaponGrantKind"/>'dadır:
    /// <list type="bullet">
    /// <item><b>Disposable</b> (§10.5 <c>weaponSource:"random"</c>): tanım gereği tutuluyordur ve
    /// reload KAPALIDIR.</item>
    /// <item><b>Persistent</b> (çerçeveden seçilen silah, <see cref="WeaponFrame"/>): reload açıktır
    /// ve rezervi vardır.</item>
    /// </list>
    /// İki türde de örnek <see cref="WeaponGranter"/>'ın DDOL kökünün altında park eder (el
    /// anchor'ının ÇOCUĞU DEĞİLDİR) ve pozu her karede kanonik kavramayla sürülür; ikinci el ön
    /// kabzayı tutabilir (<see cref="SecondaryHand"/>).
    /// </para>
    /// <para>
    /// <b>ÖN KABZA kapısı ve göstergesi de bu sınıftadır</b> — ayrı bir bileşen YOKTUR:
    /// <see cref="IsHandOnSecondaryGrip"/> "bu elin avucu ön kabzanın kabul yarıçapında mı" sorusunun
    /// tek cevabıdır (granter ikinci eli buna göre bağlar), <see cref="TickSecondaryGripIndicator"/>
    /// aynı ölçüyle boş elin yaklaştığı ön kabzaya katalogdaki gösterge prefabını koyar.
    /// </para>
    /// </summary>
    public class Weapon : MonoBehaviour
    {
        // Analog tetik histerezisi: eşik çevresindeki titreme tek basışı çift saymasın.
        private const float TriggerPressThreshold = 0.55f;
        private const float TriggerReleaseThreshold = 0.35f;

        /// <summary>Tıkalı namlu geri bildiriminin en kısa tekrar aralığı (sn).</summary>
        private const float BlockedCueSeconds = 0.4f;

        /// <summary>Tıkalı namlu darbesinin şiddeti — atış darbesinden hafif, "olmadı" demek için.</summary>
        private const float BlockedHapticAmplitude = 0.6f;

        /// <summary>Tıkalı namlu darbesinin süresi (sn).</summary>
        private const float BlockedHapticSeconds = 0.06f;

        /// <summary>
        /// İki elle tutuşta geri tepme çarpanının VARSAYILANI.
        /// <para>
        /// Uzak replika (<see cref="VortexArena.Core.Player.RemoteAvatar.ApplyShotRecoil"/>) bu
        /// <b>varsayılanı</b> okur, prefabdaki alanı değil: <see cref="twoHandRecoilMultiplier"/>
        /// prefaba serialize edilir ve telde GİTMEZ, uzak taraf onu hiçbir yoldan öğrenemez.
        /// Bir prefabda alan elle değiştirilirse o fark yalnız atanın kendi ekranında görünür —
        /// karşı taraf geri tepmeyi bu sabitle çizmeye devam eder.
        /// </para>
        /// </summary>
        public const float DefaultTwoHandRecoilMultiplier = 0.35f;

        [Header("Tanım")]
        [Tooltip("Silah tanımı SO'su (_Shared/Arsenal/Data) — ZORUNLU: tüm istatistik/ses buradan okunur.")]
        [SerializeField] private WeaponDefinition definition;

        [Header("Referanslar")]
        [SerializeField] private Transform muzzle;
        [Tooltip("Silah geometrisini taşıyan çocuk; geri tepme buraya uygulanır.")]
        [SerializeField] private Transform modelPivot;
        [SerializeField] private Grabbable grabbable;
        [SerializeField] private ParticleSystem muzzleFlash;
        [SerializeField] private WeaponAudio weaponAudio;
        [SerializeField] private GameObject hitEffectPrefab;
        [Tooltip("YALNIZ editör fallback'i (kontrolcü çözülemezse 'Player/Attack'). Boşsa proje geneli aksiyonlar.")]
        [SerializeField] private InputActionAsset inputActions;

        [Header("İki El Dengeleme")]
        [Tooltip("İki elle tutarken saçılım çarpanı.")]
        [SerializeField] private float twoHandSpreadMultiplier = 0.45f;
        [Tooltip("İki elle tutarken geri tepme çarpanı.")]
        [SerializeField] private float twoHandRecoilMultiplier = DefaultTwoHandRecoilMultiplier;

        // ⚠️ Haptik alanı burada YOKTUR ve geri eklenmez: darbenin şiddeti/süresi silahın kendi
        // verisidir (WeaponDefinition.HapticAmplitude/HapticDuration). Prefaba serialize edilen
        // ikinci bir kopya, aynı silahın sahne örneği ile verilen klonunu farklı hissettirirdi.

        /// <summary>Arayüz adı: tanımın DisplayName'i, yoksa obje adı.</summary>
        public string WeaponName => definition != null && !string.IsNullOrEmpty(definition.DisplayName)
            ? definition.DisplayName
            : gameObject.name;

        /// <summary>Protokol silah anahtarı — yalnız kill feed etiketi, sunucu doğrulamaz (§10.3).</summary>
        public string WeaponId => definition != null ? definition.WeaponId : "";

        /// <summary>
        /// Telde giden eşya kimliği (§6.6); <c>0</c> = tanım yok ya da <c>netItemId</c> atanmamış.
        /// ⚠️ <see cref="WeaponId"/> ile karıştırma: o bir kill feed <i>etiketi</i> (string, serbest),
        /// bu bir <i>ağ kimliği</i> (u8) ve uzak tarafın hangi eşyayı çizeceğini o belirler.
        /// </summary>
        public byte NetItemId => definition != null && definition.HasNetItemId ? definition.NetItemId : (byte)0;

        /// <summary>Silah tanımı (Awake'te doğrulanır; null ise silah kilitlidir).</summary>
        public WeaponDefinition Definition => definition;

        /// <summary>Geri tepmenin uygulandığı görsel pivot (WeaponAnimator da bunu kullanır).</summary>
        public Transform ModelPivot => modelPivot;

        /// <summary>
        /// <c>weaponSource:"random"</c> modlarında (§10.5) silah doğrudan kumandaya VERİLİR:
        /// ISDK kavraması hiç işletilmez. <c>None</c> = normal sahne silahı (kavranarak tutulur).
        /// <para>
        /// Neden ayrı bir yol: <see cref="Grabbable"/>'ı programla "seçili" hâle getirmek ISDK'nın
        /// iç durumuna girmek demektir — kırılgan ve sürüm bağımlı. Verilen silah zaten tanım
        /// gereği tutuluyor, kavrama sistemine hiç sokulmaz.
        /// </para>
        /// </summary>
        public OVRInput.Controller GrantedHand { get; private set; } = OVRInput.Controller.None;

        /// <summary>Verilme TÜRÜ (bkz. <see cref="WeaponGrantKind"/>) — "elde sabit duruyor" ile
        /// "reload kapalı, rezervsiz" kuralları bu tiple ayrıldı; ikisi artık aynı bayrağa bağlı
        /// değil.</summary>
        public WeaponGrantKind GrantKind { get; private set; } = WeaponGrantKind.None;

        /// <summary>Silah verilerek mi tutuluyor (sahne silahında her zaman false).</summary>
        public bool IsGranted => GrantedHand != OVRInput.Controller.None;

        /// <summary>Çerçeveden seçilen KALICI klon mu (reload açık, rezerv var, ön kabza tutulabilir).</summary>
        public bool IsPersistentGrant => GrantKind == WeaponGrantKind.Persistent;

        /// <summary>FFA'nın TEK KULLANIMLIK rastgele silahı mı (reload kapalı, rezerv yok).</summary>
        public bool IsDisposableGrant => GrantKind == WeaponGrantKind.Disposable;

        /// <summary>Tutuluyor mu: verilen silah TANIM GEREĞİ tutuluyordur, sahne silahı ISDK'nın
        /// pointer olaylarından izlenir. Bu <c>||</c> olmadan verilen silah hiç ateş edemezdi.</summary>
        public bool IsHeld => IsGranted || heldPoints.Count > 0;

        /// <summary>
        /// İki elle sabitleme: sahne silahında İKİ kavrama noktası, VERİLEN silahta ise ana el +
        /// granter'ın yazdığı ikinci el (<see cref="SetSecondaryHand"/>).
        /// <para>⚠️ <b>Disposable silah artık iki elli OLABİLİR</b> (bilinçli değişiklik): FFA'da
        /// eline tüfek verilen oyuncunun ön kabzayı tutamaması, aynı tüfeği çerçeveden alan
        /// oyuncudan farklı bir his üretiyordu. Kural artık tek yerde: ikinci eli granter çözer,
        /// kaynak (verildi mi / kavrandı mı) fark etmez.</para>
        /// <para>⚠️ Kapı <see cref="SecondaryHand"/> DEĞİL bu alandır: kontrolcü çözülemeyen
        /// oturumlarda (editör) <c>SecondaryHand</c> <c>None</c> döner, oysa iki kavrama noktası
        /// varlığını sürdürür — o durumda telde çift el bildirmek doğrudur (yalnız iki elli POZ
        /// çözülemez, ona <c>SecondaryHand</c> bakar).</para>
        /// </summary>
        public bool IsTwoHanded => HasSecondaryGrip;

        /// <summary>İkinci bir kavrama noktası VAR MI (elin çözülüp çözülmediğinden bağımsız).</summary>
        private bool HasSecondaryGrip => IsGranted
            ? _grantedSecondaryHand != OVRInput.Controller.None ||
              (IsPersistentGrant && heldPoints.Count > 0)
            : heldPoints.Count > 1;

        /// <summary>
        /// Ön kabzayı tutan ikinci elin kontrolcüsü; <c>None</c> = ikinci el yok ya da çözülemedi.
        /// <para>
        /// Kaynak silahın türüne göre ayrılır: <b>verilen</b> silahta (Disposable ve Persistent)
        /// granter yazar — kavrama algısının tek kapısı odur; <b>sahne</b> silahında ikinci ISDK
        /// kavrama noktasıdır. Persistent klonun ISDK yolu ikinci kaynak olarak duruyor ama bugün
        /// fiilen boştur (<c>WeaponGranter.PrepareSummonedClone</c> kavrama bileşenlerini kapatıyor).
        /// </para>
        /// <para>⚠️ Ana elle AYNI çıkarsa <c>None</c> döner: silahı tek elin iki kaynaktan tuttuğu
        /// bir durumda iki elli çözüm sıfır uzunlukta bir eksene nişan alırdı.</para>
        /// </summary>
        public OVRInput.Controller SecondaryHand
        {
            get
            {
                OVRInput.Controller hand;

                if (IsGranted)
                {
                    hand = _grantedSecondaryHand;
                    if (hand == OVRInput.Controller.None && IsPersistentGrant && heldPoints.Count > 0)
                    {
                        hand = heldPoints[0].ctl;
                    }
                }
                else
                {
                    hand = heldPoints.Count > 1 ? heldPoints[1].ctl : OVRInput.Controller.None;
                }

                return hand == MainHand ? OVRInput.Controller.None : hand;
            }
        }

        /// <summary>
        /// Tetik/ana el: VERİLEN silahta silahın verildiği el, sahne silahında İLK kavrayan el.
        /// <c>None</c> = tutulmuyor ya da kontrolcü çözülemedi (editör fallback'i).
        /// </summary>
        public OVRInput.Controller MainHand =>
            IsGranted ? GrantedHand
                      : (heldPoints.Count > 0 ? heldPoints[0].ctl : OVRInput.Controller.None);

        /// <summary>
        /// Ana el sağ mı — §6.4 olay bayrağının ve §6.6 <c>FLAG_PRIMARY_RIGHT</c>'ının kaynağı.
        /// <para>⚠️ Çözülemeyen el SAĞ sayılır: telde "bilinmeyen el" diye bir değer yok, tek bir
        /// bit var. Yanlış ele düşen bir tracer, hiç çizilmeyen bir tracer'dan iyidir.</para>
        /// </summary>
        public bool IsMainHandRight => MainHand != OVRInput.Controller.LTouch;

        public int CurrentAmmo { get; private set; }
        public int MagazineSize => definition != null ? definition.MagazineSize : 0;

        /// <summary>Rezervdeki toplam mermi (her iki rezerv modunda da tek sayaç).</summary>
        public int ReserveRounds => reserveRounds;

        /// <summary>Rezervin tam şarjör karşılığı (HUD şarjör ikonları için).</summary>
        public int SpareMagazineCount => reserveRounds / Mathf.Max(1, MagazineSize);

        public bool IsReloading { get; private set; }

        /// <summary>
        /// Silahın <b>herhangi bir parçası</b> şu an bir iç engele değiyor mu (§10.9) — böyleyken
        /// tetik hiç işlemez. Yalnız tetiğe basılıyken ölçülür, boştaki silahta <c>false</c> kalır.
        /// </summary>
        public bool IsWeaponBlocked { get; private set; }

        /// <summary>Anlık saçılım yarı açısı (taban + bloom, derece). Çift el çarpanı DAHİL DEĞİL — ham değer.</summary>
        public float CurrentSpreadDegrees => definition != null ? definition.BaseSpreadDegrees + currentBloom : 0f;

        /// <summary>Her atışta (mermi çıktıktan sonra).</summary>
        public event Action Fired;

        /// <summary>Boş şarjörle tetik çekildiğinde.</summary>
        public event Action DryFired;

        /// <summary>Reload başladı; parametre toplam süre (saniye).</summary>
        public event Action<float> ReloadStarted;

        /// <summary>Reload bitti (<see cref="RefillFull"/> ile iptal edildiğinde de yayınlanır).</summary>
        public event Action ReloadCompleted;

        /// <summary>Şarjör/rezerv sayısı değişti.</summary>
        public event Action AmmoChanged;

        /// <summary>Tutuluyor durumu 0↔&gt;0 geçişi yaptı.</summary>
        public event Action<bool> HeldChanged;

        /// <summary>
        /// Sahnedeki etkin silahlar — silah objesine referansı olmayan dinleyiciler
        /// (ör. AmmoHud) için. OnEnable/OnDisable'da güncellenir; sıranın anlamı yok.
        /// </summary>
        public static readonly List<Weapon> Active = new List<Weapon>();

        /// <summary>Active listesi değişti VEYA bir silahın tutulma durumu değişti.</summary>
        public static event Action ActiveChanged;

        // HeldItems toplayıcısının durumu (§6.6): abonelik bir kez kurulur, uyarılar tek seferlik
        // (durum değişimi insan hızında olsa da hatalı bir kurulum her kavramada tekrarlanırdı).
        private static bool heldItemsHooked;
        private static bool missingNetItemIdWarned;
        private static bool handConflictWarned;

        // Tutan eller SIRALI tutulur: İLK eleman tetik/ana eldir. id = PointerEvent.Identifier
        // (Unselect/Cancel'da eşleştirme anahtarı), ctl = ele çözülen OVR kontrolcüsü
        // (None = çözülemedi → tetik Input System fallback'inden okunur).
        private readonly List<(int id, OVRInput.Controller ctl)> heldPoints = new List<(int, OVRInput.Controller)>();

        private InputAction attackAction;
        private float nextFireTime;
        private float nextBlockedCueTime;
        private Bounds bodyBounds;
        private bool bodyBoundsResolved;
        private float reloadEndTime;
        private float currentBloom;
        private float currentKick;
        private float currentKickBack;
        private Vector3 modelBasePosition;
        private Quaternion modelBaseRotation;
        private Coroutine hapticRoutine;
        private bool triggerHeld;
        private bool aliveSubscribed;
        private int reserveRounds;

        // VERİLEN silahta ön kabzayı tutan ikinci el — tek yazarı WeaponGranter (SetSecondaryHand).
        private OVRInput.Controller _grantedSecondaryHand = OVRInput.Controller.None;

        // İki elli çözümün ağırlığı ve ikinci elin SON bilinen avuç konumu. Konum saklanır çünkü
        // ikinci el bırakıldığı anda çözüm kapansaydı silah zıplardı: ağırlık sıfıra inerken poz
        // hâlâ o son noktadan çözülür (ItemGripSolver.StepAimBlend).
        private float _aimBlend;
        private Vector3 _lastSecondaryPalm;
        private bool _hasLastSecondaryPalm;

        // tracerEveryNthRound sayacı — silah BAŞINA tutulur (uzak tarafta RemoteShotFx aynısını
        // oyuncu başına tutuyor): sayaç paylaşılsaydı çift tabancada izler iki silaha rastgele
        // dağılır ve hangi namlunun ateş ettiği okunaksız olurdu.
        private int shotCount;

        // Yaylımın saçma uçları (dünya uzayı) — silah başına bir kez ayrılır, atış başına DEĞİL:
        // tetik yolu otomatik ateşte saniyede ~10 kez koşuyor ve GC dikeni Quest'te doğrudan
        // görünür. Boy PelletCount'tan gelir (normal silahta 1) ve ShotTracer'ın tavanına kırpılır.
        private Vector3[] tracerPoints;

        private bool weaponIdWarned;

        protected virtual void Awake()
        {
            // ⚠️ Kavrama yazımının el modelleri OYUNDA HİÇ ÇİZİLMEZ (ItemHandRig): bake onları
            // kapatıyor, burası yalnız emniyet. Açık unutulmuş bir düğüm arenada havada duran bir
            // el olarak görünürdü — raftaki silahta, kavrama tezgâhında, oyuncunun elinde.
            ItemHandRig.HideAll(transform);

            if (definition == null)
            {
                // Tanım ZORUNLU: denge sayılarının tek doğruluk kaynağı SO'dur (§10.3).
                // Ateş kilidi ayrıca canFire koşulundadır (definition != null).
                Debug.LogError($"[Weapon] '{name}' için WeaponDefinition atanmadı; silah kilitli.", this);
            }
            else
            {
                CurrentAmmo = definition.MagazineSize;
                reserveRounds = definition.SpareMagazines * definition.MagazineSize;

                if (string.IsNullOrEmpty(definition.WeaponId))
                {
                    Debug.LogWarning($"[Weapon] '{name}' tanımında weaponId boş; kill feed etiketi boş kalır.", this);
                }
            }

            if (inputActions == null)
                inputActions = InputSystem.actions;
            if (inputActions != null)
                attackAction = inputActions.FindAction("Player/Attack");
            if (attackAction == null)
                Debug.LogWarning("[Weapon] 'Player/Attack' aksiyonu bulunamadı; editör fallback tetiği çalışmaz.", this);
            if (muzzle == null)
                Debug.LogError($"[Weapon] '{name}' muzzle atanmadı; ateş edilemez.", this);
            if (grabbable == null)
                Debug.LogWarning($"[Weapon] '{name}' Grabbable atanmadı; silah yalnız VERİLEN silah olarak " +
                                 "kullanılabilir (weaponSource:\"random\"), sahneden alınamaz.", this);

            if (modelPivot != null)
            {
                modelBasePosition = modelPivot.localPosition;
                modelBaseRotation = modelPivot.localRotation;
            }

            if (weaponAudio != null)
                weaponAudio.Configure(definition);
        }

        protected virtual void OnEnable()
        {
            attackAction?.Enable();

            if (grabbable != null)
                grabbable.WhenPointerEventRaised += HandlePointerEvent;

            TrySubscribeAlive();

            // Toplayıcı abone OLMADAN listeye ekleme: aşağıdaki ActiveChanged bu silahı da
            // saysın (aksi hâlde ilk silah HeldItems'a hiç yazılmazdı).
            EnsureHeldItemsHook();

            Active.Add(this);
            ActiveChanged?.Invoke();
        }

        protected virtual void OnDisable()
        {
            if (grabbable != null)
                grabbable.WhenPointerEventRaised -= HandlePointerEvent;

            // ISDK'nin Cancel olayları artık bize ulaşmaz; el listesi burada temizlenir.
            bool wasHeld = IsHeld;
            heldPoints.Clear();
            GrantedHand = OVRInput.Controller.None;
            GrantKind = WeaponGrantKind.None;
            _grantedSecondaryHand = OVRInput.Controller.None;
            triggerHeld = false;
            HideIndicator();
            if (wasHeld)
                HeldChanged?.Invoke(false);

            Active.Remove(this);
            ActiveChanged?.Invoke();

            if (aliveSubscribed)
            {
                if (PlayerCombatState.Instance != null)
                    PlayerCombatState.Instance.AliveChanged -= HandleAliveChanged;
                aliveSubscribed = false;
            }

            if (hapticRoutine != null)
            {
                StopCoroutine(hapticRoutine);
                hapticRoutine = null;
            }

            // Güvenli taraf: darbe yarıda kesilmiş olabilir — iki kontrolcüde de titreşimi kes.
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
        }

        protected virtual void Update()
        {
            // PlayerCombatState kendini sahne yüklendikten SONRA önyükler; OnEnable
            // sırasında henüz doğmamış olabilir. Tek seferlik tembel abonelik.
            if (!aliveSubscribed)
                TrySubscribeAlive();

            // Reload silah bırakılmış/yere konmuşken de tamamlanır (süre fiziksel değil).
            if (IsReloading && Time.time >= reloadEndTime)
                FinishReload();

            if (muzzle != null && IsHeld)
                TickTrigger();
            else
                triggerHeld = false;

            currentBloom = Mathf.MoveTowards(currentBloom, 0f,
                (definition != null ? definition.BloomRecoveryPerSecond : 0f) * Time.deltaTime);

            float recoverSpeed = definition != null ? definition.RecoilRecoverSpeed : 0f;
            currentKick = Mathf.MoveTowards(currentKick, 0f, recoverSpeed * Time.deltaTime);
            currentKickBack = Mathf.MoveTowards(currentKickBack, 0f, recoverSpeed * 0.02f * Time.deltaTime);

            if (modelPivot != null)
            {
                modelPivot.localRotation = modelBaseRotation * Quaternion.Euler(-currentKick, 0f, 0f);
                modelPivot.localPosition = modelBasePosition + modelPivot.localRotation * (Vector3.back * currentKickBack);
            }
        }

        /// <summary>El izlemesi güncellendikten SONRA sürülür: LateUpdate'te yapılmazsa silah bir
        /// kare geriden gelir ve nişan hissi bulanıklaşır.</summary>
        protected virtual void LateUpdate()
        {
            ApplyCanonicalGrip();
            TickSecondaryGripIndicator();
        }

        protected virtual void OnDestroy()
        {
            // Gösterge için alınan materyal ÖRNEĞİ (renderer.material) silahla birlikte gider —
            // bırakılsa sahne değişimine kadar sızardı.
            if (indicatorMaterial != null)
            {
                Destroy(indicatorMaterial);
                indicatorMaterial = null;
            }
        }

        // -------------------------------------------------------- kanonik kavrama

        /// <summary>
        /// §6.6 <b>KANONİK KAVRAMA</b>: silah tutulduğu sürece kökü ana elin anchor'ından +
        /// tanımın SABİT kavrama kaydından (konum <b>ve dönüş</b>) sürülür — kavradığı andaki keyfi
        /// ofset korunmaz.
        /// <para>
        /// <b>Neden zorunlu:</b> duruş telde gitmez; uzak taraf silahı "elin pozu × sabit kavrama
        /// ofseti" olarak çizer. Serbest kavrama o eşitliği bozar ve iki uçta iki ayrı duruş doğar.
        /// </para>
        /// <para>
        /// <b>VERİLEN silahın İKİ türü de buradan sürülür</b> (Disposable dahil): iki elli çözüm
        /// eşyanın pozunu her karede yeniden hesapladığı için Disposable örnek de artık anchor'ın
        /// ÇOCUĞU değil <see cref="WeaponGranter"/>'ın DDOL kökünün altındadır. ⚠️ İki değişiklik
        /// birbirine bağlıdır: anchor'ın altındaki bir örneğe dünya pozu yazmak parent dönüşümüyle
        /// çakışır ve silah katlanarak uzaklaşır.
        /// </para>
        /// <para>
        /// <b>İki elli nişan</b> (<see cref="SecondaryHand"/>): ana kavrama noktası ana avuçta
        /// kalır, silahın ekseni ikinci elin avucuna döner — matematiği
        /// <see cref="ItemGripSolver"/>'dadır (uzak taraf AYNI fonksiyonu koşar, §6.6).
        /// </para>
        /// <para>
        /// Rig yoksa (admin gözlemci, editör oturumu, sahne henüz yüklenmemiş) hiçbir şey yapılmaz —
        /// silah bulunduğu yerde kalır; bırakılınca da öyle (mevcut davranış).
        /// </para>
        /// <para>⚠️ Geri tepme <see cref="ModelPivot"/>'a uygulanır ve buraya KARIŞMAZ: kökü el
        /// sürer, görsel sarsıntı çocukta yaşar. Fizik de yarışmaz — Grabbable tutuş boyunca
        /// Rigidbody'yi kinematik yapıyor (<c>_kinematicWhileSelected</c>).</para>
        /// </summary>
        private void ApplyCanonicalGrip()
        {
            if (definition == null)
            {
                return;
            }

            if (!IsGranted && heldPoints.Count == 0)
            {
                // Bırakılan silahın nişan ağırlığı taşınmaz: tekrar alındığında tek elli başlasın.
                _aimBlend = 0f;
                _hasLastSecondaryPalm = false;
                return;
            }

            if (!WeaponGranter.TryResolvePalm(MainHand, out Pose primaryPalm))
            {
                return;
            }

            bool wantTwoHand = false;
            if (IsTwoHanded && WeaponGranter.TryResolvePalm(SecondaryHand, out Pose secondaryPalm))
            {
                wantTwoHand = true;
                _lastSecondaryPalm = secondaryPalm.position;
                _hasLastSecondaryPalm = true;
            }

            _aimBlend = ItemGripSolver.StepAimBlend(_aimBlend, wantTwoHand, Time.deltaTime);
            if (_aimBlend <= 0f)
            {
                _hasLastSecondaryPalm = false;
            }

            // Ana kavramanın ölçüsü ÖNCE canlı bilek deltasıyla anchor uzayına çevrilir
            // (ItemGripAuthority: el başına); kavrama yazılmamışsa tanımın kendi ölçüsüne düşülür
            // (bilek ≡ anchor varsayımı). ⚠️ Düşme bir hata değil normal bir yoldur.
            bool mainHandRight = HandGripPivot.IsRight(MainHand);
            bool secondaryRight = SecondaryHandIsRight(mainHandRight);
            Vector3 position;
            Quaternion rotation;

            if (ItemGripAuthority.TryResolvePrimaryGrip(definition, mainHandRight,
                    out Vector3 gripPosition, out Quaternion gripRotation))
            {
                ItemGripSolver.Solve(definition, secondaryRight, gripPosition, gripRotation,
                    primaryPalm, _hasLastSecondaryPalm, _lastSecondaryPalm, _aimBlend,
                    out position, out rotation);
            }
            else
            {
                ItemGripSolver.Solve(definition, mainHandRight, secondaryRight, primaryPalm,
                    _hasLastSecondaryPalm, _lastSecondaryPalm, _aimBlend, out position, out rotation);
            }

            transform.SetPositionAndRotation(position, rotation);
        }

        /// <summary>
        /// Ön kabzayı saran elin SAĞ olup olmadığı; ikinci el henüz yoksa <b>ana elin tersi</b>.
        /// <para>⚠️ <c>HandGripPivot.IsRight(None)</c> "sağ" der (kontrolcü çözülemeyen oturumlar
        /// için makul bir varsayım), oysa burada None "ikinci el yok" demektir: doğrudan çağrılsaydı
        /// iki eli de sağ sayan bir ölçü çıkar ve sağ elle tutulan tüfeğin ön kabza ekseni SAĞ
        /// kaydından okunurdu.</para>
        /// </summary>
        private bool SecondaryHandIsRight(bool mainHandRight)
        {
            OVRInput.Controller secondary = SecondaryHand;
            return secondary == OVRInput.Controller.None
                ? !mainHandRight
                : HandGripPivot.IsRight(secondary);
        }

        // ------------------------------------------------------------------ tetik

        private void TickTrigger()
        {
            bool pressed;
            bool pressedThisFrame;

            // Ana el: VERİLEN silahta silahın verildiği el, sahne silahında ilk kavrayan el.
            // Ayrım zorunlu: 'Player/Attack' tek bir Button action'dır ve
            // <XRController>/{PrimaryAction} ile İKİ kumandayı da toplar — iki elde iki
            // silahla oynanan FFA'da tek tetiğe basmak ikisini birden ateşlerdi.
            OVRInput.Controller mainHand = MainHand;

            if (mainHand != OVRInput.Controller.None)
            {
                float trigger = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, mainHand);
                bool wasHeld = triggerHeld;
                triggerHeld = wasHeld ? trigger >= TriggerReleaseThreshold : trigger > TriggerPressThreshold;
                pressed = triggerHeld;
                pressedThisFrame = triggerHeld && !wasHeld;
            }
            else
            {
                // Editör fallback'i: kontrolcü çözülemedi, Input System aksiyonu okunur.
                pressed = attackAction != null && attackAction.IsPressed();
                pressedThisFrame = attackAction != null && attackAction.WasPressedThisFrame();
                triggerHeld = pressed;
            }

            // Ateş yetkisi sunucu durumundan gelir (ölüyken / Loading-Countdown-End
            // fazlarında tetik BOŞA basılır: boş şarjör sesi bile çalmaz).
            bool combatAllows = ArenaCombat.CanFire;

            // §10.9 duvar arkasından ateş kapısı. ⚠️ YALNIZ tetiğe basılıyken yoklanır: karar
            // sadece ateş anında gerekiyor ve boştaki her silah için kare başına üç physics
            // sorgusu yapmak (iki elde iki silah × 72 Hz) bedavaya ödenmiş bir maliyet olurdu.
            IsWeaponBlocked = pressed && muzzle != null &&
                              ArenaCombat.IsWeaponBlocked(modelPivot, ResolveBodyBounds(),
                                  muzzle.position, muzzle.forward);

            // İki kapı, tek geri bildirim. Oyuncunun kendi gövdesi kapısı burada DEĞİL
            // ArenaCombat.CanFire'da uygulanıyor (silaha ait bir soru değil); burada yalnız
            // "sessizce hiçbir şey olmasın" diye okunuyor — sebebi bilmeden tık sesi çalamayız.
            bool obstacleBlocked = pressed &&
                                   (IsWeaponBlocked || ObstacleViolationProbe.IsBodyBlocked);

            bool canFire = !IsReloading && CurrentAmmo > 0 && combatAllows && definition != null &&
                           !IsWeaponBlocked;

            if (pressed && canFire && Time.time >= nextFireTime)
            {
                Fire();
            }
            else if (obstacleBlocked && ArenaCombat.IsAlive && !IsReloading && CurrentAmmo > 0)
            {
                // Silah ya da oyuncu bir engele değiyor: mermi HİÇ çıkmaz. ⚠️ Burada Fire()
                // çağrılmadığı için cephane gitmez, namlu alevi/sesi oynamaz, ağa shot_event
                // gitmez ve atış gecikmesi (nextFireTime) de ilerlemez — oyuncu duvara ateş
                // ederek şarjör yakamaz.
                CueBlockedFire();
            }
            else if (pressedThisFrame && combatAllows && !canFire && !IsReloading && CurrentAmmo == 0)
            {
                // Boş şarjör: kuru tetik. Otomatik reload BİLEREK yok — dolum oyuncunun
                // bilinçli hareketiyle başlar (TryStartReload).
                weaponAudio?.PlayDry();
                DryFired?.Invoke();
            }
        }

        /// <summary>
        /// Tıkalı namlu geri bildirimi: kuru tetik sesi + kısa darbe.
        /// <para>⚠️ <b>Kısılır</b> (<see cref="BlockedCueSeconds"/>): tetik basılı tutuluyor olabilir
        /// ve kare başına bir tık sesi hem duyulmaz hem de ses kanalını doldurur.</para>
        /// <para><see cref="DryFired"/> yayınlanmaz: o olay "şarjör boş" demek ve HUD'ın reload
        /// istemi ona bağlı — tıkalı namlu ise dolu bir silahın geçici durumudur.</para>
        /// </summary>
        private void CueBlockedFire()
        {
            if (Time.unscaledTime < nextBlockedCueTime)
            {
                return;
            }

            nextBlockedCueTime = Time.unscaledTime + BlockedCueSeconds;

            weaponAudio?.PlayDry();

            // Atış darbesiyle aynı yol: coroutine tek, temizliği OnDisable'da — kumandada asılı
            // kalan bir titreşim bırakmaz.
            if (hapticRoutine != null)
            {
                StopCoroutine(hapticRoutine);
            }

            hapticRoutine = StartCoroutine(HapticPulse(BlockedHapticAmplitude, BlockedHapticSeconds));
        }

        /// <summary>
        /// Silah gövdesinin <see cref="modelPivot"/> uzayındaki sınırları — engel kutusu testinin
        /// girdisi. <b>Bir kez</b> hesaplanır: mesh'ler silahın ömrü boyunca değişmez ve renderer
        /// başına 8 köşe dönüşümü her tetik karesinde tekrarlanacak bir iş değildir.
        /// <para>⚠️ Kaynak <see cref="Mesh.bounds"/>'un YEREL sınırlarıdır, <c>Renderer.bounds</c>
        /// (dünya AABB'si) DEĞİL: dünya kutusunu yerel uzaya geri çevirmek çapraz duran bir tüfekte
        /// hacmi neredeyse iki katına çıkarır ve kutu testi siperin yanındaki meşru atışları
        /// keserdi.</para>
        /// <para>⚠️ Tarama <see cref="modelPivot"/>'tan başlar, silahın kökünden DEĞİL: kökün altında
        /// kavrama çerçevesi gibi silaha ait olmayan görseller var.</para>
        /// </summary>
        private Bounds ResolveBodyBounds()
        {
            if (bodyBoundsResolved || modelPivot == null)
            {
                return bodyBounds;
            }

            bodyBoundsResolved = true;

            Matrix4x4 toPivotSpace = modelPivot.worldToLocalMatrix;
            MeshFilter[] filters = modelPivot.GetComponentsInChildren<MeshFilter>(false);
            bool found = false;

            for (int i = 0; i < filters.Length; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                if (mesh == null)
                    continue;

                Renderer meshRenderer = filters[i].GetComponent<Renderer>();
                if (meshRenderer == null || !meshRenderer.enabled)
                    continue;

                Matrix4x4 toPivot = toPivotSpace * filters[i].transform.localToWorldMatrix;
                Vector3 center = mesh.bounds.center;
                Vector3 extents = mesh.bounds.extents;

                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3(
                        center.x + ((c & 1) == 0 ? -extents.x : extents.x),
                        center.y + ((c & 2) == 0 ? -extents.y : extents.y),
                        center.z + ((c & 4) == 0 ? -extents.z : extents.z));

                    Vector3 point = toPivot.MultiplyPoint3x4(corner);
                    if (!found)
                    {
                        bodyBounds = new Bounds(point, Vector3.zero);
                        found = true;
                        continue;
                    }

                    bodyBounds.Encapsulate(point);
                }
            }

            return bodyBounds;
        }

        protected virtual void Fire()
        {
            nextFireTime = Time.time + definition.SecondsPerShot;

            bool stabilized = IsTwoHanded;
            // Saçılım atıştan ÖNCEKİ bloom ile hesaplanır; bloom atışla büyür.
            float spread = (definition.BaseSpreadDegrees + currentBloom) * (stabilized ? twoHandSpreadMultiplier : 1f);
            currentBloom = Mathf.Min(currentBloom + definition.BloomPerShotDegrees, definition.MaxBloomDegrees);
            float recoilScale = stabilized ? twoHandRecoilMultiplier : 1f;

            CurrentAmmo--;
            AmmoChanged?.Invoke();
            Fired?.Invoke();
            weaponAudio?.PlayFire();

            if (muzzleFlash != null)
                muzzleFlash.Emit(14);

            if (string.IsNullOrEmpty(WeaponId))
                WarnMissingWeaponId();

            // Saçmalı silahta tek tetik çekişi birden çok ışın atar (§10.3 bunu bekliyor: atış hızı
            // denetimi tam da "pompalı saçması" gibi örüntüler düşmesin diye yok). Normal silahta
            // sayı 1'dir ve döngü tek turda biter — ayrı bir kod yolu YOKTUR.
            int pellets = definition.PelletCount;

            // Tracer kapısı yaylımdan ÖNCE, bir kez sorulur: sayaç TETİK ÇEKİŞİNİ sayar, saçmayı
            // değil. Saçma başına sorulsaydı `tracerEveryNthRound` yaylımın içini eleyip 9 saçmanın
            // 3'ünü çizerdi — oysa ayarın anlamı "kaçta bir ATIŞ iz bırakır".
            bool drawTracer = ShouldDrawTracer();
            int tracerCount = 0;

            for (int p = 0; p < pellets; p++)
            {
                Vector2 scatter = Random.insideUnitCircle * spread;
                Vector3 direction = Quaternion.AngleAxis(scatter.x, muzzle.up) *
                                    Quaternion.AngleAxis(scatter.y, muzzle.right) *
                                    muzzle.forward;

                // ⚠️ Işın BURADA atılmaz: engel kuralı (namlu bir iç engelin içinden ateş edemez) tek
                // kapıdadır — kendi Physics.Raycast'ini yazan her yeni hasar kaynağı o kuralı kaybeder.
                ArenaCombat.ShotTrace trace = ArenaCombat.TraceShot(muzzle.position, direction, definition.Range);

                if (p == 0)
                {
                    // Ağ — ATIŞ (§6.4, UDP olay kanalı): her Fire()'da TAM BİR KEZ, isabet olsun
                    // olmasın. Uzak taraf bununla namlu alevini/sesini oynatır ve tracer'ı çizer, o
                    // yüzden mesafe ışının GERÇEKTE gittiği yoldur: isabet varsa oraya kadar, engel
                    // yuttuysa namlu ucu, yoksa menzil sonu.
                    // ⚠️ Bu bildirim VURUŞTAN (aşağıdaki hit_report) BAĞIMSIZDIR — biri sunum, öteki
                    // otoriter durum; kanalları da ayrıdır (Docs/Gelistirici/Yemek-Kitabi.md).
                    // ⚠️ Saçmalıda da TEK KEZ gider (ilk saçmanın yönü/mesafesiyle): saçma başına
                    // yollamak aynı ateşi 9 kez duyurur ve olay kanalının paket başı sınırını
                    // (§6.4) tek tetikle doldururdu — uzakta çizilen tek bir namlu alevi zaten
                    // doğru sunumdur.
                    ArenaCombat.ReportShot(direction, trace.Distance, NetItemId, IsMainHandRight);
                }

                // Yerel mermi izinin ucu. ⚠️ Uzak sunum yolu (RemoteShotFx) atanın kendi izini
                // ÇİZEMEZ ve çizmeyecek: sunucu olayı atana geri yollamaz, istemci de kendi
                // playerId'sini süzer (§6.5). Atanın izini çizecek tek yer burasıdır — atlanırsa
                // "herkes görüyor, ateş eden görmüyor" gibi teşhisi zor bir eksik doğar.
                // ⚠️ Uç noktalar BURADA toplanır (çizim yaylım bitince, tek çağrıda): atanın
                // ekranında her saçma KENDİ ışınının gerçekten gittiği yere kadar çizilir —
                // saçılım vektörü de mesafesi de zaten elde, uzaktaki gibi yeniden üretilmez.
                if (drawTracer && tracerCount < tracerPoints.Length)
                {
                    tracerPoints[tracerCount++] = muzzle.position + direction * trace.Distance;
                }

                if (!trace.HasHit)
                {
                    continue;
                }

                RaycastHit hit = trace.Hit;
                if (hitEffectPrefab != null)
                {
                    GameObject fx = Instantiate(hitEffectPrefab, hit.point + hit.normal * 0.01f,
                        Quaternion.LookRotation(hit.normal));
                    Destroy(fx, 2f);
                }

                // Bölge çarpanı BURADA uygulanır: hasar istemci-otoriter, sunucu
                // hit_report.damage'ı aynen işler (protokol §10.3).
                // ⚠️ Hasar SAÇMA BAŞINADIR — bölünmez: WeaponDefinition.Damage zaten tek saçmanın
                // hasarıdır (CS2 modeli), toplam hasar isabet eden saçma sayısından doğar.
                float damage = definition.Damage * definition.GetZoneMultiplier(ArenaCombat.GetHitZone(hit.collider));

                // Hasar HİÇBİR KOŞULDA yerelde uygulanmaz: can sunucu-otoriterdir, geri
                // health_update ile gelir. Hedef ağ oyuncusu değilse (dekor, duvar) hiçbir şey
                // olmaz — yukarıda oynatılan çarpma efekti kalır. Kırılabilir objeler ağsal
                // olduğunda onlar da bu hit_report yoluna girecek.
                // ⚠️ Aynı hedefe giden saçmalar TEK bir rapora TOPLANMAZ: sunucu her raporu ayrı
                // işler ve bölge çarpanı saçma başına farklıdır (biri kafaya, biri bacağa gidebilir).
                ArenaCombat.ReportRaycastHit(hit, damage, WeaponId);
            }

            if (tracerCount > 0)
            {
                DrawLocalTracer(tracerCount);
            }

            currentKick = Mathf.Min(currentKick + definition.KickDegrees * recoilScale, definition.KickDegrees * 4f);
            currentKickBack = Mathf.Min(currentKickBack + definition.KickBackMeters * recoilScale, definition.KickBackMeters * 3f);

            TriggerHapticPulse();
        }

        /// <summary>
        /// Atanın KENDİ mermi izi — çizgi ve yol boyunca duman (uzaktakini
        /// <see cref="RemoteShotFx"/> çizer). Duman ayrı bir çağrı DEĞİLDİR: çizim çağrısının
        /// içindedir, yani burada unutulması mümkün değil.
        /// <para>Sıklık ve görünüm uzak izle <b>aynı kaynaktan</b> okunur
        /// (<see cref="ItemDefinition"/>): iki taraf ayrı ayarlansa aynı silah kendi ekranında
        /// başka, karşı ekranda başka görünürdü. Havuz da paylaşımlıdır
        /// (<see cref="ShotTracer.Shared"/>) — silah başına havuz açmak, silahların sürekli
        /// üretilip yok edildiği modlarda (<c>weaponSource:"random"</c>) materyali ve Update
        /// döngüsünü silah sayısınca çoğaltırdı.</para>
        /// <para>⚠️ <b>Saçmalı silahta her saçmanın kendi izi çizilir</b> ve hepsi TEK çağrıyla
        /// gider (<see cref="ShotTracer.Play"/>): duman bütçesi ile çizgi kalınlığı
        /// yaylımın tamamına göre ölçülüyor, saçma başına ayrı çağrı ikisini de saçma sayısınca
        /// çoğaltırdı.</para>
        /// </summary>
        /// <param name="pointCount"><see cref="tracerPoints"/>'in dolu eleman sayısı.</param>
        private void DrawLocalTracer(int pointCount)
        {
            ShotTracer.Shared.Play(
                muzzle.position,
                tracerPoints,
                pointCount,
                definition.TracerColor,
                definition.TracerWidth,
                definition.TracerLifetime);
        }

        /// <summary>
        /// Bu tetik çekişi iz bırakacak mı — sayacı ilerletir ve <c>tracerEveryNthRound</c>
        /// kapısını uygular. Olumluysa yaylımın uç tamponu da hazırlanır.
        /// <para>⚠️ Sayaç <b>tetik çekişini</b> sayar, saçmayı değil: ayarın anlamı "kaçta bir
        /// atış iz bırakır"dır, "yaylımın kaçta biri çizilir" değil. Saçma başına sayılsaydı 9
        /// saçmalı bir silahta ayar hem sıklığı hem yelpazenin bütünlüğünü aynı anda bozardı.</para>
        /// </summary>
        private bool ShouldDrawTracer()
        {
            shotCount++;

            int everyNth = definition.TracerEveryNthRound;
            if (everyNth < 1 || shotCount % everyNth != 0)
                return false;

            // Tampon ilk izde kurulur ve silah boyunca yaşar. Boy PelletCount'tan gelir; tavan
            // ShotTracer'ındır (havuz bütçesi orada tanımlı, çağıran kendi tavanını taşımaz).
            int capacity = Mathf.Clamp(definition.PelletCount, 1, ShotTracer.MaxScatterLines);
            if (tracerPoints == null || tracerPoints.Length != capacity)
                tracerPoints = new Vector3[capacity];

            return true;
        }

        // ------------------------------------------------------------ silah verme

        /// <summary>
        /// Silahı bir kumandaya VERİR (<see cref="WeaponGranter"/> çağırır): tutuş sayılır, ISDK
        /// kavraması hiç işletilmez.
        /// <para>
        /// <b>Cephane davranışı türe göre AYRILIR</b> (<see cref="WeaponGrantKind"/>):
        /// <list type="bullet">
        /// <item><c>Disposable</c>: silah dolu şarjörle başlar, rezerv YOKTUR (reload kapalı olduğu
        /// için yedek şarjör sayacı yalnız HUD'a yalan söylerdi) ve yarım kalmış reload iptal edilir
        /// — her çağrı yeni bir silahtır.</item>
        /// <item><c>Persistent</c>: cephaneye <b>HİÇ DOKUNULMAZ</b> ve devam eden reload da iptal
        /// edilmez. Çerçeve silahı gizlenip tekrar açılan TEK örnek olduğu için "aynı silah aynı
        /// mermiyle geri gelir" kuralı doğrudan bundan doğar; doldurmak, oyuncuya sonsuz cephane
        /// veren bir bırak-tut hilesi açardı.</item>
        /// </list>
        /// </para>
        /// </summary>
        public void GrantTo(OVRInput.Controller hand, WeaponGrantKind kind)
        {
            bool wasHeld = IsHeld;
            GrantedHand = hand;
            GrantKind = kind;
            triggerHeld = false;

            // İkinci el yeni tutuşa taşınmaz: silah el değiştirmiş olabilir ve granter ön kabzayı
            // bir sonraki karede yeniden çözer.
            _grantedSecondaryHand = OVRInput.Controller.None;

            if (kind == WeaponGrantKind.Disposable)
            {
                IsReloading = false;
                CurrentAmmo = MagazineSize;
                reserveRounds = 0;
            }

            AmmoChanged?.Invoke();
            if (!wasHeld)
                HeldChanged?.Invoke(true);
            ActiveChanged?.Invoke();
        }

        /// <summary>
        /// Verilmeyi geri alır (silah artık elde değil). Çerçeve klonu gizlenirken
        /// <see cref="WeaponGranter"/> çağırır.
        /// <para>Gizleme (<c>SetActive(false)</c>) zaten <see cref="OnDisable"/>'ı tetikleyip aynı
        /// temizliği yapıyor; açık API yine de var, çünkü "silahı elden al" niyeti bir yan etkiye
        /// değil bir çağrıya bağlı olmalı — <c>OnDisable</c>'ın sırasına güvenen kod kırılgandır.</para>
        /// </summary>
        public void Revoke()
        {
            if (!IsGranted)
            {
                return;
            }

            bool wasHeld = IsHeld;
            GrantedHand = OVRInput.Controller.None;
            GrantKind = WeaponGrantKind.None;
            _grantedSecondaryHand = OVRInput.Controller.None;
            triggerHeld = false;

            if (wasHeld && !IsHeld)
                HeldChanged?.Invoke(false);
            ActiveChanged?.Invoke();
        }

        /// <summary>
        /// VERİLEN silahta ön kabzayı tutan eli bildirir (<c>None</c> = ikinci el bıraktı).
        /// <para>
        /// ⚠️ <b>Tek çağıranı <see cref="WeaponGranter"/>'dır</b> ve sahne silahında sessiz no-op'tur:
        /// orada ikinci el ISDK'nın kavrama olaylarından gelir. Verilen silahta kavrama algısının
        /// TEK kaynağı granter'ın grip yoklamasıdır — iki yol birden açık olsaydı aynı el iki ayrı
        /// kaynaktan "tutuyor" görünürdü.
        /// </para>
        /// <para><see cref="ActiveChanged"/> yalnız DEĞİŞİMDE yayınlanır: olay <c>HeldItems</c>
        /// toplayıcısını (§6.6 <c>GRIP_LINKED</c>) tazeliyor, koşulsuz yayınlasa grip basılı olan
        /// her kare bir tam tarama olurdu.</para>
        /// </summary>
        public void SetSecondaryHand(OVRInput.Controller hand)
        {
            if (!IsGranted)
            {
                return;
            }

            if (hand == GrantedHand)
            {
                hand = OVRInput.Controller.None;
            }

            if (_grantedSecondaryHand == hand)
            {
                return;
            }

            _grantedSecondaryHand = hand;
            ActiveChanged?.Invoke();
        }

        // ------------------------------------------------------------------ reload

        /// <summary>
        /// Reload başlatmayı dener; başlattıysa true. Reddetme koşulları: TEK KULLANIMLIK verilen
        /// silah (§10.5 <c>weaponSource:"random"</c>; çerçeve silahında reload AÇIKTIR ve bel-altı
        /// jestiyle çalışır), zaten reload'da, tanımsız, şarjör tam, oyuncu ölü, rezerv yetersiz
        /// (Discard: tam şarjör yok; Pool: havuz boş). Discard modunda şarjör başlangıçta
        /// ÇIKAR: tetik reload boyunca ölüdür ve şarjörde kalan mermi YANMIŞTIR. Ses
        /// çalınmaz — şarjör seslerini WeaponAnimator kendi zaman çizgisinde çalar.
        /// </summary>
        public bool TryStartReload()
        {
            if (IsDisposableGrant || IsReloading || definition == null)
                return false;
            if (CurrentAmmo >= definition.MagazineSize)
                return false;
            if (!ArenaCombat.IsAlive)
                return false;

            if (definition.ReserveMode == WeaponReserveMode.DiscardMagazine)
            {
                if (reserveRounds < definition.MagazineSize)
                    return false;

                // Yeni şarjörün mermileri ŞİMDİ rezervden düşülür; eski şarjördekiler
                // şarjörle birlikte atılmış sayılır (varsayılan ürün kuralı).
                reserveRounds -= definition.MagazineSize;
                CurrentAmmo = 0;
            }
            else if (reserveRounds <= 0)
            {
                return false;
            }
            // Pool modunda şarjördeki mermiler korunur (CS2 kuralı), düşüm bitişte yapılır.

            IsReloading = true;
            reloadEndTime = Time.time + definition.ReloadTime;
            ReloadStarted?.Invoke(definition.ReloadTime);
            AmmoChanged?.Invoke();
            return true;
        }

        private void FinishReload()
        {
            IsReloading = false;

            if (definition != null)
            {
                if (definition.ReserveMode == WeaponReserveMode.DiscardMagazine)
                {
                    // Yeni şarjörün bedeli reload BAŞLARKEN rezervden düşülmüştü.
                    CurrentAmmo = definition.MagazineSize;
                }
                else
                {
                    int need = definition.MagazineSize - CurrentAmmo;
                    int take = Mathf.Min(need, reserveRounds);
                    CurrentAmmo += take;
                    reserveRounds -= take;
                }
            }

            ReloadCompleted?.Invoke();
            AmmoChanged?.Invoke();
        }

        /// <summary>
        /// Şarjörü ve rezervi tanımındaki tam değerlere döndürür (canlanma dolumu).
        /// Devam eden reload iptal edilir ve dinleyiciler kapansın diye
        /// <see cref="ReloadCompleted"/> yayınlanır. TEK KULLANIMLIK verilen silahta rezerv 0 kalır
        /// (o modda reload yok); çerçeve silahı tam rezervle döner.
        /// </summary>
        public void RefillFull()
        {
            if (definition == null)
                return;

            if (IsReloading)
            {
                IsReloading = false;
                ReloadCompleted?.Invoke();
            }

            CurrentAmmo = definition.MagazineSize;
            reserveRounds = IsDisposableGrant ? 0 : definition.SpareMagazines * definition.MagazineSize;
            AmmoChanged?.Invoke();
        }

        // ------------------------------------------------------------- el takibi

        private void HandlePointerEvent(PointerEvent evt)
        {
            switch (evt.Type)
            {
                case PointerEventType.Select:
                    AddHeldPoint(evt);
                    break;
                case PointerEventType.Unselect:
                case PointerEventType.Cancel:
                    // Cancel hover'daki bir interactor'dan da gelebilir; listede yoksa no-op.
                    RemoveHeldPoint(evt.Identifier);
                    break;
            }
        }

        private void AddHeldPoint(in PointerEvent evt)
        {
            for (int i = 0; i < heldPoints.Count; i++)
            {
                if (heldPoints[i].id == evt.Identifier)
                    return; // aynı interactor'dan çift Select (teorik) — kopya ekleme
            }

            bool wasHeld = heldPoints.Count > 0;
            heldPoints.Add((evt.Identifier, WeaponGranter.ResolveController(evt)));

            if (!wasHeld)
            {
                HeldChanged?.Invoke(true);
                weaponAudio?.PlayPickup();
            }

            // ⚠️ ActiveChanged KOŞULSUZ yayınlanır (yalnız 0→1 geçişinde değil): ikinci elin
            // kabzaya girmesi telde bir değişikliktir (§6.6 GRIP_LINKED) ve HeldItems toplayıcısı
            // bu olaya bağlı. Koşula bağlıyken çift el kavraması ağa hiç yansımıyordu.
            ActiveChanged?.Invoke();
        }

        private void RemoveHeldPoint(int identifier)
        {
            for (int i = 0; i < heldPoints.Count; i++)
            {
                if (heldPoints[i].id != identifier)
                    continue;

                heldPoints.RemoveAt(i);

                // Ana el değişti (ya da silah bırakıldı): tetik durumu tazelenir — yeni
                // ana elin basılı tetiği bir sonraki karede taze basış olarak görülür.
                if (i == 0)
                    triggerHeld = false;

                if (heldPoints.Count == 0)
                {
                    HeldChanged?.Invoke(false);
                }

                // Koşulsuz — gerekçe AddHeldPoint'te (ikinci elin bırakılması da telde değişiklik).
                ActiveChanged?.Invoke();
                return;
            }
        }

        // ⚠️ El çözümü BURADA DEĞİL: interactor'dan OVR kontrolcüsü çıkarmanın tek yeri
        // WeaponGranter.ResolveController(evt) / ResolveControllerFromGameObject(go). İki ayrı
        // tüketicisi var (bu sınıf, WeaponFrame) ve kopyalandığında biri düzeltilip diğeri
        // unutuluyordu.

        // ------------------------------------------- ön kabza: kapı + gösterge

        /// <summary>
        /// Ön kabza göstergesinin GÖRÜNÜR olduğu mesafe (m) — playtest değeri, tüm silahlarda aynı.
        /// <para>⚠️ Kabul yarıçapı ise silah başınadır (<see cref="ItemDefinition.SecondaryGripRadius"/>)
        /// ve bu sabiti AŞABİLİR; etkin görünme mesafesi bu yüzden
        /// <c>Mathf.Max(SecondaryGripHoverRadius, radius)</c>'tur — yarıçap görünmeyi geçerse
        /// "önce ipucu görünür, sonra kavranır" sırası tersine döner ve gösterge işlevsiz kalır.</para>
        /// </summary>
        private const float SecondaryGripHoverRadius = 0.30f;

        /// <summary>Kabul mesafesine girince göstergenin büyüme oranı — "şimdi bas" okumasını gözle ayırır.</summary>
        private const float IndicatorReadyScale = 1.35f;

        /// <summary>Yaklaşma durumunda alfa: gösterge "burada bir yer var" der, "şimdi bas" demez.</summary>
        private const float IndicatorHoverAlpha = 0.35f;

        /// <summary>Yaklaşma rengi (mavi): "burada bir yer var".</summary>
        private static readonly Color IndicatorHoverColor = new Color(0.45f, 0.85f, 1f, 1f);

        /// <summary>Kabul rengi (yeşil): "şimdi bas". Renk ayrımı alfa/ölçek farkının ÜSTÜNE gelir —
        /// VR'da yalnız parlaklık değişimi kabul mesafesine girildiğini yeterince okutmuyor.</summary>
        private static readonly Color IndicatorReadyColor = new Color(0.35f, 0.95f, 0.45f, 1f);

        /// <summary>Gösterge prefabı katalogda yoksa OTURUM başına bir uyarı (silah başına değil).</summary>
        private static bool indicatorPrefabWarned;

        // Bu silahın gösterge örneği (tembel; yalnız yaklaşan bir el olunca doğar) ve rengi
        // sürülecek yüzeyi: halka prefabında LineRenderer, tasarlanmış bir sanatta materyal.
        private Transform indicator;
        private LineRenderer indicatorLine;
        private Material indicatorMaterial;

        /// <summary>
        /// Ön kabza noktasının DÜNYA konumu — <paramref name="rightHand"/> elin kaydından
        /// (kayıt el başınadır: kabza simetrik olmadığı için iki elin bileği farklı yere düşer).
        /// <para>⚠️ <see cref="Transform.TransformPoint"/> DEĞİL elle bileşim: kayıt metredir ve
        /// <c>WPN_*</c> kökleri 0.8 ölçekli — <c>TransformPoint</c> ölçeği ikinci kez uygular
        /// (<see cref="ItemGripPose"/>). Kapı, gösterge ve <c>RemoteAvatar</c> aynı bileşimi kullanır.</para>
        /// </summary>
        public Vector3 SecondaryGripWorld(bool rightHand)
        {
            return definition == null
                ? transform.position
                : transform.position + transform.rotation * definition.SecondaryGripPosition(rightHand);
        }

        /// <summary>
        /// Bu elin AVUCU ön kabzanın kabul yarıçapında mı — ikinci elin <b>KURULMA</b> kapısı.
        /// <para>
        /// <b>Tek kural, iki okuyucu:</b> <see cref="WeaponGranter"/> ikinci eli buna göre bağlar
        /// (grip basılı + bu <c>true</c> → el ön kabzaya kilitlenir), gösterge de yeşile buna göre
        /// döner. İki ayrı ölçü olsaydı oyuncuya "şimdi bas" diye büyütülen gösterge sessizce
        /// reddedilebilirdi.
        /// </para>
        /// <para>
        /// ⚠️ Mesafe ANCHOR'dan değil AVUÇtan ölçülür (<see cref="WeaponGranter.TryResolvePalm"/>):
        /// oyuncunun gördüğü şey kumanda değil sentetik eldir; anchor'a göre ölçülen kabul mesafesi
        /// elin birkaç santim ötesinde başlar ve gösterge "elimin içinde ama yeşile dönmüyor" diye
        /// okunur.
        /// </para>
        /// <para>⚠️ Bu yalnız kurulma kapısıdır; bağın SÜRDÜRÜLMESİ mesafeye bakmaz (gerekçe
        /// <c>WeaponGranter.ResolveSecondaryHand</c>'de).</para>
        /// </summary>
        public bool IsHandOnSecondaryGrip(OVRInput.Controller hand)
        {
            if (definition == null || !definition.IsTwoHanded)
            {
                return false;
            }

            if (!WeaponGranter.TryResolvePalm(hand, out Pose palm))
            {
                return false;
            }

            float radius = definition.SecondaryGripRadius;
            Vector3 socket = SecondaryGripWorld(HandGripPivot.IsRight(hand));
            return (palm.position - socket).sqrMagnitude <= radius * radius;
        }

        /// <summary>
        /// Ön kabza göstergesinin bir karelik durumu: silah tutuluyor, iki elli ve ikinci el henüz
        /// bağlı değilken BOŞ elin avucu ön kabzaya yaklaştıkça gösterge belirir (mavi, soluk),
        /// kabul yarıçapına girince yeşile döner ve büyür ("şimdi bas"); ikinci el bağlanınca kaybolur.
        /// <para>
        /// <b>Gösterge YALNIZ ön kabza içindir.</b> Ana kabzanın göstergesi yoktur: silah zaten ana
        /// elde doğuyor (verilen/çağrılan silah) ya da çerçeveden seçiliyor — ana kabza için
        /// oyuncunun elini bir yere götürmesi gerekmiyor.
        /// </para>
        /// <para>
        /// <b>Sanat prefabdadır</b> (<see cref="WeaponCatalog.SecondaryGripIndicatorPrefab"/>, tüm
        /// silahlar aynı göstergeyi paylaşır); bu sınıf yalnız yerini, ölçeğini ve rengini sürer.
        /// Prefab yoksa gösterge çizilmez ve bir kez uyarılır — kapı yine çalışır (kavrama göstergesiz
        /// de kabul edilir).
        /// </para>
        /// <para>
        /// ⚠️ <b>Uzak avatarlarda çizilmez</b> ve bunun için bir şey yapmak gerekmez:
        /// <c>RemoteAvatar.SterilizeVisual</c> kopyadaki tüm MonoBehaviour'ları (bu sınıf dahil) yok
        /// eder — gösterge yalnız yerel oyuncunun elindeki silahta yaşar.
        /// </para>
        /// <para>
        /// ⚠️ <see cref="LateUpdate"/>'te ve <see cref="ApplyCanonicalGrip"/>'ten SONRA çağrılır: silahın
        /// bu karedeki pozu yazılmadan ölçülen mesafe bir kare geriden gelir ve gösterge hızlı
        /// harekette silahtan kopuk görünür.
        /// </para>
        /// </summary>
        private void TickSecondaryGripIndicator()
        {
            if (definition == null || !definition.IsTwoHanded || !IsHeld ||
                SecondaryHand != OVRInput.Controller.None)
            {
                HideIndicator();
                return;
            }

            OVRInput.Controller free = FreeHand();
            if (free == OVRInput.Controller.None || !WeaponGranter.TryResolvePalm(free, out Pose palm))
            {
                // Rig yok (admin gözlemci, editör oturumu) ya da ana el çözülemedi: gösterilecek el yok.
                HideIndicator();
                return;
            }

            Vector3 socket = SecondaryGripWorld(HandGripPivot.IsRight(free));
            float distance = Vector3.Distance(palm.position, socket);
            float radius = definition.SecondaryGripRadius;

            if (distance > Mathf.Max(SecondaryGripHoverRadius, radius))
            {
                HideIndicator();
                return;
            }

            if (!EnsureIndicator())
            {
                return;
            }

            bool ready = distance <= radius;

            indicator.gameObject.SetActive(true);
            indicator.SetPositionAndRotation(socket, IndicatorRotation());

            // ⚠️ Ölçek DÜNYA ölçüsüdür: gösterge silahın altında duruyor ve WPN kökleri 0.8 ölçekli —
            // yerel ölçek düz yazılsa halka silahtan silaha farklı büyüklükte görünürdü.
            float parentScale = Mathf.Max(1e-4f, transform.lossyScale.x);
            indicator.localScale = Vector3.one * ((ready ? IndicatorReadyScale : 1f) / parentScale);

            Color color = ready ? IndicatorReadyColor : IndicatorHoverColor;
            color.a = ready ? 1f : IndicatorHoverAlpha;

            if (indicatorLine != null)
            {
                indicatorLine.startColor = color;
                indicatorLine.endColor = color;
            }
            else if (indicatorMaterial != null)
            {
                indicatorMaterial.color = color;
            }
        }

        /// <summary>Ana elin karşısındaki el; ana el çözülemediyse <c>None</c>.</summary>
        private OVRInput.Controller FreeHand()
        {
            switch (MainHand)
            {
                case OVRInput.Controller.LTouch: return OVRInput.Controller.RTouch;
                case OVRInput.Controller.RTouch: return OVRInput.Controller.LTouch;
                default: return OVRInput.Controller.None;
            }
        }

        /// <summary>
        /// Gösterge KAMERAYA çevrilir: bir NOKTAYI işaretliyor ve nokta işareti bakana dönük okunur —
        /// halka silahın dönüşünü alsaydı düzlemine kenarından bakan oyuncu halka yerine bir çizgi
        /// görürdü (<c>LineAlignment.View</c> bunu çözmez: o yalnız şeridin kalınlığını çevirir).
        /// Kamera yoksa silahın dönüşü.
        /// </summary>
        private Quaternion IndicatorRotation()
        {
            Camera camera = Camera.main;
            return camera != null
                ? Quaternion.LookRotation(camera.transform.forward, camera.transform.up)
                : transform.rotation;
        }

        /// <summary>
        /// Gösterge örneğini katalogdaki prefabtan üretir (bir kez, silahın altına). Prefabtan fizik
        /// sökülür: collider bırakılırsa gösterge hem ateş ışınına hem kavramaya takılır — oyuncuya
        /// yardım etmesi gereken şey nişanı bozardı.
        /// </summary>
        private bool EnsureIndicator()
        {
            if (indicator != null)
            {
                return true;
            }

            WeaponCatalog catalog = WeaponCatalog.Load();
            GameObject prefab = catalog != null ? catalog.SecondaryGripIndicatorPrefab : null;
            if (prefab == null)
            {
                if (!indicatorPrefabWarned)
                {
                    indicatorPrefabWarned = true;
                    Debug.LogWarning("[Weapon] WeaponCatalog.secondaryGripIndicatorPrefab boş — ön kabza " +
                                     "göstergesi çizilmeyecek (kavrama yine çalışır). " +
                                     "'Build Weapon Prefabs' göstergeyi üretip bağlar.");
                }

                return false;
            }

            GameObject instance = Instantiate(prefab, transform);
            instance.name = "[GripIndicator]";

            Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Destroy(colliders[i]);
            }

            Rigidbody[] bodies = instance.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                Destroy(bodies[i]);
            }

            indicator = instance.transform;
            indicatorLine = instance.GetComponentInChildren<LineRenderer>(true);

            if (indicatorLine == null)
            {
                // Materyal örneği BİR KEZ burada alınır (her karede .material yeni bir örnek üretirdi).
                // Renk özelliği yoksa sessizce geçilir: gösterge yalnız ölçek/aktiflikle konuşur.
                var renderer = instance.GetComponentInChildren<Renderer>(true);
                if (renderer != null)
                {
                    Material material = renderer.material;
                    if (material != null &&
                        (material.HasProperty("_BaseColor") || material.HasProperty("_Color")))
                    {
                        indicatorMaterial = material;
                    }
                }
            }

            return true;
        }

        private void HideIndicator()
        {
            if (indicator != null && indicator.gameObject.activeSelf)
            {
                indicator.gameObject.SetActive(false);
            }
        }

        // ------------------------------------------------------- canlanma dolumu

        private void TrySubscribeAlive()
        {
            if (aliveSubscribed || PlayerCombatState.Instance == null)
                return;

            PlayerCombatState.Instance.AliveChanged += HandleAliveChanged;
            aliveSubscribed = true;
        }

        private void HandleAliveChanged(bool alive)
        {
            // Tabanında dirilen oyuncu tam cephaneyle başlar (yalnız elindeki silah:
            // yerdeki silahlar dolmaz, sahiplerini bekler).
            if (alive && IsHeld)
                RefillFull();
        }

        // ------------------------------------------------------------------- ağ

        /// <summary>
        /// §6.6: YEREL oyuncunun elinde ne olduğunu <see cref="HeldItems"/>'a bildiren TEK yer.
        /// <para>
        /// <b>Neden statik ve merkezî:</b> <c>HeldItems</c> oyuncunun TAMAMINI anlatır (iki slot +
        /// kavrama bitleri), tek bir silahı değil — çift tabanca meşru bir durumdur. Her silah kendi
        /// durumunu bildirseydi ikinci silah birincinin slotunu ezer ve elde ne olduğu silahların
        /// rastgele sırasına bağlı kalırdı.
        /// </para>
        /// <para>
        /// ⚠️ Her karede DEĞİL, yalnız <see cref="ActiveChanged"/>'de koşar: "elde ne var" insan
        /// hızında değişir, <see cref="Active"/>'i kare başına taramanın karşılığı yoktur.
        /// </para>
        /// </summary>
        private static void RefreshHeldItems()
        {
            byte left = 0;
            byte right = 0;
            bool gripLinked = false;
            bool primaryRight = false;

            for (int i = 0; i < Active.Count; i++)
            {
                Weapon weapon = Active[i];
                if (weapon == null || !weapon.IsHeld)
                {
                    continue;
                }

                byte id = weapon.NetItemId;
                if (id == 0)
                {
                    // Kimliksiz silah uzak tarafta HİÇ çizilmez; sessiz kalması sahada teşhis
                    // edilemez bir "elinde bir şey yok" olarak görünürdü.
                    WarnMissingNetItemId(weapon);
                    continue;
                }

                weapon.GetHeldHands(out bool wantsLeft, out bool wantsRight);

                if (wantsLeft && wantsRight)
                {
                    // Çift ellide İKİ slota AYNI id yazılır + GRIP_LINKED: "aynı id iki slotta"
                    // tek başına çift el demek değildir (çift tabanca), ayrımı yalnız bayrak taşır.
                    if (left != 0 || right != 0)
                    {
                        WarnHandConflict(weapon);
                        continue;
                    }

                    left = id;
                    right = id;
                    gripLinked = true;
                    primaryRight = weapon.IsMainHandRight;
                    continue;
                }

                if (wantsLeft)
                {
                    if (left != 0)
                    {
                        WarnHandConflict(weapon);
                        continue;
                    }

                    left = id;
                }
                else if (wantsRight)
                {
                    if (right != 0)
                    {
                        WarnHandConflict(weapon);
                        continue;
                    }

                    right = id;
                }
            }

            if (left == 0 && right == 0)
            {
                HeldItems.Clear();
                return;
            }

            HeldItems.Report(left, right, gripLinked, primaryRight);
        }

        /// <summary>
        /// Bu silahı hangi el(ler) tutuyor. VERİLEN silahta ana el + (varsa) ön kabzayı tutan
        /// ikinci el; sahne silahında eller <see cref="heldPoints"/>'tan gelir.
        /// <para>⚠️ <b>Verilen silahın iki türü de aynı daldan geçer:</b> ön kabzayı tutan ikinci el
        /// telde <c>GRIP_LINKED</c> üretmeli, yoksa uzak taraf silahı tek elle tutuyor çizerdi. Kural
        /// Disposable için de geçerlidir (bkz. <see cref="IsTwoHanded"/> notu).</para>
        /// <para>⚠️ Çözülemeyen el (<c>None</c>) SAĞ sayılır — telde "bilinmeyen el" diye bir değer
        /// yok. Ama iki kavrama noktası varsa el çözülemese bile İKİSİ birden işaretlenir: aksi
        /// hâlde editör oturumunda (kontrolcü çözülemez) çift el kavraması tek elli görünürdü.</para>
        /// </summary>
        private void GetHeldHands(out bool left, out bool right)
        {
            left = false;
            right = false;

            if (IsGranted)
            {
                if (GrantedHand == OVRInput.Controller.LTouch)
                {
                    left = true;
                }
                else
                {
                    right = true;
                }

                // Ön kabzayı tutan ikinci el varsa iki el birden işaretlenir (el çözülemese de):
                // yukarıdaki "None → sağ" kuralı yüzünden ikinci el ana elle çakışabilirdi ve
                // çift el kavraması telde tek elli görünürdü.
                if (HasSecondaryGrip)
                {
                    left = true;
                    right = true;
                }

                return;
            }

            for (int i = 0; i < heldPoints.Count; i++)
            {
                if (heldPoints[i].ctl == OVRInput.Controller.LTouch)
                {
                    left = true;
                }
                else
                {
                    right = true;
                }
            }

            if (heldPoints.Count > 1)
            {
                left = true;
                right = true;
            }
        }

        /// <summary>Toplayıcıyı <see cref="ActiveChanged"/>'e BİR KEZ bağlar (statik olay, statik
        /// dinleyici: sahne değişse de abonelik kalır, iki kez bağlanmak refresh'i çiftlerdi).</summary>
        private static void EnsureHeldItemsHook()
        {
            if (heldItemsHooked)
            {
                return;
            }

            heldItemsHooked = true;
            ActiveChanged += RefreshHeldItems;
        }

        private static void WarnMissingNetItemId(Weapon weapon)
        {
            if (missingNetItemIdWarned)
            {
                return;
            }

            missingNetItemIdWarned = true;
            Debug.LogWarning($"[Weapon] '{weapon.name}' tanımında netItemId yok (0); elde tutulan " +
                             "eşya AĞA BİLDİRİLMEZ ve uzak oyuncularda çizilmez. Tanıma 1-255 " +
                             "arası kararlı bir kimlik ver (Tools > VortexArena > Weapons > Rebuild Net Item Catalog).",
                weapon);
        }

        private static void WarnHandConflict(Weapon weapon)
        {
            if (handConflictWarned)
            {
                return;
            }

            handConflictWarned = true;
            Debug.LogWarning($"[Weapon] '{weapon.name}' zaten dolu bir el slotunu istedi; ilk bulunan " +
                             "silah kazandı ve bu silah ağa bildirilmedi. Aynı eli iki silahın " +
                             "tutması beklenmez — kavrama/verme yollarından biri temizlenmemiş olabilir.",
                weapon);
        }

        private void WarnMissingWeaponId()
        {
            if (weaponIdWarned)
                return;
            weaponIdWarned = true;
            Debug.LogWarning($"[Weapon] '{name}' weaponId olmadan ateş etti; vuruş gönderildi ama " +
                             "kill feed etiketi boş kalacak.", this);
        }

        // --------------------------------------------------------------- haptik

        /// <summary>
        /// Atış darbesini başlatır. Şiddet ve süre <b>silahın tanımından</b> okunur
        /// (<see cref="WeaponDefinition.HapticAmplitude"/> / <see cref="WeaponDefinition.HapticDuration"/>)
        /// — kodda sabit bir darbe YOKTUR, her silahın vuruş hissi kendi SO'sundan gelir.
        /// <para>Değerler her atışta <b>yeniden</b> okunur ve darbeye parametre olarak geçer:
        /// koşan bir darbenin ortasında tanım değişirse (silah verilip geri alınırsa) yarım kalan
        /// darbe kendi şiddetiyle biter.</para>
        /// <para>⚠️ Sıfır şiddet ya da sıfır süre "haptik kapalı" demektir ve darbe hiç
        /// başlatılmaz: sıfır süreli bir coroutine titreşimi açıp kapatmayı aynı kareye sıkıştırır,
        /// bu da kumandada asılı kalan bir titreşim bırakabilir.</para>
        /// </summary>
        private void TriggerHapticPulse()
        {
            if (hapticRoutine != null)
            {
                StopCoroutine(hapticRoutine);
                hapticRoutine = null;
            }

            float amplitude = definition != null ? Mathf.Clamp01(definition.HapticAmplitude) : 0f;
            float duration = definition != null ? definition.HapticDuration : 0f;

            if (amplitude <= 0f || duration <= 0f)
            {
                // Haptik kapalı. Yarıda kesilen bir darbe kalmış olabilir — titreşimi kapat.
                SetHeldVibration(0f, 0f);
                return;
            }

            hapticRoutine = StartCoroutine(HapticPulse(amplitude, duration));
        }

        private IEnumerator HapticPulse(float amplitude, float duration)
        {
            // Yalnız silahı FİİLEN tutan el(ler) titrer; None (çözülememiş el) atlanır.
            SetHeldVibration(1f, amplitude);
            yield return new WaitForSeconds(duration);
            // Darbe sırasında bırakılan elin titreşimi OVR tarafında kendiliğinden söner;
            // kalıcı temizlik OnDisable'dadır.
            SetHeldVibration(0f, 0f);
            hapticRoutine = null;
        }

        private void SetHeldVibration(float frequency, float amplitude)
        {
            if (IsGranted)
            {
                OVRInput.SetControllerVibration(frequency, amplitude, GrantedHand);

                // Ön kabzayı tutan el de titrer: tek elde kalan darbe, iki elle sabitlenmiş bir
                // silahta tutuşun yarısını hissettirirdi.
                OVRInput.Controller secondary = SecondaryHand;
                if (secondary != OVRInput.Controller.None)
                {
                    OVRInput.SetControllerVibration(frequency, amplitude, secondary);
                }

                return;
            }

            for (int i = 0; i < heldPoints.Count; i++)
            {
                if (heldPoints[i].ctl != OVRInput.Controller.None)
                    OVRInput.SetControllerVibration(frequency, amplitude, heldPoints[i].ctl);
            }
        }
    }
}
