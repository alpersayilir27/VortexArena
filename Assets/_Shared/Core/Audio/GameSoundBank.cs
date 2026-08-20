using System;
using UnityEngine;

namespace VortexArena.Core.Audio
{
    /// <summary>Single source for the game sounds (announcement/feedback) shared by all maps. The
    /// per-map ambience is NOT here but in <see cref="Arena.MapDefinition"/>.
    /// <para>⚠️ The asset lives at <c>Assets/_Shared/Data/Resources/GameSoundBank.asset</c> — no
    /// scene references it, <see cref="Load"/> takes it via <c>Resources.Load</c> (same rationale as
    /// <c>GameCatalog</c>). Moving or renaming it silences every announcement.</para>
    /// <para>An empty clip is skipped silently: not every sound has to be filled.</para></summary>
    [CreateAssetMenu(fileName = "GameSoundBank", menuName = "VortexArena/Game Sound Bank")]
    public class GameSoundBank : ScriptableObject
    {
        /// <summary>Asset name under Resources.</summary>
        public const string ResourceName = "GameSoundBank";

        [Header("Öldürme / ölüm")]
        [Tooltip("Yerel oyuncu bir rakibi öldürdü — heyecanlı, kısa (0.5-1.5 sn).")]
        [SerializeField] private AudioClip enemyEliminated;
        [Tooltip("Yerel oyuncu kendi takım arkadaşını öldürdü (dost ateşi açıkken) — rakip " +
                 "sesinin YERİNE çalar, ona ek değil.")]
        [SerializeField] private AudioClip teammateEliminated;
        [Tooltip("Yerel oyuncu öldü — birden çok klip yazmak varyasyon üretir, biri rastgele seçilir.")]
        [SerializeField] private AudioClip[] localDeathClips = Array.Empty<AudioClip>();
        [Tooltip("Yerel oyuncu canlandı.")]
        [SerializeField] private AudioClip localRespawn;

        [Header("Maç")]
        [SerializeField] private AudioClip matchStart;
        [Tooltip("Maç KIRMIZI takımın kazanmasıyla bitti — herkeste aynı çalar, kimin kazandığı " +
                 "dinleyene göre değişmez.")]
        [SerializeField] private AudioClip teamRedWon;
        [Tooltip("Maç MAVİ takımın kazanmasıyla bitti.")]
        [SerializeField] private AudioClip teamBlueWon;
        [Tooltip("Maç berabere bitti (kazanan takım da kazanan oyuncu da yok).")]
        [SerializeField] private AudioClip matchDraw;
        [Tooltip("Geri sayımın her saniyesi.")]
        [SerializeField] private AudioClip countdownTick;

        [Header("Admin")]
        [Tooltip("Bir oyuncunun fiziksel ihlali başladı — kısa ve dikkat çekici, ama alarm değil: " +
                 "operatör onu maç boyunca duyacak. Yalnız admin PC'sinde çalar.")]
        [SerializeField] private AudioClip adminViolation;

        [Header("Karışım")]
        [Range(0f, 1f)]
        [Tooltip("Tüm duyuru seslerinin ortak seviyesi.")]
        [SerializeField] private float volume = 1f;

        private static GameSoundBank _cached;
        private static bool _loaded;

        /// <summary>Last death clip played — used to reject immediate repeats. NOT serialized:
        /// variation matters within a session, it is not a setting to write to disk.</summary>
        [NonSerialized] private AudioClip _lastLocalDeath;

        /// <summary>Shared volume (0..1).</summary>
        public float Volume => volume;

        /// <summary>Loads the asset from <c>Resources</c> once and caches it. Returns null when the
        /// asset is missing, and every announcement is then skipped silently.</summary>
        public static GameSoundBank Load()
        {
            if (_loaded)
            {
                return _cached;
            }

            _loaded = true;
            _cached = Resources.Load<GameSoundBank>(ResourceName);
            return _cached;
        }

        /// <summary>Clip for the requested sound; null when unassigned.
        /// <para>⚠️ <see cref="GameSoundId.LocalDeath"/> returns a RANDOM clip on every call — the
        /// caller must not cache the result.</para></summary>
        public AudioClip Clip(GameSoundId id)
        {
            switch (id)
            {
                case GameSoundId.EnemyEliminated: return enemyEliminated;
                case GameSoundId.TeammateEliminated: return teammateEliminated;
                case GameSoundId.LocalDeath: return PickLocalDeath();
                case GameSoundId.LocalRespawn: return localRespawn;
                case GameSoundId.MatchStart: return matchStart;
                case GameSoundId.TeamRedWon: return teamRedWon;
                case GameSoundId.TeamBlueWon: return teamBlueWon;
                case GameSoundId.MatchDraw: return matchDraw;
                case GameSoundId.CountdownTick: return countdownTick;
                case GameSoundId.AdminViolation: return adminViolation;
                default: return null;
            }
        }

        /// <summary>A random death clip; null when all are empty.
        /// <para>⚠️ The previous pick is excluded when two or more clips are filled: pure randomness
        /// repeats the same clip back to back, which in a one-announcement-per-death system is heard
        /// as "always the same sound". With two clips this alternates — visible variation beats pure
        /// randomness (same rule as <see cref="ModeAudioRegistry.Rule.PickClip"/>).</para></summary>
        private AudioClip PickLocalDeath()
        {
            if (localDeathClips == null)
            {
                return null;
            }

            // If exclusion empties the pool (only one filled clip) it is dropped — that bank plays
            // the same sound on every death, which is correct.
            AudioClip[] pool = Collect(localDeathClips, _lastLocalDeath);
            if (pool.Length == 0)
            {
                pool = Collect(localDeathClips, null);
            }

            if (pool.Length == 0)
            {
                return null;
            }

            _lastLocalDeath = pool[UnityEngine.Random.Range(0, pool.Length)];
            return _lastLocalDeath;
        }

        /// <summary>Collects the filled clips, skipping <paramref name="exclude"/> if given.</summary>
        private static AudioClip[] Collect(AudioClip[] clips, AudioClip exclude)
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
    }
}
