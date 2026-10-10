using System.Diagnostics;
using System.IO;

namespace VortexArena.Launcher.Services;

/// <summary>
/// Starts and stops the admin build (<c>admin_versions\admin_v&lt;N&gt;\VortexArena.exe</c>).
/// <para>⚠️ Replay playback starts the SAME exe but is tracked separately (by PID): a watched
/// recording must not make the Admin page look "running".</para>
/// </summary>
public sealed class AdminController
{
    private const string ProcessName = "VortexArena";

    private const int CloseGraceMs = 3000;

    private const string MissingVersionMessage = "Admin sürümü yok — aşağıdan bir sürüm indirin.";

    private readonly LauncherPaths _paths;
    private readonly LauncherSettings _settings;
    private readonly InstalledBuilds _builds;
    private readonly HashSet<int> _replayPids = [];

    public AdminController(LauncherPaths paths, LauncherSettings settings)
    {
        _paths = paths;
        _settings = settings;
        _builds = new InstalledBuilds(paths.AdminVersionsDir, BuildPackage.Admin, () => settings.AdminVersion);
    }

    /// <summary>Admin process this launcher owns; null when none (or when an adopted one exited).</summary>
    public Process? Started { get; private set; }

    /// <summary>Version of the tracked process; 0 when nothing is running.</summary>
    public int RunningVersion { get; private set; }

    /// <summary>Unpacked versions, newest first. Cached — <see cref="RefreshInstalled"/> re-reads the disk.</summary>
    public IReadOnlyList<int> InstalledVersions => _builds.Versions;

    /// <summary>Version <see cref="Start"/> would launch; 0 when no build is unpacked.</summary>
    public int SelectedVersion => _builds.Selected;

    /// <summary>Re-reads the unpacked versions; true when the set changed.</summary>
    public bool RefreshInstalled() => _builds.Refresh();

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
        RefreshInstalled(); // the folder may have been copied in or deleted since the last scan
        var version = SelectedVersion;
        if (version == 0) return new StartResult(null, MissingVersionMessage);

        if (!LauncherSettings.IsValidIp(_settings.ServerIp))
            return new StartResult(null, $"Geçersiz IP: '{_settings.ServerIp.Trim()}'. Örnek: 192.168.1.10");

        return StartAdmin(version, _settings.AdminArguments(port), track: true);
    }

    /// <summary>Opens one recording for playback; the instance stays out of the running count.</summary>
    public StartResult StartReplay(string replayPath)
    {
        RefreshInstalled();
        var version = SelectedVersion;
        if (version == 0) return new StartResult(null, MissingVersionMessage);

        var result = StartAdmin(version, LauncherSettings.ReplayArguments(replayPath), track: false);
        if (result.Process is not null) _replayPids.Add(result.Process.Id);
        return result;
    }

    private StartResult StartAdmin(int version, IReadOnlyList<string> arguments, bool track)
    {
        var exe = _paths.AdminExeFor(version);

        // Cache may be stale: the folder can be gone since the last refresh.
        if (!File.Exists(exe)) return new StartResult(null, $"Yönetim oyunu bulunamadı: {exe}");

        var info = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = _paths.AdminVersionDir(version),
            UseShellExecute = false,
        };

        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        try
        {
            var process = Process.Start(info);
            if (process is null) return new StartResult(null, "Yönetim oyunu başlatılamadı.");
            if (track)
            {
                Started = process;
                RunningVersion = version;
            }

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
                Forget();
                return "Yönetim oyunu zaten kapanmış.";
            }

            process.CloseMainWindow();
            var exited = await Task.Run(() => process.WaitForExit(CloseGraceMs)).ConfigureAwait(true);
            if (!exited)
            {
                process.Kill(entireProcessTree: true);
                Forget();
                return "Yönetim oyunu yanıt vermedi, zorla kapatıldı.";
            }

            Forget();
            return "Yönetim oyunu kapatıldı.";
        }
        catch (Exception ex)
        {
            Forget();
            return $"Yönetim oyunu kapatılamadı: {ex.Message}";
        }
    }

    private void Forget()
    {
        Started = null;
        RunningVersion = 0;
    }

    /// <summary>
    /// Picks up an admin left running by a previous launcher session, whichever version it is.
    /// Replay instances are excluded by PID; an unreadable <c>MainModule</c> is skipped rather than
    /// guessed.
    /// </summary>
    public void AdoptRunningInstance()
    {
        if (IsRunning) return;

        foreach (var process in Process.GetProcessesByName(ProcessName))
        {
            var version = 0;
            try
            {
                if (!_replayPids.Contains(process.Id))
                {
                    version = BuildVersionCatalog.VersionOfExe(
                        process.MainModule?.FileName, _paths.AdminVersionsDir, BuildPackage.Admin);
                }
            }
            catch (Exception)
            {
                // Access denied on another user's process; cannot verify, so do not adopt.
            }

            if (version > 0 && Started is null)
            {
                Started = process;
                RunningVersion = version;
                continue;
            }

            process.Dispose();
        }
    }
}
