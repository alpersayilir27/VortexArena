using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Net
{
    /// <summary>
    /// Sahne objesinin bake'li ağ kimliği (<c>sceneId</c>).
    /// <para>
    /// ⚠️ v1'de OYUNCU senkronu <c>playerId</c> ile gider (Snapshot/PoseData) — bu bileşen
    /// oyuncuya/avatara TAKILMAZ. NetIdentity YALNIZ SAHNE OBJELERİ içindir: ileriki dinamik
    /// obje senkronunun (kapı, pickup, kırılabilir kapak) altyapısıdır.
    /// </para>
    /// <see cref="SceneId"/> ELLE yazılmaz, sahne kaydında bake'lenir: atama
    /// <c>GameObject &gt; VortexArena &gt; Network Parent</c> menüsüyle, 0 kalan ve çakışan
    /// id'lerin onarımı kayıt sırasında <c>SceneIdGuard</c> ile yapılır (VortexArena.Net.Editor).
    /// Kayıt <see cref="OnEnable"/>/<see cref="OnDisable"/> ile yapılır; sahne değişiminde
    /// statik liste kendiliğinden boşalır (sızıntı yok).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetIdentity : MonoBehaviour
    {
        [Tooltip("Sahne kaydında bake'lenen benzersiz kimlik — ELLE DÜZENLEMEYİN (0 = atanmamış).")]
        [SerializeField] private uint sceneId;

        private static readonly List<NetIdentity> Registry = new List<NetIdentity>();

        /// <summary>Sahne içinde benzersiz, kayıtta bake'lenen ağ kimliği (0 = atanmamış).</summary>
        public uint SceneId => sceneId;

        /// <summary>Sahnede aktif olan tüm ağ kimlikleri (salt okunur).</summary>
        public static IReadOnlyList<NetIdentity> All => Registry;

        private void OnEnable()
        {
            if (!Registry.Contains(this))
            {
                Registry.Add(this);
            }
        }

        private void OnDisable()
        {
            Registry.Remove(this);
        }

        /// <summary>
        /// sceneId ile kimlik bulur; eşleşme yoksa null. 0 sorgusu HER ZAMAN null döner —
        /// bake edilmemiş obje ağ üzerinden adreslenemez.
        /// </summary>
        public static NetIdentity Find(uint sceneId)
        {
            if (sceneId == 0u)
            {
                return null;
            }

            for (int i = 0; i < Registry.Count; i++)
            {
                NetIdentity identity = Registry[i];
                if (identity != null && identity.sceneId == sceneId)
                {
                    return identity;
                }
            }

            return null;
        }
    }
}
