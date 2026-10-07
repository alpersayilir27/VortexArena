using System;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Catalog of all weapon definitions + remote shot FX prefab + near-miss audio.
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
        [Tooltip("Yakından geçen düşman mermisinin vızıltı sesleri. Boşsa hiç çalmaz.")]
        [SerializeField] private AudioClip[] nearMissClips = Array.Empty<AudioClip>();
        [Range(0f, 1f)]
        [Tooltip("Vızıltı sesinin seviyesi (geçiş mesafesine göre ayrıca kısılır).")]
        [SerializeField] private float nearMissVolume = 0.8f;

        /// <summary>Weapon definitions in the catalog.</summary>
        public WeaponDefinition[] Definitions => definitions;

        /// <summary>Remote shot FX prefab (may be null).</summary>
        public GameObject RemoteShotFxPrefab => remoteShotFxPrefab;

        /// <summary>Near-miss whiz clips (<see cref="NearMissWhizFx"/>); empty = feature off.</summary>
        public AudioClip[] NearMissClips => nearMissClips;

        /// <summary>Near-miss whiz level (0-1).</summary>
        public float NearMissVolume => nearMissVolume;

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
