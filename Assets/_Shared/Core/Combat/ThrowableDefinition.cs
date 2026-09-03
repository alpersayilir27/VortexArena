using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Throwable definition (bomb today; molotov / flashbang / smoke later): identity + fuse, flight,
    /// holster and effect values.
    /// <para>What the network and remote drawing need lives in the base
    /// (<see cref="ItemDefinition"/>): <c>netItemId</c>, prefab, hold mode, grip records. Everything
    /// here is BEHAVIOUR — the same split as <see cref="WeaponDefinition"/>.</para>
    /// <para><b>A new throwable type costs NOTHING on the wire</b> (Docs/ArenaNet-Protokol.md §6.4):
    /// the throw event carries <c>itemId</c>, so every type travels through the same 9 bytes and is
    /// told apart by its <c>netItemId</c>. Adding one means a definition + a catalog entry, never a
    /// protocol version bump.</para>
    /// <para>⚠️ <b>Damage is client-authoritative</b> (§10.3): the blast is computed by the THROWER's
    /// client and reported per target; the server has no throwable table.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "Throwable", menuName = "VortexArena/Throwable Definition")]
    public class ThrowableDefinition : ItemDefinition
    {
        [Header("Kimlik")]
        [Tooltip("Kill feed / istatistik etiketi (\"bomba\"). Sunucu doğrulamaz.")]
        [SerializeField] private string throwableId = "";

        [Header("Tetik")]
        [Tooltip("Fuse: bırakma anından itibaren sayar (bomba). Impact: statik geometriye ilk temasta (molotof).")]
        [SerializeField] private ThrowableTrigger trigger = ThrowableTrigger.Fuse;

        // ⚠️ Counted from the RELEASE, never from the pickup: there is no "cooking". A fuse that ran in
        // the hand would need the hold time on the wire — the event carries none.
        [Tooltip("Fitil süresi (saniye) — BIRAKMA anından sayılır, elde tutarken işlemez.")]
        [SerializeField] private float fuseSeconds = 5f;

        [Header("Kılıf")]
        // ⚠️ Measured from the EXPLOSION, not the throw: the wrist stays empty through flight + fuse, so
        // the real gap between two throws is fuseSeconds + this.
        [Tooltip("Patlamadan kaç saniye sonra bileklikte yenisi belirir. Mermi sınırı YOKTUR, tek sınır budur.")]
        [SerializeField] private float holsterRefillSeconds = 3f;

        [Header("Atış")]
        [Tooltip("Kumanda hızının çarpanı (his ayarı).")]
        [SerializeField] private float throwSpeedScale = 1f;

        // The wire carries speed as a ushort in cm/s, so the ceiling is 655 m/s — this clamp is about
        // feel, not the format.
        [Tooltip("Üst hız sınırı (m/sn). Kolun savurması bunu aşamaz.")]
        [SerializeField] private float maxThrowSpeed = 12f;

        // ⚠️ DERIVED from the throw direction, never random: hand rotation is not on the wire, so a
        // random spin would make every client's copy tumble differently and land somewhere else.
        [Tooltip("Havadaki dönüş hızı (derece/sn). Ekseni atış yönünden türetilir — rastgele DEĞİLDİR.")]
        [SerializeField] private float spinDegreesPerSecond = 180f;

        [Header("Uçuş")]
        // ⚠️ Avatars are excluded by the flight code itself (it ignores every RemoteHitBox / local body
        // collider): remote bodies sit in a different place on every client, so a bounce off one would
        // split the copies. This mask is the second net, for scene volumes that are not geometry.
        [Tooltip("Çarpışmadan geçilecek layer'lar (UI, tetikleyici hacimler…). Avatarlar zaten kod tarafından hariç tutulur.")]
        [SerializeField] private LayerMask excludedLayers;

        [Header("Patlama")]
        [Tooltip("Etki yarıçapı (metre).")]
        [SerializeField] private float blastRadius = 4f;

        [Tooltip("Merkezdeki hasar. Kenara doğru edgeScale ile lineer düşer.")]
        [SerializeField] private float blastDamage = 120f;

        [Tooltip("Yarıçapın kenarındaki hasar çarpanı (0.25 = merkezin dörtte biri).")]
        [SerializeField] private float edgeScale = 0.25f;

        [Tooltip("Açıkken siper sayılır: sağlam engel hasarı tümden keser, KIRILABİLIR siper yalnız " +
                 "kalan canı kadarını soğurur, gerisi arkasındakine geçer. Saha ayarı.")]
        [SerializeField] private bool requireLineOfSight;

        [Header("Sunum")]
        [Tooltip("Patlama efekti prefabı (kendi kendini temizler).")]
        [SerializeField] private GameObject explosionPrefab;

        [Tooltip("Patlama sesi.")]
        [SerializeField] private AudioClip explosionClip;

        [Range(0f, 1f)]
        [SerializeField] private float explosionVolume = 1f;

        /// <summary>Kill feed / stats label — never validated by the server.</summary>
        public string ThrowableId => throwableId;

        /// <summary>What sets it off (fuse or impact).</summary>
        public ThrowableTrigger Trigger => trigger;

        /// <summary>Fuse seconds, counted from the release.</summary>
        public float FuseSeconds => Mathf.Max(0f, fuseSeconds);

        /// <summary>Seconds after the blast until the wrist holster refills.</summary>
        public float HolsterRefillSeconds => Mathf.Max(0f, holsterRefillSeconds);

        /// <summary>Controller speed multiplier at release.</summary>
        public float ThrowSpeedScale => Mathf.Max(0f, throwSpeedScale);

        /// <summary>Upper bound of the release speed (m/s).</summary>
        public float MaxThrowSpeed => Mathf.Max(0.1f, maxThrowSpeed);

        /// <summary>Tumble rate in flight (deg/s); the axis is derived from the throw direction.</summary>
        public float SpinDegreesPerSecond => spinDegreesPerSecond;

        /// <summary>Layers the flight passes through (second net; avatars are excluded in code).</summary>
        public LayerMask ExcludedLayers => excludedLayers;

        /// <summary>Blast radius (m).</summary>
        public float BlastRadius => Mathf.Max(0f, blastRadius);

        /// <summary>Damage at the centre of the blast.</summary>
        public float BlastDamage => Mathf.Max(0f, blastDamage);

        /// <summary>Damage multiplier at the edge of the radius.</summary>
        public float EdgeScale => Mathf.Clamp01(edgeScale);

        /// <summary>Should cover count — solid blocks, breakable absorbs its remaining health.</summary>
        public bool RequireLineOfSight => requireLineOfSight;

        /// <summary>Explosion effect prefab (may be unassigned).</summary>
        public GameObject ExplosionPrefab => explosionPrefab;

        /// <summary>Explosion sound (may be unassigned).</summary>
        public AudioClip ExplosionClip => explosionClip;

        /// <summary>Explosion sound volume.</summary>
        public float ExplosionVolume => Mathf.Clamp01(explosionVolume);
    }
}
