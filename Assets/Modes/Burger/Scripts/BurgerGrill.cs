using System;
using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core.World;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Modes.Burger
{
    /// <summary>The grill volume: reports patties entering and leaving with <c>grill</c>
    /// (<c>i:[1]</c> on / <c>i:[0]</c> off, §10.5). Not a network object itself — the doneness
    /// counter is the SERVER's, the grill only says "in" and "out".
    /// <para>⚠️ The report must come from exactly ONE client, and the selector is
    /// <see cref="NetObjectPoseSender.RestSent"/>: the player who PUT the patty on the grill is the one
    /// who measured its stop.</para>
    /// <para>The sizzle and smoke live HERE, not on the patty, and follow LOCAL state (a free patty
    /// inside the volume, phase <c>playing</c>): a sound riding the patty kept sizzling on the board
    /// whenever one "off" report was lost. Shift end fades both out.</para></summary>
    [DisallowMultipleComponent]
    public sealed class BurgerGrill : MonoBehaviour
    {
        [Tooltip("Izgaranın cızırtısı (loop). Boşsa bu objedeki AudioSource aranır; o da yoksa sessizdir.")]
        [SerializeField] private AudioSource sizzleSource;

        [Tooltip("Pişerken tüten duman. Boşsa çocuklardaki ParticleSystem aranır; yoksa dumansızdır.")]
        [SerializeField] private ParticleSystem smoke;

        [Tooltip("Cızırtının açılıp kapanma süresi (sn) — vardiya bitince de bu sürede söner.")]
        [SerializeField] private float fadeSeconds = 1.5f;

        /// <summary>Authored level of <see cref="sizzleSource"/>; the fade runs between 0 and this.</summary>
        private float _sizzleVolume = 1f;

        /// <summary>Phase is <c>playing</c>. True until told otherwise: a serverless sandbox gets no
        /// <c>match_state</c>.</summary>
        private bool _phasePlaying = true;

        /// <summary>Patties currently inside, with the sender we subscribed to (may be null).</summary>
        private readonly Dictionary<NetObject, NetObjectPoseSender> _inside =
            new Dictionary<NetObject, NetObjectPoseSender>();

        /// <summary>Patties whose counter WE started — only those may be stopped by us.</summary>
        private readonly HashSet<int> _startedByMe = new HashSet<int>();

        /// <summary>Stop reports waiting out <see cref="ExitGraceSeconds"/>, keyed by netId.</summary>
        private readonly Dictionary<int, float> _pendingStop = new Dictionary<int, float>();

        /// <summary>⚠️ A resting patty turning kinematic makes PhysX re-filter the pair: exit + enter
        /// in the same step. Stopping on that exit silenced the grill for good, so an exit only
        /// counts if the patty stays out this long.</summary>
        private const float ExitGraceSeconds = 0.25f;

        private void Awake()
        {
            if (sizzleSource == null)
            {
                sizzleSource = GetComponent<AudioSource>();
            }

            if (smoke == null)
            {
                smoke = GetComponentInChildren<ParticleSystem>(true);
            }

            if (sizzleSource != null)
            {
                _sizzleVolume = sizzleSource.volume;
                sizzleSource.loop = true;
                sizzleSource.volume = 0f;
            }
        }

        private void OnEnable()
        {
            NetEvents.OnMatchState += HandleMatchState;
            NetEvents.OnMatchEnd += HandleMatchEnd;
        }

        private void HandleMatchState(MatchStateMsg msg)
        {
            if (msg != null)
            {
                _phasePlaying = string.Equals(msg.phase, ArenaProtocol.PHASE_PLAYING, StringComparison.Ordinal);
            }
        }

        private void HandleMatchEnd(MatchEndMsg msg)
        {
            _phasePlaying = false;
        }

        private void OnTriggerEnter(Collider other)
        {
            NetObject patty = ResolvePatty(other);
            if (patty == null || _inside.ContainsKey(patty))
            {
                return;
            }

            // Back inside before the grace ran out: the counter never stopped.
            _pendingStop.Remove(patty.NetId);

            var sender = patty.GetComponent<NetObjectPoseSender>();
            _inside.Add(patty, sender);

            if (sender != null)
            {
                sender.RestSent += HandleRestSent;
            }
        }

        private void OnTriggerExit(Collider other)
        {
            NetObject patty = ResolvePatty(other);
            if (patty == null || !_inside.TryGetValue(patty, out NetObjectPoseSender sender))
            {
                return;
            }

            _inside.Remove(patty);

            if (sender != null)
            {
                sender.RestSent -= HandleRestSent;
            }

            if (_startedByMe.Contains(patty.NetId))
            {
                _pendingStop[patty.NetId] = Time.time + ExitGraceSeconds;
            }
        }

        private void Update()
        {
            UpdateSizzle();
            FlushPendingStops();
        }

        /// <summary>Fades the sizzle toward "cooking" and runs the smoke with it.</summary>
        private void UpdateSizzle()
        {
            bool cooking = _phasePlaying && AnyPattyCooking();

            if (sizzleSource != null)
            {
                if (cooking && !sizzleSource.isPlaying)
                {
                    sizzleSource.volume = 0f;
                    sizzleSource.Play();
                }

                if (sizzleSource.isPlaying)
                {
                    float step = fadeSeconds > 0f ? _sizzleVolume / fadeSeconds * Time.deltaTime : 1f;
                    sizzleSource.volume = Mathf.MoveTowards(sizzleSource.volume, cooking ? _sizzleVolume : 0f, step);
                    if (!cooking && sizzleSource.volume <= 0f)
                    {
                        sizzleSource.Stop();
                    }
                }
            }

            if (smoke != null)
            {
                if (cooking && !smoke.isEmitting)
                {
                    smoke.Play(true);
                }
                else if (!cooking && smoke.isEmitting)
                {
                    // Emission stops, puffs already out finish their life — a fade, not a cut.
                    smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }
        }

        /// <summary>A free patty lies inside. ⚠️ Despawned/pooled patties never raise OnTriggerExit,
        /// so dead entries are dropped here.</summary>
        private bool AnyPattyCooking()
        {
            if (_inside.Count == 0)
            {
                return false;
            }

            bool cooking = false;
            List<NetObject> dead = null;
            foreach (KeyValuePair<NetObject, NetObjectPoseSender> entry in _inside)
            {
                NetObject patty = entry.Key;
                if (patty == null || !patty.gameObject.activeInHierarchy)
                {
                    (dead ??= new List<NetObject>()).Add(patty);
                    continue;
                }

                if (!patty.IsHeld)
                {
                    cooking = true;
                }
            }

            if (dead != null)
            {
                for (int i = 0; i < dead.Count; i++)
                {
                    NetObject patty = dead[i];
                    if (_inside.TryGetValue(patty, out NetObjectPoseSender sender) && sender != null)
                    {
                        sender.RestSent -= HandleRestSent;
                    }

                    _inside.Remove(patty);
                }
            }

            return cooking;
        }

        private void FlushPendingStops()
        {
            if (_pendingStop.Count == 0)
            {
                return;
            }

            float now = Time.time;
            List<int> due = null;
            foreach (KeyValuePair<int, float> entry in _pendingStop)
            {
                if (now >= entry.Value)
                {
                    (due ??= new List<int>()).Add(entry.Key);
                }
            }

            if (due == null)
            {
                return;
            }

            for (int i = 0; i < due.Count; i++)
            {
                int netId = due[i];
                _pendingStop.Remove(netId);
                if (_startedByMe.Remove(netId))
                {
                    NetObjectSync.SendEvent(netId, BurgerKinds.EventGrill, new[] { 0 });
                }
            }
        }

        /// <summary>We brought this patty to rest. Still inside = it came to rest ON the grill.</summary>
        private void HandleRestSent(NetObject patty)
        {
            if (patty == null || patty.NetId <= 0 || !_inside.ContainsKey(patty))
            {
                return;
            }

            // On EVERY rest, not once: the server drops a held patty off the grill, and a patty
            // grabbed and put back inside the exit grace never sent its "off" (server is idempotent).
            _startedByMe.Add(patty.NetId);
            _pendingStop.Remove(patty.NetId);
            NetObjectSync.SendEvent(patty.NetId, BurgerKinds.EventGrill, new[] { 1 });
        }

        private void OnDisable()
        {
            NetEvents.OnMatchState -= HandleMatchState;
            NetEvents.OnMatchEnd -= HandleMatchEnd;

            if (sizzleSource != null)
            {
                sizzleSource.Stop();
            }

            // ⚠️ A leftover subscription would keep reporting into a grill that is no longer in play.
            foreach (KeyValuePair<NetObject, NetObjectPoseSender> entry in _inside)
            {
                if (entry.Value != null)
                {
                    entry.Value.RestSent -= HandleRestSent;
                }
            }

            _inside.Clear();
            _startedByMe.Clear();
            _pendingStop.Clear();
        }

        private static NetObject ResolvePatty(Collider other)
        {
            NetObject net = other != null ? other.GetComponentInParent<NetObject>() : null;
            if (net == null || net.NetId <= 0 || net.Kind == null || net.Kind.Kind != BurgerKinds.Patty)
            {
                return null;
            }

            return net;
        }
    }
}
