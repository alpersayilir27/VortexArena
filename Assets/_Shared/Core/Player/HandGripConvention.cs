using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Core.Player
{
    /// <summary>The SINGLE answer to "how is a tracking-space hand pose converted to a humanoid hand
    /// bone".</summary>
    /// <remarks>
    /// <b>Why a separate class:</b> the networked hand rotation is in <c>OVRCameraRig.leftHandAnchor</c> /
    /// <c>rightHandAnchor</c> space (the controller's pose), while the bone is in the character's bind
    /// axes. The two spaces define "where do the fingers/palm face" differently; without this bridge the
    /// wrist is drawn inverted (measured: 115.4° left, 128.1° right).
    /// <para>⚠️ <b>Narrow scope: the body does NOT go through here.</b> The arm/wrist chain comes from
    /// Movement SDK retargeting, which does its own mapping. Nor does an item's pose in the hand: grip
    /// records are in ANCHOR space (<c>ItemGripPose</c>). Nor the local synthetic wrist relative to the
    /// controller (<c>HandPoseLibrary.AnchorToWrist</c>). Adding a body-related consumer back here would
    /// create a second mapping source alongside retargeting.</para>
    /// <para>Today's only consumer is the REMOTE avatar (<see cref="HandFingerRig"/> →
    /// <see cref="Correction"/>).</para>
    /// <para><b>Derivation:</b> both skeletons must face the same anatomical direction, so
    /// <c>hand.rotation * boneBasis == anchorRotation * anchorBasis</c> →
    /// <c>hand.rotation = anchorRotation * (anchorBasis * Inverse(boneBasis))</c>. The parenthesised
    /// part is <see cref="Correction"/>, computed ONCE per character in bind pose.</para>
    /// <para>⚠️ <b>BOTH sides are MEASURED at runtime</b> via <see cref="TryMeasureBoneBasis"/>, never
    /// hard-coded: the bone side from the character's bind pose, the anchor side from the synthetic
    /// hand's own skeleton (<see cref="AnchorBasis"/>). So neither a new character nor an SDK skeleton
    /// change needs a line here — and since the correction is a ratio of the two, the shared
    /// construction cancels out of it.</para>
    /// </remarks>
    public static class HandGripConvention
    {
        /// <summary>Hand anatomy in controller anchor space — <b>LAST RESORT ONLY</b>, for a skeleton
        /// that cannot be read. Fingers point forward from the controller; the palm faces down and
        /// slightly outward.</summary>
        /// <remarks>
        /// ⚠️ <b>Not a tuning point and not an estimate:</b> these are a snapshot of the same
        /// measurement <see cref="AnchorBasis"/> takes from the synthetic hand's own skeleton. Editing
        /// them by eye splits the hand the player holds from the hand the observer sees — the two ends
        /// stop reading one description.
        /// <para>⚠️ <b>The local hand's wrist does NOT go through here</b> and must not be wired to it:
        /// it is locked to the controller (<c>HandPoseLibrary.AnchorToWrist</c>).</para>
        /// <para>The two vectors need not be perpendicular:
        /// <see cref="Quaternion.LookRotation"/> orthogonalises the second against the first.</para>
        /// </remarks>
        public static readonly Vector3 LeftAnchorFingerDirection = new Vector3(0.018f, -0.027f, 0.999f);
        public static readonly Vector3 LeftAnchorPalmNormal = new Vector3(-0.456f, -0.890f, -0.015f);
        public static readonly Vector3 RightAnchorFingerDirection = new Vector3(-0.018f, -0.027f, 0.999f);
        public static readonly Vector3 RightAnchorPalmNormal = new Vector3(0.456f, -0.890f, -0.015f);

        // ⚠️ The anchor→wrist delta is NOT here and is not added back: it is a DEFINITION (where the
        // wrist locks onto the controller), not a mapping, and its sole owner is
        // VortexArena.Core.Combat.HandPoseLibrary.AnchorToWrist. While it lived here it stayed a
        // "constant to be measured on device and pasted in", was never measured, and left the studio and
        // the game out of sync.

        /// <summary>Minimum squared length for a direction vector to count as meaningful.</summary>
        private const float MinDirectionSqrMagnitude = 1e-8f;

        /// <summary>If finger/thumb directions become parallel no palm normal exists (cross ≈ 0).</summary>
        private const float MinCrossSqrMagnitude = 1e-6f;

        /// <summary>Anatomical basis in controller anchor space.</summary>
        /// <remarks>
        /// ⚠️ <b>MEASURED from the synthetic hand's own skeleton</b>
        /// (<see cref="HandPoseLibrary.TryMeasureWristAnatomy"/>), never estimated: the wrist's rotation
        /// on the anchor is DEFINED as identity (<c>HandPoseLibrary.AnchorToWrist</c>), so "how the hand
        /// faces relative to the controller" is already answered by the hand the player is holding.
        /// Guessing it a second time produces a hand that faces the right way and is drawn rolled about
        /// the finger axis — the term nothing else pins down, and the one the studio's ghost hand was
        /// caught on before.
        /// <para>⚠️ <b>Both sides of <see cref="Correction"/> go through
        /// <see cref="TryMeasureBoneBasis"/></b> and must keep doing so: the correction is a ratio of
        /// two bases, so a shared convention cancels out of it exactly — two constructions leave their
        /// difference in the drawn wrist.</para>
        /// <para>Falls back to the last-resort constants when the SDK skeleton is unreadable.</para>
        /// </remarks>
        public static Quaternion AnchorBasis(bool rightHand)
        {
            if (HandPoseLibrary.TryMeasureWristAnatomy(rightHand, out Vector3 fingerDirection,
                    out Vector3 thumbDirection) &&
                TryMeasureBoneBasis(fingerDirection, thumbDirection, rightHand, out Quaternion measured))
            {
                return measured;
            }

            return rightHand
                ? Quaternion.LookRotation(RightAnchorFingerDirection, RightAnchorPalmNormal)
                : Quaternion.LookRotation(LeftAnchorFingerDirection, LeftAnchorPalmNormal);
        }

        /// <summary>Measures a skeleton's hand anatomy from its OWN bind pose (in hand-LOCAL space):
        /// finger direction = hand→middle, thumb direction = hand→thumb, palm normal = their cross
        /// product.</summary>
        /// <remarks>
        /// ⚠️ The cross-product order depends on HANDEDNESS (mirrored skeletons would give an inverted
        /// normal for the same order); the rule is written only in the <see cref="Vector3"/> overload
        /// below, which this version delegates to — never copied elsewhere.
        /// <para>⚠️ <b>Must be called in bind pose</b> (BEFORE the solver writes bones): a basis measured
        /// on a posed skeleton bakes in that frame's pose and the correction is permanently wrong.</para>
        /// <para>Finger bones are OPTIONAL in humanoid; if missing or the directions are degenerate it
        /// returns <c>false</c> — the caller leaves the correction at identity and warns (explicit
        /// failure).</para>
        /// </remarks>
        public static bool TryMeasureBoneBasis(
            Transform hand,
            Transform middleProximal,
            Transform thumbProximal,
            bool rightHand,
            out Quaternion basis)
        {
            basis = Quaternion.identity;

            if (hand == null || middleProximal == null || thumbProximal == null)
            {
                return false;
            }

            return TryMeasureBoneBasis(
                hand.InverseTransformDirection(middleProximal.position - hand.position),
                hand.InverseTransformDirection(thumbProximal.position - hand.position),
                rightHand,
                out basis);
        }

        /// <summary>Same measurement with the directions supplied <b>ready-made</b> (hand-LOCAL space), so
        /// skeletons without bone <see cref="Transform"/>s (ISDK's data skeleton,
        /// <c>HandPoseLibrary</c>) can use this gate too.</summary>
        /// <remarks>⚠️ The cross-product <b>order rule lives ONLY HERE</b> (LEFT: <c>Cross(thumb,
        /// finger)</c>, RIGHT: <c>Cross(finger, thumb)</c>) and the
        /// <see cref="TryMeasureBoneBasis(Transform, Transform, Transform, bool, out Quaternion)"/>
        /// overload delegates to it: with two copies one would get fixed and the other forgotten,
        /// leaving one hand's palm normal silently inverted.</remarks>
        public static bool TryMeasureBoneBasis(
            Vector3 fingerDirectionLocal,
            Vector3 thumbDirectionLocal,
            bool rightHand,
            out Quaternion basis)
        {
            basis = Quaternion.identity;

            if (fingerDirectionLocal.sqrMagnitude < MinDirectionSqrMagnitude ||
                thumbDirectionLocal.sqrMagnitude < MinDirectionSqrMagnitude)
            {
                return false;
            }

            Vector3 fingerDirection = fingerDirectionLocal.normalized;
            Vector3 thumbDirection = thumbDirectionLocal.normalized;

            Vector3 palmNormal = rightHand
                ? Vector3.Cross(fingerDirection, thumbDirection)
                : Vector3.Cross(thumbDirection, fingerDirection);

            if (palmNormal.sqrMagnitude < MinCrossSqrMagnitude)
            {
                return false;
            }

            basis = Quaternion.LookRotation(fingerDirection, palmNormal.normalized);
            return true;
        }

        /// <summary>Correction to multiply on the RIGHT of a tracking-space rotation:
        /// <c>hand.rotation = anchorRotation * Correction(...)</c>.</summary>
        /// <remarks>⚠️ <b>No manual adjustment term, and none comes back:</b> both bases are measured
        /// now, so a hand-turned offset here would not correct an unknown — it would bend the drawn
        /// wrist away from the measurement and hide whichever side actually broke.</remarks>
        public static Quaternion Correction(bool rightHand, Quaternion boneBasisLocal)
        {
            return AnchorBasis(rightHand) * Quaternion.Inverse(boneBasisLocal);
        }
    }
}
