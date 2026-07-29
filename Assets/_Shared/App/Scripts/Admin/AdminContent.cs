using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core;
using VortexArena.Core.Arena;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// Admin arayüzünün içerik kataloğuna (mod/harita) erişimi.
    /// <para>
    /// Admin arayüzü tamamen prosedürel olduğu için <c>[SerializeField]</c> ile katalog
    /// bağlanamaz; asset <c>Assets/_Shared/Data/Resources/GameCatalog.asset</c> altında durur ve
    /// bir kez <see cref="Resources.Load{T}(string)"/> ile yüklenir. Klasör değişirse mod/harita
    /// seçicisi boş kalır (uyarı basılır) — maç başlatma o zaman yalnız sunucu konsolundan yapılır.
    /// </para>
    /// </summary>
    public static class AdminContent
    {
        /// <summary>Resources altındaki katalog adı (uzantısız).</summary>
        public const string CatalogResourceName = "GameCatalog";

        private static GameCatalog _catalog;
        private static bool _loadAttempted;

        /// <summary>İçerik kataloğu; bulunamazsa null (bir kez uyarı basılır).</summary>
        public static GameCatalog Catalog
        {
            get
            {
                if (_catalog != null || _loadAttempted)
                {
                    return _catalog;
                }

                _loadAttempted = true;
                _catalog = Resources.Load<GameCatalog>(CatalogResourceName);
                if (_catalog == null)
                {
                    Debug.LogWarning(
                        $"[AdminContent] '{CatalogResourceName}' Resources altında bulunamadı " +
                        "(beklenen yer: Assets/_Shared/Data/Resources/GameCatalog.asset); " +
                        "mod/harita seçicisi boş kalacak.");
                }

                return _catalog;
            }
        }

        /// <summary>Katalogdaki <b>başlatılabilir</b> modlar: modId'si dolu ve lobi profili
        /// olmayanlar. Lobi profili (§10.7) katalogda olmak zorundadır — istemci silah loadout'unu
        /// oradan çözüyor — ama sunucuda <c>IGameMode</c> karşılığı yoktur, yani seçiciye konsaydı
        /// operatöre her seferinde sessizce reddedilen bir düğme gösterilirdi.</summary>
        public static void CollectModes(List<ModeDefinition> buffer)
        {
            if (buffer == null)
            {
                return;
            }

            buffer.Clear();
            ModeDefinition[] modes = Catalog != null ? Catalog.Modes : null;
            if (modes == null)
            {
                return;
            }

            for (int i = 0; i < modes.Length; i++)
            {
                if (modes[i] != null && !string.IsNullOrEmpty(modes[i].ModeId) && !modes[i].IsLobbyProfile)
                {
                    buffer.Add(modes[i]);
                }
            }
        }

        /// <summary>
        /// Verilen modun <b>bu mekanda</b> oynanabildiği haritalar (sahne adı dolu olanlar).
        /// <para>İki süzgeç arka arkaya çalışır: (1) mod uyumu — katalogdan, (2) mekan —
        /// <see cref="AdminSelection.IsInVenue"/> ile sunucunun açılışta seçtiği mekandan (§11).
        /// Katalog tüm projeyi tanır; hangi arenaların oynatılabildiğine sunucu karar verir, bu
        /// yüzden mekan süzgeci yerelde üretilmez — sunucu <c>admin_state</c> ile bildirir.</para>
        /// </summary>
        public static void CollectMaps(string modeId, List<MapDefinition> buffer)
        {
            if (buffer == null)
            {
                return;
            }

            buffer.Clear();
            if (Catalog == null || string.IsNullOrEmpty(modeId))
            {
                return;
            }

            List<MapDefinition> maps = Catalog.MapsForMode(modeId);
            if (maps == null)
            {
                return;
            }

            for (int i = 0; i < maps.Count; i++)
            {
                if (maps[i] != null && !string.IsNullOrEmpty(maps[i].SceneName) &&
                    AdminSelection.IsInVenue(maps[i].SceneName))
                {
                    buffer.Add(maps[i]);
                }
            }
        }

        /// <summary>Modun görünen adı; katalogda yoksa modId'nin kendisi.</summary>
        public static string ModeDisplayName(string modeId)
        {
            if (string.IsNullOrEmpty(modeId))
            {
                return "-";
            }

            ModeDefinition mode = Catalog != null ? Catalog.FindMode(modeId) : null;
            return mode != null && !string.IsNullOrEmpty(mode.DisplayName) ? mode.DisplayName : modeId;
        }
    }
}
