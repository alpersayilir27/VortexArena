using UnityEngine;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// Arena geometrisinin layer sözleşmesi — <b>layer adının tek yazıldığı yer</b>.
    /// <para>
    /// <b>Neden bir sınıf:</b> layer adı bir dizedir ve <see cref="LayerMask.NameToLayer"/> tanımsız
    /// adda <c>-1</c> döner (maske <c>0</c> olur, sorgu sessizce hiçbir şey bulmaz). Ad üç ayrı
    /// dosyada yazılsaydı yanlış yazımın belirtisi "sistem hiç çalışmıyor" olurdu ve hiçbir yerde
    /// hata görünmezdi. Burada bir kez çözülür ve bulunamazsa <b>bir kez</b> açıkça bağırır.
    /// </para>
    /// </summary>
    public static class ArenaLayers
    {
        /// <summary>
        /// <b>İç engel</b> layer'ı: sütun, kasa, sandık, blok. Sözleşmesi tek cümledir —
        /// <b>"bunun içinde olmak ihlaldir"</b> (Docs/ArenaNet-Protokol.md §10.9).
        /// <para>
        /// ⚠️ <b>Arenanın DIŞ duvarları, zemini ve tavanı bu layer'a KONMAZ.</b> Dış sınırı
        /// <see cref="ArenaBoundary"/> yönetir (karartma + uyarı, hasar yok). Gerekçe kalibrasyondur:
        /// dış duvar oyuncunun her an dibinde olduğu için kayan bir hizalamada sürekli yalancı ihlal
        /// üretir ve oyuncu durduk yere ölür.
        /// </para>
        /// <para>
        /// ⚠️ Bu layer'daki collider <b>KONVEKS olmak zorundadır</b> (Box/Sphere/Capsule ya da
        /// <c>MeshCollider</c> + <c>Convex</c>) — gerekçe
        /// <see cref="VortexArena.Core.Arena.ObstacleVolumes"/>'de.
        /// </para>
        /// </summary>
        public const string ObstacleName = "Obstacle";

        private static int obstacleMask;
        private static bool obstacleResolved;

        /// <summary>
        /// <see cref="ObstacleName"/> layer'ının maskesi; layer tanımlı değilse <c>0</c> (hiçbir
        /// sorgu eşleşmez → sistem sessizce değil, <b>bir hata satırıyla</b> devre dışı kalır).
        /// </summary>
        public static int ObstacleMask
        {
            get
            {
                if (obstacleResolved)
                {
                    return obstacleMask;
                }

                obstacleResolved = true;

                int layer = LayerMask.NameToLayer(ObstacleName);
                if (layer < 0)
                {
                    // ⚠️ Uyarı değil HATA: bu olmadan engel ihlali sistemi tümden çalışmaz ve
                    // belirtisi "hiçbir şey olmuyor"dur — teşhis edilemez bir sessizlik.
                    Debug.LogError(
                        $"[ArenaLayers] '{ObstacleName}' layer'ı projede tanımlı değil — engel ihlali " +
                        "tespiti DEVRE DIŞI. Project Settings > Tags and Layers altında bu adla bir " +
                        "user layer açılmalı.");
                    obstacleMask = 0;
                    return obstacleMask;
                }

                obstacleMask = 1 << layer;
                return obstacleMask;
            }
        }
    }
}
