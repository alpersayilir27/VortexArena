using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Modes.Zombie
{
    /// <summary>Every number of the zombie's HIT REACTION: flinch, hit clip, death impulse, hit-stop,
    /// stagger, dismemberment, gibs, blood decals — plus the gore meshes, materials and clips they
    /// need.</summary>
    /// <remarks>Lives in an asset so tuning is a data edit, not a code edit (recipe in
    /// <c>Docs/Gelistirici/Yemek-Kitabi.md</c>).
    /// <para>⚠️ Every object reference is OPTIONAL: a missing mesh, material or clip means that one
    /// effect is skipped, never a broken death. The built-in defaults below are the numbers the code
    /// shipped with, so a headset with no asset behaves exactly as before.</para></remarks>
    public class ZombieReactionTuning : ScriptableObject
    {
        /// <summary><c>Resources</c> path of the live asset (without extension).</summary>
        public const string ResourcePath = "Zombie/ZombieReactionTuning";

        // ------------------------------------------------------------------- flinch

        [Header("Sarsılma (yay) — derece")]
        [SerializeField] private float headFlinchAmplitude = 18f;
        [SerializeField] private float bodyFlinchAmplitude = 11f;
        [SerializeField] private float stomachFlinchAmplitude = 9f;
        [SerializeField] private float legFlinchAmplitude = 7f;

        [Tooltip("Yay sertliği ve sönümü. Sertlik vuruş kazancını da belirler.")]
        [SerializeField] private float flinchStiffness = 140f;
        [SerializeField] private float flinchDamping = 16f;
        [Tooltip("Toplam bükülme sınırı (derece) — aşılırsa seri ateşte boyun geriye katlanır.")]
        [SerializeField] private float flinchMaxAngle = 26f;

        [Tooltip("Patron gövdelerinde sarsılma ölçeği: her mermiyle irkilen gövde sersemlemiş okunur.")]
        [SerializeField] private float bruteFlinchScale = 0.5f;
        [SerializeField] private float devFlinchScale = 0.3f;

        // ------------------------------------------------------------------- hit clip

        [Header("Vuruş klibi")]
        [Tooltip("Üst gövde Hit tetiği için en kısa aralık (sn) — seri ateşte klip her mermide " +
                 "baştan başlamasın.")]
        [SerializeField] private float hitClipCooldownSeconds = 0.35f;
        [Tooltip("Yerel vuruş raporu ile sunucudan dönen can düşüşü aynı mermidir: bu pencerede " +
                 "ikincisi klibi yeniden tetiklemez (sn).")]
        [SerializeField] private float hitAnimDedupeSeconds = 0.3f;

        // ------------------------------------------------------------------- death impulse

        [Header("Ölüm itkisi (N·s)")]
        [SerializeField] private float headImpulse = 45f;
        [SerializeField] private float bodyImpulse = 32f;
        [SerializeField] private float stomachImpulse = 30f;
        [SerializeField] private float legImpulse = 25f;

        [Tooltip("İtki ölçeği = kare hasarı / bu değer, alt-üst sınırla kırpılır.")]
        [SerializeField] private float impulseDamageReference = 25f;
        [SerializeField] private float impulseScaleMin = 0.8f;
        [SerializeField] private float impulseScaleMax = 2.2f;

        [Tooltip("Bundan eski vuruş raporu öldüren mermi sayılmaz (sn).")]
        [SerializeField] private float hitMemorySeconds = 0.6f;

        // ------------------------------------------------------------------- corpse timings

        [Header("Ceset zamanlaması (sn)")]
        [Tooltip("Ragdoll bu süre sonunda dondurulur — çözülme oturmuş bir poz üzerinde koşsun.")]
        [SerializeField] private float ragdollFreezeSeconds = 2.6f;
        [Tooltip("⚠️ Gecikme + süre, sunucunun despawn gecikmesinin (4,5 sn) ALTINDA kalmalı.")]
        [SerializeField] private float deathDissolveDelay = 3.3f;
        [SerializeField] private float deathDissolveSeconds = 0.8f;
        [SerializeField] private float bloodPoolDelay = 0.8f;

        [Tooltip("Tahminle başlayan ölümde sunucu bu süre içinde hâlâ 'ayakta' derse gövde geri alınır (sn).")]
        [SerializeField] private float predictionRestoreSeconds = 1.5f;

        // ------------------------------------------------------------------- hit stop

        [Header("Mikro duraklama (yalnız oyuncu başlığı)")]
        [SerializeField] private float hitStopAnimatorSpeed = 0.05f;
        [SerializeField] private float hitStopSeconds = 0.06f;
        [Tooltip("Kafa dışındaki vuruşlarda duraklama için gereken kare hasarı.")]
        [SerializeField] private float hitStopDamage = 30f;

        // ------------------------------------------------------------------- stagger

        [Header("Sendeleme + geri itme (admin beyni)")]
        [Tooltip("Hasarın biriktiği pencere (sn).")]
        [SerializeField] private float staggerWindowSeconds = 0.35f;
        [Tooltip("Pencerede biriken hasar eşiği.")]
        [SerializeField] private float staggerDamage = 45f;
        [Tooltip("Brute'un eşiği; Dev hiç sendelemez.")]
        [SerializeField] private float bruteStaggerDamage = 150f;
        [SerializeField] private float staggerSeconds = 0.6f;
        [SerializeField] private float staggerSpeedScale = 0.15f;
        [Tooltip("Geri itme mesafesi (m) ve süresi (sn).")]
        [SerializeField] private float knockbackMeters = 0.35f;
        [SerializeField] private float knockbackSeconds = 0.15f;
        [Tooltip("Geri itme hız sınırı (m/s) — oyuncudan UZAĞA gittiği için yürüme sınırının üstüne çıkabilir.")]
        [SerializeField] private float knockbackSpeedCap = 2.5f;

        // ------------------------------------------------------------------- dismemberment

        [Header("Uzuv kopması")]
        [Tooltip("Ölümcül kol/bacak vuruşunun uzvu koparması için gereken kare hasarı.")]
        [SerializeField] private float limbSeverDamage = 22f;

        [Tooltip("Kesik ucu kapaklarının YARIÇAPI (m): boyun · üst kol · ön kol · uyluk · baldır. " +
                 "Kapak modeli 5 cm yarıçapla çizildi, kod bu değerlere ölçekler.")]
        [SerializeField] private float neckStumpRadius = 0.055f;
        [SerializeField] private float upperArmStumpRadius = 0.055f;
        [SerializeField] private float foreArmStumpRadius = 0.045f;
        [SerializeField] private float thighStumpRadius = 0.085f;
        [SerializeField] private float shinStumpRadius = 0.065f;
        [Tooltip("Kapak modelinin kendi yarıçapı (m) — ölçek bölenidir.")]
        [SerializeField] private float stumpMeshRadius = 0.05f;

        [Header("Gore modelleri (FBX) — boş = o parça çizilmez")]
        [Tooltip("ZG_Stump_Neck: pivot kesit merkezi, dışa bakan yön +Y, ön +Z.")]
        [SerializeField] private GameObject neckStumpPrefab;
        [Tooltip("ZG_Stump_Limb: dairesel kapak, dışa bakan yön +Y.")]
        [SerializeField] private GameObject limbStumpPrefab;
        [SerializeField] private GameObject[] fleshGibPrefabs;
        [SerializeField] private GameObject[] boneGibPrefabs;
        [SerializeField] private GameObject[] skullGibPrefabs;

        [Header("Gore materyalleri")]
        [Tooltip("Modellerdeki 'Flesh' yuvasının yerine geçer.")]
        [SerializeField] private Material fleshMaterial;
        [Tooltip("Modellerdeki 'Bone' yuvasının yerine geçer.")]
        [SerializeField] private Material boneMaterial;

        [Header("Patlama efektleri (opsiyonel prefab)")]
        [Tooltip("Kafa patlamasında çalar — FX_Impact_ZombieHead.")]
        [SerializeField] private GameObject headBurstPrefab;
        [Tooltip("Uzuv kopmasında çalar — FX_Impact_ZombieBlood.")]
        [SerializeField] private GameObject severBurstPrefab;

        // ------------------------------------------------------------------- gibs

        [Header("Et parçaları")]
        [SerializeField] private int gibPoolSize = 24;
        [SerializeField] private int gibsPerSever = 4;
        [SerializeField] private int gibsPerHeadPop = 6;
        [SerializeField] private float gibLifetimeSeconds = 4f;
        [SerializeField] private float gibShrinkSeconds = 0.5f;
        [SerializeField] private float gibEjectSpeedMin = 1.5f;
        [SerializeField] private float gibEjectSpeedMax = 4f;
        [Tooltip("Fırlama yönüne eklenen yukarı bileşen.")]
        [SerializeField] private float gibUpBias = 0.45f;
        [SerializeField] private float gibSpinDegrees = 540f;
        [Tooltip("Parçanın çarpışma küresinin yarıçapı (m).")]
        [SerializeField] private float gibRadius = 0.04f;

        // ------------------------------------------------------------------- blood decals

        [Header("Kan izleri")]
        [Tooltip("Duvar/zemin izi şansı: kafa ve diğer bölgeler.")]
        [Range(0f, 1f)] [SerializeField] private float splatChanceHead = 0.8f;
        [Range(0f, 1f)] [SerializeField] private float splatChanceOther = 0.4f;
        [Tooltip("İzin arandığı mesafe (m) — vuruş noktasından mermi yönünde.")]
        [SerializeField] private float splatRayMeters = 2.5f;
        [SerializeField] private float splatSizeMin = 0.3f;
        [SerializeField] private float splatSizeMax = 0.7f;
        [SerializeField] private float splatLifetimeSeconds = 25f;
        [SerializeField] private float splatFadeSeconds = 5f;
        [SerializeField] private int splatPoolSize = 48;

        [Tooltip("Zombi gövdesindeki yara izi boyutu (m) ve gövde başına üst sınır.")]
        [SerializeField] private float woundSizeMin = 0.06f;
        [SerializeField] private float woundSizeMax = 0.12f;
        [SerializeField] private int woundsPerZombie = 5;
        [SerializeField] private int woundPoolSize = 80;

        [Tooltip("Kan izi materyali (URP/Unlit, saydam). Boş = hiç iz çizilmez. Doku kodda üretilir.")]
        [SerializeField] private Material bloodDecalMaterial;

        // ------------------------------------------------------------------- sounds

        [Header("Acı sesi (klipler ZombieSounds'ta)")]
        [Tooltip("Vuruş başına acı sesi şansı.")]
        [Range(0f, 1f)] [SerializeField] private float painChance = 0.7f;
        [Tooltip("Aynı zombinin iki acı sesi arası en kısa süre (sn).")]
        [SerializeField] private float painCooldownSeconds = 0.6f;
        [Tooltip("Aynı anda duyulabilen acı sesi sayısı (tüm zombiler).")]
        [SerializeField] private int painConcurrent = 3;

        [Header("Gore sesleri (boş = sessiz)")]
        [SerializeField] private AudioClip[] headPopClips;
        [SerializeField] private AudioClip[] limbSeverClips;
        [SerializeField] private AudioClip[] gibSplatClips;
        [Tooltip("Sakatlanan gövdenin yere çökmesi.")]
        [SerializeField] private AudioClip[] crawlCollapseClips;
        [Range(0f, 2f)] [SerializeField] private float goreVolume = 1f;

        // ------------------------------------------------------------------- properties

        public float HeadFlinchAmplitude => headFlinchAmplitude;

        public float BodyFlinchAmplitude => bodyFlinchAmplitude;

        public float StomachFlinchAmplitude => stomachFlinchAmplitude;

        public float LegFlinchAmplitude => legFlinchAmplitude;

        public float FlinchStiffness => Mathf.Max(1f, flinchStiffness);

        public float FlinchDamping => Mathf.Max(0f, flinchDamping);

        public float FlinchMaxAngle => Mathf.Max(1f, flinchMaxAngle);

        public float BruteFlinchScale => bruteFlinchScale;

        public float DevFlinchScale => devFlinchScale;

        public float HitClipCooldownSeconds => Mathf.Max(0f, hitClipCooldownSeconds);

        public float HitAnimDedupeSeconds => Mathf.Max(0f, hitAnimDedupeSeconds);

        public float BodyImpulse => bodyImpulse;

        public float HitMemorySeconds => Mathf.Max(0f, hitMemorySeconds);

        public float RagdollFreezeSeconds => ragdollFreezeSeconds;

        public float DeathDissolveDelay => deathDissolveDelay;

        public float DeathDissolveSeconds => deathDissolveSeconds;

        public float BloodPoolDelay => bloodPoolDelay;

        public float PredictionRestoreSeconds => Mathf.Max(0.1f, predictionRestoreSeconds);

        public float HitStopAnimatorSpeed => Mathf.Clamp(hitStopAnimatorSpeed, 0.001f, 1f);

        public float HitStopSeconds => Mathf.Max(0f, hitStopSeconds);

        public float HitStopDamage => hitStopDamage;

        public float StaggerWindowSeconds => Mathf.Max(0.01f, staggerWindowSeconds);

        public float StaggerDamage => staggerDamage;

        public float BruteStaggerDamage => bruteStaggerDamage;

        public float StaggerSeconds => Mathf.Max(0f, staggerSeconds);

        public float StaggerSpeedScale => Mathf.Clamp01(staggerSpeedScale);

        public float KnockbackMeters => Mathf.Max(0f, knockbackMeters);

        public float KnockbackSeconds => Mathf.Max(0.01f, knockbackSeconds);

        public float KnockbackSpeedCap => Mathf.Max(0.1f, knockbackSpeedCap);

        public float LimbSeverDamage => limbSeverDamage;

        public float StumpMeshRadius => Mathf.Max(0.001f, stumpMeshRadius);

        public GameObject NeckStumpPrefab => neckStumpPrefab;

        public GameObject LimbStumpPrefab => limbStumpPrefab;

        public GameObject[] FleshGibPrefabs => fleshGibPrefabs;

        public GameObject[] BoneGibPrefabs => boneGibPrefabs;

        public GameObject[] SkullGibPrefabs => skullGibPrefabs;

        public Material FleshMaterial => fleshMaterial;

        public Material BoneMaterial => boneMaterial;

        public GameObject HeadBurstPrefab => headBurstPrefab;

        public GameObject SeverBurstPrefab => severBurstPrefab;

        public int GibPoolSize => Mathf.Clamp(gibPoolSize, 4, 128);

        public int GibsPerSever => Mathf.Max(0, gibsPerSever);

        public int GibsPerHeadPop => Mathf.Max(0, gibsPerHeadPop);

        public float GibLifetimeSeconds => Mathf.Max(0.2f, gibLifetimeSeconds);

        public float GibShrinkSeconds => Mathf.Max(0.05f, gibShrinkSeconds);

        public float GibEjectSpeedMin => gibEjectSpeedMin;

        public float GibEjectSpeedMax => Mathf.Max(gibEjectSpeedMin, gibEjectSpeedMax);

        public float GibUpBias => gibUpBias;

        public float GibSpinDegrees => gibSpinDegrees;

        public float GibRadius => Mathf.Max(0.005f, gibRadius);

        public float SplatChanceHead => splatChanceHead;

        public float SplatChanceOther => splatChanceOther;

        public float SplatRayMeters => Mathf.Max(0.1f, splatRayMeters);

        public float SplatSizeMin => Mathf.Max(0.02f, splatSizeMin);

        public float SplatSizeMax => Mathf.Max(SplatSizeMin, splatSizeMax);

        public float SplatLifetimeSeconds => Mathf.Max(1f, splatLifetimeSeconds);

        public float SplatFadeSeconds => Mathf.Max(0.1f, splatFadeSeconds);

        public int SplatPoolSize => Mathf.Clamp(splatPoolSize, 4, 128);

        public float WoundSizeMin => Mathf.Max(0.01f, woundSizeMin);

        public float WoundSizeMax => Mathf.Max(WoundSizeMin, woundSizeMax);

        public int WoundsPerZombie => Mathf.Max(0, woundsPerZombie);

        public int WoundPoolSize => Mathf.Clamp(woundPoolSize, 4, 256);

        public Material BloodDecalMaterial => bloodDecalMaterial;

        public float GoreVolume => goreVolume;

        public float PainChance => painChance;

        public float PainCooldownSeconds => Mathf.Max(0f, painCooldownSeconds);

        public int PainConcurrent => Mathf.Clamp(painConcurrent, 1, 8);

        /// <summary>Death impulse (N·s) of a zone, before the weapon's damage scale.</summary>
        public float ImpulseOf(HitZone zone) => zone switch
        {
            HitZone.Head => headImpulse,
            HitZone.Stomach => stomachImpulse,
            HitZone.Leg => legImpulse,
            _ => bodyImpulse,
        };

        /// <summary>Flinch amplitude (deg) of a zone, before the per-kind scale.</summary>
        public float FlinchAmplitudeOf(HitZone zone) => zone switch
        {
            HitZone.Head => headFlinchAmplitude,
            HitZone.Stomach => stomachFlinchAmplitude,
            HitZone.Leg => legFlinchAmplitude,
            _ => bodyFlinchAmplitude,
        };

        /// <summary>Weapon scale of the death impulse: a shotgun's whole pellet pattern throws the body,
        /// a pistol round sweeps it.</summary>
        public float ImpulseScale(float frameDamage)
        {
            float reference = Mathf.Max(1f, impulseDamageReference);
            return Mathf.Clamp(frameDamage / reference, impulseScaleMin, impulseScaleMax);
        }

        /// <summary>Stump cap radius (m) for one segment.</summary>
        public float StumpRadiusOf(ZombieSegment segment) => segment switch
        {
            ZombieSegment.Neck => neckStumpRadius,
            ZombieSegment.UpperArm => upperArmStumpRadius,
            ZombieSegment.ForeArm => foreArmStumpRadius,
            ZombieSegment.Thigh => thighStumpRadius,
            ZombieSegment.Shin => shinStumpRadius,
            _ => upperArmStumpRadius,
        };

        public AudioClip RandomHeadPop() => Pick(headPopClips);

        public AudioClip RandomLimbSever() => Pick(limbSeverClips);

        public AudioClip RandomGibSplat() => Pick(gibSplatClips);

        public AudioClip RandomCrawlCollapse() => Pick(crawlCollapseClips);

        private static AudioClip Pick(AudioClip[] pool) =>
            pool == null || pool.Length == 0 ? null : pool[Random.Range(0, pool.Length)];

        private static ZombieReactionTuning live;

        /// <summary>The numbers in use: the <c>Resources</c> asset when it exists, otherwise an
        /// in-memory instance carrying the defaults above.</summary>
        /// <remarks>⚠️ No warning when the asset is missing: the defaults ARE the shipped behaviour,
        /// only the gore meshes and clips stay empty (and those are each optional).</remarks>
        public static ZombieReactionTuning Live
        {
            get
            {
                if (live != null)
                {
                    return live;
                }

                live = Resources.Load<ZombieReactionTuning>(ResourcePath);
                if (live == null)
                {
                    live = CreateInstance<ZombieReactionTuning>();
                    live.name = "ZombieReactionTuning (gömülü)";
                }

                return live;
            }
        }
    }
}
