#nullable enable
using System.Text.Encodings.Web;
using System.Text.Json;
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>System.Text.Json helpers. Since the DTOs use public FIELDS, IncludeFields is MANDATORY
/// (Docs/ArenaNet-Protokol.md §7); field names match the camelCase of the wire format exactly.</summary>
public static class JsonUtil
{
    public static readonly JsonSerializerOptions Options = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // so "tuğba" stays readable on the wire/in the file
    };

    public static string Serialize<T>(T message) => JsonSerializer.Serialize(message, Options);

    /// <summary>Returns null on malformed JSON; logging and ignoring unknown types is the caller's
    /// job.</summary>
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

    /// <summary>Envelope rule (§5): the receiver first parses only the type field.</summary>
    public static string? GetMessageType(string json) => Deserialize<MsgEnvelope>(json)?.type;
}
