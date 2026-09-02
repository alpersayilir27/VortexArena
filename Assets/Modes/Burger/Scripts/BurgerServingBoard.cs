using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core.Player;
using VortexArena.Core.World;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Modes.Burger
{
    /// <summary>The serving board: while it sits in a counter slot it reports the stack to the waiting
    /// customer with <c>serve</c> (<c>i:[müşteri netId, malzeme netId'leri alttan üste]</c>, §10.5).
    /// <para><b>Two moments trigger it, and the burger must be CLOSED for either:</b> placing the TOP BUN
    /// on a board that is already parked, or putting a finished board down into a slot. The first is what
    /// makes the game playable — a parked board never rests again, so without it a burger built where the
    /// customer stands could never be handed over.</para>
    /// <para>⚠️ Only the client that brought the object to rest reports
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

        [Tooltip("İki servis denemesi arasındaki en kısa süre (saniye).")]
        [SerializeField] private float serveCooldownSeconds = 1f;

        [Tooltip("Red sebebinin müşteri balonunda kalma süresi (saniye).")]
        [SerializeField] private float noticeSeconds = 2.5f;

        /// <summary>How long a serve WE sent is watched for an acceptance. Acceptance has no message of
        /// its own — the only sign is the customer turning happy (§10.5).</summary>
        private const float AcceptWindowSeconds = 2f;

        private NetObject _net;
        private NetObjectPoseSender _sender;

        /// <summary>Customer of the serve this headset sent, so only the player who handed the burger
        /// over feels the confirmation.</summary>
        private int _servedCustomer;

        private float _servedUntil;

        /// <summary>⚠️ Both triggers can fire for ONE burger: a loaded board carried into a slot rests,
        /// and the top bun riding it rests right after. The second <c>serve</c> names ingredients the
        /// server has already despawned, and a rejection is relayed as the event itself (§10.5) — the
        /// player would hear the reject sound on a burger that was accepted.</summary>
        private float _serveCooldown;

        private readonly List<NetObject> _stack = new List<NetObject>();
        private readonly List<int> _payload = new List<int>();

        /// <summary>Scratch for the rejection diagnosis — reused so a refused serve allocates nothing.</summary>
        private readonly List<NetObject> _served = new List<NetObject>();

        private readonly Dictionary<string, int> _wanted = new Dictionary<string, int>();

        /// <summary>Ingredients currently over the board, with the sender we subscribed to.</summary>
        private readonly Dictionary<NetObject, NetObjectPoseSender> _watched =
            new Dictionary<NetObject, NetObjectPoseSender>();

        private static readonly Collider[] Overlap = new Collider[64];

        private void Awake()
        {
            _net = GetComponent<NetObject>();
            _sender = GetComponent<NetObjectPoseSender>();

            if (stackTrigger == null)
            {
                Debug.LogError($"[BurgerServingBoard] '{name}' için yığın hacmi atanmamış — tahtaya " +
                               "konulan malzemeler servise girmez.", this);
            }
        }

        private void OnEnable()
        {
            if (_sender != null)
            {
                _sender.RestSent += HandleRestSent;
            }

            _net.EventReceived += HandleEventReceived;
        }

        private void OnDisable()
        {
            if (_sender != null)
            {
                _sender.RestSent -= HandleRestSent;
            }

            _net.EventReceived -= HandleEventReceived;

            foreach (KeyValuePair<NetObject, NetObjectPoseSender> entry in _watched)
            {
                if (entry.Value != null)
                {
                    entry.Value.RestSent -= HandleIngredientRest;
                }
            }

            _watched.Clear();
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

            if (sender != null)
            {
                sender.RestSent -= HandleIngredientRest;
            }
        }

        /// <summary>An ingredient WE put down settled on the board. The TOP BUN is the serve gesture —
        /// placing it is what closes the burger.</summary>
        /// <remarks>⚠️ Without this the loop cannot be closed at all: the board's own rest is the only
        /// other trigger, and a board already sitting in its slot never rests again. Stacking onto a
        /// parked board would then never reach the customer. Gating on the top bun also keeps a wrong
        /// order from being re-rejected on every single ingredient.</remarks>
        private void HandleIngredientRest(NetObject ingredient)
        {
            if (ingredient == null || ingredient.Kind == null ||
                ingredient.Kind.Kind != BurgerKinds.BunTop)
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

        /// <summary>The board itself came to rest — the "assemble elsewhere, carry it over" flow.</summary>
        private void HandleRestSent(NetObject net) => TryServe();

        private void Update()
        {
            if (_serveCooldown > 0f)
            {
                _serveCooldown -= Time.deltaTime;
            }

            TickAccepted();
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
            if (stackTrigger == null || _net == null || _net.NetId <= 0 || _serveCooldown > 0f)
            {
                return;
            }

            BurgerCounterSlot slot = ResolveSlot();
            if (slot == null)
            {
                return;
            }

            BurgerCustomer customer = FindWaitingCustomer(slot.SlotIndex);
            if (customer == null)
            {
                return;
            }

            CollectStack();

            // A burger is served CLOSED: the top bun must be the highest thing on the board. Without
            // this gate every half-built stack would be reported and rejected.
            if (_stack.Count < 2 ||
                _stack[_stack.Count - 1].Kind.Kind != BurgerKinds.BunTop)
            {
                return;
            }

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

        private static BurgerCustomer FindWaitingCustomer(int slotIndex)
        {
            BurgerCustomer[] customers =
                FindObjectsByType<BurgerCustomer>(FindObjectsSortMode.None);

            for (int i = 0; i < customers.Length; i++)
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
