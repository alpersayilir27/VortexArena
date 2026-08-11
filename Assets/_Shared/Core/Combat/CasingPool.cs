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
        /// erkenden geri alınır.
        /// <para>⚠️ Havuz silah başına DEĞİL <b>prefab başına</b>: aynı kalibreyi taşıyan TÜM
        /// silahlar (iki el + o kalibredeki her model) bu tek tavanı paylaşır.</para>
        /// <para>⚠️ <b>Tavan, kalibreyi paylaşan silah sayısıyla birlikte büyütülmelidir</b> ve
        /// hesabı şudur: tek silah <c>rpm/60 × <see cref="LifetimeSeconds"/></c> kadar kovanı aynı
        /// anda havada tutar (666 rpm × 3 sn ≈ 33). Tavan bunun altına düşerse kovan ömrünü
        /// tamamlamadan geri alınır ve belirti "kovan hiç çıkmıyor"a benzer: kovan doğar, birkaç
        /// karede yeniden kullanılır, oyuncu yalnız bir titreme görür. Bu YANILTICIDIR — hata
        /// basılmaz ve silahın kurulumu kusursuz görünür. Çift el + tek kalibrede birden çok model
        /// oynandığı için tavan tek silahın ihtiyacının iki katına yakın tutulur.</para>
        /// <para>Maliyet tembeldir: slot ancak gerçekten kullanıldığında instantiate edilir, yani
        /// bu sayıyı büyütmek hiç ateş edilmeyen bir kalibreye hiçbir şey ödetmez.</para>
        /// </summary>
        private const int PoolSizePerPrefab = 64;

        /// <summary>Kovanın doğuşundan gizlenmesine kadar geçen süre (sn).</summary>
        private const float LifetimeSeconds = 3f;

        /// <summary>
        /// Kovanın collider'ının KAPALI kaldığı süre (sn) — doğduğu andan itibaren.
        /// <para>⚠️ <b>Bu gecikme şart.</b> Kovan, silahın kendi collider'ının İÇİNDE doğar: çıkış
        /// noktası gövdenin bir noktasıdır ve gövde de kavranabilir olduğu için kutu collider
        /// taşır. İki collider iç içe doğduğunda PhysX onları ayırmak için kovana depenetrasyon
        /// hızı bindirir; bu hız fırlatma itkisinin (1–2 m/s) kat kat üstüne çıkabilir. Sonuç
        /// belirtisi <b>"kovan hiç çıkmıyor"</b>dur: kovan doğar, göz kaydedemeden sahneden fırlar
        /// (zeminin altına geçtiği de ölçüldü). Silahtan silaha değişmesi, çıkış noktasının kutu
        /// içindeki derinliğine ve o karedeki temas çözümüne bağlı olmasındandır — yani aynı
        /// kurulum bir silahta çalışır, ötekinde çalışmaz gibi görünür ve hata basılmaz.</para>
        /// <para>Süre kovanın silahtan çıkması için yeter (2 m/s × 0.08 sn ≈ 16 cm) ve zemine
        /// varmasından çok kısadır, yani kovan yine yere çarpıp yuvarlanır.</para>
        /// </summary>
        private const float ColliderOffSeconds = 0.08f;

        /// <summary>
        /// Kovanın depenetrasyon hızı tavanı (m/s). Üstteki gecikme ana savunma; bu ikinci hattır
        /// (kovan yine de bir el/kol/duvar collider'ının içinde doğarsa fırlamasın). Unity'nin
        /// varsayılanı 10 m/s'dir ve 1 cm'lik bir obje için bu "sahneden kaybol" demektir.
        /// </summary>
        private const float MaxDepenetrationSpeed = 1f;

        /// <summary>Tek bir kovan prefabının round-robin havuzu.</summary>
        private sealed class PrefabPool
        {
            public readonly Transform[] Items = new Transform[PoolSizePerPrefab];
            public readonly Rigidbody[] Bodies = new Rigidbody[PoolSizePerPrefab];

            /// <summary>Slotun kovanının collider'ları (kapatıp açmak için; alt objelerde olabilir).</summary>
            public readonly Collider[][] Colliders = new Collider[PoolSizePerPrefab][];

            /// <summary>Collider'ın geri açılacağı an (<c>Time.time</c>); 0 = beklenen bir açılma yok.</summary>
            public readonly float[] EnableColliderAt = new float[PoolSizePerPrefab];

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
                pool.Colliders[i] = go.GetComponentsInChildren<Collider>(true);

                if (pool.Bodies[i] != null)
                {
                    pool.Bodies[i].maxDepenetrationVelocity = MaxDepenetrationSpeed;
                }
            }

            Transform casing = pool.Items[i];
            Rigidbody body = pool.Bodies[i];

            // ⚠️ Collider'lar KAPALI doğar (gerekçe: ColliderOffSeconds). Sıra önemli: kapatma
            // SetActive'ten ÖNCE olmalı, yoksa kovan bir kare silahın içinde temas üretir ve
            // depenetrasyon o tek karede işini yapar.
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

                    // Silahın içinden çıkmış: collider geri açılır (bkz. ColliderOffSeconds).
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
