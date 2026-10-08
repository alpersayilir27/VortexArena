using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.Player;
using VortexArena.Net;
using Random = UnityEngine.Random;

namespace VortexArena.Modes.Burger
{
    /// <summary>The ketchup stream: a pooled droplet simulation at the bottle's nozzle, run LOCALLY on
    /// every headset while <see cref="BurgerKetchupBottle"/> says the bottle is being squeezed.
    /// <para>⚠️ Nothing here is on the wire (§10.5): every client simulates from the same hand pose and
    /// the cosmetic difference is accepted. The only report that leaves this component is
    /// <c>squirt</c> — and ONLY from the headset that owns the bottle, because the sauce layer it asks
    /// for is a scored ingredient, not an effect.</para>
    /// <para>⚠️ No allocation per frame: droplets are structs in a fixed ring, their visuals are
    /// instantiated lazily once, and the collision/overlap queries use the non-alloc forms.</para></summary>
    [DisallowMultipleComponent]
    public sealed class BurgerKetchupStream : MonoBehaviour
    {
        [Tooltip("Çıkış noktası: konumu damlanın doğduğu yer, +Z ekseni fışkırma yönü. Boşsa bu obje.")]
        [SerializeField] private Transform nozzle;

        [Tooltip("Damla görseli (Unlit, ışıksız kalın şerit/küre). Havuzlanır.")]
        [SerializeField] private GameObject dropletPrefab;

        [Tooltip("Leke quad'ı prefabı — havuzu BurgerSplatPool tutar.")]
        [SerializeField] private GameObject splatPrefab;

        [Tooltip("Ketçap rengi; alfası lekenin tepe opaklığıdır.")]
        [SerializeField] private Color ketchupColor = new Color(0.62f, 0.07f, 0.06f, 1f);

        [Header("Akış")]
        [Tooltip("Saniyedeki damla sayısı.")]
        [SerializeField] private float dropletsPerSecond = 25f;

        [Tooltip("Aynı anda havada olabilen damla sayısı — havuzun boyu. Dolunca en eskisi kullanılır.")]
        [SerializeField] private int maxDroplets = 48;

        [Tooltip("Çıkış hızı (m/sn), şişe ekseni boyunca.")]
        [SerializeField] private float exitSpeed = 4f;

        [Tooltip("Çıkış yönündeki rastgele saçılma (derece) — kusursuz bir çizgi boru gibi görünür.")]
        [SerializeField] private float spreadDegrees = 2.5f;

        [Tooltip("Yerçekimi çarpanı.")]
        [SerializeField] private float gravityScale = 1f;

        [Tooltip("Damlanın çarpışma yarıçapı (m).")]
        [SerializeField] private float dropletRadius = 0.012f;

        [Tooltip("Hiçbir şeye çarpmayan damlanın ömrü (sn).")]
        [SerializeField] private float dropletLifeSeconds = 3f;

        [Tooltip("Damlanın çarpabileceği katmanlar.")]
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("Leke")]
        [Tooltip("Leke boyu aralığı (m) — aynı dokunun tekrarı bu aralıkla kırılır.")]
        [SerializeField] private float splatScaleMin = 0.07f;

        [Tooltip("Leke boyu aralığının üst ucu (m).")]
        [SerializeField] private float splatScaleMax = 0.14f;

        [Header("Şerit görünümü")]
        [Tooltip("Akışı tek parça kalın şerit olarak çizen çizgi (nozül → havadaki damlalar). Atanmışsa " +
                 "damla prefabı örneklenmez.")]
        [SerializeField] private LineRenderer ribbon;

        [Header("Çarpma sesi")]
        [Tooltip("Damla yüzeye çarpınca çalan tek seferlik ses. Boşsa sessiz.")]
        [SerializeField] private AudioClip splatClip;

        [Tooltip("Çarpma sesinin kaynakları (sırayla kullanılır; 3D, loop kapalı).")]
        [SerializeField] private AudioSource[] splatSources;

        [Tooltip("İki çarpma sesi arasındaki en kısa süre (sn) — 25 damla/sn her biri çalmasın.")]
        [SerializeField] private float splatSoundInterval = 0.12f;

        /// <summary>Uninterrupted stream on a stack before a sauce layer is asked for (s).</summary>
        private const float SquirtHoldSeconds = 0.4f;

        /// <summary>How long a broken chain is tolerated (s) — droplets arrive in bursts, and a
        /// per-frame flag would reset the chain between two of them.</summary>
        private const float ChainGraceSeconds = 0.15f;

        /// <summary>Probe radius around a hit point while looking for a stack volume (m).</summary>
        private const float BoardProbeRadius = 0.03f;

        /// <summary>Lift over the hit point for the reported sauce pose (m): the layer is dropped ONTO
        /// the stack, so it must be born above it.</summary>
        private const float SquirtLift = 0.06f;

        private static readonly Collider[] Probe = new Collider[16];

        private struct Droplet
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float DieAt;
            public bool Active;
        }

        private NetObject _net;
        private Droplet[] _droplets;
        private Transform[] _visuals;
        private int _next;

        private bool _emitting;
        private float _emitAccumulator;

        // Chain of hits on ONE stack volume; only the bottle's owner keeps it.
        private int _chainBoardId;
        private float _chainSeconds;
        private float _chainGrace;
        private Vector3 _chainPoint;
        private bool _hitThisFrame;
        private int _hitBoardId;
        private Vector3 _hitPoint;

        private int _nextSplatSource;
        private float _nextSplatSoundAt;

        private Vector3[] _ribbonPoints;

        private void Awake()
        {
            _net = GetComponentInParent<NetObject>();

            if (nozzle == null)
            {
                nozzle = transform;
            }

            int size = Mathf.Max(1, maxDroplets);
            _droplets = new Droplet[size];
            _visuals = new Transform[size];
            _ribbonPoints = new Vector3[size + 1];

            if (ribbon != null)
            {
                ribbon.useWorldSpace = true;
                ribbon.positionCount = 0;
                ribbon.enabled = false;
            }
            else if (dropletPrefab == null)
            {
                Debug.LogError($"[BurgerKetchupStream] '{name}' için damla prefabı atanmamış — akış " +
                               "görünmez.", this);
            }
        }

        private void OnDisable()
        {
            _emitting = false;
            _emitAccumulator = 0f;
            ResetChain();

            if (_droplets == null)
            {
                return;
            }

            for (int i = 0; i < _droplets.Length; i++)
            {
                Deactivate(i);
            }
        }

        /// <summary>Stream on/off; driven by <see cref="BurgerKetchupBottle"/> on EVERY headset.</summary>
        /// <remarks>Droplets already in the air are not killed — the stream stops at the nozzle, the
        /// last drops still land.</remarks>
        public void SetEmitting(bool on)
        {
            if (on == _emitting)
            {
                return;
            }

            _emitting = on;
            _emitAccumulator = 0f;

            if (!on)
            {
                ResetChain();
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            if (_emitting)
            {
                Emit(dt);
            }

            Step(dt);
            TickChain(dt);
            DrawRibbon();
        }

        /// <summary>One thick line from the nozzle through the airborne droplets, newest to oldest; the
        /// first landed (inactive) droplet ends it, so the stream reads as one pour that breaks where it
        /// hits.</summary>
        private void DrawRibbon()
        {
            if (ribbon == null)
            {
                return;
            }

            int count = 0;
            if (_emitting && nozzle != null)
            {
                _ribbonPoints[count++] = nozzle.position;
            }

            int n = _droplets.Length;
            for (int k = 1; k <= n; k++)
            {
                int i = (_next - k + n) % n;
                if (!_droplets[i].Active)
                {
                    break;
                }

                _ribbonPoints[count++] = _droplets[i].Position;
            }

            if (count < 2)
            {
                if (ribbon.enabled)
                {
                    ribbon.enabled = false;
                }

                return;
            }

            ribbon.positionCount = count;
            for (int p = 0; p < count; p++)
            {
                ribbon.SetPosition(p, _ribbonPoints[p]);
            }

            ribbon.enabled = true;
        }

        private void Emit(float dt)
        {
            if ((dropletPrefab == null && ribbon == null) || nozzle == null || dropletsPerSecond <= 0f)
            {
                return;
            }

            _emitAccumulator += dt * dropletsPerSecond;

            // Capped by the ring: a frame hitch must not spend the whole pool in one step.
            int budget = _droplets.Length;
            while (_emitAccumulator >= 1f && budget-- > 0)
            {
                _emitAccumulator -= 1f;
                Spawn();
            }

            if (_emitAccumulator >= 1f)
            {
                _emitAccumulator = 0f;
            }
        }

        private void Spawn()
        {
            int i = _next;
            _next = (_next + 1) % _droplets.Length;

            if (_visuals[i] == null && ribbon == null)
            {
                // Parentless on purpose: a droplet lives in world space while the nozzle is in a moving
                // hand, and it belongs to the arena scene just like this component.
                _visuals[i] = Instantiate(dropletPrefab).transform;
            }

            Vector3 direction = spreadDegrees > 0f
                ? Quaternion.Euler(Random.Range(-spreadDegrees, spreadDegrees),
                    Random.Range(-spreadDegrees, spreadDegrees), 0f) * nozzle.forward
                : nozzle.forward;

            _droplets[i].Position = nozzle.position;
            _droplets[i].Velocity = direction * exitSpeed;
            _droplets[i].DieAt = Time.time + dropletLifeSeconds;
            _droplets[i].Active = true;

            if (_visuals[i] != null)
            {
                _visuals[i].position = nozzle.position;
                _visuals[i].gameObject.SetActive(true);
            }
        }

        private void Step(float dt)
        {
            Vector3 gravity = Physics.gravity * gravityScale;
            float now = Time.time;
            BurgerKetchupVision vision = BurgerKetchupVision.Active;

            for (int i = 0; i < _droplets.Length; i++)
            {
                if (!_droplets[i].Active)
                {
                    continue;
                }

                if (now >= _droplets[i].DieAt)
                {
                    Deactivate(i);
                    continue;
                }

                Vector3 from = _droplets[i].Position;
                _droplets[i].Velocity += gravity * dt;
                Vector3 to = from + _droplets[i].Velocity * dt;
                Vector3 delta = to - from;
                float distance = delta.magnitude;

                // Own face first: the droplet must not be eaten by the head collider's hit instead.
                // A point test is enough — one step is centimetres against a 20 cm radius.
                if (vision != null && (to - vision.HeadPosition).sqrMagnitude <
                    BurgerKetchupVision.HeadRadius * BurgerKetchupVision.HeadRadius)
                {
                    vision.Splash();
                    Deactivate(i);
                    continue;
                }

                if (distance > 0.0001f && Physics.SphereCast(from, dropletRadius, delta / distance,
                        out RaycastHit hit, distance, hitMask, QueryTriggerInteraction.Ignore))
                {
                    OnHit(hit);
                    Deactivate(i);
                    continue;
                }

                _droplets[i].Position = to;

                if (_visuals[i] != null)
                {
                    // Stretched along the flight: a sphere at 4 m/s reads as a dotted line, a streak
                    // reads as a stream.
                    _visuals[i].SetPositionAndRotation(to, Quaternion.LookRotation(
                        _droplets[i].Velocity.sqrMagnitude > 0.0001f ? _droplets[i].Velocity : Vector3.down));
                }
            }
        }

        private void Deactivate(int index)
        {
            _droplets[index].Active = false;

            if (_visuals[index] != null)
            {
                _visuals[index].gameObject.SetActive(false);
            }
        }

        /// <summary>Round-robin one-shot moved to the hit point, rate-limited.</summary>
        private void PlaySplatSound(Vector3 point)
        {
            if (splatClip == null || splatSources == null || splatSources.Length == 0 || Time.time < _nextSplatSoundAt)
            {
                return;
            }

            _nextSplatSoundAt = Time.time + splatSoundInterval;
            AudioSource source = splatSources[_nextSplatSource];
            _nextSplatSource = (_nextSplatSource + 1) % splatSources.Length;
            if (source == null)
            {
                return;
            }

            source.transform.position = point;
            source.pitch = Random.Range(0.9f, 1.1f);
            source.PlayOneShot(splatClip);
        }

        /// <summary>A droplet landed: a stain for everyone, plus the owner's stack bookkeeping.</summary>
        private void OnHit(RaycastHit hit)
        {
            // A stain on an avatar rides the BONE it landed on; everything else stays in world space.
            RemoteHitBox box = hit.collider.GetComponentInParent<RemoteHitBox>();

            BurgerSplatPool.Shared.Add(splatPrefab, hit.point, hit.normal,
                box != null ? hit.collider.transform : null, ketchupColor,
                Random.Range(splatScaleMin, splatScaleMax));
            PlaySplatSound(hit.point);

            if (box != null || _net == null || !_net.IsMine)
            {
                return;
            }

            int boardId = ResolveBoard(hit.point);
            if (boardId == 0)
            {
                return;
            }

            _hitThisFrame = true;
            _hitBoardId = boardId;
            _hitPoint = hit.point;
        }

        /// <summary>netId of the board whose stack volume contains the point, or <c>0</c>.</summary>
        /// <remarks>The STACK VOLUME is a trigger, so the query must include triggers; the kind check is
        /// the same gate the server applies to <c>squirt</c> (§10.5).</remarks>
        private static int ResolveBoard(Vector3 point)
        {
            int count = Physics.OverlapSphereNonAlloc(point, BoardProbeRadius, Probe, ~0,
                QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                Collider candidate = Probe[i];
                if (candidate == null || !candidate.isTrigger)
                {
                    continue;
                }

                if (candidate.GetComponentInParent<BurgerServingBoard>() == null &&
                    candidate.GetComponentInParent<BurgerCuttingBoard>() == null)
                {
                    continue;
                }

                NetObject board = candidate.GetComponentInParent<NetObject>();
                if (board == null || board.NetId <= 0 || board.Kind == null)
                {
                    continue;
                }

                string kind = board.Kind.Kind;
                if (kind == BurgerKinds.Board || kind == BurgerKinds.CuttingBoard)
                {
                    return board.NetId;
                }
            }

            return 0;
        }

        /// <summary>Uninterrupted seconds on one stack → one <c>squirt</c>.</summary>
        /// <remarks>⚠️ The accumulator is only reset after a report; the per-bottle cooldown is the
        /// SERVER's (§10.5) — a client-side copy would be a second rule to keep in sync.</remarks>
        private void TickChain(float dt)
        {
            if (_hitThisFrame)
            {
                _chainSeconds = _hitBoardId == _chainBoardId ? _chainSeconds + dt : dt;
                _chainBoardId = _hitBoardId;
                _chainPoint = _hitPoint;
                _chainGrace = ChainGraceSeconds;
                _hitThisFrame = false;
            }
            else if (_chainBoardId != 0)
            {
                _chainGrace -= dt;
                if (_chainGrace <= 0f)
                {
                    ResetChain();
                    return;
                }
            }

            if (_chainBoardId == 0 || _chainSeconds < SquirtHoldSeconds)
            {
                return;
            }

            _chainSeconds = 0f;

            Vector3 arena = ArenaSpace.WorldToArena(_chainPoint + Vector3.up * SquirtLift);
            NetObjectSync.SendEvent(_net.NetId, BurgerKinds.EventSquirt,
                new[] { _chainBoardId }, new[] { arena.x, arena.y, arena.z });
        }

        private void ResetChain()
        {
            _chainBoardId = 0;
            _chainSeconds = 0f;
            _chainGrace = 0f;
            _hitThisFrame = false;
        }
    }
}
