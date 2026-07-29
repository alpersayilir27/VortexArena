using System.IO;
using System.Text.Json;

namespace VortexArena.Launcher;

/// <summary>Mekan listesindeki tek satır: ad + kaç harita + lobisi var mı.</summary>
/// <param name="Name">Mekan adı — sunucuya <c>--venue</c> ile geçen değer.</param>
/// <param name="MapCount">Bu mekana ait harita sayısı.</param>
/// <param name="HasLobby">Mekanda <c>modes == ["lobby"]</c> olan bir harita var mı. Yoksa sunucu
/// açık sahne çözemez ve §11 fail-fast ile 2 çıkış koduyla kapanır.</param>
public sealed record VenueInfo(string Name, int MapCount, bool HasLobby)
{
    /// <summary>Listede adın altında görünen satır. Lobisi olmayan mekan kırmızı gösterilir —
    /// o mekanla başlatılan sunucu hiç açılmaz.</summary>
    public string Summary => HasLobby
        ? $"{MapCount} harita · lobi var"
        : $"{MapCount} harita · LOBİ YOK — sunucu açılmaz";
}

/// <summary>
/// Sunucunun <c>config\maps.json</c> dosyasından mekan listesini çıkarır — launcher'ın mekan
/// seçicisini besleyen kaynak budur.
/// <para>
/// ⚠️ <b>İkinci bir katalog tutulmaz.</b> Liste, sunucunun kendi okuduğu dosyadan gelir; launcher
/// içinde mekan adı sabitlenmez. Böylece Unity'de <c>Export Server Config</c> çalıştırılıp yeni bir
/// işletme eklendiğinde launcher'da yapılacak iş yoktur.
/// </para>
/// <para>
/// Dosya, sunucu exe'sinin yanından başlayıp yukarı doğru aranır (sunucunun kendi davranışının
/// aynısı): dağıtımda <c>deploy\server\config\maps.json</c>, geliştirmede
/// <c>Server\config\maps.json</c> bulunur.
/// </para>
/// </summary>
public sealed class VenueCatalog
{
    /// <summary><c>venue</c> alanı boş bırakılmış eski export'lar için — sunucudaki
    /// <c>MapTable.DefaultVenue</c> ile aynı olmalı.</summary>
    public const string DefaultVenue = "Standard";

    /// <summary><c>ArenaProtocol.LOBBY_MODE_ID</c> ile aynı olmalı.</summary>
    public const string LobbyModeId = "lobby";

    /// <summary>Aranan dosyanın exe'den yukarı kaç seviye takip edileceği.</summary>
    private const int SearchDepth = 6;

    public static VenueCatalog Empty { get; } = new([], null, null);

    private VenueCatalog(IReadOnlyList<VenueInfo> venues, string? sourcePath, string? problem)
    {
        Venues = venues;
        SourcePath = sourcePath;
        Problem = problem;
    }

    /// <summary>Bulunan mekanlar (ada göre sıralı). Okunamadıysa boş.</summary>
    public IReadOnlyList<VenueInfo> Venues { get; }

    /// <summary>Okunan <c>maps.json</c>'un tam yolu; bulunamadıysa null.</summary>
    public string? SourcePath { get; }

    /// <summary>Okunamama sebebi (operatöre gösterilir); sorun yoksa null.</summary>
    public string? Problem { get; }

    public IReadOnlyList<string> Names => Venues.Select(v => v.Name).ToArray();

    /// <summary>Sunucu exe yolundan yola çıkarak mekan listesini çıkarır.</summary>
    public static VenueCatalog ForServerExe(string serverExePath)
    {
        if (string.IsNullOrWhiteSpace(serverExePath))
            return new VenueCatalog([], null, "Sunucu exe seçilmedi.");

        var mapsPath = FindMapsJson(serverExePath);
        if (mapsPath == null)
        {
            return new VenueCatalog([], null,
                "config\\maps.json bulunamadı. Unity'de Tools > VortexArena > Export Server Config " +
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

    /// <summary>Exe'nin yanından başlayıp yukarı doğru <c>config\maps.json</c> arar.</summary>
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

    /// <summary>maps.json içeriğini ayrıştırır. Biçim: <c>{ "maps": [ { sceneName, venue, modes } ] }</c>.</summary>
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

    /// <summary>Sunucudaki kuralın aynısı: lobi = <c>modes</c> tam olarak <c>["lobby"]</c>.
    /// Sahne adına BAKILMAZ.</summary>
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
