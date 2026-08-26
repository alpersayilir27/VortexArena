#nullable enable
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>Sends the discovery beacon every BEACON_INTERVAL from every usable IPv4 interface, to both
/// 255.255.255.255 and the interface's subnet broadcast (§4; cosmos UdpBeaconService pattern).</summary>
/// <remarks>Only an auto-fill convenience — a manually entered IP always wins.</remarks>
public sealed class BeaconService
{
    private readonly string _serverId = Guid.NewGuid().ToString(); // fixed for the application's lifetime
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

    /// <summary>Cancel → drain → dispose. Idempotent: a second call is a no-op.</summary>
    /// <remarks>No resource to close afterwards — the UdpClient is created and disposed per round
    /// inside the loop, nothing outlives it.</remarks>
    public async Task StopAsync()
    {
        var cts = _cts;
        var loop = _loop;
        _cts = null;
        _loop = null;
        if (cts == null && loop == null) return;

        cts?.Cancel();
        await ServiceShutdown.DrainAsync("beacon", loop);
        cts?.Dispose();
    }

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            // Re-enumerated on every round: Wi-Fi/adapter changes are picked up automatically.
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
                    // Bound to the interface address so the global broadcast leaves through it.
                    using var udp = new UdpClient(new IPEndPoint(local, 0)) { EnableBroadcast = true };
                    await udp.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, _beaconPort));
                    await udp.SendAsync(bytes, bytes.Length, new IPEndPoint(subnetBroadcast, _beaconPort));
                }
                catch (Exception ex)
                {
                    // A failure on a single interface must not kill the loop.
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
                // Skip if the interface disappeared while enumerating.
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
