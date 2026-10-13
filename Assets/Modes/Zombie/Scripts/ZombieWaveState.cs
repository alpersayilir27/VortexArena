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

                if (!int.TryParse(raw, out int value) || value < 0)
                {
                    continue;
                }

                if (string.Equals(key, ZombieKinds.StateWave, StringComparison.Ordinal))
                {
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
        }
    }
}
