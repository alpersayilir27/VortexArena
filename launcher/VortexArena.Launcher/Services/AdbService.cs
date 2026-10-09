using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace VortexArena.Launcher.Services;

/// <summary>One line of <c>adb devices -l</c>.</summary>
/// <param name="Serial">Headset serial — the <c>-s</c> target.</param>
/// <param name="State">Raw adb state: <c>device</c>, <c>unauthorized</c>, <c>offline</c>…</param>
/// <param name="Model">Model from the <c>model:</c> field; empty when adb did not report one.</param>
public sealed record AdbDevice(string Serial, string State, string Model)
{
    public const string StateReady = "device";
    public const string StateUnauthorized = "unauthorized";
    public const string StateOffline = "offline";

    /// <summary>Authorized and usable for install.</summary>
    public bool IsReady => State == StateReady;

    public string Display => Model.Length > 0 ? $"{Model} ({Serial})" : Serial;
}

public sealed record AdbResult(int ExitCode, string StdOut, string StdErr, bool TimedOut)
{
    public bool Ok => !TimedOut && ExitCode == 0;

    /// <summary>stdout + stderr — adb reports install failures on both depending on the step.</summary>
    public string Combined => (StdOut + "\n" + StdErr).Trim();
}

/// <summary>
/// adb wrapper. adb runs from the package's <c>platform-tools\</c> folder, so no operator ever
/// installs the Android SDK.
/// <para>⚠️ The adb server is never killed on exit — other tools on the PC may be using it.</para>
/// </summary>
public sealed class AdbService(string adbExe)
{
    public static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Installing a ~1 GB APK over USB takes minutes.</summary>
    public static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(5);

    private static readonly Regex PackageLine = new(
        @"^package:com\.vortex\.arenav(\d+)$", RegexOptions.Compiled);

    /// <summary>Checks adb is in place and starts its server. Returns the operator-facing error, or null.</summary>
    public async Task<string?> EnsureReadyAsync(CancellationToken ct)
    {
        if (!File.Exists(adbExe))
            return $"adb yok. platform-tools klasörünü şuraya koyun: {Path.GetDirectoryName(adbExe)}";

        var start = await RunAsync(["start-server"], ShortTimeout, ct).ConfigureAwait(false);
        if (start.TimedOut) return "adb başlatılamadı: start-server zaman aşımına uğradı.";
        if (start.ExitCode != 0) return $"adb başlatılamadı: {Firstline(start.Combined)}";

        return null;
    }

    public async Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(CancellationToken ct)
    {
        var result = await RunAsync(["devices", "-l"], ShortTimeout, ct).ConfigureAwait(false);
        return result.TimedOut ? [] : ParseDevices(result.StdOut);
    }

    /// <summary>Versions of our game installed on one headset.</summary>
    public async Task<IReadOnlyList<int>> ListInstalledVersionsAsync(string serial, CancellationToken ct)
    {
        var result = await RunAsync(
            ["-s", serial, "shell", "pm", "list", "packages", GamePackage.Prefix],
            ShortTimeout, ct).ConfigureAwait(false);

        return result.TimedOut ? [] : ParseInstalledVersions(result.StdOut);
    }

    /// <summary><c>install -r -g</c>: replaces only its own package id and pre-grants permissions.</summary>
    public Task<AdbResult> InstallAsync(string serial, string apkPath, CancellationToken ct)
        => RunAsync(["-s", serial, "install", "-r", "-g", apkPath], InstallTimeout, ct);

    /// <summary>Removes ONE version's package. Never called without the operator confirming.</summary>
    public Task<AdbResult> UninstallAsync(string serial, string packageName, CancellationToken ct)
        => RunAsync(["-s", serial, "uninstall", packageName], ShortTimeout, ct);

    // --------------------------------------------------------------------- process

    public async Task<AdbResult> RunAsync(
        IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct)
    {
        var info = new ProcessStartInfo
        {
            FileName = adbExe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = info };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new AdbResult(-1, "", ex.Message, false);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            if (ct.IsCancellationRequested) throw;
            return new AdbResult(-1, stdout.ToString(), stderr.ToString(), true);
        }

        // Flushes the async readers; the process already exited.
        process.WaitForExit();
        return new AdbResult(process.ExitCode, stdout.ToString(), stderr.ToString(), false);
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Already gone.
        }
    }

    // --------------------------------------------------------------------- parsers

    /// <summary>
    /// Parses <c>adb devices -l</c>. The banner line and blanks are skipped; the second column is
    /// the state and <c>model:</c> is picked out of the trailing key:value fields.
    /// </summary>
    public static IReadOnlyList<AdbDevice> ParseDevices(string stdout)
    {
        var devices = new List<AdbDevice>();
        foreach (var raw in stdout.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith('*')) continue;

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            var model = "";
            foreach (var field in parts.Skip(2))
            {
                if (field.StartsWith("model:", StringComparison.Ordinal))
                    model = field["model:".Length..].Replace('_', ' ');
            }

            devices.Add(new AdbDevice(parts[0], parts[1], model));
        }

        return devices;
    }

    /// <summary>Parses <c>pm list packages</c> into our version numbers, biggest first.</summary>
    public static IReadOnlyList<int> ParseInstalledVersions(string stdout)
    {
        var versions = new SortedSet<int>();
        foreach (var raw in stdout.Split('\n'))
        {
            var match = PackageLine.Match(raw.Trim());
            if (match.Success && int.TryParse(match.Groups[1].Value, out var version))
                versions.Add(version);
        }

        return versions.Reverse().ToArray();
    }

    private static string Firstline(string text)
    {
        var line = text.Split('\n').FirstOrDefault(l => l.Trim().Length > 0);
        return line?.Trim() ?? "bilinmeyen hata";
    }
}
