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
    }
}
