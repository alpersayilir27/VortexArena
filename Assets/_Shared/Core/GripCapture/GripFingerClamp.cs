using System.Collections.Generic;
using Oculus.Interaction.HandGrab.Visuals;
using Oculus.Interaction.Input;
using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Core.GripCapture
{
    /// <summary>
    /// Stops the tracked fingers at the item's surface: each joint is blended from the open pose
    /// toward the tracked pose only as far as it stays outside the item.
    /// <para>⚠️ The blend is MARCHED, not endpoint-tested: an overlap query against a non-convex
    /// mesh only reports the surface, so a finger fully inside the item — or pushed through a thin
    /// handle — passes an endpoint test and the capture records a hand buried in the weapon.</para>
    /// </summary>
    public static class GripFingerClamp
    {
        /// <summary>A clamped finger bone as a capsule — the hand's own collider shape.</summary>
        public struct Segment
        {
            public Vector3 A;
            public Vector3 B;
            public float Radius;
        }

        /// <summary>Marching resolution; the hit is then narrowed by bisection.</summary>
        private const int MarchSteps = 12;

        private const int BisectionSteps = 5;

        /// <summary>Only the item is queried (layer 0); the hand's own capsules sit on layer 2.</summary>
        private const int ItemMask = 1 << 0;

        private const float MinSegmentSqr = 1e-8f;

        /// <summary>Fallback tip length when the distal bone has no child (metres).</summary>
        private const float TipFallbackLength = 0.02f;

        /// <summary>Capsule radius per finger (metres) — thumb…pinky.</summary>
        private static readonly float[] FingerRadius = { 0.0105f, 0.009f, 0.009f, 0.0085f, 0.0075f };

        /// <summary>
        /// Writes the clamped pose onto the puppet bones and reports it.
        /// </summary>
        /// <param name="maps">Joint maps indexed like <c>FingersMetadata.HAND_JOINT_IDS</c> (may hold
        /// nulls).</param>
        /// <param name="open">The open-hand array (march start).</param>
        /// <param name="target">The tracked array (march end).</param>
        /// <param name="result">Receives the clamped array.</param>
        /// <param name="touching">Per finger: stopped on the surface.</param>
        /// <param name="penetrating">Per finger: already inside at the open pose.</param>
        /// <param name="segments">Receives the clamped bone capsules.</param>
        public static void Apply(HandJointMap[] maps, Quaternion[] open, Quaternion[] target,
            Quaternion[] result, bool[] touching, bool[] penetrating, List<Segment> segments)
        {
            segments?.Clear();

            for (int i = 0; i < result.Length && i < target.Length; i++)
            {
                result[i] = target[i];
            }

            int[][] fingerJoints = FingersMetadata.FINGER_TO_JOINT_INDEX;
            HandJointId[] ids = FingersMetadata.HAND_JOINT_IDS;

            for (int finger = 0; finger < fingerJoints.Length; finger++)
            {
                touching[finger] = false;
                penetrating[finger] = false;

                int[] chain = fingerJoints[finger];
                float radius = FingerRadius[Mathf.Min(finger, FingerRadius.Length - 1)];

                for (int j = 0; j < chain.Length; j++)
                {
                    int slot = chain[j];
                    if (slot < 0 || slot >= ids.Length || maps[slot] == null ||
                        maps[slot].transform == null)
                    {
                        continue;
                    }

                    HandJointMap map = maps[slot];

                    if (!HandPoseLibrary.IsDrivable(ids[slot]))
                    {
                        // Metacarpals are not recorded and not clamped; written so the children sit
                        // where the record expects them.
                        map.transform.localRotation = map.RotationOffset * target[slot];
                        continue;
                    }

                    Transform endpoint = ResolveEndpoint(maps, chain, j);
                    ClampJoint(map, endpoint, open[slot], target[slot], radius, out Quaternion value,
                        out bool hitSurface, out bool inside);

                    result[slot] = value;
                    map.transform.localRotation = map.RotationOffset * value;

                    touching[finger] |= hitSurface;
                    penetrating[finger] |= inside;

                    if (segments != null && endpoint != null)
                    {
                        segments.Add(new Segment
                        {
                            A = map.transform.position,
                            B = endpoint.position,
                            Radius = radius,
                        });
                    }
                }
            }
        }

        private static void ClampJoint(HandJointMap map, Transform endpoint, in Quaternion open,
            in Quaternion target, float radius, out Quaternion value, out bool hitSurface,
            out bool inside)
        {
            value = target;
            hitSurface = false;
            inside = false;

            if (endpoint == null)
            {
                return;
            }

            float free = 0f;
            float hit = -1f;

            for (int step = 0; step <= MarchSteps; step++)
            {
                float t = step / (float)MarchSteps;
                if (Overlaps(map, endpoint, open, target, t, radius))
                {
                    hit = t;
                    break;
                }

                free = t;
            }

            if (hit < 0f)
            {
                return;
            }

            if (hit <= 0f)
            {
                // Already inside with the hand open: nothing to clamp away, the tracked pose is kept
                // and the HUD flags the finger instead.
                inside = true;
                return;
            }

            for (int step = 0; step < BisectionSteps; step++)
            {
                float middle = (free + hit) * 0.5f;
                if (Overlaps(map, endpoint, open, target, middle, radius))
                {
                    hit = middle;
                }
                else
                {
                    free = middle;
                }
            }

            value = Quaternion.Slerp(open, target, free);
            hitSurface = true;
        }

        private static bool Overlaps(HandJointMap map, Transform endpoint, in Quaternion open,
            in Quaternion target, float t, float radius)
        {
            map.transform.localRotation = map.RotationOffset * Quaternion.Slerp(open, target, t);

            Vector3 a = map.transform.position;
            Vector3 b = endpoint.position;
            if ((b - a).sqrMagnitude < MinSegmentSqr)
            {
                return false;
            }

            return Physics.CheckCapsule(a, b, radius, ItemMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// The far end of this bone: the next joint, or for the distal bone its tip child. Tips are
        /// absent from the joint maps, so they are read from the rig hierarchy.
        /// </summary>
        private static Transform ResolveEndpoint(HandJointMap[] maps, int[] chain, int index)
        {
            if (index + 1 < chain.Length)
            {
                int next = chain[index + 1];
                if (next >= 0 && next < maps.Length && maps[next] != null)
                {
                    return maps[next].transform;
                }
            }

            return ResolveTip(maps[chain[index]].transform);
        }

        /// <summary>
        /// The distal bone's tip. Missing child: a synthetic tip is created once so the distal
        /// segment is still tested — without it the fingertip passes through the item.
        /// </summary>
        private static Transform ResolveTip(Transform distal)
        {
            for (int i = 0; i < distal.childCount; i++)
            {
                Transform child = distal.GetChild(i);
                if (child.name.IndexOf("Tip", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return child;
                }
            }

            if (distal.childCount > 0)
            {
                return distal.GetChild(0);
            }

            var tip = new GameObject("SyntheticTip").transform;
            tip.SetParent(distal, false);

            // Local +X is the bone direction in the ISDK hand rig (bones run down the finger).
            tip.localPosition = Vector3.right * TipFallbackLength;
            return tip;
        }
    }
}
