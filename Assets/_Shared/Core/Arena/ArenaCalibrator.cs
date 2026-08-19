using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Protocol;

namespace VortexArena.Core.Arena
{
    /// <summary>Two-point calibration aligning the virtual arena with the physical play space. Hold A on the
    /// right controller and double-tap B with the controller TIP on a floor mark: the first capture
    /// lights anchor_a, the second lights anchor_b and moves the rig so both virtual markers land on
    /// their physical marks. The pose persists as an OVRSpatialAnchor and is restored next session;
    /// repeating the gesture after a completed calibration starts over.
    /// <para>6DOF: yaw and horizontal position from A-&gt;B, floor height from the tip captured at
    /// B. Floor comes from the measurement, never the tracking origin — venues run without
    /// guardian/space setup, so the system floor is a guess.</para>
    /// <para>⚠️ No rotation enters the measurement: the point sits
    /// <see cref="floorProbeDropMeters"/> below the pivot along <b>world</b> -Y, and yaw comes only
    /// from the horizontal delta of the two captured points.</para>
    /// <para>Map changes do NOT reset calibration: the anchor UUID persists in PlayerPrefs, so the
    /// next scene's calibrator restores it and nobody respawns (Docs/ArenaNet-Protokol.md §10.4).
    /// Precondition: every arena in a venue shares the same physical floor marks.</para>
    /// <para>Operator clear has two modes (<c>clear_calibration.keepSaved</c>, §5.2/§10.6) behind
    /// <see cref="ApplyOperatorClear"/>: soft keeps the device anchor and leans on
    /// <see cref="autoRestoreBlocked"/>, hard also drops anchor and UUID.</para>
    /// <para>Marker POSITIONS come from the venue dimensions file (<c>calibration.a</c>/<c>.b</c>),
    /// so a venue's arenas and lobby share one measurement. ⚠️ Markers are the dimension mock's
    /// cubes — no second marker family.</para>
    /// <para>⚠️ Single position contract: a marker's transform position IS the floor point, no
    /// mesh-base offset, else two markers of the same point never overlap.</para>
    /// <para>⚠️ Order is always A → B and cannot be verified geometrically; the guarantee is
    /// procedural. Swapped = arena flipped 180°.</para>
    /// <para>Markers are a setup aid, not scenery: shown only during manual calibration and for
    /// <see cref="markerVisibleSeconds"/> after alignment, never on the silent restore path.</para>
    /// <para><c>PlayerPoseTracker</c> does not wait for alignment; earlier poses are offset by
    /// design so the network sees a connected player. An uncalibrated headset is placed by guess
    /// (<see cref="PreAlignWhenTracked"/>) — not a calibration, just enough to see the
    /// arena.</para></summary>
    public class ArenaCalibrator : MonoBehaviour
    {
        /// <summary>Object name of the first calibration marker — single source for the scene
        /// marker, mock cube and editor tools. The field <c>anchorA</c> is the same name in
        /// camelCase and is a serialization key, so it never changes.</summary>
        public const string AnchorAName = "anchor_a";

        /// <summary>Object name of the second calibration marker (see <see cref="AnchorAName"/>).</summary>
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

        // Floor consistency: both tips must sit on one plane. A mismatch is measurement error, not
        // slope. ⚠️ No slope compensation — two points do not define a plane and a tilted world is
        // nauseating in VR.
        private const float FloorMismatchWarn = 0.03f;
        private const float FloorMismatchReject = 0.10f;
        private const float LongPulseSeconds = 0.6f;

        /// <summary>Point A captured: one SHORT pulse.</summary>
        private const float PointAPulseSeconds = 0.3f;

        /// <summary>Point B captured and rig aligned: one LONG pulse, audibly distinct from A.</summary>
        private const float PointBPulseSeconds = 1f;

        /// <summary>Retry count for restoring the saved anchor; the anchor service is not always
        /// ready at scene load. ⚠️ With <see cref="RestoreRetryDelayMs"/> this is a ~10 s window and
        /// is NOT shortened: localization waits for the room to be recognized, so a short window
        /// turns a transient failure permanent. Polling faster does not help — the wait is tracking,
        /// so the INTERVAL is long, not the count.</summary>
        private const int RestoreAttempts = 6;

        /// <summary>Retry interval (ms).</summary>
        private const int RestoreRetryDelayMs = 2000;

        /// <summary>Longest pre-align wait for tracking (seconds). Applied anyway on timeout: the
        /// editor sandbox has no HMD and the camera must still land inside the arena.</summary>
        private const float PreAlignTrackingTimeout = 2f;

        /// <summary>Minimum local head displacement (m², rig-relative) to count as tracked;
        /// CenterEyeAnchor stays at zero until HMD data arrives.</summary>
        private const float PreAlignHeadEpsilonSqr = 1e-4f;

        /// <summary>True once the arena has been aligned, manually or from a saved anchor.</summary>
        public bool IsCalibrated => capturedCount >= 2;

        /// <summary>Calibration source tag (<c>set_calibration.source</c>, §5.1). Deliberately a
        /// string: the server does not validate it, so adding a source shifts no numeric index.</summary>
        public const string SourceManual = "manual";
        public const string SourceAnchor = "anchor";

        /// <summary>Raised on the main thread when calibration completes; the argument is the
        /// SOURCE (<see cref="SourceManual"/> / <see cref="SourceAnchor"/>).
        /// <c>CalibrationState</c> listens and sends <c>set_calibration</c> (§10.6). Pose sending
        /// does not depend on this.</summary>
        public static event Action<string> Calibrated;

        /// <summary>Bumped on every rig alignment. An alignment legitimately teleports root, hands
        /// and body in one frame, so continuity guards (e.g. the root-jump clamp in
        /// <c>ArenaNetCharacterBehaviour</c>) read this and drop their stored reference.</summary>
        public static int CalibrationGeneration { get; private set; }

        /// <summary>Floor offset from the last MANUAL calibration (signed meters), sent as
        /// <c>set_calibration.floorOffset</c> (§10.6). Zero on the anchor-restore path — nothing is
        /// measured there and a stale value would warn the operator falsely.</summary>
        public static float LastFloorOffsetMeters { get; private set; }

        /// <summary>UUID of the anchor created in THIS process; survives scene changes, dies with
        /// the app. Separate from the disk record because the map-change restore runs from here and
        /// is INDEPENDENT of calibration mode (<see cref="ResolveSavedUuid"/>).</summary>
        private static string sessionAnchorUuid;

        /// <summary>Operator invalidated the alignment → AUTOMATIC restore (scene start, map change) is off
        /// for the rest of this process. Pierced by the operator's own <c>reload_calibration</c>
        /// (<c>operatorRequest</c>) and reopened by any legitimate alignment.
        /// <para>⚠️ Only thing holding a soft clear (<c>clear_calibration.keepSaved</c>): the anchor
        /// still lives on the device, so the next scene load would restore it and silently undo the
        /// operator's decision. Second line of defence in hard mode.</para>
        /// <para>⚠️ Process-scoped: after a restart a soft clear no longer holds and a
        /// <c>saved_anchor</c> headset restores on launch. Use the hard command to really
        /// erase.</para></summary>
        private static bool autoRestoreBlocked;

        private OVRCameraRig cameraRig;
        private OVRSpatialAnchor worldAnchor;
        private Vector3 capturedA;
        private int capturedCount;
        private int pendingBTaps;
        private float firstBTapTime;
        private bool waitingForRelease;
        private bool manualCalibrationStarted;

        /// <summary>An in-flight restore was cancelled (<see cref="Invalidate"/>). Operator-side
        /// twin of <see cref="manualCalibrationStarted"/>: the retry window lasts seconds, and
        /// ignoring a clear inside it would realign the arena after the clear (§10.6).</summary>
        private bool restoreAborted;

        /// <summary>An operator-initiated reload is in flight. A second request is rejected, not
        /// queued: two runs would apply the same anchor twice and the admin button could not tell
        /// which result was its own.</summary>
        private bool forcedReloadRunning;
        private bool trackingEventsHooked;
        private bool realignQueued;
        private Coroutine markerHideRoutine;

        /// <summary>Pre-align already queued. It has two triggers (no saved UUID / restore failed
        /// outright) that can fire back to back; a second run would shift the rig again.</summary>
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

        /// <summary>Arena floor height (world space), read off marker A. No offset math: by the
        /// single contract a marker's transform position already is the floor point.</summary>
        private float VirtualFloorY =>
            anchorA != null ? anchorA.transform.position.y : 0f;

        private void Start()
        {
            ResolveMarkers();
            PlaceMarkersFromPlan();
            SetMarkersVisible(false);
            TryHookTrackingEvents();

            // No saved alignment → no restore will run, so queue the pre-align immediately.
            if (string.IsNullOrEmpty(ResolveSavedUuid()))
                StartPreAlign();

            _ = RestoreSavedCalibrationAsync();
        }

        /// <summary>UUID of the anchor to restore, empty if none. Order: <see cref="sessionAnchorUuid"/>
        /// (carries MAP CHANGES, mode-independent), then the disk record if
        /// <see cref="CalibrationState.DiskRestoreAllowed"/>.
        /// <para>⚠️ The mode gates only the app LAUNCH restore: under <c>two_anchor</c> the disk
        /// UUID is never read but is still written, so the freshest calibration is ready if the
        /// operator switches to <c>saved_anchor</c>.</para>
        /// <para>⚠️ <paramref name="operatorRequest"/> deliberately pierces that gate and
        /// <see cref="autoRestoreBlocked"/>: a reload is not a launch, and gating it would make the
        /// button useless in <c>two_anchor</c> venues — the case it exists for (§10.6).</para></summary>
        private static string ResolveSavedUuid(bool operatorRequest = false)
        {
            // Operator invalidated the alignment: no self-driven restore brings it back (in soft
            // mode the anchor is still on the device).
            if (!operatorRequest && autoRestoreBlocked)
                return string.Empty;

            if (!string.IsNullOrEmpty(sessionAnchorUuid))
                return sessionAnchorUuid;

            return operatorRequest || CalibrationState.DiskRestoreAllowed
                ? PlayerPrefs.GetString(AnchorUuidKey, string.Empty)
                : string.Empty;
        }

        /// <summary>Resolves empty marker fields: Inspector field, then the mock's
        /// <see cref="DimensionAnchor"/> cubes, then name. ⚠️ The name path stays — scenes without a
        /// mock have hand-placed markers and would become unalignable.</summary>
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

        /// <summary>Finds a marker by <see cref="DimensionAnchor.Kind"/>. Duplicates use the first
        /// and warn: two A cubes mean two mocks, and which matches the floor tape is
        /// unknowable.</summary>
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

        /// <summary>Finds a marker by name — compatibility path for scenes without a mock.</summary>
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

        /// <summary>Places the markers on the dimensions file's calibration points, or leaves them
        /// alone if it has none. ⚠️ Never silent: without points the markers keep prefab positions
        /// and miss the floor tape, so alignment succeeds but puts the arena in the wrong
        /// place.</summary>
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
            // OVRManager may wake after us; keep trying until hooked.
            if (!trackingEventsHooked)
                TryHookTrackingEvents();

            // Gesture: while A is HELD, tap B twice within doubleTapSeconds. No hold duration —
            // waiting with the tip on the mark makes the hand shake and the measurement drift.
            bool aHeld = OVRInput.Get(OVRInput.Button.One, Hand);

            if (waitingForRelease)
            {
                if (!aHeld)
                    waitingForRelease = false;
                return;
            }

            // A released → drop the half-finished sequence; taps only count while A is held.
            if (!aHeld)
            {
                pendingBTaps = 0;
                return;
            }

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

            // §10.6: manual calibration is CLOSED while calibrated; only the operator reopens it.
            // Never swallowed silently (long pulse + log). Checked at the END of the sequence — B is
            // used for other things in game and a single tap would false-alarm.
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

        /// <summary>Puts a marker on the given FLOOR point (world space, so parenting is
        /// irrelevant). ⚠️ Single contract: the transform position IS the floor point, no mesh-base
        /// offset — two Y contracts mean two markers of the same point never overlap.</summary>
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

            // The captured point is the controller TIP, not its pivot (which sits inside the body).
            // ⚠️ Offset is along WORLD -Y, never the controller's LOCAL axis (no TransformPoint): a
            // local offset would tie the measurement to grip angle, so a controller held at 45°
            // would drift horizontally and fall short vertically.
            Vector3 point = pointer.position + Vector3.down * floorProbeDropMeters;

            // A completed calibration restarts from scratch on the next capture. ⚠️ The device
            // record goes too, else orphan anchors pile up.
            if (capturedCount >= 2)
            {
                ResetAlignmentState();
                PurgeSavedAnchor();
            }

            // ⚠️ Order is fixed: first capture is A, second B. It cannot be derived geometrically,
            // so this counter plus the lit marker is the only guarantee.
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
            MeasureFloorOffset(point);
            if (anchorB != null) anchorB.SetActive(true);
            StartCoroutine(Pulse(1, PointBPulseSeconds));
            Debug.Log($"ArenaCalibrator: 2/2 — B yakalandı, fiziksel {point} → sanal " +
                      $"'{AnchorBName}' {(anchorB != null ? anchorB.transform.position.ToString() : "(yok)")}.");
            AlignRig(capturedA, point);
            HideMarkersAfterConfirmation();
            RaiseCalibrated(SourceManual);
            _ = CreateAndSaveAnchorAsync();
        }

        /// <summary>Measures the floor offset (§10.6). Under <c>Stage</c> origin the floor guess is
        /// tracking-local y=0, so the tracking-local Y of a tip on the real floor IS the offset.
        /// ⚠️ Read via <see cref="Transform.InverseTransformPoint"/> and BEFORE alignment — the rig
        /// may already carry a pre-align shift that world Y would include. The threshold is a
        /// diagnostic, not a gate.</summary>
        private void MeasureFloorOffset(Vector3 floorPoint)
        {
            Transform rig = RigRoot;
            LastFloorOffsetMeters = rig != null ? rig.InverseTransformPoint(floorPoint).y : 0f;

            if (Mathf.Abs(LastFloorOffsetMeters) > ArenaProtocol.CALIB_FLOOR_WARN_METERS)
            {
                Debug.LogWarning(
                    $"ArenaCalibrator: zemin sapması {LastFloorOffsetMeters:F2} m " +
                    $"(eşik {ArenaProtocol.CALIB_FLOOR_WARN_METERS:F2} m) — gözlüğün alan verisi " +
                    "(space setup) bayat. Kalibrasyon yine de kabul edildi; veriyi temizleyip " +
                    "yeniden kalibre etmek gerekir.", this);
            }
        }

        /// <summary>The captured span must match the marker span: a bare lower bound would silently
        /// accept a bad calibration when the floor tape is laid at the wrong distance.</summary>
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

        /// <summary>Moves the rig so the physical points land on the virtual markers: yaw from the
        /// A-&gt;B directions, horizontal position from A, floor height from the tip at B.</summary>
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

            // Yaw is about up and the shift is horizontal, so physicalB.y still holds its
            // capture-time value.
            float rise = virtualFloorY - physicalB.y;
            rig.position += Vector3.up * rise;

            CalibrationGeneration++;
            Debug.Log($"ArenaCalibrator: rig aligned (yaw {yaw:F1} deg, floor {rise:F3} m).");
        }

        /// <summary>Aligns the rig from a persisted anchor pose. The anchor sits at the floor point
        /// under marker A facing B, so one pose carries the full calibration including floor
        /// height (the offset is applied as a full vector).</summary>
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

        /// <summary>Queues the pre-align once. Two callers: a headset with no saved UUID, and a
        /// restore that failed on every attempt.</summary>
        private void StartPreAlign()
        {
            if (preAlignStarted)
                return;

            preAlignStarted = true;
            StartCoroutine(PreAlignWhenTracked());
        }

        /// <summary>Uncalibrated pre-align: GUESSES the rig placement so the head sits midway between A and
        /// B facing A→B (same as the saved anchor's contract, so the player does not feel rotated
        /// later). An unaligned rig lands on the raw tracking origin, usually outside the arena
        /// inside <see cref="ArenaBoundary"/>'s blackout.
        /// <para>⚠️ NOT a calibration: <see cref="capturedCount"/> does not move,
        /// <see cref="Calibrated"/> is not raised, no anchor is saved and the manual gate stays
        /// open. A real alignment repositions absolutely, so the guess never accumulates.</para>
        /// <para>⚠️ The HEAD is moved, not the rig root: the root is the floor reference, while the
        /// player is a standing head — centering the root leaves them off by the HMD offset.</para>
        /// <para>Tracking is awaited at most <see cref="PreAlignTrackingTimeout"/> seconds; the
        /// HMD-less sandbox keeps the head at local zero and must still land in the arena.</para></summary>
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

            // Markers are already placed by PlaceMarkersFromPlan and ResolveMarkers already warned
            // about missing/identical ones; a second warning would be noise.
            if (anchorA == null || anchorB == null || anchorA == anchorB)
                yield break;

            Vector3 pointA = anchorA.transform.position;
            Vector3 pointB = anchorB.transform.position;

            Vector3 forward = pointB - pointA;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f)
                yield break;
            forward.Normalize();

            // Yaw first (about the head), then the shift: the rotation keeps the head in place.
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

            // The rig moved legitimately: jump suppressors must not read this as a fault.
            CalibrationGeneration++;
            Debug.Log("ArenaCalibrator: kalibrasyon yok — rig arenanın A-B ortasına TAHMİNEN " +
                      "yerleştirildi. Bu bir kalibrasyon DEĞİLDİR; oyuncu elle hizalamalıdır.");
        }

        /// <summary>Is the pre-align still wanted: skipped if a real alignment landed in between or
        /// the operator cleared, since a guess must not overwrite a measurement.</summary>
        private bool PreAlignStillWanted()
        {
            return !IsCalibrated && !manualCalibrationStarted && !restoreAborted && capturedCount == 0;
        }

        /// <summary>Recenter and tracking reacquisition stale the calibration: the origin moves but
        /// the rig transform does not, so the arena drifts. Stage origin already disables system
        /// recenter; this is the second line of defence.</summary>
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
            // The anchor transform refreshes a frame or two after the origin change.
            yield return null;
            yield return null;
            realignQueued = false;

            if (worldAnchor == null)
                yield break;

            Debug.Log("ArenaCalibrator: tracking origin changed, realigning from the saved anchor.");
            AlignRigToAnchorPose(worldAnchor.transform.position, worldAnchor.transform.rotation);
        }

        /// <summary>Invalidates the alignment (§10.6): markers hide, the rig alignment drops, the manual gate
        /// reopens. <paramref name="keepSavedAnchor"/> = <c>true</c> keeps the device anchor and
        /// leans on <see cref="autoRestoreBlocked"/>; <c>false</c> also runs
        /// <see cref="PurgeSavedAnchor"/>. ⚠️ The rig is NOT moved — free-roam keeps the player
        /// physically where they are.
        /// <para>⚠️ Every intermediate state goes too, so a clear never strands the player midway:
        /// a captured A point with pending taps, the held gesture
        /// (<see cref="waitingForRelease"/>), and an in-flight restore
        /// (<see cref="restoreAborted"/>) that would otherwise realign the arena later.</para></summary>
        public void Invalidate(bool keepSavedAnchor)
        {
            restoreAborted = true;
            waitingForRelease = true;
            ResetAlignmentState();
            if (!keepSavedAnchor)
                PurgeSavedAnchor();
        }

        /// <summary>Alignment done: markers stay up for a short confirmation window, then hide.
        /// Short because a match can start meanwhile — they are feedback, not scenery.</summary>
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

        /// <summary>SOFT half of a clear: completed alignment AND any half-finished sequence. Leaves the
        /// device record to <see cref="PurgeSavedAnchor"/>.
        /// <para>⚠️ Intermediate state must go: clearing only <see cref="capturedCount"/> leaves
        /// <see cref="capturedA"/> and the pending tap counter alive, so one B tap after the clear
        /// could complete a calibration from the PRE-clear A point (§10.6).</para>
        /// <para>⚠️ The <see cref="worldAnchor"/> binding is released (device anchor kept):
        /// <see cref="HandleTrackingDisturbed"/> realigns from that field, so keeping it would undo
        /// a soft clear on the first tracking event.</para>
        /// <para>⚠️ <see cref="waitingForRelease"/> is driven from <see cref="Invalidate"/>, not
        /// here: the other caller is <see cref="CapturePoint"/>, where A must STAY held.</para></summary>
        private void ResetAlignmentState()
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
                Destroy(stale.gameObject);
            }
            Debug.Log("ArenaCalibrator: hizalama geçersiz kılındı (cihazdaki kayda dokunulmadı).");
        }

        /// <summary>HARD half of a clear: erases the device <c>OVRSpatialAnchor</c> plus the session and disk
        /// UUID records, leaving <c>reload_calibration</c> nothing to load. Static, so it works with
        /// no calibrator in the scene.
        /// <para>⚠️ Erasure goes through the UUID, never <see cref="worldAnchor"/>: that field
        /// starts null on every scene load and a restore can take
        /// <see cref="RestoreAttempts"/>×<see cref="RestoreRetryDelayMs"/>, so a clear landing in
        /// that window would erase nothing.</para></summary>
        private static void PurgeSavedAnchor()
        {
            string uuidText = !string.IsNullOrEmpty(sessionAnchorUuid)
                ? sessionAnchorUuid
                : PlayerPrefs.GetString(AnchorUuidKey, string.Empty);

            // ⚠️ The session record goes too, else the next scene aligns from that anchor and
            // silently undoes the clear.
            sessionAnchorUuid = null;
            PlayerPrefs.DeleteKey(AnchorUuidKey);
            // ⚠️ Save() is mandatory (symmetric with CreateAndSaveAnchorAsync): a delete kept only
            // in memory leaves the old on-disk UUID alive if the app is killed, and the "cleared"
            // headset restores its old alignment on the next launch.
            PlayerPrefs.Save();

            if (Guid.TryParse(uuidText, out Guid uuid))
                _ = EraseSavedAnchorAsync(uuid);

            Debug.Log("ArenaCalibrator: cihazdaki kalibrasyon kaydı silindi.");
        }

        /// <summary>Single entry point for <c>clear_calibration</c> (§10.6).
        /// <paramref name="keepSaved"/> = <c>true</c> voids only the alignment and keeps the device
        /// anchor so a following <c>reload_calibration</c> works. ⚠️ Hard mode erases even with no
        /// calibrator in the scene: racing a scene load must not swallow the clear.</summary>
        public static void ApplyOperatorClear(bool keepSaved)
        {
            // Closed in both modes: soft mode has nothing else, and in hard mode an in-flight
            // restore could still return the alignment before the erase lands.
            autoRestoreBlocked = true;

            ArenaCalibrator calibrator = FindFirstObjectByType<ArenaCalibrator>();
            if (calibrator != null)
            {
                calibrator.Invalidate(keepSaved);
                return;
            }

            if (!keepSaved)
                PurgeSavedAnchor();
        }

        /// <summary>Alignment complete → raise the event; both completion paths go through here.
        /// <para>⚠️ Body measurement does NOT hang off this (§10.8): the operator starts it, since
        /// auto-measuring would measure a player bent over to touch the floor.</para>
        /// <para>⚠️ Reopens the auto-restore gate: the alignment returned legitimately, so the
        /// operator's invalidation is spent — leaving it shut loses the next map change.</para></summary>
        private static void RaiseCalibrated(string source)
        {
            autoRestoreBlocked = false;
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
                    // The disk record is always written even if the mode never reads it; the session
                    // record is read regardless (see ResolveSavedUuid).
                    sessionAnchorUuid = anchor.Uuid.ToString();
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

        /// <summary>The operator's <c>reload_calibration</c> (§10.6): reloads from the saved anchor and
        /// reports back. <paramref name="onResult"/> fires on the main thread exactly once; empty
        /// string = success. Static because <c>CalibrationState</c> is a persistent singleton that
        /// does not know the per-scene calibrator.
        /// <para>⚠️ An explicit request beats the player's half-finished sequence and an earlier
        /// abort — a broken alignment is why the command exists; the launch path keeps those
        /// guards.</para></summary>
        public static void RequestReload(Action<string> onResult)
        {
            ArenaCalibrator calibrator = FindFirstObjectByType<ArenaCalibrator>();
            if (calibrator == null)
            {
                onResult?.Invoke("sahnede kalibratör yok");
                return;
            }

            calibrator.BeginForcedReload(onResult);
        }

        private void BeginForcedReload(Action<string> onResult)
        {
            if (forcedReloadRunning)
            {
                // A second run would apply the same anchor twice; let the operator wait.
                onResult?.Invoke("yükleme zaten sürüyor");
                return;
            }

            // ⚠️ Pierces the mode gate: calibration mode gates only the launch restore (§10.6).
            string saved = ResolveSavedUuid(true);
            if (string.IsNullOrEmpty(saved) || !Guid.TryParse(saved, out _))
            {
                // Never silently "successful" (§10.6): the operator must know there is nothing to load.
                onResult?.Invoke("cihazda kayıtlı kalibrasyon yok");
                return;
            }

            forcedReloadRunning = true;
            restoreAborted = false;
            _ = RestoreSavedCalibrationAsync(true, onResult);
        }

        /// <summary>Reports an operator request's result exactly once. No-op on the launch run,
        /// which has no owner.</summary>
        private void FinishForcedReload(bool forced, Action<string> onResult, string error)
        {
            if (!forced)
                return;

            forcedReloadRunning = false;
            onResult?.Invoke(error);
        }

        private enum RestoreOutcome
        {
            Aligned,

            /// <summary>Transient failure — a further attempt may run.</summary>
            Retry,

            /// <summary>Lost its owner: scene changed or the player/operator acted. Not retried.</summary>
            Abandoned
        }

        /// <summary>Restores the saved calibration (also the map-change path), retried
        /// <see cref="RestoreAttempts"/> times. Liveness is checked after every <c>await</c>: a dead
        /// calibrator must not touch the new scene's rig.
        /// <para><paramref name="forced"/> = explicit operator request: the player's half sequence
        /// and an earlier abort do not stop it, and on failure <see cref="StartPreAlign"/> is NOT
        /// called — pre-align belongs to the launch path, not to guessing mid-game.</para>
        /// <para>⚠️ <paramref name="onResult"/> must fire on EVERY exit branch, including the one
        /// where the component died during an <c>await</c>: a missing branch hangs the admin
        /// button.</para></summary>
        private async Task RestoreSavedCalibrationAsync(bool forced = false, Action<string> onResult = null)
        {
            // On an operator request the disk record is read regardless of mode (see ResolveSavedUuid).
            string saved = ResolveSavedUuid(forced);
            if (string.IsNullOrEmpty(saved) || !Guid.TryParse(saved, out Guid uuid))
            {
                FinishForcedReload(forced, onResult, "cihazda kayıtlı kalibrasyon yok");
                return;
            }

            for (int attempt = 1; attempt <= RestoreAttempts; attempt++)
            {
                if (this == null)
                {
                    FinishForcedReload(forced, onResult, "sahne değişti");
                    return;
                }

                // Player beat us with their own gesture, or the operator cleared: drop the restore
                // (in a soft clear the anchor is still on the device, so applying it would undo the
                // operator's decision). ⚠️ No such gate on an explicit request (§10.6).
                if (!forced && (manualCalibrationStarted || restoreAborted))
                    return;

                RestoreOutcome outcome = await TryRestoreOnceAsync(uuid, attempt, forced);
                if (outcome == RestoreOutcome.Aligned)
                {
                    FinishForcedReload(forced, onResult, "");
                    return;
                }

                if (outcome == RestoreOutcome.Abandoned)
                {
                    FinishForcedReload(forced, onResult, "sahne değişti");
                    return;
                }

                if (attempt < RestoreAttempts)
                    await Task.Delay(RestoreRetryDelayMs);
            }

            if (this == null)
            {
                FinishForcedReload(forced, onResult, "sahne değişti");
                return;
            }

            if (!forced && restoreAborted)
                return;

            Debug.LogWarning(
                $"ArenaCalibrator: kayıtlı kalibrasyon {RestoreAttempts} denemede geri " +
                "yüklenemedi — sağ kumandada A basılıyken B'ye çift basarak ELLE kalibre edin " +
                "(o ana dek gönderilen " +
                "pozlar arena ile örtüşmez).",
                this);

            if (forced)
            {
                FinishForcedReload(true, onResult, $"kayıtlı çapa {RestoreAttempts} denemede yüklenemedi");
                return;
            }

            // A player about to calibrate manually must SEE the arena; an unaligned rig can leave
            // them inside the boundary blackout.
            StartPreAlign();
        }

        /// <summary>One attempt. Swallows transient errors and returns
        /// <see cref="RestoreOutcome.Retry"/>.</summary>
        private async Task<RestoreOutcome> TryRestoreOnceAsync(Guid uuid, int attempt, bool forced)
        {
            try
            {
                var unbound = new List<OVRSpatialAnchor.UnboundAnchor>();
                var load = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(new[] { uuid }, unbound);
                if (this == null) return RestoreOutcome.Abandoned; // scene changed: this calibrator is done
                if (!load.Success || unbound.Count == 0)
                {
                    Debug.Log($"ArenaCalibrator: kayıtlı anchor yüklenemedi ({load.Status}), deneme {attempt}.");
                    return RestoreOutcome.Retry;
                }

                OVRSpatialAnchor.UnboundAnchor unboundAnchor = unbound[0];
                if (!unboundAnchor.Localized && !await unboundAnchor.LocalizeAsync())
                {
                    if (this == null) return RestoreOutcome.Abandoned;
                    Debug.Log($"ArenaCalibrator: kayıtlı anchor localize edilemedi, deneme {attempt}.");
                    return RestoreOutcome.Retry;
                }

                if (this == null) return RestoreOutcome.Abandoned;

                // The user beat the restore to it; keep their manual calibration. ⚠️ An operator
                // clear stops here too (it may have arrived after the last await). Both are skipped
                // on an explicit reload request, which arrived after them (§10.6).
                if (!forced && (manualCalibrationStarted || restoreAborted))
                    return RestoreOutcome.Abandoned;

                if (!unboundAnchor.TryGetPose(out Pose pose))
                {
                    Debug.Log($"ArenaCalibrator: kayıtlı anchor pozu okunamadı, deneme {attempt}.");
                    return RestoreOutcome.Retry;
                }

                AlignRigToAnchorPose(pose.position, pose.rotation);

                var go = new GameObject("ArenaWorldAnchor");
                var anchor = go.AddComponent<OVRSpatialAnchor>();
                unboundAnchor.BindTo(anchor);
                worldAnchor = anchor;
                // ⚠️ The session record is written here too, not only in CreateAndSaveAnchorAsync:
                // left empty on a disk-restored headset, the map-change restore would hit the
                // calibration-mode gate though that path is mode-independent (see ResolveSavedUuid).
                sessionAnchorUuid = uuid.ToString();
                capturedCount = 2;
                // Markers stay hidden: this restore is silent and runs on map change. Floor is not
                // measured here, so there is no offset to report (§10.6).
                LastFloorOffsetMeters = 0f;
                RaiseCalibrated(SourceAnchor);
                return RestoreOutcome.Aligned;
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
                return RestoreOutcome.Retry;
            }
        }

        /// <summary>Erases the device anchor by UUID, never via <see cref="worldAnchor"/>: it must
        /// work whether or not the anchor is bound here. Errors are swallowed — the clear already
        /// applied locally. ⚠️ The anchor list is <c>null</c>, so the UUID list must be non-empty —
        /// the SDK throws <c>ArgumentException</c> if both are <c>null</c>.</summary>
        private static async Task EraseSavedAnchorAsync(Guid uuid)
        {
            try
            {
                var result = await OVRSpatialAnchor.EraseAnchorsAsync(null, new[] { uuid });
                if (!result.Success)
                    Debug.LogWarning($"ArenaCalibrator: kayıtlı çapa silinemedi ({result.Status}).");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"ArenaCalibrator: kayıtlı çapa silinemedi ({e.Message}).");
            }
        }

        /// <summary>Haptic vocabulary: 1 short (0.3 s) = A captured · 1 long (1 s) = B captured and
        /// aligned · 3 short = distance error · 1 medium (0.6 s) = floor mismatch or locked.</summary>
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
