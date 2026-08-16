using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;
using VortexArena.Core.Arena;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Silahın <b>ÇERÇEVESİ</b>: silah sahnede çerçevenin içinde sabit durur ve oradan hiç ayrılmaz;
    /// oyuncu ≤<see cref="maxGrabDistance"/> m'den kumandayla nişan alıp grip'e basınca silahın bir
    /// KLONU eline gelir (klonu <see cref="WeaponGranter"/> üretir ve yönetir).
    /// <para>
    /// <b>Yalnız sabit duran silahta vardır:</b> silah hangi yoldan tutulursa tutulsun (verildi ya
    /// da kavrandı) çerçeve kapanır, bırakılınca geri gelir — <see cref="HandleHeldChanged"/>.
    /// <para>
    /// <c>VA_WeaponFrame</c> prefabının KÖKÜNDE durur ve o prefab her <c>WPN_*</c> prefabının
    /// ÇOCUĞU olarak bulunur. ⚠️ Temsil ettiği silahı <b>parent'ından</b> okur
    /// (<c>GetComponentInParent&lt;Weapon&gt;()</c> → <see cref="Weapon.Definition"/>); çerçevede
    /// ayrı bir <see cref="WeaponDefinition"/> alanı YOKTUR ve eklenmez — olsaydı aynı silah iki
    /// yerde yazılır, biri değiştirilip diğeri unutulunca çerçeve bir silahı gösterip başkasını
    /// verirdi.
    /// </para>
    /// <para>
    /// <b>Kapı ISDK'nın kendi uzatma noktasıdır</b>:
    /// bu bileşen bir <see cref="IGameObjectFilter"/>'dır ve çerçevenin mesafe-kavrama
    /// bileşenlerinin <c>_interactorFilters</c> listesine yazılır — mesafe kapısını
    /// <see cref="Filter"/> uygular, seçimin ALGISI ISDK'da kalır.
    /// </para>
    /// <para>
    /// ⚠️ <b>İKİ mesafe-kavrama bileşeni birden taşınır ve ikisi de dinlenir:</b>
    /// <see cref="DistanceGrabInteractable"/> (kumanda hattı) ve
    /// <see cref="DistanceHandGrabInteractable"/> (el hattı). Sebep, hangisinin koşacağına ISDK
    /// rig'inin karar vermesidir: interactor grubu "el izleniyor mu" sorusuna göre seçiliyor
    /// (<c>Controller and No Hand</c> ↔ <c>Controller and Hand</c>) ve el izleme
    /// <c>OVRManager.controllerDrivenHandPosesType</c> ile açılıp kapanıyor. Tek bileşen
    /// tutulsaydı o anahtarın her değişimi silahı sessizce alınamaz yapardı
    /// (<c>Docs/Sistem-Ozeti.md</c> §7).
    /// </para>
    /// <para>
    /// ⚠️ <b>El hattının <c>Hand Alignment</c>'ı prefabda <c>None</c>'dır ve öyle kalır.</b>
    /// <c>AlignOnGrab</c> olsaydı ISDK kavrama boyunca sentetik elin bileğini kavranan nesneye
    /// kilitlerdi (<c>HandGrabStateVisual</c> → <c>SyntheticHand.LockWristPose</c>); çerçeve
    /// <see cref="FrozenGrabTransformer"/> ile yerinde durduğu için oyuncunun eli sahnedeki silaha
    /// yapışır, oysa elinde silahın <b>klonu</b> vardır. Çerçeve bir kavrama hedefi değil bir
    /// SEÇİM tetikleyicisidir: ele dair hiçbir şeyi sürmemelidir.
    /// </para>
    /// </summary>
    public class WeaponFrame : MonoBehaviour, IGameObjectFilter
    {
        /// <summary>Nişan ışını materyalinin shader arama zinciri (ilk bulunan kullanılır).</summary>
        // ⚠️ Zincir ShotTracer'daki ile BİREBİR aynı ve "Sprites/Default" başta
        // duruyor: o shader Graphics Settings'in *Always Included Shaders* listesinde varsayılan
        // olarak bulunur → build'de kesin paketlenir (çalışma anında Shader.Find ile bulunan,
        // hiçbir materyalde referanslanmayan shader STRIPLENİR ve gösterge sahada sessizce
        // çizilmez). Vertex rengini çarptığı için LineRenderer.startColor de işler.
        private static readonly string[] ShaderCandidates =
        {
            "Sprites/Default",
            "Universal Render Pipeline/Unlit",
            "Unlit/Color",
        };

        // Fail-open uyarısı OTURUM başına bir kez (örnek başına değil): sahnedeki her silahta bir
        // çerçeve var, örnek başına olsaydı aynı teşhis satırı onlarca kez düşerdi.
        private static bool _warnedFailOpen;

        // "Çerçeve modeli yok" uyarısı da OTURUM başına bir kez: eksikse sahnedeki HER silahta
        // eksiktir (hepsi aynı prefabtan geliyor), örnek başına loglamak aynı satırı çoğaltırdı.
        private static bool _warnedNoFrameArt;

        [Header("Görünüm")]
        [Tooltip("Çerçeve görselini açar. Sahnedeki WPN_* ÖRNEĞİ üstünde override edilir — " +
                 "çerçevesiz durması istenen silahlarda kapatılır.")]
        [SerializeField] private bool isFrameVisible = true;

        [Tooltip("Çerçevenin KENDİ nişan ışınını çizer. VARSAYILAN KAPALI: aynı geri bildirimi " +
                 "ISDK'nın mesafe-kavrama göstergesi (tüp + reticle) zaten veriyor, ikisi birden " +
                 "çizilince oyuncu elinde iki ışın görür.")]
        [SerializeField] private bool isRayVisible;

        [Tooltip("Prefabdaki PASİF görsel kökü. Altındaki çerçeve MODELİ çalışma anında silahın " +
                 "ölçüsüne oturtulur (konum/dönüş/ölçek buradan yazılır).")]
        [SerializeField] private Transform frameVisual;

        [Tooltip("Silahın seçilebildiği en uzak mesafe (m) — AVUÇ ile çerçeve merkezi arası. " +
                 "⚠️ ISDK'nın kendi mesafe-kavrama konisi 5 m'de biter: bunun üstüne çıkmak işe " +
                 "yaramaz, silah aday bile olmaz.")]
        [SerializeField] private float maxGrabDistance = 4f;

        [Tooltip("Nişan ışınının rengi (alfa dahil).")]
        [SerializeField] private Color rayColor = new Color(0.35f, 0.7f, 1f, 0.9f);

        [Tooltip("Nişan ışınının kalınlığı (m).")]
        [SerializeField] private float rayWidth = 0.006f;

        [Tooltip("Silahın sınırlarına eklenen pay (m) — çerçeve silaha yapışık durmasın.")]
        [SerializeField] private float framePadding = 0.06f;

        [Header("Referanslar")]
        [Tooltip("Çerçevenin KENDİ mesafe-kavrama bileşeni (kumanda hattı). Boşsa GetComponent ile çözülür.")]
        [SerializeField] private DistanceGrabInteractable distanceGrab;

        [Tooltip("Çerçevenin KENDİ mesafe-kavrama bileşeni (el hattı). Boşsa GetComponent ile " +
                 "çözülür. İkisi birden tutulur — hangisinin koşacağını ISDK rig'i seçer.")]
        [SerializeField] private DistanceHandGrabInteractable distanceHandGrab;

        [Tooltip("Nişan alınacak hacim — silahın sınırlarına göre ÇALIŞMA ANINDA boyutlanır. " +
                 "Boşsa GetComponent ile çözülür.")]
        [SerializeField] private BoxCollider grabCollider;

        // Hover eden interactor'lar: id = PointerEvent.Identifier (Unhover/Cancel eşleştirme
        // anahtarı), ctl = ele çözülen OVR kontrolcüsü (None = çözülemedi → ışın çizilmez).
        private readonly List<(int id, OVRInput.Controller ctl)> _hovering =
            new List<(int, OVRInput.Controller)>();

        private Weapon _weapon;

        /// <summary>Çerçeve merkezinin SİLAHIN yerel uzayındaki konumu (Awake'te bir kez hesaplanır;
        /// kaynak silah dondurulduğu için bir daha değişmez).</summary>
        private Vector3 _centerLocal;

        private LineRenderer _rayLeft;
        private LineRenderer _rayRight;
        private Material _lineMaterial;
        private bool _warnedNoShader;
        private bool _detached;

        private void Awake()
        {
            _weapon = GetComponentInParent<Weapon>();
            if (_weapon == null)
            {
                // Çerçeve tek başına anlamsızdır: neyi temsil ettiğini yalnız parent silahtan
                // öğrenebiliyor. Sessiz kalsaydı sahnede "hiçbir şey yapmayan boş bir çerçeve"
                // olarak durur, teşhisi pahalı olurdu.
                Debug.LogWarning($"[WeaponFrame] '{name}' bir Weapon'ın altında değil; çerçeve " +
                                 "kapatıldı. VA_WeaponFrame prefabı WPN_* prefabının ÇOCUĞU olmalı.", this);
                enabled = false;
                return;
            }

            // Çerçeve YALNIZ sahnede sabit duran silaha aittir (bkz. HandleHeldChanged).
            // ⚠️ Abonelik Awake/OnDestroy'da kurulur, OnEnable/OnDisable'da DEĞİL: bu işleyici
            // çerçevenin kendi GameObject'ini kapatıyor — OnDisable'da abonelikten çıksaydı
            // "silah bırakıldı" sinyalini hiç duymaz ve çerçeve bir daha geri gelmezdi.
            _weapon.HeldChanged += HandleHeldChanged;

            if (distanceGrab == null)
            {
                distanceGrab = GetComponent<DistanceGrabInteractable>();
            }

            if (distanceHandGrab == null)
            {
                distanceHandGrab = GetComponent<DistanceHandGrabInteractable>();
            }

            if (distanceGrab == null && distanceHandGrab == null)
            {
                // ⚠️ Uyarı değil HATA: hiçbir kavrama bileşeni yoksa çerçeve sahnede görünür ama
                // silah HİÇ alınamaz — belirtisi "kavrama bozuk" diye okunur, oysa eksik olan
                // prefabdaki bir bileşendir. Kiti tazelemek (Configure All Build Elements) düzeltir.
                Debug.LogError($"[WeaponFrame] '{name}' üzerinde ne DistanceGrabInteractable ne " +
                               "DistanceHandGrabInteractable var; bu çerçeveden silah alınamaz. " +
                               "Tools > VortexArena > Build > Configure All Build Elements çalıştırılmalı.", this);
            }

            if (grabCollider == null)
            {
                grabCollider = GetComponent<BoxCollider>();
            }

            FreezeSource();

            // Ölçü BİR KEZ alınır ve iki tüketiciyi birden besler (çerçeve dikdörtgeni + nişan
            // hacmi): kaynak silah dondurulduğu için bir daha değişmez.
            bool measured = MeasureWeaponBounds(out Bounds local);
            _centerLocal = measured ? local.center : Vector3.zero;

            SizeGrabCollider(measured, local);
            BuildFrameVisual(measured, local);
        }

        private void OnEnable()
        {
            // ⚠️ İkisine de abone olunur ve bu ÇİFT SAYIM üretmez: bir karede yalnız bir interactor
            // grubu koşuyor (gerekçe sınıf açıklamasında), yani aynı olay iki bileşenden birden
            // gelmez. Gelse bile Identifier ayrı olurdu ve _hovering onları ayrı tutar.
            if (distanceGrab != null)
            {
                distanceGrab.WhenPointerEventRaised += HandlePointerEvent;
            }

            if (distanceHandGrab != null)
            {
                distanceHandGrab.WhenPointerEventRaised += HandlePointerEvent;
            }
        }

        private void OnDisable()
        {
            if (distanceGrab != null)
            {
                distanceGrab.WhenPointerEventRaised -= HandlePointerEvent;
            }

            if (distanceHandGrab != null)
            {
                distanceHandGrab.WhenPointerEventRaised -= HandlePointerEvent;
            }

            // ISDK'nın Unhover/Cancel olayları artık bize ulaşmaz; ışınlar açık kalmasın.
            _hovering.Clear();
            HideRay(_rayLeft);
            HideRay(_rayRight);
        }

        private void OnDestroy()
        {
            if (_weapon != null)
            {
                _weapon.HeldChanged -= HandleHeldChanged;
            }

            if (_lineMaterial != null)
            {
                Destroy(_lineMaterial);
                _lineMaterial = null;
            }
        }

        /// <summary>
        /// <b>Çerçeve yalnız silah sahnede SABİT dururken vardır.</b> Silah hangi yoldan tutulursa
        /// tutulsun (ele verildi — <see cref="Weapon.GrantTo"/>, ya da ISDK ile kavrandı) çerçeve
        /// kapanır; bırakılınca geri gelir.
        /// <para>
        /// Kural <see cref="Weapon.HeldChanged"/>'e bağlıdır, çağrı noktalarına değil: "silahı ele
        /// alan" birden çok yol var (<see cref="WeaponGranter"/>'ın iki kipi + doğrudan kavrama) ve
        /// her birine ayrı ayrı "çerçeveyi de kapat" eklemek, yeni bir yol açıldığında sessizce
        /// unutulacak bir adım demekti.
        /// </para>
        /// <para>
        /// ⚠️ Yok etmek DEĞİL kapatmak: aynı örnek bırakıldığında çerçevesiyle geri dönmeli.
        /// Kapatma <c>OnDisable</c> üzerinden nişan ışınlarını ve ISDK aboneliğini de toplar.
        /// </para>
        /// </summary>
        private void HandleHeldChanged(bool held)
        {
            gameObject.SetActive(!held);
        }

        private void Update()
        {
            if (_weapon == null || _detached || !isRayVisible)
            {
                // Kutu çalışma anında kaldırılmış olabilir (editörde denenir): açık kalan şeridi
                // de topla, yoksa ışın kapatıldığı hâliyle donar.
                HideRay(_rayLeft);
                HideRay(_rayRight);
                return;
            }

            TickRay(OVRInput.Controller.LTouch, ref _rayLeft);
            TickRay(OVRInput.Controller.RTouch, ref _rayRight);
        }

        /// <summary>
        /// <see cref="WeaponGranter"/> klon hazırlarken çağırır: çerçevenin görselini ve ışınlarını
        /// kapatır. Granter zaten çerçeve objesini yok ediyor; bu metot yalnız güvenli tarafta
        /// durmak için var — yok etme <c>Destroy</c> ile kare sonuna ertelendiği için aradaki
        /// karelerde elde çerçeve/ışın parlamasın.
        /// </summary>
        public void DetachForClone()
        {
            _detached = true;
            _hovering.Clear();
            HideRay(_rayLeft);
            HideRay(_rayRight);

            if (frameVisual != null)
            {
                frameVisual.gameObject.SetActive(false);
            }
        }

        // ------------------------------------------------------------ kaynak dondurma

        /// <summary>
        /// Çerçevedeki silahı yerine çiviler: fizik kapatılır, YAKIN kavrama yolları tümden kapanır.
        /// <para>
        /// Karar: çerçevedeki silah <b>YALNIZ uzaktan</b> seçilir. Bu yüzden
        /// <see cref="Grabbable"/>/<see cref="GrabInteractable"/> kapatılır. Ön kabza göstergesi için
        /// ayrıca bir şey yapılmaz: <see cref="Weapon"/> onu yalnız TUTULAN silahta çizer, çerçevedeki
        /// kaynak tutulmadığı için gösterge orada kendiliğinden yoktur.
        /// </para>
        /// <para>
        /// ⚠️ Tarama YALNIZ parent silahın ağacında yapılır ve <b>çerçevenin kendi alt ağacı ATLANIR</b>:
        /// çerçevenin <see cref="Grabbable"/>'ı ve <see cref="Rigidbody"/>'si uzaktan seçmenin ta
        /// kendisidir, kapatılırsa silah hiç alınamaz. (Çerçevenin kıpırdamamasını
        /// <see cref="FrozenGrabTransformer"/> sağlıyor, bileşeni kapatmak değil.)
        /// </para>
        /// </summary>
        private void FreezeSource()
        {
            Rigidbody[] bodies = _weapon.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                if (IsUnderFrame(bodies[i].transform))
                {
                    continue;
                }

                bodies[i].isKinematic = true;
                bodies[i].useGravity = false;
            }

            MonoBehaviour[] behaviours = _weapon.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || IsUnderFrame(behaviour.transform))
                {
                    continue;
                }

                // ⚠️ Kumanda ve el hattı BİRLİKTE kapatılır: biri açık kalırsa sahnedeki donmuş
                // silah çerçeveyi atlayarak doğrudan kavranabilir hale gelir.
                if (behaviour is Grabbable || behaviour is GrabInteractable ||
                    behaviour is HandGrabInteractable || behaviour is DistanceHandGrabInteractable)
                {
                    behaviour.enabled = false;
                }
            }
        }

        /// <summary>Bu transform çerçevenin alt ağacında mı (çerçevenin kendisi dahil).</summary>
        private bool IsUnderFrame(Transform candidate)
        {
            return candidate != null && candidate.IsChildOf(transform);
        }

        // ---------------------------------------------------------------- çerçeve görseli

        /// <summary>
        /// Çerçeve <b>MODELİNİ</b> (<see cref="frameVisual"/> altındaki sanat) silahın Renderer
        /// sınırlarına oturtur: düzlemi silahın en büyük iki eksenine çevirir, o iki ekseni
        /// kaplayacak kadar ölçekler ve silahın merkezine hizalar.
        /// <para>
        /// ⚠️ <b>Neden çalışma anında ve neden elle konmuyor</b> (<see cref="SizeGrabCollider"/>
        /// ile aynı gerekçe): çerçeve TEK bir prefabtır ve altı ayrı boydaki silahın altında
        /// duruyor. Her silaha elle çerçeve yerleştirmek, modelin ölçüsü/pivotu her değiştiğinde
        /// silah sayısı kadar elle iş demekti — ölçüyü <b>silah</b> söyler, prefab değil.
        /// </para>
        /// <para>
        /// ⚠️ <see cref="frameVisual"/> prefabda <b>PASİFTİR</b> ve burada yalnız
        /// <see cref="isFrameVisible"/> ise açılır. Gerekçe: <c>RemoteAvatar.SterilizeVisual</c>
        /// kopyadaki tüm MonoBehaviour'ları siler ama GameObject'leri KAPATMAZ (üstelik kopya pasif
        /// bir kuluçka kökünde kurulduğu için hiçbir <c>Awake</c> koşmaz). Pasif başlamasaydı hem
        /// uzak oyuncunun elindeki silahta hem de yerel klonda çerçeve görünürdü.
        /// </para>
        /// <para>
        /// İki tarafın da <b>en büyük iki ekseni</b> eşleştirilir (büyük→büyük, küçük→küçük):
        /// böylece yatan da duran da silah doğru çerçevelenir ve modelin hangi eksende modellendiği
        /// önemini yitirir. Derinlik ölçeği ikisinin KÜÇÜĞÜNE eşitlenir — düzlemde esneyen bir
        /// çerçevenin kalınlığı da esnerse ahşap profil sünmüş görünür.
        /// </para>
        /// </summary>
        private void BuildFrameVisual(bool measured, Bounds local)
        {
            if (!measured)
            {
                // Renderer yok (ya da hepsi çerçevenin altında): oturtulacak bir ölçü yok — ışının
                // bir hedefi olsun yeter (merkez silahın kökü sayıldı).
                return;
            }

            if (!isFrameVisible || frameVisual == null)
            {
                return;
            }

            // ⚠️ Ölçüm TABAN duruşta yapılmalı: aşağıdaki fit'in girdisi modelin ölçeklenmemiş
            // kutusudur. Sıfırlanmasaydı ikinci bir çağrı (ya da prefabda elle bırakılmış bir
            // ölçek) kendi üstüne çarpılır ve çerçeve her seferinde büyürdü.
            frameVisual.localPosition = Vector3.zero;
            frameVisual.localRotation = Quaternion.identity;
            frameVisual.localScale = Vector3.one;

            if (!MeasureFrameArtBounds(out Bounds art) || art.size.sqrMagnitude < 1e-8f)
            {
                WarnNoFrameArt();
                return;
            }

            frameVisual.gameObject.SetActive(true);

            // Düzlem eksenleri: her iki tarafta da "en büyük iki eksen", büyükten küçüğe sıralı.
            LargestTwoAxes(local.size, out int weaponA, out int weaponB);
            OrderBySize(local.size, ref weaponA, ref weaponB);
            int weaponNormal = 3 - weaponA - weaponB;

            LargestTwoAxes(art.size, out int artA, out int artB);
            OrderBySize(art.size, ref artA, ref artB);
            int artNormal = 3 - artA - artB;

            // Dönüş: modelin (düzlem normali, uzun kenarı) silahın karşılıklarına götürülür.
            // Silahın eksenleri ÇERÇEVE GÖRSELİNİN EBEVEYN uzayına çevrilir — silah kökü ile
            // çerçeve kökü aynı uzay olmak zorunda değil.
            Transform parent = frameVisual.parent;
            Vector3 targetNormal = ToParentDirection(parent, AxisVector(weaponNormal));
            Vector3 targetUp = ToParentDirection(parent, AxisVector(weaponA));

            Quaternion rotation =
                Quaternion.LookRotation(targetNormal, targetUp) *
                Quaternion.Inverse(Quaternion.LookRotation(AxisVector(artNormal), AxisVector(artA)));

            // Ölçek MODELİN kendi eksenlerinde yazılır (localScale dönüşten ÖNCE uygulanır).
            var scale = Vector3.one;
            scale[artA] = (local.size[weaponA] + framePadding * 2f) / Mathf.Max(art.size[artA], 1e-4f);
            scale[artB] = (local.size[weaponB] + framePadding * 2f) / Mathf.Max(art.size[artB], 1e-4f);
            scale[artNormal] = Mathf.Min(scale[artA], scale[artB]);

            // Konum: modelin ÖLÇÜLEN merkezi silahın merkezine otursun. Model pivotu merkezinde
            // olmak zorunda değil (bu modelde değil), o yüzden merkez farkı geri alınıyor —
            // dönüş+ölçek uygulandıktan SONRAKİ hâliyle.
            Vector3 targetCenter = parent.InverseTransformPoint(_weapon.transform.TransformPoint(local.center));

            frameVisual.localRotation = rotation;
            frameVisual.localScale = scale;
            frameVisual.localPosition = targetCenter - rotation * Vector3.Scale(scale, art.center);
        }

        /// <summary>İki ekseni BÜYÜKTEN küçüğe sıralar (büyük kenar büyük kenarla eşleşsin).</summary>
        private static void OrderBySize(in Vector3 size, ref int major, ref int minor)
        {
            if (size[minor] > size[major])
            {
                (major, minor) = (minor, major);
            }
        }

        /// <summary>Eksen indeksinin (0=X,1=Y,2=Z) birim vektörü.</summary>
        private static Vector3 AxisVector(int axis)
        {
            var v = Vector3.zero;
            v[axis] = 1f;
            return v;
        }

        /// <summary>Silahın yerel uzayındaki bir YÖNÜ çerçeve görselinin ebeveyn uzayına çevirir.</summary>
        private Vector3 ToParentDirection(Transform parent, in Vector3 weaponLocalDirection)
        {
            Vector3 world = _weapon.transform.TransformDirection(weaponLocalDirection);
            return parent != null ? parent.InverseTransformDirection(world).normalized : world.normalized;
        }

        /// <summary>
        /// Çerçeve sanatının sınırlarını <see cref="frameVisual"/>'ın YEREL uzayında ölçer.
        /// <para>⚠️ Dünya AABB'si (<c>Renderer.bounds</c>) DEĞİL, <c>localBounds</c>'ın köşeleri —
        /// gerekçesi <see cref="MeasureWeaponBounds"/>'takiyle aynı: döndürülmüş duran bir modelin
        /// dünya kutusu gerçek boyutundan büyük çıkar ve çerçeve olduğundan geniş ölçeklenirdi.</para>
        /// </summary>
        private bool MeasureFrameArtBounds(out Bounds local)
        {
            local = new Bounds();
            bool any = false;

            Renderer[] renderers = frameVisual.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                Bounds rendererLocal = renderer.localBounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var offset = new Vector3(
                        (corner & 1) == 0 ? rendererLocal.min.x : rendererLocal.max.x,
                        (corner & 2) == 0 ? rendererLocal.min.y : rendererLocal.max.y,
                        (corner & 4) == 0 ? rendererLocal.min.z : rendererLocal.max.z);

                    Vector3 point = frameVisual.InverseTransformPoint(
                        renderer.transform.TransformPoint(offset));

                    if (!any)
                    {
                        local = new Bounds(point, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        local.Encapsulate(point);
                    }
                }
            }

            return any;
        }

        /// <summary>
        /// Görsel kökünün altında hiç Renderer yoksa bir kez uyarır. <b>Neden loglanıyor:</b>
        /// çerçeve sessizce hiç çizilmez ama silah yine seçilebilir — yani özellik "çalışıyor gibi
        /// görünüp" görünmez kalır. Genellikle sebebi çerçeve modelinin prefabtan düşmesidir.
        /// </summary>
        private static void WarnNoFrameArt()
        {
            if (_warnedNoFrameArt)
            {
                return;
            }

            _warnedNoFrameArt = true;
            Debug.LogWarning("[WeaponFrame] Görsel kökünün altında Renderer yok — çerçeve " +
                             "çizilmeyecek (silah yine seçilebilir). VA_WeaponFrame prefabında " +
                             "FrameVisual altındaki çerçeve modeli duruyor mu?");
        }

        /// <summary>
        /// Nişan hacmini silahın sınırlarına oturtur (<see cref="framePadding"/> payıyla).
        /// <para>
        /// ⚠️ <b>Neden çalışma anında:</b> <c>VA_WeaponFrame</c> TEK bir prefabtır ve altı ayrı
        /// boydaki silahın altında duruyor. Prefabda sabit bir kutu bırakılsaydı kısa silahta
        /// gereğinden geniş (yanındaki silahı da yutan), uzun silahta dar (namlusuna nişan alınca
        /// tutmayan) bir hedef olurdu — yani ölçüyü <b>silah</b> söylemeli, prefab değil.
        /// </para>
        /// <para>
        /// Bu kutu <see cref="DistanceGrabInteractable"/>'ın aday hesabının TEK girdisidir: ISDK
        /// mesafe kavraması <c>Physics.Raycast</c> yapmaz, doğrudan
        /// <c>Rigidbody.GetComponentsInChildren&lt;Collider&gt;()</c> üstünden koni testi yapar.
        /// Dolayısıyla katman/maske kurulumu gerekmez ve kutu <b>trigger olabilir</b> (öyledir:
        /// free-roam'da silahın fiziksel çarpışması yok).
        /// </para>
        /// <para>Ölçüsüz kalırsa (Renderer yok) prefabtaki kutuya DOKUNULMAZ — hiç olmamasındansa
        /// kaba bir hedef iyidir.</para>
        /// </summary>
        private void SizeGrabCollider(bool measured, Bounds local)
        {
            if (grabCollider == null || !measured)
            {
                return;
            }

            // Silahın yerel kutusu ÇERÇEVENİN yerel uzayına çevrilir: ikisi aynı olmak zorunda
            // değil (prefabda çerçeve kaydırılmış/döndürülmüş olabilir), o yüzden sekiz köşe
            // taşınıp yeniden kutulanıyor.
            Bounds inFrame = default;
            bool any = false;

            for (int corner = 0; corner < 8; corner++)
            {
                var point = new Vector3(
                    (corner & 1) == 0 ? local.min.x : local.max.x,
                    (corner & 2) == 0 ? local.min.y : local.max.y,
                    (corner & 4) == 0 ? local.min.z : local.max.z);

                Vector3 inFrameSpace = transform.InverseTransformPoint(_weapon.transform.TransformPoint(point));

                if (!any)
                {
                    inFrame = new Bounds(inFrameSpace, Vector3.zero);
                    any = true;
                }
                else
                {
                    inFrame.Encapsulate(inFrameSpace);
                }
            }

            grabCollider.center = inFrame.center;
            grabCollider.size = inFrame.size + Vector3.one * (framePadding * 2f);
        }

        /// <summary>
        /// Silahın Renderer'larını SİLAHIN yerel uzayında ölçer (çerçevenin kendi görselleri hariç).
        /// <para>⚠️ Dünya AABB'si (<c>Renderer.bounds</c>) DEĞİL, <c>localBounds</c>'ın köşeleri
        /// kullanılır: silah döndürülmüş duruyorsa dünya kutusu gerçek boyutundan büyük çıkar ve
        /// çerçeve silahtan kat kat geniş olurdu.</para>
        /// </summary>
        private bool MeasureWeaponBounds(out Bounds local)
        {
            local = new Bounds();
            bool any = false;

            Renderer[] renderers = _weapon.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || IsUnderFrame(renderer.transform))
                {
                    continue;
                }

                Bounds rendererLocal = renderer.localBounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var offset = new Vector3(
                        (corner & 1) == 0 ? rendererLocal.min.x : rendererLocal.max.x,
                        (corner & 2) == 0 ? rendererLocal.min.y : rendererLocal.max.y,
                        (corner & 4) == 0 ? rendererLocal.min.z : rendererLocal.max.z);

                    Vector3 point = _weapon.transform.InverseTransformPoint(
                        renderer.transform.TransformPoint(offset));

                    if (!any)
                    {
                        local = new Bounds(point, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        local.Encapsulate(point);
                    }
                }
            }

            return any;
        }

        /// <summary>En büyük iki eksenin indeksini döndürür (0=X, 1=Y, 2=Z).</summary>
        private static void LargestTwoAxes(Vector3 size, out int axisA, out int axisB)
        {
            int smallest = 0;
            if (size.y < size[smallest])
            {
                smallest = 1;
            }

            if (size.z < size[smallest])
            {
                smallest = 2;
            }

            axisA = smallest == 0 ? 1 : 0;
            axisB = smallest == 2 ? 1 : 2;
        }

        // -------------------------------------------------------------------- ISDK kapısı

        private void HandlePointerEvent(PointerEvent evt)
        {
            switch (evt.Type)
            {
                case PointerEventType.Hover:
                    AddHover(evt);
                    break;

                case PointerEventType.Select:
                    // Seçim = "bu silah artık benim": klonu WeaponGranter üretir/gizler, bu bileşenin
                    // eldeki silahla hiçbir işi yoktur (kaynak silah çerçevede kalmaya devam eder).
                    WeaponGranter.SelectWeapon(_weapon != null ? _weapon.Definition : null);
                    _hovering.Clear();
                    HideRay(_rayLeft);
                    HideRay(_rayRight);
                    break;

                case PointerEventType.Unhover:
                case PointerEventType.Unselect:
                case PointerEventType.Cancel:
                    RemoveHover(evt.Identifier);
                    break;
            }
        }

        private void AddHover(in PointerEvent evt)
        {
            for (int i = 0; i < _hovering.Count; i++)
            {
                if (_hovering[i].id == evt.Identifier)
                {
                    return;
                }
            }

            _hovering.Add((evt.Identifier, WeaponGranter.ResolveController(evt)));
        }

        private void RemoveHover(int identifier)
        {
            for (int i = 0; i < _hovering.Count; i++)
            {
                if (_hovering[i].id != identifier)
                {
                    continue;
                }

                _hovering.RemoveAt(i);
                return;
            }
        }

        /// <summary>
        /// ISDK kapısı: bu interactor şu an silahı seçebilir mi — yalnız MESAFE sorusu
        /// (<see cref="maxGrabDistance"/>).
        /// <para>
        /// ⚠️ <b>"Elde çift elli silah varken seçim kapansın" diye bir kapı BURAYA EKLENMEZ.</b>
        /// Çerçeve bir kavrama değil bir <b>seçim</b> tetikleyicisidir ve seçimi değiştirmek ikinci
        /// bir silah üretmez: çift elli seçimde <c>WeaponGranter</c> oyuncu başına zaten TEK klon
        /// tutar, yeni tanım eskisinin yerine geçer. Böyle bir kapı rafta silah değiştirmeyi de
        /// kapatır ve belirtisi teşhis edilmesi zor bir biçimde görünür: <b>ışın çıkar ama seçim
        /// olmaz</b> — çünkü grip'e basıldığı anda granter eski silahın klonunu ele çağırır ve kapı
        /// tam o karede kapanır. "Aynı anda ikinci silah tutulamaz" kuralının yeri
        /// <c>WeaponGranter.TickHand</c>'dir (rastgele dağıtım yolu, öteki el).
        /// </para>
        /// <para>
        /// ⚠️ <b>El/anchor çözülemezse FAIL-OPEN</b> (izin verilir): bu bir emniyet kapısı değil bir
        /// HİS kapısıdır — editör oturumunda kontrolcü çözülemez ve fail-close olsaydı silah editörde
        /// hiç seçilemez, yani sahne testi imkânsız hale gelirdi.
        /// </para>
        /// </summary>
        public bool Filter(GameObject interactorGameObject)
        {
            // ⚠️ Kalibresiz oyuncu silah ALAMAZ (§10.6) ve kapı BURADA durur, seçim yolunda değil:
            // aday listesinden düşen çerçeve ışın/reticle de çizdirmez. Seçim tarafına konsaydı
            // oyuncu nişan alır, grip'e basar ve hiçbir şey olmazdı — bu sınıfın kendi yorumundaki
            // "ışın çıkar ama seçim olmaz" belirtisinin ta kendisi.
            // Silahın ele GELMESİ ayrıca WeaponGranter.CanHoldWeapon ile kapalıdır; buradaki kapı
            // onun yerine geçmez, oyuncuya doğru geri bildirimi verir.
            if (!CalibrationState.IsCalibrated)
            {
                return false;
            }

            OVRInput.Controller hand = WeaponGranter.ResolveControllerFromGameObject(interactorGameObject);
            if (hand == OVRInput.Controller.None)
            {
                WarnFailOpen();
                return true;
            }

            if (!WeaponGranter.TryResolvePalm(hand, out Pose palm))
            {
                return true; // rig yok → kapı anlamsız
            }

            return (palm.position - FrameCenterWorld).sqrMagnitude <= maxGrabDistance * maxGrabDistance;
        }

        // ------------------------------------------------------------------ nişan ışını

        /// <summary>Çerçeve merkezinin dünya konumu (ışının hedefi ve mesafe ölçüsünün kaynağı).</summary>
        private Vector3 FrameCenterWorld =>
            _weapon != null ? _weapon.transform.TransformPoint(_centerLocal) : transform.position;

        /// <summary>
        /// Hover eden elin AVUCUNDAN çerçeve merkezine ışın çizer — yalnız el MENZİL İÇİNDEYSE.
        /// <para>
        /// ⚠️ <b>Bu ışın varsayılan olarak KAPALIDIR</b> (<see cref="isRayVisible"/>): ISDK'nın
        /// mesafe kavraması kendi göstergesini çiziyor (tüp + reticle) ve ikisi birden açıkken
        /// oyuncu elinde iki ışın görüyor.
        /// </para>
        /// <para>
        /// ⚠️ ISDK'nın göstergesi <b>menzil dışında yalan söylemez</b>, o yüzden kapatmak bir şey
        /// kaybettirmiyor: mesafe kavramasının aday listesi
        /// <c>InteractableRegistry.List(interactor)</c>'dan geçiyor ve orası her adayı
        /// <c>CanBeSelectedBy</c> ile — yani <see cref="Filter"/> ile — süzüyor. Menzil dışındaki
        /// çerçeve aday bile olmaz, dolayısıyla hover göstergesi hiç çıkmaz.
        /// </para>
        /// <para>
        /// Mesafe testi burada yine de tekrarlanır: <see cref="Filter"/> el çözülemediğinde
        /// FAIL-OPEN'dır (editör oturumu), o durumda ışının kendi kapısı olmalı.
        /// </para>
        /// </summary>
        private void TickRay(OVRInput.Controller hand, ref LineRenderer ray)
        {
            if (!IsHovering(hand))
            {
                HideRay(ray);
                return;
            }

            // Mesafe AVUÇtan ölçülür — Filter ile aynı kaynaktan, yoksa ışın kapının izin verdiği
            // menzilden birkaç santim önce/sonra sönerdi.
            if (!WeaponGranter.TryResolvePalm(hand, out Pose palm))
            {
                HideRay(ray);
                return;
            }

            Vector3 center = FrameCenterWorld;
            if ((palm.position - center).sqrMagnitude > maxGrabDistance * maxGrabDistance)
            {
                HideRay(ray);
                return;
            }

            ray = ray != null ? ray : CreateRay(hand);
            if (ray == null)
            {
                return;
            }

            ray.SetPosition(0, palm.position);
            ray.SetPosition(1, center);
            ray.enabled = true;
        }

        private bool IsHovering(OVRInput.Controller hand)
        {
            for (int i = 0; i < _hovering.Count; i++)
            {
                if (_hovering[i].ctl == hand)
                {
                    return true;
                }
            }

            return false;
        }

        private LineRenderer CreateRay(OVRInput.Controller hand)
        {
            Material material = EnsureLineMaterial();
            if (material == null)
            {
                return null;
            }

            var go = new GameObject(hand == OVRInput.Controller.LTouch ? "[FrameRay_L]" : "[FrameRay_R]");
            go.transform.SetParent(transform, false);

            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            // Dünya uzayı: uçlardan biri ELDE, öteki çerçevede — ikisi aynı transformun altında değil.
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.textureMode = LineTextureMode.Stretch;
            line.alignment = LineAlignment.View;
            line.startWidth = rayWidth;
            line.endWidth = rayWidth;
            line.startColor = rayColor;
            line.endColor = rayColor;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            line.enabled = false;

            return line;
        }

        private static void HideRay(LineRenderer ray)
        {
            if (ray != null && ray.enabled)
            {
                ray.enabled = false;
            }
        }

        // ---------------------------------------------------------------------- yardımcı

        /// <summary>İki nişan ışınının PAYLAŞTIĞI materyal (renk LineRenderer'ın vertex renginden
        /// gelir). Çerçevenin kendisi artık çizilmiyor — o bir MODEL ve kendi materyalini taşıyor;
        /// burası yalnız ışınlar için.</summary>
        private Material EnsureLineMaterial()
        {
            if (_lineMaterial != null)
            {
                return _lineMaterial;
            }

            for (int i = 0; i < ShaderCandidates.Length; i++)
            {
                Shader shader = Shader.Find(ShaderCandidates[i]);
                if (shader != null)
                {
                    _lineMaterial = new Material(shader) { name = "M_WeaponFrame(runtime)" };
                    return _lineMaterial;
                }
            }

            if (!_warnedNoShader)
            {
                _warnedNoShader = true;
                Debug.LogWarning(
                    "[WeaponFrame] Nişan ışını için shader bulunamadı (Sprites/Default dahil) — " +
                    "silah yine seçilebilir ve çerçeve görünür, ama nişan ışını çizilmez. " +
                    "Graphics Settings > Always Included Shaders listesini kontrol et.", this);
            }

            return null;
        }

        /// <summary>
        /// Fail-open bir kez loglanır. <b>Neden loglanıyor:</b> el çözülemediğinde mesafe kapısı
        /// tümüyle devre dışı kalır ve silah arenanın öbür ucundan da seçilebilir — yani özellik
        /// <i>çalışıyor gibi görünüp</i> hiçbir şey yapmaz.
        /// <para>Editör oturumunda BEKLENEN durumdur (kumanda yok); başlıkta görülüyorsa BB
        /// kontrolcü rig'inin <c>InteractorControllerDecorator</c> kurulumu eksiktir.</para>
        /// </summary>
        private static void WarnFailOpen()
        {
            if (_warnedFailOpen)
            {
                return;
            }

            _warnedFailOpen = true;
            Debug.LogWarning("[WeaponFrame] Interactor'dan el çözülemedi — mesafe kapısı AÇIK " +
                             "bırakıldı (silah her mesafeden seçilebilir). Editörde normaldir; " +
                             "başlıkta görülüyorsa rig'in InteractorControllerDecorator kurulumuna bak.");
        }
    }
}
