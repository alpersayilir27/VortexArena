using UnityEngine;

namespace VortexArena.Modes.Zombie
{
    /// <summary>The zombie mode's own sound data: creature voices (groan · attack · death · spawn ·
    /// footstep), the two music layers and the optional 2D cues (sting · tick · unlock · revive ·
    /// outcome) plus the mode's own announcement lines.</summary>
    /// <remarks>Lives in an asset so swapping a clip is a data edit, not a code edit —
    /// see <c>Docs/Gelistirici/Yemek-Kitabi.md</c>.
    /// <para>⚠️ Announcement lines go through <c>GameAudio</c>'s single announcement channel, not the
    /// mode's own source: two spoken lines at once are unintelligible.</para>
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

        // Above 1 is legal: PlayOneShot's volumeScale amplifies the clip, and the voices are
        // mastered quiet next to the weapons.
        [Header("Seviyeler")]
        [Range(0f, 1.5f)] [SerializeField] private float groanVolume = 0.65f;
        [Range(0f, 1.5f)] [SerializeField] private float attackVolume = 0.9f;
        [Range(0f, 1.5f)] [SerializeField] private float deathVolume = 0.9f;
        [Range(0f, 1.5f)] [SerializeField] private float spawnVolume = 0.7f;
        [Range(0f, 2f)] [SerializeField] private float screamVolume = 1.5f;

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
        [Tooltip("Bu mesafeye kadar tam seviye (m). Büyütmek sesi yakında da uzakta da yükseltir.")]
        [SerializeField] private float minDistance = 2f;
        [Tooltip("Bu mesafeden sonra ses daha fazla kısılmaz (m) — kesilmez, o seviyede duyulmaya devam eder.")]
        [SerializeField] private float maxDistance = 20f;
        [Tooltip("Çığlık bu mesafeye kadar tam seviye çalar (m).")]
        [SerializeField] private float screamMinDistance = 5f;
        [Tooltip("Çığlık bu mesafeden sonra daha fazla kısılmaz (m) — kalabalığa verilen uyarı, uzaktan duyulmalı.")]
        [SerializeField] private float screamMaxDistance = 40f;

        [Header("Müzik (2B, Müzik kanalı)")]
        [Tooltip("Dalgalar arası gerilim katmanı — döngü.")]
        [SerializeField] private AudioClip musicTension;
        [Tooltip("Dalga sürerken çalan katman — döngü.")]
        [SerializeField] private AudioClip musicWave;
        [Range(0f, 1f)] [SerializeField] private float musicVolume = 0.4f;

        [Header("Opsiyonel")]
        [Tooltip("Opsiyonel: dalga başlangıcı vurgusu. Boş bırakılabilir.")]
        [SerializeField] private AudioClip waveStartSting;
        [Tooltip("Opsiyonel: dalga temizlendi vurgusu. Boş bırakılabilir.")]
        [SerializeField] private AudioClip waveClearSting;
        [Tooltip("Opsiyonel: Son Direniş vurgusu. Boş bırakılabilir.")]
        [SerializeField] private AudioClip finalStandSting;

        [Header("Opsiyonel — tür sesleri")]
        [Tooltip("Opsiyonel: Koşucu homurtuları. Boşsa aylak homurtu havuzu kullanılır.")]
        [SerializeField] private AudioClip[] runnerGroanClips;
        [Tooltip("Opsiyonel: Dev kükremesi. Boşsa çığlık havuzu kullanılır.")]
        [SerializeField] private AudioClip[] giantRoarClips;
        [Tooltip("Opsiyonel: Dev vuruşu. Boşsa vuruş havuzu kullanılır.")]
        [SerializeField] private AudioClip[] giantSlamClips;
        [Tooltip("Opsiyonel: Dev ölümü. Boşsa ölüm havuzu kullanılır.")]
        [SerializeField] private AudioClip[] giantDeathClips;

        [Header("Opsiyonel — ayak sesleri")]
        [Tooltip("Opsiyonel: yürürken çalan adım sesleri. Boşsa hiç adım sesi çalmaz.")]
        [SerializeField] private AudioClip[] footstepClips;
        [Tooltip("Opsiyonel: Dev'in ağır adımları. Boşsa normal adım havuzu kullanılır.")]
        [SerializeField] private AudioClip[] giantStepClips;
        [Range(0f, 1f)] [SerializeField] private float footstepVolume = 0.5f;

        [Header("Opsiyonel — 2B arayüz sesleri (Müzik kanalı)")]
        [Tooltip("Opsiyonel: dalga arası geri sayımın son 5 saniyesinde her saniye çalar.")]
        [SerializeField] private AudioClip countdownTick;
        [Tooltip("Opsiyonel: temizlik fazında yeni silah açıldığında çalar.")]
        [SerializeField] private AudioClip weaponUnlockClip;
        [Tooltip("Opsiyonel: kaldırma sürerken döngüyle çalar.")]
        [SerializeField] private AudioClip reviveProgressClip;
        [Tooltip("Opsiyonel: oyuncu ayağa kalktığında çalar.")]
        [SerializeField] private AudioClip reviveCompleteClip;

        [Header("Opsiyonel — maç sonu")]
        [Tooltip("Opsiyonel: dayanıldığında çalar.")]
        [SerializeField] private AudioClip victoryClip;
        [Tooltip("Opsiyonel: kurtulunduğunda çalar. Boşsa zafer klibi kullanılır.")]
        [SerializeField] private AudioClip rescueClip;
        [Tooltip("Opsiyonel: yenilgide çalar.")]
        [SerializeField] private AudioClip defeatClip;

        [Header("Anons (Voiceover kanalı)")]
        [Range(0f, 1f)]
        [Tooltip("Anons kliplerinin seviyesi. Üstüne Voiceover kanalı uygulanır.")]
        [SerializeField] private float voiceVolume = 1f;
        [Tooltip("Opsiyonel: dalga temizlendi anonsu. Boş bırakılabilir.")]
        [SerializeField] private AudioClip waveClearVoice;
        [Tooltip("Opsiyonel: yeni silah açıldı anonsu — yalnız gerçekten silah açılınca çalar.")]
        [SerializeField] private AudioClip newWeaponsVoice;
        [Tooltip("Opsiyonel: Son Direniş anonsu. Boş bırakılabilir.")]
        [SerializeField] private AudioClip finalStandVoice;
        [Tooltip("Opsiyonel: Dev ilk göründüğünde bir kez çalar.")]
        [SerializeField] private AudioClip giantIncomingVoice;
        [Tooltip("Opsiyonel: ekip arkadaşı düştüğünde çalar.")]
        [SerializeField] private AudioClip mateDownedVoice;
        [Tooltip("Opsiyonel: dayanıldığında çalan anons.")]
        [SerializeField] private AudioClip survivedVoice;
        [Tooltip("Opsiyonel: kurtulunduğunda çalan anons. Boşsa dayanma anonsu kullanılır.")]
        [SerializeField] private AudioClip rescuedVoice;
        [Tooltip("Opsiyonel: yenilgi anonsu. Boş bırakılabilir.")]
        [SerializeField] private AudioClip lostVoice;

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

        public float ScreamMinDistance => Mathf.Max(0.1f, screamMinDistance);

        public float ScreamMaxDistance => Mathf.Max(ScreamMinDistance + 0.1f, screamMaxDistance);

        public AudioClip MusicTension => musicTension;

        public AudioClip MusicWave => musicWave;

        public float MusicVolume => musicVolume;

        /// <summary>Optional.</summary>
        public AudioClip WaveStartSting => waveStartSting;

        /// <summary>Optional.</summary>
        public AudioClip WaveClearSting => waveClearSting;

        /// <summary>Optional.</summary>
        public AudioClip FinalStandSting => finalStandSting;

        public float FootstepVolume => footstepVolume;

        /// <summary>Optional; empty = no footsteps at all (not a warning).</summary>
        public bool HasFootsteps =>
            (footstepClips != null && footstepClips.Length > 0)
            || (giantStepClips != null && giantStepClips.Length > 0);

        /// <summary>Optional.</summary>
        public AudioClip CountdownTick => countdownTick;

        /// <summary>Optional.</summary>
        public AudioClip WeaponUnlockClip => weaponUnlockClip;

        /// <summary>Optional.</summary>
        public AudioClip ReviveProgressClip => reviveProgressClip;

        /// <summary>Optional.</summary>
        public AudioClip ReviveCompleteClip => reviveCompleteClip;

        /// <summary>Optional.</summary>
        public AudioClip VictoryClip => victoryClip;

        /// <summary>Optional; falls back to the victory clip — a rescue is a win too.</summary>
        public AudioClip RescueClip => rescueClip != null ? rescueClip : victoryClip;

        /// <summary>Optional.</summary>
        public AudioClip DefeatClip => defeatClip;

        /// <summary>Level of the voice lines, before the voiceover channel.</summary>
        public float VoiceVolume => Mathf.Clamp01(voiceVolume);

        /// <summary>Optional.</summary>
        public AudioClip WaveClearVoice => waveClearVoice;

        /// <summary>Optional.</summary>
        public AudioClip NewWeaponsVoice => newWeaponsVoice;

        /// <summary>Optional.</summary>
        public AudioClip FinalStandVoice => finalStandVoice;

        /// <summary>Optional.</summary>
        public AudioClip GiantIncomingVoice => giantIncomingVoice;

        /// <summary>Optional.</summary>
        public AudioClip MateDownedVoice => mateDownedVoice;

        /// <summary>Optional.</summary>
        public AudioClip SurvivedVoice => survivedVoice;

        /// <summary>Optional; falls back to the survived line — a rescue is a win too.</summary>
        public AudioClip RescuedVoice => rescuedVoice != null ? rescuedVoice : survivedVoice;

        /// <summary>Optional.</summary>
        public AudioClip LostVoice => lostVoice;

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

        /// <summary>Runner groan; falls back to the walker pool.</summary>
        public AudioClip RandomRunnerGroan()
        {
            AudioClip clip = Pick(runnerGroanClips);
            return clip != null ? clip : Pick(groanClips);
        }

        /// <summary>The Giant's roar; falls back to the scream chain, so the squad still hears it
        /// coming.</summary>
        public AudioClip RandomGiantRoar()
        {
            AudioClip clip = Pick(giantRoarClips);
            return clip != null ? clip : RandomScream();
        }

        /// <summary>The Giant's slam; falls back to the attack pool.</summary>
        public AudioClip RandomGiantSlam()
        {
            AudioClip clip = Pick(giantSlamClips);
            return clip != null ? clip : Pick(attackClips);
        }

        /// <summary>The Giant's death; falls back to the death pool.</summary>
        public AudioClip RandomGiantDeath()
        {
            AudioClip clip = Pick(giantDeathClips);
            return clip != null ? clip : Pick(deathClips);
        }

        /// <summary>Footstep; <c>null</c> = this body takes no steps (no fallback to a voice pool: a
        /// groan per step would be a different sound entirely).</summary>
        public AudioClip RandomFootstep() => Pick(footstepClips);

        /// <summary>The Giant's step; falls back to the normal step pool.</summary>
        public AudioClip RandomGiantStep()
        {
            AudioClip clip = Pick(giantStepClips);
            return clip != null ? clip : Pick(footstepClips);
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
