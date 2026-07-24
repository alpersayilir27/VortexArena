using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// Two-point calibration that aligns the virtual arena with the physical play
    /// space. Hold A+B on the right controller while resting it on a floor mark:
    /// the first capture lights up anchor_a, the second lights up anchor_b and
    /// moves the camera rig so both virtual markers land on their physical marks.
    /// The calibrated pose is persisted as an OVRSpatialAnchor and restored
    /// automatically on the next session. Holding A+B again after a completed
    /// calibration starts a fresh one.
    /// </summary>
    public class ArenaCalibrator : MonoBehaviour
    {
        [Header("Virtual markers")]
        [Tooltip("Marker at the first physical floor mark. Enabled on first capture.")]
        [SerializeField] private GameObject anchorA;
        [Tooltip("Marker at the second physical floor mark. Enabled on second capture.")]
        [SerializeField] private GameObject anchorB;
        [Tooltip("Marker pivot height above the arena floor (half of the marker cube).")]
        [SerializeField] private float markerHalfHeight = 0.05f;

        [Header("Rig")]
        [Tooltip("Root moved by the alignment. Falls back to the OVRCameraRig transform.")]
        [SerializeField] private Transform rigRoot;

        [Header("Capture")]
        [Tooltip("How long A+B must be held before a point is captured (seconds).")]
        [SerializeField] private float holdSeconds = 3f;
        [Tooltip("Minimum horizontal distance between the two captured points (meters).")]
        [SerializeField] private float minPointDistance = 1f;

        private const string AnchorUuidKey = "VortexArena.CalibrationAnchorUuid";
        private const OVRInput.Controller Hand = OVRInput.Controller.RTouch;

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

        private void Start()
        {
            if (anchorA != null) anchorA.SetActive(false);
            if (anchorB != null) anchorB.SetActive(false);
            _ = RestoreSavedCalibrationAsync();
        }

        private void Update()
        {
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

        private void CapturePoint()
        {
            Transform pointer = RightController;
            if (pointer == null)
            {
                Debug.LogWarning("ArenaCalibrator: right controller not found.", this);
                return;
            }
            Vector3 point = pointer.position;

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
            if (flat.magnitude < minPointDistance)
            {
                StartCoroutine(Pulse(3));
                Debug.LogWarning("ArenaCalibrator: points are too close together, capture B again.");
                return;
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
        /// Moves the rig so the physical points land on the virtual markers:
        /// yaw from the A->B directions, horizontal position from point A, and the
        /// rig pinned to the arena floor (floor-level tracking origin keeps the
        /// rig root on the physical floor).
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
            rig.position = new Vector3(rig.position.x, virtualA.y - markerHalfHeight, rig.position.z);

            Debug.Log($"ArenaCalibrator: rig aligned (yaw {yaw:F1} deg).");
        }

        /// <summary>
        /// Aligns the rig from a persisted anchor pose. The anchor lives at the
        /// floor point under marker A facing marker B, so a single pose carries
        /// the full calibration.
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

            Vector3 target = new Vector3(virtualA.x, virtualA.y - markerHalfHeight, virtualA.z);
            rig.position += target - anchorPos;

            Debug.Log($"ArenaCalibrator: rig aligned from saved anchor (yaw {yaw:F1} deg).");
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
                Vector3 floorPoint = new Vector3(virtualA.x, virtualA.y - markerHalfHeight, virtualA.z);

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

        private async Task RestoreSavedCalibrationAsync()
        {
            try
            {
                string saved = PlayerPrefs.GetString(AnchorUuidKey, string.Empty);
                if (string.IsNullOrEmpty(saved) || !Guid.TryParse(saved, out Guid uuid))
                    return;

                var unbound = new List<OVRSpatialAnchor.UnboundAnchor>();
                var load = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(new[] { uuid }, unbound);
                if (!load.Success || unbound.Count == 0)
                {
                    Debug.LogWarning($"ArenaCalibrator: saved anchor could not be loaded ({load.Status}).", this);
                    return;
                }

                OVRSpatialAnchor.UnboundAnchor unboundAnchor = unbound[0];
                if (!unboundAnchor.Localized && !await unboundAnchor.LocalizeAsync())
                {
                    Debug.LogWarning("ArenaCalibrator: saved anchor could not be localized.", this);
                    return;
                }

                // The user beat the restore to it; keep their manual calibration.
                if (manualCalibrationStarted)
                    return;

                if (!unboundAnchor.TryGetPose(out Pose pose))
                {
                    Debug.LogWarning("ArenaCalibrator: saved anchor pose unavailable.", this);
                    return;
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
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
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

        private IEnumerator Pulse(int count)
        {
            for (int i = 0; i < count; i++)
            {
                OVRInput.SetControllerVibration(1f, 1f, Hand);
                yield return new WaitForSeconds(0.12f);
                OVRInput.SetControllerVibration(0f, 0f, Hand);
                if (i < count - 1)
                    yield return new WaitForSeconds(0.1f);
            }
        }
    }
}
