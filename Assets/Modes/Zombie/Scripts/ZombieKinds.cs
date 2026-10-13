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

        /// <summary>Present only when the run is over: <c>over:lost</c>.</summary>
        public const string StateOver = "over";

        public const string OverLost = "lost";

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
