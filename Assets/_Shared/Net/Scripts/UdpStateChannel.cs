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
    /// Atış/atma olaylarını (0x03) HEMEN yollar ve gelen olay batch'lerini (0x04)
    /// NetEvents.OnRemoteFireEvent olarak yayınlar (§6.4/6.5).
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

        // ---- 0x03 olay gönderimi (yalnız ana thread dokunur) ----
        // ⚠️ Poz tamponundan AYRI: olay HEMEN gider, yani poz yazımının ortasına düşebilir;
        // paylaşılan stream'de ikisi birbirinin pozisyonunu ezerdi. Ayrıca 10 olay/sn'de
        // her seferinde tampon ayırmak boşuna GC olurdu.
        private byte[] _eventBuffer;
        private MemoryStream _eventStream;
        private BinaryWriter _eventWriter;
        private bool _eventSendWarned;

        // ⚠️ Poz _seq'inden AYRI sayaç: POZ seq'i sıra zorlaması yapar (durum — son gelen
        // kazanır), OLAY seq'i yalnız kopya bastırır (§6.4). Tek sayaca indirgenirse poz
        // kaybı olay numaralarında boşluk açar ve sunucunun kayıp ölçümü yalan söyler.
        private ushort _eventSeq;

        // ---- 0x04 batch alımı (ağ thread'i) ----
        // Son işlenen tik'lerin halkası: batch'in kimliği serverTick ve tik başına en fazla
        // bir batch üretilir (§6.5). Halka yalnız BİREBİR TEKRARI düşürür.
        private readonly uint[] _seenTicks = new uint[ArenaProtocol.EVENT_TICK_HISTORY];
        private readonly bool[] _seenTicksValid = new bool[ArenaProtocol.EVENT_TICK_HISTORY];
        private int _seenTicksNext;

        // ---- Ağ telemetrisi (§6.7) — ölçümün TAMAMI istemcide ----
        // ⚠️ Yüksek çözünürlüklü monotonik saat şart: Environment.TickCount'un çözünürlüğü ~10-16 ms
        // ve LAN'da beklenen RTT 5-15 ms — onunla ölçmek gürültüden başka bir şey vermez.
        // (System.Diagnostics tam nitelikli çağrılıyor: `using` eklemek Debug'ı UnityEngine.Debug ile
        // çakıştırır ve dosyadaki her Debug.Log satırı derlenmez olur.)
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

        // §6.7: RTT yoklaması 1 Hz. ⚠️ ARTIRILMAZ — her yoklama 2 datagram (gidiş + echo) ve bu
        // ürünün darboğazı bant değil paket sayısıdır. Jitter zaten snapshot varışlarından 20 Hz
        // çözünürlükle ve sıfır ek paketle ölçülüyor; bu paket yalnız operatörün okuduğu sayı içindir.
        private const float RttProbeIntervalSeconds = 1f;

        /// <summary>Telemetri alanlarının kilidi: ağ thread'i yazar, ana thread (status kurulumu)
        /// okur. 20 Hz yazma / 0.2 Hz okuma olduğu için çekişme yok sayılır.</summary>
        private readonly object _telemetryGate = new object();

        private float _probeAccumulator;
        private byte[] _probeBuffer;
        private MemoryStream _probeStream;
        private BinaryWriter _probeWriter;

        /// <summary>Bekleyen yoklamanın telde giden nonce'ı ve yerel yüksek çözünürlüklü damgası.
        /// Aynı anda yalnız BİR yoklama açıktır (1 Hz gönderim, RTT ≪ 1 sn) — bu yüzden halka
        /// gerekmiyor; nonce yalnız bayat bir echo'yu ayıklamak için.</summary>
        private uint _probeNonce;
        private long _probeSentTicks;
        private bool _probePending;

        private int _rttMs = -1;
        private float _jitterMs = -1f;

        /// <summary>Downlink snapshot varış damgası (yüksek çözünürlük) ve son görülen serverTick.</summary>
        private long _lastSnapshotTicks;
        private uint _lastServerTick;
        private bool _hasServerTick;

        private int _snapshotsReceived;
        private int _snapshotsLost;

        private void Awake()
        {
            // Önceden ayrılmış gönderim tamponu: buffer sabit kalır, stream her
            // gönderimde pozisyon sıfırlanarak yeniden kullanılır (karede GC yok).
            _sendBuffer = new byte[PoseUpdate.SIZE];
            _sendStream = new MemoryStream(_sendBuffer, 0, _sendBuffer.Length, true);
            _sendWriter = new BinaryWriter(_sendStream);

            _eventBuffer = new byte[FireEvent.SIZE];
            _eventStream = new MemoryStream(_eventBuffer, 0, _eventBuffer.Length, true);
            _eventWriter = new BinaryWriter(_eventStream);

            _probeBuffer = new byte[RttProbe.SIZE];
            _probeStream = new MemoryStream(_probeBuffer, 0, _probeBuffer.Length, true);
            _probeWriter = new BinaryWriter(_probeStream);
        }

        /// <summary>
        /// ANA THREAD: ölçülen telemetriyi okur ve pencere sayaçlarını sıfırlar (§6.7).
        /// <c>ArenaClient</c> <c>status</c> kurarken çağırır.
        /// <para>RTT ve jitter <b>süreklidir</b> (EWMA — sıfırlanmaz), kayıp ise pencere başına
        /// hesaplanır: yüzdenin anlamlı olması için paydası "son ölçüm penceresi" olmalı, yoksa
        /// oturum başındaki tek bir kayıp saatler boyunca yüzdeyi kirletir.</para>
        /// </summary>
        public void SampleTelemetry(out int rttMs, out float jitterMs, out float lossPct)
        {
            lock (_telemetryGate)
            {
                rttMs = _rttMs;
                jitterMs = _jitterMs;

                int total = _snapshotsReceived + _snapshotsLost;
                lossPct = total > 0 ? 100f * _snapshotsLost / total : -1f;

                _snapshotsReceived = 0;
                _snapshotsLost = 0;
            }
        }

        /// <summary>
        /// Poz kaynağını atar (App'teki PlayerPoseTracker Start'ta çağırır; kalibrasyon beklenmez).
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
            _eventSendWarned = false;

            // Yeni oturum = yeni sunucu tik ekseni ve yeni ağ yolu → telemetri sıfırlanır.
            // ⚠️ _lastServerTick taşınırsa (sunucu yeniden başladıysa tik sıfırdan sayar) ilk
            // snapshot'lar "geriye gitti" görünür ve kayıp yüzdesi yalan söyler.
            lock (_telemetryGate)
            {
                _rttMs = -1;
                _jitterMs = -1f;
                _lastSnapshotTicks = 0;
                _hasServerTick = false;
                _lastServerTick = 0;
                _snapshotsReceived = 0;
                _snapshotsLost = 0;
                _probePending = false;
            }

            _probeAccumulator = 0f;

            // Yeni oturum = yeni sunucu tik ekseni: eski halka bu oturumun tik'lerini yanlışlıkla
            // "görülmüş" sayabilir (sunucu yeniden başladıysa tik sıfırdan sayar) → ilk batch'ler
            // sessizce düşerdi.
            Array.Clear(_seenTicksValid, 0, _seenTicksValid.Length);
            _seenTicksNext = 0;

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
            SendRttProbeIfDue();
        }

        /// <summary>ANA THREAD: 1 Hz RTT yoklaması (§6.7). Kayıt yoksa hiç gönderilmez.
        /// <para>Cevapsız kalan yoklama <b>zaman aşımına uğratılmaz</b>: RTT son BAŞARILI ölçümü
        /// göstermeye devam eder. Sebebi, kaybın kendi göstergesi olması — <c>lossPct</c> zaten
        /// düşerken ping'i "-" yapmak operatöre iki kez aynı şeyi söylerdi ve panelde satırın
        /// titremesine yol açardı.</para></summary>
        private void SendRttProbeIfDue()
        {
            if (!Registered || _udp == null)
            {
                _probeAccumulator = 0f;
                return;
            }

            _probeAccumulator += Time.unscaledDeltaTime;
            if (_probeAccumulator < RttProbeIntervalSeconds)
            {
                return;
            }

            _probeAccumulator = 0f;

            long sentTicks = _clock.ElapsedTicks;
            // Nonce = gönderim anının ms değeri; bayat bir echo'yu ayıklamaya yeter (aynı anda tek
            // yoklama açık). Sunucu bu değeri OKUMAZ, aynen geri yazar.
            uint nonce = unchecked((uint)_clock.ElapsedMilliseconds);

            lock (_telemetryGate)
            {
                _probeNonce = nonce;
                _probeSentTicks = sentTicks;
                _probePending = true;
            }

            var probe = new RttProbe { playerId = _playerId, clientStamp = nonce };

            try
            {
                _probeStream.Position = 0;
                probe.Write(_probeWriter);
                _probeWriter.Flush();
                _udp.Send(_probeBuffer, RttProbe.SIZE, _serverEndpoint);
            }
            catch (Exception)
            {
                // Yoklama kaybı zararsız: bir sonraki saniye yenisi gider. Poz/olay yolundaki gibi
                // uyarı bile basılmaz — telemetri için log gürültüsü üretmeye değmez.
            }
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

            // §6.2: eşya baytları pozla AYNI pakette gider (aynı otorite — istemci-otoriter
            // sunum bilgisi). Kaynak bunları kendi çözer; Net katmanı eşya tablosunu bilmez.
            _poseSource.GetHeldItems(out byte itemL, out byte itemR, out byte gripFlags);

            var update = new PoseUpdate
            {
                playerId = _playerId,
                seq = _seq++,
                clientTimeMs = (uint)Environment.TickCount,
                itemL = itemL,
                itemR = itemR,
                gripFlags = gripFlags,
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

        /// <summary>
        /// §6.4: atış/atma olayı yollar. <b>HEMEN gider</b> (poz tik'i beklenmez — bekletmek yerel
        /// tetik ile relay arasına 0–50 ms koyar, karşılığı yoktur). Kayıt yoksa sessiz no-op.
        /// </summary>
        /// <param name="kind"><c>FireEventEntry.KIND_SHOT</c> / <c>KIND_THROW</c>.</param>
        /// <param name="rightHand">Olay sağ elden mi çıktı.</param>
        /// <param name="itemId">Eşyanın <c>netItemId</c>'si (§6.6); 0 = çözülemedi.</param>
        /// <param name="arenaDirection">Nişan yönü, <b>ARENA uzayında</b> — dünya→arena çevirimi
        /// ÇAĞIRANIN işidir (Net katmanı dönüşümü bilmez). Birim olmak zorunda değil.</param>
        /// <param name="magnitudeMeters">Türe göre: atışta mesafe (m), atmada başlangıç hızı (m/sn).</param>
        public void SendFireEvent(byte kind, bool rightHand, byte itemId, Vector3 arenaDirection, float magnitudeMeters)
        {
            if (!Registered || _udp == null)
            {
                return; // henüz kayıtlı değiliz: olayın gideceği bir endpoint yok
            }

            OctahedralDirection.Encode(
                arenaDirection.x, arenaDirection.y, arenaDirection.z,
                out short dirOctX, out short dirOctY);

            var entry = new FireEventEntry
            {
                playerId = _playerId,
                kindHand = FireEventEntry.PackKindHand(kind, rightHand),
                itemId = itemId,
                dirOctX = dirOctX,
                dirOctY = dirOctY,
                magnitude = ToMagnitudeCm(magnitudeMeters)
            };

            var msg = new FireEvent { seq = _eventSeq++, entry = entry };

            try
            {
                _eventStream.Position = 0;
                msg.Write(_eventWriter);
                _eventWriter.Flush();
                _udp.Send(_eventBuffer, FireEvent.SIZE, _serverEndpoint);
            }
            catch (Exception e)
            {
                // Poz yolundaki gibi: yut + spam'siz tek uyarı (yeni kayıtta sıfırlanır).
                if (!_eventSendWarned)
                {
                    _eventSendWarned = true;
                    Debug.LogWarning($"[UdpStateChannel] FireEvent gönderimi başarısız: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Metre → cm, u16'ya <b>clamp</b>. Taşma sarmalanmaz: 700 m'lik bir mesafe 4400 cm olarak
        /// görünürse tracer arkaya doğru kısacık çizilir; tavana kilitlemek en kötü halde biraz
        /// kısa bir tracer verir. Negatif değer (hatalı çağrı) 0'a düşer.
        /// </summary>
        private static ushort ToMagnitudeCm(float meters)
        {
            if (!(meters > 0f))
            {
                return 0; // NaN de buraya düşer (karşılaştırma false)
            }

            double cm = Math.Round(meters * 100.0);
            return cm >= ushort.MaxValue ? ushort.MaxValue : (ushort)cm;
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

        /// <summary>
        /// Batch tik'i son <see cref="ArenaProtocol.EVENT_TICK_HISTORY"/> tik içinde işlendi mi
        /// (§6.5 kopya bastırma). Halka yalnız alım thread'inden okunup yazılır — kilit yok;
        /// tek yazarı olan diğer nokta <see cref="StartRegistration"/> ve orası alım döngüsü
        /// iptal edildikten SONRA temizler.
        /// </summary>
        private bool WasTickSeen(uint serverTick)
        {
            for (int i = 0; i < _seenTicks.Length; i++)
            {
                if (_seenTicksValid[i] && _seenTicks[i] == serverTick)
                {
                    return true;
                }
            }

            return false;
        }

        private float TicksToMs(long tickDelta)
            => (float)(tickDelta * 1000.0 / System.Diagnostics.Stopwatch.Frequency);

        /// <summary>
        /// AĞ THREAD'İ: downlink jitter'ı ve snapshot kaybını gelen akıştan ölçer (§6.7) — <b>ek
        /// paket yoktur</b>, ölçüm zaten alınan 20 Hz snapshot'ın yan ürünüdür.
        /// <para>⚠️ <b>Aynı tik'in parçaları sayılmaz</b> (§6.3 MTU parçalama): 16'dan fazla girdide
        /// bir tik birden çok datagramla gelir ve her parçayı ayrı "varış" saymak jitter'ı 0'a,
        /// kaybı yanlış paydaya çekerdi.</para>
        /// <para>⚠️ <b>Geriye giden tik yok sayılır:</b> UDP sırayı bozabilir ve "eski tik" bir kayıp
        /// değildir. Kayıp yalnız İLERİ boşluktan sayılır.</para>
        /// </summary>
        private void TrackDownlink(uint serverTick)
        {
            long nowTicks = _clock.ElapsedTicks;

            lock (_telemetryGate)
            {
                if (_hasServerTick)
                {
                    // Aynı tik = parçalanmış snapshot'ın ikinci datagramı → ölçüme girmez.
                    if (serverTick == _lastServerTick)
                    {
                        return;
                    }

                    long advance = (long)serverTick - _lastServerTick;
                    if (advance < 0)
                    {
                        return; // sırası bozuk geldi; kayıp değil
                    }

                    if (advance > 1)
                    {
                        _snapshotsLost += (int)(advance - 1);
                    }

                    if (_lastSnapshotTicks != 0)
                    {
                        float intervalMs = TicksToMs(nowTicks - _lastSnapshotTicks);
                        // Kayıp, aralığı katları kadar uzatır; beklenen aralık boşlukla ölçeklenmezse
                        // kayıp jitter olarak ikinci kez raporlanır.
                        float expectedMs = 1000f / ArenaProtocol.SNAPSHOT_RATE_HZ * advance;
                        float deviation = Mathf.Abs(intervalMs - expectedMs);
                        _jitterMs = _jitterMs < 0f ? deviation : _jitterMs * 0.9f + deviation * 0.1f;
                    }
                }

                _snapshotsReceived++;
                _lastServerTick = serverTick;
                _hasServerTick = true;
                _lastSnapshotTicks = nowTicks;
            }
        }

        /// <summary>Tik'i halkaya yazar (en eskisinin üstüne — sabit bellek, GC yok).</summary>
        private void MarkTickSeen(uint serverTick)
        {
            _seenTicks[_seenTicksNext] = serverTick;
            _seenTicksValid[_seenTicksNext] = true;
            _seenTicksNext = (_seenTicksNext + 1) % _seenTicks.Length;
        }

        /// <summary>
        /// AĞ THREAD'İ: bir tik'in atış/atma olaylarını uygular. <c>0x04</c> ve <c>0x05</c>'in
        /// <b>ortak</b> yoludur (§6.5/6.8) — ikisi de aynı tik halkasını kullanmak ZORUNDA, ayrı
        /// halka açılırsa aynı tik iki kez oynar (çift tracer + çift ses).
        /// </summary>
        private void DispatchFireEvents(uint serverTick, FireEventEntry[] events)
        {
            // §6.5: kopya koruması seq DEĞİL TİK'tir — aynı serverTick'i ikinci kez görürsek tüm
            // bloğu düşürürüz (UDP paket çoğaltabilir → çift tracer).
            // ⚠️ SIRA ZORLAMASI YOK: "tick < lastTick → at" YAZILMAZ. O kural POZ kuralıdır (durum:
            // son gelen kazanır); olaya kopyalamak en kolay yapılan hatadır. Eski tik'li ama
            // GÖRÜLMEMİŞ blok OYNATILIR — ~50 ms gecikmiş tracer, kaybolmuş tracer'dan iyidir.
            if (WasTickSeen(serverTick))
            {
                return;
            }

            MarkTickSeen(serverTick);

            if (events == null)
            {
                return;
            }

            for (int i = 0; i < events.Length; i++)
            {
                FireEventEntry e = events[i];

                // §6.5: atan kendi olayını da geri alır ve KENDİSİ yok sayar (sunucu hedef başına
                // ayrı blok üretmez) — snapshot'ta kendi pozunu yok saymasıyla birebir aynı desen.
                if (e.playerId == _playerId)
                {
                    continue;
                }

                OctahedralDirection.Decode(e.dirOctX, e.dirOctY,
                    out float dx, out float dy, out float dz);

                var evt = new RemoteFireEvent
                {
                    playerId = e.playerId,
                    kind = e.Kind,
                    rightHand = e.IsRightHand,
                    itemId = e.itemId,
                    arenaDirection = new Vector3(dx, dy, dz),
                    magnitude = e.magnitude / 100f, // telde cm (§6.4)
                    serverTick = serverTick
                };

                // AĞ THREAD'İNDEYİZ: yayın ana thread'e taşınır (dinleyiciler sahne/Unity API'sine
                // dokunuyor).
                _mainThreadActions.Enqueue(() => NetEvents.RaiseRemoteFireEvent(evt));
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
                        // 1(tip) + 1(playerCount) + 4(serverTick) + n×88 — kısa paketi yok say.
                        if (buffer.Length < 6 || buffer.Length < 6 + buffer[1] * SnapshotEntry.SIZE)
                        {
                            return;
                        }

                        Snapshot snap = Snapshot.Read(reader);
                        // §6.7: downlink jitter ve kaybı BU akıştan ölçülür — ek paket yok.
                        TrackDownlink(snap.serverTick);
                        // AĞ THREAD'İ: registry kilit altında alır, olayları ana thread'de yayınlar.
                        RemotePlayerRegistry.Instance?.IngestFromNetThread(snap, Environment.TickCount, _playerId);
                        break;
                    }

                    case UdpPacketType.RttProbe:
                    {
                        if (buffer.Length < RttProbe.SIZE)
                        {
                            return;
                        }

                        RttProbe echo = RttProbe.Read(reader);
                        long nowTicks = _clock.ElapsedTicks;

                        lock (_telemetryGate)
                        {
                            // Bayat/yabancı echo'yu ayıkla: yalnız bekleyen yoklamanın nonce'ı sayılır.
                            if (!_probePending || echo.clientStamp != _probeNonce)
                            {
                                return;
                            }

                            _probePending = false;

                            float rtt = TicksToMs(nowTicks - _probeSentTicks);
                            // EWMA: tek bir gecikmiş echo göstergeyi zıplatmasın. İlk ölçüm doğrudan
                            // yazılır, yoksa -1'den yavaşça tırmanan yanlış bir değer görünürdü.
                            _rttMs = _rttMs < 0
                                ? Mathf.RoundToInt(rtt)
                                : Mathf.RoundToInt(_rttMs * 0.7f + rtt * 0.3f);
                        }

                        break;
                    }

                    case UdpPacketType.EventBatch:
                    {
                        // 1(tip) + 1(count) + 4(serverTick) + n×9 — kısa paketi yok say.
                        if (buffer.Length < 6 || buffer.Length < 6 + buffer[1] * FireEventEntry.SIZE)
                        {
                            return;
                        }

                        EventBatch batch = EventBatch.Read(reader);
                        DispatchFireEvents(batch.serverTick, batch.events);
                        break;
                    }

                    case UdpPacketType.SnapshotWithEvents:
                    {
                        // 1(tip) + 1(playerCount) + 1(eventCount) + 4(serverTick) + n×88 + m×9
                        if (buffer.Length < SnapshotWithEvents.HEADER_SIZE
                            || buffer.Length < SnapshotWithEvents.HEADER_SIZE
                                               + buffer[1] * SnapshotEntry.SIZE
                                               + buffer[2] * FireEventEntry.SIZE)
                        {
                            return;
                        }

                        SnapshotWithEvents combined = SnapshotWithEvents.Read(reader);

                        // ⚠️ Downlink ölçümü 0x05'i de SAYMALI (§6.7): saymazsa birleştirme devreye
                        // girdiği anda kayıp %100 görünür.
                        TrackDownlink(combined.serverTick);

                        // Snapshot bloğu: 0x02 ile birebir aynı işlem. Tik tekrarında (UDP paket
                        // çoğaltabilir) durumu yeniden uygulamak zararsızdır — son gelen kazanır.
                        RemotePlayerRegistry.Instance?.IngestFromNetThread(
                            new Snapshot { serverTick = combined.serverTick, players = combined.players },
                            Environment.TickCount, _playerId);

                        // Olay bloğu: 0x04 ile AYNI koddan ve AYNI tik halkasından geçer (§6.8).
                        DispatchFireEvents(combined.serverTick, combined.events);
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
