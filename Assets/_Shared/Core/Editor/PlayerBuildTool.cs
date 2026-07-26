using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// Batch-mode build girişi — <c>scripts/deploy-admin-game.bat</c> buradan çağırır:
    /// <code>
    /// Unity.exe -batchmode -quit -projectPath &lt;proje&gt; \
    ///   -executeMethod VortexArena.Core.Editor.PlayerBuildTool.BuildWindowsAdmin \
    ///   -buildOutput &lt;deploy\admin&gt;
    /// </code>
    /// <para>
    /// <b>Sahne listesi Build Settings'ten gelir</b> (etkin olanlar, sırayla). Boot index 0
    /// olmalıdır; arena sahneleri listede olmalıdır — `start_match` sahneyi TÜM oyuncuların
    /// <c>hello.scenes</c> listesinde arar (CLAUDE.md).
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

        /// <summary>Windows 64-bit admin/yönetim build'i. Hata durumunda exit code 1 döner.</summary>
        public static void BuildWindowsAdmin()
        {
            try
            {
                string outputDir = ResolveOutputDir();
                string[] scenes = EnabledScenes();

                if (scenes.Length == 0)
                {
                    Fail("Build Settings'te etkin sahne yok — build iptal edildi.");
                    return;
                }

                Directory.CreateDirectory(outputDir);
                string exePath = Path.Combine(outputDir, ExeName);

                Debug.Log($"[PlayerBuildTool] Hedef: {exePath}");
                Debug.Log($"[PlayerBuildTool] Sahneler ({scenes.Length}): {string.Join(", ", scenes.Select(Path.GetFileNameWithoutExtension))}");

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = exePath,
                    target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
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
                    $"[PlayerBuildTool] Build BAŞARILI: {exePath} " +
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

        /// <summary>Build Settings'teki etkin sahneler; sıra korunur (index 0 = Boot).</summary>
        private static string[] EnabledScenes()
        {
            return EditorBuildSettings.scenes
                .Where(s => s.enabled && !string.IsNullOrEmpty(s.path))
                .Select(s => s.path)
                .ToArray();
        }

        /// <summary>`-buildOutput &lt;yol&gt;`; verilmezse &lt;proje&gt;/Builds/admin.</summary>
        private static string ResolveOutputDir()
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
            return Path.Combine(projectRoot ?? ".", "Builds", "admin");
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
