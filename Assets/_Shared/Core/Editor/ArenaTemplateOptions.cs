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
        /// <summary>Kopyalanacak kaynak sahnenin asset yolu.</summary>
        public string sourceScenePath = "Assets/Arenas/Standard/A10x10/Scenes/Arena10x10.unity";

        /// <summary>
        /// Kaynak <c>MapDefinition</c> asset yolu — yeni haritanın <c>supportedModeIds</c>
        /// listesi buradan kopyalanır (boş/eksikse yeni harita kısıtsız olur).
        /// </summary>
        public string sourceMapPath = "Assets/Arenas/Standard/A10x10/Data/A10x10.asset";

        /// <summary>Arena kutusunun klasör adı ve MapDefinition asset adı (ör. <c>A12x12</c>).</summary>
        public string arenaId = "";

        /// <summary>
        /// Yeni sahnenin adı = KATALOG ANAHTARI (<c>start_match.sceneName</c> ile birebir).
        /// Boş bırakılırsa <see cref="ArenaTemplateWizard.SuggestSceneName"/> değeri kullanılır.
        /// </summary>
        public string sceneName = "";

        /// <summary>Arayüzde gösterilen ad (ör. "Standart 12×12").</summary>
        public string displayName = "";

        /// <summary>Fiziksel alan genişliği (metre, arena X ekseni).</summary>
        public float sizeX = 12f;

        /// <summary>Fiziksel alan derinliği (metre, arena Z ekseni).</summary>
        public float sizeZ = 12f;

        /// <summary>Takım başına üretilecek SpawnPoint sayısı (en az 1).</summary>
        public int spawnSlotsPerTeam = 4;

        /// <summary>Hedef kutu: standart katalog arenası mı, işletmeye özel mi.</summary>
        public ArenaTemplateTarget target = ArenaTemplateTarget.Standard;

        /// <summary>İşletme klasör adı — yalnız <see cref="ArenaTemplateTarget.Venue"/> için.</summary>
        public string venueName = "";

        /// <summary>Yeni haritanın ekleneceği <c>GameCatalog</c> asset yolu (boş = katalog güncellenmez).</summary>
        public string catalogPath = "Assets/_Shared/Data/GameCatalog.asset";
    }
}
