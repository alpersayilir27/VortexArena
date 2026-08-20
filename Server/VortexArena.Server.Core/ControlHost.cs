#nullable enable
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>Owner of the Kestrel lifecycle: the http://0.0.0.0:&lt;controlPort&gt;/ws WebSocket
/// endpoint; one ClientConnection per connection (the cosmos ClassroomHost pattern).</summary>
public sealed class ControlHost
{
    private readonly PlayerRegistry _registry;
    private readonly LobbyService _lobby;
    private readonly MatchDirector _director;
    private readonly int _port;
    private WebApplication? _app;

    /// <summary>Cuts the connection loops on shutdown (see <see cref="StopAsync"/>).</summary>
    private readonly CancellationTokenSource _shutdown = new();

    /// <summary>Time granted to Kestrel to wind down after the connections are cut; on expiry the host
    /// stops waiting, so shutdown never hangs.</summary>
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(2);

    public ControlHost(PlayerRegistry registry, LobbyService lobby, MatchDirector director, int port)
    {
        _registry = registry;
        _lobby = lobby;
        _director = director;
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

        _app = app;
        await app.StartAsync();
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
