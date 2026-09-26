using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.Combat;
using VortexArena.Core.World;
using VortexArena.Net;

namespace VortexArena.Modes.Burger
{
    /// <summary>The CARRIED board a burger is built on: a straight column of its own
    /// (<see cref="BurgerStackColumn"/>) while it rests, and the whole column comes along when it is
    /// picked up.
    /// <para><b>Nothing new goes on the wire.</b> Cargo is claimed with a plain <c>object_grab</c> and
    /// let go with <c>object_release</c>+<c>object_rest</c> — a carried layer is simply "held by the
    /// board's owner" (§10.5).</para>
    /// <para><b>The hand-over is the gesture:</b> letting go over a <see cref="BurgerServingBoard"/>
    /// pours the stack onto that board IN ORDER and puts this board down beside it. Serving still
    /// happens only from the serving board.</para>
    /// <para>⚠️ Cargo is claimed at the LOCAL grab, not after the server confirms: the board is already
    /// moving in the hand and a layer claimed one round trip later would be lifted out of thin air.
    /// A refused claim is silent (§10.10) — the only sign is that the owner never becomes us, so every
    /// claim carries a deadline.</para></summary>
    /// <remarks>⚠️ Runs after <see cref="NetObjectGrabBridge"/> (default order) and before
    /// <c>HandGripPoser</c> (order 100): the board must already be at the hand this frame, or the cargo
    /// trails it by one frame.</remarks>
    [RequireComponent(typeof(NetObject))]
    [RequireComponent(typeof(NetObjectGrabBridge))]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(50)]
    public sealed class BurgerCuttingBoard : MonoBehaviour, INetRestPoseAdjuster, IReleasePoseOverride
    {
        [Tooltip("Tahtanın üstündeki malzemeleri toplayan hacim (tetik collider).")]
        [SerializeField] private Collider stackTrigger;

        [Tooltip("Yığının dizildiği nokta: alt katman burada oturur, üstü buranın +Y ekseninde yükselir. " +
                 "Boşsa yığın hacminin taban ortası kullanılır.")]
        [SerializeField] private Transform stackAnchor;

        [Tooltip("Taşınan yığının bağlandığı nokta — hangi servis tahtasının üstünde olduğumuz da " +
                 "buradan ölçülür. Boşsa yığın noktası, o da boşsa hacmin kendi transform'u.")]
        [SerializeField] private Transform cargoAnchor;

        /// <summary>How long a claim is waited for. A refusal has no message of its own (§10.10) — the
        /// only sign is that the owner never becomes us.</summary>
        private const float ClaimAnswerSeconds = 0.5f;

        /// <summary>How long a layer we just put down keeps its pin while the wire still calls it held —
        /// one round trip plus slack; without it gravity walks the layer off the published pose.</summary>
        private const float SettlePinSeconds = 1.5f;

        /// <summary>Fallback half footprint (m) when the board has no solid collider to measure.</summary>
        private const float FallbackRadius = 0.15f;

        private NetObject _net;
        private NetObjectGrabBridge _bridge;
        private NetObjectPoseSender _sender;
        private BurgerStackColumn _column;

        /// <summary>Cargo bottom to top — the order the stack keeps all the way onto the serving
        /// board.</summary>
        private readonly List<NetObject> _cargo = new List<NetObject>();

        /// <summary>Pose of each cargo layer in BOARD space, taken at the pickup: the column the player
        /// built is the column that must arrive, so the offsets are kept rather than re-stacked at a
        /// fixed spacing.</summary>
        private readonly List<Pose> _cargoLocal = new List<Pose>();

        private readonly List<NetObjectGrabBridge> _cargoBridges = new List<NetObjectGrabBridge>();

        /// <summary>Claim sent, no answer yet: netId → the moment we give up on it.</summary>
        private readonly Dictionary<int, float> _claimed = new Dictionary<int, float>();

        /// <summary>Ingredients over the board, with the sender we subscribed to.</summary>
        private readonly Dictionary<NetObject, NetObjectPoseSender> _watched =
            new Dictionary<NetObject, NetObjectPoseSender>();

        /// <summary>Scratch for the pickup query — reused so a grab allocates nothing.</summary>
        private readonly List<NetObject> _free = new List<NetObject>();

        private void Awake()
        {
            _net = GetComponent<NetObject>();
            _bridge = GetComponent<NetObjectGrabBridge>();
            _sender = GetComponent<NetObjectPoseSender>();
            _column = new BurgerStackColumn(stackTrigger, stackAnchor, transform);

            if (cargoAnchor == null)
            {
                cargoAnchor = stackAnchor != null
                    ? stackAnchor
                    : (stackTrigger != null ? stackTrigger.transform : transform);
            }

            if (stackTrigger == null)
            {
                Debug.LogError($"[BurgerCuttingBoard] '{name}' için yığın hacmi atanmamış — tahtaya " +
                               "konulan malzemeler ne dizilir ne de tahtayla birlikte taşınır.", this);
            }

            if (_sender == null)
            {
                // Without the rest event the cargo would never be put down after a throw.
                Debug.LogError($"[BurgerCuttingBoard] '{name}' üzerinde NetObjectPoseSender yok — " +
                               "tahta yere indiğinde üstündeki yığın bırakılmaz.", this);
            }
        }

        private void OnEnable()
        {
            _bridge.GrabbedLocally += HandleGrabbedLocally;
            _bridge.ReleasedLocally += HandleReleasedLocally;
            _bridge.ReleaseOverride = this;
            _net.OwnerChanged += HandleOwnerChanged;

            if (_sender != null)
            {
                _sender.RestSent += HandleBoardRest;
            }
        }

        private void OnDisable()
        {
            _bridge.GrabbedLocally -= HandleGrabbedLocally;
            _bridge.ReleasedLocally -= HandleReleasedLocally;
            _net.OwnerChanged -= HandleOwnerChanged;

            if (_sender != null)
            {
                _sender.RestSent -= HandleBoardRest;
            }

            if (ReferenceEquals(_bridge.ReleaseOverride, this))
            {
                _bridge.ReleaseOverride = null;
            }

            // ⚠️ Cargo must not outlive this component: nothing else writes those transforms, so the
            // layers would hang wherever the board last was.
            SettleCargo();

            foreach (KeyValuePair<NetObject, NetObjectPoseSender> entry in _watched)
            {
                if (entry.Value != null)
                {
                    entry.Value.RemoveRestAdjuster(this);
                }
            }

            _watched.Clear();
            _column.Clear();
        }

        private void LateUpdate()
        {
            if (_net == null || _net.NetId <= 0)
            {
                return;
            }

            PruneCargo();
            SeatCargo();
            _column.TickPins();
        }

        // ------------------------------------------------------------------- the column at rest

        /// <remarks>⚠️ The child stack volume is attached to this object's Rigidbody, so its trigger
        /// messages arrive here.</remarks>
        private void OnTriggerEnter(Collider other)
        {
            NetObject ingredient = ResolveIngredient(other);
            if (ingredient == null || _watched.ContainsKey(ingredient))
            {
                return;
            }

            var sender = ingredient.GetComponent<NetObjectPoseSender>();
            _watched.Add(ingredient, sender);

            if (sender != null)
            {
                sender.AddRestAdjuster(this);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            NetObject ingredient = ResolveIngredient(other);
            if (ingredient == null || !_watched.TryGetValue(ingredient, out NetObjectPoseSender sender))
            {
                return;
            }

            _watched.Remove(ingredient);
            _column.RemovePin(ingredient);

            if (sender != null)
            {
                sender.RemoveRestAdjuster(this);
            }
        }

        private static NetObject ResolveIngredient(Collider other)
        {
            NetObject net = other != null ? other.GetComponentInParent<NetObject>() : null;
            return net != null && net.NetId > 0 && net.Kind != null &&
                   BurgerKinds.IsIngredient(net.Kind.Kind)
                ? net
                : null;
        }

        /// <summary>Snaps a layer coming to rest onto this board's column — only while the board itself
        /// is DOWN: a column computed on a board that is moving under the layer lands nowhere.</summary>
        public bool TryAdjustRestPose(NetObject net, ref Pose worldPose)
        {
            if (net == null || net.Kind == null || !BurgerKinds.IsIngredient(net.Kind.Kind) ||
                !_watched.ContainsKey(net) || _net.IsHeld || _net.IsAwake || _cargo.Count > 0)
            {
                return false;
            }

            return _column.TryAdjustRestPose(net, ref worldPose);
        }

        // ------------------------------------------------------------------- pickup

        /// <summary>The board went into OUR hand: everything standing in its column comes along.</summary>
        private void HandleGrabbedLocally(bool rightHand)
        {
            if (_column == null || !_column.HasVolume)
            {
                return;
            }

            // Free layers only: one in a hand or still flying was never part of this column.
            _column.Collect(_free, true);

            for (int i = 0; i < _free.Count; i++)
            {
                NetObject layer = _free[i];
                if (layer == null || layer == _net || _cargo.Contains(layer))
                {
                    continue;
                }

                // Optimistic, like every other grab (§10.10): the anchor goes on BEFORE the answer, or
                // the layer's own bridge would seat it in the palm for a frame and fight for the hand the
                // board is already in.
                _cargo.Add(layer);
                _cargoLocal.Add(ToLocal(layer.transform));
                _cargoBridges.Add(Anchor(layer));
                _claimed[layer.NetId] = Time.time + ClaimAnswerSeconds;
                _column.RemovePin(layer);

                NetObjectSync.SendGrab(layer.NetId, rightHand);
            }
        }

        /// <summary>Drops cargo the server did not give us — a refusal is silent, so the deadline is the
        /// only answer there is.</summary>
        private void PruneCargo()
        {
            for (int i = _cargo.Count - 1; i >= 0; i--)
            {
                NetObject layer = _cargo[i];
                bool lost = layer == null || layer.NetId <= 0;

                if (!lost && _claimed.TryGetValue(layer.NetId, out float deadline))
                {
                    if (layer.IsMine && layer.IsHeld)
                    {
                        _claimed.Remove(layer.NetId);
                    }
                    else
                    {
                        lost = (!layer.IsMine && layer.Owner != 0) || Time.time >= deadline;
                    }
                }
                else if (!lost && !layer.IsMine)
                {
                    // Ownership walked away (somebody caught it, or the server rested it).
                    lost = true;
                }

                if (lost)
                {
                    Drop(i);
                }
            }
        }

        /// <summary>Cargo rides the board rigidly: the offsets taken at the pickup, written every frame.
        /// <para>⚠️ <c>isKinematic</c> is NOT written here — the ownership flag drives it
        /// (<c>NetObjectBody</c>) and a second writer loses the interpolation setting with it.</para></summary>
        private void SeatCargo()
        {
            if (_cargo.Count == 0)
            {
                return;
            }

            transform.GetPositionAndRotation(out Vector3 origin, out Quaternion rotation);

            for (int i = 0; i < _cargo.Count; i++)
            {
                NetObject layer = _cargo[i];
                if (layer == null)
                {
                    continue;
                }

                if (_cargoBridges[i] != null)
                {
                    _cargoBridges[i].CarryAnchor = cargoAnchor;
                }

                var body = layer.GetComponent<Rigidbody>();
                if (body != null && !body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }

                Pose local = _cargoLocal[i];
                layer.transform.SetPositionAndRotation(origin + rotation * local.position,
                    rotation * local.rotation);
            }
        }

        // ------------------------------------------------------------------- putting down

        /// <summary>Letting go OVER a serving board pours the stack onto it and puts this board down
        /// beside the burger instead of on top of it.</summary>
        public bool TryOverrideReleasePose(ref Pose worldPose)
        {
            if (_cargo.Count == 0)
            {
                return false;
            }

            BurgerServingBoard target = FindServingBoard();
            if (target == null)
            {
                return false;
            }

            // Handed over BEFORE the board is put down: the layers are still ours, and the serving board
            // computes its column from what is STANDING on it — this board's cargo is held, so it is not
            // counted twice.
            target.AcceptTransferredStack(_cargo);
            ClearCargo();

            if (!target.TryResolveSideSpot(transform.position, FootprintRadius(), out Pose spot))
            {
                return false;
            }

            // The spot's Y is the SURFACE; the board's own bottom is what has to land on it.
            float lift = BurgerStackColumn.TryBounds(_net, out Bounds bounds)
                ? transform.position.y - bounds.min.y
                : 0f;

            worldPose = new Pose(spot.position + Vector3.up * lift,
                BurgerStackColumn.Upright(spot.rotation, transform.rotation));
            return true;
        }

        /// <summary>The serving board this one is being held over.</summary>
        private BurgerServingBoard FindServingBoard()
        {
            Vector3 point = cargoAnchor != null ? cargoAnchor.position : transform.position;
            IReadOnlyList<BurgerServingBoard> boards = BurgerServingBoard.All;

            for (int i = 0; i < boards.Count; i++)
            {
                BurgerServingBoard board = boards[i];
                if (board != null && board.IsOver(point))
                {
                    return board;
                }
            }

            return null;
        }

        private float FootprintRadius()
        {
            return BurgerStackColumn.TryBounds(_net, out Bounds bounds)
                ? Mathf.Max(bounds.extents.x, bounds.extents.z)
                : FallbackRadius;
        }

        /// <summary>A local release that was UNDONE means the board is someone else's — the cargo is
        /// still ours, so it is put down where it is rather than left hanging on a board we no longer
        /// drive. A published release keeps the cargo: it rides the flight and lands with the board
        /// (<see cref="HandleBoardRest"/>).</summary>
        private void HandleReleasedLocally(bool published)
        {
            if (!published)
            {
                SettleCargo();
            }
        }

        private void HandleOwnerChanged(NetObject net, int previousOwner)
        {
            if (!net.IsMine)
            {
                SettleCargo();
            }
        }

        private void HandleBoardRest(NetObject net)
        {
            SettleCargo();
        }

        /// <summary>Puts the cargo down at the pose it is riding at, bottom to top: from here it is an
        /// ordinary resting column again.
        /// <para>⚠️ <c>object_release</c> + <c>object_rest</c> are sent although the wire still calls the
        /// layer held — the pair is what ends ownership and freezes the pose. The PIN carries it through
        /// the round trip, otherwise gravity walks it off the pose we just published.</para></summary>
        private void SettleCargo()
        {
            if (_cargo.Count == 0)
            {
                return;
            }

            transform.GetPositionAndRotation(out Vector3 origin, out Quaternion rotation);

            for (int i = 0; i < _cargo.Count; i++)
            {
                NetObject layer = _cargo[i];
                if (layer == null || layer.NetId <= 0 || !layer.IsMine)
                {
                    continue;
                }

                Pose local = _cargoLocal[i];
                var pose = new Pose(origin + rotation * local.position, rotation * local.rotation);

                var body = layer.GetComponent<Rigidbody>();
                if (body != null && !body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }

                layer.transform.SetPositionAndRotation(pose.position, pose.rotation);

                Pose arena = ArenaSpace.WorldToArena(pose);
                NetObjectSync.SendRelease(layer.NetId, arena.position, arena.rotation);
                NetObjectSync.SendRest(layer.NetId, arena.position, arena.rotation);

                _column.Pin(layer, pose, SettlePinSeconds);
            }

            ClearCargo();
        }

        // ------------------------------------------------------------------- cargo bookkeeping

        private NetObjectGrabBridge Anchor(NetObject layer)
        {
            var bridge = layer.GetComponent<NetObjectGrabBridge>();
            if (bridge != null)
            {
                bridge.CarryAnchor = cargoAnchor;
            }

            return bridge;
        }

        private void Drop(int index)
        {
            NetObject layer = _cargo[index];
            NetObjectGrabBridge bridge = _cargoBridges[index];

            // ⚠️ A leftover anchor would keep the layer's own bridge silent forever: it would hang
            // wherever this board last left it, with nothing writing its pose.
            if (bridge != null)
            {
                bridge.CarryAnchor = null;
            }

            if (layer != null)
            {
                _claimed.Remove(layer.NetId);
            }

            _cargo.RemoveAt(index);
            _cargoLocal.RemoveAt(index);
            _cargoBridges.RemoveAt(index);
        }

        private void ClearCargo()
        {
            for (int i = 0; i < _cargoBridges.Count; i++)
            {
                if (_cargoBridges[i] != null)
                {
                    _cargoBridges[i].CarryAnchor = null;
                }
            }

            _cargo.Clear();
            _cargoLocal.Clear();
            _cargoBridges.Clear();
            _claimed.Clear();
        }

        private Pose ToLocal(Transform layer)
        {
            Quaternion inverse = Quaternion.Inverse(transform.rotation);
            layer.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);
            return new Pose(inverse * (position - transform.position), inverse * rotation);
        }
    }
}
