using UnityEngine;

namespace VortexArena.Core.Audio
{
    /// <summary>Per-channel LOCAL level applied at play time (0..1).</summary>
    /// <remarks>
    /// A multiplier on playback only: it changes WHO HEARS what, not WHAT IS PLAYED. Clip
    /// selection, the shared phase (<c>sceneElapsed</c>) and network behaviour are untouched.
    /// <para>⚠️ It NEVER goes on the wire and is NOT persisted: persistence and "who writes it"
    /// belong to App (<c>AdminSession</c>). Core cannot see App (the asmdef graph flows down), so
    /// APP writes the value and is the only writer — a second writer would leave "who turned it
    /// down" unanswerable. Same pattern as <c>RemoteShotFx.SpectatorAudioFocus</c>.</para>
    /// <para>Default is 1 → on a client that writes nothing (the VR player) nothing changes.</para>
    /// </remarks>
    public static class AudioMix
    {
        /// <summary>Number of <see cref="AudioChannel"/> values.</summary>
        public const int ChannelCount = 4;

        private static readonly float[] Levels = { 1f, 1f, 1f, 1f };

        public static float Ambience => Levels[(int)AudioChannel.Ambience];

        public static float Weapons => Levels[(int)AudioChannel.Weapons];

        public static float Voiceover => Levels[(int)AudioChannel.Voiceover];

        public static float Music => Levels[(int)AudioChannel.Music];

        /// <summary>Level of the channel; 1 for an out-of-range index (nothing is attenuated by an
        /// unknown channel).</summary>
        public static float Of(AudioChannel channel)
        {
            int index = (int)channel;
            return index >= 0 && index < Levels.Length ? Levels[index] : 1f;
        }

        /// <summary>Sets the channel level (clamped to 0..1); an out-of-range index is ignored.</summary>
        public static void Set(AudioChannel channel, float level)
        {
            int index = (int)channel;
            if (index < 0 || index >= Levels.Length)
            {
                return;
            }

            Levels[index] = Mathf.Clamp01(level);
        }

        /// <summary>Back to the default: every channel at 1.</summary>
        public static void Reset()
        {
            for (int i = 0; i < Levels.Length; i++)
            {
                Levels[i] = 1f;
            }
        }
    }
}
