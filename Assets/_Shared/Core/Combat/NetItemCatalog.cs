using System;
using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Wire <c>netItemId</c> → <see cref="ItemDefinition"/> mapping (§6.6): the ONLY lookup table
    /// of remote rendering, resolving a snapshot byte into a prefab and a grip pose.
    /// <para>MUST live under Resources
    /// (<c>Assets/_Shared/Data/Resources/NetItemCatalog.asset</c>): <c>RemoteAvatar</c> carries no
    /// scene/prefab reference and uses <c>Resources.Load</c> — moved out, nothing is drawn in
    /// remote hands (silent failure). Same rationale as <c>WeaponCatalog</c>.</para>
    /// <para>⚠️ Array order is NOT identity — that is each definition's own <c>netItemId</c>.
    /// Reordering shifts nothing; uniqueness is protected by the guard in the
    /// <c>Configure All Build Elements</c> sync.</para>
    /// All queries tolerate null/empty input so a missing asset reference does not break the flow.
    /// </summary>
    [CreateAssetMenu(fileName = "NetItemCatalog", menuName = "VortexArena/Net Item Catalog")]
    public class NetItemCatalog : ScriptableObject
    {
        /// <summary>Resources.Load key (identical to the asset file name).</summary>
        private const string ResourcePath = "NetItemCatalog";

        private static NetItemCatalog _cached;
        private static bool _loadAttempted;

        [Tooltip("Ağda kimliği olan tüm eşyalar (silah, bomba…). Sıralama serbesttir; kimlik " +
                 "her tanımın kendi netItemId alanıdır.")]
        [SerializeField] private ItemDefinition[] items = Array.Empty<ItemDefinition>();

        /// <summary>Item definitions in the catalog.</summary>
        public ItemDefinition[] Items => items;

        // Looked up every frame (one query per remote player × hand), so the dictionary is built
        // on first call. The catalog never changes at runtime — a reloaded asset is a new
        // ScriptableObject instance, so the cache goes with it.
        private Dictionary<byte, ItemDefinition> _byNetId;

        /// <summary>
        /// Finds an item definition by <c>netItemId</c>. Null for <c>0</c> (empty-hand reservation)
        /// and for unknown ids — the caller reads that as "draw no item".
        /// </summary>
        public ItemDefinition FindByNetItemId(byte netItemId)
        {
            if (netItemId == 0)
            {
                return null;
            }

            if (_byNetId == null)
            {
                BuildIndex();
            }

            return _byNetId.TryGetValue(netItemId, out ItemDefinition def) ? def : null;
        }

        private void BuildIndex()
        {
            _byNetId = new Dictionary<byte, ItemDefinition>();
            if (items == null)
            {
                return;
            }

            for (int i = 0; i < items.Length; i++)
            {
                ItemDefinition def = items[i];
                if (def == null || !def.HasNetItemId)
                {
                    continue;
                }

                // On a clash the first entry wins and we stay SILENT here: there is nothing to do
                // at runtime, the right place is the editor guard (NetItemIdGuard).
                _byNetId[def.NetItemId] = def;
            }
        }

        /// <summary>
        /// Loads the catalog from Resources; the result is cached once.
        /// If not found it logs a SINGLE warning and returns null — callers must tolerate null.
        /// </summary>
        public static NetItemCatalog Load()
        {
            if (_cached != null)
            {
                return _cached;
            }

            if (_loadAttempted)
            {
                return null;
            }

            _loadAttempted = true;
            _cached = Resources.Load<NetItemCatalog>(ResourcePath);
            if (_cached == null)
            {
                Debug.LogWarning(
                    $"[NetItemCatalog] Resources'ta '{ResourcePath}' bulunamadı — uzak oyuncuların " +
                    "elindeki eşyalar çizilmez.");
            }

            return _cached;
        }
    }
}
