using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>Plays the impact of a round on the surface it hit: snow puffs on snow, splinters on
    /// wood, sparks on metal.
    /// <para>No scene setup step (<see cref="HitMarker"/>'s pattern): self-bootstraps on the first
    /// impact, goes <c>DontDestroyOnLoad</c> and keeps its pools across map changes. A scene
    /// component would add a manual step to every new arena — which is also why the surface table
    /// lives in <c>Resources</c> (<see cref="SurfaceLibrary"/>), with no reference field to bind.</para>
    /// <para>⚠️ Instances are POOLED per surface, never instantiated per shot: at 600 RPM an
    /// <c>Instantiate</c>/<c>Destroy</c> pair per round is a GC spike on Quest, and it is multiplied
    /// by however many players are shooting.</para>
    /// <para>⚠️ Do NOT call this directly from a damage source — go through
    /// <see cref="ArenaCombat.ReportImpact"/>. One gate means a new damage source (bow, axe, blast)
    /// gets impacts for free instead of each one growing its own copy of the rule.</para></summary>
    public class SurfaceImpactFx : MonoBehaviour
    {
        /// <summary>Concurrent instances per surface.</summary>
        // At 600 RPM: 10 impacts/s × ~0.5 s of visible particles ≈ 5 alive at once on ONE surface;
        // when full the oldest is recycled, which is far better than hiding a fresh impact.
        private const int NodesPerSurface = 8;

        /// <summary>Lift off the surface (m) so the effect does not z-fight with the wall it sits on.</summary>
        private const float SurfaceLiftMeters = 0.01f;

        /// <summary>Fallback audio rolloff distance (m) when the prefab brings no AudioSource.</summary>
        private const float FallbackMaxDistanceMeters = 30f;

        /// <summary>One pooled instance of one surface's effect.</summary>
        private sealed class Node
        {
            public Transform Root;
            public ParticleSystem[] Particles;
            public AudioSource Audio;
            public bool Active;

            /// <summary><c>Time.unscaledTime</c> at which the node is hidden — unscaled so a paused
            /// match does not freeze an impact on the wall.</summary>
            public float HideAt;
        }

        /// <summary>Round-robin ring for one surface.</summary>
        private sealed class Pool
        {
            public readonly Node[] Nodes = new Node[NodesPerSurface];
            public int Next;
        }

        private readonly Dictionary<SurfaceDefinition, Pool> _pools =
            new Dictionary<SurfaceDefinition, Pool>();

        private SurfaceLibrary _library;
        private bool _libraryResolved;

        private static SurfaceImpactFx _shared;

        /// <summary>The ONE pool set every impact uses; self-bootstraps on first use and goes
        /// <c>DontDestroyOnLoad</c>. Never placed in a scene, never referenced.</summary>
        public static SurfaceImpactFx Shared
        {
            get
            {
                if (_shared == null)
                {
                    var go = new GameObject("[SurfaceImpactFx]");
                    DontDestroyOnLoad(go);
                    _shared = go.AddComponent<SurfaceImpactFx>();
                }

                return _shared;
            }
        }

        /// <summary>Plays the impact for a ray hit. Silently does nothing when no surface resolves
        /// (no library, no default) — the shot path must never depend on decoration.</summary>
        public void Play(in RaycastHit hit)
        {
            SurfaceDefinition surface = Resolve(hit.collider);
            if (surface == null)
            {
                return;
            }

            Node node = TakeNode(surface);
            if (node == null || node.Root == null)
            {
                return;
            }

            // Facing the surface NORMAL: a particle system authored to spray along +Z then sprays
            // away from the wall, which is the one orientation that reads correctly on every angle.
            node.Root.SetPositionAndRotation(
                hit.point + hit.normal * SurfaceLiftMeters,
                Quaternion.LookRotation(hit.normal));

            node.Root.gameObject.SetActive(true);
            node.Active = true;
            node.HideAt = Time.unscaledTime + surface.LifetimeSeconds;

            RestartParticles(node);
            PlaySound(node, surface);
        }

        /// <summary>Hides expired nodes; pool instances are never destroyed.</summary>
        private void Update()
        {
            float now = Time.unscaledTime;

            foreach (KeyValuePair<SurfaceDefinition, Pool> entry in _pools)
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

                    if (now >= node.HideAt)
                    {
                        node.Root.gameObject.SetActive(false);
                        node.Active = false;
                    }
                }
            }
        }

        private SurfaceDefinition Resolve(Collider collider)
        {
            if (!_libraryResolved)
            {
                _libraryResolved = true;
                _library = SurfaceLibrary.Load();
            }

            return _library != null ? _library.Resolve(collider) : null;
        }

        // ---------------------------------------------------------------------- pool

        /// <summary>Round-robin node of this surface's pool, created lazily. A node whose instance was
        /// destroyed (scene unload took it) is rebuilt rather than skipped.</summary>
        private Node TakeNode(SurfaceDefinition surface)
        {
            if (!_pools.TryGetValue(surface, out Pool pool))
            {
                pool = new Pool();
                _pools.Add(surface, pool);
            }

            Node node = pool.Nodes[pool.Next];
            if (node != null && node.Root == null)
            {
                node = null;
            }

            if (node == null)
            {
                node = CreateNode(surface);
                pool.Nodes[pool.Next] = node;
            }

            pool.Next = (pool.Next + 1) % NodesPerSurface;
            return node;
        }

        private Node CreateNode(SurfaceDefinition surface)
        {
            GameObject instance;

            if (surface.ImpactPrefab != null)
            {
                // Under our DDOL root, so the pool survives a map change.
                instance = Instantiate(surface.ImpactPrefab, transform);
            }
            else
            {
                // Sound-only surface: still a node, so a surface with clips but no particles works
                // without a special case anywhere else.
                instance = new GameObject("[ImpactFx]");
                instance.transform.SetParent(transform, false);
            }

            instance.name = $"[Impact:{surface.SurfaceId}]";
            instance.SetActive(false);

            var node = new Node
            {
                Root = instance.transform,
                Particles = instance.GetComponentsInChildren<ParticleSystem>(true),
            };

            if (surface.HasSound)
            {
                node.Audio = instance.GetComponentInChildren<AudioSource>(true);
                if (node.Audio == null)
                {
                    node.Audio = instance.AddComponent<AudioSource>();
                    node.Audio.playOnAwake = false;
                    node.Audio.spatialBlend = 1f;
                    node.Audio.rolloffMode = AudioRolloffMode.Logarithmic;
                    node.Audio.maxDistance = FallbackMaxDistanceMeters;
                }
            }

            return node;
        }

        /// <summary>⚠️ Particles are CLEARED before playing: a recycled instance may still be running
        /// the previous impact, and restarting without clearing leaves its particles hanging at the
        /// old wall for a frame.</summary>
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

        private static void PlaySound(Node node, SurfaceDefinition surface)
        {
            if (node.Audio == null)
            {
                return;
            }

            AudioClip clip = surface.PickClip();
            if (clip == null)
            {
                return;
            }

            node.Audio.pitch = surface.PickPitch();
            // PlayOneShot, not Play: a recycled node may still be sounding the previous impact and
            // Play would cut it off mid-tick.
            node.Audio.PlayOneShot(clip, surface.Volume);
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
