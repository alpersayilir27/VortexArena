using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.App.Admin;
using VortexArena.Protocol;

namespace VortexArena.App
{
    /// <summary>
    /// Boot scene (index 0): resolves role + server address, then loads `Lobby` **in every role**.
    /// Android → player; desktop/Editor → `--role player|admin` > VORTEX_ROLE env var > admin.
    ///
    /// <para>
    /// The admin has NO separate shell/dashboard scene: it stands in the players' scene and follows
    /// `load_match`/`return_to_lobby` (<c>SceneRouter</c>), with <c>AdminHud</c> on top.
    /// </para>
    ///
    /// Address: the desktop admin build is started **by the operator launcher** with
    /// `--server-ip 192.168.1.10 [--server-port 47821]`, so there is NO in-game IP screen. The
    /// command line address is read regardless of role and, when given, outranks the VR player's
    /// discovery chain (see LobbyController).
    ///
    /// In the editor the role/address come from `Tools > VortexArena > Development > Dev`, NOT the
    /// Inspector (`DevSession` writes them before Boot; here the only rule is "leave an already
    /// resolved role alone"). ⚠️ A [SerializeField] override dirtied Boot.unity on every change and
    /// team members overwrote each other's settings.
    /// </summary>
    public class AppBoot : MonoBehaviour
    {
        public const string ArgServerIp = "--server-ip";
        public const string ArgServerPort = "--server-port";
        public const string ArgRole = "--role";

        /// <summary>Plays a match recording instead of connecting (§12.4); desktop only.</summary>
        public const string ArgReplay = "--replay";

        private void Start()
        {
            // DevSession (editor only) may have written the role/address before Boot — do not overwrite.
            if (!AppSession.RoleResolved)
            {
                // Replay forces admin: the spectator cameras + HUD it needs only exist there.
                AppSession.Role = AppSession.IsReplay ? AppSession.RoleAdmin : ResolveRole();
                AppSession.RoleResolved = true;
                ResolveServerEndpoint();
            }

            // The admin must not hold XR: on Standalone it auto-starts (needed for Link) and grabs
            // the idle HMD.
            AdminXrRelease.Apply();
            QuestRenderSettings.Install();

            // One shell for every role: Lobby. The admin spectator follows the server from there.
            string sceneName = AppSession.SceneLobby;

            string endpoint = AppSession.HasServerEndpoint
                ? $"{AppSession.ServerIp}:{AppSession.ServerPort}"
                : "(adres yok — keşif kullanılacak)";
            Debug.Log($"[AppBoot] Rol '{AppSession.Role}' → sahne '{sceneName}', sunucu {endpoint}.");
            SceneManager.LoadScene(sceneName);
        }

        private string ResolveRole()
        {
            if (Application.platform == RuntimePlatform.Android)
            {
                return AppSession.RolePlayer;
            }

            string fromArgs = NormalizeRole(FindArgValue(ArgRole));
            if (fromArgs != null)
            {
                return fromArgs;
            }

            string fromEnv = NormalizeRole(Environment.GetEnvironmentVariable("VORTEX_ROLE"));
            if (fromEnv != null)
            {
                return fromEnv;
            }

            return AppSession.RoleAdmin;
        }

        /// <summary>
        /// Writes the command line address into AppSession, INDEPENDENT of the role: the admin's
        /// only address source, and the top of the player's discovery chain (which otherwise stays
        /// PlayerPrefs > beacon > arena.json).
        /// </summary>
        private void ResolveServerEndpoint()
        {
            AppSession.ServerIp = "";
            AppSession.ServerPort = 0;

            string ip = FindArgValue(ArgServerIp);

            if (string.IsNullOrWhiteSpace(ip))
            {
                if (AppSession.Role == AppSession.RoleAdmin && !AppSession.IsReplay)
                {
                    Debug.LogWarning(
                        $"[AppBoot] Admin rolünde '{ArgServerIp}' verilmedi. Bu build launcher'dan " +
                        "başlatılmalıdır; adres olmadan bağlanılamaz.");
                }

                return;
            }

            AppSession.ServerIp = ip.Trim();
            AppSession.ServerPort = int.TryParse(FindArgValue(ArgServerPort), out int port) && port > 0
                ? port
                : ArenaProtocol.CONTROL_PORT;
        }

        /// <summary>
        /// `--replay &lt;dosya&gt;` (see <see cref="AppSession.ReplayPath"/>). ⚠️ Read BEFORE the first
        /// scene, not in <see cref="Start"/>: <c>AppSingletons</c> installs on AfterSceneLoad — earlier
        /// than any Start — and must already know it is a replay session.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResolveReplay()
        {
            // Not on Android: the headset is a player and a recording lives on the operator's PC.
            if (Application.platform == RuntimePlatform.Android)
            {
                return;
            }

            // No argument = leave it alone: in the editor the dev window may have set a path.
            string path = FindArgValue(ArgReplay);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            AppSession.ReplayPath = path.Trim();
            Debug.Log($"[AppBoot] Kayıt oynatma kipi: '{AppSession.ReplayPath}' (rol admin'e sabitlenecek).");
        }

        /// <summary>Reads a `--name value` pair from the command line; null when absent.</summary>
        private static string FindArgValue(string argName)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], argName, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private static string NormalizeRole(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            value = value.Trim().ToLowerInvariant();
            if (value == AppSession.RolePlayer || value == AppSession.RoleAdmin)
            {
                return value;
            }

            return null;
        }
    }
}
