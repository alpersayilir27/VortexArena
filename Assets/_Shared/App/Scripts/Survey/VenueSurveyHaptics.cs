using System.Collections;
using UnityEngine;

namespace VortexArena.App.Survey
{
    /// <summary>
    /// The survey's haptic vocabulary (same shape as <c>ArenaCalibrator.Pulse</c>): 1 short = point
    /// captured · 2 short = mode changed · 3 short = rejected · 1 long = entered / sent · 1 medium =
    /// undone.
    /// <para>⚠️ Vibration is the ONLY feedback the player gets with their eyes on the wall, so a
    /// rejected capture must always buzz — a silent rejection reads as "captured".</para>
    /// </summary>
    internal static class VenueSurveyHaptics
    {
        /// <summary>The survey is a right-hand gesture set; input and haptics share this one value.</summary>
        internal const OVRInput.Controller Hand = OVRInput.Controller.RTouch;

        internal const float Short = 0.12f;
        internal const float Medium = 0.6f;
        internal const float Long = 1f;

        /// <summary>Runs the pulse train on <paramref name="host"/>'s coroutine.</summary>
        internal static void Pulse(MonoBehaviour host, int count, float seconds = Short)
        {
            if (host == null || !host.isActiveAndEnabled)
            {
                return;
            }

            host.StartCoroutine(Run(count, seconds));
        }

        /// <remarks>⚠️ Realtime waits: the survey can be triggered while a match is paused
        /// (<c>timeScale == 0</c>), where <c>WaitForSeconds</c> would never return and leave the
        /// motor running.</remarks>
        private static IEnumerator Run(int count, float seconds)
        {
            for (int i = 0; i < count; i++)
            {
                OVRInput.SetControllerVibration(1f, 1f, Hand);
                yield return new WaitForSecondsRealtime(seconds);
                OVRInput.SetControllerVibration(0f, 0f, Hand);

                if (i < count - 1)
                {
                    yield return new WaitForSecondsRealtime(0.1f);
                }
            }
        }
    }
}
