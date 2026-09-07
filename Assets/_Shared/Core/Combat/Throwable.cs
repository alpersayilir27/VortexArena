using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.Player;
using VortexArena.Protocol;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Flight of a thrown item, simulated INDEPENDENTLY on every client (Docs/ArenaNet-Protokol.md
    /// §6.4, throw contract): the event is a start condition, not a trajectory — not one byte flows
    /// during the flight. The thrower's copy and the remote copies run the exact same
    /// <see cref="Arm"/>, from the same QUANTIZED values, so nobody is the odd one out.
    /// <para>⚠️ Divergence is kept small by three rules: only static geometry is collided with
    /// (avatars sit somewhere else on every client), everything not on the wire (rotation, spin) is
    /// DERIVED from the direction, and the remote copy catches up from the event's tick instead of
    /// starting "now". The remaining drift is cosmetic — damage always comes from the thrower's copy
    /// (<see cref="LocalOwner"/>).</para>
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class Throwable : MonoBehaviour
    {
        /// <summary>Upper bound of the analytic catch-up. A longer lead means an event so late that
        /// jumping the item metres ahead is worse than the delay itself.</summary>
        private const float MaxCatchUpSeconds = 0.5f;

        /// <summary>Avatars can spawn DURING the flight, so the ignore list is rebuilt this often.</summary>
        private const float AvatarScanIntervalSeconds = 0.5f;

        /// <summary>Extra life after the fuse before a never-triggered item removes itself (an
        /// Impact type that hits nothing would otherwise live forever).</summary>
        private const float SafetyExtraSeconds = 10f;

        /// <summary>Damping from the first contact with geometry. PhysX has no rolling resistance:
        /// a sphere would roll on until the fuse. In flight the prefab's values stay (the derived
        /// tumble must survive the flight). Same rule on every copy → no extra divergence.</summary>
        private const float LandedLinearDamping = 1.5f;
        private const float LandedAngularDamping = 4f;

        private static readonly Collider[] EmptyColliders = new Collider[0];

        private Rigidbody _body;
        private Collider[] _ownColliders = EmptyColliders;
        private float _flightLinearDamping;
        private float _flightAngularDamping;

        private bool _armed;
        private bool _triggered;
        private bool _landed;

        private float _nextAvatarScan;
        private float _detonateTime;
        private float _expireTime;
        private float _probeRadius = 0.05f;

        /// <summary>Definition the item was armed with (null until <see cref="Arm"/>).</summary>
        public ThrowableDefinition Definition { get; private set; }

        /// <summary>Was the throw made by THIS client — the single copy allowed to report damage.</summary>
        public bool LocalOwner { get; private set; }

        /// <summary>Raised at detonation, right before the object is destroyed (the wrist holster
        /// starts its refill from here, never from the throw).</summary>
        public System.Action<Throwable> Triggered;

        /// <summary>Instantiates the definition's prefab and arms it. Null if there is no prefab.</summary>
        public static Throwable SpawnAndArm(ThrowableDefinition definition, Vector3 worldOrigin,
            Vector3 worldDirection, float speedMetersPerSecond, bool localOwner, float catchUpSeconds)
        {
            if (definition == null || definition.Prefab == null)
            {
                Debug.LogWarning("[Throwable] Atılabilir tanımı ya da prefabı yok — atış görsel olarak oynatılamadı.");
                return null;
            }

            GameObject instance = Instantiate(definition.Prefab, worldOrigin, Quaternion.identity);

            var throwable = instance.GetComponent<Throwable>();
            if (throwable == null)
            {
                Debug.LogWarning($"[Throwable] '{definition.Prefab.name}' prefabında Throwable bileşeni yok — " +
                                 "çalışma zamanında eklendi (prefaba eklenmesi gerekir).");
                throwable = instance.AddComponent<Throwable>();
            }

            throwable.Arm(definition, worldOrigin, worldDirection, speedMetersPerSecond, localOwner, catchUpSeconds);
            return throwable;
        }

        /// <summary>
        /// Starts the flight. Every step here is IDEMPOTENT across clients: the same event produces
        /// the same trajectory locally and remotely.
        /// </summary>
        /// <param name="catchUpSeconds">How long the event's playback time is already past (remote
        /// copies); 0 for the thrower.</param>
        public void Arm(ThrowableDefinition definition, Vector3 worldOrigin, Vector3 worldDirection,
            float speedMetersPerSecond, bool localOwner, float catchUpSeconds)
        {
            if (definition == null)
            {
                return;
            }

            Definition = definition;
            LocalOwner = localOwner;

            if (_body == null)
            {
                _body = GetComponent<Rigidbody>();
                _flightLinearDamping = _body.linearDamping;
                _flightAngularDamping = _body.angularDamping;
            }

            _ownColliders = GetComponentsInChildren<Collider>(true);
            _probeRadius = ResolveProbeRadius();

            // Build the effect's presentation while the fuse burns, not at the explosion.
            ThrowableEffect armedEffect = GetComponentInChildren<ThrowableEffect>();
            if (armedEffect != null)
            {
                armedEffect.Prewarm(definition);
            }

            // 1) QUANTIZE FIRST. The thrower runs the very same octahedral round-trip and cm/s
            // rounding the wire would have applied — otherwise the odd copy out is the thrower's own
            // (§6.4).
            Vector3 dir = Quantize(worldDirection);
            float speed = Mathf.Clamp(Mathf.Round(speedMetersPerSecond * 100f) / 100f, 0f, definition.MaxThrowSpeed);

            // 2) Rotation is DERIVED from the direction. No randomness anywhere in this method.
            transform.SetPositionAndRotation(worldOrigin, Quaternion.LookRotation(dir));

            // The body is REBUILT, not trusted: a carried copy arrives kinematic, collision-less and
            // uninterpolated (WristHolster.Deactivate). ⚠️ detectCollisions is INDEPENDENT of
            // isKinematic — left off, contact is never generated and enabled colliders change nothing.
            RigidbodyDrive.SetKinematic(_body, false);
            _body.detectCollisions = true;
            _body.useGravity = true;
            _body.linearDamping = _flightLinearDamping;
            _body.angularDamping = _flightAngularDamping;
            _landed = false;
            _body.linearVelocity = dir * speed;

            // 3) ⚠️ Spin is derived too: hand rotation is not on the wire, so a random tumble would
            // pull the copies apart within the first second.
            Vector3 axis = Vector3.Cross(dir, Vector3.up);
            if (axis.sqrMagnitude < 1e-4f)
            {
                axis = Vector3.Cross(dir, Vector3.right);
            }

            _body.angularVelocity = axis.normalized * (definition.SpinDegreesPerSecond * Mathf.Deg2Rad);

            // 4) Second net over the code-side avatar exclusion below (scene volumes that are not
            // geometry).
            _body.excludeLayers = definition.ExcludedLayers;

            IgnoreAvatarColliders();
            _nextAvatarScan = Time.time + AvatarScanIntervalSeconds;

            ApplyCatchUp(Mathf.Clamp(catchUpSeconds, 0f, MaxCatchUpSeconds));

            float fuse = definition.FuseSeconds;
            _detonateTime = Time.time + Mathf.Max(0f, fuse - Mathf.Max(0f, catchUpSeconds));
            _expireTime = Time.time + fuse + SafetyExtraSeconds;
            _armed = true;
        }

        /// <summary>⚠️ The fuse keeps running after the thrower DIES and the blast is still reported:
        /// <c>ArenaCombat.CanFire</c> closes throwing/firing, never an item already in the air (the
        /// server has a posthumous window for it, §10.3). No phase gate is read here.</summary>
        private void Update()
        {
            if (!_armed || _triggered)
            {
                return;
            }

            float now = Time.time;

            if (now >= _nextAvatarScan)
            {
                _nextAvatarScan = now + AvatarScanIntervalSeconds;
                IgnoreAvatarColliders();
            }

            if (Definition.Trigger == ThrowableTrigger.Fuse && now >= _detonateTime)
            {
                Detonate();
                return;
            }

            if (now >= _expireTime)
            {
                // Nothing ever set it off (an Impact type that hit nothing): leave silently, no FX.
                Destroy(gameObject);
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!_armed || _triggered)
            {
                return;
            }

            if (!_landed)
            {
                // First contact with geometry: settle from here instead of rolling to the fuse.
                _landed = true;
                _body.linearDamping = LandedLinearDamping;
                _body.angularDamping = LandedAngularDamping;
            }

            if (Definition.Trigger == ThrowableTrigger.Impact)
            {
                Detonate();
            }
        }

        private void Detonate()
        {
            if (_triggered)
            {
                return;
            }

            _triggered = true;

            ThrowableEffect effect = GetComponentInChildren<ThrowableEffect>();
            if (effect != null)
            {
                effect.Trigger(this);
            }

            Triggered?.Invoke(this);
            Destroy(gameObject);
        }

        // ------------------------------------------------------------------ determinism helpers

        /// <summary>World direction through the wire's own octahedral round-trip (§6.4), back in
        /// world space.</summary>
        private static Vector3 Quantize(Vector3 worldDirection)
        {
            Vector3 arenaDir = ArenaSpace.WorldToArenaDirection(worldDirection);
            OctahedralDirection.Encode(arenaDir.x, arenaDir.y, arenaDir.z, out short ox, out short oy);
            OctahedralDirection.Decode(ox, oy, out float x, out float y, out float z);

            // ⚠️ The DIRECTION gate, not ArenaToWorld(Vector3) — that one is for positions.
            return ArenaSpace.ArenaToWorldDirection(new Vector3(x, y, z));
        }

        /// <summary>
        /// Advances a late remote copy ANALYTICALLY (gravity is the only force) and probes the path
        /// with a single sphere cast; on a hit the item is placed at the contact and PhysX takes over
        /// from there.
        /// <para>⚠️ <c>Physics.Simulate</c> is not an option: it is global and would step the whole
        /// scene for one item.</para>
        /// </summary>
        private void ApplyCatchUp(float seconds)
        {
            if (seconds <= 0f)
            {
                return;
            }

            Vector3 gravity = Physics.gravity;
            Vector3 start = transform.position;
            Vector3 velocity = _body.linearVelocity;

            Vector3 end = start + velocity * seconds + 0.5f * gravity * seconds * seconds;
            _body.linearVelocity = velocity + gravity * seconds;

            Vector3 delta = end - start;
            float distance = delta.magnitude;
            if (distance < 1e-4f)
            {
                return;
            }

            Vector3 direction = delta / distance;
            int mask = ~Definition.ExcludedLayers.value;

            if (Physics.SphereCast(start, _probeRadius, direction, out RaycastHit hit, distance, mask,
                    QueryTriggerInteraction.Ignore) &&
                hit.collider.GetComponentInParent<RemoteHitBox>() == null)
            {
                // Avatars are excluded from the query too: a hitbox standing in the path is in a
                // different place on every client and would stop the catch-up at a different spot.
                transform.position = hit.point + hit.normal * _probeRadius;
                return;
            }

            transform.position = end;
        }

        /// <summary>
        /// Excludes every avatar collider (remote hitboxes + the local body) from this item's
        /// physics. ⚠️ Remote bodies are interpolated, so they sit somewhere slightly different on
        /// every client — bouncing off one would split the copies (§6.4). A throwable collides with
        /// STATIC GEOMETRY only.
        /// </summary>
        private void IgnoreAvatarColliders()
        {
            if (_ownColliders.Length == 0)
            {
                return;
            }

            RemoteHitBox[] hitBoxes = FindObjectsByType<RemoteHitBox>(FindObjectsSortMode.None);
            for (int i = 0; i < hitBoxes.Length; i++)
            {
                if (hitBoxes[i] == null)
                {
                    continue;
                }

                IgnoreAll(hitBoxes[i].GetComponentsInChildren<Collider>(true));
            }

            LocalBodyAvatar local = LocalBodyAvatar.Instance;
            if (local != null)
            {
                IgnoreAll(local.GetComponentsInChildren<Collider>(true));
            }
        }

        private void IgnoreAll(Collider[] others)
        {
            for (int i = 0; i < others.Length; i++)
            {
                Collider other = others[i];
                if (other == null)
                {
                    continue;
                }

                for (int j = 0; j < _ownColliders.Length; j++)
                {
                    Collider own = _ownColliders[j];
                    if (own != null)
                    {
                        Physics.IgnoreCollision(own, other, true);
                    }
                }
            }
        }

        /// <summary>Sphere radius of the catch-up probe, from the item's own bounds.</summary>
        private float ResolveProbeRadius()
        {
            float radius = 0f;
            for (int i = 0; i < _ownColliders.Length; i++)
            {
                Collider own = _ownColliders[i];
                if (own == null)
                {
                    continue;
                }

                Vector3 extents = own.bounds.extents;
                radius = Mathf.Max(radius, Mathf.Min(extents.x, Mathf.Min(extents.y, extents.z)));
            }

            return Mathf.Clamp(radius, 0.02f, 0.5f);
        }
    }
}
