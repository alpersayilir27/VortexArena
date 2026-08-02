using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// Batch-mode build girişleri — <c>scripts/deploy-admin-game.bat</c> ve
    /// <c>scripts/deploy-player-apk.bat</c> buradan çağırır:
    /// <code>
    /// Unity.exe -batchmode -quit -projectPath &lt;proje&gt; -buildTarget Win64 \
    ///   -executeMethod VortexArena.Core.Editor.PlayerBuildTool.BuildWindowsAdmin \
    ///   -buildOutput &lt;deploy\admin&gt;
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;proje&gt; -buildTarget Android \
    ///   -executeMethod VortexArena.Core.Editor.PlayerBuildTool.BuildQuestPlayer \
    ///   -buildOutput &lt;deploy\player&gt;
    /// </code>
    /// <para>
    /// <b>Hedef platform aktif platformdan TÜRETİLMEZ:</b> her iki giriş de hedefini sabit
    /// tutar (<c>StandaloneWindows64</c> / <c>Android</c>) ve çağıran <c>.bat</c> Unity'yi
    /// aynı hedefle <b>başlatır</b> (<c>-buildTarget</c>). Bayrak açılışta verilir, çünkü
    /// platformu bu metodun içinden çevirmek domain reload tetikler ve çalışan
    /// <c>-executeMethod</c> yarıda kalır. Projede hangi platform açık kalmış olursa olsun
    /// betikler kendi çıktısını üretir.
    /// </para>
    /// <para>
    /// <b>İki rol, iki platform, TEK sahne listesi:</b> Windows build'i admin (yönetim),
    /// Android build'i Quest oyuncusudur. İkisi de Build Settings'teki etkin sahneleri
    /// aynen kullanır — Boot index 0 olmalıdır ve arena sahneleri listede olmalıdır, çünkü
    /// <c>start_match</c> sahneyi TÜM oyuncuların <c>hello.scenes</c> listesinde arar
    /// (CLAUDE.md). Sahne listesi platforma göre AYRIŞTIRILMAZ: ayrışsaydı bir arenayı
    /// admin bilir oyuncu bilmez olurdu ve maç sessizce reddedilirdi.
    /// </para>
    /// <para>
    /// <b>Rol ve adres build'e gömülmez:</b> masaüstü build'i çalışma anında admin rolüne
    /// düşer ve sunucu adresini launcher'ın geçtiği <c>--server-ip</c> argümanından okur
    /// (<c>AppBoot</c>). Bu yüzden admin ve ileride başka masaüstü rolleri için ayrı build
    /// gerekmez.
    /// </para>
    /// <para>
    /// Batch-mode Unity, editör aynı projeyi açıkken **proje kilidine takılır**; .bat bunu
    /// önceden kontrol eder.
    /// </para>
    /// </summary>
    public static class PlayerBuildTool
    {
        private const string ArgBuildOutput = "-buildOutput";
        private const string ExeName = "VortexArena.exe";

        /// <summary>APK adı — <c>install_game.bat</c> tam olarak bu adı arar, değiştirme.</summary>
        private const string ApkName = "game.apk";

        /// <summary>Windows 64-bit admin/yönetim build'i. Hata durumunda exit code 1 döner.</summary>
        public static void BuildWindowsAdmin()
        {
            Run("admin", ExeName, BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone);
        }

        /// <summary>
        /// Meta Quest 3/3S oyuncu build'i (Android, <c>game.apk</c>). Hata durumunda exit code 1.
        /// <para>
        /// Baştaki kontrol hedefi SEÇMEZ — hedef zaten sabittir; yalnız <c>-buildTarget Android</c>
        /// bayrağının <b>uygulanmadığı</b> hâli yakalar. Tek gerçek sebebi Android Build Support
        /// modülünün kurulu olmamasıdır: o hâlde Unity platformu çeviremez, sessizce Windows'ta
        /// devam eder ve <c>game.apk</c> adında bir <c>.exe</c> üretirdi. Windows tarafında böyle
        /// bir sessiz düşüş yok (masaüstü desteği her kurulumda var), o yüzden orada kontrol de yok.
        /// </para>
        /// </summary>
        public static void BuildQuestPlayer()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                Fail($"`-buildTarget Android` uygulanmamış, platform {EditorUserBuildSettings.activeBuildTarget} " +
                     "olarak kaldı. En olası sebep: Android Build Support modülü kurulu değil " +
                     "(deploy-player-apk.bat bayrağı zaten geçiyor).");
                return;
            }

            Run("player", ApkName, BuildTarget.Android, BuildTargetGroup.Android);
        }

        // ------------------------------------------------------------------ ortak

        /// <summary>İki build girişinin de gövdesi — tek fark hedef platform ve çıktı adı.</summary>
        private static void Run(string defaultFolder, string artifactName,
            BuildTarget target, BuildTargetGroup group)
        {
            try
            {
                string outputDir = ResolveOutputDir(defaultFolder);
                if (!TryGetEnabledScenes(out string[] scenes))
                {
                    return;
                }

                Directory.CreateDirectory(outputDir);
                string artifactPath = Path.Combine(outputDir, artifactName);

                Debug.Log($"[PlayerBuildTool] Hedef: {artifactPath} ({target})");
                Debug.Log($"[PlayerBuildTool] Sahneler ({scenes.Length}): {string.Join(", ", scenes.Select(Path.GetFileNameWithoutExtension))}");

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = artifactPath,
                    target = target,
                    targetGroup = group,
                    options = BuildOptions.None
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;

                if (summary.result != BuildResult.Succeeded)
                {
                    Fail($"Build sonucu: {summary.result} — {summary.totalErrors} hata.");
                    return;
                }

                Debug.Log(
                    $"[PlayerBuildTool] Build BAŞARILI: {artifactPath} " +
                    $"({summary.totalSize / (1024f * 1024f):0.0} MB, {summary.totalTime.TotalSeconds:0} sn, " +
                    $"{summary.totalWarnings} uyarı)");

                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(0);
                }
            }
            catch (Exception e)
            {
                Fail($"Build istisna ile düştü: {e}");
            }
        }

        /// <summary>
        /// Build Settings'teki etkin sahneler; sıra korunur (index 0 = Boot).
        /// <para>
        /// ⚠️ <b>Diskte olmayan sahne burada yakalanır.</b> Silinmiş bir arenanın satırı
        /// Build Settings'te kalabiliyor (klasör dosya sisteminden silinince Unity satırı
        /// temizlemez); o hâlde <c>BuildPipeline</c> yığın izli, sebebi görünmeyen bir hatayla
        /// düşerdi. Erken ve adıyla söylemek 20 dakikalık bir build'i baştan kurtarır.
        /// </para>
        /// </summary>
        private static bool TryGetEnabledScenes(out string[] scenes)
        {
            scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled && !string.IsNullOrEmpty(s.path))
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Fail("Build Settings'te etkin sahne yok — build iptal edildi.");
                return false;
            }

            string[] missing = scenes.Where(p => !File.Exists(p)).ToArray();
            if (missing.Length > 0)
            {
                Fail($"Build Settings'te diskte olmayan {missing.Length} sahne var — build iptal edildi:" +
                     Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", missing) +
                     Environment.NewLine + "File > Build Profiles listesinden bu satırları silin.");
                return false;
            }

            return true;
        }

        /// <summary>`-buildOutput &lt;yol&gt;`; verilmezse &lt;proje&gt;/Builds/&lt;defaultFolder&gt;.</summary>
        private static string ResolveOutputDir(string defaultFolder)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], ArgBuildOutput, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    return Path.GetFullPath(args[i + 1]);
                }
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectRoot ?? ".", "Builds", defaultFolder);
        }

        private static void Fail(string message)
        {
            Debug.LogError($"[PlayerBuildTool] {message}");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
        }
    }
}
