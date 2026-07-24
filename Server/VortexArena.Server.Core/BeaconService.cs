#nullable enable
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>Keşif beacon'ını BEACON_INTERVAL'de bir, kullanılabilir her IPv4 arayüzünden hem
/// 255.255.255.255'e hem arayüzün subnet-broadcast adresine yollar (§4; cosmos UdpBeaconService deseni).
/// Beacon yalnız otomatik doldurma kolaylığıdır — elle girilen IP her zaman önceliklidir.</summary>
public sealed class BeaconService
{
    private readonly string _serverId = Guid.NewGuid().ToString(); // uygulama ömrü boyunca sabit
    private readonly int _beaconPort;
    private readonly int _controlPort;
    private readonly int _statePort;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public string ServerId => _serverId;

    public BeaconService(int beaconPort, int controlPort, int statePort)
    {
        _beaconPort = beaconPort;
        _controlPort = controlPort;
        _statePort = statePort;
    }

    public void Start()
    {
        if (_loop is { IsCompleted: false }) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(() => LoopAsync(token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        _loop = null;
    }

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            // Her turda yeniden numaralandırılır: Wi-Fi/adaptör değişiklikleri kendiliğinden yakalanır.
            foreach (var (local, subnetBroadcast) in GetIPv4Interfaces())
            {
                try
                {
                    var beacon = new BeaconMsg
                    {
                        app = ArenaProtocol.APP_ID,
                        ver = ArenaProtocol.PROTOCOL_VERSION,
                        ip = local.ToString(),
                        controlPort = _controlPort,
                        statePort = _statePort,
                        serverId = _serverId
                    };
                    var bytes = Encoding.UTF8.GetBytes(JsonUtil.Serialize(beacon));
                    // Arayüz adresine bağlanır ki global broadcast bu arayüzden çıksın.
                    using var udp = new UdpClient(new IPEndPoint(local, 0)) { EnableBroadcast = true };
                    await udp.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, _beaconPort));
                    await udp.SendAsync(bytes, bytes.Length, new IPEndPoint(subnetBroadcast, _beaconPort));
                }
                catch (Exception ex)
                {
                    // Tek arayüzün hatası döngüyü öldürmemeli.
                    Console.WriteLine($"[BeaconService] {local} üzerinden gönderim başarısız: {ex.Message}");
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(ArenaProtocol.BEACON_INTERVAL), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static List<(IPAddress Local, IPAddress Broadcast)> GetIPv4Interfaces()
    {
        var result = new List<(IPAddress, IPAddress)>();
        NetworkInterface[] nics;
        try
        {
            nics = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch
        {
            return result;
        }

        foreach (var nic in nics)
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            try
            {
                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var mask = unicast.IPv4Mask;
                    if (mask == null || mask.Equals(IPAddress.Any)) continue;
                    result.Add((unicast.Address, GetBroadcastAddress(unicast.Address, mask)));
                }
            }
            catch
            {
                // Arayüz numaralandırma sırasında kaybolduysa atla.
            }
        }
        return result;
    }

    private static IPAddress GetBroadcastAddress(IPAddress address, IPAddress mask)
    {
        var a = address.GetAddressBytes();
        var m = mask.GetAddressBytes();
        var b = new byte[4];
        for (var i = 0; i < 4; i++) b[i] = (byte)(a[i] | ~m[i]);
        return new IPAddress(b);
    }
}
