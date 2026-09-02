using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.Combat;
using VortexArena.Core.Player;
using VortexArena.Net;

namespace VortexArena.Modes.Burger
{
    /// <summary>Carries ingredients on a HELD object — the spatula's blade, the serving board's face.
    /// <para><b>Nothing new goes on the wire.</b> Cargo is claimed with a plain <c>object_grab</c>, so a
    /// carried ingredient is simply "held by the carrier's owner, in the carrier's hand" (§10.5). Every
    /// client derives the same set from that state and seats it on the anchor; a separate field would be
    /// a second contract to keep in sync with the first.</para>
    /// <para>⚠️ <b>Stacking order comes from the RESTING pose, not from arrival order:</b> a headset
    /// joining mid-carry gets <c>world_state</c> unordered, so arrival order would scramble ITS burger —
    /// and the recipe is read bottom to top off the board, i.e. the scramble becomes a wrong serve.</para>
    /// <para>⚠️ Only the OWNER claims and spills; everyone else derives the same placement.</para></summary>
    /// <remarks>⚠️ Runs after <see cref="NetObjectGrabBridge"/> (default order) and before
    /// <c>HandGripPoser</c> (order 100): the anchor must already be at the hand this frame, or the cargo
    /// trails the spatula by one frame.</remarks>
    [RequireComponent(typeof(NetObject))]
    [RequireComponent(typeof(NetObjectGrabBridge))]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(50)]
    public sealed class BurgerCarrier : MonoBehaviour
    {
        [Tooltip("Malzemenin bineceği hacim (tetik collider). Spatulanın bıçağı, tahtanın yığın hacmi.")]
        [SerializeField] private Collider cargoVolume;

        [Tooltip("Yükün oturduğu nokta — 0. katman burada durur. Boşsa hacmin kendi transform'u.")]
        [SerializeField] private Transform anchor;

        [Tooltip("Aynı anda taşınabilecek malzeme sayısı. Spatula 1, servis tahtası bir hamburger kadar.")]
        [SerializeField] private int capacity = 1;

        [Tooltip("Katmanlar arası yükseklik (metre): yük ankorun yukarısına bu aralıkla dizilir.")]
        [SerializeField] private float slotHeight = 0.03f;

        [Tooltip("Aday malzemenin merkezi yığının tepesinden en fazla bu kadar (m) uzakta olmalı; " +
                 "0 = hacmin tamamı.")]
        [SerializeField] private float claimBand;

        [Tooltip("Taşıyıcı bu açıdan (derece) fazla yatınca yük dökülür. 0 = hiç dökülmez.")]
        [SerializeField] private float spillAngle = 55f;

        [Tooltip("Döküldükten sonra yeniden almaya başlamadan önce beklenen süre (saniye).")]
        [SerializeField] private float reclaimDelaySeconds = 0.6f;

        /// <summary>How long an unanswered <c>object_grab</c> is waited for. A refusal has no message of
        /// its own (§10.10) — the only sign is that the owner never becomes us.</summary>
        private const float AskSeconds = 0.5f;

        private const string HapticSource = "carry";
        private const float HapticSeconds = 0.1f;
        private const float HapticAmplitude = 0.5f;

        private NetObject _net;

        /// <summary>Cargo in stacking order. Rebuilt from state while the carrier is held, and kept for
        /// one more pass after it leaves the hand so the load can be let go of.</summary>
        private readonly List<NetObject> _cargo = new List<NetObject>();

        /// <summary>Bridges whose <see cref="NetObjectGrabBridge.CarryAnchor"/> WE wrote — kept so it can
        /// be handed back when the ingredient stops riding.</summary>
        private readonly List<NetObjectGrabBridge> _anchored = new List<NetObjectGrabBridge>();

        /// <summary>Grab sent, no answer yet: netId → the moment we give up on it.</summary>
        private readonly Dictionary<int, float> _asked = new Dictionary<int, float>();

        /// <summary>Released by us but still flagged held — the state has not come back yet. Re-seating
        /// one of these would fight the physics we just handed it.</summary>
        private readonly HashSet<int> _dropped = new HashSet<int>();

        /// <summary>Scratch for dictionary/list pruning: mutating one while walking it throws, and a
        /// per-frame lambda would allocate on every headset frame.</summary>
        private readonly List<int> _expired = new List<int>();

        private static readonly Collider[] Overlap = new Collider[64];

        private Vector3 _lastAnchorPosition;
        private Vector3 _velocity;
        private float _reclaimAt;

        /// <summary>End of the pickup buzz; the arbiter wants a report every frame (heartbeat), so the
        /// clock lives here rather than in a coroutine.</summary>
        private float _hapticUntil;

        private bool _hapticRight;

        private void Awake()
        {
            _net = GetComponent<NetObject>();

            if (cargoVolume == null)
            {
                Debug.LogError($"[BurgerCarrier] '{name}' için taşıma hacmi atanmamış — üstüne hiçbir " +
                               "malzeme binmez.", this);
            }

            if (anchor == null)
            {
                anchor = cargoVolume != null ? cargoVolume.transform : transform;
            }

            _lastAnchorPosition = anchor.position;
        }

        private void OnDisable()
        {
            // ⚠️ A leftover anchor would keep the grab bridge silent forever: the ingredient would hang
            // wherever this carrier last left it, with nothing writing its pose.
            for (int i = 0; i < _anchored.Count; i++)
            {
                if (_anchored[i] != null)
                {
                    _anchored[i].CarryAnchor = null;
                }
            }

            _anchored.Clear();
            _cargo.Clear();
            _asked.Clear();
            _dropped.Clear();

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

            if (_net == null || _net.NetId <= 0 || cargoVolume == null)
            {
                return;
            }

            TrackVelocity();

            if (_net.IsHeld)
            {
                Rebuild();

                if (_net.IsMine)
                {
                    if (Tipped)
                    {
                        Spill();
                    }
                    else
                    {
                        Claim();
                    }
                }
            }
            else if (_cargo.Count > 0)
            {
                // The carrier left the hand and the load goes with it. ⚠️ Only the owner may say so —
                // everyone else keeps seating the cargo until the owner's release lands, or the burger
                // would jump to that player's palm for a round trip.
                if (_net.IsMine)
                {
                    Spill();
                }
                else
                {
                    _cargo.RemoveAll(StoppedBeingHeld);
                }
            }

            WriteAnchors();
            Seat();
        }

        // ------------------------------------------------------------------- derivation

        /// <summary>Cargo is whatever the carrier's owner holds in the carrier's hand — the one fact the
        /// wire already carries.</summary>
        private void Rebuild()
        {
            _cargo.Clear();

            foreach (NetObject candidate in NetObjectRegistry.All)
            {
                if (Rides(candidate))
                {
                    _cargo.Add(candidate);
                }
            }

            _cargo.Sort(CompareByRestHeight);
        }

        private bool Rides(NetObject candidate)
        {
            return candidate != null && candidate != _net && candidate.NetId > 0 &&
                   candidate.Kind != null && BurgerKinds.IsIngredient(candidate.Kind.Kind) &&
                   candidate.IsHeld && candidate.Owner == _net.Owner &&
                   candidate.HeldByRightHand == _net.HeldByRightHand &&
                   !_dropped.Contains(candidate.NetId);
        }

        private static bool StoppedBeingHeld(NetObject ingredient) =>
            ingredient == null || !ingredient.IsHeld;

        /// <summary>Bottom to top by the pose the server last published for each ingredient. ⚠️ The LIVE
        /// height cannot be used — seated cargo already sits where this carrier put it, so the comparison
        /// would only reproduce the previous frame's order and never correct it.</summary>
        private static int CompareByRestHeight(NetObject a, NetObject b)
        {
            float ay = ArenaSpace.ArenaToWorld(a.RestPosition).y;
            float by = ArenaSpace.ArenaToWorld(b.RestPosition).y;
            return ay.CompareTo(by);
        }

        // ------------------------------------------------------------------- claiming

        /// <summary>Free ingredients inside the volume are asked for. ⚠️ The overlap is an AABB
        /// (<c>bounds</c>), which only matches the volume while the carrier is roughly level — and it is,
        /// because a tipped carrier spills instead of claiming.</summary>
        private void Claim()
        {
            PruneAsked();

            if (Time.time < _reclaimAt || _cargo.Count + _asked.Count >= capacity)
            {
                return;
            }

            Bounds bounds = cargoVolume.bounds;
            int count = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents, Overlap,
                Quaternion.identity, ~0, QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                NetObject ingredient = Overlap[i] != null
                    ? Overlap[i].GetComponentInParent<NetObject>()
                    : null;

                if (!IsClaimable(ingredient))
                {
                    continue;
                }

                // Optimistic, like every other grab (§10.10): the anchor is written BEFORE the answer, or
                // the grab bridge would seat the ingredient in the palm for a frame and fight for the
                // hand the carrier is already in. A refusal leaves it free and PruneAsked takes the
                // anchor back.
                Anchor(ingredient);
                _asked[ingredient.NetId] = Time.time + AskSeconds;
                NetObjectSync.SendGrab(ingredient.NetId, _net.HeldByRightHand);
                Buzz();

                if (_cargo.Count + _asked.Count >= capacity)
                {
                    return;
                }
            }
        }

        private bool IsClaimable(NetObject ingredient)
        {
            // ⚠️ An AWAKE ingredient is mid-flight: scooping one out of the air is not a gesture the
            // player made, it is the carrier happening to be under it.
            return ingredient != null && ingredient != _net && ingredient.NetId > 0 &&
                   ingredient.Kind != null && BurgerKinds.IsIngredient(ingredient.Kind.Kind) &&
                   ingredient.Owner == 0 && !ingredient.IsHeld && !ingredient.IsAwake &&
                   !_asked.ContainsKey(ingredient.NetId) && !_dropped.Contains(ingredient.NetId) &&
                   InClaimBand(ingredient);
        }

        /// <summary>Contact band around the TOP of the stack. ⚠️ Without it the whole volume claims: the
        /// serving board is 32 cm tall, so passing an empty board over the grill sweeps up every patty
        /// under it.</summary>
        private bool InClaimBand(NetObject ingredient)
        {
            if (claimBand <= 0f)
            {
                return true;
            }

            float top = anchor.position.y + _cargo.Count * slotHeight;
            return Mathf.Abs(ingredient.transform.position.y - top) <= claimBand;
        }

        private void PruneAsked()
        {
            if (_asked.Count == 0)
            {
                return;
            }

            float now = Time.time;
            _expired.Clear();

            foreach (KeyValuePair<int, float> entry in _asked)
            {
                bool answered = NetObjectRegistry.TryGet(entry.Key, out NetObject ingredient) &&
                                _cargo.Contains(ingredient);

                if (answered || now >= entry.Value)
                {
                    _expired.Add(entry.Key);
                }
            }

            for (int i = 0; i < _expired.Count; i++)
            {
                int netId = _expired[i];
                _asked.Remove(netId);

                // Unanswered past the deadline = refused, somebody got there first. The optimistic anchor
                // comes back off; an answered one is cargo now and keeps it.
                if (NetObjectRegistry.TryGet(netId, out NetObject ingredient) &&
                    !_cargo.Contains(ingredient))
                {
                    Unanchor(ingredient);
                }
            }
        }

        // ------------------------------------------------------------------- spilling

        /// <summary>Cargo leaves the carrier with a plain <c>object_release</c>: from there the ordinary
        /// flight window takes over (physics, pose stream, <c>object_rest</c>), so the grill and the
        /// serving board hear about the landing through the hooks they already have.</summary>
        private void Spill()
        {
            if (_cargo.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _cargo.Count; i++)
            {
                NetObject ingredient = _cargo[i];
                if (ingredient == null)
                {
                    continue;
                }

                var body = ingredient.GetComponent<Rigidbody>();
                if (body != null)
                {
                    // Set here rather than left to NetObjectBody: the "held" flag has not fallen yet, so
                    // the fall would start a frame late and the ingredient would hang in the air.
                    body.isKinematic = false;
                    body.linearVelocity = _velocity;
                }

                ingredient.transform.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);
                NetObjectSync.SendRelease(ingredient.NetId,
                    ArenaSpace.WorldToArena(position), ArenaSpace.WorldToArena(rotation));

                _dropped.Add(ingredient.NetId);
                Unanchor(ingredient);
            }

            _cargo.Clear();

            // ⚠️ Without this the load is taken straight back: what was tipped off is still free and still
            // inside the volume, so levelling the blade again would scoop it out of mid-air.
            _reclaimAt = Time.time + reclaimDelaySeconds;
        }

        private bool Tipped => spillAngle > 0f && Vector3.Angle(anchor.up, Vector3.up) > spillAngle;

        // ------------------------------------------------------------------- feel

        /// <summary>Short buzz in the hand that scooped: cargo rides the carrier silently and without
        /// this the pickup has no feedback at all.</summary>
        private void Buzz()
        {
            bool right = _net.HeldByRightHand;

            // A hand switch would leave the old side buzzing until the arbiter's timeout.
            if (_hapticUntil > 0f && right != _hapticRight)
            {
                ControllerHaptics.ReportHand(HapticSource, _hapticRight, 0f);
            }

            _hapticRight = right;
            _hapticUntil = Time.unscaledTime + HapticSeconds;
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

            ControllerHaptics.ReportHand(HapticSource, _hapticRight, HapticAmplitude);
        }

        /// <summary>Carrier speed, so spilled cargo keeps the motion of the gesture that tipped it
        /// (a flick off the blade throws, a slow tilt lets go).</summary>
        private void TrackVelocity()
        {
            Vector3 position = anchor.position;
            float dt = Time.deltaTime;

            _velocity = dt > 0f ? (position - _lastAnchorPosition) / dt : Vector3.zero;
            _lastAnchorPosition = position;
        }

        // ------------------------------------------------------------------- placement

        /// <summary>Keeps the grab bridge's hands off exactly the ingredients we place, and gives the
        /// transform back the moment one stops riding.</summary>
        private void WriteAnchors()
        {
            for (int i = 0; i < _cargo.Count; i++)
            {
                Anchor(_cargo[i]);
            }

            for (int i = _anchored.Count - 1; i >= 0; i--)
            {
                NetObjectGrabBridge bridge = _anchored[i];
                if (bridge == null)
                {
                    _anchored.RemoveAt(i);
                    continue;
                }

                var ingredient = bridge.GetComponent<NetObject>();
                if (ingredient != null &&
                    (_cargo.Contains(ingredient) || _asked.ContainsKey(ingredient.NetId)))
                {
                    continue;
                }

                bridge.CarryAnchor = null;
                _anchored.RemoveAt(i);
            }

            PruneDropped();
        }

        /// <summary>A drop is settled once the server agrees the ingredient left the hand; only then may
        /// it be scooped up again.</summary>
        private void PruneDropped()
        {
            if (_dropped.Count == 0)
            {
                return;
            }

            _expired.Clear();
            foreach (int netId in _dropped)
            {
                if (!NetObjectRegistry.TryGet(netId, out NetObject ingredient) || !ingredient.IsHeld)
                {
                    _expired.Add(netId);
                }
            }

            for (int i = 0; i < _expired.Count; i++)
            {
                _dropped.Remove(_expired[i]);
            }
        }

        private void Anchor(NetObject ingredient)
        {
            if (ingredient == null)
            {
                return;
            }

            var bridge = ingredient.GetComponent<NetObjectGrabBridge>();
            if (bridge == null)
            {
                return;
            }

            bridge.CarryAnchor = anchor;

            if (!_anchored.Contains(bridge))
            {
                _anchored.Add(bridge);
            }
        }

        private void Unanchor(NetObject ingredient)
        {
            if (ingredient == null)
            {
                return;
            }

            var bridge = ingredient.GetComponent<NetObjectGrabBridge>();
            if (bridge == null)
            {
                return;
            }

            bridge.CarryAnchor = null;
            _anchored.Remove(bridge);
        }

        /// <summary>Cargo is re-stacked at a fixed spacing above the anchor rather than kept at the
        /// offsets it had: those offsets are physics results, and no two headsets measured the same
        /// ones.</summary>
        private void Seat()
        {
            if (_cargo.Count == 0)
            {
                return;
            }

            anchor.GetPositionAndRotation(out Vector3 origin, out Quaternion rotation);
            Vector3 up = anchor.up;

            for (int i = 0; i < _cargo.Count; i++)
            {
                if (_cargo[i] != null)
                {
                    _cargo[i].transform.SetPositionAndRotation(origin + up * (i * slotHeight), rotation);
                }
            }
        }
    }
}
