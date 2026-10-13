using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using VortexArena.Core.Arena;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Modes.Zombie
{
    /// <summary>The zombie's BRAIN: walks it on the NavMesh, picks a target, times the melee and streams
    /// the pose (<c>0x09</c>). Runs on ONE machine only — the owner's, i.e. the object host admin
    /// (§10.10) — and does nothing at all on every other headset.</summary>
    /// <remarks>⚠️ Why the client: the server has no NavMesh, no colliders and no metres (§10.10); it
    /// owns the wave machine, the damage number and the health, and this component owns the movement.
    /// <para>⚠️ <b><c>NetObjectPoseSender</c> must NOT be put on this prefab.</b> That component
    /// measures "it stopped" and then sends <c>object_rest</c>, which ENDS ownership — a standing zombie
    /// would hand its own brain back and freeze for the rest of the match. Pose streaming is done here,
    /// without the rest contract.</para>
    /// <para>⚠️ The agent is DISABLED while the object is not ours: an enabled agent moves the transform
    /// and would fight the interpolated stream (<see cref="ZombieView"/>).</para></remarks>
    [RequireComponent(typeof(NetObject))]
    [DisallowMultipleComponent]
    public class ZombieBrain : MonoBehaviour
    {
        private static readonly float PoseSendInterval = 1f / ArenaProtocol.OBJECT_POSE_RATE_HZ;

        [Tooltip("Boş bırakılırsa aynı objeden alınır.")]
        [SerializeField] private NavMeshAgent agent;

        [Tooltip("Hedefe bu kadar kalınca durur (m) — vuruş mesafesinin biraz altı.")]
        [SerializeField] private float stoppingDistance = 1f;

        [Tooltip("Vuruş kararı ile olayın gönderilmesi arasındaki hazırlık (sn).")]
        [SerializeField] private float attackWindupSeconds = 0.4f;

        [Tooltip("Hedef kaç saniyede bir yeniden seçilir.")]
        [SerializeField] private float retargetSeconds = 0.25f;

        [Tooltip("Doğum noktasına oturtmak için NavMesh arama yarıçapı (m).")]
        [SerializeField] private float navSampleRadius = 4f;

        private NetObject _net;
        private readonly List<int> _playerIds = new List<int>();

        private float _poseTimer;
        private float _retargetTimer;

        /// <summary>Unscaled time the local cooldown expires; mirrors the server's (§10.5).</summary>
        private float _cooldownUntil;

        /// <summary>Unscaled time the pending attack event goes out; <c>0</c> = no attack in progress.</summary>
        private float _attackAt;

        private int _attackTargetId;

        /// <summary>We are the owner but the agent could not be placed — the zombie stands still and only
        /// streams its pose, so the others see a standing body instead of nothing at all.</summary>
        private bool _navMissing;

        /// <summary>Did we already warp onto the spawn point for THIS ownership? ⚠️ Warping again on every
        /// state message would teleport the zombie back to its door mid-wave.</summary>
        private bool _placed;

        private void Awake()
        {
            _net = GetComponent<NetObject>();

            if (agent == null)
            {
                agent = GetComponent<NavMeshAgent>();
            }

            if (agent != null)
            {
                agent.enabled = false;
            }
        }

        private void OnEnable()
        {
            _net.OwnerChanged += HandleOwnerChanged;
            _net.StateChanged += HandleStateChanged;
            ApplyOwnership();
        }

        private void OnDisable()
        {
            _net.OwnerChanged -= HandleOwnerChanged;
            _net.StateChanged -= HandleStateChanged;
        }

        private void HandleOwnerChanged(NetObject _, int previousOwner)
        {
            // Ownership can move to another admin mid-match (§10.10): the new brain must start from where
            // the body IS, not from the spawn door, so placement is only redone from scratch when it has
            // never happened on this machine.
            ApplyOwnership();
        }

        private void HandleStateChanged(NetObject _, NetStateOrigin origin)
        {
            ApplyOwnership();
        }

        private void ApplyOwnership()
        {
            bool mine = _net.IsMine && !_net.IsBroken;

            if (agent == null)
            {
                return;
            }

            if (!mine)
            {
                if (agent.enabled)
                {
                    agent.enabled = false;
                }

                _attackAt = 0f;
                return;
            }

            agent.speed = ZombieKinds.SpeedOfWave(ZombieKinds.PayloadInt(_net, ZombieKinds.PayloadWave, 1));
            agent.stoppingDistance = stoppingDistance;

            if (!agent.enabled)
            {
                agent.enabled = true;
            }

            if (!_placed)
            {
                Place();
            }
        }

        /// <summary>Puts the body on its spawn point and onto the NavMesh. The spawn point travels as a
        /// <c>netId</c> in the payload (§10.5) — the spawn pose on the wire is meaningless, because the
        /// server does not know where its own scene objects are.</summary>
        private void Place()
        {
            _placed = true;

            Vector3 target = transform.position;

            // ⚠️ The spawn door is only for the FIRST brain. On a hand-over (§10.10) the previous owner's
            // stream already put the body where it belongs, and warping it home would teleport the zombie
            // back to the fence in the middle of a wave. "Have we ever seen a pose" is the one available
            // discriminator — the server relays even our own poses back.
            RemoteObjectRegistry streams = ArenaClient.Instance != null
                ? ArenaClient.Instance.RemoteObjects
                : null;
            bool fresh = streams == null || !streams.IsStreaming(_net.NetId);

            int spawnNetId = ZombieKinds.PayloadInt(_net, ZombieKinds.PayloadSpawn);
            if (fresh && spawnNetId > 0 && NetObjectRegistry.TryGet(spawnNetId, out NetObject spawnPoint))
            {
                target = spawnPoint.transform.position;
            }

            if (NavMesh.SamplePosition(target, out NavMeshHit hit, navSampleRadius, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
            }
            else
            {
                transform.position = target;
            }

            // ⚠️ Zombies arrive from OUTSIDE the play area, so "is it on the NavMesh" is the only sane
            // check here — a boundary test would reject every legitimate spawn.
            _navMissing = !agent.isOnNavMesh;
            if (_navMissing)
            {
                Debug.LogWarning("[Zombie] NavMesh bulunamadı — zombi olduğu yerde duruyor. " +
                                 "Arena sahnesinde NavMesh bake'i (dış yollar dahil) eksik.", this);
            }
        }

        private void Update()
        {
            if (_net == null || _net.NetId <= 0 || !_net.IsMine)
            {
                return;
            }

            if (_net.IsBroken)
            {
                if (agent != null && agent.enabled)
                {
                    agent.enabled = false;
                }

                return;
            }

            StreamPose();

            if (_navMissing || agent == null || !agent.enabled || !agent.isOnNavMesh)
            {
                return;
            }

            if (_attackAt > 0f)
            {
                FinishAttack();
                return;
            }

            Hunt();
        }

        /// <summary>Walks toward the nearest chaseable player and starts a melee when close enough.</summary>
        private void Hunt()
        {
            _retargetTimer -= Time.deltaTime;

            if (!TryFindTarget(out int targetId, out Vector3 targetFloor))
            {
                agent.isStopped = true;
                return;
            }

            if (_retargetTimer <= 0f)
            {
                _retargetTimer = retargetSeconds;
                agent.isStopped = false;
                agent.SetDestination(targetFloor);
            }

            if (Time.time < _cooldownUntil)
            {
                return;
            }

            Vector3 flat = targetFloor - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > ZombieKinds.AttackReach * ZombieKinds.AttackReach)
            {
                return;
            }

            _attackTargetId = targetId;
            _attackAt = Time.time + attackWindupSeconds;
            agent.isStopped = true;

            // Turn to the target: the lunge comes from the view and would otherwise swing sideways.
            if (flat.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            }
        }

        /// <summary>Windup over: the attack goes out as an <c>object_event</c>. ⚠️ No damage number rides
        /// it — the server reads that from the wave table, so a tampered client cannot pick it
        /// (§10.5).</summary>
        private void FinishAttack()
        {
            if (Time.time < _attackAt)
            {
                return;
            }

            _attackAt = 0f;
            _cooldownUntil = Time.time + ZombieKinds.AttackCooldownSeconds;

            if (_attackTargetId > 0)
            {
                NetObjectSync.SendEvent(_net.NetId, ZombieKinds.EventAttack, new[] { _attackTargetId });
            }

            _attackTargetId = 0;
        }

        /// <summary>Nearest player who is alive, calibrated and not spawn protected; their HEAD pose
        /// projected onto our own floor level (the agent walks on a plane, the head does not).</summary>
        private bool TryFindTarget(out int playerId, out Vector3 floorPosition)
        {
            playerId = 0;
            floorPosition = Vector3.zero;

            ArenaClient client = ArenaClient.Instance;
            RemotePlayerRegistry remotes = client != null ? client.Remotes : null;
            if (remotes == null)
            {
                return false;
            }

            remotes.GetActivePlayerIds(_playerIds);
            float best = float.MaxValue;

            for (int i = 0; i < _playerIds.Count; i++)
            {
                int id = _playerIds[i];
                if (!ZombieRoster.CanBeChased(id) || !remotes.IsAlive(id) || remotes.IsSpawnProtected(id))
                {
                    continue;
                }

                if (!remotes.GetInterpolatedPose(id, out Pose head, out _, out _))
                {
                    continue;
                }

                Vector3 world = ArenaSpace.ArenaToWorld(head.position);
                world.y = transform.position.y;

                float distance = (world - transform.position).sqrMagnitude;
                if (distance >= best)
                {
                    continue;
                }

                best = distance;
                playerId = id;
                floorPosition = world;
            }

            return playerId > 0;
        }

        /// <summary>Streams the pose at <c>OBJECT_POSE_RATE_HZ</c> — the only thing every other headset
        /// knows about this zombie.</summary>
        private void StreamPose()
        {
            _poseTimer -= Time.deltaTime;
            if (_poseTimer > 0f)
            {
                return;
            }

            _poseTimer = PoseSendInterval;

            ArenaClient client = ArenaClient.Instance;
            UdpStateChannel channel = client != null ? client.UdpChannel : null;
            if (channel == null)
            {
                return;
            }

            transform.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);
            channel.SendObjectPose(_net.NetId, ArenaSpace.WorldToArena(new Pose(position, rotation)));
        }
    }
}
