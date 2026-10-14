using System;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.Audio;
using VortexArena.Core.Combat;
using VortexArena.Net;

namespace VortexArena.Modes.Zombie
{
    /// <summary>The zombie's PRESENTATION on every headset: picks a body, applies the owner's streamed
    /// pose and feeds the Animator. No rules, no authority.</summary>
    /// <remarks>⚠️ <b>Look, size, eyes and the crawl come from the TYPE</b> the server put in the payload
    /// (<c>t</c>, §10.5); the remaining variety (gait set, size jitter, attack and death variant) is
    /// derived from the <c>netId</c> through <see cref="ZombieKinds"/> — a field per zombie would cost
    /// bandwidth for something both ends can compute. Either way the single rule in <c>ZombieKinds</c> is
    /// what keeps the brain's speed and this body's animation talking about the same creature.
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

        /// <summary>Death animation (or the ragdoll's fall) gets this long before the body starts to
        /// fade.
        /// <para>⚠️ <see cref="DeathDissolveDelay"/> + <see cref="DeathDissolveSeconds"/> must stay
        /// UNDER the server's despawn delay (4,5 s, protocol §10.5): otherwise the corpse pops out of
        /// existence mid-fade.</para></summary>
        private const float DeathDissolveDelay = 3.3f;

        private const float DeathDissolveSeconds = 0.8f;

        /// <summary>The ragdoll is frozen this long after death, so the dissolve runs on a settled
        /// pose instead of a body still sliding — and the budget comes back before the next wave.</summary>
        private const float RagdollFreezeSeconds = 2.6f;

        /// <summary>Delay before the blood pool is laid: the body has to be roughly where it will
        /// end up, or the pool appears next to the corpse.</summary>
        private const float BloodPoolDelay = 0.8f;

        /// <summary>A hit report older than this is not the killing shot any more — the corpse then
        /// falls with a neutral push instead of being thrown by a stale direction.</summary>
        private const float HitMemorySeconds = 0.6f;

        /// <summary>The hit report and the hp drop are two signals for ONE round (the report is local
        /// and instant, the hp comes back from the server): within this window the second one must not
        /// retrigger the flinch clip.</summary>
        private const float HitAnimDedupeSeconds = 0.3f;

        // Death impulse (N·s) per zone: a headshot throws the body, a leg shot sweeps it.
        private const float HeadImpulse = 45f;
        private const float BodyImpulse = 32f;
        private const float StomachImpulse = 30f;
        private const float LegImpulse = 25f;

        /// <summary>How far BEHIND the hit point the blood ray starts (m): it has to begin outside the
        /// hit box it is about to test, or the collider reports no hit at all.</summary>
        private const float BloodRayBackMeters = 0.25f;

        /// <summary>Flinch amplitude scale of the bosses — a body shrugged by every round reads as
        /// stunned, and the squad empties a magazine into it.</summary>
        private const float BruteFlinchScale = 0.5f;

        private const float DevFlinchScale = 0.3f;

        private const float SpawnDissolveSeconds = 1.2f;

        /// <summary>SpawnIndex of <c>ZA_Scream</c> — ⚠️ mirrors ZombieLookBuilder's spawn clip order. The
        /// scream reuses the Spawn one-shot because that state is entered from AnyState, so it is
        /// reachable in the middle of locomotion too.</summary>
        private const int SpawnIndexScream = 2;

        /// <summary>A crawler has no attack clip: its crawl is briefly sped up instead, which reads as a
        /// lunge from a body already on the ground.</summary>
        private const float CrawlAttackBoost = 2f;

        private const float CrawlAttackBoostSeconds = 0.4f;

        /// <summary>Under this measured speed (m/s) the body counts as standing: a step on a zombie that
        /// only sways in place would be heard as a stuck loop.</summary>
        private const float FootstepSpeed = 0.3f;

        /// <summary>Speed (m/s) the step interval is at its shortest from on.</summary>
        private const float FootstepFullSpeed = 2.2f;

        // Step interval (s) walking → running. The Giant's stride is longer, so its steps stay apart.
        private const float FootstepIntervalSlow = 0.9f;
        private const float FootstepIntervalFast = 0.35f;
        private const float GiantStepIntervalSlow = 1.2f;
        private const float GiantStepIntervalFast = 0.6f;

        /// <summary>Lead-in (s) given when a standing body starts moving — the first step then lands
        /// after a stride instead of on the frame it sets off.</summary>
        private const float FootstepLeadIn = 0.2f;

        [Header("Görünüşler")]
        [Tooltip("ZB_* görünüş prefabları — SIRA ÖNEMLİ: ZombieLookBuilder.LookNames sırasıyla " +
                 "aynı olmalı (Ghoul · Cop · Soldier · Clown · Girl), çünkü zombi türü hangi gövdeyle " +
                 "çıkacağını bu indekslerden okur.")]
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
        private int _screamCounter;
        private float _hp;

        /// <summary>Type from the payload (§10.5): look, size, eyes and the crawl all come from it.</summary>
        private string _type = ZombieKinds.TypeWalker;

        /// <summary>Brute and Dev take no flinch: a boss interrupted by every bullet reads as stunned,
        /// and the squad empties a magazine into it.</summary>
        private bool _noFlinch;

        private float _crawlBoostUntil;

        private float _idleFor;
        private float _nextIdleRoll;
        private int _idleIndex;

        private bool _dead;
        private float _deadAt;
        private bool _fadedOut;

        private ZombieRagdoll _ragdoll;
        private ZombieFlinch _flinch;

        private bool _ragdolled;
        private bool _ragdollFrozen;
        private bool _bloodPlaced;

        /// <summary>Last hit report for THIS body: where it landed, which way it was going and which
        /// zone it took. The death state arrives separately (and a little later), so the killing
        /// shot's direction has to be remembered to throw the corpse with it.</summary>
        private Vector3 _lastHitPoint;
        private Vector3 _lastHitDirection;
        private HitZone _lastHitZone;
        private float _lastHitAt = -99f;

        /// <summary>When the flinch CLIP was last triggered — the dedupe window's anchor.</summary>
        private float _hitAnimAt = -99f;

        private int _hitVariant;

        /// <summary>Have we ever had a pose for this zombie? Until then there is NO body at all: the
        /// server spawns it with a meaningless pose (§10.5), and a model standing at the arena origin
        /// would be visible to the whole squad.</summary>
        private bool _posed;

        // ------------------------------------------------------------------------- voice

        /// <summary>The zombie's own 3D voice (groan · attack · death · spawn). Created at runtime from
        /// <see cref="ZombieSounds"/>: an AudioSource on the prefab would need the clip pools wired per
        /// prefab, and the asset is the single place they live. Stays null with no sound data.</summary>
        private AudioSource _voice;

        /// <summary>Built on the first scream only: most bodies never scream.</summary>
        private AudioSource _screamVoice;

        private ZombieSounds _sounds;

        private float _nextGroan;
        private float _nextStep;
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

            // ⚠️ On EVERY headset, not only the shooter's: the hit report is relayed (Local == false)
            // and the flinch, the stagger and the blood have to read the same event everywhere.
            ArenaCombat.ObjectHit += HandleObjectHit;

            _attackCounter = ZombieKinds.PayloadInt(_net, ZombieKinds.PayloadAttack, -1);
            _screamCounter = ZombieKinds.PayloadInt(_net, ZombieKinds.PayloadScream);
            _hp = _net.Hp;

            // Re-enabled body: the budget dropped it on disable, and without this it would never
            // groan again.
            SetGroanRegistered(_posed && !_net.IsBroken);
        }

        private void OnDisable()
        {
            _net.StateChanged -= HandleStateChanged;
            ArenaCombat.ObjectHit -= HandleObjectHit;
            SetGroanRegistered(false);
        }

        private void HandleStateChanged(NetObject netObject, NetStateOrigin origin)
        {
            // Re-read every state: the payload can arrive after the object does, and the body is only
            // built on the first POSE — by then the type has to be known.
            _type = ZombieKinds.TypeOf(_net);

            int counter = ZombieKinds.PayloadInt(_net, ZombieKinds.PayloadAttack, _attackCounter);
            bool broken = _net.IsBroken;

            if (origin == NetStateOrigin.Live && counter > _attackCounter && !broken)
            {
                PlayAttack(counter);
                PlayVoice(AttackClip(), _sounds != null ? _sounds.AttackVolume : 0f);
            }

            _attackCounter = counter;

            int scream = ZombieKinds.PayloadInt(_net, ZombieKinds.PayloadScream, _screamCounter);
            if (origin == NetStateOrigin.Live && scream > _screamCounter && !broken)
            {
                PlayScream();
            }

            _screamCounter = scream;

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
                    PlayVoice(DeathClip(), _sounds != null ? _sounds.DeathVolume : 0f);
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
                UpdateCorpse();
                return;
            }

            DriveGait(dt);
            UpdateGroan();
            UpdateFootsteps();
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

            // ⚠️ A corpse is never posed again: the ragdoll owns the body from here on and writing the
            // root pose on top of it drags the whole skeleton back under the agent's last position.
            // Only a body that has NEVER been posed still needs one (a late joiner's dead snapshot).
            if (_dead && _posed)
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
            _type = ZombieKinds.TypeOf(_net);

            ZombieLook prefab = looks[ZombieKinds.LookIndexOf(_type, netId, looks.Length)];
            if (prefab == null)
            {
                Debug.LogWarning("[Zombie] Görünüş listesinde boş giriş var — zombi gövdesiz kalıyor. " +
                                 "Tools > VortexArena > Zombie > Zombi görünüşlerini üret menüsünü koştur.", this);
                return;
            }

            _look = Instantiate(prefab, transform);
            _look.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            // The type's size multiplies the look's own normalising scale, it does not replace it: the
            // five models arrive at different heights.
            _look.transform.localScale *= ZombieKinds.ScaleOf(_type, netId);
            _look.SetEyeColor(ZombieKinds.EyeColorOf(_type));

            _animator = _look.Animator;
            _crawler = ZombieKinds.IsCrawlerType(_type);
            _noFlinch = _type == ZombieKinds.TypeBrute || _type == ZombieKinds.TypeDev;
            _locoSet = ZombieKinds.LocoSet(netId);
            _nominalWalk = _look.WalkSpeed(_locoSet);

            _ragdoll = _look.GetComponent<ZombieRagdoll>();
            _flinch = _look.GetComponent<ZombieFlinch>();

            if (_flinch != null)
            {
                _flinch.Configure(_crawler, FlinchScaleOf(_type));
            }

            if (_animator != null)
            {
                _animator.SetInteger(LocoSetParam, _locoSet);
                _animator.SetBool(CrawlerParam, _crawler);
            }
        }

        private static float FlinchScaleOf(string type)
        {
            if (type == ZombieKinds.TypeDev)
            {
                return DevFlinchScale;
            }

            return type == ZombieKinds.TypeBrute ? BruteFlinchScale : 1f;
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

            if (_crawler && Time.time < _crawlBoostUntil)
            {
                rate *= CrawlAttackBoost;
            }

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
        /// it would stand the body up for half a second — it gets a short crawl SPEED-UP instead.</summary>
        private void PlayAttack(int counter)
        {
            if (_animator == null || _dead)
            {
                return;
            }

            if (_crawler)
            {
                _crawlBoostUntil = Time.time + CrawlAttackBoostSeconds;
                return;
            }

            _animator.SetInteger(AttackIndexParam, Mathf.Abs(counter + _net.NetId) % AttackVariants);
            _animator.SetTrigger(AttackParam);
        }

        /// <summary>Upper-body flinch from the HP DROP. ⚠️ The second of the two signals for one round:
        /// the hit report usually got here first and already played it, so a fresh one is skipped —
        /// otherwise every round restarts the clip twice and the arms never finish a reaction.</summary>
        private void PlayHit(float hp)
        {
            if (Time.time - _hitAnimAt < HitAnimDedupeSeconds)
            {
                return;
            }

            // The variant follows the remaining hp, so it alternates without a random draw that would
            // differ per headset.
            TriggerHitClip(Mathf.Max(0, Mathf.RoundToInt(hp)) % 2);
        }

        private void TriggerHitClip(int variant)
        {
            if (_animator == null || _crawler || _dead || _noFlinch)
            {
                return;
            }

            _hitAnimAt = Time.time;
            _animator.SetInteger(HitIndexParam, variant);
            _animator.SetTrigger(HitParam);
        }

        // ------------------------------------------------------------------------- hit reports

        /// <summary>A round landed on a network object: the flinch, the blood and the corpse's push
        /// all hang off this. Fires on EVERY headset — the shooter's immediately
        /// (<c>Local</c>), the others through the server's relay.</summary>
        private void HandleObjectHit(ArenaCombat.ObjectHitInfo info)
        {
            if (info.NetId != _net.NetId || info.NetId <= 0)
            {
                return;
            }

            _lastHitPoint = info.Point;
            _lastHitDirection = info.Direction;
            _lastHitZone = (HitZone)info.Zone;
            _lastHitAt = Time.time;

            if (!_dead)
            {
                if (_flinch != null)
                {
                    _flinch.Hit(_lastHitZone, info.Direction, info.Point);
                }

                TriggerHitClip(_hitVariant++ & 1);
            }

            // The shooter already played their own impact locally (ArenaCombat.ReportImpact); this is
            // what gives the REST of the squad blood on the body they are watching.
            if (!info.Local && (!_dead || Time.time - _deadAt < 0.5f))
            {
                PlayRemoteBlood(info);
            }
        }

        /// <summary>Impact FX for a hit somebody else reported. The surface comes from the hit box the
        /// ray crosses, so the head bleeds differently from the body — exactly as on the shooter's
        /// headset.</summary>
        /// <remarks>⚠️ Tested against THIS body's colliders one by one, not with a scene raycast: a
        /// scene ray would stop on the wall behind, on another zombie, or on the shooter's own hand,
        /// and the blood would appear on the wrong thing.</remarks>
        private void PlayRemoteBlood(in ArenaCombat.ObjectHitInfo info)
        {
            Collider[] parts = _ragdoll != null ? _ragdoll.Colliders : null;
            if (parts == null)
            {
                return;
            }

            Vector3 direction = info.Direction;
            if (direction.sqrMagnitude < 1e-6f)
            {
                // No direction on the wire: aim at the body's middle, which is close enough for a
                // surface lookup.
                Transform hips = _ragdoll.Hips;
                direction = (hips != null ? hips.position : transform.position) - info.Point;
            }

            if (direction.sqrMagnitude < 1e-6f)
            {
                return;
            }

            direction.Normalize();
            var ray = new Ray(info.Point - direction * BloodRayBackMeters, direction);

            float best = float.MaxValue;
            RaycastHit bestHit = default;
            bool found = false;

            for (int i = 0; i < parts.Length; i++)
            {
                Collider part = parts[i];
                if (part == null || !part.enabled || !part.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (part.Raycast(ray, out RaycastHit hit, BloodRayBackMeters * 2f) &&
                    hit.distance < best)
                {
                    best = hit.distance;
                    bestHit = hit;
                    found = true;
                }
            }

            if (found)
            {
                ArenaCombat.ReportImpact(bestHit);
            }
        }

        /// <summary>Scream of a screamer or of the Dev (payload <c>c</c>): the <c>ZA_Scream</c> variant of
        /// the Spawn one-shot — that state is entered from AnyState, so it cuts into locomotion — plus a
        /// cry that carries much further than the usual voice.</summary>
        private void PlayScream()
        {
            if (_dead)
            {
                return;
            }

            if (_animator != null && !_crawler)
            {
                _animator.SetInteger(SpawnIndexParam, SpawnIndexScream);
                _animator.SetTrigger(SpawnParam);
            }

            PlayScreamVoice();
        }

        private void ApplyDeath()
        {
            _dead = true;
            _deadAt = Time.time;

            if (_flinch != null)
            {
                // The spring would keep bending bones the ragdoll solver owns from here on.
                _flinch.enabled = false;
            }

            if (TryCollapse())
            {
                return;
            }

            if (_look != null)
            {
                // Hit boxes go down with the first broken state: a corpse that still reports hits lets
                // the squad farm score off it for the whole despawn delay. (The ragdoll path does this
                // with the layer instead — those colliders have to stay on to carry the body.)
                _look.SetHitboxesActive(false);
            }

            if (_animator == null)
            {
                return;
            }

            if (_crawler)
            {
                // ⚠️ A crawler NEVER plays the standing death clip: it is already on the ground and the
                // clip stands it up to drop it again. Its current pose is frozen instead.
                _animator.speed = 0f;
                return;
            }

            _animator.SetInteger(DeathIndexParam, _net.NetId % 2);
            _animator.SetBool(DeadParam, true);
        }

        /// <summary>Hands the corpse to physics and throws it along the killing shot. <c>false</c> =
        /// the animated death runs instead (budget spent, no ragdoll on this body, or the Dev, whose
        /// end is staged).</summary>
        private bool TryCollapse()
        {
            if (_ragdoll == null || _type == ZombieKinds.TypeDev || !ZombieRagdoll.HasBudget)
            {
                return false;
            }

            bool fresh = Time.time - _lastHitAt < HitMemorySeconds;
            Vector3 direction = fresh ? _lastHitDirection : Vector3.zero;

            if (direction.sqrMagnitude < 1e-6f)
            {
                // No usable shot (blast, bleed-out, a late joiner's corpse): a small shove backwards,
                // which at least drops the body away from the squad instead of straight down.
                direction = -transform.forward * 0.6f + Vector3.up * 0.35f;
            }

            Transform hips = _ragdoll.Hips;
            Vector3 point = fresh
                ? _lastHitPoint
                : hips != null ? hips.position : transform.position;

            float impulse = fresh ? ImpulseOf(_lastHitZone) : BodyImpulse * 0.4f;

            if (!_ragdoll.Collapse(point, direction.normalized, impulse))
            {
                return false;
            }

            _ragdolled = true;
            return true;
        }

        private static float ImpulseOf(HitZone zone) => zone switch
        {
            HitZone.Head => HeadImpulse,
            HitZone.Stomach => StomachImpulse,
            HitZone.Leg => LegImpulse,
            _ => BodyImpulse,
        };

        /// <summary>The corpse's own clock: settle, bleed, fade. The server despawns the object
        /// itself — all of this only has to finish BEFORE it does.</summary>
        private void UpdateCorpse()
        {
            float since = Time.time - _deadAt;

            if (_ragdolled && !_ragdollFrozen && since >= RagdollFreezeSeconds)
            {
                _ragdollFrozen = true;
                _ragdoll.Freeze();
            }

            if (!_bloodPlaced && since >= BloodPoolDelay)
            {
                _bloodPlaced = true;

                Transform hips = _ragdoll != null ? _ragdoll.Hips : null;
                ZombieBloodPool.Shared.Place(hips != null ? hips.position : transform.position);
            }

            FadeOutCorpse(since);
        }

        private void FadeOutCorpse(float since)
        {
            if (_fadedOut || since < DeathDissolveDelay)
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
            // Below the default 128: a late wave has dozens of bodies, and Unity culls the lowest
            // priority virtual sources first.
            _voice.priority = 100;
        }

        /// <summary>The scream has its OWN source: it must be heard across the arena
        /// (<see cref="ZombieSounds.ScreamMaxDistance"/>) while the groan rolls off in a few metres, and
        /// one source cannot carry both distance curves.</summary>
        private void PlayScreamVoice()
        {
            if (_sounds == null)
            {
                return;
            }

            // The Dev roars, the screamer cries: one pool for both would give the boss the same voice
            // as the body the squad learned to shoot on sight.
            AudioClip clip = _type == ZombieKinds.TypeDev
                ? _sounds.RandomGiantRoar()
                : _sounds.RandomScream();
            if (clip == null)
            {
                return;
            }

            float channel = AudioMix.Of(AudioChannel.Ambience);
            if (channel <= 0f)
            {
                return;
            }

            if (_screamVoice == null)
            {
                _screamVoice = gameObject.AddComponent<AudioSource>();
                _screamVoice.playOnAwake = false;
                _screamVoice.spatialBlend = 1f;
                _screamVoice.rolloffMode = AudioRolloffMode.Logarithmic;
                _screamVoice.minDistance = _sounds.ScreamMinDistance;
                _screamVoice.maxDistance = _sounds.ScreamMaxDistance;
                // Above the voices and the music: the scream is the warning, it may never be the
                // source Unity culls.
                _screamVoice.priority = 40;
            }

            float jitter = _sounds.PitchJitter;
            _screamVoice.pitch = 1f + UnityEngine.Random.Range(-jitter, jitter);
            // Clamp to 2, not 1: PlayOneShot's volumeScale amplifies above unity gain.
            _screamVoice.PlayOneShot(clip, Mathf.Clamp(_sounds.ScreamVolume * channel, 0f, 2f));
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
            PlayVoice(GroanClip(), _sounds.GroanVolume);
        }

        private AudioClip GroanClip() =>
            _type == ZombieKinds.TypeRunner ? _sounds.RandomRunnerGroan() : _sounds.RandomGroan();

        private AudioClip AttackClip()
        {
            if (_sounds == null)
            {
                return null;
            }

            return _type == ZombieKinds.TypeDev ? _sounds.RandomGiantSlam() : _sounds.RandomAttack();
        }

        private AudioClip DeathClip()
        {
            if (_sounds == null)
            {
                return null;
            }

            return _type == ZombieKinds.TypeDev ? _sounds.RandomGiantDeath() : _sounds.RandomDeath();
        }

        /// <summary>Steps while the body WALKS, paced from the measured speed. Optional: with no step
        /// clips nothing is played and nothing is warned about.</summary>
        /// <remarks>⚠️ Shares the groan budget (the nearest N bodies) and the voice's own earshot: a
        /// late wave has dozens of walkers, and steps from across the arena would both drone and steal
        /// virtual sources from the near ones.</remarks>
        private void UpdateFootsteps()
        {
            if (_voice == null || !_posed || _dead || _sounds == null || !_sounds.HasFootsteps)
            {
                return;
            }

            if (_speed < FootstepSpeed)
            {
                _nextStep = Time.time + FootstepLeadIn;
                return;
            }

            if (Time.time < _nextStep)
            {
                return;
            }

            bool giant = _type == ZombieKinds.TypeDev;
            float pace = Mathf.InverseLerp(FootstepSpeed, FootstepFullSpeed, _speed);
            _nextStep = Time.time + Mathf.Lerp(
                giant ? GiantStepIntervalSlow : FootstepIntervalSlow,
                giant ? GiantStepIntervalFast : FootstepIntervalFast,
                pace);

            if (!ZombieGroanBudget.MayGroan(this, _sounds.MaxGroaners) || !InEarshot())
            {
                return;
            }

            PlayVoice(giant ? _sounds.RandomGiantStep() : _sounds.RandomFootstep(),
                _sounds.FootstepVolume);
        }

        /// <summary>Is the listener inside the voice's roll-off? Past it the step is inaudible but would
        /// still cost a voice.</summary>
        private bool InEarshot()
        {
            Camera listener = Camera.main;
            if (listener == null)
            {
                return false;
            }

            float range = _sounds.MaxDistance;
            return (listener.transform.position - transform.position).sqrMagnitude <= range * range;
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
            _voice.PlayOneShot(clip, Mathf.Clamp(volume * channel, 0f, 2f));
        }
    }
}
