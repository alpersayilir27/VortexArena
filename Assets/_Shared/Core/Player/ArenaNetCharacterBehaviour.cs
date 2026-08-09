using Meta.XR.Movement;
using Meta.XR.Movement.Networking;
using Meta.XR.Movement.Retargeting;
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

        /// <summary>Emniyet silahlıyken bir gönderimde kabul edilen en büyük kök sıçraması (metre).
        /// Free-roam'da 20 Hz'de bu kadar yol alınmaz; bunun üstü bir ölçüm değil çöküştür.</summary>
        private const float RootJumpLimitMeters = 1.5f;

        /// <summary>Kök en fazla kaç gönderim tutulur (≈2 sn @ 20 Hz). ⚠️ Tavan olmadan tek bir
        /// yanlış ölçüm gövdeyi eski yerine SONSUZA DEK çivilerdi — oyuncu gerçekten oraya gitmiş
        /// olabilir ve bu emniyet bir konum otoritesi değildir.</summary>
        private const int RootHoldMaxSends = 24;

        /// <summary>SDK kökü ile HMD'nin zemin izdüşümü arasında kabul edilen en büyük YATAY mesafe
        /// (metre, arena uzayı). Karakter kökü kalça altı/zemin hizasındadır ve oyuncunun kafasının
        /// altında durur: eğilen, yatan, koşan oyuncuda bile bu kadar uzağa meşru biçimde düşmez.
        /// Üstü bir duruş değil, çözümün kaçtığının işaretidir.</summary>
        private const float SuspectHorizontalMeters = 1.5f;

        /// <summary>Arena uzayında kökün kabul edilen |y| tavanı (metre). Arena sözleşmesi zemini
        /// y=0'a sabitliyor (<see cref="ArenaSpace"/>) — kök bu kadar yükselip alçalıyorsa zemin
        /// tahmini kaymıştır (çok katlı mekân, bayat izleme haritası).</summary>
        private const float SuspectRootHeightMeters = 1.0f;

        /// <summary>SDK bu kadar süredir hiç kare üretmiyorsa akış BAYAT sayılır (sn). Kaynak
        /// susunca uzak avatar son karesinde donar ve arıza hiçbir yerde görünmez; yedek onu
        /// görünür bir belirtiye çevirir.</summary>
        private const float StaleFrameSeconds = 1.0f;

        /// <summary>Histerezis: SDK yoluna dönmek için gereken ARDIŞIK temiz kare sayısı
        /// (≈1 sn @ 12 Hz). ⚠️ Tek temiz kareyle dönmek iki yolu kare kare değiştirirdi — uzak
        /// tarafta gövde T-poz ile bozuk poz arasında titrerdi.</summary>
        private const int RecoverStreakFrames = 12;

        /// <summary>
        /// Bu karakterin oyuncusu. Yerel gövdede <see cref="LocalBodyAvatar"/>, uzak gövdede
        /// <see cref="RemoteAvatar"/> atar.
        /// </summary>
        public int PlayerId { get; private set; }

        /// <inheritdoc cref="INetworkCharacterBehaviour.HasInputAuthority"/>
        public bool HasInputAuthority { get; private set; }

        /// <summary>
        /// Bu karakterin üniform gövde ölçeği (§10.8) — <c>1</c> = ölçüsüz. Yazarı
        /// <see cref="RemoteAvatar"/>'dır (roster'dan gelir).
        /// <para>⚠️ <b>Yalnız UZAK gövdede anlamlıdır ve yalnız orada uygulanır</b>
        /// (<see cref="ApplyArenaRoot"/>). Yerel gövdede yazılsa gönderenin kendi ölçüm referansı
        /// kayardı: ikinci ölçüm zaten ölçeklenmiş bir göz hizasını okur ve çarpanı <c>1</c>'e
        /// yaklaştırırdı — düğme ikinci basışta sessizce bozulurdu.</para>
        /// </summary>
        public float BodyScale { get; set; } = 1f;

        /// <summary>
        /// Sensör kaynağı gerçekten koşuyor mu.
        /// <para>⚠️ <see cref="Initialize"/>'ın onu açmış olması YETMEZ: <c>OVRBody.OnEnable</c>
        /// body tracking'i başlatamazsa <b>kendini kapatır</b> ve bir daha denemez. Bu, gövdenin
        /// sessizce hiç çizilmemesi demektir — çağıran tarafın kurulumdan hemen sonra bunu
        /// okuyup durumu bildirmesi beklenir.</para>
        /// </summary>
        public bool IsSourceProviderRunning => _sourceProvider != null && _sourceProvider.enabled;

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

        /// <summary>SDK retargeter'ı — T-poz yedeğinin serileştirme kapısı (handle + bind pozu
        /// buradan okunur). Kök/kaynak çözümüyle aynı yerde, <see cref="ResolveReferences"/>'ta dolar.</summary>
        private NetworkCharacterRetargeter _retargeter;

        /// <summary>T-poz yedeği istendi mi (yalnız yerel gövde; çağıran <see cref="LocalBodyAvatar"/>).
        /// İstek bir mandaldır — kapısını <see cref="TickTPoseFallback"/> her karede yeniden yargılar.</summary>
        private bool _tPoseFallbackRequested;

        /// <summary>Yedek karelerinin kadans sayacı (SDK'nın <c>IntervalToSendData</c>'sıyla aynı aralık).</summary>
        private float _tPoseFallbackElapsed;

        /// <summary>SDK'nın son TEMİZ karesinin yerel saati (<c>0</c> = bu oturumda hiç kare
        /// gelmedi). Bayatlık ölçüsü budur; sıfırken bayatlık hiç yargılanmaz — "henüz başlamadı"
        /// ile "sustu" aynı şey değildir, ilkinin cevabı ilk açılış yedeğidir.</summary>
        private float _lastSdkFrameTime;

        /// <summary>Toparlanma sayacı: üst üste kaç temiz SDK karesi geldi
        /// (<see cref="RecoverStreakFrames"/>).</summary>
        private int _saneStreak;

        /// <summary>SDK kökü akıl sağlığı denetiminden düştü mü. Yalnız histerezisle iner —
        /// bayatlıktan farkı budur (bayatlık ilk kareyle kendiliğinden geçer).</summary>
        private bool _poseSuspect;

        /// <summary>Bu şüphe epizodunda uyarı basıldı mı (kare başına log basmamak için).
        /// Toparlanma logu bu bayrağın düşüşüyle eşlenir.</summary>
        private bool _poseSuspectWarned;

        /// <summary>
        /// Sensör kaynağı (<c>MetaSourceDataProvider</c>). ⚠️ <b>Prefabdan SİLİNMEZ, yalnız
        /// kapatılır:</b> <c>CharacterRetargeter.Awake</c> sağlayıcıyı <b>kendi GameObject'inden</b>
        /// <c>GetComponent</c> ile arıyor ve yoksa assert atıyor — tek prefabın hem yerel hem uzak
        /// çalışabilmesi bileşenin orada DURMASINA bağlı.
        /// <para>Uzak gövdede kapatılmasının sebebi: her uzak avatar açık bir <c>OVRBody</c> demek
        /// olurdu ve hepsi aynı (yerel) sensörü okurdu — hem israf hem yanlış.</para>
        /// <para>
        /// ⚠️ <b>Bileşen prefabda KAPALI gelir ve öyle kalmalı</b> — burada yalnız yerel gövdede
        /// AÇILIR. Açık gelemez: <c>OnEnable</c> <c>Instantiate</c> anında, yani
        /// <see cref="Initialize"/>'dan ÖNCE koşar; açık gelen bir sağlayıcı her uzak avatar
        /// doğarken bir kez <c>OVRBody.StartBodyTracking</c> çağırır. Admin'de (HMD yok) bu her
        /// spawn'da bir hata satırıdır, Quest'te ise oyuncunun kendi izlemesini uzak bir avatarın
        /// yeniden başlatmasıdır. "Sonradan kapatmak" bu pencereyi kapatmaz.
        /// </para>
        /// </summary>
        private Behaviour _sourceProvider;

        /// <summary>Gelen blob'u SDK'ya vermek için yeniden kullanılan native tampon — kare başına
        /// tahsis etmemek için alanda tutulur (SDK zaten kendi kopyasını alıyor).</summary>
        private NativeArray<byte> _receiveScratch;

        /// <summary>Giden blob'un yönetilen kopyası (SDK native, tel yönetilen bekliyor). Aynı
        /// gerekçe: kare başına tahsis etmemek için büyüdüğü yerde kalır.</summary>
        private byte[] _sendScratch;

        /// <summary>Son GÖNDERİLEN arena kökü — sıçrama emniyetinin karşılaştırma referansı.</summary>
        private Pose _lastSentRoot;
        private bool _hasLastSentRoot;

        /// <summary>Kök üst üste kaç gönderimdir tutuluyor (bkz. <see cref="RootHoldMaxSends"/>).</summary>
        private int _heldRootSends;

        /// <summary>Hangi hizalama sürümünde saklandığı — değişince referans düşer
        /// (<see cref="ArenaCalibrator.CalibrationGeneration"/>).</summary>
        private int _rootCalibrationGeneration = -1;

        /// <summary>Bu tutma epizodunda uyarı basıldı mı (kare başına log basmamak için).</summary>
        private bool _rootHoldWarned;

        /// <summary>Tutulan kökün KAFAYA göreli pozu — epizot başında bir kez kurulur.</summary>
        private Pose _heldRootLocalToHead;
        private bool _hasHeldRootFrame;

        /// <summary>HMD anchor'ı bulunamadığında iki arama arasındaki en kısa süre (sn).</summary>
        private const float CenterEyeSearchIntervalSeconds = 0.5f;

        private Transform _centerEye;
        private float _centerEyeSearchTime = float.NegativeInfinity;

        private void Awake()
        {
            ResolveReferences();
        }

        /// <summary>
        /// Bileşen referanslarını çözer; idempotenttir.
        /// <para>⚠️ <b><see cref="Awake"/>'ten AYRI olmak zorunda:</b> <see cref="Initialize"/>,
        /// objesi henüz PASİF olan bir karakter üzerinde çağrılabilir (yerel gövde kurulana dek
        /// gizli duruyor) ve <b>pasif objenin <c>Awake</c>'i hiç koşmaz</b> — o hâlde bütün alanlar
        /// null kalır ve <c>Setup</c> bir <c>NullReferenceException</c> ile düşer. Sessiz sonucu
        /// şudur: retargeter'ın sahipliği <c>None</c> kalır ve karakter T-pozunda donar.</para>
        /// </summary>
        private void ResolveReferences()
        {
            if (_handler != null)
            {
                return;
            }

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

            _retargeter = retargeter;
            _characterRoot = retargeter != null ? retargeter.transform : transform;

            if (retargeter != null)
            {
                _sourceProvider = retargeter.GetComponent<ISourceDataProvider>() as Behaviour;
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
            // Objesi pasifken çağrılmış olabilir → Awake koşmamış olabilir (bkz. ResolveReferences).
            ResolveReferences();

            if (_handler == null)
            {
                Debug.LogError("[ArenaNetCharacterBehaviour] NetworkCharacterHandler yok — karakter " +
                               "sürülemez. Prefabdaki Character objesine kurulmalı.", this);
                return;
            }

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

        /// <summary>
        /// T-poz yedeğini ister (yalnız yerel gövde; çağıran <see cref="LocalBodyAvatar"/>, tanıma
        /// süresi dolunca). Body tracking hiç geçerli poz üretmemişse SDK'nın gönderim kapısı
        /// (<c>AppliedPose</c>) hiç açılmaz ve oyuncu diğer ekranlarda TÜMDEN görünmez kalırdı;
        /// yedek bunun yerine karakterin bind (T) pozunu, kökü HMD'den türeterek yollar — oyuncu
        /// konumunu izleyen donuk bir T-pozunda çizilir ve arıza "izlemesi bozuk" diye okunur,
        /// "oyuncu yok / ağ bozuk" diye değil.
        /// <para>⚠️ İlk gerçek poz uygulandığı anda (<c>AppliedPose</c> mandalı) SDK yolu devralır
        /// ve BU istek kalıcı olarak etkisizleşir — iki yol aynı anda asla göndermez. Yedeğin oyun
        /// içi arıza tetiği ayrıdır ve isteğe bağlı değildir (<see cref="TickTPoseFallback"/>).</para>
        /// </summary>
        public void RequestTPoseFallback()
        {
            _tPoseFallbackRequested = true;
        }

        /// <summary>
        /// Yedek kadansı. Yedeğin İKİ ayrı tetiği vardır ve ikisi de aynı
        /// <c>IntervalToSendData</c> aralığıyla kare üretir:
        /// <para>
        /// <b>(a) İlk açılış</b> — istek var (<see cref="RequestTPoseFallback"/>) ve SDK bu oturumda
        /// HİÇ poz uygulamadı. ⚠️ Kapı <c>AppliedPose</c>'dur, <c>RetargeterValid</c> DEĞİL:
        /// <c>_isValid</c> kare kare titrer (görüş kaybı, sahne yüklemesi) ve ona bağlanmak, SDK'nın
        /// donuk-son-poz gönderdiği meşru karelerin arasına yedek kareleri sokardı.
        /// <c>AppliedPose</c> ise "bu oturumda en az bir gerçek poz uygulandı" mandalıdır — bir kez
        /// <c>true</c> olunca bu tetik kendiliğinden ve kalıcı olarak devreden çıkar.
        /// </para>
        /// <para>
        /// <b>(b) Oyun içi arıza</b> — SDK poz uyguluyor ama ürettiği kök akıl sağlığı denetiminden
        /// düştü (<see cref="_poseSuspect"/>) ya da akış bayatladı (<see cref="StaleFrameSeconds"/>).
        /// ⚠️ Bu tetik <see cref="_tPoseFallbackRequested"/>'a BAĞLI DEĞİLDİR (kendi kendini kurar)
        /// ve <c>AppliedPose</c> mandalıyla çelişmez: mandal "kaynak bir kez çalıştı" der, arıza ise
        /// sonradan çıkar. Karşılığı olmasaydı zemin/boy karışan başlıkta uzak taraf oyuncuyu
        /// rastgele bir noktada ya da donmuş görürdü — ikisi de sahada "ağ bozuk" diye okunur.
        /// </para>
        /// <para>⚠️ İki tetik aynı anda gönderim yapamaz çünkü (a) yalnız <c>AppliedPose</c>
        /// kapalıyken, (b) yalnız açıkken koşar; ve (b) devredeyken SDK'nın kendi karesi
        /// <see cref="ReceiveStreamData"/>'da bastırılır — telde tek yol vardır.</para>
        /// </summary>
        private void TickTPoseFallback()
        {
            if (_retargeter == null)
            {
                return;
            }

            SkeletonRetargeter skeleton = _retargeter.SkeletonRetargeter;
            if (skeleton == null || !skeleton.IsInitialized ||
                _retargeter.RetargetingHandle == MSDKUtility.INVALID_HANDLE)
            {
                return;
            }

            bool coldStart = _tPoseFallbackRequested && !skeleton.AppliedPose;
            bool inFlightFault = skeleton.AppliedPose && (_poseSuspect || IsSdkStreamStale());
            if (!coldStart && !inFlightFault)
            {
                return;
            }

            _tPoseFallbackElapsed += Time.deltaTime;
            if (_tPoseFallbackElapsed < _retargeter.IntervalToSendData)
            {
                return;
            }

            _tPoseFallbackElapsed = 0f;
            SendTPoseFrame();
        }

        /// <summary>SDK bu süredir hiç TEMİZ kare üretmiyor mu. Hiç kare gelmemişken (<c>0</c>)
        /// bayat sayılmaz: o durumun cevabı ilk açılış tetiğidir.</summary>
        private bool IsSdkStreamStale()
        {
            return _lastSdkFrameTime > 0f && Time.time - _lastSdkFrameTime > StaleFrameSeconds;
        }

        /// <summary>
        /// Tek bir yedek kare serileştirip yollar. Blob'un içeriği karakterin O ANKİ kemik
        /// transformlarıdır: ilk açılış tetiğinde hiç poz uygulanmadığı için bu prefabın bind (T)
        /// pozudur, oyun içi arızada SDK'nın son uyguladığı (artık ilerlemeyen) pozdur. İkisinde de
        /// gövde donuktur ve kökü HMD izler — arıza "izlemesi bozuk" diye okunur. Alıcı tarafta
        /// sıradan bir <c>0x07</c> karesi gibi çözülür; telde ve sunucuda özel bir durum
        /// yoktur (§6.9).
        /// <para>⚠️ Kök karakterden OKUNMAZ: ilk açılışta SDK kökü hiç yazmamıştır (karakter dünya
        /// orijininde durur), oyun içi arızada ise yazdığı kök zaten güvenilmez olduğu için yedek
        /// devrededir — yedeğin karakterden kök okuması arızayı aynen tele taşımak olurdu. Kök
        /// HMD'den türetilir (zemine izdüşüm, yalnız yaw) — gövde oyuncunun gerçek konumunu izler.
        /// Zemin, arena sözleşmesi gereği dünya y=0'dır (<see cref="ArenaSpace"/>).</para>
        /// <para>⚠️ 0. eklemin ölçeği 1'e sabitlenir: normal yolda o alanı
        /// <c>SkeletonRetargeter.RetargetedPose</c> doldurur, ilk açılış yolunda o dizi hiç
        /// yazılmamıştır (ilk değeri belirsiz bellek) ve alıcı <c>DeserializeData</c>'da bu ölçeği
        /// karakter köküne aynen uygular.</para>
        /// <para>⚠️ Kök sıçrama emniyeti (<see cref="GuardRootJump"/>) BİLEREK atlanır: o kapı SDK
        /// kökünün "kumanda düşünce rig orijinine yazılması" arızasına karşıdır; buradaki kök
        /// HMD'den türetiliyor ve o arızayı yaşamaz. <c>_lastSentRoot</c>'a da yazılmaz — emniyetin
        /// referansı SDK yolunun kendi karelerinden kurulur, HMD'den türetilmiş bir kök oraya
        /// karışırsa iki farklı ölçü aynı eşikle karşılaştırılmış olurdu.</para>
        /// </summary>
        private void SendTPoseFrame()
        {
            if (!TryGetHeadYawPose(out Pose head))
            {
                return;
            }

            var worldRoot = new Pose(new Vector3(head.position.x, 0f, head.position.z), head.rotation);

            NativeArray<MSDKUtility.NativeTransform> bodyPose =
                _retargeter.GetCurrentBodyPose(JointType.NoWorldSpace);
            if (bodyPose.Length == 0)
            {
                bodyPose.Dispose();
                return;
            }

            MSDKUtility.NativeTransform rootJoint = bodyPose[0];
            bodyPose[0] = new MSDKUtility.NativeTransform(rootJoint.Orientation, rootJoint.Position, Vector3.one);

            NativeArray<float> facePose = _retargeter.GetCurrentFacePose(true);

            // İndeks listeleri SDK'nın SerializeData'sıyla aynı kaynaktan gelir; BaselineAck = -1 =
            // tam keyframe — delta bu projede zaten kapalı (§6.9).
            int[] bodySync = _retargeter.BodyIndicesToSync;
            int[] faceSync = _retargeter.FaceIndicesToSync;
            var bodyIndices = new NativeArray<int>(bodySync?.Length ?? 0, Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            var faceIndices = new NativeArray<int>(faceSync?.Length ?? 0, Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            if (bodySync != null && bodySync.Length > 0)
            {
                bodyIndices.CopyFrom(bodySync);
            }

            if (faceSync != null && faceSync.Length > 0)
            {
                faceIndices.CopyFrom(faceSync);
            }

            var snapshot = new MSDKUtility.SnapshotData
            {
                BaselineAck = -1,
                Timestamp = NetworkTime,
                TargetSkeletonPose = bodyPose,
                TargetSkeletonIndices = bodyIndices,
                FacePose = facePose,
                FaceIndices = faceIndices,
                RecordingCoordinateSpaceSource = new MSDKUtility.CoordinateSpace(
                    up: new Vector3(0.0f, 1.0f, 0.0f),
                    forward: new Vector3(0.0f, 0.0f, 1.0f),
                    right: new Vector3(1.0f, 0.0f, 0.0f),
                    metersToUnitScale: 1.0f)
            };

            NativeArray<byte> serialized = default;
            bool serializedOk = MSDKUtility.SerializeSkeletonAndFace(
                _retargeter.RetargetingHandle, snapshot, ref serialized);

            if (bodyPose.IsCreated)
            {
                bodyPose.Dispose();
            }

            if (facePose.IsCreated)
            {
                facePose.Dispose();
            }

            if (!serializedOk || !serialized.IsCreated || serialized.Length == 0)
            {
                return;
            }

            SendBlobToRelay(serialized, ArenaSpace.WorldToArena(worldRoot));
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
                // gönderim SDK'nın kendi LateUpdate'inden ReceiveStreamData ile geliyor. Tek
                // istisna T-poz yedeğidir: SDK hiç poz uygulayamamışsa gönderim kapısı
                // (NetworkCharacterHandler.SendData, AppliedPose) hiç açılmaz; oyun içinde de
                // kökü bozulan ya da susan SDK'nın kareleri bastırılır. İki durumda da kareyi biz
                // üretiriz (TickTPoseFallback).
                TickTPoseFallback();
                return;
            }

            ApplyArenaRoot();
        }

        /// <summary>
        /// Karakter kökünü arena uzayındaki interpole edilmiş poza oturtur (§6.9).
        /// <para>⚠️ SDK'nın <c>ApplyBodyPose</c>'undan SONRA koşmak zorunda: o, 0. eklemi kök
        /// transformuna yazıp gönderenin dünya pozunu buraya taşıyor. <c>LateUpdate</c> tüm
        /// <c>Update</c>'lerden sonra çalıştığı için sıra garantidir.</para>
        /// <para>⚠️ <b>Ölçeği de BU sınıf yazar</b> (§10.8): gerçek boy ayrı bir alanla
        /// (<c>bodyScale</c>) taşınıyor ve tek ölçek yazarı burasıdır — iki yazar olsaydı hangi
        /// boyun çizildiği kareye göre değişirdi.</para>
        /// <para>
        /// ⚠️ <b>Gönderenin kök ölçeğinin <c>1</c> olması <c>Calibrate()</c> çağrılmamasına
        /// DEĞİL, retargeter'daki <c>ApplyRootScale</c>'in KAPALI olmasına bağlıdır</b> ve o
        /// bayrak açılırsa buradaki hiçbir satır bozulmadan sistem sessizce yanlışa döner:
        /// SDK o hâlde karakter kökünü oyuncu/model boy oranıyla ölçekler (<c>ApplyPose</c>) ve
        /// pozları o ölçekli uzayda yazar, yani <c>_characterRoot.position</c> artık bir dünya
        /// noktası değil <b>orijine göre ölçeklenmiş</b> bir noktadır. Hata dünya orijininde sıfır,
        /// orijinden uzakta ise mesafeyle orantılıdır — arenası orijinde olan bir sahnede hiç
        /// görünmez, 200 m öteye taşınmış bir arenada gövdeyi onlarca metre uzağa fırlatır.
        /// Gerekçenin tamamı ve ölçüm: <c>Docs/Sistem-Ozeti.md</c> §7.
        /// </para>
        /// </summary>
        private void ApplyArenaRoot()
        {
            if (!TryGetRootWorld(out Pose world))
            {
                // ⚠️ İskelet gelmemişken gövde SIFIR ÖLÇEĞE çekilerek gizlenir. Bu, SDK'nın
                // ApplyRootScale açıkken Setup'ta kendiliğinden yaptığı şeyin yerine geçer: o
                // bayrak kapatıldığı için (gerekçe yukarıda) gizleme artık bizim işimiz. Kapı
                // olmazsa poz kanalı avatarı görünür yaptığı anda, iskeleti hiç gelmemiş ya da
                // akışı susmuş bir oyuncu BIND POZUNDA donmuş bir gövde olarak çizilirdi.
                // ⚠️ Görünürlüğün genel sahibi RemoteAvatar.SetVisible'dır; burası onunla
                // yarışmaz, yalnız bu sınıfın zaten TEK yazarı olduğu ölçeği kullanır.
                _characterRoot.localScale = Vector3.zero;
                return;
            }

            _characterRoot.SetPositionAndRotation(world.position, world.rotation);

            // ⚠️ Kare başına koşulsuz yazılır: ölçeğe dokunan ikinci bir yazar çıkarsa (SDK'nın
            // ApplyRootScale'i geri açılırsa) "değiştiyse yaz" guard'ı bir kare sonra bayat kalır.
            _characterRoot.localScale = Vector3.one * BodyScale;
        }

        /// <summary>Karakter kökünün BU KAREdeki dünya pozu (arena uzayından çevrilmiş).
        /// <para>Transform'dan okunmaz, kaynaktan hesaplanır: transform'a yazan
        /// <see cref="ApplyArenaRoot"/> bu sınıfın <c>LateUpdate</c>'inde koşuyor ve daha erken
        /// sıradaki bir okuyucu bir kare bayat değer alırdı. Aynı <c>RenderTime</c> ile aynı
        /// interpolasyon sorulduğu için burada hesaplamak <b>birebir</b> aynı sonucu verir.</para>
        /// </summary>
        private bool TryGetRootWorld(out Pose world)
        {
            RemoteSkeletonRegistry registry = RemoteSkeletonRegistry.Instance;
            if (registry == null || !registry.TryGetInterpolatedRoot(PlayerId, out Pose arenaRoot))
            {
                world = default;
                return false;
            }

            world = ArenaSpace.ArenaToWorld(arenaRoot);
            return true;
        }

        /// <summary>
        /// Ham (ölçeksiz) bir DÜNYA noktasının, ölçeklenmiş gövdede gerçekte <b>çizildiği</b> yer
        /// (§10.8).
        /// <para>
        /// ⚠️ <b>Gerekçe:</b> <see cref="BodyScale"/> karakterin KÖKÜNE yazılıyor, yani tüm iskelet
        /// kök NOKTASI etrafında ölçekleniyor. Telden gelen el pozu ise ham ölçüdür (gönderenin
        /// gerçek eli). İkisi ölçek <c>1</c>'den saptığı anda ayrışır: çizilen el
        /// <c>kök + ölçek × (ham − kök)</c> noktasındadır, ham poz ise yerinde durur. El pozundan
        /// sürülen her şey (elde çizilen eşya) bu dönüşümden geçmezse gövdeden kopar — 1.3 ölçekte
        /// silah elden yarım metre uzakta çizilir.
        /// </para>
        /// <para>⚠️ Dönüşüm <b>yalnız konumu</b> taşır. Üniform ölçek yön değiştirmez, ve eşyanın
        /// kendisi ölçeklenmez: gerçek boyunda bir silah, büyütülmüş bir elin içinde durur
        /// (<c>RemoteAvatar.SetBodyScale</c>).</para>
        /// </summary>
        public Vector3 ScalePointAboutRoot(in Vector3 worldPoint)
        {
            float scale = BodyScale;
            if (scale <= 0f || Mathf.Approximately(scale, 1f) || !TryGetRootWorld(out Pose root))
            {
                return worldPoint;
            }

            return root.position + (worldPoint - root.position) * scale;
        }

        /// <inheritdoc cref="INetworkCharacterBehaviour.ReceiveStreamData"/>
        /// <remarks>
        /// Adı yanıltıcı — bu, SDK'nın <b>gönderilecek</b> veriyi bize uzattığı kancadır
        /// (<c>NetworkCharacterHandler.SendData</c> çağırır). Buradan sonrası bizim telimizdir.
        /// <para>⚠️ Kök TAM BU ANDA okunur: SDK pozu bu karede çözdü ve karakterin kökü şu an
        /// gönderenin dünya pozunda. Bir kare sonra okumak gövde ile kökü ayırırdı.</para>
        /// <para>⚠️ Kare burada bir <b>akıl sağlığı denetiminden</b> geçer. SDK "geçerli" bayrağıyla
        /// çöp kök üretebilir (zemin/boy karışması) ve o kök tele girdiğinde uzak taraf oyuncuyu
        /// rastgele bir noktada çizer — sessiz ve teşhis edilemez bir arıza. Düşen kare
        /// GÖNDERİLMEZ; yerine <see cref="TickTPoseFallback"/> oyuncunun konumunu izleyen donuk bir
        /// gövde yollar, yani arıza görünür bir belirtiye dönüşür.</para>
        /// </remarks>
        public void ReceiveStreamData(ulong clientId, bool isReliable, NativeArray<byte> bytes)
        {
            if (!bytes.IsCreated || bytes.Length == 0)
            {
                return;
            }

            // Kanal yokken kök emniyetinin durumu (GuardRootJump) ilerletilmesin — gönderilmeyen
            // bir kare emniyetin karşılaştırma referansı olamaz.
            ArenaClient client = ArenaClient.Instance;
            if (client == null || client.UdpChannel == null)
            {
                return;
            }

            // ⚠️ Buranın bir DÜNYA noktası olması retargeter'daki ApplyRootScale'in KAPALI
            // olmasına bağlıdır: açıkken SDK kökü boy oranıyla ölçekler ve bu okuma orijine göre
            // ölçeklenmiş bir nokta döndürür (gerekçe ApplyArenaRoot'ta, ölçüm Sistem-Ozeti §7).
            Pose candidate = ArenaSpace.WorldToArena(
                new Pose(_characterRoot.position, _characterRoot.rotation));

            if (!AcceptSdkFrame(candidate))
            {
                // ⚠️ Bastırılan kare emniyetin durumuna HİÇ dokunmaz: GuardRootJump çağrısı bu
                // yüzden karardan SONRAYA alındı. Çöp bir kök _lastSentRoot'a yazılsaydı emniyet
                // bir sonraki temiz kareyi "sıçrama" sanardı — kanal yokken de aynı gerekçeyle
                // duruluyor.
                return;
            }

            SendBlobToRelay(bytes, GuardRootJump(candidate));
        }

        /// <summary>
        /// SDK karesinin kökü tele girecek kadar makul mü. Ölçünün paydası HMD'dir: karakter kökü
        /// kalça altı/zemin hizasındadır, yani kafanın zemin izdüşümünün yakınında ve arena zemini
        /// olan y=0 civarında durmak zorundadır.
        /// <para>⚠️ HMD pozu çözülemiyorsa denetim ATLANIR ve kare kabul edilir: yargılayacak
        /// referans yokken kareyi düşürmek gövdeyi tümden susturmak olurdu — üstelik yedek de aynı
        /// referansa muhtaç, yani yerine bir şey koyamazdı.</para>
        /// <para>⚠️ Şüpheden çıkış histerezisle olur (<see cref="RecoverStreakFrames"/>): sayaç
        /// dolana kadar temiz kareler de BASTIRILIR. Toparlanma sırasında iki yolun karışması, uzak
        /// tarafta kare kare değişen iki farklı gövde demektir.</para>
        /// </summary>
        private bool AcceptSdkFrame(in Pose arenaRoot)
        {
            if (TryGetHeadYawPose(out Pose head))
            {
                Vector3 arenaHead = ArenaSpace.WorldToArena(head.position);
                float horizontal = Vector2.Distance(
                    new Vector2(arenaRoot.position.x, arenaRoot.position.z),
                    new Vector2(arenaHead.x, arenaHead.z));

                if (horizontal > SuspectHorizontalMeters ||
                    Mathf.Abs(arenaRoot.position.y) > SuspectRootHeightMeters)
                {
                    _poseSuspect = true;
                    _saneStreak = 0;
                    if (!_poseSuspectWarned)
                    {
                        _poseSuspectWarned = true;
                        Debug.LogWarning(
                            "[ArenaNetCharacterBehaviour] Gövde kökü akıl sağlığı denetiminden düştü " +
                            $"(kafaya yatay uzaklık {horizontal:F1} m, kök yüksekliği " +
                            $"{arenaRoot.position.y:F1} m) — gövde T-poz yedeğine geçti; diğerleri " +
                            "oyuncuyu konumunu izleyen donuk T-pozda görecek. Zemin/boy çözümü " +
                            "bozulmuş olabilir (izleme haritası bayat).", this);
                    }

                    return false;
                }
            }

            _lastSdkFrameTime = Time.time;

            if (!_poseSuspect)
            {
                return true;
            }

            _saneStreak++;
            if (_saneStreak < RecoverStreakFrames)
            {
                return false;
            }

            _poseSuspect = false;
            _poseSuspectWarned = false;
            _saneStreak = 0;
            Debug.Log("[ArenaNetCharacterBehaviour] Gövde kökü yeniden makul — SDK yoluna dönüldü, " +
                      "T-poz yedeği susuyor.", this);
            return true;
        }

        /// <summary>
        /// Native blob'u yönetilen tampona kopyalayıp iskelet kanalından (§6.9) yollar — SDK
        /// yolunun (<see cref="ReceiveStreamData"/>) ve T-poz yedeğinin ortak çıkışı.
        /// <para>Native → yönetilen kopya: tel katmanı <c>BinaryWriter</c> ile yazıyor ve o yalnız
        /// <c>byte[]</c> alıyor. Tampon büyüdüğü yerde kalır, yani ilk birkaç karenin dışında
        /// tahsis yok.</para>
        /// </summary>
        private void SendBlobToRelay(NativeArray<byte> bytes, in Pose arenaRoot)
        {
            ArenaClient client = ArenaClient.Instance;
            if (client == null || client.UdpChannel == null)
            {
                return;
            }

            byte[] managed = _sendScratch;
            if (managed == null || managed.Length < bytes.Length)
            {
                managed = new byte[Mathf.NextPowerOfTwo(bytes.Length)];
                _sendScratch = managed;
            }

            NativeArray<byte>.Copy(bytes, 0, managed, 0, bytes.Length);
            client.UdpChannel.SendSkeleton(managed, bytes.Length, arenaRoot);
        }

        /// <summary>
        /// Kök sıçrama emniyeti: bilinen bir cihaz arızası sürerken kökün tek gönderimde
        /// <see cref="RootJumpLimitMeters"/>'den fazla atmasını bastırır ve son kökü yollar.
        /// <para>
        /// ⚠️ <b>Emniyet YALNIZ bir el tutuluyorken silahlanır</b> ve eller sağlamken kök olduğu
        /// gibi geçer. Bu bilinçlidir: koşulsuz bir hız kelepçesi kök için ikinci bir otorite
        /// olurdu ve gerçek hareketi (koşan, eğilen, sıçrayan oyuncu) lastikletirdi. Kapı, körü
        /// körüne bir "makul hız" varsayımına değil <b>tanımlı bir arızaya</b> bağlıdır: kumanda
        /// düşünce rig el anchor'ını rig orijinine yazar, body tracking çözümü o eli hedefler ve
        /// SDK'nın yazdığı kök sıçrar. Arıza geçince kapı kendiliğinden kapanır.
        /// </para>
        /// <para>
        /// ⚠️ Yeniden kalibrasyon herkesi <b>meşru olarak</b> taşır — saklanan kök o anda
        /// geçersizdir (<see cref="ArenaCalibrator.CalibrationGeneration"/>), yoksa hizalamayı
        /// arıza sanıp <see cref="RootHoldMaxSends"/> gönderim boyunca eski yeri yollardık.
        /// </para>
        /// </summary>
        private Pose GuardRootJump(Pose candidate)
        {
            int generation = ArenaCalibrator.CalibrationGeneration;
            if (generation != _rootCalibrationGeneration)
            {
                _rootCalibrationGeneration = generation;
                _hasLastSentRoot = false;
                _heldRootSends = 0;
            }

            bool armed = !ControllerTracking.IsValid(false) || !ControllerTracking.IsValid(true);
            bool jumped = _hasLastSentRoot &&
                          Vector3.Distance(candidate.position, _lastSentRoot.position) > RootJumpLimitMeters;

            if (armed && jumped && _heldRootSends < RootHoldMaxSends)
            {
                _heldRootSends++;
                if (!_rootHoldWarned)
                {
                    _rootHoldWarned = true;
                    Debug.LogWarning(
                        "[ArenaNetCharacterBehaviour] Kumanda izlemesi düşükken gövde kökü " +
                        $"{Vector3.Distance(candidate.position, _lastSentRoot.position):F1} m sıçradı — " +
                        "kök KAFAYA sabitlenerek gönderiliyor. Kumandanın pili bitmiş olabilir.", this);
                }

                return HoldRootRelativeToHead();
            }

            // Kabul: ya emniyet silahsız, ya sıçrama yok, ya da tutma tavana vurdu (oyuncu
            // gerçekten oraya gitmiş olabilir → sayaçlar sıfırlanır, bir sonraki epizot yeniden
            // uyarabilir).
            _lastSentRoot = candidate;
            _hasLastSentRoot = true;
            _heldRootSends = 0;
            _rootHoldWarned = false;
            _hasHeldRootFrame = false;
            return candidate;
        }

        /// <summary>
        /// Tutulan kökü DÜNYA uzayında dondurmak yerine <b>kafaya sabitler</b>.
        /// <para>
        /// ⚠️ <b>Kapatılan açık:</b> arıza kumandadadır, HMD'de değil — oyuncu o sırada yürümeye
        /// devam ediyor. Kök dünya uzayında dondurulursa gövde yerinde çakılı kalır, oysa el
        /// kanalının aynı arızaya verdiği cevap farklıdır: <c>PlayerPoseTracker</c> eli
        /// <b>kafaya göreli</b> saklayıp yeniden kurar, yani eller kafayla birlikte yürür. İki
        /// kanal aynı arızada iki ayrı referansa tutunduğu için gövde geride kalırken eller (ve
        /// elde çizilen silah) oyuncuyla gider — tutma penceresi boyunca (≈1,2 sn) uzak avatar
        /// ikiye ayrılır. Kökü de kafaya bağlamak iki kanalı aynı referansa oturtur.
        /// </para>
        /// <para>⚠️ Yalnız YAW kullanılır: kafa eğildiğinde gövdenin de yatması gerekmez
        /// (<c>RemoteAvatar.ApplyBody</c> ile aynı sözleşme).</para>
        /// <para>Kafa da çözülemiyorsa (rig yok) eski davranışa düşülür — dünya uzayında dondurmak,
        /// hiçbir şey göndermemekten iyidir.</para>
        /// </summary>
        private Pose HoldRootRelativeToHead()
        {
            if (!TryGetHeadYawPose(out Pose head))
            {
                return _lastSentRoot;
            }

            if (!_hasHeldRootFrame)
            {
                _hasHeldRootFrame = true;
                Quaternion inverse = Quaternion.Inverse(head.rotation);
                _heldRootLocalToHead = new Pose(
                    inverse * (_lastSentRoot.position - head.position),
                    inverse * _lastSentRoot.rotation);
            }

            return new Pose(
                head.position + head.rotation * _heldRootLocalToHead.position,
                head.rotation * _heldRootLocalToHead.rotation);
        }

        /// <summary>HMD'nin dünya pozu, rotasyonu YAW'a düzleştirilmiş. Arama kısılır: rig yoksa
        /// (admin gözlemci) kare başına sahne geneli tip araması yapılmasın.</summary>
        private bool TryGetHeadYawPose(out Pose head)
        {
            if (_centerEye == null && Time.unscaledTime - _centerEyeSearchTime >= CenterEyeSearchIntervalSeconds)
            {
                _centerEyeSearchTime = Time.unscaledTime;
                OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
                _centerEye = rig != null ? rig.centerEyeAnchor : null;
            }

            if (_centerEye == null)
            {
                head = default;
                return false;
            }

            Vector3 forward = _centerEye.forward;
            forward.y = 0f;
            head = new Pose(
                _centerEye.position,
                forward.sqrMagnitude > 1e-6f
                    ? Quaternion.LookRotation(forward, Vector3.up)
                    : Quaternion.identity);
            return true;
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
