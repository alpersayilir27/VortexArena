using System.Collections.Generic;
using UnityEngine;
using VortexArena.Net;

namespace VortexArena.Core.World
{
    /// <summary>Counter or grill body: an object let go or coming to rest INSIDE it (under the top, in a
    /// shelf gap) is lifted onto the top surface before its pose is published.</summary>
    /// <remarks>
    /// ⚠️ The solid <see cref="BoxCollider"/>s on THIS GameObject are the counter; the component adds a
    /// matching trigger per box at runtime so the trigger messages land here. Put it on the object that
    /// carries the solid boxes (table root, grill <c>Visual</c>).
    /// <para>The lift is done here, not left to physics depenetration: PhysX pushes an object out through
    /// whichever face is nearest, so a knife dropped in the gap hung there or popped to the top at
    /// random. Runs before <c>object_rest</c> (<see cref="INetRestPoseAdjuster"/>), so every headset gets
    /// the same pose.</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class CounterSurface : MonoBehaviour, INetRestPoseAdjuster
    {
        /// <summary>Trigger top sits this far under the counter top (m) so objects lying ON the
        /// counter rarely register at all.</summary>
        private const float TopInset = 0.02f;

        /// <summary>Gap left under the lifted object (m) — flush contact re-wakes the body.</summary>
        private const float Clearance = 0.002f;

        private static readonly List<Collider> Parts = new List<Collider>();

        private static readonly List<CounterSurface> Active = new List<CounterSurface>();

        private readonly List<BoxCollider> _bodies = new List<BoxCollider>();

        /// <summary>Objects overlapping the triggers, with an overlap count: an object with several
        /// colliders, or one spanning two boxes of an L counter, enters more than once.</summary>
        private readonly Dictionary<NetObject, int> _inside = new Dictionary<NetObject, int>();

        private void Awake()
        {
            // Collect first: the triggers added below are BoxColliders on this same object.
            foreach (BoxCollider box in GetComponents<BoxCollider>())
            {
                if (!box.isTrigger)
                {
                    _bodies.Add(box);
                }
            }

            if (_bodies.Count == 0)
            {
                Debug.LogWarning($"[CounterSurface] '{name}' üzerinde katı BoxCollider yok — tezgâh yüzeyi çalışmaz.", this);
                return;
            }

            float inset = TopInset / Mathf.Max(0.0001f, transform.lossyScale.y);
            for (int i = 0; i < _bodies.Count; i++)
            {
                BoxCollider body = _bodies[i];
                var zone = gameObject.AddComponent<BoxCollider>();
                zone.isTrigger = true;
                zone.center = body.center - new Vector3(0f, inset * 0.5f, 0f);
                zone.size = body.size - new Vector3(0f, inset, 0f);
            }
        }

        private void OnEnable()
        {
            Active.Add(this);
        }

        /// <summary>Overlap (m) through a side or bottom face below which the object is only grazing the
        /// counter and is left where it is.</summary>
        private const float SideTolerance = 0.005f;

        /// <summary>Separation direction with at least this world-up component means the top face is the
        /// nearest exit: any overlap counts.</summary>
        private const float TopFaceDot = 0.7f;

        /// <summary>Lift passes: clearing one box can land the object inside a taller one (shelf on top).</summary>
        private const int MaxPasses = 3;

        /// <summary>Lifts an object let go INSIDE any counter onto its top. ⚠️ Asked before the body turns
        /// dynamic: set free inside the solid box, PhysX depenetration throws it out sideways.
        /// <para>Needs no trigger: a release through a side face or right under the top never enters one.</para></summary>
        public static bool TryLiftOut(NetObject net, ref Pose worldPose)
        {
            return net != null && LiftAboveCounters(net, ref worldPose);
        }

        private void OnDisable()
        {
            Active.Remove(this);

            foreach (NetObject net in _inside.Keys)
            {
                var sender = net != null ? net.GetComponent<NetObjectPoseSender>() : null;
                if (sender != null)
                {
                    sender.RemoveRestAdjuster(this);
                }
            }

            _inside.Clear();
        }

        private void OnTriggerEnter(Collider other)
        {
            NetObject net = Resolve(other, out NetObjectPoseSender sender);
            if (net == null)
            {
                return;
            }

            if (_inside.TryGetValue(net, out int count))
            {
                _inside[net] = count + 1;
                return;
            }

            _inside.Add(net, 1);
            sender.AddRestAdjuster(this);
        }

        private void OnTriggerExit(Collider other)
        {
            NetObject net = Resolve(other, out NetObjectPoseSender sender);
            if (net == null || !_inside.TryGetValue(net, out int count))
            {
                return;
            }

            if (count > 1)
            {
                _inside[net] = count - 1;
                return;
            }

            _inside.Remove(net);
            sender.RemoveRestAdjuster(this);
        }

        /// <summary>Lifts an object resting inside the counter onto its top; objects on the top are left alone.</summary>
        public bool TryAdjustRestPose(NetObject net, ref Pose worldPose)
        {
            return net != null && _inside.ContainsKey(net) && LiftAboveCounters(net, ref worldPose);
        }

        /// <summary>Moves the pose straight up (world Y only, rotation kept) until no counter box holds the
        /// object. ⚠️ Never along the separation direction: that is PhysX's nearest-face push again.</summary>
        private static bool LiftAboveCounters(NetObject net, ref Pose worldPose)
        {
            // Bounds only follow the transform after a sync.
            net.transform.SetPositionAndRotation(worldPose.position, worldPose.rotation);
            Physics.SyncTransforms();

            bool lifted = false;
            for (int pass = 0; pass < MaxPasses; pass++)
            {
                if (!TryBounds(net, out Bounds bounds))
                {
                    break;
                }

                float top = float.NegativeInfinity;
                for (int i = 0; i < Active.Count; i++)
                {
                    Active[i].RaiseToPenetratedTop(net, ref top);
                }

                float lift = top - bounds.min.y + Clearance;
                if (float.IsNegativeInfinity(top) || lift <= 0f)
                {
                    break;
                }

                worldPose.position += Vector3.up * lift;
                net.transform.position = worldPose.position;
                Physics.SyncTransforms();
                lifted = true;
            }

            return lifted;
        }

        /// <summary>Raises <paramref name="top"/> to the top of every box of this counter the object
        /// really overlaps — through any face, so a release against the side or near the floor counts.</summary>
        private void RaiseToPenetratedTop(NetObject net, ref float top)
        {
            net.GetComponentsInChildren(Parts);
            for (int b = 0; b < _bodies.Count; b++)
            {
                BoxCollider body = _bodies[b];
                if (body == null || !body.enabled || body.bounds.max.y <= top)
                {
                    continue;
                }

                for (int p = 0; p < Parts.Count; p++)
                {
                    Collider part = Parts[p];
                    if (part == null || part.isTrigger || !part.enabled || !part.bounds.Intersects(body.bounds))
                    {
                        continue;
                    }

                    Transform pt = part.transform;
                    Transform bt = body.transform;
                    if (!Physics.ComputePenetration(part, pt.position, pt.rotation, body, bt.position, bt.rotation,
                            out Vector3 direction, out float distance))
                    {
                        continue;
                    }

                    // Grazing a side face (resting against it) is not "inside".
                    if (direction.y < TopFaceDot && distance < SideTolerance)
                    {
                        continue;
                    }

                    top = body.bounds.max.y;
                    break;
                }
            }

            Parts.Clear();
        }

        /// <summary>World box of the object's solid colliders.</summary>
        private static bool TryBounds(NetObject net, out Bounds bounds)
        {
            bounds = default;
            bool found = false;

            net.GetComponentsInChildren(Parts);
            for (int i = 0; i < Parts.Count; i++)
            {
                Collider part = Parts[i];
                if (part == null || part.isTrigger || !part.enabled)
                {
                    continue;
                }

                if (found)
                {
                    bounds.Encapsulate(part.bounds);
                    continue;
                }

                bounds = part.bounds;
                found = true;
            }

            Parts.Clear();
            return found;
        }

        private static NetObject Resolve(Collider other, out NetObjectPoseSender sender)
        {
            NetObject net = other != null ? other.GetComponentInParent<NetObject>() : null;
            sender = net != null && net.NetId > 0 ? net.GetComponent<NetObjectPoseSender>() : null;
            return sender != null ? net : null;
        }
    }
}
