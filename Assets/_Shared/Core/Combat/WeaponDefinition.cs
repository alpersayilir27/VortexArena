using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Weapon definition (ScriptableObject): identity + all balance/feel/audio values.
    /// <para>What the network and remote rendering see lives in the base
    /// (<see cref="ItemDefinition"/>): <c>netItemId</c>, prefab, <c>holdMode</c> and grip poses (the
    /// old <c>grantedHoldPosition/Euler</c> were promoted there as <c>primaryGrip*</c>,
    /// Docs/ArenaNet-Protokol.md §6.6). Everything remaining here is BEHAVIOUR and is deliberately
    /// not promoted.</para>
    /// <para>SINGLE source of truth for balance numbers, and damage is CLIENT-authoritative
    /// (Docs/ArenaNet-Protokol.md §10.3): the server has no weapon table. The client computes damage
    /// here — the zone multiplier picked from <c>RemoteHitBox.Zone</c>
    /// (<see cref="GetZoneMultiplier"/>) — and reports it in <c>hit_report.damage</c>, which the
    /// server applies as-is. So changing a value needs NO server export, but does need a client
    /// build.</para>
    /// <para><see cref="WeaponId"/> is only a LABEL ("ak47" / "m4"): carried in shot_fired and
    /// hit_report, shown in the kill feed and stats. The server never validates it, so adding a
    /// weapon requires no server-side registration.</para>
    /// <see cref="Weapon"/> carries this definition as a MANDATORY reference;
    /// <see cref="WeaponAudio"/> reads clip/pitch/volume from it after Configure.
    /// </summary>
    [CreateAssetMenu(fileName = "Weapon", menuName = "VortexArena/Weapon Definition")]
    public class WeaponDefinition : ItemDefinition
    {
        [Header("Kimlik")]
        [Tooltip("Kill feed / istatistik etiketi. Sunucu doğrulamaz, serbestçe seçilebilir.")]
        [SerializeField] private string weaponId = "";

        [Header("Vuruş")]
        [Tooltip("Gövde vuruşu başına hasar (istemci hesaplar, hit_report.damage ile gider).")]
        [SerializeField] private float damage = 25f;
        [Tooltip("Kafa vuruşunda hasar çarpanı (RemoteHitBox.Zone — istemcide uygulanır).")]
        [SerializeField] private float headshotMultiplier = 4f;
        [Tooltip("Karın/leğen vuruşunda hasar çarpanı (CS2: 1.25).")]
        [SerializeField] private float stomachMultiplier = 1.25f;
        [Tooltip("Bacak vuruşunda hasar çarpanı (CS2: 0.75). Kollar GÖVDE sayılır, çarpanı 1'dir.")]
        [SerializeField] private float legMultiplier = 0.75f;
        [Tooltip("Dakikadaki atış sayısı.")]
        [SerializeField] private float fireRateRpm = 700f;
        [Tooltip("Hitscan menzili (metre).")]
        [SerializeField] private float range = 60f;
        [Tooltip("Tek tetik çekişinde atılan ışın sayısı. 1 = normal silah, >1 = saçmalı " +
                 "(her saçma AYRI hasar taşır ve ayrı hit_report üretir).")]
        [SerializeField] private int pelletCount = 1;

        [Header("Saçılım (bloom)")]
        [Tooltip("HAM taban saçılım yarı açısı (derece) — sahadaki koni DEĞİL: kavrayış çarpanıyla " +
                 "ölçeklenir (iki elde Weapon.twoHandSpreadMultiplier, tek elde ayrıca " +
                 "oneHandSpreadMultiplier).")]
        [SerializeField] private float baseSpreadDegrees = 1f;
        [Tooltip("Her atışın saçılıma eklediği büyüme (derece).")]
        [SerializeField] private float bloomPerShotDegrees = 0.25f;
        [Tooltip("Bloom üst sınırı (derece; tabana EKLENEN kısım, taban dahil değil).")]
        [SerializeField] private float maxBloomDegrees = 2.5f;
        [Tooltip("Bloom'un saniyede toparlanma hızı (derece/sn).")]
        [SerializeField] private float bloomRecoveryPerSecond = 4f;

        [Header("Geri Tepme")]
        [Tooltip("HAM atış başına namlu kalkışı (derece) — kavrayış çarpanıyla ölçeklenir (iki elde " +
                 "Weapon.twoHandRecoilMultiplier, tek elde ayrıca oneHandRecoilMultiplier).")]
        [SerializeField] private float kickDegrees = 2f;
        [Tooltip("HAM atış başına geri itilme (metre) — kickDegrees ile aynı kavrayış çarpanını yer.")]
        [SerializeField] private float kickBackMeters = 0.02f;
        [Tooltip("Geri tepme toparlanma hızı (iki el). Tek elde oneHandRecoveryPenalty ile yavaşlar.")]
        [SerializeField] private float recoilRecoverSpeed = 10f;

        [Header("Tek El Cezası")]
        [Min(1f)]
        [Tooltip("Tek elle tutarken saçılım çarpanı. 1 = iki elle aynı, 1.5 = %50 daha geniş koni.")]
        [SerializeField] private float oneHandSpreadMultiplier = 1.5f;
        [Min(1f)]
        [Tooltip("Tek elle tutarken geri tepme çarpanı. 1 = iki elle aynı, 2 = iki katı kalkış.")]
        [SerializeField] private float oneHandRecoilMultiplier = 1.5f;
        [Range(0.1f, 1f)]
        [Tooltip("Tek elle toparlanma hızı çarpanı. 0.75 = %25 daha yavaş toparlanma.")]
        [SerializeField] private float oneHandRecoveryPenalty = 0.75f;

        [Header("Haptik")]
        [Range(0f, 1f)]
        [Tooltip("Atış başına kumanda titreşiminin şiddeti (0 = haptik kapalı).")]
        [SerializeField] private float hapticAmplitude = 0.8f;
        [Min(0f)]
        [Tooltip("Atış başına titreşimin süresi (saniye). 0 = haptik kapalı.")]
        [SerializeField] private float hapticDuration = 0.05f;

        [Header("Cephane")]
        [SerializeField] private int magazineSize = 30;
        [Tooltip("Başlangıçtaki yedek şarjör sayısı (rezerv = spareMagazines × magazineSize).")]
        [SerializeField] private int spareMagazines = 2;
        [Tooltip("DiscardMagazine: erken reload'da şarjörde kalan YANAR (varsayılan ürün kuralı). " +
                 "PoolRounds: CS2 tarzı mermi havuzu, kayıp yok.")]
        [SerializeField] private WeaponReserveMode reserveMode = WeaponReserveMode.DiscardMagazine;
        [Tooltip("Şarjör değiştirme süresi (saniye).")]
        [SerializeField] private float reloadTime = 2.4f;

        [Header("Ses")]
        [Tooltip("Ateş klipleri; her atışta rastgele biri seçilir.")]
        [SerializeField] private AudioClip[] fireClips;
        [Tooltip("Şarjör çıkarma sesi (WeaponAnimator zaman çizgisi çalar).")]
        [SerializeField] private AudioClip magOutClip;
        [Tooltip("Şarjör takma sesi (WeaponAnimator zaman çizgisi çalar).")]
        [SerializeField] private AudioClip magInClip;
        [Tooltip("Pompalı gibi TEK TEK fişek dolduran silah: MagOutClip reload boyunca şarjör " +
                 "kapasitesi kadar kez, eşit aralıkla çalınır (klip tek fişeğin sesi olmalı). " +
                 "Kapalıyken klip reload başında bir kez çalar.")]
        [SerializeField] private bool perShellReloadAudio;
        [SerializeField] private AudioClip dryFireClip;
        [SerializeField] private AudioClip pickupClip;
        [Tooltip("Ateş sesinin taban pitch'i.")]
        [SerializeField] private float firePitchBase = 1f;
        [Range(0f, 0.3f)]
        [Tooltip("Atış başına rastgele pitch sapması (otomatik ateş robotik durmasın).")]
        [SerializeField] private float firePitchJitter = 0.05f;
        [Range(0f, 1f)]
        [SerializeField] private float fireVolume = 1f;

        /// <summary>Kill feed / stats label ("ak47" / "m4") — never validated by the server.</summary>
        public string WeaponId => weaponId;

        /// <summary>Damage per body hit.</summary>
        public float Damage => damage;

        /// <summary>Damage multiplier on a head hit.</summary>
        public float HeadshotMultiplier => headshotMultiplier;

        /// <summary>Damage multiplier on a stomach/pelvis hit.</summary>
        public float StomachMultiplier => stomachMultiplier;

        /// <summary>Damage multiplier on a leg hit (arms count as body, multiplier 1).</summary>
        public float LegMultiplier => legMultiplier;

        /// <summary>The zone's damage multiplier — APPLYING it is the caller's job (damage is
        /// client-authoritative).</summary>
        public float GetZoneMultiplier(HitZone zone) => zone switch
        {
            HitZone.Head => headshotMultiplier,
            HitZone.Stomach => stomachMultiplier,
            HitZone.Leg => legMultiplier,
            _ => 1f,
        };

        /// <summary>Rounds per minute.</summary>
        public float FireRateRpm => fireRateRpm;

        /// <summary>Hitscan range (metres).</summary>
        public float Range => range;

        /// <summary>
        /// Rays fired per trigger pull (&gt;1 on shotguns). ⚠️ Damage is PER PELLET, not total:
        /// <see cref="Damage"/> × pellets that hit. There is NO distance falloff curve and none is
        /// added — a shotgun's range identity is carried by <see cref="BaseSpreadDegrees"/> (the
        /// cone widens and most pellets miss). A second distance curve would make the same
        /// behaviour tunable from two places.
        /// </summary>
        public int PelletCount => Mathf.Max(1, pelletCount);

        /// <summary>RAW base spread half-angle (degrees) — the grip scale is NOT in it.
        /// <para>The cone in the field is always this times a grip factor
        /// (<see cref="Weapon.GripSpreadScale"/>): two-handed is the balanced reference, one-handed
        /// stacks <see cref="OneHandSpreadMultiplier"/> on top. Reading this number as "the cone the
        /// player sees" makes every weapon look tighter than it is.</para></summary>
        public float BaseSpreadDegrees => baseSpreadDegrees;

        /// <summary>Bloom growth per shot (degrees).</summary>
        public float BloomPerShotDegrees => bloomPerShotDegrees;

        /// <summary>Bloom cap (degrees; the part added to the base).</summary>
        public float MaxBloomDegrees => maxBloomDegrees;

        /// <summary>Bloom recovery rate (degrees/s).</summary>
        public float BloomRecoveryPerSecond => bloomRecoveryPerSecond;

        /// <summary>RAW muzzle rise per shot (degrees) — scaled by the grip
        /// (<see cref="Weapon.GripRecoilScale"/>), like <see cref="BaseSpreadDegrees"/>.</summary>
        public float KickDegrees => kickDegrees;

        /// <summary>RAW push-back per shot (metres) — eats the same grip scale as
        /// <see cref="KickDegrees"/>.</summary>
        public float KickBackMeters => kickBackMeters;

        /// <summary>Recoil recovery speed for the TWO-HANDED reference grip; one-handed it is
        /// multiplied by <see cref="OneHandRecoveryPenalty"/>.</summary>
        public float RecoilRecoverSpeed => recoilRecoverSpeed;

        /// <summary>
        /// STABILITY PENALTY of a one-handed hold — spread. Two-handed is the 1.0 baseline, so this
        /// is a ratio against it: 1.5 = a 50% wider cone one-handed.
        /// <para>⚠️ Clamped to <c>&gt;= 1</c>: a one-handed hold must never be BETTER than a
        /// two-handed one. The penalty is per weapon on purpose — an SMG is far easier to hold in one
        /// hand than a rifle, and one shared number could only be right for one of them.</para>
        /// </summary>
        public float OneHandSpreadMultiplier => Mathf.Max(1f, oneHandSpreadMultiplier);

        /// <summary>STABILITY PENALTY of a one-handed hold — recoil (kick + push-back), as a ratio
        /// against the two-handed 1.0 baseline. Clamped to <c>&gt;= 1</c>.</summary>
        public float OneHandRecoilMultiplier => Mathf.Max(1f, oneHandRecoilMultiplier);

        /// <summary>Recovery multiplier of a one-handed hold: <c>&lt; 1</c> = the muzzle settles
        /// slower (0.75 = 25% slower). Clamped to <c>0.1 - 1</c>.</summary>
        public float OneHandRecoveryPenalty => Mathf.Clamp(oneHandRecoveryPenalty, 0.1f, 1f);

        /// <summary>
        /// Controller vibration strength per shot (0-1). <c>0</c> = haptics off.
        /// <para>⚠️ Haptics are the WEAPON's own data and live only here: the pulse is read from
        /// this definition, not the <see cref="Weapon"/> prefab, so a scene instance and a granted
        /// (<c>weaponSource:"random"</c>) clone of the same weapon feel identical.</para>
        /// </summary>
        public float HapticAmplitude => hapticAmplitude;

        /// <summary>Vibration duration per shot (seconds). <c>0</c> = haptics off.</summary>
        public float HapticDuration => hapticDuration;

        /// <summary>Magazine capacity.</summary>
        public int MagazineSize => magazineSize;

        /// <summary>Starting spare magazine count.</summary>
        public int SpareMagazines => spareMagazines;

        /// <summary>Reserve accounting rule (see <see cref="WeaponReserveMode"/>).</summary>
        public WeaponReserveMode ReserveMode => reserveMode;

        /// <summary>Magazine change duration (seconds).</summary>
        public float ReloadTime => reloadTime;

        /// <summary>Fire clips (one picked at random per shot).</summary>
        public AudioClip[] FireClips => fireClips;

        /// <summary>Magazine-out sound.</summary>
        public AudioClip MagOutClip => magOutClip;

        /// <summary>Magazine-in sound.</summary>
        public AudioClip MagInClip => magInClip;

        /// <summary>
        /// When on, <see cref="MagOutClip"/> plays <see cref="MagazineSize"/> times spread over the
        /// reload (shell-by-shell loading). The clip must be a single shell's sound.
        /// </summary>
        public bool PerShellReloadAudio => perShellReloadAudio;

        /// <summary>Dry fire sound.</summary>
        public AudioClip DryFireClip => dryFireClip;

        /// <summary>Weapon pickup sound.</summary>
        public AudioClip PickupClip => pickupClip;

        /// <summary>Base pitch of the fire sound.</summary>
        public float FirePitchBase => firePitchBase;

        /// <summary>Random pitch jitter per shot.</summary>
        public float FirePitchJitter => firePitchJitter;

        /// <summary>Fire sound level (0-1).</summary>
        public float FireVolume => fireVolume;

        // ⚠️ There is NO pose field here and none is opened: the single name is the grip record in
        // ItemDefinition (GetGrip / PrimaryGripPosition(bool) / GripJointRotations) and it MUST stay
        // single — "the granted weapon's pose" and "the pose drawn remotely" are the same measure,
        // and a second name inevitably means one gets updated and the other forgotten. Scene and
        // granted weapons also go through one rule (Weapon.ApplyCanonicalGrip).

        /// <summary>Minimum time between two shots (seconds).</summary>
        public float SecondsPerShot => 60f / Mathf.Max(1f, fireRateRpm);
    }
}
