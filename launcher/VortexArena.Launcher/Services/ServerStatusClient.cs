using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using VortexArena.Protocol;

namespace VortexArena.Launcher.Services;

/// <summary>What the last status poll said.</summary>
public enum ServerLink
{
    /// <summary>Nothing answered on the control port.</summary>
    Down,

    Running,

    /// <summary>Something answers but not the launcher endpoints — an older server build.</summary>
    Incompatible,
}

public sealed record ServerStatusResult(ServerLink Link, LauncherStatus? Status, string? Error)
{
    public static ServerStatusResult Down(string? error = null) => new(ServerLink.Down, null, error);
}

/// <summary>
/// Client of the server's launcher control endpoints (Docs/ArenaNet-Protokol.md, "Launcher kontrol
/// uçları").
/// <para>⚠️ <b>"Running" is the status answer, not process tracking:</b> a server started before (or
/// by another) launcher must be visible and stoppable too.</para>
/// </summary>
public sealed class ServerStatusClient : IDisposable
{
    /// <summary>Poll budget — a dead port must not stall the 1 s UI tick.</summary>
    public static readonly TimeSpan PollTimeout = TimeSpan.FromMilliseconds(700);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // LauncherApi DTOs are public fields (shared with Unity's JsonUtility).
        IncludeFields = true,
    };

    private readonly HttpClient _http = new()
    {
        // Ceiling only; each call narrows it with its own token.
        Timeout = TimeSpan.FromSeconds(10),
    };

    public static string BaseUrl(int port) => $"http://127.0.0.1:{port}";

    public async Task<ServerStatusResult> GetStatusAsync(int port, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(PollTimeout);

        try
        {
            using var response = await _http
                .GetAsync($"{BaseUrl(port)}{LauncherApi.STATUS_PATH}", timeout.Token)
                .ConfigureAwait(false);

            return await ReadStatusAsync(response, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ServerStatusResult.Down();
        }
        catch (HttpRequestException)
        {
            return ServerStatusResult.Down();
        }
    }

    /// <summary>Flips recording and returns the fresh status.</summary>
    public async Task<ServerStatusResult> SetRecordingAsync(int port, bool on, CancellationToken ct)
    {
        try
        {
            using var response = await _http
                .PostAsJsonAsync(
                    $"{BaseUrl(port)}{LauncherApi.RECORDING_PATH}",
                    new LauncherRecordingRequest { on = on },
                    JsonOptions,
                    ct)
                .ConfigureAwait(false);

            return await ReadStatusAsync(response, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return ServerStatusResult.Down($"Sunucuya ulaşılamadı: {ex.Message}");
        }
    }

    /// <summary>Asks for a clean shutdown. The 202 answer comes BEFORE the server is down.</summary>
    public async Task<string?> RequestShutdownAsync(int port, CancellationToken ct)
    {
        try
        {
            using var response = await _http
                .PostAsync($"{BaseUrl(port)}{LauncherApi.SHUTDOWN_PATH}", content: null, ct)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound) return IncompatibleMessage;

            return response.IsSuccessStatusCode
                ? null
                : $"Sunucu kapatma isteğini reddetti (HTTP {(int)response.StatusCode}).";
        }
        catch (HttpRequestException ex)
        {
            return $"Sunucuya ulaşılamadı: {ex.Message}";
        }
    }

    public const string IncompatibleMessage =
        "Sunucu çalışıyor ama bu launcher'la uyumsuz (eski sürüm).";

    private static async Task<ServerStatusResult> ReadStatusAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
            return new ServerStatusResult(ServerLink.Incompatible, null, IncompatibleMessage);

        if (!response.IsSuccessStatusCode)
            return ServerStatusResult.Down($"HTTP {(int)response.StatusCode}");

        try
        {
            var status = await response.Content
                .ReadFromJsonAsync<LauncherStatus>(JsonOptions, ct)
                .ConfigureAwait(false);

            return status is null
                ? new ServerStatusResult(ServerLink.Incompatible, null, IncompatibleMessage)
                : new ServerStatusResult(ServerLink.Running, status, null);
        }
        catch (JsonException)
        {
            return new ServerStatusResult(ServerLink.Incompatible, null, IncompatibleMessage);
        }
    }

    public void Dispose() => _http.Dispose();
}
