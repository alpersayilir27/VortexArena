using System;
using UnityEngine;

namespace VortexArena.Core.Audio
{
    /// <summary>
    /// Moda ve haritaya göre değişen duyuru seslerinin tek kaynağı: "hangi modda, hangi haritada,
    /// hangi anda ne çalsın" sorusunun cevabı. Tüm haritalarda ORTAK olan sesler burada DEĞİL
    /// <see cref="GameSoundBank"/>'tedir.
    /// <para>
    /// ⚠️ Asset'in yeri <c>Assets/_Shared/Data/Resources/ModeAudioRegistry.asset</c> — hiçbir
    /// sahneden referansı yoktur, <see cref="Load"/> onu <c>Resources.Load</c> ile alır
    /// (<c>GameCatalog</c> ile aynı gerekçe). Klasörden çıkarılırsa ya da adı değişirse moda özel
    /// tüm sesler sessizce susar.
    /// </para>
    /// <para>
    /// <b>Kural eşleşmesi:</b> boş <c>modeId</c> = her mod, boş <c>sceneName</c> = her harita.
    /// Eşleşen kurallar arasından <b>en spesifik olan</b> kazanır (mod eşleşmesi haritadan ağır
    /// basar); aynı spesiflikte birden çok kural varsa listedeki İLK'i kullanılır. Böylece genel
    /// bir satır ("her mod, her harita") tavan olarak durur ve tek bir haritaya özel satır onu
    /// yalnız o haritada ezer.
    /// </para>
    /// <para>
    /// <b>Klip listesi rastgele seçilir:</b> aynı tetikleyiciye birden çok klip yazmak varyasyon
    /// üretmenin yoludur; tek klip yazmak da geçerlidir.
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "ModeAudioRegistry", menuName = "VortexArena/Mode Audio Registry")]
    public class ModeAudioRegistry : ScriptableObject
    {
        /// <summary>Resources altındaki asset adı.</summary>
        public const string ResourceName = "ModeAudioRegistry";

        /// <summary>Tek bir "şu koşulda şu ses" satırı.</summary>
        [Serializable]
        public class Rule
        {
            [Tooltip("Hangi mod (tdm, ffa, tournament, lobby). BOŞ = her mod.")]
            [SerializeField] private string modeId = "";

            [Tooltip("Hangi harita — sahne adıyla BİREBİR aynı. BOŞ = her harita.")]
            [SerializeField] private string sceneName = "";

            [Tooltip("Sesin çalacağı an.")]
            [SerializeField] private ModeAudioEvent trigger = ModeAudioEvent.RoundStart;

            [Tooltip("Bu andan biri rastgele seçilir. Boş bırakılan girdi atlanır.")]
            [SerializeField] private AudioClip[] clips = Array.Empty<AudioClip>();

            [Range(0f, 1f)]
            [Tooltip("Klibin çalma seviyesi.")]
            [SerializeField] private float volume = 1f;

            [Min(0f)]
            [Tooltip("Yalnız uyarı tetikleyicilerinde: süre bitmeden kaç saniye önce çalsın.")]
            [SerializeField] private float warningSeconds = 5f;

            /// <summary>Kuralın bağlı olduğu modId; boş = her mod.</summary>
            public string ModeId => modeId;

            /// <summary>Kuralın bağlı olduğu sahne adı; boş = her harita.</summary>
            public string SceneName => sceneName;

            /// <summary>Sesin çalacağı an.</summary>
            public ModeAudioEvent Trigger => trigger;

            /// <summary>Uyarı tetikleyicilerinde eşik (saniye).</summary>
            public float WarningSeconds => warningSeconds;

            /// <summary>Klibin çalma seviyesi (0..1).</summary>
            public float Volume => volume;

            /// <summary>
            /// Bu kuralın en son çaldığı klip — ardışık tekrarı elemek için. <b>Serialize
            /// EDİLMEZ</b>: varyasyon oturumun içinde anlamlıdır, diske yazılacak bir ayar değil.
            /// </summary>
            [NonSerialized] private AudioClip _lastPicked;

            /// <summary>
            /// Listeden rastgele bir klip; hepsi boşsa null.
            /// <para>⚠️ <b>Bir önceki seçim elenir</b> (liste iki ya da daha fazla dolu klip
            /// taşıyorsa): saf rastgelede aynı klip peş peşe gelebiliyor ve tur başına tek duyuru
            /// çalan bir sistemde bu "hep aynı ses" olarak duyuluyor. İki klipte sonuç sırayla
            /// çalmaktır — kabul edilen bir sonuç, çünkü varyasyonun görünürlüğü rastgeleliğin
            /// saflığından önemli.</para>
            /// </summary>
            public AudioClip PickClip()
            {
                if (clips == null)
                {
                    return null;
                }

                // Dolu klipler; bir öncekini eleyerek. Eleme sonucu liste boşalırsa (tek dolu
                // klip var) eleme yapılmaz — o kural her turda aynı sesi çalar, doğrusu da bu.
                AudioClip[] pool = Collect(_lastPicked);
                if (pool.Length == 0)
                {
                    pool = Collect(null);
                }

                if (pool.Length == 0)
                {
                    return null;
                }

                _lastPicked = pool[UnityEngine.Random.Range(0, pool.Length)];
                return _lastPicked;
            }

            /// <summary>Dolu klipleri toplar; <paramref name="exclude"/> varsa onu atlar.</summary>
            private AudioClip[] Collect(AudioClip exclude)
            {
                int count = 0;
                for (int i = 0; i < clips.Length; i++)
                {
                    if (clips[i] != null && clips[i] != exclude)
                    {
                        count++;
                    }
                }

                var pool = new AudioClip[count];
                int next = 0;
                for (int i = 0; i < clips.Length && next < count; i++)
                {
                    if (clips[i] != null && clips[i] != exclude)
                    {
                        pool[next++] = clips[i];
                    }
                }

                return pool;
            }

            /// <summary>
            /// Kural verilen bağlama uyuyor mu. Boş alan "kısıt yok" demektir.
            /// </summary>
            public bool Matches(ModeAudioEvent wanted, string activeModeId, string activeSceneName)
            {
                return trigger == wanted &&
                       Fits(modeId, activeModeId) &&
                       Fits(sceneName, activeSceneName);
            }

            /// <summary>
            /// Eşleşmenin darlığı: mod 2, harita 1 puan. Yüksek puan kazanır — mod eşleşmesinin
            /// haritadan ağır basması bilinçli: aynı arena birden çok modda oynanıyor.
            /// </summary>
            public int Specificity()
            {
                int score = 0;
                if (!string.IsNullOrEmpty(modeId))
                {
                    score += 2;
                }

                if (!string.IsNullOrEmpty(sceneName))
                {
                    score += 1;
                }

                return score;
            }

            private static bool Fits(string filter, string active)
            {
                return string.IsNullOrEmpty(filter) ||
                       string.Equals(filter, active, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Tooltip("Sırası önemsizdir: en spesifik kural kazanır, eşitlikte listedeki ilki.")]
        [SerializeField] private Rule[] rules = Array.Empty<Rule>();

        private static ModeAudioRegistry _cached;
        private static bool _loaded;

        /// <summary>
        /// Kaynak asset'i <c>Resources</c>'tan yükler (tek sefer, sonuç önbelleklenir).
        /// Asset yoksa null döner ve moda özel tüm sesler sessizce atlanır.
        /// </summary>
        public static ModeAudioRegistry Load()
        {
            if (_loaded)
            {
                return _cached;
            }

            _loaded = true;
            _cached = Resources.Load<ModeAudioRegistry>(ResourceName);
            return _cached;
        }

        /// <summary>
        /// Verilen bağlama uyan en spesifik kuralı bulur. Eşleşme yoksa <c>false</c> döner —
        /// çağıranın kontrol yazması gerekmez, o an ses yok demektir.
        /// </summary>
        public bool TryResolve(ModeAudioEvent trigger, string modeId, string sceneName, out Rule rule)
        {
            rule = null;
            if (rules == null)
            {
                return false;
            }

            int best = -1;
            for (int i = 0; i < rules.Length; i++)
            {
                Rule candidate = rules[i];
                if (candidate == null || !candidate.Matches(trigger, modeId, sceneName))
                {
                    continue;
                }

                int score = candidate.Specificity();
                if (score > best)
                {
                    best = score;
                    rule = candidate;
                }
            }

            return rule != null;
        }
    }
}
