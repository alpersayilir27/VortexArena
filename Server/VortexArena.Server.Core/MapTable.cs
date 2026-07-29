#nullable enable
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>config/maps.json'daki tek harita girdisi. Public ALAN — JsonUtil (IncludeFields)
/// ile okunur; adlar Unity'deki MapDefinition SO alanlarıyla birebir aynıdır (export bu adlarla
/// yazar: Tools &gt; VortexArena &gt; Export Server Config).
///
/// <para><b>Arena ÖLÇÜSÜ burada YOKTUR</b> ve eklenmemelidir: sunucu metre bilmez (pozlar
/// istemci-otoriter, arena uzayında gelir) ve her işletmenin alanı farklı — çoğu kare/dikdörtgen
/// bile olmadığı için tek bir ölçü çifti arenayı tarif etmez. Ölçü yalnız istemcide, sahnenin
/// <c>ArenaBoundary.halfExtentX/Z</c>'sinde yaşar.</para></summary>
public sealed class MapEntry
{
    public string sceneName = "";

    /// <summary>Haritanın MEKANI (§11). Export bunu asset yolundan türetir:
    /// <c>Assets/Arenas/Venues/&lt;İşletme&gt;/…</c> → o işletme, başka her yer →
    /// <c>"Standard"</c>. Boş gelirse (eski export) <c>"Standard"</c> sayılır.</summary>
    public string venue = "";

    /// <summary>Bu haritanın desteklediği modId'ler; BOŞ = kısıt yok (MapDefinition.SupportsMode
    /// ile aynı semantik — yeni haritada unutulan alan modu gizlemesin).</summary>
    public string[] modes = Array.Empty<string>();
}

/// <summary>maps.json kökü: <c>{ "maps": [ ... ] }</c>.</summary>
public sealed class MapTableFile
{
    public MapEntry[] maps = Array.Empty<MapEntry>();
}

/// <summary>Harita kataloğu (§10.1 start_match doğrulaması). İçerik
/// projesinden gelir: Unity <c>Tools &gt; VortexArena &gt; Export Server Config</c> menüsü
/// MapDefinition SO'larından üretir — dosya ELLE DÜZENLENMEZ (export ezer).
///
/// <para>Dosya yoksa varsayılan ÜRETİLMEZ ve dosya YAZILMAZ — sunucunun uyduracağı bir harita
/// listesi yoktur. Boş tablo = harita doğrulaması kapalı.
/// Yükleme sonrası salt-okunurdur → kilit gerekmez.</para>
///
/// <para>Sunucunun okuduğu TEK içerik tablosu budur; silah tablosu (weapons.json) YOKTUR
/// (§10.3) — hasarı istemci bildirir.</para></summary>
public sealed class MapTable
{
    /// <summary>Mekanı boş gelen girdinin (eski export) sayıldığı mekan.</summary>
    public const string DefaultVenue = "Standard";

    private readonly Dictionary<string, MapEntry> _byScene = new(StringComparer.Ordinal);

    /// <summary>Tabloya alınan sahne adları (açılış özeti / red mesajları için).</summary>
    public IReadOnlyList<string> SceneNames { get; }

    /// <summary>Tablodaki mekanlar (alfabetik, tekrarsız). Açılışta operatöre bu liste sorulur.</summary>
    public IReadOnlyList<string> Venues { get; }

    /// <summary>Bu tablonun mekanı; tüm mekanları içeren TAM tabloda boştur.</summary>
    public string Venue { get; }

    /// <summary>true = harita doğrulaması yapılmaz (maps.json yok/boş/bozuk).</summary>
    public bool IsEmpty => _byScene.Count == 0;

    private MapTable(IEnumerable<MapEntry> entries, string venue = "")
    {
        Venue = venue;
        var names = new List<string>();
        var venues = new List<string>();
        foreach (var entry in entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.sceneName))
            {
                Console.WriteLine("[MapTable] sceneName'i boş girdi — atlandı.");
                continue;
            }
            entry.modes ??= Array.Empty<string>();
            if (string.IsNullOrWhiteSpace(entry.venue)) entry.venue = DefaultVenue;
            _byScene[entry.sceneName] = entry;
            names.Add(entry.sceneName);
            if (!venues.Contains(entry.venue)) venues.Add(entry.venue);
        }
        venues.Sort(StringComparer.Ordinal);
        SceneNames = names;
        Venues = venues;
    }

    /// <summary>Yalnız verilen mekanın haritalarını içeren yeni tablo. Sunucu bunu
    /// <see cref="MatchDirector"/>'a verir → <c>start_match</c> başka mekanın haritasını
    /// otomatik reddeder (ayrı bir kontrol yazmaya gerek kalmaz).</summary>
    public MapTable ForVenue(string venue)
    {
        var subset = new List<MapEntry>();
        foreach (var entry in _byScene.Values)
        {
            if (string.Equals(entry.venue, venue, StringComparison.OrdinalIgnoreCase)) subset.Add(entry);
        }
        subset.Sort((a, b) => string.CompareOrdinal(a.sceneName, b.sceneName));
        return new MapTable(subset, venue);
    }

    /// <summary>Bu tablodaki tek lobi haritası (modes == ["lobby"], §10.7); yoksa boş string.
    /// Birden çoksa alfabetik ilki döner ve uyarı basılır — export deterministik sıralı olduğu
    /// için seçim de deterministiktir.</summary>
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

    /// <summary>Dosya yoksa/bozuksa BOŞ tablo döner (doğrulama atlanır) — sunucu asla
    /// varsayılan harita uydurmaz, çünkü harita listesi Unity içerik projesinden gelir.</summary>
    public static MapTable Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Console.WriteLine($"[MapTable] {path} yok — harita doğrulaması atlanıyor (Unity: Tools > VortexArena > Export Server Config).");
                return new MapTable(Array.Empty<MapEntry>());
            }

            var loaded = JsonUtil.Deserialize<MapTableFile>(File.ReadAllText(path));
            if (loaded?.maps is { Length: > 0 }) return new MapTable(loaded.maps);

            Console.WriteLine($"[MapTable] {path} çözümlenemedi/boş — harita doğrulaması atlanıyor.");
            return new MapTable(Array.Empty<MapEntry>());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MapTable] okuma hatası ({ex.Message}) — harita doğrulaması atlanıyor.");
            return new MapTable(Array.Empty<MapEntry>());
        }
    }

    /// <summary>false = sahne tabloda yok → tablo doluysa start_match reddedilir (§10.1).</summary>
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

    /// <summary>Harita bu modu destekliyor mu. <c>modes</c> boşsa kısıt YOKTUR; dolu ise
    /// OrdinalIgnoreCase eşleşme aranır (Unity MapDefinition.SupportsMode ile aynı semantik).</summary>
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
}
