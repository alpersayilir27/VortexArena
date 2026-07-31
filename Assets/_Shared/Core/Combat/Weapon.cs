using System;
using System.Collections;
using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.InputSystem;
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
    /// <see cref="ItemDefinition.PrimaryGripPosition"/>/<see cref="ItemDefinition.PrimaryGripRotation"/>'dan
    /// sürülür (<see cref="LateUpdate"/>). ISDK'nın <c>*GrabFreeTransformer</c>'ları bu yüzden
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
    /// <item><b>Disposable</b> (§10.5 <c>weaponSource:"random"</c>): <see cref="WeaponGranter"/>
    /// silahı el anchor'ının ALTINA örnekler; tanım gereği tutuluyordur, her zaman tek ellidir ve
    /// reload KAPALIDIR.</item>
    /// <item><b>Persistent</b> (çerçeveden seçilen silah, <see cref="WeaponFrame"/>): klon anchor'ın
    /// çocuğu DEĞİLDİR — pozu her karede kanonik kavramayla sürülür. Reload açıktır, rezervi vardır
    /// ve ikinci el ön kabzayı tutabilir.</item>
    /// </list>
    /// </para>
    /// </summary>
    public class Weapon : MonoBehaviour
    {
        // Analog tetik histerezisi: eşik çevresindeki titreme tek basışı çift saymasın.
        private const float TriggerPressThreshold = 0.55f;
        private const float TriggerReleaseThreshold = 0.35f;

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
        [SerializeField] private float twoHandRecoilMultiplier = 0.35f;

        [Header("Haptik")]
        [SerializeField] private float hapticAmplitude = 0.6f;

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
        /// el anchor'ının altına örneklenir, ISDK kavraması hiç işletilmez. <c>None</c> = normal
        /// raf silahı (kavranarak tutulur).
        /// <para>
        /// Neden ayrı bir yol: <see cref="Grabbable"/>'ı programla "seçili" hâle getirmek ISDK'nın
        /// iç durumuna girmek demektir — kırılgan ve sürüm bağımlı. Verilen silah zaten tanım
        /// gereği tutuluyor, kavrama sistemine hiç sokulmaz.
        /// </para>
        /// </summary>
        public OVRInput.Controller GrantedHand { get; private set; } = OVRInput.Controller.None;

        /// <summary>Verilme TÜRÜ (bkz. <see cref="WeaponGrantKind"/>) — "elde sabit duruyor" ile
        /// "reload kapalı, tek elli" kuralları bu tiple ayrıldı; ikisi artık aynı bayrağa bağlı
        /// değil.</summary>
        public WeaponGrantKind GrantKind { get; private set; } = WeaponGrantKind.None;

        /// <summary>Silah verilerek mi tutuluyor (raf silahında her zaman false).</summary>
        public bool IsGranted => GrantedHand != OVRInput.Controller.None;

        /// <summary>Çerçeveden seçilen KALICI klon mu (reload açık, rezerv var, ön kabza tutulabilir).</summary>
        public bool IsPersistentGrant => GrantKind == WeaponGrantKind.Persistent;

        /// <summary>FFA'nın TEK KULLANIMLIK rastgele silahı mı (reload kapalı, rezerv yok, tek elli).</summary>
        public bool IsDisposableGrant => GrantKind == WeaponGrantKind.Disposable;

        /// <summary>Tutuluyor mu: verilen silah TANIM GEREĞİ tutuluyordur, raf silahı ISDK'nın
        /// pointer olaylarından izlenir. Bu <c>||</c> olmadan verilen silah hiç ateş edemezdi.</summary>
        public bool IsHeld => IsGranted || heldPoints.Count > 0;

        /// <summary>
        /// İki elle sabitleme: raf silahında İKİ kavrama noktası, çerçeve klonunda ise VERİLEN el +
        /// ön kabzayı tutan ikinci el (klonun ön kabzası ISDK kavramasına açıktır).
        /// <para>⚠️ <b>Verilen (Disposable) silah her zaman tek ellidir</b> — o yolda ISDK kavraması
        /// hiç işletilmez; iki el iki AYRI silah tutabilir (çapraz-el durumu tutulmaz).</para>
        /// </summary>
        public bool IsTwoHanded => IsPersistentGrant
            ? heldPoints.Count > 0
            : (!IsGranted && heldPoints.Count > 1);

        /// <summary>
        /// Tetik/ana el: VERİLEN silahta silahın verildiği el, raf silahında İLK kavrayan el.
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

        private bool weaponIdWarned;

        protected virtual void Awake()
        {
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
                                 "kullanılabilir (weaponSource:\"random\"), raftan alınamaz.", this);

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
            triggerHeld = false;
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
        }

        // -------------------------------------------------------- kanonik kavrama

        /// <summary>
        /// §6.6 <b>KANONİK KAVRAMA</b>: silah tutulduğu sürece kökü ana elin anchor'ından +
        /// tanımın SABİT kavrama ofsetinden sürülür — kavradığı andaki keyfi ofset korunmaz.
        /// <para>
        /// <b>Neden zorunlu:</b> duruş telde gitmez; uzak taraf silahı "elin pozu × sabit kavrama
        /// ofseti" olarak çizer. Serbest kavrama o eşitliği bozar ve iki uçta iki ayrı duruş doğar.
        /// </para>
        /// <para>
        /// <b>Yalnız Disposable verilen silah burada işlenmez</b> (<see cref="IsDisposableGrant"/>):
        /// o örnek anchor'ın ÇOCUĞU olarak doğar (<see cref="WeaponGranter"/>) ve pozu zaten aynı
        /// ofsetten gelir. İki yol aynı kurala uyar, ama biri parent hiyerarşisiyle, öteki her
        /// karede sürülerek.
        /// </para>
        /// <para>
        /// <b>Çerçeve klonu (Persistent) BURADAN sürülür</b>: o örnek anchor'ın çocuğu DEĞİLDİR
        /// (DDOL kökünde park eder). Üstelik ön kabzasının ISDK kavramasına açık olması sahiden bir
        /// transformer'ı devreye sokabilir — <c>LateUpdate</c>'te kanonik kavramayı yazmak §6.6'yı
        /// o durumda da garantiler.
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

            if (IsDisposableGrant)
            {
                return;
            }

            if (!IsGranted && heldPoints.Count == 0)
            {
                return;
            }

            Transform anchor = WeaponGranter.ResolveHandAnchor(MainHand);
            if (anchor == null)
            {
                return;
            }

            // TransformPoint DEĞİL: anchor'ın ölçeği (rig'de 1 olmalı ama garanti değil) kavrama
            // ofsetini büyütür/küçültürdü. Kavrama ofseti METRE cinsindendir, ölçeklenmez.
            transform.SetPositionAndRotation(
                anchor.position + anchor.rotation * definition.PrimaryGripPosition,
                anchor.rotation * definition.PrimaryGripRotation);
        }

        // ------------------------------------------------------------------ tetik

        private void TickTrigger()
        {
            bool pressed;
            bool pressedThisFrame;

            // Ana el: VERİLEN silahta silahın verildiği el, raf silahında ilk kavrayan el.
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
            bool canFire = !IsReloading && CurrentAmmo > 0 && combatAllows && definition != null;

            if (pressed && canFire && Time.time >= nextFireTime)
            {
                Fire();
            }
            else if (pressedThisFrame && combatAllows && !canFire && !IsReloading && CurrentAmmo == 0)
            {
                // Boş şarjör: kuru tetik. Otomatik reload BİLEREK yok — dolum oyuncunun
                // bilinçli hareketiyle başlar (TryStartReload).
                weaponAudio?.PlayDry();
                DryFired?.Invoke();
            }
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

            Vector2 scatter = Random.insideUnitCircle * spread;
            Vector3 direction = Quaternion.AngleAxis(scatter.x, muzzle.up) *
                                Quaternion.AngleAxis(scatter.y, muzzle.right) *
                                muzzle.forward;

            if (string.IsNullOrEmpty(WeaponId))
                WarnMissingWeaponId();

            bool didHit = Physics.Raycast(muzzle.position, direction, out RaycastHit hit, definition.Range);

            // Ağ — ATIŞ (§6.4, UDP olay kanalı): her Fire()'da TAM BİR KEZ, isabet olsun olmasın.
            // Uzak taraf bununla namlu alevini/sesini oynatır ve tracer'ı çizer, o yüzden mesafe
            // ışının GERÇEKTE gittiği yoldur: isabet varsa oraya kadar, yoksa menzil sonu.
            // ⚠️ Bu bildirim VURUŞTAN (aşağıdaki hit_report) BAĞIMSIZDIR — biri sunum, öteki
            // otoriter durum; kanalları da ayrıdır (Docs/Gelistirici/Yemek-Kitabi.md).
            ArenaCombat.ReportShot(direction, didHit ? hit.distance : definition.Range,
                NetItemId, IsMainHandRight);

            if (didHit)
            {
                if (hitEffectPrefab != null)
                {
                    GameObject fx = Instantiate(hitEffectPrefab, hit.point + hit.normal * 0.01f,
                        Quaternion.LookRotation(hit.normal));
                    Destroy(fx, 2f);
                }

                // Headshot çarpanı BURADA uygulanır: hasar istemci-otoriter, sunucu
                // hit_report.damage'ı aynen işler (protokol §10.3).
                float damage = definition.Damage *
                               (ArenaCombat.IsHeadshot(hit.collider) ? definition.HeadshotMultiplier : 1f);

                // Hasar HİÇBİR KOŞULDA yerelde uygulanmaz: can sunucu-otoriterdir, geri
                // health_update ile gelir. Hedef ağ oyuncusu değilse (dekor, duvar) hiçbir şey
                // olmaz — yukarıda oynatılan çarpma efekti kalır. Kırılabilir objeler ağsal
                // olduğunda onlar da bu hit_report yoluna girecek.
                ArenaCombat.ReportRaycastHit(hit, damage, WeaponId);
            }

            currentKick = Mathf.Min(currentKick + definition.KickDegrees * recoilScale, definition.KickDegrees * 4f);
            currentKickBack = Mathf.Min(currentKickBack + definition.KickBackMeters * recoilScale, definition.KickBackMeters * 3f);

            if (hapticRoutine != null)
                StopCoroutine(hapticRoutine);
            hapticRoutine = StartCoroutine(HapticPulse());
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
            triggerHeld = false;

            if (wasHeld && !IsHeld)
                HeldChanged?.Invoke(false);
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
        // WeaponGranter.ResolveController(evt) / ResolveControllerFromGameObject(go). Üç ayrı
        // tüketicisi var (bu sınıf, ItemGripSockets, WeaponFrame) ve kopyalandığında biri
        // düzeltilip diğerleri unutuluyordu.

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
        /// Bu silahı hangi el(ler) tutuyor. TEK KULLANIMLIK verilen silah TANIM GEREĞİ tek ellidir;
        /// raf silahında eller <see cref="heldPoints"/>'tan gelir.
        /// <para>⚠️ <b>Çerçeve klonunda ikisi BİRLEŞİR:</b> önce verilen el işaretlenir, sonra
        /// <see cref="heldPoints"/>'takiler de eklenir — ön kabzayı tutan ikinci el telde
        /// <c>GRIP_LINKED</c> üretmeli, yoksa uzak taraf silahı tek elle tutuyor çizerdi.</para>
        /// <para>⚠️ Çözülemeyen el (<c>None</c>) SAĞ sayılır — telde "bilinmeyen el" diye bir değer
        /// yok. Ama iki kavrama noktası varsa el çözülemese bile İKİSİ birden işaretlenir: aksi
        /// hâlde editör oturumunda (kontrolcü çözülemez) çift el kavraması tek elli görünürdü.</para>
        /// </summary>
        private void GetHeldHands(out bool left, out bool right)
        {
            left = false;
            right = false;

            if (IsDisposableGrant)
            {
                if (GrantedHand == OVRInput.Controller.LTouch)
                {
                    left = true;
                }
                else
                {
                    right = true;
                }

                return;
            }

            if (IsPersistentGrant)
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
                if (heldPoints.Count > 0)
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
                             "arası kararlı bir kimlik ver (Tools > VortexArena > Validate Net Item Ids).",
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

        private IEnumerator HapticPulse()
        {
            // Yalnız silahı FİİLEN tutan el(ler) titrer; None (çözülememiş el) atlanır.
            SetHeldVibration(1f, hapticAmplitude);
            yield return new WaitForSeconds(0.05f);
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
