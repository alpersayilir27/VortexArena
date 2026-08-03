using System;
using UnityEngine;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// Bir mekanın (işletmenin) 2B planı: zemin sınırı (sıralı köşe halkası) + içindeki kolonlar.
    /// <b>Arena ölçüsünün TEK doğruluk kaynağıdır</b> — elle yazılabilir bir JSON dosyası olarak
    /// yaşar, işletmenin fiziksel ölçüsü şeritmetreyle alınıp doğrudan buraya girilir (Unity açmaya
    /// gerek yok). Ölçü ikinci bir yere (bileşen alanı, prefab, sahne) yazılmaz.
    /// <para>
    /// <b>Dosya MEKAN başınadır</b> (<c>Venues/&lt;Mekan&gt;/Data/&lt;Mekan&gt;_dimensions.json</c>):
    /// bir işletmede hep aynı fiziksel alan oynatıldığı için mekanın tüm sahneleri (arenalar +
    /// lobi) <see cref="ArenaBoundary.dimensionsJson"/> alanında AYNI dosyayı gösterir. Sahne
    /// başına kopya çıkarmak, kaçınılmaz olarak birbirinden sapan iki ölçü üretir.
    /// </para>
    /// <para>
    /// <b>Çalışma anında okunur:</b> <see cref="ArenaBoundary"/> kendisine bağlanan
    /// <c>TextAsset</c>'i <see cref="Parse"/> ile çözer. Bu yüzden JSON dosyası bir sahneden
    /// referanslanmalıdır (referanslanan TextAsset build'e girer); <c>Assets/</c> altında durup
    /// kimsenin referanslamadığı bir JSON build'e GİRMEZ.
    /// </para>
    /// <para>
    /// ⚠️ <b>Koordinat sistemi:</b> tüm noktalar metre cinsinden ve <see cref="ArenaBoundary"/>'yi
    /// taşıyan transformun YEREL XZ düzlemindedir (JSON'da <c>y</c> alanı = dünya Z'si). Ölçüyü bir
    /// köşeden alıyorsan o köşe (0,0) olur; plan sıfırının arena origin'i olması GEREKMEZ — ağ
    /// koordinatlarının sıfırı dünya orijinidir (<see cref="ArenaSpace"/>) ve muhafazanın ölçüsü
    /// ondan bağımsız olarak bu yerel düzlemde tanımlıdır.
    /// </para>
    /// <para>
    /// ⚠️ Halkalar <b>kapalı</b> kabul edilir: son köşe ilk köşeye kendiliğinden bağlanır, aynı
    /// noktayı sona tekrar yazma. Sarım yönü (saat yönü ya da tersi) önemsizdir.
    /// </para>
    /// <para>
    /// ⚠️ <b>Taban TEK halkadır ve parçalardan birleştirilmez.</b> İçbükeylik için ek bir şey
    /// gerekmez: L şekli, yamuk, girintili duvar hepsi tek sıralı köşe halkasıdır. Aynısı kolonlar
    /// için de geçerli — gerekçe <see cref="Polygon2D"/> belgesinde.
    /// </para>
    /// <para>
    /// ⚠️ <b>Duvar yüksekliği alanı YOKTUR ve geri eklenmez:</b> duvar üretimi de muhafazanın
    /// yarı saydam duvar göstergesi de kaldırıldı, yani okuyanı olmayan bir sayı olurdu — okunmayan
    /// ölçü bayatlar. Arenanın duvarları environment sanatından gelir.
    /// </para>
    /// <para>
    /// <b>Kalibrasyon noktaları da buradadır</b> (<see cref="calibration"/>): zemine yapıştırılan
    /// A/B bantlarının yeri de bir ÖLÇÜDÜR ve mekan başınadır — aynı odada oynanan tüm sahneler
    /// aynı iki fiziksel işareti kullanır. Sahnedeki <c>anchor_a</c>/<c>anchor_b</c> objeleri
    /// buradan konumlandırılır, yani ölçü sahneye elle kopyalanmaz.
    /// </para>
    /// <example>
    /// Örnek dosya: <c>Assets/Arenas/Venues/VortexAntep/Data/VortexAntep_dimensions.json</c>
    /// <code>
    /// {
    ///   "name": "VortexAntep",
    ///   "plane": [ { "x": 0, "y": 0 }, { "x": 8.32, "y": 0 }, { "x": 8.32, "y": 13.23 } ],
    ///   "columns": [
    ///     { "name": "Kolon_Orta", "height": 0,
    ///       "points": [ { "x": 3.27, "y": 7.19 }, { "x": 3.94, "y": 7.19 },
    ///                   { "x": 3.94, "y": 7.57 }, { "x": 3.27, "y": 7.57 } ] }
    ///   ],
    ///   "calibration": { "a": { "x": 3.17, "y": 1.82 }, "b": { "x": 3.17, "y": 7.19 } },
    ///   "defaultColumnHeight": 3.0
    /// }
    /// </code>
    /// </example>
    /// </summary>
    [Serializable]
    public class ArenaDimensions
    {
        /// <summary>Geçerli bir çokgen için gereken en az köşe sayısı.</summary>
        public const int MinOutlinePoints = Polygon2D.MinPoints;

        /// <summary>
        /// Arena içindeki bir kolon — kendi sıralı köşe halkası olarak. Prizma olarak çizilir ve
        /// <b>her zaman</b> muhafaza hesabına engel olarak girer: kolon binanın taşıyıcısıdır,
        /// oyuncu ona çarpar. "Bu kolon bloklamasın" diye bir anahtar YOKTUR ve eklenmez.
        /// <para>
        /// ⚠️ <b>Sarmalayıcı bir nesne olmasının sebebi teknik:</b> <see cref="JsonUtility"/> iç içe
        /// dizi (<c>Vector2[][]</c>) serialize etmiyor. Karşılığında <see cref="name"/> ve
        /// <see cref="height"/> bedava geliyor — paralel dizilerde tutulsalardı indeksleri elle
        /// hizada tutulan, sessizce kayabilen bir yapı olurdu.
        /// </para>
        /// <para>
        /// ⚠️ Merkez + ölçü + dönüş (<c>center</c>/<c>size</c>/<c>yaw</c>) gösterimi bilinçli
        /// olarak kaldırıldı: gerçek kolonlar her zaman eksen hizalı dikdörtgen değil, ve eğik bir
        /// paye o gösterimde ancak yaklaşık temsil ediliyordu.
        /// </para>
        /// </summary>
        [Serializable]
        public struct Column
        {
            /// <summary>Üretilen objenin adı. Boşsa <c>Kolon_&lt;sıra&gt;</c> kullanılır.</summary>
            public string name;

            /// <summary>Yükseklik (metre). 0 bırakılırsa <see cref="defaultColumnHeight"/> kullanılır.</summary>
            public float height;

            /// <summary>
            /// Ayak izinin sıralı köşeleri (metre, arena yerel XZ — <c>y</c> = dünya Z'si). Kapalı
            /// kabul edilir.
            /// </summary>
            public Vector2[] points;
        }

        /// <summary>
        /// Mekanın iki kalibrasyon noktası (zemine yapıştırılan A ve B bantları), metre ve
        /// plan uzayında. Hizalama sırası <b>her zaman A → B</b>'dir: yaw bu doğrultudan çıkar,
        /// yani ikisini karıştırmak arenayı 180° ters çevirir.
        /// <para>
        /// ⚠️ <b>Ayrı bir nesne olmasının sebebi teknik:</b> <see cref="JsonUtility"/> alan adını
        /// birebir eşliyor ve <c>anchor_a</c> gibi bir ad C# alan adı olarak yazılamıyor. Nesne
        /// olarak sarınca dosyada <c>"calibration": { "a": …, "b": … }</c> okunur kalıyor.
        /// </para>
        /// </summary>
        [Serializable]
        public struct CalibrationMarks
        {
            /// <summary>İlk yakalanan nokta (sahnedeki <c>anchor_a</c>).</summary>
            public Vector2 a;

            /// <summary>İkinci yakalanan nokta (sahnedeki <c>anchor_b</c>).</summary>
            public Vector2 b;
        }

        /// <summary>
        /// İki nokta arasında olması gereken en kısa mesafe (metre). Bunun altındaki bir çift
        /// yön tanımlamaz: yaw hatası mesafeyle ters orantılı büyüdüğü için 20 cm'lik bir aralık
        /// birkaç milimetrelik ölçüm hatasını arenanın öbür ucunda metrelere çevirirdi.
        /// </summary>
        public const float MinCalibrationSpan = 0.5f;

        /// <summary>Bilgi amaçlı ad (üretilen geometriyi ve hata mesajlarını etiketler).</summary>
        public string name = string.Empty;

        /// <summary>
        /// Tabanın sıralı köşeleri (metre, arena yerel XZ). Kapalı kabul edilir — ilk noktayı sona
        /// tekrarlama.
        /// </summary>
        public Vector2[] plane = Array.Empty<Vector2>();

        /// <summary>Arena içindeki kolonlar/engeller. Boş bırakılabilir.</summary>
        public Column[] columns = Array.Empty<Column>();

        /// <summary>
        /// Zemin bandındaki A/B işaretlerinin yeri (metre, plan uzayı). Yazılmazsa iki nokta da
        /// (0,0) kalır ve <see cref="HasCalibration"/> false döner — kalibratör o durumda sahnedeki
        /// işaretçilere dokunmaz ve konsola uyarı basar.
        /// </summary>
        public CalibrationMarks calibration;

        /// <summary>Kolonun <c>height</c> alanı 0 bırakılırsa kullanılacak yükseklik (metre).</summary>
        public float defaultColumnHeight = 3f;

        /// <summary>Kullanılabilir bir plan mı (en az bir üçgen).</summary>
        public bool IsValid => Polygon2D.IsValid(plane);

        /// <summary>
        /// Kalibrasyon noktaları kullanılabilir mi: yazılmışlar ve aralarında en az
        /// <see cref="MinCalibrationSpan"/> metre var mı.
        /// <para>
        /// ⚠️ Bu <b>plan geçerliliğinin parçası DEĞİLDİR</b> (<see cref="IsValid"/>): noktasız bir
        /// dosya muhafazayı çalıştırmaya yeter, yalnız kalibrasyon işaretçileri kendiliğinden
        /// yerleşmez. Ölçüyü ölçüsüzlükten ayıran çizgi taban halkasıdır.
        /// </para>
        /// </summary>
        public bool HasCalibration =>
            (calibration.b - calibration.a).sqrMagnitude >= MinCalibrationSpan * MinCalibrationSpan;

        /// <summary>Bir kolonun etkin yüksekliği (kendi değeri 0 ise varsayılan).</summary>
        public float HeightOf(in Column column)
        {
            return column.height > 0f ? column.height : defaultColumnHeight;
        }

        /// <summary>
        /// Taban halkasının XZ sınırlayıcı kutusu (arena yerel). <see cref="ArenaBoundary"/>
        /// muhafaza ölçüsünü ve admin kuş bakışı kadrajını bundan türetir. Plan geçersizse sıfır
        /// kutu döner.
        /// </summary>
        public Rect LocalBounds()
        {
            return Polygon2D.Bounds(plane);
        }

        // ------------------------------------------------------------- ayrıştırma

        /// <summary>
        /// JSON metnini plana çevirir. <b>Exception FIRLATMAZ</b> — bozuk bir dosya yüzünden
        /// sahne açılışında patlamak yerine <c>null</c> döner ve sebebi
        /// <paramref name="error"/>'a yazar (çağıran yüksek sesle hata basar).
        /// <para>
        /// ⚠️ <see cref="JsonUtility.FromJsonOverwrite"/> kullanılır ki JSON'da YAZILMAYAN alanlar
        /// bu sınıftaki varsayılanlarında kalsın (<c>defaultColumnHeight</c> 3). <c>FromJson</c>
        /// ile eksik bir alan sessizce 0 olurdu — yüksekliksiz, yani hiç çizilmeyen kolonlar demek.
        /// </para>
        /// </summary>
        /// <param name="json">Dosya içeriği.</param>
        /// <param name="error">Başarısızsa sebebi; başarılıysa <c>null</c>.</param>
        /// <returns>Geçerli plan ya da <c>null</c>.</returns>
        public static ArenaDimensions Parse(string json, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Boyut dosyası boş.";
                return null;
            }

            var dimensions = new ArenaDimensions();
            try
            {
                JsonUtility.FromJsonOverwrite(json, dimensions);
            }
            catch (Exception exception)
            {
                error = "Boyut dosyası ayrıştırılamadı: " + exception.Message;
                return null;
            }

            if (!dimensions.IsValid)
            {
                int count = dimensions.plane?.Length ?? 0;
                error = $"Geçersiz plan: 'plane' en az {MinOutlinePoints} köşe içermeli (bulunan: {count}).";
                return null;
            }

            dimensions.columns ??= Array.Empty<Column>();

            // Noktasız bir kolon geometri de üretmez, muhafazaya da giremez: sessizce taşımak
            // yerine burada ayıklanır ki tüketiciler her elemanın kullanılabilir olduğuna
            // güvenebilsin.
            int usable = 0;
            for (int i = 0; i < dimensions.columns.Length; i++)
            {
                if (Polygon2D.IsValid(dimensions.columns[i].points))
                {
                    dimensions.columns[usable++] = dimensions.columns[i];
                }
            }

            if (usable != dimensions.columns.Length)
            {
                Array.Resize(ref dimensions.columns, usable);
            }

            return dimensions;
        }

        /// <summary>
        /// Bir <c>TextAsset</c>'ten plan okur (bkz. <see cref="Parse"/>). Asset null ise sessizce
        /// <c>null</c> döner — "plan verilmedi" hata değildir.
        /// </summary>
        public static ArenaDimensions FromTextAsset(TextAsset asset, out string error)
        {
            error = null;
            return asset == null ? null : Parse(asset.text, out error);
        }

        /// <summary>Planı JSON metnine çevirir (editör araçları dosya yazarken kullanır).</summary>
        public string ToJson(bool pretty = true)
        {
            return JsonUtility.ToJson(this, pretty);
        }
    }
}
