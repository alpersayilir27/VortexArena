using UnityEngine;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// Ölçü maketindeki bir çokgenin türünü söyleyen işaretçi: bu obje tabanın kendisi mi, yoksa
    /// bir kolon mu. <c>DimensionMesh'i JSON'a Çevir</c> maketi bu bileşene bakarak gezer.
    /// <para>
    /// ⚠️ <b>Bileşende nokta, ad ya da yükseklik TUTULMAZ.</b> Noktaların tek doğruluk kaynağı
    /// mesh'in kendisidir (yoksa ProBuilder ile yapılan düzenleme sessizce göz ardı edilirdi), ad
    /// zaten <c>GameObject</c>'in adıdır, yükseklik ise mesh'in Y aralığıdır. Üçünü de bileşene
    /// kopyalamak, sahnede düzenlenen değerden sessizce sapan ikinci bir kaynak üretirdi.
    /// </para>
    /// <para>
    /// ⚠️ Ebeveyn adına (<c>Columns</c>) bakmak yerine bileşen kullanılmasının sebebi: hiyerarşi
    /// elle yeniden düzenlenebilir, bileşen objeyle birlikte taşınır.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class DimensionPolygon : MonoBehaviour
    {
        /// <summary>
        /// Çokgenin maketteki rolü.
        /// <para>
        /// ⚠️ Serialize edilen enum: yeni değer <b>SONA</b> eklenir — Unity sayısal indeks saklar,
        /// başa/ortaya ekleme sahnelerdeki değerleri kaydırır.
        /// </para>
        /// </summary>
        public enum PolygonKind
        {
            /// <summary>Tabanın kendisi (maket başına tek).</summary>
            Plane,

            /// <summary>Alan içindeki bir kolon/engel (prizma).</summary>
            Column
        }

        [Tooltip("Bu çokgen tabanı mı yoksa bir kolonu mu temsil ediyor.")]
        [SerializeField] private PolygonKind kind = PolygonKind.Plane;

        /// <summary>Çokgenin maketteki rolü.</summary>
        public PolygonKind Kind => kind;

        /// <summary>Üretim aracı tarafından doldurulur — çalışma anında kimse yazmaz.</summary>
        public void SetKind(PolygonKind value)
        {
            kind = value;
        }
    }
}
