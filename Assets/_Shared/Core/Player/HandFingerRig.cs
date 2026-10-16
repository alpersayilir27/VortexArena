using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Core.Player
{
    /// <summary>Resolves a hand's finger chains <b>once in bind pose</b>, then RETARGETS the authored
    /// ISDK joint array onto those bones (<see cref="HandPoseLibrary.TryResolveRetargetBones"/>).</summary>
    /// <remarks>
    /// The authored pose crosses between the two skeletons as two measured quantities per bone —
    /// wrist-space ORIENTATION and bone DIRECTION — each read from its OWN skeleton's bind pose. So
    /// curl, spread and thumb opposition all survive, while a raw ISDK quaternion is still never
    /// written onto a humanoid bone (the project's learned rule).
    /// <para>⚠️ The left/right handedness of the palm normal does NOT come from here —
    /// <see cref="HandGripConvention"/> is the single implementation of the cross-product order.</para>
    /// <para>⚠️ Setup MUST happen in bind pose (in <c>Awake</c>, before the retargeter writes bones): a
    /// direction/offset measured on a posed skeleton bakes in that frame's curl and the fingers stay
    /// wrong forever.</para>
    /// </remarks>
    public class HandFingerRig
    {
        /// <summary>Driven joints per finger (the tip bone is not rotated).</summary>
        private const int JointsPerFinger = 3;

        private const int FingerCount = 5;

        /// <summary>Finger suffixes in bone names — same order as ISDK's <c>HandFinger</c>, which is
        /// the order <see cref="HandPoseLibrary.TryResolveRetargetBones"/> fills its slots in.</summary>
        private static readonly string[] FingerNames = { "Thumb", "Index", "Middle", "Ring", "Pinky" };

        /// <summary>Wrist bone name (Mixamo humanoid). ⚠️ Finger bones append to this name
        /// (<c>…LeftHandIndex1</c>), so the lookup must be an exact match — a "starts with" search would
        /// confuse the wrist with the first finger.</summary>
        public const string LeftWristBoneName = "mixamorig:LeftHand";

        /// <inheritdoc cref="LeftWristBoneName"/>
        public const string RightWristBoneName = "mixamorig:RightHand";

        private readonly Transform[] _bones = new Transform[FingerCount * JointsPerFinger];

        /// <summary>Each bone's own direction toward the next joint, in BONE space (bind pose).</summary>
        private readonly Vector3[] _localDirections = new Vector3[FingerCount * JointsPerFinger];

        /// <summary>Bind-pose difference between the ISDK bone and this bone, in the ISDK wrist frame.</summary>
        private readonly Quaternion[] _offsets = new Quaternion[FingerCount * JointsPerFinger];

        // Per-frame retarget output (instance buffers: Apply must not allocate).
        private readonly Quaternion[] _rot = new Quaternion[HandPoseLibrary.RetargetBoneCount];
        private readonly Vector3[] _dir = new Vector3[HandPoseLibrary.RetargetBoneCount];

        private bool _rightHand;

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

        /// <summary>Resolves the finger chains under the wrist and measures their bind-pose
        /// directions and retarget offsets.</summary>
        /// <remarks>Returns <c>null</c> if even one joint or the SDK bind array cannot be resolved —
        /// better than a half-built hand that silently poses some fingers and freezes others.</remarks>
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

            // The SDK side of the pairing, measured in ITS bind pose: the offsets below are the
            // difference between the two skeletons' rest hands, nothing else.
            if (!HandPoseLibrary.TryResolveRetargetBones(
                    HandPoseLibrary.BindJointRotations(rightHand), rightHand, rig._rot, rig._dir))
            {
                return null;
            }

            rig.Wrist = wrist;
            rig._rightHand = rightHand;
            rig.WristCorrection = HandGripConvention.Correction(rightHand, boneBasis);

            Quaternion toWrist = Quaternion.Inverse(wrist.rotation);

            for (int finger = 0; finger < FingerCount; finger++)
            {
                for (int joint = 0; joint < JointsPerFinger; joint++)
                {
                    Transform bone = chains[finger][joint];
                    Transform next = chains[finger][joint + 1];
                    int slot = finger * JointsPerFinger + joint;

                    Vector3 boneDirection = next.position - bone.position;
                    if (boneDirection.sqrMagnitude < 1e-8f)
                    {
                        return null;
                    }

                    rig._bones[slot] = bone;
                    rig._localDirections[slot] =
                        bone.InverseTransformDirection(boneDirection).normalized;
                    rig._offsets[slot] = Quaternion.Inverse(rig._rot[slot]) * rig.WristCorrection *
                                         (toWrist * bone.rotation);
                }
            }

            return rig;
        }

        /// <summary>Retargets an authored ISDK joint array onto this hand. Called per frame; writes
        /// only, never measures.</summary>
        /// <remarks>⚠️ MUST run AFTER the wrist's final rotation for this frame is written: every bone
        /// is derived from the wrist.
        /// <para>Derivation: the Mixamo wrist is <c>sdkWrist * WristCorrection</c>, so
        /// <c>candidate</c> carries the ISDK bone's bind→pose rotation onto this bone's bind rotation;
        /// <c>FromToRotation</c> then pins the exact bone direction, absorbing the two skeletons'
        /// differing bind poses (finger length/spread).</para>
        /// <para>World rotation is written on purpose: <see cref="SkeletonPoseMirror"/> (order 30100)
        /// copies <c>localRotation</c> afterwards, so the second body follows for free.</para></remarks>
        public void Apply(Quaternion[] jointRotations)
        {
            if (Wrist == null ||
                !HandPoseLibrary.TryResolveRetargetBones(jointRotations, _rightHand, _rot, _dir))
            {
                return;
            }

            Quaternion sdkWrist = Wrist.rotation * Quaternion.Inverse(WristCorrection);

            for (int slot = 0; slot < _bones.Length; slot++)
            {
                Transform bone = _bones[slot];
                if (bone == null)
                {
                    continue; // may have been destroyed on a scene change
                }

                Quaternion candidate = sdkWrist * _rot[slot] * _offsets[slot];
                Vector3 target = sdkWrist * _dir[slot];
                bone.rotation =
                    Quaternion.FromToRotation(candidate * _localDirections[slot], target) * candidate;
            }
        }

        /// <summary>Resolves the <c>&lt;wrist&gt;/…Thumb1/…Thumb2/…Thumb3/…Thumb4</c> chain.</summary>
        /// <remarks>Four bones are required: the last one is not rotated but is needed to MEASURE
        /// direction — a joint's bone direction is only defined by the point after it.
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
