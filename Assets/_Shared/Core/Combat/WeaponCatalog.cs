using System;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Catalog of all weapon definitions + remote shot FX prefab + foregrip indicator prefab.
    /// Like GameCatalog it MUST live under Resources
    /// (`Assets/_Shared/Data/Resources/WeaponCatalog.asset`): consumers read it via
    /// <c>Resources.Load</c>, carrying no scene/prefab reference. No admin/player split — remote
    /// shots play on the admin spectator too. All queries tolerate null/empty input so a missing
    /// asset reference does not break the flow.
    /// </summary>
    [CreateAssetMenu(fileName = "WeaponCatalog", menuName = "VortexArena/Weapon Catalog")]
    public class WeaponCatalog : ScriptableObject
    {
        /// <summary>Resources.Load key (identical to the asset file name).</summary>
        private const string ResourcePath = "WeaponCatalog";

        private static WeaponCatalog _cached;
        private static bool _loadAttempted;

        [SerializeField] private WeaponDefinition[] definitions = Array.Empty<WeaponDefinition>();
        [Tooltip("Uzak oyuncu atışlarının FX düğümü (RemoteShotFx havuzunda çoğaltılır); boşsa sade ses fallback'i üretilir.")]
        [SerializeField] private GameObject remoteShotFxPrefab;
        [Tooltip("Ön kabza SOKETİ — boş elin kumandası ön kabzaya yaklaşınca Weapon bunu kavrama kaydına koyar ve " +
                 "kabul yarıçapının iki katına ölçekler (prefab 1 m ÇAP sözleşmesiyle tasarlanır; görülen küre = " +
                 "kabul hacmi). Tüm silahlar aynı sanatı paylaşır. Boşsa soket çizilmez, kavrama yine çalışır. " +
                 "Silah kiti koşusu (Configure All Build Elements) varsayılan küreyi üretip yalnız alan BOŞSA bağlar.")]
        [SerializeField] private GameObject secondaryGripIndicatorPrefab;

        /// <summary>Weapon definitions in the catalog.</summary>
        public WeaponDefinition[] Definitions => definitions;

        /// <summary>Remote shot FX prefab (may be null).</summary>
        public GameObject RemoteShotFxPrefab => remoteShotFxPrefab;

        /// <summary>
        /// Foregrip socket prefab (null → socket not drawn). Art lives here; position/scale/alpha
        /// are driven by <c>Weapon</c> into the first Renderer's material
        /// (<c>_BaseColor</c>/<c>_Color</c>), or the <c>LineRenderer</c> color when there is none.
        /// <para>⚠️ 1 m diameter contract: authored at unit size, scaled to
        /// <c>2 × secondaryGripRadius</c> — the drawn sphere IS the acceptance volume.</para>
        /// </summary>
        public GameObject SecondaryGripIndicatorPrefab => secondaryGripIndicatorPrefab;

        /// <summary>Finds a definition by weaponId (case-insensitive); null when missing/empty.</summary>
        public WeaponDefinition FindByWeaponId(string id)
        {
            if (string.IsNullOrEmpty(id) || definitions == null)
            {
                return null;
            }

            for (int i = 0; i < definitions.Length; i++)
            {
                WeaponDefinition def = definitions[i];
                if (def != null && string.Equals(def.WeaponId, id, StringComparison.OrdinalIgnoreCase))
                {
                    return def;
                }
            }

            return null;
        }

        /// <summary>
        /// Loads the catalog from Resources; the result is cached once.
        /// If not found it logs a SINGLE warning and returns null — callers must tolerate null.
        /// </summary>
        public static WeaponCatalog Load()
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
            _cached = Resources.Load<WeaponCatalog>(ResourcePath);
            if (_cached == null)
            {
                Debug.LogWarning(
                    $"[WeaponCatalog] Resources'ta '{ResourcePath}' bulunamadı — silah tanımları ve uzak atış FX'i çalışmaz.");
            }

            return _cached;
        }
    }
}
