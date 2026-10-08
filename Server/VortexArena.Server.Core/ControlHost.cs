#nullable enable
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>Owner of the Kestrel lifecycle: the http://0.0.0.0:&lt;controlPort&gt;/ws WebSocket
/// endpoint plus the launcher's HTTP endpoints (§13); one ClientConnection per connection (the
/// cosmos ClassroomHost pattern).</summary>
public sealed class ControlHost
{
    private readonly PlayerRegistry _registry;
    private readonly LobbyService _lobby;
    private readonly MatchDirector _director;
    private readonly MatchRecorder _recorder;

    /// <summary>Runs the SAME shutdown path as Ctrl+C (§13).</summary>
    private readonly Action _requestShutdown;

    private readonly int _port;
    private WebApplication? _app;

    /// <summary>Cuts the connection loops on shutdown (see <see cref="StopAsync"/>).</summary>
    private readonly CancellationTokenSource _shutdown = new();

    /// <summary>Time granted to Kestrel to wind down after the connections are cut; on expiry the host
    /// stops waiting, so shutdown never hangs.</summary>
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(2);

    public ControlHost(PlayerRegistry registry, LobbyService lobby, MatchDirector director,
        MatchRecorder recorder, Action requestShutdown, int port)
    {
        _registry = registry;
        _lobby = lobby;
        _director = director;
        _recorder = recorder;
        _requestShutdown = requestShutdown;
        _port = port;
    }

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions());
        builder.Logging.ClearProviders(); // the console lines are ours; Kestrel's log noise is unwanted
        builder.WebHost.UseUrls($"http://0.0.0.0:{_port}");

        var app = builder.Build();
        app.UseWebSockets();
        app.Map(ArenaProtocol.WS_PATH, async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var connection = new ClientConnection(socket, _registry, _lobby, _director);
            // RequestAborted alone is not enough: on shutdown it only fires after the graceful period
            // expires, so the connection loop would wait that long and hang the shutdown.
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                context.RequestAborted, _shutdown.Token);
            await connection.RunAsync(linked.Token);
        });

        // Launcher endpoints (§13): same port as the WS, loopback only.
        app.MapGet(LauncherApi.STATUS_PATH, async context =>
        {
            if (!await AllowLoopbackAsync(context)) return;
            await WriteStatusAsync(context, StatusCodes.Status200OK);
        });

        app.MapPost(LauncherApi.RECORDING_PATH, async context =>
        {
            if (!await AllowLoopbackAsync(context)) return;

            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            var request = JsonUtil.Deserialize<LauncherRecordingRequest>(await reader.ReadToEndAsync());
            if (request == null)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            _director.SetRecording(request.on);
            await WriteStatusAsync(context, StatusCodes.Status200OK);
        });

        app.MapPost(LauncherApi.SHUTDOWN_PATH, async context =>
        {
            if (!await AllowLoopbackAsync(context)) return;

            // ⚠️ The answer goes out BEFORE the shutdown starts: the shutdown stops this very host and
            // waits for open requests, so answering afterwards would make the request wait for itself
            // (§13).
            context.Response.StatusCode = StatusCodes.Status202Accepted;
            await context.Response.CompleteAsync();
            _requestShutdown();
        });

        _app = app;
        await app.StartAsync();
    }

    /// <summary>Loopback gate (§13); a remote caller gets 403 and the handler stops.</summary>
    /// <remarks>⚠️ The port is open to the game network — no device there may stop the server or touch
    /// the recording.</remarks>
    private static async Task<bool> AllowLoopbackAsync(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        // Kestrel reports an IPv4 client on a dual-stack socket as ::ffff:127.0.0.1.
        if (address != null && address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address != null && IPAddress.IsLoopback(address)) return true;

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.CompleteAsync();
        return false;
    }

    private async Task WriteStatusAsync(HttpContext context, int statusCode)
    {
        var match = _director.CurrentMatchInfo();
        var connected = _registry.Snapshot();
        var status = new LauncherStatus
        {
            protocolVersion = ArenaProtocol.PROTOCOL_VERSION,
            venue = _director.VenueId,
            phase = match.phase,
            phaseReason = match.phaseReason,
            modeId = match.modeId,
            sceneName = match.sceneName,
            playerCount = connected.Count(p => p.IsConnected && p.Role == "player"),
            adminCount = connected.Count(p => p.IsConnected && p.Role == "admin"),
            recording = _recorder.BuildStatus()
        };

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(JsonUtil.Serialize(status), Encoding.UTF8);
    }

    /// <summary>Stops the host — ⚠️ connections first, host second.</summary>
    /// <remarks>Kestrel's graceful shutdown counts every open WebSocket as an in-flight request, and
    /// none finish on their own, so calling <c>StopAsync</c> first makes the console hang for seconds
    /// after Ctrl+C. Raising the signal first lets the loops exit their own way (dropping the player
    /// records properly), leaving the host nothing to wait for.</remarks>
    public async Task StopAsync()
    {
        if (_app == null) return;
        // A throwing cancellation callback must not stop the shutdown.
        try { await _shutdown.CancelAsync(); }
        catch (Exception ex) { Console.WriteLine($"[control] bağlantı iptali: {ex.Message}"); }
        try
        {
            using var drain = new CancellationTokenSource(DrainTimeout);
            await _app.StopAsync(drain.Token);
        }
        catch (OperationCanceledException) { /* expired: the wait is cut, the shutdown continues */ }
        await _app.DisposeAsync();
        _app = null;
        // _shutdown is deliberately not disposed: a request accepted but not yet linked would hit
        // ObjectDisposedException on Token. The process is exiting anyway.
    }
}
