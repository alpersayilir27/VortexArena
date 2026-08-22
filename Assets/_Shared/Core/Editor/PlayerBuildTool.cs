using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
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
    ///   -buildOutput &lt;deploy\player&gt; -buildVersion 132
    /// </code>
    /// <para><b>The player build is versioned, the admin build is not:</b> <c>-buildVersion</c> is
    /// REQUIRED for <c>BuildQuestPlayer</c> and produces <c>game_v&lt;version&gt;.apk</c> with the
    /// application id <c>com.vortex.arenav&lt;version&gt;</c> ('v' without a dot — an Android package
    /// segment cannot start with a digit), so several versions can stay installed side by side on
    /// one headset.</para>
    /// <para><b>PlayerSettings are changed TEMPORARILY and restored in a <c>finally</c>:</b>
    /// application id, bundleVersion, bundleVersionCode and productName. Therefore
    /// <c>EditorApplication.Exit</c> is called only AFTER the restore, from the outermost level —
    /// <c>Exit</c> ends the process at once and would skip <c>finally</c>, leaving
    /// <c>ProjectSettings.asset</c> permanently pointing at a versioned id.</para>
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
        private const string ArgBuildVersion = "-buildVersion";
        private const string ExeName = "VortexArena.exe";

        /// <summary>Base application id; the versioned build appends <c>v&lt;version&gt;</c>.</summary>
        private const string BaseApplicationId = "com.vortex.arena";

        /// <summary>Sanity bound for the version; also keeps <c>bundleVersionCode</c> inside int.</summary>
        private const int MaxBuildVersion = 2000000000;

        /// <summary>Windows 64-bit admin build; exit code 1 on failure.</summary>
        public static void BuildWindowsAdmin()
        {
            Run("admin", ExeName, BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, version: -1);
        }

        /// <summary>Meta Quest 3/3S player build (Android, <c>game_v&lt;version&gt;.apk</c>); exit
        /// code 1 on failure.</summary>
        /// <remarks>The leading check does not SELECT the target (it is pinned); it catches
        /// <c>-buildTarget Android</c> not taking effect, whose only real cause is a missing
        /// Android Build Support module — Unity would then stay on Windows and emit an <c>.exe</c>
        /// named <c>game_v&lt;version&gt;.apk</c>. Desktop support exists in every install, so
        /// Windows needs no such check.</remarks>
        public static void BuildQuestPlayer()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                Fail($"`-buildTarget Android` uygulanmamış, platform {EditorUserBuildSettings.activeBuildTarget} " +
                     "olarak kaldı. En olası sebep: Android Build Support modülü kurulu değil " +
                     "(deploy-player-apk.bat bayrağı zaten geçiyor).");
                Quit(false);
                return;
            }

            if (!TryGetBuildVersion(out int version))
            {
                Quit(false);
                return;
            }

            Run("player", $"game_v{version}.apk", BuildTarget.Android, BuildTargetGroup.Android, version);
        }

        // ------------------------------------------------------------------ shared

        /// <summary>Body of both entry points; applies the version overrides and ALWAYS restores
        /// them before the process may exit.</summary>
        /// <remarks>⚠️ <c>EditorApplication.Exit</c> lives here and only after the <c>finally</c>:
        /// it kills the process immediately, so an <c>Exit</c> inside the try would skip the
        /// restore and leave the versioned id/name written into <c>ProjectSettings.asset</c>.
        /// </remarks>
        private static void Run(string defaultFolder, string artifactName,
            BuildTarget target, BuildTargetGroup group, int version)
        {
            bool ok = false;
            bool overridden = false;
            string prevAppId = null;
            string prevBundleVersion = null;
            string prevProductName = null;
            int prevVersionCode = 0;

            try
            {
                if (version > 0)
                {
                    prevAppId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
                    prevBundleVersion = PlayerSettings.bundleVersion;
                    prevProductName = PlayerSettings.productName;
                    prevVersionCode = PlayerSettings.Android.bundleVersionCode;

                    // 'v' is glued to the id without a dot: an Android package segment cannot
                    // start with a digit.
                    string appId = StripVersionSuffix(prevAppId, '\0') + "v" + version;
                    string productName = StripVersionSuffix(prevProductName, ' ') + " v" + version;

                    PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, appId);
                    PlayerSettings.bundleVersion = version.ToString();
                    PlayerSettings.Android.bundleVersionCode = version;
                    PlayerSettings.productName = productName;
                    overridden = true;

                    Debug.Log($"[PlayerBuildTool] Sürüm {version} — paket adı: {appId}, uygulama adı: {productName}");
                }

                ok = Execute(defaultFolder, artifactName, target, group);
            }
            catch (Exception e)
            {
                Fail($"Build istisna ile düştü: {e}");
                ok = false;
            }
            finally
            {
                if (overridden)
                {
                    PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, prevAppId);
                    PlayerSettings.bundleVersion = prevBundleVersion;
                    PlayerSettings.Android.bundleVersionCode = prevVersionCode;
                    PlayerSettings.productName = prevProductName;
                    AssetDatabase.SaveAssets();
                    Debug.Log($"[PlayerBuildTool] Proje ayarları geri alındı (paket adı: {prevAppId}).");
                }
            }

            Quit(ok);
        }

        /// <summary>Actual build; returns success and never exits the process (see
        /// <see cref="Run"/>).</summary>
        private static bool Execute(string defaultFolder, string artifactName,
            BuildTarget target, BuildTargetGroup group)
        {
            string outputDir = ResolveOutputDir(defaultFolder);
            if (!TryGetEnabledScenes(out string[] scenes))
            {
                return false;
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
                return false;
            }

            Debug.Log(
                $"[PlayerBuildTool] Build BAŞARILI: {artifactPath} " +
                $"({summary.totalSize / (1024f * 1024f):0.0} MB, {summary.totalTime.TotalSeconds:0} sn, " +
                $"{summary.totalWarnings} uyarı)");

            return true;
        }

        /// <summary>Drops a trailing <c>[separator]v&lt;digits&gt;</c> suffix
        /// (<c>separator == '\0'</c> = no separator).</summary>
        /// <remarks>A crashed previous run can leave <c>...v118</c> written into the settings;
        /// without stripping we would produce <c>...v118v132</c>.</remarks>
        private static string StripVersionSuffix(string value, char separator)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            int i = value.Length - 1;
            while (i >= 0 && value[i] >= '0' && value[i] <= '9')
            {
                i--;
            }

            // Needs at least one digit and a 'v' right before them.
            if (i == value.Length - 1 || i < 0 || value[i] != 'v')
            {
                return value;
            }

            if (separator == '\0')
            {
                return value.Substring(0, i);
            }

            if (i < 1 || value[i - 1] != separator)
            {
                return value;
            }

            return value.Substring(0, i - 1);
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
            string value = GetArgValue(ArgBuildOutput);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return Path.GetFullPath(value);
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectRoot ?? ".", "Builds", defaultFolder);
        }

        /// <summary><c>-buildVersion &lt;int&gt;</c>; required, no default.</summary>
        private static bool TryGetBuildVersion(out int version)
        {
            version = 0;
            string raw = GetArgValue(ArgBuildVersion);

            if (string.IsNullOrWhiteSpace(raw))
            {
                Fail($"`{ArgBuildVersion} <sayı>` verilmedi — oyuncu build'i sürümsüz alınamaz. " +
                     "Sürüm hem APK adına (game_v<sayı>.apk) hem paket adına " +
                     $"({BaseApplicationId}v<sayı>) giriyor. Build'i deploy-player-apk.bat ile başlatın.");
                return false;
            }

            if (!int.TryParse(raw.Trim(), out version))
            {
                Fail($"`{ArgBuildVersion} {raw}` sayıya çevrilemedi — sürüm pozitif bir tam sayı olmalı (ör. 132).");
                return false;
            }

            if (version <= 0 || version > MaxBuildVersion)
            {
                Fail($"`{ArgBuildVersion} {version}` geçersiz — sürüm 1 ile {MaxBuildVersion} arasında olmalı.");
                version = 0;
                return false;
            }

            return true;
        }

        /// <summary>Value following <paramref name="name"/> on the Unity command line.</summary>
        private static string GetArgValue(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        /// <summary>Logs the failure; exiting is the caller's job.</summary>
        /// <remarks>⚠️ Does NOT call <c>Exit</c>: that would skip the PlayerSettings restore in
        /// <see cref="Run"/>'s <c>finally</c>.</remarks>
        private static void Fail(string message)
        {
            Debug.LogError($"[PlayerBuildTool] {message}");
        }

        /// <summary>Batch-mode exit; interactive runs stay open.</summary>
        private static void Quit(bool ok)
        {
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(ok ? 0 : 1);
            }
        }
    }
}
