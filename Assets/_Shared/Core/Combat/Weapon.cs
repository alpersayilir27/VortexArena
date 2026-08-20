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
    /// <summary>Holdable hitscan VR weapon: sits in the world (ISDK Grabbable + GrabInteractable)
    /// and fires only while held. The trigger is read from the MAIN hand's controller (resolved
    /// from the Grabbable pointer event; falling back to the Input System "Player/Attack" action in
    /// the Editor). A two-handed hold scales spread and recoil down; recoil is applied to
    /// <see cref="ModelPivot"/>, so it does not race the canonical grip driving the root.
    /// <para>THE GRIP IS CANONICAL (§6.6): while held, the root is driven from the main hand anchor
    /// plus the studio-authored grip position
    /// (<see cref="ItemDefinition.PrimaryGripPosition(bool)"/>); one-handed the rotation IS the
    /// main controller's (the record carries no rotation), two-handed the second hand's POSITION
    /// aims the weapon (<see cref="LateUpdate"/>, <see cref="ItemGripSolver"/>). ISDK's
    /// <c>*GrabFreeTransformer</c>s are therefore removed from <c>WPN_*</c> prefabs: grab SENSING
    /// stays in Grabbable/GrabInteractable, CARRYING the weapon happens here. The reason is the
    /// network — pose does not travel, so the remote side draws the weapon as "hand pose × fixed
    /// grip offset"; a free grab breaks that equality and a rifle held by the barrel would look
    /// correctly held on the other screen.</para>
    /// <para>All balance/feel/audio values come from <see cref="WeaponDefinition"/> (MANDATORY).
    /// Damage is client-authoritative: zone multiplier included, computed here and reported through
    /// <see cref="ArenaCombat"/> (§10.3). No message is ever written to the network directly —
    /// ArenaCombat is the only gate.</para>
    /// <para>No auto-reload on an empty magazine: reloading starts deliberately via
    /// <see cref="TryStartReload"/> (e.g. <see cref="WeaponReloadGesture"/>'s below-the-belt
    /// gesture). Reserve accounting follows <see cref="WeaponReserveMode"/> and a reload completes
    /// even if the weapon is dropped. This class does NOT play magazine sounds (WeaponAnimator
    /// does).</para>
    /// <para>SECOND HOLD PATH — a GRANTED weapon (<see cref="GrantTo"/>): held by telling a
    /// controller directly, with ISDK grabbing never involved. Two kinds
    /// (<see cref="WeaponGrantKind"/>): <b>Disposable</b> (§10.5 <c>weaponSource:"random"</c>) is
    /// held by definition and has reload DISABLED; <b>Persistent</b> (selected from a
    /// <see cref="WeaponFrame"/>) has reload and a reserve. In both cases the instance parks under
    /// <see cref="WeaponGranter"/>'s DDOL root (NEVER as a child of the hand anchor) and its pose
    /// is driven every frame by the canonical grip; a second hand may take the front grip
    /// (<see cref="SecondaryHand"/>).</para>
    /// <para>THE FRONT-GRIP gate and its indicator live in this class — there is no separate
    /// component: <see cref="IsHandOnSecondaryGrip"/> is the single answer to "is this hand's
    /// controller inside the socket", and <see cref="TickSecondaryGripIndicator"/> draws that same
    /// sphere from the catalog prefab.</para></summary>
    public class Weapon : MonoBehaviour
    {
        // Analog trigger hysteresis: jitter around the threshold must not double-count a press.
        private const float TriggerPressThreshold = 0.55f;
        private const float TriggerReleaseThreshold = 0.35f;

        /// <summary>Minimum repeat interval of the blocked-muzzle cue (s).</summary>
        private const float BlockedCueSeconds = 0.4f;

        /// <summary>Blocked-muzzle haptic amplitude — lighter than a shot, it just says "no".</summary>
        private const float BlockedHapticAmplitude = 0.6f;

        private const float BlockedHapticSeconds = 0.06f;

        /// <summary>DEFAULT two-handed recoil multiplier.
        /// <para>The remote replica (<see cref="VortexArena.Core.Player.RemoteAvatar.ApplyShotRecoil"/>)
        /// reads this DEFAULT, not the prefab field: <see cref="twoHandRecoilMultiplier"/> is
        /// serialized per prefab and never travels on the wire. Editing it on a prefab changes only
        /// the shooter's own screen.</para></summary>
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

        // ⚠️ No haptic field here, and none is added back: amplitude/duration are the weapon's own
        // data (WeaponDefinition.HapticAmplitude/HapticDuration). A serialized second copy would
        // make the same weapon feel different as a scene instance and as a granted clone.

        public string WeaponName => definition != null && !string.IsNullOrEmpty(definition.DisplayName)
            ? definition.DisplayName
            : gameObject.name;

        /// <summary>Protocol weapon key — a kill feed label only, never validated (§10.3).</summary>
        public string WeaponId => definition != null ? definition.WeaponId : "";

        /// <summary>Item id on the wire (§6.6); <c>0</c> = no definition or unassigned
        /// <c>netItemId</c>. ⚠️ Do not confuse with <see cref="WeaponId"/>: that is a free-form kill
        /// feed label, this is the network identity (u8) that decides what the remote side draws.</summary>
        public byte NetItemId => definition != null && definition.HasNetItemId ? definition.NetItemId : (byte)0;

        public WeaponDefinition Definition => definition;

        public Transform ModelPivot => modelPivot;

        /// <summary>In <c>weaponSource:"random"</c> modes (§10.5) the weapon is GRANTED straight to
        /// a controller with ISDK grabbing never involved. <c>None</c> = a normal scene weapon.
        /// <para>A separate path because forcing <see cref="Grabbable"/> into a "selected" state
        /// means reaching into ISDK internals — fragile and version-dependent. A granted weapon is
        /// held by definition and never enters the grab system.</para></summary>
        public OVRInput.Controller GrantedHand { get; private set; } = OVRInput.Controller.None;

        /// <summary>Grant KIND (<see cref="WeaponGrantKind"/>): "fixed in hand" and "no reload, no
        /// reserve" are separate rules and no longer share one flag.</summary>
        public WeaponGrantKind GrantKind { get; private set; } = WeaponGrantKind.None;

        public bool IsGranted => GrantedHand != OVRInput.Controller.None;

        /// <summary>Is this the PERSISTENT frame clone (reload on, reserve, front grip allowed).</summary>
        public bool IsPersistentGrant => GrantKind == WeaponGrantKind.Persistent;

        /// <summary>Is this FFA's DISPOSABLE random weapon (no reload, no reserve).</summary>
        public bool IsDisposableGrant => GrantKind == WeaponGrantKind.Disposable;

        /// <summary>Held: a granted weapon is held BY DEFINITION, a scene weapon is tracked from
        /// ISDK pointer events. Without this <c>||</c> a granted weapon could never fire.</summary>
        public bool IsHeld => IsGranted || heldPoints.Count > 0;

        /// <summary>Two-handed steadying: TWO grab points on a scene weapon, main hand + the
        /// granter-written second hand on a granted one (<see cref="SetSecondaryHand"/>).
        /// <para>⚠️ A Disposable weapon MAY be two-handed: not being able to take the front grip on
        /// an FFA rifle felt different from the same rifle taken from a frame. The rule lives in
        /// one place — the granter resolves the second hand regardless of source.</para>
        /// <para>⚠️ The gate is this property, NOT <see cref="SecondaryHand"/>: in sessions where
        /// the controller cannot be resolved (Editor) <c>SecondaryHand</c> is <c>None</c> while two
        /// grab points still exist, and reporting two hands on the wire is correct there (only the
        /// two-handed POSE, which reads <c>SecondaryHand</c>, cannot be solved).</para></summary>
        public bool IsTwoHanded => HasSecondaryGrip;

        private bool HasSecondaryGrip => IsGranted
            ? _grantedSecondaryHand != OVRInput.Controller.None ||
              (IsPersistentGrant && heldPoints.Count > 0)
            : heldPoints.Count > 1;

        /// <summary>Controller of the second hand on the front grip; <c>None</c> = none or
        /// unresolved. Source depends on the weapon kind: on a GRANTED weapon the granter writes it
        /// (the single grab-sensing gate), on a SCENE weapon it is the second ISDK grab point.
        /// <para>⚠️ Returns <c>None</c> if it equals the main hand: with one hand holding from two
        /// sources the two-handed solver would aim along a zero-length axis.</para></summary>
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

        /// <summary>Trigger/main hand: the granted hand, or the FIRST hand that grabbed a scene
        /// weapon. <c>None</c> = not held, or the controller could not be resolved.</summary>
        public OVRInput.Controller MainHand =>
            IsGranted ? GrantedHand
                      : (heldPoints.Count > 0 ? heldPoints[0].ctl : OVRInput.Controller.None);

        /// <summary>Is the main hand the right one — source of the §6.4 event flag and §6.6
        /// <c>FLAG_PRIMARY_RIGHT</c>.
        /// <para>⚠️ An unresolved hand counts as RIGHT: the wire has one bit, no "unknown hand"
        /// value. A tracer on the wrong hand beats a tracer that is never drawn.</para></summary>
        public bool IsMainHandRight => MainHand != OVRInput.Controller.LTouch;

        public int CurrentAmmo { get; private set; }
        public int MagazineSize => definition != null ? definition.MagazineSize : 0;

        public int ReserveRounds => reserveRounds;

        public int SpareMagazineCount => reserveRounds / Mathf.Max(1, MagazineSize);

        public bool IsReloading { get; private set; }

        /// <summary>Is ANY part of the weapon touching an inner obstacle (§10.9) — the trigger is
        /// dead while true. Measured only while the trigger is pressed; <c>false</c> when idle.</summary>
        public bool IsWeaponBlocked { get; private set; }

        /// <summary>Current spread half-angle (base + bloom, degrees). Raw: the two-hand multiplier
        /// is NOT included.</summary>
        public float CurrentSpreadDegrees => definition != null ? definition.BaseSpreadDegrees + currentBloom : 0f;

        public event Action Fired;

        public event Action DryFired;

        public event Action<float> ReloadStarted;

        /// <summary>Reload finished (also raised when <see cref="RefillFull"/> cancels it).</summary>
        public event Action ReloadCompleted;

        public event Action AmmoChanged;

        public event Action<bool> HeldChanged;

        /// <summary>Active weapons in the scene, for listeners with no direct reference (e.g.
        /// HandGripPoser). Updated in OnEnable/OnDisable; the order is meaningless.</summary>
        public static readonly List<Weapon> Active = new List<Weapon>();

        public static event Action ActiveChanged;

        // HeldItems collector state (§6.6): the hook is installed once and warnings fire once —
        // a broken setup would otherwise log on every grab.
        private static bool heldItemsHooked;
        private static bool missingNetItemIdWarned;
        private static bool handConflictWarned;

        // Holding hands are ORDERED: the FIRST entry is the trigger/main hand. id =
        // PointerEvent.Identifier (Unselect/Cancel match key), ctl = resolved OVR controller
        // (None = unresolved → the trigger is read from the Input System fallback).
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

        // Second hand on the front grip of a GRANTED weapon; only WeaponGranter writes it.
        // ⚠️ The second hand does not CARRY the weapon, it only aims it (ItemGripSolver), and the
        // input is that hand's POSITION alone — no controller or wrist rotation reaches the weapon.
        private OVRInput.Controller _grantedSecondaryHand = OVRInput.Controller.None;

        // Two-handed solve weight and the LAST known second-hand palm position. The position is
        // kept because closing the solve the instant the hand releases would make the weapon jump:
        // while the weight falls to zero the pose still solves from that last point.
        private float _aimBlend;
        private Vector3 _lastSecondaryPalm;
        private bool _hasLastSecondaryPalm;

        // tracerEveryNthRound counter, kept PER WEAPON (RemoteShotFx keeps it per player): a shared
        // counter would scatter trails randomly across dual-wielded guns.
        private int shotCount;

        // Pellet endpoints (world space), allocated once per weapon and NOT per shot: the trigger
        // path runs ~10/s on full auto and a GC spike is visible on Quest. Length comes from
        // PelletCount, clamped to ShotTracer's cap.
        private Vector3[] tracerPoints;

        private bool weaponIdWarned;

        protected virtual void Awake()
        {
            // ⚠️ Grip-authoring hand models are NEVER drawn in game (ItemHandRig); the bake
            // disables them and this is only a safety net. One left enabled would show as a hand
            // floating in the arena.
            ItemHandRig.HideAll(transform);

            if (definition == null)
            {
                // The definition is MANDATORY: the SO is the single source of balance numbers
                // (§10.3). The fire lock is additionally in the canFire condition.
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

            // Hook the collector BEFORE adding to the list, so the ActiveChanged below counts this
            // weapon too (otherwise the first weapon would never reach HeldItems).
            EnsureHeldItemsHook();

            Active.Add(this);
            ActiveChanged?.Invoke();
        }

        protected virtual void OnDisable()
        {
            if (grabbable != null)
                grabbable.WhenPointerEventRaised -= HandlePointerEvent;

            // ISDK's Cancel events no longer reach us; clear the hand list here.
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

            // The pulse may have been cut mid-way: stop vibration on both controllers.
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
        }

        protected virtual void Update()
        {
            // PlayerCombatState bootstraps after scene load, so it may not exist during OnEnable.
            // One-shot lazy subscription.
            if (!aliveSubscribed)
                TrySubscribeAlive();

            // A reload completes even if the weapon was dropped (the duration is not physical).
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

        /// <summary>Driven AFTER hand tracking updates: outside LateUpdate the weapon lags one
        /// frame and aiming feels mushy.</summary>
        protected virtual void LateUpdate()
        {
            ApplyCanonicalGrip();
            TickSecondaryGripIndicator();
        }

        protected virtual void OnDestroy()
        {
            // The indicator's material INSTANCE (renderer.material) dies with the weapon; leaving
            // it would leak until the next scene change.
            if (indicatorMaterial != null)
            {
                Destroy(indicatorMaterial);
                indicatorMaterial = null;
            }
        }

        // --------------------------------------------------------- canonical grip

        /// <summary>§6.6 CANONICAL GRIP: while held, the root is driven from the main hand anchor
        /// plus the definition's FIXED grip position — the arbitrary offset at grab time is not
        /// kept.
        /// <para>⚠️ HAND ROTATION NEVER ENTERS (<see cref="ItemGripSolver"/>): the record carries
        /// position only, so one-handed the weapon is ALWAYS aligned with the main controller. The
        /// second hand aims by POSITION alone; its rotation is never read.</para>
        /// <para>Mandatory because pose does not travel: the remote side draws the weapon as "hand
        /// pose × fixed grip offset", and a free grab breaks that equality.</para>
        /// <para>BOTH granted kinds are driven here (Disposable included): since the two-handed
        /// solve recomputes the world pose every frame, even a Disposable instance lives under
        /// <see cref="WeaponGranter"/>'s DDOL root, never as an anchor child. ⚠️ The two facts are
        /// linked: writing a world pose to an instance under the anchor collides with the parent
        /// transform and the weapon drifts away compounding.</para>
        /// <para>Two-handed aim (<see cref="SecondaryHand"/>): the main grip point stays in the
        /// main palm while the weapon's axis turns toward the second palm — the maths lives in
        /// <see cref="ItemGripSolver"/> and the remote side runs the SAME function (§6.6).</para>
        /// <para>With no rig (admin spectator, Editor session, scene not loaded) nothing happens
        /// and the weapon stays where it is.</para>
        /// <para>⚠️ Recoil goes to <see cref="ModelPivot"/> and never interferes here: the hand
        /// drives the root, the visual kick lives on the child. Physics does not race either —
        /// Grabbable keeps the Rigidbody kinematic while selected.</para></summary>
        private void ApplyCanonicalGrip()
        {
            if (definition == null)
            {
                return;
            }

            if (!IsGranted && heldPoints.Count == 0)
            {
                // A released weapon does not carry its aim weight: it restarts one-handed.
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
                // ⚠️ POSITION only: aim comes from where the hand reaches, not how it is turned.
                _lastSecondaryPalm = secondaryPalm.position;
                _hasLastSecondaryPalm = true;
            }

            _aimBlend = ItemGripSolver.StepAimBlend(_aimBlend, wantTwoHand, Time.deltaTime);
            if (_aimBlend <= 0f)
            {
                _hasLastSecondaryPalm = false;
            }

            // The record is in ANCHOR space (ItemGripPose): the position is read straight from the
            // definition, no delta. An unauthored record is zero → the weapon sits on the
            // controller, aligned with it.
            bool mainHandRight = HandGripPivot.IsRight(MainHand);
            bool secondaryRight = SecondaryHandIsRight(mainHandRight);

            ItemGripSolver.Solve(definition, mainHandRight, secondaryRight, primaryPalm,
                _hasLastSecondaryPalm, _lastSecondaryPalm, _aimBlend,
                out Vector3 position, out Quaternion rotation);

            transform.SetPositionAndRotation(position, rotation);
        }

        /// <summary>Is the front-grip hand the right one; with no second hand yet, the OPPOSITE of
        /// the main hand.
        /// <para>⚠️ <c>HandGripPivot.IsRight(None)</c> answers "right" (a fair assumption when a
        /// controller cannot be resolved), but here None means "no second hand": calling it directly
        /// would treat both hands as right and read the front-grip axis from the RIGHT record.</para>
        /// </summary>
        private bool SecondaryHandIsRight(bool mainHandRight)
        {
            OVRInput.Controller secondary = SecondaryHand;
            return secondary == OVRInput.Controller.None
                ? !mainHandRight
                : HandGripPivot.IsRight(secondary);
        }

        // ----------------------------------------------------------------- trigger

        private void TickTrigger()
        {
            bool pressed;
            bool pressedThisFrame;

            // The distinction is mandatory: 'Player/Attack' is a single Button action bound to
            // BOTH controllers, so with a weapon in each hand one trigger would fire both.
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
                // Editor fallback: no controller resolved, read the Input System action.
                pressed = attackAction != null && attackAction.IsPressed();
                pressedThisFrame = attackAction != null && attackAction.WasPressedThisFrame();
                triggerHeld = pressed;
            }

            // Fire permission comes from server state: while dead or in loading/countdown/end
            // phases the trigger does nothing at all (not even a dry-fire sound).
            bool combatAllows = ArenaCombat.CanFire;

            // §10.9 shoot-through-cover gate. ⚠️ Polled ONLY while the trigger is pressed: the
            // answer is needed at fire time, and three physics queries per frame per idle weapon
            // would be paid for nothing.
            IsWeaponBlocked = pressed && muzzle != null &&
                              ArenaCombat.IsWeaponBlocked(modelPivot, ResolveBodyBounds(),
                                  muzzle.position, muzzle.forward);

            // Two gates, one cue. The player's own body gate is enforced in ArenaCombat.CanFire,
            // not here (it is not a weapon question); it is read here only so the cue knows why
            // nothing happened.
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
                // The weapon or the player touches an obstacle: no round leaves. ⚠️ Fire() is not
                // called, so no ammo is spent, no flash/sound plays, no shot_event goes out and
                // nextFireTime does not advance — the player cannot burn a magazine into a wall.
                CueBlockedFire();
            }
            else if (pressedThisFrame && combatAllows && !canFire && !IsReloading && CurrentAmmo == 0)
            {
                // Empty magazine: dry fire. Auto-reload is deliberately absent — reloading starts
                // from a deliberate player gesture (TryStartReload).
                weaponAudio?.PlayDry();
                DryFired?.Invoke();
            }
        }

        /// <summary>Blocked-muzzle cue: dry-fire sound + a short pulse.
        /// <para>⚠️ Rate limited (<see cref="BlockedCueSeconds"/>): the trigger may be held, and a
        /// click per frame is both inaudible and channel-flooding.</para>
        /// <para><see cref="DryFired"/> is NOT raised: that event means "magazine empty" and drives
        /// the HUD's reload prompt, while a blocked muzzle is a temporary state of a loaded gun.</para>
        /// </summary>
        private void CueBlockedFire()
        {
            if (Time.unscaledTime < nextBlockedCueTime)
            {
                return;
            }

            nextBlockedCueTime = Time.unscaledTime + BlockedCueSeconds;

            weaponAudio?.PlayDry();

            // Same path as the shot pulse: one coroutine, cleaned up in OnDisable, so no vibration
            // is left hanging on the controller.
            if (hapticRoutine != null)
            {
                StopCoroutine(hapticRoutine);
            }

            hapticRoutine = StartCoroutine(HapticPulse(BlockedHapticAmplitude, BlockedHapticSeconds));
        }

        /// <summary>Weapon body bounds in <see cref="modelPivot"/> space — input to the obstacle
        /// box test. Computed ONCE: the meshes never change and 8 corner transforms per renderer is
        /// not work for every trigger frame.
        /// <para>⚠️ Built from <see cref="Mesh.bounds"/> (LOCAL), NOT <c>Renderer.bounds</c> (world
        /// AABB): converting a world box back to local nearly doubles the volume of a diagonally
        /// held rifle and would cut legitimate shots beside cover.</para>
        /// <para>⚠️ The scan starts at <see cref="modelPivot"/>, not the weapon root: the root also
        /// carries visuals that are not the weapon, such as the grab frame.</para></summary>
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
            // Spread uses the PRE-shot bloom; bloom grows with the shot.
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

            // A shotgun trigger pull casts several rays (§10.3 expects this: there is no rate
            // check precisely so pellet patterns are not dropped). A normal weapon runs one
            // iteration — there is NO separate code path.
            int pellets = definition.PelletCount;

            // The tracer gate is asked ONCE, before the spread: the counter counts TRIGGER PULLS,
            // not pellets. Per pellet, `tracerEveryNthRound` would thin the fan itself instead of
            // meaning "every Nth SHOT leaves a trail".
            bool drawTracer = ShouldDrawTracer();
            int tracerCount = 0;

            for (int p = 0; p < pellets; p++)
            {
                Vector2 scatter = Random.insideUnitCircle * spread;
                Vector3 direction = Quaternion.AngleAxis(scatter.x, muzzle.up) *
                                    Quaternion.AngleAxis(scatter.y, muzzle.right) *
                                    muzzle.forward;

                // ⚠️ The ray is not cast here by hand: the obstacle rule lives behind one gate, and
                // any damage source writing its own Physics.Raycast loses it.
                ArenaCombat.ShotTrace trace = ArenaCombat.TraceShot(muzzle.position, direction, definition.Range);

                if (p == 0)
                {
                    // Network SHOT (§6.4, UDP event channel): EXACTLY ONCE per Fire(), hit or miss.
                    // The remote side plays the flash/sound and draws the tracer from it, so the
                    // distance is where the ray REALLY went: to the hit, to the muzzle if an
                    // obstacle swallowed it, else to max range.
                    // ⚠️ Independent of the hit_report below — one is presentation, the other
                    // authoritative state, on separate channels.
                    // ⚠️ Also ONCE for a shotgun (first pellet's direction/distance): per-pellet
                    // events would announce the same shot 9 times and fill the per-packet limit
                    // (§6.4) on a single trigger, while one remote muzzle flash is already correct.
                    ArenaCombat.ReportShot(direction, trace.Distance, NetItemId, IsMainHandRight);
                }

                // Local trail endpoint. ⚠️ RemoteShotFx CANNOT draw the shooter's own trail: the
                // server does not echo the event back and the client filters its own playerId
                // (§6.5). This is the only place the shooter's trail is drawn; skipping it gives the
                // hard-to-diagnose "everyone sees it except the shooter".
                // ⚠️ Endpoints are collected here and drawn in ONE call after the spread: locally
                // each pellet is drawn to where ITS ray really went, since scatter and distance are
                // already known and need no regeneration.
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

                // The zone multiplier is applied HERE: damage is client-authoritative and the
                // server applies hit_report.damage verbatim (§10.3).
                // ⚠️ Damage is PER PELLET and never divided: WeaponDefinition.Damage is already one
                // pellet's damage (CS2 model); total damage comes from how many pellets connect.
                float damage = definition.Damage * definition.GetZoneMultiplier(ArenaCombat.GetHitZone(hit.collider));

                // Damage is NEVER applied locally: health is server-authoritative and comes back
                // as health_update. A non-networked target (decor, wall) does nothing beyond the
                // impact FX above.
                // ⚠️ Pellets hitting the same target are NOT merged into one report: the server
                // processes each separately and the zone multiplier differs per pellet.
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

        /// <summary>The shooter's OWN trail — line plus smoke (<see cref="RemoteShotFx"/> draws
        /// remote ones). Smoke is not a separate call; it lives inside the draw call and cannot be
        /// forgotten.
        /// <para>Frequency and look come from the SAME source as the remote trail
        /// (<see cref="ItemDefinition"/>), or the weapon would look different on each screen. The
        /// pool is shared too (<see cref="ShotTracer.Shared"/>).</para>
        /// <para>⚠️ Every pellet gets its own trail and all of them go in ONE call
        /// (<see cref="ShotTracer.Play"/>): smoke budget and line width are scaled per
        /// volley.</para></summary>
        /// <param name="pointCount">Filled element count of <see cref="tracerPoints"/>.</param>
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

        /// <summary>Will this trigger pull leave a trail — advances the counter and applies the
        /// <c>tracerEveryNthRound</c> gate, preparing the endpoint buffer if it passes.
        /// <para>⚠️ The counter counts TRIGGER PULLS, not pellets: the setting means "every Nth shot
        /// leaves a trail", not "every Nth pellet of a spread is drawn".</para></summary>
        private bool ShouldDrawTracer()
        {
            shotCount++;

            int everyNth = definition.TracerEveryNthRound;
            if (everyNth < 1 || shotCount % everyNth != 0)
                return false;

            // Allocated on the first trail and kept for the weapon's life. Length from
            // PelletCount; the cap belongs to ShotTracer (callers do not carry their own).
            int capacity = Mathf.Clamp(definition.PelletCount, 1, ShotTracer.MaxScatterLines);
            if (tracerPoints == null || tracerPoints.Length != capacity)
                tracerPoints = new Vector3[capacity];

            return true;
        }

        // --------------------------------------------------------------- granting

        /// <summary>GRANTS the weapon to a controller (called by <see cref="WeaponGranter"/>): it
        /// counts as held and ISDK grabbing is never involved.
        /// <para>Ammo behaviour splits by kind (<see cref="WeaponGrantKind"/>). <c>Disposable</c>:
        /// starts with a full magazine, NO reserve (a spare counter would only lie to the HUD while
        /// reload is off) and any in-progress reload is cancelled — every call is a new weapon.
        /// <c>Persistent</c>: ammo is left UNTOUCHED and a running reload is not cancelled. The
        /// frame weapon is the ONE instance that hides and returns, which is exactly where "the
        /// same weapon comes back with the same ammo" comes from; refilling would open a
        /// release-and-hold infinite-ammo exploit.</para></summary>
        public void GrantTo(OVRInput.Controller hand, WeaponGrantKind kind)
        {
            bool wasHeld = IsHeld;
            GrantedHand = hand;
            GrantKind = kind;
            triggerHeld = false;

            // The second hand does not carry into a new hold: the weapon may have swapped hands
            // and the granter re-resolves the front grip next frame.
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

        /// <summary>Revokes the grant (the weapon is no longer in hand); called by
        /// <see cref="WeaponGranter"/> while stowing a frame clone.
        /// <para><c>SetActive(false)</c> already triggers <see cref="OnDisable"/> and the same
        /// cleanup, but the explicit API exists because "take the weapon away" should be a call, not
        /// a side effect — code relying on <c>OnDisable</c> ordering is fragile.</para></summary>
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

        /// <summary>Reports the hand on the front grip of a GRANTED weapon (<c>None</c> = released).
        /// <para>⚠️ <see cref="WeaponGranter"/> is the ONLY caller, and this is a silent no-op on a
        /// scene weapon, where the second hand comes from ISDK grab events. On a granted weapon the
        /// granter's grip poll is the single source of grab sensing; both paths open would make one
        /// hand "hold" from two sources.</para>
        /// <para><see cref="ActiveChanged"/> is raised only on CHANGE: it refreshes the
        /// <c>HeldItems</c> collector (§6.6 <c>GRIP_LINKED</c>), and unconditional raising would
        /// mean a full scan on every frame grip is held.</para></summary>
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

        /// <summary>Tries to start a reload; true if it started. Rejected for: a Disposable granted
        /// weapon (§10.5; a frame weapon HAS reload, driven by the below-the-belt gesture), already
        /// reloading, no definition, full magazine, dead player, insufficient reserve (Discard: no
        /// full magazine; Pool: empty). In Discard mode the magazine is dropped up front: the
        /// trigger is dead for the duration and remaining rounds are BURNED. No sound is played —
        /// WeaponAnimator owns the magazine audio timeline.</summary>
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

                // The new magazine is deducted NOW; rounds in the old one count as thrown away
                // with it (the default product rule).
                reserveRounds -= definition.MagazineSize;
                CurrentAmmo = 0;
            }
            else if (reserveRounds <= 0)
            {
                return false;
            }
            // Pool mode keeps the rounds in the magazine (CS2 rule) and deducts on completion.

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
                    // The new magazine was already deducted when the reload started.
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

        /// <summary>Restores magazine and reserve to the definition's full values (revive refill).
        /// An in-progress reload is cancelled and <see cref="ReloadCompleted"/> is raised so
        /// listeners close. A Disposable grant keeps reserve 0 (no reload in that mode); a frame
        /// weapon returns with a full reserve.</summary>
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

        // ------------------------------------------------------------ hand tracking

        private void HandlePointerEvent(PointerEvent evt)
        {
            switch (evt.Type)
            {
                case PointerEventType.Select:
                    AddHeldPoint(evt);
                    break;
                case PointerEventType.Unselect:
                case PointerEventType.Cancel:
                    // Cancel may come from a hovering interactor too; no-op if not in the list.
                    RemoveHeldPoint(evt.Identifier);
                    break;
            }
        }

        private void AddHeldPoint(in PointerEvent evt)
        {
            for (int i = 0; i < heldPoints.Count; i++)
            {
                if (heldPoints[i].id == evt.Identifier)
                    return; // double Select from one interactor (theoretical) — no duplicate
            }

            bool wasHeld = heldPoints.Count > 0;
            heldPoints.Add((evt.Identifier, WeaponGranter.ResolveController(evt)));

            if (!wasHeld)
            {
                HeldChanged?.Invoke(true);
                weaponAudio?.PlayPickup();
            }

            // ⚠️ ActiveChanged is raised UNCONDITIONALLY (not only on the 0→1 transition): a second
            // hand joining the grip is a wire change (§6.6 GRIP_LINKED) and the HeldItems collector
            // depends on this event.
            ActiveChanged?.Invoke();
        }

        private void RemoveHeldPoint(int identifier)
        {
            for (int i = 0; i < heldPoints.Count; i++)
            {
                if (heldPoints[i].id != identifier)
                    continue;

                heldPoints.RemoveAt(i);

                // Main hand changed (or the weapon was released): reset the trigger so the new
                // main hand's held trigger reads as a fresh press next frame.
                if (i == 0)
                    triggerHeld = false;

                if (heldPoints.Count == 0)
                {
                    HeldChanged?.Invoke(false);
                }

                // Unconditional — rationale in AddHeldPoint.
                ActiveChanged?.Invoke();
                return;
            }
        }

        // ⚠️ Hand resolution does NOT live here: the only place an OVR controller is extracted from
        // an interactor is WeaponGranter.ResolveController / ResolveControllerFromGameObject. It has
        // two consumers (this class, WeaponFrame) and copies drift.

        // ------------------------------------------------- front grip: gate + indicator

        /// <summary>Distance at which the front-grip socket becomes VISIBLE (m, controller anchor
        /// to socket centre) — a playtest value, identical on all weapons.
        /// <para>⚠️ The acceptance radius is per weapon
        /// (<see cref="ItemDefinition.SecondaryGripRadius"/>) and MAY exceed this constant, so the
        /// effective visibility distance is <c>Mathf.Max(SecondaryGripHoverRadius, radius)</c> — a
        /// larger radius would invert "visible first, grabbable second" and make the socket
        /// useless.</para></summary>
        private const float SecondaryGripHoverRadius = 0.30f;

        /// <summary>Socket alpha while approaching, and while the controller is inside (slightly
        /// more solid to read as "you are in, press"; colour and size never change — the sphere IS
        /// the acceptance volume).</summary>
        private const float IndicatorHoverAlpha = 0.30f;
        private const float IndicatorReadyAlpha = 0.50f;

        private static readonly Color IndicatorColor = new Color(0.55f, 0.82f, 1f, 1f);

        /// <summary>One warning per SESSION (not per weapon) when the socket prefab is missing.</summary>
        private static bool indicatorPrefabWarned;

        /// <summary>Definitions with no front-grip record — one warning per session each (weapons
        /// are re-cloned on every grab, so a per-instance warning would spam).</summary>
        private static readonly HashSet<ItemDefinition> unauthoredSecondaryWarned = new HashSet<ItemDefinition>();

        // This weapon's socket instance (lazy: born only when a hand approaches) and the surface
        // whose alpha is driven: a material on a sphere prefab, the line colour on LineRenderer art.
        private Transform indicator;
        private LineRenderer indicatorLine;
        private Material indicatorMaterial;

        /// <summary>WORLD position of the front grip, from the <paramref name="rightHand"/> record
        /// (records are per hand: the grip is not symmetric, so each controller lands somewhere
        /// different). The record is the controller ANCHOR's pose relative to the item, so this is
        /// where that hand's anchor will sit; the socket sphere is centred here.
        /// <para>⚠️ Composed by hand, NOT via <see cref="Transform.TransformPoint"/>: the record is
        /// in metres and <c>WPN_*</c> roots are 0.8-scaled, so TransformPoint would apply the scale
        /// twice (<see cref="ItemGripPose"/>). The gate, the socket and <c>RemoteAvatar</c> all use
        /// this same composition.</para>
        /// <para>⚠️ Meaningful only while <see cref="ItemDefinition.HasSecondaryGrip"/>; with an
        /// unauthored record this point is the item root.</para></summary>
        public Vector3 SecondaryGripWorld(bool rightHand)
        {
            return definition == null
                ? transform.position
                : transform.position + transform.rotation * definition.SecondaryGripPosition(rightHand);
        }

        /// <summary>Is this hand's controller ANCHOR inside the front-grip socket — the ESTABLISH
        /// gate for the second hand.
        /// <para>One rule, two readers: <see cref="WeaponGranter"/> binds the second hand from it
        /// (grip pressed + this <c>true</c>) and the socket switches to its "inside" alpha from it.
        /// Two separate measurements would let a grab be refused where the player is told "you are
        /// in".</para>
        /// <para>⚠️ The measured point is the controller ANCHOR (<see cref="TryResolveAnchor"/>) —
        /// the SAME frame as the grip record (<see cref="ItemGripPose"/> is in anchor space).
        /// Measuring the wrist would judge from centimetres away even with the controller exactly
        /// where it was authored, and the player would feel "my hand is right there but it will not
        /// hold".</para>
        /// <para>⚠️ Establish gate only; MAINTAINING the link ignores distance (rationale in
        /// <c>WeaponGranter.ResolveSecondaryHand</c>).</para>
        /// <para>⚠️ With no authored front-grip record the gate is CLOSED
        /// (<see cref="ItemDefinition.HasSecondaryGrip"/>): an unauthored record falls to the item
        /// root, which on most weapons sits next to the main hand, so an open gate would "bind" the
        /// second hand on top of the main one. <see cref="TickSecondaryGripIndicator"/> logs the
        /// warning.</para></summary>
        public bool IsHandOnSecondaryGrip(OVRInput.Controller hand)
        {
            if (definition == null || !definition.HasSecondaryGrip)
            {
                return false;
            }

            if (!TryResolveAnchor(hand, out Vector3 anchor))
            {
                return false;
            }

            float radius = definition.SecondaryGripRadius;
            Vector3 socket = SecondaryGripWorld(HandGripPivot.IsRight(hand));
            return (anchor - socket).sqrMagnitude <= radius * radius;
        }

        /// <summary>World position of the hand's controller anchor
        /// (<see cref="WeaponGranter.ResolveHandAnchor"/>, the only rig discovery path);
        /// <c>false</c> if the rig or hand cannot be resolved. Same frame as the grip record, or the
        /// socket would say "outside" with the controller exactly where it was authored.</summary>
        private static bool TryResolveAnchor(OVRInput.Controller hand, out Vector3 position)
        {
            Transform anchor = WeaponGranter.ResolveHandAnchor(hand);
            if (anchor == null)
            {
                position = default;
                return false;
            }

            position = anchor.position;
            return true;
        }

        /// <summary>One frame of the front-grip socket: while the weapon is held, two-handed and
        /// the second hand is not bound yet, the sphere appears as the FREE hand's controller
        /// approaches, gets slightly more solid once the anchor is INSIDE ("press"), and disappears
        /// once bound.
        /// <para>The sphere IS the acceptance volume: the prefab is designed at 1 m diameter and
        /// scaled here to twice the acceptance radius, so what the player sees and what
        /// <see cref="IsHandOnSecondaryGrip"/> judges are the same thing. Separate numbers would
        /// produce "I am inside but it will not hold".</para>
        /// <para>The socket is for the FRONT grip only. The main grip has none: the weapon is born
        /// in the main hand or selected from a frame, so the player never has to move a hand
        /// there.</para>
        /// <para>The art is a prefab (<see cref="WeaponCatalog.SecondaryGripIndicatorPrefab"/>,
        /// shared by all weapons); this class only drives its position, scale and alpha. Without
        /// the prefab nothing is drawn and one warning is logged — the gate still works.</para>
        /// <para>⚠️ Never drawn on remote avatars, and nothing is needed for that:
        /// <c>RemoteAvatar.SterilizeVisual</c> strips every MonoBehaviour (this class included)
        /// from the copy.</para>
        /// <para>⚠️ Called in <see cref="LateUpdate"/> AFTER <see cref="ApplyCanonicalGrip"/>:
        /// measuring before this frame's pose is written lags one frame and the indicator looks
        /// detached during fast movement.</para></summary>
        private void TickSecondaryGripIndicator()
        {
            if (definition == null || !definition.IsTwoHanded || !IsHeld ||
                SecondaryHand != OVRInput.Controller.None)
            {
                HideIndicator();
                return;
            }

            if (!definition.HasSecondaryGrip)
            {
                // Unauthored front grip = NO front grip: the socket would be drawn at the item
                // root, i.e. next to the main hand. Not silent — this is a content error whose only
                // fix is the grip studio.
                HideIndicator();
                if (unauthoredSecondaryWarned.Add(definition))
                {
                    Debug.LogWarning($"[Weapon] '{definition.name}' iki elli ama ÖN KABZA KAYDI YAZILMAMIŞ — " +
                                     "soket çizilmez, ikinci el bağlanmaz. Kavrama Pozu Stüdyosu'nda " +
                                     "(WPN prefabı prefab kipinde) 'Ön Kabza Ellerini Oluştur' → yerleştir → " +
                                     "Kaydet.", definition);
                }

                return;
            }

            OVRInput.Controller free = FreeHand();
            if (free == OVRInput.Controller.None || !TryResolveAnchor(free, out Vector3 anchor))
            {
                // No rig (admin spectator, Editor session) or unresolved main hand: no hand to show.
                HideIndicator();
                return;
            }

            Vector3 socket = SecondaryGripWorld(HandGripPivot.IsRight(free));
            float distance = Vector3.Distance(anchor, socket);
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

            bool inside = distance <= radius;

            indicator.gameObject.SetActive(true);
            // Rotation follows the weapon: irrelevant for a sphere, meaningful for authored art.
            indicator.SetPositionAndRotation(socket, transform.rotation);

            // ⚠️ The scale is a WORLD measurement and IS the acceptance sphere: the prefab ships at
            // 1 m diameter, so diameter = 2 × radius. The parent scale is undone because weapons are
            // 0.8-scaled; a raw local scale would size the sphere differently per weapon.
            float parentScale = Mathf.Max(1e-4f, transform.lossyScale.x);
            indicator.localScale = Vector3.one * (2f * radius / parentScale);

            Color color = IndicatorColor;
            color.a = inside ? IndicatorReadyAlpha : IndicatorHoverAlpha;

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

        private OVRInput.Controller FreeHand()
        {
            switch (MainHand)
            {
                case OVRInput.Controller.LTouch: return OVRInput.Controller.RTouch;
                case OVRInput.Controller.RTouch: return OVRInput.Controller.LTouch;
                default: return OVRInput.Controller.None;
            }
        }

        /// <summary>Instantiates the socket from the catalog prefab (once, under the weapon).
        /// Physics is stripped: a leftover collider would catch both the shot ray and grabbing, so
        /// the thing meant to help the player would ruin their aim.
        /// <para>Alpha surface: the first Renderer's material INSTANCE, or a LineRenderer's colour
        /// when there is no Renderer.</para></summary>
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
                                     "Silah kiti koşusu (Tools > VortexArena > Build > Configure All " +
                                     "Build Elements) göstergeyi üretip bağlar.");
                }

                return false;
            }

            GameObject instance = Instantiate(prefab, transform);
            instance.name = "[GripSocket]";

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
                // The material instance is taken ONCE here (.material allocates a new one per
                // call). With no colour property this is skipped silently.
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

        // ------------------------------------------------------------ revive refill

        private void TrySubscribeAlive()
        {
            if (aliveSubscribed || PlayerCombatState.Instance == null)
                return;

            PlayerCombatState.Instance.AliveChanged += HandleAliveChanged;
            aliveSubscribed = true;
        }

        private void HandleAliveChanged(bool alive)
        {
            // A revived player starts with full ammo — only the weapon in hand; scene weapons wait
            // for their owners.
            if (alive && IsHeld)
                RefillFull();
        }

        // ------------------------------------------------------------------ network

        /// <summary>§6.6: the ONE place that reports what the LOCAL player holds to
        /// <see cref="HeldItems"/>.
        /// <para>Static and central because <c>HeldItems</c> describes the WHOLE player (two slots
        /// + grip bits), not a single weapon — dual wielding is legitimate. Per-weapon reporting
        /// would let the second weapon overwrite the first's slot, leaving the result dependent on
        /// arbitrary ordering.</para>
        /// <para>⚠️ Runs on <see cref="ActiveChanged"/>, NOT every frame: what is in hand changes
        /// at human speed and scanning <see cref="Active"/> per frame buys
        /// nothing.</para></summary>
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
                    // An id-less weapon is never drawn remotely; staying silent would show as an
                    // undiagnosable "empty hands" in the field.
                    WarnMissingNetItemId(weapon);
                    continue;
                }

                weapon.GetHeldHands(out bool wantsLeft, out bool wantsRight);

                if (wantsLeft && wantsRight)
                {
                    // Two-handed writes the SAME id into BOTH slots + GRIP_LINKED: "same id in two
                    // slots" alone does not mean two-handed (dual pistols); only the flag does.
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

        /// <summary>Which hand(s) hold this weapon: main hand + optional front-grip hand when
        /// granted, otherwise from <see cref="heldPoints"/>.
        /// <para>⚠️ Both granted kinds take the same branch: a second hand on the front grip must
        /// produce <c>GRIP_LINKED</c> on the wire or the remote side draws a one-handed hold. The
        /// rule applies to Disposable too (see <see cref="IsTwoHanded"/>).</para>
        /// <para>⚠️ An unresolved hand (<c>None</c>) counts as RIGHT — the wire has no "unknown"
        /// value. But with two grab points BOTH are marked even when unresolved, otherwise an
        /// Editor session would report a two-handed hold as one-handed.</para></summary>
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

                // With a second hand on the front grip, mark both (even unresolved): the
                // "None → right" rule above could otherwise collide the two hands and report a
                // two-handed hold as one-handed.
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

        /// <summary>Hooks the collector to <see cref="ActiveChanged"/> ONCE (static event, static
        /// listener: the hook survives scene changes and a double hook would double the refresh).</summary>
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
                             "arası kararlı bir kimlik ver; katalog Tools > VortexArena > Build > " +
                             "Configure All Build Elements eşitlemesinde tazelenir.",
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

        // ---------------------------------------------------------------- haptics

        /// <summary>Starts the shot pulse. Amplitude and duration come from the weapon's definition
        /// — there is no hard-coded pulse; every weapon's feel lives in its own SO.
        /// <para>The values are re-read on every shot and passed as parameters, so a pulse already
        /// running finishes with its own amplitude if the definition changes mid-way.</para>
        /// <para>⚠️ Zero amplitude or zero duration means "haptics off" and no pulse starts: a
        /// zero-length coroutine squeezes on and off into the same frame and can leave a vibration
        /// hanging on the controller.</para></summary>
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
                // Haptics off; a cut pulse may remain — stop the vibration.
                SetHeldVibration(0f, 0f);
                return;
            }

            hapticRoutine = StartCoroutine(HapticPulse(amplitude, duration));
        }

        private IEnumerator HapticPulse(float amplitude, float duration)
        {
            // Only the hand(s) actually holding vibrate; None (unresolved) is skipped.
            SetHeldVibration(1f, amplitude);
            yield return new WaitForSeconds(duration);
            // A hand released mid-pulse stops on the OVR side; permanent cleanup is in OnDisable.
            SetHeldVibration(0f, 0f);
            hapticRoutine = null;
        }

        private void SetHeldVibration(float frequency, float amplitude)
        {
            if (IsGranted)
            {
                OVRInput.SetControllerVibration(frequency, amplitude, GrantedHand);

                // The front-grip hand vibrates too: a one-hand-only pulse would feel like half a
                // two-handed hold.
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
