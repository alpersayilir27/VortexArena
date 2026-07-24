using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Silah tanımı (ScriptableObject): silahın protokol kimliği + istatistikleri.
    /// <para>
    /// <see cref="WeaponId"/> PROTOKOL ANAHTARIDIR ("ak47" / "m4") — shot_fired ve
    /// hit_report mesajlarında bu string taşınır. Sunucudaki <c>config/weapons.json</c>
    /// ile ELLE senkron tutulur (Docs/ArenaNet-Protokol.md §10.3): iki taraf sapınca
    /// hasar sunucu tablosundan uygulanır ve uyumsuzluk loglanır.
    /// </para>
    /// <see cref="Weapon"/> bileşeni, kendisine bir tanım atanmışsa istatistiklerini
    /// Awake'te buradan okur (Inspector değerleri yedek kalır).
    /// </summary>
    [CreateAssetMenu(fileName = "Weapon", menuName = "VortexArena/Weapon Definition")]
    public class WeaponDefinition : ScriptableObject
    {
        [Header("Kimlik")]
        [Tooltip("Protokol anahtarı — sunucudaki weapons.json ile birebir aynı olmalı.")]
        [SerializeField] private string weaponId = "";
        [SerializeField] private string displayName = "";

        [Header("İstatistikler")]
        [Tooltip("Vuruş başına hasar (sunucu tablosuyla aynı olmalı).")]
        [SerializeField] private float damage = 25f;
        [Tooltip("Dakikadaki atış sayısı; sunucu rate-limit'i de bunu kullanır.")]
        [SerializeField] private float fireRateRpm = 700f;
        [SerializeField] private float range = 60f;
        [Tooltip("Tek elle tutuşta mermi saçılımı yarı açısı (derece).")]
        [SerializeField] private float spreadDegrees = 1f;

        [Header("Cephane")]
        [SerializeField] private int magazineSize = 30;
        [SerializeField] private float reloadTime = 2.4f;

        [Header("Referanslar")]
        [Tooltip("Silah prefabı (opsiyonel; loadout kurulumları için).")]
        [SerializeField] private GameObject prefab;

        /// <summary>Protokol anahtarı ("ak47" / "m4").</summary>
        public string WeaponId => weaponId;

        /// <summary>Arayüzde gösterilen ad.</summary>
        public string DisplayName => displayName;

        /// <summary>Vuruş başına hasar.</summary>
        public float Damage => damage;

        /// <summary>Dakikadaki atış sayısı.</summary>
        public float FireRateRpm => fireRateRpm;

        /// <summary>Hitscan menzili (metre).</summary>
        public float Range => range;

        /// <summary>Saçılım yarı açısı (derece, tek elle).</summary>
        public float SpreadDegrees => spreadDegrees;

        /// <summary>Şarjör kapasitesi.</summary>
        public int MagazineSize => magazineSize;

        /// <summary>Şarjör değiştirme süresi (saniye).</summary>
        public float ReloadTime => reloadTime;

        /// <summary>Silah prefabı (atanmamış olabilir).</summary>
        public GameObject Prefab => prefab;

        /// <summary>İki atış arası en kısa süre (saniye) — sunucudaki rate-limit formülüyle aynı.</summary>
        public float SecondsPerShot => 60f / Mathf.Max(1f, fireRateRpm);
    }
}
