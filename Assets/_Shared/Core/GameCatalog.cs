using System;
using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core.Arena;

namespace VortexArena.Core
{
    /// <summary>
    /// Tüm mod + harita tanımlarının kataloğu (asset: <c>_Shared/Data/GameCatalog.asset</c>).
    /// AdminConsole mod/harita seçim arayüzü ve mod HUD eşlemesi bunu okur.
    /// <para>
    /// SUNUCUYA KATALOG GEREKMEZ: admin yalnız <c>start_match{modeId, sceneName}</c> gönderir;
    /// sunucu modId'yi kendi IGameMode kayıtlarıyla eşler, bilinmeyeni reddedip loglar.
    /// </para>
    /// Tüm sorgular null/boş girişlere dayanıklıdır (eksik asset referansı arayüzü kırmasın).
    /// </summary>
    [CreateAssetMenu(fileName = "GameCatalog", menuName = "VortexArena/Game Catalog")]
    public class GameCatalog : ScriptableObject
    {
        [SerializeField] private ModeDefinition[] modes = Array.Empty<ModeDefinition>();
        [SerializeField] private MapDefinition[] maps = Array.Empty<MapDefinition>();

        /// <summary>Katalogdaki mod tanımları.</summary>
        public ModeDefinition[] Modes => modes;

        /// <summary>Katalogdaki harita tanımları.</summary>
        public MapDefinition[] Maps => maps;

        /// <summary>modId ile mod tanımı bulur; yoksa null.</summary>
        public ModeDefinition FindMode(string modeId)
        {
            if (string.IsNullOrEmpty(modeId) || modes == null)
            {
                return null;
            }

            for (int i = 0; i < modes.Length; i++)
            {
                ModeDefinition mode = modes[i];
                if (mode != null && string.Equals(mode.ModeId, modeId, StringComparison.OrdinalIgnoreCase))
                {
                    return mode;
                }
            }

            return null;
        }

        /// <summary>Sahne adı ile harita tanımı bulur; yoksa null.</summary>
        public MapDefinition FindMap(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName) || maps == null)
            {
                return null;
            }

            for (int i = 0; i < maps.Length; i++)
            {
                MapDefinition map = maps[i];
                if (map != null && string.Equals(map.SceneName, sceneName, StringComparison.OrdinalIgnoreCase))
                {
                    return map;
                }
            }

            return null;
        }

        /// <summary>
        /// Verilen modda oynanabilir haritalar: modun kendi listesi doluysa o liste,
        /// boşsa katalogdaki tüm haritalar taranır; her aday ayrıca
        /// <see cref="MapDefinition.SupportsMode"/> süzgecinden geçer.
        /// </summary>
        public List<MapDefinition> MapsForMode(string modeId)
        {
            var result = new List<MapDefinition>();
            if (string.IsNullOrEmpty(modeId))
            {
                return result;
            }

            ModeDefinition mode = FindMode(modeId);
            MapDefinition[] pool = mode != null && mode.Maps != null && mode.Maps.Length > 0 ? mode.Maps : maps;
            if (pool == null)
            {
                return result;
            }

            for (int i = 0; i < pool.Length; i++)
            {
                MapDefinition map = pool[i];
                if (map == null || !map.SupportsMode(modeId) || result.Contains(map))
                {
                    continue;
                }

                result.Add(map);
            }

            return result;
        }
    }
}
