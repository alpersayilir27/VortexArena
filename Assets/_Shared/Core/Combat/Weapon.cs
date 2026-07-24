using System.Collections;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.InputSystem;
using VortexArena.Core.Arena;
using VortexArena.Core.Player;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Grabbable hitscan weapon for the VR arena. The weapon rests in the world
    /// (ISDK Grabbable + GrabInteractable, hold-to-grab with the grip button) and
    /// only fires while it is held. Reads the "Player/Attack" action from
    /// InputSystem_Actions (right trigger / PrimaryAction). Holding it with BOTH
    /// hands stabilizes it: spread and recoil are multiplied down.
    /// Recoil is applied to the model pivot so it never fights the grab system
    /// that drives the root transform.
    /// </summary>
    public class Weapon : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string weaponName = "Rifle";
        [SerializeField] private Team team = Team.Red;
        // Tanım atanırsa istatistikler Awake'te SO'dan okunur; Inspector değerleri yedektir.
        [Tooltip("Silah tanımı SO'su (_Shared/Arsenal/Data). Atanırsa istatistikler buradan okunur.")]
        [SerializeField] private WeaponDefinition definition;
        [Tooltip("Protokol silah anahtarı ('ak47'/'m4'); tanım atanırsa oradan okunur.")]
        [SerializeField] private string weaponId = "";

        [Header("Stats")]
        [SerializeField] private float damage = 25f;
        [SerializeField] private float fireRateRpm = 700f;
        [SerializeField] private float range = 60f;
        [Tooltip("Bullet spread half-angle in degrees (one-handed).")]
        [SerializeField] private float spreadDegrees = 1f;

        [Header("Ammo")]
        [SerializeField] private int magazineSize = 30;
        [SerializeField] private float reloadTime = 2.4f;

        [Header("Recoil (one-handed)")]
        [SerializeField] private float recoilKickDegrees = 2f;
        [SerializeField] private float recoilKickBackMeters = 0.02f;
        [SerializeField] private float recoilRecoverSpeed = 10f;

        [Header("Two-Handed Stabilization")]
        [Tooltip("Spread multiplier while gripping with both hands.")]
        [SerializeField] private float twoHandSpreadMultiplier = 0.45f;
        [Tooltip("Recoil multiplier while gripping with both hands.")]
        [SerializeField] private float twoHandRecoilMultiplier = 0.35f;

        [Header("References")]
        [SerializeField] private Transform muzzle;
        [Tooltip("Child holding the gun geometry; recoil kick is applied here.")]
        [SerializeField] private Transform modelPivot;
        [SerializeField] private Grabbable grabbable;
        [SerializeField] private ParticleSystem muzzleFlash;
        [SerializeField] private WeaponAudio weaponAudio;
        [SerializeField] private GameObject hitEffectPrefab;
        [Tooltip("Falls back to the project-wide input actions when empty.")]
        [SerializeField] private InputActionAsset inputActions;

        [Header("Haptics")]
        [SerializeField] private float hapticAmplitude = 0.6f;

        public string WeaponName => weaponName;

        /// <summary>Protokol silah anahtarı: tanım varsa ondan, yoksa Inspector alanından.</summary>
        public string WeaponId => definition != null ? definition.WeaponId : weaponId;

        /// <summary>Silahın durduğu taban takımı (ateş YETKİSİ PlayerCombatState'ten gelir).</summary>
        public Team Team => team;
        public bool IsHeld => grabbable != null && grabbable.SelectingPointsCount > 0;
        public bool IsTwoHanded => grabbable != null && grabbable.SelectingPointsCount > 1;
        public int CurrentAmmo { get; private set; }
        public int MagazineSize => magazineSize;
        public bool IsReloading { get; private set; }

        private InputAction attackAction;
        private float nextFireTime;
        private float reloadEndTime;
        private float currentKick;
        private float currentKickBack;
        private Vector3 modelBasePosition;
        private Quaternion modelBaseRotation;
        private Coroutine hapticRoutine;

        // --- Ağ (Faz 3) ---
        // Her atışta yeni DTO ayırmamak için tek örnek yeniden kullanılır; dizi
        // alanları da bir kez ayrılır (JsonUtility içerikleri her gönderimde okur).
        private readonly ShotFiredMsg netShot = new ShotFiredMsg { muzzlePos = new float[3], muzzleDir = new float[3] };
        private readonly HitReportMsg netHit = new HitReportMsg { hitPos = new float[3] };
        private int netSeq;
        private bool weaponIdWarned;

        protected virtual void Awake()
        {
            // Tanım atanmışsa istatistikler SO'dan gelir (iki taraf aynı değeri kullansın diye
            // tek doğruluk kaynağı SO'dur; sunucu tablosuyla elle senkron — protokol §10.3).
            if (definition != null)
            {
                if (!string.IsNullOrEmpty(definition.DisplayName))
                {
                    weaponName = definition.DisplayName;
                }

                damage = definition.Damage;
                fireRateRpm = definition.FireRateRpm;
                range = definition.Range;
                spreadDegrees = definition.SpreadDegrees;
                magazineSize = definition.MagazineSize;
                reloadTime = definition.ReloadTime;
            }

            if (string.IsNullOrEmpty(WeaponId))
            {
                Debug.LogWarning($"[Weapon] '{name}' için weaponId boş; sunucu vuruşları reddeder (WeaponDefinition ata).", this);
            }

            if (inputActions == null)
                inputActions = InputSystem.actions;
            if (inputActions != null)
                attackAction = inputActions.FindAction("Player/Attack");
            if (attackAction == null)
                Debug.LogError("Weapon: 'Player/Attack' action not found. Assign the InputSystem_Actions asset.", this);
            if (muzzle == null)
                Debug.LogError("Weapon: muzzle transform is not assigned.", this);
            if (grabbable == null)
                Debug.LogWarning("Weapon: no Grabbable assigned; the weapon can never be held or fired.", this);

            if (modelPivot != null)
            {
                modelBasePosition = modelPivot.localPosition;
                modelBaseRotation = modelPivot.localRotation;
            }

            CurrentAmmo = magazineSize;
        }

        protected virtual void OnEnable() => attackAction?.Enable();

        protected virtual void OnDisable()
        {
            if (hapticRoutine != null)
            {
                StopCoroutine(hapticRoutine);
                hapticRoutine = null;
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
            }
        }

        protected virtual void Update()
        {
            if (IsReloading && Time.time >= reloadEndTime)
            {
                IsReloading = false;
                CurrentAmmo = magazineSize;
            }

            if (attackAction != null && muzzle != null && IsHeld)
            {
                // Ateş yetkisi sunucu durumundan gelir (ölüyken / Loading-Countdown-End
                // fazlarında tetik BOŞA basılır: boş şarjör sesi bile çalmaz).
                bool combatAllows = PlayerCombatState.Instance == null || PlayerCombatState.Instance.CanFire;
                bool canFire = !IsReloading && CurrentAmmo > 0 && combatAllows;
                if (canFire && attackAction.IsPressed() && Time.time >= nextFireTime)
                    Fire();
                else if (!canFire && combatAllows && attackAction.WasPressedThisFrame())
                    weaponAudio?.PlayEmpty();
            }

            currentKick = Mathf.MoveTowards(currentKick, 0f, recoilRecoverSpeed * Time.deltaTime);
            currentKickBack = Mathf.MoveTowards(currentKickBack, 0f, recoilRecoverSpeed * 0.02f * Time.deltaTime);

            if (modelPivot != null)
            {
                modelPivot.localRotation = modelBaseRotation * Quaternion.Euler(-currentKick, 0f, 0f);
                modelPivot.localPosition = modelBasePosition + modelPivot.localRotation * (Vector3.back * currentKickBack);
            }
        }

        protected virtual void Fire()
        {
            nextFireTime = Time.time + 60f / Mathf.Max(1f, fireRateRpm);

            bool stabilized = IsTwoHanded;
            float spread = spreadDegrees * (stabilized ? twoHandSpreadMultiplier : 1f);
            float recoilScale = stabilized ? twoHandRecoilMultiplier : 1f;

            CurrentAmmo--;
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

            if (Physics.Raycast(muzzle.position, direction, out RaycastHit hit, range))
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
                    // health_update yayınlar (protokol §10.3).
                    SendHitReport(hitBox.PlayerId, hit.point);
                }
                else
                {
                    // Ağ oyuncusu değil (pratik dummy'si, kırılabilir hedef): eski
                    // yerel hasar yolu korunur — bunların canı sunucuda tutulmaz.
                    Health target = hit.collider.GetComponentInParent<Health>();
                    if (target != null)
                        target.TakeDamage(damage, this);
                }
            }

            currentKick = Mathf.Min(currentKick + recoilKickDegrees * recoilScale, recoilKickDegrees * 4f);
            currentKickBack = Mathf.Min(currentKickBack + recoilKickBackMeters * recoilScale, recoilKickBackMeters * 3f);

            if (hapticRoutine != null)
                StopCoroutine(hapticRoutine);
            hapticRoutine = StartCoroutine(HapticPulse());

            if (CurrentAmmo <= 0)
                StartReload();
        }

        /// <summary>Starts a reload (also triggered automatically on an empty magazine).</summary>
        public void StartReload()
        {
            if (IsReloading || CurrentAmmo == magazineSize)
                return;
            IsReloading = true;
            reloadEndTime = Time.time + reloadTime;
            weaponAudio?.PlayReload();
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
        /// hit_report gönderir (hedef bir ağ oyuncusu). Sunucu faz/takım/rate-limit/hasar
        /// doğrulamasını yapar; geçerse health_update yayınlar (protokol §10.3).
        /// </summary>
        private void SendHitReport(int targetPlayerId, Vector3 worldHitPosition)
        {
            ArenaClient client = ArenaClient.Instance;
            if (client == null || !client.IsConnected)
                return;

            if (string.IsNullOrEmpty(WeaponId))
            {
                WarnMissingWeaponId();
                return;
            }

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
            Debug.LogError($"[Weapon] '{name}' weaponId olmadan ateş etti; ağ mesajı gönderilmedi.", this);
        }

        private static void Write(float[] target, in Vector3 value)
        {
            target[0] = value.x;
            target[1] = value.y;
            target[2] = value.z;
        }

        private IEnumerator HapticPulse()
        {
            // The grab can be with either (or both) hands; pulse both controllers.
            OVRInput.SetControllerVibration(1f, hapticAmplitude, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(1f, hapticAmplitude, OVRInput.Controller.RTouch);
            yield return new WaitForSeconds(0.05f);
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
            hapticRoutine = null;
        }
    }
}
