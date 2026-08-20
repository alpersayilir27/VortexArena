using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Core.Player
{
    /// <summary>Resolves a hand's finger chains <b>once in bind pose</b>, then applies
    /// <see cref="HandPoseProfile"/> curl ratios on that skeleton's OWN axes.</summary>
    /// <remarks>
    /// ⚠️ The bend axis is MEASURED, not hard-coded (same rationale as <c>HandGripConvention</c>): a
    /// different model must not require a line change here. Axis = bone direction × palm normal, both
    /// read in bind pose, with its sign settled by a self-check.
    /// <para>⚠️ The left/right handedness of the palm normal does NOT come from here —
    /// <see cref="HandGripConvention"/> is the single implementation of the cross-product order; only
    /// the palm normal is read out of the basis it returns.</para>
    /// <para>⚠️ Setup MUST happen in bind pose (in <c>Awake</c>, before the retargeter writes bones): an
    /// axis measured on a posed skeleton bakes in that frame's curl and the fingers bend wrong
    /// forever.</para>
    /// </remarks>
    public class HandFingerRig
    {
        /// <summary>Driven joints per finger (the tip bone is not rotated).</summary>
        private const int JointsPerFinger = 3;

        private const int FingerCount = 5;

        /// <summary>Small probe angle used for the bend-axis sign self-check (degrees).</summary>
        private const float SignProbeDegrees = 10f;

        /// <summary>Finger suffixes in bone names — same order as <see cref="HandPoseProfile.Get"/>.</summary>
        private static readonly string[] FingerNames = { "Thumb", "Index", "Middle", "Ring", "Pinky" };

        /// <summary>Wrist bone name (Mixamo humanoid). ⚠️ Finger bones append to this name
        /// (<c>…LeftHandIndex1</c>), so the lookup must be an exact match — a "starts with" search would
        /// confuse the wrist with the first finger.</summary>
        public const string LeftWristBoneName = "mixamorig:LeftHand";

        /// <inheritdoc cref="LeftWristBoneName"/>
        public const string RightWristBoneName = "mixamorig:RightHand";

        /// <summary>Degrees per joint at full curl. The thumb differs: anatomically it curls less and
        /// lies on top of the grip rather than wrapping it.</summary>
        private static readonly float[] FingerMaxAngles = { 50f, 60f, 40f };
        private static readonly float[] ThumbMaxAngles = { 25f, 35f, 30f };

        private readonly Transform[] _bones = new Transform[FingerCount * JointsPerFinger];
        private readonly Quaternion[] _bindLocalRotations = new Quaternion[FingerCount * JointsPerFinger];
        private readonly Vector3[] _bendAxes = new Vector3[FingerCount * JointsPerFinger];

        /// <summary>This hand's wrist bone.</summary>
        public Transform Wrist { get; private set; }

        /// <summary>Correction turning a tracking/grip-space hand pose into this skeleton's wrist:
        /// <c>wrist.rotation = palmPose.rotation * WristCorrection</c>.</summary>
        /// <remarks>⚠️ <b>Stored</b>, never re-measured: it comes from
        /// <see cref="HandGripConvention.TryMeasureBoneBasis"/>, which must run in bind pose. Measuring
        /// again elsewhere would require re-proving that guarantee at the new site — and it would sooner
        /// or later be copied without it.</remarks>
        public Quaternion WristCorrection { get; private set; } = Quaternion.identity;

        /// <summary>Finds the wrist by name under the body root and hands it to <see cref="TryBuild"/>,
        /// so bone-name knowledge does not leak out of this class.</summary>
        public static HandFingerRig TryBuildFromBody(Transform bodyRoot, bool rightHand)
        {
            if (bodyRoot == null)
            {
                return null;
            }

            string wanted = rightHand ? RightWristBoneName : LeftWristBoneName;
            Transform[] all = bodyRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == wanted)
                {
                    return TryBuild(all[i], rightHand);
                }
            }

            return null;
        }

        /// <summary>Resolves the finger chains under the wrist and measures their bend axes.</summary>
        /// <remarks>Returns <c>null</c> if even one joint cannot be resolved — better than a half-built
        /// hand that silently animates some fingers and freezes others.</remarks>
        /// <param name="wrist">Wrist bone (e.g. <c>mixamorig:LeftHand</c>).</param>
        /// <param name="rightHand">Right hand? (sign of the palm normal).</param>
        public static HandFingerRig TryBuild(Transform wrist, bool rightHand)
        {
            if (wrist == null)
            {
                return null;
            }

            var rig = new HandFingerRig();
            var chains = new Transform[FingerCount][];

            for (int finger = 0; finger < FingerCount; finger++)
            {
                chains[finger] = ResolveChain(wrist, FingerNames[finger]);
                if (chains[finger] == null)
                {
                    return null;
                }
            }

            // Palm normal: measured from the middle and thumb ROOT joints (in bind pose).
            if (!HandGripConvention.TryMeasureBoneBasis(
                    wrist, chains[2][0], chains[0][0], rightHand, out Quaternion boneBasis))
            {
                return null;
            }

            Vector3 palmNormalWorld = wrist.TransformDirection(boneBasis * Vector3.up);
            if (palmNormalWorld.sqrMagnitude < 1e-8f)
            {
                return null;
            }

            palmNormalWorld = palmNormalWorld.normalized;

            rig.Wrist = wrist;
            rig.WristCorrection = HandGripConvention.Correction(rightHand, boneBasis);

            for (int finger = 0; finger < FingerCount; finger++)
            {
                for (int joint = 0; joint < JointsPerFinger; joint++)
                {
                    Transform bone = chains[finger][joint];
                    Transform next = chains[finger][joint + 1];
                    int slot = finger * JointsPerFinger + joint;

                    Vector3 boneDirection = next.position - bone.position;
                    if (boneDirection.sqrMagnitude < 1e-8f || bone.parent == null)
                    {
                        return null;
                    }

                    boneDirection = boneDirection.normalized;

                    Vector3 axisWorld = Vector3.Cross(boneDirection, palmNormalWorld);
                    if (axisWorld.sqrMagnitude < 1e-8f)
                    {
                        return null;
                    }

                    axisWorld = axisWorld.normalized;

                    // ⚠️ The sign is fixed by SELF-CHECK, never by cross-product order — the same rule
                    // as HandPoseLibrary's hinge table and for the same reason: a positive rotation
                    // must carry the fingertip INTO the palm, and an order taken on trust bends every
                    // finger toward the BACK of the hand. That failure raises no error and reads on
                    // site as "the remote player's hand opens inside out".
                    Vector3 probed = Quaternion.AngleAxis(SignProbeDegrees, axisWorld) * boneDirection;
                    if (Vector3.Dot(probed - boneDirection, palmNormalWorld) <= 0f)
                    {
                        axisWorld = -axisWorld;
                    }

                    rig._bones[slot] = bone;
                    rig._bindLocalRotations[slot] = bone.localRotation;

                    // Stored in PARENT space: a hinge axis is fixed in the joint and turns with the
                    // parent — exactly like a real finger joint.
                    rig._bendAxes[slot] = bone.parent.InverseTransformDirection(axisWorld).normalized;
                }
            }

            return rig;
        }

        /// <summary>Applies the pose. Called per frame; writes only, never measures.</summary>
        public void Apply(in HandPoseProfile profile)
        {
            for (int finger = 0; finger < FingerCount; finger++)
            {
                float curl = Mathf.Clamp01(profile.Get(finger));
                float[] maxAngles = finger == 0 ? ThumbMaxAngles : FingerMaxAngles;

                for (int joint = 0; joint < JointsPerFinger; joint++)
                {
                    int slot = finger * JointsPerFinger + joint;
                    Transform bone = _bones[slot];
                    if (bone == null)
                    {
                        continue; // may have been destroyed on a scene change
                    }

                    bone.localRotation =
                        Quaternion.AngleAxis(curl * maxAngles[joint], _bendAxes[slot]) *
                        _bindLocalRotations[slot];
                }
            }
        }

        /// <summary>Resolves the <c>&lt;wrist&gt;/…Thumb1/…Thumb2/…Thumb3/…Thumb4</c> chain.</summary>
        /// <remarks>Four bones are required: the last one is not rotated but is needed to MEASURE
        /// direction — a joint's bend axis is only defined by the point after it.
        /// <para>⚠️ The lookup searches the whole subtree by exact name, NOT via two levels of
        /// <c>Find</c>: some models insert intermediate nodes, and silently applying no pose is the
        /// hardest failure to diagnose.</para></remarks>
        private static Transform[] ResolveChain(Transform wrist, string fingerName)
        {
            string prefix = wrist.name + fingerName;
            var chain = new Transform[JointsPerFinger + 1];
            Transform[] all = wrist.GetComponentsInChildren<Transform>(true);

            for (int i = 0; i < chain.Length; i++)
            {
                string wanted = prefix + (i + 1).ToString();
                for (int b = 0; b < all.Length; b++)
                {
                    if (all[b] != null && all[b].name == wanted)
                    {
                        chain[i] = all[b];
                        break;
                    }
                }

                if (chain[i] == null)
                {
                    return null;
                }
            }

            return chain;
        }
    }
}
