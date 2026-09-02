using System.Collections.Generic;
using UnityEngine;
using VortexArena.Net;

namespace VortexArena.Modes.Burger
{
    /// <summary>The customer: walks in, waits at its counter slot with an order, then leaves happy or
    /// unhappy. Stage and order come from the server (<c>stage</c> + <c>s</c>, §10.5); the WALK is
    /// derived locally from <see cref="BurgerCustomerPath"/>.
    /// <para>⚠️ This prefab carries NO <c>NetObjectBody</c> / <c>NetObjectPoseSender</c> /
    /// <c>NetObjectGrabBridge</c>: the customer's pose never goes on the wire. Adding them would drive
    /// the object from the path AND from the network at once — two writers on one transform, i.e. one
    /// customer in two places.</para></summary>
    [RequireComponent(typeof(NetObject))]
    [DisallowMultipleComponent]
    public sealed class BurgerCustomer : MonoBehaviour
    {
        [Tooltip("Sipariş balonu. Boşsa çocuklarda aranır.")]
        [SerializeField] private BurgerOrderBubble bubble;

        [Tooltip("Mutlu/mutsuz renginin uygulanacağı görsel. Atanmazsa renk değiştirilmez.")]
        [SerializeField] private Renderer moodRenderer;

        [Tooltip("Müşteri bankoya varınca çalan ses. Atanmazsa sessizdir.")]
        [SerializeField] private AudioSource arriveSound;

        [Tooltip("Sipariş kabul edilince çalan ses. Atanmazsa sessizdir.")]
        [SerializeField] private AudioSource happySound;

        [Tooltip("Sabrı bitip giderken çalan ses. Atanmazsa sessizdir.")]
        [SerializeField] private AudioSource unhappySound;

        /// <summary>Path fraction at which the customer starts peeling off toward its own slot.</summary>
        private const float SlotBlendStart = 0.75f;

        private static readonly List<BurgerCustomer> Customers = new List<BurgerCustomer>();

        /// <summary>All enabled customers in the scene.</summary>
        public static IReadOnlyList<BurgerCustomer> All => Customers;

        private NetObject _net;

        private float _walkTimer;
        private float _leaveTimer;

        /// <summary>Local clock of the wait. ⚠️ A late joiner starts from FULL patience — the remaining
        /// time is not on the wire (§10.5), so the gauge is an estimate, never a promise.</summary>
        private float _waitStart;

        private int _lastStage = -1;

        /// <summary>Counter slot from the payload (<c>slot:&lt;n&gt;</c>); <c>-1</c> until it arrives.</summary>
        public int Slot { get; private set; } = -1;

        /// <summary>Order from the payload (<c>r:&lt;tarif&gt;</c>), bottom to top.</summary>
        public string Recipe { get; private set; } = "";

        public int NetId => _net != null ? _net.NetId : 0;

        public int Stage => _net != null ? _net.Stage : 0;

        private void Awake()
        {
            _net = GetComponent<NetObject>();

            if (bubble == null)
            {
                bubble = GetComponentInChildren<BurgerOrderBubble>(true);
            }
        }

        private void OnEnable()
        {
            if (!Customers.Contains(this))
            {
                Customers.Add(this);
            }

            _net.StateChanged += HandleStateChanged;
            ReadPayload();
            ApplyStage(_net.Stage);
        }

        private void OnDisable()
        {
            Customers.Remove(this);
            _net.StateChanged -= HandleStateChanged;
        }

        public static BurgerCustomer Find(int netId)
        {
            for (int i = 0; i < Customers.Count; i++)
            {
                BurgerCustomer customer = Customers[i];
                if (customer != null && customer.NetId == netId)
                {
                    return customer;
                }
            }

            return null;
        }

        /// <summary>Temporary line in this customer's bubble (why the serve was refused).</summary>
        public void ShowNotice(string notice, float seconds)
        {
            if (bubble != null)
            {
                bubble.ShowNotice(notice, seconds);
            }
        }

        /// <summary>Payload is re-read on EVERY state, including the one that arrives with the spawn —
        /// that is where a late joiner gets the waiting customer's order from (§10.5).</summary>
        private void HandleStateChanged(NetObject net, NetStateOrigin origin)
        {
            ReadPayload();
            ApplyStage(net.Stage);
        }

        private void ReadPayload()
        {
            Slot = _net.TryGetPayloadValue(BurgerKinds.PayloadSlot, out string slot) &&
                   int.TryParse(slot, out int parsed)
                ? parsed
                : -1;

            Recipe = _net.TryGetPayloadValue(BurgerKinds.PayloadRecipe, out string recipe) ? recipe : "";
        }

        private void ApplyStage(int stage)
        {
            // A late joiner's FIRST apply is a snapshot, not a transition: the sounds stay silent for it,
            // or every joiner would hear the whole counter arrive at once.
            bool known = _lastStage >= 0;
            bool stageChanged = stage != _lastStage;
            _lastStage = stage;

            if (stageChanged && stage == BurgerKinds.CustomerWalking)
            {
                _walkTimer = 0f;
            }

            if (stageChanged && stage == BurgerKinds.CustomerWaiting)
            {
                _waitStart = Time.time;
            }

            if (stageChanged && (stage == BurgerKinds.CustomerHappy || stage == BurgerKinds.CustomerUnhappy))
            {
                _leaveTimer = 0f;
            }

            if (stageChanged && known)
            {
                PlayStageSound(stage);
            }

            if (bubble != null)
            {
                if (stage == BurgerKinds.CustomerWaiting)
                {
                    bubble.Show(Recipe);
                }
                else
                {
                    bubble.Hide();
                }
            }

            if (moodRenderer == null)
            {
                return;
            }

            if (stage == BurgerKinds.CustomerHappy)
            {
                moodRenderer.material.color = Color.green;
            }
            else if (stage == BurgerKinds.CustomerUnhappy)
            {
                moodRenderer.material.color = Color.red;
            }
        }

        private static void Play(AudioSource source)
        {
            if (source != null)
            {
                source.Play();
            }
        }

        private void PlayStageSound(int stage)
        {
            switch (stage)
            {
                case BurgerKinds.CustomerWaiting:
                    Play(arriveSound);
                    break;

                case BurgerKinds.CustomerHappy:
                    Play(happySound);
                    break;

                case BurgerKinds.CustomerUnhappy:
                    Play(unhappySound);
                    break;
            }
        }

        private void Update()
        {
            switch (_net.Stage)
            {
                case BurgerKinds.CustomerWalking:
                    TickWalk();
                    break;

                case BurgerKinds.CustomerWaiting:
                    TickWait();
                    break;

                default:
                    TickLeave();
                    break;
            }
        }

        private void TickWalk()
        {
            _walkTimer += Time.deltaTime;
            MoveAlongPath(Mathf.Clamp01(_walkTimer / BurgerKinds.CustomerWalkSeconds));
        }

        /// <summary>Seated at the slot anchor. ⚠️ The walk timer is reset HERE so the same budget is
        /// available again for the walk out.</summary>
        private void TickWait()
        {
            _walkTimer = 0f;
            _leaveTimer = 0f;

            if (bubble != null)
            {
                float spent = (Time.time - _waitStart) / BurgerKinds.CustomerPatienceSeconds;
                bubble.SetPatience(1f - Mathf.Clamp01(spent));
            }

            BurgerCounterSlot slot = BurgerCounterSlot.Find(Slot);
            if (slot == null)
            {
                return;
            }

            Transform anchor = slot.CustomerAnchor;
            transform.SetPositionAndRotation(anchor.position, anchor.rotation);
        }

        /// <summary>Same polyline, walked BACKWARDS: <c>t</c> runs 1 → 0.</summary>
        private void TickLeave()
        {
            _leaveTimer += Time.deltaTime;
            MoveAlongPath(1f - Mathf.Clamp01(_leaveTimer / BurgerKinds.CustomerLeaveSeconds));
        }

        private void MoveAlongPath(float t)
        {
            BurgerCustomerPath path = BurgerCustomerPath.Instance;
            if (path == null)
            {
                return;
            }

            Vector3 previous = transform.position;
            Vector3 position = path.Sample(t);

            // The path is shared and ends at the counter; the slots sit beside it. Without this the
            // customer would jump sideways the frame it starts waiting (and back on the way out).
            BurgerCounterSlot slot = BurgerCounterSlot.Find(Slot);
            if (slot != null)
            {
                position = Vector3.Lerp(position, slot.CustomerAnchor.position,
                    Mathf.InverseLerp(SlotBlendStart, 1f, t));
            }

            transform.position = position;

            Vector3 direction = position - previous;
            direction.y = 0f;

            // Below the threshold the direction is noise and would spin the customer on the spot.
            if (direction.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            }
        }
    }
}
