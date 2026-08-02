using UnityEngine;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// Bir mekanın <b>ölçü maketinin</b> kökündeki işaretçi: bu dalın altındaki <c>Plane</c> ve
    /// <c>Columns</c> geometrisi, <see cref="SourceJson"/>'daki boyut dosyasından üretilmiştir.
    /// <para>
    /// <b>Ne işe yarar:</b> maket, işletmenin fiziksel alanını sahnede görünür kılar — arena
    /// sanatı bunun üstüne kurulur. Ölçü yanlış alınmışsa köşeler ProBuilder ile yerinde
    /// düzeltilir ve <c>Tools &gt; VortexArena &gt; DimensionMesh'i JSON'a Çevir</c> maketi geri
    /// okuyup <see cref="SourceJson"/>'un ÜSTÜNE yazar. Bu bileşen o aracın "neyi çevireceğim"
    /// sorusunun cevabıdır.
    /// </para>
    /// <para>
    /// ⚠️ <b>Maket oynanan geometri DEĞİLDİR.</b> Kökü <c>EditorOnly</c> etiketiyle damgalanır,
    /// yani build'e hiç girmez. Oyuncunun gördüğü zemin/duvar environment sanatından gelir.
    /// Bileşenin runtime asmdef'inde olmasının sebebi teknik: sahne objesi editör-only bir tipe
    /// referans veremez.
    /// </para>
    /// <para>
    /// ⚠️ <b>Maketin kökü <see cref="ArenaBoundary"/> ile hizalı olmalıdır</b> (üretim aracı onu
    /// muhafazanın çocuğu olarak, yerel dönüşümü sıfırlanmış hâlde kurar): boyut dosyasındaki
    /// koordinatlar muhafaza transformunun yerel XZ'sindedir, maketi başka bir transformun altına
    /// koymak planı sessizce kaydırır.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class ArenaDimensionMesh : MonoBehaviour
    {
        /// <summary>Kök obje adının soneki: <c>&lt;Mekan&gt;_DimensionMesh</c>.</summary>
        public const string RootSuffix = "_DimensionMesh";

        /// <summary>Taban çokgeni objesinin adı.</summary>
        public const string PlaneName = "Plane";

        /// <summary>Kolonların toplandığı grup objesinin adı.</summary>
        public const string ColumnsGroupName = "Columns";

        /// <summary>Maketin build'e girmemesini sağlayan Unity etiketi.</summary>
        public const string EditorOnlyTag = "EditorOnly";

        [Tooltip("Mekan (işletme) klasör adı — kök obje adı ve raporlar bunu kullanır.")]
        [SerializeField] private string venueName = string.Empty;

        [Tooltip("Maketin üretildiği ve geri yazılacağı boyut dosyası (ArenaDimensions JSON'u).")]
        [SerializeField] private TextAsset sourceJson;

        [Tooltip("Kolonun kendi 'height' değeri 0 ise kullanılan yükseklik (metre).")]
        [SerializeField] private float defaultColumnHeight = 3f;

        /// <summary>Mekan (işletme) klasör adı.</summary>
        public string VenueName => venueName;

        /// <summary>Maketin kaynağı ve geri yazma hedefi olan boyut dosyası.</summary>
        public TextAsset SourceJson => sourceJson;

        /// <summary>
        /// Geri yazarken korunan taşıyıcı alan (bkz. <see cref="Configure"/>).
        /// </summary>
        public float DefaultColumnHeight => defaultColumnHeight;

        /// <summary>
        /// Üretim aracı tarafından doldurulur. <b>Yalnız editör araçları çağırır</b>, çalışma
        /// anında kimse yazmaz.
        /// <para>
        /// ⚠️ <paramref name="defaultColumnHeight"/> burada <b>gidiş-dönüş taşıyıcısı</b> olarak
        /// bekletilir, ikinci bir doğruluk kaynağı değil: geometriye dönüşmediği için maketten
        /// okunamaz, saklanmasaydı her geri yazmada kaybolur ve dosyadaki değer sessizce
        /// varsayılana dönerdi.
        /// </para>
        /// </summary>
        public void Configure(string venue, TextAsset json, float defaultColumnHeight)
        {
            venueName = venue;
            sourceJson = json;
            this.defaultColumnHeight = defaultColumnHeight;
        }

        /// <summary>Bir mekan adı için beklenen kök obje adı.</summary>
        public static string RootNameFor(string venue)
        {
            return (string.IsNullOrWhiteSpace(venue) ? "Arena" : venue.Trim()) + RootSuffix;
        }
    }
}
