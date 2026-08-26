#nullable enable
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>One network object baked into a scene (§10.10): which identity carries which kind.</summary>
/// <remarks>Public FIELDS — read via JsonUtil (IncludeFields); names match maps.json exactly. The
/// export produces this list WITHOUT opening the scene — its source is <c>Data/&lt;SceneName&gt;_objects.json</c>
/// written at scene save.</remarks>
public sealed class MapObjectEntry
{
    public int sceneId;
    public string kind = "";
}

/// <summary>A single map entry in config/maps.json.</summary>
/// <remarks>Public FIELDS — read via JsonUtil (IncludeFields); names match the Unity MapDefinition SO
/// exactly (written by Tools &gt; VortexArena &gt; Server &gt; Export Server Config).
/// <para>⚠️ Arena DIMENSIONS are NOT here and must not be added: the server knows no metres (poses are
/// client-authoritative, in arena space) and every venue differs — most are not even rectangular, so
/// one pair of numbers cannot describe an arena. Dimensions live only on the client, in the JSON bound
/// to the scene's <c>ArenaBoundary</c> (<c>ArenaDimensions</c>).</para></remarks>
public sealed class MapEntry
{
    public string sceneName = "";

    /// <summary>The map's VENUE (§11), derived by the export from the asset path
    /// (<c>Assets/Arenas/Venues/&lt;Venue&gt;/…</c>).</summary>
    /// <remarks>Maps outside a venue folder (templates) never enter the export. Empty (old export) is
    /// treated as <see cref="MapTable.DefaultVenue"/>.</remarks>
    public string venue = "";

    /// <summary>Game family of the map (§11): "quickbattle" | "kids". EMPTY = old export → quick
    /// battle.</summary>
    public string gameType = "";

    /// <summary>modIds supported by this map; EMPTY = no restriction.</summary>
    /// <remarks>Same semantics as MapDefinition.SupportsMode — a field forgotten on a new map must not
    /// hide the mode.</remarks>
    public string[] modes = Array.Empty<string>();

    /// <summary>Network objects baked into this scene (§10.10): which identity carries which kind. The
    /// kind's RULES are not here, they live in the root <c>kinds[]</c>.</summary>
    public MapObjectEntry[] objects = Array.Empty<MapObjectEntry>();
}

/// <summary>The root of maps.json: <c>{ "maps": [ ... ] }</c>.</summary>
public sealed class MapTableFile
{
    public MapEntry[] maps = Array.Empty<MapEntry>();

    /// <summary>Network object kinds, shared by every map (§10.10).</summary>
    public KindEntry[] kinds = Array.Empty<KindEntry>();
}

/// <summary>The map catalog (§10.1 start_match validation), generated from the MapDefinition SOs by
/// Unity's <c>Tools &gt; VortexArena &gt; Server &gt; Export Server Config</c>.</summary>
/// <remarks>⚠️ Never edited by hand — the export overwrites it. If the file is missing, no default is
/// generated and nothing is written (the server may not invent maps); an empty table = validation
/// off. Read-only after load → no lock needed. This is the ONLY content table the server reads; there
/// is no weapons.json (§10.3) — damage is reported by the client.</remarks>
public sealed class MapTable
{
    /// <summary>The venue an entry with an empty venue (an old export) is assigned to.</summary>
    public const string DefaultVenue = "Standard";

    /// <summary>The game family an empty <c>gameType</c> (an old export) is assigned to.</summary>
    public const string DefaultGameType = "quickbattle";

    private readonly Dictionary<string, MapEntry> _byScene = new(StringComparer.Ordinal);

    /// <summary>Scene names taken into the table (for the startup summary / rejection messages).</summary>
    public IReadOnlyList<string> SceneNames { get; }

    /// <summary>Scene names with their game family (<c>"Name (kids)"</c>), for the startup summary
    /// only — rejection messages keep using <see cref="SceneNames"/>.</summary>
    public IReadOnlyList<string> SceneSummaries { get; }

    /// <summary>Venues in the table (alphabetical, deduplicated); offered to the operator at
    /// startup.</summary>
    public IReadOnlyList<string> Venues { get; }

    /// <summary>The venue of this table; empty in the FULL table that contains all venues.</summary>
    public string Venue { get; }

    /// <summary>true = no map validation is performed (maps.json is missing/empty/malformed).</summary>
    public bool IsEmpty => _byScene.Count == 0;

    /// <summary>Network object kinds (§10.10); never null — an empty table means no object enters the
    /// world table.</summary>
    public KindTable Kinds { get; }

    private MapTable(IEnumerable<MapEntry> entries, string venue = "", KindTable? kinds = null)
    {
        Venue = venue;
        Kinds = kinds ?? KindTable.Empty;
        var names = new List<string>();
        var summaries = new List<string>();
        var venues = new List<string>();
        foreach (var entry in entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.sceneName))
            {
                Console.WriteLine("[MapTable] sceneName'i boş girdi — atlandı.");
                continue;
            }
            entry.modes ??= Array.Empty<string>();
            entry.objects ??= Array.Empty<MapObjectEntry>();
            if (string.IsNullOrWhiteSpace(entry.venue)) entry.venue = DefaultVenue;
            // Normalized on load so an old export (empty gameType) reads as quick battle everywhere.
            if (string.IsNullOrWhiteSpace(entry.gameType)) entry.gameType = DefaultGameType;
            _byScene[entry.sceneName] = entry;
            names.Add(entry.sceneName);
            summaries.Add($"{entry.sceneName} ({entry.gameType})");
            if (!venues.Contains(entry.venue)) venues.Add(entry.venue);
        }
        venues.Sort(StringComparer.Ordinal);
        SceneNames = names;
        SceneSummaries = summaries;
        Venues = venues;
    }

    /// <summary>A new table holding only the given venue's maps.</summary>
    /// <remarks>The server hands this to <see cref="MatchDirector"/> → <c>start_match</c> rejects
    /// another venue's map automatically, with no extra check.</remarks>
    public MapTable ForVenue(string venue)
    {
        var subset = new List<MapEntry>();
        foreach (var entry in _byScene.Values)
        {
            if (string.Equals(entry.venue, venue, StringComparison.OrdinalIgnoreCase)) subset.Add(entry);
        }
        subset.Sort((a, b) => string.CompareOrdinal(a.sceneName, b.sceneName));
        // Kinds are content-wide, not per venue — the subset carries the same table.
        return new MapTable(subset, venue, Kinds);
    }

    /// <summary>The single lobby map of this table (modes == ["lobby"], §10.7); "" if none.</summary>
    /// <remarks>With more than one, the alphabetically first is returned with a warning — the export
    /// is deterministically ordered, so the choice is deterministic too.</remarks>
    public string ResolveLobbyScene()
    {
        var found = new List<string>();
        foreach (var entry in _byScene.Values)
        {
            if (entry.modes.Length == 1 &&
                string.Equals(entry.modes[0], ArenaProtocol.LOBBY_MODE_ID, StringComparison.OrdinalIgnoreCase))
            {
                found.Add(entry.sceneName);
            }
        }
        if (found.Count == 0) return "";
        found.Sort(StringComparer.Ordinal);
        if (found.Count > 1)
        {
            Console.WriteLine($"[MapTable] '{Venue}' mekanında {found.Count} lobi haritası var " +
                              $"({string.Join(", ", found)}) — '{found[0]}' seçildi. " +
                              "Kesinleştirmek için server.json → lobbyScene yazın.");
        }
        return found[0];
    }

    /// <summary>Returns an EMPTY table if the file is missing/malformed (validation skipped).</summary>
    /// <remarks>The server never invents a default map — the list comes from the Unity content
    /// project.</remarks>
    public static MapTable Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Console.WriteLine($"[MapTable] {path} yok — harita doğrulaması atlanıyor (Unity: Tools > VortexArena > Server > Export Server Config).");
                return new MapTable(Array.Empty<MapEntry>());
            }

            var loaded = JsonUtil.Deserialize<MapTableFile>(File.ReadAllText(path));
            if (loaded?.maps is { Length: > 0 })
                return new MapTable(loaded.maps, "", KindTable.From(loaded.kinds));

            Console.WriteLine($"[MapTable] {path} çözümlenemedi/boş — harita doğrulaması atlanıyor.");
            return new MapTable(Array.Empty<MapEntry>());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MapTable] okuma hatası ({ex.Message}) — harita doğrulaması atlanıyor.");
            return new MapTable(Array.Empty<MapEntry>());
        }
    }

    /// <summary>false = scene not in the table → start_match is rejected if the table is non-empty
    /// (§10.1).</summary>
    public bool TryGet(string? sceneName, out MapEntry entry)
    {
        if (!string.IsNullOrEmpty(sceneName) && _byScene.TryGetValue(sceneName, out var found))
        {
            entry = found;
            return true;
        }
        entry = null!;
        return false;
    }

    /// <summary>Whether the map supports this mode; empty <c>modes</c> = no restriction.</summary>
    /// <remarks>Otherwise an OrdinalIgnoreCase match — same semantics as Unity's
    /// MapDefinition.SupportsMode.</remarks>
    public static bool SupportsMode(MapEntry entry, string modeId)
    {
        if (string.IsNullOrEmpty(modeId)) return false;
        if (entry.modes is not { Length: > 0 }) return true;
        foreach (var id in entry.modes)
        {
            if (string.Equals(id, modeId, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>Do the map and the mode belong to the same game family? An empty value on either side
    /// means quick battle, so an old export never hides a map.</summary>
    public static bool MatchesGameType(MapEntry map, string? modeGameType)
    {
        var mapType = string.IsNullOrWhiteSpace(map.gameType) ? DefaultGameType : map.gameType;
        var modeType = string.IsNullOrWhiteSpace(modeGameType) ? DefaultGameType : modeGameType;
        return StringComparer.OrdinalIgnoreCase.Equals(mapType, modeType);
    }
}
