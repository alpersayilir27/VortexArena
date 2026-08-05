using UnityEngine;

namespace VortexArena.Net
{
    /// <summary>
    /// UdpStateChannel'ın 20 Hz gönderim döngüsüne poz sağlayan kaynak.
    /// Dünya→arena dönüşümü KAYNAĞIN sorumluluğudur (App'teki PlayerPoseTracker
    /// yapar) — Net katmanı yalnız hazır arena-uzayı pozları alır.
    /// </summary>
    public interface IPoseSource
    {
        /// <summary>Arena-uzayı pozlarını verir; izleme hazır değilse false.</summary>
        bool TryGetArenaPoses(out Pose head, out Pose handL, out Pose handR);

        /// <summary>
        /// §6.2: o anda elde tutulan eşya baytları. <c>gripFlags</c>'te istemciye ait BEŞ bit
        /// anlamlıdır — <see cref="VortexArena.Protocol.SnapshotEntry.FLAG_GRIP_LINKED"/> /
        /// <see cref="VortexArena.Protocol.SnapshotEntry.FLAG_PRIMARY_RIGHT"/> (kavrama),
        /// <see cref="VortexArena.Protocol.SnapshotEntry.FLAG_HAND_L_STALE"/> /
        /// <see cref="VortexArena.Protocol.SnapshotEntry.FLAG_HAND_R_STALE"/> (o elin pozu ölçüm
        /// değil, tutulan son geçerli poz) ve
        /// <see cref="VortexArena.Protocol.SnapshotEntry.FLAG_IN_OBSTACLE"/> (gövde bir iç engelin
        /// içinde, §10.9). Süzgeç
        /// <see cref="VortexArena.Protocol.SnapshotEntry.GRIP_FLAG_MASK"/>; bit0 sunucunundur
        /// (<c>FLAG_ALIVE</c> — istemci kendini canlı ilan edemez). Eşya yoksa üçü de 0 döner.
        /// <para>⚠️ Ad "grip"tir ama içerik yalnız kavrama değildir: bayrak baytı istemcinin
        /// <b>elle ilgili tüm sunum bitlerini</b> taşır. Yeni bir bit eklerken maskeyi de büyüt,
        /// yoksa sunucu onu sessizce düşürür.</para>
        /// <para><b>Neden poz seam'inden geliyor:</b> iki sebep var. (1) <b>Asmdef yönü</b> —
        /// bağımlılık Protocol ← Net ← Core ← App, yani bu katman <c>HeldItems</c>/
        /// <c>ItemDefinition</c>/<c>NetItemCatalog</c> gibi Core tiplerini GÖREMEZ; eşya bilgisi
        /// ancak yukarıdan sızdırılabilir. (2) <b>Aynı otoriteye ait</b> — "elimde ne var" da
        /// "elim nerede" gibi istemci-otoriter bir sunum bilgisidir ve pozla aynı pakette gider.</para>
        /// </summary>
        void GetHeldItems(out byte itemL, out byte itemR, out byte gripFlags);
    }
}
