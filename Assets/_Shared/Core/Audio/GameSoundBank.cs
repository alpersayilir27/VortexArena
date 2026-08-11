using UnityEngine;

namespace VortexArena.Core.Audio
{
    /// <summary>
    /// Tüm haritalarda ortak olan oyun seslerinin (duyuru/geri bildirim) tek kaynağı.
    /// Harita başına değişen ortam sesi burada DEĞİL <see cref="Arena.MapDefinition"/>'dadır.
    /// <para>
    /// ⚠️ Asset'in yeri <c>Assets/_Shared/Data/Resources/GameSoundBank.asset</c> — hiçbir
    /// sahneden referansı yoktur, <see cref="Load"/> onu <c>Resources.Load</c> ile alır
    /// (<c>GameCatalog</c> ile aynı gerekçe). Klasörden çıkarılırsa ya da adı değişirse tüm
    /// duyuru sesleri sessizce susar.
    /// </para>
    /// <para>Boş bırakılan klip sessizce atlanır: her sesin dolu olması gerekmez.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "GameSoundBank", menuName = "VortexArena/Game Sound Bank")]
    public class GameSoundBank : ScriptableObject
    {
        /// <summary>Resources altındaki asset adı.</summary>
        public const string ResourceName = "GameSoundBank";

        [Header("Öldürme / ölüm")]
        [Tooltip("Yerel oyuncu bir rakibi öldürdü — heyecanlı, kısa (0.5-1.5 sn).")]
        [SerializeField] private AudioClip enemyEliminated;
        [Tooltip("Yerel oyuncu öldü.")]
        [SerializeField] private AudioClip localDeath;
        [Tooltip("Yerel oyuncu canlandı.")]
        [SerializeField] private AudioClip localRespawn;

        [Header("Maç")]
        [SerializeField] private AudioClip matchStart;
        [SerializeField] private AudioClip matchWin;
        [SerializeField] private AudioClip matchLose;
        [Tooltip("Geri sayımın her saniyesi.")]
        [SerializeField] private AudioClip countdownTick;

        [Header("Karışım")]
        [Range(0f, 1f)]
        [Tooltip("Tüm duyuru seslerinin ortak seviyesi.")]
        [SerializeField] private float volume = 1f;

        private static GameSoundBank _cached;
        private static bool _loaded;

        /// <summary>Ortak ses seviyesi (0..1).</summary>
        public float Volume => volume;

        /// <summary>
        /// Kaynak asset'i <c>Resources</c>'tan yükler (tek sefer, sonuç önbelleklenir).
        /// Asset yoksa null döner ve tüm duyuru sesleri sessizce atlanır.
        /// </summary>
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

        /// <summary>İstenen sesin klibi; atanmamışsa null.</summary>
        public AudioClip Clip(GameSoundId id)
        {
            switch (id)
            {
                case GameSoundId.EnemyEliminated: return enemyEliminated;
                case GameSoundId.LocalDeath: return localDeath;
                case GameSoundId.LocalRespawn: return localRespawn;
                case GameSoundId.MatchStart: return matchStart;
                case GameSoundId.MatchWin: return matchWin;
                case GameSoundId.MatchLose: return matchLose;
                case GameSoundId.CountdownTick: return countdownTick;
                default: return null;
            }
        }
    }
}
