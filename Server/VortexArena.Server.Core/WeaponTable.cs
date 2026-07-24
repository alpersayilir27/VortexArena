#nullable enable
using System.Text.Json;
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>config/weapons.json'daki tek silah girdisi. Public ALAN — JsonUtil (IncludeFields)
/// ile okunur; adlar Unity'deki WeaponDefinition SO alanlarıyla birebir aynıdır.</summary>
public sealed class WeaponEntry
{
    public string weaponId = "";
    public float damage;
    public int rpm;
}

/// <summary>weapons.json kökü: <c>{ "weapons": [ ... ] }</c>.</summary>
public sealed class WeaponTableFile
{
    public WeaponEntry[] weapons = Array.Empty<WeaponEntry>();
}

/// <summary>Sunucu-otoriter silah tablosu (§10.3). Unity'deki WeaponDefinition SO'larıyla ELLE
/// senkron tutulur (Faz 4'te export otomasyonu gelecek); iki taraf sapınca hasar HER ZAMAN
/// buradan uygulanır. Yükleme sonrası salt-okunurdur → kilit gerekmez.</summary>
public sealed class WeaponTable
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        IncludeFields = true,
        WriteIndented = true
    };

    private readonly Dictionary<string, WeaponEntry> _byId = new(StringComparer.Ordinal);

    /// <summary>Tabloya alınan silah kimlikleri (açılış özeti için).</summary>
    public IReadOnlyList<string> WeaponIds { get; }

    private WeaponTable(IEnumerable<WeaponEntry> entries)
    {
        var ids = new List<string>();
        foreach (var entry in entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.weaponId)) continue;
            if (entry.rpm <= 0 || entry.damage <= 0f)
            {
                Console.WriteLine($"[WeaponTable] '{entry.weaponId}' geçersiz (damage {entry.damage}, rpm {entry.rpm}) — atlandı.");
                continue;
            }
            _byId[entry.weaponId] = entry;
            ids.Add(entry.weaponId);
        }
        WeaponIds = ids;
    }

    /// <summary>Protokoldeki v1 silahları (Docs/ArenaNet-Protokol.md §10.3 notu).</summary>
    private static WeaponEntry[] DefaultEntries() => new[]
    {
        new WeaponEntry { weaponId = "ak47", damage = 34f, rpm = 600 },
        new WeaponEntry { weaponId = "m4", damage = 22f, rpm = 800 }
    };

    /// <summary>ServerConfig.Load deseni: dosya yoksa varsayılanlarla oluşturur, bozuksa
    /// varsayılanlarla devam eder (sunucu silahsız kalmasın).</summary>
    public static WeaponTable Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var loaded = JsonUtil.Deserialize<WeaponTableFile>(File.ReadAllText(path));
                if (loaded?.weapons is { Length: > 0 }) return new WeaponTable(loaded.weapons);
                Console.WriteLine($"[WeaponTable] {path} çözümlenemedi/boş — varsayılan silahlar kullanılıyor.");
                return new WeaponTable(DefaultEntries());
            }

            var defaults = DefaultEntries();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(new WeaponTableFile { weapons = defaults }, WriteOptions));
            Console.WriteLine($"[WeaponTable] {path} yoktu — varsayılanlarla oluşturuldu.");
            return new WeaponTable(defaults);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WeaponTable] okuma hatası ({ex.Message}) — varsayılanlar kullanılıyor.");
            return new WeaponTable(DefaultEntries());
        }
    }

    /// <summary>false = bilinmeyen weaponId → hit_report reddedilir (§10.3/5).</summary>
    public bool TryGet(string? weaponId, out WeaponEntry entry)
    {
        if (!string.IsNullOrEmpty(weaponId) && _byId.TryGetValue(weaponId, out var found))
        {
            entry = found;
            return true;
        }
        entry = null!;
        return false;
    }

    /// <summary>İki kabul edilen vuruş arasındaki en kısa süre (sn): 60/rpm × FIRE_RATE_TOLERANCE
    /// (§10.3/6 — ağ jitter'ı için tolerans).</summary>
    public static float MinShotInterval(WeaponEntry entry) =>
        60f / entry.rpm * ArenaProtocol.FIRE_RATE_TOLERANCE;
}
