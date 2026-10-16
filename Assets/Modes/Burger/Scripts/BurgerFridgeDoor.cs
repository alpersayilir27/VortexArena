using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.Combat;
using VortexArena.Net;

namespace VortexArena.Modes.Burger
{
    /// <summary>Fridge door leaf swung by hand on its hinge; the angle is synced as the object's stage.</summary>
    /// <remarks>Sits on the non-rotating hinge pivot; <see cref="leaf"/> rotates. ⚠️ Not a
    /// NetObjectGrabBridge and no <c>object_grab</c>: only <c>object_event "swing"</c> goes out, the
    /// server's <c>stage</c> is the only authority.</remarks>
    [RequireComponent(typeof(NetObject))]
    [DisallowMultipleComponent]
    public sealed class BurgerFridgeDoor : MonoBehaviour, IGrabClaimant
    {
        [Tooltip("Dönen kanat (bu objenin çocuğu). Menteşe pivotu BU obje, kanat yalnız yerel Y'de döner.")]
        [SerializeField] private Transform leaf;

        [Tooltip("Tutamak çubuğundaki yakınlık soketi (kanadın çocuğu). Boşsa çocuklarda aranır.")]
        [SerializeField] private GripSocket handle;

        [Tooltip("Tutamağın eşya tanımı: elin tutamağa nasıl kilitlendiğini taşır. Örneklemesi " +
                 "WorldSingle olmalı — kapı kopyalanmaz.")]
        [SerializeField] private ItemDefinition handleItem;

        [Tooltip("Açılma yönü: kanat yerel Y'de +1 ya da -1 ile döner (sağ/sol kanat).")]
        [SerializeField] private float openSign = 1f;

        [Tooltip("Azami açıklık (derece). Üstünde kanat gövdenin içine girer — sunucu da kıskaçlar.")]
        [SerializeField] private float maxAngle = 110f;

        [Tooltip("Bırakışta bu açının altındaki kanat tam kapanır (derece).")]
        [SerializeField] private float snapClosedBelow = 6f;

        [Tooltip("Başkasının açtığı kanadın takip hızı (derece/sn).")]
        [SerializeField] private float remoteSwingSpeed = 240f;

        [Tooltip("Kapı açılırken çıkan soğuk buhar (isteğe bağlı).")]
        [SerializeField] private ParticleSystem openPuff;

        [Tooltip("Açılma sesi (isteğe bağlı).")]
        [SerializeField] private AudioSource openSound;

        [Tooltip("Kapanma sesi (isteğe bağlı).")]
        [SerializeField] private AudioSource closeSound;

        // Opening feedback threshold: above a hand resting on the handle, below a visibly open door.
        private const float PuffAngle = 3f;

        // Hand farther than this from the handle drops the grip, else the door follows a walking player.
        private const float LostGripMeters = 0.35f;

        // Reliable queue also carries damage/phase messages: keep swing reports sparse.
        private const float SendInterval = 0.1f;

        // After release our own older sends are still in flight and would pull the door back.
        private const float ReleaseSettleSeconds = 0.35f;

        private NetObject _net;
        private Quaternion _leafClosed;

        private float _angle;   // drawn angle (deg, 0 = closed)
        private float _target;  // remote target angle

        private OVRInput.Controller _localHand = OVRInput.Controller.None;
        private bool _localRight;

        private bool _gripWasLeft;
        private bool _gripWasRight;

        // Door follows the hand's azimuth DELTA from the press, so it never jumps to the hand.
        private float _grabAzimuth;
        private float _grabAngle;

        private float _nextSendTime;
        private int _sentDegrees;
        private float _remoteBlockedUntil;

        private bool Dragging => _localHand != OVRInput.Controller.None;

        private void Awake()
        {
            _net = GetComponent<NetObject>();

            if (handle == null)
            {
                handle = GetComponentInChildren<GripSocket>(true);
            }

            if (leaf != null)
            {
                _leafClosed = leaf.localRotation;
            }
            else
            {
                Debug.LogError($"[BurgerFridgeDoor] '{name}' için kanat (leaf) atanmamış — kapı hiç " +
                               "dönmez.", this);
            }

            if (handle == null)
            {
                Debug.LogError($"[BurgerFridgeDoor] '{name}' altında GripSocket yok — kapı hiçbir elden " +
                               "tutulamaz.", this);
            }

            if (handleItem == null)
            {
                // Without a definition HeldItems refuses the claim: the grab button silently does nothing.
                Debug.LogError($"[BurgerFridgeDoor] '{name}' için handleItem atanmamış — tutamak ele " +
                               "kaydedilemez, kapı tutulamaz.", this);
            }
        }

        private void OnEnable()
        {
            _net.StateChanged += HandleStateChanged;

            // State may land before this wakes (scene load order); adopt it silently.
            _target = Mathf.Clamp(_net.Stage, 0f, MaxAngle);
            ApplyAngle(_target, true);
        }

        private void OnDisable()
        {
            _net.StateChanged -= HandleStateChanged;

            // ⚠️ A leftover hand gate keeps that hand closed to weapons for the rest of the session.
            ReleaseHand(false);
        }

        private void LateUpdate()
        {
            TickInput();

            if (!Dragging)
            {
                TickRemote();
            }

            TickSocket();
        }

        // ------------------------------------------------------------------- input

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

            if (Dragging)
            {
                bool stillHeld = _localRight ? gripRight : gripLeft;
                if (!stillHeld)
                {
                    ReleaseHand(true);
                    return;
                }

                TickDrag();
                return;
            }

            if (leaf == null || handle == null || handleItem == null || !CalibrationState.IsCalibrated)
            {
                return;
            }

            if (!handle.TryResolveHand(out OVRInput.Controller hand, out bool rightHand) ||
                !handle.TryMeasure(hand, out float distance))
            {
                return;
            }

            // Highlight lane uses the same ranking that will answer the press.
            if (HandCanTake(rightHand))
            {
                GrabArbiter.SubmitPreview(this, rightHand, distance + handle.RankBiasMeters);
            }

            bool press = rightHand ? pressRight : pressLeft;
            if (!press)
            {
                return;
            }

            // ⚠️ Never grab here: the arbiter picks one winner per hand (Yapma-Listesi).
            GrabArbiter.Submit(this, rightHand, distance + handle.RankBiasMeters);
        }

        // A hand holding a world object must let go first; a weapon gets stowed by the grab.
        private static bool HandCanTake(bool rightHand)
        {
            HeldItems.Slot slot = rightHand ? HeldItems.RightHand : HeldItems.LeftHand;
            return slot.Definition == null || !slot.Definition.IsWorldSingle;
        }

        /// <summary>Arbiter callback: this door won that hand this frame.</summary>
        public void CommitGrab(bool rightHand)
        {
            if (Dragging || leaf == null || handle == null || handleItem == null)
            {
                return;
            }

            OVRInput.Controller hand = rightHand ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch;
            if (!TryReadAzimuth(hand, out float azimuth))
            {
                return;
            }

            _localHand = hand;
            _localRight = rightHand;

            // ⚠️ Close the hand before stowing, or the granter re-grants a weapon next frame.
            WeaponGranter.SetThrowableHeld(hand, true);
            WeaponGranter.StowHeld(hand);

            // ⚠️ Report after the stow (the parked weapon frees the slot); a refusal is a full rollback.
            if (!HeldItems.Report(this, rightHand, handleItem, handle.transform, GripSocketKind.Primary,
                    hand))
            {
                Debug.LogWarning($"[BurgerFridgeDoor] '{name}': el başka bir eşya tarafından tutuluyor — " +
                                 "kapı tutulamadı.", this);
                ReleaseHand(false);
                return;
            }

            handle.Hide();

            _grabAzimuth = azimuth;
            _grabAngle = _angle;
            _sentDegrees = Mathf.RoundToInt(_angle);
            _nextSendTime = 0f;
        }

        private void TickDrag()
        {
            if (!TryReadAzimuth(_localHand, out float azimuth) || LostGrip())
            {
                ReleaseHand(true);
                return;
            }

            // Leaf turns by OpenSign * angle, so the hand's delta maps back through the same sign.
            float delta = Mathf.DeltaAngle(_grabAzimuth, azimuth) * OpenSign;
            ApplyAngle(Mathf.Clamp(_grabAngle + delta, 0f, MaxAngle), false);
            TrySend(false);
        }

        private bool LostGrip()
        {
            Transform anchor = WeaponGranter.ResolveHandAnchor(_localHand);
            if (anchor == null)
            {
                return true;
            }

            return (handle.transform.position - anchor.position).sqrMagnitude >
                   LostGripMeters * LostGripMeters;
        }

        /// <summary>Hand anchor's angle around the hinge in the hinge's own frame (deg).</summary>
        /// <remarks>Not world space: calibration rotates the arena and would shift the door's zero.</remarks>
        private bool TryReadAzimuth(OVRInput.Controller hand, out float azimuth)
        {
            azimuth = 0f;

            Transform anchor = WeaponGranter.ResolveHandAnchor(hand);
            if (anchor == null)
            {
                return false;
            }

            Vector3 local = transform.InverseTransformPoint(anchor.position);
            if (local.x * local.x + local.z * local.z < 1e-6f)
            {
                return false;   // on the hinge axis: azimuth undefined
            }

            azimuth = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            return true;
        }

        /// <summary>Lets the handle go; <paramref name="send"/> = real release (snap closed + final send).</summary>
        private void ReleaseHand(bool send)
        {
            if (!Dragging)
            {
                return;
            }

            OVRInput.Controller hand = _localHand;
            _localHand = OVRInput.Controller.None;

            // ⚠️ Free the slot before restoring the weapon, or the restore is refused.
            HeldItems.Release(this);
            WeaponGranter.RestoreStowed(hand);
            WeaponGranter.SetThrowableHeld(hand, false);

            if (!send)
            {
                _target = _angle;
                return;
            }

            if (_angle < snapClosedBelow)
            {
                ApplyAngle(0f, false);
            }

            _target = _angle;
            _remoteBlockedUntil = Time.time + ReleaseSettleSeconds;
            TrySend(true);
        }

        // ------------------------------------------------------------------- network

        // ⚠️ Client only reports; stage is written by the server (Yapma-Listesi).
        private void TrySend(bool force)
        {
            if (_net.NetId <= 0)
            {
                return;   // offline editor: door works locally
            }

            int degrees = Mathf.RoundToInt(_angle);
            if (!force && (degrees == _sentDegrees || Time.time < _nextSendTime))
            {
                return;
            }

            _sentDegrees = degrees;
            _nextSendTime = Time.time + SendInterval;
            NetObjectSync.SendEvent(_net.NetId, BurgerKinds.EventSwing, new[] { degrees });
        }

        // ⚠️ Ignored while we drag: the round-trip-old server angle would fight the hand.
        private void HandleStateChanged(NetObject net, NetStateOrigin origin)
        {
            if (Dragging)
            {
                return;
            }

            float stage = Mathf.Clamp(net.Stage, 0f, MaxAngle);

            if (origin == NetStateOrigin.Snapshot)
            {
                // Late joiner: adopt without feedback.
                _target = stage;
                ApplyAngle(stage, true);
                return;
            }

            if (Time.time < _remoteBlockedUntil)
            {
                return;
            }

            _target = stage;
        }

        // Wire carries ≤10 samples/s: ease toward the target instead of stepping.
        private void TickRemote()
        {
            if (Mathf.Approximately(_angle, _target))
            {
                return;
            }

            ApplyAngle(Mathf.MoveTowards(_angle, _target, remoteSwingSpeed * Time.deltaTime), false);
        }

        // ------------------------------------------------------------------- presentation

        // Feedback follows the DRAWN angle, so every headset plays it whoever opened the door.
        private void ApplyAngle(float angle, bool silent)
        {
            float previous = _angle;
            _angle = angle;

            if (leaf != null)
            {
                leaf.localRotation = _leafClosed * Quaternion.Euler(0f, OpenSign * _angle, 0f);
            }

            if (silent)
            {
                return;
            }

            if (previous < PuffAngle && _angle >= PuffAngle)
            {
                if (openPuff != null) openPuff.Play();
                if (openSound != null) openSound.Play();
            }
            else if (previous > 0f && _angle <= 0f && closeSound != null)
            {
                closeSound.Play();
            }
        }

        private void TickSocket()
        {
            if (handle == null)
            {
                return;
            }

            bool available = !Dragging && leaf != null && handleItem != null &&
                             CalibrationState.IsCalibrated;

            bool show = GrabArbiter.IsPreviewWinner(this) ||
                        (handleItem != null && handleItem.ShowGrabIndicator);

            handle.Tick(available && show);
        }

        // Never 0: an unset sign would freeze the leaf and read as a broken door.
        private float OpenSign => openSign < 0f ? -1f : 1f;

        // ⚠️ Mirrors the server clamp (BurgerMode.FridgeDoorMaxAngle); drifting apart desyncs headsets.
        private float MaxAngle => Mathf.Clamp(maxAngle, 1f, 180f);
    }
}
