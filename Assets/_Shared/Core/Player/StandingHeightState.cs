using UnityEngine;
using VortexArena.Core.Combat;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Core.Player
{
    /// <summary>The player's <b>standing eye height</b> (m from the floor) — the denominator of
    /// body-relative gestures. Unlike live eye height, this number does <b>not</b> change when the player
    /// bends, crouches or looks down.</summary>
    /// <remarks>
    /// ⚠️ <b>Why a separate measure:</b> a threshold using live eye height sinks together with the very
    /// motion made to reach it. Bending drops the head 40–55 cm, the threshold drops with it and a hanging
    /// arm ends up below it — the gesture fires without the player doing anything. With a fixed
    /// denominator the same gesture demands the same motion in every posture.
    /// <para>⚠️ <b>The measure is LEARNED, not asked for:</b> the ceiling rises instantly and leaks
    /// downward very slowly at <see cref="DecayMetersPerSecond"/>. The asymmetry is deliberate — an
    /// overestimate only makes the gesture harder (the player lowers their hand a bit more), an
    /// underestimate fires it by itself. The leak exists so the measure converges when a shorter person
    /// takes over the headset.</para>
    /// <para>⚠️ <b>Not stored on the device</b> (unlike <see cref="BodyScaleState"/>): a venue headset
    /// passes from hand to hand and a stored height would start the next player with the wrong
    /// denominator. For the same reason it resets on every <c>hello</c>; it re-settles on the first frame
    /// the player stands upright.</para>
    /// <para>It does NOT live in the scene: a self-bootstrapping persistent singleton (the
    /// <see cref="BodyScaleState"/> pattern) so no manual setup step is added per arena. With no rig
    /// (admin observer) it does nothing and <see cref="TryGet"/> returns <c>false</c>: in a session with
    /// no head there is no right answer to "how tall is this player", so it stays silent instead of
    /// inventing one.</para>
    /// </remarks>
    public class StandingHeightState : MonoBehaviour
    {
        /// <summary>Samples below this never enter the measure: the player is on the floor/crouching or
        /// the rig has not settled (the rig's first frames come in near zero).</summary>
        private const float MinSampleMeters = 0.8f;

        /// <summary>Downward leak rate of the estimate (m/s) — 3 cm/min. It exists only to converge on a
        /// shorter player taking over the headset; slow enough to lose nothing worth mentioning while a
        /// player crouches and stands back up.</summary>
        private const float DecayMetersPerSecond = 0.0005f;

        public static StandingHeightState Instance { get; private set; }

        /// <summary>Learned standing eye height (m); <c>0</c> = not measurable yet.</summary>
        private float _estimate;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var go = new GameObject("[StandingHeightState]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<StandingHeightState>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // We are a persistent singleton: we subscribe in Awake/OnDestroy instead of
            // OnEnable/OnDisable so no event is missed if the object is disabled (BodyScaleState pattern).
            NetEvents.OnConnected += HandleConnected;
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            NetEvents.OnConnected -= HandleConnected;
            Instance = null;
        }

        /// <summary>New session = probably a new player: the measure resets and is relearned on the first
        /// upright frame (same rationale as calibration and body scale).</summary>
        private void HandleConnected(WelcomeMsg msg)
        {
            _estimate = 0f;
        }

        private void Update()
        {
            if (!WeaponGranter.TryResolveEyeAndFloor(out float eyeY, out float floorY))
            {
                return;
            }

            float sample = eyeY - floorY;
            if (sample < MinSampleMeters)
            {
                return;
            }

            // Both directions in one line: a higher sample raises the ceiling INSTANTLY, otherwise the
            // estimate leaks slowly toward the sample without dropping below it.
            _estimate = Mathf.Max(sample, _estimate - DecayMetersPerSecond * Time.deltaTime);
        }

        /// <summary>Standing eye height (m from the floor). <c>false</c> if no valid sample was ever taken
        /// (no rig, player on the floor) — the calling gesture is then NOT recognised that frame.</summary>
        public static bool TryGet(out float standingEyeHeight)
        {
            standingEyeHeight = Instance != null ? Instance._estimate : 0f;
            return standingEyeHeight >= MinSampleMeters;
        }
    }
}
