using UnityEngine;

namespace VortexArena.Modes.Zombie
{
    /// <summary>Pooled 2D one-shot player for the mode's confirmation sounds (hit tick, headshot crunch,
    /// kill confirm, pain, close-fire layer).</summary>
    /// <remarks>2D on purpose: these are the SHOOTER's feedback, not a world event — a spatialized
    /// confirmation would arrive from the zombie instead of from the hit.
    /// <para>⚠️ No mixer group is set: the project routes SFX through the default group
    /// (<c>GameAudio</c> plays its own clips on a plain <c>spatialBlend = 0</c> source and no
    /// <c>AudioMixerGroup</c> exists anywhere), so forcing one here would create a second routing rule.</para>
    /// <para>⚠️ Fixed pool: a dropped sound on a full pool beats growing the source count under a
    /// shotgun volley on a Quest.</para></remarks>
    internal sealed class ZombieFeelAudio
    {
        private readonly AudioSource[] sources;
        private int next;

        public ZombieFeelAudio(GameObject host, int poolSize)
        {
            sources = new AudioSource[Mathf.Max(1, poolSize)];

            for (int i = 0; i < sources.Length; i++)
            {
                AudioSource source = host.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;
                source.spatialize = false;
                source.dopplerLevel = 0f;
                sources[i] = source;
            }
        }

        /// <summary>Plays one random clip of <paramref name="clips"/>; silent no-op on an empty slot
        /// (the sounds are produced later and an unfilled asset must not throw).</summary>
        public void Play(AudioClip[] clips, float volume, float pitchJitter)
        {
            if (clips == null || clips.Length == 0 || volume <= 0f)
            {
                return;
            }

            AudioClip clip = clips.Length == 1 ? clips[0] : clips[Random.Range(0, clips.Length)];
            if (clip == null)
            {
                return;
            }

            AudioSource source = Take();
            source.pitch = pitchJitter > 0f ? 1f + Random.Range(-pitchJitter, pitchJitter) : 1f;
            source.PlayOneShot(clip, volume);
        }

        public void StopAll()
        {
            for (int i = 0; i < sources.Length; i++)
            {
                if (sources[i] != null)
                {
                    sources[i].Stop();
                }
            }
        }

        /// <summary>A free source, else the round-robin one (the oldest sound is cut).</summary>
        private AudioSource Take()
        {
            for (int i = 0; i < sources.Length; i++)
            {
                int index = (next + i) % sources.Length;
                if (!sources[index].isPlaying)
                {
                    next = (index + 1) % sources.Length;
                    return sources[index];
                }
            }

            AudioSource stolen = sources[next];
            next = (next + 1) % sources.Length;
            return stolen;
        }
    }
}
