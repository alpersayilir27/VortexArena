using System;
using System.Collections;
using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Input;
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
    /// çarpanla düşürür; geri tepme <see cref="ModelPivot"/>'a uygulanır (grab sistemi
    /// kök transformu sürdüğü için onunla yarışmaz).
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
    /// İKİNCİ TUTUŞ YOLU — <b>verilen silah</b> (§10.5 <c>weaponSource:"random"</c>):
    /// <see cref="WeaponGranter"/> silahı doğrudan el anchor'ının altına örnekler ve
    /// <see cref="GrantTo"/> ile bu silaha kendi elini bildirir; ISDK kavraması hiç işletilmez.
    /// Bu yolda silah tanım gereği tutuluyordur, her zaman tek ellidir ve reload KAPALIDIR.
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

        /// <summary>Silah verilerek mi tutuluyor (raf silahında her zaman false).</summary>
        public bool IsGranted => GrantedHand != OVRInput.Controller.None;

        /// <summary>Tutuluyor mu: verilen silah TANIM GEREĞİ tutuluyordur, raf silahı ISDK'nın
        /// pointer olaylarından izlenir. Bu <c>||</c> olmadan verilen silah hiç ateş edemezdi.</summary>
        public bool IsHeld => IsGranted || heldPoints.Count > 0;

        /// <summary>İki elle sabitleme YALNIZ ISDK kavramasıyla gelir; verilen silah her zaman tek
        /// ellidir (iki el iki AYRI silah tutabilir — çapraz-el durumu tutulmaz).</summary>
        public bool IsTwoHanded => !IsGranted && heldPoints.Count > 1;

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

        // ------------------------------------------------------------------ tetik

        private void TickTrigger()
        {
            bool pressed;
            bool pressedThisFrame;

            // Ana el: VERİLEN silahta silahın verildiği el, raf silahında ilk kavrayan el.
            // Ayrım zorunlu: 'Player/Attack' tek bir Button action'dır ve
            // <XRController>/{PrimaryAction} ile İKİ kumandayı da toplar — iki elde iki
            // silahla oynanan FFA'da tek tetiğe basmak ikisini birden ateşlerdi.
            OVRInput.Controller mainHand = IsGranted ? GrantedHand : heldPoints[0].ctl;

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

            // Ağ: her atış relay edilir (uzak namlu alevi/sesi + sayım). Arena uzayı dönüşümü
            // ve DTO yönetimi ArenaCombat'ın işi — kendi hasar kaynağını yazan da aynı kapıyı
            // kullanır (Docs/Gelistirici/Yemek-Kitabi.md).
            if (string.IsNullOrEmpty(WeaponId))
                WarnMissingWeaponId();
            ArenaCombat.ReportShot(muzzle.position, direction, WeaponId);

            if (Physics.Raycast(muzzle.position, direction, out RaycastHit hit, definition.Range))
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

                // AĞ OYUNCUSU: hasar YEREL UYGULANMAZ — sunucu doğrular ve health_update
                // yayınlar. false = hedef ağ oyuncusu değil (pratik dummy'si, kırılabilir
                // hedef): eski yerel hasar yolu korunur, canları sunucuda tutulmaz.
                if (!ArenaCombat.ReportRaycastHit(hit, damage, WeaponId))
                {
                    Health target = hit.collider.GetComponentInParent<Health>();
                    if (target != null)
                        target.TakeDamage(definition.Damage, this);
                }
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
        /// kavraması hiç işletilmez ve <b>şarjör değiştirme kapanır</b> — bu modda şarjör bitince
        /// silahı bırakıp yenisini çekmek oyuncunun işidir (§10.5 <c>weaponSource:"random"</c>).
        /// </summary>
        public void GrantTo(OVRInput.Controller hand)
        {
            bool wasHeld = IsHeld;
            GrantedHand = hand;

            // Ele yeni geçen silah dolu şarjörle başlar; yarım kalmış bir reload iptal edilir.
            IsReloading = false;
            CurrentAmmo = MagazineSize;
            // Rezerv YOK: reload kapalı olduğu için yedek şarjör sayacı yalnız HUD'a yalan söylerdi.
            reserveRounds = 0;
            triggerHeld = false;

            AmmoChanged?.Invoke();
            if (!wasHeld)
                HeldChanged?.Invoke(true);
            ActiveChanged?.Invoke();
        }

        // ------------------------------------------------------------------ reload

        /// <summary>
        /// Reload başlatmayı dener; başlattıysa true. Reddetme koşulları: VERİLEN silah
        /// (§10.5), zaten reload'da, tanımsız, şarjör tam, oyuncu ölü, rezerv yetersiz
        /// (Discard: tam şarjör yok; Pool: havuz boş). Discard modunda şarjör başlangıçta
        /// ÇIKAR: tetik reload boyunca ölüdür ve şarjörde kalan mermi YANMIŞTIR. Ses
        /// çalınmaz — şarjör seslerini WeaponAnimator kendi zaman çizgisinde çalar.
        /// </summary>
        public bool TryStartReload()
        {
            if (IsGranted || IsReloading || definition == null)
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
        /// <see cref="ReloadCompleted"/> yayınlanır. Verilen silahta rezerv 0 kalır
        /// (o modda reload yok).
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
            reserveRounds = IsGranted ? 0 : definition.SpareMagazines * definition.MagazineSize;
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
            heldPoints.Add((evt.Identifier, ResolveController(evt)));

            if (!wasHeld)
            {
                HeldChanged?.Invoke(true);
                weaponAudio?.PlayPickup();
                ActiveChanged?.Invoke();
            }
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
                    ActiveChanged?.Invoke();
                }

                return;
            }
        }

        /// <summary>
        /// Select olayını üreten interactor'dan tutan elin OVR kontrolcüsünü çözer.
        /// <c>evt.Data</c> varsayılan olarak interactor'ın kendisidir (Interactor._data);
        /// BB kontrolcü rig'i interactor↔IController eşlemesini
        /// <see cref="InteractorControllerDecorator"/> ile kurar. Çözülemezse None döner
        /// (editör fallback işareti: tetik Input System'den okunur).
        /// </summary>
        private static OVRInput.Controller ResolveController(in PointerEvent evt)
        {
            if (evt.Data is IInteractorView view &&
                InteractorControllerDecorator.TryGetControllerForInteractor(view, out IController controller))
            {
                return ToOvrController(controller.Handedness);
            }

            // Yedek: decorator kurulu değilse interactor hiyerarşisindeki ControllerRef'e bak.
            Component dataComponent = evt.Data as Component;
            if (dataComponent != null)
            {
                ControllerRef controllerRef = dataComponent.GetComponentInParent<ControllerRef>();
                if (controllerRef != null)
                    return ToOvrController(controllerRef.Handedness);
            }

            return OVRInput.Controller.None;
        }

        private static OVRInput.Controller ToOvrController(Handedness handedness)
        {
            return handedness == Handedness.Left ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
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
