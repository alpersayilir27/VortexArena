using System;
using UnityEngine;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// Harita (arena) tanımı: sahne adı + fiziksel boyut + hangi modlarda oynanabildiği.
    /// <para>
    /// <see cref="SceneName"/> Build Settings'teki sahne adıyla BİREBİR aynı olmalıdır —
    /// admin <c>start_match{sceneName}</c> gönderir, sunucu bu string'i tüm istemcilerin
    /// <c>hello.scenes</c> listesinde arar (Docs/ArenaNet-Protokol.md §10.1).
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "Map", menuName = "VortexArena/Map Definition")]
    public class MapDefinition : ScriptableObject
    {
        [Header("Kimlik")]
        [Tooltip("Build listesindeki sahne adıyla BİREBİR aynı (katalog anahtarı).")]
        [SerializeField] private string sceneName = "";
        [SerializeField] private string displayName = "";

        [Header("Fiziksel alan")]
        [Tooltip("Arena zemin ölçüsü (metre): X × Z.")]
        [SerializeField] private Vector2 size = new Vector2(10f, 10f);
        [Tooltip("Takım başına sahnedeki SpawnPoint sayısı.")]
        [SerializeField] private int spawnSlotsPerTeam = 4;

        [Header("Uyumluluk")]
        [Tooltip("Bu haritanın desteklediği modId'ler; boş bırakılırsa tüm modlar sayılır.")]
        [SerializeField] private string[] supportedModeIds = Array.Empty<string>();

        /// <summary>Build listesindeki sahne adı (katalog anahtarı).</summary>
        public string SceneName => sceneName;

        /// <summary>Arayüzde gösterilen ad.</summary>
        public string DisplayName => displayName;

        /// <summary>Arena zemin ölçüsü (metre, X × Z).</summary>
        public Vector2 Size => size;

        /// <summary>Takım başına spawn slot sayısı.</summary>
        public int SpawnSlotsPerTeam => spawnSlotsPerTeam;

        /// <summary>Desteklenen modId listesi (boş = kısıt yok).</summary>
        public string[] SupportedModeIds => supportedModeIds;

        /// <summary>
        /// Verilen mod bu haritada oynanabilir mi. Liste boş/eksikse kısıt yok sayılır
        /// (yeni harita eklerken unutulan alan modu gizlemesin).
        /// </summary>
        public bool SupportsMode(string modeId)
        {
            if (string.IsNullOrEmpty(modeId))
            {
                return false;
            }

            if (supportedModeIds == null || supportedModeIds.Length == 0)
            {
                return true;
            }

            for (int i = 0; i < supportedModeIds.Length; i++)
            {
                if (string.Equals(supportedModeIds[i], modeId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
