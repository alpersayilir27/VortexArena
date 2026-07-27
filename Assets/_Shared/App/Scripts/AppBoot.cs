using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Protocol;

namespace VortexArena.App
{
    /// <summary>
    /// Boot sahnesi (index 0): rolü ve sunucu adresini belirler, **her rolde `Lobby`** sahnesine
    /// geçer. Android → player; masaüstü/Editor → `--role player|admin` > VORTEX_ROLE ortam
    /// değişkeni > varsayılan admin.
    ///
    /// <para>
    /// Faz 6: admin'in ayrı bir kabuk sahnesi (`AdminConsole` dashboard'u) YOK. Admin de
    /// oyuncularla aynı sahnede durur ve sunucunun `load_match`/`return_to_lobby`'siyle onları
    /// takip eder (<c>SceneRouter</c>); sahne üstü yönetim arayüzünü <c>AdminHud</c> çizer.
    /// </para>
    ///
    /// Adres: masaüstü admin build'i **Flutter launcher tarafından** başlatılır ve adres
    /// komut satırından gelir (`--server-ip 192.168.1.10 [--server-port 47821]`). Bu yüzden
    /// oyun içinde IP soran bir ekran YOKTUR. Komut satırı adresi rolden bağımsız
    /// okunur: verilmişse VR oyuncusunda da keşif zincirinin ÜSTÜNDE yer alır
    /// (bkz. LobbyController) — açıkça verilen adres her zaman kazanır.
    ///
    /// Editörde rol/adres seçimi Inspector'dan DEĞİL `Tools > VortexArena > Dev`
    /// penceresinden yapılır (`DevSession` bu değerleri Boot koşmadan önce yazar; burada
    /// yalnız "zaten çözülmüşse dokunma" kuralı vardır). Sebep: [SerializeField] override
    /// her değişiklikte Boot.unity'yi kirletiyordu ve ekipte birbirinin ayarını eziyordu.
    /// </summary>
    public class AppBoot : MonoBehaviour
    {
        public const string ArgServerIp = "--server-ip";
        public const string ArgServerPort = "--server-port";
        public const string ArgRole = "--role";

        private void Start()
        {
            // DevSession (yalnız editör) rolü/adresi Boot'tan önce yazmış olabilir — ezme.
            if (!AppSession.RoleResolved)
            {
                AppSession.Role = ResolveRole();
                AppSession.RoleResolved = true;
                ResolveServerEndpoint();
            }

            // Rol ne olursa olsun tek kabuk: Lobby. Admin gözlemci oradan sunucuyu takip eder.
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
        /// Komut satırı adresini AppSession'a yazar. Rolden BAĞIMSIZ: admin bunu tek adres
        /// kaynağı olarak kullanır, player rolünde ise keşif zincirinin en üstüne oturur
        /// (verilmemişse zincir bugünkü gibi PlayerPrefs > beacon > arena.json ile sürer).
        /// </summary>
        private void ResolveServerEndpoint()
        {
            AppSession.ServerIp = "";
            AppSession.ServerPort = 0;

            string ip = FindArgValue(ArgServerIp);

            if (string.IsNullOrWhiteSpace(ip))
            {
                if (AppSession.Role == AppSession.RoleAdmin)
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
