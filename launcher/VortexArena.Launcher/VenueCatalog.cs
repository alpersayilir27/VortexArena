using System.IO;
using System.Text.Json;

namespace VortexArena.Launcher;

/// <summary>One row of the venue list: name + map count + whether it has a lobby.</summary>
/// <param name="Name">Venue name — the value passed to the server as <c>--venue</c>.</param>
/// <param name="MapCount">Number of maps in this venue.</param>
/// <param name="HasLobby">Does the venue have a map with <c>modes == ["lobby"]</c>. Without one the
/// server cannot resolve an open scene and fails fast with exit code 2 (§11).</param>
public sealed record VenueInfo(string Name, int MapCount, bool HasLobby)
{
    /// <summary>Sub-line shown under the name. A lobby-less venue is shown in red — a server started
    /// with it never comes up.</summary>
    public string Summary => HasLobby
        ? $"{MapCount} harita · lobi var"
        : $"{MapCount} harita · LOBİ YOK — sunucu açılmaz";
}

/// <summary>
/// Extracts the venue list from the server's <c>config\maps.json</c> — the source feeding the
/// launcher's venue picker.
/// <para>
/// ⚠️ <b>No second catalog is kept.</b> The list comes from the file the server itself reads; no
/// venue name is hard-coded in the launcher. Adding a business via <c>Export Server Config</c> in
/// Unity therefore requires no launcher work.
/// </para>
/// <para>
/// The file is searched from next to the server exe upwards (same behaviour as the server):
/// <c>deploy\server\config\maps.json</c> when deployed, <c>Server\config\maps.json</c> in
/// development.
/// </para>
/// </summary>
public sealed class VenueCatalog
{
    /// <summary>For older exports leaving <c>venue</c> empty — must match the server's
    /// <c>MapTable.DefaultVenue</c>.</summary>
    public const string DefaultVenue = "Standard";

    /// <summary>Must match <c>ArenaProtocol.LOBBY_MODE_ID</c>.</summary>
    public const string LobbyModeId = "lobby";

    /// <summary>How many levels above the exe the search walks up.</summary>
    private const int SearchDepth = 6;

    public static VenueCatalog Empty { get; } = new([], null, null);

    private VenueCatalog(IReadOnlyList<VenueInfo> venues, string? sourcePath, string? problem)
    {
        Venues = venues;
        SourcePath = sourcePath;
        Problem = problem;
    }

    /// <summary>Discovered venues (sorted by name). Empty when unreadable.</summary>
    public IReadOnlyList<VenueInfo> Venues { get; }

    /// <summary>Full path of the <c>maps.json</c> read; null when not found.</summary>
    public string? SourcePath { get; }

    /// <summary>Why it could not be read (shown to the operator); null when fine.</summary>
    public string? Problem { get; }

    public IReadOnlyList<string> Names => Venues.Select(v => v.Name).ToArray();

    /// <summary>Builds the venue list starting from the server exe path.</summary>
    public static VenueCatalog ForServerExe(string serverExePath)
    {
        if (string.IsNullOrWhiteSpace(serverExePath))
            return new VenueCatalog([], null, "Sunucu exe seçilmedi.");

        var mapsPath = FindMapsJson(serverExePath);
        if (mapsPath == null)
        {
            return new VenueCatalog([], null,
                "config\\maps.json bulunamadı. Unity'de Tools > VortexArena > Server > Export Server Config " +
                "çalıştırıp sunucuyu yeniden dağıtın.");
        }

        try
        {
            return FromJson(File.ReadAllText(mapsPath), mapsPath);
        }
        catch (Exception ex)
        {
            return new VenueCatalog([], mapsPath, $"maps.json okunamadı: {ex.Message}");
        }
    }

    /// <summary>Searches for <c>config\maps.json</c> from next to the exe upwards.</summary>
    public static string? FindMapsJson(string serverExePath)
    {
        string? dir;
        try
        {
            dir = Path.GetDirectoryName(Path.GetFullPath(serverExePath));
        }
        catch (Exception)
        {
            return null;
        }

        for (int i = 0; i < SearchDepth && dir != null; i++)
        {
            var candidate = Path.Combine(dir, "config", "maps.json");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    /// <summary>Parses maps.json. Shape: <c>{ "maps": [ { sceneName, venue, modes } ] }</c>.</summary>
    public static VenueCatalog FromJson(string json, string? sourcePath)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lobbies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var display = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            if (!doc.RootElement.TryGetProperty("maps", out var maps) ||
                maps.ValueKind != JsonValueKind.Array)
            {
                return new VenueCatalog([], sourcePath, "maps.json'da 'maps' dizisi yok.");
            }

            foreach (var entry in maps.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;

                var venue = entry.TryGetProperty("venue", out var v) && v.ValueKind == JsonValueKind.String
                    ? (v.GetString() ?? "").Trim()
                    : "";
                if (venue.Length == 0) venue = DefaultVenue;

                display.TryAdd(venue, venue);
                counts[venue] = counts.TryGetValue(venue, out var c) ? c + 1 : 1;

                if (IsLobby(entry)) lobbies.Add(venue);
            }
        }
        catch (JsonException ex)
        {
            return new VenueCatalog([], sourcePath, $"maps.json ayrıştırılamadı: {ex.Message}");
        }

        if (counts.Count == 0)
            return new VenueCatalog([], sourcePath, "maps.json'da harita yok.");

        var venues = counts.Keys
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => new VenueInfo(display[name], counts[name], lobbies.Contains(name)))
            .ToArray();

        return new VenueCatalog(venues, sourcePath, null);
    }

    /// <summary>Same rule as the server: lobby = <c>modes</c> is exactly <c>["lobby"]</c>. The scene
    /// name is NOT considered.</summary>
    private static bool IsLobby(JsonElement entry)
    {
        if (!entry.TryGetProperty("modes", out var modes) || modes.ValueKind != JsonValueKind.Array)
            return false;

        if (modes.GetArrayLength() != 1) return false;

        var only = modes[0];
        return only.ValueKind == JsonValueKind.String &&
               string.Equals(only.GetString(), LobbyModeId, StringComparison.OrdinalIgnoreCase);
    }
}
