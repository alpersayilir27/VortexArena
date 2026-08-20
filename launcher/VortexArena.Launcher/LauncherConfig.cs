using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VortexArena.Launcher;

/// <summary>
/// Launcher's persisted settings + the single place that builds launch arguments.
/// <para>
/// Settings live in the user profile (<see cref="SettingsPath"/>), NOT next to the launcher folder:
/// a redeploy (deploy-launcher.bat wipes and re-copies the output) must not lose the operator's
/// IP/paths.
/// </para>
/// <para>
/// ⚠️ <b>Argument names are a contract.</b> <see cref="ArgServerIp"/>/<see cref="ArgServerPort"/>
/// must match Unity's <c>AppBoot.ArgServerIp</c>/<c>ArgServerPort</c> exactly, and
/// <see cref="ArgVenue"/> the <c>--venue</c> read by the server's <c>Program.SelectVenue</c>. Both
/// are covered by tests (<c>LauncherConfigTests</c>) — change one and you change BOTH sides.
/// </para>
/// </summary>
public sealed class LauncherConfig
{
    /// <summary>Protocol WS control port (ArenaProtocol.CONTROL_PORT).</summary>
    public const int DefaultPort = 47821;

    /// <summary>Unity <c>AppBoot.ArgServerIp</c>.</summary>
    public const string ArgServerIp = "--server-ip";

    /// <summary>Unity <c>AppBoot.ArgServerPort</c>.</summary>
    public const string ArgServerPort = "--server-port";

    /// <summary>Server's venue argument (<c>Program.SelectVenue</c>).</summary>
    public const string ArgVenue = "--venue";

    [JsonPropertyName("adminExePath")]
    public string AdminExePath { get; set; } = "";

    [JsonPropertyName("serverIp")]
    public string ServerIp { get; set; } = "127.0.0.1";

    [JsonPropertyName("serverPort")]
    public int ServerPort { get; set; } = DefaultPort;

    /// <summary>Server exe (<c>deploy\server\VortexArena.Server.App.exe</c>); empty = the server is
    /// not started from this launcher and is run by hand.</summary>
    [JsonPropertyName("serverExePath")]
    public string ServerExePath { get; set; } = "";

    /// <summary>Venue to open this session — passed to the server as <c>--venue</c>.</summary>
    [JsonPropertyName("venue")]
    public string Venue { get; set; } = "";

    // ----------------------------------------------------------------- persistence

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary><c>%APPDATA%\VortexArena\launcher\settings.json</c>.</summary>
    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VortexArena", "launcher", "settings.json");

    /// <summary>Reads settings. Missing/corrupt file returns defaults — the launcher must open so
    /// the operator can re-enter them.</summary>
    public static LauncherConfig Load() => Load(SettingsPath);

    public static LauncherConfig Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new LauncherConfig();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<LauncherConfig>(json, JsonOptions) ?? new LauncherConfig();
        }
        catch (Exception)
        {
            return new LauncherConfig();
        }
    }

    public void Save() => Save(SettingsPath);

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    // ------------------------------------------------------------------ validation

    public bool AdminExeExists => AdminExePath.Length > 0 && File.Exists(AdminExePath);

    public bool ServerExeExists => ServerExePath.Length > 0 && File.Exists(ServerExePath);

    /// <summary>
    /// Is the address fully written.
    /// <para>
    /// ⚠️ <b><see cref="IPAddress.TryParse(string, out IPAddress)"/> alone is not enough:</b> .NET
    /// accepts short forms for legacy compatibility and silently turns <c>"192.168.1"</c> into
    /// <c>192.168.0.1</c>. The operator then dials the wrong machine and only sees "cannot connect".
    /// Hence four parts are required for IPv4.
    /// </para>
    /// </summary>
    public static bool IsValidIp(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return false;
        if (!IPAddress.TryParse(trimmed, out var address)) return false;

        return address.AddressFamily != AddressFamily.InterNetwork ||
               trimmed.Count(c => c == '.') == 3;
    }

    public static bool IsValidPort(int value) => value > 0 && value <= 65535;

    /// <summary>Arguments passed to the admin game — read by <c>AppBoot</c>.</summary>
    public IReadOnlyList<string> GameArguments =>
    [
        ArgServerIp, ServerIp.Trim(),
        ArgServerPort, ServerPort.ToString(CultureInfo.InvariantCulture),
    ];

    /// <summary>
    /// Arguments passed to the server. <b>The venue is always explicit</b> — the launcher never
    /// starts a venue-less server (<see cref="ValidateServer"/>), because without one the server
    /// silently picks the alphabetically first venue.
    /// </summary>
    public IReadOnlyList<string> ServerArguments
    {
        get
        {
            var venue = Venue.Trim();
            return venue.Length == 0 ? [] : [ArgVenue, venue];
        }
    }

    /// <summary>Can the admin game start; otherwise the reason to show the operator.</summary>
    public string? Validate()
    {
        if (AdminExePath.Trim().Length == 0)
            return "Admin exe seçilmedi — Gözat ile deploy\\admin\\VortexArena.exe dosyasını seçin.";

        if (!AdminExeExists)
            return $"Admin exe bulunamadı: {AdminExePath}";

        if (!IsValidIp(ServerIp))
            return $"Geçersiz IP: '{ServerIp.Trim()}'. Örnek: 192.168.1.10";

        if (!IsValidPort(ServerPort))
            return $"Geçersiz port: {ServerPort}. 1-65535 arası olmalı.";

        return null;
    }

    /// <summary>
    /// Can the server start; otherwise the reason.
    /// <para>
    /// <b>The venue is mandatory</b> by design: started without one and without an interactive
    /// console (script/service/launcher), the server silently opens the alphabetically first venue
    /// and the operator manages the wrong business's arenas. The launcher never leaves that path
    /// open.
    /// </para>
    /// </summary>
    /// <param name="knownVenues">Venues read from <c>maps.json</c>; empty when unreadable.</param>
    public string? ValidateServer(IReadOnlyList<string> knownVenues)
    {
        if (ServerExePath.Trim().Length == 0)
            return "Sunucu exe seçilmedi — Gözat ile deploy\\server\\VortexArena.Server.App.exe dosyasını seçin.";

        if (!ServerExeExists)
            return $"Sunucu exe bulunamadı: {ServerExePath}";

        var venue = Venue.Trim();
        if (venue.Length == 0)
        {
            return knownVenues.Count == 0
                ? "Mekan yazılmadı. maps.json okunamadığı için liste çıkarılamıyor — mekan adını elle yazın."
                : "Mekan seçilmedi. Mekansız başlatılırsa sunucu alfabetik ilk mekanı açar; listeden seçin.";
        }

        if (knownVenues.Count > 0 &&
            !knownVenues.Any(v => string.Equals(v, venue, StringComparison.OrdinalIgnoreCase)))
        {
            return $"'{venue}' bu sunucunun maps.json'unda yok. Bilinen mekanlar: {string.Join(", ", knownVenues)}.";
        }

        return null;
    }
}
