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
    /// <b>Bilek HER DURUMDA kilitlidir</b> — ne izlemeden ne Meta'nın kumandadan sentezlediği
    /// "doğal" el pozundan gelir:
    /// <list type="bullet">
    /// <item><b>Boş el:</b> bilek <b>KUMANDAYA</b> kilitlenir
    /// (<see cref="ItemGripAuthority.WristFromAnchor"/> — anchor + ofset). El kumandanın rijit bir
    /// parçası gibi davranır.</item>
    /// <item><b>Eşya tutan el</b> (ana kabza da ön kabza da): bilek <b>EŞYAYA</b> kilitlenir
    /// (kaydın anchor'ı + ofset) — el silaha yapışır ve silah nereye dönerse onunla döner.</item>
    /// </list>
    /// ⚠️ <b>Ana el de EŞYADAN türetilir ve bu bilinçlidir:</b> iki elli tutuşta silahın dönüşü ana
    /// kumandanınki değildir (ön kabzadaki ele nişanlanır), yani ana el kumandaya kilitli kalsaydı
    /// oyuncu silahı ön kabzadan çevirdiğinde el yerinde kalır ve silahın dışında görünürdü.
    /// Tek elli tutuşta bu bir şey değiştirmez: çözücünün kimliği gereği eşyadan türetilen anchor
    /// KONUMU zaten kumanda anchor'ının kendisidir, eşyadan gelen tek şey dönüştür.
    /// Ofset, kavraması yazılmış slotta <b>o slotun kendi el yerleşimidir</b> (stüdyoda <c>Hand</c>
    /// modeli taşınıp çevrilerek yazılır: kimi silah yandan, kimi alttan tutulur), yazılmamışta
    /// paylaşılan tanım. Silahın kumandaya göre yeri bundan ETKİLENMEZ.
    /// Parmaklar boş elde boşta duruşunda (<see cref="HandPoseLibrary.IdleJointRotations"/>), eşya
    /// tutan elde o slot için riglenmiş duruştadır.
    /// </para>
    /// <para>
    /// ⚠️ <b>Bileği serbest bırakmak GERİ GELMEZ.</b> Serbestken bilek Meta'nın kumandadan
    /// sentezlediği el pozundan geliyordu; o pozun anchor'a göre ofseti bizde yazılı değil, silah ise
    /// anchor'dan konumlanıyor — yani el ile silah iki ayrı referanstan çiziliyordu ve stüdyoda
    /// yazılan kavrama oyunda birkaç santim kaymış görünüyordu. Ofseti kilitle birlikte kendimiz
    /// tanımlayınca tezgâh ile oyun <b>kurgu gereği</b> aynı oluyor ve ölçülecek bir sabit kalmıyor.
    /// Bedeli, elin kumandaya rijit bağlı olmasıdır (doğal bilek oynaması yok) — parmaklar zaten
    /// donanımdan sürülmediği için tutarlı olan da budur.
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

            /// <summary>Elin BİLEĞİ şu an kilitli mi (kumandaya ya da eşyaya). Yalnız kumanda
            /// anchor'ı hiç çözülemediğinde düşer — o hâlde ortada çizilecek bir el de yoktur.</summary>
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

            /// <summary>Yeni sahne / ilk kurulum: her şey idle'a ve "oturmuş" durumuna döner.</summary>
            public void Reset(bool rightHand)
            {
                Synthetic = null;
                WristLocked = false;
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
        /// <para>⚠️ <b>Kilit hedefini KAVRAMANIN VARLIĞI belirler, kavrama NOKTASI değil:</b> eşya
        /// tutan el (ana kabza ya da ön kabza fark etmez) EŞYAYA, boş el KUMANDAYA kilitlenir. İkisi
        /// de ofseti aynı kapıdan alır (<see cref="ItemGripAuthority"/>), yani el hiçbir durumda
        /// "başka bir yerden" gelmez — ofsetin kendisi slot başına yazılabilir
        /// (<see cref="ItemGripPose.Wrist"/>), yazılmamışsa paylaşılan tanıma düşer.</para>
        /// <para>Parmaklar da her durumda bizim yazdığımızdır: boş elde boşta duruşu, eşya tutan elde
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
                return;
            }

            Weapon weapon = FindWeaponUsing(hand, out GripSocketKind kind);
            ItemDefinition definition = weapon != null ? weapon.Definition : null;
            bool hasGrip = definition != null && definition.HasGrip(kind, rightHand);

            if (weapon != null && !hasGrip)
            {
                // Kavraması yazılmamış silah: el idle'a düşer.
                WarnMissingPose(weapon, kind, rightHand);
            }

            // Elin kumanda üstündeki yerleşimi kavramanın PARÇASIDIR: kavraması yazılmış slot kendi
            // yerleşimini getirir (ön kabzayı yandan saran el ile kabzayı avuçlayan el aynı açıda
            // duramaz), boş el paylaşılan tanıma düşer.
            ItemGripPose grip = hasGrip ? definition.GetGrip(kind, rightHand) : default;
            Pose anchorToWrist = hasGrip
                ? ItemGripAuthority.ResolveAnchorToWrist(grip, rightHand)
                : ItemGripAuthority.ResolveAnchorToWrist(rightHand);

            if (hasGrip)
            {
                LockToItemGrip(synthetic, weapon.transform, grip, anchorToWrist);
                state.WristLocked = true;
            }
            else
            {
                LockToController(synthetic, hand, anchorToWrist, state);
            }

            ApplyFingers(state, hasGrip
                ? definition.GripJointRotations(kind, rightHand)
                : HandPoseLibrary.IdleJointRotations(rightHand));
        }

        /// <summary>
        /// <b>Boş elin</b> bileğini <b>kumandaya</b> kilitler: anchor + tanımlı ofset
        /// (<see cref="ItemGripAuthority.WristFromAnchor"/>). Eşya tutan el buraya uğramaz —
        /// o <see cref="LockToItemGrip"/>'ten geçer.
        /// <para>
        /// ⚠️ <b>Bu kilit, elin Meta'nın sentezlediği "doğal" el pozundan gelmesini bilerek
        /// engeller.</b> O poz anchor'a göre bizde yazılı olmayan bir ofset taşıyor; silah ise
        /// anchor'dan konumlanıyor. İki referans = tezgâhta yazılan kavramanın oyunda kaymış
        /// görünmesi. Kilitle birlikte el ile silah tek referanstan çıkıyor ve stüdyodaki hayalet el
        /// aynı ofseti okuduğu için tezgâh ile oyun kurgu gereği aynı oluyor.
        /// </para>
        /// <para>⚠️ Anchor çözülemezse kilit BIRAKILIR (rig henüz yok): kilitli bırakmak eli son
        /// bilinen yerde dondururdu.</para>
        /// <para>⚠️ Poz DÜNYA uzayındadır ve öyle verilmek zorunda (<c>worldPose: true</c>) —
        /// gerekçe <see cref="LockToItemGrip"/>'te.</para>
        /// </summary>
        private static void LockToController(SyntheticHand synthetic, OVRInput.Controller hand,
            in Pose anchorToWrist, HandState state)
        {
            // Rig keşfinin TEK yolu: ikinci bir arama açmak iki bileşenin farklı karelerde farklı
            // rig bulmasına yol açardı (Scan ile aynı gerekçe).
            Transform anchor = WeaponGranter.ResolveHandAnchor(hand);
            if (anchor == null)
            {
                FreeWristIfLocked(state);
                return;
            }

            Pose wrist = ItemGripAuthority.WristFromAnchor(
                new Pose(anchor.position, anchor.rotation), anchorToWrist);
            synthetic.LockWristPose(wrist, 1f, SyntheticHand.WristLockMode.Full, true);
            state.WristLocked = true;
        }

        /// <summary>
        /// Eşya tutan elin bileğini eşyanın üstündeki kayda <b>TAM</b> (konum + dönüş) kilitler —
        /// o el eşyaya yapışır. <b>İki kavrama noktası için de aynı yol</b>: ana kabza da ön kabza da
        /// buradan geçer.
        /// <para>
        /// ⚠️ <b>Ana el neden KUMANDAYA değil EŞYAYA kilitleniyor:</b> iki elli tutuşta silahın
        /// dönüşü ana kumandanınki DEĞİLDİR — <see cref="ItemGripSolver"/> onu ön kabzadaki ele
        /// nişanlar. Ana el kumandaya kilitli kalsaydı, oyuncu ön kabzadan silahı çevirdiğinde silah
        /// döner ama arka el dönmez ve el silahın dışında kalırdı. Eşyadan türetmek bunu kurgu gereği
        /// kapatır ve <b>tek elli tutuşta hiçbir şeyi değiştirmez</b>: çözücü kimliği gereği
        /// <c>item.position + item.rotation * kayıt</c> her zaman ana kumanda anchor'ının TA
        /// KENDİSİDİR (<c>itemPosition = palm.position − itemRotation * kayıt</c>), yani elin
        /// KONUMU her hâlükârda kumandada kalır — eşyadan gelen tek şey DÖNÜŞTÜR.
        /// </para>
        /// <para>
        /// Kayıt kumanda ANCHOR'ının eşyaya göre KONUMUDUR (<see cref="ItemGripPose"/>; anchor
        /// yarısında dönüş yok — kumanda eşyayla hizalı sayılır); sentetik ele verilecek olan ise
        /// BİLEK. Köprü <see cref="ItemGripAuthority.WristFromAnchor"/>'dır
        /// (<c>wrist = (item ∘ kayıt) ∘ delta</c>, delta o slotun el yerleşimi). Delta yanlışsa
        /// bozulan şey silahın yönü değil, elin kabzada birkaç santim/derece kaymış durmasıdır.
        /// </para>
        /// <para>
        /// ⚠️ <b>Kilit KOŞULSUZDUR</b> — mesafe/açı kapısı yoktur ve eklenmez. Takas bilinçli:
        /// bedeli, fiziksel kumanda ön kabzadan uzaklaştığında elin kolla arasının görsel olarak
        /// gerilmesidir; kazancı, oyuncu grip tuşunu bırakmadıkça elin silahtan kopmamasıdır.
        /// Mesafeye bakan bir kapı eli oyuncu hiçbir şey yapmadan bırakır ve "silahı iki elle
        /// tuttum ama ikinci el havada" hissi üretir.
        /// </para>
        /// <para>
        /// ⚠️ Uyarı ana el için <b>okunmaz</b>: orada kilidin konumu zaten kumandadadır (yukarıdaki
        /// kimlik), yani gerilecek bir mesafe hiç doğmaz.
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
        /// <param name="grip">Kavrama kaydı — kumanda anchor'ının EŞYAYA göre yerel konumu (metre,
        /// ölçeksiz).</param>
        /// <param name="anchorToWrist">O slotun el yerleşimi (yazılmamışsa paylaşılan tanım).</param>
        private static void LockToItemGrip(SyntheticHand synthetic, Transform item,
            in ItemGripPose grip, in Pose anchorToWrist)
        {
            // Anchor kaydı dönüş taşımaz: kabzadaki kumanda eşyayla hizalı sayılır. Elin o kumandanın
            // üstündeki açısı ayrı bir alandır (anchorToWrist) ve tam burada devreye girer: kimi
            // kabza yandan, kimi alttan tutuluyor.
            var anchor = new Pose(item.position + item.rotation * grip.position, item.rotation);

            Pose wrist = ItemGripAuthority.WristFromAnchor(anchor, anchorToWrist);
            synthetic.LockWristPose(wrist, 1f, SyntheticHand.WristLockMode.Full, true);
        }

        /// <summary>Bilek kilidini bırakır — <b>tek çağıranı</b> anchor'ın hiç çözülemediği hâldir
        /// (rig yok). Parmaklara dokunmaz: çağıran onları hemen ardından yazıyor ve
        /// <c>FreeAllJoints</c> çağrılsaydı el bir kare izlemeye dönüp titrerdi.</summary>
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
