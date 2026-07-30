using System;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// Sihirbazın arena ölçüsünü nereden okuyacağı. İki seçenek vardır ve <b>kaynak
    /// ZORUNLUDUR</b>: ölçüsüz bir arenanın <c>ArenaBoundary</c>'si devre dışı kalır, yani arena
    /// sessizce sınırsız olurdu. İki kaynak da aynı temsile (<c>ArenaDimensions</c> JSON'u)
    /// indirgenir ve aynı üretim kapısından geçer.
    /// </summary>
    public enum ArenaGeometrySource
    {
        /// <summary>Elle yazılan boyut dosyası (<c>ArenaDimensions</c> JSON'u, <c>TextAsset</c>).</summary>
        DimensionsJson,

        /// <summary>
        /// Kaba bloklardan oluşan bir TestMesh prefabı. Bloklar bir <c>ArenaDimensions</c> planına
        /// ÇIKARILIR, plan arena kutusunun <c>Data/</c> klasörüne JSON olarak yazılır ve geometri
        /// oradan üretilir — yani sonuç elle yazılmış bir JSON'dan ayırt edilemez.
        /// </summary>
        TestMesh
    }

    /// <summary>
    /// <see cref="ArenaTemplateWizard.Create"/> girdisi — sihirbaz penceresinin alanlarının
    /// veri karşılığı.
    /// <para>
    /// Asset referansları TİP DEĞİL YOL olarak tutulur: pencere ObjectField ↔ yol dönüşümünü
    /// kendisi yapar, otomasyon (MCP / batch) ise sadece string atayarak çağırabilir.
    /// Alanlar düz <c>public</c>'tir (Unity serileştirmesi + reflection dostu).
    /// </para>
    /// </summary>
    [Serializable]
    public class ArenaTemplateOptions
    {
        /// <summary>
        /// Kopyalanacak kaynak sahnenin asset yolu.
        /// <para>
        /// Varsayılan <b>Default12x12</b>'dir: harita dizaynı taşımayan, yalnız ağa bağlanmak için
        /// gereken bileşenleri (kalibrasyon, poz, HUD, sınır, taban, raf, <c>VA_CameraRig</c>)
        /// içeren TEK KAYNAK arena. Dizaynlı bir arenadan türetmek, o arenanın geometrisini de
        /// yeni kutuya kopyalar ve elle temizlenmesi gerekirdi.
        /// </para>
        /// <para>
        /// ⚠️ <b>Farklı ÖLÇÜDEKİ arena bundan türetilmez</b> — 10×10 bir arena 12×12 duvar/zeminle
        /// gelirdi. O ölçü için kendi <c>Default</c>'unu kur (ölçekleme bilinçli olarak yoktur).
        /// </para>
        /// </summary>
        public string sourceScenePath = "Assets/Arenas/Template/Default12x12/Scenes/Default12x12.unity";

        /// <summary>
        /// Kaynak <c>MapDefinition</c> asset yolu — yeni haritanın <c>supportedModeIds</c>
        /// listesi buradan kopyalanır (boş/eksikse yeni harita kısıtsız olur).
        /// </summary>
        public string sourceMapPath = "Assets/Arenas/Template/Default12x12/Data/Default12x12.asset";

        /// <summary>Arena kutusunun klasör adı ve MapDefinition asset adı (ör. <c>A12x12</c>).</summary>
        public string arenaId = "";

        /// <summary>
        /// Yeni sahnenin adı = KATALOG ANAHTARI (<c>start_match.sceneName</c> ile birebir).
        /// Boş bırakılırsa <see cref="ArenaTemplateWizard.SuggestSceneName"/> değeri kullanılır.
        /// </summary>
        public string sceneName = "";

        /// <summary>Arayüzde gösterilen ad (ör. "Standart 12×12").</summary>
        public string displayName = "";

        /// <summary>
        /// Geometri kaynağı — hangi yol alanının okunacağını bu belirler
        /// (<see cref="dimensionsJsonPath"/> / <see cref="testMeshPath"/>).
        /// <para>
        /// ⚠️ Seçilen kaynağa AİT OLMAYAN yol alanı görmezden gelinir — bir arenanın ölçüsü tek
        /// bir yerden gelmeli.
        /// </para>
        /// </summary>
        public ArenaGeometrySource geometrySource = ArenaGeometrySource.DimensionsJson;

        /// <summary>
        /// Boyut dosyası (<c>ArenaDimensions</c> JSON'u) asset yolu — yalnız
        /// <see cref="ArenaGeometrySource.DimensionsJson"/> seçiliyken okunur.
        /// <para>
        /// Şablondan gelen hazır zemin/duvar mesh'leri SİLİNİR, geometri plandan üretilir ve
        /// sahnedeki <c>ArenaBoundary.dimensionsJson</c> bu <c>TextAsset</c>'e bağlanır.
        /// </para>
        /// <para>
        /// ⚠️ Boş bırakılır ya da ayrıştırılamazsa sahne şablondan OLDUĞU GİBİ kalır (sihirbaz
        /// yarıda kesilmez) — ama arena ölçüsüz olur ve sonuç uyarısı bunu yüksek sesle söyler.
        /// </para>
        /// </summary>
        public string dimensionsJsonPath = "";

        /// <summary>
        /// TestMesh kökünün (kaba blok yığını) prefab asset yolu — yalnız
        /// <see cref="ArenaGeometrySource.TestMesh"/> seçiliyken okunur.
        /// <para>
        /// TestMesh bir <c>ArenaDimensions</c> planına çıkarılır, plan yeni arena kutusunun
        /// <c>Data/</c> klasörüne <c>&lt;sahneAdı&gt;_dimensions.json</c> olarak YAZILIR ve
        /// geometri oradan üretilir; <c>ArenaBoundary.dimensionsJson</c> o dosyaya bağlanır.
        /// Yani iki kaynak arasındaki tek fark JSON'un kim tarafından yazıldığıdır.
        /// </para>
        /// </summary>
        public string testMeshPath = "";

        /// <summary>
        /// Mekan (işletme) klasör adı — <b>ZORUNLU</b>. Arena
        /// <c>Assets/Arenas/Venues/&lt;venueName&gt;/&lt;arenaId&gt;</c> altına üretilir.
        /// <para>
        /// Mekansız arena kutusu yoktur: haritanın mekanı yalnız klasör yolundan türetilir
        /// (<c>MapDefinition</c>'da mekan alanı YOKTUR) ve sunucu açılışta operatöre bu mekanları
        /// sorar. Mekan dışına üretilen bir arena o listede sahte bir seçenek açardı.
        /// </para>
        /// </summary>
        public string venueName = "";

        /// <summary>
        /// Seçili kaynağa ait yol alanı (<see cref="dimensionsJsonPath"/> ya da
        /// <see cref="testMeshPath"/>). Sihirbaz "Oluştur" düğmesini bu boşken kapatır: kaynak
        /// zorunludur, ölçüsüz arena üretilmez.
        /// </summary>
        public string SourcePath()
        {
            return geometrySource == ArenaGeometrySource.TestMesh ? testMeshPath : dimensionsJsonPath;
        }
    }
}
