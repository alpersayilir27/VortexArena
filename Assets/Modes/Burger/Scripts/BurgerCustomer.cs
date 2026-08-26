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

        /// <summary>Path fraction at which the customer starts peeling off toward its own slot.</summary>
        private const float SlotBlendStart = 0.75f;

        private NetObject _net;

        private float _walkTimer;
        private float _leaveTimer;

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
            _net.StateChanged += HandleStateChanged;
            ReadPayload();
            ApplyStage(_net.Stage);
        }

        private void OnDisable()
        {
            _net.StateChanged -= HandleStateChanged;
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
            bool stageChanged = stage != _lastStage;
            _lastStage = stage;

            if (stageChanged && stage == BurgerKinds.CustomerWalking)
            {
                _walkTimer = 0f;
            }

            if (stageChanged && (stage == BurgerKinds.CustomerHappy || stage == BurgerKinds.CustomerUnhappy))
            {
                _leaveTimer = 0f;
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
