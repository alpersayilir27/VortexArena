using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Net;

namespace VortexArena.Core.Player
{
    /// <summary>Draws a remote body from the <b>pose channel</b> when the skeleton channel produces
    /// nothing (§6.11): head bone from the wire's head pose, both arms solved to the wire's hand
    /// positions, everything else left where it stands.
    /// <para>Runs only while <see cref="ArenaNetCharacterBehaviour.IsPoseDriven"/> is true; a live
    /// skeleton stream — including a live T-pose fallback stream — keeps ownership of the bones.</para>
    /// </summary>
    /// <remarks>
    /// <b>Why this sits on the RECEIVER.</b> The sender-side T-pose fallback is bolted onto the very
    /// retargeter it stands in for, so it dies with it and covers none of the links after it (blob size,
    /// packet loss, a stream that never started). This one's only inputs are the interpolated head and
    /// hand poses — the same data that already draws the player's name label and weapon in the right
    /// place, which is the standing proof that it survives the fault. It therefore cannot be killed by
    /// the broken headset at all.
    /// <para>⚠️ <b>Without it a bodiless player is also UNHITTABLE:</b> hit boxes hang off these bones
    /// under a root that was being scaled to zero, so every collider collapsed to a point. Drawing the
    /// body restores the hit volume with it — this is a fairness guard, not decoration.</para>
    /// <para>⚠️ <b>Execution order must stay between 100 and 30050.</b> The lower bound is the SDK
    /// (<c>NetworkCharacterHandler</c>) and <see cref="ArenaNetCharacterBehaviour"/> (50), which own the
    /// applied pose and the root; writing before them is overwritten in the same frame. The upper bound
    /// is <see cref="RemoteHandPoser"/> (30050): when the player HOLDS something, seating the hand on the
    /// item must win over the raw hand target written here. <see cref="SkeletonPoseMirror"/> (30100) then
    /// carries everything to the red team body for free.</para>
    /// <para>⚠️ <b>Not placed in a prefab</b>: <see cref="RemoteAvatar"/> adds it in <c>Awake</c>, for the
    /// same reason as <see cref="RemoteHandPoser"/> — the head correction must be measured in BIND POSE,
    /// before the retargeter first writes to the skeleton.</para>
    /// </remarks>
    [DefaultExecutionOrder(30040)]
    public class RemotePoseBody : MonoBehaviour
    {
        /// <summary>Head bone name (Mixamo humanoid) — same rig contract as
        /// <see cref="HandFingerRig.LeftWristBoneName"/> and <see cref="SkeletonPoseMirror"/>.</summary>
        public const string HeadBoneName = "mixamorig:Head";

        private ArenaNetCharacterBehaviour _character;

        private Transform _head;
        private TwoBoneIk _leftArm;
        private TwoBoneIk _rightArm;

        /// <summary>Head bone orientation expressed in the character root's frame, measured in BIND POSE.
        /// <para>⚠️ The wire's head rotation cannot be written to the bone directly: a Mixamo head bone's
        /// local axes do not line up with the HMD's, so a raw write draws the head sideways. Measuring
        /// the offset once in bind pose converts between the two conventions and keeps working if the
        /// character model is replaced.</para></summary>
        private Quaternion _headBindInRoot = Quaternion.identity;

        private bool _hasHead;

        /// <summary>Resolves the bones in bind pose. Missing parts are skipped INDIVIDUALLY — a body
        /// drawn with no head still beats no body at all, which is what this class exists to prevent.
        /// </summary>
        internal void Bind(ArenaNetCharacterBehaviour character, Transform bodyRoot)
        {
            _character = character;

            if (bodyRoot == null)
            {
                enabled = false;
                return;
            }

            _head = FindBone(bodyRoot, HeadBoneName);
            if (_head != null)
            {
                _hasHead = true;
                _headBindInRoot = Quaternion.Inverse(bodyRoot.rotation) * _head.rotation;
            }

            Transform leftWrist = FindBone(bodyRoot, HandFingerRig.LeftWristBoneName);
            Transform rightWrist = FindBone(bodyRoot, HandFingerRig.RightWristBoneName);
            _leftArm = leftWrist != null ? TwoBoneIk.TryBuild(leftWrist) : null;
            _rightArm = rightWrist != null ? TwoBoneIk.TryBuild(rightWrist) : null;

            if (_hasHead || _leftArm != null || _rightArm != null)
            {
                return;
            }

            enabled = false;
            Debug.LogWarning(
                $"[RemotePoseBody] Kemik bulunamadı ('{HeadBoneName}' / " +
                $"'{HandFingerRig.LeftWristBoneName}' / '{HandFingerRig.RightWristBoneName}'). " +
                "Gövde izlemesi olmayan oyuncu, konumunu izleyen DONUK bir gövde olarak çizilecek. " +
                "Karakter modeli değiştiyse kemik adı sabitleri güncellenmeli.", character);
        }

        /// <remarks>⚠️ The field guard covers more than <see cref="_character"/>: this component is added
        /// with <c>AddComponent</c> in <see cref="RemoteAvatar"/>'s <c>Awake</c> and an <c>enabled</c>
        /// write there does not always stick — the same trap <see cref="RemoteHandPoser"/> documents.
        /// </remarks>
        private void LateUpdate()
        {
            if (_character == null || !_character.IsPoseDriven)
            {
                return;
            }

            RemotePlayerRegistry registry = RemotePlayerRegistry.Instance;
            if (registry == null ||
                !registry.GetInterpolatedPose(_character.PlayerId, out Pose head, out Pose handL, out Pose handR))
            {
                return;
            }

            if (_hasHead && _head != null)
            {
                _head.rotation = ArenaSpace.ArenaToWorld(head).rotation * _headBindInRoot;
            }

            // ⚠️ Only the arm is solved, the wrist's own rotation is left alone: with an item in hand
            // RemoteHandPoser writes it from the grip one step later, and with an empty hand the bind
            // orientation is closer to a resting hand than anything derivable from the controller anchor.
            // ⚠️ BodyScale needs no term here — the chain is solved with the SCALED skeleton's own bones,
            // so the hand lands on the target whatever the height factor.
            _leftArm?.Solve(ArenaSpace.ArenaToWorld(handL.position));
            _rightArm?.Solve(ArenaSpace.ArenaToWorld(handR.position));
        }

        /// <summary>Exact-name search under the body root. ⚠️ Exact, not "starts with": Mixamo appends to
        /// bone names (<c>…LeftHandIndex1</c>), so a prefix match confuses a bone with its child.</summary>
        private static Transform FindBone(Transform bodyRoot, string boneName)
        {
            Transform[] all = bodyRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == boneName)
                {
                    return all[i];
                }
            }

            return null;
        }
    }
}
