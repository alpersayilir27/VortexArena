using UnityEngine;
using VortexArena.Core.Arena;
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
    /// <para>⚠️ <b>The measure is LEARNED, not asked for, and it is a CEILING:</b> it rises to the tallest
    /// confirmed sample and never sags by itself. The asymmetry is deliberate — an overestimate only makes
    /// the gesture harder, an underestimate fires it unbidden. What keeps a stale ceiling from outliving
    /// its player is therefore not a slow leak but a RESET at each boundary where the player may have
    /// changed: connect, match load, and the headset being put on. A leak would have to be slow enough not
    /// to follow a crouch, which makes it far too slow to follow a new player.</para>
    /// <para>⚠️ <b>Only a WORN, plausible, SUSTAINED sample counts.</b> A headset spends part of its life
    /// above every head — carried, handed over, lowered onto a face — and each of those moments is a
    /// perfectly valid eye-height reading of nobody. A ceiling poisoned that way has one visible symptom
    /// and it is not obviously about height: the gesture built on it becomes physically unreachable and
    /// dies silently for the rest of the session.</para>
    /// <para>⚠️ <b>Not stored on the device</b> (unlike <see cref="BodyScaleState"/>): a venue headset
    /// passes from hand to hand and a stored height would start the next player with the wrong
    /// denominator.</para>
    /// <para>It does NOT live in the scene: a self-bootstrapping persistent singleton (the
    /// <see cref="BodyScaleState"/> pattern) so no manual setup step is added per arena. With no rig
    /// (admin observer) it does nothing and <see cref="TryGet"/> returns <c>false</c>: in a session with
    /// no head there is no right answer to "how tall is this player", so it stays silent instead of
    /// inventing one.</para>
    /// </remarks>
    public class StandingHeightState : MonoBehaviour
    {
        /// <summary>Samples below this never enter the measure: the player is on the floor/crouching or
        /// the rig has not settled (the rig's first frames come in near zero). Doubles as the validity
        /// gate of <see cref="TryGet"/>.</summary>
        private const float MinSampleMeters = 0.8f;

        /// <summary>Samples above this are not an eye height but a headset in the air. ⚠️ Generous on
        /// purpose — the tallest real player must still be measurable, so this catches only the absurd;
        /// the merely suspicious is caught by <see cref="RaiseConfirmSeconds"/> and the user-present
        /// gate.</summary>
        private const float MaxSampleMeters = 2.1f;

        /// <summary>How long a taller sample must hold before the ceiling adopts it (s) — and the ceiling
        /// then takes the LOWEST value of that streak, not its peak.
        /// <para>⚠️ This is what makes a leak-free ceiling safe. A hop, a jump or a headset swung up over
        /// the head peaks for a fraction of a second; standing up straight does not. Adopting the minimum
        /// of the streak means even a confirmed rise carries no spike inside it.</para></summary>
        private const float RaiseConfirmSeconds = 0.75f;

        public static StandingHeightState Instance { get; private set; }

        /// <summary>Learned standing eye height (m); <c>0</c> = not measurable yet.</summary>
        private float _estimate;

        /// <summary>Lowest sample of the current above-the-ceiling streak, and how long it has held.</summary>
        private float _raiseCandidate;
        private float _raiseHold;

        /// <summary>Previous frame's user-present reading; its rising edge means "a head went in".</summary>
        private bool _wasUserPresent;

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
            NetEvents.OnLoadMatch += HandleLoadMatch;
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            NetEvents.OnConnected -= HandleConnected;
            NetEvents.OnLoadMatch -= HandleLoadMatch;
            Instance = null;
        }

        /// <summary>New session = probably a new player: the measure is forgotten and relearned on the
        /// first upright frame (same rationale as calibration and body scale).</summary>
        private void HandleConnected(WelcomeMsg msg) => Forget();

        /// <summary>A new match is where a venue changes players. ⚠️ Forgetting costs nothing here — the
        /// first worn, plausible sample is adopted the very same frame — so there is no reason to carry
        /// the previous player's ceiling into the next match.</summary>
        private void HandleLoadMatch(LoadMatchMsg msg) => Forget();

        private void Update()
        {
            bool userPresent = IsUserPresent();

            // A head just went in, and it is not necessarily the head the ceiling was learned from.
            if (userPresent && !_wasUserPresent)
            {
                Forget();
            }

            _wasUserPresent = userPresent;

            if (!userPresent || !TryTakeSample(out float sample))
            {
                ClearRaiseStreak();
                return;
            }

            if (sample <= _estimate)
            {
                // The ceiling already covers this posture; there is nothing to confirm.
                ClearRaiseStreak();
                return;
            }

            if (_estimate <= 0f)
            {
                // First sample after a reset: adopted at once, so a fresh match or a fresh player never
                // waits for the gesture to become available.
                _estimate = sample;
                return;
            }

            _raiseCandidate = _raiseCandidate <= 0f ? sample : Mathf.Min(_raiseCandidate, sample);
            _raiseHold += Time.deltaTime;

            if (_raiseHold < RaiseConfirmSeconds)
            {
                return;
            }

            _estimate = _raiseCandidate;
            ClearRaiseStreak();
        }

        /// <summary>Is a head actually inside the headset.
        /// <para>⚠️ Asked only when there IS a headset: with no HMD (Editor without Link) the proximity
        /// sensor answers "nobody" forever, and gating on it would leave the measure — and every gesture
        /// built on it — dead for the whole session.</para></summary>
        private static bool IsUserPresent()
        {
            return !OVRPlugin.hmdPresent || OVRPlugin.userPresent;
        }

        /// <summary>One eye-height reading; <c>false</c> when there is nothing measurable this frame.
        /// <para>⚠️ The floor reference is the ARENA floor, not the headset's tracking space. The
        /// headset's floor is a guardian setting and is exactly what is wrong when this measure lies,
        /// while the arena floor is pinned at y=0 by our own alignment — which corrects that setting
        /// (<c>ArenaCalibrator</c> captures the floor at the second anchor). Before calibration there is
        /// no arena floor yet, so the headset's own is the only answer available.</para></summary>
        private static bool TryTakeSample(out float sample)
        {
            sample = 0f;

            if (!WeaponGranter.TryResolveEyeAndFloor(out float eyeY, out float floorY))
            {
                return false;
            }

            sample = CalibrationState.IsCalibrated
                ? ArenaSpace.WorldToArena(new Vector3(0f, eyeY, 0f)).y
                : eyeY - floorY;

            return sample >= MinSampleMeters && sample <= MaxSampleMeters;
        }

        /// <summary>Drops the learned ceiling; the next worn, plausible sample rebuilds it immediately.</summary>
        private void Forget()
        {
            _estimate = 0f;
            ClearRaiseStreak();
        }

        private void ClearRaiseStreak()
        {
            _raiseCandidate = 0f;
            _raiseHold = 0f;
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
