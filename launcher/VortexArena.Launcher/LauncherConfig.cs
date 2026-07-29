using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VortexArena.Launcher;

/// <summary>
/// Launcher'ın kalıcı ayarları + başlatma argümanlarını üreten tek yer.
/// <para>
/// Ayarlar kullanıcı profilinde saklanır (<see cref="SettingsPath"/>), launcher klasörünün
/// yanında DEĞİL: klasör yeniden dağıtıldığında (deploy-launcher.bat çıktıyı silip yeniden
/// kopyalar) operatörün girdiği IP/yol kaybolmasın.
/// </para>
/// <para>
/// ⚠️ <b>Argüman adları sözleşmedir.</b> <see cref="ArgServerIp"/>/<see cref="ArgServerPort"/>
/// Unity tarafındaki <c>AppBoot.ArgServerIp</c>/<c>ArgServerPort</c> ile, <see cref="ArgVenue"/>
/// ise sunucudaki <c>Program.SelectVenue</c>'nun okuduğu <c>--venue</c> ile birebir aynı olmalıdır.
/// İkisi de testte doğrulanır (<c>LauncherConfigTests</c>) — birini değiştirirsen İKİ tarafı
/// birlikte değiştir.
/// </para>
/// </summary>
public sealed class LauncherConfig
{
    /// <summary>Protokolün WS kontrol portu (ArenaProtocol.CONTROL_PORT).</summary>
    public const int DefaultPort = 47821;

    /// <summary>Unity <c>AppBoot.ArgServerIp</c>.</summary>
    public const string ArgServerIp = "--server-ip";

    /// <summary>Unity <c>AppBoot.ArgServerPort</c>.</summary>
    public const string ArgServerPort = "--server-port";

    /// <summary>Sunucunun mekan argümanı (<c>Program.SelectVenue</c>).</summary>
    public const string ArgVenue = "--venue";

    [JsonPropertyName("adminExePath")]
    public string AdminExePath { get; set; } = "";

    [JsonPropertyName("serverIp")]
    public string ServerIp { get; set; } = "127.0.0.1";

    [JsonPropertyName("serverPort")]
    public int ServerPort { get; set; } = DefaultPort;

    /// <summary>Sunucu exe'si (<c>deploy\server\VortexArena.Server.App.exe</c>); boş = sunucu
    /// bu launcher'dan başlatılmayacak, elle çalıştırılacak.</summary>
    [JsonPropertyName("serverExePath")]
    public string ServerExePath { get; set; } = "";

    /// <summary>Bu oturumda açılacak mekan — sunucuya <c>--venue</c> ile geçer.</summary>
    [JsonPropertyName("venue")]
    public string Venue { get; set; } = "";

    // ------------------------------------------------------------------ kalıcılık

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

    /// <summary>Ayarları okur. Dosya yoksa/bozuksa varsayılanlarla döner — launcher açılmalı,
    /// operatör ayarı yeniden girebilsin.</summary>
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

    // ------------------------------------------------------------------ doğrulama

    public bool AdminExeExists => AdminExePath.Length > 0 && File.Exists(AdminExePath);

    public bool ServerExeExists => ServerExePath.Length > 0 && File.Exists(ServerExePath);

    /// <summary>
    /// Adres tam yazılmış mı.
    /// <para>
    /// ⚠️ <b>Tek başına <see cref="IPAddress.TryParse(string, out IPAddress)"/> yetmez:</b> .NET
    /// eksik dörtlüleri eski uyumluluk için kabul eder ve <c>"192.168.1"</c> adresini sessizce
    /// <c>192.168.0.1</c> yapar. Operatör bunu fark etmeden yanlış makineye bağlanmaya çalışır ve
    /// ekranda yalnız "sunucuya bağlanılamıyor" görür. Bu yüzden IPv4'te dört parça şart koşulur.
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

    /// <summary>Yönetim oyununa geçilecek argümanlar — <c>AppBoot</c> bunları okur.</summary>
    public IReadOnlyList<string> GameArguments =>
    [
        ArgServerIp, ServerIp.Trim(),
        ArgServerPort, ServerPort.ToString(CultureInfo.InvariantCulture),
    ];

    /// <summary>
    /// Sunucuya geçilecek argümanlar. <b>Mekan her zaman açıkça geçer</b> — launcher mekansız
    /// sunucu başlatmaz (<see cref="ValidateServer"/>), çünkü mekansız açılışta sunucu alfabetik
    /// ilk mekanı sessizce seçer.
    /// </summary>
    public IReadOnlyList<string> ServerArguments
    {
        get
        {
            var venue = Venue.Trim();
            return venue.Length == 0 ? [] : [ArgVenue, venue];
        }
    }

    /// <summary>Yönetim oyunu başlatılabilir mi; engel varsa operatöre gösterilecek sebep.</summary>
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
    /// Sunucu başlatılabilir mi; engel varsa sebep.
    /// <para>
    /// <b>Mekan zorunludur</b> ve bu bilinçlidir: mekansız açılışta sunucunun konsolu
    /// etkileşimli değilse (betik/servis/launcher) alfabetik ilk mekan sessizce açılır ve
    /// operatör yanlış işletmenin arenalarını yönetir. Launcher bu yolu hiç bırakmaz.
    /// </para>
    /// </summary>
    /// <param name="knownVenues"><c>maps.json</c>'dan okunan mekanlar; okunamadıysa boş.</param>
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
