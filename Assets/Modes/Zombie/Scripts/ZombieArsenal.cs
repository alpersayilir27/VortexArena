using System;
using UnityEngine;

namespace VortexArena.Modes.Zombie
{
    /// <summary>Which wave opens which weapon on the zombie boards — and the wrist bomb: <c>weaponId</c> /
    /// <c>throwableId</c> → wave number, plus the mode's OWN damage, reload-time and dry-click numbers
    /// (installed by <see cref="ZombieDamageTuning"/>).</summary>
    /// <remarks>Lives in an asset so balancing is a data edit, not a code edit — see
    /// <c>Docs/Gelistirici/Yemek-Kitabi.md</c> for the recipe.
    /// <para>⚠️ The table IS the zombie roster: a weapon it does not list is not part of the mode —
    /// its board slot is hidden and the selection gate refuses it (<see cref="IsListed"/>).</para>
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

        [Tooltip("Silah → açılış dalgası. Listede olmayan silah zombi modunda YOKTUR: panodaki " +
                 "yuvası gizlenir, seçilemez.")]
        [SerializeField] private Entry[] entries = DefaultEntries();

        [Range(0.3f, 1f)]
        [Tooltip("YALNIZ zombi modunda şarjör değiştirme süresi çarpanı (WeaponDefinition.reloadTime × " +
                 "bu). 1 = PvP ile aynı.")]
        [SerializeField] private float reloadTimeScale = 0.8f;

        [Range(0f, 3f)]
        [Tooltip("YALNIZ zombi modunda boş tetik sesine ek çarpan (WeaponDefinition.dryFireVolume × bu) " +
                 "— zombi kalabalığının içinde de duyulmalı.")]
        [SerializeField] private float dryFireVolumeScale = 1.5f;

        /// <summary>Zombie-only multiplier on <c>WeaponDefinition.ReloadTime</c>.</summary>
        public float ReloadTimeScale => Mathf.Clamp(reloadTimeScale, 0.3f, 1f);

        /// <summary>Zombie-only multiplier on <c>WeaponDefinition.DryFireVolume</c>.</summary>
        public float DryFireVolumeScale => Mathf.Max(0f, dryFireVolumeScale);

        /// <summary>Built-in table, used when no asset has been created yet (and as the asset's own
        /// starting content). ⚠️ Kept so a fresh clone plays the mode correctly without a data step;
        /// the asset, once present, is the only source.
        /// <para>One weapon per wave, in the order the squad meets them.</para>
        /// <para>⚠️ Head ladder against a 100 HP walker: light weapons ~50 per head (TWO headshots),
        /// magnum, ak47 and the m4a4/aug rifles ~101 (ONE). Progression is felt as fewer shots per head;
        /// a value between the two steps blurs both.</para></summary>
        public static Entry[] DefaultEntries() => new[]
        {
            new Entry { weaponId = "magnum", wave = 1, bodyDamage = 30f, headMultiplier = 3.4f },
            new Entry { weaponId = "famas", wave = 2, bodyDamage = 24f, headMultiplier = 2.1f },
            new Entry { weaponId = "p90", wave = 3, bodyDamage = 20f, headMultiplier = 2.5f },
            new Entry { weaponId = "bomba", wave = 3 },
            new Entry { weaponId = "ak47", wave = 4, bodyDamage = 32f, headMultiplier = 3.2f },
            new Entry { weaponId = "xm1014", wave = 5, bodyDamage = 11f, headMultiplier = 2.0f },
            new Entry { weaponId = "aug", wave = 6, bodyDamage = 28f, headMultiplier = 3.6f },
            new Entry { weaponId = "m4a4", wave = 7, bodyDamage = 28f, headMultiplier = 3.6f },
            new Entry { weaponId = "nova", wave = 8, bodyDamage = 10f, headMultiplier = 2.0f },
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

        /// <summary>Is <paramref name="weaponId"/> part of the zombie roster at all.</summary>
        public bool IsListed(string weaponId) => IndexOf(weaponId) >= 0;

        /// <summary>The wave <paramref name="weaponId"/> opens at; 1 when unlisted (callers check
        /// <see cref="IsListed"/> first).</summary>
        public int UnlockWave(string weaponId)
        {
            int index = IndexOf(weaponId);
            return index >= 0 ? Mathf.Max(1, entries[index].wave) : 1;
        }

        private int IndexOf(string weaponId)
        {
            if (string.IsNullOrEmpty(weaponId) || entries == null)
            {
                return -1;
            }

            for (int i = 0; i < entries.Length; i++)
            {
                if (string.Equals(entries[i].weaponId, weaponId, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
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
