using System;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.Audio;
using VortexArena.Net;

namespace VortexArena.Modes.Zombie
{
    /// <summary>The zombie's PRESENTATION on every headset: picks a body, applies the owner's streamed
    /// pose and feeds the Animator. No rules, no authority.</summary>
    /// <remarks>⚠️ <b>Every choice is derived from the <c>netId</c></b> (look · crawler · gait set ·
    /// size · attack and death variant) through <see cref="ZombieKinds"/>. The protocol carries none of
    /// it: a field per zombie would cost bandwidth for something both ends can compute, and the single
    /// rule in <c>ZombieKinds</c> is what keeps the brain's speed and this body's animation talking
    /// about the same creature.
    /// <para>⚠️ The gait comes from the MEASURED pose delta, not from a network field: walking is then
    /// in step with the real movement on every headset, owner included, and needs no extra
    /// stream.</para>
    /// <para>⚠️ The attack is played from the payload's attack counter
    /// (<see cref="ZombieKinds.PayloadAttack"/>) and only on a <see cref="NetStateOrigin.Live"/> state:
    /// a late joiner's snapshot carries the same counter, and reacting to it would make every zombie
    /// swing at the moment they walk in.</para>
    /// <para>⚠️ When the stream stops the LAST pose is kept, never the origin: a dead or temporarily
    /// brainless zombie must stay where it was, not jump to the arena's centre.</para></remarks>
    [RequireComponent(typeof(NetObject))]
    [DisallowMultipleComponent]
    public class ZombieView : MonoBehaviour
    {
        // ------------------------------------------------------------------- animator vocabulary

        // ⚠️ Mirrors ZombieAnimator.controller one to one (ZombieLookBuilder writes both). A typo here
        // is silent: Animator.SetFloat on an unknown name does nothing and the zombie just stands.
        private static readonly int SpeedParam = Animator.StringToHash("Speed");
        private static readonly int LocoSetParam = Animator.StringToHash("LocoSet");
        private static readonly int LocoRateParam = Animator.StringToHash("LocoRate");
        private static readonly int CrawlerParam = Animator.StringToHash("Crawler");
        private static readonly int SpawnParam = Animator.StringToHash("Spawn");
        private static readonly int SpawnIndexParam = Animator.StringToHash("SpawnIndex");
        private static readonly int AttackParam = Animator.StringToHash("Attack");
        private static readonly int AttackIndexParam = Animator.StringToHash("AttackIndex");
        private static readonly int HitParam = Animator.StringToHash("Hit");
        private static readonly int HitIndexParam = Animator.StringToHash("HitIndex");
        private static readonly int DeadParam = Animator.StringToHash("Dead");
        private static readonly int DeathIndexParam = Animator.StringToHash("DeathIndex");
        private static readonly int IdleIndexParam = Animator.StringToHash("IdleIndex");

        /// <summary>Attack clip count in the controller — the variant is <c>(a + netId) % this</c>.</summary>
        private const int AttackVariants = 5;

        private const int SpawnVariants = 3;

        // ------------------------------------------------------------------- tuning constants

        /// <summary>A pose jump over this (m) is a TELEPORT, not movement: the brain relocates a zombie
        /// between storeys, and both the interpolation and the gait have to stop sliding through it.</summary>
        private const float SnapDistance = 1.5f;

        /// <summary>Below this the body counts as standing — the idle variants are allowed to run.</summary>
        private const float IdleSpeed = 0.12f;

        /// <summary>Damping (s) on the Animator's Speed and LocoRate: the pose stream is jittery, and
        /// undamped values make the blend tree and the clip rate jump frame to frame.</summary>
        private const float SpeedDamp = 0.15f;

        private const float RateDamp = 0.25f;

        // IdleIndex values — ⚠️ mirror ZombieLookBuilder.BuildBaseLayer.
        private const int IdleBase = 0;
        private const int IdleScratch = 1;
        private const int IdleAgonizing = 2;
        private const int IdleAltA = 3;
        private const int IdleAltB = 4;

        /// <summary>Ground speed of the run clips (m/s) after the look's normalising scale.</summary>
        private const float RunClipSpeed = 1.1f;

        /// <summary>Ground speed of the crawl clip (m/s). Measured loosely — the crawl barely moves, so
        /// its rate is clamped wide.</summary>
        private const float CrawlClipSpeed = 0.25f;

        /// <summary>Death animation gets this long before the body starts to fade.
        /// <para>⚠️ <see cref="DeathDissolveDelay"/> + <see cref="DeathDissolveSeconds"/> must stay
        /// UNDER the server's despawn delay (3 s, protocol §10.5): otherwise the corpse pops out of
        /// existence mid-fade.</para></summary>
        private const float DeathDissolveDelay = 2.2f;

        private const float DeathDissolveSeconds = 0.8f;

        private const float SpawnDissolveSeconds = 1.2f;

        [Header("Görünüşler")]
        [Tooltip("ZB_* görünüş prefabları — hangisinin çıkacağı netId'den gelir, sıra ÖNEMLİ değil " +
                 "ama listeden eleman çıkarmak sahadaki zombilerin görünüşünü kaydırır.")]
        [SerializeField] private ZombieLook[] looks;

        [Header("Belirme efekti")]
        [Tooltip("Belirme/kat değiştirme dumanı — playOnAwake kapalı, Stop Action None.")]
        [SerializeField] private ParticleSystem spawnFog;

        /// <summary>The body was teleported to the position carried here.</summary>
        public event Action<Vector3> Relocated;

        private NetObject _net;

        private ZombieLook _look;
        private Animator _animator;
        private bool _crawler;
        private int _locoSet;
        private float _nominalWalk = 0.25f;

        private Vector3 _lastPosition;
        private float _speed;

        private int _attackCounter = -1;
        private float _hp;

        private float _idleFor;
        private float _nextIdleRoll;
        private int _idleIndex;

        private bool _dead;
        private float _deadAt;
        private bool _fadedOut;

        /// <summary>Have we ever had a pose for this zombie? Until then there is NO body at all: the
        /// server spawns it with a meaningless pose (§10.5), and a model standing at the arena origin
        /// would be visible to the whole squad.</summary>
        private bool _posed;

        // ------------------------------------------------------------------------- voice

        /// <summary>The zombie's own 3D voice (groan · attack · death · spawn). Created at runtime from
        /// <see cref="ZombieSounds"/>: an AudioSource on the prefab would need the clip pools wired per
        /// prefab, and the asset is the single place they live. Stays null with no sound data.</summary>
        private AudioSource _voice;

        private ZombieSounds _sounds;

        private float _nextGroan;
        private bool _deathPlayed;
        private bool _groanRegistered;

        private void Awake()
        {
            _net = GetComponent<NetObject>();
            _lastPosition = transform.position;

            BuildVoice();
        }

        private void OnEnable()
        {
            _net.StateChanged += HandleStateChanged;
            _attackCounter = ZombieKinds.PayloadInt(_net, ZombieKinds.PayloadAttack, -1);
            _hp = _net.Hp;

            // Re-enabled body: the budget dropped it on disable, and without this it would never
            // groan again.
            SetGroanRegistered(_posed && !_net.IsBroken);
        }

        private void OnDisable()
        {
            _net.StateChanged -= HandleStateChanged;
            SetGroanRegistered(false);
        }

        private void HandleStateChanged(NetObject netObject, NetStateOrigin origin)
        {
            int counter = ZombieKinds.PayloadInt(_net, ZombieKinds.PayloadAttack, _attackCounter);
            bool broken = _net.IsBroken;

            if (origin == NetStateOrigin.Live && counter > _attackCounter && !broken)
            {
                PlayAttack(counter);
                PlayVoice(_sounds != null ? _sounds.RandomAttack() : null,
                    _sounds != null ? _sounds.AttackVolume : 0f);
            }

            _attackCounter = counter;

            // Damage is the ONLY hit signal every headset has: object_state carries hp to all of them
            // (NetObject.ApplyState), while the shooter's hit report is local to one visor.
            if (origin == NetStateOrigin.Live && _net.Hp < _hp - 0.01f && !broken)
            {
                PlayHit(_net.Hp);
            }

            _hp = _net.Hp;

            if (broken && !_deathPlayed)
            {
                _deathPlayed = true;
                SetGroanRegistered(false);

                // ⚠️ Only on a Live state, like the attack: a late joiner's snapshot of an already dead
                // zombie would otherwise scream a death cry the moment the arena appears.
                if (origin == NetStateOrigin.Live)
                {
                    PlayVoice(_sounds != null ? _sounds.RandomDeath() : null,
                        _sounds != null ? _sounds.DeathVolume : 0f);
                }
            }

            if (broken && !_dead)
            {
                ApplyDeath();
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            ApplyStream();
            MeasureSpeed(dt);

            if (_look == null)
            {
                return;
            }

            if (_dead)
            {
                FadeOutCorpse();
                return;
            }

            DriveGait(dt);
            UpdateGroan();
        }

        /// <summary>Puts the body on the owner's interpolated pose. ⚠️ Not applied on the OWNER's machine:
        /// there the brain's agent drives the transform and the registry refuses the pose anyway
        /// (<see cref="RemoteObjectRegistry.TryGetInterpolatedPose"/>).</summary>
        private void ApplyStream()
        {
            if (_net.NetId <= 0)
            {
                return;
            }

            if (_net.IsMine)
            {
                MarkPosed();
                return;
            }

            RemoteObjectRegistry streams = ArenaClient.Instance != null
                ? ArenaClient.Instance.RemoteObjects
                : null;
            if (streams == null ||
                !streams.TryGetInterpolatedPose(_net.NetId, out Pose arenaPose, SnapDistance))
            {
                return;
            }

            Pose worldPose = ArenaSpace.ArenaToWorld(arenaPose);
            transform.SetPositionAndRotation(worldPose.position, worldPose.rotation);

            if (!_posed)
            {
                _lastPosition = worldPose.position;
                MarkPosed();
                return;
            }

            if ((worldPose.position - _lastPosition).sqrMagnitude > SnapDistance * SnapDistance)
            {
                // ⚠️ The baseline is moved up with the jump: otherwise MeasureSpeed reads the teleport as
                // a sprint and the gait winds up to full stride for a moment.
                _lastPosition = worldPose.position;
                Relocated?.Invoke(worldPose.position);

                // The zombie walked in from another storey: the same fog and voice as appearing, so it
                // is seen and heard arriving instead of materialising silently behind the squad.
                PlaySpawnEffect();
                PlaySpawnVoice();
            }
        }

        /// <summary>Walking speed from the pose DELTA — the one measure that exists on every headset
        /// (the agent lives only on the owner's).</summary>
        private void MeasureSpeed(float dt)
        {
            Vector3 position = transform.position;
            float raw = dt > 0f ? Vector3.Distance(position, _lastPosition) / dt : 0f;
            _lastPosition = position;

            // Smoothed: the stream arrives at OBJECT_POSE_RATE_HZ while this runs per frame, so the raw
            // delta is zero on most frames and the gait would stutter.
            _speed = Mathf.Lerp(_speed, raw, 1f - Mathf.Exp(-8f * dt));
        }

        // ------------------------------------------------------------------------- body

        /// <summary>Instantiates the body, once. ⚠️ Not in <c>Awake</c>: every choice needs the
        /// <c>netId</c>, which is only bound when the server's state arrives.</summary>
        private void EnsureLook()
        {
            if (_look != null || looks == null || looks.Length == 0)
            {
                return;
            }

            int netId = _net.NetId;
            ZombieLook prefab = looks[ZombieKinds.LookIndex(netId, looks.Length)];
            if (prefab == null)
            {
                Debug.LogWarning("[Zombie] Görünüş listesinde boş giriş var — zombi gövdesiz kalıyor. " +
                                 "Tools > VortexArena > Zombie > Zombi görünüşlerini üret menüsünü koştur.", this);
                return;
            }

            _look = Instantiate(prefab, transform);
            _look.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            // Size jitter multiplies the look's own normalising scale, it does not replace it: the
            // five models arrive at different heights.
            _look.transform.localScale *= ZombieKinds.ScaleJitter(netId);

            _animator = _look.Animator;
            _crawler = ZombieKinds.IsCrawler(netId);
            _locoSet = ZombieKinds.LocoSet(netId);
            _nominalWalk = _look.WalkSpeed(_locoSet);

            if (_animator != null)
            {
                _animator.SetInteger(LocoSetParam, _locoSet);
                _animator.SetBool(CrawlerParam, _crawler);
            }
        }

        /// <summary>First pose: the body appears — and becomes audible. The groan phase starts at a
        /// RANDOM point so a whole wave does not breathe in unison.</summary>
        private void MarkPosed()
        {
            if (_posed)
            {
                return;
            }

            _posed = true;
            EnsureLook();

            if (_net.IsBroken)
            {
                // Already dead when we first saw it (late joiner snapshot): no fog, no cry, no
                // spawn animation — just a corpse where it fell.
                _deathPlayed = true;
                ApplyDeath();
                return;
            }

            _nextGroan = Time.time + UnityEngine.Random.Range(0f, GroanInterval());
            SetGroanRegistered(true);
            PlaySpawnEffect();
            PlaySpawnVoice();
        }

        /// <summary>Fog + dissolve-in + the stand-up animation. ⚠️ A crawler gets no stand-up: it is
        /// already on the ground and the clip would stand it up only to drop it again.</summary>
        private void PlaySpawnEffect()
        {
            if (_look == null || _dead)
            {
                return;
            }

            if (spawnFog != null)
            {
                spawnFog.Play(true);
            }

            if (_look.Dissolve != null)
            {
                _look.Dissolve.Appear(SpawnDissolveSeconds);
            }

            if (_animator != null && !_crawler)
            {
                _animator.SetInteger(SpawnIndexParam, _net.NetId % SpawnVariants);
                _animator.SetTrigger(SpawnParam);
            }
        }

        /// <summary>Feeds the Animator: planar speed, the clip rate that keeps the feet from skating,
        /// and the idle variant.</summary>
        private void DriveGait(float dt)
        {
            if (_animator == null)
            {
                return;
            }

            _animator.SetFloat(SpeedParam, _speed, SpeedDamp, dt);

            // LocoRate stretches the CLIP to the real speed: the blend tree alone would walk a 0.2 m/s
            // cycle while the agent moves at 1.2 m/s, which reads as ice skating. ⚠️ Must stay
            // continuous in speed: between the walk and run thresholds the blend already moves at
            // the real speed (rate 1); a hard walk/run switch made the stride jump 3x mid-step.
            float rate = _crawler
                ? Mathf.Clamp(_speed / CrawlClipSpeed, 0.5f, 2.5f)
                : Mathf.Clamp(_speed / Mathf.Clamp(_speed, _nominalWalk, RunClipSpeed), 0.6f, 1.8f);
            _animator.SetFloat(LocoRateParam, rate, RateDamp, dt);

            UpdateIdleVariant(dt);
        }

        /// <summary>Picks an idle variant while the body is standing still. The scratch and the
        /// agonising loop exist for the zombies the brain has left WANDERING an empty storey — standing
        /// still is the only signal of that available on a remote headset.</summary>
        private void UpdateIdleVariant(float dt)
        {
            if (_speed > IdleSpeed || _crawler)
            {
                _idleFor = 0f;

                // ⚠️ A short wander stop shows the plain idle: rolling on the first frame of every
                // stop made the same scratch follow every few steps.
                _nextIdleRoll = UnityEngine.Random.Range(2.5f, 5f);
                SetIdleIndex(IdleBase);
                return;
            }

            _idleFor += dt;
            if (_idleFor < _nextIdleRoll)
            {
                return;
            }

            // Drawn interval, so a row of idle zombies does not change pose in unison.
            _nextIdleRoll = _idleFor + UnityEngine.Random.Range(5f, 10f);
            SetIdleIndex(DrawIdleVariant());
        }

        /// <summary>Weighted draw that never repeats the current variant.</summary>
        private int DrawIdleVariant()
        {
            // The other base idle: Loco_B stands on Idle_B, the other two sets on Idle_A.
            int alt = _locoSet == 1 ? IdleAltA : IdleAltB;

            for (int attempt = 0; attempt < 4; attempt++)
            {
                float draw = UnityEngine.Random.value;
                int pick = draw < 0.3f ? IdleBase
                    : draw < 0.6f ? alt
                    : draw < 0.8f ? IdleAgonizing
                    : IdleScratch;

                if (pick != _idleIndex)
                {
                    return pick;
                }
            }

            return _idleIndex == IdleBase ? alt : IdleBase;
        }

        private void SetIdleIndex(int index)
        {
            if (_idleIndex == index || _animator == null)
            {
                return;
            }

            _idleIndex = index;
            _animator.SetInteger(IdleIndexParam, index);
        }

        /// <summary>⚠️ No attack animation for a crawler: the crawl is a full-body loop and cutting into
        /// it would stand the body up for half a second.</summary>
        private void PlayAttack(int counter)
        {
            if (_animator == null || _crawler || _dead)
            {
                return;
            }

            _animator.SetInteger(AttackIndexParam, Mathf.Abs(counter + _net.NetId) % AttackVariants);
            _animator.SetTrigger(AttackParam);
        }

        /// <summary>Upper-body flinch. The variant follows the remaining hp, so it alternates without a
        /// random draw that would differ per headset.</summary>
        private void PlayHit(float hp)
        {
            if (_animator == null || _crawler || _dead)
            {
                return;
            }

            _animator.SetInteger(HitIndexParam, Mathf.Max(0, Mathf.RoundToInt(hp)) % 2);
            _animator.SetTrigger(HitParam);
        }

        private void ApplyDeath()
        {
            _dead = true;
            _deadAt = Time.time;

            if (_look != null)
            {
                // Hit boxes go down with the first broken state: a corpse that still reports hits lets
                // the squad farm score off it for the whole despawn delay.
                _look.SetHitboxesActive(false);
            }

            if (_animator == null)
            {
                return;
            }

            // Crawler always takes index 0: one variant is enough for a body already on the ground.
            _animator.SetInteger(DeathIndexParam, _crawler ? 0 : _net.NetId % 2);
            _animator.SetBool(DeadParam, true);
        }

        /// <summary>Starts the fade once the death animation has played. The server despawns the object
        /// itself — this only has to finish BEFORE it does.</summary>
        private void FadeOutCorpse()
        {
            if (_fadedOut || Time.time - _deadAt < DeathDissolveDelay)
            {
                return;
            }

            _fadedOut = true;

            if (_look.Dissolve != null)
            {
                _look.Dissolve.Vanish(DeathDissolveSeconds);
            }
        }

        // ------------------------------------------------------------------------- voice

        /// <summary>⚠️ On EVERY headset, not only the brain's: the voice is presentation, and a sound
        /// that only the object host hears is a zombie nobody else can locate.</summary>
        private void BuildVoice()
        {
            _sounds = ZombieSounds.Live;
            if (_sounds == null)
            {
                return;
            }

            _voice = gameObject.AddComponent<AudioSource>();
            _voice.playOnAwake = false;
            _voice.spatialBlend = 1f;
            _voice.rolloffMode = AudioRolloffMode.Logarithmic;
            _voice.minDistance = _sounds.MinDistance;
            _voice.maxDistance = _sounds.MaxDistance;
        }

        private void PlaySpawnVoice()
        {
            if (_net.IsBroken)
            {
                return;
            }

            PlayVoice(_sounds != null ? _sounds.RandomSpawn() : null,
                _sounds != null ? _sounds.SpawnVolume : 0f);
        }

        private void UpdateGroan()
        {
            if (_voice == null || !_posed || Time.time < _nextGroan)
            {
                return;
            }

            // Not one of the nearest → wait a short while instead of the full interval: the player
            // walks, and the budget is re-ranked twice a second.
            if (!ZombieGroanBudget.MayGroan(this, _sounds.MaxGroaners))
            {
                _nextGroan = Time.time + 1f;
                return;
            }

            _nextGroan = Time.time + GroanInterval();
            PlayVoice(_sounds.RandomGroan(), _sounds.GroanVolume);
        }

        private float GroanInterval() =>
            _sounds != null ? UnityEngine.Random.Range(_sounds.GroanIntervalMin, _sounds.GroanIntervalMax) : 10f;

        private void SetGroanRegistered(bool wanted)
        {
            if (_groanRegistered == wanted)
            {
                return;
            }

            _groanRegistered = wanted;

            if (wanted)
            {
                ZombieGroanBudget.Register(this);
            }
            else
            {
                ZombieGroanBudget.Unregister(this);
            }
        }

        /// <summary>One shot through the zombie's own source. The channel level is read AT PLAYBACK —
        /// the operator may have pulled the ambience channel down mid-wave.</summary>
        private void PlayVoice(AudioClip clip, float volume)
        {
            if (_voice == null || clip == null)
            {
                return;
            }

            float channel = AudioMix.Of(AudioChannel.Ambience);
            if (channel <= 0f)
            {
                return;
            }

            float jitter = _sounds != null ? _sounds.PitchJitter : 0f;
            _voice.pitch = 1f + UnityEngine.Random.Range(-jitter, jitter);

            // PlayOneShot: a groan may still be sounding when the zombie is hit, and the two must
            // overlap rather than cut each other off.
            _voice.PlayOneShot(clip, Mathf.Clamp01(volume * channel));
        }
    }
}
