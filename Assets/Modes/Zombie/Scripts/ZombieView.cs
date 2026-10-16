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
    public class ZombieView : MonoBehaviour, IZombieFeelTarget
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

        /// <summary>How far BEHIND the hit point the blood ray starts (m): it has to begin outside the
        /// hit box it is about to test, or the collider reports no hit at all.</summary>
        private const float BloodRayBackMeters = 0.25f;

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

        /// <summary>Was the last report OUR shot — the one thing that decides whether this kill is the
        /// local player's.</summary>
        private bool _lastHitLocal;

        /// <summary>Hit box the last LOCAL round struck; <c>null</c> on a relayed hit (the wire does not
        /// carry it) and the nearest box is used instead.</summary>
        private Collider _lastHitCollider;

        private ZombieReactionTuning _tuning;

        private ZombieGore _gore;

        // ----------------------------------------------------------------- kill prediction

        /// <summary>Health as the SHOOTER's own damage reports see it: the server's hp is authority, but
        /// it comes back a round trip late, and a kill that waits for it reads as a sponge.
        /// <para>⚠️ Only ever DECREASES (a zombie never heals): the server's number is taken as a
        /// ceiling, so a late state cannot resurrect a body we already dropped.</para></summary>
        private float _predictedHp;

        /// <summary>Has a state with real health arrived yet? Until then a local hit must not predict a
        /// death off <c>hp = 0</c>, which is what an unbound object reports.</summary>
        private bool _predictedReady;

        /// <summary>This death started from the local prediction and the server has not confirmed it
        /// yet — the window in which it may still have to be taken back.</summary>
        private bool _predictedDeath;

        private float _predictedAt;

        /// <summary>Damage counted on this body in ONE frame (local reports + the hp drops we are told
        /// about): a shotgun's pellets are nine separate reports in the same frame and the corpse has to
        /// be thrown by the whole pattern.</summary>
        private float _frameDamage;
        private int _frameDamageFrame = -1;

        /// <summary>Last health above zero — the observers' stand-in for the killing frame's damage
        /// (they never see a damage number).</summary>
        private float _hpBeforeDeath;

        /// <summary>Hit CLIP cooldown anchor: the upper-body reaction must not restart on every round of
        /// a full-auto burst (the flinch spring still takes all of them).</summary>
        private float _hitClipAt = -99f;

        private bool _killRaised;

        // ----------------------------------------------------------------- hit stop

        private float _hitStopUntil;
        private bool _hitStopActive;

        // ----------------------------------------------------------------- crippling

        private bool _crippled;

        /// <summary>Where the leg was last hit — which leg the crippling takes off. Collected from our
        /// own leg hits and from the relayed crippling hit; without either, the <c>netId</c> decides.</summary>
        private Vector3 _crippleHint;
        private bool _hasCrippleHint;

        // ----------------------------------------------------------------- blood

        /// <summary>Last frame a world splat was placed for this body — at most one per frame, or a
        /// shotgun paints the wall nine times from one trigger pull.</summary>
        private int _splatFrame = -1;

        private int _woundCount;

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
        private bool _groanRegistered;

        /// <summary>A cry (attack · scream · death) is sounding until then: a pain yelp on top of it is
        /// two voices from one throat.</summary>
        private float _voiceBusyUntil;

        private float _nextPain;

        /// <summary>Pain voices allowed at once, process wide — a swarm under fire would otherwise be a
        /// wall of yelping. Slot end times; a free slot is one whose time has passed.</summary>
        private static float[] _painSlots;

        private bool _feelRegistered;

        // Statics survive a disabled domain reload: Time.time restarts at 0, so leftover end times
        // would sit in the "future" and silence pain for the rest of the session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _painSlots = null;
        }

        private void Awake()
        {
            _net = GetComponent<NetObject>();
            _lastPosition = transform.position;
            _tuning = ZombieReactionTuning.Live;

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
            TrackHealth();

            // Re-enabled body: the budget dropped it on disable, and without this it would never
            // groan again.
            SetGroanRegistered(_posed && !_net.IsBroken);
            SetFeelRegistered(_net.NetId > 0);
        }

        private void OnDisable()
        {
            _net.StateChanged -= HandleStateChanged;
            ArenaCombat.ObjectHit -= HandleObjectHit;
            SetGroanRegistered(false);
            SetFeelRegistered(false);

            // The body is pooled/destroyed by the spawner with no warning: its wounds hang off bones
            // that go with it, and a quad left behind leaks its pool slot. ⚠️ Only when there is
            // something to release — the call would otherwise build the pool during a scene unload.
            if (_woundCount > 0)
            {
                ZombieBloodDecals.Shared.ReleaseWounds(this);
                _woundCount = 0;
            }
        }

        // ------------------------------------------------------------------- feel contract

        int IZombieFeelTarget.NetId => _net != null ? _net.NetId : 0;

        bool IZombieFeelTarget.IsAlive => !_dead;

        /// <summary>Chest height of the body as it stands now — the ragdoll's hips are the one handle
        /// that exists on every look.</summary>
        Vector3 IZombieFeelTarget.ChestPosition
        {
            get
            {
                Transform hips = _ragdoll != null ? _ragdoll.Hips : null;
                return hips != null
                    ? hips.position + Vector3.up * 0.35f
                    : transform.position + Vector3.up * 1.1f;
            }
        }

        bool IZombieFeelTarget.IsHeavy =>
            _type == ZombieKinds.TypeBrute || _type == ZombieKinds.TypeDev;

        /// <summary>⚠️ Keyed by <c>netId</c>, so it can only be registered once the server has bound
        /// one — an unbound body would take over slot 0 for every other zombie.</summary>
        private void SetFeelRegistered(bool wanted)
        {
            if (_feelRegistered == wanted)
            {
                return;
            }

            _feelRegistered = wanted;

            if (wanted)
            {
                ZombieFeelEvents.Register(this);
            }
            else
            {
                ZombieFeelEvents.Unregister(this);
            }
        }

        private void HandleStateChanged(NetObject netObject, NetStateOrigin origin)
        {
            // Re-read every state: the payload can arrive after the object does, and the body is only
            // built on the first POSE — by then the type has to be known.
            _type = ZombieKinds.TypeOf(_net);

            SetFeelRegistered(_net.NetId > 0);

            int counter = ZombieKinds.PayloadInt(_net, ZombieKinds.PayloadAttack, _attackCounter);
            bool broken = _net.IsBroken;

            // ⚠️ Before everything else: a body we dropped locally and the server did not must be back
            // on its feet before this state's hp and counters are read into a corpse.
            if (_predictedDeath && !broken && _net.Hp > 0f)
            {
                if (Time.time - _predictedAt <= _tuning.PredictionRestoreSeconds)
                {
                    RestorePrediction();
                }
                else
                {
                    // Past the window the prediction stands: the despawn is on its way, and standing a
                    // corpse up seconds later is worse than a body the server still counts.
                    _predictedDeath = false;
                }
            }

            TrackHealth();

            if (!_crippled && ZombieKinds.PayloadInt(_net, ZombieKinds.PayloadCrippled) == 1)
            {
                ApplyCrippled(_crippleHint, _hasCrippleHint, _lastHitDirection, _lastHitLocal);
            }

            if (origin == NetStateOrigin.Live && counter > _attackCounter && !broken)
            {
                PlayAttack(counter);
                PlayVoice(AttackClip(), _sounds != null ? _sounds.AttackVolume : 0f, true);
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
                PlayHit(_net.Hp, _hp - _net.Hp);
            }

            _hp = _net.Hp;

            if (broken && !_dead)
            {
                // ⚠️ The cry and the kill event only on a Live state: a late joiner's snapshot of an
                // already dead zombie would otherwise scream a death cry — and announce a kill — the
                // moment the arena appears.
                StartDeath(false, origin == NetStateOrigin.Live);
            }
        }

        /// <summary>Keeps the predicted health in step with the server's. ⚠️ The server's number is a
        /// CEILING, not an assignment: our own reports are ahead of it by a round trip, and taking it
        /// verbatim would undo every prediction on the next state.</summary>
        private void TrackHealth()
        {
            float hp = _net.Hp;

            if (!_predictedReady)
            {
                if (hp <= 0f)
                {
                    return;
                }

                _predictedReady = true;
                _predictedHp = hp;
            }
            else if (hp < _predictedHp)
            {
                _predictedHp = hp;
            }

            if (hp > 0f)
            {
                _hpBeforeDeath = hp;
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            ApplyStream();
            MeasureSpeed(dt);
            UpdateHitStop();

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

        /// <summary>⚠️ After the Animator has written the pose: a severed bone's scale has to be
        /// rewritten there, or the retarget grows the limb back on the next frame.</summary>
        private void LateUpdate()
        {
            _gore?.Tick();
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

            // The Dev is never dismembered: its end is staged, and a boss losing an arm to a stray
            // round undercuts the fight.
            _gore = new ZombieGore(this, _look, _ragdoll, _look.transform, netId,
                _type != ZombieKinds.TypeDev);

            if (_flinch != null)
            {
                _flinch.Configure(_crawler, FlinchScaleOf(_type));
            }

            if (_animator != null)
            {
                _animator.SetInteger(LocoSetParam, _locoSet);
                _animator.SetBool(CrawlerParam, _crawler);
            }

            // The payload said "crippled" before there was a body to cripple (a late joiner, or the
            // state arriving ahead of the first pose): replayed on the body that just appeared.
            if (_crippled)
            {
                ApplyCrippledBody(_lastHitDirection, false);
            }
        }

        private float FlinchScaleOf(string type)
        {
            if (type == ZombieKinds.TypeDev)
            {
                return _tuning.DevFlinchScale;
            }

            return type == ZombieKinds.TypeBrute ? _tuning.BruteFlinchScale : 1f;
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

        /// <summary>The reaction to a HP DROP. ⚠️ The second of the two signals for one round (the hit
        /// report is the first and usually arrives earlier): inside the dedupe window this does
        /// nothing, or every round would be presented — and counted — twice.</summary>
        private void PlayHit(float hp, float drop)
        {
            if (Time.time - _hitAnimAt < _tuning.HitAnimDedupeSeconds)
            {
                return;
            }

            _hitAnimAt = Time.time;

            // No report covered this round, so the hp DROP is the damage: the one number an observer
            // (and the admin) ever gets, and what the hit stop and the corpse's throw are scaled by.
            AddFrameDamage(drop);

            // The variant follows the remaining hp, so it alternates without a random draw that would
            // differ per headset.
            TriggerHitClip(Mathf.Max(0, Mathf.RoundToInt(hp)) % 2);
            ApplyHitStop(false);
        }

        /// <summary>Upper-body reaction clip. ⚠️ Rate limited by
        /// <see cref="ZombieReactionTuning.HitClipCooldownSeconds"/>: on full auto the trigger fires
        /// every round, the clip restarts from frame one every time and the arms never finish a
        /// reaction. The flinch SPRING still takes every hit — that is what keeps a burst readable.</summary>
        private void TriggerHitClip(int variant)
        {
            if (_animator == null || _crawler || _dead || _noFlinch ||
                Time.time - _hitClipAt < _tuning.HitClipCooldownSeconds)
            {
                return;
            }

            _hitAnimAt = Time.time;
            _hitClipAt = Time.time;
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
            _lastHitLocal = info.Local;
            _lastHitCollider = info.HitCollider;

            // The round is presented from HERE; the hp drop that follows is the same round and must not
            // present it a second time.
            _hitAnimAt = Time.time;

            if (info.Local && info.Damage > 0f)
            {
                AddFrameDamage(info.Damage);
            }

            // Which leg the crippling will take off. The shooter learns it from its own leg hits, the
            // rest of the squad from the relay that carries the crippling flag.
            if ((HitZone)info.Zone == HitZone.Leg && (info.Local || info.Crippled))
            {
                _crippleHint = info.Point;
                _hasCrippleHint = true;
            }

            if (!_dead)
            {
                if (_flinch != null)
                {
                    _flinch.Hit(_lastHitZone, info.Direction, info.Point);
                }

                TriggerHitClip(_hitVariant++ & 1);
                ApplyHitStop(_lastHitZone == HitZone.Head);
                PlayPain();
                MarkBlood(info);
            }

            // The shooter already played their own impact locally (ArenaCombat.ReportImpact); this is
            // what gives the REST of the squad blood on the body they are watching.
            if (!info.Local && (!_dead || Time.time - _deadAt < 0.5f))
            {
                PlayRemoteBlood(info);
            }

            // ⚠️ The relay reaches everyone BUT the shooter (§5.2): the shooter learns the crippling
            // from the payload instead, which is why both paths end in the same guarded call.
            if (info.Crippled && !_crippled)
            {
                ApplyCrippled(info.Point, true, info.Direction, false);
            }

            // Kill prediction, last: the shot's own presentation has already run, so the death
            // presentation starts on top of it in the SAME frame the trigger was pulled.
            if (info.Local && !_dead && _predictedReady && info.Damage > 0f)
            {
                _predictedHp -= info.Damage;
                if (_predictedHp <= 0f)
                {
                    StartDeath(true, true);
                }
            }
        }

        private void AddFrameDamage(float damage)
        {
            if (damage <= 0f)
            {
                return;
            }

            int frame = Time.frameCount;
            if (frame != _frameDamageFrame)
            {
                _frameDamageFrame = frame;
                _frameDamage = 0f;
            }

            _frameDamage += damage;
        }

        /// <summary>Damage that landed on this body in the CURRENT frame — a shotgun's whole pattern,
        /// not one pellet.</summary>
        private float FrameDamage() => _frameDamageFrame == Time.frameCount ? _frameDamage : 0f;

        /// <summary>Damage of the KILLING frame. ⚠️ Falls back to the last known health: an observer
        /// never sees a damage number, and the health a body still had is the closest stand-in for how
        /// hard the round that emptied it hit.</summary>
        private float KillDamage()
        {
            float frame = FrameDamage();
            return frame > 0f ? frame : Mathf.Max(0f, _hpBeforeDeath);
        }

        /// <summary>Blood on the world and on the body. ⚠️ One world splat per body per FRAME: a
        /// shotgun reports nine pellets on one trigger pull and would paint the wall nine times.</summary>
        private void MarkBlood(in ArenaCombat.ObjectHitInfo info)
        {
            if (_splatFrame != Time.frameCount)
            {
                _splatFrame = Time.frameCount;
                ZombieBloodDecals.Shared.TrySplat(info.Point, info.Direction,
                    (HitZone)info.Zone == HitZone.Head);
            }

            if (_gore == null || _woundCount >= _tuning.WoundsPerZombie)
            {
                return;
            }

            Collider box = info.HitCollider != null ? info.HitCollider : _gore.NearestHitbox(info.Point);
            Transform bone = box != null ? box.transform.parent : null;
            if (bone == null)
            {
                return;
            }

            _woundCount++;
            ZombieBloodDecals.Shared.Wound(this, bone, info.Point, info.Direction);
        }

        // ------------------------------------------------------------------- hit stop

        /// <summary>Freezes THIS zombie's animator for a few frames on a head hit or a heavy one — the
        /// one cue that reads as weight without touching the camera.</summary>
        /// <remarks>⚠️ Never on the admin: its bodies are the ones the brain streams, and a reaction
        /// that only exists on the operator's screen is a difference nobody can see or verify. (The
        /// stream carries the transform only, so this would in fact be invisible — the rule is kept
        /// anyway, so the admin stays free of presentation work.)</remarks>
        private void ApplyHitStop(bool head)
        {
            if (_dead || _animator == null)
            {
                return;
            }

            if (!head && FrameDamage() < _tuning.HitStopDamage)
            {
                return;
            }

            ArenaClient client = ArenaClient.Instance;
            if (client != null && client.IsAdminRole)
            {
                return;
            }

            _animator.speed = _tuning.HitStopAnimatorSpeed;
            _hitStopUntil = Time.time + _tuning.HitStopSeconds;
            _hitStopActive = true;
        }

        /// <summary>⚠️ Restores to 1, and only while it owns the speed: the crawler's death is the other
        /// writer of <c>animator.speed</c> (it parks the body at 0), and the two must not fight.</summary>
        private void UpdateHitStop()
        {
            if (!_hitStopActive || Time.time < _hitStopUntil)
            {
                return;
            }

            _hitStopActive = false;

            if (!_dead && _animator != null)
            {
                _animator.speed = 1f;
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

            if (RemoteShotMarksUs(info.Point, direction))
            {
                return;
            }

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

        /// <summary>Will the remote SHOT's own impact land on this body? Then the blood is already
        /// playing and a second report doubles it.</summary>
        /// <remarks>⚠️ A remote round marks its surface twice over: <c>RemoteShotFx</c> recasts the shot
        /// ray from the muzzle (§6.4) and reports what it finds, while the hit relay brings us here. The
        /// shot ray is the one we cannot change, so the body's own report is the one that stands down —
        /// decided geometrically, which also makes it order independent.
        /// <para>⚠️ Only for a directional hit: a blast carries no direction (and fires no shot event),
        /// so there is nothing to double there.</para></remarks>
        private bool RemoteShotMarksUs(Vector3 point, Vector3 direction)
        {
            Collider[] parts = _ragdoll != null ? _ragdoll.Colliders : null;
            if (parts == null)
            {
                return false;
            }

            // Same mask as both shot rays: a corpse layer in between must not count as cover.
            if (!Physics.Raycast(point - direction * BloodRayBackMeters, direction,
                    out RaycastHit hit, BloodRayBackMeters * 2f, ArenaLayers.ShotMask,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == hit.collider)
                {
                    return true;
                }
            }

            return false;
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

        /// <summary>Starts the WHOLE death presentation: the cry, the dismemberment, the fall and the
        /// kill event. One entry point for both the server's <c>broken</c> and the local prediction, so
        /// a body can never die twice.</summary>
        /// <param name="predicted">Started from our own damage, before the server answered.</param>
        /// <param name="announce">Play the cry and raise the kill. <c>false</c> = a late joiner's
        /// snapshot of a body that was already dead: a corpse where it fell, nothing else.</param>
        private void StartDeath(bool predicted, bool announce)
        {
            if (_dead)
            {
                return;
            }

            SetGroanRegistered(false);

            if (predicted)
            {
                _predictedDeath = true;
                _predictedAt = Time.time;
            }

            bool fresh = Time.time - _lastHitAt < _tuning.HitMemorySeconds;
            bool byLocal = predicted || fresh && _lastHitLocal;

            if (announce)
            {
                PlayVoice(DeathClip(), _sounds != null ? _sounds.DeathVolume : 0f, true);
            }

            ZombieLimb severed = fresh ? Dismember(byLocal) : ZombieLimb.None;

            ApplyDeath();

            if (!announce || _killRaised)
            {
                return;
            }

            _killRaised = true;
            ZombieFeelEvents.RaiseKill(new ZombieKillPresentation(_net.NetId,
                fresh ? _lastHitPoint : transform.position,
                fresh ? _lastHitZone : HitZone.Body, byLocal, predicted, severed));
        }

        /// <summary>What the killing round takes off: a lethal head hit ALWAYS pops the head, a heavy
        /// enough arm/leg hit severs that segment, a body shot takes nothing.</summary>
        private ZombieLimb Dismember(bool byLocal)
        {
            if (_gore == null)
            {
                return ZombieLimb.None;
            }

            if (_lastHitZone == HitZone.Head)
            {
                return _gore.PopHead(_lastHitPoint, _lastHitDirection, byLocal);
            }

            // Relayed hits carry no collider (§5.2): the nearest box to the reported point is the best
            // available answer, and it is the same geometry on every headset.
            Collider box = _lastHitCollider != null
                ? _lastHitCollider
                : _gore.NearestHitbox(_lastHitPoint);

            if (box == null || KillDamage() < _tuning.LimbSeverDamage)
            {
                return ZombieLimb.None;
            }

            return _gore.SeverAt(box, _lastHitPoint, _lastHitDirection, byLocal);
        }

        /// <summary>The local prediction was wrong — the server still reports this body alive. Puts it
        /// back on its feet: the alternative is a corpse that keeps walking on everyone else's headset
        /// while this visor cannot shoot it any more.</summary>
        private void RestorePrediction()
        {
            Debug.LogWarning($"[Zombie] {_net.NetId} tahminle öldü ama sunucu ayakta diyor " +
                             $"(can {_net.Hp:0}) — gövde geri alınıyor.", this);

            _predictedDeath = false;
            _dead = false;
            _killRaised = false;
            _fadedOut = false;
            _bloodPlaced = false;
            _ragdolled = false;
            _ragdollFrozen = false;
            _hitStopActive = false;
            _predictedHp = _net.Hp;

            if (_ragdoll != null)
            {
                _ragdoll.Restore();
            }

            _gore?.Restore();

            if (_look != null)
            {
                _look.SetHitboxesActive(true);
            }

            if (_flinch != null)
            {
                _flinch.enabled = true;
            }

            if (_animator != null)
            {
                _animator.enabled = true;
                _animator.speed = 1f;
                _animator.SetBool(DeadParam, false);
            }

            SetGroanRegistered(true);
        }

        /// <summary>Crippled (payload <c>cr</c>, §10.5): one leg comes off at the knee and the body
        /// crawls from here on. Irreversible — the server never clears the flag.</summary>
        /// <remarks>⚠️ The crawl is the SHARED controller's own state (<c>Crawler</c>): nothing new is
        /// authored for it, the body simply becomes a crawler mid-life, exactly like one that spawned as
        /// one. The collapse into it is the hit clip plus that transition — there is no fall-to-crawl
        /// animation to play.</remarks>
        private void ApplyCrippled(Vector3 point, bool hasPoint, Vector3 direction, bool byLocal)
        {
            if (_crippled)
            {
                return;
            }

            _crippled = true;

            if (hasPoint)
            {
                _crippleHint = point;
                _hasCrippleHint = true;
            }

            // ⚠️ Before _crawler flips: the clip is skipped on a crawler, and this is the only
            // "it went down" beat the body gets (there is no fall-to-crawl animation).
            TriggerHitClip(0);

            ApplyCrippledBody(direction, byLocal);
            PlayGore(_tuning.RandomCrawlCollapse());
        }

        /// <summary>The body half of the crippling: the leg comes off and the locomotion becomes the
        /// crawl. Separate because the payload can arrive BEFORE the body is built.</summary>
        private void ApplyCrippledBody(Vector3 direction, bool byLocal)
        {
            if (_look == null)
            {
                return;
            }

            if (_gore != null && !_dead)
            {
                _gore.SeverLegAtKnee(_crippleHint, _hasCrippleHint, direction, byLocal);
            }

            _crawler = true;

            if (_animator != null)
            {
                _animator.SetBool(CrawlerParam, true);
            }

            if (_flinch != null)
            {
                _flinch.Configure(true, FlinchScaleOf(_type));
            }
        }

        private void ApplyDeath()
        {
            _dead = true;
            _deadAt = Time.time;
            _hitStopActive = false;

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

            bool fresh = Time.time - _lastHitAt < _tuning.HitMemorySeconds;
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

            // The zone decides the direction of the throw, the WEAPON decides how hard: a pump of
            // buckshot and a pistol round must not drop the same body the same way.
            float impulse = fresh
                ? _tuning.ImpulseOf(_lastHitZone) * _tuning.ImpulseScale(KillDamage())
                : _tuning.BodyImpulse * 0.4f;

            if (!_ragdoll.Collapse(point, direction.normalized, impulse))
            {
                return false;
            }

            _ragdolled = true;
            return true;
        }

        /// <summary>The corpse's own clock: settle, bleed, fade. The server despawns the object
        /// itself — all of this only has to finish BEFORE it does.</summary>
        private void UpdateCorpse()
        {
            float since = Time.time - _deadAt;

            if (_ragdolled && !_ragdollFrozen && since >= _tuning.RagdollFreezeSeconds)
            {
                _ragdollFrozen = true;
                _ragdoll.Freeze();
            }

            if (!_bloodPlaced && since >= _tuning.BloodPoolDelay)
            {
                _bloodPlaced = true;

                Transform hips = _ragdoll != null ? _ragdoll.Hips : null;
                ZombieBloodPool.Shared.Place(hips != null ? hips.position : transform.position);
            }

            FadeOutCorpse(since);
        }

        private void FadeOutCorpse(float since)
        {
            if (_fadedOut || since < _tuning.DeathDissolveDelay)
            {
                return;
            }

            _fadedOut = true;

            if (_look.Dissolve != null)
            {
                _look.Dissolve.Vanish(_tuning.DeathDissolveSeconds);
            }
        }

        // ------------------------------------------------------------------------- voice

        /// <summary>⚠️ On EVERY headset, not only the brain's: the voice is presentation, and a sound
        /// that only the object host hears is a zombie nobody else can locate.</summary>
        private void BuildVoice()
        {
            _sounds = ZombieSounds.Live;

            // ⚠️ Built even with no sound asset: the gore clips live in the reaction tuning and would
            // otherwise have no source to play through.
            _voice = gameObject.AddComponent<AudioSource>();
            _voice.playOnAwake = false;
            _voice.spatialBlend = 1f;
            _voice.rolloffMode = AudioRolloffMode.Logarithmic;
            _voice.minDistance = _sounds != null ? _sounds.MinDistance : 2f;
            _voice.maxDistance = _sounds != null ? _sounds.MaxDistance : 20f;
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
            _voiceBusyUntil = Time.time + clip.length;
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
            // ⚠️ The source exists even with no sound asset (the gore clips use it), so the data is
            // checked separately here.
            if (_voice == null || _sounds == null || !_posed || Time.time < _nextGroan)
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
        /// <param name="blocksPain">This is a CRY (attack, scream, death): a pain yelp on top of it
        /// would be two voices from one throat.</param>
        private void PlayVoice(AudioClip clip, float volume, bool blocksPain = false)
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

            if (blocksPain)
            {
                _voiceBusyUntil = Time.time + clip.length;
            }

            float jitter = _sounds != null ? _sounds.PitchJitter : 0f;
            _voice.pitch = 1f + UnityEngine.Random.Range(-jitter, jitter);

            // PlayOneShot: a groan may still be sounding when the zombie is hit, and the two must
            // overlap rather than cut each other off.
            _voice.PlayOneShot(clip, Mathf.Clamp(volume * channel, 0f, 2f));
        }

        /// <summary>A gore cue (head pop · limb sever · collapse into the crawl) through the body's own
        /// source. Silent with no clip in the tuning asset.</summary>
        internal void PlayGore(AudioClip clip) => PlayVoice(clip, _tuning.GoreVolume, true);

        /// <summary>The zombie's own pain, on a hit. Rate limited three ways — per body, per headset and
        /// against its own cries — because a wave under fire is dozens of bodies taking rounds at
        /// once.</summary>
        private void PlayPain()
        {
            AudioClip clip = _sounds != null ? _sounds.RandomPain() : null;
            if (clip == null || _voice == null)
            {
                return;
            }

            float now = Time.time;
            if (now < _nextPain || now < _voiceBusyUntil ||
                UnityEngine.Random.value > _tuning.PainChance)
            {
                return;
            }

            if (!TakePainSlot(_tuning.PainConcurrent, clip.length))
            {
                return;
            }

            _nextPain = now + _tuning.PainCooldownSeconds;
            PlayVoice(clip, _sounds.PainVolume, true);
        }

        /// <summary>Reserves one of the headset-wide pain voices for <paramref name="seconds"/>.</summary>
        private static bool TakePainSlot(int cap, float seconds)
        {
            if (_painSlots == null || _painSlots.Length < cap)
            {
                _painSlots = new float[Mathf.Max(1, cap)];
            }

            float now = Time.time;
            for (int i = 0; i < cap && i < _painSlots.Length; i++)
            {
                if (now >= _painSlots[i])
                {
                    _painSlots[i] = now + Mathf.Max(0.1f, seconds);
                    return true;
                }
            }

            return false;
        }
    }
}
