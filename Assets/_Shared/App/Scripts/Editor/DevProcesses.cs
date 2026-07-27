using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Protocol;
using Debug = UnityEngine.Debug;

namespace VortexArena.App.Editor
{
    /// <summary>
    /// Dev penceresinin ortam düğmelerinin arkasındaki süreç yönetimi: .NET sunucusunu ve
    /// PoseBot'u editörden başlatır/durdurur.
    ///
    /// <para><b>⚠️ TUZAK 1 — <c>dotnet run</c> ASLA KULLANILMAZ.</b> <c>dotnet run</c> derleyip
    /// asıl exe'yi ÇOCUK süreç olarak doğurur. Parent (dotnet) öldürüldüğünde çocuk hayatta
    /// kalıyor: <c>VortexArena.Server.App.exe</c> YETİM kaldı, 47821'i tutmaya devam etti ve
    /// artık PID takibimizde olmadığı için öldürülemedi → sonraki sunucu porta bind olamadı.
    /// Bu gerçekten yaşandı. Bu yüzden <b>her zaman doğrudan exe</b> başlatılır (aşağıdaki arama
    /// sırası), böylece elimizdeki PID gerçekten öldürülecek sürecin PID'sidir ve
    /// <see cref="Process.Kill()"/> (ağaç değil, tek süreç) yeterlidir.</para>
    ///
    /// <para><b>⚠️ TUZAK 2 — çıktı BORULANMAZ.</b> <c>RedirectStandardOutput/Error = true</c>
    /// yapıp boruyu okumazsan çocuk süreç, boru tamponu dolduğunda yazma çağrısında kilitlenir
    /// (bu projede Flutter launcher'da aynı hata yaşandı: süreç canlı görünür ama donmuştur).
    /// Bu yüzden <c>UseShellExecute = true</c> + <c>CreateNoWindow = false</c>: her süreç KENDİ
    /// konsol penceresinde koşar, boru yoktur, geliştirici sunucu/bot loglarını canlı okur.
    /// Bir dev aracı için zaten istenen davranış budur.</para>
    ///
    /// <para><b>PID takibi domain reload'a dayanır:</b> <see cref="Process"/> nesneleri script
    /// yeniden derlenince kaybolur, bu yüzden yalnız PID'ler <see cref="SessionState"/>'te
    /// tutulur (editör oturumu boyunca yaşar, domain reload'ı aşar). Süreci geri bulurken
    /// PID'e ek olarak <b>süreç adı da doğrulanır</b>: PID'ler işletim sistemi tarafından
    /// yeniden kullanılabilir, yanlış süreci öldürmek felaket olur.</para>
    ///
    /// <para><b>Yetim süreç güvenliği:</b> editör çökerse PID kaydı kaybolur. Bu yüzden
    /// <see cref="StopAll"/> ve <see cref="SweepOrphans"/> ayrıca AD BAZLI süpürme yapar —
    /// yetim süreç tuzağının kesin çözümü bu.</para>
    ///
    /// Tüm hatalar yakalanır ve anlamlı <see cref="Debug.LogError"/> mesajına çevrilir; bu sınıf
    /// editörü kırmamalıdır.
    /// </summary>
    public static class DevProcesses
    {
        /// <summary>Sunucu exe'sinin adı (uzantısız = <see cref="Process.ProcessName"/> ile aynı).</summary>
        public const string ServerProcessName = "VortexArena.Server.App";

        /// <summary>PoseBot exe'sinin adı (uzantısız = <see cref="Process.ProcessName"/> ile aynı).</summary>
        public const string BotProcessName = "VortexArena.PoseBot";

        private const string ServerPidKey = "VortexArena.Dev.ServerPid";
        private const string BotPidsKey = "VortexArena.Dev.BotPids";

        private const string SolutionRelativePath = @"Server\VortexArena.Server.sln";

        /// <summary>
        /// Sunucu exe'sinin arama sırası (ilk bulunan kazanır): yayınlanmış <c>deploy\server\</c>
        /// → Release çıktısı → Debug çıktısı.
        /// </summary>
        private static readonly string[] ServerExeCandidates =
        {
            @"deploy\server\VortexArena.Server.App.exe",
            @"Server\VortexArena.Server.App\bin\Release\net10.0\VortexArena.Server.App.exe",
            @"Server\VortexArena.Server.App\bin\Debug\net10.0\VortexArena.Server.App.exe"
        };

        /// <summary>
        /// PoseBot exe'sinin arama sırası. <b>PoseBot bilinçli olarak <c>deploy/</c>'a publish
        /// EDİLMEZ:</b> <c>deploy/</c> işletmeye giden klasördür, PoseBot ise yalnız dev/test
        /// aracıdır — sahaya sentetik oyuncu üreten bir exe göndermek istemiyoruz. Bu yüzden
        /// yalnız <c>bin\Release</c> / <c>bin\Debug</c> çıktısı aranır.
        /// </summary>
        private static readonly string[] BotExeCandidates =
        {
            @"Server\VortexArena.PoseBot\bin\Release\net10.0\VortexArena.PoseBot.exe",
            @"Server\VortexArena.PoseBot\bin\Debug\net10.0\VortexArena.PoseBot.exe"
        };

        /// <summary>Repo kökü = <c>Assets</c>'in üst klasörü.</summary>
        public static string RepoRoot
        {
            get
            {
                DirectoryInfo parent = Directory.GetParent(Application.dataPath);
                return parent != null ? parent.FullName : Application.dataPath;
            }
        }

        // ------------------------------------------------------------------ durum

        /// <summary>
        /// Kayıtlı sunucu PID'i, yoksa 0. Süreç ölmüşse ya da o PID artık başka bir sürece
        /// aitse kayıt SİLİNİR (bu yüzden getter'ın yan etkisi var).
        /// </summary>
        public static int ServerPid => ResolveTrackedPid(ServerPidKey, ServerProcessName);

        /// <summary>Sunucu süreci koşuyor mu?</summary>
        public static bool IsServerRunning => ServerPid > 0;

        /// <summary>Kayıtlı ve HÂLÂ YAŞAYAN PoseBot süreçlerinin sayısı (süreç sayısı, bot sayısı değil).</summary>
        public static int BotCount => GetLiveBotPids().Count;

        /// <summary>En az bir PoseBot süreci koşuyor mu?</summary>
        public static bool AreBotsRunning => BotCount > 0;

        // ------------------------------------------------------------------ sunucu

        /// <summary>Sunucuyu kendi konsol penceresinde başlatır (zaten koşuyorsa uyarı loglar).</summary>
        public static void StartServer()
        {
            int existing = ServerPid;
            if (existing > 0)
            {
                Debug.LogWarning(
                    $"[DevProcesses] Sunucu zaten çalışıyor (PID {existing}) — yeni süreç " +
                    "başlatılmadı (ikinci süreç 47821'e bind olamaz).");
                return;
            }

            string exe = ResolveExe(ServerExeCandidates);
            if (exe == null)
            {
                Debug.LogError(
                    "[DevProcesses] Sunucu exe'si bulunamadı. Önce `scripts\\deploy-server.bat` " +
                    "çalıştırın ya da bu penceredeki \"Derle (dotnet build)\" düğmesine basın. " +
                    "Aranan yollar: " + DescribeCandidates(ServerExeCandidates));
                return;
            }

            Process process = StartConsoleProcess(exe, "", Path.GetDirectoryName(exe));
            if (process == null)
            {
                return;
            }

            SessionState.SetString(ServerPidKey, process.Id.ToString(CultureInfo.InvariantCulture));
            Debug.Log($"[DevProcesses] Sunucu başlatıldı (PID {process.Id}): {exe}");
            process.Dispose();
        }

        /// <summary>Kayıtlı sunucu sürecini öldürür.</summary>
        public static void StopServer()
        {
            int pid = ServerPid;
            SessionState.EraseString(ServerPidKey);

            if (pid <= 0)
            {
                Debug.Log("[DevProcesses] Kayıtlı sunucu süreci yok — durdurulacak bir şey bulunamadı. " +
                          "(Yetim süreç şüphesi varsa \"Sahipsiz süreçleri temizle\".)");
                return;
            }

            if (KillPid(pid, ServerProcessName))
            {
                Debug.Log($"[DevProcesses] Sunucu durduruldu (PID {pid}).");
            }
        }

        // -------------------------------------------------------------------- bot

        /// <summary>
        /// <paramref name="count"/> botu TEK bir PoseBot sürecinde başlatır (PoseBot bot sayısını
        /// konumsal argüman olarak alır). Komut satırı: <c>&lt;ip&gt; &lt;count&gt; --fight</c>
        /// (+ <paramref name="withAdmin"/> ise <c>--admin --mode &lt;modId&gt;</c> ve açık sahne
        /// bir ARENA sahnesiyse <c>--map &lt;sahneAdı&gt;</c>).
        /// <para>
        /// IP: <see cref="DevSession.HasAddress"/> ise seçili adres, aksi hâlde
        /// <c>127.0.0.1</c> — keşif kipinde istemci beacon'la bulur ama botun keşfi yoktur,
        /// loopback'e bağlanır.
        /// </para>
        /// Her çağrı yeni bir süreç EKLER (iki kez basılırsa iki pencere açılır).
        /// </summary>
        public static void StartBots(int count, bool withAdmin)
        {
            string exe = ResolveExe(BotExeCandidates);
            if (exe == null)
            {
                Debug.LogError(
                    "[DevProcesses] PoseBot exe'si bulunamadı. Bu penceredeki " +
                    "\"Derle (dotnet build)\" düğmesine basın (PoseBot bilinçli olarak deploy/'a " +
                    "publish edilmez — dev/test aracı). Aranan yollar: " +
                    DescribeCandidates(BotExeCandidates));
                return;
            }

            int clamped = Mathf.Clamp(count, 1, ArenaProtocol.MAX_PLAYERS);
            string ip = DevSession.HasAddress ? DevSession.Ip.Trim() : "127.0.0.1";

            var args = new StringBuilder();
            args.Append(Quote(ip))
                .Append(' ')
                .Append(clamped.ToString(CultureInfo.InvariantCulture))
                .Append(" --fight");

            if (withAdmin)
            {
                args.Append(" --admin");

                string modeId = DevSession.ModeId;
                if (!string.IsNullOrWhiteSpace(modeId))
                {
                    args.Append(" --mode ").Append(Quote(modeId.Trim()));
                }

                string arenaScene = ActiveArenaSceneName();
                if (arenaScene != null)
                {
                    args.Append(" --map ").Append(Quote(arenaScene));
                }
            }

            string arguments = args.ToString();
            Process process = StartConsoleProcess(exe, arguments, Path.GetDirectoryName(exe));
            if (process == null)
            {
                return;
            }

            List<int> pids = GetLiveBotPids();
            pids.Add(process.Id);
            WriteBotPids(pids);

            Debug.Log($"[DevProcesses] PoseBot başlatıldı (PID {process.Id}): {clamped} bot → {ip} " +
                      $"[{arguments}]");
            process.Dispose();
        }

        /// <summary>Kayıtlı tüm PoseBot süreçlerini öldürür (kayıt boşsa sessizce çıkar).</summary>
        public static void StopBots()
        {
            List<int> pids = GetLiveBotPids();
            SessionState.EraseString(BotPidsKey);

            if (pids.Count == 0)
            {
                return; // Her Play çıkışında çağrılıyor — boşken log basmıyoruz.
            }

            int killed = 0;
            for (int i = 0; i < pids.Count; i++)
            {
                if (KillPid(pids[i], BotProcessName))
                {
                    killed++;
                }
            }

            Debug.Log($"[DevProcesses] {killed} PoseBot süreci durduruldu.");
        }

        // ------------------------------------------------------------------- toplu

        /// <summary>
        /// Kayıtlı sunucu + bot süreçlerini öldürür VE ardından ad bazlı süpürme yapar
        /// (editör çöktüyse PID kaydı kaybolmuş olabilir; yetim süreç tuzağının kesin çözümü bu).
        /// </summary>
        public static void StopAll()
        {
            int killed = 0;

            int serverPid = ServerPid;
            SessionState.EraseString(ServerPidKey);
            if (serverPid > 0 && KillPid(serverPid, ServerProcessName))
            {
                killed++;
            }

            List<int> botPids = GetLiveBotPids();
            SessionState.EraseString(BotPidsKey);
            for (int i = 0; i < botPids.Count; i++)
            {
                if (KillPid(botPids[i], BotProcessName))
                {
                    killed++;
                }
            }

            killed += SweepByName();

            Debug.Log($"[DevProcesses] Hepsini durdur: {killed} süreç kapatıldı " +
                      "(kayıtlı + ad bazlı süpürme).");
        }

        /// <summary>
        /// Yalnız AD BAZLI süpürme: <c>VortexArena.Server.App</c> ve <c>VortexArena.PoseBot</c>
        /// adlı TÜM süreçleri öldürür. Kaç süreç kapatıldığını döndürür.
        /// </summary>
        public static int SweepOrphans()
        {
            int killed = SweepByName();

            if (killed > 0)
            {
                Debug.Log($"[DevProcesses] Sahipsiz süreç temizliği: {killed} süreç kapatıldı.");
            }
            else
            {
                Debug.Log("[DevProcesses] Sahipsiz süreç bulunamadı " +
                          $"(aranan adlar: {ServerProcessName}, {BotProcessName}).");
            }

            return killed;
        }

        /// <summary>
        /// <c>dotnet build Server\VortexArena.Server.sln -c Release</c> — kendi konsol
        /// penceresinde koşar ve <b>sonucu BEKLEMEZ</b>; geliştirici derleme çıktısını pencereden
        /// okur (bu yüzden pencere <c>cmd /k</c> ile açık bırakılır, hata satırları kaybolmasın).
        /// </summary>
        public static void BuildSolution()
        {
            string root = RepoRoot;
            string solution = Path.Combine(root, SolutionRelativePath);

            if (!File.Exists(solution))
            {
                Debug.LogError($"[DevProcesses] Çözüm dosyası bulunamadı: {solution}");
                return;
            }

            if (!IsDotnetOnPath())
            {
                Debug.LogError(
                    "[DevProcesses] `dotnet` PATH'te bulunamadı — .NET 10 SDK kurulu değil ya da " +
                    "PATH'e girmemiş. Kurduktan sonra tekrar deneyin ya da elle çalıştırın: " +
                    $"dotnet build \"{SolutionRelativePath}\" -c Release");
                return;
            }

            string arguments = $"/k dotnet build \"{SolutionRelativePath}\" -c Release";
            Process process = StartConsoleProcess("cmd.exe", arguments, root);
            if (process == null)
            {
                return;
            }

            Debug.Log("[DevProcesses] `dotnet build -c Release` başlatıldı (kendi penceresinde; " +
                      "bitince pencere açık kalır). Bittikten sonra Sunucu/Bot düğmeleri Release " +
                      "çıktısını bulacaktır.");
            process.Dispose();
        }

        // --------------------------------------------------------------- yardımcı

        /// <summary>
        /// Aktif sahne bir ARENA sahnesiyse adını, kabuk sahnesiyse (Boot/Lobby)
        /// ya da adsızsa <c>null</c> döndürür.
        /// </summary>
        public static string ActiveArenaSceneName()
        {
            string scene = SceneManager.GetActiveScene().name;
            return IsShellScene(scene) ? null : scene;
        }

        /// <summary>Sahne adı kabuk sahnesi mi (Boot / Lobby) ya da boş mu?</summary>
        public static bool IsShellScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                return true;
            }

            return string.Equals(sceneName, AppSession.SceneBoot, StringComparison.Ordinal) ||
                   string.Equals(sceneName, AppSession.SceneLobby, StringComparison.Ordinal);
        }

        /// <summary>Adaylardan diskte var olan ilk yolu döndürür; hiçbiri yoksa null.</summary>
        private static string ResolveExe(string[] relativeCandidates)
        {
            string root = RepoRoot;

            for (int i = 0; i < relativeCandidates.Length; i++)
            {
                string full;
                try
                {
                    full = Path.Combine(root, relativeCandidates[i]);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (File.Exists(full))
                {
                    return full;
                }
            }

            return null;
        }

        private static string DescribeCandidates(string[] relativeCandidates)
        {
            return string.Join(" | ", relativeCandidates);
        }

        /// <summary>
        /// Kendi konsol penceresinde süreç başlatır. <b>Boru YOK</b> (TUZAK 2) ve
        /// <b>ara süreç YOK</b> (TUZAK 1) — dönen PID gerçekten öldürülecek sürecin PID'idir.
        /// </summary>
        private static Process StartConsoleProcess(string fileName, string arguments, string workingDirectory)
        {
            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments ?? "",
                    WorkingDirectory = string.IsNullOrEmpty(workingDirectory) ? RepoRoot : workingDirectory,
                    UseShellExecute = true,   // kendi konsol penceresi
                    CreateNoWindow = false,   // pencere görünür kalsın (loglar canlı okunur)
                    RedirectStandardOutput = false, // BİLEREK false — okunmayan boru süreci kilitler
                    RedirectStandardError = false
                };

                Process process = Process.Start(info);
                if (process == null)
                {
                    Debug.LogError($"[DevProcesses] '{fileName}' başlatıldı ama süreç tanıtıcısı alınamadı.");
                    return null;
                }

                return process;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DevProcesses] '{fileName}' başlatılamadı: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Kayıtlı PID'i doğrular: süreç yaşıyor VE adı beklenenle aynı mı? Değilse kaydı siler
        /// ve 0 döner (PID yeniden kullanılmış olabilir — yanlış süreci öldürmemek için).
        /// </summary>
        private static int ResolveTrackedPid(string sessionKey, string expectedProcessName)
        {
            int pid = ParsePid(SessionState.GetString(sessionKey, ""));
            if (pid <= 0)
            {
                return 0;
            }

            if (IsProcessAlive(pid, expectedProcessName))
            {
                return pid;
            }

            SessionState.EraseString(sessionKey);
            return 0;
        }

        /// <summary>PID yaşıyor ve süreç adı <paramref name="expectedProcessName"/> mi?</summary>
        private static bool IsProcessAlive(int pid, string expectedProcessName)
        {
            if (pid <= 0)
            {
                return false;
            }

            Process process = null;
            try
            {
                process = Process.GetProcessById(pid);
                if (process.HasExited)
                {
                    return false;
                }

                return string.Equals(process.ProcessName, expectedProcessName, StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException)
            {
                return false; // Böyle bir PID yok.
            }
            catch (Exception)
            {
                return false; // Erişim/platform sorunu — süreci "yok" saymak güvenli taraf.
            }
            finally
            {
                process?.Dispose();
            }
        }

        /// <summary>Ad doğrulamasından geçen PID'i öldürür; başarılıysa true.</summary>
        private static bool KillPid(int pid, string expectedProcessName)
        {
            if (!IsProcessAlive(pid, expectedProcessName))
            {
                return false;
            }

            Process process = null;
            try
            {
                process = Process.GetProcessById(pid);

                // Ad ikinci kez doğrulanıyor: PID'in bu arada geri dönüşmüş olma ihtimali.
                if (!string.Equals(process.ProcessName, expectedProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // Kill() tek süreci öldürür — çocuk süreç YOK (exe'yi doğrudan başlattığımız için).
                process.Kill();
                process.WaitForExit(3000);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DevProcesses] PID {pid} ({expectedProcessName}) öldürülemedi: {ex.Message}");
                return false;
            }
            finally
            {
                process?.Dispose();
            }
        }

        /// <summary>Ad bazlı süpürme (log YAZMAZ) — kapatılan süreç sayısını döndürür.</summary>
        private static int SweepByName()
        {
            int killed = KillAllNamed(ServerProcessName) + KillAllNamed(BotProcessName);

            if (killed > 0)
            {
                SessionState.EraseString(ServerPidKey);
                SessionState.EraseString(BotPidsKey);
            }

            return killed;
        }

        private static int KillAllNamed(string processName)
        {
            Process[] found;
            try
            {
                found = Process.GetProcessesByName(processName);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DevProcesses] '{processName}' süreçleri listelenemedi: {ex.Message}");
                return 0;
            }

            int killed = 0;
            for (int i = 0; i < found.Length; i++)
            {
                Process process = found[i];
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        process.WaitForExit(3000);
                        killed++;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[DevProcesses] '{processName}' (PID {SafePid(process)}) öldürülemedi: {ex.Message}");
                }
                finally
                {
                    process.Dispose();
                }
            }

            return killed;
        }

        private static string SafePid(Process process)
        {
            try
            {
                return process.Id.ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return "?";
            }
        }

        /// <summary>SessionState'teki virgüllü PID listesinden yalnız YAŞAYANLARI döndürür (kaydı da tazeler).</summary>
        private static List<int> GetLiveBotPids()
        {
            var alive = new List<int>();
            string raw = SessionState.GetString(BotPidsKey, "");

            if (!string.IsNullOrEmpty(raw))
            {
                string[] parts = raw.Split(',');
                for (int i = 0; i < parts.Length; i++)
                {
                    int pid = ParsePid(parts[i]);
                    if (pid > 0 && !alive.Contains(pid) && IsProcessAlive(pid, BotProcessName))
                    {
                        alive.Add(pid);
                    }
                }

                if (alive.Count != parts.Length)
                {
                    WriteBotPids(alive); // ölmüş/geri dönüşmüş PID'leri kayıttan düş
                }
            }

            return alive;
        }

        private static void WriteBotPids(List<int> pids)
        {
            if (pids == null || pids.Count == 0)
            {
                SessionState.EraseString(BotPidsKey);
                return;
            }

            var sb = new StringBuilder();
            for (int i = 0; i < pids.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append(pids[i].ToString(CultureInfo.InvariantCulture));
            }

            SessionState.SetString(BotPidsKey, sb.ToString());
        }

        private static int ParsePid(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }

            return int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid) && pid > 0
                ? pid
                : 0;
        }

        /// <summary>Boşluk içeren argümanı çift tırnağa alır (sahne/mod adları güvenli olsun).</summary>
        private static string Quote(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "\"\"";
            }

            return value.IndexOf(' ') >= 0 ? "\"" + value + "\"" : value;
        }

        /// <summary>
        /// PATH'te <c>dotnet.exe</c> var mı? (Süreci <c>cmd /k dotnet …</c> ile başlattığımız
        /// için sadece PATH'e bakmak doğru testtir — PATH dışı bir kurulumu cmd de bulamaz.)
        /// </summary>
        private static bool IsDotnetOnPath()
        {
            try
            {
                string path = Environment.GetEnvironmentVariable("PATH");
                if (string.IsNullOrEmpty(path))
                {
                    return false;
                }

                string[] directories = path.Split(Path.PathSeparator);
                for (int i = 0; i < directories.Length; i++)
                {
                    string directory = directories[i];
                    if (string.IsNullOrWhiteSpace(directory))
                    {
                        continue;
                    }

                    try
                    {
                        if (File.Exists(Path.Combine(directory.Trim().Trim('"'), "dotnet.exe")))
                        {
                            return true;
                        }
                    }
                    catch (ArgumentException)
                    {
                        // PATH'te geçersiz karakter içeren girdi — atla.
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DevProcesses] PATH taranamadı: {ex.Message}");
            }

            return false;
        }
    }
}
