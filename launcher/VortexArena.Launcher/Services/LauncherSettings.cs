using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VortexArena.Launcher.Services;

/// <summary>
/// Persisted operator settings + the single place that builds launch arguments.
/// <para>
/// Settings live in the user profile (<see cref="SettingsPath"/>), NOT next to the launcher:
/// a redeploy overwrites the exe and must not lose the operator's choices.
/// </para>
/// <para>
/// ⚠️ <b>Argument names are a contract.</b> <see cref="ArgServerIp"/>/<see cref="ArgServerPort"/>/
/// <see cref="ArgReplay"/> must match Unity's <c>AppBoot</c> exactly, and <see cref="ArgVenue"/>/
/// <see cref="ArgReplayDir"/> the server's own parsing. Tests pin all of them — change one and you
/// change BOTH sides.
/// </para>
/// </summary>
public sealed class LauncherSettings
{
    /// <summary>Protocol control port (<c>ArenaProtocol.CONTROL_PORT</c>).</summary>
    public const int DefaultPort = 47821;

    /// <summary>Unity <c>AppBoot.ArgServerIp</c>.</summary>
    public const string ArgServerIp = "--server-ip";

    /// <summary>Unity <c>AppBoot.ArgServerPort</c>.</summary>
    public const string ArgServerPort = "--server-port";

    /// <summary>Unity <c>AppBoot.ArgReplay</c> — opens the admin build straight into playback.</summary>
    public const string ArgReplay = "--replay";

    /// <summary>Server's venue argument.</summary>
    public const string ArgVenue = "--venue";

    /// <summary>Server's replay output folder argument.</summary>
    public const string ArgReplayDir = "--replay-dir";

    public const string DefaultVersionsUrl = "http://159.100.20.26:8091/versions";

    public const string DefaultDownloadBaseUrl = "http://159.100.20.26:8090/game_versions/";

    /// <summary>Venue to open this session — passed to the server as <c>--venue</c>.</summary>
    [JsonPropertyName("venue")]
    public string Venue { get; set; } = "";

    /// <summary>Control port override; 0 = read <c>server\config\server.json</c>, then fall back to
    /// <see cref="DefaultPort"/>.</summary>
    [JsonPropertyName("controlPortOverride")]
    public int ControlPortOverride { get; set; }

    /// <summary>Address the admin build dials. Loopback because launcher and server share the PC.</summary>
    [JsonPropertyName("serverIp")]
    public string ServerIp { get; set; } = "127.0.0.1";

    [JsonPropertyName("versionsUrl")]
    public string VersionsUrl { get; set; } = DefaultVersionsUrl;

    [JsonPropertyName("downloadBaseUrl")]
    public string DownloadBaseUrl { get; set; } = DefaultDownloadBaseUrl;

    /// <summary>Last headset the operator picked; re-selected when it shows up again.</summary>
    [JsonPropertyName("preferredDeviceSerial")]
    public string PreferredDeviceSerial { get; set; } = "";

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

    /// <summary>Reads settings; a missing or corrupt file yields defaults so the launcher still opens.</summary>
    public static LauncherSettings Load() => Load(SettingsPath);

    public static LauncherSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new LauncherSettings();
            var loaded = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(path), JsonOptions);
            return loaded is null ? new LauncherSettings() : loaded.Normalized();
        }
        catch (Exception)
        {
            return new LauncherSettings();
        }
    }

    public void Save() => Save(SettingsPath);

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    /// <summary>Fills blanks left by an older settings file (its keys are a subset of today's).</summary>
    private LauncherSettings Normalized()
    {
        if (string.IsNullOrWhiteSpace(ServerIp)) ServerIp = "127.0.0.1";
        if (string.IsNullOrWhiteSpace(VersionsUrl)) VersionsUrl = DefaultVersionsUrl;
        if (string.IsNullOrWhiteSpace(DownloadBaseUrl)) DownloadBaseUrl = DefaultDownloadBaseUrl;
        return this;
    }

    // ------------------------------------------------------------------ validation

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

    // ------------------------------------------------------------------- arguments

    /// <summary>Arguments for the admin build — read by <c>AppBoot</c>.</summary>
    public IReadOnlyList<string> AdminArguments(int port) =>
    [
        ArgServerIp, ServerIp.Trim(),
        ArgServerPort, port.ToString(CultureInfo.InvariantCulture),
    ];

    /// <summary>Arguments for the server. <b>The venue is always explicit</b> — see
    /// <see cref="ValidateVenue"/>.</summary>
    public static IReadOnlyList<string> ServerArguments(string venue, string replayDir)
    {
        var trimmed = venue.Trim();
        return trimmed.Length == 0 ? [] : [ArgVenue, trimmed, ArgReplayDir, replayDir];
    }

    /// <summary>Arguments for watching one recording in the admin build.</summary>
    public static IReadOnlyList<string> ReplayArguments(string replayPath) => [ArgReplay, replayPath];

    /// <summary>
    /// Why the server cannot start with this venue; null when fine.
    /// <para>
    /// <b>The venue is mandatory</b> by design: started without one and without an interactive
    /// console (script/service/launcher), the server silently opens the alphabetically first venue
    /// and the operator manages the wrong business's arenas.
    /// </para>
    /// </summary>
    public static string? ValidateVenue(string venue, IReadOnlyList<string> knownVenues)
    {
        var trimmed = venue.Trim();
        if (trimmed.Length == 0)
        {
            return knownVenues.Count == 0
                ? "Mekan seçilmedi. maps.json okunamadığı için liste çıkarılamıyor."
                : "Mekan seçilmedi. Mekansız başlatılırsa sunucu alfabetik ilk mekanı açar; listeden seçin.";
        }

        if (knownVenues.Count > 0 &&
            !knownVenues.Any(v => string.Equals(v, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return $"'{trimmed}' bu sunucunun maps.json'unda yok. Bilinen mekanlar: {string.Join(", ", knownVenues)}.";
        }

        return null;
    }

    // ------------------------------------------------------------------------ port

    /// <summary>Override → <c>server.json</c> → <see cref="DefaultPort"/>.</summary>
    public int ResolveControlPort(string serverConfigJsonPath)
    {
        if (IsValidPort(ControlPortOverride)) return ControlPortOverride;

        try
        {
            if (File.Exists(serverConfigJsonPath))
            {
                var fromConfig = ParseControlPort(File.ReadAllText(serverConfigJsonPath));
                if (fromConfig.HasValue) return fromConfig.Value;
            }
        }
        catch (Exception)
        {
            // Unreadable config falls through to the protocol default.
        }

        return DefaultPort;
    }

    /// <summary>Reads <c>controlPort</c> out of the server config; null when absent or unusable.</summary>
    public static int? ParseControlPort(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty("controlPort", out var port)) return null;
            if (port.ValueKind != JsonValueKind.Number) return null;
            if (!port.TryGetInt32(out var value)) return null;

            return IsValidPort(value) ? value : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
