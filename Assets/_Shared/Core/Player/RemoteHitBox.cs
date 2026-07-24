using UnityEngine;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Uzak oyuncu avatarının vurulabilir parçası (kafa / gövde collider'ı).
    /// <para>
    /// Weapon raycast'i bu bileşene değerse hedef bir AĞ OYUNCUSUDUR:
    /// hasar YEREL uygulanmaz, sunucuya <c>hit_report</c> gönderilir ve
    /// <c>health_update</c> beklenir (Docs/ArenaNet-Protokol.md §10.3).
    /// </para>
    /// <see cref="IsHead"/> v1'de hasar çarpanı ÜRETMEZ (hasar tablosu sunucuda sabit);
    /// yalnız bilgi/efekt amaçlıdır.
    /// </summary>
    public class RemoteHitBox : MonoBehaviour
    {
        [Tooltip("Boş bırakılırsa üst hiyerarşiden otomatik bulunur.")]
        [SerializeField] private RemoteAvatar avatar;
        [Tooltip("Kafa bölgesi mi (v1: hasar çarpanı yok, yalnız bilgi).")]
        [SerializeField] private bool isHead;

        /// <summary>Bu hitbox'ın ait olduğu avatar.</summary>
        public RemoteAvatar Avatar => avatar;

        /// <summary>Avatarın oyuncu id'si; avatar yoksa 0 (geçersiz hedef).</summary>
        public int PlayerId => avatar != null ? avatar.PlayerId : 0;

        /// <summary>Kafa bölgesi mi.</summary>
        public bool IsHead => isHead;

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
    }
}
