using System;
using System.Collections.Generic;
using UnityEngine;
using VortexArena.Protocol;

namespace VortexArena.Net
{
    /// <summary>
    /// Uzak oyuncu iskelet kayıtçısı tekili (§6.10): <see cref="UdpStateChannel"/>'ın ağ thread'inde
    /// aldığı <c>0x08</c> girdilerini oyuncu başına saklar, ana thread'den okutur.
    /// <see cref="RemotePlayerRegistry"/> ile aynı desen — ArenaClient AddComponent ile kurar,
    /// sahne/oyun bilgisi içermez.
    /// <para>
    /// <b>İki alanın ömrü BİLEREK farklıdır:</b>
    /// </para>
    /// <list type="bullet">
    /// <item><b>Blob TÜKETİLİR</b> (<see cref="TryTakeBlob"/>): SDK'nın <c>ReceiveData</c>'sı gelen
    /// her kareyi kendi kuyruğuna alıyor; aynı blob'u ikinci kez vermek aynı kareyi iki kez
    /// oynatmak olurdu.</item>
    /// <item><b>Kök KALICIDIR</b> (<see cref="TryGetInterpolatedRoot"/>): karakterin kökü
    /// <b>her karede</b> yazılmak zorunda (SDK <c>ApplyBodyPose</c> ile onu sürekli eziyor), oysa
    /// yeni blob yalnız <see cref="ArenaProtocol.SKELETON_RATE_HZ"/>'de geliyor.</item>
    /// </list>
    /// <para>
    /// ⚠️ <b>Kök İNTERPOLE EDİLİR</b> ve bu isteğe bağlı bir güzelleştirme değildir: gövde
    /// 12 Hz'de, eller <see cref="ArenaProtocol.POSE_RATE_HZ"/>'de akıyor. Kök ham yazılsaydı
    /// gövde 12 Hz'lik basamaklarla, eller akıcı hareket ederdi — aynı avatarda iki farklı
    /// akıcılık, kopmuş gibi görünür. Tampon <see cref="ArenaProtocol.INTERP_DELAY_MS"/> ile
    /// poz kanalınınkiyle <b>aynı</b>dır; farklı olsaydı gövde ile eller arasında sabit bir
    /// zaman kayması kalırdı.
    /// </para>
    /// <para>
    /// ⚠️ <b>Blob yuvası tek kareliktir (son gelen kazanır).</b> Bu, SDK tarafında <b>delta
    /// sıkıştırmanın KAPALI</b> olmasına dayanır (§6.9): her kare bağımsız bir keyframe olduğu
    /// için düşen kare yalnız o kareyi kaybettirir. Delta açılırsa bu yuva kuyruğa çevrilmek
    /// zorundadır — atlanan bir baseline sonraki tüm kareleri çözümsüz bırakır.
    /// </para>
    /// </summary>
    public class RemoteSkeletonRegistry : MonoBehaviour
    {
        /// <summary>
        /// Oyuncu başına saklanan kök örneği sayısı. <see cref="ArenaProtocol.SKELETON_RATE_HZ"/>'de
        /// ~1.3 sn geçmiş — interpolasyon tamponunun (100 ms) kat kat üstünde, jitter/kayıp
        /// yutulabilsin diye.
        /// </summary>
        private const int RING_SIZE = 16;

        public static RemoteSkeletonRegistry Instance { get; private set; }

        /// <summary>Tek kök örneği (recvMs = <c>Environment.TickCount</c>).</summary>
        private struct RootSample
        {
            public int recvMs;
            public PoseData root;
        }

        private class SkeletonEntryState
        {
            /// <summary>Henüz tüketilmemiş blob; <c>null</c> = yeni kare yok.</summary>
            public byte[] pendingBlob;
            public int pendingLength;

            public readonly RootSample[] ring = new RootSample[RING_SIZE];
            public int count;
            public int nextIndex;
        }

        // Ingest (ağ thread'i) ile okuma (ana thread) bu kilit altında buluşur.
        private readonly object _gate = new object();
        private readonly Dictionary<int, SkeletonEntryState> _entries = new Dictionary<int, SkeletonEntryState>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[RemoteSkeletonRegistry] İkinci örnek yok edildi (tekil).");
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

        private void HandleDisconnected()
        {
            lock (_gate)
            {
                _entries.Clear();
            }
        }

        /// <summary>
        /// AĞ THREAD'İ: <c>0x08</c> batch'inin tek girdisini alır.
        /// <para>⚠️ Kendi <paramref name="localPlayerId"/>'miz atlanır — kendi gövdemizi sensörden
        /// çiziyoruz (§6.10: sunucu gönderene de yolluyor, süzgeç burada).</para>
        /// </summary>
        public void IngestFromNetThread(in SkeletonEntry entry, int recvTickMs, int localPlayerId)
        {
            if (entry.playerId == localPlayerId || entry.blobLength <= 0)
            {
                return;
            }

            lock (_gate)
            {
                if (!_entries.TryGetValue(entry.playerId, out SkeletonEntryState state))
                {
                    state = new SkeletonEntryState();
                    _entries.Add(entry.playerId, state);
                }

                // Son gelen kazanır (bkz. sınıf özeti — delta KAPALI olduğu için güvenli).
                state.pendingBlob = entry.blob;
                state.pendingLength = entry.blobLength;

                state.ring[state.nextIndex] = new RootSample { recvMs = recvTickMs, root = entry.root };
                state.nextIndex = (state.nextIndex + 1) % RING_SIZE;
                if (state.count < RING_SIZE)
                {
                    state.count++;
                }
            }
        }

        /// <summary>
        /// ANA THREAD: bekleyen blob'u verir ve yuvayı <b>boşaltır</b>. Yeni kare yoksa false.
        /// </summary>
        public bool TryTakeBlob(int playerId, out byte[] blob, out int length)
        {
            lock (_gate)
            {
                if (_entries.TryGetValue(playerId, out SkeletonEntryState state) && state.pendingBlob != null)
                {
                    blob = state.pendingBlob;
                    length = state.pendingLength;
                    state.pendingBlob = null;
                    state.pendingLength = 0;
                    return true;
                }
            }

            blob = null;
            length = 0;
            return false;
        }

        /// <summary>
        /// ANA THREAD: karakter kökünün <see cref="ArenaProtocol.INTERP_DELAY_MS"/> gecikmeli,
        /// interpole edilmiş <b>arena uzayı</b> pozu. Saran çift yoksa en yakın uca kilitlenir;
        /// hiç örnek yoksa false.
        /// <para>Poz kanalının <c>GetInterpolatedPose</c>'u ile aynı örnekleme zamanını kullanır —
        /// gövde ile eller arasında zaman kayması kalmasın diye.</para>
        /// </summary>
        public bool TryGetInterpolatedRoot(int playerId, out Pose root)
        {
            root = Pose.identity;

            int renderMs = Environment.TickCount - ArenaProtocol.INTERP_DELAY_MS;

            RootSample before = default;
            RootSample after = default;
            bool hasBefore = false;
            bool hasAfter = false;

            lock (_gate)
            {
                if (!_entries.TryGetValue(playerId, out SkeletonEntryState state) || state.count == 0)
                {
                    return false;
                }

                int start = state.nextIndex - state.count;
                if (start < 0)
                {
                    start += RING_SIZE;
                }

                // Halka kronolojik sıralı: örnekleme zamanını saran çifti bul.
                for (int i = 0; i < state.count; i++)
                {
                    RootSample sample = state.ring[(start + i) % RING_SIZE];
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

            if (!hasBefore && !hasAfter)
            {
                return false;
            }

            if (!hasBefore)
            {
                root = ToPose(after.root);
                return true;
            }

            if (!hasAfter)
            {
                root = ToPose(before.root);
                return true;
            }

            int span = after.recvMs - before.recvMs;
            float t = span > 0 ? Mathf.Clamp01((renderMs - before.recvMs) / (float)span) : 0f;

            Pose a = ToPose(before.root);
            Pose b = ToPose(after.root);
            root = new Pose(
                Vector3.Lerp(a.position, b.position, t),
                Quaternion.Slerp(a.rotation, b.rotation, t));
            return true;
        }

        /// <summary>Avatar yok edilirken/başka oyuncuya devredilirken çağrılır: bayat kök ve blob
        /// yeni sahibe miras kalmasın.</summary>
        public void Forget(int playerId)
        {
            lock (_gate)
            {
                _entries.Remove(playerId);
            }
        }

        private static Pose ToPose(in PoseData d)
        {
            return new Pose(
                new Vector3(d.px, d.py, d.pz),
                new Quaternion(d.qx, d.qy, d.qz, d.qw));
        }
    }
}
