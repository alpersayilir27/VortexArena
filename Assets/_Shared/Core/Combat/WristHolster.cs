using UnityEngine;
using VortexArena.Core.Audio;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// <b>The left wrist holster</b>: where the throwable is carried, taken from and refilled
    /// (plan/bomba.md §2-§3). Placed by hand on the rig prefab; it binds itself to the left hand
    /// anchor at runtime.
    /// <para><b>Nothing here reaches the wire.</b> The holster is part of the avatar and the server
    /// does not know it exists; the only network event is the throw itself
    /// (<see cref="ArenaCombat.ReportThrow"/>, §6.4) plus the held-item byte (§6.6).</para>
    /// <para><b>The taking hand is the RIGHT one</b> and only that one — a hand cannot reach its own
    /// wrist. A weapon in the right hand is not in the way: it is parked
    /// (<see cref="WeaponGranter.StowHeld"/>) and the SAME instance comes back after the throw. It is
    /// never dropped and never re-rolled.</para>
    /// <para>⚠️ <b>There is no putting it back.</b> Once taken it is thrown — a release is a throw
    /// even at zero speed (the bomb lands at the player's feet). Anything else would need a "cancel"
    /// gesture with no counterpart anywhere in the game.</para>
    /// <para>⚠️ <b>The refill counts from the BLAST, not from the throw</b>
    /// (<see cref="ThrowableDefinition.HolsterRefillSeconds"/>): the real gap between two throws is
    /// fuse + flight + refill. There is NO ammo limit; this timer is the only one.</para>
    /// </summary>
    /// <remarks>
    /// ⚠️ The item's world pose is written in <see cref="LateUpdate"/> at the DEFAULT execution
    /// order, like <c>Weapon.LateUpdate</c>: <c>HandGripPoser</c> runs at order 100 and locks the
    /// hand onto what is written here, so an earlier writer is required or the hand would lag one
    /// frame behind the bomb.
    /// </remarks>
    public sealed class WristHolster : MonoBehaviour
    {
        /// <summary>Anchor rescan interval while the rig is missing (spectator, scene not loaded).</summary>
        private const float AnchorRescanSeconds = 1f;

        /// <summary>Below this the release velocity has no usable DIRECTION (m/s): the bomb is still
        /// thrown, just straight out of the hand.</summary>
        private const float MinThrowSpeed = 0.01f;

        [Header("Eşya")]
        [Tooltip("Kılıfın taşıdığı atılabilir (bomba). Boşsa kılıf hiçbir şey yapmaz.")]
        [SerializeField] private ThrowableDefinition throwable;

        // Both fields feed the runtime GripSocket (EnsureSocket); they stay serialized HERE so no rig
        // prefab has to be re-wired.
        [Header("İşaretçi")]
        [Tooltip("Kabul küresinin prefabı — 1 m ÇAPINDA tasarlanır, ölçeği koddan verilir.")]
        [SerializeField] private GameObject socketIndicatorPrefab;

        [Tooltip("Kabul yarıçapı (m): sağ kumanda anchor'ı bu kürenin içindeyken kavramaya basılınca " +
                 "bomba ele gelir. Oyuncunun gördüğü küre de tam bu yarıçapla çizilir.")]
        [SerializeField] private float acceptRadius = 0.12f;

        [Header("Bilekteki yer")]
        [Tooltip("Sol el anchor'ına göre konum (metre).")]
        [SerializeField] private Vector3 localOffset;

        [Tooltip("Sol el anchor'ına göre dönüş (derece).")]
        [SerializeField] private Vector3 localEuler;

        [Header("Ses")]
        [Tooltip("Kılıf dolduğunda çalan kısa beliriş sesi.")]
        [SerializeField] private AudioClip refillClip;

        /// <summary>Is there something to take right now. Starts full.</summary>
        private bool _ready = true;

        /// <summary>Wall-clock time the refill completes; negative = no refill running.</summary>
        private float _refillAt = -1f;

        /// <summary>The instance sitting ON the wrist (visible while <see cref="_ready"/>).</summary>
        private GameObject _wristItem;

        /// <summary>The instance IN the right hand; the same object as <see cref="_wristItem"/> was,
        /// reparented out. Non-null exactly while the player carries the bomb.</summary>
        private GameObject _heldItem;

        private Transform _anchor;
        private float _nextAnchorScanAt;
        private bool _gripWasHeld;
        private bool _aliveSubscribed;

        /// <summary>The proximity socket, built on this same object at runtime from the two serialized
        /// fields above — <see cref="GripSocket"/> is the ONE proximity implementation and the holster
        /// drives it instead of carrying a copy. Created in code so no existing rig prefab has to be
        /// re-wired.</summary>
        private GripSocket _socket;

        private bool _grabPathWarned;

        private void OnEnable()
        {
            TrySubscribeAlive();
        }

        private void OnDisable()
        {
            if (_aliveSubscribed && PlayerCombatState.Instance != null)
            {
                PlayerCombatState.Instance.AliveChanged -= HandleAliveChanged;
            }

            _aliveSubscribed = false;

            // ⚠️ The gate must not outlive the holster: a leftover flag would keep the right hand
            // closed to weapons for the rest of the session, with nothing left to explain why.
            ReleaseHandGate();

            if (_heldItem != null)
            {
                Destroy(_heldItem);
                _heldItem = null;
                ClearHeldReport(); // same reason as the gate: a stale slot outlives the holster
            }
        }

        private void LateUpdate()
        {
            if (throwable == null || !IsWristPath())
            {
                return;
            }

            // A weaponless mode carries no bomb at all: the trigger gate alone only refuses the take
            // and would leave the wrist item visible on a family that has nothing to throw.
            if (ModeRuntime.IsWeaponless)
            {
                ClearForWeaponless();
                return;
            }

            // PlayerCombatState bootstraps after scene load, so it may not exist in OnEnable (the
            // same lazy subscription as WeaponGranter/Weapon).
            if (!_aliveSubscribed)
            {
                TrySubscribeAlive();
            }

            Transform anchor = ResolveAnchor();
            if (anchor == null)
            {
                HideIndicator();
                return;
            }

            // ⚠️ Composed by hand, NOT TransformPoint: the offset is in METRES and must not pick up
            // the rig's scale (the same rule as the grip records).
            transform.SetPositionAndRotation(
                anchor.position + anchor.rotation * localOffset,
                anchor.rotation * Quaternion.Euler(localEuler));

            // The starting state is FULL, and the wrist item can only be born once the rig is there —
            // hence here and not in OnEnable (a spectator/late rig would leave the wrist empty).
            if (_ready)
            {
                EnsureWristItem();
            }

            TickRefill();
            TickGrip();
            TickHeldItem();
            TickIndicator();
        }

        // ------------------------------------------------------------------ state

        /// <summary>Refill lands: the wrist fills again and the beep plays. Started by the BLAST
        /// (<see cref="HandleTriggered"/>), never by the throw.</summary>
        private void TickRefill()
        {
            if (_ready || _refillAt < 0f || Time.time < _refillAt)
            {
                return;
            }

            _refillAt = -1f;
            SetReady(true);

            if (refillClip != null)
            {
                AudioSource.PlayClipAtPoint(refillClip, transform.position, AudioMix.Weapons);
            }
        }

        /// <summary>Fills/empties the holster. The wrist item is the SAME object that later goes into
        /// the hand — creating a second one would let the player see one bomb and throw another.</summary>
        private void SetReady(bool ready)
        {
            _ready = ready;

            if (!ready)
            {
                if (_wristItem != null)
                {
                    Destroy(_wristItem);
                    _wristItem = null;
                }

                return;
            }

            EnsureWristItem();
        }

        private void EnsureWristItem()
        {
            if (_wristItem != null || throwable.Prefab == null)
            {
                return;
            }

            _wristItem = Instantiate(throwable.Prefab, transform, false);
            _wristItem.name = throwable.Prefab.name;
            _wristItem.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            Deactivate(_wristItem);
        }

        /// <summary>Makes the item inert while it is CARRIED (on the wrist or in the hand): physics
        /// off, colliders off, throwable behaviour off.
        /// <para>⚠️ Colliders are DISABLED, not destroyed: the thrown bomb needs them back to bounce
        /// off geometry. A live collider in the hand would catch the player's own shot ray and the
        /// grab system.</para></summary>
        private static void Deactivate(GameObject instance)
        {
            var bodies = instance.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                // Transform-driven from here: interpolation goes off with the kinematic flag, or the
                // body keeps rewriting the transform from stale physics poses (RigidbodyDrive).
                RigidbodyDrive.SetKinematic(bodies[i], true);
                bodies[i].useGravity = false;
                bodies[i].detectCollisions = false;
            }

            var colliders = instance.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }

            var throwableBehaviour = instance.GetComponent<Throwable>();
            if (throwableBehaviour != null)
            {
                throwableBehaviour.enabled = false;
            }
        }

        /// <summary>Hands the item over to flight: colliders and the throwable behaviour come back.
        /// <para>⚠️ The <see cref="Rigidbody"/> is deliberately NOT touched here — from
        /// <c>Throwable.Arm</c> on, the body belongs to the flight code, which REBUILDS everything
        /// <see cref="Deactivate"/> switched off (kinematic flag, collisions, interpolation, gravity).
        /// Two writers on one body would produce a different trajectory per client.</para></summary>
        private static void Reactivate(GameObject instance)
        {
            var colliders = instance.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = true;
            }
        }

        // ------------------------------------------------------------------- input

        /// <summary>Press edge takes, release edge throws. The button is the same analog grip and the
        /// same threshold as the weapon paths (<see cref="WeaponGranter.GripThreshold"/>).</summary>
        private void TickGrip()
        {
            bool gripHeld = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.RTouch)
                            >= WeaponGranter.GripThreshold;

            if (gripHeld && !_gripWasHeld)
            {
                TryTake();
            }
            else if (!gripHeld && _gripWasHeld && _heldItem != null)
            {
                Throw();
            }

            _gripWasHeld = gripHeld;
        }

        /// <summary>Takes the bomb into the right hand.
        /// <para>The gate is <see cref="ArenaCombat.CanFire"/> — the same one every trigger asks
        /// (plan §3): in the lobby profile free fire is on, so the bomb is taken and thrown there too
        /// and only its DAMAGE is refused by the server.</para>
        /// <para>⚠️ The refill timer does NOT start here: it starts at the blast.</para></summary>
        private void TryTake()
        {
            if (!_ready || _heldItem != null || throwable.Prefab == null || !ArenaCombat.CanFire)
            {
                return;
            }

            if (!EnsureSocket().IsInside(OVRInput.Controller.RTouch))
            {
                return;
            }

            EnsureWristItem();
            if (_wristItem == null)
            {
                return;
            }

            // ⚠️ Order: close the hand FIRST, then park the weapon — the granter's tick reads the gate
            // and would otherwise re-grant a random weapon into the hand on the very next frame.
            WeaponGranter.SetThrowableHeld(OVRInput.Controller.RTouch, true);
            WeaponGranter.StowHeld(OVRInput.Controller.RTouch);

            _heldItem = _wristItem;
            _wristItem = null;
            _heldItem.transform.SetParent(null, true);

            // §6.6: claims the right hand in the slot registry — this feeds BOTH the wire byte and
            // the finger pose. ⚠️ The weapon was parked one line above, so its own event has already
            // freed the hand; claiming before the stow would be refused.
            if (!HeldItems.Report(this, true, throwable, _heldItem.transform, GripSocketKind.Primary,
                    OVRInput.Controller.RTouch))
            {
                // The hand did not come free (something holds it through a path the stow does not
                // cover). Not silent: the bomb is visibly in hand while the wire and the fingers say
                // otherwise, and only this line names the reason.
                Debug.LogWarning("[WristHolster] Sağ el başka bir eşya tarafından tutuluyor — bomba " +
                                 "ele alındı ama AĞA BİLDİRİLMEDİ ve parmaklar kavramayı almadı.");
            }

            SetReady(false);
            HideIndicator();
        }

        /// <summary>Releasing throws — <b>always</b>, even at zero speed.
        /// <para>The event goes on the wire first (§6.4: direction + speed, no origin) and the local
        /// copy is armed with the SAME two numbers, so the thrower simulates what the receivers
        /// simulate.</para></summary>
        private void Throw()
        {
            GameObject instance = _heldItem;
            _heldItem = null;
            ClearHeldReport();

            ResolveRelease(out Vector3 direction, out float speed);
            Vector3 origin = instance.transform.position;

            ArenaCombat.ReportThrow(direction, speed, throwable.NetItemId, true);

            Reactivate(instance);

            var flight = instance.GetComponent<Throwable>();
            if (flight == null)
            {
                flight = instance.AddComponent<Throwable>();
            }

            flight.enabled = true;
            flight.Triggered += HandleTriggered;

            // catchUpSeconds 0: this is the ORIGINAL, thrown now — only remote copies replay from the
            // event's serverTick.
            flight.Arm(throwable, origin, direction, speed, true, 0f);

            // The weapon comes back into the hand it left; the gate opens after it, so no frame passes
            // with an open gate and an empty hand (which would roll a NEW random weapon).
            ReleaseHandGate();
        }

        /// <summary>Release direction + speed from the controller.
        /// <para>⚠️ <see cref="OVRInput.GetLocalControllerVelocity"/> is in TRACKING space (the hand
        /// anchor's parent); used raw, the throw would go sideways as soon as the rig is rotated in
        /// the arena.</para>
        /// <para>With no usable velocity the hand's own forward is used: the wire carries a DIRECTION
        /// and a zero vector has none — the bomb still leaves the hand and drops at the player's
        /// feet.</para></summary>
        private void ResolveRelease(out Vector3 direction, out float speed)
        {
            Transform hand = WeaponGranter.ResolveHandAnchor(OVRInput.Controller.RTouch);
            Vector3 velocity = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.RTouch);

            Transform trackingSpace = hand != null ? hand.parent : null;
            if (trackingSpace != null)
            {
                velocity = trackingSpace.TransformDirection(velocity);
            }

            speed = Mathf.Min(velocity.magnitude * throwable.ThrowSpeedScale, throwable.MaxThrowSpeed);

            if (velocity.sqrMagnitude > MinThrowSpeed * MinThrowSpeed)
            {
                direction = velocity.normalized;
                return;
            }

            speed = 0f;
            direction = hand != null ? hand.forward : Vector3.forward;
        }

        /// <summary>The blast happened: the refill starts NOW (never at the throw).</summary>
        private void HandleTriggered(Throwable thrown)
        {
            if (thrown != null)
            {
                thrown.Triggered -= HandleTriggered;
            }

            // Already full: the player died holding the bomb (the holster refilled at once) or the
            // holster was refilled by another path — do not push it back to empty.
            if (_ready)
            {
                return;
            }

            _refillAt = Time.time + throwable.HolsterRefillSeconds;
        }

        // ------------------------------------------------------------------- held item

        /// <summary>Drives the carried bomb's world pose with the SAME solver as a granted weapon, so
        /// it sits in the hand exactly as its grip record was authored.
        /// <para>The wire byte and the finger pose are NOT written here: both read this hand's slot
        /// in <see cref="HeldItems"/>, claimed once when the bomb is taken (§6.6).</para></summary>
        private void TickHeldItem()
        {
            if (_heldItem == null)
            {
                return;
            }

            if (WeaponGranter.TryResolvePalm(OVRInput.Controller.RTouch, out Pose palm))
            {
                ItemGripSolver.Solve(throwable, true, false, palm, false, Vector3.zero, 0f,
                    out Vector3 position, out Quaternion rotation);
                _heldItem.transform.SetPositionAndRotation(position, rotation);
            }
        }

        /// <summary>Weaponless mode: both instances go, the hand comes back. <c>_ready</c> and a running
        /// refill are kept — the next weapon mode finds the wrist exactly as it would have been.</summary>
        private void ClearForWeaponless()
        {
            HideIndicator();

            if (_wristItem != null)
            {
                Destroy(_wristItem);
                _wristItem = null;
            }

            if (_heldItem == null)
            {
                return;
            }

            // Same exit as dying with the bomb in hand: gone without an event.
            Destroy(_heldItem);
            _heldItem = null;
            ClearHeldReport();
            ReleaseHandGate();
        }

        /// <summary>Frees the hand this holster claimed. ⚠️ Runs BEFORE the weapon is restored: the
        /// restore claims the same hand and would be refused while the bomb still holds it.</summary>
        private void ClearHeldReport()
        {
            HeldItems.Release(this);
        }

        /// <summary>Gives the hand back to the weapon paths — the exact reverse of the two calls in
        /// <see cref="TryTake"/>.</summary>
        private void ReleaseHandGate()
        {
            WeaponGranter.RestoreStowed(OVRInput.Controller.RTouch);
            WeaponGranter.SetThrowableHeld(OVRInput.Controller.RTouch, false);
        }

        // ------------------------------------------------------------------- death

        private void TrySubscribeAlive()
        {
            if (_aliveSubscribed || PlayerCombatState.Instance == null)
            {
                return;
            }

            PlayerCombatState.Instance.AliveChanged += HandleAliveChanged;
            _aliveSubscribed = true;
        }

        /// <summary>Dying with the bomb in hand: it disappears <b>without going off</b> — no blast, no
        /// event, no damage report (plan §3). A bomb that detonated on death would kill whoever just
        /// won the duel, from a hand that never threw it.
        /// <para>The holster fills immediately, which is what the player sees on respawn: an empty
        /// wrist after a death they did not choose reads as a bug.</para></summary>
        private void HandleAliveChanged(bool alive)
        {
            if (alive || _heldItem == null)
            {
                return;
            }

            Destroy(_heldItem);
            _heldItem = null;
            ClearHeldReport();

            _refillAt = -1f;
            SetReady(true);
            ReleaseHandGate();
        }

        // ------------------------------------------------------------------- socket

        /// <summary>Is the holster the definition's actual grab path. The rule lives in the item
        /// (<see cref="ItemGrabPath"/>), not in this component: a throwable switched to another path
        /// must stop appearing on the wrist, or the same bomb exists in two acquisition routes.</summary>
        private bool IsWristPath()
        {
            if (throwable.GrabPath == ItemGrabPath.WristHolster)
            {
                return true;
            }

            if (!_grabPathWarned)
            {
                _grabPathWarned = true;
                Debug.LogWarning($"[WristHolster] '{throwable.DisplayName}' tanımının alma yolu " +
                                 $"{throwable.GrabPath} — bilek kılıfı bu eşyayı vermez. Tanımda " +
                                 "'Alma yolu' WristHolster seçilmeli.", this);
            }

            return false;
        }

        /// <summary>Builds the proximity socket on this object from the holster's own fields.
        /// <para>⚠️ RIGHT hand only: a hand cannot reach its own wrist, and the holster rides the LEFT
        /// anchor.</para></summary>
        private GripSocket EnsureSocket()
        {
            if (_socket != null)
            {
                return _socket;
            }

            _socket = GetComponent<GripSocket>();
            if (_socket == null)
            {
                _socket = gameObject.AddComponent<GripSocket>();
            }

            _socket.Configure(socketIndicatorPrefab, acceptRadius, false, true);
            return _socket;
        }

        /// <summary>One frame of the socket; the holster only says whether there is something to take.</summary>
        private void TickIndicator()
        {
            EnsureSocket().Tick(_ready && _heldItem == null && ArenaCombat.CanFire);
        }

        private void HideIndicator()
        {
            if (_socket != null)
            {
                _socket.Hide();
            }
        }

        // ------------------------------------------------------------------- rig

        /// <summary>Binds to the LEFT hand anchor, retrying once a second while the rig is missing.
        /// <para>⚠️ Resolved through <see cref="WeaponGranter.ResolveHandAnchor"/> — the ONE rig
        /// discovery path. A second search can find a different rig on a different frame and the
        /// holster would sit on someone else's wrist.</para></summary>
        private Transform ResolveAnchor()
        {
            if (_anchor != null)
            {
                return _anchor;
            }

            if (Time.time < _nextAnchorScanAt)
            {
                return null;
            }

            _nextAnchorScanAt = Time.time + AnchorRescanSeconds;
            _anchor = WeaponGranter.ResolveHandAnchor(OVRInput.Controller.LTouch);
            return _anchor;
        }
    }
}
