using UnityEngine;

namespace VortexArena.App.Survey
{
    /// <summary>
    /// The venue survey gesture vocabulary on ONE controller: A+B hold (enter/finish), B hold
    /// (capture), A held + three B taps (next mode), A hold (undo).
    /// <para>
    /// Plain class, not a MonoBehaviour: the same state machine is ticked both outside the survey
    /// scene (by the persistent gesture watcher) and inside it (by the controller). A second
    /// implementation would let the two disagree about what "3 seconds" means.
    /// </para>
    /// <para>
    /// ⚠️ <b>A gesture fires ONCE and then disarms until BOTH buttons are released.</b> Otherwise a
    /// single long A+B press would enter the scene and immediately finish it.
    /// </para>
    /// <para>
    /// ⚠️ Unscaled time: a paused match (<c>Time.timeScale == 0</c>) must not freeze the gesture.
    /// </para>
    /// </summary>
    public sealed class VenueSurveyInput
    {
        public const float HoldSeconds = 3f;
        public const float TapMaxHold = 0.5f;
        public const float TapGap = 0.6f;
        public const int ModeTaps = 3;

        private readonly OVRInput.Controller hand;

        // -1 = the hold is not running.
        private float bothSince = -1f;
        private float undoSince = -1f;
        private float captureSince = -1f;

        // A hold that was interrupted by the other button: it may not restart mid-press.
        private bool undoSpoiled;
        private bool captureSpoiled;

        private bool waitingForRelease;

        private float tapDownTime;
        private float lastTapTime;
        private int tapCount;

        public VenueSurveyInput(OVRInput.Controller hand)
        {
            this.hand = hand;
        }

        /// <summary>A+B held long enough: enter the survey, or finish it.</summary>
        public bool EnterExitFired { get; private set; }

        /// <summary>B held long enough: capture the point under the controller.</summary>
        public bool CaptureFired { get; private set; }

        /// <summary>A held + three B taps: advance to the next mode.</summary>
        public bool ModeSwitchFired { get; private set; }

        /// <summary>A held alone long enough: drop the last point.</summary>
        public bool UndoFired { get; private set; }

        /// <summary>0..1 progress of the running hold (for the on-screen label).</summary>
        public float EnterExitProgress { get; private set; }

        /// <inheritdoc cref="EnterExitProgress"/>
        public float CaptureProgress { get; private set; }

        /// <inheritdoc cref="EnterExitProgress"/>
        public float UndoProgress { get; private set; }

        /// <summary>B taps counted so far in the current sequence.</summary>
        public int ModeTapCount => tapCount;

        /// <summary>Arms nothing until both buttons are released — for a reader that starts while
        /// the previous reader's gesture (the A+B entry hold) may still be pressed.</summary>
        public void RequireRelease()
        {
            ClearHolds();
            waitingForRelease = true;
        }

        /// <summary>Drops every running hold and tap sequence (mode change, scene change).</summary>
        public void Reset()
        {
            ClearHolds();
            waitingForRelease = false;
            EnterExitFired = false;
            CaptureFired = false;
            ModeSwitchFired = false;
            UndoFired = false;
            EnterExitProgress = 0f;
            CaptureProgress = 0f;
            UndoProgress = 0f;
        }

        /// <summary>Samples the controller once per frame; the <c>*Fired</c> flags describe THIS
        /// frame only and are cleared by the next call.</summary>
        public void Tick()
        {
            EnterExitFired = false;
            CaptureFired = false;
            ModeSwitchFired = false;
            UndoFired = false;
            EnterExitProgress = 0f;
            CaptureProgress = 0f;
            UndoProgress = 0f;

            bool a = OVRInput.Get(OVRInput.Button.One, hand);
            bool b = OVRInput.Get(OVRInput.Button.Two, hand);
            bool bDown = OVRInput.GetDown(OVRInput.Button.Two, hand);
            bool bUp = OVRInput.GetUp(OVRInput.Button.Two, hand);
            float now = Time.unscaledTime;

            if (waitingForRelease)
            {
                if (!a && !b)
                {
                    waitingForRelease = false;
                }

                ClearHolds();
                return;
            }

            if (!a && !b)
            {
                // Full release is the only thing that re-arms a spoiled hold.
                ClearHolds();
                return;
            }

            // ---- next mode: A held + short B taps ----
            if (a)
            {
                if (bDown)
                {
                    tapDownTime = now;

                    // Any B press kills undo for this A hold — otherwise tapping would also undo.
                    undoSpoiled = true;
                    undoSince = -1f;
                }

                if (bUp)
                {
                    bool shortEnough = now - tapDownTime <= TapMaxHold;
                    bool continues = tapCount == 0 || now - lastTapTime <= TapGap;
                    tapCount = shortEnough && continues ? tapCount + 1 : 1;
                    lastTapTime = now;

                    if (tapCount >= ModeTaps)
                    {
                        tapCount = 0;
                        ModeSwitchFired = true;
                        waitingForRelease = true;
                        ClearHolds();
                        return;
                    }
                }
                else if (tapCount > 0 && now - lastTapTime > TapGap)
                {
                    tapCount = 0;
                }
            }
            else
            {
                tapCount = 0;
            }

            // ---- enter / finish: both buttons ----
            if (a && b)
            {
                if (bothSince < 0f)
                {
                    bothSince = now;
                }

                captureSince = -1f;
                undoSince = -1f;
                captureSpoiled = true;
                undoSpoiled = true;

                float bothHeld = now - bothSince;
                EnterExitProgress = Mathf.Clamp01(bothHeld / HoldSeconds);

                if (bothHeld >= HoldSeconds)
                {
                    EnterExitFired = true;
                    waitingForRelease = true;
                    ClearHolds();
                }

                return;
            }

            bothSince = -1f;

            // ---- undo: A alone ----
            if (a)
            {
                captureSince = -1f;
                captureSpoiled = true;

                if (undoSpoiled)
                {
                    return;
                }

                if (undoSince < 0f)
                {
                    undoSince = now;
                }

                float undoHeld = now - undoSince;
                UndoProgress = Mathf.Clamp01(undoHeld / HoldSeconds);

                if (undoHeld >= HoldSeconds)
                {
                    UndoFired = true;
                    waitingForRelease = true;
                    ClearHolds();
                }

                return;
            }

            undoSince = -1f;
            undoSpoiled = false; // A released → undo is armed again

            // ---- capture: B alone ----
            if (captureSpoiled)
            {
                captureSince = -1f;
                return;
            }

            if (captureSince < 0f)
            {
                captureSince = now;
            }

            float captureHeld = now - captureSince;
            CaptureProgress = Mathf.Clamp01(captureHeld / HoldSeconds);

            if (captureHeld >= HoldSeconds)
            {
                CaptureFired = true;
                waitingForRelease = true;
                ClearHolds();
            }
        }

        private void ClearHolds()
        {
            bothSince = -1f;
            undoSince = -1f;
            captureSince = -1f;
            undoSpoiled = false;
            captureSpoiled = false;
            tapCount = 0;
        }
    }
}
