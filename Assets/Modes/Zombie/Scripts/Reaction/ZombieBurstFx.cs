using UnityEngine;

namespace VortexArena.Modes.Zombie
{
    /// <summary>Plays the big blood bursts of a head pop / limb sever from an existing impact prefab.</summary>
    /// <remarks>⚠️ The prefab is replayed through a small POOL, not instantiated per burst: the
    /// <c>FX_Impact_*</c> prefabs carry particle systems, and a destroy per kill churns the GC in the
    /// middle of a wave.
    /// <para>⚠️ Why not <c>ArenaCombat.ReportImpact</c>: that resolves the surface from a
    /// <c>RaycastHit</c>, and a sever has no ray to hand it.</para></remarks>
    internal class ZombieBurstFx : MonoBehaviour
    {
        /// <summary>Nodes per prefab and how long one stays enabled (s) — longer than any of the
        /// impact graphs, which stop themselves.</summary>
        private const int NodesPerPrefab = 4;

        private const float VisibleSeconds = 3f;

        /// <summary>Distinct prefabs served at once (head burst + sever burst).</summary>
        private const int MaxPrefabs = 4;

        private static ZombieBurstFx _shared;

        public static ZombieBurstFx Shared
        {
            get
            {
                if (_shared == null)
                {
                    var go = new GameObject("[ZombieBurstFx]");
                    DontDestroyOnLoad(go);
                    _shared = go.AddComponent<ZombieBurstFx>();
                }

                return _shared;
            }
        }

        private readonly GameObject[] _prefabs = new GameObject[MaxPrefabs];
        private readonly Transform[][] _nodes = new Transform[MaxPrefabs][];
        private readonly float[][] _hideAt = new float[MaxPrefabs][];
        private readonly int[] _next = new int[MaxPrefabs];
        private int _used;

        /// <summary>Plays <paramref name="prefab"/> at a point, facing <paramref name="direction"/>.
        /// Silent no-op with no prefab.</summary>
        public void Play(GameObject prefab, Vector3 point, Vector3 direction)
        {
            if (prefab == null)
            {
                return;
            }

            int pool = IndexOf(prefab);
            if (pool < 0)
            {
                return;
            }

            int slot = _next[pool];
            _next[pool] = (slot + 1) % NodesPerPrefab;

            Transform node = _nodes[pool][slot];
            if (node == null)
            {
                node = Instantiate(prefab, transform).transform;
                node.gameObject.name = "[Burst]";
                _nodes[pool][slot] = node;
            }

            Quaternion rotation = direction.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(direction.normalized)
                : Quaternion.identity;

            node.SetPositionAndRotation(point, rotation);
            _hideAt[pool][slot] = Time.unscaledTime + VisibleSeconds;

            // Off/on restarts the particle graph — the same trick ZombieBloodPool uses.
            node.gameObject.SetActive(false);
            node.gameObject.SetActive(true);
        }

        private int IndexOf(GameObject prefab)
        {
            for (int i = 0; i < _used; i++)
            {
                if (_prefabs[i] == prefab)
                {
                    return i;
                }
            }

            if (_used >= MaxPrefabs)
            {
                return -1;
            }

            int index = _used++;
            _prefabs[index] = prefab;
            _nodes[index] = new Transform[NodesPerPrefab];
            _hideAt[index] = new float[NodesPerPrefab];
            return index;
        }

        private void Update()
        {
            float now = Time.unscaledTime;

            for (int pool = 0; pool < _used; pool++)
            {
                Transform[] nodes = _nodes[pool];
                for (int i = 0; i < nodes.Length; i++)
                {
                    Transform node = nodes[i];
                    if (node == null || !node.gameObject.activeSelf || now < _hideAt[pool][i])
                    {
                        continue;
                    }

                    node.gameObject.SetActive(false);
                }
            }
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
