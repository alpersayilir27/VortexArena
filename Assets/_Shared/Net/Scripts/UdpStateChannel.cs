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
    /// UDP 47822 poz kanalının Faz 1 iskeleti: welcome'daki udpToken ile 0x00 UdpHello
    /// kaydı yapar — sunucu aynı 6 baytı geri yollayana (ack) dek 1 sn arayla tekrarlar.
    /// UdpClient kayıttan sonra AÇIK tutulur: Faz 2'de 20 Hz poz gönderimi ve snapshot
    /// alımı aynı soket üzerinden eklenecek. ArenaClient tarafından yönetilir.
    /// </summary>
    public class UdpStateChannel : MonoBehaviour
    {
        // §6.1: ack gelene dek 1 sn arayla tekrar (ArenaProtocol'de ayrı sabiti yok).
        private const float HelloRetryIntervalSeconds = 1f;

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
                        // Faz 2: snapshot alımı + INTERP_DELAY_MS interpolasyonu burada işlenecek.
                        break;

                    default:
                        // Bilinmeyen paket tipi — yok say.
                        break;
                }
            }
        }
    }
}
