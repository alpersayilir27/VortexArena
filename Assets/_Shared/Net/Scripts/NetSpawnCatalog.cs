using System;
using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Net
{
    /// <summary>
    /// id → prefab records (asset: <c>_Shared/Data/NetSpawnCatalog.asset</c>).
    /// <para>
    /// RESERVED GROUNDWORK: future SERVER-COMMANDED spawns (pickup, door, shared FX) resolve here — the
    /// server sends a string id only, the prefab choice stays on the client (the protocol carries no
    /// asset references). v1 keeps only the RemoteAvatar + FX records.
    /// </para>
    /// All queries tolerate null/empty entries (a missing asset reference must not break the UI).
    /// </summary>
    [CreateAssetMenu(fileName = "NetSpawnCatalog", menuName = "VortexArena/Net Spawn Catalog")]
    public class NetSpawnCatalog : ScriptableObject
    {
        [SerializeField] private NetSpawnEntry[] entries = Array.Empty<NetSpawnEntry>();

        /// <summary>The spawn records in the catalogue (read-only).</summary>
        public IReadOnlyList<NetSpawnEntry> Entries => entries ?? Array.Empty<NetSpawnEntry>();

        /// <summary>Finds a prefab by id (case-insensitive); null when there is none.</summary>
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
