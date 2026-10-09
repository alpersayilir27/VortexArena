using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Core.Player
{
    /// <summary>Which <c>WorldSingle</c> network object a remote player holds in each hand.</summary>
    /// <remarks>
    /// Its wire byte is 0 (§6.6), so <c>NetObjectGrabBridge</c> reports it here and
    /// <see cref="RemoteHandPoser"/> seats the drawn hand on it like a byte-driven item.
    /// <para>⚠️ Heartbeat: an entry counts only in the frame it was reported, so release/owner
    /// change/carrier hand-off need no exit path. Reporter order 0, reader 30050.</para>
    /// </remarks>
    public static class RemoteWorldHolds
    {
        private struct Entry
        {
            public Transform Item;
            public ItemDefinition Definition;
            public int Frame;
        }

        private static readonly Dictionary<long, Entry> Entries = new Dictionary<long, Entry>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Entries.Clear();
        }

        /// <summary>This frame, <paramref name="playerId"/>'s hand holds <paramref name="item"/>.</summary>
        public static void Report(int playerId, bool rightHand, Transform item, ItemDefinition definition)
        {
            Entries[Key(playerId, rightHand)] = new Entry
            {
                Item = item,
                Definition = definition,
                Frame = Time.frameCount,
            };
        }

        /// <summary>The world object reported for that hand THIS frame, or <c>false</c>.</summary>
        public static bool TryGet(int playerId, bool rightHand, out Transform item,
            out ItemDefinition definition)
        {
            if (Entries.TryGetValue(Key(playerId, rightHand), out Entry entry) &&
                entry.Frame == Time.frameCount && entry.Item != null && entry.Definition != null)
            {
                item = entry.Item;
                definition = entry.Definition;
                return true;
            }

            item = null;
            definition = null;
            return false;
        }

        private static long Key(int playerId, bool rightHand)
        {
            return ((long)playerId << 1) | (rightHand ? 1L : 0L);
        }
    }
}
