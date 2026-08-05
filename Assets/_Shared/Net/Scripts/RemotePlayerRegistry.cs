using System;
using System.Collections.Generic;
using UnityEngine;
using VortexArena.Protocol;

namespace VortexArena.Net
{
    /// <summary>
    /// Uzak oyuncu poz kayıtçısı tekili: UdpStateChannel'ın ağ thread'inde aldığı
    /// Snapshot'ları oyuncu başına örnek halkasında biriktirir, ana thread'den
    /// INTERP_DELAY_MS gecikmeli interpolasyonla okutur. Katılan/ayrılan uzak
    /// oyuncular ana thread'de OnRemoteJoined/OnRemoteLeft ile duyurulur. Snapshot'taki
    /// eşya durumu (§6.6) da burada tutulur ama interpole EDİLMEZ (TryGetHeldItems).
    /// ArenaClient tarafından AddComponent ile kurulur; sahne/oyun bilgisi içermez.
    /// </summary>
    public class RemotePlayerRegistry : MonoBehaviour
    {
        /// <summary>Bu süre boyunca snapshot'ta görünmeyen oyuncu ayrılmış sayılır (ms).</summary>
        private const int LEFT_TIMEOUT_MS = 1500;

        /// <summary>Oyuncu başına saklanan örnek sayısı (20 Hz'de ~3.2 sn geçmiş).</summary>
        private const int RING_SIZE = 64;

        /// <summary>Tik→zaman eşlemesinde saklanan snapshot sayısı (20 Hz'de ~3.2 sn).</summary>
        private const int TICK_RING_SIZE = 64;

        /// <summary>İki snapshot arası nominal süre (ms) — tik farkını zamana çevirmek için.</summary>
        private const int MS_PER_SNAPSHOT = 1000 / ArenaProtocol.SNAPSHOT_RATE_HZ;

        /// <summary>
        /// Ekstrapolasyonda kabul edilen en büyük "gelecek" tik farkı (2 sn'lik tik).
        /// Bundan büyük fark sarmalama ya da sunucu yeniden başlatması işaretidir.
        /// </summary>
        private const int MAX_FUTURE_TICKS = ArenaProtocol.SNAPSHOT_RATE_HZ * 2;

        public static RemotePlayerRegistry Instance { get; private set; }

        /// <summary>Ana thread'de, uzak oyuncu ilk kez görüldüğünde tetiklenir.</summary>
        public event Action<int> OnRemoteJoined;

        /// <summary>Ana thread'de, uzak oyuncu zaman aşımına uğrayınca/kopunca tetiklenir.</summary>
        public event Action<int> OnRemoteLeft;

        /// <summary>
        /// Tek snapshot'ın tik→yerel zaman damgası (recvMs = Environment.TickCount).
        /// <para>⚠️ Bu eşleme <b>GLOBAL'dir, oyuncu başına DEĞİL</b>: bir tik'te tek snapshot
        /// yayınlanır ve içinde tüm oyuncular vardır. <see cref="RemoteEntry.ring"/>'e konsa
        /// hiç pozu olmayan (ya da o tik'te görünmeyen) bir oyuncunun olayı zamanlanamazdı.</para>
        /// </summary>
        private struct TickStamp
        {
            public uint serverTick;
            public int recvMs;
            public bool valid;
        }

        /// <summary>Tek snapshot'lık poz örneği (recvMs = Environment.TickCount).</summary>
        private struct PoseSample
        {
            public int recvMs;
            public PoseData head, handL, handR;
            public byte flags;
        }

        /// <summary>Uzak oyuncu girişi: sabit boyutlu örnek halkası + son görülme zamanı.</summary>
        private class RemoteEntry
        {
            public int playerId;
            public readonly PoseSample[] ring = new PoseSample[RING_SIZE];
            public int count;
            public int nextIndex;
            public int lastRecvMs;
            public bool announced;

            // ---- §6.6 eşya durumu: halkada DEĞİL, tek yuvada ----
            // ⚠️ Bu bir DURUM'dur, poz gibi İNTERPOLE EDİLMEZ: ayrık/kategorik veri, iki eşya
            // arasında "yarı yol" yoktur (tabanca ile tüfeğin ortası diye bir eşya yok). En son
            // gelen snapshot'ın değeri geçerlidir; interp tamponu kadar erken uygulanması
            // görünürde bir sorun değil (el değiştirme 100 ms'lik pencereden hızlı olamaz).
            public byte itemL;
            public byte itemR;
            public bool gripLinked;
            public bool primaryRight;
        }

        // Ingest (ağ thread'i) ile örnekleme/Update (ana thread) bu kilit altında buluşur.
        private readonly object _gate = new object();
        private readonly Dictionary<int, RemoteEntry> _entries = new Dictionary<int, RemoteEntry>();

        // Olay yayınları kilit DIŞINDA yapılır; scratch listeleri GC üretmemek için alan.
        private readonly List<int> _joinedScratch = new List<int>();
        private readonly List<int> _leftScratch = new List<int>();

        // Tik→zaman eşlemesi (TryGetPlaybackTimeMs). Poz halkalarıyla aynı _gate altında:
        // yazan ağ thread'i (ingest), okuyan ana thread (sunum).
        private readonly TickStamp[] _tickRing = new TickStamp[TICK_RING_SIZE];
        private int _tickRingNext;
        private bool _hasNewestTick;
        private uint _newestTick;
        private int _newestTickRecvMs;

        private int _lastSnapshotMs;

        /// <summary>Son snapshot'ın alındığı Environment.TickCount değeri (tanılama).</summary>
        public int LastSnapshotMs
        {
            get
            {
                lock (_gate)
                {
                    return _lastSnapshotMs;
                }
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[RemotePlayerRegistry] İkinci örnek yok edildi (tekil).");
                Destroy(this);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnEnable()
        {
            NetEvents.OnDisconnected += HandleDisconnected;
        }

        private void OnDisable()
        {
            NetEvents.OnDisconnected -= HandleDisconnected;
        }

        /// <summary>
        /// AĞ THREAD'İNDEN çağrılır (UdpStateChannel.HandleDatagram): snapshot'taki
        /// her uzak oyuncu için örneği halkaya ekler. Kendi playerId'miz atlanır.
        /// Snapshot'ın tik damgası da burada kaydedilir (<see cref="TryGetPlaybackTimeMs"/>).
        /// </summary>
        public void IngestFromNetThread(Snapshot snap, int recvTickMs, int localPlayerId)
        {
            lock (_gate)
            {
                // ⚠️ Tik damgası, poz erken çıkışından ÖNCE yazılır: playerCount = 0 snapshot'ı
                // meşru bir yayındır (istemciler bayat avatarı onunla temizler) ve o tik'in zaman
                // damgası yine geçerlidir — o tik'te gelen bir atış olayı da zamanlanabilmeli.
                RecordTickLocked(snap.serverTick, recvTickMs);

                if (snap.players == null)
                {
                    return;
                }

                _lastSnapshotMs = recvTickMs;

                for (int i = 0; i < snap.players.Length; i++)
                {
                    SnapshotEntry se = snap.players[i];
                    if (se.playerId == localPlayerId)
                    {
                        continue; // kendi pozumuz — sunucu echo'su yok sayılır
                    }

                    if (!_entries.TryGetValue(se.playerId, out RemoteEntry entry))
                    {
                        entry = new RemoteEntry { playerId = se.playerId };
                        _entries.Add(se.playerId, entry);
                    }

                    ref PoseSample slot = ref entry.ring[entry.nextIndex];
                    slot.recvMs = recvTickMs;
                    slot.head = se.head;
                    slot.handL = se.handL;
                    slot.handR = se.handR;
                    slot.flags = se.flags;

                    // Eşya durumu (§6.6): son gelen kazanır — halkaya girmez.
                    entry.itemL = se.itemL;
                    entry.itemR = se.itemR;
                    entry.gripLinked = (se.flags & SnapshotEntry.FLAG_GRIP_LINKED) != 0;
                    entry.primaryRight = (se.flags & SnapshotEntry.FLAG_PRIMARY_RIGHT) != 0;

                    entry.nextIndex = (entry.nextIndex + 1) % RING_SIZE;
                    if (entry.count < RING_SIZE)
                    {
                        entry.count++;
                    }

                    entry.lastRecvMs = recvTickMs;
                }
            }
        }

        /// <summary>
        /// Snapshot'ın tik damgasını halkaya yazar. Çağıran <c>_gate</c>'i TUTMALIDIR.
        /// </summary>
        private void RecordTickLocked(uint serverTick, int recvTickMs)
        {
            // ⚠️ Parçalanmış snapshot (§6.3): bir tik MTU'yu aşarsa sunucu AYNI serverTick'i birden
            // çok datagramla yollar. Aynı tik ikinci kez halkaya yazılmaz — geçerli olan İLK
            // parçanın alım zamanıdır (sonraki parçalar oynatmayı gereksizce geciktirirdi).
            if (_hasNewestTick && _newestTick == serverTick)
            {
                return;
            }

            ref TickStamp slot = ref _tickRing[_tickRingNext];
            slot.serverTick = serverTick;
            slot.recvMs = recvTickMs;
            slot.valid = true;

            _tickRingNext = (_tickRingNext + 1) % TICK_RING_SIZE;

            // "En yeni tik" geri çekilmez: UDP sıra garantisi olmadığı için gecikmiş bir datagram
            // sonra gelebilir; ekstrapolasyonun dayanağı hep gerçekten en ileri tik olmalı.
            // Karşılaştırma işaretli farkla yapılır → u32 sarmalamasında da doğru kalır.
            if (!_hasNewestTick || (int)(serverTick - _newestTick) > 0)
            {
                _hasNewestTick = true;
                _newestTick = serverTick;
                _newestTickRecvMs = recvTickMs;
            }
        }

        /// <summary>
        /// ANA THREAD: verilen sunucu tik'inin <b>oynatılacağı yerel zaman</b> (Environment.TickCount
        /// ekseni). Eşleme bilinmiyorsa false — çağıran o zaman olayı HEMEN oynatır.
        /// <para>
        /// Uzak pozlar bilerek <c>INTERP_DELAY_MS</c> geriden çizilir
        /// (<see cref="GetInterpolatedPose"/>), yani <c>recvMs = R</c> olan örnek
        /// <c>renderMs == R</c> olduğunda çizilir — duvar saatinde <c>R + INTERP_DELAY_MS</c>'te.
        /// Bir olayın "eli doğru yerdeyken" oynaması için beklenecek an tam olarak budur.
        /// </para>
        /// </summary>
        public bool TryGetPlaybackTimeMs(uint serverTick, out int playbackMs)
        {
            playbackMs = 0;

            lock (_gate)
            {
                if (!_hasNewestTick)
                {
                    return false; // henüz hiç snapshot gelmedi
                }

                for (int i = 0; i < TICK_RING_SIZE; i++)
                {
                    TickStamp stamp = _tickRing[i];
                    if (stamp.valid && stamp.serverTick == serverTick)
                    {
                        playbackMs = stamp.recvMs + ArenaProtocol.INTERP_DELAY_MS;
                        return true;
                    }
                }

                // Halkada yok: ya İLERİDE (olay batch'i kendi snapshot'ından önce gelebilir — UDP
                // sıra garantisi yok) ya da halkadan düşmüş kadar eski. u32 aritmetiği doğal sardığı
                // için fark sarmalama-güvenli çıkar; geçmiş bir tik dev bir sayı verir ve tavana
                // takılır.
                uint delta = serverTick - _newestTick;
                if (delta > MAX_FUTURE_TICKS)
                {
                    // Ya çok eski (≥ ~3.2 sn, olay zaten gecikmiş) ya sarmalama/sunucu yeniden
                    // başlatması. Saçma bir gelecek zaman üretmek yerine "bilmiyorum" denir.
                    return false;
                }

                playbackMs = _newestTickRecvMs + (int)delta * MS_PER_SNAPSHOT + ArenaProtocol.INTERP_DELAY_MS;
                return true;
            }
        }

        /// <summary>
        /// Sunucunun tik eksenine oturtulmuş <b>PAYLAŞILAN saat</b> (saniye). Hiç snapshot gelmediyse
        /// false.
        /// <para><b>Neden gerekiyor:</b> iskelet blob'unun içine gönderenin zaman damgası gömülüyor
        /// ve alıcı taraf interpolasyonu o damgayla kendi render zamanını karşılaştırarak yapıyor
        /// (§6.9). İki uç aynı epoch'u paylaşmazsa interpolasyon ya uca kilitlenir ya hiç çalışmaz —
        /// gövde 12 Hz basamaklarla oynar. <c>Environment.TickCount</c> makineye özeldir, bu iş için
        /// kullanılamaz.</para>
        /// <para><b>Neden saat senkronu paketi gerekmiyor:</b> zaten 20 Hz akan snapshot'ın
        /// <c>serverTick</c>'i tüm istemcilerde <b>aynı</b> sayıdır; tek yapılan onu saniyeye
        /// çevirip son varıştan bu yana geçen süreyi eklemek. Uçlar arasındaki hata tek yönlü
        /// gecikme farkı kadardır (LAN'da birkaç ms) — 12 Hz'lik bir akışın interpolasyonu için
        /// fazlasıyla yeterli. ⚠️ Bu bir <b>mutlak</b> saat değildir ve §6.7'nin "saat senkronu
        /// gerekmez" kuralını çiğnemez: RTT ölçümü hâlâ tek uçlu damgayla yapılıyor, burada üretilen
        /// değer yalnız iki istemcinin ORTAK bir eksende buluşması içindir.</para>
        /// <para>⚠️ Sunucu yeniden başlarsa tik sıfırdan sayar ve bu saat <b>geriye atlar</b>; SDK'nın
        /// tamponu birkaç kare içinde yeni eksene oturur. Ayrı bir düzeltme eklenmedi: yeniden
        /// başlatma zaten tüm istemcilerin yeniden bağlandığı bir olaydır.</para>
        /// <para>⚠️ Değer <c>float</c>'tur ve sunucu ne kadar uzun koşarsa çözünürlüğü o kadar
        /// kabalaşır (bir haftalık kesintisiz koşuda ~60 ms). Tavan, bir iskelet karesinin
        /// süresidir (~83 ms) ve pratikte mekân sunucusu günlük yeniden başlatılır.</para>
        /// </summary>
        public bool TryGetServerTimeSeconds(out float seconds)
        {
            lock (_gate)
            {
                if (!_hasNewestTick)
                {
                    seconds = 0f;
                    return false;
                }

                int sinceNewestMs = Environment.TickCount - _newestTickRecvMs;
                double ms = (double)_newestTick * MS_PER_SNAPSHOT + sinceNewestMs;
                seconds = (float)(ms / 1000.0);
                return true;
            }
        }

        private void Update()
        {
            _joinedScratch.Clear();
            _leftScratch.Clear();

            // TickCount farkları int çıkarma ile — ~24.9 günlük sarmalamaya dayanıklı.
            int now = Environment.TickCount;

            lock (_gate)
            {
                foreach (KeyValuePair<int, RemoteEntry> kv in _entries)
                {
                    if (!kv.Value.announced)
                    {
                        kv.Value.announced = true;
                        _joinedScratch.Add(kv.Key);
                    }
                }

                foreach (KeyValuePair<int, RemoteEntry> kv in _entries)
                {
                    if (now - kv.Value.lastRecvMs > LEFT_TIMEOUT_MS)
                    {
                        _leftScratch.Add(kv.Key);
                    }
                }

                for (int i = 0; i < _leftScratch.Count; i++)
                {
                    _entries.Remove(_leftScratch[i]);
                }
            }

            // Olaylar kilit dışında: dinleyiciler registry'ye geri çağrı yapabilir.
            for (int i = 0; i < _joinedScratch.Count; i++)
            {
                OnRemoteJoined?.Invoke(_joinedScratch[i]);
            }

            for (int i = 0; i < _leftScratch.Count; i++)
            {
                OnRemoteLeft?.Invoke(_leftScratch[i]);
            }
        }

        /// <summary>
        /// ANA THREAD: INTERP_DELAY_MS geriden örnekleyip iki örnek arasında
        /// interpolasyonlu arena-uzayı pozları verir; saran çift yoksa en yakın
        /// uca kilitler. Hiç örnek yoksa false.
        /// </summary>
        public bool GetInterpolatedPose(int playerId, out Pose head, out Pose handL, out Pose handR)
        {
            head = Pose.identity;
            handL = Pose.identity;
            handR = Pose.identity;

            int renderMs = Environment.TickCount - ArenaProtocol.INTERP_DELAY_MS;

            PoseSample before = default;
            PoseSample after = default;
            bool hasBefore = false;
            bool hasAfter = false;

            lock (_gate)
            {
                if (!_entries.TryGetValue(playerId, out RemoteEntry entry) || entry.count == 0)
                {
                    return false;
                }

                int start = entry.nextIndex - entry.count;
                if (start < 0)
                {
                    start += RING_SIZE;
                }

                // Halka kronolojik sıralı: örnekleme zamanını saran çifti bul.
                for (int i = 0; i < entry.count; i++)
                {
                    PoseSample sample = entry.ring[(start + i) % RING_SIZE];
                    if (renderMs - sample.recvMs >= 0)
                    {
                        before = sample;
                        hasBefore = true;
                    }
                    else
                    {
                        after = sample;
                        hasAfter = true;
                        break;
                    }
                }
            }

            if (hasBefore && hasAfter)
            {
                int span = after.recvMs - before.recvMs;
                float t = span > 0 ? Mathf.Clamp01((renderMs - before.recvMs) / (float)span) : 1f;
                head = LerpPose(before.head, after.head, t);
                handL = LerpPose(before.handL, after.handL, t);
                handR = LerpPose(before.handR, after.handR, t);
                return true;
            }

            if (hasBefore || hasAfter)
            {
                // Saran çift yok → en yakın uca kilitle (clamp).
                PoseSample edge = hasBefore ? before : after;
                head = ToPose(edge.head);
                handL = ToPose(edge.handL);
                handR = ToPose(edge.handR);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Oyuncunun son snapshot'taki canlılık bayrağı (SnapshotEntry.FLAG_ALIVE).
        /// Kaydı/örneği olmayan id için true döner (bilinmiyorsa canlı say).
        /// </summary>
        public bool IsAlive(int playerId)
        {
            lock (_gate)
            {
                if (!_entries.TryGetValue(playerId, out RemoteEntry entry) || entry.count == 0)
                {
                    return true;
                }

                // Halkaya en son yazılan örnek: nextIndex bir sonraki BOŞ yuvayı gösterir.
                int last = entry.nextIndex - 1;
                if (last < 0)
                {
                    last += RING_SIZE;
                }

                return (entry.ring[last].flags & SnapshotEntry.FLAG_ALIVE) != 0;
            }
        }

        /// <summary>
        /// §10.9: oyuncu son snapshot'ta bir <b>iç engelin içinde</b> miydi
        /// (<see cref="SnapshotEntry.FLAG_IN_OBSTACLE"/>). Tek tüketicisi admin gözlemcidir —
        /// ihlal eden oyuncunun kuş bakışı halkası kırmızı yanıp söner.
        /// <para>Kaydı/örneği olmayan id için <c>false</c> döner. ⚠️ Varsayılan
        /// <see cref="IsAlive"/>'ın TERSİ yönde ("bilinmiyorsa ihlal yok"): bilinmeyen bir durumu
        /// ihlal saymak, ağ boşluğunda operatöre var olmayan bir olay gösterirdi.</para>
        /// <para>⚠️ Bayrak <b>durumdur</b>: her snapshot'ta yeniden geliyor, yani oyuncu engelden
        /// çıkınca ek bir mesaj olmadan kendiliğinden söner.</para>
        /// </summary>
        public bool IsInObstacle(int playerId)
        {
            lock (_gate)
            {
                if (!_entries.TryGetValue(playerId, out RemoteEntry entry) || entry.count == 0)
                {
                    return false;
                }

                int last = entry.nextIndex - 1;
                if (last < 0)
                {
                    last += RING_SIZE;
                }

                return (entry.ring[last].flags & SnapshotEntry.FLAG_IN_OBSTACLE) != 0;
            }
        }

        /// <summary>
        /// §6.6: oyuncunun <b>son bilinen</b> eşya durumu. Oyuncu yoksa false.
        /// <para>⚠️ <c>gripLinked</c> olmadan "aynı id iki slotta" tek başına çift elle tutmak
        /// demek DEĞİLDİR — çift tabanca meşru bir durumdur (§6.6 çözüm tablosu).
        /// <c>primaryRight</c> yalnız <c>gripLinked</c> iken anlamlıdır.</para>
        /// </summary>
        public bool TryGetHeldItems(int playerId, out byte itemL, out byte itemR, out bool gripLinked, out bool primaryRight)
        {
            lock (_gate)
            {
                if (!_entries.TryGetValue(playerId, out RemoteEntry entry))
                {
                    itemL = 0;
                    itemR = 0;
                    gripLinked = false;
                    primaryRight = false;
                    return false;
                }

                itemL = entry.itemL;
                itemR = entry.itemR;
                gripLinked = entry.gripLinked;
                primaryRight = entry.primaryRight;
                return true;
            }
        }

        /// <summary>ANA THREAD: duyurulmuş (announced) uzak oyuncu id'lerini doldurur.</summary>
        public void GetActivePlayerIds(List<int> buffer)
        {
            if (buffer == null)
            {
                return;
            }

            buffer.Clear();

            lock (_gate)
            {
                foreach (KeyValuePair<int, RemoteEntry> kv in _entries)
                {
                    if (kv.Value.announced)
                    {
                        buffer.Add(kv.Key);
                    }
                }
            }
        }

        /// <summary>Kopuşta tüm girişleri temizler ve ayrılışları yayınlar (ana thread'deyiz).</summary>
        private void HandleDisconnected()
        {
            _leftScratch.Clear();

            lock (_gate)
            {
                foreach (KeyValuePair<int, RemoteEntry> kv in _entries)
                {
                    if (kv.Value.announced)
                    {
                        _leftScratch.Add(kv.Key);
                    }
                }

                _entries.Clear();

                // Tik→zaman eşlemesi de düşer: yeni oturumda sunucunun tik ekseni sıfırdan
                // başlayabilir, bayat damga yanlış bir oynatma zamanı üretirdi.
                Array.Clear(_tickRing, 0, _tickRing.Length);
                _tickRingNext = 0;
                _hasNewestTick = false;
                _newestTick = 0;
                _newestTickRecvMs = 0;
            }

            for (int i = 0; i < _leftScratch.Count; i++)
            {
                OnRemoteLeft?.Invoke(_leftScratch[i]);
            }
        }

        // ------------------------------------------------------------- dönüşümler

        private static Pose ToPose(in PoseData data)
        {
            // PoseData zaten normalize quaternion taşır — yeniden normalize gerekmez.
            return new Pose(
                new Vector3(data.px, data.py, data.pz),
                new Quaternion(data.qx, data.qy, data.qz, data.qw));
        }

        private static Pose LerpPose(in PoseData a, in PoseData b, float t)
        {
            Vector3 position = Vector3.Lerp(
                new Vector3(a.px, a.py, a.pz),
                new Vector3(b.px, b.py, b.pz), t);
            Quaternion rotation = Quaternion.Slerp(
                new Quaternion(a.qx, a.qy, a.qz, a.qw),
                new Quaternion(b.qx, b.qy, b.qz, b.qw), t);
            return new Pose(position, rotation);
        }
    }
}
