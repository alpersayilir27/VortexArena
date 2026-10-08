using System.Diagnostics;
using System.IO;

namespace VortexArena.Launcher.Services;

/// <summary>Result of a start attempt: the process when it came up, otherwise why not.</summary>
public sealed record StartResult(Process? Process, string? Error)
{
    public bool Ok => Error is null;
}

/// <summary>
/// Starts and stops <c>server\VortexArena.Server.App.exe</c>.
/// <para>⚠️ <b>stdout is NOT redirected.</b> A redirected pipe dies with the launcher and the server
/// would go down with it on the next write; the log file is the operator's window instead.</para>
/// </summary>
public sealed class ServerController
{
    /// <summary>Server exit code for "startup validation failed" (Docs/ArenaNet-Protokol.md).</summary>
    public const int ExitCodeValidationFailed = 2;

    private const string ProcessName = "VortexArena.Server.App";

    private readonly LauncherPaths _paths;

    public ServerController(LauncherPaths paths) => _paths = paths;

    /// <summary>Process this launcher started; null when the server runs on its own.</summary>
    public Process? Started { get; private set; }

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
        if (!_paths.ServerExeExists)
            return new StartResult(null, $"Sunucu bulunamadı: {_paths.ServerExe}");

        try
        {
            Directory.CreateDirectory(_paths.ReplaysDir);
        }
        catch (Exception ex)
        {
            return new StartResult(null, $"Kayıt klasörü oluşturulamadı: {ex.Message}");
        }

        var info = new ProcessStartInfo
        {
            FileName = _paths.ServerExe,
            WorkingDirectory = _paths.ServerDir,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in LauncherSettings.ServerArguments(venue, _paths.ReplaysDir))
            info.ArgumentList.Add(argument);

        try
        {
            var process = Process.Start(info);
            if (process is null) return new StartResult(null, "Sunucu süreci başlatılamadı.");

            Started = process;
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
                if (file is null ||
                    string.Equals(file, _paths.ServerExe, StringComparison.OrdinalIgnoreCase))
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
        return killed;
    }

    public void ForgetStarted() => Started = null;
}
