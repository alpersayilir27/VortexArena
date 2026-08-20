using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// The ONE pool producing every weapon's casings; self-bootstraps on first use and goes
    /// <c>DontDestroyOnLoad</c>. Never placed in a scene, never referenced — callers only say
    /// <c>CasingPool.Shared.Eject(…)</c> through <see cref="ShellEjector"/> (same pattern as
    /// <see cref="ShotTracer.Shared"/>).
    /// <para>⚠️ The pool CANNOT live on the weapon. Casings live in world space, independent of the
    /// weapon; but if the <c>Update</c> driving their lifetime sat on a weapon component, every
    /// casing live at destruction time would never be hidden again and would stay in the scene.
    /// Since the weapon instance is recreated and destroyed on every grab/release cycle
    /// (<see cref="WeaponGranter"/>, <see cref="WeaponFrame"/>) that meant hundreds of accumulated
    /// Rigidbody + Collider pairs per match. The pool must OUTLIVE the weapon — that is why this
    /// class exists, and it never moves back into a weapon component.</para>
    /// </summary>
    public class CasingPool : MonoBehaviour
    {
        /// <summary>
        /// Simultaneous casing cap PER CASING PREFAB; at the cap the oldest casing is recycled early.
        /// <para>⚠️ The pool is per PREFAB, not per weapon: all weapons sharing a calibre (both
        /// hands + every model of that calibre) share this single cap.</para>
        /// <para>⚠️ Grow the cap with the number of weapons sharing a calibre. One weapon keeps
        /// <c>rpm/60 × <see cref="LifetimeSeconds"/></c> casings airborne (666 rpm × 3 s ≈ 33).
        /// Below that, casings are recycled before their lifetime ends and the symptom LOOKS LIKE
        /// "no casings at all": one is born, reused within a few frames, and the player sees only a
        /// flicker. Misleading — no error is logged and the weapon setup looks perfect. With dual
        /// wielding plus several models per calibre the cap is kept near twice one weapon's need.</para>
        /// <para>Cost is lazy: a slot is instantiated only when actually used, so raising this
        /// number costs nothing for a calibre that is never fired.</para>
        /// </summary>
        private const int PoolSizePerPrefab = 64;

        /// <summary>Time from a casing's birth to hiding it (s).</summary>
        private const float LifetimeSeconds = 3f;

        /// <summary>
        /// How long the casing's colliders stay DISABLED after birth (s).
        /// <para>⚠️ This delay is mandatory. The casing is born INSIDE the weapon's own collider:
        /// the eject point is on the body, and the body carries a box collider because it is
        /// grabbable. With two colliders overlapping at birth, PhysX applies depenetration velocity
        /// to separate them, far above the eject impulse (1–2 m/s). The symptom is "no casings at
        /// all": the casing is born and flies off before the eye catches it (measured going below
        /// the floor). It varies per weapon because it depends on how deep the eject point sits in
        /// the box and on that frame's contact resolution — so the same setup appears to work on
        /// one weapon and not another, with no error logged.</para>
        /// <para>The delay is enough to clear the weapon (2 m/s × 0.08 s ≈ 16 cm) and far shorter
        /// than reaching the floor, so the casing still bounces and rolls.</para>
        /// </summary>
        private const float ColliderOffSeconds = 0.08f;

        /// <summary>
        /// Casing depenetration velocity cap (m/s). The delay above is the main defence; this is
        /// the second line (in case the casing is still born inside a hand/arm/wall collider).
        /// Unity's default is 10 m/s, which for a 1 cm object means "vanish from the scene".
        /// </summary>
        private const float MaxDepenetrationSpeed = 1f;

        /// <summary>Round-robin pool of a single casing prefab.</summary>
        private sealed class PrefabPool
        {
            public readonly Transform[] Items = new Transform[PoolSizePerPrefab];
            public readonly Rigidbody[] Bodies = new Rigidbody[PoolSizePerPrefab];

            /// <summary>The slot casing's colliders (to toggle; may sit on children).</summary>
            public readonly Collider[][] Colliders = new Collider[PoolSizePerPrefab][];

            /// <summary>When the collider is re-enabled (<c>Time.time</c>); 0 = nothing pending.</summary>
            public readonly float[] EnableColliderAt = new float[PoolSizePerPrefab];

            /// <summary>When the slot is hidden (<c>Time.time</c>).</summary>
            // ⚠️ NOT a coroutine: on early slot reuse the old timer would kill the new casing
            // early. Reusing a slot rewrites this field.
            public readonly float[] ExpireAt = new float[PoolSizePerPrefab];

            public int NextIndex;
        }

        private readonly Dictionary<GameObject, PrefabPool> _pools = new Dictionary<GameObject, PrefabPool>();

        /// <summary>Flat list Update walks each frame instead of the dictionary.</summary>
        private readonly List<PrefabPool> _all = new List<PrefabPool>();

        private static CasingPool _shared;

        /// <summary>
        /// The ONE pool every casing uses; self-bootstraps on first use. Never placed in a scene or
        /// added to a prefab — callers only say <c>CasingPool.Shared.Eject(…)</c>.
        /// </summary>
        public static CasingPool Shared
        {
            get
            {
                if (_shared == null)
                {
                    var go = new GameObject("[CasingPool]");
                    DontDestroyOnLoad(go);
                    _shared = go.AddComponent<CasingPool>();
                }

                return _shared;
            }
        }

        private void Awake()
        {
            // The pool is DDOL, so casings do not die on a map change — they are hidden by hand so
            // the new arena is not entered with the old match's casings.
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        /// <summary>
        /// Ejects a casing: takes the next pool slot, places it at <paramref name="ejectPoint"/> and
        /// applies random impulse/torque.
        /// </summary>
        /// <param name="casingPrefab">Casing prefab (carries a Rigidbody); the pool is per prefab.</param>
        /// <param name="ejectPoint">Eject point — where the casing is born AND the source of the
        /// impulse direction (<c>right</c> = throw sideways). Its pose is set by hand in the weapon
        /// prefab.</param>
        public void Eject(GameObject casingPrefab, Transform ejectPoint,
            float forceMin, float forceMax, float torque)
        {
            if (casingPrefab == null || ejectPoint == null)
            {
                return;
            }

            if (!_pools.TryGetValue(casingPrefab, out PrefabPool pool))
            {
                pool = new PrefabPool();
                _pools.Add(casingPrefab, pool);
                _all.Add(pool);
            }

            int i = pool.NextIndex;
            pool.NextIndex = (pool.NextIndex + 1) % PoolSizePerPrefab;

            if (pool.Items[i] == null)
            {
                // ⚠️ Born UNDER THE POOL, not parentless: a parentless casing lands in the active
                // scene and is destroyed on a map change, leaving the pool with dead references and
                // a slot that can never be used again. The pool root is at the origin with identity
                // rotation and positions are written in world space, so parenting adds nothing to
                // the casing's physics.
                GameObject go = Instantiate(casingPrefab, transform);
                pool.Items[i] = go.transform;
                pool.Bodies[i] = go.GetComponent<Rigidbody>();
                pool.Colliders[i] = go.GetComponentsInChildren<Collider>(true);

                if (pool.Bodies[i] != null)
                {
                    pool.Bodies[i].maxDepenetrationVelocity = MaxDepenetrationSpeed;
                }
            }

            Transform casing = pool.Items[i];
            Rigidbody body = pool.Bodies[i];

            // ⚠️ Colliders are born DISABLED (rationale: ColliderOffSeconds). Order matters:
            // disable BEFORE SetActive, or the casing produces one frame of contact inside the
            // weapon and depenetration does its damage in that single frame.
            SetCollidersEnabled(pool.Colliders[i], false);
            pool.EnableColliderAt[i] = Time.time + ColliderOffSeconds;

            casing.SetPositionAndRotation(ejectPoint.position, ejectPoint.rotation);
            casing.gameObject.SetActive(true);
            pool.ExpireAt[i] = Time.time + LifetimeSeconds;

            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                Vector3 force = ejectPoint.right * Random.Range(forceMin, forceMax) +
                                ejectPoint.up * Random.Range(0.2f, 0.5f) +
                                ejectPoint.forward * Random.Range(-0.1f, 0.1f);
                body.AddForce(force, ForceMode.VelocityChange);
                body.AddTorque(Random.insideUnitSphere * torque, ForceMode.VelocityChange);
            }
        }

        private void Update()
        {
            float now = Time.time;
            for (int p = 0; p < _all.Count; p++)
            {
                PrefabPool pool = _all[p];
                for (int i = 0; i < PoolSizePerPrefab; i++)
                {
                    Transform item = pool.Items[i];
                    if (item == null || !item.gameObject.activeSelf)
                    {
                        continue;
                    }

                    // Clear of the weapon: re-enable the colliders (see ColliderOffSeconds).
                    if (pool.EnableColliderAt[i] > 0f && now >= pool.EnableColliderAt[i])
                    {
                        SetCollidersEnabled(pool.Colliders[i], true);
                        pool.EnableColliderAt[i] = 0f;
                    }

                    if (now >= pool.ExpireAt[i])
                    {
                        item.gameObject.SetActive(false);
                    }
                }
            }
        }

        private static void SetCollidersEnabled(Collider[] colliders, bool value)
        {
            if (colliders == null)
            {
                return;
            }

            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                {
                    colliders[i].enabled = value;
                }
            }
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            for (int p = 0; p < _all.Count; p++)
            {
                PrefabPool pool = _all[p];
                for (int i = 0; i < PoolSizePerPrefab; i++)
                {
                    Transform item = pool.Items[i];
                    if (item != null)
                    {
                        item.gameObject.SetActive(false);
                    }
                }
            }
        }
    }
}
