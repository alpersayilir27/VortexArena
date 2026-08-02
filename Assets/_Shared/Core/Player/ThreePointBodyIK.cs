using Meta.XR.Movement.Retargeting.IK;
using UnityEngine;
using VortexArena.Core.Arena;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Üç noktalı pozdan (kafa + iki el) humanoid bir iskeleti çözer — uzak oyuncu
    /// avatarlarının sürücüsü.
    /// <para>
    /// <b>Neden IK gerekiyor:</b> ağdan gelen tek şey kafa ve iki elin pozu (§ <c>pose</c>
    /// mesajı, 92 B). Gövde, kollar ve bacaklar bu üç noktadan TÜRETİLİR; ağ tarafına tek bayt
    /// eklenmez. Dirsek/omuz yönü bir tahmindir — gerçek body tracking değildir.
    /// </para>
    /// <para>
    /// <b>Kemikler isimle DEĞİL <see cref="Animator.GetBoneTransform"/> ile bulunur:</b> karakter
    /// humanoid rig'e sahip olduğu için eşleme Avatar'da yaşar; model değiştiğinde (Mixamo'dan
    /// başka bir karaktere geçilse bile) bu bileşende tek satır değişmez ve prefabda elle
    /// bağlanacak Transform kalmaz. Gövde ölçüleri (kalça düşüşü, ayak bileği yüksekliği) de
    /// aynı sebeple SABİT DEĞİL, karakterin bind pozundan ÖLÇÜLÜR.
    /// </para>
    /// <para>
    /// <b>Bacaklar tamamen prosedüreldir</b> (adım döngüsü + ayak IK): projede yürüme animasyon
    /// klibi yoktur ve free-roam'da oyuncu fiziksel yürüdüğü için kök hareketi zaten gerçektir —
    /// tek eksik ayakların nereye basacağıdır.
    /// </para>
    /// <para>
    /// ⚠️ <b>Üç kural bozulursa avatar görsel olarak PATLAR</b> (uzamış/kopmuş uzuvlar):
    /// (1) <see cref="IKUtilities.SolveCCDIK"/> zinciri <b>UÇ→KÖK</b> sırasında bekler
    /// (effector = dizinin 0. elemanı); ters verilirse çözücü omzu/kalçayı ele doğru sürükler.
    /// (2) Her kare <b>bind pozuna dönülür</b>: bu bileşen kemiklere mutlak değil BİRİKİMLİ
    /// (<c>rotation = delta * rotation</c>) yazıyor ve sahnede pozu sıfırlayacak bir
    /// AnimatorController yok — sıfırlanmazsa gövde her karede biraz daha bükülür.
    /// (3) El/kafa kemiğine <b>konum yazılmaz, yalnız rotasyon</b>: konum yazmak kemiği
    /// ebeveyninden koparır (mesh gerilir), üstelik erişilemeyen hedefte bunu her kare yapar.
    /// </para>
    /// <para>
    /// ⚠️ <b>Çözücü İKİ dikey referansı birleştirir</b> ve bu onun en kırılgan yeridir: kafa/eller
    /// AĞDAN (gönderenin uzayı), kök ve ayaklar ARENA ZEMİNİNDEN gelir. İkisi uyuşmazsa —
    /// gönderenin rig'i hizalı değilse — çözücü imkânsız bir gövde kurmak zorunda kalır: kafa
    /// zeminin altındayken ayak hedefi kalçanın ÜSTÜNE düşer ve CCD bacakları gövdenin üzerine
    /// sarar (avatar "top" olur, kafa göğsün içinde kalır). Bu yüzden gelen poz önce
    /// <b>makullüğe</b> bakılır: göz arena zemininden
    /// [<see cref="MinPlausibleEyeHeight"/>, <see cref="MaxPlausibleEyeHeight"/>] m dışındaysa
    /// zemin referansı ARENADAN değil POZDAN türetilir — avatar yanlış yükseklikte çizilir ama
    /// <b>bütün bir insan</b> kalır (maç ortasında düşmanı kaybetmemek bilinçli bir tercihtir).
    /// (Ölçülen büyüklük GÖZÜN yüksekliğidir; gerekçesi aşağıda.)
    /// </para>
    /// <para>
    /// ⚠️ <b>Ağdan gelen rotasyon kemiğe DOĞRUDAN yazılmaz</b> — izleme uzayı ile kemik uzayının
    /// bind ekseni farklıdır (köprü <see cref="HandGripConvention"/>). Ch15'te kafa/boyun/kalça
    /// kemiklerinin bind ekseni TESADÜFEN kimlik olduğu için <c>_head.rotation = head.rotation</c>
    /// çalışıyor (ölçüldü: 0° sapma); eller 115°/128° sapıyordu. Başka bir karaktere geçilirse
    /// kafa da aynı sebeple kırılır — o zaman çözüm ellerinkiyle aynıdır: bazı ölçüp çarpmak.
    /// </para>
    /// <para>
    /// ⚠️ <b>Oyuncunun boyu TEK BİR ANDA ölçülür, sonra SABİT kalır.</b> Ölçüm penceresi
    /// kalibrasyon tamamlanınca (<see cref="ArenaCalibrator.CalibrationGeneration"/>) ya da avatar
    /// başka bir oyuncuya devredilince (<see cref="ResetPoseState"/>) açılır ve
    /// <see cref="HeightMeasureDelaySeconds"/> saniye sonra kapanır: gecikme, oyuncunun zemin
    /// işaretine eğilmiş hâlde ölçülmemesi içindir — aranan boy AYAKTA olandır. Sabitlendikten
    /// sonra çömelme, zıplama ya da poz sıçraması avatarın ölçeğini DEĞİŞTİRMEZ.
    /// </para>
    /// <para>
    /// ⚠️ <b>Sabitlenen bir tahminin geri dönüş yolu OLMAK ZORUNDADIR</b> — yukarıdaki iki
    /// tetikleyici o yollardır. Aynı sebeple ölçüm tek kareden değil, pencerenin son
    /// <see cref="HeightMeasureAverageSeconds"/> saniyesinin ORTALAMASINDAN alınır: kendini
    /// düzeltmeyen bir değerde tek gürültülü kare, maçın kalanı boyunca yanlış boy demektir.
    /// Ölçüm inene kadar geçici olarak kayan pencere maksimumu sürülür, ayaklar da ışınlanmada
    /// yeniden ziplatılır.
    /// </para>
    /// <para>
    /// ⚠️ <b>Gelen "kafa" pozu GÖZÜN pozudur</b> (<c>centerEyeAnchor</c> — hem yerel rig'den hem
    /// telden aynı kaynak gelir), kafa KEMİĞİNİN pozu değil. İkisi karıştırılırsa iskelet gözün
    /// olduğu yere oturur ve gövde bir kafa yarısı kadar yukarı + öne kayar: Ch15'te yaka kemiği
    /// gözün 18-20 cm altında olması gerekirken 6-7 cm altına çıkıyor, yani ana kameranın
    /// near-clip'inin (0.1 m) İÇİNE giriyordu — aşağı bakan oyuncu kendi gövdesinin içini
    /// görüyordu. Köprü <see cref="headBoneToEyeOffset"/>'tir ve aynı sebeple <b>ölçek</b> de
    /// kafa kemiği yüksekliğine değil <see cref="ModelEyeHeight"/>'a bölünür (aksi hâlde avatar
    /// sistematik olarak ~%8 büyük çizilirdi).
    /// </para>
    /// <para>
    /// ⚠️ <b>Meta'nın CCD çözücüsü ÖLÇEĞİ görmezden gelir</b> — hedef ona ham verilmez, bkz.
    /// <see cref="SolveChain"/>.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class ThreePointBodyIK : MonoBehaviour
    {
        [Header("Göz ↔ kafa kemiği")]
        [Tooltip("Gözün kafa KEMİĞİNE göre yeri (kemik-yerel, metre; +Y yukarı, +Z ileri). " +
                 "Gelen poz gözün pozudur; kafa kemiği bu ofset kadar geriye/aşağıya oturtulur. " +
                 "Bu KARAKTERİN ölçüsüdür, oyuncunun değil — yerel ve uzak prefabda aynı girilir. " +
                 "Sıfır bırakılırsa gövde ~12 cm yükselir ve yaka near-clip'in içine girer.")]
        [SerializeField] private Vector3 headBoneToEyeOffset = new Vector3(0f, 0.12f, 0.09f);

        [Header("Gövde")]
        [Tooltip("Kalçanın kafa yönünün GERİSİNDE durma payı — öne eğilince gövde yatar.")]
        [SerializeField] private float hipsBackOffsetMeters = 0.06f;

        [Tooltip("Gövdenin kafa yaw'ını takip hızı (derece/sn). Düşük değer omuz-kafa farkı bırakır.")]
        [SerializeField] private float torsoYawFollowSpeed = 360f;

        [Tooltip("Gövdenin kafaya göre en fazla sapabileceği açı — bu aşılırsa gövde anında yetişir.")]
        [SerializeField] private float torsoMaxYawLagDegrees = 70f;

        [Header("Bilek eşlemesi (canlı ayar)")]
        [Tooltip("Elin anatomik çerçevesinde ince ayar (derece). Z = PARMAK EKSENİ etrafında roll " +
                 "(bilek ters yüz duruyorsa aranan budur, 180 dene), X = bileği yukarı/aşağı kır, " +
                 "Y = içe/dışa çevir. Sıfır = HandGripConvention'daki değer olduğu gibi kullanılır.")]
        [SerializeField] private Vector3 leftHandTuningEuler;

        [Tooltip("Sağ elin karşılığı. İki el AYRI ayarlanır: iskeletlerin aynalanma biçimi farklı, " +
                 "ortak bir sayı ikisini birden düzeltmez.")]
        [SerializeField] private Vector3 rightHandTuningEuler;

        [Header("Kollar")]
        [Tooltip("Bileğin KENDİ ekseni etrafındaki dönüşünün önkola devredilen payı [0..1]. " +
                 "0 = burulmanın tamamı tek eklemde kalır ve bilek büküldükçe incelip kalınlaşır " +
                 "(mesh'in şeker ambalajı gibi çökmesi); 0.5 = gerçek önkolun radius/ulna paylaşımı. " +
                 "Elin YERİNİ değiştirmez — dönüş ekseni zaten önkoldan ele giden eksendir.")]
        [Range(0f, 1f)]
        [SerializeField] private float forearmTwistShare = 0.5f;

        [Tooltip("El/ayak hedefine kabul edilen yakınlık (metre).")]
        [SerializeField] private float armTolerance = 0.01f;

        [Tooltip("Kol IK yineleme sayısı.")]
        [SerializeField] private int armIterations = 8;

        [Header("Bacaklar")]
        [Tooltip("İki ayağın duruş genişliğinin yarısı (metre).")]
        [SerializeField] private float stanceHalfWidth = 0.11f;

        [Tooltip("Ayak, olması gereken yerden bu kadar uzaklaşınca adım başlar (metre).")]
        [SerializeField] private float stepTriggerDistance = 0.28f;

        [Tooltip("Bir adımın süresi (saniye).")]
        [SerializeField] private float stepDuration = 0.22f;

        [Tooltip("Adım yayının tepe yüksekliği (metre).")]
        [SerializeField] private float stepArcHeight = 0.09f;

        /// <summary>Avatar ölçeğinin alt/üst sınırı — bozuk bir poz ölçümü avatarı devleştirmesin.
        /// <para>Aralık, oynayan insan boyunu KISITLAMAYACAK kadar geniştir (çocuktan uzun yetişkine):
        /// ölçek artık ölçülmüş bir boydan geliyor, kırpma bir denetim değil son savunma hattıdır.
        /// Dar bir tavan, kısa oyuncuyu kendi gövdesinden büyük görmeye zorlardı.</para></summary>
        private const float MinScale = 0.25f;
        private const float MaxScale = 1.75f;

        /// <summary>
        /// GÖZÜN arena zemininden makul yüksekliği (m). Bu aralığın DIŞINDAKİ poz "gönderenin
        /// rig'i hizalı değil" demektir; zemin referansı o zaman arenadan değil pozdan alınır.
        /// <para>Aralık geniş tutulur (çömelmiş kısa oyuncu ↔ zıplayan uzun oyuncu): amaç boy
        /// denetlemek değil, METRELERCE kayan bir uzayı yakalamaktır.</para>
        /// </summary>
        private const float MinPlausibleEyeHeight = 0.6f;
        private const float MaxPlausibleEyeHeight = 2.6f;

        /// <summary>
        /// Boy tahmininin kayan pencere uzunluğu (sn) — yalnız ölçüm İNENE KADAR geçerli olan
        /// geçici tahmin için. ⚠️ <b>Sonsuz maksimum kullanılmaz:</b> tek bozuk kare avatarı
        /// kalıcı olarak devleştirirdi. Pencere, çömelmede küçülmeyi önleyecek kadar uzun, hatalı
        /// bir örneği unutacak kadar kısadır.
        /// </summary>
        private const float StandingHeightWindowSeconds = 5f;

        /// <summary>
        /// Tetikten (kalibrasyon tamamlanması / <see cref="ResetPoseState"/>) boy ölçümüne kadar
        /// beklenen süre (sn). Oyuncu kalibrasyonu kumandanın ucunu zemin işaretine değdirerek
        /// yapıyor, yani o anda eğilmiş durumda; ölçüm ayağa kalkacak kadar beklemek zorundadır.
        /// <para>⚠️ Süre <b>GÜVENİLİR</b> karelerde işler (bkz. <see cref="MinPlausibleEyeHeight"/>):
        /// hiç izleme yokken saymak, ölçümü boş bir pozdan almak olurdu.</para>
        /// </summary>
        private const float HeightMeasureDelaySeconds = 3f;

        /// <summary>
        /// Ölçüm penceresinin sonunda ortalaması alınan kuyruk süresi (sn) — bkz. sınıf özeti,
        /// "sabitlenen bir tahmin tek gürültülü kareye emanet edilmez".
        /// </summary>
        private const float HeightMeasureAverageSeconds = 0.5f;

        /// <summary>
        /// Ölçeğin hedefine yaklaşma hızı (ölçek birimi / sn).
        /// <para>⚠️ <b>Ölçek DOĞRUDAN yazılmaz.</b> Pencere devrinde
        /// (<see cref="StandingHeightWindowSeconds"/>) hedef bir karede atlıyor; uzak avatarda fark
        /// edilmiyordu ama YEREL gövdede oyuncu kendi kollarına bakıyor ve sıçrama "kolum uzayıp
        /// kısalıyor" olarak görünüyordu. Hız, normal bir pencere devrini (birkaç cm) yarım
        /// saniyenin altında kapatacak kadar yüksek, sıçramayı gizleyecek kadar düşüktür.</para>
        /// <para>İKİ kare istisnadır ve ikisinde de hedefe ANINDA oturulur: avatarın ilk karesi
        /// (<see cref="_scaleInitialized"/>, yoksa her doğuşta yavaşça büyürdü) ve boy ölçümünün
        /// indiği kare (<see cref="TrackStandingHeight"/> — hedef orada son kez değişir, yumuşatma
        /// onu saniyelerce süren bir büyüme animasyonuna çevirirdi).</para>
        /// </summary>
        private const float ScaleFollowRatePerSecond = 0.15f;

        /// <summary>
        /// Ayak, bulunması gereken yerden bu kadar uzaktaysa adım atılmaz, doğrudan ZIPLATILIR (m).
        /// <para>⚠️ <see cref="stepTriggerDistance"/> ile karıştırma: o "adım başlat" eşiği, bu
        /// "adım atmanın anlamsız olduğu sıçrama" eşiğidir. Işınlanan avatarda (harita değişimi,
        /// kalibrasyon düzeltmesi, poz sıçraması) ayak eski noktada kalıp bacağı metrelerce
        /// uzatıyordu.</para>
        /// </summary>
        private const float FootResnapDistance = 1f;

        /// <summary>Güvenilmez poz uyarısının en sık tekrar aralığı (sn) — 20 Hz'de log seli olurdu.</summary>
        private const float UntrustedWarnCooldownSeconds = 10f;

        /// <summary>
        /// Bir kemiğin "gizlenmiş" sayıldığı ölçek eşiği. <see cref="LocalAvatarBoneHider"/> uzuvları
        /// 0.0001'e ölçekleyerek gizliyor; eşik ondan belirgin biçimde büyük, meşru hiçbir avatar
        /// ölçeğine (<see cref="MinScale"/>) yaklaşmayacak kadar küçüktür.
        /// </summary>
        private const float DegenerateBoneScale = 1e-3f;

        private Animator _animator;
        private bool _bonesResolved;

        // Gövde
        private Transform _hips;
        private Transform _spine;
        private Transform _chest;
        private Transform _upperChest;
        private Transform _neck;
        private Transform _head;

        // Kollar — CCD zincirleri her karede yeniden ayrılmasın diye alanda tutulur.
        // ⚠️ Sıra UÇ→KÖK: { el, önkol, üstkol } (bkz. sınıf özeti).
        private Transform[] _leftArmChain;
        private Transform[] _rightArmChain;
        private Transform _leftHand;
        private Transform _rightHand;

        /// <summary>
        /// El kemiğinin ANATOMİK BAZI (el-yerel), karakterin bind pozundan BİR KEZ ölçülür —
        /// izleme uzayı → kemik ekseni köprüsünün model tarafı (bkz. <see cref="HandGripConvention"/>).
        /// <para>⚠️ Köprünün kendisi <b>her karede</b> kuruluyor, önceden hesaplanıp saklanmıyor:
        /// ince ayar alanları Inspector'dan değiştirilince sonucun ANINDA görünmesi gerekiyor.
        /// Ayar, admin (Windows) tarafında canlı bir uzak avatara bakılarak yapılıyor ve bir kare
        /// gecikme bile "değişti mi değişmedi mi" sorusunu belirsizleştirirdi. Maliyet kare başına
        /// iki quaternion çarpımıdır.</para>
        /// </summary>
        private Quaternion _leftHandBoneBasis = Quaternion.identity;
        private Quaternion _rightHandBoneBasis = Quaternion.identity;

        /// <summary>Baz ölçülebildi mi — ölçülemediyse köprü kurulmaz, düzeltme kimlik kalır
        /// (ince ayar da uygulanmaz: ölçüm yoksa ayarlanacak bir çerçeve de yoktur).</summary>
        private bool _leftHandBasisMeasured;
        private bool _rightHandBasisMeasured;

        /// <summary>
        /// Önkolun burulma ekseni (ÖNKOL-yerel, birim): önkoldan ele giden yön, bind pozunda bir kez
        /// ölçülür. Bileğin bu eksen etrafındaki dönüşü <see cref="forearmTwistShare"/> kadar önkola
        /// devredilir.
        /// <para>⚠️ Eksen sabit yazılmaz (Ch15'te önkol-yerel <c>+Y</c>): karakter değişince burada
        /// tek satır değişmesin — aynı gerekçe <see cref="_leftHandBoneBasis"/>'te de geçerli.</para>
        /// <para>Sıfır vektör = ölçülemedi (zincir yok) → burulma paylaştırılmaz.</para>
        /// </summary>
        private Vector3 _leftForearmTwistAxis;
        private Vector3 _rightForearmTwistAxis;

        // Bacaklar — aynı sıra: { ayak, alt bacak, üst bacak }.
        private Transform[] _leftLegChain;
        private Transform[] _rightLegChain;

        // Bind pozu: her kare buraya dönülür (birikimli yazımın panzehiri).
        private Transform[] _drivenBones;
        private Quaternion[] _bindLocalRotations;
        private Vector3[] _bindLocalPositions;

        /// <summary>Karakterin KENDİ ölçüleri (bind pozunda, kök uzayında; ölçek 1 iken).
        /// <para><see cref="_modelHeadHeight"/> kafa KEMİĞİNİN yüksekliğidir (Ch15'te ≈ 1.56 m);
        /// oyuncuyla karşılaştırılacak olan ise gözdür → <see cref="ModelEyeHeight"/>.</para></summary>
        private float _modelHeadHeight;
        private float _modelHipsDrop;
        private float _modelAnkleHeight;

        /// <summary>
        /// Modelin GÖZ yüksekliği — ölçeğin ve zemin türetmesinin paydası.
        /// <para>Alan değil property: <see cref="headBoneToEyeOffset"/> Inspector'dan canlı
        /// ayarlanabilsin diye (aynı gerekçe <see cref="_leftHandBoneBasis"/> yorumunda).</para>
        /// </summary>
        private float ModelEyeHeight => _modelHeadHeight + headBoneToEyeOffset.y;

        /// <summary>Oyuncunun ayakta GÖZ yüksekliği — avatar buna göre ölçeklenir.
        /// <para><see cref="_heightLocked"/> olunca bu değer ÖLÇÜLMÜŞ boydur ve bir sonraki
        /// tetiğe kadar değişmez. O ana kadar geçici tahmindir: kayan pencere maksimumu
        /// (<see cref="_recentMaxEyeHeight"/> ikinci kovadır, pencere dolunca yerine geçer —
        /// O(1), tahsissiz).</para></summary>
        private float _standingEyeHeight;
        private float _recentMaxEyeHeight;
        private float _heightWindowTimer;
        private float _scale = 1f;

        /// <summary>Ölçüm penceresi: tetikten bu yana geçen GÜVENİLİR süre ve son
        /// <see cref="HeightMeasureAverageSeconds"/> saniyenin örnek toplamı/sayısı.</summary>
        private float _measureTimer;
        private float _measureSum;
        private int _measureSamples;

        /// <summary>Boy ölçüldü mü — true iken kayan pencere ARTIK İŞLEMEZ, ölçek sabit kalır.</summary>
        private bool _heightLocked;

        /// <summary>Son görülen <see cref="ArenaCalibrator.CalibrationGeneration"/>; değişmesi
        /// "arena yeniden hizalandı, boy yeniden ölçülmeli" demektir. Olaya abone OLUNMAZ —
        /// gerekçe o property'nin özetinde.</summary>
        private int _calibrationGeneration;

        /// <summary>Ölçek ilk kez oturtuldu mu — ilk kare hedefe anında sıçrar, sonrası yumuşar.</summary>
        private bool _scaleInitialized;

        /// <summary>Son karede gelen poz makul müydü (bkz. sınıf özeti). Değişimde ayaklar sıfırlanır.</summary>
        private bool _poseTrusted = true;
        private float _lastUntrustedWarnTime = float.NegativeInfinity;

        /// <summary>
        /// Çözücüye verilecek tolerans. ⚠️ <see cref="IKUtilities.SolveCCDIK"/> toleransı
        /// <b>KARESİYLE</b> karşılaştırır (<c>sqrMagnitude &gt; tolerance</c>) — metre cinsinden
        /// verilirse 0.01 m istenirken 0.1 m'de durur ve el hedefin bir karış uzağında kalır.
        /// </summary>
        private float SolverTolerance => armTolerance * armTolerance;

        private Quaternion _torsoYaw = Quaternion.identity;
        private bool _yawInitialized;

        /// <summary>Ayağın şu an bastığı (dünya) nokta.</summary>
        private Vector3 _leftFootPlanted;
        private Vector3 _rightFootPlanted;

        /// <summary>Adım başlangıç noktası ve ilerleme [0..1]; 1 = adım bitti.</summary>
        private Vector3 _leftFootFrom;
        private Vector3 _rightFootFrom;
        private float _leftStepProgress = 1f;
        private float _rightStepProgress = 1f;

        private bool _feetInitialized;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            ResolveBones();
        }

        /// <summary>
        /// Bir karelik çözüm. Pozlar DÜNYA uzayındadır (çağıran arena→dünya dönüşümünü yapmıştır).
        /// ⚠️ <paramref name="head"/> <b>GÖZÜN</b> pozudur (<c>centerEyeAnchor</c>), kafa kemiğinin
        /// değil — dönüşümü <see cref="headBoneToEyeOffset"/> yapar (bkz. sınıf özeti).
        /// <see cref="LateUpdate"/> yerine dışarıdan çağrılır: sürücü <c>RemoteAvatar</c>'dır ve
        /// pozu ancak kayıt defterinden okuduktan sonra verebilir.
        /// </summary>
        public void Solve(in Pose head, in Pose handL, in Pose handR)
        {
            if (!_bonesResolved || _hips == null)
            {
                return;
            }

            // NaN/sonsuz poz: çizilecek makul hiçbir şey yok. Avatar son geçerli karesinde
            // bırakılır — bozuk sayıyı kemiklere yazmak Unity'de matris hatası basar ve iskelet
            // o kareden sonra geri gelmez.
            if (!IsFinite(head) || !IsFinite(handL) || !IsFinite(handR))
            {
                WarnUntrusted("poz NaN/sonsuz değer taşıyor", 0f, 0f, 0f);
                return;
            }

            Pose safeHead = Sanitize(head);
            Pose safeHandL = Sanitize(handL);
            Pose safeHandR = Sanitize(handR);

            float deltaTime = Time.deltaTime;
            float arenaGroundY = ArenaSpace.HasOrigin
                ? ArenaSpace.ArenaToWorld(Vector3.zero).y
                : 0f;

            // Gelen pozun arena zeminiyle uyumu: uyumsuzsa gövde ARENAYA göre değil KENDİNE göre
            // kurulur (bkz. sınıf özeti — yanlış yükseklikte ama bütün bir insan).
            // ⚠️ Ölçülen şey GÖZ yüksekliğidir (gelen poz gözün pozu); ölçek de ona bölünür.
            float eyeHeight = safeHead.position.y - arenaGroundY;
            bool trusted = eyeHeight >= MinPlausibleEyeHeight && eyeHeight <= MaxPlausibleEyeHeight;

            // Arena yeniden hizalandıysa boy ölçümü baştan alınır: hizalamadan ÖNCE ölçülen göz
            // yüksekliği arenanın zeminine değil, sistemin tahmin ettiği zemine göredir.
            int calibrationGeneration = ArenaCalibrator.CalibrationGeneration;
            if (calibrationGeneration != _calibrationGeneration)
            {
                _calibrationGeneration = calibrationGeneration;
                BeginHeightMeasurement();
            }

            RestoreBindPose();

            if (trusted)
            {
                ApplyScale(eyeHeight, deltaTime);
            }
            else
            {
                WarnUntrusted("göz arena zemininden makul olmayan yükseklikte",
                    eyeHeight, arenaGroundY, safeHead.position.y);
            }

            // ⚠️ Güvenilmez pozda zemin POZDAN türetilir. Arena zeminini kullanmak kökü gövdeden
            // metrelerce ayırır: ayak hedefi kalçanın üstüne çıkar ve bacaklar gövdeye sarılır.
            float groundY = trusted
                ? arenaGroundY
                : safeHead.position.y - ModelEyeHeight * _scale;

            if (trusted != _poseTrusted)
            {
                // Zemin referansı değişti: eski referansta basılan ayaklar artık anlamsız.
                _poseTrusted = trusted;
                _feetInitialized = false;
            }

            Quaternion yaw = SolveTorsoYaw(safeHead.rotation, deltaTime);

            // ⚠️ Gövde GÖZE değil KAFA KEMİĞİNE göre kurulur (bkz. sınıf özeti): iskeleti gözün
            // olduğu yere oturtmak yakayı near-clip'in içine sokuyordu. Ofset kemik-yereldir,
            // yani kafanın rotasyonuyla döner ve avatarın ölçeğiyle ölçeklenir.
            var headBone = new Pose(
                safeHead.position - safeHead.rotation * (headBoneToEyeOffset * _scale),
                safeHead.rotation);

            PlaceRoot(headBone.position, yaw, groundY);
            PlaceTorso(headBone, yaw);
            SolveArm(_leftArmChain, _leftHand, safeHandL, HandCorrection(false), _leftForearmTwistAxis);
            SolveArm(_rightArmChain, _rightHand, safeHandR, HandCorrection(true), _rightForearmTwistAxis);
            SolveLegs(yaw, deltaTime, groundY);
        }

        /// <summary>
        /// Avatar başka bir oyuncuya devredildiğinde ya da poz akışı baştan başladığında çağrılır:
        /// tahmin edilen HER ŞEYİ (boy, ölçek, gövde yaw'ı, basılan ayaklar) sıfırlar ve yeni
        /// oyuncunun boyu için ölçüm penceresini açar (<see cref="HeightMeasureDelaySeconds"/>).
        /// <para>⚠️ Bu olmadan önceki oyuncunun boyu/duruşu yeni oyuncuya miras kalır — hepsi
        /// mandallı tahminler olduğu için kendiliğinden düzelmezler.</para>
        /// </summary>
        public void ResetPoseState()
        {
            _standingEyeHeight = ModelEyeHeight;
            _recentMaxEyeHeight = ModelEyeHeight;
            _scale = 1f;
            _scaleInitialized = false;
            transform.localScale = Vector3.one;

            // Devralınan avatar yeni oyuncunun boyunu yeniden ölçer. Sayaç da tazelenir: aksi
            // hâlde ilk karede kalibrasyon kapısı da tetiklenir ve pencere iki kez başlardı.
            _calibrationGeneration = ArenaCalibrator.CalibrationGeneration;
            BeginHeightMeasurement();

            _yawInitialized = false;
            _feetInitialized = false;
            _leftStepProgress = 1f;
            _rightStepProgress = 1f;
            _poseTrusted = true;
            _lastUntrustedWarnTime = float.NegativeInfinity;
        }

        // -------------------------------------------------------------- poz denetimi

        /// <summary>Poz sonlu sayılardan mı oluşuyor (NaN/∞ kemiklere yazılmaz).</summary>
        private static bool IsFinite(in Pose pose)
        {
            Vector3 p = pose.position;
            Quaternion q = pose.rotation;
            return IsFinite(p.x) && IsFinite(p.y) && IsFinite(p.z) &&
                   IsFinite(q.x) && IsFinite(q.y) && IsFinite(q.z) && IsFinite(q.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>
        /// Rotasyonu kullanılabilir hâle getirir. ⚠️ <b>Sıfır quaternion telde meşru bir değer
        /// gibi görünür</b> (dört sıfır bayt) ama Unity'de geçersizdir: bir Transform'a yazılınca
        /// "Quaternion To Matrix conversion failed" basar ve o kemik o kareden sonra bozuk kalır
        /// (ölçüldü: ayak zeminin altına kilitleniyordu). Normalize edilemeyen rotasyon kimliğe
        /// düşürülür — yanlış yön, bozuk iskeletten iyidir.
        /// </summary>
        private static Pose Sanitize(in Pose pose)
        {
            Quaternion q = pose.rotation;
            float sqrMagnitude = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            if (sqrMagnitude < 0.5f || sqrMagnitude > 2f)
            {
                return new Pose(pose.position, Quaternion.identity);
            }

            return new Pose(pose.position, Quaternion.Normalize(q));
        }

        /// <summary>
        /// Güvenilmez pozu bir kez (sonra en fazla <see cref="UntrustedWarnCooldownSeconds"/>'de bir)
        /// SAYILARLA loglar: sahada "avatar tuhaf" demek yerine hangi uzayın kaydığı okunabilsin.
        /// </summary>
        private void WarnUntrusted(string reason, float eyeHeight, float groundY, float eyeWorldY)
        {
            if (Time.time - _lastUntrustedWarnTime < UntrustedWarnCooldownSeconds)
            {
                return;
            }

            _lastUntrustedWarnTime = Time.time;
            Debug.LogWarning(
                $"[ThreePointBodyIK] '{name}': {reason} " +
                $"(göz yüksekliği {eyeHeight:F2} m, arena zemini Y={groundY:F2}, göz dünya Y={eyeWorldY:F2}; " +
                $"makul aralık [{MinPlausibleEyeHeight:F2}, {MaxPlausibleEyeHeight:F2}] m). " +
                "Gönderen oyuncunun rig'i arenayla hizalı değil — kalibrasyon yapılmamış ya da " +
                "kayıtlı hizalama yanlış geri yüklenmiş olabilir. Gövde, zemini POZDAN türetilerek " +
                "bütün çizildi (yükseklik yanlış).", this);
        }

        // ------------------------------------------------------------ bind + ölçek

        /// <summary>
        /// Sürülen tüm kemikleri bind pozuna döndürür. Zorunludur: <see cref="ApplyLean"/> ve CCD
        /// kemiklere mevcut rotasyonun ÜSTÜNE yazıyor, sahnede de pozu sıfırlayan bir
        /// AnimatorController yok — sıfırlanmazsa hata her karede birikir ve gövde dakikalar
        /// içinde katlanır.
        /// </summary>
        private void RestoreBindPose()
        {
            for (int i = 0; i < _drivenBones.Length; i++)
            {
                Transform bone = _drivenBones[i];
                if (bone != null)
                {
                    bone.localRotation = _bindLocalRotations[i];
                    bone.localPosition = _bindLocalPositions[i];
                }
            }
        }

        /// <summary>
        /// Avatarı oyuncunun boyuna ölçekler. Model sabit boydadır; ölçeklenmezse kısa kollar ele
        /// yetişemez, bacaklar zemine değmez — tam da "uzamış/kopuk uzuv" görüntüsü buradan çıkar.
        /// <para>⚠️ Payda kafa kemiği DEĞİL <see cref="ModelEyeHeight"/>'tır: ölçülen büyüklük
        /// oyuncunun GÖZ yüksekliği, model tarafında da onun karşılığı kullanılmalı. Kafa kemiğine
        /// bölünüyordu ve avatar sistematik olarak ~%8 büyük çiziliyordu (Ch15'te kafa kemiği
        /// 1.56 m, göz ≈ 1.68 m) — büyüyen gövde oyuncunun yüzüne yaklaşan bir göğüs demekti.</para>
        /// <para>Ölçü <b>tetikten <see cref="HeightMeasureDelaySeconds"/> sn sonra ÖLÇÜLÜR ve
        /// sabitlenir</b> (bkz. sınıf özeti): oyuncu çömeldiğinde, eğildiğinde ya da zıpladığında
        /// avatarın boyu değişmez. Sabitlenene kadar geçici olarak kayan pencere maksimumu sürülür
        /// (iki kova, <see cref="StandingHeightWindowSeconds"/>) — 3 saniye boyunca model boyunda
        /// donmuş bir avatar, kısa oyuncunun kendi kollarını yanlış yerde görmesi olurdu.</para>
        /// <para>⚠️ Ölçek transform'a DOĞRUDAN yazılmaz, hedefe doğru YUMUŞATILIR
        /// (<see cref="ScaleFollowRatePerSecond"/>) — pencere devri tek karede atlıyor ve yerel
        /// gövdede bu "kolum uzayıp kısalıyor" olarak görülüyordu. <b>Ölçümün indiği kare bunun
        /// İSTİSNASIDIR:</b> orada hedef son kez değişir ve doğru boya ANINDA oturulur; yumuşatma,
        /// hedefi sürekli oynayan bir tahmin için vardır, kesinleşen bir ölçümü saniyelerce süren
        /// bir büyüme animasyonuna çevirmek için değil.</para>
        /// </summary>
        private void ApplyScale(float eyeHeightMeters, float deltaTime)
        {
            float modelEyeHeight = ModelEyeHeight;
            if (modelEyeHeight <= 0.01f || eyeHeightMeters <= 0.01f)
            {
                return;
            }

            // Sabitlendikten sonra hiçbir poz boyu değiştirmez — pencere de artık işlemez.
            bool measurementLanded = !_heightLocked && TrackStandingHeight(eyeHeightMeters, deltaTime);

            float target = Mathf.Clamp(_standingEyeHeight / modelEyeHeight, MinScale, MaxScale);

            if (!_scaleInitialized || measurementLanded)
            {
                // İlk kare: avatar zaten yeni görünüyor, yumuşatmanın gizleyeceği bir sıçrama yok.
                // Ölçüm karesi: hedef artık sabit, yumuşatacak bir şey kalmadı.
                _scaleInitialized = true;
                _scale = target;
            }
            else if (Mathf.Abs(target - _scale) > 1e-4f)
            {
                _scale = Mathf.MoveTowards(_scale, target, ScaleFollowRatePerSecond * deltaTime);
            }
            else
            {
                return;
            }

            transform.localScale = new Vector3(_scale, _scale, _scale);
        }

        /// <summary>
        /// Boy ölçüm penceresini açar: bir sonraki <see cref="HeightMeasureDelaySeconds"/> saniyelik
        /// güvenilir izlemenin sonunda <see cref="_standingEyeHeight"/> yeniden ÖLÇÜLÜR.
        /// <para>⚠️ Mevcut boy burada SIFIRLANMAZ (<see cref="ResetPoseState"/> ayrıca yapar):
        /// pencere boyunca avatar elindeki ölçüyle çizilmeye devam eder — sıfırlansaydı her
        /// hizalamadan sonra avatar üç saniyeliğine model boyuna atlar ve geri inerdi.</para>
        /// </summary>
        private void BeginHeightMeasurement()
        {
            _heightLocked = false;
            _measureTimer = 0f;
            _measureSum = 0f;
            _measureSamples = 0;
            _heightWindowTimer = 0f;
        }

        /// <summary>
        /// Açık ölçüm penceresini bir kare ilerletir; ölçüm bu karede indiyse <c>true</c> döner.
        /// Yalnız GÜVENİLİR karelerden çağrılır, yani süre de yalnız gerçek izleme varken işler.
        /// <para>Ölçüm penceresinin son <see cref="HeightMeasureAverageSeconds"/> saniyesinin
        /// ortalamasıdır — gerekçe sınıf özetinde.</para>
        /// <para>Pencere kapanana kadar boy geçici olarak kayan pencere maksimumundan sürülür:
        /// eğilen/çömelen oyuncuda anlık yüksekliğe uyulsaydı avatar her çömelmede küçülürdü,
        /// sonsuz maksimum ise tek yüksek örneğe kilitlenirdi.</para>
        /// </summary>
        private bool TrackStandingHeight(float eyeHeightMeters, float deltaTime)
        {
            _measureTimer += deltaTime;

            if (_measureTimer >= HeightMeasureDelaySeconds - HeightMeasureAverageSeconds)
            {
                _measureSum += eyeHeightMeters;
                _measureSamples++;
            }

            if (_measureTimer >= HeightMeasureDelaySeconds && _measureSamples > 0)
            {
                _heightLocked = true;
                _standingEyeHeight = _measureSum / _measureSamples;
                _recentMaxEyeHeight = _standingEyeHeight;
                _heightWindowTimer = 0f;
                return true;
            }

            if (eyeHeightMeters > _standingEyeHeight)
            {
                _standingEyeHeight = eyeHeightMeters;
            }

            if (eyeHeightMeters > _recentMaxEyeHeight)
            {
                _recentMaxEyeHeight = eyeHeightMeters;
            }

            _heightWindowTimer += deltaTime;
            if (_heightWindowTimer >= StandingHeightWindowSeconds)
            {
                // Pencere devri: geçmiş kova düşer, yerine son pencerenin maksimumu geçer.
                _heightWindowTimer = 0f;
                _standingEyeHeight = _recentMaxEyeHeight;
                _recentMaxEyeHeight = eyeHeightMeters;
            }

            return false;
        }

        // ------------------------------------------------------------------- gövde

        /// <summary>
        /// Gövde yaw'ı kafayı GECİKMELİ takip eder (anında yapışsaydı avatar her bakışta
        /// bütün gövdesiyle dönerdi). Fark <see cref="torsoMaxYawLagDegrees"/>'i aşınca gövde
        /// yetişir — sırtı dönük duran bir avatar oluşmasın diye.
        /// </summary>
        private Quaternion SolveTorsoYaw(Quaternion headRotation, float deltaTime)
        {
            Vector3 forward = headRotation * Vector3.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < 1e-6f)
            {
                // Kafa tam yukarı/aşağı bakıyor — yaw'ı kafanın tepesinden türet.
                forward = headRotation * Vector3.up;
                forward.y = 0f;
                if (forward.sqrMagnitude < 1e-6f)
                {
                    return _torsoYaw;
                }
            }

            Quaternion target = Quaternion.LookRotation(forward.normalized, Vector3.up);

            if (!_yawInitialized)
            {
                _yawInitialized = true;
                _torsoYaw = target;
                return _torsoYaw;
            }

            _torsoYaw = Quaternion.RotateTowards(_torsoYaw, target, torsoYawFollowSpeed * deltaTime);

            float lag = Quaternion.Angle(_torsoYaw, target);
            if (lag > torsoMaxYawLagDegrees)
            {
                _torsoYaw = Quaternion.RotateTowards(_torsoYaw, target, lag - torsoMaxYawLagDegrees);
            }

            return _torsoYaw;
        }

        /// <summary>Kökü zemine oturtur: yatay konum kafadan, yükseklik ARENA ZEMİNİNDEN gelir.
        /// <para>Zemin kafadan sabit bir boy çıkararak bulunmaz — oyuncu eğildiğinde avatar yere
        /// gömülürdü.</para></summary>
        private void PlaceRoot(Vector3 headPosition, Quaternion yaw, float groundY)
        {
            transform.SetPositionAndRotation(
                new Vector3(headPosition.x, groundY, headPosition.z), yaw);
        }

        /// <summary>
        /// Kalça + omurga + kafa. Kalçanın kafadan düşüşü karakterin KENDİ bind pozundan ölçülür
        /// (ölçekle çarpılır) — sabit bir sayı yazılsaydı model değiştiğinde ya da oyuncu
        /// ölçeklendiğinde omurga gerilirdi.
        /// <para>Omurga eğimi kalçadan kafaya giden vektörden gelir ve zincire PAYLAŞTIRILIR
        /// (tek kemiğe verilseydi bel kırılır gibi görünürdü).</para>
        /// </summary>
        private void PlaceTorso(in Pose head, Quaternion yaw)
        {
            Vector3 hipsPosition = head.position
                                   - Vector3.up * (_modelHipsDrop * _scale)
                                   - (yaw * Vector3.forward) * (hipsBackOffsetMeters * _scale);

            _hips.SetPositionAndRotation(hipsPosition, yaw);

            Vector3 spineDirection = head.position - hipsPosition;
            if (spineDirection.sqrMagnitude > 1e-6f)
            {
                Quaternion lean = Quaternion.FromToRotation(Vector3.up, spineDirection.normalized);

                // Eğim omurga kemiklerine eşit paylaştırılır; kaç kemik olduğu karaktere göre değişir.
                int segments = 0;
                if (_spine != null) segments++;
                if (_chest != null) segments++;
                if (_upperChest != null) segments++;

                if (segments > 0)
                {
                    Quaternion share = Quaternion.Slerp(Quaternion.identity, lean, 1f / segments);
                    ApplyLean(_spine, share);
                    ApplyLean(_chest, share);
                    ApplyLean(_upperChest, share);
                }
            }

            if (_neck != null)
            {
                // Boyun kafayı taşır ama kafanın kendi rotasyonu aşağıda birebir yazılır.
                _neck.rotation = Quaternion.Slerp(_neck.rotation, head.rotation, 0.5f);
            }

            if (_head != null)
            {
                // ⚠️ Yalnız ROTASYON: kafa kemiğine konum yazmak onu boyundan koparır (boyun mesh'i
                // gerilir). Konumu zaten kalça + omurga zinciri getiriyor.
                _head.rotation = head.rotation;
            }
        }

        private static void ApplyLean(Transform bone, Quaternion share)
        {
            if (bone != null)
            {
                bone.rotation = share * bone.rotation;
            }
        }

        // -------------------------------------------------------------------- kol

        /// <summary>
        /// Kolu ele ulaştırır (CCD), sonra elin KENDİ rotasyonunu yazar: silah tutuşu elin
        /// yönünden okunuyor, çözücünün bulduğu yönelim yeterli değil.
        /// <para>⚠️ Rotasyon <b>çarpımla</b> yazılır: gelen poz kumanda anchor'ı uzayındadır,
        /// kemik ise karakterin bind eksenindedir. <paramref name="correction"/> bu iki eksen
        /// arasındaki köprüdür ve <see cref="ResolveBones"/> içinde karakterin bind pozundan
        /// ölçülür (<see cref="HandGripConvention"/>); doğrudan atansaydı bilek ters çizilirdi.</para>
        /// <para>⚠️ El kemiğine KONUM yazılmaz. Hedef kolun erişemeyeceği kadar uzaktayken
        /// (kumanda pozu bileğin değil avucun ötesindedir) konum yazmak eli önkoldan koparır ve
        /// mesh gerilir; ulaşılamayan hedefte kol kısa kalsın, kopmasın.</para>
        /// </summary>
        private void SolveArm(
            Transform[] chain,
            Transform hand,
            in Pose handPose,
            in Quaternion correction,
            Vector3 twistAxis)
        {
            if (chain == null || hand == null)
            {
                return;
            }

            SolveChain(chain, handPose.position);

            Quaternion target = handPose.rotation * correction;

            // ⚠️ Burulma ELDEN ÖNCE önkola devredilir; el yine MUTLAK yazılır (hedef değişmiyor).
            DistributeWristTwist(chain[1], target, twistAxis);
            hand.rotation = target;
        }

        /// <summary>
        /// Bileğin kendi ekseni etrafındaki dönüşünün bir payını önkola devreder — gerçek önkolun
        /// radius/ulna burulmasının karşılığı.
        /// <para>
        /// ⚠️ <b>Neden gerekli:</b> ele MUTLAK rotasyon yazılıyor, CCD ise önkola roll VEREMİYOR
        /// (<c>Quaternion.FromToRotation</c> minimum yaydır, kemiğin kendi ekseni etrafında sıfır
        /// dönüş üretir) ve Mixamo rig'inde <b>twist kemiği yok</b>. Yani oyuncu bileğini çevirdiğinde
        /// (tüfek tutarken sürekli) dönüşün TAMAMI tek eklemde birikiyor ve lineer blend skinning
        /// bileği "şeker ambalajı" gibi çökertiyor: bilek incelip kalınlaşıyor. Quest'te
        /// (QualitySettings "Mobile", vertex başına 2 kemik) etki daha da belirgin.
        /// </para>
        /// <para>
        /// ⚠️ Bu düzeltme elin YERİNİ bozmaz: döndürülen eksen zaten önkoldan ele giden eksendir,
        /// el o eksenin ÜZERİNDE durur. Bu yüzden CCD'nin bulduğu çözüm geçerli kalır ve düzeltme
        /// çözücüden sonra uygulanabilir.
        /// </para>
        /// </summary>
        private void DistributeWristTwist(Transform lowerArm, Quaternion handTarget, Vector3 twistAxis)
        {
            if (lowerArm == null || forearmTwistShare <= 0f || twistAxis.sqrMagnitude < 0.5f)
            {
                return;
            }

            Quaternion local = Quaternion.Inverse(lowerArm.rotation) * handTarget;
            Quaternion twist = ExtractTwist(local, twistAxis);
            lowerArm.rotation *= Quaternion.Slerp(Quaternion.identity, twist, forearmTwistShare);
        }

        /// <summary>
        /// Swing-twist ayrıştırmasının twist yarısı: rotasyonun <paramref name="axis"/> etrafındaki
        /// bileşeni. Quaternion'ın vektör kısmı eksene izdüşürülür, kalan normalize edilir.
        /// <para>Dejenere durumda (dönüş ekseni <paramref name="axis"/>'e dik) kimlik döner —
        /// devredilecek burulma yok demektir.</para>
        /// </summary>
        private static Quaternion ExtractTwist(Quaternion rotation, Vector3 axis)
        {
            var vector = new Vector3(rotation.x, rotation.y, rotation.z);
            Vector3 projection = Vector3.Project(vector, axis);

            float magnitude = Mathf.Sqrt(
                projection.x * projection.x + projection.y * projection.y +
                projection.z * projection.z + rotation.w * rotation.w);

            if (magnitude < 1e-6f)
            {
                return Quaternion.identity;
            }

            return new Quaternion(
                projection.x / magnitude,
                projection.y / magnitude,
                projection.z / magnitude,
                rotation.w / magnitude);
        }

        /// <summary>
        /// Ölçek telafili CCD çağrısı — <b>çözücüye ham dünya hedefi verilmez</b>.
        /// <para>
        /// ⚠️ <b>Meta'nın <see cref="IKUtilities.SolveCCDIK"/>'i ÖLÇEĞİ görmezden gelir:</b> zinciri
        /// önbelleğe alırken <c>parent.InverseTransformPoint(bone.position)</c> ile ölçeği BÖLER,
        /// ama zinciri geri kurarken (<c>position += rotation * pose.position</c>) geri ÇARPMAZ.
        /// Yani çözücünün kafasındaki kol, gerçek kolun <c>1/S</c> katıdır (S = zincir kökünün
        /// dünya ölçeği). Avatar oyuncunun boyuna ölçeklendiği için S neredeyse hiç 1 değildir:
        /// S=1.09'da çözücü kolu ~4.5 cm kısa sanır, ölçek tavanında (<see cref="MaxScale"/>)
        /// ~17 cm. Kemikler gerilmez (yalnız rotasyon yazılıyor) ama <b>el hedefi sistematik olarak
        /// ıskalar</b> ve ıskalama kolun yönüyle değiştiği için "kol uzayıp kısalıyor" görünür.
        /// </para>
        /// <para>
        /// Çözücünün iç modeli, gerçek zincirin KÖK NOKTASI etrafında <c>1/S</c> ile ölçeklenmiş
        /// hâlidir — yani hedefi aynı dönüşümden geçirmek çözümü <b>birebir</b> doğru yapar. Aynı
        /// sebeple tolerans da <c>S²</c>'ye bölünür: çözücü mesafeyi o sahte uzayda karesiyle
        /// karşılaştırıyor.
        /// </para>
        /// <para>⚠️ Bu bir üçüncü parti davranışının TELAFİSİDİR. SDK bir gün düzeltirse telafi
        /// çift sayılır — güncellemede bu metot doğrulanmalıdır.</para>
        /// </summary>
        private void SolveChain(Transform[] chain, Vector3 worldTarget)
        {
            Transform chainRoot = chain[chain.Length - 1];

            // ⚠️ GİZLENMİŞ uzuv hiç çözülmez. LocalAvatarBoneHider görünmesini istemediği kemiği
            // sıfıra yakın ÖLÇEKLİYOR; çözücü ise zinciri önbelleğe alırken o ölçeğe BÖLÜYOR
            // (InverseTransformPoint) → iç modeli 10.000× şişiyor. Görünmeyen bir uzuv için hem
            // anlamsız hem sayısal olarak tehlikeli bir iş. Kapı ölçeğe bakar, gizleyicinin
            // listesine DEĞİL: liste iki yerde durursa er geç birbirinden sapar.
            if (chainRoot.lossyScale.x <= DegenerateBoneScale)
            {
                return;
            }

            Transform chainParent = chainRoot.parent;
            Vector3 target = worldTarget;
            float tolerance = SolverTolerance;

            if (chainParent != null)
            {
                float scale = chainParent.lossyScale.x;
                if (scale > 1e-4f && Mathf.Abs(scale - 1f) > 1e-4f)
                {
                    Vector3 root = chainParent.position;
                    target = root + (worldTarget - root) / scale;
                    tolerance /= scale * scale;
                }
            }

            IKUtilities.SolveCCDIK(chain, target, tolerance, armIterations);
        }

        // ----------------------------------------------------------------- bacaklar

        /// <summary>
        /// Prosedürel adım: her ayağın bastığı nokta saklanır; gövde yeterince uzaklaşınca o ayak
        /// yeni yerine bir yay çizerek gider. AYNI ANDA iki ayak birden adım atmaz — atsaydı
        /// avatar zıplıyormuş gibi görünürdü.
        /// <para>Hedef yüksekliği zemin DEĞİL, zemin + ayak bileği yüksekliğidir: IK'nın ucu ayak
        /// kemiği (bilek) olduğu için zemine hizalanırsa ayak tabanı yere gömülür.</para>
        /// </summary>
        private void SolveLegs(Quaternion yaw, float deltaTime, float groundY)
        {
            if (_leftLegChain == null || _rightLegChain == null)
            {
                return;
            }

            Vector3 root = transform.position;
            root.y = groundY + _modelAnkleHeight * _scale;

            Vector3 right = yaw * Vector3.right;
            float halfWidth = stanceHalfWidth * _scale;
            Vector3 leftTarget = root - right * halfWidth;
            Vector3 rightTarget = root + right * halfWidth;

            // ⚠️ Işınlanma emniyeti — ADIM eşiğinden AYRI: hedef bastığı noktadan bu kadar
            // uzaktaysa oraya adımla gidilmez, ayak ziplatılır. Yoksa avatar sıçradığında (harita
            // değişimi, kalibrasyon düzeltmesi, bozuk kare) ayak eski dünya noktasında kalıp
            // bacağı metrelerce uzatıyor ve o poz bir daha düzelmiyordu.
            float resnapDistance = FootResnapDistance * _scale;
            if (!_feetInitialized ||
                Vector3.Distance(_leftFootPlanted, leftTarget) > resnapDistance ||
                Vector3.Distance(_rightFootPlanted, rightTarget) > resnapDistance)
            {
                _feetInitialized = true;
                _leftFootPlanted = _leftFootFrom = leftTarget;
                _rightFootPlanted = _rightFootFrom = rightTarget;
                _leftStepProgress = 1f;
                _rightStepProgress = 1f;
            }

            bool leftStepping = _leftStepProgress < 1f;
            bool rightStepping = _rightStepProgress < 1f;

            // Adım başlatma — sırayla, hiç değilse bir ayak yerde kalsın.
            if (!leftStepping && !rightStepping)
            {
                float trigger = stepTriggerDistance * _scale;
                float leftError = Vector3.Distance(_leftFootPlanted, leftTarget);
                float rightError = Vector3.Distance(_rightFootPlanted, rightTarget);

                if (leftError > trigger && leftError >= rightError)
                {
                    _leftFootFrom = _leftFootPlanted;
                    _leftStepProgress = 0f;
                    leftStepping = true;
                }
                else if (rightError > trigger)
                {
                    _rightFootFrom = _rightFootPlanted;
                    _rightStepProgress = 0f;
                    rightStepping = true;
                }
            }

            Vector3 leftFoot = AdvanceStep(
                ref _leftStepProgress, ref _leftFootPlanted, _leftFootFrom, leftTarget, leftStepping, deltaTime);
            Vector3 rightFoot = AdvanceStep(
                ref _rightStepProgress, ref _rightFootPlanted, _rightFootFrom, rightTarget, rightStepping, deltaTime);

            SolveChain(_leftLegChain, leftFoot);
            SolveChain(_rightLegChain, rightFoot);
        }

        /// <summary>Adımı bir kare ilerletir; adım yoksa ayak bastığı yerde kalır.</summary>
        private Vector3 AdvanceStep(
            ref float progress, ref Vector3 planted, Vector3 from, Vector3 target, bool stepping, float deltaTime)
        {
            if (!stepping)
            {
                return planted;
            }

            progress = stepDuration > 0f
                ? Mathf.Min(1f, progress + deltaTime / stepDuration)
                : 1f;

            Vector3 position = Vector3.Lerp(from, target, progress);
            position.y += Mathf.Sin(progress * Mathf.PI) * stepArcHeight * _scale;

            if (progress >= 1f)
            {
                planted = target;
                return target;
            }

            return position;
        }

        // ----------------------------------------------------------------- kurulum

        /// <summary>Humanoid Avatar'dan kemikleri toplar. Omuz/üst göğüs gibi İSTEĞE BAĞLI
        /// kemikler karakterde olmayabilir — zincirler o duruma göre kurulur.
        /// <para>Aynı geçişte karakterin bind pozu (her kare geri dönülecek referans) ve gövde
        /// ölçüleri (kalça düşüşü, ayak bileği yüksekliği) kaydedilir.</para></summary>
        private void ResolveBones()
        {
            if (_animator == null || !_animator.isHuman)
            {
                Debug.LogWarning(
                    "[ThreePointBodyIK] Animator humanoid değil; IK devre dışı. " +
                    "Karakterin import ayarında Animation Type = Humanoid olmalı.", this);
                return;
            }

            _hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            _spine = _animator.GetBoneTransform(HumanBodyBones.Spine);
            _chest = _animator.GetBoneTransform(HumanBodyBones.Chest);
            _upperChest = _animator.GetBoneTransform(HumanBodyBones.UpperChest);
            _neck = _animator.GetBoneTransform(HumanBodyBones.Neck);
            _head = _animator.GetBoneTransform(HumanBodyBones.Head);

            _leftHand = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
            _rightHand = _animator.GetBoneTransform(HumanBodyBones.RightHand);

            // ⚠️ Zincirler UÇ→KÖK sıralanır: Meta'nın CCD çözücüsü effector olarak dizinin
            // 0. elemanını alır ve zincirin kökünü son elemanın EBEVEYNİNDEN bulur.
            _leftArmChain = BuildChain(
                _leftHand,
                _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm),
                _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm));
            _rightArmChain = BuildChain(
                _rightHand,
                _animator.GetBoneTransform(HumanBodyBones.RightLowerArm),
                _animator.GetBoneTransform(HumanBodyBones.RightUpperArm));

            _leftLegChain = BuildChain(
                _animator.GetBoneTransform(HumanBodyBones.LeftFoot),
                _animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg),
                _animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg));
            _rightLegChain = BuildChain(
                _animator.GetBoneTransform(HumanBodyBones.RightFoot),
                _animator.GetBoneTransform(HumanBodyBones.RightLowerLeg),
                _animator.GetBoneTransform(HumanBodyBones.RightUpperLeg));

            _bonesResolved = _hips != null;

            if (!_bonesResolved)
            {
                Debug.LogWarning("[ThreePointBodyIK] Hips kemiği bulunamadı; IK devre dışı.", this);
                return;
            }

            CacheBindPose();
            MeasureModel();
            MeasureHandCorrections();
            MeasureTwistAxes();
        }

        /// <summary>Önkolların burulma eksenini bind pozunda ölçer (bkz.
        /// <see cref="_leftForearmTwistAxis"/>). ⚠️ Poz bozulmadan ÖNCE çağrılmalıdır.</summary>
        private void MeasureTwistAxes()
        {
            _leftForearmTwistAxis = MeasureTwistAxis(_leftArmChain);
            _rightForearmTwistAxis = MeasureTwistAxis(_rightArmChain);
        }

        /// <summary>Zincirin önkol→el yönü, ÖNKOL-yerel uzayda. Ölçülemezse sıfır vektör.</summary>
        private static Vector3 MeasureTwistAxis(Transform[] chain)
        {
            if (chain == null)
            {
                return Vector3.zero;
            }

            Transform hand = chain[0];
            Transform lowerArm = chain[1];
            Vector3 axis = lowerArm.InverseTransformDirection(hand.position - lowerArm.position);
            return axis.sqrMagnitude > 1e-8f ? axis.normalized : Vector3.zero;
        }

        /// <summary>
        /// El kemiklerinin bind eksenini ölçüp izleme uzayına köprüyü kurar
        /// (<see cref="HandGripConvention"/>).
        /// <para>⚠️ <b>Bind pozu bozulmadan ÖNCE</b> çağrılmalıdır: <see cref="Awake"/> anında
        /// henüz hiçbir <see cref="Solve"/> koşmamıştır, sonraki bir kare ölçülseydi baz o karenin
        /// duruşunu içerirdi ve düzeltme kalıcı olarak yanlış çıkardı.</para>
        /// </summary>
        private void MeasureHandCorrections()
        {
            MeasureHandBasis(
                _leftHand,
                _animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal),
                _animator.GetBoneTransform(HumanBodyBones.LeftThumbProximal),
                false,
                ref _leftHandBoneBasis,
                ref _leftHandBasisMeasured);

            MeasureHandBasis(
                _rightHand,
                _animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal),
                _animator.GetBoneTransform(HumanBodyBones.RightThumbProximal),
                true,
                ref _rightHandBoneBasis,
                ref _rightHandBasisMeasured);
        }

        /// <summary>Tek elin bind eksenini ölçer; parmak kemikleri humanoid'de İSTEĞE BAĞLI olduğu
        /// için ölçüm düşebilir — o zaman köprü kurulmaz ve durum AÇIKÇA loglanır.</summary>
        private void MeasureHandBasis(
            Transform hand,
            Transform middleProximal,
            Transform thumbProximal,
            bool rightHand,
            ref Quaternion boneBasis,
            ref bool measured)
        {
            measured = HandGripConvention.TryMeasureBoneBasis(
                hand, middleProximal, thumbProximal, rightHand, out boneBasis);

            if (measured)
            {
                return;
            }

            boneBasis = Quaternion.identity;
            Debug.LogWarning(
                $"[ThreePointBodyIK] '{name}': {(rightHand ? "SAĞ" : "SOL")} elin bind ekseni " +
                "ölçülemedi (orta parmak / başparmak kemiği yok ya da yönleri dejenere). " +
                "Humanoid'de parmak kemikleri isteğe bağlıdır; karakterin Avatar eşlemesinde " +
                "MiddleProximal ve ThumbProximal atanmalı. Düzeltme kimlik bırakıldı — " +
                "bu elin BİLEĞİ YANLIŞ YÖNDE çizilecek.", this);
        }

        /// <summary>
        /// O elin izleme uzayı → kemik ekseni köprüsü, ince ayar dahil. Her karede kurulur
        /// (gerekçe: <see cref="_leftHandBoneBasis"/>).
        /// </summary>
        private Quaternion HandCorrection(bool rightHand)
        {
            if (!(rightHand ? _rightHandBasisMeasured : _leftHandBasisMeasured))
            {
                return Quaternion.identity;
            }

            return HandGripConvention.Correction(
                rightHand,
                rightHand ? _rightHandBoneBasis : _leftHandBoneBasis,
                rightHand ? rightHandTuningEuler : leftHandTuningEuler);
        }

        /// <summary>Sürülen kemiklerin bind pozunu saklar — her kare buraya dönülür.</summary>
        private void CacheBindPose()
        {
            var bones = new System.Collections.Generic.List<Transform>
            {
                _hips, _spine, _chest, _upperChest, _neck, _head
            };

            AddChain(bones, _leftArmChain);
            AddChain(bones, _rightArmChain);
            AddChain(bones, _leftLegChain);
            AddChain(bones, _rightLegChain);

            bones.RemoveAll(b => b == null);

            _drivenBones = bones.ToArray();
            _bindLocalRotations = new Quaternion[_drivenBones.Length];
            _bindLocalPositions = new Vector3[_drivenBones.Length];

            for (int i = 0; i < _drivenBones.Length; i++)
            {
                _bindLocalRotations[i] = _drivenBones[i].localRotation;
                _bindLocalPositions[i] = _drivenBones[i].localPosition;
            }
        }

        private static void AddChain(System.Collections.Generic.List<Transform> target, Transform[] chain)
        {
            if (chain == null)
            {
                return;
            }

            for (int i = 0; i < chain.Length; i++)
            {
                if (!target.Contains(chain[i]))
                {
                    target.Add(chain[i]);
                }
            }
        }

        /// <summary>
        /// Karakterin bind pozundaki ölçüleri (kök uzayında, ölçek 1 iken). Sabit sayı yerine
        /// modelden okunur: başka bir karaktere geçildiğinde tek satır değişmesin diye.
        /// </summary>
        private void MeasureModel()
        {
            Vector3 hipsLocal = transform.InverseTransformPoint(_hips.position);

            if (_head != null)
            {
                Vector3 headLocal = transform.InverseTransformPoint(_head.position);
                _modelHeadHeight = headLocal.y;
                _modelHipsDrop = Mathf.Max(0.05f, headLocal.y - hipsLocal.y);
            }
            else
            {
                _modelHeadHeight = hipsLocal.y;
                _modelHipsDrop = 0f;
            }

            Transform foot = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            _modelAnkleHeight = foot != null
                ? Mathf.Max(0f, transform.InverseTransformPoint(foot.position).y)
                : 0f;

            _standingEyeHeight = ModelEyeHeight;
            _recentMaxEyeHeight = ModelEyeHeight;
        }

        /// <summary>Üç kemikten zincir kurar; biri bile eksikse zincir kurulmaz (CCD en az iki
        /// kemik ister ve yarım zincir sessizce yanlış poz üretir).
        /// <para>⚠️ Sıra UÇ→KÖK verilir (<c>{ el, önkol, üstkol }</c>).</para></summary>
        private static Transform[] BuildChain(Transform tip, Transform middle, Transform root)
        {
            if (tip == null || middle == null || root == null)
            {
                return null;
            }

            return new[] { tip, middle, root };
        }
    }
}
