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

        private void OnTriggerEnter(Collider other)
        {
            NetObject patty = ResolvePatty(other);
            if (patty == null || _inside.ContainsKey(patty))
            {
                return;
            }

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

            if (_startedByMe.Remove(patty.NetId))
            {
                NetObjectSync.SendEvent(patty.NetId, BurgerKinds.EventGrill, new[] { 0 });
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
