using Oculus.Haptics;
using UnityEngine;

namespace VortexArena.Modes.Zombie
{
    /// <summary>Every number, clip and colour of the zombie mode's hit feel: spring recoil, haptic
    /// clips, 2D confirmation sounds and hit marker variants.</summary>
    /// <remarks>Lives in an asset so the feel is a data edit, not a code edit (plan: "tüm sayılar
    /// zombi SO'larında").
    /// <para>⚠️ Every clip slot is OPTIONAL: an empty slot plays nothing. The sounds arrive later and
    /// a half-filled asset must not throw.</para>
    /// <para>⚠️ Zombie mode only — <see cref="ZombieFeelInstaller"/> installs it over the shared hooks
    /// and drops it when the mode ends, so PvP keeps the weapon's own kick and pulse.</para></remarks>
    internal class ZombieFeel : ScriptableObject
    {
        /// <summary><c>Resources</c> path of the live asset (without extension).</summary>
        public const string ResourcePath = "Zombie/ZombieFeel";

        // ---- spring recoil ----

        [Header("Yaylı geri tepme")]
        [Tooltip("Yay sertliği: yüksek değer daha hızlı toparlanır.")]
        [SerializeField] private float stiffness = 220f;

        [Tooltip("Sönüm oranı. 1 = aşmasız; 0,6 hafif aşma bırakır.")]
        [SerializeField] private float dampingRatio = 0.6f;

        [Tooltip("İki elle tutuşta geri tepme çarpanı (profiller tek el referansıdır).")]
        [SerializeField] private float twoHandScale = 0.55f;

        [Tooltip("İki elli silahı tek elle tutmanın geri tepme çarpanı.")]
        [SerializeField] private float oneHandScale = 1.3f;

        [Tooltip("Biriken yukarı açının tek atış tepesine göre üst sınırı.")]
        [SerializeField] private float maxPitchKicks = 3f;

        [Header("Geri tepme profilleri (tek el referansı)")]
        [SerializeField] private ZombieRecoilProfile pistol = new ZombieRecoilProfile(6f, 1.5f, 2f, 0.025f);
        [SerializeField] private ZombieRecoilProfile magnum = new ZombieRecoilProfile(14f, 3f, 4f, 0.05f);
        [SerializeField] private ZombieRecoilProfile smg = new ZombieRecoilProfile(3f, 1.2f, 1f, 0.012f);
        [SerializeField] private ZombieRecoilProfile rifle = new ZombieRecoilProfile(4f, 1.2f, 1.5f, 0.018f);
        [SerializeField] private ZombieRecoilProfile shotgun = new ZombieRecoilProfile(12f, 3f, 3f, 0.06f);

        [Tooltip("Silah bazlı istisna; sınıf profilinin üstüne yazar.")]
        [SerializeField] private ZombieRecoilOverride[] recoilOverrides = DefaultRecoilOverrides();

        // ---- haptics ----

        [Header("Haptic")]
        [Tooltip("Kapalıyken hiçbir .haptic klibi çalmaz (titreşim yalnız silahın kendi kancalarından gelir).")]
        [SerializeField] private bool hapticEnabled = true;

        [Tooltip("Atış klibinin genlik kazancı (1 = klibin kendisi). 1'in üstü klibin gövdesini " +
                 "tavana yaklaştırır; SDK her örneği 1'de kırpar.")]
        [SerializeField] private float fireHapticGain = 2f;

        [Tooltip("Destek elinde atış klibinin genliği (kazançla çarpılır).")]
        [SerializeField] private float supportHandFireAmplitude = 0.5f;

        [Tooltip("İsabet onayının atıştan sonraki gecikmesi (s): atış klibi bitmeden çalarsa " +
                 "öncelik onu susturur.")]
        [SerializeField] private float hitConfirmDelaySeconds = 0.045f;

        [Tooltip("Atış klibinin önceliği (0 = en yüksek, 255 = en düşük).")]
        [SerializeField] private int firePriority = 112;

        [Tooltip("İsabet onayı önceliği — atıştan DÜŞÜK olmalı.")]
        [SerializeField] private int hitPriority = 160;

        [Tooltip("Öldürme / kafa patlatma / uzuv kopması önceliği.")]
        [SerializeField] private int killPriority = 96;

        [Tooltip("Oyuncunun darbe aldığı titreşimin önceliği — en yüksek olan.")]
        [SerializeField] private int hurtPriority = 64;

        [Header("Haptic klipleri (boş = titreşim yok)")]
        [SerializeField] private HapticClip firePistolHaptic;
        [SerializeField] private HapticClip fireMagnumHaptic;
        [SerializeField] private HapticClip fireSmgHaptic;
        [SerializeField] private HapticClip fireRifleHaptic;
        [SerializeField] private HapticClip fireShotgunHaptic;
        [SerializeField] private HapticClip hitConfirmHaptic;
        [SerializeField] private HapticClip hitHeadHaptic;
        [SerializeField] private HapticClip killHaptic;
        [SerializeField] private HapticClip headPopHaptic;
        [SerializeField] private HapticClip dismemberHaptic;
        [SerializeField] private HapticClip playerHurtHaptic;

        // ---- audio ----

        [Header("İsabet sesleri (2B, boş = sessiz)")]
        [SerializeField] private AudioClip[] hitTick;
        [SerializeField] private float hitTickVolume = 0.5f;
        [SerializeField] private AudioClip[] headshotCrunch;
        [SerializeField] private float headshotCrunchVolume = 0.7f;
        [SerializeField] private AudioClip[] killConfirm;
        [SerializeField] private float killConfirmVolume = 0.8f;

        [Tooltip("İsabet/öldürme seslerinde perde sapması (±).")]
        [SerializeField] private float hitPitchJitter = 0.06f;

        [Header("Oyuncu acı sesi")]
        [SerializeField] private AudioClip[] playerHurt;
        [SerializeField] private float playerHurtVolume = 0.9f;

        [Tooltip("Acı sesinin en kısa tekrar aralığı (s).")]
        [SerializeField] private float playerHurtCooldown = 0.25f;

        [Header("Yakın atış katmanı (2B, silahın 3B sesinin üstüne)")]
        [SerializeField] private AudioClip[] fireClosePistol;
        [SerializeField] private AudioClip[] fireCloseMagnum;
        [SerializeField] private AudioClip[] fireCloseSmg;
        [SerializeField] private AudioClip[] fireCloseRifle;
        [SerializeField] private AudioClip[] fireCloseShotgun;
        [SerializeField] private float fireCloseVolume = 0.35f;
        [SerializeField] private float fireClosePitchJitter = 0.04f;

        [Tooltip("2B ses havuzundaki AudioSource sayısı; dolu havuzda yeni ses düşer.")]
        [SerializeField] private int audioPoolSize = 8;

        // ---- hit markers ----

        [Header("Hitmarker varyantları")]
        [SerializeField] private Color bodyMarkerColor = new Color(1f, 0.97f, 0.88f, 1f);
        [SerializeField] private float bodyMarkerSize = 1f;
        [SerializeField] private Color headMarkerColor = new Color(1f, 0.25f, 0.2f, 1f);
        [SerializeField] private float headMarkerSize = 1.4f;
        [SerializeField] private Color killMarkerColor = new Color(1f, 0.82f, 0.25f, 1f);
        [SerializeField] private float killMarkerSize = 1.75f;
        [SerializeField] private float killMarkerLifetime = 0.5f;

        /// <summary>Weapons whose numbers do not follow their class. ⚠️ Also the asset's starting
        /// content, so a fresh clone feels the ak47 as the heavy rifle it is.</summary>
        public static ZombieRecoilOverride[] DefaultRecoilOverrides() => new[]
        {
            new ZombieRecoilOverride
            {
                weaponId = "ak47",
                profile = new ZombieRecoilProfile(5.5f, 1.5f, 1.8f, 0.022f),
            },
        };

        private static ZombieFeel live;

        /// <summary>The settings in use: the <c>Resources</c> asset when it exists, otherwise an
        /// in-memory instance carrying the defaults above.</summary>
        /// <remarks>⚠️ No asset is demanded: the mode must stay playable (recoil + markers, silent
        /// clips) before the data step has been done.</remarks>
        public static ZombieFeel Live
        {
            get
            {
                if (live != null)
                {
                    return live;
                }

                live = Resources.Load<ZombieFeel>(ResourcePath);
                if (live == null)
                {
                    live = CreateInstance<ZombieFeel>();
                    live.name = "ZombieFeel (gömülü)";
                }

                return live;
            }
        }

        public float Stiffness => Mathf.Max(1f, stiffness);

        public float DampingRatio => Mathf.Clamp(dampingRatio, 0.05f, 0.99f);

        public float TwoHandScale => Mathf.Max(0f, twoHandScale);

        public float OneHandScale => Mathf.Max(0f, oneHandScale);

        public float MaxPitchKicks => Mathf.Max(1f, maxPitchKicks);

        public bool HapticEnabled => hapticEnabled;

        public float FireHapticGain => Mathf.Max(0f, fireHapticGain);

        public float SupportHandFireAmplitude => Mathf.Clamp(supportHandFireAmplitude, 0f, 1f);

        public float HitConfirmDelaySeconds => Mathf.Max(0f, hitConfirmDelaySeconds);

        public uint FirePriority => ClampPriority(firePriority);

        public uint HitPriority => ClampPriority(hitPriority);

        public uint KillPriority => ClampPriority(killPriority);

        public uint HurtPriority => ClampPriority(hurtPriority);

        public AudioClip[] HitTick => hitTick;

        public float HitTickVolume => Mathf.Max(0f, hitTickVolume);

        public AudioClip[] HeadshotCrunch => headshotCrunch;

        public float HeadshotCrunchVolume => Mathf.Max(0f, headshotCrunchVolume);

        public AudioClip[] KillConfirm => killConfirm;

        public float KillConfirmVolume => Mathf.Max(0f, killConfirmVolume);

        public float HitPitchJitter => Mathf.Max(0f, hitPitchJitter);

        public AudioClip[] PlayerHurt => playerHurt;

        public float PlayerHurtVolume => Mathf.Max(0f, playerHurtVolume);

        public float PlayerHurtCooldown => Mathf.Max(0f, playerHurtCooldown);

        public float FireCloseVolume => Mathf.Max(0f, fireCloseVolume);

        public float FireClosePitchJitter => Mathf.Max(0f, fireClosePitchJitter);

        public int AudioPoolSize => Mathf.Clamp(audioPoolSize, 1, 32);

        public Color BodyMarkerColor => bodyMarkerColor;

        public float BodyMarkerSize => Mathf.Max(0.01f, bodyMarkerSize);

        public Color HeadMarkerColor => headMarkerColor;

        public float HeadMarkerSize => Mathf.Max(0.01f, headMarkerSize);

        public Color KillMarkerColor => killMarkerColor;

        public float KillMarkerSize => Mathf.Max(0.01f, killMarkerSize);

        public float KillMarkerLifetime => Mathf.Max(0f, killMarkerLifetime);

        /// <summary>Recoil amounts of <paramref name="weaponId"/>: the per-weapon override when the
        /// table lists it, otherwise the class profile.</summary>
        public ZombieRecoilProfile GetRecoil(string weaponId, ZombieWeaponClass weaponClass)
        {
            if (!string.IsNullOrEmpty(weaponId) && recoilOverrides != null)
            {
                for (int i = 0; i < recoilOverrides.Length; i++)
                {
                    if (string.Equals(recoilOverrides[i].weaponId, weaponId,
                            System.StringComparison.OrdinalIgnoreCase))
                    {
                        return recoilOverrides[i].profile;
                    }
                }
            }

            switch (weaponClass)
            {
                case ZombieWeaponClass.Pistol: return pistol;
                case ZombieWeaponClass.Magnum: return magnum;
                case ZombieWeaponClass.Smg: return smg;
                case ZombieWeaponClass.Shotgun: return shotgun;
                default: return rifle;
            }
        }

        /// <summary>The fire haptic as an event id, so the player cache keys one class to one slot.</summary>
        public static ZombieHapticEvent FireEvent(ZombieWeaponClass weaponClass)
        {
            switch (weaponClass)
            {
                case ZombieWeaponClass.Pistol: return ZombieHapticEvent.FirePistol;
                case ZombieWeaponClass.Magnum: return ZombieHapticEvent.FireMagnum;
                case ZombieWeaponClass.Smg: return ZombieHapticEvent.FireSmg;
                case ZombieWeaponClass.Shotgun: return ZombieHapticEvent.FireShotgun;
                default: return ZombieHapticEvent.FireRifle;
            }
        }

        public HapticClip GetHaptic(ZombieHapticEvent id)
        {
            switch (id)
            {
                case ZombieHapticEvent.FirePistol: return firePistolHaptic;
                case ZombieHapticEvent.FireMagnum: return fireMagnumHaptic;
                case ZombieHapticEvent.FireSmg: return fireSmgHaptic;
                case ZombieHapticEvent.FireRifle: return fireRifleHaptic;
                case ZombieHapticEvent.FireShotgun: return fireShotgunHaptic;
                case ZombieHapticEvent.HitConfirm: return hitConfirmHaptic;
                case ZombieHapticEvent.HitHead: return hitHeadHaptic;
                case ZombieHapticEvent.Kill: return killHaptic;
                case ZombieHapticEvent.HeadPop: return headPopHaptic;
                case ZombieHapticEvent.Dismember: return dismemberHaptic;
                case ZombieHapticEvent.PlayerHurt: return playerHurtHaptic;
                default: return null;
            }
        }

        /// <summary>Priority of a cue (0 = highest). Fire outranks the hit tick on the same hand.</summary>
        public uint GetPriority(ZombieHapticEvent id)
        {
            switch (id)
            {
                case ZombieHapticEvent.HitConfirm:
                case ZombieHapticEvent.HitHead:
                    return HitPriority;
                case ZombieHapticEvent.Kill:
                case ZombieHapticEvent.HeadPop:
                case ZombieHapticEvent.Dismember:
                    return KillPriority;
                case ZombieHapticEvent.PlayerHurt:
                    return HurtPriority;
                default:
                    return FirePriority;
            }
        }

        public AudioClip[] GetFireClose(ZombieWeaponClass weaponClass)
        {
            switch (weaponClass)
            {
                case ZombieWeaponClass.Pistol: return fireClosePistol;
                case ZombieWeaponClass.Magnum: return fireCloseMagnum;
                case ZombieWeaponClass.Smg: return fireCloseSmg;
                case ZombieWeaponClass.Shotgun: return fireCloseShotgun;
                default: return fireCloseRifle;
            }
        }

        private static uint ClampPriority(int value) => (uint)Mathf.Clamp(value, 0, 255);
    }
}
