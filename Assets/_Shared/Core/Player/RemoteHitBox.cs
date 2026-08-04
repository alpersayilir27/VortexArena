using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Uzak oyuncu avatarının vurulabilir parçası (kafa / gövde / karın / bacak collider'ı).
    /// <para>
    /// Weapon raycast'i bu bileşene değerse hedef bir AĞ OYUNCUSUDUR:
    /// hasar YEREL uygulanmaz, sunucuya <c>hit_report</c> gönderilir ve
    /// <c>health_update</c> beklenir (Docs/ArenaNet-Protokol.md §10.3).
    /// </para>
    /// Bölge çarpanı istemcide <see cref="VortexArena.Core.Combat.Weapon"/> tarafından uygulanır
    /// (hasar istemci-otoriter, <c>hit_report.damage</c>; §10.3) — <see cref="Zone"/> o çarpanın
    /// kaynağıdır, sayısı <c>WeaponDefinition.GetZoneMultiplier</c>'dan gelir.
    /// </summary>
    public class RemoteHitBox : MonoBehaviour
    {
        /// <summary>Kafa — çarpanı en yüksek bölge olduğu için en dikkat çeken renk.</summary>
        private static readonly Color HeadColor = new Color(1f, 0.25f, 0.2f, 0.9f);

        /// <summary>Karın/leğen.</summary>
        private static readonly Color StomachColor = new Color(1f, 0.6f, 0.1f, 0.9f);

        /// <summary>Bacaklar.</summary>
        private static readonly Color LegColor = new Color(1f, 0.95f, 0.25f, 0.9f);

        /// <summary>Göğüs ve kollar (referans hasar).</summary>
        private static readonly Color BodyColor = new Color(0.35f, 1f, 0.45f, 0.9f);

        [Tooltip("Boş bırakılırsa üst hiyerarşiden otomatik bulunur.")]
        [SerializeField] private RemoteAvatar avatar;

        // ⚠️ Kutular ELLE bakılır (üreten bir araç yoktur): kemiğe yeni bir kutu asan kişi bu
        // bileşeni eklemek ve bölgesini SEÇMEK zorundadır. Varsayılan Body'dir, yani unutulan bir
        // kafa kutusu sessizce 4× yerine 1× hasar verir — sahada "kafadan vurdum ama ölmedi" diye
        // okunur ve teşhisi pahalıdır.
        [Tooltip("Vuruş bölgesi — hasar çarpanının kaynağı (kafa 4×, karın 1.25×, bacak 0.75×).")]
        [SerializeField] private HitZone zone = HitZone.Body;

        /// <summary>Bu hitbox'ın ait olduğu avatar.</summary>
        public RemoteAvatar Avatar => avatar;

        /// <summary>Avatarın oyuncu id'si; avatar yoksa 0 (geçersiz hedef).</summary>
        public int PlayerId => avatar != null ? avatar.PlayerId : 0;

        /// <summary>Vuruş bölgesi; çarpanı UYGULAMAK çağıranın işidir (hasar istemci-otoriter).</summary>
        public HitZone Zone => zone;

        /// <summary>Kafa bölgesi mi — <see cref="Zone"/> üzerinden türer.</summary>
        public bool IsHead => zone == HitZone.Head;

        private void Reset()
        {
            if (avatar == null)
            {
                avatar = GetComponentInParent<RemoteAvatar>(true);
            }
        }

        private void Awake()
        {
            if (avatar == null)
            {
                avatar = GetComponentInParent<RemoteAvatar>(true);
            }

            if (avatar == null)
            {
                Debug.LogWarning($"[RemoteHitBox] '{name}' bir RemoteAvatar altında değil; vuruşlar raporlanamaz.", this);
            }
        }

        // ------------------------------------------------------------------ gizmo

        /// <summary>
        /// Kutunun gerçek (collider'dan okunan) tel kafesi. ⚠️ <c>OnDrawGizmosSelected</c> DEĞİL —
        /// gerekçe <see cref="VortexArena.Core.Combat.GripSocketMarker"/>'daki ile aynı: kutu
        /// ayarlanırken çoğu zaman seçili olan başka bir şey oluyor (kemik, karakter kökü) ve
        /// kutunun nerede olduğu o anda da görünmeli.
        /// </summary>
        private void OnDrawGizmos()
        {
            var collider = GetComponent<Collider>();
            if (collider == null)
            {
                // Kutu henüz kurulmamış olabilir; sessiz kal — gizmo bir uyarı kanalı değil.
                return;
            }

            // ⚠️ Burada Gizmos.matrix KULLANILIR ve bu, GripSocketMarker'daki "matris kullanma"
            // notunun TERSİDİR — ikisi de kendi yerinde doğru: kavrama ofsetleri hiçbir zaman
            // ölçeklenmiyor (metre cinsinden okunmalı), oysa collider ölçüleri transform ölçeğiyle
            // GERÇEKTEN ölçekleniyor (kemik kökü oyuncunun boyuyla ölçekleniyor). Matrissiz çizilen
            // tel kafes gerçek collider'ı yanlış gösterir ve elle ayar yaparken yanlış yere bakılır.
            Matrix4x4 previous = Gizmos.matrix;
            Color previousColor = Gizmos.color;

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = ZoneColor(zone);

            if (collider is SphereCollider sphere)
            {
                Gizmos.DrawWireSphere(sphere.center, sphere.radius);
            }
            else if (collider is CapsuleCollider capsule)
            {
                DrawWireCapsule(capsule);
            }

            Gizmos.matrix = previous;
            Gizmos.color = previousColor;
        }

        private static Color ZoneColor(HitZone value)
        {
            switch (value)
            {
                case HitZone.Head: return HeadColor;
                case HitZone.Stomach: return StomachColor;
                case HitZone.Leg: return LegColor;
                default: return BodyColor;
            }
        }

        /// <summary>
        /// Unity'de hazır tel kapsül gizmo'su YOKTUR: iki uç küresi + onları birleştiren dört
        /// çizgiyle kuruluyor. Uçlar merkezden <c>height/2 - radius</c> kadar uzakta (kapsülün
        /// yüksekliği uç kürelerini de kapsar), eksen collider'ın <c>direction</c> alanından.
        /// </summary>
        private static void DrawWireCapsule(CapsuleCollider capsule)
        {
            float radius = capsule.radius;
            Vector3 axis = AxisVector(capsule.direction);
            Vector3 perpA = AxisVector((capsule.direction + 1) % 3);
            Vector3 perpB = AxisVector((capsule.direction + 2) % 3);

            // Yükseklik yarıçapın iki katının altındaysa kapsül zaten bir küredir — mesafe negatife
            // düşmesin diye kırpılıyor, yoksa uç küreler ters tarafa geçer.
            float half = Mathf.Max(0f, capsule.height * 0.5f - radius);
            Vector3 top = capsule.center + axis * half;
            Vector3 bottom = capsule.center - axis * half;

            Gizmos.DrawWireSphere(top, radius);
            Gizmos.DrawWireSphere(bottom, radius);

            Gizmos.DrawLine(top + perpA * radius, bottom + perpA * radius);
            Gizmos.DrawLine(top - perpA * radius, bottom - perpA * radius);
            Gizmos.DrawLine(top + perpB * radius, bottom + perpB * radius);
            Gizmos.DrawLine(top - perpB * radius, bottom - perpB * radius);
        }

        /// <summary>CapsuleCollider.direction sözleşmesi: 0=X, 1=Y, 2=Z.</summary>
        private static Vector3 AxisVector(int direction)
        {
            switch (direction)
            {
                case 0: return Vector3.right;
                case 2: return Vector3.forward;
                default: return Vector3.up;
            }
        }
    }
}
