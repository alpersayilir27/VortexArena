using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace VortexArena.App.Editor
{
    /// <summary>
    /// Dev penceresinin ortam düğmesinin arkasındaki süreç yönetimi: sunucu çözümünü derler.
    ///
    /// <para><b>⚠️ SUNUCU BURADAN YÖNETİLMEZ.</b> <c>.NET</c> sunucusu her zaman ELLE başlatılır
    /// ve ELLE durdurulur (<c>deploy\server\VortexArena.Server.App.exe</c> ya da
    /// <c>dotnet run --project Server/VortexArena.Server.App</c>). Editör sunucuyu ne başlatır ne
    /// öldürür. Gerekçe: sunucu üretimde ayrı bir makinede uzun ömürlüdür — editörün onu
    /// yönetmesi hem bu topolojiden uzaklaştırıyor hem de elle başlatılmış bir sunucuyu
    /// beklenmedik anda öldürme riski taşıyordu. Buradaki <see cref="BuildSolution"/> yalnız
    /// DERLER, çalıştırmaz.</para>
    ///
    /// <para><b>⚠️ TUZAK 1 — <c>dotnet run</c> ASLA KULLANILMAZ.</b> <c>dotnet run</c> derleyip
    /// asıl exe'yi ÇOCUK süreç olarak doğurur. Parent (dotnet) öldürüldüğünde çocuk hayatta
    /// kalıyor: <c>VortexArena.Server.App.exe</c> YETİM kaldı, 47821'i tutmaya devam etti ve
    /// artık PID takibimizde olmadığı için öldürülemedi → sonraki sunucu porta bind olamadı.
    /// Bu gerçekten yaşandı. Bu yüzden süreçler <b>her zaman doğrudan</b> başlatılır, araya
    /// başka bir süreç sokulmaz.</para>
    ///
    /// <para><b>⚠️ TUZAK 2 — çıktı BORULANMAZ.</b> <c>RedirectStandardOutput/Error = true</c>
    /// yapıp boruyu okumazsan çocuk süreç, boru tamponu dolduğunda yazma çağrısında kilitlenir
    /// (bu projede Flutter launcher'da aynı hata yaşandı: süreç canlı görünür ama donmuştur).
    /// Bu yüzden <c>UseShellExecute = true</c> + <c>CreateNoWindow = false</c>: her süreç KENDİ
    /// konsol penceresinde koşar, boru yoktur, geliştirici derleme çıktısını canlı okur.
    /// Bir dev aracı için zaten istenen davranış budur.</para>
    ///
    /// Tüm hatalar yakalanır ve anlamlı <see cref="Debug.LogError"/> mesajına çevrilir; bu sınıf
    /// editörü kırmamalıdır.
    /// </summary>
    public static class DevProcesses
    {
        private const string SolutionRelativePath = @"Server\VortexArena.Server.sln";

        /// <summary>Repo kökü = <c>Assets</c>'in üst klasörü.</summary>
        public static string RepoRoot
        {
            get
            {
                DirectoryInfo parent = Directory.GetParent(Application.dataPath);
                return parent != null ? parent.FullName : Application.dataPath;
            }
        }

        // ----------------------------------------------------------------- derleme

        /// <summary>
        /// <c>dotnet build Server\VortexArena.Server.sln -c Release</c> — kendi konsol
        /// penceresinde koşar ve <b>sonucu BEKLEMEZ</b>; geliştirici derleme çıktısını pencereden
        /// okur (bu yüzden pencere <c>cmd /k</c> ile açık bırakılır, hata satırları kaybolmasın).
        /// Yalnız DERLER — sunucuyu çalıştırmaz (sunucu elle başlatılır).
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
                      "bitince pencere açık kalır). Sunucuyu elle başlatın.");
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
