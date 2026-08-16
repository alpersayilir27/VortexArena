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

    /// <summary>Kapanışta bağlantı döngülerini kesen imza (aşağıdaki <see cref="StopAsync"/> notu).</summary>
    private readonly CancellationTokenSource _shutdown = new();

    /// <summary>Bağlantılar kesildikten sonra Kestrel'in kendini toplaması için tanınan üst sınır;
    /// dolarsa host beklemeyi bırakır (kapanış hiçbir koşulda asılı kalmaz).</summary>
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
            // RequestAborted TEK BAŞINA yetmez: sunucu kapanırken o imza ancak zarif kapanış
            // süresi dolduğunda gelir, yani bağlantı döngüsü o ana kadar bekler ve kapanışı asar.
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                context.RequestAborted, _shutdown.Token);
            await connection.RunAsync(linked.Token);
        });

        _app = app;
        await app.StartAsync();
    }

    /// <summary>
    /// ⚠️ Sıra bilinçlidir: <b>önce bağlantılar, sonra host.</b> Kestrel'in zarif kapanışı açık her
    /// WebSocket'i "devam eden istek" sayar; hiçbiri kendiliğinden bitmediği için doğrudan
    /// <c>StopAsync</c> çağırmak host'u kapanış zaman aşımı dolana kadar bekletir — operatör
    /// Ctrl+C'ye bastıktan sonra konsol saniyelerce "Kapatılıyor..." satırında asılı kalır.
    /// İmzayı önce biz atınca döngüler kendi kapanış yolundan çıkar (oyuncu kaydı da düzgün
    /// düşer) ve host'un bekleyeceği bir şey kalmaz.
    /// </summary>
    public async Task StopAsync()
    {
        if (_app == null) return;
        // Tek bir bağlantının iptal geri çağrısı patlarsa kapanış devam etmeli.
        try { await _shutdown.CancelAsync(); }
        catch (Exception ex) { Console.WriteLine($"[control] bağlantı iptali: {ex.Message}"); }
        try
        {
            using var drain = new CancellationTokenSource(DrainTimeout);
            await _app.StopAsync(drain.Token);
        }
        catch (OperationCanceledException) { /* süre doldu: bekleme kesilir, kapanış sürer */ }
        await _app.DisposeAsync();
        _app = null;
        // _shutdown BİLEREK dispose edilmiyor: kabul edilmiş ama henüz linked kaynağını kurmamış
        // bir istek kalmışsa Token'a erişimi ObjectDisposedException'a düşerdi. Süreç zaten kapanıyor.
    }
}
