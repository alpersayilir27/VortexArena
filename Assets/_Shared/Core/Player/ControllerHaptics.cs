using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// The <b>arbiter</b> of controller vibration: several sources report their own vibration, the one
    /// asking for the highest amplitude wins and a <b>single</b> application is made.
    /// <para>
    /// <b>Why it exists:</b> the vibration motor is single but there are multiple systems asking for it
    /// (obstacle violation · leaving the arena boundary) and both can be true at once — the guard also
    /// counts the scene's <c>ArenaObstacle</c>s as "out of area". If both called
    /// <see cref="OVRInput.SetControllerVibration"/> directly, the moment one went silent it would
    /// switch off the other's vibration; the symptom would be "the vibration keeps cutting out while
    /// standing at the wall" and its cause would be spread across two separate components.
    /// <see cref="ScreenFade"/> solves the same problem the same way — this class is its vibration twin.
    /// </para>
    /// <para>
    /// <b>Heartbeat contract:</b> a source reports its vibration <b>every frame</b>; a source that stops
    /// reporting drops out by itself after <see cref="EntryTimeoutSeconds"/>. Forgetting to say "off" is
    /// impossible — a source that forgot to switch off would vibrate the controller forever.
    /// </para>
    /// <para>
    /// ⚠️ <b>The arbiter only recomputes inside <see cref="Report"/></b>, it has NO loop of its own
    /// (not a reason to bootstrap a GameObject). That means at least one source must be reporting every
    /// frame, and that is guaranteed: <see cref="ObstacleViolationProbe"/> is a self-bootstrapping
    /// persistent singleton and reports unconditionally. An arbiter that went silent would leave the
    /// last vibration it wrote switched on.
    /// </para>
    /// <para>
    /// <b>One-shot patterns are sources too</b> (<see cref="PulseBoth"/>): there is NO path that writes
    /// a pattern straight to the motor — a vibration written behind the arbiter's back either stays on
    /// until the arbiter makes its next decision, or is never felt at all.
    /// </para>
    /// </summary>
    public static class ControllerHaptics
    {
        /// <summary>If a report gets older than this, the source counts as gone silent (s).</summary>
        private const float EntryTimeoutSeconds = 0.25f;

        /// <summary>Frequency of the pulse (Hz). ⚠️ Continuous vibration stops being a warning, which is
        /// why it pulses; the sources use the same number so their phases do not drift apart when they
        /// overlap.</summary>
        private const float PulseHz = 2f;

        /// <summary>Amplitude of the pulse (0..1).</summary>
        private const float PulseAmplitude = 0.5f;

        /// <summary>The vibration's frequency parameter — a device setting, not the source's decision.</summary>
        private const float VibrationFrequency = 0.6f;

        /// <summary>Amplitude of the confirmation burst. HIGHER than the pulse: the pulse is a warning,
        /// the burst is a confirmation and both can be true at once (a base strip right next to an
        /// obstacle) — since the arbiter picks the highest, the confirmation is not lost underneath the
        /// warning.</summary>
        private const float BurstAmplitude = 0.8f;

        private const float BurstOnSeconds = 0.12f;
        private const float BurstGapSeconds = 0.08f;

        /// <summary>All bursts are a single source: two overlapping bursts are not "two vibrations at
        /// once", the second one renews the first.</summary>
        private const string BurstSourceId = "burst";

        /// <summary>Generation of the running burst — a new burst cancels the old one (so the old
        /// coroutine does not write 0 to <see cref="Report"/> at its own end and silence the new
        /// one).</summary>
        private static int _burstGeneration;

        private struct Entry
        {
            public float Amplitude;
            public float Time;
        }

        // The number of sources is single-digit; the dictionary is for setup convenience (the sources do
        // not know about each other).
        private static readonly Dictionary<string, Entry> Sources = new Dictionary<string, Entry>();

        /// <summary>The last applied amplitude — so the same value is not written over and over.</summary>
        private static float _applied;

        /// <summary>
        /// <b>Pulse</b> report: while <paramref name="active"/> is true the source asks for vibration at
        /// <see cref="PulseHz"/>. Since the phase is derived from <see cref="Time.unscaledTime"/> it is
        /// the same across all sources.
        /// </summary>
        /// <param name="sourceId">The source's fixed id (e.g. "obstacle", "boundary").</param>
        /// <param name="active">Whether the source wants vibration right now.</param>
        public static void ReportPulse(string sourceId, bool active)
        {
            bool on = active && Mathf.Repeat(Time.unscaledTime * PulseHz, 1f) < 0.5f;
            Report(sourceId, on ? PulseAmplitude : 0f);
        }

        /// <summary>
        /// <b>One-shot confirmation burst</b>: <paramref name="pulses"/> short pulses on both
        /// controllers (an EVENT notification like "you are in the right place" — not an ongoing state
        /// like the pulse).
        /// <para>
        /// ⚠️ <b>It does not drive the motor directly</b>, it is an ordinary source writing to
        /// <see cref="Report"/> every frame: because the arbiter skips rewriting when it has "already
        /// written the same amplitude", a burst driving the motor behind its back would either be
        /// silently swallowed or leave the controller switched on when the arbiter goes quiet. Since
        /// OVRInput has no duration parameter the pattern needs a coroutine; as a static class has no
        /// GameObject of its own it runs it on <paramref name="host"/> — if the host is missing/disabled
        /// it silently does nothing (vibration is feedback, not critical).
        /// </para>
        /// </summary>
        /// <param name="host">A component living in the scene that will run the coroutine.</param>
        /// <param name="pulses">Number of pulses.</param>
        public static void PulseBoth(MonoBehaviour host, int pulses = 3)
        {
            if (host == null || !host.isActiveAndEnabled || pulses <= 0)
            {
                return;
            }

            _burstGeneration++;
            host.StartCoroutine(BurstRoutine(pulses, _burstGeneration));
        }

        private static IEnumerator BurstRoutine(int pulses, int generation)
        {
            const float period = BurstOnSeconds + BurstGapSeconds;
            float total = pulses * period - BurstGapSeconds; // no gap after the last pulse
            float start = UnityEngine.Time.unscaledTime;

            while (generation == _burstGeneration)
            {
                float elapsed = UnityEngine.Time.unscaledTime - start;
                if (elapsed >= total)
                {
                    break;
                }

                Report(BurstSourceId, Mathf.Repeat(elapsed, period) < BurstOnSeconds ? BurstAmplitude : 0f);
                yield return null;
            }

            if (generation == _burstGeneration)
            {
                Report(BurstSourceId, 0f);
            }
        }

        /// <summary>
        /// A source's vibration request for that frame. If <paramref name="amplitude"/> is <c>0</c> the
        /// source does not want vibration (same outcome as not reporting, but explicit).
        /// </summary>
        /// <param name="sourceId">The source's fixed id.</param>
        /// <param name="amplitude">Vibration amplitude, 0..1.</param>
        public static void Report(string sourceId, float amplitude)
        {
            if (string.IsNullOrEmpty(sourceId))
            {
                return;
            }

            Sources[sourceId] = new Entry
            {
                Amplitude = Mathf.Clamp01(amplitude),
                // ⚠️ unscaledTime: vibration is a PRESENTATION layer and must keep its freshness even
                // if Time.timeScale is played with (the same rationale as in ScreenFade).
                Time = UnityEngine.Time.unscaledTime
            };

            Apply(Resolve());
        }

        /// <summary>
        /// The winning amplitude: the fresh source asking for the <b>highest</b> amplitude. Mixing
        /// (adding/multiplying) is deliberately ABSENT — summing two sources' amplitudes gives a result
        /// harsher than either of them, and the answer to "why is it vibrating this hard" would not be
        /// found in any single source.
        /// </summary>
        private static float Resolve()
        {
            float now = UnityEngine.Time.unscaledTime;
            float amplitude = 0f;

            foreach (KeyValuePair<string, Entry> kv in Sources)
            {
                Entry entry = kv.Value;
                if (now - entry.Time > EntryTimeoutSeconds)
                {
                    continue; // a source that went silent
                }

                if (entry.Amplitude > amplitude)
                {
                    amplitude = entry.Amplitude;
                }
            }

            return amplitude;
        }

        private static void Apply(float amplitude)
        {
            if (Mathf.Approximately(amplitude, _applied))
            {
                return;
            }

            _applied = amplitude;
            OVRInput.SetControllerVibration(VibrationFrequency, amplitude, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(VibrationFrequency, amplitude, OVRInput.Controller.RTouch);
        }
    }
}
