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
    /// ⚠️ Obje adları sahnedeki işaretçilerle <b>aynıdır</b> (<c>anchor_a</c> / <c>anchor_b</c>,
    /// tek kaynak: <see cref="ArenaCalibrator.AnchorAName"/>) — aynı şeyin iki adı olmaz. Adın
    /// çakışmasını zararsız kılan şey <b>bu bileşenin kendisidir</b>: kalibratörün ad araması
    /// <see cref="DimensionAnchor"/> taşıyan objeleri atlar, yani maket editörde Play kipinde
    /// sahnede dururken bile gerçek işaretçinin yerine geçemez.
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
