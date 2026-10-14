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

        // ------------------------------------------------------------------- client-side numbers

        /// <summary>Attack reach (m, horizontal). The server only applies a LOOSER bound
        /// (<c>2.5 m</c>, §10.5): deciding the moment is the client's, both poses are local there.</summary>
        public const float AttackReach = 1.2f;

        /// <summary>Must mirror the server's cooldown, or every second attack is rejected silently and
        /// the zombie looks like it is swinging at nothing.</summary>
        public const float AttackCooldownSeconds = 1.5f;

        /// <summary>Walking speed (m/s) per wave: <c>Base + PerWave × (wave − 1)</c>, capped. Speed is
        /// CLIENT-side: the server has no metres (§10.10).</summary>
        public const float SpeedBase = 0.7f;

        public const float SpeedPerWave = 0.1f;

        public const float SpeedCap = 1.2f;

        public static float SpeedOfWave(int wave) =>
            Mathf.Min(SpeedCap, SpeedBase + SpeedPerWave * (Mathf.Max(1, wave) - 1));

        // ------------------------------------------------------------------- deterministic variety

        /// <summary>Share of crawlers in a wave (0..1).</summary>
        private const int CrawlerPercent = 12;

        /// <summary>Speed scale of a crawler: it drags itself, and at wave speed the crawl cycle would
        /// skate.</summary>
        public const float CrawlerSpeedScale = 0.45f;

        // Salts: every deterministic choice draws from its OWN stream. Reusing one hash would correlate
        // the picks (every crawler the same look) and that reads as a bug in the field.
        private const int SaltLook = 0;
        private const int SaltCrawler = 101;
        private const int SaltLocoSet = 211;
        private const int SaltScale = 307;

        /// <summary>Stable hash of a <c>netId</c>, non-negative.</summary>
        /// <remarks>⚠️ Hand-rolled FNV-1a, NOT <c>GetHashCode</c>: the look, the crawler flag and the
        /// gait must come out IDENTICAL on every headset, and the runtime's hash is only guaranteed
        /// stable within one process. A mismatch here is a zombie that crawls on one visor and walks on
        /// the next, with no network field to blame.</remarks>
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

        /// <summary>Does this zombie crawl? ⚠️ Derived from the <c>netId</c> instead of a payload field:
        /// the BRAIN (speed) and the VIEW (animation) must agree, and the protocol carries no kind flag.
        /// Both sides call THIS — a second copy of the rule drifts silently.</summary>
        public static bool IsCrawler(int netId) =>
            netId > 0 && StableHash(netId, SaltCrawler) % 100 < CrawlerPercent;

        /// <summary>Index into the look prefab list.</summary>
        public static int LookIndex(int netId, int lookCount) =>
            lookCount > 0 ? StableHash(netId, SaltLook) % lookCount : 0;

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
