using UnityEngine;
using VortexArena.Core.Player;
using VortexArena.Net;

namespace VortexArena.Modes.Burger
{
    /// <summary>Faint heartbeat in the hand holding an ingredient — the ONLY sign that something
    /// weightless is still in the palm. Stops by itself the moment the hand lets go.
    /// <para>Self-bootstrapping persistent singleton: NO scene or prefab setup, so a new Burger arena
    /// cannot forget it. It is inert outside the mode — with no ingredient held by the local player
    /// nothing is reported at all.</para>
    /// <para>⚠️ Amplitude stays well UNDER the warning pulse and the confirmation burst
    /// (<see cref="ControllerHaptics"/>): this is a state, not an event, and the arbiter picks the
    /// highest — a loud carry buzz would mask the obstacle warning.</para></summary>
    [DisallowMultipleComponent]
    public sealed class BurgerCarryHaptics : MonoBehaviour
    {
        /// <summary>Per-hand source ids: the two hands may carry different things and must be able to
        /// go silent independently.</summary>
        private const string SourceLeft = "burger_carry_left";

        private const string SourceRight = "burger_carry_right";

        private const float Amplitude = 0.18f;

        /// <summary>Heartbeat: a double tick every period, the first on the grab itself. A steady buzz
        /// annoys within seconds; a rare beat still reads as "still holding it".</summary>
        private const float HeartbeatPeriodSeconds = 1.6f;

        private const float BeatSeconds = 0.07f;

        /// <summary>Start of the second tick after the first (the "dub" of lub-dub).</summary>
        private const float SecondBeatOffsetSeconds = 0.2f;

        private static BurgerCarryHaptics _instance;

        private bool _left;
        private bool _right;

        // When each hand started carrying — the beat phase is per hand, so a grab beats at once.
        private float _leftSince;
        private float _rightSince;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
            {
                return;
            }

            var go = new GameObject("[BurgerCarryHaptics]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<BurgerCarryHaptics>();
        }

        private void OnDisable()
        {
            Silence();
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        /// <summary>⚠️ After the grab bridges have written this frame's hold state, so a release is felt
        /// as a stop in the same frame rather than one frame late.</summary>
        private void LateUpdate()
        {
            bool left = false;
            bool right = false;

            foreach (NetObject item in NetObjectRegistry.All)
            {
                if (item == null || !item.IsHeld || !item.IsMine || item.Kind == null ||
                    !BurgerKinds.IsIngredient(item.Kind.Kind))
                {
                    continue;
                }

                if (item.HeldByRightHand)
                {
                    right = true;
                }
                else
                {
                    left = true;
                }

                if (left && right)
                {
                    break;
                }
            }

            float now = Time.unscaledTime;
            Drive(SourceLeft, false, left, ref _left, ref _leftSince, now);
            Drive(SourceRight, true, right, ref _right, ref _rightSince, now);
        }

        private static float BeatAmplitude(float sinceStart)
        {
            float phase = Mathf.Repeat(sinceStart, HeartbeatPeriodSeconds);
            bool onBeat = phase < BeatSeconds ||
                          (phase >= SecondBeatOffsetSeconds && phase < SecondBeatOffsetSeconds + BeatSeconds);
            return onBeat ? Amplitude : 0f;
        }

        /// <summary>Reports while carrying, and writes one explicit 0 when the hand empties — the
        /// heartbeat would time it out anyway, but a quarter second of buzz after the drop reads as a
        /// stuck controller.</summary>
        private static void Drive(string source, bool right, bool carrying, ref bool wasCarrying,
            ref float since, float now)
        {
            if (carrying)
            {
                if (!wasCarrying)
                {
                    since = now;
                }

                ControllerHaptics.ReportHand(source, right, BeatAmplitude(now - since));
            }
            else if (wasCarrying)
            {
                ControllerHaptics.ReportHand(source, right, 0f);
            }

            wasCarrying = carrying;
        }

        private void Silence()
        {
            float now = Time.unscaledTime;
            Drive(SourceLeft, false, false, ref _left, ref _leftSince, now);
            Drive(SourceRight, true, false, ref _right, ref _rightSince, now);
        }
    }
}
