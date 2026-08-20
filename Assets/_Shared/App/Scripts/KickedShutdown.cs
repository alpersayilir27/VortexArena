using System.Collections;
using UnityEngine;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App
{
    /// <summary>
    /// Quits the PLAYER application on `kicked` (§5.4). A kicked headset that stays up lies to the
    /// operator: the player leaves the admin panel but keeps standing in the scene. Kicking means
    /// "this device leaves the session", so the app quits.
    ///
    /// **The admin does not quit:** the operator window is the only on-site management tool; a
    /// `kicked` (e.g. a full `playerId` pool) must not leave the operator without tools. It only
    /// goes disconnected, shown by `ConnectionOverlay`.
    ///
    /// `ArenaClient` already closes the connection on `kicked`; the short delay lets the closing
    /// handshake and the log finish — it is not reading time for the player.
    /// </summary>
    public class KickedShutdown : MonoBehaviour
    {
        /// <summary>Grace before quitting (s, unscaled) — lets the socket close and the log flush.</summary>
        private const float QuitDelaySeconds = 1.5f;

        private static KickedShutdown _instance;

        private bool _quitting;

        /// <summary>Installs the singleton. ⚠️ <b>Unconditional</b> — the "is it needed in this
        /// session" decision belongs to <see cref="AppSingletons"/> (rationale is there).</summary>
        internal static void Install()
        {
            if (_instance != null)
            {
                return;
            }

            var go = new GameObject("[KickedShutdown]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<KickedShutdown>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
        }

        private void OnEnable()
        {
            NetEvents.OnKicked += HandleKicked;
        }

        private void OnDisable()
        {
            NetEvents.OnKicked -= HandleKicked;
        }

        private void HandleKicked(KickedMsg msg)
        {
            if (_quitting)
            {
                return;
            }

            if (AppSession.Role != AppSession.RolePlayer)
            {
                Debug.Log("[KickedShutdown] Admin atıldı — uygulama açık bırakılıyor.");
                return;
            }

            string reason = msg != null && !string.IsNullOrEmpty(msg.reason) ? msg.reason : "-";
            Debug.Log($"[KickedShutdown] Sunucudan atıldık (sebep: {reason}) — uygulama kapanıyor.");

            _quitting = true;
            StartCoroutine(QuitRoutine());
        }

        private IEnumerator QuitRoutine()
        {
            yield return new WaitForSecondsRealtime(QuitDelaySeconds);
            Quit();
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            // Application.Quit() is a no-op in the editor; stopping play mode means the same thing.
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
