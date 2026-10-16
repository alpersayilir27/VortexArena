using System;
using UnityEngine;
using VortexArena.Net;

namespace VortexArena.Modes.Zombie
{
    /// <summary>The client's ONE vocabulary for the Zombie mode: kind names, event name, payload keys,
    /// <c>modeState</c> keys and the numbers the CLIENT owns.
    /// <para>⚠️ Mirrors the <c>zombie</c> table of <c>Docs/ArenaNet-Protokol.md</c> §10.5 one to one. A
    /// literal typed at a call site instead of a constant here is silently rejected by the server
    /// (<c>kinds[].events[]</c> validation) and looks like "the zombie never hits".</para></summary>
    internal static class ZombieKinds
    {
        // ------------------------------------------------------------------- kinds + event

        public const string Zombie = "zombie";

        /// <summary>Scene-baked spawn marker: no visual, no health — only its <c>netId</c> matters, and it
        /// travels to the brain inside the zombie's payload.</summary>
        public const string SpawnPoint = "zombie_spawn";

        public const string EventAttack = "attack";

        // ------------------------------------------------------------------- payload keys (§10.10 `s`)

        /// <summary>Spawn point's <c>netId</c> — the brain resolves the transform itself, because the
        /// server knows no metres and the spawn pose on the wire is meaningless.</summary>
        public const string PayloadSpawn = "sp";

        /// <summary>Wave this zombie belongs to; drives its SPEED here and its damage on the server.</summary>
        public const string PayloadWave = "w";

        /// <summary>Accepted attack counter. ⚠️ The attack animation plays on its CHANGE, not on the
        /// event: the event is a request, this is the server's answer, and only the answer is on every
        /// headset.</summary>
        public const string PayloadAttack = "a";

        /// <summary>Zombie type (<c>walker|crawler|runner|brute|screamer|dev</c>); MISSING = walker.
        /// ⚠️ The type is the SERVER's choice, not a client hash: the wave table decides how many of each
        /// come out, and both the brain and the body must read the same answer.</summary>
        public const string PayloadType = "t";

        /// <summary>Scream counter of a screamer/the Dev — the scream plays on its CHANGE, like the
        /// attack counter.</summary>
        public const string PayloadScream = "c";

        /// <summary>The Dev's <c>netId</c> on a walker it summoned: that walker is placed next to the
        /// Dev instead of at a spawn door.</summary>
        public const string PayloadSummoner = "n";

        /// <summary>Crippled (<c>cr:1</c>): the leg is gone and the body crawls from here on.
        /// ⚠️ IRREVERSIBLE and hp-neutral — the server never clears it, so the client may treat the
        /// transition as one-way and needs no "healed" branch.</summary>
        public const string PayloadCrippled = "cr";

        // ------------------------------------------------------------------- types

        public const string TypeWalker = "walker";

        public const string TypeCrawler = "crawler";

        public const string TypeRunner = "runner";

        public const string TypeBrute = "brute";

        public const string TypeScreamer = "screamer";

        /// <summary>The run's single boss.</summary>
        public const string TypeDev = "dev";

        // ------------------------------------------------------------------- modeState keys (§10.5)

        public const string StateWave = "w";

        /// <summary>Zombies standing right now.</summary>
        public const string StateLeft = "left";

        /// <summary>Seconds to the next wave; <c>0</c> = the wave is running.</summary>
        public const string StateNext = "next";

        /// <summary>Zombies of the running wave the server has not spawned yet; <c>0</c> = the wave is
        /// fully out. The brain holds the empty storeys only while this is above zero.</summary>
        public const string StateRemaining = "rem";

        /// <summary>Empty-storey minimum: how many zombies may wait on a storey with no player
        /// (difficulty dependent).</summary>
        public const string StateFloorMin = "fm";

        /// <summary>Difficulty the admin picked: <c>easy|normal|hard</c>.</summary>
        public const string StateDifficulty = "diff";

        /// <summary>Present only when the run is over: <c>over:survived|rescued|lost</c>.</summary>
        public const string StateOver = "over";

        public const string OverLost = "lost";

        /// <summary>Clock ran out with the squad standing.</summary>
        public const string OverSurvived = "survived";

        /// <summary>Last Stand's Giant went down — also a win, but a different sentence.</summary>
        public const string OverRescued = "rescued";

        /// <summary>Per-player final tallies: <c>zs&lt;playerId&gt;:&lt;headshots&gt;/&lt;revives&gt;</c>,
        /// published only in the final state. ⚠️ The id is part of the KEY (one token per player), so
        /// the parser matches on this PREFIX, not on a fixed key.</summary>
        public const string StatePlayerStatsPrefix = "zs";

        /// <summary>Final stand ("Son Direniş") is on: <c>fs:0|1</c>. While it runs <c>next</c> and
        /// <c>rem</c> are <c>0</c>.</summary>
        public const string StateFinalStand = "fs";

        /// <summary>The Dev: <c>dev:&lt;netId&gt;/&lt;maxHp&gt;</c>, present from its spawn to the end of
        /// the match. ⚠️ Its max hp comes from HERE, not from the object: the boss bar must survive the
        /// headset joining after the Dev spawned.</summary>
        public const string StateDev = "dev";

        /// <summary>Special wave in progress: <c>wx:swarm|heavy|scream</c>; absent on a normal wave.</summary>
        public const string StateSpecial = "wx";

        /// <summary>Clear phase of the wave gap: <c>clr:&lt;kalan sn&gt;</c>, written only while it runs.
        /// ⚠️ <c>next</c> CONTAINS it (it is the first part of the same gap), so the two are not added.</summary>
        public const string StateClear = "clr";

        public const string SpecialSwarm = "swarm";

        public const string SpecialHeavy = "heavy";

        public const string SpecialScream = "scream";

        // ------------------------------------------------------------------- client-side numbers

        /// <summary>Attack reach (m, horizontal) of a normal body. The server only applies a LOOSER bound
        /// (<c>2.5 m</c>, <c>3 m</c> for the Dev, §10.5): deciding the moment is the client's, both poses
        /// are local there.</summary>
        public const float AttackReach = 1.2f;

        private const float BruteAttackReach = 1.5f;

        /// <summary>⚠️ Stays under the server's <c>3 m</c> bound: a longer arm here is an attack the
        /// server drops silently.</summary>
        private const float DevAttackReach = 2f;

        /// <summary>Must mirror the server's cooldown, or every second attack is rejected silently and
        /// the zombie looks like it is swinging at nothing.</summary>
        public const float AttackCooldownSeconds = 1.5f;

        private const float DevAttackCooldownSeconds = 2f;

        /// <summary>Walking speed (m/s) per wave: <c>Base + PerWave × (wave − 1)</c>, capped. Speed is
        /// CLIENT-side: the server has no metres (§10.10).</summary>
        public const float SpeedBase = 0.7f;

        public const float SpeedPerWave = 0.1f;

        public const float SpeedCap = 1.2f;

        public static float SpeedOfWave(int wave) =>
            Mathf.Min(SpeedCap, SpeedBase + SpeedPerWave * (Mathf.Max(1, wave) - 1));

        // ------------------------------------------------------------------- per-type numbers

        /// <summary>Speed scale of a crawler: it drags itself, and at wave speed the crawl cycle would
        /// skate.</summary>
        public const float CrawlerSpeedScale = 0.45f;

        /// <summary>The runner always walks at the cap; the brute and the Dev are fixed and slow (their
        /// threat is reach and health), and the screamer lags the pack it is driving.</summary>
        private const float BruteSpeed = 0.6f;

        private const float DevSpeed = 0.7f;

        private const float ScreamerSpeedScale = 0.9f;

        /// <summary>Body size per type; the others keep the per-<c>netId</c> jitter.</summary>
        private const float BruteScale = 1.3f;

        private const float DevScale = 1.6f;

        // Look indices — ⚠️ matches ZombieLookBuilder.LookNames order.
        private const int LookGhoul = 0;
        private const int LookCop = 1;
        private const int LookSoldier = 2;
        private const int LookClown = 3;
        private const int LookGirl = 4;

        /// <summary>Type of this zombie; a missing key is a walker (the server omits it for the common
        /// case).</summary>
        public static string TypeOf(NetObject netObject)
        {
            if (netObject == null || !netObject.TryGetPayloadValue(PayloadType, out string raw) ||
                raw.Length == 0)
            {
                return TypeWalker;
            }

            return raw;
        }

        public static bool IsCrawlerType(string type) =>
            string.Equals(type, TypeCrawler, StringComparison.Ordinal);

        /// <summary>Walking speed (m/s) of a type in <paramref name="wave"/>, never above the cap: the
        /// view stretches the clip to this value, and anything faster skates.</summary>
        public static float SpeedOf(string type, int wave)
        {
            float wavePace = SpeedOfWave(wave);

            float speed = type switch
            {
                TypeCrawler => wavePace * CrawlerSpeedScale,
                TypeRunner => SpeedCap,
                TypeBrute => BruteSpeed,
                TypeScreamer => wavePace * ScreamerSpeedScale,
                TypeDev => DevSpeed,
                _ => wavePace,
            };

            return Mathf.Min(SpeedCap, speed);
        }

        public static float ReachOf(string type) => type switch
        {
            TypeBrute => BruteAttackReach,
            TypeDev => DevAttackReach,
            _ => AttackReach,
        };

        public static float CooldownOf(string type) =>
            string.Equals(type, TypeDev, StringComparison.Ordinal)
                ? DevAttackCooldownSeconds
                : AttackCooldownSeconds;

        /// <summary>Body scale of a type; the sizeless types keep the <c>netId</c> jitter so a wave does
        /// not read as one model repeated.</summary>
        public static float ScaleOf(string type, int netId) => type switch
        {
            TypeBrute => BruteScale,
            TypeDev => DevScale,
            _ => ScaleJitter(netId),
        };

        /// <summary>Eye glow of a type — the ONE cue that reads across a dark arena, before the body's
        /// silhouette does.</summary>
        public static Color EyeColorOf(string type) => type switch
        {
            TypeRunner => new Color(1.6f, 0.12f, 0.1f, 1f),
            TypeBrute => new Color(1.6f, 0.55f, 0.08f, 1f),
            TypeScreamer => new Color(1.1f, 0.25f, 1.6f, 1f),
            TypeDev => new Color(1.6f, 1.6f, 1.6f, 1f),
            _ => new Color(1f, 0.824f, 0.29f, 1f),
        };

        /// <summary>Look prefab index for a type: the special types are RECOGNISABLE bodies, the walker
        /// and the crawler keep drawing from the two civilian looks.</summary>
        public static int LookIndexOf(string type, int netId, int lookCount)
        {
            if (lookCount <= 0)
            {
                return 0;
            }

            int index = type switch
            {
                TypeRunner => LookGirl,
                TypeBrute => LookSoldier,
                TypeScreamer => LookClown,
                TypeDev => LookSoldier,
                _ => StableHash(netId, SaltLook) % 2 == 0 ? LookGhoul : LookCop,
            };

            // A shorter list must not throw: the builder may not have produced every look yet.
            return Mathf.Clamp(index, 0, lookCount - 1);
        }

        // ------------------------------------------------------------------- deterministic variety

        // Salts: every deterministic choice draws from its OWN stream. Reusing one hash would correlate
        // the picks (every runner the same gait) and that reads as a bug in the field.
        private const int SaltLook = 0;
        private const int SaltLocoSet = 211;
        private const int SaltScale = 307;

        /// <summary>Stable hash of a <c>netId</c>, non-negative.</summary>
        /// <remarks>⚠️ Hand-rolled FNV-1a, NOT <c>GetHashCode</c>: the look, the gait and the size must
        /// come out IDENTICAL on every headset, and the runtime's hash is only guaranteed stable within
        /// one process. A mismatch here is a zombie that looks different on every visor, with no network
        /// field to blame.</remarks>
        public static int StableHash(int netId, int salt)
        {
            uint hash = 2166136261u;
            uint value = (uint)netId ^ (uint)(salt * 0x9E3779B9);

            for (int i = 0; i < 4; i++)
            {
                hash ^= (value >> (i * 8)) & 0xFFu;
                hash *= 16777619u;
            }

            return (int)(hash & 0x7FFFFFFF);
        }

        /// <summary>Locomotion clip set (0..2) — which idle/walk/run trio this body uses.</summary>
        public static int LocoSet(int netId) => StableHash(netId, SaltLocoSet) % 3;

        /// <summary>Body size jitter (±5%) so a wave does not read as one model repeated.</summary>
        public static float ScaleJitter(int netId) =>
            1f + (StableHash(netId, SaltScale) % 101 - 50) * 0.001f;

        /// <summary>Integer payload value, or <paramref name="fallback"/> when the key is missing or not a
        /// number. ⚠️ Tolerant on purpose: a half-written payload must leave the presentation at its
        /// default instead of throwing on a hot path.</summary>
        public static int PayloadInt(NetObject netObject, string key, int fallback = 0)
        {
            if (netObject == null || !netObject.TryGetPayloadValue(key, out string raw))
            {
                return fallback;
            }

            return int.TryParse(raw, out int value) ? value : fallback;
        }
    }
}
