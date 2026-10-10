using System.IO;
using System.Text.Json;

namespace VortexArena.Launcher.Services;

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
/// ⚠️ Read only from the selected build's own <c>config\maps.json</c>, as the server does — no
/// search upwards: a file found in a parent folder would list venues that build cannot open.
/// </para>
/// </summary>
public sealed class VenueCatalog
{
    /// <summary>For older exports leaving <c>venue</c> empty — must match the server's
    /// <c>MapTable.DefaultVenue</c>.</summary>
    public const string DefaultVenue = "Standard";

    /// <summary>Must match <c>ArenaProtocol.LOBBY_MODE_ID</c>.</summary>
    public const string LobbyModeId = "lobby";

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

    /// <summary>Builds the venue list from a build's <c>config\maps.json</c>.</summary>
    public static VenueCatalog ForMapsFile(string mapsPath)
    {
        if (string.IsNullOrWhiteSpace(mapsPath))
            return new VenueCatalog([], null, "Sunucu klasörü çözülemedi.");

        if (!File.Exists(mapsPath))
        {
            return new VenueCatalog([], null,
                $"Bu sunucu sürümünde config\\maps.json yok ({mapsPath}). Sürümü silip yeniden indirin; " +
                "sorun sürerse sunucu yeniden dağıtılmalı.");
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
