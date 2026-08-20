using System;
using UnityEngine;

namespace VortexArena.Core.Audio
{
    /// <summary>Single source for announcement sounds that vary by mode and map. Sounds SHARED by all
    /// maps are not here but in <see cref="GameSoundBank"/>.
    /// <para>⚠️ The asset lives at <c>Assets/_Shared/Data/Resources/ModeAudioRegistry.asset</c> — no
    /// scene references it, <see cref="Load"/> takes it via <c>Resources.Load</c> (same rationale as
    /// <c>GameCatalog</c>). Moving or renaming it silences every mode-specific sound.</para>
    /// <para>Rule matching: empty <c>modeId</c> = any mode, empty <c>sceneName</c> = any map. The
    /// most specific match wins (mode outweighs map); on a tie the FIRST rule in the list is used. So
    /// a general row stands as a fallback and a map-specific row overrides it on that map only.</para>
    /// <para>Clip lists are picked at random: several clips per trigger is how variation is
    /// authored, one clip is equally valid.</para></summary>
    [CreateAssetMenu(fileName = "ModeAudioRegistry", menuName = "VortexArena/Mode Audio Registry")]
    public class ModeAudioRegistry : ScriptableObject
    {
        /// <summary>Asset name under Resources.</summary>
        public const string ResourceName = "ModeAudioRegistry";

        /// <summary>A single "this sound under these conditions" row.</summary>
        [Serializable]
        public class Rule
        {
            [Tooltip("Hangi mod (tdm, ffa, tournament, lobby). BOŞ = her mod.")]
            [SerializeField] private string modeId = "";

            [Tooltip("Hangi harita — sahne adıyla BİREBİR aynı. BOŞ = her harita.")]
            [SerializeField] private string sceneName = "";

            [Tooltip("Sesin çalacağı an.")]
            [SerializeField] private ModeAudioEvent trigger = ModeAudioEvent.RoundStart;

            [Tooltip("Bu andan biri rastgele seçilir. Boş bırakılan girdi atlanır.")]
            [SerializeField] private AudioClip[] clips = Array.Empty<AudioClip>();

            [Range(0f, 1f)]
            [Tooltip("Klibin çalma seviyesi.")]
            [SerializeField] private float volume = 1f;

            [Min(0f)]
            [Tooltip("Yalnız uyarı tetikleyicilerinde: süre bitmeden kaç saniye önce çalsın.")]
            [SerializeField] private float warningSeconds = 5f;

            /// <summary>modId the rule is bound to; empty = any mode.</summary>
            public string ModeId => modeId;

            /// <summary>Scene name the rule is bound to; empty = any map.</summary>
            public string SceneName => sceneName;

            /// <summary>When the sound plays.</summary>
            public ModeAudioEvent Trigger => trigger;

            /// <summary>Threshold for warning triggers (seconds).</summary>
            public float WarningSeconds => warningSeconds;

            /// <summary>Clip volume (0..1).</summary>
            public float Volume => volume;

            /// <summary>Last clip this rule played — used to reject immediate repeats. NOT
            /// serialized: variation matters within a session, it is not a setting to write to
            /// disk.</summary>
            [NonSerialized] private AudioClip _lastPicked;

            /// <summary>A random clip from the list; null when all are empty.
            /// <para>⚠️ The previous pick is excluded when two or more clips are filled: pure
            /// randomness repeats the same clip back to back, heard as "always the same sound" in a
            /// one-announcement-per-round system. With two clips this alternates — visible variation
            /// beats pure randomness.</para></summary>
            public AudioClip PickClip()
            {
                if (clips == null)
                {
                    return null;
                }

                // Filled clips, excluding the previous pick. If that empties the pool (only one
                // filled clip) the exclusion is dropped — that rule plays the same sound every
                // round, which is correct.
                AudioClip[] pool = Collect(_lastPicked);
                if (pool.Length == 0)
                {
                    pool = Collect(null);
                }

                if (pool.Length == 0)
                {
                    return null;
                }

                _lastPicked = pool[UnityEngine.Random.Range(0, pool.Length)];
                return _lastPicked;
            }

            /// <summary>Collects the filled clips, skipping <paramref name="exclude"/> if given.</summary>
            private AudioClip[] Collect(AudioClip exclude)
            {
                int count = 0;
                for (int i = 0; i < clips.Length; i++)
                {
                    if (clips[i] != null && clips[i] != exclude)
                    {
                        count++;
                    }
                }

                var pool = new AudioClip[count];
                int next = 0;
                for (int i = 0; i < clips.Length && next < count; i++)
                {
                    if (clips[i] != null && clips[i] != exclude)
                    {
                        pool[next++] = clips[i];
                    }
                }

                return pool;
            }

            /// <summary>Does the rule fit the given context. An empty field means "no
            /// restriction".</summary>
            public bool Matches(ModeAudioEvent wanted, string activeModeId, string activeSceneName)
            {
                return trigger == wanted &&
                       Fits(modeId, activeModeId) &&
                       Fits(sceneName, activeSceneName);
            }

            /// <summary>Narrowness of the match: mode scores 2, map 1; the higher score wins. Mode
            /// deliberately outweighs map — the same arena is played in several modes.</summary>
            public int Specificity()
            {
                int score = 0;
                if (!string.IsNullOrEmpty(modeId))
                {
                    score += 2;
                }

                if (!string.IsNullOrEmpty(sceneName))
                {
                    score += 1;
                }

                return score;
            }

            private static bool Fits(string filter, string active)
            {
                return string.IsNullOrEmpty(filter) ||
                       string.Equals(filter, active, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Tooltip("Sırası önemsizdir: en spesifik kural kazanır, eşitlikte listedeki ilki.")]
        [SerializeField] private Rule[] rules = Array.Empty<Rule>();

        private static ModeAudioRegistry _cached;
        private static bool _loaded;

        /// <summary>Loads the asset from <c>Resources</c> once and caches it. Returns null when the
        /// asset is missing, and every mode-specific sound is then skipped silently.</summary>
        public static ModeAudioRegistry Load()
        {
            if (_loaded)
            {
                return _cached;
            }

            _loaded = true;
            _cached = Resources.Load<ModeAudioRegistry>(ResourceName);
            return _cached;
        }

        /// <summary>Finds the most specific rule fitting the given context. Returns <c>false</c> when
        /// nothing matches — the caller needs no extra check, it simply means no sound.</summary>
        public bool TryResolve(ModeAudioEvent trigger, string modeId, string sceneName, out Rule rule)
        {
            rule = null;
            if (rules == null)
            {
                return false;
            }

            int best = -1;
            for (int i = 0; i < rules.Length; i++)
            {
                Rule candidate = rules[i];
                if (candidate == null || !candidate.Matches(trigger, modeId, sceneName))
                {
                    continue;
                }

                int score = candidate.Specificity();
                if (score > best)
                {
                    best = score;
                    rule = candidate;
                }
            }

            return rule != null;
        }
    }
}
