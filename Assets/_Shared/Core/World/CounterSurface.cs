using System.Collections.Generic;
using UnityEngine;
using VortexArena.Net;

namespace VortexArena.Core.World
{
    /// <summary>Counter or grill body: an object coming to rest INSIDE it (under the top, in a shelf
    /// gap) is lifted onto the top surface before its rest pose is published.</summary>
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

        private void OnDisable()
        {
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

        /// <summary>Lifts the object onto the top of the box its centre is inside of; objects already
        /// on (or above) the top are left alone.</summary>
        public bool TryAdjustRestPose(NetObject net, ref Pose worldPose)
        {
            if (net == null || !_inside.ContainsKey(net))
            {
                return false;
            }

            // Bounds only follow the transform after a sync.
            net.transform.SetPositionAndRotation(worldPose.position, worldPose.rotation);
            Physics.SyncTransforms();

            if (!TryBounds(net, out Bounds bounds) || !TryBodyAround(bounds.center, out float top))
            {
                return false;
            }

            worldPose.position += Vector3.up * (top - bounds.min.y + Clearance);
            return true;
        }

        /// <summary>Top of the solid box whose footprint holds <paramref name="point"/>, when the
        /// point is below that top.</summary>
        private bool TryBodyAround(Vector3 point, out float top)
        {
            for (int i = 0; i < _bodies.Count; i++)
            {
                BoxCollider body = _bodies[i];
                if (body == null || !body.enabled)
                {
                    continue;
                }

                float bodyTop = body.bounds.max.y;
                if (point.y >= bodyTop)
                {
                    continue;
                }

                // Probed just under the top: ClosestPoint returns the point itself when it is inside.
                var probe = new Vector3(point.x, bodyTop - 0.01f, point.z);
                if ((body.ClosestPoint(probe) - probe).sqrMagnitude < 1e-8f)
                {
                    top = bodyTop;
                    return true;
                }
            }

            top = 0f;
            return false;
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
