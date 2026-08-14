using System;
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

        /// <summary>
        /// Ölüm sesinden en son çalan klip — ardışık tekrarı elemek için. <b>Serialize EDİLMEZ</b>:
        /// varyasyon oturumun içinde anlamlıdır, diske yazılacak bir ayar değil.
        /// </summary>
        [NonSerialized] private AudioClip _lastLocalDeath;

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

        /// <summary>
        /// İstenen sesin klibi; atanmamışsa null.
        /// <para>⚠️ <see cref="GameSoundId.LocalDeath"/> her çağrıda <b>rastgele</b> bir klip
        /// döndürür — çağıran sonucu önbelleklemez.</para>
        /// </summary>
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

        /// <summary>
        /// Ölüm kliplerinden rastgele biri; hepsi boşsa null.
        /// <para>⚠️ <b>Bir önceki seçim elenir</b> (liste iki ya da daha fazla dolu klip taşıyorsa):
        /// saf rastgelede aynı klip peş peşe gelebiliyor ve ölüm başına tek duyuru çalan bir
        /// sistemde bu "hep aynı ses" olarak duyuluyor. İki klipte sonuç sırayla çalmaktır —
        /// varyasyonun görünürlüğü rastgeleliğin saflığından önemli
        /// (<see cref="ModeAudioRegistry.Rule.PickClip"/> ile aynı kural).</para>
        /// </summary>
        private AudioClip PickLocalDeath()
        {
            if (localDeathClips == null)
            {
                return null;
            }

            // Eleme sonucu liste boşalırsa (tek dolu klip var) eleme yapılmaz — o banka her
            // ölümde aynı sesi çalar, doğrusu da bu.
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

        /// <summary>Dolu klipleri toplar; <paramref name="exclude"/> varsa onu atlar.</summary>
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
