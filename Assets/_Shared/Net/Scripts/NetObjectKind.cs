using System;
using System.Collections.Generic;
using UnityEngine;
using VortexArena.Protocol;

namespace VortexArena.Net
{
    /// <summary>Rules of a network object KIND (§10.10): the wire kind id, its max health, whether it can
    /// be picked up, which events it accepts and what its kind-specific flag bits are called.
    /// <para>The kind lives here, the IDENTITY list lives in the scene (<see cref="NetIdentity"/>):
    /// one kind is used across ten arenas, so its rules must be editable in ONE place.</para>
    /// <para>⚠️ <see cref="Kind"/> is carried on the wire and exported to the server — the string must
    /// match EXACTLY; a drift means the server drops the object from its table.</para>
    /// <para>⚠️ <b>No prefab reference here.</b> The <c>kind</c> → prefab mapping of a dynamically
    /// spawned object lives in the spawn catalogue on the Core side; this asmdef cannot see content.</para></summary>
    [CreateAssetMenu(fileName = "NetObjectKind", menuName = "VortexArena/Net Object Kind")]
    public class NetObjectKind : ScriptableObject
    {
        /// <summary>First bit a kind may name: bit0..bit3 are the core contract (§10.10) and never
        /// appear in <see cref="flagNames"/>.</summary>
        public const int FIRST_KIND_FLAG_BIT = 4;

        [Tooltip("Telde taşınan tür kimliği (maps.json kinds[] ile BİREBİR aynı olmalı).")]
        [SerializeField] private string kind = "";

        [Tooltip("Azami can. 0 = hasar almaz (kimliği olan ama canı olmayan dekoratif ağ nesnesi).")]
        [SerializeField] private float maxHp = 0f;

        [Tooltip("Alınabilir mi: alınamaz / boştaki objeyi ilk isteyen alır.")]
        [SerializeField] private NetObjectGrab grab = NetObjectGrab.None;

        [Tooltip("Sunucunun kabul ettiği olay listesi. BOŞ = bu türde hiçbir object_event kabul edilmez.")]
        [SerializeField] private NetObjectEventRule[] events = Array.Empty<NetObjectEventRule>();

        [Tooltip("Türe özel bayrak adları; listedeki ilk ad bit4'tür. Çekirdek bit0-3 (Held/Broken/" +
                 "Awake/HeldRight) buraya YAZILMAZ.")]
        [SerializeField] private string[] flagNames = Array.Empty<string>();

        public string Kind => kind;

        public float MaxHp => Mathf.Max(0f, maxHp);

        /// <summary>Can this kind take damage at all (§10.10: <c>maxHp == 0</c> = it cannot).</summary>
        public bool IsDamageable => MaxHp > 0f;

        public NetObjectGrab Grab => grab;

        public bool IsGrabbable => grab != NetObjectGrab.None;

        /// <summary>Wire value of <see cref="Grab"/> (<c>ArenaProtocol.OBJECT_GRAB_*</c>).</summary>
        public string WireGrab => grab == NetObjectGrab.Anyone
            ? ArenaProtocol.OBJECT_GRAB_ANYONE
            : ArenaProtocol.OBJECT_GRAB_NONE;

        /// <summary>Allowed events (read-only); empty = the kind accepts none.</summary>
        public IReadOnlyList<NetObjectEventRule> Events => events ?? Array.Empty<NetObjectEventRule>();

        /// <summary>Kind-specific flag names in bit order, starting at
        /// <see cref="FIRST_KIND_FLAG_BIT"/> (read-only).</summary>
        public IReadOnlyList<string> FlagNames => flagNames ?? Array.Empty<string>();

        /// <summary>Bit index of a kind-specific flag (§10.10 bit4+); false when the kind does not name
        /// it. Presentation reads a bit BY NAME — a bit number written into a consumer is exactly the
        /// shift that does not fail, it just draws the wrong thing.</summary>
        public bool TryGetFlagBit(string flagName, out int bit)
        {
            bit = 0;
            if (string.IsNullOrEmpty(flagName) || flagNames == null)
            {
                return false;
            }

            for (int i = 0; i < flagNames.Length; i++)
            {
                if (!string.Equals(flagNames[i], flagName, StringComparison.Ordinal))
                {
                    continue;
                }

                int candidate = FIRST_KIND_FLAG_BIT + i;
                if (candidate > 31)
                {
                    // flags is an int on the wire; a name past bit31 has no bit at all.
                    Debug.LogError($"[NetObjectKind] '{kind}' türünde '{flagName}' bayrağı bit {candidate}'e " +
                                   "düşüyor — flags 32 bit. Bayrak listesini kısaltın.", this);
                    return false;
                }

                bit = candidate;
                return true;
            }

            return false;
        }

        /// <summary>Mask of a kind-specific flag; false when the kind does not name it.</summary>
        public bool TryGetFlagMask(string flagName, out int mask)
        {
            if (TryGetFlagBit(flagName, out int bit))
            {
                mask = 1 << bit;
                return true;
            }

            mask = 0;
            return false;
        }

        /// <summary>Rule of an event name; null when the kind does not accept it.</summary>
        public NetObjectEventRule FindEvent(string eventName)
        {
            if (string.IsNullOrEmpty(eventName) || events == null)
            {
                return null;
            }

            for (int i = 0; i < events.Length; i++)
            {
                NetObjectEventRule rule = events[i];
                if (rule != null && string.Equals(rule.Name, eventName, StringComparison.Ordinal))
                {
                    return rule;
                }
            }

            return null;
        }
    }
}
