using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// Two-point calibration that aligns the virtual arena with the physical play
    /// space. Hold A+B on the right controller while resting the controller TIP on a
    /// floor mark: the first capture lights up anchor_a, the second lights up
    /// anchor_b and moves the camera rig so both virtual markers land on their
    /// physical marks. The calibrated pose is persisted as an OVRSpatialAnchor and
    /// restored automatically on the next session. Holding A+B again after a
    /// completed calibration starts a fresh one.
    /// <para>
    /// Hizalama <b>6DOF</b>'tur: yaw ve yatay konum A-&gt;B çiftinden, <b>zemin yüksekliği
    /// B noktasında yakalanan uçtan</b> gelir. Zemin tracking origin'den DEĞİL ölçümden
    /// alınır, çünkü başlıklar guardian / alan kurulumu OLMADAN çalışır: orada sistemin
    /// zemin seviyesi bir tahmindir, gözlük havadayken açılırsa yanlış başlar ve oturum
    /// içinde tracking kaybı sonrası kayabilir.
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
    /// Hizalama geri gelene dek <c>PlayerPoseTracker</c> poz GÖNDERMEZ (yanlış uzayda poz
    /// yayınlamaktansa kısa bir boşluk yeğdir). Yükleme geçici olarak başarısız olabildiği için
    /// <see cref="RestoreAttempts"/> kez denenir; hepsi düşerse oyuncu A+B ile elle
    /// kalibre etmelidir ve konsola bunu söyleyen bir uyarı düşer.
    /// </para>
    /// </summary>
    public class ArenaCalibrator : MonoBehaviour
    {
        [Header("Virtual markers")]
        [Tooltip("Marker at the first physical floor mark. Enabled on first capture.")]
        [SerializeField] private GameObject anchorA;
        [Tooltip("Marker at the second physical floor mark. Enabled on second capture.")]
        [SerializeField] private GameObject anchorB;
        [Tooltip("Fallback marker pivot height above the arena floor, used only when the marker has no Renderer to measure.")]
        [SerializeField] private float markerHalfHeight = 0.05f;

        [Header("Rig")]
        [Tooltip("Root moved by the alignment. Falls back to the OVRCameraRig transform.")]
        [SerializeField] private Transform rigRoot;

        [Header("Capture")]
        [Tooltip("How long A+B must be held before a point is captured (seconds).")]
        [SerializeField] private float holdSeconds = 3f;
        [Tooltip("Controller pivot -> tip offset, in controller local space. The tracked pivot sits inside the controller body, so resting the tip on the floor leaves the pivot a few cm above it. Measure once per controller model: with a guardian set up, hold the controller upright on the floor and read rightControllerAnchor.position.y.")]
        [SerializeField] private Vector3 tipLocalOffset = new Vector3(0f, -0.08f, 0f);
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

        /// <summary>Kayıtlı anchor yüklenemezse kaç kez daha denenir. Sahne yüklendiği anda
        /// anchor servisi her zaman hazır olmuyor; tek denemede pes etmek harita değişiminde
        /// kalibrasyonu boşuna kaybettirirdi.</summary>
        private const int RestoreAttempts = 3;

        /// <summary>Yeniden deneme aralığı (ms).</summary>
        private const int RestoreRetryDelayMs = 1000;

        /// <summary>True once the arena has been aligned, manually or from a saved anchor.</summary>
        public bool IsCalibrated => capturedCount >= 2;

        /// <summary>
        /// Kalibrasyon tamamlanınca (elle iki nokta ya da kayıtlı anchor'dan yüklenince)
        /// ana thread'de tetiklenir; Net poz gönderimine bundan sonra başlanır.
        /// </summary>
        public static event Action Calibrated;

        private OVRCameraRig cameraRig;
        private OVRSpatialAnchor worldAnchor;
        private Vector3 capturedA;
        private int capturedCount;
        private float holdTimer;
        private bool waitingForRelease;
        private bool manualCalibrationStarted;
        private float markerFloorDrop;
        private bool trackingEventsHooked;
        private bool realignQueued;

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

        /// <summary>Arena floor height in world space, derived from marker A.</summary>
        private float VirtualFloorY =>
            anchorA != null ? anchorA.transform.position.y - markerFloorDrop : 0f;

        private void Start()
        {
            markerFloorDrop = MeasureMarkerFloorDrop();
            if (anchorA != null) anchorA.SetActive(false);
            if (anchorB != null) anchorB.SetActive(false);
            TryHookTrackingEvents();
            _ = RestoreSavedCalibrationAsync();
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

            bool held = OVRInput.Get(OVRInput.Button.One, Hand) &&
                        OVRInput.Get(OVRInput.Button.Two, Hand);

            if (waitingForRelease)
            {
                if (!held)
                    waitingForRelease = false;
                return;
            }

            if (!held)
            {
                if (holdTimer > 0f)
                    OVRInput.SetControllerVibration(0f, 0f, Hand);
                holdTimer = 0f;
                return;
            }

            holdTimer += Time.deltaTime;
            OVRInput.SetControllerVibration(1f, Mathf.Lerp(0.05f, 0.4f, holdTimer / holdSeconds), Hand);

            if (holdTimer >= holdSeconds)
            {
                holdTimer = 0f;
                waitingForRelease = true;
                OVRInput.SetControllerVibration(0f, 0f, Hand);
                CapturePoint();
            }
        }

        /// <summary>
        /// Marker pivotunun zeminden yüksekliği, Renderer'dan ölçülür — marker görseli
        /// değiştiğinde elle güncellenmesi gereken bir sabit kalmasın diye.
        /// </summary>
        private float MeasureMarkerFloorDrop()
        {
            if (anchorA != null)
            {
                // Marker sahnede KAPALI kaydedilir (ilk yakalamaya dek gizli), bu yüzden
                // Renderer.bounds'a güvenilmez — mesh'in kendi bounds'u asset verisidir ve
                // obje hiç render edilmemiş olsa da doğrudur.
                MeshFilter filter = anchorA.GetComponentInChildren<MeshFilter>(true);
                if (filter != null && filter.sharedMesh != null)
                {
                    Vector3 worldMin = filter.transform.TransformPoint(filter.sharedMesh.bounds.min);
                    float drop = anchorA.transform.position.y - worldMin.y;
                    if (drop > 0f)
                        return drop;
                }
            }
            return markerHalfHeight;
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

            // Yakalanan nokta kumandanın PIVOTU değil UCUDUR: pivot gövdenin içinde
            // durur, uç yere değdiğinde pivot birkaç cm yukarıda kalır ve o fark
            // doğrudan zemin hatasına dönüşürdü.
            Vector3 point = pointer.TransformPoint(tipLocalOffset);

            // A completed calibration restarts from scratch on the next capture.
            if (capturedCount >= 2)
                ResetCalibration();

            if (capturedCount == 0)
            {
                manualCalibrationStarted = true;
                capturedA = point;
                capturedCount = 1;
                if (anchorA != null) anchorA.SetActive(true);
                StartCoroutine(Pulse(1));
                Debug.Log($"ArenaCalibrator: point A captured at {point}.");
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
            StartCoroutine(Pulse(2));
            Debug.Log($"ArenaCalibrator: point B captured at {point}.");
            AlignRig(capturedA, point);
            Calibrated?.Invoke();
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

            Debug.Log($"ArenaCalibrator: rig aligned (yaw {yaw:F1} deg, floor {rise:F3} m).");
        }

        /// <summary>
        /// Aligns the rig from a persisted anchor pose. The anchor lives at the
        /// floor point under marker A facing marker B, so a single pose carries the
        /// full calibration — floor height included, since the offset is applied as
        /// a full vector.
        /// </summary>
        private void AlignRigToAnchorPose(Vector3 anchorPos, Quaternion anchorRot)
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

            Debug.Log($"ArenaCalibrator: rig aligned from saved anchor (yaw {yaw:F1} deg).");
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

        private void ResetCalibration()
        {
            capturedCount = 0;
            if (anchorA != null) anchorA.SetActive(false);
            if (anchorB != null) anchorB.SetActive(false);
            if (worldAnchor != null)
            {
                OVRSpatialAnchor stale = worldAnchor;
                worldAnchor = null;
                _ = EraseAnchorAsync(stale);
            }
            PlayerPrefs.DeleteKey(AnchorUuidKey);
            Debug.Log("ArenaCalibrator: calibration reset.");
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
                if (this == null || manualCalibrationStarted)
                    return;

                if (await TryRestoreOnceAsync(uuid, attempt))
                    return;

                if (attempt < RestoreAttempts)
                    await Task.Delay(RestoreRetryDelayMs);
            }

            if (this == null)
                return;

            Debug.LogWarning(
                $"ArenaCalibrator: kayıtlı kalibrasyon {RestoreAttempts} denemede geri " +
                "yüklenemedi — sağ kumandada A+B ile ELLE kalibre edin (o ana dek poz gönderilmez).",
                this);
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
                if (manualCalibrationStarted)
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
                if (anchorA != null) anchorA.SetActive(true);
                if (anchorB != null) anchorB.SetActive(true);
                Calibrated?.Invoke();
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
        /// Haptik sözlüğü: 1 kısa = A alındı · 2 kısa = B alındı ve hizalandı ·
        /// 3 kısa = mesafe hatası · 1 uzun = zemin ölçümü tutarsız.
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
