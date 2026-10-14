using System;
using UnityEngine;

namespace VortexArena.Modes.Zombie
{
    /// <summary>Which wave opens which weapon on the zombie boards — and the wrist bomb: <c>weaponId</c> /
    /// <c>throwableId</c> → wave number.</summary>
    /// <remarks>Lives in an asset so balancing is a data edit, not a code edit — see
    /// <c>Docs/Gelistirici/Yemek-Kitabi.md</c> for the recipe.
    /// <para>⚠️ A weapon the table does not list is UNLOCKED. The safe direction: a board carrying a
    /// weapon nobody remembered to add would otherwise stay locked for the whole run, and a locked
    /// board reads as broken rather than as missing data.</para>
    /// <para>⚠️ Progression is CLIENT-side only: the server validates no weapon
    /// (<c>Docs/ArenaNet-Protokol.md</c> §10.5), so this table changes no protocol and needs no server
    /// deploy.</para></remarks>
    public class ZombieArsenal : ScriptableObject
    {
        /// <summary><c>Resources</c> path of the live asset (without extension).</summary>
        public const string ResourcePath = "Zombie/ZombieArsenal";

        [Serializable]
        public struct Entry
        {
            [Tooltip("WeaponDefinition.weaponId ya da ThrowableDefinition.throwableId — birebir aynı " +
                     "yazılır (ak47, ump45, bomba …).")]
            public string weaponId;

            [Tooltip("Silahın açıldığı dalga. 1 = maçın başından itibaren açık.")]
            public int wave;
        }

        [Tooltip("Silah → açılış dalgası. Listede olmayan silah AÇIK sayılır.")]
        [SerializeField] private Entry[] entries = DefaultEntries();

        /// <summary>Built-in table, used when no asset has been created yet (and as the asset's own
        /// starting content). ⚠️ Kept so a fresh clone plays the mode correctly without a data step;
        /// the asset, once present, is the only source.
        /// <para>Each wave adds a ROLE (Yemek-Kitabi zombie recipe): close defence, shotgun + big
        /// magazine, first rifles + bomb, precision, brute hunters, top tier.</para></summary>
        public static Entry[] DefaultEntries() => new[]
        {
            new Entry { weaponId = "mp9", wave = 1 },
            new Entry { weaponId = "ump45", wave = 1 },
            new Entry { weaponId = "nova", wave = 2 },
            new Entry { weaponId = "p90", wave = 2 },
            new Entry { weaponId = "famas", wave = 3 },
            new Entry { weaponId = "scarl", wave = 3 },
            new Entry { weaponId = "bomba", wave = 3 },
            new Entry { weaponId = "m4a1", wave = 4 },
            new Entry { weaponId = "g36c", wave = 4 },
            new Entry { weaponId = "m4a4", wave = 4 },
            new Entry { weaponId = "xm1014", wave = 5 },
            new Entry { weaponId = "ak47", wave = 5 },
            new Entry { weaponId = "galilar", wave = 6 },
            new Entry { weaponId = "aug", wave = 6 },
        };

        private static ZombieArsenal live;

        /// <summary>The table in use: the <c>Resources</c> asset when it exists, otherwise an in-memory
        /// copy of <see cref="DefaultEntries"/>.</summary>
        /// <remarks>⚠️ No asset is WRITTEN at runtime (a build cannot) and none is demanded either —
        /// the fallback keeps the mode playable while the asset is created by
        /// <i>Tools &gt; VortexArena &gt; Zombie &gt; Cephanelik tablosunu oluştur</i>.</remarks>
        public static ZombieArsenal Live
        {
            get
            {
                if (live != null)
                {
                    return live;
                }

                live = Resources.Load<ZombieArsenal>(ResourcePath);
                if (live == null)
                {
                    live = CreateInstance<ZombieArsenal>();
                    live.name = "ZombieArsenal (gömülü)";
                }

                return live;
            }
        }

        /// <summary>The wave <paramref name="weaponId"/> opens at; 1 (= always open) when unlisted.</summary>
        public int UnlockWave(string weaponId)
        {
            if (string.IsNullOrEmpty(weaponId) || entries == null)
            {
                return 1;
            }

            for (int i = 0; i < entries.Length; i++)
            {
                if (string.Equals(entries[i].weaponId, weaponId, StringComparison.OrdinalIgnoreCase))
                {
                    return Mathf.Max(1, entries[i].wave);
                }
            }

            return 1;
        }
    }
}
