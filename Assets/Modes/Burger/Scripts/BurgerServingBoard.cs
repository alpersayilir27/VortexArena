using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core.Player;
using VortexArena.Core.World;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Modes.Burger
{
    /// <summary>The serving board: a FIXED workstation standing in a counter slot, reporting its stack to
    /// the waiting customer with <c>serve</c> (<c>i:[müşteri netId, malzeme netId'leri alttan üste]</c>,
    /// §10.5).
    /// <para>⚠️ <b>The board is never picked up</b> — kind <c>board</c> is <c>grab: none</c> and its
    /// definition's grab path is <c>None</c>. Nobody can own it, so it never leaves kinematic and stays
    /// where the scene put it. That position is the ONLY thing tying it to a slot, because
    /// <see cref="ResolveSlot"/> searches by VOLUME: a board authored outside every slot's collider takes
    /// ingredients happily and then never serves — every blocked serve says why in the log.</para>
    /// <para><b>Placing the TOP BUN is the serve gesture</b> — it closes the burger, and the stack must be
    /// closed for the report to go out.</para>
    /// <para>⚠️ Only the client that brought the ingredient to rest reports
    /// (<see cref="NetObjectPoseSender.RestSent"/>) — one serve per gesture, from one headset.</para>
    /// <para>⚠️ <b>A rejection has no message of its own:</b> a correct serve produces
    /// <c>object_state</c>, while a wrong recipe is relayed back as the <c>serve</c> EVENT itself
    /// (§10.5). So an incoming <c>serve</c> on this object IS the rejection; there is no
    /// <c>rejected</c> event to listen for.</para></summary>
    [RequireComponent(typeof(NetObject))]
    [DisallowMultipleComponent]
    public sealed class BurgerServingBoard : MonoBehaviour
    {
        [Tooltip("Tahtanın üstündeki malzemeleri toplayan hacim (tetik collider).")]
        [SerializeField] private Collider stackTrigger;

        [Tooltip("Yanlış servis sesi. Atanmazsa yalnız log yazılır.")]
        [SerializeField] private AudioSource rejectSound;

        [Tooltip("Yığına yeni katman oturunca çalan kısa ses. Atanmazsa yalnız görsel/haptik geri " +
                 "bildirim verilir.")]
        [SerializeField] private AudioSource stackSound;

        [Tooltip("İki servis denemesi arasındaki en kısa süre (saniye).")]
        [SerializeField] private float serveCooldownSeconds = 1f;

        [Tooltip("Red sebebinin müşteri balonunda kalma süresi (saniye).")]
        [SerializeField] private float noticeSeconds = 2.5f;

        /// <summary>How long a serve WE sent is watched for an acceptance. Acceptance has no message of
        /// its own — the only sign is the customer turning happy (§10.5).</summary>
        private const float AcceptWindowSeconds = 2f;

        /// <summary>Shortest gap between two logs of the SAME gate.</summary>
        private const float GateLogSeconds = 2f;

        private const float RetryIntervalSeconds = 0.25f;

        // ---------------------------------------------------------------- seat feedback (one place)

        /// <summary>How often the stack is re-read for a new layer (s). Polled rather than driven by the
        /// trigger: a settling ingredient jitters in and out of the volume, and every enter would be a
        /// "pop" on an ingredient that was already there.</summary>
        private const float SeatPollSeconds = 0.1f;

        /// <summary>Peak scale of the seat pop (visual only, see <see cref="Pop"/>) and how long it
        /// lasts (s).</summary>
        private const float SeatPopScale = 1.12f;

        private const float SeatPopSeconds = 0.12f;

        /// <summary>The silent early returns of <see cref="TryServe"/>, named so the log is rate limited
        /// per reason.</summary>
        private enum ServeGate
        {
            None = 0,
            Board,
            Cooldown,
            Slot,
            Stack,
            Customer
        }

        private NetObject _net;

        /// <summary>Customer of the serve this headset sent, so only the player who handed the burger
        /// over feels the confirmation.</summary>
        private int _servedCustomer;

        private float _servedUntil;

        /// <summary>⚠️ One burger can still report twice: a top bun that settles, gets nudged and settles
        /// again sends a second <c>serve</c> naming ingredients the server has already despawned, and a
        /// rejection is relayed as the event itself (§10.5) — the player would hear the reject sound on a
        /// burger that was accepted.</summary>
        private float _serveCooldown;

        /// <summary>Which gate blocked the last serve — the rate limiter's key, so a changing reason is
        /// always logged and a stuck one is not.</summary>
        private ServeGate _lastGate;

        private float _lastGateLogTime = float.NegativeInfinity;

        /// <summary>A closed stack waiting only for a customer; see <see cref="TickPendingServe"/>.</summary>
        private bool _pendingServe;

        private float _nextRetryTime;

        private readonly List<NetObject> _stack = new List<NetObject>();
        private readonly List<int> _payload = new List<int>();

        /// <summary>Scratch for the rejection diagnosis — reused so a refused serve allocates nothing.</summary>
        private readonly List<NetObject> _served = new List<NetObject>();

        private readonly Dictionary<string, int> _wanted = new Dictionary<string, int>();

        /// <summary>Ingredients currently over the board, with the sender we subscribed to.</summary>
        private readonly Dictionary<NetObject, NetObjectPoseSender> _watched =
            new Dictionary<NetObject, NetObjectPoseSender>();

        /// <summary>Layers already seated, so the feedback fires ONCE per layer.</summary>
        private readonly HashSet<int> _seated = new HashSet<int>();

        /// <summary>Ingredients THIS headset brought to rest — the haptic belongs to the hand that
        /// placed the layer, while the sound and the pop belong to everyone watching.</summary>
        private readonly HashSet<int> _localRested = new HashSet<int>();

        /// <summary>Pops in flight, with the scale to put back. Kept here rather than in the coroutine:
        /// a disabled board stops its coroutines WITHOUT unwinding them, and the ingredient would stay
        /// stretched forever.</summary>
        private readonly Dictionary<Transform, Vector3> _popping = new Dictionary<Transform, Vector3>();

        private readonly List<int> _stale = new List<int>();

        private float _nextSeatPoll;

        /// <summary>Has the first stack poll run — see <see cref="TickSeatFeedback"/>.</summary>
        private bool _seatPrimed;

        private static readonly Collider[] Overlap = new Collider[64];

        private void Awake()
        {
            _net = GetComponent<NetObject>();

            if (stackTrigger == null)
            {
                Debug.LogError($"[BurgerServingBoard] '{name}' için yığın hacmi atanmamış — tahtaya " +
                               "konulan malzemeler servise girmez.", this);
            }
        }

        private void OnEnable()
        {
            _net.EventReceived += HandleEventReceived;
        }

        private void OnDisable()
        {
            _net.EventReceived -= HandleEventReceived;

            foreach (KeyValuePair<NetObject, NetObjectPoseSender> entry in _watched)
            {
                if (entry.Value != null)
                {
                    entry.Value.RestSent -= HandleIngredientRest;
                }
            }

            _watched.Clear();
            _seated.Clear();
            _localRested.Clear();
            RestorePops();
            _seatPrimed = false;
            _pendingServe = false;
        }

        // ------------------------------------------------------------------- the closing gesture

        /// <summary>Ingredients ABOVE the board are watched so the burger can be served where it is
        /// built.</summary>
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
                sender.RestSent += HandleIngredientRest;
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
            _localRested.Remove(ingredient.NetId);

            if (sender != null)
            {
                sender.RestSent -= HandleIngredientRest;
            }
        }

        /// <summary>An ingredient WE put down settled on the board. The TOP BUN is the serve gesture —
        /// placing it is what closes the burger.</summary>
        /// <remarks>⚠️ This is the ONLY trigger: the board never moves, so it never comes to rest and has
        /// no rest event of its own to serve from. Gating on the top bun also keeps a wrong order from
        /// being re-rejected on every single ingredient.</remarks>
        private void HandleIngredientRest(NetObject ingredient)
        {
            if (ingredient == null || ingredient.Kind == null)
            {
                return;
            }

            // Every rest is noted, not just the bun's: it marks the layer as OURS so only the hand that
            // placed it feels the seat buzz.
            _localRested.Add(ingredient.NetId);

            if (ingredient.Kind.Kind != BurgerKinds.BunTop)
            {
                return;
            }

            TryServe();
        }

        private static NetObject ResolveIngredient(Collider other)
        {
            NetObject net = other != null ? other.GetComponentInParent<NetObject>() : null;
            return net != null && net.NetId > 0 && net.Kind != null &&
                   BurgerKinds.IsIngredient(net.Kind.Kind)
                ? net
                : null;
        }

        // ------------------------------------------------------------------- serving

        private void Update()
        {
            if (_serveCooldown > 0f)
            {
                _serveCooldown -= Time.deltaTime;
            }

            TickAccepted();
            TickPendingServe();
            TickSeatFeedback();
        }

        // ------------------------------------------------------------------- seat feedback

        /// <summary>Short sound + scale pop (+ a buzz for the placing hand) the moment a layer settles on
        /// the stack.</summary>
        /// <remarks>⚠️ A layer counts as seated only once it is free AND asleep: an ingredient still in a
        /// hand hovering over the board, or one bouncing after the drop, is inside the volume too and
        /// would pop several times per placement.</remarks>
        private void TickSeatFeedback()
        {
            if (stackTrigger == null || Time.time < _nextSeatPoll)
            {
                return;
            }

            _nextSeatPoll = Time.time + SeatPollSeconds;
            CollectStack();

            // First pass only records: a headset joining mid-shift finds finished burgers on the boards
            // and would announce every layer of them as freshly placed.
            bool silent = !_seatPrimed;
            _seatPrimed = true;

            bool added = false;
            bool mine = false;

            for (int i = 0; i < _stack.Count; i++)
            {
                NetObject layer = _stack[i];
                if (layer.IsHeld || layer.IsAwake || !_seated.Add(layer.NetId))
                {
                    continue;
                }

                added = true;
                mine |= _localRested.Remove(layer.NetId);

                if (!silent)
                {
                    Pop(layer);
                }
            }

            if (added && !silent)
            {
                if (stackSound != null)
                {
                    stackSound.Play();
                }

                if (mine)
                {
                    ControllerHaptics.PulseBoth(this, 1);
                }
            }

            PruneSeated();
        }

        /// <summary>A layer that left the board may be seated again — that IS a new placement.</summary>
        private void PruneSeated()
        {
            if (_seated.Count == 0)
            {
                return;
            }

            _stale.Clear();
            foreach (int netId in _seated)
            {
                if (!ContainsNetId(_stack, netId))
                {
                    _stale.Add(netId);
                }
            }

            for (int i = 0; i < _stale.Count; i++)
            {
                _seated.Remove(_stale[i]);
            }
        }

        private static bool ContainsNetId(List<NetObject> stack, int netId)
        {
            for (int i = 0; i < stack.Count; i++)
            {
                if (stack[i] != null && stack[i].NetId == netId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Pops the layer's collider-free visual children only — scaling the root would grow its
        /// collider and shove the neighbouring layers of the stack.</summary>
        private void Pop(NetObject layer)
        {
            if (layer == null)
            {
                return;
            }

            Transform root = layer.transform;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform visual = root.GetChild(i);
                if (_popping.ContainsKey(visual) ||
                    visual.GetComponent<Renderer>() == null ||
                    visual.GetComponent<Collider>() != null)
                {
                    continue;
                }

                _popping.Add(visual, visual.localScale);
                StartCoroutine(PopRoutine(visual));
            }
        }

        private System.Collections.IEnumerator PopRoutine(Transform visual)
        {
            Vector3 baseScale = _popping[visual];
            float started = Time.unscaledTime;

            while (visual != null)
            {
                float t = (Time.unscaledTime - started) / SeatPopSeconds;
                if (t >= 1f)
                {
                    break;
                }

                // Up and back down within one pop: a half sine, so the end lands exactly on the base.
                visual.localScale = baseScale * (1f + (SeatPopScale - 1f) * Mathf.Sin(t * Mathf.PI));
                yield return null;
            }

            if (visual != null)
            {
                visual.localScale = baseScale;
            }

            _popping.Remove(visual);
        }

        /// <summary>Puts back every scale a running pop still owes.</summary>
        private void RestorePops()
        {
            foreach (KeyValuePair<Transform, Vector3> entry in _popping)
            {
                if (entry.Key != null)
                {
                    entry.Key.localScale = entry.Value;
                }
            }

            _popping.Clear();
        }

        /// <summary>The confirmation buzz for OUR serve. Watched instead of listened for: acceptance
        /// produces <c>object_state</c> on the customer, not an event on this board.</summary>
        private void TickAccepted()
        {
            if (_servedCustomer == 0)
            {
                return;
            }

            if (Time.time >= _servedUntil)
            {
                _servedCustomer = 0;
                return;
            }

            BurgerCustomer customer = BurgerCustomer.Find(_servedCustomer);
            if (customer == null || customer.Stage != BurgerKinds.CustomerHappy)
            {
                return;
            }

            _servedCustomer = 0;
            ControllerHaptics.PulseBoth(this, 2);
        }

        private void TryServe()
        {
            if (stackTrigger == null || _net == null || _net.NetId <= 0)
            {
                ReportGate(ServeGate.Board, "tahta ağa bağlı değil ya da yığın hacmi atanmamış");
                return;
            }

            if (_serveCooldown > 0f)
            {
                ReportGate(ServeGate.Cooldown, "önceki servisin bekleme süresi dolmadı");
                return;
            }

            BurgerCounterSlot slot = ResolveSlot();
            if (slot == null)
            {
                _pendingServe = false;
                ReportGate(ServeGate.Slot, "tahta hiçbir tezgah yuvasının hacmi içinde değil");
                return;
            }

            CollectStack();

            // A burger is served CLOSED: the top bun must be the highest thing on the board. Without
            // this gate every half-built stack would be reported and rejected.
            if (_stack.Count < 2 ||
                _stack[_stack.Count - 1].Kind.Kind != BurgerKinds.BunTop)
            {
                _pendingServe = false;
                ReportGate(ServeGate.Stack, "yığın kapalı değil — en üstte üst ekmek yok");
                return;
            }

            // The customer is checked LAST so a ready stack can be retried: the gate below is the only
            // one that clears by itself (a customer walks in).
            BurgerCustomer customer = FindWaitingCustomer(slot.SlotIndex);
            if (customer == null)
            {
                _pendingServe = true;
                ReportGate(ServeGate.Customer, $"{slot.SlotIndex} numaralı yuvada bekleyen müşteri yok");
                return;
            }

            _pendingServe = false;
            _lastGate = ServeGate.None;

            _payload.Clear();
            _payload.Add(customer.NetId);
            for (int i = 0; i < _stack.Count; i++)
            {
                _payload.Add(_stack[i].NetId);
            }

            NetObjectSync.SendEvent(_net.NetId, BurgerKinds.EventServe, _payload.ToArray());
            _serveCooldown = serveCooldownSeconds;

            _servedCustomer = customer.NetId;
            _servedUntil = Time.time + AcceptWindowSeconds;
        }

        /// <summary>One warning per blocked serve — a silent early return looks like "servis gözlükten hiç
        /// çıkmıyor" in the field. Rate limited BY GATE: a board with a closed burger retries forever.</summary>
        private void ReportGate(ServeGate gate, string reason)
        {
            // A pending retry repeats its gate every tick — log it once, not every two seconds.
            if (gate == _lastGate && (_pendingServe || Time.time < _lastGateLogTime + GateLogSeconds))
            {
                return;
            }

            _lastGate = gate;
            _lastGateLogTime = Time.time;
            Debug.LogWarning($"[Hamburgerci] Servis gönderilmedi — {reason}.", this);
        }

        /// <summary>Re-sends a serve whose stack was ready while the slot had no waiting customer yet: the
        /// customer walks in AFTER the burger is closed, and the closing gesture (the top bun coming to
        /// rest) never happens a second time.</summary>
        /// <remarks>Polled because a customer has no "started waiting" event — its stage is plain
        /// <c>object_state</c>. ⚠️ Only the REQUEST is repeated; the outcome stays the server's (§10.5).</remarks>
        private void TickPendingServe()
        {
            if (!_pendingServe || _serveCooldown > 0f || Time.time < _nextRetryTime)
            {
                return;
            }

            _nextRetryTime = Time.time + RetryIntervalSeconds;
            TryServe();
        }

        /// <summary>Which slot the board was put down in. Searched by VOLUME rather than by number: the
        /// board does not know a slot index, its position is the only fact it has.</summary>
        private BurgerCounterSlot ResolveSlot()
        {
            IReadOnlyList<BurgerCounterSlot> slots = BurgerCounterSlot.All;
            Vector3 position = transform.position;

            for (int i = 0; i < slots.Count; i++)
            {
                BurgerCounterSlot slot = slots[i];
                if (slot != null && slot.Contains(position))
                {
                    return slot;
                }
            }

            return null;
        }

        /// <summary>The customer waiting at this slot. Read from the registry rather than
        /// <c>FindObjectsByType</c>: the pending-serve retry asks repeatedly and a scene scan per retry
        /// allocates.</summary>
        private static BurgerCustomer FindWaitingCustomer(int slotIndex)
        {
            IReadOnlyList<BurgerCustomer> customers = BurgerCustomer.All;

            for (int i = 0; i < customers.Count; i++)
            {
                BurgerCustomer customer = customers[i];
                if (customer != null && customer.Slot == slotIndex &&
                    customer.Stage == BurgerKinds.CustomerWaiting && customer.NetId > 0)
                {
                    return customer;
                }
            }

            return null;
        }

        /// <summary>Ingredients inside the stack volume, sorted by world Y — bottom to top is the recipe
        /// order the server compares against (§10.5).</summary>
        private void CollectStack()
        {
            _stack.Clear();

            Bounds bounds = stackTrigger.bounds;
            int count = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents, Overlap,
                Quaternion.identity, ~0, QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                NetObject net = Overlap[i] != null ? Overlap[i].GetComponentInParent<NetObject>() : null;
                if (net == null || net.NetId <= 0 || net.Kind == null ||
                    !BurgerKinds.IsIngredient(net.Kind.Kind) || _stack.Contains(net))
                {
                    continue;
                }

                _stack.Add(net);
            }

            _stack.Sort(CompareByHeight);
        }

        private static int CompareByHeight(NetObject a, NetObject b)
        {
            return a.transform.position.y.CompareTo(b.transform.position.y);
        }

        // ------------------------------------------------------------------- rejection

        private void HandleEventReceived(ObjectEventMsg msg)
        {
            if (msg == null || msg.name != BurgerKinds.EventServe)
            {
                return;
            }

            string reason = Diagnose(msg);
            Debug.Log($"[Hamburgerci] Sipariş tutmadı — {reason}", this);

            if (rejectSound != null)
            {
                rejectSound.Play();
            }

            // Shown to EVERYONE: the event is relayed to the whole room and the bubble belongs to the
            // customer, not to the player who served.
            BurgerCustomer customer = msg.i != null && msg.i.Length > 0
                ? BurgerCustomer.Find(msg.i[0])
                : null;

            if (customer != null)
            {
                customer.ShowNotice(reason, noticeSeconds);
            }
        }

        /// <summary>Why the serve was refused, derived locally.
        /// <para>⚠️ MIRROR of the server's <c>MiddleMatches</c> + patty gate (§10.5): the reason is not on
        /// the wire, so the two rules must say the same thing — drift makes the bubble lie.</para></summary>
        private string Diagnose(ObjectEventMsg msg)
        {
            const string generic = "Sipariş tutmadı";

            if (msg.i == null || msg.i.Length < 3)
            {
                return generic;
            }

            BurgerCustomer customer = BurgerCustomer.Find(msg.i[0]);
            if (customer == null)
            {
                return generic;
            }

            _served.Clear();
            for (int i = 1; i < msg.i.Length; i++)
            {
                if (!NetObjectRegistry.TryGet(msg.i[i], out NetObject ingredient) ||
                    ingredient.Kind == null)
                {
                    return generic;
                }

                _served.Add(ingredient);
            }

            if (_served[0].Kind.Kind != BurgerKinds.BunBottom)
            {
                return "Önce alt ekmek";
            }

            if (_served[_served.Count - 1].Kind.Kind != BurgerKinds.BunTop)
            {
                return "En üste üst ekmek";
            }

            string pattyReason = DiagnosePatties();
            if (pattyReason != null)
            {
                return pattyReason;
            }

            return DiagnoseMiddle(customer.Recipe) ?? generic;
        }

        private string DiagnosePatties()
        {
            bool burnt = false;

            for (int i = 0; i < _served.Count; i++)
            {
                if (_served[i].Kind.Kind != BurgerKinds.Patty)
                {
                    continue;
                }

                if (_served[i].Stage == BurgerKinds.PattyRaw)
                {
                    return "Köfte pişmemiş";
                }

                burnt |= _served[i].Stage == BurgerKinds.PattyBurnt;
            }

            return burnt ? "Köfte yanmış" : null;
        }

        /// <summary>Filling COUNTS, ends excluded — the stacking order is free on the server too.</summary>
        private string DiagnoseMiddle(string recipe)
        {
            _wanted.Clear();

            if (!string.IsNullOrEmpty(recipe))
            {
                string[] parts = recipe.Split(',');
                for (int i = 1; i < parts.Length - 1; i++)
                {
                    string kind = parts[i].Trim();
                    if (kind.Length == 0)
                    {
                        continue;
                    }

                    _wanted.TryGetValue(kind, out int count);
                    _wanted[kind] = count + 1;
                }
            }

            for (int i = 1; i < _served.Count - 1; i++)
            {
                string kind = _served[i].Kind.Kind;
                _wanted.TryGetValue(kind, out int count);

                // Below zero on purpose: the leftover negative IS the extra ingredient.
                _wanted[kind] = count - 1;
            }

            string extra = null;
            foreach (KeyValuePair<string, int> entry in _wanted)
            {
                if (entry.Value > 0)
                {
                    return $"{BurgerKinds.DisplayName(entry.Key)} eksik";
                }

                if (entry.Value < 0 && extra == null)
                {
                    extra = $"{BurgerKinds.DisplayName(entry.Key)} fazla";
                }
            }

            return extra;
        }
    }
}
