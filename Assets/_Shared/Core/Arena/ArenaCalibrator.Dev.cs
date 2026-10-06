#if UNITY_EDITOR
using System;
using System.Collections;
using UnityEngine;
using VortexArena.Core.Player;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// EDITOR ONLY — the dev alignment: with the dev window's switch on, the physical A/B
    /// calibration is skipped, the head is placed over marker A facing A→B at the default stature,
    /// the same <c>Calibrated</c> event is raised with source <c>"dev"</c>, and the thumbsticks walk
    /// and snap-turn the rig.
    ///
    /// <para>⚠️ This is the only place in the project that moves the rig HORIZONTALLY and the only
    /// exception to the free-roam rule — editor only, it never enters a build. A real alignment
    /// (manual A/B, anchor restore) or an operator clear ends it.</para>
    ///
    /// <para>⚠️ It never writes to or touches the calibration record: no anchor is created, the
    /// PlayerPrefs UUID and <c>capturedCount</c> stay as they are.</para>
    ///
    /// <para>Plan: <c>plan/dev-kalibre-atlama.md</c>.</para>
    /// </summary>
    public partial class ArenaCalibrator
    {
        /// <summary>Calibration source tag for the dev placement (set_calibration.source). Editor only.</summary>
        public const string SourceDev = "dev";

        /// <summary>Written by DevSession before the first scene loads (editor selection).</summary>
        public static bool DevSkipRequested { get; set; }

        /// <summary>Is a dev placement in force? BodyScaleState reads it to skip the auto measurement.</summary>
        public static bool IsDevAligned => devAligned != null;

        private const float DevWalkSpeedMetersPerSecond = 2f;
        private const float DevStickDeadZone = 0.15f;
        private const float DevTurnStepDegrees = 30f;
        private const float DevTurnThreshold = 0.7f;
        private const float DevReturnHoldSeconds = 1f;

        /// <summary>Calibrator whose dev placement stands. Unity's destroyed-object null ends it on a
        /// scene change by itself.</summary>
        private static ArenaCalibrator devAligned;

        private bool devClickHeld;
        private float devClickStart;
        private bool devClickConsumed;
        private bool devTurnLatched;
        private bool devUserPresent;

        /// <summary>Eye height of the default stature (m).</summary>
        private static float DevEyeHeight =>
            BodyScaleState.DefaultStatureMeters - BodyScaleState.HeadTopAboveEyeMeters;

        partial void DevStart(ref bool handled)
        {
            if (!DevSkipRequested)
                return;

            // A real alignment from this process wins; the launch path restores it.
            if (!string.IsNullOrEmpty(sessionAnchorUuid))
                return;

            if (autoRestoreBlocked)
            {
                Debug.Log("ArenaCalibrator: DEV — operatör hizalamayı sıfırladı; dev hizalama " +
                          "kapalı. Admin'den yeniden yükle ya da elle A/B al.");
                return;
            }

            handled = true;
            Debug.Log($"ArenaCalibrator: DEV — kalibrasyon atlanıyor: kafa A işaretine, boy " +
                      $"{BodyScaleState.DefaultStatureMeters:F2} m; sol çubuk yürür, sağ çubuk döner.");
            StartCoroutine(DevAlignWhenTracked(null));
        }

        partial void DevReload(Action<string> onResult, ref bool handled)
        {
            if (!DevSkipRequested || !string.IsNullOrEmpty(sessionAnchorUuid))
                return;

            handled = true;

            // An explicit request beats a half-finished gesture and an earlier clear (same as the
            // real reload). ⚠️ forcedReloadRunning is left alone — it belongs to the real path.
            ResetAlignmentState();
            restoreAborted = false;
            StartCoroutine(DevAlignWhenTracked(onResult));
        }

        private bool DevAlignStillWanted() =>
            !manualCalibrationStarted && !restoreAborted && capturedCount == 0;

        /// <summary>Twin of <see cref="PreAlignWhenTracked"/>: waits for tracking, then places the
        /// rig and reports calibrated.</summary>
        private IEnumerator DevAlignWhenTracked(Action<string> onResult)
        {
            float deadline = Time.unscaledTime + PreAlignTrackingTimeout;

            while (Time.unscaledTime < deadline)
            {
                if (!DevAlignStillWanted())
                {
                    onResult?.Invoke("dev hizalama iptal: elle kalibrasyon başladı ya da operatör sıfırladı");
                    yield break;
                }

                Transform tracked = HeadAnchor;
                if (tracked != null && tracked.localPosition.sqrMagnitude > PreAlignHeadEpsilonSqr)
                    break;

                yield return null;
            }

            if (!DevAlignStillWanted())
            {
                onResult?.Invoke("dev hizalama iptal: elle kalibrasyon başladı ya da operatör sıfırladı");
                yield break;
            }

            string problem = ApplyDevPlacement();
            if (problem != null)
            {
                onResult?.Invoke(problem);
                yield break;
            }

            // ⚠️ capturedCount stays 0 on purpose: a manual capture on a "completed" calibration
            // purges the device record, and the dev placement must not cause that. No anchor is
            // created or saved and ReportHeadHeightAfterAlign is not called — there is no
            // measurement to judge.
            LastFloorOffsetMeters = 0f;
            devAligned = this;
            devUserPresent = OVRPlugin.userPresent;
            RaiseCalibrated(SourceDev);
            onResult?.Invoke("");
        }

        /// <summary>Places the head over marker A facing A→B at the default eye height.
        /// <c>null</c> = done, otherwise the reason.</summary>
        private string ApplyDevPlacement()
        {
            Transform rig = RigRoot;
            Transform head = HeadAnchor;
            if (rig == null || head == null)
                return "rig ya da kafa yok";
            if (!rig.gameObject.activeInHierarchy)
                return "rig kapalı";

            bool hasMarks = anchorA != null && anchorB != null && anchorA != anchorB;
            Vector3 forward = Vector3.zero;
            if (hasMarks)
            {
                forward = anchorB.transform.position - anchorA.transform.position;
                forward.y = 0f;
            }

            if (hasMarks && forward.sqrMagnitude >= 1e-6f)
            {
                forward.Normalize();

                Vector3 current = head.forward;
                current.y = 0f;
                if (current.sqrMagnitude < 1e-6f)
                {
                    current = rig.forward;
                    current.y = 0f;
                }

                // Yaw about the head first, so the shift below lands the head itself.
                if (current.sqrMagnitude >= 1e-6f)
                {
                    rig.RotateAround(head.position, Vector3.up,
                                     Vector3.SignedAngle(current, forward, Vector3.up));
                }

                Vector3 a = anchorA.transform.position;
                rig.position += new Vector3(a.x, VirtualFloorY + DevEyeHeight, a.z) - head.position;
            }
            else
            {
                rig.position += Vector3.up * (VirtualFloorY + DevEyeHeight - head.position.y);
                Debug.LogWarning("ArenaCalibrator: DEV — kalibrasyon işareti yok; rig yatayda " +
                                 "yerinde kaldı, yalnız yükseklik oturtuldu.", this);
            }

            // Absolute like the real aligners (nothing accumulates); the lift rides on top, same
            // contract.
            ApplyFloorLift();
            CalibrationGeneration++;
            return null;
        }

        partial void DevUpdate()
        {
            if (devAligned != this)
                return;

            Transform rig = RigRoot;
            Transform head = HeadAnchor;
            if (rig == null || head == null)
                return;

            DevWalk(rig, head);
            DevTurn(rig, head);
            DevClick();
            DevReseatOnMount();
        }

        /// <summary>Left stick: walks along the head's horizontal gaze.</summary>
        private void DevWalk(Transform rig, Transform head)
        {
            Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch);
            if (stick.sqrMagnitude <= DevStickDeadZone * DevStickDeadZone)
                return;

            Vector3 forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f)
                return;
            forward.Normalize();

            Vector3 right = Vector3.Cross(Vector3.up, forward);

            // ⚠️ Horizontal only, so the floor lift keeps its delta contract. NO generation bump
            // either: a bump schedules a body measurement and resets the jump guards, and this
            // motion is far below their thresholds.
            rig.position += (forward * stick.y + right * stick.x) *
                            (DevWalkSpeedMetersPerSecond * Time.deltaTime);
        }

        /// <summary>Right stick: snap-turns around the head. ⚠️ Only the stick's AXIS is read — its
        /// CLICK belongs to LobbyController's IP panel.</summary>
        private void DevTurn(Transform rig, Transform head)
        {
            float x = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch).x;
            if (Mathf.Abs(x) < DevTurnThreshold)
            {
                devTurnLatched = false;
                return;
            }

            if (devTurnLatched)
                return;

            // Edge-triggered snap about the head; a discrete move, so the guards get the bump.
            devTurnLatched = true;
            rig.RotateAround(head.position, Vector3.up, Mathf.Sign(x) * DevTurnStepDegrees);
            CalibrationGeneration++;
        }

        /// <summary>Left stick click: a short press re-seats the height, a 1 s hold returns to
        /// marker A.</summary>
        private void DevClick()
        {
            bool held = OVRInput.Get(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.LTouch);

            if (held && !devClickHeld)
            {
                devClickHeld = true;
                devClickStart = Time.unscaledTime;
                devClickConsumed = false;
                return;
            }

            if (held && !devClickConsumed && Time.unscaledTime - devClickStart >= DevReturnHoldSeconds)
            {
                devClickConsumed = true;
                string problem = ApplyDevPlacement();
                Debug.Log(problem == null
                    ? "ArenaCalibrator: DEV — A işaretine dönüldü."
                    : $"ArenaCalibrator: DEV — A'ya dönülemedi: {problem}");
                return;
            }

            if (!held && devClickHeld)
            {
                devClickHeld = false;
                if (!devClickConsumed)
                    DevReseatHeight("çubuk basışı");
            }
        }

        /// <summary>The headset was on the desk at placement time; the first mount re-seats.</summary>
        private void DevReseatOnMount()
        {
            bool present = OVRPlugin.userPresent;
            if (present && !devUserPresent)
                DevReseatHeight("gözlük takıldı");
            devUserPresent = present;
        }

        /// <summary>Vertical only; the lift is already applied, so it is part of the target.</summary>
        private void DevReseatHeight(string why)
        {
            Transform rig = RigRoot;
            Transform head = HeadAnchor;
            if (rig == null || head == null)
                return;

            rig.position += Vector3.up *
                            (VirtualFloorY + FloorLiftMeters + DevEyeHeight - head.position.y);
            CalibrationGeneration++;
            Debug.Log($"ArenaCalibrator: DEV — boy yeniden oturtuldu ({why}).");
        }

        partial void DevAlignmentReplaced()
        {
            devAligned = null;
        }
    }
}
#endif
