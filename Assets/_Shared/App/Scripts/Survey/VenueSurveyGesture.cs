using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core.Arena;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App.Survey
{
    /// <summary>
    /// Persistent watcher for the "enter the venue survey" gesture (A+B for 3 s) and the owner of
    /// the survey scene's controller.
    /// <para>
    /// ⚠️ It watches from EVERY scene, which is why it must go quiet the moment the survey scene is
    /// open: there the controller reads the same buttons, and two readers would consume the finish
    /// gesture twice.
    /// </para>
    /// <para>⚠️ <b>Player role only</b> — the gate lives in <see cref="AppSingletons"/>, not here.</para>
    /// </summary>
    public class VenueSurveyGesture : MonoBehaviour
    {
        public static VenueSurveyGesture Instance { get; private set; }

        private VenueSurveyInput input;

        /// <summary>Installs the singleton. ⚠️ <b>Unconditional</b> — the "is it needed in this
        /// session" decision belongs to <see cref="AppSingletons"/>.</summary>
        internal static void Install()
        {
            if (Instance != null)
            {
                return;
            }

            var go = new GameObject("[VenueSurveyGesture]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<VenueSurveyGesture>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            input = new VenueSurveyInput(VenueSurveyHaptics.Hand);

            SceneManager.sceneLoaded += HandleSceneLoaded;
            NetEvents.OnVenueSurveyResult += HandleResult;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            NetEvents.OnVenueSurveyResult -= HandleResult;

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            // Inside the survey scene the controller owns the input.
            if (SceneManager.GetActiveScene().name == AppSession.SceneVenueSurvey)
            {
                input.Reset();
                return;
            }

            input.Tick();
            if (!input.EnterExitFired)
            {
                return;
            }

            SceneRouter router = SceneRouter.Instance;

            // A match scene is the server's, not the player's: leaving it locally would drop the
            // player out of a running match with no way for the server to know.
            if (router != null && router.LastMatchScene.Length > 0)
            {
                VenueSurveyHaptics.Pulse(this, 3);
                Debug.LogWarning("[VenueSurvey] Maç sırasında mekan ölçümü alınamaz.");
                return;
            }

            string current = SceneManager.GetActiveScene().name;
            VenueSurveyContext.CaptureFrom(ArenaBoundary.Active, current);

            VenueSurveyHaptics.Pulse(this, 1, VenueSurveyHaptics.Long);
            Debug.Log(
                $"[VenueSurvey] Ölçüm başlatıldı (kaynak sahne '{current}', mekan " +
                $"'{VenueSurveyContext.VenueName}', şablon " +
                $"{(VenueSurveyContext.HasTemplate ? "var" : "yok")}).");

            if (router == null)
            {
                Debug.LogError("[VenueSurvey] SceneRouter yok; ölçüm sahnesi açılamadı.");
                VenueSurveyContext.Reset();
                return;
            }

            router.LoadLocalScene(AppSession.SceneVenueSurvey);
        }

        /// <summary>The survey scene carries no components of its own — its controller is created
        /// here, in the scene, so it dies with the scene.</summary>
        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != AppSession.SceneVenueSurvey)
            {
                return;
            }

            var go = new GameObject("[VenueSurvey]");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<VenueSurveyController>();
        }

        private void HandleResult(VenueSurveyResultMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            if (msg.ok)
            {
                Debug.Log($"[VenueSurvey] Sunucu ölçüm dosyasını yazdı: {msg.file}");
                VenueSurveyHaptics.Pulse(this, 1, VenueSurveyHaptics.Long);
                return;
            }

            Debug.LogError($"[VenueSurvey] Sunucu ölçümü reddetti: {msg.error}");
            VenueSurveyHaptics.Pulse(this, 3);
        }
    }
}
