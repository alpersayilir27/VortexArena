#nullable enable
using System.Text.Encodings.Web;
using System.Text.Json;
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>System.Text.Json yardımcıları. DTO'lar public ALAN kullandığı için IncludeFields ŞART
/// (Docs/ArenaNet-Protokol.md §7); alan adları wire formatındaki camelCase ile birebir aynıdır.</summary>
public static class JsonUtil
{
    public static readonly JsonSerializerOptions Options = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // "Gözlük 03" telde/dosyada okunur kalsın
    };

    public static string Serialize<T>(T message) => JsonSerializer.Serialize(message, Options);

    /// <summary>Bozuk JSON'da null döner; bilinmeyen tipleri loglayıp yok saymak çağıranın işidir.</summary>
    public static T? Deserialize<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Zarf kuralı (§5): alıcı önce yalnız type alanını parse eder.</summary>
    public static string? GetMessageType(string json) => Deserialize<MsgEnvelope>(json)?.type;
}
