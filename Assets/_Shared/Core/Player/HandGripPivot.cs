using UnityEngine;

namespace VortexArena.Core.Player
{
    /// <summary>The SINGLE answer to "given the controller anchor, where is the player's PALM".</summary>
    /// <remarks>
    /// <b>Why needed:</b> grabbing used to reference <c>OVRCameraRig.leftHandAnchor</c> /
    /// <c>rightHandAnchor</c>, but what the player sees is the <b>synthetic hand</b>, not the controller —
    /// the few centimetres between anchor and palm make the weapon look like it passes through the hand
    /// or floats. This class defines that delta in one place; every consumer that looks at the anchor
    /// (<see cref="VortexArena.Core.Combat.Weapon"/>, <c>WeaponGranter</c>, <c>WeaponFrame</c>,
    /// <see cref="RemoteAvatar"/>) goes through here.
    /// <para>⚠️ <b>The remote side MUST go through here too</b>: the hand pose on the wire is the anchor
    /// pose (§6.6), and if the offset is not applied from the same place at both ends the same weapon is
    /// drawn in two different poses on two screens.</para>
    /// <para>⚠️ <b>Do not confuse with <see cref="HandGripConvention"/>:</b> that answers "which
    /// DIRECTION does the hand face in anchor space" (bridge to the humanoid wrist), this one "WHERE is
    /// the palm relative to the anchor". Separate constants that must stay separate — merged, the remote
    /// body's wrist and the weapon's pose would hang on one number and tuning one would break the
    /// other.</para>
    /// </remarks>
    public static class HandGripPivot
    {
        /// <summary>Offset from the controller anchor to the LEFT palm — anchor space, METRES.
        /// <b>ZERO:</b> the palm is taken to be the controller anchor itself.</summary>
        /// <remarks>
        /// ⚠️ <b>Do not write an estimated number here.</b> The value that used to sit here was an
        /// unmeasured ergonomic guess and did two kinds of damage: (1) it detached the weapon's position
        /// from the controller's real pose, adding a second hidden term to "the weapon does not sit right
        /// in my hand"; (2) every weapon's grip data was already tuned ON TOP of this offset, so two
        /// separate knobs adjusted the same thing and neither could be identified as the broken one.
        /// Where the weapon sits in the hand comes from one place: the grip record written in the studio
        /// into <c>WD_*.asset</c> (<see cref="VortexArena.Core.Combat.ItemGripPose"/>).
        /// <para>If a genuinely measured wrist offset is ever needed (run
        /// <see cref="HandGripCalibrationProbe"/> on device), this is still its place — that is exactly
        /// why the class exists. Leaving a door behind an identity transform is the same pattern as
        /// <c>ArenaSpace</c>.</para>
        /// </remarks>
        public static readonly Vector3 LeftPalmOffset = Vector3.zero;

        /// <summary>Offset from the controller anchor to the RIGHT palm — rationale in
        /// <see cref="LeftPalmOffset"/>.</summary>
        public static readonly Vector3 RightPalmOffset = Vector3.zero;

        /// <summary>Per-hand palm offset (anchor space, metres).</summary>
        public static Vector3 PalmOffset(bool rightHand)
        {
            return rightHand ? RightPalmOffset : LeftPalmOffset;
        }

        /// <summary>Derives the palm pose from the anchor pose.</summary>
        /// <remarks>
        /// ⚠️ <b>The rotation is DELIBERATELY the anchor's own</b> and
        /// <see cref="HandGripConvention.AnchorBasis"/> is not mixed in: that basis exists to bridge the
        /// remote body's wrist to humanoid axes. Hanging both on one constant means fixing the wrist
        /// breaks the weapon pose (and vice versa). Studio grip records are also in anchor space
        /// (<c>ItemGripPose</c>) — changing the rotation would invalidate every weapon's grip at once.
        /// <para>⚠️ Manual composition, NOT <c>Transform.TransformPoint</c>: the offset is in METRES and
        /// must not be scaled even if the rig's scale is not 1 (a recurring rule in this project).</para>
        /// </remarks>
        public static Pose Resolve(in Pose anchor, bool rightHand)
        {
            return new Pose(
                anchor.position + anchor.rotation * PalmOffset(rightHand),
                anchor.rotation);
        }

        /// <summary><see cref="Transform"/> convenience for <see cref="Resolve(in Pose, bool)"/>.</summary>
        /// <remarks>⚠️ There is NO <c>null</c> check and none is added: every caller already resolves the
        /// anchor via "is there a rig" (<c>WeaponGranter.ResolveHandAnchor</c>), and silently returning
        /// <c>default</c> here would glue the weapon to the world origin.</remarks>
        public static Pose Resolve(Transform anchor, bool rightHand)
        {
            return Resolve(new Pose(anchor.position, anchor.rotation), rightHand);
        }

        /// <summary>Is this controller the right hand? <c>None</c> (unresolved hand) counts as RIGHT —
        /// the SAME rule as <c>Weapon.IsMainHandRight</c>: there is no "unknown hand" value on the
        /// wire.</summary>
        public static bool IsRight(OVRInput.Controller hand)
        {
            return hand != OVRInput.Controller.LTouch;
        }
    }
}
