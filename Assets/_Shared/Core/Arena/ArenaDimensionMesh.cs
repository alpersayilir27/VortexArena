using UnityEngine;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// Bir mekanın <b>ölçü maketinin</b> kökündeki işaretçi: bu dalın altındaki <c>Plane</c> ve
    /// <c>Columns</c> geometrisi, <see cref="SourceJson"/>'daki boyut dosyasından üretilmiştir.
    /// <para>
    /// <b>Ne işe yarar:</b> maket, işletmenin fiziksel alanını sahnede görünür kılar — arena
    /// sanatı bunun üstüne kurulur. Ölçü yanlış alınmışsa köşeler ProBuilder ile yerinde
    /// düzeltilir ve <c>Tools &gt; VortexArena &gt; Arena &gt; DimensionMesh'i JSON'a Çevir</c> maketi geri
    /// okuyup <see cref="SourceJson"/>'un ÜSTÜNE yazar. Bu bileşen o aracın "neyi çevireceğim"
    /// sorusunun cevabıdır.
    /// </para>
    /// <para>
    /// ⚠️ <b>Maket oynanan geometri DEĞİLDİR ama build'e GİRER:</b> kalibrasyon işaretçileri
    /// (<c>anchor_a</c> / <c>anchor_b</c>) maketin altındadır ve <see cref="ArenaCalibrator"/>
    /// onları çalışma anında arar — build'den düşen bir maket, hizalanamayan bir arena demektir.
    /// Görünmemesini sağlayan şey etiket değil davranıştır: <see cref="Awake"/> yalnız ölçü
    /// görselini (<see cref="PlaneName"/> + <see cref="ColumnsGroupName"/>) kapatır. Oyuncunun
    /// gördüğü zemin/duvar environment sanatından gelir.
    /// </para>
    /// <para>
    /// ⚠️ Gerçek bir build'de o görsel dal <b>hiç girmez</b>: <c>DimensionMeshBuildStripper</c>
    /// onu build'e giden geçici sahne kopyasından siler, çünkü <c>Plane</c>/kolonlar
    /// <c>ProBuilderMesh</c> taşır ve o da <c>Unity.ProBuilder</c>'ı runtime'a sokardı. Yani
    /// <see cref="Awake"/>'teki gizleme <b>editör Play kipi</b> içindir — iki mekanizma aynı
    /// sonucu iki ayrı bağlamda verir, biri diğerinin yedeği değildir.
    /// </para>
    /// <para>
    /// ⚠️ <b>Maket sahneden BAĞIMSIZDIR</b> — üretim aracı onu sahne köküne, dünya orijininde ve
    /// dönüşsüz kurar ki dosyadaki ölçü sahnede birebir okunabilsin. İstenirse elle taşınır ve
    /// döndürülür: ölçü çıkarımı bu <b>kökün</b> yerel uzayına göre yapıldığı için taşınmış bir
    /// maket de doğru çevrilir. Yalnız <b>ölçeğini değiştirme</b> — plan metre cinsindendir.
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
        /// Maketin ÖLÇÜ GÖRSELİNİ oyunda gizler: taban ve kolon prizmaları oyuncunun görmesi
        /// gereken geometri değil, editördeki bir referanstır.
        /// <para>
        /// ⚠️ Kalibrasyon işaretçilerine (<c>anchor_a</c> / <c>anchor_b</c>) DOKUNULMAZ: onların
        /// görünürlüğünü <see cref="ArenaCalibrator"/> yönetir (yakalama sırasında yakar, hizalama
        /// onaylanınca gizler). Burada kapatmak o geri bildirimi sessizce öldürürdü.
        /// </para>
        /// <para>
        /// ⚠️ Kapatılan şey <see cref="Renderer.enabled"/>'dır, obje DEĞİL: maketin dalı kapanırsa
        /// kalibratör işaretçileri bulamaz ve boyut dosyasındaki noktalara oturtamaz.
        /// </para>
        /// <para>
        /// Editörde görünür kalması gerekir (maket bir kurulum aracıdır), bu yüzden
        /// <c>[ExecuteAlways]</c> YOKTUR — <c>Awake</c> yalnız Play/çalışma anında koşar.
        /// </para>
        /// </summary>
        private void Awake()
        {
            HideMeasurementVisual(PlaneName);
            HideMeasurementVisual(ColumnsGroupName);
        }

        private void HideMeasurementVisual(string childName)
        {
            Transform child = transform.Find(childName);
            if (child == null)
                return;

            Renderer[] renderers = child.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = false;
            }
        }

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
