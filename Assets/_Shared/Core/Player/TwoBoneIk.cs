using UnityEngine;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Analytic two-bone IK (upper arm → forearm → hand) — <b>writes ROTATION only</b>.
    /// <para>
    /// ⚠️ <b>Not writing position is the reason this exists, not a limitation.</b> Moving the wrist
    /// straight to the target changes bone length (<c>localPosition</c>) and breaks two things:
    /// (1) the skinned mesh stretches at the wrist, (2) <see cref="SkeletonPoseMirror"/> copies only
    /// <c>localRotation</c> to the red team body, so the second body would NOT follow the first —
    /// the same player drawn in two different poses. A rotation-only solve mirrors for free.
    /// </para>
    /// <para>
    /// ⚠️ <b>Elbow bend direction is PRESERVED, not imposed</b> (no pole target): the plane normal
    /// is read from the arm's pose <b>that frame</b>. A fixed pole would override the natural elbow
    /// direction found by retargeting, for a correction only a few centimeters wide.
    /// </para>
    /// <para>
    /// Method is the law of cosines: both interior angles are recomputed for the target distance,
    /// then the upper arm is aimed at the target. Unreachable targets are <b>clamped</b> to arm
    /// length (arm extends straight) — explicit and continuous, no snapping.
    /// </para>
    /// </summary>
    public sealed class TwoBoneIk
    {
        /// <summary>Minimum length for a bone to count as present (m).</summary>
        private const float MinBoneLength = 0.02f;

        /// <summary>Margin avoiding fully-straight / fully-folded singularities (m).</summary>
        private const float ReachEpsilon = 0.001f;

        private const float MinSqr = 1e-10f;

        private readonly Transform _upper;
        private readonly Transform _lower;
        private readonly Transform _end;
        private readonly float _upperLength;
        private readonly float _lowerLength;

        private TwoBoneIk(Transform upper, Transform lower, Transform end,
            float upperLength, float lowerLength)
        {
            _upper = upper;
            _lower = lower;
            _end = end;
            _upperLength = upperLength;
            _lowerLength = lowerLength;
        }

        /// <summary>End of the chain (wrist bone).</summary>
        public Transform End => _end;

        /// <summary>
        /// Builds the chain by walking two parents up from the end and measures bone lengths
        /// <b>once, in bind pose</b>.
        /// <para>⚠️ Lengths are NOT re-measured per frame: since the IK preserves bone length the
        /// measurement must stay fixed; a live measurement would carry one frame's error into the
        /// next and slowly drift.</para>
        /// <para>Returns <c>null</c> when the chain cannot be resolved (missing parent, bone too
        /// short) — the caller then drives that hand not at all. A half-built arm is harder to
        /// diagnose than an undriven one.</para>
        /// </summary>
        public static TwoBoneIk TryBuild(Transform end)
        {
            if (end == null || end.parent == null || end.parent.parent == null)
            {
                return null;
            }

            Transform lower = end.parent;
            Transform upper = lower.parent;

            float upperLength = Vector3.Distance(upper.position, lower.position);
            float lowerLength = Vector3.Distance(lower.position, end.position);

            if (upperLength < MinBoneLength || lowerLength < MinBoneLength)
            {
                return null;
            }

            return new TwoBoneIk(upper, lower, end, upperLength, lowerLength);
        }

        /// <summary>
        /// Brings the end to <paramref name="target"/> (upper arm + forearm rotations).
        /// <para>⚠️ Does not touch the end bone's OWN rotation: the caller writes it (wrist
        /// orientation comes from the grip, not the arm).</para>
        /// </summary>
        public void Solve(Vector3 target)
        {
            if (_upper == null || _lower == null || _end == null)
            {
                return;
            }

            Vector3 a = _upper.position;
            Vector3 b = _lower.position;
            Vector3 c = _end.position;

            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 at = target - a;

            if (ab.sqrMagnitude < MinSqr || ac.sqrMagnitude < MinSqr || at.sqrMagnitude < MinSqr)
            {
                return;
            }

            float l1 = _upperLength;
            float l2 = _lowerLength;
            float lat = Mathf.Clamp(
                at.magnitude,
                Mathf.Abs(l1 - l2) + ReachEpsilon,
                l1 + l2 - ReachEpsilon);

            // Current interior angles…
            float acAb0 = Mathf.Acos(Mathf.Clamp(Vector3.Dot(ac.normalized, ab.normalized), -1f, 1f));
            float baBc0 = Mathf.Acos(Mathf.Clamp(
                Vector3.Dot((a - b).normalized, (c - b).normalized), -1f, 1f));

            // …and the angles required by the target distance (law of cosines).
            float acAb1 = Mathf.Acos(Mathf.Clamp((l2 * l2 - l1 * l1 - lat * lat) / (-2f * l1 * lat), -1f, 1f));
            float baBc1 = Mathf.Acos(Mathf.Clamp((lat * lat - l1 * l1 - l2 * l2) / (-2f * l1 * l2), -1f, 1f));

            // ⚠️ The plane normal is the SAME for both rotations and read BEFORE them: it is the
            // elbow's current bend direction, and both angle corrections must stay in that plane.
            Vector3 axis = Vector3.Cross(ac, ab);
            if (axis.sqrMagnitude < MinSqr)
            {
                // Arm fully straight: elbow plane undefined. Build one from the target direction;
                // if that is degenerate too, leave it (the arm already points at the target).
                axis = Vector3.Cross(at, ab);
                if (axis.sqrMagnitude < MinSqr)
                {
                    return;
                }
            }

            axis = axis.normalized;

            // ⚠️ LEFT-multiply into world rotation: rotates the bone about `axis` around its own
            // origin. Parent rotation carries the child, so the order must go from upper arm down.
            _upper.rotation = Quaternion.AngleAxis((acAb1 - acAb0) * Mathf.Rad2Deg, axis) * _upper.rotation;
            _lower.rotation = Quaternion.AngleAxis((baBc1 - baBc0) * Mathf.Rad2Deg, axis) * _lower.rotation;

            // Length is settled; what remains is aiming the arm at the target.
            Vector3 aimFrom = _end.position - a;
            if (aimFrom.sqrMagnitude < MinSqr)
            {
                return;
            }

            _upper.rotation = Quaternion.FromToRotation(aimFrom, at) * _upper.rotation;
        }
    }
}
