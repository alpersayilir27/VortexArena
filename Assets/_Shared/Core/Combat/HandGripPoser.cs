using System.Collections.Generic;
using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Elde tutulan silahın <b>yakalanmış</b> kavramasını ISDK'nın <b>sentetik eline</b> uygular:
    /// bilek kavrama noktasına kilitlenir, parmaklar izlemeye/kumandaya bırakılır. Oyuncunun
    /// gözlükte gördüğü el budur (<c>OVRHandVisualLeft/Right</c> bu sentetik elden sürülüyor).
    /// <para>
    /// ⚠️ <b>Silahın pozuna DOKUNMAZ.</b> Silahın dünya pozunun tek yazarı
    /// <c>Weapon.ApplyCanonicalGrip</c> + <see cref="ItemGripSolver"/>'dır; bu sınıf yalnız ELİ sürer.
    /// İki yazar olsaydı aynı silah kendi ekranında başka, karşı ekranda (ağdan gelen poz) başka
    /// görünürdü.
    /// </para>
    /// <para>
    /// ⚠️ <b>Yalnız YEREL oyuncu.</b> Uzak avatarın eli ağdan gelen iskeletle çizilir
    /// (<c>RemoteAvatar</c>) ve o yol buraya hiç uğramaz. Ağ tarafında bu sınıfın hiçbir işi yoktur,
    /// protokolde karşılığı yoktur.
    /// </para>
    /// <para>
    /// <b>Neden kendini önyükleyen kalıcı tekil</b> (<see cref="WeaponGranter"/> deseni): sahneye
    /// bileşen konsaydı her yeni arenaya elle bir kurulum adımı doğardı. Bu bileşen görünmezdir ve
    /// rig yoksa (admin gözlemci, sahne henüz yüklenmedi) sessizce hiçbir şey yapmaz.
    /// </para>
    /// </summary>
    /// <remarks>
    /// ⚠️ <b><see cref="DefaultExecutionOrder"/> bilinçlidir:</b> <c>Weapon.LateUpdate</c> silahın
    /// pozunu yazıyor, biz de bileği o poza kilitliyoruz — ondan SONRA koşmalıyız, yoksa el bir kare
    /// gerideki silaha sarılır ve hızlı hareket sırasında titrer. Bunu Project Settings'teki
    /// <c>Script Execution Order</c> ile yapmıyoruz: proje ayarı repo genelinde görünmez bir bağımlılık
    /// olurdu, attribute ise sebebiyle birlikte kodda durur.
    /// </remarks>
    [DefaultExecutionOrder(100)]
    public class HandGripPoser : MonoBehaviour
    {
        /// <summary>Sentetik el bulunamadığında yeniden arama aralığı (sahne yeni yüklenmiş olabilir).</summary>
        private const float RescanSeconds = 1f;

        /// <summary>
        /// Oyuncunun GÖRDÜĞÜ sentetik elin düğüm adı.
        /// <para>
        /// ⚠️ Ad filtresi şart: rig'in altında başka <see cref="SyntheticHand"/>'ler de var
        /// (<c>Reticle</c> dalındaki <c>LeftHandSynthetic</c>/<c>RightHandSynthetic</c> = mesafeden
        /// kavramanın hayalet eli). Filtresiz arama ilk bulduğunu sürer ve oyuncu ellerinin
        /// kıpırdamadığını, bunun yerine odanın öbür ucundaki hayalet elin silaha sarıldığını görür.
        /// </para>
        /// </summary>
        private const string SyntheticHandNodeName = "SyntheticHandData";

        public static HandGripPoser Instance { get; private set; }

        /// <summary>
        /// Poz UYGULAMASI askıya alındı mı (el çözümü ve delta ölçümü koşmaya devam eder).
        /// <para>
        /// ⚠️ <b>Kavrama kalibrasyonu bunu açmak ZORUNDA:</b> orada sentetik el HAM izlemeyi
        /// göstermeli — idle kilidi parmakları dondurur, yani oyuncu pinch yaptığını elinde
        /// göremez ve ölçtüğü kavramanın gerçekten o duruş olduğuna güvenemez.
        /// </para>
        /// <para>
        /// ⚠️ <b>El çözümünü ve <see cref="TryGetAnchorToWrist"/> ölçümünü DURDURMAZ</b>: askıdayken
        /// de bilek okunabilmeli (<see cref="TryGetTrackedWrist"/>), yoksa kalibrasyon ikinci bir
        /// okuma yolu açmak zorunda kalır ve iki uç sessizce sapardı.
        /// </para>
        /// </summary>
        public static bool Suspended { get; set; }

        private SyntheticHand _left;
        private SyntheticHand _right;

        /// <summary>Elin BİLEĞİ şu an silah kavramasına kilitli mi. Parmaklar bundan bağımsız her
        /// zaman bizim yazdığımızdır (silah pozu ya da idle).</summary>
        private bool _leftLocked;
        private bool _rightLocked;

        /// <summary>Örneklenmiş gevşek el duruşu (el başına, bir kez) — gerekçe
        /// <see cref="ApplyIdle"/>'da. Sahne değişiminde SIFIRLANMAZ: donanımdan gelir, sahneden değil.</summary>
        private Quaternion[] _idleLeft;
        private Quaternion[] _idleRight;

        /// <summary>Kumanda anchor'ından İZLENEN bileğe delta (anchor uzayı) — el başına, her karede
        /// tazelenir. Gerekçe <see cref="TryGetAnchorToWrist"/>'te.</summary>
        private Pose _anchorToWristLeft;
        private Pose _anchorToWristRight;
        private bool _hasAnchorToWristLeft;
        private bool _hasAnchorToWristRight;

        /// <summary>Parmak sayısı — ISDK garantisi (bkz. <see cref="Apply"/> içindeki serbestlik
        /// dizisi notu).</summary>
        private const int FingerCount = 5;

        /// <summary>Bu eşiğin üstünde bir tetik/grip girdisi varken idle örneklenmez.</summary>
        private const float IdleCaptureInputEpsilon = 0.05f;

        private float _nextScanAt;

        /// <summary>Pozu eksik silahlar için "oturum başına bir kez" uyarı kaydı.</summary>
        private readonly HashSet<string> _warned = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var go = new GameObject("[HandGripPoser]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<HandGripPoser>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            SceneManager.sceneLoaded -= HandleSceneLoaded;
            Instance = null;
        }

        /// <summary>Yeni sahne = yeni rig: önbellek ölür, ilk karede yeniden aranır.</summary>
        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _left = null;
            _right = null;
            _leftLocked = false;
            _rightLocked = false;
            _hasAnchorToWristLeft = false;
            _hasAnchorToWristRight = false;
            _nextScanAt = 0f;
        }

        // --------------------------------------------------- anchor → izlenen bilek deltası

        /// <summary>
        /// Kumanda anchor'ından <b>izlenen bileğe</b> olan sabit delta (anchor uzayında, metre);
        /// ölçülemiyorsa <c>false</c>.
        /// <para>
        /// <b>Ne işe yarar:</b> silahın eldeki duruşunun tek yazılı kaynağı tanımdaki yakalanmış
        /// kavramadır (<see cref="ItemGripCapture"/>) ve o, bileği <b>silaha göre</b> tarif eder. Silahın
        /// dünya pozunu çözen taraf ise elin ANCHOR pozunu biliyor. İki uç arasındaki köprü bu
        /// deltadır: <c>bilekDünya = anchor ∘ delta</c>. Onsuz kavrama tahmin sabitlerine düşer.
        /// </para>
        /// <para>
        /// ⚠️ <b>Kanonikliği bozmaz</b> (§6.6): kumanda sürümlü el pozları deterministiktir — aynı
        /// kumanda, aynı SDK, aynı sonuç. Yani her başlıkta AYNI delta ölçülür; duruş yine telde
        /// gitmez ve tel formatı değişmez.
        /// </para>
        /// <para>
        /// ⚠️ Ölçü sentetik elin KAYNAĞINDAN alınır (<c>ModifyDataFromSource</c>), sentetik elin
        /// kendisinden değil: bileği zaten biz kilitliyoruz (<see cref="Apply"/>), kilitli eli okumak
        /// "silahın nerede olduğunu silahın kendisine sormak" olurdu ve ölçü bir kare içinde kendi
        /// çıktısına kilitlenirdi.
        /// </para>
        /// <para>
        /// ⚠️ Değer <b>bir kare bayattır</b> (bu bileşen <see cref="DefaultExecutionOrder"/> 100 ile
        /// silahtan SONRA koşar) ve bu bilinçlidir: delta fiziksel olarak sabit bir ofsettir, bir
        /// karelik gecikmesi görünmez. Ölçümü öne almak için execution order'ı bozmak, elin bir kare
        /// gerideki silaha sarılması pahasına olurdu.
        /// </para>
        /// </summary>
        public static bool TryGetAnchorToWrist(bool rightHand, out Pose delta)
        {
            delta = default;

            HandGripPoser instance = Instance;
            if (instance == null)
            {
                return false;
            }

            if (rightHand)
            {
                delta = instance._anchorToWristRight;
                return instance._hasAnchorToWristRight;
            }

            delta = instance._anchorToWristLeft;
            return instance._hasAnchorToWristLeft;
        }

        /// <summary>
        /// İzlenen bileğin <b>DÜNYA</b> pozu; okunamıyorsa <c>false</c>.
        /// <para>
        /// ⚠️ <b>Kavrama kalibrasyonunun ölçtüğü bilek ile oyunun kullandığı bilek AYNI kaynaktan
        /// okunmak zorunda</b> (aynı sentetik el, aynı izleme→dünya çevirisi): ikinci bir okuma yolu
        /// açmak, iki ucun sessizce sapması ve yakalanan kavramanın oyunda birkaç santim kaymış
        /// olarak uygulanması demekti.
        /// </para>
        /// <para><see cref="Suspended"/> iken de çalışır — el çözümü ve ölçüm askıdan bağımsızdır,
        /// yalnız poz UYGULAMASI atlanır.</para>
        /// </summary>
        public static bool TryGetTrackedWrist(bool rightHand, out Pose worldWrist)
        {
            worldWrist = default;

            HandGripPoser instance = Instance;
            if (instance == null)
            {
                return false;
            }

            return TryReadSourceWrist(rightHand ? instance._right : instance._left, out worldWrist);
        }

        /// <summary>
        /// Önbellekteki sentetik el (rig yoksa / henüz bağlanmadıysa <c>null</c>).
        /// <para>El çözümünün TEK yolu bu sınıftır (<see cref="Scan"/>); ihtiyacı olan taraf
        /// (kalibrasyon, elin kendi <c>IHand</c>'ini okuyanlar) kendi aramasını açmasın diye
        /// buradan verilir.</para>
        /// </summary>
        public static SyntheticHand GetSynthetic(bool rightHand)
        {
            HandGripPoser instance = Instance;
            if (instance == null)
            {
                return null;
            }

            return rightHand ? instance._right : instance._left;
        }

        /// <summary>Bir elin deltasını tazeler; ölçülemezse o elin bayrağını düşürür.</summary>
        private void RefreshAnchorToWrist(SyntheticHand synthetic, OVRInput.Controller hand, bool rightHand)
        {
            bool measured = TryMeasureAnchorToWrist(synthetic, hand, out Pose delta);

            if (rightHand)
            {
                _hasAnchorToWristRight = measured;
                _anchorToWristRight = measured ? delta : default;
                return;
            }

            _hasAnchorToWristLeft = measured;
            _anchorToWristLeft = measured ? delta : default;
        }

        /// <summary>
        /// Anchor ile izlenen bilek arasındaki farkı ölçer.
        /// <para>⚠️ <c>Transform.InverseTransformPoint</c> DEĞİL elle bileşim: sonuç METREdir ve
        /// rig'in ölçeği 1 olmasa bile büyütülüp küçültülmemeli (projede tekrarlanan kural,
        /// <c>HandGripPivot</c> ile aynı gerekçe).</para>
        /// </summary>
        private static bool TryMeasureAnchorToWrist(SyntheticHand synthetic, OVRInput.Controller hand,
            out Pose delta)
        {
            delta = default;

            // Rig keşfinin TEK yolu: ikinci bir arama açmak iki bileşenin farklı karelerde farklı
            // rig bulmasına yol açardı (Scan ile aynı gerekçe).
            Transform anchor = WeaponGranter.ResolveHandAnchor(hand);
            if (anchor == null || !TryReadSourceWrist(synthetic, out Pose wrist))
            {
                return false;
            }

            Quaternion inverseAnchor = Quaternion.Inverse(anchor.rotation);
            delta = new Pose(
                inverseAnchor * (wrist.position - anchor.position),
                inverseAnchor * wrist.rotation);
            return true;
        }

        /// <summary>
        /// Sentetik elin kaynağındaki <b>ham</b> bilek (kök) pozunu DÜNYA uzayında okur.
        /// <para>⚠️ Veri izleme uzayındadır; dünyaya çevirmeyi <c>ITrackingToWorldTransformer</c>
        /// yapar (<c>Hand.GetRootPose</c> ile aynı yol). Çeviri atlanırsa delta, rig'in dünyadaki
        /// yerine göre sessizce kayar.</para>
        /// </summary>
        private static bool TryReadSourceWrist(SyntheticHand synthetic, out Pose wrist)
        {
            wrist = default;

            if (synthetic == null)
            {
                return false;
            }

            IDataSource<HandDataAsset> source = synthetic.ModifyDataFromSource;
            if (source == null)
            {
                return false;
            }

            HandDataAsset data = source.GetData();
            if (data == null || !data.IsDataValidAndConnected || data.RootPoseOrigin == PoseOrigin.None)
            {
                return false;
            }

            ITrackingToWorldTransformer transformer = data.Config != null
                ? data.Config.TrackingToWorldTransformer
                : null;

            wrist = transformer != null ? transformer.ToWorldPose(data.Root) : data.Root;
            return true;
        }

        private void LateUpdate()
        {
            if (Instance != this)
            {
                return;
            }

            TickHand(OVRInput.Controller.LTouch, false, ref _left, ref _leftLocked, ref _idleLeft);
            TickHand(OVRInput.Controller.RTouch, true, ref _right, ref _rightLocked, ref _idleRight);
        }

        /// <summary>
        /// Bir elin bir karelik durumu: o eli kullanan silah varsa pozunu, yoksa <b>idle</b> duruşunu
        /// uygular.
        /// <para>⚠️ <b>Boştaki el ile silah tutan el burada AYRIŞIR:</b> boş el örneklenmiş idle'a
        /// kilitlenir (elinde hiçbir şey yokken kumandanın tuşlarına basmak parmakları oynatmasın),
        /// silah tutan elin parmakları ise serbest bırakılır — kavrama ve tetik zaten kumandadan
        /// geliyor. İkisini aynı kurala bağlamak, ya boş eli tuşlarla oynatır ya silahı tutan eli
        /// dondururdu.</para>
        /// </summary>
        private void TickHand(OVRInput.Controller hand, bool rightHand, ref SyntheticHand cached,
            ref bool locked, ref Quaternion[] idle)
        {
            SyntheticHand synthetic = Resolve(ref cached);
            if (synthetic == null)
            {
                // Rig yok (gözlemci / sahne yüklenmedi): kilit diye bir şey de yok.
                locked = false;
                RefreshAnchorToWrist(null, hand, rightHand);
                return;
            }

            // ⚠️ Delta, pozu uygulamadan ÖNCE ve ASKIDAN BAĞIMSIZ ölçülür: kalibrasyon askıdayken de
            // bilek okuyabilmeli (Suspended notu), ölçünün kaynağı zaten kilitten etkilenmiyor.
            RefreshAnchorToWrist(synthetic, hand, rightHand);

            if (Suspended)
            {
                // Askıda el HAM izlemeye bırakılır: kalan kilit çözülmezse parmaklar donuk kalır ve
                // kalibrasyonu yapan kişi kendi pinch'ini göremez.
                if (locked)
                {
                    synthetic.FreeWrist();
                    locked = false;
                }

                synthetic.FreeAllJoints();
                return;
            }

            Weapon weapon = FindWeaponUsing(hand, out GripSocketKind kind);
            ItemDefinition definition = weapon != null ? weapon.Definition : null;
            bool hasGrip = definition != null && definition.HasGrip(kind, rightHand);

            if (weapon != null && !hasGrip)
            {
                // Kavraması yakalanmamış silah: el idle'a düşer.
                WarnMissingPose(weapon, kind, rightHand);
            }

            if (hasGrip)
            {
                Apply(synthetic, weapon.transform, definition.GetGrip(kind, rightHand));
                locked = true;
                return;
            }

            ApplyIdle(synthetic, hand, ref locked, ref idle);
        }

        /// <summary>
        /// Boştaki elin duruşu: <b>bileği serbest, parmakları kilitli</b>.
        /// <para>
        /// Duruşun kaynağı bir asset DEĞİL, elin kendi <b>gevşek</b> hâlidir: kumanda kipinde
        /// (<c>controllerDrivenHandPosesType = Natural</c>) hiçbir tuşa basılmıyorken üretilen poz
        /// zaten "yumruk değil, hafif açık" eldir. Bir kez örneklenip sonsuza dek uygulanır.
        /// </para>
        /// <para>
        /// ⚠️ <b>Neden yazılmış bir poz asset'i değil:</b> elle yazılacak bir idle, donanımın
        /// ürettiği elden kaçınılmaz olarak biraz farklı olurdu ve fark tam da oyuncunun kendi
        /// eline baktığı yerde görünürdü. Örnekleme ayrıca model/SDK değişince kendini günceller —
        /// projede tekrarlanan "sabit yazma, ölç" kuralı.
        /// </para>
        /// <para>⚠️ Bilek KİLİTLENMEZ: boştaki el kumandayı izlemeye devam etmeli, yalnız parmakları
        /// sabittir.</para>
        /// </summary>
        private static void ApplyIdle(SyntheticHand synthetic, OVRInput.Controller hand,
            ref bool locked, ref Quaternion[] idle)
        {
            if (locked)
            {
                // Silahtan gelen bilek kilidi bırakılır; parmaklar aşağıda yeniden yazılacağı için
                // FreeAllJoints çağrılmaz (çağrılsaydı el bir kare izlemeye dönüp titrerdi).
                synthetic.FreeWrist();
                locked = false;
            }

            idle ??= TryCaptureIdle(synthetic, hand);
            if (idle == null)
            {
                return; // temiz örnek henüz alınamadı — bugünkü davranış sürer
            }

            synthetic.OverrideAllJoints(idle, 1f);
            for (int i = 0; i < FingerCount; i++)
            {
                synthetic.SetFingerFreedom((HandFinger)i, JointFreedom.Locked);
            }
        }

        /// <summary>
        /// Elin gevşek duruşunu bir kez örnekler.
        /// <para>⚠️ <b>Tetik ya da grip basılıyken örnek ALINMAZ:</b> <c>Natural</c> kipinde
        /// parmaklar o girdiyle kıvrılıyor ve o anda yakalanan poz kalıcı olarak yanlış olurdu —
        /// el ömür boyu yarı yumruk dururdu. Temiz bir kare gelene kadar denenir.</para>
        /// <para>Eklem verisi henüz akmıyorsa (izleme başlamadı) <c>null</c> döner ve bir sonraki
        /// karede yeniden denenir.</para>
        /// </summary>
        private static Quaternion[] TryCaptureIdle(SyntheticHand synthetic, OVRInput.Controller hand)
        {
            if (OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, hand) > IdleCaptureInputEpsilon ||
                OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, hand) > IdleCaptureInputEpsilon)
            {
                return null;
            }

            HandJointId[] ids = FingersMetadata.HAND_JOINT_IDS;
            var rotations = new Quaternion[ids.Length];

            for (int i = 0; i < ids.Length; i++)
            {
                if (!synthetic.GetJointPoseLocal(ids[i], out Pose jointPose))
                {
                    return null;
                }

                rotations[i] = jointPose.rotation;
            }

            return rotations;
        }

        /// <summary>
        /// Bu eli KULLANAN silahı bulur; ana el ise <see cref="GripSocketKind.Primary"/>, ön kabza ise
        /// <see cref="GripSocketKind.Secondary"/>. İlk eşleşen kazanır (aynı el iki silaha bağlıysa
        /// zaten bir üst katmanda hata var).
        /// </summary>
        private static Weapon FindWeaponUsing(OVRInput.Controller hand, out GripSocketKind kind)
        {
            for (int i = 0; i < Weapon.Active.Count; i++)
            {
                Weapon weapon = Weapon.Active[i];
                if (weapon == null || !weapon.IsHeld)
                {
                    continue;
                }

                if (weapon.MainHand == hand)
                {
                    kind = GripSocketKind.Primary;
                    return weapon;
                }

                if (weapon.SecondaryHand == hand)
                {
                    kind = GripSocketKind.Secondary;
                    return weapon;
                }
            }

            kind = GripSocketKind.Primary;
            return null;
        }

        /// <summary>
        /// Yakalanmış kavramayı sentetik ele yazar: <b>yalnız BİLEK</b> silahın üstündeki noktaya
        /// kilitlenir.
        /// <para>
        /// ⚠️ <b>Kilit KOŞULSUZDUR</b> — mesafe/açı kapısı yoktur ve eklenmez. Takas bilinçli:
        /// bedeli, fiziksel kumanda silahın kavrama noktasından uzaklaştığında elin kolla arasının
        /// görsel olarak gerilmesidir; kazancı, oyuncu grip tuşunu bırakmadıkça elin silahtan
        /// kopmamasıdır. Mesafeye bakan bir kapı, özellikle ön kabzada, eli oyuncu hiçbir şey
        /// yapmadan bırakır ve "silahı iki elle tuttum ama ikinci el havada" hissi üretir.
        /// </para>
        /// <para>
        /// ⚠️ <b>PARMAKLARA POZ YAZILMAZ</b> (<c>OverrideAllJoints</c> çağrılmaz): parmak duruşu
        /// artık authored bir veri değildir. Kumanda kipinde
        /// (<c>controllerDrivenHandPosesType = Natural</c>) grip tuşu parmakları zaten kapatır,
        /// tetik parmağı da ateş ederken kendi kıvrılır — yazılmış bir duruş bunların ikisini de
        /// öldürür ve el silahın üstünde donuk kalırdı.
        /// </para>
        /// <para>
        /// ⚠️ <b>Beş parmağa açıkça <see cref="JointFreedom.Free"/> yazmak ZORUNLUDUR</b> ve
        /// silinemez: <see cref="ApplyIdle"/> boştaki elin parmaklarını <c>Locked</c> bırakıyor ve
        /// serbestlik seviyesi sentetik elde <b>kalıcıdır</b>. Yazılmazsa el, silahı kavradığı anda
        /// idle'dan devraldığı kilitle donar — belirtisi "kumandayı sıkıyorum, parmaklar kıpırdamıyor".
        /// </para>
        /// </summary>
        /// <param name="grip">Bileğin EŞYAYA göre yerel pozu (metre, ölçeksiz).</param>
        private static void Apply(SyntheticHand synthetic, Transform item, in ItemGripCapture grip)
        {
            // ⚠️ TransformPoint DEĞİL elle bileşim: yakalama METREdir ve eşyanın görsel ölçeğiyle
            // (WPN_* kökleri 0.8) büyütülmemeli — ölçekli bileşim bileği silahtan 1/0.8 kadar uzağa
            // koyar ve el silahın yanında yüzer.
            var wrist = new Pose(
                item.position + item.rotation * grip.position,
                item.rotation * grip.Rotation);

            synthetic.LockWristPose(wrist, 1f, SyntheticHand.WristLockMode.Full, true);

            for (int i = 0; i < FingerCount; i++)
            {
                synthetic.SetFingerFreedom((HandFinger)i, JointFreedom.Free);
            }
        }


        // ------------------------------------------------------------ el çözümü (ISDK)

        /// <summary>
        /// Sentetik eli TEMBEL çözer ve önbelleğe alır; bulunamazsa saniyede bir yeniden dener ve
        /// <c>null</c> döner (hata BASMAZ — rig'in olmaması normal bir durumdur: admin gözlemci rig'i
        /// kapatır, editör oturumunda hiç olmayabilir).
        /// </summary>
        private SyntheticHand Resolve(ref SyntheticHand cached)
        {
            if (cached != null)
            {
                return cached;
            }

            if (Time.time < _nextScanAt)
            {
                return null;
            }

            _nextScanAt = Time.time + RescanSeconds;
            Scan();
            return cached;
        }

        /// <summary>
        /// Rig'in altındaki iki sentetik eli bulur.
        /// <para>
        /// Rig'e <see cref="WeaponGranter.ResolveHandAnchor"/> üzerinden çıkılır: rig keşfinin TEK
        /// yolu odur ve ikinci bir arama açmak iki bileşenin farklı karelerde farklı rig bulmasına yol
        /// açardı.
        /// </para>
        /// <para>
        /// ⚠️ Sol/sağ ayrımı ata düğümün adından (<c>ComprehensiveInteractorsLeft/Right</c>) DEĞİL
        /// elin kendi <see cref="Hand.Handedness"/>'inden çözülür: ad tabanlı ayrım Building Blocks
        /// prefabı yeniden adlandırıldığında sessizce ters çalışırdı (sol elin pozu sağ ele).
        /// <see cref="Hand.Handedness"/> veri akmadan okunamadığı için el <b>bağlanana kadar</b>
        /// bulunmuş sayılmaz; o hâlde zaten çizilecek bir el de yoktur.
        /// </para>
        /// </summary>
        private void Scan()
        {
            Transform anchor = WeaponGranter.ResolveHandAnchor(OVRInput.Controller.RTouch);
            if (anchor == null)
            {
                anchor = WeaponGranter.ResolveHandAnchor(OVRInput.Controller.LTouch);
            }

            if (anchor == null)
            {
                return;
            }

            var rig = anchor.GetComponentInParent<OVRCameraRig>();
            if (rig == null)
            {
                return;
            }

            SyntheticHand[] hands = rig.GetComponentsInChildren<SyntheticHand>(true);
            for (int i = 0; i < hands.Length; i++)
            {
                SyntheticHand hand = hands[i];
                if (hand == null || hand.gameObject.name != SyntheticHandNodeName)
                {
                    continue;
                }

                if (!hand.isActiveAndEnabled || !hand.IsConnected)
                {
                    continue;
                }

                if (hand.Handedness == Handedness.Right)
                {
                    _right = hand;
                }
                else
                {
                    _left = hand;
                }
            }
        }

        /// <summary>
        /// Kavraması yakalanmamış silah için oturum başına <b>BİR</b> uyarı.
        /// <para>
        /// ⚠️ Döngüde koşulsuz loglamak kare başına iki satır üretir (saniyede ~140) ve konsolu
        /// boğardı; anahtar (tanım + kavrama noktası + el) olduğu için her eksik poz yine tek tek
        /// görünür.
        /// </para>
        /// </summary>
        private void WarnMissingPose(Weapon weapon, GripSocketKind kind, bool rightHand)
        {
            string weaponName = weapon.Definition != null ? weapon.Definition.name : weapon.name;
            string key = $"{weaponName}|{kind}|{(rightHand ? "R" : "L")}";
            if (!_warned.Add(key))
            {
                return;
            }

            Debug.LogWarning($"[HandGripPoser] '{weaponName}' silahının " +
                             $"'{kind}' kavraması {(rightHand ? "SAĞ" : "SOL")} el için " +
                             "yakalanmamış; el kumanda duruşunda kalıyor.");
        }
    }
}
