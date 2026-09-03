using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>Plays the explosion presentation of a blast: particles, one-shot sound and a short
    /// light flash.
    /// <para>Same pattern as <see cref="SurfaceImpactFx"/>: no scene setup step, self-bootstraps on
    /// the first blast, goes <c>DontDestroyOnLoad</c> and keeps its pools across map changes.</para>
    /// <para>⚠️ Instances are POOLED per explosion prefab, never instantiated per blast: an
    /// <c>Instantiate</c>/<c>Destroy</c> pair plus <c>PlayClipAtPoint</c>'s throwaway AudioSource is a
    /// GC spike on Quest, and the first blast also pays shader/material warm-up mid-fight.</para>
    /// <para>⚠️ Do NOT call this directly from a damage source — go through
    /// <see cref="BlastEffect"/>, so the blast rule stays in one place.</para></summary>
    public class BlastFxPool : MonoBehaviour
    {
        /// <summary>Concurrent instances per explosion prefab.</summary>
        // Simultaneous blasts are rare; when full the oldest is recycled.
        private const int NodesPerPrefab = 4;

        /// <summary>Lifetime of the blast flash (s).</summary>
        private const float FlashSeconds = 0.25f;

        // Sized to the blast PRESENTATION, not to the damage radius: the flash is what the fireball
        // throws onto the walls, so it shrinks and grows with the effect, not with the balance number.
        private const float FlashPeakIntensity = 10f;
        private const float FlashRangeMeters = 3f;

        /// <summary>Fallback audio rolloff distance (m) when the prefab brings no AudioSource.</summary>
        private const float FallbackMaxDistanceMeters = 30f;

        /// <summary>One pooled instance of one explosion prefab.</summary>
        private sealed class Node
        {
            public Transform Root;
            public ParticleSystem[] Particles;
            public AudioSource Audio;
            public Light Flash;
            public bool Active;

            /// <summary><c>Time.unscaledTime</c> at which the node is hidden — unscaled so a paused
            /// match does not freeze an explosion on screen.</summary>
            public float HideAt;

            /// <summary><c>Time.unscaledTime</c> at which the flash reaches zero.</summary>
            public float FlashUntil;
        }

        /// <summary>Round-robin ring for one explosion prefab.</summary>
        private sealed class Pool
        {
            public readonly Node[] Nodes = new Node[NodesPerPrefab];
            public int Next;
        }

        private readonly Dictionary<GameObject, Pool> _pools = new Dictionary<GameObject, Pool>();

        private static BlastFxPool _shared;

        /// <summary>The ONE pool set every blast uses; self-bootstraps on first use and goes
        /// <c>DontDestroyOnLoad</c>. Never placed in a scene, never referenced.</summary>
        public static BlastFxPool Shared
        {
            get
            {
                if (_shared == null)
                {
                    var go = new GameObject("[BlastFxPool]");
                    DontDestroyOnLoad(go);
                    _shared = go.AddComponent<BlastFxPool>();
                }

                return _shared;
            }
        }

        /// <summary>Plays one explosion at <paramref name="position"/>.</summary>
        public void Play(GameObject explosionPrefab, Vector3 position, AudioClip clip, float volume,
            float lifetimeSeconds)
        {
            if (explosionPrefab == null)
            {
                // No prefab means no node to pool; a bare clip is not worth a pool entry.
                if (clip != null)
                {
                    AudioSource.PlayClipAtPoint(clip, position, volume);
                }

                return;
            }

            Node node = TakeNode(explosionPrefab);
            if (node == null || node.Root == null)
            {
                return;
            }

            node.Root.position = position;
            node.Root.rotation = Quaternion.identity;

            node.Root.gameObject.SetActive(true);
            node.Active = true;
            node.HideAt = Time.unscaledTime + Mathf.Max(0.1f, lifetimeSeconds);

            RestartParticles(node);
            PlaySound(node, clip, volume);
            StartFlash(node);
        }

        /// <summary>Builds pool nodes ahead of time. The <c>Instantiate</c> plus material/shader
        /// warm-up otherwise lands on the first blast; called at fuse start it is paid while the fuse
        /// burns instead of at the explosion.</summary>
        public void Prewarm(GameObject explosionPrefab, int count)
        {
            if (explosionPrefab == null)
            {
                return;
            }

            if (!_pools.TryGetValue(explosionPrefab, out Pool pool))
            {
                pool = new Pool();
                _pools.Add(explosionPrefab, pool);
            }

            int wanted = Mathf.Min(count, NodesPerPrefab);
            for (int i = 0; i < wanted; i++)
            {
                if (pool.Nodes[i] == null || pool.Nodes[i].Root == null)
                {
                    pool.Nodes[i] = CreateNode(explosionPrefab);
                }
            }

            // Next is left alone: prewarming must not shift where the next blast lands in the ring.
        }

        /// <summary>Fades flashes and hides expired nodes; pool instances are never destroyed.</summary>
        private void Update()
        {
            float now = Time.unscaledTime;

            foreach (KeyValuePair<GameObject, Pool> entry in _pools)
            {
                Node[] nodes = entry.Value.Nodes;
                for (int i = 0; i < nodes.Length; i++)
                {
                    Node node = nodes[i];
                    if (node == null || !node.Active)
                    {
                        continue;
                    }

                    if (node.Root == null)
                    {
                        node.Active = false;
                        continue;
                    }

                    if (node.Flash != null)
                    {
                        float remaining = node.FlashUntil - now;
                        if (remaining > 0f)
                        {
                            node.Flash.intensity = FlashPeakIntensity * (remaining / FlashSeconds);
                        }
                        else if (node.Flash.enabled)
                        {
                            node.Flash.enabled = false;
                        }
                    }

                    if (now >= node.HideAt)
                    {
                        node.Root.gameObject.SetActive(false);
                        node.Active = false;
                    }
                }
            }
        }

        // ---------------------------------------------------------------------- pool

        /// <summary>Round-robin node of this prefab's pool, created lazily. A node whose instance was
        /// destroyed (scene unload took it) is rebuilt rather than skipped.</summary>
        private Node TakeNode(GameObject explosionPrefab)
        {
            if (!_pools.TryGetValue(explosionPrefab, out Pool pool))
            {
                pool = new Pool();
                _pools.Add(explosionPrefab, pool);
            }

            Node node = pool.Nodes[pool.Next];
            if (node != null && node.Root == null)
            {
                node = null;
            }

            if (node == null)
            {
                node = CreateNode(explosionPrefab);
                pool.Nodes[pool.Next] = node;
            }

            pool.Next = (pool.Next + 1) % NodesPerPrefab;
            return node;
        }

        private Node CreateNode(GameObject explosionPrefab)
        {
            // Under our DDOL root, so the pool survives a map change.
            GameObject instance = Instantiate(explosionPrefab, transform);
            instance.name = "[Blast:" + explosionPrefab.name + "]";
            instance.SetActive(false);

            var node = new Node
            {
                Root = instance.transform,
                Particles = instance.GetComponentsInChildren<ParticleSystem>(true),
            };

            node.Audio = instance.GetComponentInChildren<AudioSource>(true);
            if (node.Audio == null)
            {
                node.Audio = instance.AddComponent<AudioSource>();
                node.Audio.playOnAwake = false;
                node.Audio.spatialBlend = 1f;
                node.Audio.rolloffMode = AudioRolloffMode.Logarithmic;
                node.Audio.maxDistance = FallbackMaxDistanceMeters;
            }

            node.Flash = instance.GetComponentInChildren<Light>(true);
            if (node.Flash == null)
            {
                node.Flash = instance.AddComponent<Light>();
                node.Flash.type = LightType.Point;
                node.Flash.shadows = LightShadows.None;
                node.Flash.range = FlashRangeMeters;
                node.Flash.color = new Color(1f, 0.62f, 0.25f);
                node.Flash.intensity = 0f;
            }

            node.Flash.enabled = false;
            return node;
        }

        /// <summary>⚠️ Particles are CLEARED before playing: a recycled instance may still be running
        /// the previous blast, and restarting without clearing leaves its particles hanging at the old
        /// position for a frame.</summary>
        private static void RestartParticles(Node node)
        {
            if (node.Particles == null)
            {
                return;
            }

            for (int i = 0; i < node.Particles.Length; i++)
            {
                ParticleSystem particles = node.Particles[i];
                if (particles == null)
                {
                    continue;
                }

                particles.Clear(true);
                particles.Play(true);
            }
        }

        private static void PlaySound(Node node, AudioClip clip, float volume)
        {
            if (node.Audio == null || clip == null)
            {
                return;
            }

            // PlayOneShot, not Play: a recycled node may still be sounding the previous blast and
            // Play would cut it off mid-tick.
            node.Audio.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        private static void StartFlash(Node node)
        {
            if (node.Flash == null)
            {
                return;
            }

            node.Flash.intensity = FlashPeakIntensity;
            node.Flash.enabled = true;
            node.FlashUntil = Time.unscaledTime + FlashSeconds;
        }

        private void OnDestroy()
        {
            if (_shared == this)
            {
                // Never leave the static field on a destroyed component: the next Play rebuilds the
                // pools (required when domain reload is disabled).
                _shared = null;
            }
        }
    }
}
