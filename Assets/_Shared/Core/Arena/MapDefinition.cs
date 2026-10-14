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
        [Tooltip("Oyun tipi — operatörün ilk seçimi: Hızlı Savaş, Zombi ya da Çocuk Oyunları. " +
                 "Harita yalnız kendi tipindeki modlara açıktır.")]
        // Default QuickBattle: existing assets resolve correctly WITHOUT being touched.
        [SerializeField] private GameType gameType = GameType.QuickBattle;

        [Header("Uyumluluk")]
        [Tooltip("Bu haritanın desteklediği modId'ler; boş bırakılırsa tüm modlar sayılır.")]
        [SerializeField] private string[] supportedModeIds = Array.Empty<string>();

        [Header("Köstebek modu")]
        [Range(0.25f, 4f)]
        [Tooltip("Aynı anda ayakta duran köstebek yoğunluğu; 1 = temel yoğunluk. Yalnız Köstebek modunda okunur, " +
                 "diğer haritalarda hiçbir şeye dokunmaz.")]
        // Default = what a NEW venue starts with: existing assets pick it up WITHOUT being touched.
        [SerializeField] private float moleDensity = 1.5f;

        [Header("Zombi modu")]
        [Range(0.25f, 2f)]
        [Tooltip("Zombi modunda tempo: 1 = varsayılan; küçük değer doğum aralığını ve dalga arasını uzatır, " +
                 "aynı anda ayaktaki zombiyi azaltır. Yalnız zombi modu okur.")]
        // Default = what a NEW venue starts with: existing assets pick it up WITHOUT being touched.
        [SerializeField] private float zombiePace = 1f;

        [Header("Ortam sesi")]
        [Tooltip("Sahne yüklenir yüklenmez loop olarak başlayan ambiyans. Boş = sessiz.")]
        [SerializeField] private AudioClip ambienceClip;
        [Range(0f, 1f)]
        [Tooltip("Klibin çalma seviyesi. Silah sesleri bastırılmasın diye 0.15-0.25 arası tutulur.")]
        [SerializeField] private float ambienceVolume = 0.2f;

        [Tooltip("Ambiyansın ÜSTÜNE çalan müzik döngüsü — ayrı kanaldır, ambiyanstan bağımsız kısılır. Boş = müzik yok.")]
        [SerializeField] private AudioClip musicClip;
        [Range(0f, 1f)]
        [Tooltip("Müzik klibinin çalma seviyesi.")]
        [SerializeField] private float musicVolume = 0.5f;

        [Header("Anons")]
        [Tooltip("Süre bitmeden son saniyelerde çalan uyarı sesi. Kapalı = bu haritada hiç çalmaz.")]
        // Default on: existing assets keep the warning WITHOUT being touched.
        [SerializeField] private bool timeWarningSound = true;

        [Header("Admin görünümü")]
        [Tooltip("Yalnız admin/izleyici bilgisayarının ekranını aydınlatır, kaskta HİÇBİR etkisi yoktur. " +
                 "0 = dokunma; 1.5 karanlık bir arenayı tepeden izlenebilir yapar.")]
        // Default 0: every existing map stays exactly as authored.
        [SerializeField] private float adminPostExposure;
        [Tooltip("Admin ekranında sis mesafesi bu kadar katlanır (üstel siste yoğunluk bölünür). " +
                 "1 = dokunma. Kaskta hiçbir etkisi yoktur.")]
        [SerializeField] private float adminFogScale = 1f;
        [Tooltip("Admin ekranında ortam ışığı çarpanı; 1 = dokunma. Çoğunlukla ışık haritası olmayan " +
                 "nesneleri etkiler. Kaskta hiçbir etkisi yoktur.")]
        [SerializeField] private float adminAmbientScale = 1f;

        /// <summary>Scene name in the build list (the catalog key).</summary>
        public string SceneName => sceneName;

        /// <summary>Name shown in the UI.</summary>
        public string DisplayName => displayName;

        /// <summary>Game type the map belongs to (§11).</summary>
        public GameType GameType => gameType;

        /// <summary>Supported modId list (empty = no restriction).</summary>
        public string[] SupportedModeIds => supportedModeIds;

        /// <summary>Mole density multiplier of the map (1 = base density); read only by the mole
        /// mode, through maps.json.</summary>
        public float MoleDensity => moleDensity;

        /// <summary>Zombie tempo multiplier of the map (1 = default pace); read only by the zombie
        /// mode, through maps.json.</summary>
        public float ZombiePace => zombiePace;

        /// <summary>The scene's ambience clip; silent when unassigned.
        /// <c>SceneAmbience</c> reads and loops it on scene load — there is NO scene setup step.</summary>
        public AudioClip AmbienceClip => ambienceClip;

        /// <summary>Ambience level (0..1).</summary>
        public float AmbienceVolume => ambienceVolume;

        /// <summary>The scene's music clip, looping ON TOP OF the ambience; no music when
        /// unassigned. Read by <c>SceneAmbience</c> on its own mix channel.</summary>
        public AudioClip MusicClip => musicClip;

        /// <summary>Music level (0..1).</summary>
        public float MusicVolume => musicVolume;

        /// <summary>Whether the round/match end time warning plays on this map.</summary>
        public bool TimeWarningSound => timeWarningSound;

        /// <summary>Extra EV on the admin spectator view (0 = untouched). Admin build only.</summary>
        public float AdminPostExposure => adminPostExposure;

        /// <summary>Fog distance multiplier on the admin view (1 = untouched).</summary>
        public float AdminFogScale => adminFogScale;

        /// <summary>Ambient intensity multiplier on the admin view (1 = untouched).</summary>
        public float AdminAmbientScale => adminAmbientScale;

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
