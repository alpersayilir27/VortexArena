using UnityEngine;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Yerel oyuncunun kendi gövdesini birinci şahıs çizen sürücü: aşağı bakınca kendi kollarını ve
    /// gövdesini görür.
    /// <para>
    /// ⚠️ <b>Movement SDK / <c>CharacterRetargeter</c> KULLANILMAZ</b>: retarget çıktısı dünya
    /// uzayında üretildiği için kalibre edilen rig'le çakışıyordu — belgeli bir başarısızlık
    /// (<c>Docs/Sistem-Ozeti.md</c> §7). Onun yerine uzak avatarlarda ZATEN çalışan
    /// <see cref="ThreePointBodyIK"/>, yerel rig anchor'larından beslenir.
    /// </para>
    /// <para>
    /// <b>Neden kendini önyükleyen kalıcı tekil</b> (<c>WeaponGranter</c> deseni): sahneye elle
    /// konsaydı her yeni arena bir kurulum adımı doğururdu ve arena şablonu bir bileşen daha
    /// taşımak zorunda kalırdı.
    /// </para>
    /// <para>
    /// ⚠️ <b>Admin'de çizilmez ve bunun için rol kontrolü YAPILMAZ</b>: <c>AppSession</c>
    /// <c>VortexArena.App</c> asmdef'indedir, bağımlılık yönü App → Core, yani Core onu göremez.
    /// Kapı şudur: etkin bir <see cref="OVRCameraRig"/> bulunamazsa hiçbir şey yapılmaz — admin
    /// gözlemcide <c>AdminSpectator</c> rig'i kapattığı için bu kapı kendiliğinden doğru davranır
    /// (aynı gerekçe <c>WeaponGranter.ResolveRig</c>'de de geçerli).
    /// </para>
    /// <para>
    /// ⚠️ Bu avatara <b>collider konmaz</b>: <c>Weapon</c>'daki atış raycast'i maskesizdir
    /// (<c>Physics.Raycast(...)</c>, layer mask yok) — kendi gövden kendi atışını yerdi.
    /// </para>
    /// </summary>
    public class LocalBodyAvatar : MonoBehaviour
    {
        /// <summary>Prefabın <c>Resources</c> altındaki adı (önyükleme bunu yükler).</summary>
        private const string PrefabResourceName = "LocalBodyAvatar";

        /// <summary>Rig bulunamadığında iki arama arasındaki en kısa süre (sn).</summary>
        private const float RigSearchIntervalSeconds = 0.5f;

        public static LocalBodyAvatar Instance { get; private set; }

        [Tooltip("Gövdeyi çözen IK. Boşsa alt ağaçtan aranır.")]
        [SerializeField] private ThreePointBodyIK bodyIK;

        private OVRCameraRig _rig;
        private float _rigSearchTime = float.NegativeInfinity;

        /// <summary>Gövde şu an çiziliyor mu — yalnız gerçekten SÜRÜLEBİLDİĞİ sürece çizilir.</summary>
        private bool _bodyVisible = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var prefab = Resources.Load<GameObject>(PrefabResourceName);
            if (prefab == null)
            {
                // Yerel görsel yok diye oyun durmaz: tek satır uyarı, sessizce devam.
                Debug.LogWarning($"[LocalBodyAvatar] 'Resources/{PrefabResourceName}' prefabı bulunamadı; " +
                                 "yerel gövde avatarı çizilmeyecek.");
                return;
            }

            // ⚠️ Parent VERİLMEZ — avatar SAHNE KÖKÜNDE durur, rig'in ALTINA konmaz:
            // ThreePointBodyIK.PlaceRoot kendi köküne DÜNYA transformu yazıyor, rig'in altındayken
            // rig'in transformu ikinci kez uygulanır ve gövde oyuncudan kayardı (aynı tuzağın
            // Movement SDK sürümü Docs/Sistem-Ozeti.md §7'de kayıtlı).
            GameObject instance = Instantiate(prefab);
            instance.name = prefab.name;
            DontDestroyOnLoad(instance);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (bodyIK == null)
            {
                bodyIK = GetComponentInChildren<ThreePointBodyIK>(true);
            }

            if (bodyIK == null)
            {
                Debug.LogWarning("[LocalBodyAvatar] ThreePointBodyIK bulunamadı; gövde çözülemeyecek.", this);
                enabled = false;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void LateUpdate()
        {
            if (bodyIK == null)
            {
                return;
            }

            OVRCameraRig rig = ResolveRig();
            if (rig == null || rig.centerEyeAnchor == null ||
                rig.leftHandAnchor == null || rig.rightHandAnchor == null)
            {
                // ⚠️ Sürülmeyen gövde GİZLENİR, öylece bırakılmaz: bu tekil admin'de de kuruluyor
                // (önyükleme rolü bilmiyor) ve rig olmayınca Solve hiç koşmaz — görünür bırakılsaydı
                // dünya orijininde, bind pozunda duran bir manken gözlemcinin görüşüne dikilirdi.
                SetBodyVisible(false);
                return;
            }

            SetBodyVisible(true);

            // ⚠️ Pozlar DÜNYA uzayında verilir — ArenaSpace dönüşümü YAPILMAZ. O dönüşüm yalnız ağa
            // giden/gelen pozlar içindir; buradaki kaynak zaten yerel rig'in kendisi.
            bodyIK.Solve(
                new Pose(rig.centerEyeAnchor.position, rig.centerEyeAnchor.rotation),
                new Pose(rig.leftHandAnchor.position, rig.leftHandAnchor.rotation),
                new Pose(rig.rightHandAnchor.position, rig.rightHandAnchor.rotation));
        }

        /// <summary>Gövdeyi tümden açar/kapatır. Kapalıyken hiçbir alt bileşen koşmaz — kemik
        /// gizleyicinin boşuna dönmesi de böylece durur.</summary>
        private void SetBodyVisible(bool visible)
        {
            if (_bodyVisible == visible || bodyIK == null)
            {
                return;
            }

            _bodyVisible = visible;
            bodyIK.gameObject.SetActive(visible);
        }

        /// <summary>Etkin rig'i bulur. Referans önbelleğe alınır ama null'a düşünce (sahne değişimi,
        /// gözlemcinin kapattığı rig) yeniden aranır.
        /// <para>⚠️ Arama <b>kısılır</b>: rig hiç yokken (admin gözlemci — <c>AdminSpectator</c> rig'i
        /// kapatır) bu kapı kalıcı olarak boş döner ve kısılmasaydı her karede bir sahne geneli tip
        /// araması yapılırdı. Rig insan zaman ölçeğinde gelir; saniyede birkaç deneme yeter.</para></summary>
        private OVRCameraRig ResolveRig()
        {
            if (_rig != null && _rig.isActiveAndEnabled)
            {
                return _rig;
            }

            if (Time.unscaledTime - _rigSearchTime < RigSearchIntervalSeconds)
            {
                return null;
            }

            _rigSearchTime = Time.unscaledTime;
            _rig = FindFirstObjectByType<OVRCameraRig>();
            return _rig;
        }
    }
}
