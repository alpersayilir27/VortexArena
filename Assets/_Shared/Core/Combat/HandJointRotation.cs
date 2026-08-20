using System;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// A single rigged finger joint record: which joint + its LOCAL rotation. A grip record's
    /// finger pose (<see cref="ItemGripPose"/>) is an array of these.
    /// <para>⚠️ Stored BY JOINT NAME, not by index: ISDK's joint list
    /// (<c>FingersMetadata.HAND_JOINT_IDS</c>) differs per build branch (OpenXR 19 joints with
    /// metacarpals, OVR 17 with another set), so an index would silently write the thumb's rotation
    /// onto the index finger. An unmatched name only skips that line. The name is the
    /// <c>HandJointId</c> enum value name (<c>"HandIndex1"</c>), readable in the asset YAML.</para>
    /// <para>⚠️ The rotation is in ISDK's "tracked" space — BEFORE
    /// <c>HandJointMap.RotationOffset</c>. The same array feeds the studio ghost hand
    /// (<c>HandPuppet.SetJointRotations</c>, which adds the offset itself) and the in-game synthetic
    /// hand (<c>SyntheticHand.OverrideAllJoints</c>); storing it offset-applied would double-apply
    /// on one end.</para>
    /// </summary>
    [Serializable]
    public struct HandJointRotation
    {
        [Tooltip("Eklemin adı — HandJointId enum değerinin adı (ör. HandIndex1).")]
        public string joint;

        [Tooltip("Eklemin YEREL dönüşü (ISDK izleme uzayı, RotationOffset uygulanmadan).")]
        public Quaternion rotation;

        /// <summary>Builds a record from a joint name and a rotation.</summary>
        public static HandJointRotation From(string jointName, in Quaternion localRotation)
        {
            return new HandJointRotation
            {
                joint = jointName,
                rotation = localRotation,
            };
        }
    }
}
