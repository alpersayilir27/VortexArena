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
    /// oyuncular ana thread'de OnRemoteJoined/OnRemoteLeft ile duyurulur.
    /// ArenaClient tarafından AddComponent ile kurulur; sahne/oyun bilgisi içermez.
    /// </summary>
    public class RemotePlayerRegistry : MonoBehaviour
    {
        /// <summary>Bu süre boyunca snapshot'ta görünmeyen oyuncu ayrılmış sayılır (ms).</summary>
        private const int LEFT_TIMEOUT_MS = 1500;

        /// <summary>Oyuncu başına saklanan örnek sayısı (20 Hz'de ~3.2 sn geçmiş).</summary>
        private const int RING_SIZE = 64;

        public static RemotePlayerRegistry Instance { get; private set; }

        /// <summary>Ana thread'de, uzak oyuncu ilk kez görüldüğünde tetiklenir.</summary>
        public event Action<int> OnRemoteJoined;

        /// <summary>Ana thread'de, uzak oyuncu zaman aşımına uğrayınca/kopunca tetiklenir.</summary>
        public event Action<int> OnRemoteLeft;

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
        }

        // Ingest (ağ thread'i) ile örnekleme/Update (ana thread) bu kilit altında buluşur.
        private readonly object _gate = new object();
        private readonly Dictionary<int, RemoteEntry> _entries = new Dictionary<int, RemoteEntry>();

        // Olay yayınları kilit DIŞINDA yapılır; scratch listeleri GC üretmemek için alan.
        private readonly List<int> _joinedScratch = new List<int>();
        private readonly List<int> _leftScratch = new List<int>();

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
        /// </summary>
        public void IngestFromNetThread(Snapshot snap, int recvTickMs, int localPlayerId)
        {
            if (snap.players == null)
            {
                return;
            }

            lock (_gate)
            {
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

                    entry.nextIndex = (entry.nextIndex + 1) % RING_SIZE;
                    if (entry.count < RING_SIZE)
                    {
                        entry.count++;
                    }

                    entry.lastRecvMs = recvTickMs;
                }
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
