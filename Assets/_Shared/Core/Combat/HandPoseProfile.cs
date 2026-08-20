using System;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Coarse form of a hand's finger pose — five per-finger curl amounts (<c>0</c> = open,
    /// <c>1</c> = fully closed). Drives the remote avatar's humanoid (Mixamo) hand and the idle hand.
    /// <para>⚠️ Curl ratios, not quaternions, go to the humanoid hand: the rigged pose is ISDK
    /// skeleton joint rotations (<see cref="HandJointRotation"/>) and the two skeletons' bone axes
    /// differ — raw rotations would repeat, at finger scale, the trap in
    /// <c>Docs/Sistem-Ozeti.md</c> §7 ("a rotation from tracking/network space is not written
    /// directly onto a humanoid bone"). A ratio is rig-independent: the axis is measured at runtime
    /// from that skeleton's own bind pose (<c>HandFingerRig</c>). Only
    /// <see cref="HandPoseLibrary.MeasureCurl"/> derives it, and the result is NOT stored in the
    /// asset — no second source of truth.</para>
    /// <para>⚠️ Does NOT travel on the wire (§6.9): finger placement is a grip question, not a
    /// measurement, and the answer is in every client's APK. Hence the same weapon is held
    /// identically on every screen — the pose is bound to the ITEM, not the hand.</para>
    /// </summary>
    [Serializable]
    public struct HandPoseProfile
    {
        [Range(0f, 1f)] public float thumb;
        [Range(0f, 1f)] public float index;
        [Range(0f, 1f)] public float middle;
        [Range(0f, 1f)] public float ring;
        [Range(0f, 1f)] public float pinky;

        // ⚠️ ONLY the idle hand's pose lives here. Grip poses are NOT added back: holds are rigged
        // per weapon (ItemGripPose.fingerJoints), sole source WD_*.asset. A shared "squeeze/grip"
        // table would make rigged and unrigged weapons look identical — the problem the tool solves.

        /// <summary>Relaxed idle pose: fingers slightly curled, increasing toward the pinky
        /// (anatomical rest; all zeros would stand the hand flat like a board).
        /// <para>Returned to on release, and used by slots with no rigged grip.</para></summary>
        public static HandPoseProfile Idle => new HandPoseProfile
        {
            thumb = 0.15f,
            index = 0.25f,
            middle = 0.30f,
            ring = 0.35f,
            pinky = 0.40f,
        };

        /// <summary>
        /// Unauthored (all zeros). ⚠️ Required gate: older <c>WD_*.asset</c> files without the
        /// field deserialize into five zeros, i.e. a "board hand". Zero has no legitimate use as
        /// "fully open", so it counts as unauthored.
        /// </summary>
        public bool IsEmpty =>
            thumb <= 0f && index <= 0f && middle <= 0f && ring <= 0f && pinky <= 0f;

        /// <summary>Curl ratio by finger order (0=thumb … 4=pinky).</summary>
        public float Get(int fingerIndex)
        {
            switch (fingerIndex)
            {
                case 0: return thumb;
                case 1: return index;
                case 2: return middle;
                case 3: return ring;
                default: return pinky;
            }
        }

        /// <summary>
        /// Per-finger linear blend between two poses — the remote avatar's transition step (idle ↔
        /// grip, <c>RemoteHandPoser</c>). Duration and curve come from
        /// <see cref="HandPoseLibrary.TransitionSeconds"/> / <see cref="HandPoseLibrary.Ease"/>.
        /// </summary>
        public static HandPoseProfile Lerp(in HandPoseProfile a, in HandPoseProfile b, float t)
        {
            t = Mathf.Clamp01(t);
            return new HandPoseProfile
            {
                thumb = Mathf.Lerp(a.thumb, b.thumb, t),
                index = Mathf.Lerp(a.index, b.index, t),
                middle = Mathf.Lerp(a.middle, b.middle, t),
                ring = Mathf.Lerp(a.ring, b.ring, t),
                pinky = Mathf.Lerp(a.pinky, b.pinky, t),
            };
        }

        /// <summary>Are all five ratios equal (used to tell whether the transition target changed).</summary>
        public bool Approximately(in HandPoseProfile other)
        {
            return Mathf.Approximately(thumb, other.thumb) &&
                   Mathf.Approximately(index, other.index) &&
                   Mathf.Approximately(middle, other.middle) &&
                   Mathf.Approximately(ring, other.ring) &&
                   Mathf.Approximately(pinky, other.pinky);
        }
    }
}
