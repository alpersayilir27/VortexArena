using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Silah tanımı (ScriptableObject): kimlik + tüm denge/his/ses değerleri.
    /// <para>
    /// <b>Ağın ve uzak çizimin gördüğü kısım tabandadır</b> (<see cref="ItemDefinition"/>):
    /// <c>netItemId</c>, prefab, <c>holdMode</c> ve kavrama pozları — eski
    /// <c>grantedHoldPosition/Euler</c> alanları oraya <c>primaryGrip*</c> olarak terfi etti
    /// (Docs/ArenaNet-Protokol.md §6.6). Burada kalan her şey <i>davranıştır</i> ve bilinçli
    /// olarak tabana çıkarılmaz.
    /// </para>
    /// <para>
    /// <b>Denge sayılarının TEK doğruluk kaynağı burasıdır ve hasar İSTEMCİ-otoriterdir</b>
    /// (Docs/ArenaNet-Protokol.md §10.3): sunucuda silah tablosu yoktur; istemci hasarı burada
    /// hesaplar — <see cref="HeadshotMultiplier"/> istemcide <c>RemoteHitBox.IsHead</c>'e
    /// uygulanır — ve <c>hit_report.damage</c> ile bildirir, sunucu aynen uygular. Bu yüzden
    /// değer değişikliği sunucuya export GEREKTİRMEZ — ama istemci build'i ister.
    /// </para>
    /// <para>
    /// <see cref="WeaponId"/> yalnız bir ETİKETTİR ("ak47" / "m4"): shot_fired ve hit_report
    /// mesajlarında taşınır, kill feed ve istatistikte görünür. Sunucu doğrulamaz, dolayısıyla
    /// yeni bir silah eklemek için sunucu tarafında hiçbir tanıtım yapılmaz.
    /// </para>
    /// <see cref="Weapon"/> bu tanımı ZORUNLU referans olarak taşır; ses klip/pitch/volume
    /// değerlerini <see cref="WeaponAudio"/> da (Configure sonrası) buradan okur.
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
        [Tooltip("Kafa vuruşunda hasar çarpanı (RemoteHitBox.IsHead — istemcide uygulanır).")]
        [SerializeField] private float headshotMultiplier = 4f;
        [Tooltip("Dakikadaki atış sayısı.")]
        [SerializeField] private float fireRateRpm = 700f;
        [Tooltip("Hitscan menzili (metre).")]
        [SerializeField] private float range = 60f;

        [Header("Saçılım (bloom)")]
        [Tooltip("Taban saçılım yarı açısı (derece, tek elle).")]
        [SerializeField] private float baseSpreadDegrees = 1f;
        [Tooltip("Her atışın saçılıma eklediği büyüme (derece).")]
        [SerializeField] private float bloomPerShotDegrees = 0.25f;
        [Tooltip("Bloom üst sınırı (derece; tabana EKLENEN kısım, taban dahil değil).")]
        [SerializeField] private float maxBloomDegrees = 2.5f;
        [Tooltip("Bloom'un saniyede toparlanma hızı (derece/sn).")]
        [SerializeField] private float bloomRecoveryPerSecond = 4f;

        [Header("Geri Tepme")]
        [Tooltip("Atış başına namlu kalkışı (derece, tek elle).")]
        [SerializeField] private float kickDegrees = 2f;
        [Tooltip("Atış başına geri itilme (metre, tek elle).")]
        [SerializeField] private float kickBackMeters = 0.02f;
        [Tooltip("Geri tepme toparlanma hızı.")]
        [SerializeField] private float recoilRecoverSpeed = 10f;

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
        [SerializeField] private AudioClip dryFireClip;
        [SerializeField] private AudioClip pickupClip;
        [Tooltip("Ateş sesinin taban pitch'i.")]
        [SerializeField] private float firePitchBase = 1f;
        [Range(0f, 0.3f)]
        [Tooltip("Atış başına rastgele pitch sapması (otomatik ateş robotik durmasın).")]
        [SerializeField] private float firePitchJitter = 0.05f;
        [Range(0f, 1f)]
        [SerializeField] private float fireVolume = 1f;

        /// <summary>Kill feed / istatistik etiketi ("ak47" / "m4") — sunucu doğrulamaz.</summary>
        public string WeaponId => weaponId;

        /// <summary>Gövde vuruşu başına hasar.</summary>
        public float Damage => damage;

        /// <summary>Kafa vuruşunda hasar çarpanı.</summary>
        public float HeadshotMultiplier => headshotMultiplier;

        /// <summary>Dakikadaki atış sayısı.</summary>
        public float FireRateRpm => fireRateRpm;

        /// <summary>Hitscan menzili (metre).</summary>
        public float Range => range;

        /// <summary>Taban saçılım yarı açısı (derece, tek elle).</summary>
        public float BaseSpreadDegrees => baseSpreadDegrees;

        /// <summary>Atış başına bloom büyümesi (derece).</summary>
        public float BloomPerShotDegrees => bloomPerShotDegrees;

        /// <summary>Bloom üst sınırı (derece; tabana eklenen kısım).</summary>
        public float MaxBloomDegrees => maxBloomDegrees;

        /// <summary>Bloom toparlanma hızı (derece/sn).</summary>
        public float BloomRecoveryPerSecond => bloomRecoveryPerSecond;

        /// <summary>Atış başına namlu kalkışı (derece, tek elle).</summary>
        public float KickDegrees => kickDegrees;

        /// <summary>Atış başına geri itilme (metre, tek elle).</summary>
        public float KickBackMeters => kickBackMeters;

        /// <summary>Geri tepme toparlanma hızı.</summary>
        public float RecoilRecoverSpeed => recoilRecoverSpeed;

        /// <summary>Şarjör kapasitesi.</summary>
        public int MagazineSize => magazineSize;

        /// <summary>Başlangıçtaki yedek şarjör sayısı.</summary>
        public int SpareMagazines => spareMagazines;

        /// <summary>Rezerv muhasebesi kuralı (bkz. <see cref="WeaponReserveMode"/>).</summary>
        public WeaponReserveMode ReserveMode => reserveMode;

        /// <summary>Şarjör değiştirme süresi (saniye).</summary>
        public float ReloadTime => reloadTime;

        /// <summary>Ateş klipleri (atış başına rastgele seçim).</summary>
        public AudioClip[] FireClips => fireClips;

        /// <summary>Şarjör çıkarma sesi.</summary>
        public AudioClip MagOutClip => magOutClip;

        /// <summary>Şarjör takma sesi.</summary>
        public AudioClip MagInClip => magInClip;

        /// <summary>Boş tetik sesi.</summary>
        public AudioClip DryFireClip => dryFireClip;

        /// <summary>Silahı eline alma sesi.</summary>
        public AudioClip PickupClip => pickupClip;

        /// <summary>Ateş sesi taban pitch'i.</summary>
        public float FirePitchBase => firePitchBase;

        /// <summary>Atış başına rastgele pitch sapması.</summary>
        public float FirePitchJitter => firePitchJitter;

        /// <summary>Ateş sesi seviyesi (0-1).</summary>
        public float FireVolume => fireVolume;

        // ⚠️ GrantedHoldPosition/GrantedHoldRotation KALDIRILDI (v4 refactor'ünün geçici köprüsüydü).
        // Duruşun tek adı ItemDefinition.PrimaryGripPosition/PrimaryGripRotation'dır ve tek olmak
        // ZORUNDA: "verilen silahın duruşu" ile "uzak tarafta çizilen duruş" aynı ölçüdür, ikinci
        // bir ad kaçınılmaz olarak birinin güncellenip diğerinin unutulmasıyla sonuçlanırdı.
        // Kavrama artık sahne silahı ile verilen silah için tek kural (Weapon.ApplyCanonicalGrip).

        /// <summary>İki atış arası en kısa süre (saniye).</summary>
        public float SecondsPerShot => 60f / Mathf.Max(1f, fireRateRpm);
    }
}
