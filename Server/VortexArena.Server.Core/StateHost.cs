#nullable enable
using System.Net;
using System.Net.Sockets;
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>UDP durum kanalı (statePort). 0x00 UdpHello kaydı — playerId↔udpToken doğrulanır,
/// endpoint kaydedilir, aynı 6 bayt ack olarak geri yollanır (§6.1). 0x01 PoseUpdate alımı —
/// yalnız kayıtlı endpoint'ten, u16 sarmalamalı seq kontrolüyle (§6.2). 0x02 Snapshot yayını —
/// 20 Hz, pozlu oyuncular tek pakette UDP kayıtlı herkese (admin dahil) yollanır (§6.3).</summary>
public sealed class StateHost
{
    private readonly PlayerRegistry _registry;
    private readonly int _port;
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private Task? _snapshotLoop;

    /// <summary>Başarılı UDP kayıt bildirimi (konsol satırı için).</summary>
    public event Action<byte, IPEndPoint>? UdpRegistered;

    public StateHost(PlayerRegistry registry, int port)
    {
        _registry = registry;
        _port = port;
    }

    public void Start()
    {
        if (_loop is { IsCompleted: false } || _snapshotLoop is { IsCompleted: false }) return;
        var udp = new UdpClient(_port);
        _udp = udp;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(() => ReceiveLoopAsync(udp, token));
        _snapshotLoop = Task.Run(() => SnapshotLoopAsync(udp, token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _udp?.Close();
        _udp = null;
        _cts = null;
        _loop = null;
        _snapshotLoop = null;
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
                    if (data.Length < PoseUpdate.SIZE) break;
                    HandlePoseUpdate(data, result.RemoteEndPoint);
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

    /// <summary>0x01 PoseUpdate alımı: yalnız 0x00 ile kaydedilmiş endpoint'ten kabul edilir,
    /// eski/yinelenen seq atılır, kabul edilen poz PoseGate altında saklanır.
    /// 20 Hz akış olduğu için konsola satır basılmaz; ret de sessizdir.</summary>
    private void HandlePoseUpdate(byte[] data, IPEndPoint remote)
    {
        PoseUpdate pose;
        using (var ms = new MemoryStream(data, 0, data.Length, writable: false))
        using (var reader = new BinaryReader(ms))
        {
            reader.ReadByte(); // tip baytı dispatcher'da tüketildi sayılır
            pose = PoseUpdate.Read(reader);
        }

        if (!_registry.TryGetByPlayerId(pose.playerId, out var state)) return;
        // Kayıtsız/yabancı kaynaktan poz kabul edilmez (spoof koruması, §6.1).
        if (state.UdpEndpoint == null || !state.UdpEndpoint.Equals(remote)) return;

        lock (state.PoseGate)
        {
            // u16 sarmalamalı sıra kontrolü: (short) farkı 65535→0 geçişini doğru sıralar.
            if (state.HasPose && (short)(pose.seq - state.LastSeq) <= 0) return;
            state.LastPose = pose;
            state.LastSeq = pose.seq;
            state.HasPose = true;
            state.LastPoseAt = DateTime.UtcNow;
        }
    }

    /// <summary>20 Hz snapshot yayını: pozlu çevrimiçi oyuncular pakete yazılır, UDP kayıtlı
    /// ve çevrimiçi HERKESE (admin dahil — birden çok admin varsa her biri ayrı hedef) aynı
    /// buffer yollanır. Girdi yokken hedef varsa count=0 snapshot gider (istemci uzak avatar
    /// kalmadığını böyle anlar); ikisi de yoksa gönderilmez ve serverTick artmaz.
    /// <para>
    /// <b>MTU parçalama (§6.3):</b> girdi sayısı <see cref="ArenaProtocol.SNAPSHOT_MAX_ENTRIES_PER_PACKET"/>'i
    /// aşarsa aynı tik birden çok datagrama bölünür (hepsi aynı serverTick'i taşır, hepsi aynı
    /// hedeflere gider). İstemcide birleştirme YOKTUR ve gerekmez: her paket taşıdığı girdileri
    /// bağımsız uygular, oyuncu düşürme kararı zaman aşımıdır. Bu yüzden tel formatı değişmedi.
    /// </para>
    /// Saniyede bir konsola özet basılır.</summary>
    private async Task SnapshotLoopAsync(UdpClient udp, CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.0 / ArenaProtocol.SNAPSHOT_RATE_HZ));
        uint serverTick = 0;
        var summaryDue = DateTime.UtcNow.AddSeconds(1);
        var entries = new List<SnapshotEntry>(ArenaProtocol.SNAPSHOT_MAX_ENTRIES_PER_PACKET);
        var targets = new List<IPEndPoint>();
        var packets = new List<byte[]>(1);

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(token)) break;
            }
            catch (OperationCanceledException) { break; }

            entries.Clear();
            targets.Clear();
            var onlinePlayers = 0;
            foreach (var state in _registry.Snapshot())
            {
                if (!state.Online) continue;
                if (state.UdpEndpoint != null) targets.Add(state.UdpEndpoint);
                if (state.Role != "player") continue;
                onlinePlayers++;
                // flags bit0 = alive (§10.2): MatchDirector kilidi altında yazılır, burada kilitsiz
                // okunur (bool okuması atomik; bir tik gecikme snapshot için önemsiz).
                var alive = state.Alive;
                lock (state.PoseGate)
                {
                    if (!state.HasPose) continue;
                    var pose = state.LastPose;
                    entries.Add(new SnapshotEntry
                    {
                        playerId = (byte)state.PlayerId,
                        flags = alive ? SnapshotEntry.FLAG_ALIVE : (byte)0,
                        head = pose.head,
                        handL = pose.handL,
                        handR = pose.handR
                    });
                }
            }

            if (entries.Count == 0 && targets.Count == 0) continue; // boş döngü — gönderme, tik ilerletme

            serverTick++;
            BuildPackets(entries, serverTick, packets);

            foreach (var packet in packets)
            {
                foreach (var target in targets)
                {
                    try
                    {
                        await udp.SendAsync(packet, target, token);
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception)
                    {
                        // Windows'ta ulaşılamayan hedef 10054 vb. fırlatabilir — yayın döngüsü ölmesin.
                    }
                }
            }

            var now = DateTime.UtcNow;
            if (now >= summaryDue)
            {
                summaryDue = now.AddSeconds(1);
                var bytes = 0;
                foreach (var packet in packets) bytes += packet.Length;
                var split = packets.Count > 1 ? $" ({packets.Count} parça)" : "";
                Console.WriteLine($"[state] oyuncu {onlinePlayers}, pozlu {entries.Count}, snapshot {bytes} B{split}, hedef {targets.Count}");
            }
        }
    }

    /// <summary>
    /// Girdileri MTU'ya sığan datagramlara böler (§6.3). Girdi yoksa tek bir count=0 paketi
    /// üretir — istemciler bayat avatarı bununla da temizleyebilsin. Tüm parçalar aynı
    /// <paramref name="serverTick"/>'i taşır.
    /// </summary>
    private static void BuildPackets(List<SnapshotEntry> entries, uint serverTick, List<byte[]> output)
    {
        output.Clear();
        var perPacket = ArenaProtocol.SNAPSHOT_MAX_ENTRIES_PER_PACKET;

        for (var offset = 0; offset == 0 || offset < entries.Count; offset += perPacket)
        {
            var count = Math.Min(perPacket, entries.Count - offset);
            var chunk = new SnapshotEntry[count];
            entries.CopyTo(offset, chunk, 0, count);

            var snapshot = new Snapshot { serverTick = serverTick, players = chunk };
            using var ms = new MemoryStream(6 + count * SnapshotEntry.SIZE);
            using var writer = new BinaryWriter(ms);
            snapshot.Write(writer);
            output.Add(ms.ToArray());
        }
    }
}
