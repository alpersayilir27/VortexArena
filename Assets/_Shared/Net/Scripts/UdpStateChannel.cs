using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VortexArena.Protocol;

namespace VortexArena.Net
{
    /// <summary>
    /// UDP 47822 poz kanalı: welcome'daki udpToken ile 0x00 UdpHello kaydı yapar
    /// — sunucu aynı 6 baytı geri yollayana (ack) dek 1 sn arayla tekrarlar. Kayıt
    /// sonrası IPoseSource'tan aldığı arena-uzayı pozlarını 20 Hz PoseUpdate (0x01)
    /// olarak gönderir; gelen Snapshot'ları (0x02) RemotePlayerRegistry'ye iletir.
    /// ArenaClient tarafından yönetilir.
    /// </summary>
    public class UdpStateChannel : MonoBehaviour
    {
        // §6.1: ack gelene dek 1 sn arayla tekrar (ArenaProtocol'de ayrı sabiti yok).
        private const float HelloRetryIntervalSeconds = 1f;

        // 20 Hz gönderim aralığı (sabit katlama: her iki işlenen de const).
        private const float PoseSendInterval = 1f / ArenaProtocol.POSE_RATE_HZ;

        /// <summary>Sunucu UDP endpoint'imizi kaydetti mi (ack alındı mı).</summary>
        public bool Registered { get; private set; }

        /// <summary>Ana thread'de, kayıt tamamlanınca bir kez tetiklenir.</summary>
        public event Action OnRegistered;

        private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();

        private UdpClient _udp;
        private IPEndPoint _serverEndpoint;
        private CancellationTokenSource _cts;
        private byte _playerId;
        private uint _udpToken;
        private volatile bool _acked;

        // ---- 20 Hz poz gönderimi (yalnız ana thread dokunur) ----
        private IPoseSource _poseSource;
        private ushort _seq;
        private float _sendAccumulator;
        private byte[] _sendBuffer;
        private MemoryStream _sendStream;
        private BinaryWriter _sendWriter;
        private bool _sendWarned;

        private void Awake()
        {
            // Önceden ayrılmış gönderim tamponu: buffer sabit kalır, stream her
            // gönderimde pozisyon sıfırlanarak yeniden kullanılır (karede GC yok).
            _sendBuffer = new byte[PoseUpdate.SIZE];
            _sendStream = new MemoryStream(_sendBuffer, 0, _sendBuffer.Length, true);
            _sendWriter = new BinaryWriter(_sendStream);
        }

        /// <summary>
        /// Poz kaynağını atar (App'teki PlayerPoseTracker kalibrasyon sonrası çağırır).
        /// Kaynak Stop()'ta SİLİNMEZ: reconnect sonrası kayıt tamamlanınca gönderim
        /// kendiliğinden sürer.
        /// </summary>
        public void SetPoseSource(IPoseSource source)
        {
            _poseSource = source;
        }

        /// <summary>Yalnız kayıtlı kaynak verilenle aynıysa temizler (sahne yıkımı güvenliği).</summary>
        public void ClearPoseSource(IPoseSource source)
        {
            if (ReferenceEquals(_poseSource, source))
            {
                _poseSource = null;
            }
        }

        /// <summary>Kayıt sürecini (yeniden) başlatır; önceki oturum varsa kapatır.</summary>
        public void StartRegistration(string serverIp, int statePort, byte playerId, uint udpToken)
        {
            Stop();

            IPAddress address;
            if (string.IsNullOrWhiteSpace(serverIp) || !IPAddress.TryParse(serverIp, out address))
            {
                Debug.LogWarning($"[UdpStateChannel] Geçersiz sunucu IP'si: '{serverIp}'; UDP kaydı yapılamadı.");
                return;
            }

            _serverEndpoint = new IPEndPoint(address, statePort);
            _playerId = playerId;
            _udpToken = udpToken;
            _acked = false;
            Registered = false;
            _sendAccumulator = 0f;
            _sendWarned = false; // yeni oturumda gönderim uyarısı yeniden loglanabilir

            try
            {
                _udp = new UdpClient(0); // ephemeral porta hemen bağlan (alım için gerekli)
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UdpStateChannel] UDP soketi açılamadı: {e.Message}");
                _udp = null;
                return;
            }

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;
            UdpClient udp = _udp;

            _ = Task.Run(() => ReceiveLoopAsync(udp, token));
            _ = Task.Run(() => SendHelloLoopAsync(udp, token));
        }

        /// <summary>Kanalı kapatır; ArenaClient kopuşta çağırır (yeni welcome'da yeniden kurulur).</summary>
        public void Stop()
        {
            try
            {
                _cts?.Cancel();
            }
            catch (Exception)
            {
                // CTS zaten dispose olduysa yut.
            }

            _cts = null;

            if (_udp != null)
            {
                try
                {
                    _udp.Close();
                }
                catch (Exception)
                {
                    // Soket zaten kapalıysa yut.
                }

                _udp = null;
            }

            _acked = false;
            Registered = false;
        }

        private void Update()
        {
            while (_mainThreadActions.TryDequeue(out Action action))
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[UdpStateChannel] Ana thread aksiyonu hata verdi: {e}");
                }
            }

            SendPoseIfDue();
        }

        /// <summary>ANA THREAD: kayıt tamamsa ve poz kaynağı hazırsa 20 Hz PoseUpdate yollar.</summary>
        private void SendPoseIfDue()
        {
            if (!Registered || _poseSource == null || _udp == null)
            {
                _sendAccumulator = 0f;
                return;
            }

            _sendAccumulator += Time.unscaledDeltaTime;
            if (_sendAccumulator < PoseSendInterval)
            {
                return;
            }

            // Çoklu aşımda (frame hitch) tek paket yeter — birikimi modulo ile kırp.
            _sendAccumulator %= PoseSendInterval;

            if (!_poseSource.TryGetArenaPoses(out Pose head, out Pose handL, out Pose handR))
            {
                return; // izleme henüz hazır değil (ör. HMD uykuda)
            }

            var update = new PoseUpdate
            {
                playerId = _playerId,
                seq = _seq++,
                clientTimeMs = (uint)Environment.TickCount,
                head = ToPoseData(head),
                handL = ToPoseData(handL),
                handR = ToPoseData(handR)
            };

            try
            {
                _sendStream.Position = 0;
                update.Write(_sendWriter);
                _sendWriter.Flush();
                _udp.Send(_sendBuffer, PoseUpdate.SIZE, _serverEndpoint);
            }
            catch (Exception e)
            {
                // Yut + spam'siz tek uyarı (yeni kayıtta sıfırlanır); UDP zaten kayıplı.
                if (!_sendWarned)
                {
                    _sendWarned = true;
                    Debug.LogWarning($"[UdpStateChannel] PoseUpdate gönderimi başarısız: {e.Message}");
                }
            }
        }

        private static PoseData ToPoseData(in Pose pose)
        {
            PoseData data;
            data.px = pose.position.x;
            data.py = pose.position.y;
            data.pz = pose.position.z;
            data.qx = pose.rotation.x;
            data.qy = pose.rotation.y;
            data.qz = pose.rotation.z;
            data.qw = pose.rotation.w;
            return data;
        }

        private void OnDestroy()
        {
            Stop();
        }

        // ------------------------------------------------------------- döngüler

        private async Task SendHelloLoopAsync(UdpClient udp, CancellationToken ct)
        {
            byte[] packet;
            using (var stream = new MemoryStream(UdpHello.SIZE))
            using (var writer = new BinaryWriter(stream))
            {
                var hello = new UdpHello { playerId = _playerId, udpToken = _udpToken };
                hello.Write(writer);
                packet = stream.ToArray();
            }

            try
            {
                while (!ct.IsCancellationRequested && !_acked)
                {
                    await udp.SendAsync(packet, packet.Length, _serverEndpoint);
                    await Task.Delay(TimeSpan.FromSeconds(HelloRetryIntervalSeconds), ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Stop() çağrıldı.
            }
            catch (ObjectDisposedException)
            {
                // Soket kapatıldı.
            }
            catch (Exception e)
            {
                if (!ct.IsCancellationRequested)
                {
                    Debug.LogWarning($"[UdpStateChannel] UdpHello gönderimi başarısız: {e.Message}");
                }
            }
        }

        private async Task ReceiveLoopAsync(UdpClient udp, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    UdpReceiveResult datagram = await udp.ReceiveAsync();
                    HandleDatagram(datagram.Buffer);
                }
            }
            catch (ObjectDisposedException)
            {
                // Stop() soketi kapattı — normal çıkış.
            }
            catch (SocketException)
            {
                // Kapanışta beklenen; reconnect yeni kanal kurar.
            }
            catch (Exception e)
            {
                if (!ct.IsCancellationRequested)
                {
                    Debug.LogWarning($"[UdpStateChannel] UDP alım hatası: {e.Message}");
                }
            }
        }

        /// <summary>Ağ thread'inde koşar; olay kuyruk üzerinden ana thread'e taşınır.</summary>
        private void HandleDatagram(byte[] buffer)
        {
            if (buffer == null || buffer.Length < 1)
            {
                return;
            }

            using (var reader = new BinaryReader(new MemoryStream(buffer)))
            {
                byte packetType = reader.ReadByte();
                switch (packetType)
                {
                    case UdpPacketType.UdpHello:
                        if (buffer.Length < UdpHello.SIZE)
                        {
                            return;
                        }

                        UdpHello ack = UdpHello.Read(reader);
                        if (ack.playerId != _playerId || ack.udpToken != _udpToken || _acked)
                        {
                            return;
                        }

                        _acked = true;
                        _mainThreadActions.Enqueue(() =>
                        {
                            Registered = true;
                            Debug.Log("[UdpStateChannel] UDP kaydı tamamlandı (ack alındı).");
                            OnRegistered?.Invoke();
                        });
                        break;

                    case UdpPacketType.Snapshot:
                    {
                        // 1(tip) + 1(playerCount) + 4(serverTick) + n×86 — kısa paketi yok say.
                        if (buffer.Length < 6 || buffer.Length < 6 + buffer[1] * SnapshotEntry.SIZE)
                        {
                            return;
                        }

                        Snapshot snap = Snapshot.Read(reader);
                        // AĞ THREAD'İ: registry kilit altında alır, olayları ana thread'de yayınlar.
                        RemotePlayerRegistry.Instance?.IngestFromNetThread(snap, Environment.TickCount, _playerId);
                        break;
                    }

                    default:
                        // Bilinmeyen paket tipi — yok say.
                        break;
                }
            }
        }
    }
}
