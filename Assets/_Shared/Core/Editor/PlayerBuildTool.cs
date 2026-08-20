using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace VortexArena.Core.Editor
{
    /// <summary>Batch-mode build entry points, called by <c>scripts/deploy-admin-game.bat</c> and
    /// <c>scripts/deploy-player-apk.bat</c>.</summary>
    /// <remarks>
    /// <code>
    /// Unity.exe -batchmode -quit -projectPath &lt;project&gt; -buildTarget Win64 \
    ///   -executeMethod VortexArena.Core.Editor.PlayerBuildTool.BuildWindowsAdmin \
    ///   -buildOutput &lt;deploy\admin&gt;
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;project&gt; -buildTarget Android \
    ///   -executeMethod VortexArena.Core.Editor.PlayerBuildTool.BuildQuestPlayer \
    ///   -buildOutput &lt;deploy\player&gt;
    /// </code>
    /// <para><b>The target is NOT derived from the active platform:</b> both entry points pin it
    /// (<c>StandaloneWindows64</c> / <c>Android</c>) and the calling <c>.bat</c> launches Unity
    /// with the same <c>-buildTarget</c>. The flag goes on the command line because switching
    /// platform from inside this method triggers a domain reload and aborts the running
    /// <c>-executeMethod</c>.</para>
    /// <para><b>Two roles, two platforms, ONE scene list:</b> the Windows build is admin, the
    /// Android build is the Quest player; both use the enabled Build Settings scenes as-is (Boot
    /// at index 0, arenas listed) because <c>start_match</c> looks the scene up in EVERY player's
    /// <c>hello.scenes</c>. Splitting the list per platform would let admin know an arena the
    /// players do not, and the match would be rejected silently.</para>
    /// <para><b>Role and address are not baked in:</b> the desktop build falls to the admin role at
    /// runtime and reads the server address from the launcher's <c>--server-ip</c> argument
    /// (<c>AppBoot</c>), so extra desktop roles need no extra build.</para>
    /// <para>Batch-mode Unity hits the project lock while the editor has the project open; the
    /// .bat checks that up front.</para>
    /// </remarks>
    public static class PlayerBuildTool
    {
        private const string ArgBuildOutput = "-buildOutput";
        private const string ExeName = "VortexArena.exe";

        /// <summary>APK name; <c>install_game.bat</c> looks for exactly this — do not change.</summary>
        private const string ApkName = "game.apk";

        /// <summary>Windows 64-bit admin build; exit code 1 on failure.</summary>
        public static void BuildWindowsAdmin()
        {
            Run("admin", ExeName, BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone);
        }

        /// <summary>Meta Quest 3/3S player build (Android, <c>game.apk</c>); exit code 1 on
        /// failure.</summary>
        /// <remarks>The leading check does not SELECT the target (it is pinned); it catches
        /// <c>-buildTarget Android</c> not taking effect, whose only real cause is a missing
        /// Android Build Support module — Unity would then stay on Windows and emit an <c>.exe</c>
        /// named <c>game.apk</c>. Desktop support exists in every install, so Windows needs no such
        /// check.</remarks>
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

        // ------------------------------------------------------------------ shared

        /// <summary>Body of both entry points; only target and artifact name differ.</summary>
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

        /// <summary>Enabled Build Settings scenes, order preserved (index 0 = Boot).</summary>
        /// <remarks>⚠️ Catches scenes missing from disk: Unity keeps the row when a deleted arena's
        /// folder disappears from the file system, and <c>BuildPipeline</c> would then fail with a
        /// stack trace that hides the cause. Failing early and by name saves a 20 minute build.
        /// </remarks>
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

        /// <summary><c>-buildOutput &lt;path&gt;</c>; defaults to
        /// &lt;project&gt;/Builds/&lt;defaultFolder&gt;.</summary>
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
