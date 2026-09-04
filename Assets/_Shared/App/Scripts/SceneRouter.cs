using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App
{
    /// <summary>
    /// Turns server scene commands (load_match / return_to_lobby + the welcome late-join sync) into
    /// scene loads — the bridge Net does not provide. Named SceneRouter so it does not shadow
    /// UnityEngine.SceneManagement.SceneManager. Self-bootstrapping persistent singleton.
    /// Also closes the §10.1 Loading step: sends set_ready{true} once the match scene is loaded.
    /// <para>
    /// <b>Role independent:</b> the admin loads the same scene — "always the server's active scene"
    /// is the basis of the spectator view (§2). The role matters in exactly ONE place:
    /// <c>set_ready</c> in <see cref="ReportSceneLoaded"/> is player-only. An admin appearing
    /// "ready" would mislead the operator, and the Loading gate only counts <c>role=player</c>
    /// connections anyway (<c>OnlinePlayersLocked</c>).
    /// </para>
    /// <para>
    /// <b>Loading is asynchronous</b> (<c>LoadSceneAsync</c>) so the game loop keeps running and
    /// <see cref="LoadingOverlay"/> can draw progress. ⚠️ An async load <b>cannot be cancelled</b>:
    /// a new target arriving mid-load is queued and applied when the current load finishes.
    /// </para>
    /// </summary>
    public class SceneRouter : MonoBehaviour
    {
        public static SceneRouter Instance { get; private set; }

        /// <summary>Last match scene requested by the server (load_match / welcome.match); empty in
        /// the lobby.</summary>
        public string LastMatchScene { get; private set; } = "";

        /// <summary>Last mode requested by the server (read by ModeHudSpawner).</summary>
        public string LastModeId { get; private set; } = "";

        /// <summary>Lobby scene reported by the server (§10.7, <c>server.json → lobbyScene</c>).
        /// Empty falls back to the shell <c>Lobby</c> scene — the only scene known before
        /// connecting.</summary>
        public string LobbyScene { get; private set; } = "";

        /// <summary>
        /// The server's <b>open scene</b> (§10.7): the arena during a match, otherwise the lobby or
        /// a staged arena. Never empty while the server is up; empty before connecting.
        /// <para>The admin UI answers "what is open now" from here: its own map cursor is only the
        /// next match's candidate, while the open scene is what the server says.</para>
        /// </summary>
        public string OpenScene => LastMatchScene.Length > 0 ? LastMatchScene : LobbyScene;

        /// <summary>set_ready is sent once per match scene.</summary>
        private string _readyReportedScene = "";

        /// <summary>Scene currently loading asynchronously; empty when idle.</summary>
        private string _loadingScene = "";

        /// <summary>Next scene requested mid-load (an async load cannot be cancelled).</summary>
        private string _queuedScene = "";

        /// <summary>Background loading priority before the transition — restored afterwards.</summary>
        private ThreadPriority _previousLoadingPriority = ThreadPriority.BelowNormal;

        /// <summary>Installs the singleton. ⚠️ <b>Unconditional</b> — the "is it needed in this
        /// session" decision belongs to <see cref="AppSingletons"/> (rationale is there).</summary>
        internal static void Install()
        {
            if (Instance != null)
            {
                return;
            }

            var go = new GameObject("[SceneRouter]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<SceneRouter>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnEnable()
        {
            NetEvents.OnConnected += HandleConnected;
            NetEvents.OnLoadMatch += HandleLoadMatch;
            NetEvents.OnReturnToLobby += HandleReturnToLobby;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDisable()
        {
            NetEvents.OnConnected -= HandleConnected;
            NetEvents.OnLoadMatch -= HandleLoadMatch;
            NetEvents.OnReturnToLobby -= HandleReturnToLobby;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        /// <summary>
        /// On connect, go to the server's <b>open scene</b> — the client's only routing source
        /// (§5.3): the running match's arena (late join), or the lobby / a staged arena. Applies to
        /// the admin too.
        /// </summary>
        private void HandleConnected(WelcomeMsg msg)
        {
            if (msg?.match == null)
            {
                return;
            }

            // A scene also arrives outside a match (§10.7) — the server's OPEN SCENE. ⚠️ That is not
            // a match: RememberMatch is skipped, so no set_ready is sent and ModeHudSpawner does not
            // look for a match HUD. The distinction comes from the TYPE, not the phase (§10.1).
            if (msg.match.modeId == ArenaProtocol.LOBBY_MODE_ID)
            {
                LobbyScene = msg.match.sceneName ?? "";
                LoadLobbyChecked();
                return;
            }

            if (string.IsNullOrEmpty(msg.match.sceneName))
            {
                return;
            }

            RememberMatch(msg.match.modeId, msg.match.sceneName);
            LoadChecked(msg.match.sceneName);
        }

        private void HandleLoadMatch(LoadMatchMsg msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.sceneName))
            {
                Debug.LogError("[SceneRouter] load_match mesajında sahne adı yok; yok sayıldı.");
                return;
            }

            RememberMatch(msg.modeId, msg.sceneName);
            LoadChecked(msg.sceneName);
        }

        /// <summary>Return to lobby (§10.7): the target scene comes from the server, it is not
        /// fixed. <c>LastMatchScene</c> is cleared — the lobby is not a match scene, so no
        /// <c>set_ready</c> may be sent.</summary>
        private void HandleReturnToLobby(ReturnToLobbyMsg msg)
        {
            LastMatchScene = "";
            LastModeId = "";
            _readyReportedScene = "";

            LobbyScene = msg?.sceneName ?? "";
            LoadLobbyChecked();
        }

        /// <summary>
        /// **Admin map preview:** loads the selected arena LOCALLY (nothing is sent to the server),
        /// so the operator can see the map they picked before a match starts.
        /// <para>
        /// ⚠️ Deliberately does NOT touch <see cref="LastMatchScene"/>/<see cref="LastModeId"/> —
        /// those are the server's truth, so a later <c>load_match</c> takes its normal path and the
        /// preview state breaks nothing.
        /// </para>
        /// Admin role only — on the player client the scene decision belongs to the SERVER.
        /// </summary>
        public void LoadPreview(string sceneName)
        {
            if (AppSession.Role != AppSession.RoleAdmin)
            {
                Debug.LogWarning("[SceneRouter] Harita önizlemesi yalnız admin rolünde kullanılır.");
                return;
            }

            if (string.IsNullOrEmpty(sceneName))
            {
                return;
            }

            LoadChecked(sceneName);
        }

        /// <summary>
        /// Loads a player-LOCAL utility scene (the venue survey). Nothing is sent and
        /// <see cref="LastMatchScene"/>/<see cref="LastModeId"/> stay untouched — a later
        /// <c>load_match</c> takes its normal path.
        /// </summary>
        public void LoadLocalScene(string sceneName)
        {
            if (!string.IsNullOrEmpty(sceneName))
            {
                LoadChecked(sceneName);
            }
        }

        /// <summary>Returns to the server's OPEN scene: the running match's arena if any, else the
        /// lobby (with the shell fallback).</summary>
        public void ReturnToOpenScene()
        {
            if (LastMatchScene.Length > 0)
            {
                LoadChecked(LastMatchScene);
            }
            else
            {
                LoadLobbyChecked();
            }
        }

        /// <summary>Stores the server's match target (read by ModeHudSpawner + the ready report).</summary>
        private void RememberMatch(string modeId, string sceneName)
        {
            LastMatchScene = sceneName ?? "";
            LastModeId = modeId ?? "";
            _readyReportedScene = "";
        }

        /// <summary>
        /// Loads the lobby scene, falling back to the shell <c>Lobby</c> when the server's scene is
        /// missing from this build's scene list: leaving the player without a lobby is worse than a
        /// misconfigured one (the connection/recovery UI lives there). The fallback is logged.
        /// </summary>
        private void LoadLobbyChecked()
        {
            string target = LobbyScene;

            if (!string.IsNullOrEmpty(target) && !Application.CanStreamedLevelBeLoaded(target))
            {
                Debug.LogError(
                    $"[SceneRouter] Sunucunun lobi sahnesi '{target}' bu build'in sahne listesinde " +
                    $"yok — kabuk '{AppSession.SceneLobby}' sahnesine dönülüyor. (Build Settings + " +
                    "server.json → lobbyScene uyumunu kontrol edin.)");
                target = "";
            }

            LoadChecked(string.IsNullOrEmpty(target) ? AppSession.SceneLobby : target);
        }

        private void LoadChecked(string sceneName)
        {
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError($"[SceneRouter] '{sceneName}' build listesinde yok; sahne yüklenemedi.");
                return;
            }

            if (_loadingScene.Length > 0)
            {
                // New target mid-load. ⚠️ `LoadSceneAsync` CANNOT be cancelled (delaying activation
                // does not undo the load), so the target is queued for when this load finishes.
                _queuedScene = _loadingScene == sceneName ? "" : sceneName;

                if (_queuedScene.Length > 0)
                {
                    Debug.Log($"[SceneRouter] '{_loadingScene}' yüklenirken '{sceneName}' istendi; " +
                              "sıraya alındı.");
                }

                return;
            }

            if (SceneManager.GetActiveScene().name == sceneName)
            {
                // Already in this scene: sceneLoaded will not fire → report readiness by hand.
                ReportSceneLoaded(sceneName);
                return;
            }

            StartCoroutine(LoadRoutine(sceneName));
        }

        /// <summary>
        /// Async scene load + loading screen. The screen opens BEFORE the load starts (otherwise the
        /// player sees a frozen frame), progress is driven from <c>AsyncOperation.progress</c> and
        /// it closes once the scene is up.
        /// <para>
        /// <c>allowSceneActivation</c> stays default (on): turning it off would create a second
        /// "loaded but not shown" state, and the <c>set_ready</c> gate (§10.1) already waits for a
        /// real load. ⚠️ In this mode <c>progress</c> runs 0..0.9 and jumps to 1 on activation, so
        /// the bar is normalized by 0.9.
        /// </para>
        /// <para>
        /// The <c>set_ready</c> flow is UNCHANGED: <c>sceneLoaded</c> fires during activation and
        /// still calls <see cref="ReportSceneLoaded"/>, which stays the single gate.
        /// </para>
        /// <para>
        /// ⚠️ <b>Async loading is SLOWER than sync with default settings</b>, purely because of
        /// <see cref="Application.backgroundLoadingPriority"/>: Unity gives integration only a small
        /// slice per frame (project default is usually <c>BelowNormal</c>) while <c>LoadScene</c>
        /// finishes in one frame. The priority is raised to <c>High</c> for the transition and
        /// restored to its PREVIOUS value — not a constant, since something else may have set it.
        /// </para>
        /// </summary>
        private IEnumerator LoadRoutine(string sceneName)
        {
            _loadingScene = sceneName;
            Debug.Log($"[SceneRouter] Sahne yükleniyor → '{sceneName}'.");

            LoadingOverlay.Show(sceneName);

            // More time per frame for load integration during the transition; the loading screen
            // must not cost extra seconds. Restored in FinishLoad.
            _previousLoadingPriority = Application.backgroundLoadingPriority;
            Application.backgroundLoadingPriority = ThreadPriority.High;

            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName);
            if (operation == null)
            {
                // Passed the build list check but the load never started (e.g. a corrupt scene).
                Debug.LogError($"[SceneRouter] '{sceneName}' için asenkron yükleme başlatılamadı.");
                FinishLoad();
                yield break;
            }

            while (!operation.isDone)
            {
                LoadingOverlay.SetProgress(Mathf.Clamp01(operation.progress / 0.9f));
                yield return null;
            }

            LoadingOverlay.SetProgress(1f);
            FinishLoad();
        }

        /// <summary>Load finished: restore the priority, close the screen, apply any queued target.</summary>
        private void FinishLoad()
        {
            Application.backgroundLoadingPriority = _previousLoadingPriority;

            _loadingScene = "";
            LoadingOverlay.Hide();

            string queued = _queuedScene;
            _queuedScene = "";

            if (queued.Length > 0)
            {
                LoadChecked(queued);
            }
        }

        // ---------------------------------------------------- §10.1 Loading report

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ReportSceneLoaded(scene.name);

            // Safety net: the routine normally closes the screen. If a scene arrives while no load
            // of ours is running (another path changed the scene, or the routine died), the screen
            // must not stay stuck.
            if (_loadingScene.Length == 0)
            {
                LoadingOverlay.Hide();
            }
        }

        /// <summary>
        /// Sends set_ready{true} ("scene loaded", §10.1) when the loaded scene is the match scene
        /// the server asked for. The lobby scene and the admin role are skipped.
        /// </summary>
        private void ReportSceneLoaded(string sceneName)
        {
            if (AppSession.Role != AppSession.RolePlayer)
            {
                return;
            }

            if (string.IsNullOrEmpty(sceneName) || sceneName != LastMatchScene)
            {
                return;
            }

            if (_readyReportedScene == sceneName)
            {
                return; // once per match scene
            }

            ArenaClient client = ArenaClient.Instance;
            if (client == null || !client.IsConnected)
            {
                return;
            }

            _readyReportedScene = sceneName;
            client.Send(new SetReadyMsg { ready = true });
            Debug.Log($"[SceneRouter] '{sceneName}' yüklendi → set_ready gönderildi.");
        }
    }
}
