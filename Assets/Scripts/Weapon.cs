using System.Collections;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.InputSystem;

public enum Team { Red, Blue }

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

    protected virtual void Awake()
    {
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
            bool canFire = !IsReloading && CurrentAmmo > 0;
            if (canFire && attackAction.IsPressed() && Time.time >= nextFireTime)
                Fire();
            else if (!canFire && attackAction.WasPressedThisFrame())
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

        if (Physics.Raycast(muzzle.position, direction, out RaycastHit hit, range))
        {
            if (hitEffectPrefab != null)
            {
                GameObject fx = Instantiate(hitEffectPrefab, hit.point + hit.normal * 0.01f,
                    Quaternion.LookRotation(hit.normal));
                Destroy(fx, 2f);
            }

            Health target = hit.collider.GetComponentInParent<Health>();
            if (target != null)
                target.TakeDamage(damage, this);
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
