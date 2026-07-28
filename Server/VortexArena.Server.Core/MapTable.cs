#nullable enable
namespace VortexArena.Server.Core;

/// <summary>config/maps.json'daki tek harita girdisi. Public ALAN — JsonUtil (IncludeFields)
/// ile okunur; adlar Unity'deki MapDefinition SO alanlarıyla birebir aynıdır (export bu adlarla
/// yazar: Tools &gt; VortexArena &gt; Export Server Config).</summary>
public sealed class MapEntry
{
    public string sceneName = "";
    public float sizeX;
    public float sizeZ;

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
/// listesi yoktur. Boş tablo = doğrulama kapalı, yani Faz 3 davranışı (geriye dönük uyumlu).
/// Yükleme sonrası salt-okunurdur → kilit gerekmez.</para>
///
/// <para>Sunucunun okuduğu TEK içerik tablosu budur; silah tablosu (weapons.json) §10.3 ile
/// kaldırıldı — hasarı istemci bildirir.</para></summary>
public sealed class MapTable
{
    private readonly Dictionary<string, MapEntry> _byScene = new(StringComparer.Ordinal);

    /// <summary>Tabloya alınan sahne adları (açılış özeti / red mesajları için).</summary>
    public IReadOnlyList<string> SceneNames { get; }

    /// <summary>true = harita doğrulaması yapılmaz (maps.json yok/boş/bozuk).</summary>
    public bool IsEmpty => _byScene.Count == 0;

    private MapTable(IEnumerable<MapEntry> entries)
    {
        var names = new List<string>();
        foreach (var entry in entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.sceneName))
            {
                Console.WriteLine("[MapTable] sceneName'i boş girdi — atlandı.");
                continue;
            }
            entry.modes ??= Array.Empty<string>();
            _byScene[entry.sceneName] = entry;
            names.Add(entry.sceneName);
        }
        SceneNames = names;
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
