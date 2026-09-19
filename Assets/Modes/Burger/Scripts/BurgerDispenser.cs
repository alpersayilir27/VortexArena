using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.Combat;
using VortexArena.Net;

namespace VortexArena.Modes.Burger
{
    /// <summary>Ingredient dispenser: a hand squeezing the grip inside its socket raises <c>take</c>
    /// (§10.5).
    /// <para>⚠️ This kind is <c>grab:"none"</c> and carries NO <see cref="NetObjectGrabBridge"/> — the
    /// dispenser itself is never picked up, it only produces an event. The ingredient is born IN THE
    /// HAND on the server (<c>object_spawn</c> with <c>owner</c> + <c>Held</c>), so nothing is created
    /// or attached locally here.</para></summary>
    [RequireComponent(typeof(NetObject))]
    [DisallowMultipleComponent]
    public sealed class BurgerDispenser : MonoBehaviour, IGrabClaimant
    {
        [Tooltip("Elin yaklaşacağı soket (gösterge + kabul yarıçapı). Boşsa çocuklarda aranır.")]
        [SerializeField] private GripSocket socket;

        [Tooltip("İki alma arasındaki en kısa süre — tek basışın iki malzeme doğurmasını engeller.")]
        [SerializeField] private float cooldownSeconds = 0.4f;

        [Tooltip("Malzeme alınırken çalan ses. Atanmazsa sessizdir.")]
        [SerializeField] private AudioSource takeSound;

        // ⚠️ Named INVERTED, like ItemDefinition.hideGrabIndicator: a field that was never written
        // deserializes to 0, so 0 has to mean today's behaviour — the sphere is drawn.
        [Tooltip("Kabul küresi (gizmo) bu dağıtıcıda ÇİZİLMESİN. Yalnız GÖRSELİ susturur — kabul " +
                 "yarıçapı ve alma kapısı aynı kalır, malzeme yine alınır.")]
        [SerializeField] private bool hideGrabIndicator;

        private NetObject _net;

        private bool _gripWasLeft;
        private bool _gripWasRight;

        private float _cooldown;

        private void Awake()
        {
            _net = GetComponent<NetObject>();

            if (socket == null)
            {
                socket = GetComponentInChildren<GripSocket>(true);
            }

            if (socket == null)
            {
                Debug.LogError($"[BurgerDispenser] '{name}' altında GripSocket yok — bu dağıtıcıdan " +
                               "hiçbir malzeme alınamaz.", this);
            }
        }

        private void LateUpdate()
        {
            if (_cooldown > 0f)
            {
                _cooldown -= Time.deltaTime;
            }

            if (socket == null || _net == null || _net.NetId <= 0 || !CalibrationState.IsCalibrated)
            {
                if (socket != null)
                {
                    socket.Tick(false);
                }

                return;
            }

            // ⚠️ The flag is ANDed into "is it available", never into the socket's radius: the accept
            // volume and the take gate stay as they were, so hiding the sphere cannot make a dispenser
            // harder to use.
            socket.Tick(!hideGrabIndicator);
            TickInput();
        }

        /// <summary>Press edge only, same analog grip and threshold as every other hold path
        /// (<see cref="WeaponGranter.GripThreshold"/>).
        /// <para>⚠️ The edge is tracked per hand on EVERY frame, not only inside the socket: sampled on
        /// entry, a hand already squeezing would take an ingredient just by drifting into the
        /// volume.</para></summary>
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

            if (_cooldown > 0f)
            {
                return;
            }

            // ⚠️ BOTH hands are offered, not the socket's nearest one. At the counter the other hand is
            // usually full (spatula, board, an ingredient) and it is often the nearer of the two: asking
            // the socket for one hand answered THAT hand's press, found it full and refused — while the
            // empty hand a few centimetres further back was never looked at. The symptom is "the
            // dispenser gives nothing", with the take gate working exactly as written.
            Offer(false, pressLeft);
            Offer(true, pressRight);
        }

        /// <summary>Offers this dispenser as that hand's candidate, when the hand pressed this frame, is
        /// inside the socket and is empty.</summary>
        private void Offer(bool rightHand, bool press)
        {
            if (!press)
            {
                return;
            }

            OVRInput.Controller hand = rightHand ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch;
            if (!socket.TryMeasure(hand, out float distance) || distance > socket.EffectiveRadius)
            {
                return;
            }

            // ⚠️ A full hand is refused HERE: the server spawns straight into the named hand and would
            // stack a second object on top of the first — two ingredients in one fist, the older one
            // still owned by us and invisible under the new one.
            if (!(rightHand ? HeldItems.RightHand : HeldItems.LeftHand).IsEmpty)
            {
                Debug.Log($"[BurgerDispenser] '{name}': el dolu, malzeme alınmadı (sağ={rightHand}).", this);
                return;
            }

            // ⚠️ The take is NOT sent from here: this socket overlaps the ingredients lying around the
            // dispenser, and a press answered by both would fill the palm twice — one object grabbed,
            // one spawned on top of it. The arbiter calls back the nearest claimant only.
            GrabArbiter.Submit(this, rightHand, distance);
        }

        /// <summary>The arbiter's callback: this dispenser won that hand this frame.</summary>
        public void CommitGrab(bool rightHand)
        {
            // Both hands can win in the same frame; the cooldown is what keeps one press from emptying
            // two ingredients out of one dispenser.
            if (_cooldown > 0f)
            {
                return;
            }

            // A refusal has no reply (§10.10): the server logs "object_event reddedildi … faz …" and the
            // headset sees nothing. This line pairs with that log.
            Debug.Log($"[BurgerDispenser] '{name}': take istendi (sağ={rightHand}, netId={_net.NetId}).", this);
            NetObjectSync.SendEvent(_net.NetId, BurgerKinds.EventTake, new[] { rightHand ? 1 : 0 });
            _cooldown = cooldownSeconds;

            // Played on the ASK, not on the spawn: the ingredient is born on the server and arrives as a
            // spawn with no link back to this dispenser.
            if (takeSound != null)
            {
                takeSound.Play();
            }
        }
    }
}
