using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VortexArena.App
{
    /// <summary>
    /// Boot sahnesi (index 0): rolü belirler ve ilgili kabuk sahnesine geçer.
    /// Android → player/Lobby; masaüstü/Editor → `--role player|admin` komut satırı >
    /// VORTEX_ROLE ortam değişkeni > varsayılan admin/AdminConsole.
    /// </summary>
    public class AppBoot : MonoBehaviour
    {
#if UNITY_EDITOR
        [Tooltip("Editor testi için rol override: 'player' | 'admin' (boş = normal çözüm).")]
        [SerializeField] private string editorRoleOverride = "";
#endif

        private void Start()
        {
            AppSession.Role = ResolveRole();
            AppSession.RoleResolved = true;

            string sceneName = AppSession.Role == AppSession.RoleAdmin
                ? AppSession.SceneAdminConsole
                : AppSession.SceneLobby;

            Debug.Log($"[AppBoot] Rol '{AppSession.Role}' → sahne '{sceneName}'.");
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

            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--role")
                {
                    string fromArgs = NormalizeRole(args[i + 1]);
                    if (fromArgs != null)
                    {
                        return fromArgs;
                    }
                }
            }

            string fromEnv = NormalizeRole(Environment.GetEnvironmentVariable("VORTEX_ROLE"));
            if (fromEnv != null)
            {
                return fromEnv;
            }

            return AppSession.RoleAdmin;
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
