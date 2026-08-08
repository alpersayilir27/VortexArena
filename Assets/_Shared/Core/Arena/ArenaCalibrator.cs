using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// Two-point calibration that aligns the virtual arena with the physical play
    /// space. Hold A on the right controller and double-tap B while resting the
    /// controller TIP on a floor mark: the first capture lights up anchor_a, the
    /// second lights up anchor_b and moves the camera rig so both virtual markers
    /// land on their physical marks. The calibrated pose is persisted as an
    /// OVRSpatialAnchor and restored automatically on the next session. Repeating
    /// the gesture after a completed calibration starts a fresh one.
    /// <para>
    /// Hizalama <b>6DOF</b>'tur: yaw ve yatay konum A-&gt;B çiftinden, <b>zemin yüksekliği
    /// B noktasında yakalanan uçtan</b> gelir. Zemin tracking origin'den DEĞİL ölçümden
    /// alınır, çünkü başlıklar guardian / alan kurulumu OLMADAN çalışır: orada sistemin
    /// zemin seviyesi bir tahmindir, gözlük havadayken açılırsa yanlış başlar ve oturum
    /// içinde tracking kaybı sonrası kayabilir.
    /// </para>
    /// <para>
    /// ⚠️ <b>Hiçbir rotasyon bu ölçüme girmez.</b> Yakalanan nokta kumanda pivotunun
    /// <b>dünya</b> -Y ekseninde <see cref="floorProbeDropMeters"/> altındadır; kumandanın nasıl
    /// tutulduğu, gözlüğün nereye baktığı ve gözlüğün yüksekliği sonucu değiştirmez. Yaw yalnız
    /// iki YAKALANAN NOKTANIN yatay farkından gelir.
    /// </para>
    /// <para>
    /// <b>Harita değişimi kalibrasyonu SIFIRLAMAZ.</b> Kayıtlı anchor'ın UUID'si
    /// <see cref="AnchorUuidKey"/> altında PlayerPrefs'te durur — oturumu değil, cihazı aşar.
    /// Yeni arena sahnesi yüklenince o sahnenin kendi kalibratörü <see cref="Start"/>'ta aynı
    /// anchor'ı yükleyip rig'i o sahnenin <see cref="anchorA"/>/<see cref="anchorB"/>
    /// işaretlerine hizalar; oyuncu fiziksel olarak nerede duruyorsa orada kalır, kimse
    /// "yeniden doğmaz" (Docs/ArenaNet-Protokol.md §10.4).
    /// </para>
    /// <para>
    /// Bunun ön koşulu: <b>aynı işletmede oynanan tüm arenaların zemin işaretleri aynı yerde
    /// olmalı</b> — anchor fiziksel dünyada sabittir, sanal işaretler sahneden gelir. Farklı
    /// ölçüdeki bir arenaya geçilirse hizalama teknik olarak yine kurulur ama fiziksel alanla
    /// örtüşmez; işletme başına tek arena ölçüsü kuralı bu yüzden vardır.
    /// </para>
    /// <para>
    /// <b>İşaretçilerin YERİ sahneden değil boyut dosyasından gelir</b> (<c>calibration.a</c> /
    /// <c>calibration.b</c>): zemine yapıştırılan bantların yeri de bir ölçüdür ve mekan
    /// başınadır. <see cref="Start"/> işaretçileri <see cref="ArenaBoundary"/> üzerinden o
    /// noktalara oturtur, yani aynı mekanın arenaları ile lobisi ölçüyü elle kopyalamadan
    /// paylaşır. Dosyada nokta yoksa işaretçilere DOKUNULMAZ (sahnedeki yerlerinde kalırlar) ve
    /// konsola uyarı düşer.
    /// </para>
    /// <para>
    /// <b>İşaretçiler ölçü maketinin küpleridir</b> — ayrı bir "gerçek işaretçi" ailesi YOKTUR:
    /// <c>JSON'dan DimensionMesh Üret</c> aracının ürettiği <see cref="DimensionAnchor"/>'lı
    /// küpler hem ölçüyü gösterir hem kalibrasyonu sürer. <see cref="ResolveMarkers"/> bu yüzden
    /// önce Inspector'da elle bağlanmış alanlara, sonra <see cref="DimensionAnchor.Kind"/>'a, en
    /// sonda <b>ada</b> bakar (<see cref="AnchorAName"/> / <see cref="AnchorBName"/>) — ad yolu
    /// maketi olmayan eski sahneler içindir, orada işaretçiler elle konmuştur.
    /// </para>
    /// <para>
    /// Tek konum sözleşmesi: <b>işaretçinin transform konumu ZEMİN NOKTASIDIR</b> (küpün merkezi
    /// noktanın üstünde, yarısı zeminin altında durur). Maket geri okuması da transformu ham
    /// okuduğu için sözleşme her iki yönde aynıdır; mesh tabanını zemine oturtan ikinci bir
    /// sözleşme olsaydı iki aile asla üst üste gelmezdi.
    /// </para>
    /// <para>
    /// <b>Sıra her zaman A → B'dir</b> ve bu geometrik olarak doğrulanamaz: iki nokta hangisinin
    /// önce alındığını söylemez, mesafe kontrolü de simetriktir. Garanti prosedüreldir —
    /// <see cref="CapturePoint"/> ilk yakalamayı A, ikinciyi B sayar ve her yakalamada o noktanın
    /// işaretçisini yakar; operatör hangi işaretin yandığını görerek doğrular. Karıştırılırsa
    /// arena 180° ters döner.
    /// </para>
    /// <para>
    /// <b>İşaretçiler kurulum aracıdır, sahne dekoru değildir:</b> yalnız elle kalibrasyon
    /// sürerken görünürler (A yakalanınca A, B yakalanınca B) ve hizalamadan
    /// <see cref="markerVisibleSeconds"/> saniye sonra gizlenirler — o kısa pencere oyuncunun
    /// sanal işaretlerin fiziksel zemin işaretlerine oturduğunu gözle doğrulaması içindir.
    /// Kayıtlı anchor'dan geri yükleme yolunda HİÇ gösterilmezler: orada oyuncu bir şey yapmaz,
    /// gösterilecek bir onay yoktur; oyunun ortasında (harita değişiminde) ekrana obje düşerdi.
    /// </para>
    /// <para>
    /// <c>PlayerPoseTracker</c> hizalamayı BEKLEMEZ: kalibrasyondan önce de poz gönderir, ama o
    /// pozlar arena ile örtüşmez (rig henüz hizalanmadığı için ofsetlidir) — oyuncunun bağlı ve
    /// hareket hâlinde olduğu ağdan görülebilsin diye bilinçli bir tercihtir. Yükleme geçici
    /// olarak başarısız olabildiği için <see cref="RestoreAttempts"/> kez denenir; hepsi düşerse
    /// oyuncu elle (A basılıyken B'ye çift basarak) kalibre etmelidir ve konsola bunu söyleyen
    /// bir uyarı düşer.
    /// </para>
    /// <para>
    /// Hizalamasız kalan başlık arenaya <b>tahminen</b> yerleştirilir
    /// (<see cref="PreAlignWhenTracked"/>) — kalibrasyon sayılmaz, yalnız oyuncunun elle kalibre
    /// edebilmesi için arenayı görmesini sağlar.
    /// </para>
    /// </summary>
    public class ArenaCalibrator : MonoBehaviour
    {
        /// <summary>
        /// İlk kalibrasyon işaretçisinin obje adı — <b>projedeki tek kaynağı budur</b>: sahnedeki
        /// işaretçi, ölçü maketindeki küp ve editör araçlarının aradığı ad hep buradan gelir.
        /// C# alan adı (<c>anchorA</c>) aynı adın camelCase yazımıdır; alan adı serialize anahtarı
        /// olduğu için değiştirilmez.
        /// </summary>
        public const string AnchorAName = "anchor_a";

        /// <summary>İkinci kalibrasyon işaretçisinin obje adı (bkz. <see cref="AnchorAName"/>).</summary>
        public const string AnchorBName = "anchor_b";

        [Header("Virtual markers")]
        [Tooltip("İlk fiziksel zemin işaretinin sanal karşılığı; ilk yakalamada açılır. " +
                 "Boş bırakılırsa ölçü maketindeki DimensionAnchor(A) küpünden, o da yoksa " +
                 "'anchor_a' adlı objeden çözülür. Konumu boyut dosyasındaki calibration.a'dan " +
                 "gelir — elle taşımanın kalıcı bir etkisi yoktur.")]
        [SerializeField] private GameObject anchorA;
        [Tooltip("İkinci fiziksel zemin işaretinin sanal karşılığı; ikinci yakalamada açılır. " +
                 "Boş bırakılırsa ölçü maketindeki DimensionAnchor(B) küpünden, o da yoksa " +
                 "'anchor_b' adlı objeden çözülür. Konumu boyut dosyasındaki calibration.b'den gelir.")]
        [SerializeField] private GameObject anchorB;
        [Tooltip("Hizalama tamamlandıktan sonra işaretçilerin görünür kaldığı süre (saniye). Süre dolunca gizlenirler; 0 = anında gizle.")]
        [SerializeField] private float markerVisibleSeconds = 1f;

        [Header("Rig")]
        [Tooltip("Root moved by the alignment. Falls back to the OVRCameraRig transform.")]
        [SerializeField] private Transform rigRoot;
        [Tooltip("Kalibresiz ön-hizalamada HMD'nin zeminden başlatıldığı yükseklik (m). " +
                 "Kalibrasyon DEĞİLDİR: kayıtlı hizalaması olmayan bir başlıkta oyuncuyu " +
                 "arenanın ortasına, ayakta duran bir kafa yüksekliğine yerleştiren tahmindir.")]
        [SerializeField] private float uncalibratedHeadHeight = 1.8f;

        [Header("Capture")]
        [Tooltip("A basılı tutulurken B'ye iki kez basmak için tanınan süre (saniye). İki basış arası bundan uzunsa ikinci basış yeni bir sekansın ilki sayılır.")]
        [SerializeField] private float doubleTapSeconds = 1f;
        [Tooltip("Yakalanan noktanın kumanda pivotunun kaç metre ALTINDA olduğu (dünya -Y). " +
                 "İzlenen pivot kumandanın gövdesinin içindedir; uç zemine değdiğinde pivot bu " +
                 "kadar yukarıda kalır. ⚠️ Kumandanın yerel ekseninde DEĞİL dünya ekseninde " +
                 "uygulanır — gerekçe koddaki açıklamada.")]
        [SerializeField] private float floorProbeDropMeters = 0.08f;
        [Tooltip("The captured A-B distance must match the anchor_a/anchor_b distance within this fraction.")]
        [Range(0.05f, 0.5f)]
        [SerializeField] private float distanceTolerance = 0.2f;
        [Tooltip("Fallback minimum horizontal distance, used only when the marker distance cannot be read (meters).")]
        [SerializeField] private float fallbackMinDistance = 1f;

        private const string AnchorUuidKey = "VortexArena.CalibrationAnchorUuid";
        private const OVRInput.Controller Hand = OVRInput.Controller.RTouch;

        // Zemin ölçümü tutarlılığı: iki uç de aynı düzlemde olmalı. Aradaki fark eğim
        // DEĞİL ölçüm hatası sayılır (kumanda iki noktada farklı tutulmuş) — iki nokta
        // bir düzlem tanımlamaz ve VR'da dünyayı eğmek konfor açısından kabul edilemez,
        // bu yüzden eğim telafisi bilinçli olarak YAPILMAZ.
        private const float FloorMismatchWarn = 0.03f;
        private const float FloorMismatchReject = 0.10f;
        private const float LongPulseSeconds = 0.6f;

        /// <summary>A noktası alındı: tek KISA titreşim.</summary>
        private const float PointAPulseSeconds = 0.3f;

        /// <summary>B noktası alındı ve rig hizalandı: tek UZUN titreşim — kalibrasyonun
        /// bittiği A onayından duyulur biçimde ayrılsın diye.</summary>
        private const float PointBPulseSeconds = 1f;

        /// <summary>Kayıtlı anchor yüklenemezse kaç kez daha denenir. Sahne yüklendiği anda
        /// anchor servisi her zaman hazır olmuyor; tek denemede pes etmek harita değişiminde
        /// kalibrasyonu boşuna kaybettirirdi.</summary>
        private const int RestoreAttempts = 3;

        /// <summary>Yeniden deneme aralığı (ms).</summary>
        private const int RestoreRetryDelayMs = 1000;

        /// <summary>Ön-hizalamanın izlemeyi beklediği en uzun süre (saniye). Dolduğunda yine de
        /// uygulanır: editör sandbox'ında HMD hiç yoktur, kafa yerelde sıfırda kalır ve beklemekten
        /// vazgeçmemek Play testinde kamerayı arenanın dışında bırakırdı.</summary>
        private const float PreAlignTrackingTimeout = 2f;

        /// <summary>Kafanın "izleniyor" sayılması için gereken en küçük yerel yer değiştirme
        /// (m², rig köküne göre). HMD verisi gelmeden CenterEyeAnchor sıfırda durur.</summary>
        private const float PreAlignHeadEpsilonSqr = 1e-4f;

        /// <summary>True once the arena has been aligned, manually or from a saved anchor.</summary>
        public bool IsCalibrated => capturedCount >= 2;

        /// <summary>Kalibrasyon kaynağı etiketi (protokolde <c>set_calibration.source</c>, §5.1).
        /// Bilinçli olarak string: sunucu doğrulamaz, yeni kaynak eklemek hiçbir yerde sayısal
        /// indeks kaydırmaz.</summary>
        public const string SourceManual = "manual";
        public const string SourceAnchor = "anchor";

        /// <summary>
        /// Kalibrasyon tamamlanınca (elle iki nokta ya da kayıtlı anchor'dan yüklenince)
        /// ana thread'de tetiklenir; argüman kalibrasyonun KAYNAĞIdır (<see cref="SourceManual"/>
        /// / <see cref="SourceAnchor"/>). <c>CalibrationState</c> dinler ve sunucuya
        /// <c>set_calibration</c> yollar (§10.6).
        /// <para>
        /// Poz gönderimi buna BAĞLI DEĞİLDİR — <c>PlayerPoseTracker</c> baştan kaydolur.
        /// </para>
        /// </summary>
        public static event Action<string> Calibrated;

        /// <summary>
        /// Rig'in kaçıncı hizalamasında olduğumuz — rig her hizalandığında (elle yakalama, kayıtlı
        /// anchor'dan yükleme, tracking sonrası yeniden hizalama) artar.
        /// <para>Hizalama <b>herkesi meşru olarak taşır</b>: o karede kök, el ve gövde topluca
        /// yer değiştirir. Süreklilik varsayan emniyetler (ör. <c>ArenaNetCharacterBehaviour</c>'ın
        /// kök sıçrama tutması) bu sayacı okuyup sakladıkları referansı düşürür — yoksa gerçek bir
        /// hizalamayı arıza sanıp bastırırlardı.</para>
        /// </summary>
        public static int CalibrationGeneration { get; private set; }

        private OVRCameraRig cameraRig;
        private OVRSpatialAnchor worldAnchor;
        private Vector3 capturedA;
        private int capturedCount;
        private int pendingBTaps;
        private float firstBTapTime;
        private bool waitingForRelease;
        private bool manualCalibrationStarted;

        /// <summary>Uçuşta olan kayıtlı-anchor geri yüklemesi iptal edildi mi
        /// (<see cref="Invalidate"/>). <see cref="manualCalibrationStarted"/> ile aynı işi operatör
        /// tarafı için yapar: geri yükleme birkaç saniyelik bir yeniden deneme penceresi sürüyor ve
        /// o pencerede gelen sıfırlama yok sayılırsa deneme başarıya ulaşıp arenayı yeniden
        /// hizalar — operatör sıfırladı sanırken oyuncu kalibre kalırdı (§10.6).</summary>
        private bool restoreAborted;
        private bool trackingEventsHooked;
        private bool realignQueued;
        private Coroutine markerHideRoutine;

        /// <summary>Ön-hizalama bir kez kuyruğa alındı mı. İki tetikleyicisi var (kayıtlı UUID yok /
        /// geri yükleme tümden düştü) ve ikisi aynı oturumda peş peşe gelebilir — ikinci bir koşu
        /// rig'i bir daha kaydırırdı.</summary>
        private bool preAlignStarted;

        private Transform RigRoot
        {
            get
            {
                if (rigRoot != null)
                    return rigRoot;
                if (cameraRig == null)
                    cameraRig = FindFirstObjectByType<OVRCameraRig>();
                return cameraRig != null ? cameraRig.transform : null;
            }
        }

        private Transform RightController
        {
            get
            {
                if (cameraRig == null)
                    cameraRig = FindFirstObjectByType<OVRCameraRig>();
                return cameraRig != null ? cameraRig.rightControllerAnchor : null;
            }
        }

        private Transform HeadAnchor
        {
            get
            {
                if (cameraRig == null)
                    cameraRig = FindFirstObjectByType<OVRCameraRig>();
                return cameraRig != null ? cameraRig.centerEyeAnchor : null;
            }
        }

        /// <summary>
        /// Arenanın zemin yüksekliği (dünya uzayı), A işaretçisinden okunur. Öteleme hesabı YOK:
        /// tek sözleşme gereği işaretçinin transform konumu zaten zemin noktasıdır.
        /// </summary>
        private float VirtualFloorY =>
            anchorA != null ? anchorA.transform.position.y : 0f;

        private void Start()
        {
            ResolveMarkers();
            PlaceMarkersFromPlan();
            SetMarkersVisible(false);
            TryHookTrackingEvents();

            // Kayıtlı hizalama YOKSA beklemeye gerek yok: geri yükleme hiç denenmeyecek, oyuncu
            // sahneye ham tracking origin'iyle düşer. Ön-hizalama hemen kuyruğa alınır.
            if (string.IsNullOrEmpty(PlayerPrefs.GetString(AnchorUuidKey, string.Empty)))
                StartPreAlign();

            _ = RestoreSavedCalibrationAsync();
        }

        /// <summary>
        /// Boş bırakılan işaretçi alanlarını sahneden çözer. Her yeni arenada iki referansı elle
        /// sürüklemek, unutulduğunda sessizce hizalanmayan bir arena üretiyordu.
        /// <para>
        /// Sıra: (1) Inspector'da elle bağlanmış alan, (2) ölçü maketinin
        /// <see cref="DimensionAnchor"/> taşıyan küpleri, (3) ad
        /// (<see cref="AnchorAName"/> / <see cref="AnchorBName"/>).
        /// </para>
        /// <para>
        /// ⚠️ Ad yolu KALDIRILAMAZ: maketi olmayan sahnelerde işaretçiler elle konmuştur ve
        /// üzerlerinde <see cref="DimensionAnchor"/> yoktur — 2. adımın tek başına kalması o
        /// arenaları sessizce hizalanamaz hâle getirirdi.
        /// </para>
        /// </summary>
        private void ResolveMarkers()
        {
            if (anchorA == null)
                anchorA = FindMarkerByKind(DimensionAnchor.AnchorKind.A) ?? FindMarkerByName(AnchorAName);
            if (anchorB == null)
                anchorB = FindMarkerByKind(DimensionAnchor.AnchorKind.B) ?? FindMarkerByName(AnchorBName);

            if (anchorA == null || anchorB == null)
            {
                Debug.LogWarning(
                    $"ArenaCalibrator: kalibrasyon işaretçileri bulunamadı " +
                    $"('{AnchorAName}' / '{AnchorBName}'). Hizalama YAPILAMAZ — sahnede ölçü " +
                    "maketi yoksa 'Tools > VortexArena > Arena > JSON'dan DimensionMesh Üret' " +
                    "çalıştır.", this);
                return;
            }

            if (anchorA == anchorB)
            {
                Debug.LogError("ArenaCalibrator: anchorA ile anchorB aynı obje — hizalama yön " +
                               "üretemez. İkisi ayrı işaretçi olmalı.", this);
            }
        }

        /// <summary>
        /// İşaretçiyi <see cref="DimensionAnchor.Kind"/>'ından bulur — ölçü maketinin küpleri
        /// bunlardır. Aynı türden birden çok varsa ilki alınır ve uyarı düşer: iki A küpü, iki
        /// farklı ölçü maketi (ya da kopyalanmış bir maket) demektir ve hangisinin sahadaki bandı
        /// temsil ettiği kestirilemez.
        /// </summary>
        private GameObject FindMarkerByKind(DimensionAnchor.AnchorKind kind)
        {
            Scene scene = gameObject.scene;
            if (!scene.IsValid())
                return null;

            GameObject found = null;
            int matches = 0;

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                DimensionAnchor[] anchors = roots[i].GetComponentsInChildren<DimensionAnchor>(true);
                for (int k = 0; k < anchors.Length; k++)
                {
                    if (anchors[k].Kind != kind)
                        continue;
                    matches++;
                    if (found == null)
                        found = anchors[k].gameObject;
                }
            }

            if (matches > 1)
            {
                Debug.LogWarning(
                    $"ArenaCalibrator: sahnede {kind} kalibrasyon işaretçisinden {matches} tane " +
                    $"var — ilki ('{found.name}') kullanıldı. Sahnede tek bir ölçü maketi olmalı.",
                    this);
            }

            return found;
        }

        /// <summary>
        /// İşaretçiyi adından bulur. Ölçü maketi olmayan eski sahneler için geri uyumluluk yolu:
        /// orada işaretçiler elle konmuştur ve <see cref="DimensionAnchor"/> taşımazlar.
        /// </summary>
        private GameObject FindMarkerByName(string markerName)
        {
            Scene scene = gameObject.scene;
            if (!scene.IsValid())
                return null;

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Transform[] all = roots[i].GetComponentsInChildren<Transform>(true);
                for (int k = 0; k < all.Length; k++)
                {
                    if (string.Equals(all[k].name, markerName, StringComparison.Ordinal))
                        return all[k].gameObject;
                }
            }

            return null;
        }

        /// <summary>
        /// İşaretçileri boyut dosyasındaki kalibrasyon noktalarına oturtur. Dosyada nokta yoksa
        /// işaretçilere dokunulmaz.
        /// <para>
        /// ⚠️ Sessiz geçilmez: nokta yazılmamış bir mekanda işaretçiler prefabtaki yerlerinde
        /// kalır ve sahadaki bantla örtüşmezler — hizalama teknik olarak yine kurulur ama arena
        /// yanlış yere oturur. Uyarı bu yüzden bir kurulum hatasıdır, tercih değil.
        /// </para>
        /// </summary>
        private void PlaceMarkersFromPlan()
        {
            if (anchorA == null || anchorB == null)
                return;

            var boundary = FindFirstObjectByType<ArenaBoundary>(FindObjectsInactive.Include);
            if (boundary == null || !boundary.TryGetCalibrationMarks(out Vector3 markA, out Vector3 markB))
            {
                Debug.LogWarning(
                    "ArenaCalibrator: boyut dosyasında kalibrasyon noktası yok — işaretçiler " +
                    "sahnedeki yerlerinde bırakıldı. Sahadaki zemin bandı buraya uymuyorsa arena " +
                    "yanlış yere hizalanır; noktaları '<Mekan>_dimensions.json' dosyasındaki " +
                    "'calibration' alanına yaz.", this);
                return;
            }

            PlaceMarkerAtFloor(anchorA, markA);
            PlaceMarkerAtFloor(anchorB, markB);
        }

        private void OnDestroy()
        {
            UnhookTrackingEvents();
        }

        private void Update()
        {
            // OVRManager bizden sonra uyanmış olabilir; bağlanana dek denemeye devam.
            if (!trackingEventsHooked)
                TryHookTrackingEvents();

            // Sekans: A BASILI TUTULURKEN B'ye doubleTapSeconds içinde iki kez basmak.
            // Basılı tutma süresi yoktur — kumandanın ucu zemin işaretinde dururken üç saniye
            // beklemek elin titremesine ve ölçümün kaymasına yol açıyordu; çift basış anlıktır.
            bool aHeld = OVRInput.Get(OVRInput.Button.One, Hand);

            if (waitingForRelease)
            {
                if (!aHeld)
                    waitingForRelease = false;
                return;
            }

            // A bırakıldı → yarım kalan sekans düşer: çift basış yalnız A basılıyken sayılır.
            if (!aHeld)
            {
                pendingBTaps = 0;
                return;
            }

            // Pencere doldu → bir sonraki basış yeni sekansın İLKİ sayılır.
            if (pendingBTaps > 0 && Time.time - firstBTapTime > doubleTapSeconds)
                pendingBTaps = 0;

            if (!OVRInput.GetDown(OVRInput.Button.Two, Hand))
                return;

            if (pendingBTaps == 0)
            {
                pendingBTaps = 1;
                firstBTapTime = Time.time;
                return;
            }

            pendingBTaps = 0;

            // §10.6: KALİBRE durumdayken elle kalibrasyon KAPALIDIR — oyuncu kendi hizalamasını
            // kazara bozamasın; kapıyı yalnız operatör açar (admin ekranından sıfırlama).
            // Sessizce yutulmaz: tek uzun titreşim + log, yoksa oyuncu kumandayı bozuk sanır.
            // Kapı sekansın SONUNDA denetlenir: B tuşu oyunda başka işlere de basılıyor,
            // tek basışta uyarmak yanlış alarm üretirdi.
            if (!CalibrationState.ManualAllowed)
            {
                waitingForRelease = true;
                OVRInput.SetControllerVibration(0f, 0f, Hand);
                StartCoroutine(Pulse(1, LongPulseSeconds));
                Debug.Log("ArenaCalibrator: kalibrasyon zaten alınmış — yeniden almak için operatörün admin ekranından sıfırlaması gerekir (§10.6).");
                return;
            }

            CapturePoint();
        }

        /// <summary>
        /// İşaretçiyi verilen ZEMİN noktasına oturtur. Tek sözleşme: işaretçinin transform konumu
        /// zemin noktasının kendisidir (küpün merkezi noktada, yarısı zeminin altında) — ölçü
        /// maketi de küplerini böyle üretir ve geri okuma transformu ham okur.
        /// <para>
        /// ⚠️ Mesh'in tabanını zemine oturtan bir öteleme YOKTUR ve geri eklenmez: iki farklı Y
        /// sözleşmesi, aynı noktayı gösteren iki işaretçinin asla üst üste gelmemesi demektir.
        /// </para>
        /// <para>
        /// Konum dünya uzayındadır, yani işaretçinin hiyerarşide nereye bağlandığı önemsizdir.
        /// </para>
        /// </summary>
        public static void PlaceMarkerAtFloor(GameObject marker, Vector3 floorPoint)
        {
            if (marker == null)
                return;

            marker.transform.position = floorPoint;
        }

        /// <summary>Horizontal distance between the two virtual markers, 0 if unavailable.</summary>
        private float ExpectedMarkerDistance()
        {
            if (anchorA == null || anchorB == null)
                return 0f;
            Vector3 span = anchorB.transform.position - anchorA.transform.position;
            span.y = 0f;
            return span.magnitude;
        }

        private void CapturePoint()
        {
            Transform pointer = RightController;
            if (pointer == null)
            {
                Debug.LogWarning("ArenaCalibrator: right controller not found.", this);
                return;
            }

            // Yakalanan nokta kumandanın PIVOTU değil UCUDUR: pivot gövdenin içinde durur, uç
            // yere değdiğinde pivot birkaç cm yukarıda kalır ve o fark doğrudan zemin hatasına
            // dönüşürdü.
            //
            // ⚠️ Ofset DÜNYA -Y ekseninde uygulanır, kumandanın YEREL ekseninde DEĞİL
            // (TransformPoint kullanılmaz): yerel uygulamada ölçüm kumandanın tutuş açısına
            // bağlanır — 45° eğik tutulan bir kumandada nokta yatayda kayar ve dikeyde eksik
            // düşer. Oyuncu kumandayı zemin işaretine değdirirken onu dik tutmak zorunda
            // değildir; ne kumandanın ne gözlüğün rotasyonu bu ölçüme girmez.
            Vector3 point = pointer.position + Vector3.down * floorProbeDropMeters;

            // A completed calibration restarts from scratch on the next capture.
            if (capturedCount >= 2)
                ResetCalibration();

            // Sıra sabittir: ilk yakalama A, ikincisi B. İki noktadan hangisinin önce alındığı
            // GEOMETRİK olarak çıkarılamaz (mesafe kontrolü de simetriktir), bu yüzden garanti
            // buradaki sayaç ile operatörün gördüğü işaretçidir — log da hangisi olduğunu yazar.
            if (capturedCount == 0)
            {
                manualCalibrationStarted = true;
                capturedA = point;
                capturedCount = 1;
                if (anchorA != null) anchorA.SetActive(true);
                StartCoroutine(Pulse(1, PointAPulseSeconds));
                Debug.Log($"ArenaCalibrator: 1/2 — A yakalandı, fiziksel {point} → sanal " +
                          $"'{AnchorAName}' {(anchorA != null ? anchorA.transform.position.ToString() : "(yok)")}. " +
                          "Sıradaki nokta B.");
                return;
            }

            Vector3 flat = point - capturedA;
            flat.y = 0f;
            if (!IsDistancePlausible(flat.magnitude, out string distanceProblem))
            {
                StartCoroutine(Pulse(3));
                Debug.LogWarning($"ArenaCalibrator: {distanceProblem} Capture B again.", this);
                return;
            }

            float floorMismatch = Mathf.Abs(point.y - capturedA.y);
            if (floorMismatch > FloorMismatchReject)
            {
                StartCoroutine(Pulse(1, LongPulseSeconds));
                Debug.LogWarning(
                    $"ArenaCalibrator: floor heights disagree by {floorMismatch:F3} m. " +
                    "Hold the controller upright with its tip on the mark and capture B again.", this);
                return;
            }
            if (floorMismatch > FloorMismatchWarn)
            {
                Debug.LogWarning(
                    $"ArenaCalibrator: floor heights disagree by {floorMismatch:F3} m; using point B. " +
                    "Check the controller grip or the floor.", this);
            }

            capturedCount = 2;
            if (anchorB != null) anchorB.SetActive(true);
            StartCoroutine(Pulse(1, PointBPulseSeconds));
            Debug.Log($"ArenaCalibrator: 2/2 — B yakalandı, fiziksel {point} → sanal " +
                      $"'{AnchorBName}' {(anchorB != null ? anchorB.transform.position.ToString() : "(yok)")}.");
            AlignRig(capturedA, point);
            HideMarkersAfterConfirmation();
            RaiseCalibrated(SourceManual);
            _ = CreateAndSaveAnchorAsync();
        }

        /// <summary>
        /// Yakalanan mesafe sahnedeki marker mesafesine uymalı: yalnız bir alt sınır
        /// koymak, zemin bandı yanlış mesafede çekildiğinde sessizce yanlış bir
        /// kalibrasyona izin verirdi.
        /// </summary>
        private bool IsDistancePlausible(float measured, out string problem)
        {
            float expected = ExpectedMarkerDistance();
            if (expected > 0f)
            {
                float slack = expected * distanceTolerance;
                if (Mathf.Abs(measured - expected) > slack)
                {
                    problem = $"captured distance {measured:F2} m does not match the marker distance " +
                              $"{expected:F2} m (tolerance {slack:F2} m).";
                    return false;
                }
            }
            else if (measured < fallbackMinDistance)
            {
                problem = $"captured points are only {measured:F2} m apart.";
                return false;
            }

            problem = null;
            return true;
        }

        /// <summary>
        /// Moves the rig so the physical points land on the virtual markers: yaw from
        /// the A-&gt;B directions, horizontal position from point A, and floor height
        /// from the tip captured at point B.
        /// </summary>
        private void AlignRig(Vector3 physicalA, Vector3 physicalB)
        {
            Transform rig = RigRoot;
            if (rig == null || anchorA == null || anchorB == null)
            {
                Debug.LogWarning("ArenaCalibrator: missing rig or marker references.", this);
                return;
            }

            Vector3 virtualA = anchorA.transform.position;
            float virtualFloorY = VirtualFloorY;
            Vector3 physicalDir = physicalB - physicalA;
            Vector3 virtualDir = anchorB.transform.position - virtualA;
            physicalDir.y = 0f;
            virtualDir.y = 0f;
            if (physicalDir.sqrMagnitude < 1e-6f || virtualDir.sqrMagnitude < 1e-6f)
                return;

            float yaw = Vector3.SignedAngle(physicalDir, virtualDir, Vector3.up);
            rig.RotateAround(physicalA, Vector3.up, yaw);

            Vector3 delta = virtualA - physicalA;
            delta.y = 0f;
            rig.position += delta;

            // Yaw Vector3.up ekseninde, öteleme yatay → physicalB.y ikisinden de
            // etkilenmez, yakalama anındaki değeri burada hâlâ geçerlidir.
            float rise = virtualFloorY - physicalB.y;
            rig.position += Vector3.up * rise;

            CalibrationGeneration++;
            Debug.Log($"ArenaCalibrator: rig aligned (yaw {yaw:F1} deg, floor {rise:F3} m).");
        }

        /// <summary>
        /// Aligns the rig from a persisted anchor pose. The anchor lives at the
        /// floor point under marker A facing marker B, so a single pose carries the
        /// full calibration — floor height included, since the offset is applied as
        /// a full vector.
        /// </summary>
        internal void AlignRigToAnchorPose(Vector3 anchorPos, Quaternion anchorRot)
        {
            Transform rig = RigRoot;
            if (rig == null || anchorA == null || anchorB == null)
                return;

            Vector3 virtualA = anchorA.transform.position;
            Vector3 virtualDir = anchorB.transform.position - virtualA;
            virtualDir.y = 0f;
            Vector3 anchorFwd = anchorRot * Vector3.forward;
            anchorFwd.y = 0f;
            if (virtualDir.sqrMagnitude < 1e-6f || anchorFwd.sqrMagnitude < 1e-6f)
                return;

            float yaw = Vector3.SignedAngle(anchorFwd, virtualDir, Vector3.up);
            rig.RotateAround(anchorPos, Vector3.up, yaw);

            Vector3 target = new Vector3(virtualA.x, VirtualFloorY, virtualA.z);
            rig.position += target - anchorPos;

            CalibrationGeneration++;
            Debug.Log($"ArenaCalibrator: rig aligned from saved anchor (yaw {yaw:F1} deg).");
        }

        /// <summary>
        /// Ön-hizalamayı bir kez kuyruğa alır. İki çağıranı var: kayıtlı anchor UUID'si hiç
        /// olmayan bir başlık (<see cref="Start"/>) ve kayıtlı anchor'ın TÜM denemelerde geri
        /// yüklenememesi (<see cref="RestoreSavedCalibrationAsync"/>).
        /// </summary>
        private void StartPreAlign()
        {
            if (preAlignStarted)
                return;

            preAlignStarted = true;
            StartCoroutine(PreAlignWhenTracked());
        }

        /// <summary>
        /// <b>Kalibresiz ön-hizalama</b>: kayıtlı hizalaması olmayan bir başlıkta rig'i, kafası
        /// arenanın A-B ortasında ve A→B'ye bakar olacak biçimde TAHMİNEN yerleştirir.
        /// <para>
        /// <b>Neden var:</b> hizalanmamış bir rig sahneye ham tracking origin'iyle düşüyor ve
        /// oyuncu çoğu zaman arenanın dışında, yani <see cref="ArenaBoundary"/>'nin tam karartması
        /// içinde doğuyor — elle kalibre etmesi gereken oyuncu tam da o anda hiçbir şey göremiyor.
        /// Bu, <c>PlayerPoseTracker</c>'ın "kalibrasyondan önce de poz gönder" tercihiyle aynı
        /// çizgidedir: doğru olmayan ama <b>işe yarar</b> bir başlangıç, hiç olmamasından iyidir.
        /// </para>
        /// <para>
        /// ⚠️ <b>Bu bir KALİBRASYON DEĞİLDİR</b> ve öyle sayılmaz: <see cref="capturedCount"/>
        /// artmaz, <see cref="Calibrated"/> yayınlanmaz, anchor kaydedilmez ve elle kalibrasyon
        /// kapısı açık kalır. Ölçülmüş hiçbir şey yok — yalnız sahnedeki iki işaretçinin
        /// dosyadan gelen yeri var. Gerçek hizalama geldiğinde (elle ya da anchor'dan) rig
        /// oradan mutlak olarak yeniden konumlanır, yani bu tahmin birikmez.
        /// </para>
        /// <para>
        /// Bakış yönü A→B'dir: kayıtlı anchor'ın "A noktasında durur, B'ye bakar" sözleşmesiyle
        /// aynı — iki yolun aynı yöne bakması, kalibrasyon tamamlandığında oyuncunun kendini
        /// döndürülmüş hissetmemesi demektir.
        /// </para>
        /// <para>
        /// ⚠️ Taşınan <b>kafa</b>dır, rig kökü değil: kök zemin referansıdır (hizalama onun
        /// üstünden yükseklik yazar), oyuncu ise ayakta duran bir kafadır. Kökü ortaya koymak
        /// oyuncuyu HMD ofseti kadar kaymış bırakırdı.
        /// </para>
        /// <para>
        /// İzleme beklenir ama <see cref="PreAlignTrackingTimeout"/> saniyeden fazla değil:
        /// HMD'siz sandbox'ta kafa yerelde sıfırda kalır ve orada da kamerayı arenaya düşürmek
        /// istenen davranıştır.
        /// </para>
        /// </summary>
        private IEnumerator PreAlignWhenTracked()
        {
            float deadline = Time.unscaledTime + PreAlignTrackingTimeout;

            while (Time.unscaledTime < deadline)
            {
                if (!PreAlignStillWanted())
                    yield break;

                Transform tracked = HeadAnchor;
                if (tracked != null && tracked.localPosition.sqrMagnitude > PreAlignHeadEpsilonSqr)
                    break;

                yield return null;
            }

            if (!PreAlignStillWanted())
                yield break;

            Transform rig = RigRoot;
            Transform head = HeadAnchor;
            if (rig == null || head == null || !rig.gameObject.activeInHierarchy)
                yield break;

            // İşaretçiler PlaceMarkersFromPlan ile zaten boyut dosyasındaki noktalara oturmuş
            // durumda; burada yalnız okunurlar. Eksik/aynı işaretçi durumunda ResolveMarkers
            // uyarıyı zaten bastı — ikincisi gürültü olurdu.
            if (anchorA == null || anchorB == null || anchorA == anchorB)
                yield break;

            Vector3 pointA = anchorA.transform.position;
            Vector3 pointB = anchorB.transform.position;

            Vector3 forward = pointB - pointA;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f)
                yield break;
            forward.Normalize();

            // Önce yaw (kafanın kendi ekseninde), sonra öteleme: dönüş kafayı yerinde tuttuğu için
            // iki adım birbirini bozmaz.
            Vector3 currentForward = head.forward;
            currentForward.y = 0f;
            if (currentForward.sqrMagnitude < 1e-6f)
            {
                currentForward = rig.forward;
                currentForward.y = 0f;
                if (currentForward.sqrMagnitude < 1e-6f)
                    yield break;
            }

            float yaw = Vector3.SignedAngle(currentForward, forward, Vector3.up);
            rig.RotateAround(head.position, Vector3.up, yaw);

            Vector3 mid = (pointA + pointB) * 0.5f;
            Vector3 targetHead = new Vector3(mid.x, VirtualFloorY + uncalibratedHeadHeight, mid.z);
            rig.position += targetHead - head.position;

            // Rig meşru olarak taşındı: sıçrama bastırıcıları bunu arıza sanmasın.
            CalibrationGeneration++;
            Debug.Log("ArenaCalibrator: kalibrasyon yok — rig arenanın A-B ortasına TAHMİNEN " +
                      "yerleştirildi. Bu bir kalibrasyon DEĞİLDİR; oyuncu elle hizalamalıdır.");
        }

        /// <summary>
        /// Ön-hizalama hâlâ isteniyor mu: gerçek bir hizalama (elle ya da anchor'dan) araya
        /// girdiyse ya da operatör sıfırladıysa tahmin uygulanmaz — ölçülmüş bir hizalamanın
        /// üstüne tahmin yazmak onu bozmak olurdu.
        /// </summary>
        private bool PreAlignStillWanted()
        {
            return !IsCalibrated && !manualCalibrationStarted && !restoreAborted && capturedCount == 0;
        }

        /// <summary>
        /// Recenter ve tracking geri kazanımı kalibrasyonu bayatlatır: origin kayar ama
        /// rig'in hizalama transform'u eski kalır → arena kayar. Stage tracking origin
        /// sistem recenter'ını zaten kapatır; bu ikinci savunma hattıdır.
        /// </summary>
        private void TryHookTrackingEvents()
        {
            if (trackingEventsHooked)
                return;

            OVRDisplay display = OVRManager.display;
            if (display == null)
                return;

            display.RecenteredPose += HandleTrackingDisturbed;
            OVRManager.TrackingAcquired += HandleTrackingDisturbed;
            trackingEventsHooked = true;
        }

        private void UnhookTrackingEvents()
        {
            if (!trackingEventsHooked)
                return;

            OVRDisplay display = OVRManager.display;
            if (display != null)
                display.RecenteredPose -= HandleTrackingDisturbed;
            OVRManager.TrackingAcquired -= HandleTrackingDisturbed;
            trackingEventsHooked = false;
        }

        private void HandleTrackingDisturbed()
        {
            if (worldAnchor == null || capturedCount < 2 || realignQueued)
                return;

            realignQueued = true;
            StartCoroutine(RealignFromAnchor());
        }

        private IEnumerator RealignFromAnchor()
        {
            // Anchor'ın transform'u origin değişiminden bir-iki frame sonra tazelenir.
            yield return null;
            yield return null;
            realignQueued = false;

            if (worldAnchor == null)
                yield break;

            Debug.Log("ArenaCalibrator: tracking origin changed, realigning from the saved anchor.");
            AlignRigToAnchorPose(worldAnchor.transform.position, worldAnchor.transform.rotation);
        }

        /// <summary>
        /// Hizalamayı geçersiz kılar (§10.6): işaretçiler gizlenir, kayıtlı <c>OVRSpatialAnchor</c>
        /// SİLİNİR ve elle kalibrasyon kapısı yeniden açılır. Çağıran <c>CalibrationState</c>'tir — operatör
        /// admin ekranından kalibrasyonu sıfırladığında.
        /// <para>
        /// Anchor'ı bırakmak olmazdı: bir sonraki <c>load_match</c> bozuk hizalamayı sessizce geri
        /// yüklerdi (§10.4 geri yükleme yolu). ⚠️ <b>Rig TAŞINMAZ</b> — free-roam kuralı gereği
        /// oyuncu fiziksel olarak neredeyse orada kalır, yalnız hizalama geçersiz sayılır.
        /// </para>
        /// <para>
        /// ⚠️ <b>Oyuncunun hangi aşamada olduğuna bakılmaz.</b> Tamamlanmış hizalamanın yanında üç
        /// ara durum daha silinir, çünkü sıfırlama oyuncuyu yarım yolda bırakmamalıdır (§10.6):
        /// (1) yakalanmış A noktası ve bekleyen çift basış (<see cref="ResetCalibration"/>),
        /// (2) o an basılı tutulan jestin kuyruğu (<see cref="waitingForRelease"/> — sıfırlama
        /// anında A basılıysa oyuncu onu bırakıp baştan başlar; basılı değilse bayrak bir sonraki
        /// karede kendiliğinden düşer), (3) uçuşta olan kayıtlı anchor geri yüklemesi
        /// (<see cref="restoreAborted"/> — birkaç saniyelik yeniden deneme penceresi sürerken
        /// gelen sıfırlama, yoksa deneme başarıya ulaşıp arenayı yeniden hizalardı).
        /// </para>
        /// </summary>
        public void Invalidate()
        {
            restoreAborted = true;
            waitingForRelease = true;
            ResetCalibration();
        }

        /// <summary>
        /// Hizalama bitti: işaretçiler kısa bir doğrulama penceresi kadar açık kalır, sonra
        /// gizlenir. Maç bu sırada başlayabildiği için pencere bilinçli olarak kısadır —
        /// işaretçi kalibrasyon geri bildirimidir, arena dekoru değil.
        /// </summary>
        private void HideMarkersAfterConfirmation()
        {
            if (markerHideRoutine != null)
                StopCoroutine(markerHideRoutine);

            if (markerVisibleSeconds <= 0f)
            {
                SetMarkersVisible(false);
                return;
            }

            markerHideRoutine = StartCoroutine(HideMarkersDelayed());
        }

        private IEnumerator HideMarkersDelayed()
        {
            yield return new WaitForSeconds(markerVisibleSeconds);
            markerHideRoutine = null;
            SetMarkersVisible(false);
        }

        private void SetMarkersVisible(bool visible)
        {
            if (anchorA != null) anchorA.SetActive(visible);
            if (anchorB != null) anchorB.SetActive(visible);
        }

        /// <summary>
        /// Kalibrasyonu sıfırlar: tamamlanmış hizalama **ve yarım kalmış sekans** birlikte gider.
        /// <para>
        /// ⚠️ <b>Ara durumu temizlemek şart:</b> <see cref="capturedCount"/> tek başına
        /// sıfırlansaydı, A'sı alınmış bir oyuncuda <see cref="capturedA"/> ve bekleyen çift basış
        /// sayacı yaşamaya devam ederdi — operatör sıfırladıktan sonraki tek bir B basışı
        /// <b>sıfırlamadan önceki</b> A noktasıyla kalibrasyonu tamamlayabilirdi. Sıfırlama
        /// oyuncuyu hiçbir ara aşamada bırakmaz (§10.6).
        /// </para>
        /// <para>
        /// ⚠️ <see cref="waitingForRelease"/> buradan sürülmez, <see cref="Invalidate"/>'ten
        /// sürülür: bu metodun ikinci çağıranı <see cref="CapturePoint"/>'tir (tamamlanmış
        /// kalibrasyonun üstüne yeni jest) ve orada A <b>basılı tutulmaya devam etmelidir</b> —
        /// B noktası yalnız A basılıyken yakalanıyor. Kilit yalnız dışarıdan gelen, jesti ortasından
        /// kesen sıfırlamaya aittir.
        /// </para>
        /// </summary>
        private void ResetCalibration()
        {
            capturedCount = 0;
            capturedA = Vector3.zero;
            pendingBTaps = 0;
            firstBTapTime = 0f;
            manualCalibrationStarted = false;
            if (markerHideRoutine != null)
            {
                StopCoroutine(markerHideRoutine);
                markerHideRoutine = null;
            }
            SetMarkersVisible(false);
            if (worldAnchor != null)
            {
                OVRSpatialAnchor stale = worldAnchor;
                worldAnchor = null;
                _ = EraseAnchorAsync(stale);
            }
            PlayerPrefs.DeleteKey(AnchorUuidKey);
            Debug.Log("ArenaCalibrator: calibration reset.");
        }

        /// <summary>
        /// Hizalama tamamlandı → olay yayınlanır. İki tamamlanma yolu da (elle yakalama + kayıtlı
        /// anchor'dan geri yükleme) buradan geçer.
        /// <para>⚠️ Gövde ölçüsü buna BAĞLI DEĞİLDİR (§10.8): ölçümü operatör başlatır. Hizalamadan
        /// otomatik tetiklenen bir ölçüm, oyuncu kumandayı zemine değdirmek için eğilmişken ölçmek
        /// olurdu.</para>
        /// </summary>
        private static void RaiseCalibrated(string source)
        {
            Calibrated?.Invoke(source);
        }

        private async Task CreateAndSaveAnchorAsync()
        {
            try
            {
                Vector3 virtualA = anchorA.transform.position;
                Vector3 forward = anchorB.transform.position - virtualA;
                forward.y = 0f;
                Vector3 floorPoint = new Vector3(virtualA.x, VirtualFloorY, virtualA.z);

                var go = new GameObject("ArenaWorldAnchor");
                go.transform.SetPositionAndRotation(floorPoint, Quaternion.LookRotation(forward.normalized, Vector3.up));
                var anchor = go.AddComponent<OVRSpatialAnchor>();

                if (!await anchor.WhenCreatedAsync())
                {
                    Debug.LogWarning("ArenaCalibrator: spatial anchor creation failed.", this);
                    Destroy(go);
                    return;
                }
                worldAnchor = anchor;

                var save = await anchor.SaveAnchorAsync();
                if (save.Success)
                {
                    PlayerPrefs.SetString(AnchorUuidKey, anchor.Uuid.ToString());
                    PlayerPrefs.Save();
                    Debug.Log($"ArenaCalibrator: anchor saved ({anchor.Uuid}).");
                }
                else
                {
                    Debug.LogWarning($"ArenaCalibrator: anchor save failed ({save.Status}).", this);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
            }
        }

        /// <summary>
        /// Kayıtlı kalibrasyonu geri yükler (harita değişiminde de bu yol koşar). Yükleme
        /// geçici olarak başarısız olabildiği için <see cref="RestoreAttempts"/> kez denenir;
        /// her <c>await</c> sonrası bileşenin hâlâ yaşadığı denetlenir — sahne bu arada
        /// değişmiş olabilir ve ölü bir kalibratör yeni sahnenin rig'ine dokunmamalıdır.
        /// </summary>
        private async Task RestoreSavedCalibrationAsync()
        {
            string saved = PlayerPrefs.GetString(AnchorUuidKey, string.Empty);
            if (string.IsNullOrEmpty(saved) || !Guid.TryParse(saved, out Guid uuid))
                return;

            for (int attempt = 1; attempt <= RestoreAttempts; attempt++)
            {
                // Oyuncu kendi jestiyle öne geçtiyse ya da operatör sıfırladıysa geri yükleme
                // düşer: kayıtlı anchor zaten silindi, onu geri getirmek sıfırlamayı geri almak olurdu.
                if (this == null || manualCalibrationStarted || restoreAborted)
                    return;

                if (await TryRestoreOnceAsync(uuid, attempt))
                    return;

                if (attempt < RestoreAttempts)
                    await Task.Delay(RestoreRetryDelayMs);
            }

            if (this == null || restoreAborted)
                return;

            Debug.LogWarning(
                $"ArenaCalibrator: kayıtlı kalibrasyon {RestoreAttempts} denemede geri " +
                "yüklenemedi — sağ kumandada A basılıyken B'ye çift basarak ELLE kalibre edin " +
                "(o ana dek gönderilen " +
                "pozlar arena ile örtüşmez).",
                this);

            // Elle kalibre edecek oyuncunun önce arenayı GÖRMESİ gerekiyor: hizalanmamış rig onu
            // muhafazanın karartmasının içinde bırakabilir.
            StartPreAlign();
        }

        /// <summary>Tek deneme; başarılıysa true. Kalıcı olmayan hataları yutup false döner.</summary>
        private async Task<bool> TryRestoreOnceAsync(Guid uuid, int attempt)
        {
            try
            {
                var unbound = new List<OVRSpatialAnchor.UnboundAnchor>();
                var load = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(new[] { uuid }, unbound);
                if (this == null) return true; // sahne değişti: bu kalibratörün işi bitti
                if (!load.Success || unbound.Count == 0)
                {
                    Debug.Log($"ArenaCalibrator: kayıtlı anchor yüklenemedi ({load.Status}), deneme {attempt}.");
                    return false;
                }

                OVRSpatialAnchor.UnboundAnchor unboundAnchor = unbound[0];
                if (!unboundAnchor.Localized && !await unboundAnchor.LocalizeAsync())
                {
                    if (this == null) return true;
                    Debug.Log($"ArenaCalibrator: kayıtlı anchor localize edilemedi, deneme {attempt}.");
                    return false;
                }

                if (this == null) return true;

                // The user beat the restore to it; keep their manual calibration.
                // ⚠️ Operatörün sıfırlaması da burada durdurulur: son await'ten sonra gelmiş
                // olabilir ve hizalamayı uygulamak sıfırlamayı sessizce geri alırdı (§10.6).
                if (manualCalibrationStarted || restoreAborted)
                    return true;

                if (!unboundAnchor.TryGetPose(out Pose pose))
                {
                    Debug.Log($"ArenaCalibrator: kayıtlı anchor pozu okunamadı, deneme {attempt}.");
                    return false;
                }

                AlignRigToAnchorPose(pose.position, pose.rotation);

                var go = new GameObject("ArenaWorldAnchor");
                var anchor = go.AddComponent<OVRSpatialAnchor>();
                unboundAnchor.BindTo(anchor);
                worldAnchor = anchor;
                capturedCount = 2;
                // İşaretçiler burada GÖSTERİLMEZ: geri yükleme sessizdir (oyuncu bir şey
                // yapmadı) ve harita değişiminde koştuğu için maçın ortasında ekrana obje
                // düşmesi olurdu. Onay gerekiyorsa admin ekranındaki kalibrasyon tik'i var.
                RaiseCalibrated(SourceAnchor);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
                return false;
            }
        }

        private static async Task EraseAnchorAsync(OVRSpatialAnchor anchor)
        {
            try
            {
                await anchor.EraseAnchorAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"ArenaCalibrator: anchor erase failed ({e.Message}).");
            }
            if (anchor != null)
                Destroy(anchor.gameObject);
        }

        /// <summary>
        /// Haptik sözlüğü: 1 kısa (0.3 sn) = A alındı · 1 uzun (1 sn) = B alındı ve hizalandı ·
        /// 3 kısa = mesafe hatası · 1 orta (0.6 sn) = zemin ölçümü tutarsız / kalibrasyon kilitli.
        /// </summary>
        private IEnumerator Pulse(int count, float seconds = 0.12f)
        {
            for (int i = 0; i < count; i++)
            {
                OVRInput.SetControllerVibration(1f, 1f, Hand);
                yield return new WaitForSeconds(seconds);
                OVRInput.SetControllerVibration(0f, 0f, Hand);
                if (i < count - 1)
                    yield return new WaitForSeconds(0.1f);
            }
        }
    }
}
