using UnityEngine;

namespace VortexArena.Core.FX
{
    /// <summary>
    /// Bir ambiyans (hava durumu) parçacık hacmini yerel kameranın üzerinde tutar.
    /// <para>
    /// Neden gerekli: yakın alan katmanının işi oyuncunun GÖZÜNÜN ÖNÜNDEN parçacık
    /// geçirmektir. 12x12 m'lik arena hacmine bunu yapacak yoğunlukta parçacık koymak
    /// binlerce parçacık ister; bunun yerine küçük bir hacim kafayla taşınır. Böylece
    /// ~80 parçacık, binlercesinin verdiği derinlik hissini verir.
    /// </para>
    /// ⚠️ Bağlı parçacık sistemleri <b>Simulation Space = World</b> olmalıdır; aksi halde
    /// parçacıklar hacimle sürüklenir ve kar kafaya "yapışır". <see cref="Start"/> bunu
    /// denetler ve sapmada uyarır.
    /// <para>
    /// ⚠️ FREE-ROAM: yalnız BU obje taşınır. Rig'e/oyuncuya asla dokunulmaz.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("VortexArena/FX/Weather Volume Follow")]
    public class WeatherVolumeFollow : MonoBehaviour
    {
        [Tooltip("Takip edilecek kafa/kamera. Boş bırakılırsa çalışma anında Camera.main üzerinden çözülür.")]
        [SerializeField] private Transform target;

        [Tooltip("Hedefe eklenen DÜNYA uzayı ofseti (metre). Ağırlığı Y'de tut: hacim kafanın " +
                 "ÜSTÜNDE olmalı, yoksa oyuncu rüzgar yönüne baktığında hacim görüş alanının " +
                 "arkasında kalır. XZ yalnız hafif bir rüzgar sapması içindir.")]
        [SerializeField] private Vector3 offset = new Vector3(0.35f, 1.3f, -0.25f);

        private float retrySeconds;

        /// <summary>Takip hedefini koddan bağlar (prefab'da sahne referansı tutulamadığı için).</summary>
        public void SetTarget(Transform head)
        {
            target = head;
        }

        private void Start()
        {
            ParticleSystem[] systems = GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i].main.simulationSpace != ParticleSystemSimulationSpace.World)
                {
                    Debug.LogWarning(
                        $"[WeatherVolumeFollow] '{systems[i].name}' Simulation Space = " +
                        $"{systems[i].main.simulationSpace}. Hacim kamerayı takip ettiği için " +
                        "parçacıklar kafaya yapışır — World'e alın.", systems[i]);
                }
            }
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                // Rig sahne yüklenirken hazır olmayabilir: her karede aramak yerine saniyede bir dene.
                retrySeconds -= Time.deltaTime;
                if (retrySeconds > 0f)
                {
                    return;
                }

                retrySeconds = 1f;
                Camera camera = Camera.main;
                if (camera == null)
                {
                    return;
                }

                target = camera.transform;
            }

            // Yalnız konum: hacim kafayla DÖNMEZ, yoksa emisyon kutusu bakışla savrulur.
            transform.position = target.position + offset;
        }
    }
}
