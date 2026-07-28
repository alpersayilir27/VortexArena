#if UNITY_EDITOR
using System.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App
{
    /// <summary>
    /// YALNIZ EDITOR — geliştirici seçiminin runtime tarafı. `Tools &gt; VortexArena &gt; Dev`
    /// penceresinin `EditorPrefs`'e yazdığı rol/adres/takım seçimini Play başlarken uygular.
    ///
    /// <para><b>İki katmanlı config'in "seçim" katmanı burasıdır.</b> Hedeflerin adlandırılmış
    /// listesi repo'da commit'lidir (`dev-targets.json` → `DevTargets`), ama o listeden HANGİ
    /// hedefin seçili olduğu kişiseldir ve `EditorPrefs`'te durur. Sebep: anlık IP seçimi
    /// commit'lenirse ekip sürekli birbirinin ayarını ezer ve `git status` hep kirli kalır
    /// (klasik "checked-in user settings" tuzağı). Bu yüzden rol/hedef değiştirmek HİÇBİR
    /// dosyayı kirletmez — eskiden `AppBoot`'ta [SerializeField] olduğu için Boot.unity
    /// kirleniyordu.</para>
    ///
    /// <para><b>İki iş yapar:</b></para>
    /// <list type="number">
    /// <item>`BeforeSceneLoad`: rol + sunucu adresini <see cref="AppSession"/>'a yazar
    ///   (`RoleResolved = true` → <see cref="AppBoot"/> ve kabuk controller'ları kendi
    ///   varsayılanlarını yazmaz).</item>
    /// <item>`AfterSceneLoad`: doğrudan bir ARENA sahnesinden Play'e basıldıysa (a) sunucuya
    ///   **bağlanır** ve (b) player rolünde sentetik bir `load_match` yayınlar — takım/spawn slot
    ///   bilgisi böylece gerçek kod yolundan (`PlayerCombatState`, `ModeHudSpawner`,
    ///   `SceneRouter`) geçer. Dev'e özel ikinci bir API açmaktan kaçınmanın sebebi bu: editörde
    ///   denenen akış sahada koşanla aynı kalsın.</item>
    /// </list>
    ///
    /// <para><b>Bağlanmayı neden burası yapıyor:</b> `Connect(...)` çağrısı normalde kabuk
    /// sahnesinin controller'ından gelir (`LobbyController` — her rolde) —
    /// ama arena sahnelerinde o controller YOKTUR. Doğrudan bir arena sahnesinden Play'e
    /// basıldığında kimse bağlanmazdı: can/skor/faz güncellemesi gelmez, `CanFire` sunucu `Live`
    /// demediği için hiç açılmaz ve maç akışı denenemezdi.</para>
    ///
    /// <para>Sunucuda gerçekten bir maç koşuyorsa <b>sunucunun gerçek `load_match`'i kazanır:</b>
    /// bağlanınca `welcome.match` geç-katılım senkronu devreye girer
    /// (`SceneRouter.HandleConnected`) ve takımı sunucu atar. Sentetik mesaj yalnız "sunucuda maç
    /// yokken yerel çalışma" için vardır.</para>
    ///
    /// <para><b>Kapatmak:</b> dev penceresindeki "Dev enjeksiyonu" onayı kapatılırsa bu sınıf
    /// hiçbir şey yapmaz ve üretim yolu birebir koşar (beacon keşfini editörde denemek için).
    /// Adres alanı boş bırakılırsa da adres YAZILMAZ, keşif zinciri devralır.</para>
    ///
    /// Dosyanın tamamı <c>#if UNITY_EDITOR</c> içindedir → hiçbir build'e girmez.
    /// </summary>
    public class DevSession : MonoBehaviour
    {
        // ------------------------------------------------------- EditorPrefs anahtarları
        // Tek yerde durur; dev penceresi (VortexArena.App.Editor) da bu sabitleri kullanır
        // ki anahtar adları iki tarafta dağılmasın.

        private const string Prefix = "VortexArena.Dev.";

        public const string KeyEnabled = Prefix + "Enabled";
        public const string KeyRole = Prefix + "Role";
        public const string KeyTargetName = Prefix + "TargetName";
        public const string KeyIp = Prefix + "Ip";
        public const string KeyPort = Prefix + "Port";
        public const string KeyStartFromBoot = Prefix + "StartFromBoot";
        public const string KeyTeam = Prefix + "Team";
        public const string KeyModeId = Prefix + "ModeId";
        public const string KeyRoundSeconds = Prefix + "RoundSeconds";
        public const string KeyScoreLimit = Prefix + "ScoreLimit";

        // ------------------------------------------------------------------- seçim

        /// <summary>Dev enjeksiyonu açık mı? Kapalıyken üretim yolu birebir koşar.</summary>
        public static bool Enabled
        {
            get => EditorPrefs.GetBool(KeyEnabled, true);
            set => EditorPrefs.SetBool(KeyEnabled, value);
        }

        /// <summary>"player" | "admin".</summary>
        public static string Role
        {
            get => EditorPrefs.GetString(KeyRole, AppSession.RoleAdmin) == AppSession.RolePlayer
                ? AppSession.RolePlayer
                : AppSession.RoleAdmin;
            set => EditorPrefs.SetString(KeyRole, value);
        }

        /// <summary>Dev penceresinde seçili hedefin adı (yalnız UI durumu).</summary>
        public static string TargetName
        {
            get => EditorPrefs.GetString(KeyTargetName, "");
            set => EditorPrefs.SetString(KeyTargetName, value);
        }

        /// <summary>Sunucu IP'si. <b>Boş = adres yazma</b>, keşif zinciri devralsın.</summary>
        public static string Ip
        {
            get => EditorPrefs.GetString(KeyIp, "127.0.0.1");
            set => EditorPrefs.SetString(KeyIp, value ?? "");
        }

        public static int Port
        {
            get => EditorPrefs.GetInt(KeyPort, ArenaProtocol.CONTROL_PORT);
            set => EditorPrefs.SetInt(KeyPort, value);
        }

        /// <summary>true = Play her zaman Boot'tan koşar; false = açık sahneden koşar.</summary>
        public static bool StartFromBoot
        {
            get => EditorPrefs.GetBool(KeyStartFromBoot, true);
            set => EditorPrefs.SetBool(KeyStartFromBoot, value);
        }

        /// <summary>Sentetik load_match'in takımı ("red" | "blue").</summary>
        public static string Team
        {
            get => EditorPrefs.GetString(KeyTeam, "red") == "blue" ? "blue" : "red";
            set => EditorPrefs.SetString(KeyTeam, value);
        }

        /// <summary>Sentetik load_match'in modu (ModeHudSpawner HUD'ı buna göre seçer).</summary>
        public static string ModeId
        {
            get => EditorPrefs.GetString(KeyModeId, "tdm");
            set => EditorPrefs.SetString(KeyModeId, value ?? "");
        }

        public static int RoundSeconds
        {
            get => EditorPrefs.GetInt(KeyRoundSeconds, 300);
            set => EditorPrefs.SetInt(KeyRoundSeconds, value);
        }

        public static int ScoreLimit
        {
            get => EditorPrefs.GetInt(KeyScoreLimit, 50);
            set => EditorPrefs.SetInt(KeyScoreLimit, value);
        }

        /// <summary>Seçimin tek satırlık özeti (pencere başlığı + konsol satırı için).</summary>
        public static string Summary
        {
            get
            {
                string address = HasAddress ? $"{Ip}:{Port}" : "keşif (adres yok)";
                string start = StartFromBoot ? "Boot'tan" : "açık sahneden";
                return $"{Role} · {address} · {start}";
            }
        }

        /// <summary>Adres verildi mi? Boş IP = keşif zinciri kullanılsın demek.</summary>
        public static bool HasAddress => !string.IsNullOrWhiteSpace(Ip) && Port > 0;

        // -------------------------------------------------------------- 1) rol + adres

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplySelection()
        {
            if (!Enabled)
            {
                return;
            }

            AppSession.Role = Role;
            AppSession.RoleResolved = true;

            if (HasAddress)
            {
                AppSession.ServerIp = Ip.Trim();
                AppSession.ServerPort = Port;
            }
            else
            {
                AppSession.ServerIp = "";
                AppSession.ServerPort = 0;
            }

            Debug.Log($"[DevSession] Dev seçimi uygulandı → {Summary}. " +
                      "Değiştirmek için: Tools > VortexArena > Dev (rol: Ctrl+Alt+R).");
        }

        // ---------------------------- 2) arena sahnesinden Play: bağlan + sentetik load_match

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ScheduleArenaSceneSetup()
        {
            if (!Enabled || StartFromBoot)
            {
                return; // Boot'tan koşuyorsak akışı Boot + kabuk controller'ları sürer.
            }

            if (IsShellScene(SceneManager.GetActiveScene().name))
            {
                return; // Lobby kendi bağlanmasını yapar (her rolde), Boot yönlendirir.
            }

            // Bir kare beklemek ZORUNLU: `ArenaClient` ve `SceneRouter` da AfterSceneLoad ile
            // doğuyor ve üç AfterSceneLoad kancasının sırası TANIMSIZ. Sahne Start()'ları da
            // bitsin diye işi bir MonoBehaviour'a bırakıyoruz.
            var go = new GameObject("[DevArenaSceneSetup]");
            DontDestroyOnLoad(go);
            go.AddComponent<DevSession>();
        }

        /// <summary>Sahne bir kabuk sahnesi mi (kendi bağlanma/yönlendirme akışı var mı)?</summary>
        private static bool IsShellScene(string sceneName)
        {
            return sceneName == AppSession.SceneBoot ||
                   sceneName == AppSession.SceneLobby;
        }

        private IEnumerator Start()
        {
            yield return null; // tüm tekiller ve sahne aboneleri (OnEnable/Start) hazır olsun

            ConnectFromArenaScene();
            InjectSyntheticLoadMatch();

            Destroy(gameObject);
        }

        /// <summary>
        /// Arena sahnesinde bağlanmayı üstlenir (kabuk controller'ları burada yok — sınıf
        /// dokümanındaki gerekçe). Adres yoksa bağlanmaz; sebebi loglar, çünkü arena
        /// sahnesinde adres girecek bir arayüz de yoktur.
        /// </summary>
        private void ConnectFromArenaScene()
        {
            if (ArenaClient.Instance == null)
            {
                Debug.LogWarning("[DevSession] ArenaClient tekili yok; bağlanılamadı.");
                return;
            }

            if (ArenaClient.Instance.State != ArenaConnectionState.Disconnected)
            {
                return; // Play sahne değiştirdiyse bağlantı zaten kurulmuş olabilir.
            }

            if (!HasAddress)
            {
                Debug.LogWarning(
                    "[DevSession] Adres yok (hedef 'keşif' kipinde) — arena sahnesinden " +
                    "bağlanılamaz, bu sahnede adres girecek arayüz yok. Dev penceresinde somut " +
                    "bir hedef seçin ya da Boot'tan başlatın.");
                return;
            }

            Debug.Log($"[DevSession] Arena sahnesinden bağlanılıyor: {Ip}:{Port} ({Role}).");
            ArenaClient.Instance.Connect(Ip.Trim(), Port, Role);
        }

        /// <summary>
        /// Takım/mod bilgisini gerçek kod yolundan geçirir. Yalnız player rolünde:
        /// `SceneRouter` admin rolünde `load_match`'i zaten yok sayar.
        /// </summary>
        private void InjectSyntheticLoadMatch()
        {
            if (Role != AppSession.RolePlayer)
            {
                return;
            }

            // Kurallar telde YOK (rules = null): sunucusuz oturumda ModeRuntime onları katalogdan
            // okur (§10.5 K7). Takımı burada kendimiz belirlemek için önce kuralı çözeriz —
            // takımsız modda "red" göndermek oyuncuyu var olmayan bir tabana yönlendirirdi.
            ModeRuntime.ApplyFromCatalog(ModeId);

            var msg = new LoadMatchMsg
            {
                modeId = ModeId,
                sceneName = SceneManager.GetActiveScene().name,
                roundSeconds = RoundSeconds,
                scoreLimit = ScoreLimit,
                yourTeam = ModeRuntime.IsTeamless ? "" : Team
            };

            Debug.Log($"[DevSession] Sentetik load_match → sahne '{msg.sceneName}', mod " +
                      $"'{msg.modeId}', takım {(string.IsNullOrEmpty(msg.yourTeam) ? "yok" : msg.yourTeam)}. " +
                      "(Sunucudan gelmedi; yalnız editör. Sunucuda maç varsa gerçek load_match bunu ezer.)");

            NetEvents.InjectLoadMatch(msg);
        }
    }
}
#endif
