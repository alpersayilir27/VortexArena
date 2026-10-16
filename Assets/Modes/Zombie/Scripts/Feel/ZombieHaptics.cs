using System;
using Oculus.Haptics;
using UnityEngine;

namespace VortexArena.Modes.Zombie
{
    /// <summary>The zombie mode's Meta Haptics SDK layer: one lazily created
    /// <see cref="HapticClipPlayer"/> per (cue, hand), plus a small allocation-free delay queue.</summary>
    /// <remarks>⚠️ The SDK does NOT mix: on one controller only the highest-priority player is
    /// rendered (<see cref="HapticClipPlayer.priority"/>, 0 = highest). Fire therefore outranks the hit
    /// tick, and the tick is scheduled AFTER the fire clip has ended
    /// (<see cref="ZombieFeel.HitConfirmDelaySeconds"/>) — otherwise it would be silently muted.
    /// <para>⚠️ Everything else in the game still vibrates through <c>OVRInput.SetControllerVibration</c>
    /// (dry fire, reload, <c>ControllerHaptics</c> arbiter, damage indicator). The two paths do not know
    /// about each other: the SDK drives the controller directly, so while one of these clips plays it
    /// takes precedence over an OVRInput amplitude written behind its back. Cues here are short on
    /// purpose so the arbiter's longer buzzes are not swallowed.</para>
    /// <para>⚠️ Guarded init: on an unsupported platform or a failed runtime init the SDK returns no
    /// instance (and a player's constructor would throw). The whole layer then disables itself after one
    /// log line — the mode stays playable without vibration.</para></remarks>
    internal static class ZombieHaptics
    {
        // Left / Right / Both: one player can only render on one controller at a time, so a two-hand
        // cue needs its own player rather than two Play() calls.
        private const int HandCount = 3;

        private const int PendingCapacity = 8;

        private static readonly HapticClipPlayer[] Players =
            new HapticClipPlayer[(int)ZombieHapticEvent.Count * HandCount];

        // Clip currently loaded in each player; a reassigned asset must reload instead of playing the old one.
        private static readonly HapticClip[] Loaded = new HapticClip[(int)ZombieHapticEvent.Count * HandCount];

        private static readonly Pending[] Queue = new Pending[PendingCapacity];

        private static bool disabled;
        private static bool warned;

        private struct Pending
        {
            public bool Active;
            public ZombieHapticEvent Id;
            public Controller Hand;
            public float Amplitude;
            public float DueTime;
        }

        /// <summary>Plays a cue now; silent no-op with no clip, no hand or no working SDK.</summary>
        public static void Play(ZombieHapticEvent id, Controller hand, float amplitude = 1f)
        {
            ZombieFeel feel = ZombieFeel.Live;
            if (!feel.HapticEnabled || disabled)
            {
                return;
            }

            HapticClip clip = feel.GetHaptic(id);
            if (clip == null)
            {
                return;
            }

            HapticClipPlayer player = GetPlayer(id, hand, clip);
            if (player == null)
            {
                return;
            }

            try
            {
                player.amplitude = Mathf.Clamp(amplitude, 0f, 1f);
                player.priority = feel.GetPriority(id);
                player.Play(hand);
            }
            catch (Exception exception)
            {
                Disable(exception);
            }
        }

        /// <summary>Queues a cue for <paramref name="delaySeconds"/> later (hit confirm after the fire
        /// clip). Dropped when the queue is full — a lost tick beats a growing list.</summary>
        public static void Schedule(ZombieHapticEvent id, Controller hand, float delaySeconds,
            float amplitude = 1f)
        {
            if (disabled || !ZombieFeel.Live.HapticEnabled)
            {
                return;
            }

            if (delaySeconds <= 0f)
            {
                Play(id, hand, amplitude);
                return;
            }

            for (int i = 0; i < Queue.Length; i++)
            {
                if (Queue[i].Active)
                {
                    continue;
                }

                Queue[i].Active = true;
                Queue[i].Id = id;
                Queue[i].Hand = hand;
                Queue[i].Amplitude = amplitude;
                Queue[i].DueTime = Time.unscaledTime + delaySeconds;
                return;
            }
        }

        /// <summary>Drains the delay queue; driven by <see cref="ZombieFeelRunner"/>.</summary>
        public static void Tick()
        {
            float now = Time.unscaledTime;

            for (int i = 0; i < Queue.Length; i++)
            {
                if (!Queue[i].Active || now < Queue[i].DueTime)
                {
                    continue;
                }

                Queue[i].Active = false;
                Play(Queue[i].Id, Queue[i].Hand, Queue[i].Amplitude);
            }
        }

        /// <summary>Stops everything and releases the players — the mode ended or the hooks were dropped.</summary>
        public static void Shutdown()
        {
            for (int i = 0; i < Queue.Length; i++)
            {
                Queue[i].Active = false;
            }

            for (int i = 0; i < Players.Length; i++)
            {
                HapticClipPlayer player = Players[i];
                Players[i] = null;
                Loaded[i] = null;

                if (player == null)
                {
                    continue;
                }

                try
                {
                    player.Stop();
                    player.Dispose();
                }
                catch (Exception)
                {
                    // Already-dead runtime: nothing left to release and nothing the mode can do about it.
                }
            }
        }

        /// <summary>Maps a held controller to the SDK's hand; <c>false</c> = unresolved (Editor session
        /// with no controllers) and the cue is skipped.</summary>
        public static bool TryMapHand(OVRInput.Controller controller, out Controller hand)
        {
            bool left = (controller & OVRInput.Controller.LTouch) != 0;
            bool right = (controller & OVRInput.Controller.RTouch) != 0;

            if (left && right)
            {
                hand = Controller.Both;
                return true;
            }

            if (left)
            {
                hand = Controller.Left;
                return true;
            }

            if (right)
            {
                hand = Controller.Right;
                return true;
            }

            hand = Controller.Both;
            return false;
        }

        private static HapticClipPlayer GetPlayer(ZombieHapticEvent id, Controller hand, HapticClip clip)
        {
            int index = ((int)id * HandCount) + (int)hand;
            if (index < 0 || index >= Players.Length)
            {
                return null;
            }

            HapticClipPlayer player = Players[index];

            try
            {
                if (player == null)
                {
                    // Asked BEFORE constructing: with no instance the constructor dereferences null.
                    if (Haptics.Instance == null)
                    {
                        Disable(null);
                        return null;
                    }

                    player = new HapticClipPlayer(clip);
                    Players[index] = player;
                    Loaded[index] = clip;
                    return player;
                }

                if (Loaded[index] != clip)
                {
                    player.clip = clip;
                    Loaded[index] = clip;
                }
            }
            catch (Exception exception)
            {
                Disable(exception);
                return null;
            }

            return player;
        }

        private static void Disable(Exception exception)
        {
            disabled = true;

            if (!warned)
            {
                warned = true;
                Debug.LogWarning("Zombi haptic: Meta Haptics SDK kullanılamıyor, titreşim kapatıldı. " +
                                 (exception != null ? exception.Message : "Haptics çalışma zamanı başlatılamadı."));
            }

            Shutdown();
        }
    }
}
