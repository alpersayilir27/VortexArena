// ⚠️ Never add `using System;` here: UnityEngine.Random would clash with System.Random.
using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core.Audio;

namespace VortexArena.Core.Combat
{
    /// <summary>3D whiz where an ENEMY round passed the LOCAL head — tells an ambushed player they are
    /// being shot at. Fed only by <see cref="RemoteShotFx"/> (own shots never reach it).
    /// <para>⚠️ Scheduled to the tracer's arrival: a whiz before the streak reads as a phantom
    /// shooter. Self-bootstraps (DDOL); clips from <c>WeaponCatalog.NearMissClips</c>, empty = off.</para></summary>
    public class NearMissWhizFx : MonoBehaviour
    {
        /// <summary>Concurrent whizzes. Small on purpose: the rate limit below already keeps them
        /// ~0.06 s apart, and a thick wall of whizzes is noise, not information.</summary>
        private const int PoolSize = 4;

        /// <summary>How close the round has to pass the head (m) to be heard. Tight on purpose: a
        /// whiz from a round a metre overhead reads as a false alarm.</summary>
        private const float NearMissRadiusMeters = 0.25f;

        /// <summary>If the shot ENDS this close to the head (m) the round hit us (or the wall behind
        /// our ear): the hit/damage feedback already covers that, a whiz would double it.</summary>
        private const float HitProximityMeters = 0.35f;

        /// <summary>Shots fired this close to us (m) are skipped: at that range the gunshot itself is
        /// already deafening and the whiz only muddies it.</summary>
        private const float PointBlankMeters = 2f;

        /// <summary>Minimum gap between two whizzes (s) — full auto would otherwise turn into a
        /// continuous buzz that carries no direction.</summary>
        private const float MinIntervalSeconds = 0.06f;

        /// <summary>Volume factor of a pass at the outer radius; a pass through the head is 1.</summary>
        private const float FarVolumeScale = 0.5f;

        private const float PitchJitter = 0.08f;

        /// <summary>Below this the segment is degenerate (round swallowed at the muzzle) — there is
        /// no flight path to pass us.</summary>
        private const float MinShotLengthMeters = 0.2f;

        private const float MaxDistanceMeters = 8f;

        /// <summary>Scheduled-whiz cap. Overflow DROPS the new one (unlike RemoteShotFx's events):
        /// a missing whiz costs nothing, an out-of-order early one lies about the direction.</summary>
        private const int MaxPending = 16;

        /// <summary>A whiz waiting for its tracer to reach the passing point.</summary>
        private struct Pending
        {
            public Vector3 Position;
            public float PlayAt;
            public float Volume;
        }

        private readonly List<Pending> _pending = new List<Pending>(MaxPending);
        private readonly AudioSource[] _pool = new AudioSource[PoolSize];
        private int _nextSource;

        private float _lastScheduledAt = float.NegativeInfinity;

        private static NearMissWhizFx _shared;

        /// <summary>The one instance every remote shot reports to; self-bootstraps on first use.</summary>
        public static NearMissWhizFx Shared
        {
            get
            {
                if (_shared == null)
                {
                    var go = new GameObject("[NearMissWhizFx]");
                    DontDestroyOnLoad(go);
                    _shared = go.AddComponent<NearMissWhizFx>();
                }

                return _shared;
            }
        }

        /// <summary>Reports ONE remote shot as a segment and schedules a whiz if it passed close to
        /// the local head. Silent (and allocation-free) in every reject case.
        /// <para>⚠️ One call per SHOT, not per pellet: a shotgun would otherwise fire nine whizzes on
        /// a single trigger pull. The centre ray is used — it is the only ray that comes from the
        /// wire, the spread is regenerated cosmetics (<c>RemoteShotFx.BuildScatter</c>).</para>
        /// <para>No local head (admin/spectator build, rig not spawned yet) → nothing happens.</para></summary>
        public void Report(Vector3 origin, Vector3 end)
        {
            WeaponCatalog catalog = WeaponCatalog.Load();
            AudioClip[] clips = catalog != null ? catalog.NearMissClips : null;
            if (clips == null || clips.Length == 0)
            {
                return; // not authored → feature is off, no head lookup either
            }

            if (AudioMix.Weapons <= 0f)
            {
                return;
            }

            if (!WeaponGranter.TryResolveHead(out Vector3 head))
            {
                return; // admin/spectator: there is no local player to be shot at
            }

            Vector3 segment = end - origin;
            float segmentSqr = segment.sqrMagnitude;
            if (segmentSqr < MinShotLengthMeters * MinShotLengthMeters)
            {
                return;
            }

            if ((origin - head).sqrMagnitude < PointBlankMeters * PointBlankMeters)
            {
                return;
            }

            if ((end - head).sqrMagnitude < HitProximityMeters * HitProximityMeters)
            {
                return; // the round STOPPED on us: hit feedback owns this one
            }

            float t = Mathf.Clamp01(Vector3.Dot(head - origin, segment) / segmentSqr);
            Vector3 closest = origin + segment * t;

            float missSqr = (head - closest).sqrMagnitude;
            if (missSqr >= NearMissRadiusMeters * NearMissRadiusMeters)
            {
                return;
            }

            // Time.unscaledTime: a paused match must not freeze a whiz mid-flight.
            float travelMeters = (closest - origin).magnitude;
            float playAt = Time.unscaledTime + travelMeters / ShotTracer.TracerSpeedMetersPerSecond;

            // Rate limit on the PLAYBACK stamp, not on arrival: events are scheduled ahead, so
            // spacing the reports would still let two whizzes land on the same frame.
            if (playAt - _lastScheduledAt < MinIntervalSeconds || _pending.Count >= MaxPending)
            {
                return;
            }

            _lastScheduledAt = playAt;

            float miss = Mathf.Sqrt(missSqr);
            float volume = Mathf.Lerp(1f, FarVolumeScale, miss / NearMissRadiusMeters);

            _pending.Add(new Pending
            {
                Position = closest,
                PlayAt = playAt,
                Volume = volume,
            });
        }

        /// <summary>Builds the AudioSource pool (SILENT) so the first whiz of the match pays no
        /// GameObject/AudioSource construction. ⚠️ Does not load the clip DATA — clips with
        /// <c>preloadAudioData</c> off still decode on first play.</summary>
        public void Warmup()
        {
            for (int i = 0; i < PoolSize; i++)
            {
                TakeSource();
            }
        }

        /// <summary>Plays due whizzes and compacts the list in one pass (RemoteShotFx's pattern).</summary>
        private void Update()
        {
            int count = _pending.Count;
            if (count == 0)
            {
                return;
            }

            float now = Time.unscaledTime;

            int write = 0;
            for (int i = 0; i < count; i++)
            {
                Pending whiz = _pending[i];
                if (now >= whiz.PlayAt)
                {
                    Play(whiz);
                    continue;
                }

                if (write != i)
                {
                    _pending[write] = whiz;
                }

                write++;
            }

            _pending.RemoveRange(write, count - write);
        }

        private void Play(in Pending whiz)
        {
            WeaponCatalog catalog = WeaponCatalog.Load();
            AudioClip[] clips = catalog != null ? catalog.NearMissClips : null;
            if (clips == null || clips.Length == 0)
            {
                return;
            }

            AudioClip clip = clips[Random.Range(0, clips.Length)];
            if (clip == null)
            {
                return;
            }

            // Re-read at playback: the player may have muted weapons during the flight.
            float channel = AudioMix.Weapons;
            if (channel <= 0f)
            {
                return;
            }

            AudioSource source = TakeSource();
            if (source == null)
            {
                return;
            }

            source.transform.position = whiz.Position;
            source.pitch = 1f + Random.Range(-PitchJitter, PitchJitter);
            // PlayOneShot, not Play: a recycled source may still be sounding the previous whiz.
            source.PlayOneShot(clip, Mathf.Clamp01(whiz.Volume * catalog.NearMissVolume * channel));
        }

        private AudioSource TakeSource()
        {
            AudioSource source = _pool[_nextSource];
            if (source == null)
            {
                var go = new GameObject("[NearMissWhiz]");
                go.transform.SetParent(transform, false);

                source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                // Full level anywhere inside the near-miss radius: the pass point is at most that
                // far from the ear, so rolloff must not eat the very sound it is meant to sell.
                source.minDistance = NearMissRadiusMeters;
                source.maxDistance = MaxDistanceMeters;

                _pool[_nextSource] = source;
            }

            _nextSource = (_nextSource + 1) % PoolSize;
            return source;
        }

        private void OnDestroy()
        {
            if (_shared == this)
            {
                // Never leave the static field on a destroyed component (domain reload disabled).
                _shared = null;
            }
        }
    }
}
