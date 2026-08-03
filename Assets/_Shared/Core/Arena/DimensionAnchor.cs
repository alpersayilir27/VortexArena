using UnityEngine;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// Ölçü maketindeki bir kalibrasyon noktası işaretçisi: bu obje zemin bandının <b>A</b>'sı mı
    /// yoksa <b>B</b>'si mi. <c>DimensionMesh'i JSON'a Çevir</c> maketi bu bileşene bakarak
    /// noktaları boyut dosyasının <c>calibration</c> alanına geri yazar.
    /// <para>
    /// ⚠️ <b>Bileşende koordinat TUTULMAZ</b> — nokta objenin transformudur. İkisini birden
    /// saklamak, sahnede sürüklenen konumdan sessizce sapan ikinci bir kaynak üretirdi
    /// (<see cref="DimensionPolygon"/> ile aynı gerekçe).
    /// </para>
    /// <para>
    /// ⚠️ <b>Bu işaretçi çalışma anındaki işaretçinin ta kendisidir</b> — ikinci bir işaretçi
    /// ailesi YOKTUR. <see cref="ArenaCalibrator"/> hizalayacağı objeyi önce bu bileşene ve
    /// <see cref="Kind"/>'ına bakarak bulur; maketi olmayan eski sahneler için sonda
    /// <b>ada</b> bakan bir yol kalır (<see cref="ArenaCalibrator.AnchorAName"/> /
    /// <see cref="ArenaCalibrator.AnchorBName"/>). Maket bu yüzden build'e girer ve
    /// <c>EditorOnly</c> etiketlenmez; oynanan geometri olmadığı için yalnız zemin/kolon
    /// görseli çalışma anında gizlenir.
    /// </para>
    /// <para>
    /// ⚠️ <b>Konum sözleşmesi: objenin transformu ZEMİN NOKTASIDIR</b> (küpün merkezi noktada
    /// durur, yarısı zeminin altında kalır). Geri okuma transformu ham okuduğu için sözleşmenin
    /// tek olması şart — işaretçiyi mesh tabanı zemine gelecek şekilde kaldırmak, dosyaya
    /// yazılan nokta ile sahnede görünen noktayı birbirinden ayırırdı.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class DimensionAnchor : MonoBehaviour
    {
        /// <summary>
        /// Noktanın kalibrasyon sırasındaki yeri.
        /// <para>
        /// ⚠️ Serialize edilen enum: yeni değer <b>SONA</b> eklenir — Unity sayısal indeks saklar,
        /// başa/ortaya ekleme sahnelerdeki değerleri kaydırır.
        /// </para>
        /// </summary>
        public enum AnchorKind
        {
            /// <summary>İlk yakalanan nokta.</summary>
            A,

            /// <summary>İkinci yakalanan nokta; A→B doğrultusu arenanın yönünü verir.</summary>
            B
        }

        [Tooltip("Bu işaretçi kalibrasyonun A noktası mı yoksa B noktası mı.")]
        [SerializeField] private AnchorKind kind = AnchorKind.A;

        /// <summary>Noktanın kalibrasyon sırasındaki yeri.</summary>
        public AnchorKind Kind => kind;

        /// <summary>Üretim aracı tarafından doldurulur — çalışma anında kimse yazmaz.</summary>
        public void SetKind(AnchorKind value)
        {
            kind = value;
        }
    }
}
