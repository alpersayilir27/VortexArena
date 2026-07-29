using System;
using UnityEngine;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// Bir arenanın 2B planı: zemin sınırı (sıralı köşe listesi) + içindeki kolonlar.
    /// <b>Arena ölçüsünün TEK doğruluk kaynağıdır</b> — elle yazılabilir bir JSON dosyası olarak
    /// yaşar, işletmenin fiziksel ölçüsü şeritmetreyle alınıp doğrudan buraya girilir (Unity açmaya
    /// gerek yok). Ölçü ikinci bir yere (bileşen alanı, prefab, sahne) yazılmaz.
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
    /// origin'i ayrı bir objededir (<see cref="SpawnPoint"/>).
    /// </para>
    /// <para>
    /// ⚠️ Sınır <b>kapalı</b> kabul edilir: son köşe ilk köşeye kendiliğinden bağlanır, aynı
    /// noktayı sona tekrar yazma. Köşeler saat yönü ya da tersi olabilir — hesap yöne duyarsızdır.
    /// </para>
    /// <para>
    /// ⚠️ <b>Dikdörtgen bir alan için ayrı bir kip YOKTUR:</b> alan tam kare bile olsa dört köşeli
    /// bir <c>outline</c> olarak yazılır. "Dikdörtgense şu hızlı yol" ayrımı bilinçli olarak
    /// kaldırıldı — aynı ölçünün iki ayrı ifadesi kaçınılmaz olarak birbirinden sapıyordu.
    /// </para>
    /// <example>
    /// Örnek dosya: <c>Assets/Arenas/Venues/VortexAntep/Data/vortexantep_dimensions.json</c>
    /// <code>
    /// {
    ///   "name": "VortexAntep",
    ///   "outline": [ { "x": 0, "y": 0 }, { "x": 8.32, "y": 0 }, { "x": 8.32, "y": 13.23 } ],
    ///   "wallHeight": 3.0,
    ///   "columns": [
    ///     { "name": "Kolon_Orta", "center": { "x": 3.605, "y": 7.38 },
    ///       "size": { "x": 0.67, "y": 0.38 }, "yaw": 0, "height": 0 }
    ///   ]
    /// }
    /// </code>
    /// </example>
    /// </summary>
    [Serializable]
    public class ArenaDimensions
    {
        /// <summary>Geçerli bir çokgen için gereken en az köşe sayısı.</summary>
        public const int MinOutlinePoints = 3;

        /// <summary>
        /// Arena içindeki bir kolon/engel. Kutu olarak çizilir ve (istenirse) muhafaza hesabına
        /// engel olarak girer.
        /// </summary>
        [Serializable]
        public struct Column
        {
            /// <summary>Üretilen objenin adı. Boşsa <c>Kolon_&lt;sıra&gt;</c> kullanılır.</summary>
            public string name;

            /// <summary>Kolon merkezi (metre, arena yerel XZ — <c>y</c> = dünya Z'si).</summary>
            public Vector2 center;

            /// <summary>Kolon ölçüsü: <c>x</c> = genişlik, <c>y</c> = derinlik (metre).</summary>
            public Vector2 size;

            /// <summary>Y ekseni etrafında dönüş (derece). Duvara paralel olmayan kolonlar için.</summary>
            public float yaw;

            /// <summary>Yükseklik (metre). 0 bırakılırsa <see cref="defaultColumnHeight"/> kullanılır.</summary>
            public float height;
        }

        /// <summary>Bilgi amaçlı ad (üretilen geometriyi ve hata mesajlarını etiketler).</summary>
        public string name = string.Empty;

        /// <summary>
        /// Sıralı sınır köşeleri (metre, arena yerel XZ). Kapalı kabul edilir — ilk noktayı sona
        /// tekrarlama.
        /// </summary>
        public Vector2[] outline = Array.Empty<Vector2>();

        /// <summary>Duvar yüksekliği (metre) — üretilen geometri için.</summary>
        public float wallHeight = 3f;

        /// <summary>Arena içindeki kolonlar/engeller. Boş bırakılabilir.</summary>
        public Column[] columns = Array.Empty<Column>();

        /// <summary>Kolonun <c>height</c> alanı 0 bırakılırsa kullanılacak yükseklik (metre).</summary>
        public float defaultColumnHeight = 3f;

        /// <summary>
        /// Kolonlar da muhafaza hesabına girsin mi (yaklaşınca uyarı). Kapalıysa kolonlar yalnız
        /// geometridir. JSON'da yazılmazsa <c>true</c> kalır.
        /// </summary>
        public bool columnsBlockPlayer = true;

        /// <summary>Kullanılabilir bir plan mı (en az bir üçgen).</summary>
        public bool IsValid => outline != null && outline.Length >= MinOutlinePoints;

        /// <summary>Bir kolonun etkin yüksekliği (kendi değeri 0 ise varsayılan).</summary>
        public float HeightOf(in Column column)
        {
            return column.height > 0f ? column.height : defaultColumnHeight;
        }

        /// <summary>
        /// Sınır çokgeninin XZ sınırlayıcı kutusu (arena yerel). <see cref="ArenaBoundary"/>
        /// muhafaza ölçüsünü ve admin kuş bakışı kadrajını bundan türetir. Plan geçersizse sıfır
        /// kutu döner.
        /// </summary>
        public Rect LocalBounds()
        {
            if (!IsValid)
            {
                return new Rect(0f, 0f, 0f, 0f);
            }

            float minX = outline[0].x, maxX = outline[0].x;
            float minZ = outline[0].y, maxZ = outline[0].y;
            for (int i = 1; i < outline.Length; i++)
            {
                minX = Mathf.Min(minX, outline[i].x);
                maxX = Mathf.Max(maxX, outline[i].x);
                minZ = Mathf.Min(minZ, outline[i].y);
                maxZ = Mathf.Max(maxZ, outline[i].y);
            }

            return Rect.MinMaxRect(minX, minZ, maxX, maxZ);
        }

        // ------------------------------------------------------------- ayrıştırma

        /// <summary>
        /// JSON metnini plana çevirir. <b>Exception FIRLATMAZ</b> — bozuk bir dosya yüzünden
        /// sahne açılışında patlamak yerine <c>null</c> döner ve sebebi
        /// <paramref name="error"/>'a yazar (çağıran yüksek sesle hata basar).
        /// <para>
        /// ⚠️ <see cref="JsonUtility.FromJsonOverwrite"/> kullanılır ki JSON'da YAZILMAYAN alanlar
        /// bu sınıftaki varsayılanlarında kalsın (<c>wallHeight</c> 3, <c>columnsBlockPlayer</c>
        /// true). <c>FromJson</c> ile eksik bir alan sessizce 0/false olurdu — duvarsız bir arena
        /// ya da muhafazaya girmeyen kolonlar demek.
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
                int count = dimensions.outline?.Length ?? 0;
                error = $"Geçersiz plan: 'outline' en az {MinOutlinePoints} köşe içermeli (bulunan: {count}).";
                return null;
            }

            dimensions.columns ??= Array.Empty<Column>();
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
