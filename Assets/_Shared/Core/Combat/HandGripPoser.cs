using System.Collections.Generic;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Elde tutulan silahın kavrama pozunu ISDK'nın <b>sentetik eline</b> uygular: parmaklar silaha
    /// sarılır, bilek kavrama noktasına kilitlenir. Oyuncunun gözlükte gördüğü el budur
    /// (<c>OVRHandVisualLeft/Right</c> bu sentetik elden sürülüyor).
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

        /// <summary>El şu an bizim kilidimiz altında mı — serbest bırakma yalnız GEÇİŞTE yapılır
        /// (bkz. <see cref="Release"/>).</summary>
        private bool _leftLocked;
        private bool _rightLocked;

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
            _nextScanAt = 0f;
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
        /// Bir elin bir karelik durumu: o eli kullanan silah varsa pozunu uygula, yoksa eli serbest
        /// bırak.
        /// </summary>
        private void TickHand(OVRInput.Controller hand, bool rightHand, ref SyntheticHand cached, ref bool locked)
        {
            SyntheticHand synthetic = Resolve(ref cached);
            if (synthetic == null)
            {
                // Rig yok (gözlemci / sahne yüklenmedi): kilit diye bir şey de yok.
                locked = false;
                return;
            }

            Weapon weapon = FindWeaponUsing(hand, out GripSocketKind kind);
            if (weapon == null)
            {
                Release(synthetic, ref locked);
                return;
            }

            HandGrabPose pose = ItemGripPoses.Find(weapon.transform, kind, rightHand);
            if (pose == null)
            {
                // Pozu olmayan silahta bugünkü davranış korunur: el kumanda duruşunda kalır.
                WarnMissingPose(weapon, kind, rightHand);
                Release(synthetic, ref locked);
                return;
            }

            Apply(synthetic, weapon, pose);
            locked = true;
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
        /// Pozu sentetik ele yazar: bilek dünya pozuna kilitlenir, parmak eklemleri pozun
        /// rotasyonlarını alır.
        /// <para>
        /// ⚠️ <b>Kilit KOŞULSUZDUR</b> — mesafe/açı kapısı yoktur ve eklenmez. Takas bilinçli:
        /// bedeli, fiziksel kumanda silahın kavrama noktasından uzaklaştığında elin kolla arasının
        /// görsel olarak gerilmesidir; kazancı, oyuncu grip tuşunu bırakmadıkça elin silahtan
        /// kopmamasıdır. Mesafeye bakan bir kapı, özellikle ön kabzada, eli oyuncu hiçbir şey
        /// yapmadan bırakır ve "silahı iki elle tuttum ama ikinci el havada" hissi üretir.
        /// </para>
        /// <para>
        /// ⚠️ <b><see cref="SyntheticHand.OverrideAllJoints"/> TEK BAŞINA hiçbir şey yapmaz:</b>
        /// yalnız hedef rotasyonları saklar; eklem serbestlik seviyesi <c>Free</c> kaldığı sürece
        /// ISDK izlenen parmakları aynen geçirir. Parmakların gerçekten sarılması için serbestlik
        /// seviyesinin de yazılması gerekir (ISDK'nın kendi <c>HandGrabStateVisual.UpdateFingers</c>'ı
        /// da bu ikiliyi birlikte kullanır).
        /// </para>
        /// <para>
        /// ⚠️ <b>Serbestlik POZUN KENDİSİNDEN okunur</b> (<see cref="HandPose.FingersFreedom"/>), beş
        /// parmağa koşulsuz <see cref="JointFreedom.Locked"/> YAZILMAZ. Sebep tetik parmağıdır: kilitli
        /// bir işaret parmağı ateş ederken kıpırdamaz, yani oyuncu tetiği çektiğini elinde göremez.
        /// Hangi parmağın kilitli, hangisinin serbest olacağı silah başına bir TASARIM kararıdır ve
        /// pozla birlikte yazılır — koda gömülürse tüm silahlar aynı ele mahkûm olurdu.
        /// ISDK'nın varsayılanı (<c>FingersMetadata.DefaultFingersFreedom</c>) baş+işaret
        /// <c>Locked</c>, orta+yüzük <c>Constrained</c>, serçe <c>Free</c>'dir; tüfeklerde işaret
        /// parmağı <c>Free</c> yapılır.
        /// </para>
        /// </summary>
        private static void Apply(SyntheticHand synthetic, Weapon weapon, HandGrabPose pose)
        {
            // ⚠️ TransformPoint DEĞİL: kavrama ofsetleri METRE cinsindendir ve transform ölçeğiyle
            // büyütülmemeli (aynı gerekçe ItemGripSockets.PrimarySocketWorld ve
            // GripSocketAuthoring.LocalPose'ta da elle bileşim yaptırıyor). Referans, pozun kendi
            // RelativeTo'sudur; boşsa silahın kökü.
            Transform reference = pose.RelativeTo != null ? pose.RelativeTo : weapon.transform;
            Pose relative = pose.RelativePose;

            var wrist = new Pose(
                reference.position + reference.rotation * relative.position,
                reference.rotation * relative.rotation);

            HandPose handPose = pose.HandPose;

            synthetic.LockWristPose(wrist, 1f, SyntheticHand.WristLockMode.Full, true);
            synthetic.OverrideAllJoints(handPose.JointRotations, 1f);

            // Serbestlik dizisi ISDK garantisiyle beş elemanlıdır (FingersMetadata.DefaultFingersFreedom
            // boş/null diziyi getter'da tazeliyor); yine de dizinin kendi uzunluğu geziliyor —
            // ileride parmak sayısı değişirse burası sessizce taşmasın.
            JointFreedom[] freedom = handPose.FingersFreedom;
            for (int i = 0; i < freedom.Length; i++)
            {
                synthetic.SetFingerFreedom((HandFinger)i, freedom[i]);
            }
        }

        /// <summary>
        /// Eli ISDK'ya geri verir.
        /// <para>
        /// ⚠️ <b>Yalnız kilitli → serbest GEÇİŞİNDE</b> çağrılır. Her karede koşulsuz çağrılsaydı
        /// ISDK'nın kendi kilitlerini (poke sınırlama, başka bir interactor'ın kavraması) her karede
        /// iptal ederdik — belirtisi, silah tutulmuyorken elin yüzeylere gömülmesi olurdu.
        /// </para>
        /// </summary>
        private static void Release(SyntheticHand synthetic, ref bool locked)
        {
            if (!locked)
            {
                return;
            }

            synthetic.FreeWrist();
            synthetic.FreeAllJoints();
            locked = false;
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
        /// Pozu olmayan silah için oturum başına <b>BİR</b> uyarı.
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

            Debug.LogWarning($"[HandGripPoser] '{weaponName}' silahında " +
                             $"'{ItemGripPoses.RootNodeName}/{ItemGripPoses.NodeName(kind, rightHand)}' " +
                             "kavrama pozu yok; el kumanda duruşunda kalıyor.");
        }
    }
}
