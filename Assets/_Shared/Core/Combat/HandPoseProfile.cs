using System;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Coarse form of a hand's finger pose — five per-finger curl amounts (<c>0</c> = open,
    /// <c>1</c> = fully closed). Authors the <b>empty</b> hand's rest pose; a grip is never described
    /// this way.
    /// <para>⚠️ Ratios do not reach a skeleton directly: they are turned into an ISDK joint array
    /// (<see cref="HandPoseLibrary.BuildProfileJointRotations"/>) and both hands draw that array —
    /// locally raw, remotely retargeted. So "ratio" is an authoring convenience, not a second pose
    /// format.</para>
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

    }
}
