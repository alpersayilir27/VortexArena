using UnityEngine;
using Random = UnityEngine.Random;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Ateş anında kovan fırlatır: <see cref="Weapon.Fired"/> olayına abone olur,
    /// küçük bir round-robin havuzdan (yüksek RPM'de Instantiate/Destroy tekrarı
    /// olmasın) bir kovan alır, <see cref="ejectPoint"/>'e konumlar, rastgele
    /// itki/tork uygular. Süre kontrolü coroutine DEĞİL <c>Update</c> içinde
    /// <c>Time.time</c> ile yapılır — aksi halde havuz slotu erken yeniden
    /// kullanıldığında eski zamanlayıcı yeni kovanı erken söndürebilir.
    /// </summary>
    [RequireComponent(typeof(Weapon))]
    public class ShellEjector : MonoBehaviour
    {
        private const int PoolSize = 10;
        private const float LifetimeSeconds = 3f;

        [Tooltip("Kovan prefabı (Rigidbody + Collider taşır).")]
        [SerializeField] private GameObject casingPrefab;
        [Tooltip("Kovanın fırlatılacağı nokta (namlu değil, gövdenin yan/kapak tarafı).")]
        [SerializeField] private Transform ejectPoint;
        [SerializeField] private float ejectForceMin = 1.2f;
        [SerializeField] private float ejectForceMax = 2.0f;
        [SerializeField] private float ejectTorque = 6f;

        private Weapon weapon;
        private readonly Transform[] pool = new Transform[PoolSize];
        private readonly Rigidbody[] poolBodies = new Rigidbody[PoolSize];
        private readonly float[] poolExpireAt = new float[PoolSize];
        private int nextIndex;

        private void Awake()
        {
            weapon = GetComponent<Weapon>();
        }

        private void OnEnable()
        {
            if (weapon != null)
                weapon.Fired += HandleFired;
        }

        private void OnDisable()
        {
            if (weapon != null)
                weapon.Fired -= HandleFired;
        }

        private void Update()
        {
            float now = Time.time;
            for (int i = 0; i < PoolSize; i++)
            {
                if (pool[i] != null && pool[i].gameObject.activeSelf && now >= poolExpireAt[i])
                    pool[i].gameObject.SetActive(false);
            }
        }

        private void HandleFired()
        {
            if (casingPrefab == null || ejectPoint == null)
                return;

            int i = nextIndex;
            nextIndex = (nextIndex + 1) % PoolSize;

            if (pool[i] == null)
            {
                GameObject go = Instantiate(casingPrefab);
                pool[i] = go.transform;
                poolBodies[i] = go.GetComponent<Rigidbody>();
            }

            Transform casing = pool[i];
            Rigidbody body = poolBodies[i];

            casing.SetPositionAndRotation(ejectPoint.position, ejectPoint.rotation);
            casing.gameObject.SetActive(true);
            poolExpireAt[i] = Time.time + LifetimeSeconds;

            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                Vector3 force = ejectPoint.right * Random.Range(ejectForceMin, ejectForceMax) +
                                ejectPoint.up * Random.Range(0.2f, 0.5f) +
                                ejectPoint.forward * Random.Range(-0.1f, 0.1f);
                body.AddForce(force, ForceMode.VelocityChange);
                body.AddTorque(Random.insideUnitSphere * ejectTorque, ForceMode.VelocityChange);
            }
        }
    }
}
