using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Modes.Burger
{
    /// <summary>One counter slot: the place a customer waits at and the volume the serving board must be
    /// put down in. Plain scene component — the slot NUMBER is what travels (in the customer's payload,
    /// §10.5), never its position.</summary>
    [DisallowMultipleComponent]
    public sealed class BurgerCounterSlot : MonoBehaviour
    {
        [Tooltip("Slot numarası (0..2). Müşterinin s alanındaki 'slot' değeriyle birebir eşleşir.")]
        [SerializeField] private int slotIndex;

        [Tooltip("Müşterinin bankoda duracağı yer. Boşsa bu objenin kendi transform'u kullanılır.")]
        [SerializeField] private Transform customerAnchor;

        [Tooltip("Slotun hacmi (tahtanın bırakıldığı yer). Boşsa bu objedeki collider aranır.")]
        [SerializeField] private Collider volume;

        private static readonly List<BurgerCounterSlot> Slots = new List<BurgerCounterSlot>();

        /// <summary>All enabled slots in the scene.</summary>
        public static IReadOnlyList<BurgerCounterSlot> All => Slots;

        public int SlotIndex => slotIndex;

        public Transform CustomerAnchor => customerAnchor != null ? customerAnchor : transform;

        private void Awake()
        {
            if (volume == null)
            {
                volume = GetComponent<Collider>();
            }
        }

        private void OnEnable()
        {
            if (!Slots.Contains(this))
            {
                Slots.Add(this);
            }
        }

        private void OnDisable()
        {
            Slots.Remove(this);
        }

        public static BurgerCounterSlot Find(int slotIndex)
        {
            for (int i = 0; i < Slots.Count; i++)
            {
                if (Slots[i] != null && Slots[i].slotIndex == slotIndex)
                {
                    return Slots[i];
                }
            }

            return null;
        }

        /// <summary>Is that world point inside this slot's volume? False without a collider — a slot with
        /// no volume must not swallow every board.</summary>
        public bool Contains(Vector3 worldPoint)
        {
            return volume != null && volume.bounds.Contains(worldPoint);
        }
    }
}
