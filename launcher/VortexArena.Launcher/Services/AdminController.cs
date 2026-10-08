using System.Diagnostics;

namespace VortexArena.Launcher.Services;

/// <summary>
/// Starts and stops the admin build (<c>admin\VortexArena.exe</c>).
/// <para>⚠️ Replay playback starts the SAME exe but is tracked separately (by PID): a watched
/// recording must not make the Admin page look "running".</para>
/// </summary>
public sealed class AdminController
{
    private const string ProcessName = "VortexArena";

    private const int CloseGraceMs = 3000;

    private readonly LauncherPaths _paths;
    private readonly LauncherSettings _settings;
    private readonly HashSet<int> _replayPids = [];

    public AdminController(LauncherPaths paths, LauncherSettings settings)
    {
        _paths = paths;
        _settings = settings;
    }

    /// <summary>Admin process this launcher owns; null when none (or when an adopted one exited).</summary>
    public Process? Started { get; private set; }

    public bool IsRunning
    {
        get
        {
            try
            {
                return Started is { HasExited: false };
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public StartResult Start(int port)
    {
        if (!_paths.AdminExeExists)
            return new StartResult(null, $"Yönetim oyunu bulunamadı: {_paths.AdminExe}");

        if (!LauncherSettings.IsValidIp(_settings.ServerIp))
            return new StartResult(null, $"Geçersiz IP: '{_settings.ServerIp.Trim()}'. Örnek: 192.168.1.10");

        return StartAdmin(_settings.AdminArguments(port), track: true);
    }

    /// <summary>Opens one recording for playback; the instance stays out of the running count.</summary>
    public StartResult StartReplay(string replayPath)
    {
        if (!_paths.AdminExeExists)
            return new StartResult(null, $"Yönetim oyunu bulunamadı: {_paths.AdminExe}");

        var result = StartAdmin(LauncherSettings.ReplayArguments(replayPath), track: false);
        if (result.Process is not null) _replayPids.Add(result.Process.Id);
        return result;
    }

    private StartResult StartAdmin(IReadOnlyList<string> arguments, bool track)
    {
        var info = new ProcessStartInfo
        {
            FileName = _paths.AdminExe,
            WorkingDirectory = _paths.AdminDir,
            UseShellExecute = false,
        };

        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        try
        {
            var process = Process.Start(info);
            if (process is null) return new StartResult(null, "Yönetim oyunu başlatılamadı.");
            if (track) Started = process;
            return new StartResult(process, null);
        }
        catch (Exception ex)
        {
            return new StartResult(null, $"Yönetim oyunu başlatılamadı: {ex.Message}");
        }
    }

    /// <summary>Asks politely, then kills. Returns what the operator should be told.</summary>
    public async Task<string> StopAsync()
    {
        var process = Started;
        if (process is null) return "Yönetim oyunu çalışmıyor.";

        try
        {
            if (process.HasExited)
            {
                Started = null;
                return "Yönetim oyunu zaten kapanmış.";
            }

            process.CloseMainWindow();
            var exited = await Task.Run(() => process.WaitForExit(CloseGraceMs)).ConfigureAwait(true);
            if (!exited)
            {
                process.Kill(entireProcessTree: true);
                Started = null;
                return "Yönetim oyunu yanıt vermedi, zorla kapatıldı.";
            }

            Started = null;
            return "Yönetim oyunu kapatıldı.";
        }
        catch (Exception ex)
        {
            Started = null;
            return $"Yönetim oyunu kapatılamadı: {ex.Message}";
        }
    }

    /// <summary>
    /// Picks up an admin left running by a previous launcher session. Replay instances are excluded
    /// by PID; an unreadable <c>MainModule</c> is skipped rather than guessed.
    /// </summary>
    public void AdoptRunningInstance()
    {
        if (IsRunning) return;

        foreach (var process in Process.GetProcessesByName(ProcessName))
        {
            var adopt = false;
            try
            {
                adopt = !_replayPids.Contains(process.Id) &&
                        string.Equals(process.MainModule?.FileName, _paths.AdminExe,
                            StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                // Access denied on another user's process; cannot verify, so do not adopt.
            }

            if (adopt && Started is null)
            {
                Started = process;
                continue;
            }

            process.Dispose();
        }
    }
}
