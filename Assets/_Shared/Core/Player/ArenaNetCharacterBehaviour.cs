using Meta.XR.Movement.Networking;
using Unity.Collections;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Movement SDK'nın ağ katmanı ile ArenaNet arasındaki <b>tek köprü</b>: SDK'nın ürettiği
    /// iskelet blob'unu <c>0x07</c> olarak yollar, ağdan gelen blob'u SDK'ya verir ve karakterin
    /// kökünü arena uzayına oturtur (§6.9/6.10).
    /// <para>
    /// SDK'nın <see cref="INetworkCharacterBehaviour"/>'ı <b>transport'tan bağımsızdır</b> —
    /// paketteki NGO/Photon uygulamaları yalnız birer örnektir ve bu projede kullanılmaz. Ne Meta
    /// hesabı, ne entitlement, ne internet gerekir: <c>MetaId</c> çekirdek tarafından hiç okunmaz
    /// (arayüz "0 if user not entitled" diyor) ve serileştirme yerel native eklentide yapılır.
    /// </para>
    /// <para>
    /// <b>Rol ayrımı tek yerden gelir:</b> <see cref="HasInputAuthority"/>. Yerel oyuncuda
    /// <c>true</c> → SDK gövdeyi sensörden çözer (<c>Owner = Host</c>) ve blob üretir; uzak
    /// oyuncuda/adminde <c>false</c> → SDK hiç body tracking koşturmaz, yalnız gelen blob'u uygular
    /// (<c>Owner = Client</c>). Yerel ve uzak gövdenin <b>aynı prefab + aynı retarget config</b>
    /// olmasının sebebi budur: fark kaynak, kod değil.
    /// </para>
    /// <para>
    /// ⚠️ <b>Kökü BU sınıf yazar, SDK değil.</b> Blob'un 0. eklemi gönderenin dünya uzayındadır
    /// (§6.9) ve blob opak olduğu için içeriden çevrilemez. Bu yüzden kök arena uzayında ayrıca
    /// taşınır ve burada <see cref="LateUpdate"/>'te — SDK'nın <c>ApplyBodyPose</c>'undan SONRA —
    /// karakter köküne yazılır. <c>LateUpdate</c> tüm <c>Update</c>'lerden sonra koştuğu için sıra
    /// execution order'dan bağımsız garantidir.
    /// </para>
    /// <para>
    /// ⚠️ <b>Karakter hiçbir şeyin altına PARENT'LANMAZ</b> (özellikle rig'in altına):
    /// <c>Docs/Sistem-Ozeti.md</c> §7, "Movement SDK retarget avatarı hareket eden kökün altına
    /// konmaz" — SDK kök eklemi <c>SetLocalPositionAndRotation</c> ile yazıyor, dolu bir ebeveyn
    /// dönüşümü ikinci kez uygulardı. Kökü zaten biz yazdığımız için ebeveyne de ihtiyaç yoktur.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(50)]
    [RequireComponent(typeof(NetworkCharacterHandler))]
    public class ArenaNetCharacterBehaviour : MonoBehaviour, INetworkCharacterBehaviour
    {
        /// <summary>
        /// SDK'nın <c>SendData</c>'sına verilen sahte alıcı kimliği. Gerçek bir istemci değildir:
        /// paket sunucuya gidip oradan herkese dağıtılıyor (§6.10), yani gönderenin tek bir
        /// "hedefi" var — relay'in kendisi.
        /// <para>⚠️ <see cref="LocalClientId"/>'den FARKLI olmak zorunda: SDK kendi kimliğine eşit
        /// olan alıcıyı atlıyor (<c>NetworkCharacterHandler.SendData</c>), eşit olsaydı hiç paket
        /// üretilmezdi. <c>playerId</c> bir <c>u8</c> olduğu için bu değere asla ulaşamaz.</para>
        /// </summary>
        private const ulong RelayClientId = ulong.MaxValue;

        /// <summary>
        /// Bu karakterin oyuncusu. Yerel gövdede <see cref="LocalBodyAvatar"/>, uzak gövdede
        /// <see cref="RemoteAvatar"/> atar.
        /// </summary>
        public int PlayerId { get; private set; }

        /// <inheritdoc cref="INetworkCharacterBehaviour.HasInputAuthority"/>
        public bool HasInputAuthority { get; private set; }

        /// <inheritdoc cref="INetworkCharacterBehaviour.CharacterPrefab"/>
        /// <remarks>Karakteri SDK doğurmuyor (<c>Setup(instantiateCharacter: false)</c>) — bu alan
        /// yalnız arayüz sözleşmesini doldurur.</remarks>
        public GameObject CharacterPrefab => gameObject;

        /// <inheritdoc cref="INetworkCharacterBehaviour.MetaId"/>
        /// <remarks>Çekirdek bunu hiç okumaz; entitlement/internet gerektiren tek yol NGO ve Fusion
        /// <i>spawner örnekleridir</i> ve onlar kullanılmıyor.</remarks>
        public ulong MetaId => 0;

        /// <inheritdoc cref="INetworkCharacterBehaviour.CharacterId"/>
        public int CharacterId => 0;

        /// <inheritdoc cref="INetworkCharacterBehaviour.LocalClientId"/>
        public ulong LocalClientId => (ulong)PlayerId;

        /// <inheritdoc cref="INetworkCharacterBehaviour.ClientIds"/>
        public ulong[] ClientIds => _relayTargets;

        /// <inheritdoc cref="INetworkCharacterBehaviour.DeltaTime"/>
        public float DeltaTime => Time.deltaTime;

        /// <summary>
        /// SDK'nın gönderdiği damganın ve alıcıdaki interpolasyonun ORTAK ekseni: sunucunun tik
        /// saati (<see cref="RemotePlayerRegistry.TryGetServerTimeSeconds"/>).
        /// <para>⚠️ <c>Time.unscaledTime</c> KULLANILAMAZ — makineye özeldir; iki uç farklı epoch'ta
        /// olur ve SDK'nın interpolasyonu ya uca kilitlenir ya hiç çalışmaz. Snapshot akışı henüz
        /// başlamadıysa (ilk saniye) yerel saate düşülür: o pencerede zaten çizilecek uzak gövde
        /// yoktur.</para>
        /// </summary>
        public float NetworkTime
        {
            get
            {
                RemotePlayerRegistry registry = RemotePlayerRegistry.Instance;
                if (registry != null && registry.TryGetServerTimeSeconds(out float seconds))
                {
                    return seconds;
                }

                return Time.unscaledTime;
            }
        }

        /// <summary>
        /// Çizim zamanı = ortak saat − <see cref="ArenaProtocol.INTERP_DELAY_MS"/>.
        /// <para>Tampon poz kanalınınkiyle <b>aynı</b>dır ve öyle kalmalı: farklı olsaydı gövde ile
        /// eller arasında sabit bir zaman kayması oluşurdu (silah elde, gövde bir tık geride).</para>
        /// </summary>
        public float RenderTime => NetworkTime - ArenaProtocol.INTERP_DELAY_MS / 1000f;

        private readonly ulong[] _relayTargets = { RelayClientId };

        private NetworkCharacterHandler _handler;
        private Transform _characterRoot;
        private bool _initialized;

        /// <summary>
        /// Sensör kaynağı (<c>MetaSourceDataProvider</c>). ⚠️ <b>Prefabdan SİLİNMEZ, yalnız
        /// kapatılır:</b> <c>CharacterRetargeter.Awake</c> sağlayıcıyı <b>kendi GameObject'inden</b>
        /// <c>GetComponent</c> ile arıyor ve yoksa assert atıyor — tek prefabın hem yerel hem uzak
        /// çalışabilmesi bileşenin orada DURMASINA bağlı.
        /// <para>Uzak gövdede kapatılmasının sebebi: her uzak avatar açık bir <c>OVRBody</c> demek
        /// olurdu ve hepsi aynı (yerel) sensörü okurdu — hem israf hem yanlış.</para>
        /// </summary>
        private Behaviour _sourceProvider;

        /// <summary>Gelen blob'u SDK'ya vermek için yeniden kullanılan native tampon — kare başına
        /// tahsis etmemek için alanda tutulur (SDK zaten kendi kopyasını alıyor).</summary>
        private NativeArray<byte> _receiveScratch;

        /// <summary>Giden blob'un yönetilen kopyası (SDK native, tel yönetilen bekliyor). Aynı
        /// gerekçe: kare başına tahsis etmemek için büyüdüğü yerde kalır.</summary>
        private byte[] _sendScratch;

        private bool _rootWarned;

        private void Awake()
        {
            _handler = GetComponent<NetworkCharacterHandler>();

            // Kök = SDK'nın 0. eklemi yazdığı transform, yani retargeter'ın kendi objesi.
            // ⚠️ Handler'ın property'sine GÜVENİLMEZ: onu kendi Awake'inde dolduruyor ve iki Awake'in
            // sırası garanti değil. Boş dönerse alt ağaçtan kendimiz buluruz — yanlış bir kök,
            // gövdenin arenada tümden yanlış yerde çizilmesi demektir.
            NetworkCharacterRetargeter retargeter = _handler != null
                ? _handler.NetworkCharacterRetargeter
                : null;

            if (retargeter == null)
            {
                retargeter = GetComponentInChildren<NetworkCharacterRetargeter>(true);
            }

            _characterRoot = retargeter != null ? retargeter.transform : transform;

            if (retargeter != null)
            {
                _sourceProvider = retargeter.GetComponent<Meta.XR.Movement.Retargeting.ISourceDataProvider>()
                    as Behaviour;
            }
        }

        private void OnDestroy()
        {
            if (_receiveScratch.IsCreated)
            {
                _receiveScratch.Dispose();
            }
        }

        /// <summary>
        /// Karakteri bir oyuncuya bağlar ve SDK'yı başlatır. <paramref name="hasInputAuthority"/>
        /// yerel gövdede <c>true</c>, uzak gövdede <c>false</c>'tur.
        /// <para>⚠️ <c>Setup(instantiateCharacter: false)</c> ile çağrılır: karakteri
        /// <see cref="RemoteAvatar"/>/<see cref="LocalBodyAvatar"/> zaten doğurdu.
        /// <c>INetworkCharacterSpawner</c>'a bu yüzden hiç ihtiyaç yok.</para>
        /// </summary>
        public void Initialize(int playerId, bool hasInputAuthority)
        {
            PlayerId = playerId;
            HasInputAuthority = hasInputAuthority;

            // Rol ayrımının TEK uygulandığı yer: sensör yalnız gövdenin sahibinde koşar.
            if (_sourceProvider != null)
            {
                _sourceProvider.enabled = hasInputAuthority;
            }

            if (_initialized)
            {
                return;
            }

            _initialized = true;
            _handler.Setup(false);
        }

        private void Update()
        {
            // Yalnız uzak gövde: ağdan gelen kareyi SDK'nın kuyruğuna koy.
            // ⚠️ Execution order 50 < handler'ın 100'ü: kare AYNI karede uygulanır, bir kare
            // gecikme oluşmaz.
            if (!_initialized || HasInputAuthority)
            {
                return;
            }

            RemoteSkeletonRegistry registry = RemoteSkeletonRegistry.Instance;
            if (registry == null || !registry.TryTakeBlob(PlayerId, out byte[] blob, out int length))
            {
                return;
            }

            EnsureScratch(length);
            NativeArray<byte>.Copy(blob, 0, _receiveScratch, 0, length);
            _handler.ReceiveData(_receiveScratch.GetSubArray(0, length));
        }

        private void LateUpdate()
        {
            if (!_initialized)
            {
                return;
            }

            if (HasInputAuthority)
            {
                // Yerel gövde: kök zaten sensörden geliyor (SDK yazdı), ağa bir şey yazmıyoruz —
                // gönderim SDK'nın kendi LateUpdate'inden ReceiveStreamData ile geliyor.
                return;
            }

            ApplyArenaRoot();
        }

        /// <summary>
        /// Karakter kökünü arena uzayındaki interpole edilmiş poza oturtur (§6.9).
        /// <para>⚠️ SDK'nın <c>ApplyBodyPose</c>'undan SONRA koşmak zorunda: o, 0. eklemi kök
        /// transformuna yazıp gönderenin dünya pozunu buraya taşıyor. <c>LateUpdate</c> tüm
        /// <c>Update</c>'lerden sonra çalıştığı için sıra garantidir.</para>
        /// <para>⚠️ <b>Ölçeğe DOKUNULMAZ:</b> boy blob'un 0. ekleminden geliyor ve SDK onu
        /// <c>localScale</c>'e yazıyor (oyuncunun gerçek boyu). Burada da yazmak iki doğruluk
        /// kaynağı olurdu.</para>
        /// </summary>
        private void ApplyArenaRoot()
        {
            RemoteSkeletonRegistry registry = RemoteSkeletonRegistry.Instance;
            if (registry == null || !registry.TryGetInterpolatedRoot(PlayerId, out Pose arenaRoot))
            {
                return;
            }

            Pose world = ArenaSpace.ArenaToWorld(arenaRoot);
            _characterRoot.SetPositionAndRotation(world.position, world.rotation);
        }

        /// <inheritdoc cref="INetworkCharacterBehaviour.ReceiveStreamData"/>
        /// <remarks>
        /// Adı yanıltıcı — bu, SDK'nın <b>gönderilecek</b> veriyi bize uzattığı kancadır
        /// (<c>NetworkCharacterHandler.SendData</c> çağırır). Buradan sonrası bizim telimizdir.
        /// <para>⚠️ Kök TAM BU ANDA okunur: SDK pozu bu karede çözdü ve karakterin kökü şu an
        /// gönderenin dünya pozunda. Bir kare sonra okumak gövde ile kökü ayırırdı.</para>
        /// </remarks>
        public void ReceiveStreamData(ulong clientId, bool isReliable, NativeArray<byte> bytes)
        {
            if (!bytes.IsCreated || bytes.Length == 0)
            {
                return;
            }

            ArenaClient client = ArenaClient.Instance;
            if (client == null || client.UdpChannel == null)
            {
                return;
            }

            if (!ArenaSpace.HasOrigin && !_rootWarned)
            {
                _rootWarned = true;
                Debug.LogWarning(
                    "[ArenaNetCharacterBehaviour] Sahnede arena origin'i (SpawnPoint) yok — iskelet " +
                    "kökü dünya uzayında gidiyor. Uzak oyuncular gövdeyi yanlış yerde çizer.", this);
            }

            Pose arenaRoot = ArenaSpace.WorldToArena(
                new Pose(_characterRoot.position, _characterRoot.rotation));

            // Native → yönetilen kopya: tel katmanı BinaryWriter ile yazıyor ve o yalnız byte[]
            // alıyor. Tampon büyüdüğü yerde kalır, yani ilk birkaç karenin dışında tahsis yok.
            byte[] managed = _sendScratch;
            if (managed == null || managed.Length < bytes.Length)
            {
                managed = new byte[Mathf.NextPowerOfTwo(bytes.Length)];
                _sendScratch = managed;
            }

            NativeArray<byte>.Copy(bytes, 0, managed, 0, bytes.Length);
            client.UdpChannel.SendSkeleton(managed, bytes.Length, arenaRoot);
        }

        /// <inheritdoc cref="INetworkCharacterBehaviour.ReceiveStreamAck"/>
        /// <remarks>
        /// ⚠️ <b>Bilerek boş.</b> Ack yalnız delta sıkıştırma içindir ve bu projede delta
        /// KAPALIDIR (§6.9): Meta'nın deltası <b>alıcı başına</b> baseline tutuyor
        /// (<c>NetworkCharacterHandler._clientsLastAck</c>), bizim topolojimiz ise sunucu-relay
        /// yayını — alıcı başına baseline, tik başına alıcı sayısı kadar serileştirme demek olurdu.
        /// Delta bir gün açılırsa bu metot da, <see cref="RemoteSkeletonRegistry"/>'nin tek kareli
        /// blob yuvası da kuyruğa çevrilmek zorundadır.
        /// </remarks>
        public void ReceiveStreamAck(ulong clientId, int ack)
        {
        }

        private void EnsureScratch(int length)
        {
            if (_receiveScratch.IsCreated && _receiveScratch.Length >= length)
            {
                return;
            }

            if (_receiveScratch.IsCreated)
            {
                _receiveScratch.Dispose();
            }

            _receiveScratch = new NativeArray<byte>(
                Mathf.NextPowerOfTwo(length), Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }
    }
}
