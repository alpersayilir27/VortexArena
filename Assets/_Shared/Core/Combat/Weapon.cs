using System;
using System.Collections;
using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using VortexArena.Core.Arena;
using VortexArena.Core.Player;
using VortexArena.Net;
using VortexArena.Protocol;
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
    /// <c>hit_report.damage</c> ile bildirilir (protokol §10.3).
    /// </para>
    /// <para>
    /// Şarjör bitince otomatik reload YOKTUR — dolum <see cref="TryStartReload"/> ile
    /// bilinçli başlatılır (ör. WeaponAnimator'ın el hareketi/etkileşimi). Rezerv
    /// muhasebesi <see cref="WeaponReserveMode"/>'a göre yürür; reload silah bırakılsa
    /// da tamamlanır. Şarjör seslerini bu sınıf ÇALMAZ (WeaponAnimator zaman çizgisi).
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

        public bool IsHeld => heldPoints.Count > 0;
        public bool IsTwoHanded => heldPoints.Count > 1;
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

        // --- Ağ (Faz 3) ---
        // Her atışta yeni DTO ayırmamak için tek örnek yeniden kullanılır; dizi
        // alanları da bir kez ayrılır (JsonUtility içerikleri her gönderimde okur).
        private readonly ShotFiredMsg netShot = new ShotFiredMsg { muzzlePos = new float[3], muzzleDir = new float[3] };
        private readonly HitReportMsg netHit = new HitReportMsg { hitPos = new float[3] };
        private int netSeq;
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
                Debug.LogWarning($"[Weapon] '{name}' Grabbable atanmadı; silah asla tutulamaz.", this);

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
        }

        protected virtual void OnDisable()
        {
            if (grabbable != null)
                grabbable.WhenPointerEventRaised -= HandlePointerEvent;

            // ISDK'nin Cancel olayları artık bize ulaşmaz; el listesi burada temizlenir.
            bool wasHeld = heldPoints.Count > 0;
            heldPoints.Clear();
            triggerHeld = false;
            if (wasHeld)
                HeldChanged?.Invoke(false);

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
            OVRInput.Controller mainHand = heldPoints[0].ctl;

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
            bool combatAllows = PlayerCombatState.Instance == null || PlayerCombatState.Instance.CanFire;
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

            // Ağ: her atış relay edilir (uzak namlu alevi/sesi + sayım). Poz/yön ARENA
            // UZAYINDA gider — alıcı kendi dünyasına çevirir (protokol §3).
            SendShotFired(muzzle.position, direction);

            if (Physics.Raycast(muzzle.position, direction, out RaycastHit hit, definition.Range))
            {
                if (hitEffectPrefab != null)
                {
                    GameObject fx = Instantiate(hitEffectPrefab, hit.point + hit.normal * 0.01f,
                        Quaternion.LookRotation(hit.normal));
                    Destroy(fx, 2f);
                }

                RemoteHitBox hitBox = hit.collider.GetComponentInParent<RemoteHitBox>();
                if (hitBox != null && hitBox.PlayerId > 0)
                {
                    // AĞ OYUNCUSU: hasar YEREL UYGULANMAZ — sunucu doğrular ve
                    // health_update yayınlar (protokol §10.3). Headshot çarpanı BURADA
                    // uygulanır: hasar istemci-otoriter, hit_report.damage aynen işlenir.
                    float damage = definition.Damage * (hitBox.IsHead ? definition.HeadshotMultiplier : 1f);
                    SendHitReport(hitBox.PlayerId, hit.point, damage);
                }
                else
                {
                    // Ağ oyuncusu değil (pratik dummy'si, kırılabilir hedef): eski
                    // yerel hasar yolu korunur — bunların canı sunucuda tutulmaz.
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

        // ------------------------------------------------------------------ reload

        /// <summary>
        /// Reload başlatmayı dener; başlattıysa true. Reddetme koşulları: zaten reload'da,
        /// tanımsız, şarjör tam, oyuncu ölü, rezerv yetersiz (Discard: tam şarjör yok;
        /// Pool: havuz boş). Discard modunda şarjör başlangıçta ÇIKAR: tetik reload
        /// boyunca ölüdür ve şarjörde kalan mermi YANMIŞTIR. Ses çalınmaz — şarjör
        /// seslerini WeaponAnimator kendi zaman çizgisinde çalar.
        /// </summary>
        public bool TryStartReload()
        {
            if (IsReloading || definition == null)
                return false;
            if (CurrentAmmo >= definition.MagazineSize)
                return false;
            if (PlayerCombatState.Instance != null && !PlayerCombatState.Instance.IsAlive)
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
        /// <see cref="ReloadCompleted"/> yayınlanır.
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
            reserveRounds = definition.SpareMagazines * definition.MagazineSize;
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
                    HeldChanged?.Invoke(false);

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

        /// <summary>
        /// shot_fired gönderir (sunucu doğrulamaz, yalnız relay eder). Bağlantı yoksa
        /// ArenaClient.Send zaten no-op'tur — yerel sunum etkilenmez.
        /// </summary>
        private void SendShotFired(Vector3 worldMuzzlePosition, Vector3 worldDirection)
        {
            ArenaClient client = ArenaClient.Instance;
            if (client == null || !client.IsConnected)
                return;

            if (string.IsNullOrEmpty(WeaponId))
            {
                WarnMissingWeaponId();
                return;
            }

            Vector3 arenaPos = ArenaSpace.WorldToArena(worldMuzzlePosition);
            // Yön bir NOKTA değildir: origin farkı düşülür (yalnız dönüş uygulanır).
            Vector3 arenaDir = (ArenaSpace.WorldToArena(worldMuzzlePosition + worldDirection) - arenaPos).normalized;

            netShot.seq = ++netSeq;
            netShot.weaponId = WeaponId;
            Write(netShot.muzzlePos, arenaPos);
            Write(netShot.muzzleDir, arenaDir);
            client.Send(netShot);
        }

        /// <summary>
        /// hit_report gönderir (hedef bir ağ oyuncusu). <b>Hasarı biz belirleriz</b>: sunucuda
        /// silah tablosu yoktur, buradaki <paramref name="damage"/> (headshot çarpanı dahil)
        /// aynen uygulanır (protokol §10.3). Sunucu yalnız durum tutarlılığına bakar
        /// (faz, atıcı/hedef canlı mı, dost ateşi).
        /// </summary>
        private void SendHitReport(int targetPlayerId, Vector3 worldHitPosition, float damage)
        {
            ArenaClient client = ArenaClient.Instance;
            if (client == null || !client.IsConnected)
                return;

            // weaponId artık yalnız kill feed etiketi (sunucu doğrulamaz) — eksikse uyarırız ama
            // vuruşu YİNE göndeririz: kozmetik bir alan yüzünden meşru hasar kaybolmasın.
            if (string.IsNullOrEmpty(WeaponId))
                WarnMissingWeaponId();

            netHit.seq = ++netSeq;
            netHit.targetPlayerId = targetPlayerId;
            netHit.weaponId = WeaponId;
            netHit.damage = damage;
            Write(netHit.hitPos, ArenaSpace.WorldToArena(worldHitPosition));
            client.Send(netHit);
        }

        private void WarnMissingWeaponId()
        {
            if (weaponIdWarned)
                return;
            weaponIdWarned = true;
            Debug.LogWarning($"[Weapon] '{name}' weaponId olmadan ateş etti; vuruş gönderildi ama " +
                             "kill feed etiketi boş kalacak.", this);
        }

        private static void Write(float[] target, in Vector3 value)
        {
            target[0] = value.x;
            target[1] = value.y;
            target[2] = value.z;
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
            for (int i = 0; i < heldPoints.Count; i++)
            {
                if (heldPoints[i].ctl != OVRInput.Controller.None)
                    OVRInput.SetControllerVibration(frequency, amplitude, heldPoints[i].ctl);
            }
        }
    }
}
