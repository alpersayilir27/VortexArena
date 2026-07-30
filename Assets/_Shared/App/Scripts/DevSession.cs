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
    /// penceresinin `EditorPrefs`'e yazdığı rol/adres seçimini Play başlarken uygular.
    ///
    /// <para><b>İki katmanlı config'in "seçim" katmanı burasıdır.</b> Hedeflerin adlandırılmış
    /// listesi repo'da commit'lidir (`dev-targets.json` → `DevTargets`), ama o listeden HANGİ
    /// hedefin seçili olduğu kişiseldir ve `EditorPrefs`'te durur. Sebep: anlık IP seçimi
    /// commit'lenirse ekip sürekli birbirinin ayarını ezer ve `git status` hep kirli kalır
    /// (klasik "checked-in user settings" tuzağı). Bu yüzden rol/hedef değiştirmek HİÇBİR
    /// dosyayı kirletmez ve bu ayarlar `AppBoot`'a [SerializeField] olarak KOYULMAZ — koyulursa
    /// her rol değişimi Boot.unity'yi kirletir.</para>
    ///
    /// <para><b>İki iş yapar:</b></para>
    /// <list type="number">
    /// <item>`BeforeSceneLoad`: rol + sunucu adresini <see cref="AppSession"/>'a yazar
    ///   (`RoleResolved = true` → <see cref="AppBoot"/> ve kabuk controller'ları kendi
    ///   varsayılanlarını yazmaz).</item>
    /// <item>`AfterSceneLoad`: doğrudan bir ARENA sahnesinden Play'e basıldıysa sunucuya
    ///   **bağlanır** — başka hiçbir şey yapmaz. Maç verisi (mod, takım, faz, süre) yalnız
    ///   sunucudan gelir. <b>Sandbox kipinde bağlanmaz</b>, bunun yerine kuralları yerelde
    ///   uygular (aşağı).</item>
    /// </list>
    ///
    /// <para><b>Bağlanmayı neden burası yapıyor:</b> `Connect(...)` çağrısı normalde kabuk
    /// sahnesinin controller'ından gelir (`LobbyController` — her rolde) —
    /// ama arena sahnelerinde o controller YOKTUR. Doğrudan bir arena sahnesinden Play'e
    /// basıldığında kimse bağlanmazdı: can/skor/faz güncellemesi gelmez, `CanFire` sunucu `Live`
    /// demediği için hiç açılmaz ve maç akışı denenemezdi.</para>
    ///
    /// <para><b>Takım/mod/faz bilgisinin TEK kaynağı sunucudur.</b> Bağlanınca `welcome.match`
    /// geç-katılım senkronu devreye girer (`SceneRouter.HandleConnected`) ve takımı sunucu atar.
    /// Sunucuda maç koşmuyorsa istemci maç verisi ALMAZ — bu beklenen davranıştır; bir admin
    /// maçı başlatmalıdır. Bu sınıf sunucudan gelmiş gibi mesaj üretmez.</para>
    ///
    /// <para><b>Sandbox kipi (sunucusuz):</b> silah duruşu/namlu/ses gibi YEREL şeyleri denemek
    /// için sunucu + admin + kalibrasyon üçlüsünü atlar. Sunucuya <b>hiç bağlanılmaz</b> —
    /// kalibrasyon kapısı zaten "hiç bağlanılmadıysa açık"tır (`CalibrationState.IsCalibrated`)
    /// ve `ArenaCombat` kanal yokken sessiz no-op'tur, yani silahlar olduğu gibi çalışır. Kapalı
    /// kalan iki kapıyı tek çağrı açar: <see cref="ModeRuntime.Apply"/> ile `fireWhilePaused`
    /// (faz `playing` olmadan tetik) ve `modeId` (loadout'un okunduğu yer — onsuz raf boş doğar).
    /// <b>Sunucudan gelmiş gibi mesaj üretilmez</b>, yalnız istemci kural durumu yazılır.</para>
    ///
    /// <para>⚠️ Sandbox <b>maç kuralı testi DEĞİLDİR</b>: takım/skor/canlanma alanları
    /// <see cref="ModeRulesInfo"/> varsayılanlarında (TDM) kalır — modun kendi kuralları
    /// sunucudan gelir ve sapmada sunucu kazanır (§10.5). Burada anlamlı olan `modeId`
    /// (loadout) ve silah kaynağıdır.</para>
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
        public const string KeySandbox = Prefix + "Sandbox";
        public const string KeySandboxModeId = Prefix + "SandboxModeId";
        public const string KeySandboxWeapons = Prefix + "SandboxWeapons";

        /// <summary>Sandbox silah kaynağı — <c>ModeRulesInfo.weaponSource</c> tel değerleri
        /// (<c>ModeRuntime.ParseWeapons</c> "random" dışındaki her şeyi raf sayar).</summary>
        public const string WeaponsRack = "rack";

        public const string WeaponsRandom = "random";

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

        /// <summary>
        /// Sunucusuz sandbox kipi: bağlanma yok, kurallar yerelde uygulanır. Yalnız <b>açık
        /// sahneden</b> Play'de anlamlıdır — Boot'tan koşulursa akışı kabuk sahnesi sürer ve
        /// <c>LobbyController</c> bağlanmayı dener.
        /// </summary>
        public static bool Sandbox
        {
            get => EditorPrefs.GetBool(KeySandbox, false);
            set => EditorPrefs.SetBool(KeySandbox, value);
        }

        /// <summary>Sandbox'ta uygulanacak modId — <b>loadout bu anahtardan bulunur</b>
        /// (<c>GameCatalog.FindMode</c>); boşsa silah gelmez.</summary>
        public static string SandboxModeId
        {
            get => EditorPrefs.GetString(KeySandboxModeId, "");
            set => EditorPrefs.SetString(KeySandboxModeId, value ?? "");
        }

        /// <summary><see cref="WeaponsRack"/> | <see cref="WeaponsRandom"/>.</summary>
        public static string SandboxWeapons
        {
            get => EditorPrefs.GetString(KeySandboxWeapons, WeaponsRack) == WeaponsRandom
                ? WeaponsRandom
                : WeaponsRack;
            set => EditorPrefs.SetString(KeySandboxWeapons, value);
        }

        /// <summary>Seçimin tek satırlık özeti (pencere başlığı + konsol satırı için).</summary>
        public static string Summary
        {
            get
            {
                string start = StartFromBoot ? "Boot'tan" : "açık sahneden";
                if (Sandbox)
                {
                    string mode = string.IsNullOrEmpty(SandboxModeId) ? "(mod seçilmedi)" : SandboxModeId;
                    return $"{Role} · SANDBOX (sunucusuz) · {mode} · silah: {SandboxWeapons} · {start}";
                }

                string address = HasAddress ? $"{Ip}:{Port}" : "keşif (adres yok)";
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

            // Sandbox = sunucusuz. Adresi SİLİYORUZ ki kabuk controller'ları ya da keşif zinciri
            // kazara bağlanmasın: tek bir başarılı bağlantı `_hasEverConnected`'i kalıcı olarak
            // açar ve kalibrasyon kapısı (CalibrationState.IsCalibrated) kapanır — yani sandbox'ın
            // sağladığı "kalibresiz ateş" kolaylığı sessizce kaybolurdu.
            if (Sandbox)
            {
                AppSession.ServerIp = "";
                AppSession.ServerPort = 0;
                Debug.Log($"[DevSession] Dev seçimi uygulandı → {Summary}. " +
                          "Değiştirmek için: Tools > VortexArena > Dev (rol: Ctrl+Alt+R).");
                return;
            }

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

        // -------------------------------------- 2) arena sahnesinden Play: sunucuya bağlan

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ScheduleArenaSceneSetup()
        {
            if (!Enabled)
            {
                return;
            }

            if (StartFromBoot)
            {
                // Boot'tan koşuyorsak akışı Boot + kabuk controller'ları sürer.
                if (Sandbox)
                {
                    Debug.LogWarning(
                        "[DevSession] Sandbox kipi AÇIK ama başlangıç \"Boot'tan\" — sandbox " +
                        "uygulanmadı. Dev penceresinde başlangıcı \"Açık sahneden\" yapın.");
                }

                return;
            }

            if (IsShellScene(SceneManager.GetActiveScene().name))
            {
                // Lobby kendi bağlanmasını yapar (her rolde), Boot yönlendirir. Sandbox'ta bu
                // sahnelerin ikisi de anlamsızdır: sandbox oynanan bir sahneden Play'e basmak
                // içindir (mekan lobisi de bir arena kutusudur, bu kontrole takılmaz).
                if (Sandbox)
                {
                    Debug.LogWarning(
                        "[DevSession] Sandbox kipi kabuk sahnesinde (Boot/Lobby) uygulanmaz — " +
                        "test edeceğiniz arena ya da mekan lobisi sahnesini açıp Play'e basın.");
                }

                return;
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

            if (Sandbox)
            {
                ApplySandboxRules();
            }
            else
            {
                ConnectFromArenaScene();
            }

            Destroy(gameObject);
        }

        /// <summary>
        /// Sunucusuz sandbox: kural durumunu YERELDE yazar, hiçbir yere bağlanmaz.
        /// <para>
        /// Bir kare beklemek burada da şart: <c>WeaponRackSpawner.OnEnable</c> kendi
        /// <c>Apply()</c>'ını çoktan koşmuş (modId boş olduğu için rafı boş bırakmış) olur;
        /// <see cref="ModeRuntime.Apply"/> <c>Changed</c>'i tetikleyince raf ikinci kez —
        /// bu sefer dolu — kurulur. Aynı olay <c>WeaponGranter</c>'ı da uyandırır.
        /// </para>
        /// </summary>
        private void ApplySandboxRules()
        {
            string modeId = SandboxModeId;
            if (string.IsNullOrEmpty(modeId))
            {
                Debug.LogWarning(
                    "[DevSession] Sandbox kipinde mod seçilmemiş — silah loadout'u moddan " +
                    "okunduğu için raf boş kalır ve elde silah belirmez. Dev penceresinden " +
                    "bir mod seçin.");
                return;
            }

            // Yalnız iki alan yazılır; gerisi ModeRulesInfo varsayılanlarında (TDM) kalır —
            // sandbox bir maç kuralı testi değildir (sınıf dokümanı).
            var rules = new ModeRulesInfo
            {
                weaponSource = SandboxWeapons,

                // Faz sunucusuz 'paused' kalır; tetiği açan tek kapı budur (§10.5).
                // Hasar yine yoktur — hit_report kapısı her hâlükârda 'playing'dir (§10.3).
                fireWhilePaused = true
            };

            ModeRuntime.Apply(modeId, rules);

            Debug.Log(
                $"[DevSession] SANDBOX (sunucusuz): mod '{modeId}', silah kaynağı " +
                $"'{SandboxWeapons}', serbest atış açık. Sunucuya bağlanılmadı — hasar/skor/faz " +
                "yoktur, kalibrasyon istenmez.");
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
    }
}
#endif
