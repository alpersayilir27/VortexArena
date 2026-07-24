#nullable enable
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using VortexArena.Protocol;

namespace VortexArena.PoseBot;

/// <summary>Sentetik oyuncu test istemcisi: WS hello → welcome → UDP kayıt → 20 Hz dairesel
/// yürüyüş pozu. Gerçek Quest olmadan poz senkronunu (snapshot, uzak avatar, taktik görünüm)
/// uçtan uca test etmek için. Kullanım: PoseBot [ip] [botSayısı] (varsayılan 127.0.0.1, 1).</summary>
internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true };

    private static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var ip = args.Length > 0 ? args[0] : "127.0.0.1";
        var count = args.Length > 1 && int.TryParse(args[1], out var n) ? Math.Clamp(n, 1, ArenaProtocol.MAX_PLAYERS) : 1;

        Console.WriteLine($"PoseBot: {count} bot → {ip}:{ArenaProtocol.CONTROL_PORT} (durdurmak için Ctrl+C)");
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var bots = Enumerable.Range(0, count).Select(i => RunBotAsync(ip, i, cts.Token));
        await Task.WhenAll(bots);
        Console.WriteLine("PoseBot kapandı.");
    }

    private static async Task RunBotAsync(string ip, int index, CancellationToken ct)
    {
        var tag = $"[bot{index}]";
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(ip, index, tag, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Console.WriteLine($"{tag} kopma: {ex.Message} — 2 sn sonra yeniden.");
            }
            try { await Task.Delay(2000, ct); } catch (OperationCanceledException) { break; }
        }
    }

    private static async Task RunSessionAsync(string ip, int index, string tag, CancellationToken ct)
    {
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri($"ws://{ip}:{ArenaProtocol.CONTROL_PORT}{ArenaProtocol.WS_PATH}"), ct);

        var hello = new HelloMsg
        {
            protocolVersion = ArenaProtocol.PROTOCOL_VERSION,
            role = "player",
            deviceId = $"posebot-{index:00}",
            deviceName = $"PoseBot {index:00}",
            appVersion = "posebot",
            currentScene = "Lobby",
            scenes = new[] { "Lobby" }
        };
        await SendJsonAsync(ws, hello, ct);
        Console.WriteLine($"{tag} bağlandı, hello gönderildi.");

        using var udp = new UdpClient(0);
        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task? poseTask = null;

        var buffer = new byte[64 * 1024];
        var message = new MemoryStream();
        var lastStatus = DateTime.UtcNow;

        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), sessionCts.Token);
            if (result.MessageType == WebSocketMessageType.Close) break;
            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage) continue;

            var json = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            message.SetLength(0);

            using var doc = JsonDocument.Parse(json);
            var type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;

            if (type == MessageTypes.Welcome && poseTask == null)
            {
                var playerId = (byte)doc.RootElement.GetProperty("playerId").GetInt32();
                var udpToken = doc.RootElement.GetProperty("udpToken").GetUInt32();
                Console.WriteLine($"{tag} welcome: playerId {playerId}.");
                poseTask = Task.Run(() => PoseLoopAsync(udp, ip, playerId, udpToken, index, tag, sessionCts.Token), sessionCts.Token);
            }
            else if (type == MessageTypes.Ping || DateTime.UtcNow - lastStatus > TimeSpan.FromSeconds(ArenaProtocol.STATUS_INTERVAL))
            {
                lastStatus = DateTime.UtcNow;
                await SendJsonAsync(ws, new StatusMsg { scene = "Lobby", battery = 1f, fps = 72f }, ct);
            }
        }

        sessionCts.Cancel();
        if (poseTask != null) { try { await poseTask; } catch (OperationCanceledException) { } }
    }

    /// <summary>UDP kayıt (0x00 ack'e dek 1 sn) + 20 Hz dairesel yürüyüş PoseUpdate'leri.</summary>
    private static async Task PoseLoopAsync(UdpClient udp, string ip, byte playerId, uint udpToken, int index, string tag, CancellationToken ct)
    {
        var server = new IPEndPoint(IPAddress.Parse(ip), ArenaProtocol.STATE_PORT);

        // Kayıt: aynı 6 bayt geri gelene dek tekrar.
        var helloBytes = new byte[UdpHello.SIZE];
        using (var w = new BinaryWriter(new MemoryStream(helloBytes)))
            new UdpHello { playerId = playerId, udpToken = udpToken }.Write(w);

        var acked = false;
        var recvTask = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested && !acked)
            {
                var r = await udp.ReceiveAsync(ct);
                if (r.Buffer.Length >= UdpHello.SIZE && r.Buffer[0] == UdpPacketType.UdpHello) acked = true;
            }
        }, ct);
        while (!ct.IsCancellationRequested && !acked)
        {
            await udp.SendAsync(helloBytes, helloBytes.Length, server);
            await Task.Delay(1000, ct);
        }
        Console.WriteLine($"{tag} UDP kaydı tamam; 20 Hz poz akışı başladı.");

        // Dairesel yürüyüş: bot başına faz + yarıçap; arena uzayı (origin merkez, y=0 zemin).
        var radius = 2.0f + index * 0.7f;
        var phase = index * 1.7f;
        ushort seq = 0;
        var packet = new byte[PoseUpdate.SIZE];
        var stream = new MemoryStream(packet);
        var writer = new BinaryWriter(stream);
        var start = Environment.TickCount;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.0 / ArenaProtocol.POSE_RATE_HZ));
        while (await timer.WaitForNextTickAsync(ct))
        {
            var tSec = (Environment.TickCount - start) / 1000f;
            var a = phase + tSec * 0.6f; // ~0.6 rad/sn açısal hız
            var (sin, cos) = MathF.SinCos(a);
            float px = cos * radius, pz = sin * radius;
            var yaw = MathF.Atan2(-sin, cos); // teğet yön (hareket yönüne bakış)
            var (hs, hc) = MathF.SinCos(yaw * 0.5f);

            PoseData At(float ox, float oy, float oz) => new()
            {
                // Ofsetler baş yaw'ına göre döndürülür (sağ = +x).
                px = px + ox * MathF.Cos(yaw) + oz * MathF.Sin(yaw),
                py = oy,
                pz = pz - ox * MathF.Sin(yaw) + oz * MathF.Cos(yaw),
                qx = 0f, qy = hs, qz = 0f, qw = hc
            };

            var pose = new PoseUpdate
            {
                playerId = playerId,
                seq = seq++,
                clientTimeMs = (uint)Environment.TickCount,
                head = At(0f, 1.65f, 0f),
                handL = At(-0.25f, 1.15f, 0.25f),
                handR = At(0.25f, 1.15f, 0.25f)
            };
            stream.Position = 0;
            pose.Write(writer);
            await udp.SendAsync(packet, packet.Length, server);
        }
    }

    private static async Task SendJsonAsync<T>(ClientWebSocket ws, T msg, CancellationToken ct)
    {
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(msg, Json));
        await ws.SendAsync(payload, WebSocketMessageType.Text, true, ct);
    }
}
