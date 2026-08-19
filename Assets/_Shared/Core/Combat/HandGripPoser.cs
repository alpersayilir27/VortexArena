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
    /// <item><b>Boş el:</b> bilek serbest, parmaklar boşta duruşunda
    /// (<see cref="HandPoseLibrary.IdleJointRotations"/>).</item>
    /// <item><b>Ana el:</b> bilek <b>SERBEST</b> (el izlemeden/kumandadan gelir, silah ona uyar —
    /// eşyayı ana kavrama kaydı döndürüyor), parmaklar o slot için riglenmiş duruş.</item>
    /// <item><b>Ön kabza:</b> bilek <b>TAM</b> kilitlenir (konum + dönüş) — o el eşyaya yapışır,
    /// parmaklar o slot için riglenmiş duruş.</item>
    /// </list>
    /// </para>
    /// <para>
    /// ⚠️ <b>Parmaklar HİÇBİR durumda donanımdan sürülmez</b> — ne kumandanın tetiği/kabzası ne el
    /// izlemesi bir parmağı kıpırdatır. Beş parmak her karede kilitlidir
    /// (<c>JointFreedom.Locked</c>) ve duruş her karede ya boş elin dizisidir ya da tutulan eşyanın
    /// o slot için stüdyoda riglenmiş dizisi; ikisi arasındaki geçiş (boş el ↔ kavrama, ana kabza ↔
    /// ön kabza) <see cref="HandPoseLibrary.TransitionSeconds"/> içinde eklem eklem yumuşatılır
    /// (<see cref="HandState"/>). Bir parmağı bile serbest bırakmak, stüdyoda görülen el ile oyunda
    /// görülen elin o parmakta ayrışması demektir; boş elin parmaklarını donanımdan örneklemek de
    /// aynı sebeple YOKTUR.
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

        /// <summary>Parmak sayısı — ISDK garantisi (bkz. <see cref="ApplyFingers"/>).</summary>
        private const int FingerCount = 5;

        public static HandGripPoser Instance { get; private set; }

        /// <summary>
        /// Bir elin kare-ötesi durumu: sentetik el önbelleği, bilek kilidi ve parmak duruşunun
        /// <b>gösterilen</b> hâli.
        /// <para>
        /// <b>Geçiş neden burada, ISDK'da değil:</b> <c>SyntheticHand</c> yalnız serbest↔kilitli
        /// geçişini yumuşatır (kendi lock eğrisi); kilitliyken hedef dönüşü değiştirmek ANINDA
        /// uygulanır. Boş el ile kavrama arasında (ya da ana kabza ↔ ön kabza) el o yüzden burada,
        /// <see cref="HandPoseLibrary.TransitionSeconds"/> boyunca <see cref="From"/>'dan hedefe
        /// eklem eklem slerp'lenerek götürülür; sentetik ele her karede bu ARA dizi yazılır.
        /// </para>
        /// <para>⚠️ Hedef değişince başlangıç noktası o anki GÖSTERİLEN dizidir, önceki hedefin
        /// dizisi değil: geçişin ortasında yeni bir hedef gelirse el zıplamadan yön değiştirir.</para>
        /// </summary>
        private sealed class HandState
        {
            public SyntheticHand Synthetic;

            /// <summary>Elin BİLEĞİ şu an ön kabzaya kilitli mi. Parmaklar bundan bağımsız her zaman
            /// bizim yazdığımızdır (slotun riglenmiş duruşu ya da boş elin duruşu).</summary>
            public bool WristLocked;

            /// <summary>
            /// Şu anki hedef dizi — <b>referansıyla</b> tutulur ve <b>referansıyla</b> karşılaştırılır.
            /// <para>⚠️ Bu yüzden hedef diziler önbellekli/paylaşımlı olmak ZORUNDA
            /// (<see cref="ItemDefinition.GripJointRotations"/>,
            /// <see cref="HandPoseLibrary.IdleJointRotations"/>): kare başına yeni dizi üreten bir
            /// kaynak, her karede "hedef değişti" sayılır ve geçiş hiç bitmezdi. Eklem eklem
            /// karşılaştırma da alternatif değil — 19 quaternion'u kare başına iki el için
            /// kıyaslamak, referans kimliği zaten garantiliyken bedava değil.</para>
            /// </summary>
            public Quaternion[] Target;

            /// <summary>Geçişin başladığı andaki gösterilen dizi (kopya).</summary>
            public readonly Quaternion[] From = new Quaternion[FingersMetadata.HAND_JOINT_IDS.Length];

            /// <summary>Sentetik ele bu karede yazılan dizi.</summary>
            public readonly Quaternion[] Shown = new Quaternion[FingersMetadata.HAND_JOINT_IDS.Length];

            /// <summary><c>0..1</c> geçiş ilerlemesi (1 = hedefe oturmuş).</summary>
            public float Progress = 1f;

            /// <summary>Kumanda anchor'ından İZLENEN bileğe delta (anchor uzayı) — her karede
            /// tazelenir. Gerekçe <see cref="TryGetAnchorToWrist"/>'te.</summary>
            public Pose AnchorToWrist;
            public bool HasAnchorToWrist;

            /// <summary>Yeni sahne / ilk kurulum: her şey idle'a ve "oturmuş" durumuna döner.</summary>
            public void Reset(bool rightHand)
            {
                Synthetic = null;
                WristLocked = false;
                HasAnchorToWrist = false;
                AnchorToWrist = default;
                Progress = 1f;

                Quaternion[] idle = HandPoseLibrary.IdleJointRotations(rightHand);
                Target = idle;
                for (int i = 0; i < Shown.Length && i < idle.Length; i++)
                {
                    Shown[i] = idle[i];
                    From[i] = idle[i];
                }
            }
        }

        private readonly HandState _left = new HandState();
        private readonly HandState _right = new HandState();

        private float _nextScanAt;

        /// <summary>Pozu eksik silahlar için "oturum başına bir kez" uyarı kaydı.</summary>
        private readonly HashSet<string> _warned = new HashSet<string>();

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
            _left.Reset(false);
            _right.Reset(true);
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
            _left.Reset(false);
            _right.Reset(true);
            _nextScanAt = 0f;
        }

        // --------------------------------------------------- anchor → izlenen bilek deltası

        /// <summary>
        /// Kumanda anchor'ından <b>izlenen bileğe</b> olan sabit delta (anchor uzayında, metre);
        /// ölçülemiyorsa <c>false</c>.
        /// <para>
        /// <b>Ne işe yarar:</b> kavrama kaydı (<see cref="ItemGripPose"/>) kumanda ANCHOR'ını silaha
        /// göre tarif eder; ön kabzada sentetik ele verilecek olan ise BİLEK. Köprü bu deltadır:
        /// <c>bilekDünya = anchor ∘ delta</c> (<see cref="ItemGripAuthority.WristFromAnchor"/>). Onsuz
        /// kilit ölçülmüş sabite düşer (<see cref="HandGripConvention.AnchorToWrist"/>); silahın kendi
        /// duruşu deltayı HİÇ okumaz.
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

            HandState state = rightHand ? instance._right : instance._left;
            delta = state.AnchorToWrist;
            return state.HasAnchorToWrist;
        }

        /// <summary>Bir elin deltasını tazeler; ölçülemezse o elin bayrağını düşürür.</summary>
        private void RefreshAnchorToWrist(HandState state, OVRInput.Controller hand, bool rightHand)
        {
            bool measured = TryMeasureAnchorToWrist(state.Synthetic, hand, out Pose delta);

            LogMeasuredDelta(rightHand, measured, delta);

            state.HasAnchorToWrist = measured;
            state.AnchorToWrist = measured ? delta : default;
        }

        /// <summary>
        /// Delta ilk kez KARARLI ölçüldüğünde el başına <b>bir</b> satır basar — çıktı doğrudan
        /// <see cref="HandGripConvention.LeftAnchorToWrist"/> ailesine yapıştırılır.
        /// <para>
        /// <b>Neden log:</b> o sabit stüdyodaki hayalet elin (kumanda köküne göre nereye çizileceği)
        /// ve rig'in henüz veri akıtmadığı ilk karelerin tek kaynağı ve <b>ölçülmeden</b> yazılamaz.
        /// Ölçen tek yer burası olduğu için değeri de burası söyler; ikinci bir ölçüm aracı açmak iki
        /// ucun sapması demek olurdu.
        /// </para>
        /// <para>Build'de de basılır (oturum başına iki satır): Link'siz geliştirici değeri başlıkta
        /// koşan APK'dan <c>adb logcat -s Unity</c> ile okur — editörde koşmayan bir ölçüm, Link
        /// kullanmayan geliştiriciye sabiti hiç vermezdi.</para>
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
                      $"HandGripConvention.{field}'e yapıştır (stüdyodaki hayalet elin ve rig'siz " +
                      "kilidin fallback'i).");
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

            TickHand(OVRInput.Controller.LTouch, false, _left);
            TickHand(OVRInput.Controller.RTouch, true, _right);
        }

        /// <summary>
        /// Bir elin bir karelik durumu.
        /// <para>⚠️ <b>Ana el ile ön kabza burada AYRIŞIR:</b> ana elin bileği SERBEST kalır (eşya
        /// ele uyar), ön kabzayı saran elin bileği TAM kilitlenir (el eşyaya yapışır). İkisini aynı
        /// kurala bağlamak, ya silahı elden koparır ya ikinci eli havada bırakır.</para>
        /// <para>Parmaklar üç durumda da bizim yazdığımızdır: boş elde boşta duruşu, eşya tutan elde
        /// slotun kendi riglenmiş duruşu — hedef her karede <see cref="ApplyFingers"/>'a verilir,
        /// geçişi o yumuşatır.</para>
        /// </summary>
        private void TickHand(OVRInput.Controller hand, bool rightHand, HandState state)
        {
            SyntheticHand synthetic = Resolve(state);
            if (synthetic == null)
            {
                // Rig yok (gözlemci / sahne yüklenmedi): kilit diye bir şey de yok.
                state.WristLocked = false;
                RefreshAnchorToWrist(state, hand, rightHand);
                return;
            }

            // ⚠️ Delta, pozu uygulamadan ÖNCE ölçülür ve kaynağı sentetik elin GİRDİSİDİR: kilitli
            // eli okumak, silahın nerede olduğunu silahın kendisine sormak olurdu.
            RefreshAnchorToWrist(state, hand, rightHand);

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
                FreeWristIfLocked(state);
                ApplyFingers(state, HandPoseLibrary.IdleJointRotations(rightHand));
                return;
            }

            if (kind == GripSocketKind.Secondary)
            {
                LockToSecondaryGrip(synthetic, weapon.transform,
                    definition.GetGrip(kind, rightHand), rightHand);
                state.WristLocked = true;
            }
            else
            {
                // ⚠️ ANA elde bilek KİLİTLENMEZ: eşyanın dönüşü artık kavrama kaydından geliyor,
                // yani silah zaten ele uyuyor (Weapon.ApplyCanonicalGrip). Bileği ayrıca kilitlemek
                // eli kumandadan koparır ve iki yazar aynı şeyi sürer.
                FreeWristIfLocked(state);
            }

            ApplyFingers(state, definition.GripJointRotations(kind, rightHand));
        }

        /// <summary>
        /// Ön kabzayı saran elin bileğini eşyanın üstündeki kayda <b>TAM</b> (konum + dönüş)
        /// kilitler — o el eşyaya yapışır.
        /// <para>
        /// Kayıt kumanda ANCHOR'ının eşyaya göre KONUMUDUR (<see cref="ItemGripPose"/>; dönüş yok —
        /// ön kabzadaki kumanda eşyayla hizalı sayılır); sentetik ele verilecek olan ise BİLEK. Köprü
        /// <see cref="ItemGripAuthority.WristFromAnchor"/>'dır (<c>wrist = (item ∘ kayıt) ∘ delta</c>,
        /// delta bu bileşenin canlı ölçümü). Delta yanlışsa bozulan şey silahın yönü değil, elin ön
        /// kabzada birkaç santim/derece kaymış durmasıdır.
        /// </para>
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
        /// <param name="grip">Kumanda anchor'ının EŞYAYA göre yerel konumu (metre, ölçeksiz).</param>
        /// <param name="rightHand">Kilitlenen el sağ mı (delta el başına ölçülür).</param>
        private static void LockToSecondaryGrip(SyntheticHand synthetic, Transform item,
            in ItemGripPose grip, bool rightHand)
        {
            // Kayıt dönüş taşımaz: ön kabzadaki kumanda eşyayla hizalı sayılır.
            var anchor = new Pose(item.position + item.rotation * grip.position, item.rotation);

            Pose wrist = ItemGripAuthority.WristFromAnchor(rightHand, anchor);
            synthetic.LockWristPose(wrist, 1f, SyntheticHand.WristLockMode.Full, true);
        }

        /// <summary>Ön kabzadan kalan bilek kilidini bırakır (parmaklara dokunmaz — çağıran onları
        /// hemen ardından yazıyor; <c>FreeAllJoints</c> çağrılsaydı el bir kare izlemeye dönüp
        /// titrerdi).</summary>
        private static void FreeWristIfLocked(HandState state)
        {
            if (!state.WristLocked)
            {
                return;
            }

            state.Synthetic.FreeWrist();
            state.WristLocked = false;
        }

        /// <summary>
        /// Elin parmaklarını hedef eklem dizisine götürür ve sentetik ele yazar.
        /// <para>
        /// <b>Geçiş:</b> hedef değiştiği karede o anki gösterilen dizi <see cref="HandState.From"/>'a
        /// kopyalanır ve ilerleme sıfırlanır; sonraki karelerde dizi
        /// <see cref="HandPoseLibrary.TransitionSeconds"/> boyunca hedefe slerp'lenir
        /// (<see cref="HandPoseLibrary.Ease"/>). Oturmuş durumda (ilerleme 1) hedef dizi aynen yazılır.
        /// </para>
        /// <para>
        /// ⚠️ <b>Beş parmak HER KAREDE kilitlenir</b> (<c>JointFreedom.Locked</c>) ve bu
        /// kısaltılamaz: serbestlik sentetik elde kalıcıdır ve başka bir bileşen (ISDK'nın kendi
        /// kavrama görselleri, <c>FreeAllJoints</c> çağıran bir el) onu değiştirebilir — kilit
        /// yazılmazsa o kareden sonra parmaklar donanıma döner ve tetik parmağı kumandayla
        /// kıpırdamaya başlar. Değişmeyen seviyeyi yeniden yazmak ISDK'da ucuzdur (karşılaştırıp
        /// geçer).
        /// </para>
        /// <para>⚠️ Hedef dizi ÖNBELLEKLİDİR ve paylaşılır (<see cref="HandState.Target"/>):
        /// değiştirilmez, yalnız okunur — ara dizi <see cref="HandState.Shown"/>'dur.</para>
        /// </summary>
        private static void ApplyFingers(HandState state, Quaternion[] goal)
        {
            SyntheticHand synthetic = state.Synthetic;
            Quaternion[] shown = state.Shown;

            // Referans kıyası: hedef diziler slot başına önbellekli olduğu için kimlik yeterli
            // (gerekçe HandState.Target'ta).
            if (!ReferenceEquals(goal, state.Target))
            {
                state.Target = goal;
                state.Progress = 0f;
                System.Array.Copy(shown, state.From, shown.Length);
            }

            int count = Mathf.Min(shown.Length, goal.Length);

            if (state.Progress < 1f)
            {
                state.Progress = HandPoseLibrary.TransitionSeconds > 0f
                    ? Mathf.Min(1f, state.Progress + Time.deltaTime / HandPoseLibrary.TransitionSeconds)
                    : 1f;

                float t = HandPoseLibrary.Ease(state.Progress);
                for (int i = 0; i < count; i++)
                {
                    shown[i] = Quaternion.Slerp(state.From[i], goal[i], t);
                }
            }
            else
            {
                System.Array.Copy(goal, shown, count);
            }

            synthetic.OverrideAllJoints(shown, 1f);
            for (int i = 0; i < FingerCount; i++)
            {
                synthetic.SetFingerFreedom((HandFinger)i, JointFreedom.Locked);
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
        private SyntheticHand Resolve(HandState state)
        {
            if (state.Synthetic != null)
            {
                return state.Synthetic;
            }

            if (Time.time < _nextScanAt)
            {
                return null;
            }

            _nextScanAt = Time.time + RescanSeconds;
            Scan();
            return state.Synthetic;
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
                    _right.Synthetic = hand;
                }
                else
                {
                    _left.Synthetic = hand;
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
