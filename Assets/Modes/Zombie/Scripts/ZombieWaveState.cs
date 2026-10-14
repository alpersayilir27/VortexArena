using System;
using UnityEngine;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Modes.Zombie
{
    /// <summary>The last <c>modeState</c> the mode broadcast (§10.5), parsed once for the whole machine:
    /// the running wave, how many zombies are still to spawn in it, the empty-storey minimum and the
    /// difficulty.</summary>
    /// <remarks>⚠️ Separate from <see cref="ZombieClientController"/> on purpose: the HUD prefab only
    /// exists on a player headset, while the BRAIN needs the same numbers on the object host admin —
    /// where there is no HUD at all. Both read the same <c>match_state</c>, so there is no second source.
    /// <para>ONE static subscription for the whole mode: a dozen brains re-splitting the same string is
    /// the same answer computed twelve times.</para></remarks>
    internal static class ZombieWaveState
    {
        /// <summary>Missing <c>rem</c> → "still spawning": the safe direction is to keep holding the empty
        /// storeys, otherwise a wave's first zombies all pile onto the players.</summary>
        private const int DefaultRemaining = 1;

        /// <summary>Missing <c>fm</c> → the <c>normal</c> value of the difficulty table.</summary>
        private const int DefaultFloorMin = 3;

        /// <summary>Raised when <see cref="Wave"/> CHANGES (main thread), with the new wave.</summary>
        public static event Action<int> WaveChanged;

        /// <summary>Running wave; <c>0</c> before the first wave (the pre-start countdown).</summary>
        public static int Wave { get; private set; }

        /// <summary>Zombies the server has not spawned yet in the running wave.</summary>
        public static int Remaining { get; private set; } = DefaultRemaining;

        /// <summary>Seconds to the next wave; <c>0</c> = a wave is RUNNING. The music layer hangs off
        /// this, so missing it reads as "wave running" — same safe direction as the HUD.</summary>
        public static int Next { get; private set; }

        /// <summary>How many zombies the empty storeys may hold while the wave is still spawning.</summary>
        public static int FloorMin { get; private set; } = DefaultFloorMin;

        /// <summary>Difficulty the admin picked (<c>easy|normal|hard</c>); empty before the first state.</summary>
        public static string Difficulty { get; private set; } = string.Empty;

        /// <summary>Raised when <see cref="FinalStand"/> FLIPS — the HUD's toast and the weapon unlocks
        /// hang off the edge, not off the value.</summary>
        public static event Action FinalStandChanged;

        /// <summary>Final stand ("Son Direniş") is running: no more waves, every weapon is open.</summary>
        public static bool FinalStand { get; private set; }

        /// <summary>The Dev's <c>netId</c>; <c>0</c> = no Dev in the run yet.</summary>
        public static int DevNetId { get; private set; }

        /// <summary>The Dev's max hp, as the mode announced it — the boss bar divides by THIS, so a
        /// headset that joined after the Dev spawned still draws a full bar.</summary>
        public static int DevMaxHp { get; private set; }

        /// <summary>Running special wave (<c>swarm|heavy|scream</c>); empty = a normal wave.</summary>
        public static string Special { get; private set; } = string.Empty;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            // Static handler on a static event: nothing to unsubscribe, and the values survive scene loads
            // exactly like the match they describe.
            NetEvents.OnMatchState += Apply;

            // ⚠️ A new match drops the wave BEFORE its first match_state: otherwise the previous run's
            // wave number rules the gap, and the weapons it had unlocked stay open for a second.
            NetEvents.OnLoadMatch += _ =>
            {
                Next = 0;
                Special = string.Empty;
                DevNetId = 0;
                DevMaxHp = 0;
                SetFinalStand(false);
                SetWave(0);
            };
            NetEvents.OnReturnToLobby += _ => Reset();
            NetEvents.OnDisconnected += Reset;
        }

        private static void Reset()
        {
            Next = 0;
            Remaining = DefaultRemaining;
            FloorMin = DefaultFloorMin;
            Difficulty = string.Empty;
            Special = string.Empty;
            DevNetId = 0;
            DevMaxHp = 0;
            SetFinalStand(false);
            SetWave(0);
        }

        /// <summary>Writes the wave and announces a CHANGE — including the drop back to 0 on returning
        /// to the lobby, which is how the weapon locks go back up for the next run.</summary>
        private static void SetWave(int wave)
        {
            if (Wave == wave)
            {
                return;
            }

            Wave = wave;
            WaveChanged?.Invoke(wave);
        }

        /// <summary><c>"w:3;left:5;next:0;rem:2;fm:3;diff:normal"</c> → the cached numbers.</summary>
        /// <remarks>⚠️ Tolerant like the HUD's parser: a partial or malformed string leaves the previous
        /// values in place and an unknown token is skipped — the mode's contract can gain tokens later.
        /// The pause reason rides the same field as free text and simply parses to nothing.</remarks>
        private static void Apply(MatchStateMsg msg)
        {
            string modeState = msg?.modeState;
            if (string.IsNullOrEmpty(modeState))
            {
                return;
            }

            // ⚠️ These two are read from the ABSENCE of their key as well: a special wave and the final
            // stand both END by the key dropping out, and a stale "fs" would keep every weapon open for
            // the rest of the run.
            bool sawSpecial = false;
            bool sawFinalStand = false;
            bool sawWave = false;

            string[] tokens = modeState.Split(';');
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];
                int sep = token.IndexOf(':');
                if (sep <= 0 || sep == token.Length - 1)
                {
                    continue;
                }

                string key = token.Substring(0, sep).Trim();
                string raw = token.Substring(sep + 1).Trim();

                if (string.Equals(key, ZombieKinds.StateDifficulty, StringComparison.Ordinal))
                {
                    Difficulty = raw;
                    continue;
                }

                if (string.Equals(key, ZombieKinds.StateSpecial, StringComparison.Ordinal))
                {
                    sawSpecial = true;
                    Special = raw;
                    continue;
                }

                if (string.Equals(key, ZombieKinds.StateDev, StringComparison.Ordinal))
                {
                    ApplyDev(raw);
                    continue;
                }

                if (string.Equals(key, ZombieKinds.StateFinalStand, StringComparison.Ordinal))
                {
                    sawFinalStand = true;
                    SetFinalStand(!string.Equals(raw, "0", StringComparison.Ordinal));
                    continue;
                }

                if (!int.TryParse(raw, out int value) || value < 0)
                {
                    continue;
                }

                if (string.Equals(key, ZombieKinds.StateWave, StringComparison.Ordinal))
                {
                    sawWave = true;
                    SetWave(value);
                }
                else if (string.Equals(key, ZombieKinds.StateNext, StringComparison.Ordinal))
                {
                    Next = value;
                }
                else if (string.Equals(key, ZombieKinds.StateRemaining, StringComparison.Ordinal))
                {
                    Remaining = value;
                }
                else if (string.Equals(key, ZombieKinds.StateFloorMin, StringComparison.Ordinal))
                {
                    FloorMin = value;
                }
            }

            // ⚠️ A mode pause replaces the whole string with its reason text (no "w"): absence there
            // means nothing, and reading it would re-lock every weapon mid final stand.
            if (!sawWave)
            {
                return;
            }

            if (!sawSpecial)
            {
                Special = string.Empty;
            }

            if (!sawFinalStand)
            {
                SetFinalStand(false);
            }
        }

        /// <summary><c>"&lt;netId&gt;/&lt;maxHp&gt;"</c> → the boss bar's two numbers. A malformed value
        /// leaves the previous Dev in place: half a bar is better than a bar that vanishes mid-fight.</summary>
        private static void ApplyDev(string raw)
        {
            int slash = raw.IndexOf('/');
            if (slash <= 0 || slash == raw.Length - 1)
            {
                return;
            }

            if (!int.TryParse(raw.Substring(0, slash), out int netId) || netId <= 0 ||
                !int.TryParse(raw.Substring(slash + 1), out int maxHp) || maxHp <= 0)
            {
                return;
            }

            DevNetId = netId;
            DevMaxHp = maxHp;
        }

        private static void SetFinalStand(bool value)
        {
            if (FinalStand == value)
            {
                return;
            }

            FinalStand = value;
            FinalStandChanged?.Invoke();
        }
    }
}
