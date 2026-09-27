using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core.World;
using VortexArena.Net;

namespace VortexArena.Modes.Burger
{
    /// <summary>The grill volume: reports patties entering and leaving with <c>grill</c>
    /// (<c>i:[1]</c> start / <c>i:[0]</c> stop, §10.5). Not a network object itself — the doneness
    /// counter is the SERVER's, the grill only says "in" and "out".
    /// <para>⚠️ The report must come from exactly ONE client, and the selector is
    /// <see cref="NetObjectPoseSender.RestSent"/>: the player who PUT the patty on the grill is the one
    /// who measured its stop. If everyone reported, the server would start the same counter N
    /// times.</para></summary>
    [DisallowMultipleComponent]
    public sealed class BurgerGrill : MonoBehaviour
    {
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

            if (!_startedByMe.Add(patty.NetId))
            {
                return;
            }

            NetObjectSync.SendEvent(patty.NetId, BurgerKinds.EventGrill, new[] { 1 });
        }

        private void OnDisable()
        {
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
