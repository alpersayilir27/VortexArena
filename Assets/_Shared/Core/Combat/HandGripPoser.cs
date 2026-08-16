using System.Collections.Generic;
using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Elin duruşunu ISDK'nın <b>sentetik eline</b> yazar. Oyuncunun gözlükte gördüğü el budur
    /// (<c>OVRHandVisualLeft/Right</c> bu sentetik elden sürülüyor).
    /// <para>
    /// <b>Üç durum, üç davranış:</b>
    /// <list type="bullet">
    /// <item><b>Boş el:</b> bilek serbest, parmaklar <see cref="HandGripPreset.Idle"/>.</item>
    /// <item><b>Ana el:</b> bilek <b>SERBEST</b> (el izlemeden/kumandadan gelir, silah ona uyar —
    /// eşyayı ana kavrama kaydı döndürüyor), parmaklar slotun preset'i.</item>
    /// <item><b>Ön kabza:</b> bilek <b>TAM</b> kilitlenir (konum + dönüş) — o el eşyaya yapışır,
    /// parmaklar slotun preset'i.</item>
    /// </list>
    /// </para>
    /// <para>
    /// ⚠️ <b>Boş elin parmakları donanımdan ÖRNEKLENMEZ</b> (o yol kalktı ve geri gelmez): üç
    /// preset de tek kaynaktan (<see cref="HandGripPresets"/>) gelmek zorunda, yoksa stüdyoda
    /// yazılan duruş ile oyunda görülen el iki ayrı şey olur. Örneklenen idle ayrıca yalnız kumanda
    /// kipinde tanımlıydı ve temiz bir kare bekliyordu.
    /// </para>
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

        private SyntheticHand _left;
        private SyntheticHand _right;

        /// <summary>Elin BİLEĞİ şu an ön kabzaya kilitli mi. Parmaklar bundan bağımsız her zaman
        /// bizim yazdığımızdır (slotun preset'i ya da idle).</summary>
        private bool _leftLocked;
        private bool _rightLocked;

        /// <summary>Kumanda anchor'ından İZLENEN bileğe delta (anchor uzayı) — el başına, her karede
        /// tazelenir. Gerekçe <see cref="TryGetAnchorToWrist"/>'te.</summary>
        private Pose _anchorToWristLeft;
        private Pose _anchorToWristRight;
        private bool _hasAnchorToWristLeft;
        private bool _hasAnchorToWristRight;

        /// <summary>Parmak sayısı — ISDK garantisi (bkz. <see cref="ApplyPreset"/> içindeki
        /// serbestlik dizisi notu).</summary>
        private const int FingerCount = 5;

        private float _nextScanAt;

        /// <summary>Pozu eksik silahlar için "oturum başına bir kez" uyarı kaydı.</summary>
        private readonly HashSet<string> _warned = new HashSet<string>();

#if UNITY_EDITOR
        /// <summary>
        /// Ölçülen anchor→bilek deltasının "oturdu" sayılması için gereken ardışık kare sayısı.
        /// <para>İlk kareler izleme ısınırken gürültülüdür; tek kareye bakıp loglamak
        /// <c>HandGripConvention</c>'a yapıştırılacak sayıyı yanlış verirdi.</para>
        /// </summary>
        private const int DeltaLogStableFrames = 30;

        /// <summary>Kararlılık eşikleri: 2 mm ve 0.5° altındaki oynama ölçüm gürültüsüdür.</summary>
        private const float DeltaLogPositionEpsilon = 0.002f;
        private const float DeltaLogAngleEpsilon = 0.5f;

        /// <summary>El başına (0 = sol, 1 = sağ) kararlılık takibi ve "bir kez basıldı" kaydı.</summary>
        private readonly Pose[] _deltaLogPrevious = new Pose[2];
        private readonly int[] _deltaLogStreak = new int[2];
        private readonly bool[] _deltaLogged = new bool[2];
#endif

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
        /// <b>Ne işe yarar:</b> silahın eldeki duruşunun tek yazılı kaynağı tanımdaki kavrama
        /// kaydıdır (<see cref="ItemGripPose"/>) ve o, <b>BİLEĞİ</b> silaha göre tarif eder. Silahın
        /// dünya pozunu çözen taraf ise elin ANCHOR pozunu biliyor. İki uç arasındaki köprü bu
        /// deltadır: <c>bilekDünya = anchor ∘ delta</c>. Onsuz kavrama, ölçülmüş sabite düşer
        /// (<see cref="HandGripConvention.AnchorToWrist"/>).
        /// </para>
        /// <para>
        /// ⚠️ <b>Kanonikliği bozmaz</b> (§6.6): kumanda sürümlü el pozları deterministiktir — aynı
        /// kumanda, aynı SDK, aynı sonuç. Yani her başlıkta AYNI delta ölçülür; duruş yine telde
        /// gitmez ve tel formatı değişmez.
        /// </para>
        /// <para>
        /// ⚠️ Ölçü sentetik elin KAYNAĞINDAN alınır (<c>ModifyDataFromSource</c>), sentetik elin
        /// kendisinden değil: ön kabzada bileği zaten biz kilitliyoruz
        /// (<see cref="LockToSecondaryGrip"/>), kilitli eli okumak "silahın nerede olduğunu silahın
        /// kendisine sormak" olurdu ve ölçü bir kare içinde kendi çıktısına kilitlenirdi.
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

        /// <summary>Bir elin deltasını tazeler; ölçülemezse o elin bayrağını düşürür.</summary>
        private void RefreshAnchorToWrist(SyntheticHand synthetic, OVRInput.Controller hand, bool rightHand)
        {
            bool measured = TryMeasureAnchorToWrist(synthetic, hand, out Pose delta);

#if UNITY_EDITOR
            LogMeasuredDelta(rightHand, measured, delta);
#endif

            if (rightHand)
            {
                _hasAnchorToWristRight = measured;
                _anchorToWristRight = measured ? delta : default;
                return;
            }

            _hasAnchorToWristLeft = measured;
            _anchorToWristLeft = measured ? delta : default;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Delta ilk kez KARARLI ölçüldüğünde el başına <b>bir</b> satır basar — çıktı doğrudan
        /// <see cref="HandGripConvention.LeftAnchorToWrist"/> ailesine yapıştırılır.
        /// <para>
        /// <b>Neden log:</b> o sabit rig'i olmayan izleyicinin (admin gözlemci) ve ilk karelerin tek
        /// kaynağı ve <b>ölçülmeden</b> yazılamaz. Ölçen tek yer burası olduğu için değeri de burası
        /// söyler; ikinci bir ölçüm aracı açmak iki ucun sapması demek olurdu.
        /// </para>
        /// <para>⚠️ Yalnız editörde: sahadaki başlıkta bu satırı okuyan kimse yok ve sabit zaten
        /// APK'ya derlenmiş olarak gidiyor.</para>
        /// </summary>
        private void LogMeasuredDelta(bool rightHand, bool measured, in Pose delta)
        {
            int slot = rightHand ? 1 : 0;

            if (!measured || _deltaLogged[slot])
            {
                if (!measured)
                {
                    _deltaLogStreak[slot] = 0;
                }

                return;
            }

            Pose previous = _deltaLogPrevious[slot];
            _deltaLogPrevious[slot] = delta;

            bool stable = _deltaLogStreak[slot] > 0 &&
                          (delta.position - previous.position).magnitude < DeltaLogPositionEpsilon &&
                          Quaternion.Angle(delta.rotation, previous.rotation) < DeltaLogAngleEpsilon;

            _deltaLogStreak[slot] = stable ? _deltaLogStreak[slot] + 1 : 1;
            if (_deltaLogStreak[slot] < DeltaLogStableFrames)
            {
                return;
            }

            _deltaLogged[slot] = true;

            Vector3 p = delta.position;
            Vector3 e = delta.rotation.eulerAngles;
            string field = rightHand ? "RightAnchorToWrist" : "LeftAnchorToWrist";
            Debug.Log($"[HandGripPoser] anchor→bilek ({(rightHand ? "SAĞ" : "SOL")}) ölçüldü: " +
                      $"pos ({p.x:F4}, {p.y:F4}, {p.z:F4}) euler ({e.x:F2}, {e.y:F2}, {e.z:F2}) → " +
                      $"HandGripConvention.{field}'e yapıştır (rig'siz izleyicinin fallback'i).");
        }
#endif

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

            TickHand(OVRInput.Controller.LTouch, false, ref _left, ref _leftLocked);
            TickHand(OVRInput.Controller.RTouch, true, ref _right, ref _rightLocked);
        }

        /// <summary>
        /// Bir elin bir karelik durumu.
        /// <para>⚠️ <b>Ana el ile ön kabza burada AYRIŞIR:</b> ana elin bileği SERBEST kalır (eşya
        /// ele uyar), ön kabzayı saran elin bileği TAM kilitlenir (el eşyaya yapışır). İkisini aynı
        /// kurala bağlamak, ya silahı elden koparır ya ikinci eli havada bırakır.</para>
        /// <para>Parmaklar üç durumda da bizim yazdığımızdır: boş elde
        /// <see cref="HandGripPreset.Idle"/>, eşya tutan elde slotun kendi preset'i.</para>
        /// </summary>
        private void TickHand(OVRInput.Controller hand, bool rightHand, ref SyntheticHand cached,
            ref bool locked)
        {
            SyntheticHand synthetic = Resolve(ref cached);
            if (synthetic == null)
            {
                // Rig yok (gözlemci / sahne yüklenmedi): kilit diye bir şey de yok.
                locked = false;
                RefreshAnchorToWrist(null, hand, rightHand);
                return;
            }

            // ⚠️ Delta, pozu uygulamadan ÖNCE ölçülür ve kaynağı sentetik elin GİRDİSİDİR: kilitli
            // eli okumak, silahın nerede olduğunu silahın kendisine sormak olurdu.
            RefreshAnchorToWrist(synthetic, hand, rightHand);

            Weapon weapon = FindWeaponUsing(hand, out GripSocketKind kind);
            ItemDefinition definition = weapon != null ? weapon.Definition : null;
            bool hasGrip = definition != null && definition.HasGrip(kind, rightHand);

            if (weapon != null && !hasGrip)
            {
                // Kavraması yazılmamış silah: el idle'a düşer.
                WarnMissingPose(weapon, kind, rightHand);
            }

            if (!hasGrip)
            {
                FreeWristIfLocked(synthetic, ref locked);
                ApplyPreset(synthetic, HandGripPreset.Idle, rightHand);
                return;
            }

            if (kind == GripSocketKind.Secondary)
            {
                LockToSecondaryGrip(synthetic, weapon.transform,
                    definition.GetGrip(kind, rightHand));
                locked = true;
            }
            else
            {
                // ⚠️ ANA elde bilek KİLİTLENMEZ: eşyanın dönüşü artık kavrama kaydından geliyor,
                // yani silah zaten ele uyuyor (Weapon.ApplyCanonicalGrip). Bileği ayrıca kilitlemek
                // eli kumandadan koparır ve iki yazar aynı şeyi sürer.
                FreeWristIfLocked(synthetic, ref locked);
            }

            ApplyPreset(synthetic, definition.GripPreset(kind, rightHand), rightHand);
        }

        /// <summary>
        /// Ön kabzayı saran elin bileğini eşyanın üstündeki kayda <b>TAM</b> (konum + dönüş)
        /// kilitler — o el eşyaya yapışır.
        /// <para>
        /// ⚠️ <b>Kilit KOŞULSUZDUR</b> — mesafe/açı kapısı yoktur ve eklenmez. Takas bilinçli:
        /// bedeli, fiziksel kumanda ön kabzadan uzaklaştığında elin kolla arasının görsel olarak
        /// gerilmesidir; kazancı, oyuncu grip tuşunu bırakmadıkça elin silahtan kopmamasıdır.
        /// Mesafeye bakan bir kapı eli oyuncu hiçbir şey yapmadan bırakır ve "silahı iki elle
        /// tuttum ama ikinci el havada" hissi üretir.
        /// </para>
        /// <para>
        /// ⚠️ <c>TransformPoint</c> DEĞİL elle bileşim: kayıt METREdir ve eşyanın görsel ölçeğiyle
        /// (<c>WPN_*</c> kökleri 0.8) büyütülmemeli — ölçekli bileşim bileği silahtan 1/0.8 kadar
        /// uzağa koyar ve el silahın yanında yüzer.
        /// </para>
        /// <para>
        /// ⚠️ Poz DÜNYA uzayındadır ve öyle verilmek zorunda (<c>worldPose: true</c>): sentetik el
        /// izleme uzayında çalışır, çeviriyi <c>LockWristPose</c> yapar. Doğrudan
        /// <c>LockWristPosition</c> çağrılırsa çeviri ATLANIR ve el, rig'in dünyadaki yerine göre
        /// sessizce kayar.
        /// </para>
        /// </summary>
        /// <param name="grip">Bileğin EŞYAYA göre yerel pozu (metre, ölçeksiz).</param>
        private static void LockToSecondaryGrip(SyntheticHand synthetic, Transform item,
            in ItemGripPose grip)
        {
            var wrist = new Pose(
                item.position + item.rotation * grip.position,
                item.rotation * grip.Rotation);

            synthetic.LockWristPose(wrist, 1f, SyntheticHand.WristLockMode.Full, true);
        }

        /// <summary>Ön kabzadan kalan bilek kilidini bırakır (parmaklara dokunmaz — çağıran onları
        /// hemen ardından yazıyor; <c>FreeAllJoints</c> çağrılsaydı el bir kare izlemeye dönüp
        /// titrerdi).</summary>
        private static void FreeWristIfLocked(SyntheticHand synthetic, ref bool locked)
        {
            if (!locked)
            {
                return;
            }

            synthetic.FreeWrist();
            locked = false;
        }

        /// <summary>
        /// Bir preset'in parmak duruşunu sentetik ele yazar.
        /// <para>
        /// ⚠️ <b>Serbestlik dizisi HER KAREDE yazılır ve kısaltılamaz</b> (yalnız "değişince
        /// yazalım" denemez): seviye sentetik elde <b>kalıcıdır</b>, yani bir preset'ten ötekine
        /// geçerken <see cref="JointFreedom.Free"/> olması gereken parmağa açıkça <c>Free</c>
        /// yazılmazsa el, önceki duruştan devraldığı kilitle donar — belirtisi "ateş ediyorum,
        /// tetik parmağım kıpırdamıyor".
        /// </para>
        /// <para>⚠️ <see cref="HandGripPresets.JointRotations"/>'ın dizisi ÖNBELLEKLİDİR:
        /// değiştirilmez, yalnız yazılır.</para>
        /// </summary>
        private static void ApplyPreset(SyntheticHand synthetic, HandGripPreset preset, bool rightHand)
        {
            synthetic.OverrideAllJoints(HandGripPresets.JointRotations(preset, rightHand), 1f);

            JointFreedom[] freedom = HandGripPresets.Freedom(preset);
            for (int i = 0; i < FingerCount; i++)
            {
                synthetic.SetFingerFreedom((HandFinger)i, freedom[i]);
            }
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
        /// Kavraması yazılmamış silah için oturum başına <b>BİR</b> uyarı.
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
                             "stüdyoda YAZILMAMIŞ; el idle duruşunda kalıyor.");
        }
    }
}
