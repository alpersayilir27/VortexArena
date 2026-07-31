using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Silahın <b>ÇERÇEVESİ</b>: silah sahnede çerçevenin içinde sabit durur ve oradan hiç ayrılmaz;
    /// oyuncu ≤<see cref="maxGrabDistance"/> m'den kumandayla nişan alıp grip'e basınca silahın bir
    /// KLONU eline gelir (klonu <see cref="WeaponGranter"/> üretir ve yönetir).
    /// <para>
    /// <c>VA_WeaponFrame</c> prefabının KÖKÜNDE durur ve o prefab her <c>WPN_*</c> prefabının
    /// ÇOCUĞU olarak bulunur. ⚠️ Temsil ettiği silahı <b>parent'ından</b> okur
    /// (<c>GetComponentInParent&lt;Weapon&gt;()</c> → <see cref="Weapon.Definition"/>); çerçevede
    /// ayrı bir <see cref="WeaponDefinition"/> alanı YOKTUR ve eklenmez — olsaydı aynı silah iki
    /// yerde yazılır, biri değiştirilip diğeri unutulunca çerçeve bir silahı gösterip başkasını
    /// verirdi.
    /// </para>
    /// <para>
    /// <b>Kapı ISDK'nın kendi uzatma noktasıdır</b> (<see cref="ItemGripSockets"/> ile aynı desen):
    /// bu bileşen bir <see cref="IGameObjectFilter"/>'dır ve çerçevenin kendi
    /// <see cref="DistanceGrabInteractable"/>'ının <c>_interactorFilters</c> listesine yazılır —
    /// mesafe kapısını <see cref="Filter"/> uygular, seçimin ALGISI ISDK'da kalır.
    /// </para>
    /// </summary>
    public class WeaponFrame : MonoBehaviour, IGameObjectFilter
    {
        /// <summary>Nişan ışını materyalinin shader arama zinciri (ilk bulunan kullanılır).</summary>
        // ⚠️ Zincir ItemGripSockets/ShotTracer'daki ile BİREBİR aynı ve "Sprites/Default" başta
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

        [Tooltip("Prefabdaki PASİF görsel kökü. Altındaki çerçeve MODELİ çalışma anında silahın " +
                 "ölçüsüne oturtulur (konum/dönüş/ölçek buradan yazılır).")]
        [SerializeField] private Transform frameVisual;

        [Tooltip("Silahın seçilebildiği en uzak mesafe (m) — el anchor'ı ile çerçeve merkezi arası.")]
        [SerializeField] private float maxGrabDistance = 2f;

        [Tooltip("Nişan ışınının rengi (alfa dahil).")]
        [SerializeField] private Color rayColor = new Color(0.35f, 0.7f, 1f, 0.9f);

        [Tooltip("Nişan ışınının kalınlığı (m).")]
        [SerializeField] private float rayWidth = 0.006f;

        [Tooltip("Silahın sınırlarına eklenen pay (m) — çerçeve silaha yapışık durmasın.")]
        [SerializeField] private float framePadding = 0.06f;

        [Header("Referanslar")]
        [Tooltip("Çerçevenin KENDİ mesafe-kavrama bileşeni. Boşsa GetComponent ile çözülür.")]
        [SerializeField] private DistanceGrabInteractable distanceGrab;

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

            if (distanceGrab == null)
            {
                distanceGrab = GetComponent<DistanceGrabInteractable>();
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
            if (distanceGrab != null)
            {
                distanceGrab.WhenPointerEventRaised += HandlePointerEvent;
            }
        }

        private void OnDisable()
        {
            if (distanceGrab != null)
            {
                distanceGrab.WhenPointerEventRaised -= HandlePointerEvent;
            }

            // ISDK'nın Unhover/Cancel olayları artık bize ulaşmaz; ışınlar açık kalmasın.
            _hovering.Clear();
            HideRay(_rayLeft);
            HideRay(_rayRight);
        }

        private void OnDestroy()
        {
            if (_lineMaterial != null)
            {
                Destroy(_lineMaterial);
                _lineMaterial = null;
            }
        }

        private void Update()
        {
            if (_weapon == null || _detached)
            {
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
        /// <see cref="Grabbable"/>/<see cref="GrabInteractable"/> ile birlikte
        /// <see cref="ItemGripSockets"/> de kapatılır — soket halkası çizilseydi "buradan tut" diye
        /// yalan söylerdi (kapı zaten kapalı olduğu için hiçbir kavrama kabul edilmezdi).
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

                if (behaviour is Grabbable || behaviour is GrabInteractable || behaviour is ItemGripSockets)
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
        /// ⚠️ <b>El/anchor çözülemezse FAIL-OPEN</b> (izin verilir), gerekçesi
        /// <c>ItemGripSockets.Filter</c>'daki ile birebir aynı: bu bir emniyet kapısı değil bir HİS
        /// kapısıdır — editör oturumunda kontrolcü çözülemez ve fail-close olsaydı silah editörde
        /// hiç seçilemez, yani sahne testi imkânsız hale gelirdi.
        /// </para>
        /// </summary>
        public bool Filter(GameObject interactorGameObject)
        {
            OVRInput.Controller hand = WeaponGranter.ResolveControllerFromGameObject(interactorGameObject);
            if (hand == OVRInput.Controller.None)
            {
                WarnFailOpen();
                return true;
            }

            Transform anchor = WeaponGranter.ResolveHandAnchor(hand);
            if (anchor == null)
            {
                return true; // rig yok → kapı anlamsız
            }

            return (anchor.position - FrameCenterWorld).sqrMagnitude <= maxGrabDistance * maxGrabDistance;
        }

        // ------------------------------------------------------------------ nişan ışını

        /// <summary>Çerçeve merkezinin dünya konumu (ışının hedefi ve mesafe ölçüsünün kaynağı).</summary>
        private Vector3 FrameCenterWorld =>
            _weapon != null ? _weapon.transform.TransformPoint(_centerLocal) : transform.position;

        /// <summary>
        /// Hover eden elin anchor'ından çerçeve merkezine mavi ışın çizer — yalnız el MENZİL
        /// İÇİNDEYSE.
        /// <para>
        /// ⚠️ <b>Mesafe testi burada TEKRAR yapılır</b>, çünkü <see cref="Filter"/> yalnız
        /// <c>Select</c>'i keser: <b>ISDK filtresi hover'ı KESMEZ</b>. Tekrarlanmasaydı 5 m'den de
        /// mavi ışın çıkar ama grip hiçbir şey yapmazdı — yani oyuncuya yalan söyleyen bir vurgu.
        /// </para>
        /// <para>
        /// Ayrıca ışını bizim çizmemiz gerekiyor: ISDK'nın mesafe kavraması kendi başına ışın
        /// çizmez (yalnız bir reticle gösterir).
        /// </para>
        /// </summary>
        private void TickRay(OVRInput.Controller hand, ref LineRenderer ray)
        {
            if (!IsHovering(hand))
            {
                HideRay(ray);
                return;
            }

            Transform anchor = WeaponGranter.ResolveHandAnchor(hand);
            if (anchor == null)
            {
                HideRay(ray);
                return;
            }

            Vector3 center = FrameCenterWorld;
            if ((anchor.position - center).sqrMagnitude > maxGrabDistance * maxGrabDistance)
            {
                HideRay(ray);
                return;
            }

            ray = ray != null ? ray : CreateRay(hand);
            if (ray == null)
            {
                return;
            }

            ray.SetPosition(0, anchor.position);
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
