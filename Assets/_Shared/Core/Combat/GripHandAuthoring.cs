#if UNITY_EDITOR
using System.Collections.Generic;
using Oculus.Interaction.HandGrab.Visuals;
using Oculus.Interaction.Input;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Identity and tuning surface of the single hand the Grip Pose Studio places in the scene:
    /// which grip point, which handedness, and the ghost hand's puppet.
    /// <para>⚠️ Deliberately in the RUNTIME asmdef (<c>VortexArena.Core</c>) with the whole file
    /// under <c>#if UNITY_EDITOR</c>. In an editor asmdef Unity refuses to add the
    /// <see cref="MonoBehaviour"/> to a GameObject ("it is an editor script") and
    /// <c>AddComponent</c> silently returns <c>null</c>. Build safety: the wrapper keeps the type
    /// out of the build, and <see cref="HideFlags.DontSave"/> keeps instances out of
    /// scenes/prefabs (no "missing script" left behind).</para>
    /// <para>⚠️ Fingers live IN THE HAND'S BONES, not here: the rigged pose IS the ghost hand's
    /// joint <see cref="Transform"/>s, rotated in the Scene View and read from there by Save
    /// (<c>GripPoseStudio</c>). A second "pose" field would let workbench and saved hand diverge —
    /// the very thing this tool exists to prevent. Same reason there is no save button here.</para>
    /// <para>⚠️ Metacarpals are not rigged (<see cref="HandPoseLibrary.IsDrivable"/>): the OpenXR
    /// synthetic hand expects proximal rotations in wrist space, so rotating the metacarpal is
    /// right on the workbench and displaced in game. Full rationale in
    /// <see cref="HandPoseLibrary"/>.</para>
    /// <para>⚠️ This transform IS the CONTROLLER (anchor) frame; what enters the record is its
    /// POSITION only (<see cref="ItemGripPose"/> carries no anchor rotation — the root is kept
    /// weapon-aligned and only moved). The ISDK ghost hand and controller model are its CHILDREN
    /// (<see cref="Puppet"/>).</para>
    /// <para>⚠️ The ghost hand's own local pose enters the record too (<c>ItemGripPose.Wrist</c>):
    /// where and at which ANGLE the hand sits, per weapon and per hand. The controller model is
    /// outside this and locked (identity pose = alignment reference). Rotating the hand does NOT
    /// affect the weapon's pose.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GripHandAuthoring : MonoBehaviour
    {
        /// <summary>Length of the root gizmo's controller-forward arrow (m) — so it stays readable
        /// at weapon scale.</summary>
        private const float GizmoForwardLength = 0.08f;
        private const float GizmoSideLength = 0.03f;
        private const float GizmoOriginRadius = 0.008f;

        [SerializeField] private GripSocketKind _kind;
        [SerializeField] private bool _rightHand = true;

        // ⚠️ [SerializeField] on purpose: DontSave keeps it off disk, but it must survive DOMAIN
        // RELOAD — otherwise the puppet reference is lost after every compile and the finger joints
        // can no longer be found.
        [SerializeField] private HandPuppet _puppet;

        // ⚠️ The ghost hand's pose relative to the root is NOT stored here: it lives in the ghost
        // hand's OWN transform and Save reads it from there (same rationale as the fingers) — a
        // copy would let workbench and saved hand diverge. Initial placement comes from
        // ItemGripPose.Wrist, or ItemGripAuthority.ResolveAnchorToWrist when unauthored.

        public GripSocketKind Kind => _kind;
        public bool RightHand => _rightHand;
        public Handedness Handedness => _rightHand ? Handedness.Right : Handedness.Left;
        public HandPuppet Puppet => _puppet;

        /// <summary>
        /// Introduces the hand; called once by the studio during setup. The finger pose is written
        /// separately by <see cref="ApplyPose"/>.
        /// </summary>
        public void Resolve(HandPuppet puppet, GripSocketKind kind, bool rightHand)
        {
            _puppet = puppet;
            _kind = kind;
            _rightHand = rightHand;
        }

        /// <summary>
        /// Writes a recorded finger pose onto the ghost hand's bones (idle pose when the record is
        /// empty). Called during SETUP so the hand arrives in its last saved pose.
        /// <para>⚠️ Never called again on a set-up hand: snapping the bones the user is rotating
        /// back to disk would be "the fingers keep resetting while I rig".</para>
        /// <para>⚠️ Silent no-op without a puppet — this is also hit mid-setup before the puppet is
        /// resolved, so an error here would fire on every healthy hand setup.</para>
        /// </summary>
        public void ApplyPose(HandJointRotation[] joints)
        {
            if (_puppet == null)
            {
                return;
            }

            _puppet.SetJointRotations(HandPoseLibrary.BuildJointRotations(joints, _rightHand));
        }

        /// <summary>
        /// This hand's riggable finger joints (metacarpals excluded — see class warning). The
        /// studio offers these for selection and records them; empty without a puppet.
        /// </summary>
        public List<HandJointMap> DrivableJoints()
        {
            var result = new List<HandJointMap>();
            List<HandJointMap> maps = _puppet != null ? _puppet.JointMaps : null;

            for (int i = 0; maps != null && i < maps.Count; i++)
            {
                HandJointMap map = maps[i];
                if (map == null || map.transform == null || !HandPoseLibrary.IsDrivable(map.id))
                {
                    continue;
                }

                result.Add(map);
            }

            return result;
        }

        /// <summary>
        /// Controller root gizmo: blue = controller forward (= muzzle direction, root is
        /// weapon-aligned), green = up, red = right, small sphere at the origin. The root has no
        /// renderer — without this the user cannot see what they are dragging.
        /// </summary>
        private void OnDrawGizmos()
        {
            Transform t = transform;
            Vector3 origin = t.position;

            Gizmos.color = new Color(0.25f, 0.55f, 1f, 1f);
            Gizmos.DrawRay(origin, t.forward * GizmoForwardLength);
            Gizmos.color = new Color(0.35f, 0.9f, 0.35f, 1f);
            Gizmos.DrawRay(origin, t.up * GizmoSideLength);
            Gizmos.color = new Color(1f, 0.4f, 0.4f, 1f);
            Gizmos.DrawRay(origin, t.right * GizmoSideLength);
            Gizmos.color = new Color(1f, 1f, 1f, 0.9f);
            Gizmos.DrawWireSphere(origin, GizmoOriginRadius);
        }
    }
}
#endif
