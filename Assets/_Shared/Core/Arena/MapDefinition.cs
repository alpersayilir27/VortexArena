using System;
using UnityEngine;

namespace VortexArena.Core.Arena
{
    /// <summary>Map (arena) definition: scene name + which modes it can be played in.
    /// <para><see cref="SceneName"/> must match the Build Settings scene name EXACTLY — the admin
    /// sends <c>start_match{sceneName}</c> and the server looks that string up in every client's
    /// <c>hello.scenes</c> list (Docs/ArenaNet-Protokol.md §10.1).</para></summary>
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

        /// <summary>Scene name in the build list (the catalog key).</summary>
        public string SceneName => sceneName;

        /// <summary>Name shown in the UI.</summary>
        public string DisplayName => displayName;

        /// <summary>Supported modId list (empty = no restriction).</summary>
        public string[] SupportedModeIds => supportedModeIds;

        /// <summary>The scene's ambience clip (ambience + game music); silent when unassigned.
        /// <c>SceneAmbience</c> reads and loops it on scene load — there is NO scene setup step.</summary>
        public AudioClip AmbienceClip => ambienceClip;

        /// <summary>Ambience level (0..1).</summary>
        public float AmbienceVolume => ambienceVolume;

        /// <summary>Can the given mode be played on this map. An empty/missing list counts as no
        /// restriction, so a field forgotten on a new map does not hide the mode.</summary>
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
