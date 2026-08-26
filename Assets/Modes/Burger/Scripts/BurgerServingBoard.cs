using System.Collections.Generic;
using UnityEngine;
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

        private NetObject _net;
        private NetObjectPoseSender _sender;

        private readonly List<NetObject> _stack = new List<NetObject>();
        private readonly List<int> _payload = new List<int>();

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

        private void TryServe()
        {
            if (stackTrigger == null || _net == null || _net.NetId <= 0)
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

            Debug.Log("[Hamburgerci] Sipariş tutmadı — servis reddedildi.", this);

            if (rejectSound != null)
            {
                rejectSound.Play();
            }
        }
    }
}
