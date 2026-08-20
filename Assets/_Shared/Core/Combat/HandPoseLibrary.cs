using System;
using System.Collections.Generic;
using Oculus.Interaction.Input;
using UnityEngine;
using VortexArena.Core.Player;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// The single distribution gate for finger poses: the idle hand pose, converting a
    /// weapon-specific rigged pose into an ISDK joint array, reducing that pose to curl ratios for
    /// the humanoid hand, and the duration/curve of transitions between poses.
    /// <para>Two consumers, two forms. The local synthetic hand (ISDK) receives RAW rotations per
    /// joint — the player sees their own hand up close, so the fine detail belongs there and what
    /// is rigged in the studio is what is seen in game. The remote avatar's Mixamo hand CANNOT take
    /// raw rotations (the two skeletons' bone axes differ; the project's learned rule: a rotation
    /// from tracking/network space is not written directly onto a humanoid bone) — it receives five
    /// curl ratios MEASURED from the rigged pose (<see cref="MeasureCurl"/>,
    /// <see cref="HandPoseProfile"/>). The ratio is not a second source of truth: it is never
    /// stored, always derived.</para>
    /// <para>⚠️ Fingers are NEVER driven by hardware — not by the controller trigger/grip, not by
    /// hand tracking. Each frame a hand is either in the idle pose or in the held item's pose rigged
    /// for that slot, smoothed over <see cref="TransitionSeconds"/>. There is no "free" joint
    /// (<c>JointFreedom.Free</c>) and none comes back: leaving even one finger to hardware means the
    /// studio hand and the in-game hand diverge on that finger.</para>
    /// <para>⚠️ The bind pose and hinge axes are MEASURED from ISDK's own skeleton, never hardcoded
    /// (<see cref="HandSkeleton.DefaultLeftSkeleton"/> / <c>DefaultRightSkeleton</c> — ISDK's
    /// <c>HandPose</c> constructor uses exactly these rotations for the open hand, so that is the
    /// baseline). So an SDK skeleton change needs no line changed here; the project's recurring
    /// "measure, do not hardcode" rule (same rationale as <see cref="HandFingerRig"/>).</para>
    /// <para>⚠️ The volar (palm) direction is derived FROM THE DATA too: a relaxed skeleton's
    /// fingers already curl slightly toward the palm, so "which side is the palm" is answered by the
    /// skeleton itself. Relying on a thumb/palm-normal convention (left/right cross product order)
    /// invites a sign error whose symptom would be "the fingers bend toward the back of the
    /// hand".</para>
    /// <para>⚠️ METACARPALS are NOT driven and do NOT enter the record
    /// (<c>FingersMetadata.HAND_JOINT_CAN_MOVE</c>). Not just because "they move with the wrist and
    /// rotating them fans the hand out": on the OpenXR branch
    /// <c>SyntheticHand.AmendMetacarpalRotation</c> expects the rotation given for
    /// <c>Index1/Middle1/Ring1</c> in WRIST space and undoes the metacarpal's rotation. Since those
    /// three metacarpals' bind rotation is IDENTITY in the default skeleton, "local to the
    /// metacarpal" and "relative to the wrist" are the same today — opening metacarpals to rigging
    /// breaks exactly that equality, giving a hand that is right on the workbench and displaced in
    /// game (that identity is this tool's entire job).</para>
    /// </summary>
    public static class HandPoseLibrary
    {
        /// <summary>Finger count (guaranteed by ISDK).</summary>
        private const int FingerCount = 5;

        /// <summary>
        /// Degrees per drivable joint at full curl — the SAME numbers as the table in
        /// <see cref="HandFingerRig"/>. The thumb differs: anatomically it curls less and lies on
        /// top of the grip rather than wrapping it. Reducing a rigged pose to curl ratios
        /// (<see cref="MeasureCurl"/>) uses this table as the denominator, so "fully closed" means
        /// the same on both ends.
        /// </summary>
        private static readonly float[] FingerMaxAngles = { 50f, 60f, 40f };
        private static readonly float[] ThumbMaxAngles = { 25f, 35f, 30f };

        /// <summary>Minimum squared length for a direction vector to count as meaningful.</summary>
        private const float MinDirectionSqr = 1e-8f;

        /// <summary>
        /// Below this total bend the volar direction cannot be derived from the data (a perfectly
        /// straight-fingered skeleton) and the <see cref="HandGripConvention"/> basis is used.
        /// </summary>
        private const float MinVolarMagnitude = 1e-4f;

        /// <summary>Small probe angle used for the sign self-check (degrees).</summary>
        private const float SignProbeDegrees = 10f;

        /// <summary>
        /// A drivable joint's measured hinge: its array slot, its finger, its rotation axis in the
        /// PARENT frame, its bind rotation and its angle at full curl.
        /// <para>The table is used both for pose GENERATION (ratio → rotation) and pose MEASUREMENT
        /// (rotation → ratio); it lives in one place so both directions come from one geometry.</para>
        /// </summary>
        private struct Hinge
        {
            public int Slot;
            public int Finger;
            public Vector3 AxisLocal;
            public Quaternion Bind;
            public float MaxAngle;
        }

        /// <summary>Caches per hand ([0] = left, [1] = right): building them involves skeleton
        /// resolution + axis measurement, so they are not cheap, and they are read per frame.</summary>
        private static readonly Quaternion[][] BindCache = new Quaternion[2][];
        private static readonly Hinge[][] HingeCache = new Hinge[2][];
        private static readonly Quaternion[][] IdleCache = new Quaternion[2][];
        private static readonly Pose[] WristCache = new Pose[2];
        private static readonly bool[] WristResolved = new bool[2];
        private static readonly Vector3[] FingerDirectionCache = new Vector3[2];
        private static readonly Vector3[] ThumbDirectionCache = new Vector3[2];
        private static readonly bool[] AnatomyResolved = new bool[2];
        private static readonly bool[] AnatomyValid = new bool[2];

        /// <summary>
        /// Offset from the controller anchor to the hand's WRIST (anchor space, metres) — a
        /// <b>DEFINED</b> value, not a measured one: the palm centre sits on the controller and the
        /// hand is aligned with it.
        /// <para>
        /// <b>Why defined:</b> the hand seen in game is ISDK's synthetic hand, and unless locked its
        /// wrist comes from Meta's controller-synthesized "natural" pose — an offset we know nowhere.
        /// The weapon meanwhile is positioned from the anchor (grip records are in anchor space). Two
        /// references meant the bench hand and the in-game hand diverging, and the only way to close
        /// the gap would have been measuring that offset in the headset and pasting a constant.
        /// Defining it ourselves leaves nothing to measure: <b>bench and game are identical by
        /// construction</b>. Fingers are not hardware-driven and hand tracking is unused — this was
        /// the last dependency on Meta's hand side.
        /// </para>
        /// <para>
        /// ⚠️ <b>The number is NOT hardcoded, it is computed from the skeleton</b> (palm centre = half
        /// of wrist→middle proximal, the OpenXR definition): so a new hand model needs no line changed
        /// here — the project's recurring "measure, do not hardcode" rule.
        /// </para>
        /// <para>
        /// ⚠️ <b>The rotation is IDENTITY and is not filled in by guessing.</b> Adding an anatomical
        /// tilt means inventing an unmeasured constant; tried once, it drifted ~70° about the finger
        /// axis.
        /// </para>
        /// </summary>
        public static Pose AnchorToWrist(bool rightHand)
        {
            int hand = rightHand ? 1 : 0;
            if (WristResolved[hand])
            {
                return WristCache[hand];
            }

            WristResolved[hand] = true;
            WristCache[hand] = MeasurePalmCentre(rightHand);
            return WristCache[hand];
        }

        /// <summary>
        /// The synthetic hand's own anatomy in its WRIST frame: finger direction (wrist → middle
        /// proximal) and thumb direction (wrist → the thumb's first joint).
        /// <para>
        /// ⚠️ <b>This is the CONTROLLER ANCHOR's anatomy as well — by construction, not by
        /// coincidence:</b> <see cref="AnchorToWrist"/> DEFINES the wrist's rotation on the anchor as
        /// identity, so "how the hand faces relative to the anchor" and "how this skeleton faces
        /// relative to its own wrist" are one quantity. <c>HandGripConvention.AnchorBasis</c> reads it
        /// from here instead of describing the same hand a second time.
        /// </para>
        /// <para>Returns <c>false</c> when the SDK skeleton is unreadable — the caller falls back to
        /// its own last-resort definition.</para>
        /// </summary>
        public static bool TryMeasureWristAnatomy(bool rightHand, out Vector3 fingerDirection,
            out Vector3 thumbDirection)
        {
            int hand = rightHand ? 1 : 0;

            if (!AnatomyResolved[hand])
            {
                AnatomyResolved[hand] = true;
                AnatomyValid[hand] = MeasureWristAnatomy(rightHand,
                    out FingerDirectionCache[hand], out ThumbDirectionCache[hand]);
            }

            fingerDirection = FingerDirectionCache[hand];
            thumbDirection = ThumbDirectionCache[hand];
            return AnatomyValid[hand];
        }

        /// <summary>Reads the SDK skeleton once for <see cref="TryMeasureWristAnatomy"/>.</summary>
        private static bool MeasureWristAnatomy(bool rightHand, out Vector3 fingerDirection,
            out Vector3 thumbDirection)
        {
            fingerDirection = Vector3.zero;
            thumbDirection = Vector3.zero;

            HandSkeleton skeleton = rightHand
                ? HandSkeleton.DefaultRightSkeleton
                : HandSkeleton.DefaultLeftSkeleton;

            HandSkeletonJoint[] joints = skeleton != null ? skeleton.joints : null;
            if (joints == null)
            {
                return false;
            }

            ResolveHandSpace(joints, out Vector3[] handPos, out Quaternion[] handRot);

            return TryMeasureAnatomyDirections(joints, handPos, handRot, out _,
                out fingerDirection, out thumbDirection);
        }

        /// <summary>
        /// Measures the palm centre from the skeleton and pushes the wrist BACK by that much (so the
        /// palm lands on the controller). Identity when the skeleton is unreadable — the wrist is
        /// assumed to sit right on the controller.
        /// </summary>
        private static Pose MeasurePalmCentre(bool rightHand)
        {
            HandSkeleton skeleton = rightHand
                ? HandSkeleton.DefaultRightSkeleton
                : HandSkeleton.DefaultLeftSkeleton;

            HandSkeletonJoint[] joints = skeleton != null ? skeleton.joints : null;
            if (joints == null)
            {
                return Pose.identity;
            }

            int root = RootIndex(joints);
            HandJointId[] middle = ChainOf((int)HandFinger.Middle);

            // ⚠️ The middle finger's PROXIMAL is wanted, not its metacarpal: the chain always ends
            // with "… proximal, intermediate, distal, tip", so it is the fourth from the end (correct
            // on ISDK branches with and without metacarpals).
            if (root < 0 || middle == null || middle.Length < 4)
            {
                return Pose.identity;
            }

            ResolveHandSpace(joints, out Vector3[] handPos, out Quaternion[] handRot);

            int proximal = (int)middle[middle.Length - 4];
            if (proximal < 0 || proximal >= handPos.Length)
            {
                return Pose.identity;
            }

            Vector3 palmLocal =
                Quaternion.Inverse(handRot[root]) * (handPos[proximal] - handPos[root]) * 0.5f;
            return new Pose(-palmLocal, Quaternion.identity);
        }

        /// <summary>
        /// Duration of a transition from one pose to another (seconds) — how long an empty hand takes
        /// to close into a grip pose and to open again on release.
        /// <para>
        /// ⚠️ <b>One number, both ends:</b> the local synthetic hand (<see cref="HandGripPoser"/>) and
        /// the remote avatar's hand (<c>RemoteHandPoser</c>) use the same duration, so the same grip
        /// does not close at different speeds on two screens. Kept short: the weapon arrives in the
        /// hand instantly (<c>Weapon.ApplyCanonicalGrip</c>), and fingers visibly lagging behind it
        /// make the hand pass through the weapon for a moment.
        /// </para>
        /// </summary>
        public const float TransitionSeconds = 0.3f;

        /// <summary>
        /// Transition curve: converts <c>0..1</c> progress into a smoothstepped blend ratio. Both ends
        /// use the same curve (same rationale as <see cref="TransitionSeconds"/>).
        /// </summary>
        public static float Ease(float progress)
        {
            float t = Mathf.Clamp01(progress);
            return t * t * (3f - 2f * t);
        }

        // ------------------------------------------------------------------ empty hand

        /// <summary>
        /// The <b>empty hand's</b> joint array — in <see cref="FingersMetadata.HAND_JOINT_IDS"/> order
        /// (the form <c>SyntheticHand.OverrideAllJoints</c> expects).
        /// <para>The hand returns to this on release, and a slot with an unrigged grip falls back here:
        /// no hand in the project stays "flat".</para>
        /// <para>⚠️ The returned array is <b>CACHED and shared</b>: the caller does NOT modify it.</para>
        /// </summary>
        public static Quaternion[] IdleJointRotations(bool rightHand)
        {
            int hand = rightHand ? 1 : 0;
            return IdleCache[hand] ??= BuildProfile(HandPoseProfile.Idle, rightHand);
        }

        // --------------------------------------------------------- rigged pose

        /// <summary>
        /// Converts a weapon-specific rigged pose into an ISDK joint array: the base is each joint's
        /// <b>bind</b> rotation, overwritten by the joints matching the record's names.
        /// <para>⚠️ The returned array is <b>NEW</b> (not the shared cache): the caller must cache it on
        /// its side — this is not a per-frame path
        /// (<see cref="ItemDefinition.GripJointRotations"/> does that).</para>
        /// <para>An empty record returns a <b>copy</b> of the idle array.</para>
        /// </summary>
        public static Quaternion[] BuildJointRotations(HandJointRotation[] joints, bool rightHand)
        {
            if (joints == null || joints.Length == 0)
            {
                return Copy(IdleJointRotations(rightHand));
            }

            Quaternion[] result = Copy(Bind(rightHand));

            for (int i = 0; i < joints.Length; i++)
            {
                int slot = SlotOf(joints[i].joint);
                if (slot < 0 || slot >= result.Length)
                {
                    // ⚠️ Skipping silently is DELIBERATE: an unrecognized joint name is normal after an
                    // ISDK branch change (see HandJointRotation) and this runs on a cache miss, not per
                    // frame — logging would only fill the console.
                    continue;
                }

                result[slot] = joints[i].rotation;
            }

            return result;
        }

        /// <summary>
        /// The rigged pose's counterpart <b>for the humanoid hand</b>: curl ratio per finger.
        /// <para>
        /// Measurement is the inverse of generation: at each drivable joint the bind→authored rotation
        /// is projected <b>signed</b> onto that joint's hinge axis; the finger's total deflection is
        /// divided by the same finger's full-curl total. Signed is mandatory — with absolute angles a
        /// finger bent backwards would look curled forwards on the remote side.
        /// </para>
        /// <para>An empty record returns the idle ratios.</para>
        /// </summary>
        public static HandPoseProfile MeasureCurl(HandJointRotation[] joints, bool rightHand)
        {
            if (joints == null || joints.Length == 0)
            {
                return HandPoseProfile.Idle;
            }

            Quaternion[] pose = BuildJointRotations(joints, rightHand);
            Hinge[] hinges = Hinges(rightHand);

            var signed = new float[FingerCount];
            var total = new float[FingerCount];

            for (int i = 0; i < hinges.Length; i++)
            {
                Hinge hinge = hinges[i];
                if (hinge.Slot < 0 || hinge.Slot >= pose.Length ||
                    hinge.Finger < 0 || hinge.Finger >= FingerCount)
                {
                    continue;
                }

                (pose[hinge.Slot] * Quaternion.Inverse(hinge.Bind))
                    .ToAngleAxis(out float angle, out Vector3 axis);

                // ToAngleAxis returns 0..360; beyond 180 is a small rotation the other way.
                if (angle > 180f)
                {
                    angle -= 360f;
                }

                if (float.IsNaN(angle) || float.IsInfinity(angle) || float.IsNaN(axis.x))
                {
                    angle = 0f;
                }

                signed[hinge.Finger] += angle * (Vector3.Dot(axis, hinge.AxisLocal) >= 0f ? 1f : -1f);
                total[hinge.Finger] += hinge.MaxAngle;
            }

            return new HandPoseProfile
            {
                thumb = Ratio(signed[0], total[0]),
                index = Ratio(signed[1], total[1]),
                middle = Ratio(signed[2], total[2]),
                ring = Ratio(signed[3], total[3]),
                pinky = Ratio(signed[4], total[4]),
            };
        }

        /// <summary>
        /// Is this joint <b>riggable</b> (metacarpals excluded — rationale in the class warning). The
        /// studio filters both the selectable joints and the recorded ones through this.
        /// </summary>
        public static bool IsDrivable(HandJointId id)
        {
            int slot = FingersMetadata.HandJointIdToIndex(id);
            return slot >= 0 && slot < FingersMetadata.HAND_JOINT_CAN_MOVE.Length &&
                   FingersMetadata.HAND_JOINT_CAN_MOVE[slot];
        }

        private static float Ratio(float signed, float total)
        {
            return total > 0f ? Mathf.Clamp01(signed / total) : 0f;
        }

        private static Quaternion[] Copy(Quaternion[] source)
        {
            var result = new Quaternion[source.Length];
            Array.Copy(source, result, source.Length);
            return result;
        }

        /// <summary>Array slot of a joint name (<c>HandJointId</c>); <c>-1</c> when unrecognized.</summary>
        private static int SlotOf(string jointName)
        {
            if (string.IsNullOrEmpty(jointName) ||
                !Enum.TryParse(jointName, false, out HandJointId id))
            {
                return -1;
            }

            return FingersMetadata.HandJointIdToIndex(id);
        }

        // ------------------------------------------------------------------ measurement

        /// <summary>The hand's bind (open skeleton) rotations — shared cache, never modified.</summary>
        private static Quaternion[] Bind(bool rightHand)
        {
            EnsureTables(rightHand);
            return BindCache[rightHand ? 1 : 0];
        }

        /// <summary>The hand's drivable joint hinges — shared cache, never modified.</summary>
        private static Hinge[] Hinges(bool rightHand)
        {
            EnsureTables(rightHand);
            return HingeCache[rightHand ? 1 : 0];
        }

        /// <summary>
        /// Measures a hand's bind array and hinge table from ISDK's default skeleton: the skeleton is
        /// composed into hand space, the palm direction is derived from the data, and each drivable
        /// joint gets a hinge axis perpendicular to its own bone direction.
        /// </summary>
        private static void EnsureTables(bool rightHand)
        {
            int hand = rightHand ? 1 : 0;
            if (BindCache[hand] != null)
            {
                return;
            }

            HandSkeleton skeleton = rightHand
                ? HandSkeleton.DefaultRightSkeleton
                : HandSkeleton.DefaultLeftSkeleton;

            HandJointId[] ids = FingersMetadata.HAND_JOINT_IDS;
            var bind = new Quaternion[ids.Length];
            HandSkeletonJoint[] joints = skeleton != null ? skeleton.joints : null;

            if (joints == null)
            {
                for (int i = 0; i < bind.Length; i++)
                {
                    bind[i] = Quaternion.identity;
                }

                BindCache[hand] = bind;
                HingeCache[hand] = Array.Empty<Hinge>();
                return;
            }

            for (int i = 0; i < ids.Length; i++)
            {
                int raw = (int)ids[i];
                bind[i] = raw >= 0 && raw < joints.Length
                    ? joints[raw].pose.rotation
                    : Quaternion.identity;
            }

            BindCache[hand] = bind;

            ResolveHandSpace(joints, out Vector3[] handPos, out Quaternion[] handRot);

            if (!TryResolveVolar(joints, handPos, handRot, rightHand, out Vector3 volar))
            {
                // Palm direction unresolved: the bend direction is undefined, the hinge table stays
                // empty and the only pose produced is the bind (open hand) itself.
                HingeCache[hand] = Array.Empty<Hinge>();
                return;
            }

            var hinges = new List<Hinge>(ids.Length);

            for (int finger = 0; finger < FingerCount; finger++)
            {
                HandJointId[] chain = ChainOf(finger);
                if (chain == null || chain.Length < 2)
                {
                    continue;
                }

                float[] maxAngles = finger == (int)HandFinger.Thumb ? ThumbMaxAngles : FingerMaxAngles;
                int drivable = 0;

                // The last element is the TIP: never rotated, but it defines the previous joint's bone
                // direction.
                for (int j = 0; j < chain.Length - 1; j++)
                {
                    int raw = (int)chain[j];
                    int next = (int)chain[j + 1];
                    int slot = FingersMetadata.HandJointIdToIndex(chain[j]);
                    if (slot < 0 || slot >= bind.Length || raw < 0 || raw >= joints.Length)
                    {
                        continue;
                    }

                    // ⚠️ Metacarpals are NOT driven (rationale in the class warning). The drivable-joint
                    // counter therefore SKIPS them — the angle table starts at the proximal.
                    if (!FingersMetadata.HAND_JOINT_CAN_MOVE[slot])
                    {
                        continue;
                    }

                    Vector3 bone = handPos[next] - handPos[raw];
                    if (bone.sqrMagnitude < MinDirectionSqr)
                    {
                        drivable++;
                        continue;
                    }

                    bone = bone.normalized;
                    Vector3 axis = Vector3.Cross(bone, volar);
                    if (axis.sqrMagnitude < MinDirectionSqr)
                    {
                        drivable++;
                        continue;
                    }

                    axis = axis.normalized;

                    // ⚠️ The sign is fixed by SELF-CHECK; Unity's rotation-direction convention is NOT
                    // guessed: if a small positive rotation about the axis does not move the bone toward
                    // the palm, the axis is inverted. Guessed, the symptom would be "fingers bend toward
                    // the back of the hand" with no diagnosis but trying the opposite.
                    Vector3 probed = Quaternion.AngleAxis(SignProbeDegrees, axis) * bone;
                    if (Vector3.Dot(probed - bone, volar) <= 0f)
                    {
                        axis = -axis;
                    }

                    int parent = joints[raw].parent;
                    Quaternion parentRotation = parent >= 0 && parent < handRot.Length
                        ? handRot[parent]
                        : Quaternion.identity;

                    // The axis is converted into the PARENT frame: local rotation is written relative to
                    // the parent, and the hinge axis is fixed in the joint, turning with the parent.
                    hinges.Add(new Hinge
                    {
                        Slot = slot,
                        Finger = finger,
                        AxisLocal = Quaternion.Inverse(parentRotation) * axis,
                        Bind = joints[raw].pose.rotation,
                        MaxAngle = maxAngles[Mathf.Min(drivable, maxAngles.Length - 1)],
                    });

                    drivable++;
                }
            }

            HingeCache[hand] = hinges.ToArray();
        }

        /// <summary>Builds a joint array from five ratios (the idle pose comes from here).</summary>
        private static Quaternion[] BuildProfile(in HandPoseProfile profile, bool rightHand)
        {
            Quaternion[] result = Copy(Bind(rightHand));
            Hinge[] hinges = Hinges(rightHand);

            for (int i = 0; i < hinges.Length; i++)
            {
                Hinge hinge = hinges[i];
                if (hinge.Slot < 0 || hinge.Slot >= result.Length)
                {
                    continue;
                }

                float curl = Mathf.Clamp01(profile.Get(hinge.Finger));
                result[hinge.Slot] = Quaternion.AngleAxis(curl * hinge.MaxAngle, hinge.AxisLocal) *
                                     hinge.Bind;
            }

            return result;
        }

        /// <summary>
        /// Composes every skeleton joint's pose in HAND space top-down (root = its own pose; the wrist
        /// is identity in the default skeleton).
        /// </summary>
        private static void ResolveHandSpace(HandSkeletonJoint[] joints,
            out Vector3[] positions, out Quaternion[] rotations)
        {
            positions = new Vector3[joints.Length];
            rotations = new Quaternion[joints.Length];
            var resolved = new bool[joints.Length];

            for (int i = 0; i < joints.Length; i++)
            {
                Resolve(joints, positions, rotations, resolved, i);
            }
        }

        /// <summary>Resolves a joint (its parent first if needed) into hand space.</summary>
        private static void Resolve(HandSkeletonJoint[] joints, Vector3[] positions,
            Quaternion[] rotations, bool[] resolved, int index)
        {
            if (index < 0 || index >= joints.Length || resolved[index])
            {
                return;
            }

            // Cycle guard: marking before resolving prevents infinite recursion on a broken parent
            // chain (it then behaves like a root).
            resolved[index] = true;

            Pose local = joints[index].pose;
            int parent = joints[index].parent;

            if (parent < 0 || parent >= joints.Length)
            {
                positions[index] = local.position;
                rotations[index] = local.rotation;
                return;
            }

            Resolve(joints, positions, rotations, resolved, parent);
            positions[index] = positions[parent] + rotations[parent] * local.position;
            rotations[index] = rotations[parent] * local.rotation;
        }

        /// <summary>
        /// The volar (palm) direction — the way fingers CLOSE, in hand space.
        /// <para>
        /// Measurement: for the four fingers (index…pinky) take the proximal bone direction <c>m</c> and
        /// the next phalanx direction <c>p</c>; the component of <c>p</c> perpendicular to <c>m</c>
        /// already points palmwards (a relaxed skeleton is slightly curled). Their sum gives the
        /// direction.
        /// </para>
        /// <para>
        /// If the sum is degenerate (a perfectly straight skeleton) it falls back to
        /// <see cref="HandGripConvention"/>'s anatomical basis, with the <b>sign still taken from the
        /// data</b>: a relaxed thumb droops toward the palm, so whichever way its bend deflects is the
        /// palm side. The basis alone is not enough because its left/right cross product order is
        /// exactly what is sign-error prone here.
        /// </para>
        /// </summary>
        private static bool TryResolveVolar(HandSkeletonJoint[] joints, Vector3[] handPos,
            Quaternion[] handRot, bool rightHand, out Vector3 volar)
        {
            volar = Vector3.zero;

            Vector3 sum = Vector3.zero;
            for (int finger = (int)HandFinger.Index; finger <= (int)HandFinger.Pinky; finger++)
            {
                if (TryMeasureBend(handPos, ChainOf(finger), out Vector3 bend))
                {
                    sum += bend;
                }
            }

            if (sum.magnitude >= MinVolarMagnitude)
            {
                volar = sum.normalized;
                return true;
            }

            return TryVolarFromConvention(joints, handPos, handRot, rightHand, out volar);
        }

        /// <summary>
        /// A finger chain's "bend deflection": the component of the second bone PERPENDICULAR to the
        /// first. Needs the chain's first three joints (metacarpal/proximal + two phalanges).
        /// </summary>
        private static bool TryMeasureBend(Vector3[] handPos, HandJointId[] chain, out Vector3 bend)
        {
            bend = Vector3.zero;

            if (chain == null || chain.Length < 3)
            {
                return false;
            }

            int a = (int)chain[0];
            int b = (int)chain[1];
            int c = (int)chain[2];
            if (a < 0 || b < 0 || c < 0 ||
                a >= handPos.Length || b >= handPos.Length || c >= handPos.Length)
            {
                return false;
            }

            Vector3 first = handPos[b] - handPos[a];
            Vector3 second = handPos[c] - handPos[b];
            if (first.sqrMagnitude < MinDirectionSqr || second.sqrMagnitude < MinDirectionSqr)
            {
                return false;
            }

            first = first.normalized;
            second = second.normalized;
            bend = second - first * Vector3.Dot(second, first);
            return true;
        }

        /// <summary>
        /// Fallback palm direction: the anatomical basis' <c>+Y</c> axis, with the sign taken from the
        /// thumb's bend deflection. Rationale in <see cref="TryResolveVolar"/>.
        /// </summary>
        private static bool TryVolarFromConvention(HandSkeletonJoint[] joints, Vector3[] handPos,
            Quaternion[] handRot, bool rightHand, out Vector3 volar)
        {
            volar = Vector3.zero;

            // The result is converted back to hand space below — the directions themselves are in the
            // root's frame, where the basis is defined.
            if (!TryMeasureAnatomyDirections(joints, handPos, handRot, out int root,
                    out Vector3 fingerDirection, out Vector3 thumbDirection) ||
                !HandGripConvention.TryMeasureBoneBasis(
                    fingerDirection, thumbDirection, rightHand, out Quaternion basis))
            {
                return false;
            }

            HandJointId[] thumb = ChainOf((int)HandFinger.Thumb);
            if (thumb == null)
            {
                return false;
            }

            Vector3 candidate = handRot[root] * (basis * Vector3.up);
            if (candidate.sqrMagnitude < MinDirectionSqr)
            {
                return false;
            }

            candidate = candidate.normalized;

            if (TryMeasureBend(handPos, thumb, out Vector3 thumbBend) &&
                thumbBend.sqrMagnitude >= MinDirectionSqr &&
                Vector3.Dot(candidate, thumbBend) < 0f)
            {
                candidate = -candidate;
            }

            volar = candidate;
            return true;
        }

        /// <summary>
        /// The hand's finger and thumb directions in the ROOT's frame — the two vectors every
        /// anatomical basis on this side is built from (<c>HandGripConvention.TryMeasureBoneBasis</c>).
        /// <para>⚠️ <b>One reader, not two:</b> the volar fallback and the anchor basis ask the same
        /// question of the same skeleton; measured separately they would answer it differently the day
        /// the joint layout changes.</para>
        /// <para>⚠️ The middle finger's PROXIMAL is wanted, not its metacarpal: the chain always ends
        /// with "… proximal, intermediate, distal, tip", so it is the fourth from the end (correct on
        /// ISDK branches with and without metacarpals).</para>
        /// <para>⚠️ The ROOT's frame is not hand space: identical today since the wrist is identity in
        /// the default skeleton, but conflating them would drift silently if the skeleton ever shipped
        /// with a rotated wrist.</para>
        /// </summary>
        private static bool TryMeasureAnatomyDirections(HandSkeletonJoint[] joints, Vector3[] handPos,
            Quaternion[] handRot, out int root, out Vector3 fingerDirection, out Vector3 thumbDirection)
        {
            fingerDirection = Vector3.zero;
            thumbDirection = Vector3.zero;

            root = RootIndex(joints);
            if (root < 0 || root >= handPos.Length || root >= handRot.Length)
            {
                return false;
            }

            HandJointId[] middle = ChainOf((int)HandFinger.Middle);
            HandJointId[] thumb = ChainOf((int)HandFinger.Thumb);
            if (middle == null || middle.Length < 4 || thumb == null || thumb.Length < 1)
            {
                return false;
            }

            int middleProximal = (int)middle[middle.Length - 4];
            int thumbFirst = (int)thumb[0];
            if (middleProximal < 0 || middleProximal >= handPos.Length ||
                thumbFirst < 0 || thumbFirst >= handPos.Length)
            {
                return false;
            }

            Quaternion toRootLocal = Quaternion.Inverse(handRot[root]);
            fingerDirection = toRootLocal * (handPos[middleProximal] - handPos[root]);
            thumbDirection = toRootLocal * (handPos[thumbFirst] - handPos[root]);
            return true;
        }

        /// <summary>A finger's joint chain (tip INCLUDED) — order comes from ISDK's own table.</summary>
        private static HandJointId[] ChainOf(int finger)
        {
            var list = HandJointUtils.FingerToJointList;
            return finger >= 0 && finger < list.Count ? list[finger] : null;
        }

        /// <summary>The parentless joint (wrist root).</summary>
        private static int RootIndex(HandSkeletonJoint[] joints)
        {
            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i].parent < 0)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
