using UnityEngine;

namespace VortexArena.Core.FX
{
    /// <summary>
    /// Keeps an ambience (weather) particle volume above the local camera.
    /// <para>
    /// Why it is needed: the job of the near-field layer is to pass particles RIGHT IN FRONT OF THE
    /// PLAYER'S EYES. Filling a 12x12 m arena volume at that density would take thousands of
    /// particles; instead a small volume is carried along with the head. That way ~80 particles give
    /// the depth impression of thousands.
    /// </para>
    /// ⚠️ The attached particle systems must be <b>Simulation Space = World</b>; otherwise the
    /// particles are dragged along with the volume and the snow "sticks" to the head.
    /// <see cref="Start"/> checks this and warns on deviation.
    /// <para>
    /// ⚠️ FREE-ROAM: only THIS object is moved. The rig/player is never touched.
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

        /// <summary>Binds the follow target from code (a prefab cannot hold a scene reference).</summary>
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
                // The rig may not be ready while the scene loads: retry once a second instead of
                // searching every frame.
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

            // Position only: the volume does NOT rotate with the head, otherwise the emission box
            // would swing around with the gaze.
            transform.position = target.position + offset;
        }
    }
}
