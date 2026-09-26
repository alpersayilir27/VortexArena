using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core.Combat;
using VortexArena.Core.World;
using VortexArena.Net;

namespace VortexArena.Modes.Burger
{
    /// <summary>The straight ingredient column of a burger board — shared by the fixed
    /// <see cref="BurgerServingBoard"/> and the carried <see cref="BurgerCuttingBoard"/>, so both stack
    /// the same way and a transfer between them is a pure re-computation.
    /// <para>⚠️ The order of a stack is the order it was PLACED in, not the order physics produced: a
    /// thrown thin layer (marul ~9 mm) slips under the one placed before it and the recipe is read bottom
    /// to top (§10.5) — i.e. the slip becomes a wrong serve.</para>
    /// <para>The correction is published, not just shown: <see cref="TryAdjustRestPose"/> runs before
    /// <c>object_rest</c> leaves (<see cref="INetRestPoseAdjuster"/>), so every headset gets the same
    /// column.</para></summary>
    internal sealed class BurgerStackColumn
    {
        /// <summary>Volume that holds the column (trigger collider on the board).</summary>
        private readonly Collider _volume;

        /// <summary>Where layer zero sits; null = bottom centre of the volume.</summary>
        private readonly Transform _anchor;

        /// <summary>Board the column belongs to — its rotation is the fallback anchor orientation.</summary>
        private readonly Transform _board;

        /// <summary>Layers WE snapped onto the column, with the pose they were snapped to. Held until the
        /// server confirms the rest: the body is still dynamic on this headset until then, and gravity
        /// would walk the layer back off the pose we just published.</summary>
        private readonly Dictionary<NetObject, PinEntry> _pinned = new Dictionary<NetObject, PinEntry>();

        private readonly List<NetObject> _unpin = new List<NetObject>();

        /// <summary>Scratch for collider queries — a per-placement array would allocate in the hand.</summary>
        private static readonly List<Collider> Parts = new List<Collider>();

        private static readonly Collider[] Overlap = new Collider[64];

        private struct PinEntry
        {
            public Pose Pose;

            /// <summary>Until when the pin survives a layer the state still calls HELD. A transfer
            /// publishes release+rest on a layer that is in our hand, and the flags only fall a round trip
            /// later — without the grace the layer would be let go of for exactly those frames.</summary>
            public float HeldUntil;
        }

        public BurgerStackColumn(Collider volume, Transform anchor, Transform board)
        {
            _volume = volume;
            _anchor = anchor;
            _board = board;
        }

        public bool HasVolume => _volume != null;

        /// <summary>World box of the stack volume.</summary>
        public Bounds Volume => _volume.bounds;

        // ------------------------------------------------------------------- placement

        /// <summary>Snaps a layer coming to rest onto the column: straight up from the anchor, on top of
        /// whatever is already standing there.</summary>
        /// <returns><c>false</c> when the layer does not belong to this column (outside the volume, or
        /// riding a carrier) — the caller's own filters run first.</returns>
        public bool TryAdjustRestPose(NetObject net, ref Pose worldPose)
        {
            if (_volume == null || net == null)
            {
                return false;
            }

            // A layer riding a carrier rests ON THE CARRIER, not on this column — the carrier places it
            // and two writers on one transform is a visible jitter.
            if (IsCarried(net) || !_volume.bounds.Contains(worldPose.position))
            {
                return false;
            }

            AnchorPose(out Vector3 anchorPosition, out Quaternion anchorRotation);
            Quaternion rotation = Upright(anchorRotation, worldPose.rotation);

            // Measured AT THE TARGET ROTATION: thickness along the column is a property of how the layer
            // ends up lying, and the physics bounds only follow the transform after a sync.
            Transform layer = net.transform;
            layer.rotation = rotation;
            Physics.SyncTransforms();

            if (!TryBounds(net, out Bounds bounds))
            {
                return false;
            }

            float bottom = TryStackTop(net, out float top) ? top : anchorPosition.y;

            Vector3 position = layer.position + new Vector3(
                anchorPosition.x - bounds.center.x,
                bottom - bounds.min.y,
                anchorPosition.z - bounds.center.z);

            worldPose = new Pose(position, rotation);
            Pin(net, worldPose);
            return true;
        }

        /// <summary>Poses for a WHOLE ordered list landing on this column at once (a transferred stack):
        /// the running top carries from one layer to the next, so the list keeps its order and its
        /// thicknesses instead of being re-measured one rest at a time.</summary>
        /// <remarks>⚠️ Rotates each layer while measuring (thickness depends on how it lies) — the caller
        /// must apply the returned poses in the same pass. Null entries get an empty pose so the two lists
        /// stay index-aligned.</remarks>
        public bool TryComputeColumnPoses(IReadOnlyList<NetObject> layers, List<Pose> poses)
        {
            poses.Clear();

            if (_volume == null || layers == null || layers.Count == 0)
            {
                return false;
            }

            AnchorPose(out Vector3 anchorPosition, out Quaternion anchorRotation);
            float top = TryStackTop(null, out float standing) ? standing : anchorPosition.y;

            for (int i = 0; i < layers.Count; i++)
            {
                NetObject layer = layers[i];
                if (layer == null)
                {
                    poses.Add(default);
                    continue;
                }

                Quaternion rotation = Upright(anchorRotation, layer.transform.rotation);
                layer.transform.rotation = rotation;
                Physics.SyncTransforms();

                if (!TryBounds(layer, out Bounds bounds))
                {
                    poses.Add(new Pose(layer.transform.position, rotation));
                    continue;
                }

                Vector3 position = layer.transform.position + new Vector3(
                    anchorPosition.x - bounds.center.x,
                    top - bounds.min.y,
                    anchorPosition.z - bounds.center.z);

                poses.Add(new Pose(position, rotation));
                top += bounds.size.y;
            }

            return true;
        }

        /// <summary>Is <paramref name="worldPoint"/> over the column: inside its footprint and no higher
        /// than <paramref name="height"/> above the volume's top.</summary>
        public bool IsOver(Vector3 worldPoint, float height)
        {
            if (_volume == null)
            {
                return false;
            }

            Bounds bounds = _volume.bounds;
            return worldPoint.x >= bounds.min.x && worldPoint.x <= bounds.max.x &&
                   worldPoint.z >= bounds.min.z && worldPoint.z <= bounds.max.z &&
                   worldPoint.y >= bounds.min.y && worldPoint.y <= bounds.max.y + height;
        }

        /// <summary>Where layer zero sits. Without an authored anchor the bottom centre of the stack
        /// volume is used — the board's own surface.</summary>
        private void AnchorPose(out Vector3 position, out Quaternion rotation)
        {
            if (_anchor != null)
            {
                _anchor.GetPositionAndRotation(out position, out rotation);
                return;
            }

            Bounds volume = _volume.bounds;
            position = new Vector3(volume.center.x, volume.min.y, volume.center.z);
            rotation = _board != null ? _board.rotation : Quaternion.identity;
        }

        /// <summary>Anchor orientation, keeping only the layer's spin around the anchor's up axis: the
        /// tower must be straight, but two identical slices lying at the same angle look printed.</summary>
        public static Quaternion Upright(Quaternion anchorRotation, Quaternion layerRotation)
        {
            float yaw = (Quaternion.Inverse(anchorRotation) * layerRotation).eulerAngles.y;
            return anchorRotation * Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>Top of the column as it stands. Only layers that are DOWN count: a piece still flying
        /// through the volume, one in a hand above it, or one riding a carrier is not part of the stack
        /// yet. A layer we just pinned counts too — the server has not confirmed its rest, so it still
        /// reads as awake.</summary>
        private bool TryStackTop(NetObject placing, out float topY)
        {
            topY = 0f;
            bool found = false;

            Bounds volume = _volume.bounds;
            int count = Physics.OverlapBoxNonAlloc(volume.center, volume.extents, Overlap,
                Quaternion.identity, ~0, QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                NetObject net = Overlap[i] != null ? Overlap[i].GetComponentInParent<NetObject>() : null;
                if (net == null || net == placing || !IsStandingLayer(net))
                {
                    continue;
                }

                if (!TryBounds(net, out Bounds bounds) || (found && bounds.max.y <= topY))
                {
                    continue;
                }

                topY = bounds.max.y;
                found = true;
            }

            return found;
        }

        /// <summary>Ingredients in the volume, bottom to top by SOLID box centre.</summary>
        /// <param name="freeOnly"><c>true</c> = only layers nobody owns and nothing is moving — the set a
        /// board may pick up. <c>false</c> = the stack as it reads for a serve: a layer we just pinned
        /// counts, one in a hand or riding a carrier does not.</param>
        public void Collect(List<NetObject> into, bool freeOnly)
        {
            into.Clear();

            if (_volume == null)
            {
                return;
            }

            Bounds bounds = _volume.bounds;
            int count = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents, Overlap,
                Quaternion.identity, ~0, QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                NetObject net = Overlap[i] != null ? Overlap[i].GetComponentInParent<NetObject>() : null;
                if (net == null || into.Contains(net))
                {
                    continue;
                }

                bool ok = freeOnly
                    ? IsIngredient(net) && net.Owner == 0 && !net.IsHeld && !net.IsAwake &&
                      !IsCarried(net)
                    : IsStandingLayer(net);

                if (ok)
                {
                    into.Add(net);
                }
            }

            into.Sort(CompareByHeight);
        }

        /// <summary>A layer that counts as part of the standing column right now.</summary>
        private bool IsStandingLayer(NetObject net)
        {
            if (!IsIngredient(net) || IsCarried(net))
            {
                return false;
            }

            // ⚠️ A held layer is cargo hovering over the board, not a layer of it — except one WE just
            // pinned: its rest is on the wire and only the answer is missing.
            bool pinned = _pinned.ContainsKey(net);
            return (!net.IsHeld || pinned) && (!net.IsAwake || pinned);
        }

        private static bool IsIngredient(NetObject net)
        {
            return net != null && net.NetId > 0 && net.Kind != null &&
                   BurgerKinds.IsIngredient(net.Kind.Kind);
        }

        /// <summary>Riding a carrier (spatula blade, cutting board): somebody else writes its
        /// transform.</summary>
        public static bool IsCarried(NetObject net)
        {
            var bridge = net != null ? net.GetComponent<NetObjectGrabBridge>() : null;
            return bridge != null && bridge.CarryAnchor != null;
        }

        // ------------------------------------------------------------------- pins

        /// <summary>Holds a layer on the pose we published for it.</summary>
        /// <param name="heldSeconds">Grace in which a layer the state still calls HELD keeps its pin —
        /// only a transfer needs it (see <see cref="PinEntry.HeldUntil"/>).</param>
        public void Pin(NetObject layer, Pose worldPose, float heldSeconds = 0f)
        {
            if (layer == null)
            {
                return;
            }

            _pinned[layer] = new PinEntry
            {
                Pose = worldPose,
                HeldUntil = heldSeconds > 0f ? Time.time + heldSeconds : 0f
            };
        }

        public void RemovePin(NetObject layer)
        {
            if (layer != null)
            {
                _pinned.Remove(layer);
            }
        }

        public void Clear()
        {
            _pinned.Clear();
        }

        /// <summary>Keeps pinned layers on their published pose while their body is still ours and awake.
        /// Call from <c>LateUpdate</c>.
        /// <para>⚠️ <c>isKinematic</c> is NOT written here: the ownership flag drives it
        /// (<c>NetObjectBody</c>), and a second writer on that flag loses the interpolation setting with
        /// it.</para></summary>
        public void TickPins()
        {
            if (_pinned.Count == 0)
            {
                return;
            }

            _unpin.Clear();
            float now = Time.time;

            foreach (KeyValuePair<NetObject, PinEntry> entry in _pinned)
            {
                NetObject layer = entry.Key;
                PinEntry pin = entry.Value;

                bool keep = layer != null && layer.NetId > 0 && layer.IsMine &&
                            (now < pin.HeldUntil || (!layer.IsHeld && layer.IsAwake));

                if (!keep)
                {
                    // Confirmed (or taken away again): from here the server's rest pose places it.
                    _unpin.Add(layer);
                    continue;
                }

                var body = layer.GetComponent<Rigidbody>();
                if (body != null && !body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }

                layer.transform.SetPositionAndRotation(pin.Pose.position, pin.Pose.rotation);
            }

            for (int i = 0; i < _unpin.Count; i++)
            {
                _pinned.Remove(_unpin[i]);
            }
        }

        // ------------------------------------------------------------------- measuring

        /// <summary>World box of a layer's solid colliders — the thickness table the stack needs, read
        /// off the prefab itself so no hand-written per-kind number can go stale when the art changes.</summary>
        public static bool TryBounds(NetObject layer, out Bounds bounds)
        {
            bounds = default;
            bool found = false;

            if (layer == null)
            {
                return false;
            }

            layer.GetComponentsInChildren(Parts);

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

        /// <summary>⚠️ Compared by the layer's SOLID box, not by its pivot: two ingredient prefabs do not
        /// share a pivot height, so on a tight column pivot order and stacking order are not the same
        /// list — and that list is the recipe.</summary>
        public static int CompareByHeight(NetObject a, NetObject b)
        {
            return CentreY(a).CompareTo(CentreY(b));
        }

        private static float CentreY(NetObject layer)
        {
            return TryBounds(layer, out Bounds bounds) ? bounds.center.y : layer.transform.position.y;
        }
    }
}
