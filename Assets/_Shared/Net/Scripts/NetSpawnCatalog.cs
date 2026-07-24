using System;
using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Net
{
    /// <summary>
    /// id → prefab kayıtları (asset: <c>_Shared/Data/NetSpawnCatalog.asset</c>).
    /// <para>
    /// REZERV ALTYAPI: ileride SUNUCU-KOMUTLU spawn (pickup, kapı, paylaşılan FX) bu
    /// katalogdan çözülecek — sunucu yalnız string id gönderir, prefab seçimi istemcide
    /// kalır (protokolde varlık referansı taşınmaz). v1'de yalnız RemoteAvatar + FX
    /// kayıtları tutulur.
    /// </para>
    /// Tüm sorgular null/boş girişlere dayanıklıdır (eksik asset referansı arayüzü kırmasın).
    /// </summary>
    [CreateAssetMenu(fileName = "NetSpawnCatalog", menuName = "VortexArena/Net Spawn Catalog")]
    public class NetSpawnCatalog : ScriptableObject
    {
        [SerializeField] private NetSpawnEntry[] entries = Array.Empty<NetSpawnEntry>();

        /// <summary>Katalogdaki spawn kayıtları (salt okunur).</summary>
        public IReadOnlyList<NetSpawnEntry> Entries => entries ?? Array.Empty<NetSpawnEntry>();

        /// <summary>id ile prefab bulur (büyük/küçük harf duyarsız); yoksa null.</summary>
        public GameObject Find(string id)
        {
            if (string.IsNullOrEmpty(id) || entries == null)
            {
                return null;
            }

            for (int i = 0; i < entries.Length; i++)
            {
                NetSpawnEntry entry = entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.Id))
                {
                    continue;
                }

                if (string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.Prefab;
                }
            }

            return null;
        }
    }
}
