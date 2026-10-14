using UnityEngine;
using VortexArena.Core.Arena;

namespace VortexArena.Modes.Zombie
{
    /// <summary>The blood that stays on the floor under a corpse.</summary>
    /// <remarks>Self-bootstrapping pool, same shape as <c>SurfaceImpactFx</c>: no scene step and no
    /// reference to bind, so a new arena inherits it for free.
    /// <para>⚠️ Instances are POOLED and the oldest is recycled: a wave kills tens of bodies and an
    /// <c>Instantiate</c>/<c>Destroy</c> pair per death is a GC spike on Quest. The cap also bounds
    /// how much overdraw the floor can collect.</para>
    /// <para>⚠️ The prefab is OPTIONAL: with nothing at <see cref="PrefabPath"/> the corpse simply
    /// leaves no pool — a missing decoration must never break a death.</para></remarks>
    public class ZombieBloodPool : MonoBehaviour
    {
        /// <summary>Resources path of the pool prefab; its own fade is the prefab's business.</summary>
        public const string PrefabPath = "Zombie/FX_ZombieBloodPool";

        private const int Capacity = 8;

        /// <summary>How long one pool stays enabled (s).</summary>
        private const float LifetimeSeconds = 12f;

        /// <summary>Floor probe: starts this far above the hips and reaches this far down (m).</summary>
        private const float ProbeUpMeters = 0.4f;

        private const float ProbeDownMeters = 3.5f;

        /// <summary>Lift off the floor (m) so the decal does not z-fight with it.</summary>
        private const float LiftMeters = 0.012f;

        private readonly Transform[] _nodes = new Transform[Capacity];
        private readonly float[] _hideAt = new float[Capacity];
        private int _next;

        private GameObject _prefab;
        private bool _prefabResolved;

        private static ZombieBloodPool _shared;

        public static ZombieBloodPool Shared
        {
            get
            {
                if (_shared == null)
                {
                    var go = new GameObject("[ZombieBloodPool]");
                    DontDestroyOnLoad(go);
                    _shared = go.AddComponent<ZombieBloodPool>();
                }

                return _shared;
            }
        }

        /// <summary>Puts a pool on the floor under <paramref name="bodyWorld"/>, laid on the floor's
        /// normal with a random yaw. Silent no-op with no prefab and no floor under the body.</summary>
        public void Place(Vector3 bodyWorld)
        {
            GameObject prefab = EnsurePrefab();
            if (prefab == null)
            {
                return;
            }

            // Default + Obstacle only: a maskless ray lands on the corpse's own colliders and on the
            // boundary volumes, and the pool ends up floating at hip height.
            if (!Physics.Raycast(bodyWorld + Vector3.up * ProbeUpMeters, Vector3.down,
                    out RaycastHit hit, ProbeDownMeters + ProbeUpMeters, ArenaLayers.AreaTargetMask,
                    QueryTriggerInteraction.Ignore))
            {
                return;
            }

            Transform node = _nodes[_next];
            if (node == null)
            {
                node = Instantiate(prefab, transform).transform;
                node.gameObject.name = "[BloodPool]";
                _nodes[_next] = node;
            }

            _hideAt[_next] = Time.unscaledTime + LifetimeSeconds;
            _next = (_next + 1) % Capacity;

            // Yaw drawn around the FLOOR normal, not around world up: on a ramp the two differ and a
            // world-up yaw lifts one edge of the decal off the surface.
            node.SetPositionAndRotation(
                hit.point + hit.normal * LiftMeters,
                Quaternion.AngleAxis(Random.Range(0f, 360f), hit.normal) *
                Quaternion.FromToRotation(Vector3.up, hit.normal));

            node.gameObject.SetActive(false);
            node.gameObject.SetActive(true);
        }

        private void Update()
        {
            float now = Time.unscaledTime;

            for (int i = 0; i < Capacity; i++)
            {
                Transform node = _nodes[i];
                if (node == null || !node.gameObject.activeSelf || now < _hideAt[i])
                {
                    continue;
                }

                node.gameObject.SetActive(false);
            }
        }

        private GameObject EnsurePrefab()
        {
            if (!_prefabResolved)
            {
                _prefabResolved = true;
                _prefab = Resources.Load<GameObject>(PrefabPath);
            }

            return _prefab;
        }

        private void OnDestroy()
        {
            if (_shared == this)
            {
                // Never leave the static on a destroyed component (required with domain reload off).
                _shared = null;
            }
        }
    }
}
