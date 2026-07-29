using System;

namespace VortexArena.Core.Editor
{
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
        public string sourceScenePath = "Assets/Arenas/Standard/Default12x12/Scenes/Default12x12.unity";

        /// <summary>
        /// Kaynak <c>MapDefinition</c> asset yolu — yeni haritanın <c>supportedModeIds</c>
        /// listesi buradan kopyalanır (boş/eksikse yeni harita kısıtsız olur).
        /// </summary>
        public string sourceMapPath = "Assets/Arenas/Standard/Default12x12/Data/Default12x12.asset";

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
        /// İsteğe bağlı <c>ArenaShapeDefinition</c> asset yolu — arena planı (zemin sınırı +
        /// kolonlar).
        /// <para>
        /// <b>Boş bırakılırsa hiçbir şey değişmez:</b> sahne kaynak arenadan bire bir kopyalanır
        /// ve geometriye dokunulmaz (sihirbazın öteden beri yaptığı iş).
        /// </para>
        /// <para>
        /// Doluysa şablondan gelen hazır zemin/duvar mesh'leri SİLİNİR, yerine plandan üretilen
        /// geometri konur ve sahnedeki <c>ArenaBoundary</c> bu asset'e + üretilen duvarlara
        /// bağlanır. Kalibrasyon işaretçileri, taban bölgeleri ve rig yerinde kalır.
        /// </para>
        /// </summary>
        public string shapePath = "";

        /// <summary>Hedef kutu: standart katalog arenası mı, işletmeye özel mi.</summary>
        public ArenaTemplateTarget target = ArenaTemplateTarget.Standard;

        /// <summary>İşletme klasör adı — yalnız <see cref="ArenaTemplateTarget.Venue"/> için.</summary>
        public string venueName = "";

        /// <summary>
        /// Yeni haritanın ekleneceği <c>GameCatalog</c> asset yolu (boş = katalog güncellenmez).
        /// <para>
        /// ⚠️ Katalog <c>Resources/</c> ALTINDADIR — prosedürel admin arayüzü onu
        /// <c>Resources.Load</c> ile okuduğu için oradan çıkarılamaz. Bu yol yanlış yazılırsa
        /// sihirbaz katalog kaydını sessizce atlar ve yeni arena admin listesinde görünmez.
        /// </para>
        /// </summary>
        public string catalogPath = "Assets/_Shared/Data/Resources/GameCatalog.asset";
    }
}
