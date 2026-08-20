using System.Collections.Generic;
using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Writes the hand pose onto ISDK's <b>synthetic hand</b> — the hand the player sees in the
    /// headset (<c>OVRHandVisualLeft/Right</c> is driven from it).
    /// <para>
    /// <b>The wrist is locked in EVERY case</b> — neither from tracking nor from Meta's controller-
    /// synthesized "natural" hand pose:
    /// <list type="bullet">
    /// <item><b>Empty hand:</b> wrist locked to the <b>CONTROLLER</b>
    /// (<see cref="ItemGripAuthority.WristFromAnchor"/> — anchor + offset); the hand behaves as a
    /// rigid part of the controller.</item>
    /// <item><b>Hand holding an item</b> (primary or foregrip): wrist locked to the <b>ITEM</b>
    /// (record anchor + offset) — the hand sticks to the weapon and turns with it.</item>
    /// </list>
    /// ⚠️ <b>The main hand is derived from the ITEM too, deliberately:</b> in a two-handed hold the
    /// weapon's rotation is not the main controller's (it aims at the foregrip hand), so a
    /// controller-locked main hand would stay put and end up outside the weapon when the player turns
    /// it by the foregrip. Changes nothing in a one-handed hold: by the solver's identity the
    /// item-derived anchor POSITION is the controller anchor itself, only rotation comes from the
    /// item.
    /// The offset is <b>that slot's own hand placement</b> when the grip is authored (written in the
    /// studio by moving and rotating the <c>Hand</c> model: some weapons are held from the side,
    /// some from below), otherwise the shared definition. The weapon's placement relative to the
    /// controller is NOT affected by it.
    /// Fingers are in the idle pose on an empty hand
    /// (<see cref="HandPoseLibrary.IdleJointRotations"/>) and in the slot's rigged pose when holding.
    /// </para>
    /// <para>
    /// ⚠️ <b>Freeing the wrist does not come back.</b> When free, the wrist came from Meta's
    /// controller-synthesized hand pose whose offset relative to the anchor is written nowhere on our
    /// side, while the weapon is positioned from the anchor — hand and weapon drawn from two
    /// references, so a grip authored in the studio looked centimetres off in game. Defining the
    /// offset ourselves along with the lock makes bench and game identical <b>by construction</b>,
    /// with no constant left to measure. The cost is a rigidly attached hand (no natural wrist play)
    /// — consistent with fingers not being hardware-driven either.
    /// </para>
    /// <para>
    /// ⚠️ <b>Fingers are NEVER driven by hardware</b> — neither the controller trigger/grip nor hand
    /// tracking moves a finger. All five are locked every frame (<c>JointFreedom.Locked</c>) and the
    /// pose each frame is either the idle array or the held item's array rigged for that slot;
    /// transitions (empty ↔ grip, primary ↔ foregrip) are smoothed joint by joint over
    /// <see cref="HandPoseLibrary.TransitionSeconds"/> (<see cref="HandState"/>) — <b>the hand's
    /// placement on the controller shares the same duration and curve</b>, so the hand slides into
    /// its pose instead of snapping. Freeing even one finger means the studio hand and the in-game
    /// hand diverge on it; sampling an empty hand's fingers from hardware is absent for the same
    /// reason.
    /// </para>
    /// <para>
    /// ⚠️ <b>Does not touch the weapon pose.</b> The sole writer of the weapon's world pose is
    /// <c>Weapon.ApplyCanonicalGrip</c> + <see cref="ItemGripSolver"/>; this class drives only the
    /// HAND. Two writers would make the same weapon look different on its own screen than on the
    /// other screen (network pose).
    /// </para>
    /// <para>
    /// ⚠️ <b>Local player only.</b> A remote avatar's hand is drawn from the networked skeleton
    /// (<c>RemoteAvatar</c>) and never comes through here. This class has no network job and no
    /// protocol counterpart.
    /// </para>
    /// <para>
    /// <b>Why a self-bootstrapping persistent singleton</b> (<see cref="WeaponGranter"/> pattern): a
    /// scene component would add a manual setup step to every new arena. Invisible, and silently does
    /// nothing when there is no rig (admin spectator, scene not loaded yet).
    /// </para>
    /// </summary>
    /// <remarks>
    /// ⚠️ <b><see cref="DefaultExecutionOrder"/> is deliberate:</b> <c>Weapon.LateUpdate</c> writes the
    /// weapon pose and we lock the wrist to it, so we must run AFTER — otherwise the hand grabs a
    /// one-frame-old weapon and jitters during fast movement. Not done via Project Settings'
    /// <c>Script Execution Order</c>: a project setting would be an invisible repo-wide dependency,
    /// the attribute stays in code with its reason.
    /// </remarks>
    [DefaultExecutionOrder(100)]
    public class HandGripPoser : MonoBehaviour
    {
        /// <summary>Rescan interval when the synthetic hand is missing (a new scene may have loaded).</summary>
        private const float RescanSeconds = 1f;

        /// <summary>
        /// Node name of the synthetic hand the player SEES.
        /// <para>
        /// ⚠️ The name filter is mandatory: the rig holds other <see cref="SyntheticHand"/>s
        /// (<c>LeftHandSynthetic</c>/<c>RightHandSynthetic</c> under <c>Reticle</c> = the ghost hand
        /// of distance grab). An unfiltered search drives the first match, and the player sees their
        /// own hands frozen while a ghost hand across the room grabs the weapon.
        /// </para>
        /// </summary>
        private const string SyntheticHandNodeName = "SyntheticHandData";

        /// <summary>Finger count — guaranteed by ISDK (see <see cref="ApplyFingers"/>).</summary>
        private const int FingerCount = 5;

        public static HandGripPoser Instance { get; private set; }

        /// <summary>
        /// Cross-frame state of one hand: synthetic hand cache, wrist lock and the <b>displayed</b>
        /// finger pose.
        /// <para>
        /// <b>Why the transition lives here and not in ISDK:</b> <c>SyntheticHand</c> only smooths the
        /// free↔locked switch (its own lock curve); changing the target rotation while locked applies
        /// INSTANTLY. So between empty hand and grip (or primary ↔ foregrip) the hand is slerped joint
        /// by joint from <see cref="From"/> to the target over
        /// <see cref="HandPoseLibrary.TransitionSeconds"/>; this INTERMEDIATE array is written to the
        /// synthetic hand every frame.
        /// </para>
        /// <para>⚠️ On a target change the start point is the currently DISPLAYED array, not the
        /// previous target's: a new target mid-transition redirects the hand without a jump.</para>
        /// </summary>
        private sealed class HandState
        {
            public SyntheticHand Synthetic;

            /// <summary>Is this hand's WRIST currently locked (to controller or item). Drops only when
            /// the controller anchor cannot be resolved at all — and then there is no hand to draw.</summary>
            public bool WristLocked;

            /// <summary>
            /// Current target array — held and compared <b>by reference</b>.
            /// <para>⚠️ Hence target arrays MUST be cached/shared
            /// (<see cref="ItemDefinition.GripJointRotations"/>,
            /// <see cref="HandPoseLibrary.IdleJointRotations"/>): a source producing a new array per
            /// frame would count as "target changed" every frame and the transition would never
            /// finish. Per-joint comparison is no alternative either — comparing 19 quaternions per
            /// frame for two hands is not free when reference identity already guarantees it.</para>
            /// </summary>
            public Quaternion[] Target;

            /// <summary>Displayed array at the moment the transition started (a copy).</summary>
            public readonly Quaternion[] From = new Quaternion[FingersMetadata.HAND_JOINT_IDS.Length];

            /// <summary>Array written to the synthetic hand this frame.</summary>
            public readonly Quaternion[] Shown = new Quaternion[FingersMetadata.HAND_JOINT_IDS.Length];

            /// <summary><c>0..1</c> transition progress (1 = settled on target).</summary>
            public float Progress = 1f;

            /// <summary>Target wrist offset relative to the ANCHOR — the transition's destination.</summary>
            public Pose WristGoal;

            /// <summary>Displayed offset at the moment the transition started.</summary>
            public Pose WristFrom;

            /// <summary>The (intermediate) wrist offset used this frame.</summary>
            public Pose WristShown;

            /// <summary><c>0..1</c> wrist transition progress (1 = settled).</summary>
            public float WristProgress = 1f;

            /// <summary>Has this hand ever been seated — the first frame has no transition (below).</summary>
            public bool WristSeated;

            /// <summary>
            /// Moves the anchor→wrist offset <b>smoothly</b> toward the target and returns the
            /// intermediate offset for this frame.
            /// <para>
            /// ⚠️ <b>The blend happens in ANCHOR space, not world space.</b> The wrist's world pose
            /// moves with the controller every frame; blending that would drag the hand behind the
            /// real hand (tracking lag). Only the <b>offset</b> blends — "where on the controller the
            /// hand sits" — so the hand keeps following the controller with no added latency while its
            /// pose changes.
            /// </para>
            /// <para>
            /// The trade-off: moving to an item also changes the anchor FRAME (controller → item), but
            /// that is not a jump — by the solver's identity the item-derived anchor position is the
            /// controller anchor itself (<c>LockToItemGrip</c>), and so is its rotation in a one-handed
            /// hold. So this offset is the only visible difference during the transition.
            /// </para>
            /// <para>⚠️ <b>The first frame has no transition</b> (<see cref="WristSeated"/>): when the
            /// hand first appears in the rig there is no "previous pose" to show — blending from zero
            /// would float the hand in from the origin.</para>
            /// <para>⚠️ On a target change the start point is the currently DISPLAYED offset: dropping
            /// the weapon mid-transition redirects the hand without a jump (same rule as fingers).</para>
            /// </summary>
            public Pose StepWrist(in Pose goal)
            {
                if (!WristSeated)
                {
                    WristSeated = true;
                    WristGoal = goal;
                    WristFrom = goal;
                    WristShown = goal;
                    WristProgress = 1f;
                    return WristShown;
                }

                // ⚠️ Vector3/Quaternion comparison is APPROXIMATE, and that is what is wanted here: the
                // target comes from a shared constant or an asset, i.e. it does not change per frame —
                // exact equality would let float noise restart the transition every frame.
                if (WristGoal.position != goal.position || WristGoal.rotation != goal.rotation)
                {
                    WristFrom = WristShown;
                    WristGoal = goal;
                    WristProgress = 0f;
                }

                if (WristProgress < 1f)
                {
                    WristProgress = HandPoseLibrary.TransitionSeconds > 0f
                        ? Mathf.Min(1f, WristProgress + Time.deltaTime / HandPoseLibrary.TransitionSeconds)
                        : 1f;

                    float t = HandPoseLibrary.Ease(WristProgress);
                    WristShown = new Pose(
                        Vector3.Lerp(WristFrom.position, WristGoal.position, t),
                        Quaternion.Slerp(WristFrom.rotation, WristGoal.rotation, t));
                }
                else
                {
                    WristShown = WristGoal;
                }

                return WristShown;
            }

            /// <summary>New scene / first setup: everything returns to idle and "seated".</summary>
            public void Reset(bool rightHand)
            {
                Synthetic = null;
                WristLocked = false;
                Progress = 1f;

                // ⚠️ The wrist returns to "never seated", the offset itself is NOT cleared: in a new
                // scene the hand snaps straight to its target on the first frame (new rig, new hand —
                // a transition would float the hand in at scene open).
                WristSeated = false;
                WristProgress = 1f;

                Quaternion[] idle = HandPoseLibrary.IdleJointRotations(rightHand);
                Target = idle;
                for (int i = 0; i < Shown.Length && i < idle.Length; i++)
                {
                    Shown[i] = idle[i];
                    From[i] = idle[i];
                }
            }
        }

        private readonly HandState _left = new HandState();
        private readonly HandState _right = new HandState();

        private float _nextScanAt;

        /// <summary>"Once per session" warning log for weapons with a missing pose.</summary>
        private readonly HashSet<string> _warned = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var go = new GameObject("[HandGripPoser]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<HandGripPoser>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            _left.Reset(false);
            _right.Reset(true);
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            SceneManager.sceneLoaded -= HandleSceneLoaded;
            Instance = null;
        }

        /// <summary>New scene = new rig: the cache dies, rescanned on the first frame.</summary>
        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _left.Reset(false);
            _right.Reset(true);
            _nextScanAt = 0f;
        }

        private void LateUpdate()
        {
            if (Instance != this)
            {
                return;
            }

            TickHand(OVRInput.Controller.LTouch, false, _left);
            TickHand(OVRInput.Controller.RTouch, true, _right);
        }

        /// <summary>
        /// One frame of one hand.
        /// <para>⚠️ <b>The lock target is decided by the PRESENCE of a grip, not the grip POINT:</b> a
        /// hand holding an item (primary or foregrip alike) locks to the ITEM, an empty hand to the
        /// CONTROLLER. Both take the offset from the same gate (<see cref="ItemGripAuthority"/>), so
        /// the hand never comes "from somewhere else" — the offset itself may be authored per slot
        /// (<see cref="ItemGripPose.Wrist"/>) and falls back to the shared definition.</para>
        /// <para>Fingers are ours in every case: idle pose on an empty hand, the slot's own rigged pose
        /// when holding — the target is handed to <see cref="ApplyFingers"/> each frame, which smooths
        /// the transition.</para>
        /// <para>⚠️ <b>Placement is smoothed over the SAME duration as the fingers</b>
        /// (<c>HandState.StepWrist</c>, <see cref="HandPoseLibrary.TransitionSeconds"/>): since the
        /// hand's position/angle on the controller is authored per weapon, the offset would otherwise
        /// change with a large jump when a weapon arrives. Sharing one duration is required — a hand
        /// whose fingers and wrist move at different speeds looks broken.</para>
        /// </summary>
        private void TickHand(OVRInput.Controller hand, bool rightHand, HandState state)
        {
            SyntheticHand synthetic = Resolve(state);
            if (synthetic == null)
            {
                // No rig (spectator / scene not loaded): nothing to lock either.
                state.WristLocked = false;
                return;
            }

            Weapon weapon = FindWeaponUsing(hand, out GripSocketKind kind);
            ItemDefinition definition = weapon != null ? weapon.Definition : null;
            bool hasGrip = definition != null && definition.HasGrip(kind, rightHand);

            if (weapon != null && !hasGrip)
            {
                // Weapon with no authored grip: the hand falls back to idle.
                WarnMissingPose(weapon, kind, rightHand);
            }

            // The hand's placement on the controller is PART of the grip: an authored slot brings its
            // own placement (a hand wrapping a foregrip from the side cannot sit at the same angle as
            // one palming the grip), an empty hand falls back to the shared definition.
            // ⚠️ The offset is not used directly but passed through the transition: otherwise the hand
            // pose would change INSTANTLY when a weapon arrives (or is dropped) — records are authored
            // per weapon, so the gap can be large (some grips are held from the side, some below).
            ItemGripPose grip = hasGrip ? definition.GetGrip(kind, rightHand) : default;
            Pose anchorToWrist = state.StepWrist(hasGrip
                ? ItemGripAuthority.ResolveAnchorToWrist(grip, rightHand)
                : ItemGripAuthority.ResolveAnchorToWrist(rightHand));

            if (hasGrip)
            {
                LockToItemGrip(synthetic, weapon.transform, grip, anchorToWrist);
                state.WristLocked = true;
            }
            else
            {
                LockToController(synthetic, hand, anchorToWrist, state);
            }

            ApplyFingers(state, hasGrip
                ? definition.GripJointRotations(kind, rightHand)
                : HandPoseLibrary.IdleJointRotations(rightHand));
        }

        /// <summary>
        /// Locks an <b>empty hand's</b> wrist to the <b>controller</b>: anchor + defined offset
        /// (<see cref="ItemGripAuthority.WristFromAnchor"/>). A hand holding an item never comes here —
        /// it goes through <see cref="LockToItemGrip"/>.
        /// <para>
        /// ⚠️ <b>This lock deliberately prevents the hand from coming out of Meta's synthesized
        /// "natural" pose.</b> That pose carries an anchor-relative offset written nowhere on our side,
        /// while the weapon is positioned from the anchor. Two references = a grip authored on the
        /// bench looking displaced in game. With the lock, hand and weapon share one reference, and
        /// since the studio's ghost hand reads the same offset, bench and game match by construction.
        /// </para>
        /// <para>⚠️ If the anchor cannot be resolved the lock is RELEASED (no rig yet): keeping it
        /// locked would freeze the hand at its last known place.</para>
        /// <para>⚠️ The pose is in WORLD space and must be given as such (<c>worldPose: true</c>) —
        /// rationale in <see cref="LockToItemGrip"/>.</para>
        /// </summary>
        private static void LockToController(SyntheticHand synthetic, OVRInput.Controller hand,
            in Pose anchorToWrist, HandState state)
        {
            // The ONE way to discover the rig: a second search would let two components find different
            // rigs on different frames (same rationale as Scan).
            Transform anchor = WeaponGranter.ResolveHandAnchor(hand);
            if (anchor == null)
            {
                FreeWristIfLocked(state);
                return;
            }

            Pose wrist = ItemGripAuthority.WristFromAnchor(
                new Pose(anchor.position, anchor.rotation), anchorToWrist);
            synthetic.LockWristPose(wrist, 1f, SyntheticHand.WristLockMode.Full, true);
            state.WristLocked = true;
        }

        /// <summary>
        /// Locks a holding hand's wrist <b>FULLY</b> (position + rotation) to the record on the item —
        /// that hand sticks to the item. <b>Same path for both grip points</b>: primary and foregrip
        /// both come through here.
        /// <para>
        /// ⚠️ <b>Why the main hand locks to the ITEM and not the CONTROLLER:</b> in a two-handed hold
        /// the weapon's rotation is NOT the main controller's — <see cref="ItemGripSolver"/> aims it at
        /// the foregrip hand. A controller-locked main hand would stay unrotated when the player turns
        /// the weapon by the foregrip, ending up outside it. Deriving from the item closes that by
        /// construction and <b>changes nothing in a one-handed hold</b>: by the solver's identity
        /// <c>item.position + item.rotation * record</c> is always the main controller anchor ITSELF
        /// (<c>itemPosition = palm.position − itemRotation * record</c>), so the hand's POSITION stays
        /// on the controller either way — only ROTATION comes from the item.
        /// </para>
        /// <para>
        /// The record is the controller ANCHOR's POSITION relative to the item (<see cref="ItemGripPose"/>;
        /// the anchor half carries no rotation — the controller is taken as aligned with the item);
        /// what the synthetic hand needs is the WRIST. The bridge is
        /// <see cref="ItemGripAuthority.WristFromAnchor"/> (<c>wrist = (item ∘ record) ∘ delta</c>,
        /// delta being that slot's hand placement). A wrong delta breaks not the weapon's direction but
        /// the hand's placement on the grip by a few centimetres/degrees.
        /// </para>
        /// <para>
        /// ⚠️ <b>The lock is UNCONDITIONAL</b> — no distance/angle gate, and none is added. Deliberate
        /// trade: the cost is the hand visually stretching from the arm when the physical controller
        /// moves away from the foregrip; the gain is that the hand never detaches from the weapon
        /// until the player releases grip. A distance gate would release the hand with the player doing
        /// nothing, producing "I hold the weapon with two hands but the second one is in mid-air".
        /// </para>
        /// <para>
        /// ⚠️ The warning does not apply to the main hand: there the lock position is already on the
        /// controller (identity above), so no stretch can arise.
        /// </para>
        /// <para>
        /// ⚠️ Manual composition, NOT <c>TransformPoint</c>: the record is in METRES and must not be
        /// scaled by the item's visual scale (<c>WPN_*</c> roots are 0.8) — a scaled composition puts
        /// the wrist 1/0.8 away and the hand floats beside the weapon.
        /// </para>
        /// <para>
        /// ⚠️ The pose is in WORLD space and must be given as such (<c>worldPose: true</c>): the
        /// synthetic hand works in tracking space and <c>LockWristPose</c> does the conversion. Calling
        /// <c>LockWristPosition</c> directly SKIPS it and the hand silently drifts with the rig's world
        /// position.
        /// </para>
        /// </summary>
        /// <param name="grip">Grip record — the controller anchor's local position relative to the ITEM
        /// (metres, unscaled).</param>
        /// <param name="anchorToWrist">That slot's hand placement (shared definition when unauthored).</param>
        private static void LockToItemGrip(SyntheticHand synthetic, Transform item,
            in ItemGripPose grip, in Pose anchorToWrist)
        {
            // The anchor record carries no rotation: the controller on the grip is taken as aligned
            // with the item. The hand's angle on that controller is a separate field (anchorToWrist)
            // and matters exactly here: some grips are held from the side, some from below.
            var anchor = new Pose(item.position + item.rotation * grip.position, item.rotation);

            Pose wrist = ItemGripAuthority.WristFromAnchor(anchor, anchorToWrist);
            synthetic.LockWristPose(wrist, 1f, SyntheticHand.WristLockMode.Full, true);
        }

        /// <summary>Releases the wrist lock — the <b>only caller</b> is the case where the anchor cannot
        /// be resolved at all (no rig). Leaves fingers alone: the caller writes them right after, and
        /// <c>FreeAllJoints</c> would return the hand to tracking for one frame and make it jitter.</summary>
        private static void FreeWristIfLocked(HandState state)
        {
            if (!state.WristLocked)
            {
                return;
            }

            state.Synthetic.FreeWrist();
            state.WristLocked = false;
        }

        /// <summary>
        /// Moves the hand's fingers toward the target joint array and writes it to the synthetic hand.
        /// <para>
        /// <b>Transition:</b> on the frame the target changes, the currently displayed array is copied
        /// to <see cref="HandState.From"/> and progress is reset; on following frames the array is
        /// slerped toward the target over <see cref="HandPoseLibrary.TransitionSeconds"/>
        /// (<see cref="HandPoseLibrary.Ease"/>). Once settled (progress 1) the target array is written
        /// verbatim.
        /// </para>
        /// <para>
        /// ⚠️ <b>All five fingers are locked EVERY FRAME</b> (<c>JointFreedom.Locked</c>) and this
        /// cannot be shortened: freedom is persistent on the synthetic hand and another component
        /// (ISDK's own grab visuals, anything calling <c>FreeAllJoints</c>) can change it — without
        /// rewriting the lock the fingers return to hardware after that frame and the trigger finger
        /// starts moving with the controller. Rewriting an unchanged level is cheap in ISDK (it
        /// compares and skips).
        /// </para>
        /// <para>⚠️ The target array is CACHED and shared (<see cref="HandState.Target"/>): never
        /// modified, only read — the intermediate array is <see cref="HandState.Shown"/>.</para>
        /// </summary>
        private static void ApplyFingers(HandState state, Quaternion[] goal)
        {
            SyntheticHand synthetic = state.Synthetic;
            Quaternion[] shown = state.Shown;

            // Reference comparison: target arrays are cached per slot, so identity is enough
            // (rationale in HandState.Target).
            if (!ReferenceEquals(goal, state.Target))
            {
                state.Target = goal;
                state.Progress = 0f;
                System.Array.Copy(shown, state.From, shown.Length);
            }

            int count = Mathf.Min(shown.Length, goal.Length);

            if (state.Progress < 1f)
            {
                state.Progress = HandPoseLibrary.TransitionSeconds > 0f
                    ? Mathf.Min(1f, state.Progress + Time.deltaTime / HandPoseLibrary.TransitionSeconds)
                    : 1f;

                float t = HandPoseLibrary.Ease(state.Progress);
                for (int i = 0; i < count; i++)
                {
                    shown[i] = Quaternion.Slerp(state.From[i], goal[i], t);
                }
            }
            else
            {
                System.Array.Copy(goal, shown, count);
            }

            synthetic.OverrideAllJoints(shown, 1f);
            for (int i = 0; i < FingerCount; i++)
            {
                synthetic.SetFingerFreedom((HandFinger)i, JointFreedom.Locked);
            }
        }

        /// <summary>
        /// Finds the weapon USING this hand; <see cref="GripSocketKind.Primary"/> for the main hand,
        /// <see cref="GripSocketKind.Secondary"/> for the foregrip. First match wins (the same hand
        /// bound to two weapons is already a bug one layer up).
        /// </summary>
        private static Weapon FindWeaponUsing(OVRInput.Controller hand, out GripSocketKind kind)
        {
            for (int i = 0; i < Weapon.Active.Count; i++)
            {
                Weapon weapon = Weapon.Active[i];
                if (weapon == null || !weapon.IsHeld)
                {
                    continue;
                }

                if (weapon.MainHand == hand)
                {
                    kind = GripSocketKind.Primary;
                    return weapon;
                }

                if (weapon.SecondaryHand == hand)
                {
                    kind = GripSocketKind.Secondary;
                    return weapon;
                }
            }

            kind = GripSocketKind.Primary;
            return null;
        }


        // ------------------------------------------------------------ hand resolution (ISDK)

        /// <summary>
        /// Resolves the synthetic hand LAZILY and caches it; on failure retries once a second and
        /// returns <c>null</c> (does NOT log — a missing rig is normal: the admin spectator disables
        /// it, an editor session may have none at all).
        /// </summary>
        private SyntheticHand Resolve(HandState state)
        {
            if (state.Synthetic != null)
            {
                return state.Synthetic;
            }

            if (Time.time < _nextScanAt)
            {
                return null;
            }

            _nextScanAt = Time.time + RescanSeconds;
            Scan();
            return state.Synthetic;
        }

        /// <summary>
        /// Finds the two synthetic hands under the rig.
        /// <para>
        /// The rig is reached via <see cref="WeaponGranter.ResolveHandAnchor"/>: that is the ONE way to
        /// discover it, and a second search would let two components find different rigs on different
        /// frames.
        /// </para>
        /// <para>
        /// ⚠️ Left/right is resolved from the hand's own <see cref="Hand.Handedness"/>, NOT from the
        /// ancestor node name (<c>ComprehensiveInteractorsLeft/Right</c>): name-based resolution would
        /// silently invert when the Building Blocks prefab is renamed (left hand's pose onto the right
        /// hand). Since <see cref="Hand.Handedness"/> cannot be read before data flows, a hand does not
        /// count as found <b>until connected</b> — and then there is no hand to draw anyway.
        /// </para>
        /// </summary>
        private void Scan()
        {
            Transform anchor = WeaponGranter.ResolveHandAnchor(OVRInput.Controller.RTouch);
            if (anchor == null)
            {
                anchor = WeaponGranter.ResolveHandAnchor(OVRInput.Controller.LTouch);
            }

            if (anchor == null)
            {
                return;
            }

            var rig = anchor.GetComponentInParent<OVRCameraRig>();
            if (rig == null)
            {
                return;
            }

            SyntheticHand[] hands = rig.GetComponentsInChildren<SyntheticHand>(true);
            for (int i = 0; i < hands.Length; i++)
            {
                SyntheticHand hand = hands[i];
                if (hand == null || hand.gameObject.name != SyntheticHandNodeName)
                {
                    continue;
                }

                if (!hand.isActiveAndEnabled || !hand.IsConnected)
                {
                    continue;
                }

                if (hand.Handedness == Handedness.Right)
                {
                    _right.Synthetic = hand;
                }
                else
                {
                    _left.Synthetic = hand;
                }
            }
        }

        /// <summary>
        /// <b>One</b> warning per session for a weapon with no authored grip.
        /// <para>
        /// ⚠️ Logging unconditionally in the loop produces two lines per frame (~140/s) and would drown
        /// the console; since the key is (definition + grip point + hand), every missing pose still
        /// shows up individually.
        /// </para>
        /// </summary>
        private void WarnMissingPose(Weapon weapon, GripSocketKind kind, bool rightHand)
        {
            string weaponName = weapon.Definition != null ? weapon.Definition.name : weapon.name;
            string key = $"{weaponName}|{kind}|{(rightHand ? "R" : "L")}";
            if (!_warned.Add(key))
            {
                return;
            }

            Debug.LogWarning($"[HandGripPoser] '{weaponName}' silahının " +
                             $"'{kind}' kavraması {(rightHand ? "SAĞ" : "SOL")} el için " +
                             "stüdyoda YAZILMAMIŞ; el idle duruşunda kalıyor.");
        }
    }
}
