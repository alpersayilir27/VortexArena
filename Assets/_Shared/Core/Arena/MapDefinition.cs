using System;
using UnityEngine;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// Harita (arena) tanımı: sahne adı + hangi modlarda oynanabildiği.
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

        [Header("Uyumluluk")]
        [Tooltip("Bu haritanın desteklediği modId'ler; boş bırakılırsa tüm modlar sayılır.")]
        [SerializeField] private string[] supportedModeIds = Array.Empty<string>();

        [Header("Ortam sesi")]
        [Tooltip("Sahne yüklenir yüklenmez loop olarak başlayan ambiyans/müzik. Boş = sessiz.")]
        [SerializeField] private AudioClip ambienceClip;
        [Range(0f, 1f)]
        [Tooltip("Klibin çalma seviyesi. Silah sesleri bastırılmasın diye 0.15-0.25 arası tutulur.")]
        [SerializeField] private float ambienceVolume = 0.2f;

        /// <summary>Build listesindeki sahne adı (katalog anahtarı).</summary>
        public string SceneName => sceneName;

        /// <summary>Arayüzde gösterilen ad.</summary>
        public string DisplayName => displayName;

        /// <summary>Desteklenen modId listesi (boş = kısıt yok).</summary>
        public string[] SupportedModeIds => supportedModeIds;

        /// <summary>
        /// Sahnenin ortam sesi (ambiyans + oyun müziği); atanmamışsa harita sessizdir.
        /// Sahne yüklendiğinde <c>SceneAmbience</c> okur ve loop'lar — sahnede kurulum adımı YOKTUR.
        /// </summary>
        public AudioClip AmbienceClip => ambienceClip;

        /// <summary>Ortam sesinin seviyesi (0..1).</summary>
        public float AmbienceVolume => ambienceVolume;

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
