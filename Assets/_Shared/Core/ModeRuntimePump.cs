using UnityEngine;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Core
{
    /// <summary>
    /// <see cref="ModeRuntime"/>'ın tek besleme noktası: <c>load_match</c> / <c>welcome</c>
    /// dinler, lobiye dönüşte ve bağlantı kopunca varsayılana çeker.
    /// <para>
    /// Sahnede DURMAZ: <c>load_match</c> sahne yüklenmeden ÖNCE gelir, bu yüzden
    /// <c>PlayerCombatState</c>/<c>ArenaClient</c> deseniyle kendini önyükler
    /// (<c>AfterSceneLoad</c> + <c>DontDestroyOnLoad</c>).
    /// </para>
    /// <para>
    /// Kuralları okuyan bileşenler aynı kancayla doğuyor ve üç <c>AfterSceneLoad</c> çağrısının
    /// sırası TANIMSIZ — ama bu bir yarış değil: kural mesajı ancak bir ağ bağlantısı kurulduktan
    /// sonra gelebilir, o da en erken sahne <c>Start()</c>'larında olur.
    /// </para>
    /// <para>Rol ayrımı yoktur: admin de aynı kuralları alır (takım kipi arayüzün tek/çift kolon
    /// kararını besler).</para>
    /// </summary>
    public class ModeRuntimePump : MonoBehaviour
    {
        private static ModeRuntimePump _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
            {
                return;
            }

            var go = new GameObject("[ModeRuntimePump]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ModeRuntimePump>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;

            // Kalıcı tekiliz: obje devre dışı bırakılsa bile kural mesajı kaçmasın diye
            // OnEnable/OnDisable yerine Awake/OnDestroy'da abone olunur.
            NetEvents.OnLoadMatch += HandleLoadMatch;
            NetEvents.OnConnected += HandleConnected;
            NetEvents.OnReturnToLobby += HandleReturnToLobby;
            NetEvents.OnDisconnected += HandleDisconnected;
            NetEvents.OnSelectionState += HandleSelectionState;
            NetEvents.OnRulesUpdate += HandleRulesUpdate;

            // Domain reload kapalıyken statikler önceki oturumdan sarkabilir.
            ModeRuntime.Reset();
            ModeSelection.Reset();
        }

        private void OnDestroy()
        {
            if (_instance != this)
            {
                return;
            }

            NetEvents.OnLoadMatch -= HandleLoadMatch;
            NetEvents.OnConnected -= HandleConnected;
            NetEvents.OnReturnToLobby -= HandleReturnToLobby;
            NetEvents.OnDisconnected -= HandleDisconnected;
            NetEvents.OnSelectionState -= HandleSelectionState;
            NetEvents.OnRulesUpdate -= HandleRulesUpdate;

            _instance = null;
        }

        /// <summary>Maç kuruluyor: kurallar bu mesajdan gelir. <c>rules</c> boşsa (kuralları
        /// taşımayan bir sunucu) katalog devralır — <see cref="ModeRuntime.ApplyFromCatalog"/>.</summary>
        private static void HandleLoadMatch(LoadMatchMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            ModeRuntime.Apply(msg.modeId, msg.rules);
        }

        /// <summary>Geç katılım: koşan maçın kuralları <c>welcome.match</c>'ten gelir.</summary>
        private static void HandleConnected(WelcomeMsg msg)
        {
            if (msg?.match == null)
            {
                return;
            }

            // Lobide bekleyen sunucuda mod boştur; o durumda varsayılan zaten doğru cevaptır.
            ModeRuntime.Apply(msg.match.modeId, msg.match.rules);
        }

        /// <summary>Lobiye dönüş: kurallar SIFIRLANMAZ, lobi profili uygulanır (§10.7).
        /// <para>Lobi de içerik taşır — silah loadout'u <c>ModeRuntime.ModeId</c> ile
        /// katalogdan çözülüyor. Mesaj boş <c>modeId</c> taşıyorsa (lobi yapılandırılmamış ya da
        /// eski sunucu) <see cref="ModeRuntime.Apply"/> zaten varsayılana düşer, yani eski
        /// davranışın aynısı olur.</para></summary>
        private static void HandleReturnToLobby(ReturnToLobbyMsg msg)
        {
            if (msg == null)
            {
                ModeRuntime.Reset();
                return;
            }

            ModeRuntime.Apply(msg.modeId, msg.rules);
        }

        /// <summary>
        /// Seçim bildirimi (§5.3 <c>selection_state</c>) — <b>kural DEĞİL sunum</b>: bilerek
        /// <see cref="ModeRuntime"/>'a değil <see cref="ModeSelection"/>'a yazılır.
        /// <para>
        /// ⚠️ Buradan <c>ModeRuntime.Apply</c> çağrılMAZ: seçim koşan maçı değiştirmez ve
        /// çağrılsaydı lobide bekleyen oyuncunun HUD'u ile loadout'u maç başlamadan seçili moda
        /// atlardı (sunucunun sahneleme sırasında <c>modeId</c>'yi bilerek <c>"lobby"</c> tuttuğu
        /// kararın istemci tarafındaki karşılığı, §10.7).
        /// </para>
        /// </summary>
        private static void HandleSelectionState(SelectionStateMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            ModeSelection.Apply(msg.modeId, msg.teamMode);
        }

        /// <summary>
        /// Kural şekli maç ORTASINDA değişti (§5.3 <c>rules_update</c>) — bugün tek sebebi
        /// operatörün dost ateşi anahtarıdır (§5.2).
        /// <para>
        /// ⚠️ <c>HandleSelectionState</c>'in aksine bu <b>uygulanır</b>: seçim gelecekteki bir maçı
        /// anlatır, bu ise koşan maçın kuralını. Otorite yine sunucudadır — istemci yalnız aynasını
        /// tazeler; hasar kararını zaten sunucu veriyor (§10.3).
        /// </para>
        /// </summary>
        private static void HandleRulesUpdate(RulesUpdateMsg msg)
        {
            if (msg?.rules == null)
            {
                return;
            }

            ModeRuntime.Apply(msg.modeId, msg.rules);
        }

        private static void HandleDisconnected()
        {
            ModeRuntime.Reset();
            ModeSelection.Reset();
        }
    }
}
