using System;
using UnityEngine;

namespace VortexArena.Modes.Zombie
{
    /// <summary>Which wave opens which weapon on the zombie boards — and the wrist bomb: <c>weaponId</c> /
    /// <c>throwableId</c> → wave number, plus the mode's OWN damage numbers
    /// (<see cref="TryGetDamage"/>, installed by <see cref="ZombieDamageTuning"/>).</summary>
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

            [Tooltip("YALNIZ zombi modunda geçerli gövde hasarı. 0 = üstüne yazma, " +
                     "WeaponDefinition.damage kullanılır. Saçmalı silahta SAÇMA BAŞINA.")]
            public float bodyDamage;

            [Tooltip("YALNIZ zombi modunda geçerli kafa çarpanı. 0 = üstüne yazma, " +
                     "WeaponDefinition.headshotMultiplier kullanılır.")]
            public float headMultiplier;
        }

        [Tooltip("Silah → açılış dalgası. Listede olmayan silah AÇIK sayılır.")]
        [SerializeField] private Entry[] entries = DefaultEntries();

        /// <summary>Built-in table, used when no asset has been created yet (and as the asset's own
        /// starting content). ⚠️ Kept so a fresh clone plays the mode correctly without a data step;
        /// the asset, once present, is the only source.
        /// <para>Each wave adds a ROLE (Yemek-Kitabi zombie recipe): sidearms, close defence, first
        /// rifles + bomb, shotgun + brute hunter, precision, top tier.</para>
        /// <para>⚠️ Damage ceiling: against a 100 HP walker the strongest pair (ak47, magnum) needs TWO
        /// headshots and everything else three or more — nothing one-shots a head, or the wave
        /// progression stops being felt.</para></summary>
        public static Entry[] DefaultEntries() => new[]
        {
            new Entry { weaponId = "glock", wave = 1, bodyDamage = 20f, headMultiplier = 1.75f },
            new Entry { weaponId = "magnum", wave = 1, bodyDamage = 30f, headMultiplier = 1.9f },
            new Entry { weaponId = "g36c", wave = 2, bodyDamage = 22f, headMultiplier = 1.6f },
            new Entry { weaponId = "ump45", wave = 2, bodyDamage = 24f, headMultiplier = 1.5f },
            new Entry { weaponId = "scarl", wave = 3, bodyDamage = 26f, headMultiplier = 1.65f },
            new Entry { weaponId = "famas", wave = 3, bodyDamage = 24f, headMultiplier = 1.6f },
            new Entry { weaponId = "bomba", wave = 3 },
            new Entry { weaponId = "ak47", wave = 4, bodyDamage = 32f, headMultiplier = 1.7f },
            new Entry { weaponId = "nova", wave = 4, bodyDamage = 10f, headMultiplier = 1.5f },
            new Entry { weaponId = "m4a4", wave = 5, bodyDamage = 28f, headMultiplier = 1.65f },
            new Entry { weaponId = "xm1014", wave = 5, bodyDamage = 11f, headMultiplier = 1.5f },
            new Entry { weaponId = "aug", wave = 6, bodyDamage = 28f, headMultiplier = 1.7f },
            new Entry { weaponId = "p90", wave = 6, bodyDamage = 20f, headMultiplier = 1.6f },
            new Entry { weaponId = "m4a1", wave = 7, bodyDamage = 29f, headMultiplier = 1.7f },
            new Entry { weaponId = "mp9", wave = 7, bodyDamage = 18f, headMultiplier = 1.6f },
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

        /// <summary>Whether any row opens exactly at <paramref name="wave"/> — read from the table, so it
        /// also answers on the admin, which has no board views.</summary>
        public bool OpensAt(int wave)
        {
            if (entries == null)
            {
                return false;
            }

            for (int i = 0; i < entries.Length; i++)
            {
                if (Mathf.Max(1, entries[i].wave) == wave)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Zombie-only damage of <paramref name="weaponId"/>; <c>false</c> when the row is
        /// missing or leaves both numbers at 0 (= the WeaponDefinition's own values stand).</summary>
        public bool TryGetDamage(string weaponId, out float bodyDamage, out float headMultiplier)
        {
            bodyDamage = 0f;
            headMultiplier = 0f;

            if (string.IsNullOrEmpty(weaponId) || entries == null)
            {
                return false;
            }

            for (int i = 0; i < entries.Length; i++)
            {
                if (!string.Equals(entries[i].weaponId, weaponId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bodyDamage = Mathf.Max(0f, entries[i].bodyDamage);
                headMultiplier = Mathf.Max(0f, entries[i].headMultiplier);
                return bodyDamage > 0f || headMultiplier > 0f;
            }

            return false;
        }
    }
}
