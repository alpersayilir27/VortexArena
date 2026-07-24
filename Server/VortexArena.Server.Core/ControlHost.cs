#nullable enable
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>Kestrel yaşam döngüsü sahibi: http://0.0.0.0:&lt;controlPort&gt;/ws WebSocket ucu;
/// bağlantı başına bir ClientConnection (cosmos ClassroomHost deseni).</summary>
public sealed class ControlHost
{
    private readonly PlayerRegistry _registry;
    private readonly LobbyService _lobby;
    private readonly MatchDirector _director;
    private readonly int _port;
    private WebApplication? _app;

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
        builder.Logging.ClearProviders(); // konsol satırları bizim; Kestrel log gürültüsü istenmiyor
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
            await connection.RunAsync(context.RequestAborted);
        });

        _app = app;
        await app.StartAsync();
    }

    public async Task StopAsync()
    {
        if (_app == null) return;
        await _app.StopAsync();
        await _app.DisposeAsync();
        _app = null;
    }
}
