using System.Collections;
using UnityEngine;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App
{
    /// <summary>
    /// `kicked` geldiğinde OYUNCU uygulamasını kapatır (§5.4). Atılan başlığın açık kalması
    /// operatör için yalan bir durumdu: admin panelinden oyuncu düşer ama başlık sahnede
    /// durmayı sürdürür, kulaklığı elle kapatmak gerekirdi. Atmak = "bu cihaz oturumdan
    /// çıksın" demek olduğu için karşılığı uygulamanın kapanmasıdır.
    ///
    /// **Admin kapanmaz:** operatör penceresi sahadaki tek yönetim aracıdır; bir `kicked`
    /// (ör. `playerId` havuzu dolduğunda gelen ret) operatörü aletsiz bırakmamalı. Admin
    /// yalnız bağlantısız duruma düşer, `ConnectionOverlay` durumu yazar.
    ///
    /// `ArenaClient` `kicked`'i alır almaz bağlantıyı zaten kapatır ve yeniden bağlanmayı
    /// durdurur; buradaki kısa gecikme kapanış el sıkışmasının ve log'un tamamlanması içindir,
    /// oyuncuya bir şey okutmak için değil.
    /// </summary>
    public class KickedShutdown : MonoBehaviour
    {
        /// <summary>Kapanmadan önceki pay (sn, unscaled). Soketin kapanışını ve log'u toparlar.</summary>
        private const float QuitDelaySeconds = 1.5f;

        private static KickedShutdown _instance;

        private bool _quitting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
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
            // Editörde Application.Quit() no-op'tur; oynatmayı durdurmak aynı anlama gelir.
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
