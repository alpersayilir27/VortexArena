using UnityEngine;
using VortexArena.Net;

namespace VortexArena.Modes.Burger
{
    /// <summary>The knife: touching a free whole bun with the blade raises <c>cut</c> (§10.5). The server
    /// despawns the whole bun and spawns the two halves — nothing is cut locally.
    /// <para>⚠️ The event's policy is <c>anyone</c> because the SENDER does not own the target: the bun
    /// sits free on the board (<c>owner == 0</c>) while the knife is in a hand. An <c>owner</c> policy
    /// would never let this event through. The mode's gate is instead "the bun must be FREE" — a bun in
    /// someone's hand is not cut.</para></summary>
    [RequireComponent(typeof(NetObject))]
    [DisallowMultipleComponent]
    public sealed class BurgerKnife : MonoBehaviour
    {
        [Tooltip("Kesen ağzın tetik collider'ı. Boşsa çocuklardaki ilk isTrigger collider kullanılır.")]
        [SerializeField] private Collider bladeTrigger;

        [Tooltip("İki kesme arasındaki en kısa süre — tek hamlenin birden çok kesme yollamasını engeller.")]
        [SerializeField] private float cooldownSeconds = 1f;

        private NetObject _net;
        private float _cooldown;

        private void Awake()
        {
            _net = GetComponent<NetObject>();

            if (bladeTrigger == null)
            {
                bladeTrigger = FindTrigger();
            }

            if (bladeTrigger == null)
            {
                Debug.LogError($"[BurgerKnife] '{name}' altında tetik collider yok — bıçak hiçbir şeyi " +
                               "kesemez.", this);
            }
        }

        private Collider FindTrigger()
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i].isTrigger)
                {
                    return colliders[i];
                }
            }

            return null;
        }

        private void Update()
        {
            if (_cooldown > 0f)
            {
                _cooldown -= Time.deltaTime;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            // Only the knife WE are holding cuts: someone else's knife is driven by their own client and
            // two senders would cut the same bun twice.
            if (_cooldown > 0f || _net == null || !_net.IsMine || !_net.IsHeld)
            {
                return;
            }

            NetObject bun = other != null ? other.GetComponentInParent<NetObject>() : null;
            if (bun == null || bun.NetId <= 0 || bun.Kind == null)
            {
                return;
            }

            if (bun.Kind.Kind != BurgerKinds.BunWhole || bun.Owner != 0)
            {
                return;
            }

            NetObjectSync.SendEvent(bun.NetId, BurgerKinds.EventCut);
            _cooldown = cooldownSeconds;
        }
    }
}
