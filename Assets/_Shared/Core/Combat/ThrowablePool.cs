using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>Pool of thrown-item instances, one ring per item prefab.
    /// <para>Same pattern as <see cref="BlastFxPool"/>: no scene setup step, self-bootstraps on
    /// first use, goes <c>DontDestroyOnLoad</c> and keeps its rings across map changes.</para>
    /// <para>⚠️ The point is NOT the <c>Instantiate</c> cost but the FIRST DRAW: a bomb that is
    /// created the moment it is thrown compiles its own Lit material variants on that frame. Pooled
    /// copies are built and drawn once behind the loading cover (<c>CombatFxWarmup</c>).</para>
    /// <para>⚠️ Only the copy spawned by <see cref="Throwable.SpawnAndArm"/> comes from here. The
    /// wrist holster owns its own instance and destroys it — it lives on the hand, not in a
    /// ring.</para></summary>
    public class ThrowablePool : MonoBehaviour
    {
        /// <summary>Concurrent instances per item prefab.</summary>
        // More than this in the air at once is rare; when full the oldest is taken back.
        public const int NodesPerPrefab = 4;

        /// <summary>Round-robin ring for one item prefab.</summary>
        private sealed class Pool
        {
            public readonly GameObject[] Nodes = new GameObject[NodesPerPrefab];
            public int Next;
        }

        /// <summary>A node held visible by <see cref="Warmup"/> until its deadline.</summary>
        private struct VisibleNode
        {
            public GameObject Instance;
            public float HideAt;
        }

        private readonly Dictionary<GameObject, Pool> _pools = new Dictionary<GameObject, Pool>();
        private readonly List<VisibleNode> _visible = new List<VisibleNode>();

        private static ThrowablePool _shared;

        /// <summary>The ONE pool set every remote throw uses; self-bootstraps on first use and goes
        /// <c>DontDestroyOnLoad</c>. Never placed in a scene, never referenced.</summary>
        public static ThrowablePool Shared
        {
            get
            {
                if (_shared == null)
                {
                    var go = new GameObject("[ThrowablePool]");
                    DontDestroyOnLoad(go);
                    _shared = go.AddComponent<ThrowablePool>();
                }

                return _shared;
            }
        }

        /// <summary>Next instance of this prefab, enabled and ready to be armed. Null if there is no
        /// prefab.
        /// <para>⚠️ A node that is still flying is taken back through
        /// <see cref="Throwable.ReturnToPool"/>, never with a bare <c>SetActive(false)</c>: the
        /// component would keep its armed state and the recycled copy would detonate on its old
        /// fuse.</para>
        /// <para>The instance STAYS under the pool root; the world pose comes from
        /// <see cref="Throwable.Arm"/>.</para></summary>
        public GameObject Take(GameObject prefab)
        {
            if (prefab == null)
            {
                return null;
            }

            Pool pool = ResolvePool(prefab);

            GameObject instance = pool.Nodes[pool.Next];
            if (instance == null)
            {
                instance = CreateNode(prefab);
                pool.Nodes[pool.Next] = instance;
            }
            else if (instance.activeSelf)
            {
                ForceRelease(instance);
            }

            pool.Next = (pool.Next + 1) % NodesPerPrefab;

            // A warmup may still be showing this node; it must not be parked mid-flight.
            ForgetVisible(instance);

            instance.SetActive(true);
            return instance;
        }

        /// <summary>Parks an instance: disabled, inert and back at the pool root.
        /// <para>⚠️ The body is made kinematic through <c>RigidbodyDrive</c> (flag + interpolation
        /// together) and its velocities are cleared, or the next <see cref="Throwable.Arm"/> starts
        /// from the previous flight's momentum.</para></summary>
        public void Release(GameObject instance)
        {
            if (instance == null)
            {
                return;
            }

            // Body first, while the object is still active: the native fields are written on a live
            // component.
            var body = instance.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.detectCollisions = false;
                RigidbodyDrive.SetKinematic(body, true);
            }

            instance.SetActive(false);

            Transform root = instance.transform;
            root.SetParent(transform, false);
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
        }

        /// <summary>Builds ring nodes ahead of time — <c>Instantiate</c> ONLY, left disabled.
        /// <para>⚠️ Nothing is drawn here, so material/PSO compilation is NOT paid; that part is
        /// <see cref="Warmup"/>'s job, behind the loading cover.</para></summary>
        public void Prewarm(GameObject prefab, int count)
        {
            if (prefab == null)
            {
                return;
            }

            Pool pool = ResolvePool(prefab);

            int wanted = Mathf.Min(count, NodesPerPrefab);
            for (int i = 0; i < wanted; i++)
            {
                if (pool.Nodes[i] == null)
                {
                    pool.Nodes[i] = CreateNode(prefab);
                }
            }

            // Next is left alone: prewarming must not shift where the next throw lands in the ring.
        }

        /// <summary>Shows one node at <paramref name="position"/> for a moment so the item's own
        /// materials are drawn once behind the loading cover. The copy stays KINEMATIC and is never
        /// armed, so nothing flies or explodes. Caller must keep the view covered for
        /// <paramref name="visibleSeconds"/>.</summary>
        public void Warmup(GameObject prefab, Vector3 position, float visibleSeconds)
        {
            if (prefab == null)
            {
                return;
            }

            Pool pool = ResolvePool(prefab);

            GameObject instance = pool.Nodes[0];
            if (instance == null)
            {
                instance = CreateNode(prefab);
                pool.Nodes[0] = instance;
            }
            else if (instance.activeSelf)
            {
                // Already in play — a warmup never takes a flying item away from the match.
                return;
            }

            instance.transform.SetPositionAndRotation(position, Quaternion.identity);
            instance.SetActive(true);

            _visible.Add(new VisibleNode
            {
                Instance = instance,
                HideAt = Time.unscaledTime + Mathf.Max(0.02f, visibleSeconds),
            });
        }

        /// <summary>Parks warmup nodes whose time is up. Unscaled time: the warmup runs while the
        /// scene load may have the simulation stopped.</summary>
        private void Update()
        {
            if (_visible.Count == 0)
            {
                return;
            }

            float now = Time.unscaledTime;
            for (int i = _visible.Count - 1; i >= 0; i--)
            {
                VisibleNode node = _visible[i];
                if (node.Instance == null)
                {
                    _visible.RemoveAt(i);
                    continue;
                }

                if (now < node.HideAt)
                {
                    continue;
                }

                Release(node.Instance);
                _visible.RemoveAt(i);
            }
        }

        private void ForgetVisible(GameObject instance)
        {
            for (int i = _visible.Count - 1; i >= 0; i--)
            {
                if (_visible[i].Instance == instance)
                {
                    _visible.RemoveAt(i);
                }
            }
        }

        // ---------------------------------------------------------------------- pool

        private Pool ResolvePool(GameObject prefab)
        {
            if (!_pools.TryGetValue(prefab, out Pool pool))
            {
                pool = new Pool();
                _pools.Add(prefab, pool);
            }

            return pool;
        }

        private GameObject CreateNode(GameObject prefab)
        {
            // Under our DDOL root, so the pool survives a map change.
            GameObject instance = Instantiate(prefab, transform);
            instance.name = "[Throwable:" + prefab.name + "]";

            var throwable = instance.GetComponent<Throwable>();
            if (throwable != null)
            {
                throwable.MarkPooled();
            }

            Release(instance);
            return instance;
        }

        /// <summary>Takes back a node that is still in play, through the component so its armed
        /// state is reset.</summary>
        private void ForceRelease(GameObject instance)
        {
            var throwable = instance.GetComponent<Throwable>();
            if (throwable != null)
            {
                throwable.ReturnToPool();
                return;
            }

            Release(instance);
        }

        private void OnDestroy()
        {
            if (_shared == this)
            {
                // Never leave the static field on a destroyed component: the next Take rebuilds the
                // pools (required when domain reload is disabled).
                _shared = null;
            }
        }
    }
}
