using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Core.Player
{
    /// <summary>Seats a remote avatar's hand <b>onto the item it holds</b>: drives the fingers from the
    /// item's pose (§6.9 — fingers do not travel on the wire; <c>Idle</c> when empty, the slot's preset
    /// while holding, with a <see cref="HandPoseLibrary.TransitionSeconds"/> blend between) and solves the
    /// arm so the wrist reaches the item's grip point (<see cref="TwoBoneIk"/>).</summary>
    /// <remarks>
    /// <b>Why the arm must be driven:</b> the item is drawn from the main hand's <b>controller anchor</b>
    /// pose (<c>RemoteAvatar.ApplyItemPoses</c>), while the remotely drawn hand is the retargeted
    /// <b>anatomical wrist</b>. Those two points differ (by <see cref="HandGripPivot"/>'s as-yet unmeasured
    /// palm offset plus retarget error) and nothing joined them — the symptom was "everyone's weapon sits
    /// slightly ahead of their hand". The same gap is invisible locally because <c>HandGripPoser</c> hard-
    /// locks the synthetic wrist to the grip pose; this is its remote mirror.
    /// <para>⚠️ <b>The wrist's POSITION is not written, the arm is rotated.</b> Moving the wrist directly
    /// changes bone length, and since <see cref="SkeletonPoseMirror"/> copies only <c>localRotation</c> to
    /// the red team body, the second body could not follow (full rationale in
    /// <see cref="TwoBoneIk"/>).</para>
    /// <para>⚠️ <b>Scale (§10.8) is NOT handled separately here and must not be:</b> the target is the
    /// item's world pose and the arm is solved with the scaled skeleton's own bones — so whatever the
    /// height factor, the hand lands on the item. A second scale term would apply the correction
    /// twice.</para>
    /// <para>⚠️ <b>Execution order must be BETWEEN 100 and 30100.</b> The lower bound is the SDK
    /// (<c>NetworkCharacterHandler</c>, 100), which writes the skeleton: fingers/arm written before it get
    /// overwritten in the same frame. The upper bound is <see cref="SkeletonPoseMirror"/> (30100): the red
    /// team body copies the character's <c>localRotation</c>s, so writing BEFORE it makes the second body
    /// correct for free — no separate hand/arm setup. The same holds for the ghost body.</para>
    /// <para>⚠️ <b>Not placed in a scene/prefab</b>: <see cref="RemoteAvatar"/> adds it in <c>Awake</c>.
    /// The reason is TIMING, not convenience: finger axes and the wrist correction must be measured in
    /// bind pose (<see cref="HandFingerRig"/>), and a prefab-placed component's own <c>Awake</c> is not
    /// guaranteed to run before the skeleton is driven.</para>
    /// </remarks>
    [DefaultExecutionOrder(30050)]
    public class RemoteHandPoser : MonoBehaviour
    {
        /// <summary>
        /// Finger pose transition for one hand — remote mirror of the local hand
        /// (<c>HandGripPoser</c>): when the target pose changes, it travels from the currently shown
        /// pose over <see cref="HandPoseLibrary.TransitionSeconds"/>. Remote fingers therefore close
        /// on pickup and open on release at the same speed as the local hand, without snapping.
        /// </summary>
        private struct PoseBlend
        {
            private HandPoseProfile _from;
            private HandPoseProfile _target;
            private HandPoseProfile _shown;
            private float _progress;
            private bool _started;

            /// <summary>Takes the target, advances one frame and returns the pose to draw.</summary>
            public HandPoseProfile Step(in HandPoseProfile target, float deltaTime)
            {
                if (!_started)
                {
                    // First frame: no blend, start at the target (fingers must not look like they
                    // close from empty when the avatar spawns).
                    _started = true;
                    _from = target;
                    _target = target;
                    _shown = target;
                    _progress = 1f;
                    return _shown;
                }

                if (!target.Approximately(_target))
                {
                    _from = _shown;
                    _target = target;
                    _progress = 0f;
                }

                if (_progress < 1f)
                {
                    _progress = HandPoseLibrary.TransitionSeconds > 0f
                        ? Mathf.Min(1f, _progress + deltaTime / HandPoseLibrary.TransitionSeconds)
                        : 1f;
                    _shown = HandPoseProfile.Lerp(_from, _target, HandPoseLibrary.Ease(_progress));
                }
                else
                {
                    _shown = _target;
                }

                return _shown;
            }
        }

        private RemoteAvatar _avatar;
        private HandFingerRig _left;
        private HandFingerRig _right;
        private TwoBoneIk _leftArm;
        private TwoBoneIk _rightArm;
        private PoseBlend _leftBlend;
        private PoseBlend _rightBlend;

        /// <summary>
        /// Resolves the character's finger chains in bind pose. On failure the component disables
        /// itself: a half-driven hand is harder to diagnose than an undriven one.
        /// </summary>
        internal void Bind(RemoteAvatar avatar, Transform bodyRoot)
        {
            _avatar = avatar;
            _left = HandFingerRig.TryBuildFromBody(bodyRoot, false);
            _right = HandFingerRig.TryBuildFromBody(bodyRoot, true);

            if (_left == null || _right == null)
            {
                enabled = false;
                Debug.LogWarning(
                    $"[RemoteHandPoser] Parmak zinciri çözülemedi ('{HandFingerRig.LeftWristBoneName}' / " +
                    $"'{HandFingerRig.RightWristBoneName}' altında Thumb/Index/Middle/Pinky 1-4). " +
                    "Uzak eller bind pozunda (düz) kalacak. Karakter modeli değiştiyse kemik adı " +
                    "sabitlerini HandFingerRig'de güncelle.", avatar);
                return;
            }

            // ⚠️ The arm chain is found by walking UP from the wrist, not by name: the wrist is
            // already resolved by name and in a humanoid its two parents are always forearm + upper
            // arm. A second name constant would be a second place to forget on a model change.
            _leftArm = TwoBoneIk.TryBuild(_left.Wrist);
            _rightArm = TwoBoneIk.TryBuild(_right.Wrist);

            if (_leftArm == null || _rightArm == null)
            {
                Debug.LogWarning(
                    "[RemoteHandPoser] Kol zinciri çözülemedi (bileğin iki üstünde kemik yok ya da " +
                    "çok kısa). Parmaklar sürülecek ama el eşyaya OTURMAYACAK: silah, çizilen elin " +
                    "birkaç santim ilerisinde durur.", avatar);
            }
        }

        /// <summary>
        /// ⚠️ The guard covers the finger chains too, not just <see cref="_avatar"/>:
        /// <see cref="Bind"/> sets <c>enabled = false</c> when a chain fails, but this component is
        /// added via <c>AddComponent</c> in <see cref="RemoteAvatar"/>'s <c>Awake</c> and that write
        /// does not always stick. When it doesn't, this method throws a
        /// <c>NullReferenceException</c> per frame (~90 lines/s) BEFORE reaching
        /// <see cref="ApplyGrip"/>, so remote hands never seat on the weapon either — the
        /// "disabled" component runs loudly and breaks two things at once. Checking the fields makes
        /// the disable outcome irrelevant.
        /// </summary>
        private void LateUpdate()
        {
            if (_avatar == null || _left == null || _right == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            _left.Apply(_leftBlend.Step(_avatar.ResolveHandPose(false), dt));
            _right.Apply(_rightBlend.Step(_avatar.ResolveHandPose(true), dt));

            ApplyGrip(_left, _leftArm, false);
            ApplyGrip(_right, _rightArm, true);
        }

        /// <summary>
        /// Seats one hand on the item's grip point: the arm reaches the target via IK, then the
        /// wrist orientation is written from the grip.
        /// <para>
        /// ⚠️ <b>The target is the record's WRIST pose</b> (<c>RemoteAvatar.TryResolveGripWrist</c>),
        /// not the controller anchor itself: how the hand sits on that grip (from the side, from
        /// below) is written in the record's hand half and <b>the local hand reads exactly that</b>
        /// (<c>HandGripPoser</c>). Targeting the anchor directly would hold the same weapon one way
        /// in the headset and another on the spectator screen — and with the wrist turned into the
        /// forearm the symptom reads as "the remote player has no hand".
        /// <see cref="HandFingerRig.WristCorrection"/> comes AFTER and does a different job:
        /// converting the grip-space wrist into the humanoid bone.
        /// </para>
        /// <para>
        /// ⚠️ <b>Order matters</b> — arm first, wrist second. IK rotates the upper arm and thus the
        /// wrist's world rotation; writing the wrist first would be overwritten on the next line.
        /// </para>
        /// <para>
        /// ⚠️ <b>With no item in hand the arm is NOT touched</b> (no target does not mean "go to
        /// zero"): an empty arm stays where retargeting put it, only its fingers are idle.
        /// </para>
        /// </summary>
        private void ApplyGrip(HandFingerRig rig, TwoBoneIk arm, bool rightHand)
        {
            if (rig == null || rig.Wrist == null || !_avatar.TryResolveGripWrist(rightHand, out Pose wrist))
            {
                return;
            }

            arm?.Solve(wrist.position);
            rig.Wrist.rotation = wrist.rotation * rig.WristCorrection;
        }
    }
}
