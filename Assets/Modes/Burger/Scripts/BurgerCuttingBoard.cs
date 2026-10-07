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
    /// <para><b>A layer dropped on the board while it is HELD joins the cargo</b> the same way one laid
    /// on the resting board joins the column — a held board is not stable ground, so a layer left to
    /// physics has nothing to come to rest on.</para>
    /// <para><b>The board carries a burger, never a tool:</b> while it is held, non-ingredients stop
    /// colliding with it and drop away (<see cref="TickToolPassthrough"/>).</para>
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

        /// <summary>Ranking penalty (m) on the board's OWN grip socket: the board and the layers standing
        /// on it are centimetres apart, and a hand reaching into the stack means the layer, not the
        /// plate under it. Only the arbiter's ordering sees it — the accept radius is untouched.</summary>
        private const float BoardGrabRankBias = 0.05f;

        /// <summary>Margin (m) around the board's solid box while looking for tools to fall through it.
        /// Contacts that already exist raise no <c>OnCollisionEnter</c>, so the pass is a proximity scan
        /// rather than an event.</summary>
        private const float ToolScanMargin = 0.04f;

        /// <summary>Distance (m) from the top of the cargo column in which a layer landing on the held
        /// board joins it. ⚠️ Without a band the whole stack volume claims: carrying the board through
        /// the grill would sweep up every patty under it.</summary>
        private const float ClaimBandMetres = 0.10f;

        /// <summary>Layer thickness (m) for a layer with no solid collider to measure.</summary>
        private const float FallbackLayerHeight = 0.03f;

        private NetObject _net;
        private NetObjectGrabBridge _bridge;
        private NetObjectPoseSender _sender;
        private BurgerStackColumn _column;

        /// <summary>Cargo bottom to top — the order the stack keeps all the way onto the serving
        /// board.</summary>
        private readonly List<NetObject> _cargo = new List<NetObject>();

        /// <summary>Pose of each cargo layer in BOARD space, laid out by <see cref="RestackCargo"/>.</summary>
        private readonly List<Pose> _cargoLocal = new List<Pose>();

        /// <summary>Height of the cargo column along the cargo anchor's up axis (anchor-local units).</summary>
        private float _cargoTop;

        private readonly List<NetObjectGrabBridge> _cargoBridges = new List<NetObjectGrabBridge>();

        /// <summary>Claim sent, no answer yet: netId → the moment we give up on it.</summary>
        private readonly Dictionary<int, float> _claimed = new Dictionary<int, float>();

        /// <summary>Ingredients over the board, with the sender we subscribed to.</summary>
        private readonly Dictionary<NetObject, NetObjectPoseSender> _watched =
            new Dictionary<NetObject, NetObjectPoseSender>();

        /// <summary>Scratch for the pickup query — reused so a grab allocates nothing.</summary>
        private readonly List<NetObject> _free = new List<NetObject>();

        /// <summary>Solid (non-trigger) colliders of the board: the surface a tool would ride on.</summary>
        private readonly List<Collider> _solids = new List<Collider>();

        /// <summary>Tools whose collision with the board is switched OFF right now, with the colliders
        /// the pairs were made from — kept so exactly those pairs can be switched back on.</summary>
        private readonly Dictionary<NetObject, List<Collider>> _passthrough =
            new Dictionary<NetObject, List<Collider>>();

        private readonly List<NetObject> _restored = new List<NetObject>();

        /// <summary>Cargo derived on a headset that is not the holder's.</summary>
        private readonly List<NetObject> _mirror = new List<NetObject>();

        private static readonly Collider[] Overlap = new Collider[64];

        private void Awake()
        {
            _net = GetComponent<NetObject>();
            _bridge = GetComponent<NetObjectGrabBridge>();
            _sender = GetComponent<NetObjectPoseSender>();
            _column = new BurgerStackColumn(stackTrigger, stackAnchor, transform);

            CollectSolids();

            // Set in code, not on the prefab: the bias is a rule of THIS board (its socket must lose to
            // the layers standing on it), not a knob someone should retune per instance.
            var boardSocket = GetComponentInChildren<GripSocket>(true);
            if (boardSocket != null)
            {
                boardSocket.RankBiasMeters = BoardGrabRankBias;
            }

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
            RestoreAllPassthrough();
        }

        private void LateUpdate()
        {
            if (_net == null || _net.NetId <= 0)
            {
                return;
            }

            TickToolPassthrough();

            // A board in SOMEBODY ELSE'S hand: the cargo is derived here, it is never claimed or placed
            // from this headset (see MirrorCargo).
            if (_net.IsHeld && !_net.IsMine)
            {
                MirrorCargo();
                SeatCargo();
                _column.TickPins();
                return;
            }

            PruneCargo();
            ClaimOverBoard();
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

                AddCargo(layer, rightHand);
            }

            RestackCargo();
        }

        /// <summary>Takes a layer onto the cargo and asks for it.
        /// <para>⚠️ Optimistic, like every other grab (§10.10): the anchor goes on BEFORE the answer, or
        /// the layer's own bridge would seat it in the palm for a frame and fight for the hand the board
        /// is already in.</para></summary>
        /// <remarks>The caller lays the column out afterwards (<see cref="RestackCargo"/>).</remarks>
        private void AddCargo(NetObject layer, bool rightHand)
        {
            _cargo.Add(layer);
            _cargoLocal.Add(ToLocal(layer.transform));
            _cargoBridges.Add(Anchor(layer));
            _claimed[layer.NetId] = Time.time + ClaimAnswerSeconds;
            _column.RemovePin(layer);

            NetObjectSync.SendGrab(layer.NetId, rightHand);
        }

        // ------------------------------------------------------------------- claiming mid-carry

        /// <summary>A layer dropped onto the board WHILE IT IS HELD joins the cargo, the same way one
        /// laid on the resting board joins the column. Without it the layer has nothing to rest on (a
        /// held board is not stable ground, <c>NetObjectPoseSender</c>) and slides off the moving
        /// plate.</summary>
        /// <remarks>⚠️ Only the holder claims — everyone else derives the same set (<see cref="MirrorCargo"/>).</remarks>
        private void ClaimOverBoard()
        {
            // ⚠️ The LOCAL hold is part of the gate: the wire still calls the board held for a round trip
            // after it is let go, and a stack just poured onto the serving board sits in this volume —
            // it would be claimed straight back out of the burger.
            if (!_net.IsHeld || !_net.IsMine || !HeldItems.Holds(transform) || _watched.Count == 0)
            {
                return;
            }

            bool rightHand = _net.HeldByRightHand;
            bool added = false;

            foreach (KeyValuePair<NetObject, NetObjectPoseSender> entry in _watched)
            {
                NetObject layer = entry.Key;
                if (!CanJoinCargo(layer))
                {
                    continue;
                }

                AddCargo(layer, rightHand);
                added = true;
            }

            if (added)
            {
                RestackCargo();
            }
        }

        /// <summary>Free enough to be taken onto the moving board: in nobody's hand, on no other carrier,
        /// and either resting unowned or in OUR OWN flight (a layer somebody else is flying is theirs
        /// until it lands).</summary>
        private bool CanJoinCargo(NetObject layer)
        {
            // HeldItems: a layer just taken off by our free hand is briefly "not held" on the wire.
            if (layer == null || layer == _net || layer.NetId <= 0 || layer.IsHeld ||
                HeldItems.Holds(layer.transform) ||
                _cargo.Contains(layer) || _claimed.ContainsKey(layer.NetId) ||
                BurgerStackColumn.IsCarried(layer))
            {
                return false;
            }

            if (layer.Owner != 0 && !layer.IsMine)
            {
                return false;
            }

            float top = CargoTopY();
            float height = BurgerStackColumn.TryBounds(layer, out Bounds bounds)
                ? bounds.min.y
                : layer.transform.position.y;

            return Mathf.Abs(height - top) <= ClaimBandMetres;
        }

        /// <summary>Top of the cargo column in world Y; the cargo anchor itself when nothing rides
        /// yet.</summary>
        private float CargoTopY()
        {
            return _cargo.Count > 0
                ? cargoAnchor.TransformPoint(0f, _cargoTop, 0f).y
                : cargoAnchor.position.y;
        }

        /// <summary>Lays the cargo out as the straight column the resting board builds: centred on the
        /// cargo anchor's axis, each layer level with the anchor (own spin kept), stacked by its own
        /// thickness in cargo order. Stores each pose in BOARD space for <see cref="SeatCargo"/>.
        /// <para>⚠️ Measured in the ANCHOR's frame (<see cref="BurgerStackColumn.TryLocalBounds"/>): the
        /// board tilts in the hand, and world boxes of tilted layers open gaps and slide layers off the
        /// axis. The result depends only on set + order + prefab, so every headset gets the same column.</para></summary>
        private void RestackCargo()
        {
            _cargoTop = 0f;
            Quaternion anchorRotation = cargoAnchor.rotation;

            for (int i = 0; i < _cargo.Count; i++)
            {
                NetObject layer = _cargo[i];
                if (layer == null)
                {
                    continue;
                }

                Transform pose = layer.transform;
                pose.rotation = BurgerStackColumn.Upright(anchorRotation, pose.rotation);

                Vector3 shift;
                float thickness;
                if (BurgerStackColumn.TryLocalBounds(layer, cargoAnchor, out Bounds bounds))
                {
                    shift = new Vector3(-bounds.center.x, _cargoTop - bounds.min.y, -bounds.center.z);
                    thickness = bounds.size.y;
                }
                else
                {
                    Vector3 pivot = cargoAnchor.InverseTransformPoint(pose.position);
                    shift = new Vector3(-pivot.x, _cargoTop - pivot.y, -pivot.z);
                    thickness = FallbackLayerHeight / Mathf.Max(1e-4f, cargoAnchor.lossyScale.y);
                }

                pose.position += cargoAnchor.TransformVector(shift);
                _cargoTop += thickness;
                _cargoLocal[i] = ToLocal(pose);
            }
        }

        /// <summary>Drops cargo the server did not give us — a refusal is silent, so the deadline is the
        /// only answer there is.</summary>
        private void PruneCargo()
        {
            int before = _cargo.Count;

            for (int i = _cargo.Count - 1; i >= 0; i--)
            {
                NetObject layer = _cargo[i];

                // Taken off by our free hand (NetObjectGrabBridge): the wire still says "board's hand"
                // for a round trip, but seating it again would pull it out of the palm.
                bool lost = layer == null || layer.NetId <= 0 || HeldItems.Holds(layer.transform);

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

            // A layer taken out of the middle must not leave a gap in the column.
            if (_cargo.Count != before)
            {
                RestackCargo();
            }
        }

        /// <summary>Cargo rides the board rigidly: the column's board-space poses, written every frame.
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

        // ------------------------------------------------------------------- cargo on other headsets

        /// <summary>Cargo of a board held by SOMEBODY ELSE: nothing on the wire says which layers ride
        /// it, so the same fact the spatula uses is derived here — held by the board's owner, in the
        /// board's hand.
        /// <para>⚠️ Without this every headset but the holder's seats those layers in the remote
        /// player's PALM (their own grab bridge does it), i.e. the burger floats beside the board.</para>
        /// <para>Laid out by the same rule as the holder's (<see cref="RestackCargo"/>): set + order +
        /// prefab thickness, nothing measured that is not on every headset.</para></summary>
        private void MirrorCargo()
        {
            _mirror.Clear();

            foreach (NetObject candidate in NetObjectRegistry.All)
            {
                if (Rides(candidate))
                {
                    _mirror.Add(candidate);
                }
            }

            _mirror.Sort(CompareByRestHeight);

            // Same set, same order: the layout is still valid — re-measuring every frame buys nothing.
            if (SameLayers(_mirror, _cargo))
            {
                return;
            }

            // Handed back BEFORE the set is rebuilt: a layer that stopped riding would hang wherever
            // this board last left it, with nothing writing its pose.
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

            for (int i = 0; i < _mirror.Count; i++)
            {
                _cargo.Add(_mirror[i]);
                _cargoLocal.Add(default);
                _cargoBridges.Add(Anchor(_mirror[i]));
            }

            RestackCargo();
        }

        private static bool SameLayers(List<NetObject> a, List<NetObject> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Held by the board's owner, in the board's hand — the one fact the wire carries.</summary>
        private bool Rides(NetObject candidate)
        {
            return candidate != null && candidate != _net && candidate.NetId > 0 &&
                   candidate.Kind != null && BurgerKinds.IsIngredient(candidate.Kind.Kind) &&
                   candidate.IsHeld && candidate.Owner == _net.Owner &&
                   candidate.HeldByRightHand == _net.HeldByRightHand;
        }

        /// <summary>Bottom to top by the pose the server last published. ⚠️ The LIVE height cannot be
        /// used: seated cargo already sits where this board put it, so the comparison would only
        /// reproduce the previous frame's order and never correct it.</summary>
        private static int CompareByRestHeight(NetObject a, NetObject b)
        {
            float ay = ArenaSpace.ArenaToWorld(a.RestPosition).y;
            float by = ArenaSpace.ArenaToWorld(b.RestPosition).y;
            return ay.CompareTo(by);
        }

        // ------------------------------------------------------------------- tools fall through

        /// <summary>The board carries a burger, never a tool: while it is HELD the knife, the spatula and
        /// the whole bun lying on it stop colliding with it and drop away.
        /// <para>⚠️ A contact that already exists raises no <c>OnCollisionEnter</c>, so this is a
        /// proximity scan, not an event — a tool put down on the board before the pickup would otherwise
        /// ride it up.</para></summary>
        private void TickToolPassthrough()
        {
            if (_solids.Count == 0)
            {
                return;
            }

            // A board in a LOCAL hand counts as held too: the Held bit has not come back yet, and one
            // round trip of contact is enough to flick the knife off the table.
            if (_net.IsHeld || HeldItems.Holds(transform))
            {
                HideFromTools();
                return;
            }

            RestoreSeparatedTools();
        }

        private void HideFromTools()
        {
            for (int s = 0; s < _solids.Count; s++)
            {
                Collider solid = _solids[s];
                if (solid == null || !solid.enabled)
                {
                    continue;
                }

                Bounds bounds = solid.bounds;
                int count = Physics.OverlapBoxNonAlloc(bounds.center,
                    bounds.extents + Vector3.one * ToolScanMargin, Overlap, Quaternion.identity, ~0,
                    QueryTriggerInteraction.Ignore);

                for (int i = 0; i < count; i++)
                {
                    NetObject tool = Overlap[i] != null
                        ? Overlap[i].GetComponentInParent<NetObject>()
                        : null;

                    if (IsTool(tool) && !_passthrough.ContainsKey(tool))
                    {
                        IgnoreTool(tool);
                    }
                }
            }
        }

        /// <summary>A grabbable net object that is not an ingredient. ⚠️ The grab bridge is part of the
        /// filter: fixed furniture is a net object too, and nothing is gained by letting the board sink
        /// through the counter it is lifted off.</summary>
        private bool IsTool(NetObject candidate)
        {
            return candidate != null && candidate != _net && candidate.NetId > 0 &&
                   candidate.Kind != null && !BurgerKinds.IsIngredient(candidate.Kind.Kind) &&
                   candidate.GetComponent<NetObjectGrabBridge>() != null;
        }

        private void IgnoreTool(NetObject tool)
        {
            var parts = new List<Collider>();
            tool.GetComponentsInChildren(parts);

            for (int i = parts.Count - 1; i >= 0; i--)
            {
                Collider part = parts[i];
                if (part == null || part.isTrigger || !part.enabled)
                {
                    parts.RemoveAt(i);
                    continue;
                }

                for (int s = 0; s < _solids.Count; s++)
                {
                    if (_solids[s] != null && _solids[s].enabled)
                    {
                        Physics.IgnoreCollision(_solids[s], part, true);
                    }
                }
            }

            _passthrough[tool] = parts;
        }

        /// <summary>Gives a tool its collisions back — but only once it is CLEAR of the board:
        /// re-enabling a pair that is interpenetrating fires the tool across the room.</summary>
        private void RestoreSeparatedTools()
        {
            if (_passthrough.Count == 0)
            {
                return;
            }

            _restored.Clear();

            foreach (KeyValuePair<NetObject, List<Collider>> entry in _passthrough)
            {
                if (entry.Key != null && StillOverlaps(entry.Value))
                {
                    continue;
                }

                RestoreTool(entry.Value);
                _restored.Add(entry.Key);
            }

            for (int i = 0; i < _restored.Count; i++)
            {
                _passthrough.Remove(_restored[i]);
            }
        }

        private bool StillOverlaps(List<Collider> parts)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                Collider part = parts[i];
                if (part == null || !part.enabled)
                {
                    continue;
                }

                for (int s = 0; s < _solids.Count; s++)
                {
                    Collider solid = _solids[s];
                    if (solid == null || !solid.enabled)
                    {
                        continue;
                    }

                    if (Physics.ComputePenetration(solid, solid.transform.position,
                            solid.transform.rotation, part, part.transform.position,
                            part.transform.rotation, out Vector3 _, out float _))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void RestoreTool(List<Collider> parts)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                Collider part = parts[i];
                if (part == null || !part.enabled)
                {
                    continue;
                }

                for (int s = 0; s < _solids.Count; s++)
                {
                    if (_solids[s] != null && _solids[s].enabled)
                    {
                        Physics.IgnoreCollision(_solids[s], part, false);
                    }
                }
            }
        }

        /// <summary>⚠️ An ignored pair outlives this component: the flag lives in the physics scene, not
        /// here, so a board disabled mid-carry would leave the tool falling through it for good.</summary>
        private void RestoreAllPassthrough()
        {
            foreach (KeyValuePair<NetObject, List<Collider>> entry in _passthrough)
            {
                RestoreTool(entry.Value);
            }

            _passthrough.Clear();
        }

        private void CollectSolids()
        {
            _solids.Clear();
            GetComponentsInChildren(true, _solids);

            for (int i = _solids.Count - 1; i >= 0; i--)
            {
                if (_solids[i] == null || _solids[i].isTrigger)
                {
                    _solids.RemoveAt(i);
                }
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
            _cargoTop = 0f;
        }

        private Pose ToLocal(Transform layer)
        {
            Quaternion inverse = Quaternion.Inverse(transform.rotation);
            layer.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);
            return new Pose(inverse * (position - transform.position), inverse * rotation);
        }
    }
}
