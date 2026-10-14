using UnityEngine;

namespace VortexArena.Modes.Zombie
{
    /// <summary>The zombie mode's own sound data: creature voices (groan · attack · death · spawn) and
    /// the two music layers.</summary>
    /// <remarks>Lives in an asset so swapping the placeholder clips for the final ones is a data edit —
    /// see <c>Docs/Gelistirici/Yemek-Kitabi.md</c>.
    /// <para>⚠️ Hit sounds are NOT here: they come from the <c>SUR_ZombieFlesh</c>/<c>SUR_ZombieHead</c>
    /// surface definitions through <c>SurfaceImpactFx</c>. A second copy here would double every hit.</para>
    /// <para>⚠️ No built-in fallback table (unlike <see cref="ZombieArsenal"/>): clips cannot be written
    /// in code, so a missing asset means silence + one warning, never a half-sounding mode.</para></remarks>
    public class ZombieSounds : ScriptableObject
    {
        /// <summary><c>Resources</c> path of the live asset (without extension).</summary>
        public const string ResourcePath = "Zombie/ZombieSounds";

        [Header("Yaratık sesleri (havuzdan rastgele seçilir)")]
        [Tooltip("Aylak homurtular.")]
        [SerializeField] private AudioClip[] groanClips;
        [Tooltip("Vuruş hamlesiyle birlikte çalar.")]
        [SerializeField] private AudioClip[] attackClips;
        [Tooltip("Zombi kırılınca çalar.")]
        [SerializeField] private AudioClip[] deathClips;
        [Tooltip("Zombi ilk göründüğünde ve kat değiştirdiğinde çalar. Boşsa homurtu havuzu kullanılır.")]
        [SerializeField] private AudioClip[] spawnClips;
        [Tooltip("Çığırtkan ve Dev çığlığı. Boşsa belirme havuzu kullanılır.")]
        [SerializeField] private AudioClip[] screamClips;

        [Header("Seviyeler")]
        [Range(0f, 1f)] [SerializeField] private float groanVolume = 0.65f;
        [Range(0f, 1f)] [SerializeField] private float attackVolume = 0.9f;
        [Range(0f, 1f)] [SerializeField] private float deathVolume = 0.9f;
        [Range(0f, 1f)] [SerializeField] private float spawnVolume = 0.7f;
        [Range(0f, 1f)] [SerializeField] private float screamVolume = 1f;

        [Tooltip("Perde sapması: her çalışta ±bu değer. Aynı klip sürüyle çalınca tek ses gibi duyulmasın.")]
        [Range(0f, 0.5f)] [SerializeField] private float pitchJitter = 0.12f;

        [Header("Homurtu temposu")]
        [Tooltip("Bir zombinin iki homurtusu arası en kısa süre (sn).")]
        [SerializeField] private float groanIntervalMin = 8f;
        [Tooltip("Bir zombinin iki homurtusu arası en uzun süre (sn).")]
        [SerializeField] private float groanIntervalMax = 15f;
        [Tooltip("Aynı anda homurdanabilen zombi sayısı — dinleyiciye en yakın olanlar.")]
        [SerializeField] private int maxGroaners = 6;

        [Header("3B mesafe")]
        [Tooltip("Bu mesafeye kadar tam seviye (m).")]
        [SerializeField] private float minDistance = 1f;
        [Tooltip("Bu mesafeden sonra duyulmaz (m).")]
        [SerializeField] private float maxDistance = 12f;
        [Tooltip("Çığlık bu mesafeye kadar duyulur (m) — kalabalığa verilen uyarı, uzaktan duyulmalı.")]
        [SerializeField] private float screamMaxDistance = 40f;

        [Header("Müzik (2B, Müzik kanalı)")]
        [Tooltip("Dalgalar arası gerilim katmanı — döngü.")]
        [SerializeField] private AudioClip musicTension;
        [Tooltip("Dalga sürerken çalan katman — döngü.")]
        [SerializeField] private AudioClip musicWave;
        [Range(0f, 1f)] [SerializeField] private float musicVolume = 0.55f;

        [Header("Opsiyonel")]
        [Tooltip("Opsiyonel: dalga başlangıcı vurgusu. Boş bırakılabilir.")]
        [SerializeField] private AudioClip waveStartSting;
        [Tooltip("Opsiyonel: dalga temizlendi vurgusu. Boş bırakılabilir.")]
        [SerializeField] private AudioClip waveClearSting;
        [Tooltip("Opsiyonel: Son Direniş vurgusu. Boş bırakılabilir.")]
        [SerializeField] private AudioClip finalStandSting;

        public float GroanVolume => groanVolume;

        public float AttackVolume => attackVolume;

        public float DeathVolume => deathVolume;

        public float SpawnVolume => spawnVolume;

        public float ScreamVolume => screamVolume;

        public float PitchJitter => pitchJitter;

        public float GroanIntervalMin => Mathf.Max(0.5f, groanIntervalMin);

        public float GroanIntervalMax => Mathf.Max(GroanIntervalMin, groanIntervalMax);

        public int MaxGroaners => Mathf.Max(1, maxGroaners);

        public float MinDistance => Mathf.Max(0.1f, minDistance);

        public float MaxDistance => Mathf.Max(MinDistance + 0.1f, maxDistance);

        public float ScreamMaxDistance => Mathf.Max(MaxDistance, screamMaxDistance);

        public AudioClip MusicTension => musicTension;

        public AudioClip MusicWave => musicWave;

        public float MusicVolume => musicVolume;

        /// <summary>Optional.</summary>
        public AudioClip WaveStartSting => waveStartSting;

        /// <summary>Optional.</summary>
        public AudioClip WaveClearSting => waveClearSting;

        /// <summary>Optional.</summary>
        public AudioClip FinalStandSting => finalStandSting;

        private static ZombieSounds live;
        private static bool resolved;

        /// <summary>The live asset, or <c>null</c> when it has not been created — in which case the
        /// mode stays silent and warns ONCE (per load), not per zombie.</summary>
        public static ZombieSounds Live
        {
            get
            {
                if (resolved)
                {
                    return live;
                }

                resolved = true;
                live = Resources.Load<ZombieSounds>(ResourcePath);

                if (live == null)
                {
                    Debug.LogWarning(
                        "[ZombieSounds] Ses verisi bulunamadı (Resources/Zombie/ZombieSounds) — zombi sesleri ve mod müziği çalmayacak. " +
                        "Tools > VortexArena > Zombie > Ses verisini oluştur.");
                }

                return live;
            }
        }

        public AudioClip RandomGroan() => Pick(groanClips);

        public AudioClip RandomAttack() => Pick(attackClips);

        public AudioClip RandomDeath() => Pick(deathClips);

        /// <summary>Spawn voice; falls back to the groan pool so a half-filled asset still sounds.</summary>
        public AudioClip RandomSpawn()
        {
            AudioClip clip = Pick(spawnClips);
            return clip != null ? clip : Pick(groanClips);
        }

        /// <summary>Scream; falls back to the spawn pool (itself backed by the groans), so a half-filled
        /// asset still gives the squad an audible warning.</summary>
        public AudioClip RandomScream()
        {
            AudioClip clip = Pick(screamClips);
            return clip != null ? clip : RandomSpawn();
        }

        private static AudioClip Pick(AudioClip[] pool)
        {
            if (pool == null || pool.Length == 0)
            {
                return null;
            }

            // One draw, no retry: a null slot reads as "no sound this time" instead of looping over a
            // pool that may be entirely empty.
            return pool[Random.Range(0, pool.Length)];
        }
    }
}
