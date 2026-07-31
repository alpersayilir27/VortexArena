// ⚠️ `using System;` EKLENMEZ: bu dosya UnityEngine.Random kullanıyor, System de bir Random
// tipi taşıyor → ad çakışması. System.Environment tam adıyla çağrılır.
using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.Player;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Uzak oyuncuların atış sunumu: sunucunun 20 Hz batch'lediği olay kanalını
    /// (<c>NetEvents.OnRemoteFireEvent</c>, §6.4/6.5) dinler; namlu alevi + mekânsal ses oynatır
    /// ve <see cref="ShotTracer"/> ile mermi izini (çizgi + duman) çizer. Kendi atışımız buradan ASLA gelmez
    /// (kanal atanı süzer, §6.5); yerel atış FX'i Weapon/WeaponAudio'da kalır.
    /// <para>
    /// ⚠️ <b>Olayda namlu KONUMU yoktur</b> ve bu kasıtlıdır (§6.4 "Orijin neden gönderilmiyor"):
    /// tracer, alıcının ÇİZDİĞİ silahın namlusundan çıkmalıdır. Mutlak bir namlu konumu telde
    /// gelse alıcı silahı interpole edilmiş el pozundan çizdiği için tracer çizilen namludan
    /// kaymış başlardı — atıcının gerçeğine daha sadık, gözle daha bozuk. <b>Tutarlılık ></b>
    /// <b>sadakat.</b> Orijin bu yüzden yerelde çözülür: uzak avatarın o elde ÇİZDİĞİ eşya örneği
    /// (<c>RemoteAvatar.GetHeldItemVisual</c>) → onun <c>Muzzle</c> çocuğu → (yoksa) elin pozu →
    /// (yoksa) kafanın pozu.
    /// </para>
    /// <para>
    /// Sahnede DURMAZ: kendini önyükler ve DontDestroyOnLoad olur. FX düğümleri 8'lik round-robin
    /// havuzda tembel üretilir (<c>WeaponCatalog.RemoteShotFxPrefab</c> varsa o, yoksa sade
    /// AudioSource fallback'i); tracer havuzu <see cref="ShotTracer"/>'da. Admin gözlemci de bu
    /// olayları alır ve aynı sunumu görür — rol ayrımı YOKTUR.
    /// </para>
    /// </summary>
    public class RemoteShotFx : MonoBehaviour
    {
        /// <summary>Havuzdaki FX düğümü sayısı (aynı anda canlı kalabilecek atış efekti).</summary>
        private const int PoolSize = 8;

        /// <summary>Bu mesafeden (metre) uzak atışlarda ses çalınmaz, yalnız flaş kalır.</summary>
        private const float MaxAudibleDistanceMeters = 40f;

        /// <summary>Fallback AudioSource'un sönümlenme mesafesi.</summary>
        private const float FallbackMaxDistanceMeters = 60f;

        /// <summary>Atış başına yayılan parçacık sayısı.</summary>
        private const int ParticlesPerShot = 14;

        /// <summary>Eşya prefabında namlu ucunu tutan çocuğun adı (WeaponKitBuilder bunu kökte kurar).</summary>
        private const string MuzzleChildName = "Muzzle";

        /// <summary>Avatar dizininin yenilenme aralığı (sn) — sahne taraması bu sıklıktan hızlı yapılmaz.</summary>
        private const float AvatarScanIntervalSeconds = 0.5f;

        /// <summary>Oyuncu kaydı temizleme aralığı (sn) ve kaydın bayatlama süresi (sn).</summary>
        private const float PruneIntervalSeconds = 5f;
        private const float PlayerStaleSeconds = 10f;

        /// <summary>Oynatma sırası bekleyen olay tavanı; aşılırsa en eskisi HEMEN oynatılır (atılmaz).</summary>
        private const int MaxPendingEvents = 128;

        /// <summary>
        /// Bir olayın oynatılmak için bekleyebileceği en uzun süre (ms). Bozuk/atlamalı bir
        /// tik→zaman eşlemesi olayı süresiz bekletmesin diye tavan: INTERP_DELAY_MS'in iki
        /// katından fazlası, yani sağlıklı durumda hiç devreye girmez.
        /// </summary>
        private const int MaxPlaybackLeadMs = 250;

        public static RemoteShotFx Instance { get; private set; }

        /// <summary>Havuz düğümü; bileşenler üretim anında önbelleklenir (atış başına GetComponent yok).</summary>
        private sealed class FxNode
        {
            public Transform Root;
            public AudioSource Source;
            public ParticleSystem Particles;
        }

        /// <summary>
        /// Oyuncu başına sunum durumu: avatar + el başına çözülmüş namlu + tracer sayacı.
        /// Olay yolu 53-160/sn olduğu için her şey burada ÖNBELLEKLENİR (olay başına sahne
        /// taraması/GetComponentsInChildren kabul edilemez).
        /// </summary>
        private sealed class PlayerFx
        {
            public RemoteAvatar Avatar;

            public Transform MuzzleL;
            public Transform MuzzleR;

            // Namlu önbelleğinin ANAHTARI, eşya örneğinin kendisidir (RemoteAvatar.GetHeldItemVisual'ın
            // döndürdüğü kök). Eşya değişince referans da değişir → önbellek kendiliğinden geçersizleşir
            // ve bayat namlu asla kullanılmaz. Aramanın BAŞARISIZ olduğu durum da aynı anahtarla
            // önbelleklenir (Muzzle=null saklanır), yoksa her atışta boşuna hiyerarşi taraması olurdu.
            public Transform MuzzleSourceL;
            public Transform MuzzleSourceR;

            /// <summary>Bu oyuncu için "eşyada Muzzle yok" uyarısı bir kez basıldı mı.</summary>
            public bool MuzzleWarned;

            /// <summary>Bu oyuncudan gelen atış sayısı (tracerEveryNthRound sayacı).</summary>
            public int ShotCount;

            public float LastEventTime;
        }

        /// <summary>Oynatma zamanı gelmemiş olay (playAtMs = Environment.TickCount ekseni).</summary>
        private struct PendingShot
        {
            public RemoteFireEvent evt;
            public int playAtMs;
        }

        private readonly FxNode[] _pool = new FxNode[PoolSize];
        private int _nextNode;

        // ⚠️ YALNIZ ANA THREAD dokunur, kilit yok: NetEvents.OnRemoteFireEvent ana thread'de
        // yayınlanıyor (UdpStateChannel olayı ağ thread'inden _mainThreadActions kuyruğuyla
        // taşıyor), boşaltan da Update. Buraya ağ thread'inden yazan bir yol EKLENMEZ.
        private readonly List<PendingShot> _pending = new List<PendingShot>();

        private readonly Dictionary<int, PlayerFx> _players = new Dictionary<int, PlayerFx>();
        private readonly List<int> _pruneScratch = new List<int>();

        private float _nextAvatarScan;
        private float _nextPrune;

        // netItemId başına tek "katalogda yok" uyarısı (log taşmasın).
        private readonly HashSet<byte> _warnedItemIds = new HashSet<byte>();
        private bool _warnedNoPrefab;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var go = new GameObject("[RemoteShotFx]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<RemoteShotFx>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // İkinci kopya (sahneye elle konmuş olabilir) kendini yok eder.
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // Kalıcı tekiliz: OnEnable/OnDisable yerine Awake/OnDestroy'da abone oluruz,
            // böylece obje devre dışı bırakılsa bile sunucu olayları kaçmaz.
            NetEvents.OnRemoteFireEvent += HandleFireEvent;
            NetEvents.OnDisconnected += HandleDisconnected;
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            NetEvents.OnRemoteFireEvent -= HandleFireEvent;
            NetEvents.OnDisconnected -= HandleDisconnected;
            Instance = null;
        }

        /// <summary>
        /// Kopuşta bekleyen olaylar DÜŞÜRÜLÜR: yeniden bağlanınca eski oturumun tracer'ları
        /// oynamamalı ve yeni oturumda sunucunun tik ekseni sıfırdan başlayabileceği için
        /// hesaplanmış oynatma zamanları da anlamsızdır.
        /// </summary>
        private void HandleDisconnected()
        {
            _pending.Clear();
        }

        /// <summary>
        /// Vadesi gelen atış olaylarını oynatır (sıra korunarak, tek geçişte sıkıştırarak).
        /// </summary>
        private void Update()
        {
            int count = _pending.Count;
            if (count == 0)
            {
                return;
            }

            // TickCount farkı int çıkarma ile — ~24.9 günlük sarmalamaya dayanıklı
            // (RemotePlayerRegistry.Update aynı deseni kullanıyor).
            int now = System.Environment.TickCount;

            int write = 0;
            for (int i = 0; i < count; i++)
            {
                PendingShot shot = _pending[i];
                if (now - shot.playAtMs >= 0)
                {
                    PlayFireEvent(shot.evt);
                    continue;
                }

                if (write != i)
                {
                    _pending[write] = shot;
                }

                write++;
            }

            _pending.RemoveRange(write, count - write);
        }

        /// <summary>
        /// Gelen atış olayının ZAMANLAMASI (§6.5): ucuz redler burada yapılır, sonra olay kendi
        /// <c>serverTick</c>'inin oynatma zamanına kadar bekletilir.
        /// <para>
        /// Olay, kendi <c>serverTick</c>'inde <b>alıcının interpolasyon saatiyle</b> oynatılır
        /// (<c>RemotePlayerRegistry.TryGetPlaybackTimeMs</c>). Sebebi: uzak pozlar bilerek
        /// <c>INTERP_DELAY_MS</c> geriden çizilir, ama sunucu snapshot'ı (0x02) ile olay batch'ini
        /// (0x04) AYNI tik'te yayınlar → olay geldiği anda oynatılsa alev/ses/tracer elin
        /// <b>100 ms öncesindeki</b> konumundan çıkardı (kol 2 m/s ise ~20 cm kayma). Doğrusu,
        /// avatarın eli o tik'e ULAŞANA kadar beklemektir.
        /// </para>
        /// <para>
        /// Bu yüzden 20 Hz batch'lemesi algılanan gecikmeye <b>EKLENMEZ</b>: ≤50 ms'lik batch
        /// beklemesi 100 ms'lik interp tamponunun içinde erir.
        /// </para>
        /// <para>
        /// Eşleme yoksa (henüz snapshot gelmemiş ya da tik halkadan düşmüş kadar eski) olay
        /// <b>hemen</b> oynatılır: geciken tracer kabul edilebilir, kaybolan tracer edilemez.
        /// </para>
        /// <para>
        /// ⚠️ Geçmiş pozu örneklemeye GEREK YOKTUR ve o kapı bilerek açılmadı: orijin telden gelen
        /// bir konum değil, o karede ÇİZİLMİŞ silahın namlusudur (§6.4 "tutarlılık > sadakat").
        /// Olay doğru anda oynayınca çizili namlu zaten o tik'in namlusudur.
        /// </para>
        /// </summary>
        private void HandleFireEvent(RemoteFireEvent evt)
        {
            // Ucuz redler kuyruktan ÖNCE: oynatılmayacak olay hiç beklemeye girmesin.
            // KIND_THROW (fırlatılan cismin sunumu) Faz 4'ün işi — burada sessizce atlanır.
            if (evt.kind != FireEventEntry.KIND_SHOT)
            {
                return;
            }

            if (evt.arenaDirection.sqrMagnitude < 1e-6f)
            {
                return;
            }

            int now = System.Environment.TickCount;

            RemotePlayerRegistry registry = RemotePlayerRegistry.Instance;
            if (registry == null || !registry.TryGetPlaybackTimeMs(evt.serverTick, out int playAtMs) ||
                now - playAtMs >= 0)
            {
                // Eşleme yok ya da oynatma zamanı çoktan geçmiş → beklemenin anlamı kalmadı.
                PlayFireEvent(evt);
                return;
            }

            if (playAtMs - now > MaxPlaybackLeadMs)
            {
                playAtMs = now + MaxPlaybackLeadMs;
            }

            if (_pending.Count >= MaxPendingEvents)
            {
                // Tavan: en eskisini oynat ve çıkar. Sessizce ATILMAZ — kaybolan tracer teşhis
                // edilemez, 100 ms erken oynatılan tracer görünür ve zararsızdır.
                PendingShot oldest = _pending[0];
                _pending.RemoveAt(0);
                PlayFireEvent(oldest.evt);
            }

            _pending.Add(new PendingShot { evt = evt, playAtMs = playAtMs });
        }

        /// <summary>
        /// Tek uzak atış olayının SUNUMU (§6.5): orijin çözümü, namlu alevi, mekânsal ses, tracer.
        /// Zamanlama <see cref="HandleFireEvent"/>'in işidir — buraya vadesi gelmiş olay gelir.
        /// </summary>
        private void PlayFireEvent(in RemoteFireEvent evt)
        {
            Vector3 worldDir = ArenaToWorldDirection(evt.arenaDirection);
            if (worldDir.sqrMagnitude < 1e-6f)
            {
                return;
            }

            worldDir = worldDir.normalized;

            float now = Time.unscaledTime;
            PlayerFx fx = GetOrCreatePlayer(evt.playerId);
            fx.LastEventTime = now;
            fx.ShotCount++;

            ItemDefinition item = ResolveItem(evt.itemId);

            // Geri tepme, orijin çözümünden ÖNCE tetiklenir: aşağıdaki erken çıkış (hiç poz yok)
            // sunumun görsel kalanını düşürür ama silahın sarsılmaması için bir sebep değildir.
            // Zamanlama zaten doğru — olay kendi serverTick'inin interpolasyon anında buraya
            // ulaşıyor (HandleFireEvent bekletir), yani kick avatarın eli o tik'e vardığında başlar.
            // Silah OLMAYAN eşyada (bomba vb.) desen tutmaz: geri tepme diye bir şey yoktur.
            if (item is WeaponDefinition recoilWeapon)
            {
                RemoteAvatar avatar = ResolveAvatar(fx, evt.playerId, now);
                if (avatar != null)
                {
                    avatar.ApplyShotRecoil(evt.rightHand, recoilWeapon);
                }
            }

            if (!TryResolveOrigin(fx, evt, now, out Vector3 origin))
            {
                // Ne namlu, ne el, ne kafa: oyuncunun hiç pozu yok (henüz snapshot gelmemiş) →
                // efektin nereye konacağı bilinmiyor, olay sessizce düşer.
                return;
            }

            WeaponCatalog catalog = WeaponCatalog.Load();
            FxNode node = TakeNode(catalog);
            if (node != null && node.Root != null)
            {
                node.Root.SetPositionAndRotation(origin, Quaternion.LookRotation(worldDir));

                if (node.Particles != null)
                {
                    node.Particles.Emit(ParticlesPerShot);
                }

                // Ses/alev profili silaha özgüdür; eşya bir silah DEĞİLSE (bomba vb.) yalnız atlanır
                // — tracer aşağıda yine çizilir.
                PlayShotSound(node, item as WeaponDefinition, origin);
            }

            DrawTracer(fx, item, origin, worldDir, evt.magnitude);
            PrunePlayers(now);
        }

        // --------------------------------------------------------------------- tracer

        /// <summary>
        /// Mermi izini (çizgi + yol boyunca duman) çizer. <c>magnitude</c> KIND_SHOT'ta vuruş
        /// MESAFESİDİR (metre), bu yüzden bitiş noktası <c>origin + yön × mesafe</c>'dir (§6.4).
        /// <para>⚠️ Her mermiye çizilmez: <c>TracerEveryNthRound</c> oyuncu BAŞINA sayaçla
        /// uygulanır — sayaç paylaşılsa yoğun ateşte tracer'lar rastgele oyunculara dağılır ve
        /// "kim ateş ediyor" okunaksız olurdu.</para>
        /// </summary>
        private void DrawTracer(PlayerFx fx, ItemDefinition item, Vector3 origin, Vector3 worldDir, float distanceMeters)
        {
            if (item == null)
            {
                // Eşya çözülemedi → görünüm parametresi de yok. Uydurma bir tracer çizmek
                // yanlış kalibre/renk demektir; flaş+ses yeterli.
                return;
            }

            int everyNth = item.TracerEveryNthRound;
            if (everyNth < 1 || fx.ShotCount % everyNth != 0)
            {
                return;
            }

            // Havuz atanın kendi iziyle PAYLAŞILIR (ShotTracer.Shared): yerel ve uzak izlerin
            // görünümü zaten aynı kaynaktan (ItemDefinition) geliyor, havuzu da bölmenin sebebi yok.
            // Duman izi de bu tek çağrının içinde yayılır — ayrı bir adım YOKTUR (ShotTracer sınıf
            // özeti: iki çağırandan birinin dumanı unutması böylece imkânsız).
            ShotTracer.Shared.Play(
                origin,
                origin + worldDir * distanceMeters,
                item.TracerColor,
                item.TracerWidth,
                item.TracerLifetime);
        }

        // --------------------------------------------------------------------- orijin

        /// <summary>
        /// Efektin/tracer'ın başlangıç noktası (§6.4): çizilen silahın namlusu → elin pozu →
        /// kafanın pozu. Hiçbiri yoksa false.
        /// </summary>
        private bool TryResolveOrigin(PlayerFx fx, in RemoteFireEvent evt, float now, out Vector3 origin)
        {
            origin = default;

            RemotePlayerRegistry registry = RemotePlayerRegistry.Instance;

            // out değişkenleri ÖNCE ilan edilir: çağrı kısa devre yaparsa (registry yok) derleyici
            // onları "atanmamış" sayar ve aşağıdaki kullanım derlenmezdi.
            Pose headArena = Pose.identity;
            Pose handLArena = Pose.identity;
            Pose handRArena = Pose.identity;
            bool hasPose = registry != null &&
                           registry.GetInterpolatedPose(evt.playerId, out headArena, out handLArena, out handRArena);

            Vector3 handWorld = default;
            bool hasHand = false;
            if (hasPose)
            {
                Pose handArena = evt.rightHand ? handRArena : handLArena;
                // Tam sıfır = hiç doldurulmamış poz (interpolasyon kimlik döndürmüş); elin arena
                // uzayında gerçekten sıfırda olması pratikte imkânsız (zemin merkezi).
                if (handArena.position.sqrMagnitude > 1e-6f)
                {
                    handWorld = ArenaSpace.ArenaToWorld(handArena.position);
                    hasHand = true;
                }
            }

            Transform muzzle = ResolveMuzzle(fx, evt, now);
            if (muzzle != null)
            {
                origin = muzzle.position;
                return true;
            }

            if (hasHand)
            {
                origin = handWorld;
                return true;
            }

            if (hasPose)
            {
                origin = ArenaSpace.ArenaToWorld(headArena.position);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Olayın elinde ÇİZİLMİŞ eşya örneğinin <c>Muzzle</c> çocuğunu bulur (el başına
        /// önbelleklenir; örnek değişince kendiliğinden yenilenir).
        /// <para>
        /// Eşya örneği <b>sözleşmeli API'den</b> gelir (<see cref="RemoteAvatar.GetHeldItemVisual"/>),
        /// sahne aramasından DEĞİL: tracer'ın <b>çizilen</b> namludan çıkması (§6.4 "tutarlılık >
        /// sadakat") ancak alıcının o eli için gerçekten Instantiate ettiği örnek sorulursa garanti
        /// olur. Çift tabancada doğru örneği <c>rightHand</c> seçer; çift elli tutuşta (GRIP_LINKED)
        /// tek örnek vardır ve hangi el sorulursa o döner.
        /// </para>
        /// <para>
        /// ⚠️ Namlunun kendisi hâlâ ADA göre aranıyor (kapsam tek eşya örneği) — prefabda
        /// <c>Muzzle</c> yeniden adlandırılırsa sunum sessizce el pozuna düşer. Bu yüzden
        /// bulunamadığında oyuncu başına <b>tek</b> uyarı basılır (olay yolu 53-160/sn, spam yasak).
        /// </para>
        /// </summary>
        private Transform ResolveMuzzle(PlayerFx fx, in RemoteFireEvent evt, float now)
        {
            RemoteAvatar avatar = ResolveAvatar(fx, evt.playerId, now);
            if (avatar == null)
            {
                return null;
            }

            Transform itemVisual = avatar.GetHeldItemVisual(evt.rightHand);
            if (itemVisual == null)
            {
                return null; // o elde eşya çizilmemiş → el pozuna düşülür
            }

            // Önbellek anahtarı örneğin KENDİSİ: aynı örnek → aynı namlu (arama tekrarlanmaz),
            // farklı örnek → eşya değişmiş, yeniden aranır.
            Transform source = evt.rightHand ? fx.MuzzleSourceR : fx.MuzzleSourceL;
            if (source == itemVisual)
            {
                return evt.rightHand ? fx.MuzzleR : fx.MuzzleL;
            }

            Transform found = FindMuzzle(itemVisual);

            if (evt.rightHand)
            {
                fx.MuzzleSourceR = itemVisual;
                fx.MuzzleR = found;
            }
            else
            {
                fx.MuzzleSourceL = itemVisual;
                fx.MuzzleL = found;
            }

            if (found == null && !fx.MuzzleWarned)
            {
                fx.MuzzleWarned = true;
                Debug.LogWarning(
                    $"[RemoteShotFx] Oyuncu {evt.playerId}: elindeki '{itemVisual.name}' örneğinde " +
                    $"'{MuzzleChildName}' çocuğu yok — atış efekti/tracer el pozundan çıkacak.");
            }

            return found;
        }

        // GetComponentsInChildren KULLANILMAZ: dizi ayırır. Elle özyineleme allocation'sız
        // (yalnız Transform.name bir string üretir — bu arama eşya değişiminde bir kez koşar).
        // Kapsam tek eşya örneği olduğu için İLK eşleşme alınır: eşyanın bir namlusu vardır.
        private static Transform FindMuzzle(Transform parent)
        {
            int count = parent.childCount;
            for (int i = 0; i < count; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == MuzzleChildName)
                {
                    return child;
                }

                Transform found = FindMuzzle(child);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// <c>playerId</c> → <see cref="RemoteAvatar"/>. Avatarların statik bir kaydı YOK ve onları
        /// üreten <c>RemotePlayerSpawner</c> App katmanındadır (Core onu göremez), bu yüzden dizin
        /// sahne taramasıyla kurulur — tarama <see cref="AvatarScanIntervalSeconds"/>'ten sık
        /// yapılmaz (FindObjectsByType dizi ayırır, olay yolu 53-160/sn).
        /// </summary>
        private RemoteAvatar ResolveAvatar(PlayerFx fx, int playerId, float now)
        {
            if (fx.Avatar != null)
            {
                return fx.Avatar;
            }

            if (now < _nextAvatarScan)
            {
                return null;
            }

            _nextAvatarScan = now + AvatarScanIntervalSeconds;

            RemoteAvatar[] avatars = FindObjectsByType<RemoteAvatar>(FindObjectsSortMode.None);
            for (int i = 0; i < avatars.Length; i++)
            {
                RemoteAvatar avatar = avatars[i];
                if (avatar == null)
                {
                    continue;
                }

                PlayerFx target = GetOrCreatePlayer(avatar.PlayerId);
                if (target.Avatar != avatar)
                {
                    target.Avatar = avatar;
                    // Yeni avatar = yeni hiyerarşi: namlu önbelleği geçersiz. (Anahtar eşya örneği
                    // olduğu için zaten kendiliğinden düşerdi; yine de açıkça temizlenir.)
                    target.MuzzleL = null;
                    target.MuzzleR = null;
                    target.MuzzleSourceL = null;
                    target.MuzzleSourceR = null;
                }
            }

            return fx.Avatar;
        }

        // ------------------------------------------------------------------ oyuncu kaydı

        private PlayerFx GetOrCreatePlayer(int playerId)
        {
            if (!_players.TryGetValue(playerId, out PlayerFx fx))
            {
                fx = new PlayerFx();
                _players.Add(playerId, fx);
            }

            return fx;
        }

        /// <summary>
        /// Ayrılan oyuncuların kaydını (tracer sayacı dahil) düşürür: avatarı yok edilmiş ve bir
        /// süredir olay göndermeyen girişler silinir. Registry'nin <c>OnRemoteLeft</c>'ine abone
        /// olmak yerine bunun tercih edilme sebebi, sunumun oyuncu YAŞAM DÖNGÜSÜNE bağlanmaması —
        /// avatar hiç üretilmemiş olsa da (spawner yok/prefab boş) sayaç yine temizlenir.
        /// </summary>
        private void PrunePlayers(float now)
        {
            if (now < _nextPrune)
            {
                return;
            }

            _nextPrune = now + PruneIntervalSeconds;
            _pruneScratch.Clear();

            foreach (KeyValuePair<int, PlayerFx> kv in _players)
            {
                if (kv.Value.Avatar == null && now - kv.Value.LastEventTime > PlayerStaleSeconds)
                {
                    _pruneScratch.Add(kv.Key);
                }
            }

            for (int i = 0; i < _pruneScratch.Count; i++)
            {
                _players.Remove(_pruneScratch[i]);
            }
        }

        // ------------------------------------------------------------------- yardımcılar

        /// <summary>
        /// Arena uzayındaki bir YÖNÜ dünyaya çevirir.
        /// <para>⚠️ <b>Yön bir NOKTA değildir:</b> <c>ArenaToWorld(dir)</c> yönü origin kadar
        /// öteler. Doğru dönüşüm iki dünya noktasının farkıdır (origin dönük/ötelenmişse de
        /// doğru kalır). <c>ArenaSpace</c>'te bir yön yardımcısı olduğunda burası ona devredilir.</para>
        /// </summary>
        private static Vector3 ArenaToWorldDirection(Vector3 arenaDirection)
        {
            return ArenaSpace.ArenaToWorld(arenaDirection) - ArenaSpace.ArenaToWorld(Vector3.zero);
        }

        /// <summary>
        /// Olayın <c>itemId</c>'sini (§6.6) eşya tanımına çözer. Sunum profilinin tek kaynağı bu
        /// bayttır — snapshot'taki <c>itemL/itemR</c> durum baytları kaybolsa da olay kendi
        /// kendine yeter (§6.4).
        /// </summary>
        private ItemDefinition ResolveItem(byte itemId)
        {
            NetItemCatalog catalog = NetItemCatalog.Load();
            ItemDefinition def = catalog != null ? catalog.FindByNetItemId(itemId) : null;
            if (def == null)
            {
                WarnUnknownItem(itemId);
            }

            return def;
        }

        /// <summary>def yoksa (silah olmayan/katalog dışı eşya) veya dinleyici yok/uzaksa yalnız flaş kalır.</summary>
        private static void PlayShotSound(FxNode node, WeaponDefinition def, Vector3 worldPos)
        {
            if (def == null || node.Source == null || def.FireClips == null || def.FireClips.Length == 0)
            {
                return;
            }

            Camera listener = Camera.main;
            if (listener == null)
            {
                return;
            }

            if ((listener.transform.position - worldPos).sqrMagnitude >
                MaxAudibleDistanceMeters * MaxAudibleDistanceMeters)
            {
                return;
            }

            AudioClip clip = def.FireClips[Random.Range(0, def.FireClips.Length)];
            if (clip == null)
            {
                return;
            }

            node.Source.pitch = def.FirePitchBase + Random.Range(-def.FirePitchJitter, def.FirePitchJitter);
            node.Source.PlayOneShot(clip, def.FireVolume);
        }

        /// <summary>Round-robin: sıradaki (en eski) düğümü döndürür; henüz yoksa tembel üretir.</summary>
        private FxNode TakeNode(WeaponCatalog catalog)
        {
            FxNode node = _pool[_nextNode];
            if (node == null || node.Root == null)
            {
                node = CreateNode(catalog);
                _pool[_nextNode] = node;
            }

            _nextNode = (_nextNode + 1) % PoolSize;
            return node;
        }

        private FxNode CreateNode(WeaponCatalog catalog)
        {
            GameObject prefab = catalog != null ? catalog.RemoteShotFxPrefab : null;
            GameObject go;

            if (prefab != null)
            {
                // DDOL kökümüzün altında yaşar — sahne geçişinde havuz yok olmaz.
                go = Instantiate(prefab, transform);
            }
            else
            {
                if (!_warnedNoPrefab)
                {
                    _warnedNoPrefab = true;
                    Debug.LogWarning(
                        "[RemoteShotFx] WeaponCatalog.RemoteShotFxPrefab atanmadı — parçacıksız sade ses düğümü kullanılacak.");
                }

                go = new GameObject("[RemoteShotFxNode]");
                go.transform.SetParent(transform, false);

                var source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.maxDistance = FallbackMaxDistanceMeters;
            }

            return new FxNode
            {
                Root = go.transform,
                Source = go.GetComponentInChildren<AudioSource>(true),
                Particles = go.GetComponentInChildren<ParticleSystem>(true),
            };
        }

        private void WarnUnknownItem(byte itemId)
        {
            if (!_warnedItemIds.Add(itemId))
            {
                return;
            }

            Debug.LogWarning(
                $"[RemoteShotFx] netItemId {itemId} NetItemCatalog'da yok — atış yalnız flaş olarak " +
                "oynatılır (ses ve tracer atlanır).");
        }
    }
}
