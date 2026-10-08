using UnityEngine;
using VortexArena.Core.Combat;
using VortexArena.Core.World;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Modes.Burger
{
    /// <summary>The squeeze bottle hanging over a work island: reads the index trigger of the hand that
    /// holds it, publishes the stream's on/off state (<c>squeeze</c>, §10.5), drives
    /// <see cref="BurgerKetchupStream"/> on every headset and springs the bottle back to its ceiling
    /// anchor once it is let go.
    /// <para>⚠️ The stream itself is NOT on the wire — only its on/off edge. Every client runs the same
    /// simulation from the same hand pose; the cosmetic difference is accepted (§10.5).</para>
    /// <para>⚠️ The stream is closed LOCALLY whenever the bottle leaves a hand or loses its owner: a
    /// lost <c>f:[0]</c> would otherwise leave ketchup pouring forever on the other headsets.</para></summary>
    [RequireComponent(typeof(NetObject))]
    [RequireComponent(typeof(NetObjectGrabBridge))]
    [DisallowMultipleComponent]
    public sealed class BurgerKetchupBottle : MonoBehaviour, INetRestPoseAdjuster
    {
        [Tooltip("Akış simülasyonu (şişenin ucundaki çocuk obje).")]
        [SerializeField] private BurgerKetchupStream stream;

        [Tooltip("Tavandaki asılma noktası — bırakılan şişe buraya döner ve dinlenme pozu burasıdır. " +
                 "Sahnedeki HangAnchor objesi.")]
        [SerializeField] private Transform hangAnchor;

        [Tooltip("Kordonun tavan ucu (makara). Boşsa asılma noktası kullanılır.")]
        [SerializeField] private Transform cordAnchor;

        [Tooltip("Kordonun şişe ucu (boğaz). Boşsa şişenin kökü kullanılır.")]
        [SerializeField] private Transform cordNeck;

        [Tooltip("Kordon çizgisi. Fizik değildir, iki noktalı çizgidir; her istemcide çizilir.")]
        [SerializeField] private LineRenderer cord;

        [Header("Yay geri dönüşü (yalnız sahip)")]
        [Tooltip("Asılma noktasına çeken yay katsayısı (1/sn²).")]
        [SerializeField] private float springStrength = 24f;

        [Tooltip("Salınımı söndüren katsayı (1/sn). Çok küçükse şişe durmaz, dinlenme hiç gitmez.")]
        [SerializeField] private float springDamping = 4f;

        [Tooltip("Bırakılan şişenin asılma açısına dönme hızı (derece/sn).")]
        [SerializeField] private float uprightSpeed = 720f;

        [Tooltip("Kordon boyu (m): şişe asılma noktasından bu kadar uzaklaşınca elden düşer ve yerine döner.")]
        [SerializeField] private float leashRadius = 3f;

        [Tooltip("Görüş kenarı lekesi prefabı (BurgerKetchupVision). İlk şişe, oyuncu kamerasının " +
                 "(CenterEyeAnchor) altına bir kez örnekler; kamera rig'i yoksa (admin) örneklenmez.")]
        [SerializeField] private GameObject visionPrefab;

        [Header("Ses")]
        [Tooltip("Sıkma başlangıcı — tek seferlik. Döngülü ses taşınan objede çalmaz.")]
        [SerializeField] private AudioSource squeezeStartSound;

        [Tooltip("Sıkma bitişi — tek seferlik.")]
        [SerializeField] private AudioSource squeezeStopSound;

        /// <summary>Index trigger hysteresis, copied from <c>Weapon.TickTrigger</c>.</summary>
        /// <remarks>⚠️ Copied, not called: <c>ArenaCombat.CanFire</c> is false for the whole Burger shift
        /// (<c>weaponSource:"none"</c>, §10.5), so the weapon path would never let a squeeze through.
        /// A single threshold would stutter the stream on a finger resting at the edge.</remarks>
        private const float TriggerPressThreshold = 0.55f;

        /// <inheritdoc cref="TriggerPressThreshold"/>
        private const float TriggerReleaseThreshold = 0.35f;

        private NetObject _net;
        private NetObjectGrabBridge _bridge;
        private NetObjectPoseSender _sender;
        private Rigidbody _body;

        /// <summary>The controller holding it on THIS headset; <c>None</c> = not in our hand.</summary>
        private OVRInput.Controller _localHand = OVRInput.Controller.None;

        /// <summary>Our own trigger state (hysteresis) — the thing we publish.</summary>
        private bool _triggerHeld;

        /// <summary>Is the stream running right now (ours or a relayed one)?</summary>
        private bool _streaming;

        private void Awake()
        {
            _net = GetComponent<NetObject>();
            _bridge = GetComponent<NetObjectGrabBridge>();
            _sender = GetComponent<NetObjectPoseSender>();
            _body = GetComponent<Rigidbody>();

            if (hangAnchor == null)
            {
                Debug.LogError($"[BurgerKetchupBottle] '{name}' için asılma noktası atanmamış — şişe " +
                               "bırakıldığı yerde kalır, yerine dönmez.", this);
            }

            if (stream == null)
            {
                Debug.LogError($"[BurgerKetchupBottle] '{name}' için akış bileşeni atanmamış — şişe " +
                               "sıkılır ama ketçap çıkmaz.", this);
            }

            EnsureVision();
        }

        /// <summary>Puts the visor overlay under the local camera once per scene.</summary>
        /// <remarks>⚠️ Not baked into the Burger HUD prefab: that prefab is regenerated by the Girdap UI
        /// builder and a hand-added child would be wiped on the next build.</remarks>
        private void EnsureVision()
        {
            if (visionPrefab == null || BurgerKetchupVision.Active != null)
            {
                return;
            }

            var rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig == null || rig.centerEyeAnchor == null)
            {
                return;
            }

            GameObject vision = Instantiate(visionPrefab, rig.centerEyeAnchor, false);
            vision.transform.localPosition = visionPrefab.transform.localPosition;
            vision.transform.localRotation = visionPrefab.transform.localRotation;
        }

        private void OnEnable()
        {
            _bridge.GrabbedLocally += HandleGrabbedLocally;
            _bridge.ReleasedLocally += HandleReleasedLocally;
            _net.EventReceived += HandleEventReceived;
            _net.OwnerChanged += HandleOwnerChanged;

            if (_sender != null)
            {
                _sender.AddRestAdjuster(this);
            }

            if (cord != null)
            {
                cord.useWorldSpace = true;
                cord.positionCount = 2;
            }
        }

        private void OnDisable()
        {
            _bridge.GrabbedLocally -= HandleGrabbedLocally;
            _bridge.ReleasedLocally -= HandleReleasedLocally;
            _net.EventReceived -= HandleEventReceived;
            _net.OwnerChanged -= HandleOwnerChanged;

            if (_sender != null)
            {
                _sender.RemoveRestAdjuster(this);
            }

            SetStreaming(false);
            _localHand = OVRInput.Controller.None;
            _triggerHeld = false;
        }

        // ----------------------------------------------------------------- grab

        private void HandleGrabbedLocally(bool rightHand)
        {
            _localHand = rightHand ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch;
            _triggerHeld = false;
        }

        private void HandleReleasedLocally(bool published)
        {
            _localHand = OVRInput.Controller.None;

            // ⚠️ The "off" must go out before we forget we were squeezing — otherwise the other headsets
            // keep pouring. On an UNDONE grab nothing is published: the object is someone else's and the
            // owner policy would refuse the event anyway (§10.10).
            if (_triggerHeld && published)
            {
                SendSqueeze(false);
            }

            _triggerHeld = false;
            SetStreaming(false);
        }

        /// <summary>Ownership ended (rest, death, a dropped headset): nobody is squeezing any more.</summary>
        private void HandleOwnerChanged(NetObject net, int previousOwner)
        {
            if (_net.Owner == 0 || !_net.IsHeld)
            {
                SetStreaming(false);
            }
        }

        // ----------------------------------------------------------------- squeeze

        private void Update()
        {
            if (_localHand != OVRInput.Controller.None && _net.IsMine && _net.IsHeld)
            {
                // Tether: carried too far from its reel, the bottle slips out of the hand and springs back.
                if (hangAnchor != null && leashRadius > 0f &&
                    (transform.position - hangAnchor.position).sqrMagnitude > leashRadius * leashRadius)
                {
                    _bridge.ForceRelease();
                    return;
                }

                TickTrigger();
            }
            else if (_triggerHeld)
            {
                // We lost the bottle without a local release (the server handed it over): close our own
                // stream and let the relayed state rule again.
                _triggerHeld = false;
                SetStreaming(false);
            }

            // Held by nobody = no stream, whatever the last event said.
            if (_streaming && !_net.IsHeld)
            {
                SetStreaming(false);
            }

            DrawCord();
        }

        private void TickTrigger()
        {
            float trigger = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, _localHand);
            bool wasHeld = _triggerHeld;
            _triggerHeld = wasHeld
                ? trigger >= TriggerReleaseThreshold
                : trigger > TriggerPressThreshold;

            if (_triggerHeld == wasHeld)
            {
                return;
            }

            SendSqueeze(_triggerHeld);
            SetStreaming(_triggerHeld);
        }

        private void SendSqueeze(bool on)
        {
            NetObjectSync.SendEvent(_net.NetId, BurgerKinds.EventSqueeze, f: new[] { on ? 1f : 0f });
        }

        /// <summary>The relayed state of someone else's bottle (§10.5).</summary>
        private void HandleEventReceived(ObjectEventMsg msg)
        {
            if (msg == null || msg.name != BurgerKinds.EventSqueeze || _net.IsMine)
            {
                return;
            }

            bool on = msg.f != null && msg.f.Length > 0 && msg.f[0] > 0.5f;
            SetStreaming(on && _net.IsHeld);
        }

        private void SetStreaming(bool on)
        {
            if (on == _streaming)
            {
                return;
            }

            _streaming = on;

            if (stream != null)
            {
                stream.SetEmitting(on);
            }

            AudioSource sound = on ? squeezeStartSound : squeezeStopSound;
            if (sound != null)
            {
                sound.Play();
            }
        }

        // ----------------------------------------------------------------- cord + spring

        /// <summary>Reel ↔ neck, on every headset. A line, not physics: a VR hand cannot be stopped, so
        /// the cord stretches without limit instead of fighting the arm.</summary>
        private void DrawCord()
        {
            if (cord == null)
            {
                return;
            }

            Transform top = cordAnchor != null ? cordAnchor : hangAnchor;
            if (top == null)
            {
                return;
            }

            cord.SetPosition(0, top.position);
            cord.SetPosition(1, cordNeck != null ? cordNeck.position : transform.position);
        }

        /// <summary>Damped spring back to the ceiling anchor — ONLY on the owner, whose physics is the
        /// one running (<see cref="NetObjectBody"/> keeps everyone else kinematic).</summary>
        /// <remarks>⚠️ The damping is what ends the flight: the pose sender closes ownership when the
        /// speed stays under <c>OBJECT_REST_SPEED</c>, and an undamped spring would swing forever.</remarks>
        private void FixedUpdate()
        {
            if (_body == null || _body.isKinematic || hangAnchor == null)
            {
                return;
            }

            if (_net.NetId <= 0 || !_net.IsMine || _net.IsHeld)
            {
                return;
            }

            Vector3 toHome = hangAnchor.position - transform.position;
            _body.AddForce(toHome * springStrength - _body.linearVelocity * springDamping,
                ForceMode.Acceleration);

            // Straighten right away instead of keeping the hand's last angle until the rest snap.
            _body.angularVelocity = Vector3.zero;
            _body.MoveRotation(Quaternion.RotateTowards(_body.rotation, hangAnchor.rotation,
                uprightSpeed * Time.fixedDeltaTime));
        }

        /// <summary>Rests the bottle exactly on its anchor instead of wherever the swing died, so the
        /// published resting pose is the same home pose on every headset.</summary>
        public bool TryAdjustRestPose(NetObject net, ref Pose worldPose)
        {
            if (net != _net || hangAnchor == null)
            {
                return false;
            }

            worldPose = new Pose(hangAnchor.position, hangAnchor.rotation);
            return true;
        }
    }
}
