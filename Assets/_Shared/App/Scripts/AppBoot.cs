using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Protocol;

namespace VortexArena.App
{
    /// <summary>
    /// Boot sahnesi (index 0): rolü ve sunucu adresini belirler, ilgili kabuk sahnesine geçer.
    /// Android → player/Lobby; masaüstü/Editor → `--role player|admin` >
    /// VORTEX_ROLE ortam değişkeni > varsayılan admin/AdminConsole.
    ///
    /// Adres: masaüstü admin build'i **Flutter launcher tarafından** başlatılır ve adres
    /// komut satırından gelir (`--server-ip 192.168.1.10 [--server-port 47821]`). Bu yüzden
    /// AdminConsole'da IP soran bir ekran YOKTUR. Editor'den elle oynatma için
    /// `editorServerIp` alanı fallback'tir.
    /// </summary>
    public class AppBoot : MonoBehaviour
    {
        public const string ArgServerIp = "--server-ip";
        public const string ArgServerPort = "--server-port";
        public const string ArgRole = "--role";

#if UNITY_EDITOR
        [Tooltip("Editor testi için rol override: 'player' | 'admin' (boş = normal çözüm).")]
        [SerializeField] private string editorRoleOverride = "";

        [Tooltip("Editor testi için sunucu adresi (launcher yok). Boş = 127.0.0.1.")]
        [SerializeField] private string editorServerIp = "127.0.0.1";
#endif

        private void Start()
        {
            AppSession.Role = ResolveRole();
            AppSession.RoleResolved = true;
            ResolveServerEndpoint();

            string sceneName = AppSession.Role == AppSession.RoleAdmin
                ? AppSession.SceneAdminConsole
                : AppSession.SceneLobby;

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

#if UNITY_EDITOR
            string fromOverride = NormalizeRole(editorRoleOverride);
            if (fromOverride != null)
            {
                return fromOverride;
            }
#endif

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

        /// <summary>Komut satırı adresini AppSession'a yazar; player rolü keşif zincirini kullanır.</summary>
        private void ResolveServerEndpoint()
        {
            AppSession.ServerIp = "";
            AppSession.ServerPort = 0;

            if (AppSession.Role != AppSession.RoleAdmin)
            {
                return; // VR oyuncusu beacon / arena.json / lobide elle giriş ile bulur.
            }

            string ip = FindArgValue(ArgServerIp);

#if UNITY_EDITOR
            // Launcher yok: Editor'de elle oynatmak için Inspector alanına düş.
            if (string.IsNullOrWhiteSpace(ip))
            {
                ip = string.IsNullOrWhiteSpace(editorServerIp) ? "127.0.0.1" : editorServerIp;
            }
#endif

            if (string.IsNullOrWhiteSpace(ip))
            {
                Debug.LogWarning(
                    $"[AppBoot] Admin rolünde '{ArgServerIp}' verilmedi. Bu build launcher'dan " +
                    "başlatılmalıdır; adres olmadan bağlanılamaz.");
                return;
            }

            AppSession.ServerIp = ip.Trim();
            AppSession.ServerPort = int.TryParse(FindArgValue(ArgServerPort), out int port) && port > 0
                ? port
                : ArenaProtocol.CONTROL_PORT;
        }

        /// <summary>`--ad deger` çiftini komut satırından okur; yoksa null.</summary>
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
