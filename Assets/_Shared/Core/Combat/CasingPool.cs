using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Tüm silahların kovanlarını üreten TEK havuz; ilk istendiğinde kendini kurar ve
    /// <c>DontDestroyOnLoad</c> olur. Sahneye konmaz, kimse referans bağlamaz — çağıran yalnız
    /// <see cref="ShellEjector"/> üzerinden <c>CasingPool.Shared.Eject(…)</c> der
    /// (<see cref="ShotTracer.Shared"/> ile birebir aynı desen).
    /// <para>⚠️ <b>Havuz silahın üstünde DURAMAZ.</b> Kovan dünya uzayında, silahtan bağımsız
    /// yaşar; ama süreyi işleten <c>Update</c> silahın bileşeninde olursa silah yok edildiği anda
    /// o an açık olan kovan bir daha hiç kapanmaz ve sahnede kalıcılaşır. Silah örneği her
    /// kavra/bırak döngüsünde yeniden yaratılıp yok edildiği için (<see cref="WeaponGranter"/>,
    /// <see cref="WeaponFrame"/>) bu, maç boyunca biriken yüzlerce Rigidbody + Collider demekti.
    /// Havuzun ömrü silahın ömründen UZUN olmak zorundadır — sınıfın var oluş sebebi budur ve
    /// havuz bir daha silah bileşenine geri taşınmaz.</para>
    /// </summary>
    public class CasingPool : MonoBehaviour
    {
        /// <summary>
        /// Kovan prefabı başına eşzamanlı kovan tavanı. Tavana vurulduğunda en eski kovan
        /// erkenden geri alınır — o kovan zaten yerde durduğu için gözle fark edilmez.
        /// <para>⚠️ Havuz artık silah başına DEĞİL <b>prefab başına</b>: aynı kalibreyi taşıyan
        /// iki silah (iki el) bu tavanı paylaşır. Sayı bu yüzden eski silah-başına tavandan
        /// (10) yüksek tutuldu.</para>
        /// </summary>
        private const int PoolSizePerPrefab = 16;

        /// <summary>Kovanın doğuşundan gizlenmesine kadar geçen süre (sn).</summary>
        private const float LifetimeSeconds = 3f;

        /// <summary>Tek bir kovan prefabının round-robin havuzu.</summary>
        private sealed class PrefabPool
        {
            public readonly Transform[] Items = new Transform[PoolSizePerPrefab];
            public readonly Rigidbody[] Bodies = new Rigidbody[PoolSizePerPrefab];

            /// <summary>Slotun gizleneceği an (<c>Time.time</c>).</summary>
            // ⚠️ Coroutine DEĞİL: havuz slotu erken yeniden kullanıldığında eski zamanlayıcı yeni
            // kovanı erken söndürürdü. Slot yeniden kullanıldığında bu alan da yeniden yazılır.
            public readonly float[] ExpireAt = new float[PoolSizePerPrefab];

            public int NextIndex;
        }

        private readonly Dictionary<GameObject, PrefabPool> _pools = new Dictionary<GameObject, PrefabPool>();

        /// <summary>Update'in her karede sözlük yerine üzerinde gezdiği düz liste.</summary>
        private readonly List<PrefabPool> _all = new List<PrefabPool>();

        private static CasingPool _shared;

        /// <summary>
        /// Tüm kovanların kullandığı TEK havuz; ilk istendiğinde kendini kurar. Sahneye konmaz,
        /// prefaba eklenmez — çağıran yalnız <c>CasingPool.Shared.Eject(…)</c> der.
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
            // Havuz DDOL olduğu için kovanlar harita değişiminde kendiliğinden yok olmaz —
            // yeni arenaya eski maçın kovanlarıyla girilmemesi için elle gizlenirler.
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        /// <summary>
        /// Bir kovan fırlatır: havuzdan sıradaki slotu alır, <paramref name="ejectPoint"/>'e
        /// konumlar ve rastgele itki/tork uygular.
        /// </summary>
        /// <param name="casingPrefab">Kovan prefabı (Rigidbody taşır); havuz prefab başınadır.</param>
        /// <param name="ejectPoint">Çıkış noktası — kovanın doğduğu yer VE itki yönünün kaynağı
        /// (<c>right</c> = yana atma). Yeri/dönüşü silah prefabında elle ayarlanır.</param>
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
                // ⚠️ Kovan HAVUZUN ALTINA doğar (ebeveynsiz DEĞİL): ebeveynsiz doğsaydı aktif
                // sahneye düşer ve harita değişiminde yok edilirdi — havuz elinde yok edilmiş
                // referanslarla kalır, o slot bir daha hiç kullanılamazdı. Havuz kökü
                // orijinde/kimlik dönüşümündedir, konumlar dünya uzayında yazıldığı için
                // ebeveynlik kovanın fiziğine hiçbir şey katmaz.
                GameObject go = Instantiate(casingPrefab, transform);
                pool.Items[i] = go.transform;
                pool.Bodies[i] = go.GetComponent<Rigidbody>();
            }

            Transform casing = pool.Items[i];
            Rigidbody body = pool.Bodies[i];

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
                    if (item != null && item.gameObject.activeSelf && now >= pool.ExpireAt[i])
                    {
                        item.gameObject.SetActive(false);
                    }
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
