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
    public sealed class BurgerDispenser : MonoBehaviour
    {
        [Tooltip("Elin yaklaşacağı soket (gösterge + kabul yarıçapı). Boşsa çocuklarda aranır.")]
        [SerializeField] private GripSocket socket;

        [Tooltip("İki alma arasındaki en kısa süre — tek basışın iki malzeme doğurmasını engeller.")]
        [SerializeField] private float cooldownSeconds = 0.4f;

        [Tooltip("Malzeme alınırken çalan ses. Atanmazsa sessizdir.")]
        [SerializeField] private AudioSource takeSound;

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

            socket.Tick(true);
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

            if (!socket.TryResolveHand(out OVRInput.Controller _, out bool rightHand))
            {
                return;
            }

            if (!(rightHand ? pressRight : pressLeft))
            {
                return;
            }

            // ⚠️ A full hand is refused HERE: the server spawns straight into the named hand and would
            // stack a second object on top of the first — two ingredients in one fist, the older one
            // still owned by us and invisible under the new one.
            if (!(rightHand ? HeldItems.RightHand : HeldItems.LeftHand).IsEmpty)
            {
                return;
            }

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
