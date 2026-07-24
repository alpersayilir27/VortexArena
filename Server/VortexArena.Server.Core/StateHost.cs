#nullable enable
using System.Net;
using System.Net.Sockets;
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>UDP durum kanalı (statePort). Faz 1 kapsamı: yalnız 0x00 UdpHello kaydı —
/// playerId↔udpToken doğrulanır, endpoint kaydedilir, aynı 6 bayt ack olarak geri yollanır (§6.1).
/// Poz alımı (0x01) ve snapshot yayını (0x02) Faz 2'de eklenir.</summary>
public sealed class StateHost
{
    private readonly PlayerRegistry _registry;
    private readonly int _port;
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>Başarılı UDP kayıt bildirimi (konsol satırı için).</summary>
    public event Action<byte, IPEndPoint>? UdpRegistered;

    public StateHost(PlayerRegistry registry, int port)
    {
        _registry = registry;
        _port = port;
    }

    public void Start()
    {
        if (_loop is { IsCompleted: false }) return;
        var udp = new UdpClient(_port);
        _udp = udp;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(() => ReceiveLoopAsync(udp, token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _udp?.Close();
        _udp = null;
        _cts = null;
        _loop = null;
    }

    private async Task ReceiveLoopAsync(UdpClient udp, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await udp.ReceiveAsync(token);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException)
            {
                // Windows'ta ulaşılamayan hedefe gönderim sonrası recv 10054 fırlatabilir — döngü ölmesin.
                continue;
            }

            var data = result.Buffer;
            if (data.Length == 0) continue;

            switch (data[0])
            {
                case UdpPacketType.UdpHello:
                    if (data.Length < UdpHello.SIZE) break;
                    await HandleUdpHelloAsync(udp, data, result.RemoteEndPoint, token);
                    break;
                case UdpPacketType.PoseUpdate:
                    // Faz 2: poz alımı + snapshot yayını — şimdilik yok sayılır.
                    break;
                default:
                    // Bilinmeyen paket tipi — yok sayılır (ileri sürüm uyumluluğu).
                    break;
            }
        }
    }

    private async Task HandleUdpHelloAsync(UdpClient udp, byte[] data, IPEndPoint remote, CancellationToken token)
    {
        UdpHello hello;
        using (var ms = new MemoryStream(data, 0, data.Length, writable: false))
        using (var reader = new BinaryReader(ms))
        {
            reader.ReadByte(); // tip baytı dispatcher'da tüketildi sayılır
            hello = UdpHello.Read(reader);
        }

        if (!_registry.TryRegisterUdpEndpoint(hello.playerId, hello.udpToken, remote))
        {
            Console.WriteLine($"[StateHost] udp_hello reddedildi: playerId {hello.playerId} ({remote}) token eşleşmedi.");
            return;
        }

        try
        {
            // Ack = aynı 6 baytın geri yollanması; istemci ack gelene dek 1 sn arayla tekrarlar.
            await udp.SendAsync(data.AsMemory(0, UdpHello.SIZE), remote, token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StateHost] ack gönderimi başarısız ({remote}): {ex.Message}");
            return;
        }
        UdpRegistered?.Invoke(hello.playerId, remote);
    }
}
