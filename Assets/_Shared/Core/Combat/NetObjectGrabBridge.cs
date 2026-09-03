using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.Player;
using VortexArena.Net;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Puts a grabbable NETWORK OBJECT into a hand and keeps it there — the <c>WorldSingle</c>
    /// counterpart of <see cref="WristHolster"/> (§10.10).
    /// <para><b>Optimistic grab.</b> <c>object_grab</c> has no reply: the press grabs LOCALLY at once
    /// and the answer is the broadcast <c>object_state.owner</c>. If that owner turns out to be someone
    /// else the local grab is UNDONE from <see cref="NetObject.OwnerChanged"/> — which is exactly why
    /// that event is raised BEFORE <c>StateChanged</c>. There is no rejection message and none is
    /// needed.</para>
    /// <para><b>The server can also hand an object over on its own</b> — a dispenser's <c>take</c> spawns
    /// it owned AND held (§10.10). Nothing was pressed locally, so the hand is adopted from the state's
    /// own flags; without that the object would hang in the air with <c>owner</c> pointing at us.</para>
    /// <para>⚠️ <b>The object is attached with the CANONICAL grip pose</b> (<see cref="ItemGripSolver"/>,
    /// §10.10): a free grip means a different offset on every client, i.e. the same knife held
    /// differently on each screen. The pose never goes on the wire — both ends run the same
    /// solver.</para>
    /// <para>⚠️ <b>This object's <c>itemL</c>/<c>itemR</c> byte stays 0</b> (§6.6): the remote hand draws
    /// the network object's own instance, not a byte-driven clone. The byte is suppressed at the source
    /// (<c>HeldItems.Slot.NetItemId</c> via <see cref="ItemDefinition.IsWorldSingle"/>) so the local
    /// finger pose still works.</para>
    /// <para><b>Seam.</b> This component owns the object only while <see cref="NetObject.IsHeld"/>. On
    /// release it sends <c>object_release</c> — the flight window opens and from there
    /// <c>NetObjectPoseSender</c> (pose stream + <c>object_rest</c>) and <c>NetObjectBody</c> (free
    /// placement, physics authority) take over. Two writers on one transform is a visible jitter, not a
    /// compile error.</para>
    /// </summary>
    /// <remarks>
    /// ⚠️ The held pose is written in <see cref="LateUpdate"/> at the DEFAULT execution order, like
    /// <c>Weapon</c> and <c>WristHolster</c>: <c>HandGripPoser</c> runs at order 100 and locks the hand
    /// onto what is written here, so an earlier writer is required or the hand would lag one frame
    /// behind the object.
    /// </remarks>
    [RequireComponent(typeof(NetObject))]
    [DisallowMultipleComponent]
    public sealed class NetObjectGrabBridge : MonoBehaviour
    {
        [Tooltip("Eşya tanımı: kavrama kaydı (elin nasıl duracağı) + örnekleme/bırakma eksenleri. " +
                 "Örnekleme WorldSingle olmalı — kopyalanan bir eşyanın sahiplik devrine ihtiyacı yok.")]
        [SerializeField] private ItemDefinition item;

        [Tooltip("Objenin alındığı yakınlık soketi. Boşsa çocuklarda aranır.")]
        [SerializeField] private GripSocket socket;

        private const string HapticSource = "grab";
        private const float GrabHapticSeconds = 0.12f;
        private const float GrabHapticAmplitude = 0.6f;
        private const float ReleaseHapticSeconds = 0.08f;
        private const float ReleaseHapticAmplitude = 0.35f;

        private NetObject _net;
        private Rigidbody _body;

        /// <summary>The object rides a CARRIER (a spatula blade, a serving board) instead of a palm: the
        /// same hand, a different anchor. Set on EVERY client by the carrier, which derives the relation
        /// from <c>owner</c>+<c>held</c> — it is never on the wire.
        /// <para>⚠️ While it is set this bridge writes NOTHING: the carrier owns the transform, and two
        /// writers on one transform is a visible jitter, not a compile error.</para></summary>
        public Transform CarryAnchor { get; set; }

        /// <summary>The controller holding it LOCALLY; <c>None</c> = we are not holding it (someone else
        /// may be).</summary>
        private OVRInput.Controller _localHand = OVRInput.Controller.None;
        private bool _localRight;

        private bool _gripWasLeft;
        private bool _gripWasRight;

        /// <summary>Last seen <see cref="NetObject.IsHeld"/> — only its EDGE is acted on.</summary>
        private bool _wasHeld;

        /// <summary>End of the take/let-go buzz. The arbiter wants a report every frame (heartbeat), so
        /// the clock runs in <see cref="LateUpdate"/> rather than in a coroutine.</summary>
        private float _hapticUntil;

        private float _hapticAmplitude;
        private bool _hapticRight;

        // ⚠️ The home pose is kept in ARENA space (§3): calibration can move the arena root under the
        // scene, and a world-space copy would send a Return prop back to a place that no longer exists.
        private Vector3 _homeArenaPosition;
        private Quaternion _homeArenaRotation;

        private void Awake()
        {
            _net = GetComponent<NetObject>();
            _body = GetComponent<Rigidbody>();

            if (socket == null)
            {
                socket = GetComponentInChildren<GripSocket>(true);
            }

            _homeArenaPosition = ArenaSpace.WorldToArena(transform.position);
            _homeArenaRotation = ArenaSpace.WorldToArena(transform.rotation);

            if (item == null)
            {
                // Loud: with no definition the object would still be grabbable but land in the hand at
                // the bare controller anchor, which reads on site as "the grip pose is broken".
                Debug.LogError($"[NetObjectGrabBridge] '{name}' için ItemDefinition atanmamış — obje " +
                               "ele gelse bile kavrama pozu uygulanmaz.", this);
            }
            else if (!item.IsWorldSingle)
            {
                Debug.LogError($"[NetObjectGrabBridge] '{name}': '{item.DisplayName}' tanımının " +
                               "örneklemesi WorldSingle DEĞİL — uzak oyuncular hem bu objeyi hem de " +
                               "bayttan üretilen bir kopyayı görür (iki eşya).", this);
            }

            if (socket == null && item != null && item.GrabPath == ItemGrabPath.ProximitySocket)
            {
                Debug.LogError($"[NetObjectGrabBridge] '{name}' altında GripSocket yok ama tanımın " +
                               "alma yolu ProximitySocket — bu obje hiçbir elden alınamaz.", this);
            }
        }

        private void OnEnable()
        {
            _net.OwnerChanged += HandleOwnerChanged;
            _net.StateChanged += HandleStateChanged;
            _wasHeld = _net.IsHeld;
        }

        private void OnDisable()
        {
            _net.OwnerChanged -= HandleOwnerChanged;
            _net.StateChanged -= HandleStateChanged;

            // ⚠️ The hand gate must not outlive the bridge: a leftover flag would keep that hand closed
            // to weapons for the rest of the session, with nothing left to explain why.
            ReleaseLocal(false);

            if (_hapticUntil > 0f)
            {
                _hapticUntil = 0f;
                ControllerHaptics.ReportHand(HapticSource, _hapticRight, 0f);
            }
        }

        private void LateUpdate()
        {
            // Before the guards: a buzz that started must still be able to switch itself off.
            TickHaptics();

            if (_net.NetId <= 0 || _net.Kind == null || !_net.Kind.IsGrabbable)
            {
                return;
            }

            TickInput();
            TickSocket();
            TickHeldPose();
        }

        // ------------------------------------------------------------------- input

        /// <summary>Press edge takes, release edge lets go. The button is the same analog grip and the
        /// same threshold as every other hold path (<see cref="WeaponGranter.GripThreshold"/>).</summary>
        private void TickInput()
        {
            bool gripLeft = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.LTouch)
                            >= WeaponGranter.GripThreshold;
            bool gripRight = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.RTouch)
                             >= WeaponGranter.GripThreshold;

            bool pressLeft = gripLeft && !_gripWasLeft;
            bool pressRight = gripRight && !_gripWasRight;

            _gripWasLeft = gripLeft;
            _gripWasRight = gripRight;

            if (_localHand != OVRInput.Controller.None)
            {
                bool stillHeld = _localHand == OVRInput.Controller.RTouch ? gripRight : gripLeft;
                if (!stillHeld)
                {
                    ReleaseLocal(true);
                }

                return;
            }

            // Already in a hand (ours would have returned above, so: someone else's). No stealing —
            // ownership is only free after the holder releases (§10.10).
            if (_net.IsHeld || !IsTakeable || !CalibrationState.IsCalibrated)
            {
                return;
            }

            if (!socket.TryResolveHand(out OVRInput.Controller hand, out bool rightHand))
            {
                return;
            }

            // ⚠️ The EDGE is tracked per hand across every frame, not only while the hand is inside the
            // socket: sampled on entry, a hand already squeezing the grip would grab the moment it
            // drifts into the volume, without the player pressing anything.
            if (rightHand ? pressRight : pressLeft)
            {
                Grab(hand, rightHand);
            }
        }

        /// <summary>Takes the object into that hand LOCALLY and asks the server. There is no waiting: the
        /// answer is <see cref="HandleOwnerChanged"/>.</summary>
        private void Grab(OVRInput.Controller hand, bool rightHand)
        {
            _localHand = hand;
            _localRight = rightHand;

            // ⚠️ Order: close the hand FIRST, then park the weapon — the granter's tick reads the gate
            // and would otherwise re-grant a random weapon into the hand on the very next frame.
            WeaponGranter.SetThrowableHeld(hand, true);
            WeaponGranter.StowHeld(hand);

            if (_body != null)
            {
                // Physics must not fight the hand during the optimistic window (NetObjectBody leaves a
                // held object alone, but the state saying "held" has not arrived yet).
                RigidbodyDrive.SetKinematic(_body, true);
            }

            // Claims the hand in the slot registry: this is what feeds the FINGER pose. The wire byte
            // stays 0 for a WorldSingle item — suppressed inside HeldItems, not here (§6.6).
            if (!HeldItems.Report(this, rightHand, item, transform, GripSocketKind.Primary, hand))
            {
                Debug.LogWarning($"[NetObjectGrabBridge] '{name}': el başka bir eşya tarafından " +
                                 "tutuluyor — obje ele alındı ama parmaklar kavramayı almadı.", this);
            }

            socket.Hide();
            Buzz(rightHand, GrabHapticAmplitude, GrabHapticSeconds);

            NetObjectSync.SendGrab(_net.NetId, rightHand);
        }

        /// <summary>Short buzz in the hand that took or let go — the grab is optimistic and has no other
        /// confirmation until the state comes back.</summary>
        private void Buzz(bool right, float amplitude, float seconds)
        {
            // A hand switch would leave the old side buzzing until the arbiter's timeout.
            if (_hapticUntil > 0f && right != _hapticRight)
            {
                ControllerHaptics.ReportHand(HapticSource, _hapticRight, 0f);
            }

            _hapticRight = right;
            _hapticAmplitude = amplitude;
            _hapticUntil = Time.unscaledTime + seconds;
        }

        private void TickHaptics()
        {
            if (_hapticUntil <= 0f)
            {
                return;
            }

            if (Time.unscaledTime >= _hapticUntil)
            {
                _hapticUntil = 0f;
                ControllerHaptics.ReportHand(HapticSource, _hapticRight, 0f);
                return;
            }

            ControllerHaptics.ReportHand(HapticSource, _hapticRight, _hapticAmplitude);
        }

        /// <summary>Lets the object go locally.</summary>
        /// <param name="send">True on a real hand release (<c>object_release</c> goes out and the release
        /// axis is applied); false when the grab was UNDONE — the object belongs to someone else now and
        /// telling the server we dropped it would fight the real owner.</param>
        private void ReleaseLocal(bool send)
        {
            OVRInput.Controller hand = _localHand;
            if (hand == OVRInput.Controller.None)
            {
                return;
            }

            _localHand = OVRInput.Controller.None;

            // ⚠️ The slot is freed BEFORE the weapon is restored: the restore claims the same hand and
            // would be refused while this object still holds it.
            HeldItems.Release(this);
            WeaponGranter.RestoreStowed(hand);
            WeaponGranter.SetThrowableHeld(hand, false);

            if (!send)
            {
                return;
            }

            Buzz(_localRight, ReleaseHapticAmplitude, ReleaseHapticSeconds);

            if (item != null && item.ReleaseMode == ItemReleaseMode.Return)
            {
                ApplyReturn();
                return;
            }

            ApplyPhysicsRelease(hand);
        }

        /// <summary>Release axis <c>Return</c>: the object goes back to its socket, and THAT pose is what
        /// goes on the wire as the release pose — the flight window then opens on an object already at
        /// rest, so <c>NetObjectPoseSender</c> closes ownership almost immediately.</summary>
        private void ApplyReturn()
        {
            Vector3 world = ArenaSpace.ArenaToWorld(_homeArenaPosition);
            Quaternion rotation = ArenaSpace.ArenaToWorld(_homeArenaRotation);
            transform.SetPositionAndRotation(world, rotation);

            if (_body != null)
            {
                // ⚠️ isKinematic is NOT written here — NetObjectBody owns it (physics runs on the owner
                // only) and two writers would flip it every frame. Zeroing motion and gravity is enough
                // to keep a returned prop on its socket while the rest timer runs out.
                _body.useGravity = false;
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
            }

            NetObjectSync.SendRelease(_net.NetId, _homeArenaPosition, _homeArenaRotation);
        }

        /// <summary>Release axis <c>Physics</c>: the rigidbody is set free with the hand's own velocity.
        /// <para>⚠️ Ownership is KEPT through the flight (§5.1) — the owner is the only one simulating,
        /// everyone else follows the streamed pose. A single-instance object must NOT be simulated on
        /// both headsets: that is one knife in two places.</para></summary>
        private void ApplyPhysicsRelease(OVRInput.Controller hand)
        {
            if (_body != null)
            {
                // Set here rather than left to NetObjectBody: the "held" flag has not fallen yet, so
                // NetObjectBody still skips this object and the throw would start a frame late.
                RigidbodyDrive.SetKinematic(_body, false);
                _body.linearVelocity = ResolveReleaseVelocity(hand);
            }

            transform.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);
            NetObjectSync.SendRelease(_net.NetId,
                ArenaSpace.WorldToArena(position), ArenaSpace.WorldToArena(rotation));
        }

        /// <summary>Release velocity of the controller, in WORLD space.
        /// <para>⚠️ <see cref="OVRInput.GetLocalControllerVelocity"/> is in TRACKING space (the hand
        /// anchor's parent); used raw, the throw would go sideways as soon as the rig is rotated in the
        /// arena.</para></summary>
        private static Vector3 ResolveReleaseVelocity(OVRInput.Controller hand)
        {
            Vector3 velocity = OVRInput.GetLocalControllerVelocity(hand);

            Transform anchor = WeaponGranter.ResolveHandAnchor(hand);
            Transform trackingSpace = anchor != null ? anchor.parent : null;
            return trackingSpace != null ? trackingSpace.TransformDirection(velocity) : velocity;
        }

        // ------------------------------------------------------------------- server answers

        /// <summary>The server decided who owns it.
        /// <para>⚠️ Raised BEFORE <c>StateChanged</c> exactly so the optimistic grab can be undone here:
        /// if the owner is not us the object must fall out of THIS hand before presentation reacts to
        /// the same message, and appear in the real owner's hand instead.</para></summary>
        private void HandleOwnerChanged(NetObject net, int previousOwner)
        {
            if (_localHand == OVRInput.Controller.None || net.IsMine)
            {
                return;
            }

            ReleaseLocal(false);
        }

        /// <summary>Only the "went into a hand" EDGE is acted on: a held object must not be simulated.
        /// <para>The falling edge is deliberately NOT handled — once the object leaves a hand it belongs
        /// to <c>NetObjectBody</c>/<c>NetObjectPoseSender</c>, and a second writer on the same body
        /// diverges per client.</para></summary>
        private void HandleStateChanged(NetObject net, NetStateOrigin origin)
        {
            if (net.IsHeld == _wasHeld)
            {
                return;
            }

            _wasHeld = net.IsHeld;

            if (!net.IsHeld)
            {
                return;
            }

            if (_body != null)
            {
                RigidbodyDrive.SetKinematic(_body, true);
            }

            // The server can put an object straight into a hand: a dispenser's `take` spawns it already
            // owned and held (§10.10). Nobody pressed anything locally, so the hand is adopted HERE —
            // otherwise the object hangs in the air with `owner` pointing at us and no hand driving it.
            // ⚠️ A carried object is the exception: the hand is already full of the CARRIER, so claiming
            // the slot would be refused and the spatula would lose its finger pose.
            if (net.IsMine && _localHand == OVRInput.Controller.None && CarryAnchor == null)
            {
                AdoptServerGrab();
            }
        }

        /// <summary>Takes an object the SERVER already handed us into the hand its flags name (bit3).</summary>
        /// <remarks>⚠️ Sends NOTHING: <c>object_grab</c> is the client ASKING, and here the server has
        /// already decided — asking again would be a second claim on an object we own.</remarks>
        private void AdoptServerGrab()
        {
            bool rightHand = _net.HeldByRightHand;
            OVRInput.Controller hand = rightHand ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch;

            _localHand = hand;
            _localRight = rightHand;

            // Same order as Grab(): close the hand before parking the weapon.
            WeaponGranter.SetThrowableHeld(hand, true);
            WeaponGranter.StowHeld(hand);

            if (!HeldItems.Report(this, rightHand, item, transform, GripSocketKind.Primary, hand))
            {
                Debug.LogWarning($"[NetObjectGrabBridge] '{name}': sunucu objeyi bu ele verdi ama el " +
                                 "başka bir eşya tarafından tutuluyor — parmaklar kavramayı almadı.", this);
            }

            if (socket != null)
            {
                socket.Hide();
            }
        }

        // ------------------------------------------------------------------- per frame

        private void TickSocket()
        {
            if (socket == null)
            {
                return;
            }

            socket.Tick(IsTakeable && !_net.IsHeld && _localHand == OVRInput.Controller.None &&
                        CalibrationState.IsCalibrated);
        }

        /// <summary>Does the definition actually route this object through a socket. The grab path is the
        /// item's rule, not the prefab's: a definition switched to <c>None</c> must stop drawing the
        /// socket too, or the player is invited to press on something that will not answer.</summary>
        private bool IsTakeable =>
            item != null && socket != null && item.GrabPath == ItemGrabPath.ProximitySocket;

        /// <summary>Seats the object in the OWNER's hand while it is held: our own palm when the owner is
        /// us, the remote player's interpolated hand otherwise.
        /// <para>⚠️ The remote hand comes from the remote player record (<c>RemotePlayerRegistry</c>) —
        /// the SAME source the remote avatar's own items use. A second channel would place the object and
        /// the hand at two different times.</para></summary>
        private void TickHeldPose()
        {
            // CarryAnchor: the object sits on a carrier, not in the palm — that placement is the
            // carrier's and this bridge must not fight it.
            if (!_net.IsHeld || item == null || CarryAnchor != null)
            {
                return;
            }

            if (_localHand != OVRInput.Controller.None)
            {
                if (WeaponGranter.TryResolvePalm(_localHand, out Pose palm))
                {
                    ApplyGrip(palm, _localRight);
                }

                return;
            }

            if (_net.IsMine)
            {
                // Owned by us but held by no local hand: the state is stale for a frame (release just
                // went out). Leave the transform alone rather than snapping it somewhere.
                return;
            }

            RemotePlayerRegistry registry = RemotePlayerRegistry.Instance;
            if (registry == null ||
                !registry.GetInterpolatedPose(_net.Owner, out Pose _, out Pose handL, out Pose handR))
            {
                return;
            }

            // Which hand is bit3 of the object's flags — the only thing that says it (§10.10).
            bool rightHand = _net.HeldByRightHand;
            Pose palmWorld = ArenaSpace.ArenaToWorld(rightHand ? handR : handL);
            ApplyGrip(palmWorld, rightHand);
        }

        /// <summary>The ONE grip implementation runs on both ends (<see cref="ItemGripSolver"/>) — two
        /// measures would place the same object differently on each screen. One-handed only: a world prop
        /// has no foregrip contract.</summary>
        private void ApplyGrip(in Pose palm, bool rightHand)
        {
            ItemGripSolver.Solve(item, rightHand, !rightHand, palm, false, Vector3.zero, 0f,
                out Vector3 position, out Quaternion rotation);

            transform.SetPositionAndRotation(position, rotation);
        }
    }
}
