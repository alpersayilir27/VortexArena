using System.Diagnostics;
using System.IO;

namespace VortexArena.Launcher.Services;

/// <summary>Result of a start attempt: the process when it came up, otherwise why not.</summary>
public sealed record StartResult(Process? Process, string? Error)
{
    public bool Ok => Error is null;
}

/// <summary>
/// Starts and stops <c>server_versions\server_v&lt;N&gt;\VortexArena.Server.App.exe</c>, always
/// pointed at the venue data folder (<c>--data-dir</c>).
/// <para>⚠️ <b>stdout is NOT redirected.</b> A redirected pipe dies with the launcher and the server
/// would go down with it on the next write; the log file is the operator's window instead.</para>
/// </summary>
public sealed class ServerController
{
    /// <summary>Server exit code for "startup validation failed" (Docs/ArenaNet-Protokol.md).</summary>
    public const int ExitCodeValidationFailed = 2;

    private const string ProcessName = "VortexArena.Server.App";

    private const string MissingVersionMessage = "Sunucu sürümü yok — aşağıdan bir sürüm indirin.";

    private readonly LauncherPaths _paths;
    private readonly InstalledBuilds _builds;

    public ServerController(LauncherPaths paths, LauncherSettings settings)
    {
        _paths = paths;
        _builds = new InstalledBuilds(paths.ServerVersionsDir, BuildPackage.Server, () => settings.ServerVersion);
    }

    /// <summary>Process this launcher started; null when the server runs on its own.</summary>
    public Process? Started { get; private set; }

    /// <summary>Unpacked versions, newest first. Cached — <see cref="RefreshInstalled"/> re-reads the disk.</summary>
    public IReadOnlyList<int> InstalledVersions => _builds.Versions;

    /// <summary>Version <see cref="Start"/> would launch; 0 when no build is unpacked.</summary>
    public int SelectedVersion => _builds.Selected;

    /// <summary>
    /// Version of the server that is up; 0 when none runs or it came from the pre-version
    /// <c>server\</c> folder. Read by <see cref="RefreshRunningVersion"/>, not per tick.
    /// </summary>
    public int RunningVersion { get; private set; }

    /// <summary>Re-reads the unpacked versions; true when the set changed.</summary>
    public bool RefreshInstalled() => _builds.Refresh();

    /// <summary>
    /// Re-reads the running server's version from its exe path.
    /// <para>Enumerating processes is not cheap, so this is called on run-state changes only —
    /// "running" itself is decided by the HTTP status, not by process lookup.</para>
    /// </summary>
    public int RefreshRunningVersion()
    {
        var version = 0;
        foreach (var process in Process.GetProcessesByName(ProcessName))
        {
            try
            {
                if (version == 0)
                {
                    version = BuildVersionCatalog.VersionOfExe(
                        process.MainModule?.FileName, _paths.ServerVersionsDir, BuildPackage.Server);
                }
            }
            catch (Exception)
            {
                // Access denied on another user's process; cannot tell its version.
            }
            finally
            {
                process.Dispose();
            }
        }

        RunningVersion = version;
        return version;
    }

    public TimeSpan? Uptime
    {
        get
        {
            try
            {
                return Started is { HasExited: false } p ? DateTime.Now - p.StartTime : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    public StartResult Start(string venue)
    {
        RefreshInstalled(); // the folder may have been copied in or deleted since the last scan
        var version = SelectedVersion;
        if (version == 0) return new StartResult(null, MissingVersionMessage);

        var exe = _paths.ServerExeFor(version);

        // Cache may be stale: the folder can be gone since the last refresh.
        if (!File.Exists(exe)) return new StartResult(null, $"Sunucu bulunamadı: {exe}");

        try
        {
            Directory.CreateDirectory(_paths.ReplaysDir);
            Directory.CreateDirectory(_paths.ServerDir); // --data-dir target; the server seeds it
        }
        catch (Exception ex)
        {
            return new StartResult(null, $"Klasör oluşturulamadı: {ex.Message}");
        }

        var info = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = _paths.ServerVersionDir(version),
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in LauncherSettings.ServerArguments(venue, _paths.ReplaysDir, _paths.ServerDir))
            info.ArgumentList.Add(argument);

        try
        {
            var process = Process.Start(info);
            if (process is null) return new StartResult(null, "Sunucu süreci başlatılamadı.");

            Started = process;
            RunningVersion = version;
            return new StartResult(process, null);
        }
        catch (Exception ex)
        {
            return new StartResult(null, $"Sunucu başlatılamadı: {ex.Message}");
        }
    }

    /// <summary>Message for a server that died during startup — exit code first, then the log tail.</summary>
    public string DescribeEarlyExit(Process process)
    {
        int code;
        try
        {
            code = process.ExitCode;
        }
        catch (Exception)
        {
            code = -1;
        }

        var reason = code == ExitCodeValidationFailed
            ? "Sunucu açılış doğrulamasını geçemedi (çıkış kodu 2). Mekanın lobisi ve haritaları eksik olabilir."
            : $"Sunucu beklenmedik şekilde kapandı (çıkış kodu {code}).";

        var tail = LogTailer.ReadTail(LogTailer.FindNewestLog(_paths.ServerLogsDir), 12);
        return tail.Length == 0 ? reason : $"{reason}\n\nGünlüğün sonu:\n{tail}";
    }

    /// <summary>
    /// Server processes belonging to THIS root. Matching on the exe path keeps a second deployment's
    /// server alive; an inaccessible <c>MainModule</c> (access denied) counts as a match by name.
    /// </summary>
    public IReadOnlyList<Process> FindProcesses()
    {
        var found = new List<Process>();
        foreach (var process in Process.GetProcessesByName(ProcessName))
        {
            try
            {
                var file = process.MainModule?.FileName;
                if (file is null || IsOurs(file))
                {
                    found.Add(process);
                    continue;
                }
            }
            catch (Exception)
            {
                found.Add(process);
                continue;
            }

            process.Dispose();
        }

        return found;
    }

    /// <summary>
    /// Any unpacked version's exe under this root, or the pre-version <c>server\</c> one.
    /// <para>⚠️ The legacy path stays matched: an old server left running must still be killable,
    /// otherwise it keeps the control port and the new one never comes up.</para>
    /// </summary>
    private bool IsOurs(string exePath)
        => BuildVersionCatalog.VersionOfExe(exePath, _paths.ServerVersionsDir, BuildPackage.Server) > 0 ||
           string.Equals(exePath, _paths.LegacyServerExe, StringComparison.OrdinalIgnoreCase);

    /// <summary>Last resort after a clean shutdown did not land. Returns how many were killed.</summary>
    public int KillProcesses()
    {
        int killed = 0;
        foreach (var process in FindProcesses())
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    killed++;
                }
            }
            catch (Exception)
            {
                // Already gone or not ours to kill.
            }
            finally
            {
                process.Dispose();
            }
        }

        Started = null;
        RunningVersion = 0;
        return killed;
    }

    public void ForgetStarted()
    {
        Started = null;
        RunningVersion = 0;
    }
}
