using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core.Arena;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Core.Audio
{
    /// <summary>
    /// Haritanın ortam sesi (ambiyans + oyun müziği): sahne yüklenir yüklenmez başlar, loop'lar
    /// ve <b>harita değişene kadar hiç durmaz</b> — maçın başlaması, bitmesi, lobiye dönülmesi
    /// müziğe dokunmaz. Hangi klibin çalacağı sahnenin <see cref="MapDefinition"/>'ından okunur
    /// (<see cref="MapDefinition.AmbienceClip"/>).
    /// <para>
    /// <b>Kendini önyükleyen kalıcı tekildir; sahneye bileşen KONMAZ</b> (<c>WeaponGranter</c>
    /// deseni): her yeni arenaya elle bir kurulum adımı eklemek, o adımı unutan sahneyi sessizce
    /// müziksiz bırakırdı. Yeni arenanın ortam sesi zaten sahneyle birlikte üretilen
    /// <see cref="MapDefinition"/> asset'ine bir klip sürüklemekle biter.
    /// </para>
    ///
    /// <para><b>Ortak faz — tüm başlıklarda müzik aynı yerdedir.</b> Sunucu her sahne
    /// bildiriminde (<c>welcome.match</c> · <c>load_match</c> · <c>return_to_lobby</c>) sahnenin
    /// <b>kaç saniyedir sahnelendiğini</b> yollar (<c>sceneElapsed</c>, Docs/ArenaNet-Protokol.md
    /// §5.3). İstemci bunu yerel bir zaman çıpasına çevirir ve klibi
    /// <c>(geçen süre) mod (klip uzunluğu)</c> noktasından açar. Sonuçları:
    /// <list type="bullet">
    /// <item>Herkes aynı anda aynı yeri duyar — geç katılan başlık da <b>atlayarak</b> katılır.</item>
    /// <item>Sahne klipten uzun süredir açıksa müzik kendiliğinden baştan sarmış olur; ayrı bir
    /// hesap gerekmez.</item>
    /// <item>Sunucu yoksa (editör sandbox'ı) çıpa da yoktur, klip 0'dan başlar.</item>
    /// </list>
    /// Çıpa <b>mesajın geldiği ana</b> bağlanır, sahnenin yüklendiği ana değil: sahne yükleme
    /// süresi başlıktan başlığa değişir ve ona bağlansaydı yavaş yüklenen başlık geride kalırdı.
    /// Kalan saat kayması (ses kartı ↔ sistem saati) <see cref="DriftCheckSeconds"/> aralıklarla
    /// denetlenir ve yalnız <see cref="MaxDriftSeconds"/>'i aşarsa düzeltilir — her denetimde
    /// düzeltmek duyulabilir bir sıçrama üretirdi.</para>
    ///
    /// <para>
    /// Sahne geçişi çapraz geçiştir (crossfade). İki sahne <b>aynı</b> klibi paylaşıyorsa
    /// (ör. iki mekanın lobisi) ses baştan başlamaz, kesintisiz çalmaya devam eder.
    /// </para>
    /// <para>
    /// Ses <b>2D</b>'dir (<c>spatialBlend = 0</c>): ortam sesinin arenada bir yeri yoktur,
    /// spatializer'a verilirse oyuncu kafasını çevirdikçe kaynak "dönüyormuş" gibi duyulur.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class SceneAmbience : MonoBehaviour
    {
        /// <summary>Sahne geçişindeki çapraz geçiş süresi (sn).</summary>
        private const float CrossfadeSeconds = 1.5f;

        /// <summary>Ortam sesi sürekli çalar ve asla kısılmamalıdır → varsayılandan (128) güçlü
        /// öncelik. Yine de 0 değil: atış sesleri ondan önce gelir.</summary>
        private const int SourcePriority = 64;

        /// <summary>Ortak fazdan sapma denetimi aralığı (sn).</summary>
        private const float DriftCheckSeconds = 15f;

        /// <summary>Bu kadarını aşan sapma düzeltilir. Altındaki fark kimsenin duymayacağı
        /// kadar küçüktür; her denetimde düzeltmek ise duyulabilir sıçrama üretir.</summary>
        private const float MaxDriftSeconds = 0.35f;

        public static SceneAmbience Instance { get; private set; }

        private AudioSource _active;
        private AudioSource _fading;
        private float _clipVolume;
        private float _masterVolume = 1f;

        private GameCatalog _catalog;
        private bool _catalogLoaded;

        /// <summary>Çıpanın ait olduğu sahne — başka bir sahne yüklenirse çıpa geçersizdir.</summary>
        private string _epochScene = "";

        /// <summary>Sahnenin sahnelendiği anın YEREL karşılığı: <c>realtime - sceneElapsed</c>.</summary>
        private float _epochRealtime;
        private bool _hasEpoch;
        private float _nextDriftCheck;

        /// <summary>
        /// Tüm ortam sesinin ortak çarpanı (0..1) — kısma/susturma kapısı. Klip seçimini ve
        /// ortak fazı değiştirmez; 0'a çekilse bile klip çalmaya devam eder, geri açıldığında
        /// diğer başlıklarla aynı yerden duyulur.
        /// </summary>
        public float MasterVolume
        {
            get => _masterVolume;
            set => _masterVolume = Mathf.Clamp01(value);
        }

        /// <summary>Şu an çalan klip; sessizse null.</summary>
        public AudioClip CurrentClip => _active != null ? _active.clip : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var go = new GameObject("[SceneAmbience]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<SceneAmbience>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            _active = CreateSource();
            _fading = CreateSource();

            // Kalıcı tekiliz: obje devre dışı bırakılsa bile olay kaçmasın diye Awake/OnDestroy'da
            // abone oluruz (PlayerCombatState deseni).
            SceneManager.sceneLoaded += HandleSceneLoaded;
            NetEvents.OnConnected += HandleConnected;
            NetEvents.OnLoadMatch += HandleLoadMatch;
            NetEvents.OnReturnToLobby += HandleReturnToLobby;
            AudioSettings.OnAudioConfigurationChanged += HandleAudioConfigurationChanged;

            ApplyScene(SceneManager.GetActiveScene().name);
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            SceneManager.sceneLoaded -= HandleSceneLoaded;
            NetEvents.OnConnected -= HandleConnected;
            NetEvents.OnLoadMatch -= HandleLoadMatch;
            NetEvents.OnReturnToLobby -= HandleReturnToLobby;
            AudioSettings.OnAudioConfigurationChanged -= HandleAudioConfigurationChanged;

            Instance = null;
        }

        /// <summary>
        /// Ses cihazı ya da yapılandırması değişti (hoparlör takıldı/çıkarıldı, operatör admin
        /// panelinden başka bir çıkış seçti). Unity bu anda ses motorunu yeniden kurar ve
        /// <b>çalan tüm <c>AudioSource</c>'ları durdurur.</b>
        /// <para>
        /// ⚠️ Ortam sesi kendiliğinden geri gelmez ve <b>bu sessizlik fark edilmez</b>: sapma
        /// denetimi "çalmıyor" diye erken çıkar (<see cref="CorrectDrift"/>), yeni klip ise ancak
        /// harita değişince seçilir — yani ses, maç boyunca susmuş kalırdı. Klip yerinde
        /// olduğundan burada elle sürdürülür ve ortak faza geri oturtulur (geç katılan başlık
        /// gibi: atlayarak devam eder, baştan başlamaz).
        /// </para>
        /// </summary>
        private void HandleAudioConfigurationChanged(bool deviceWasChanged)
        {
            if (_active == null || _active.clip == null)
            {
                return;
            }

            if (!_active.isPlaying)
            {
                _active.Play();
            }

            SeekToEpoch(_active);
            _nextDriftCheck = Time.realtimeSinceStartup + DriftCheckSeconds;
        }

        private void Update()
        {
            // ⚠️ unscaledDeltaTime: timeScale ile oynanırsa (ölüm ekranı, duraklatma) geçiş donmasın.
            float step = Time.unscaledDeltaTime / CrossfadeSeconds;
            float target = _active != null && _active.clip != null ? _clipVolume * _masterVolume : 0f;

            if (_active != null)
            {
                _active.volume = Mathf.MoveTowards(_active.volume, target, step);
            }

            if (_fading != null)
            {
                _fading.volume = Mathf.MoveTowards(_fading.volume, 0f, step);
                if (_fading.volume <= 0f && _fading.isPlaying)
                {
                    _fading.Stop();
                    _fading.clip = null;
                }
            }

            CorrectDrift();
        }

        /// <summary>
        /// Ortam sesini elle değiştirir (klip null = sessizlik). Normalde çağırmaya gerek yoktur:
        /// sahne yüklendiğinde harita tanımından kendisi seçer.
        /// </summary>
        public void Play(AudioClip clip, float volume)
        {
            _clipVolume = Mathf.Clamp01(volume);

            if (_active != null && _active.clip == clip)
            {
                // ⚠️ Aynı klip → baştan başlatma ve fazına DOKUNMA. Harita değişmediği sürece
                // müzik kesilmez; maç başlangıcı/bitişi buradan geçer.
                if (clip != null && !_active.isPlaying)
                {
                    _active.Play();
                    SeekToEpoch(_active);
                }

                return;
            }

            // Rolleri takasla: yeni klip boş kaynakta sıfırdan yükselir, eskisi Update'te söner.
            AudioSource previous = _active;
            _active = _fading;
            _fading = previous;

            _active.volume = 0f;
            _active.clip = clip;

            if (clip != null)
            {
                _active.Play();
                SeekToEpoch(_active);
                _nextDriftCheck = Time.realtimeSinceStartup + DriftCheckSeconds;
            }
            else
            {
                _active.Stop();
            }
        }

        private AudioSource CreateSource()
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.spatialize = false;
            source.volume = 0f;
            source.priority = SourcePriority;
            return source;
        }

        // ------------------------------------------------------------------ ortak faz

        private void HandleConnected(WelcomeMsg msg)
        {
            if (msg?.match != null)
            {
                SetEpoch(msg.match.sceneName, msg.match.sceneElapsed);
            }
        }

        private void HandleLoadMatch(LoadMatchMsg msg)
        {
            if (msg != null)
            {
                SetEpoch(msg.sceneName, msg.sceneElapsed);
            }
        }

        private void HandleReturnToLobby(ReturnToLobbyMsg msg)
        {
            if (msg != null)
            {
                SetEpoch(msg.sceneName, msg.sceneElapsed);
            }
        }

        /// <summary>
        /// Sunucunun bildirdiği "bu sahne şu kadar saniyedir açık" değerini yerel bir zaman
        /// çıpasına çevirir. Çıpa mesajın GELDİĞİ ana bağlanır; sahne yükleme süresi başlıktan
        /// başlığa değiştiği için sahne yüklendiği ana bağlanamaz.
        /// </summary>
        private void SetEpoch(string sceneName, float elapsedSeconds)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                return;
            }

            _epochScene = sceneName;
            _epochRealtime = Time.realtimeSinceStartup - Mathf.Max(0f, elapsedSeconds);
            _hasEpoch = true;

            // Bildirilen sahne zaten açıksa (aynı haritada yeni maç, maç sonu lobiye dönüş) klip
            // değişmez — yalnız referans tazelenir ve bir sonraki denetimde hizalama doğrulanır.
            if (string.Equals(SceneManager.GetActiveScene().name, sceneName, StringComparison.Ordinal))
            {
                _nextDriftCheck = 0f;
            }
        }

        /// <summary>
        /// Klibin ortak fazdaki yeri: sahnenin açık olduğu süre, klip uzunluğuna göre modlanır.
        /// Çıpa yoksa (sunucusuz oturum) 0 döner.
        /// </summary>
        private float EpochOffset(AudioClip clip)
        {
            if (!_hasEpoch || clip == null)
            {
                return 0f;
            }

            float length = clip.length;
            if (length <= 0f)
            {
                return 0f;
            }

            float elapsed = Time.realtimeSinceStartup - _epochRealtime;
            if (elapsed <= 0f)
            {
                return 0f;
            }

            // ⚠️ Sona çok yakın bir değer Play() ile birlikte anında başa sarar → küçük pay bırak.
            return Mathf.Clamp(Mathf.Repeat(elapsed, length), 0f, Mathf.Max(0f, length - 0.05f));
        }

        private void SeekToEpoch(AudioSource source)
        {
            if (source != null && source.clip != null)
            {
                source.time = EpochOffset(source.clip);
            }
        }

        /// <summary>
        /// Ses saati ile ortak faz arasındaki kaymayı seyrek denetler ve yalnız eşiği aşarsa
        /// düzeltir. Aynı odadaki iki başlık açık hoparlörle çaldığı için birkaç yüz ms'lik kayma
        /// yankı gibi duyulur; küçük farklar ise duyulmaz.
        /// </summary>
        private void CorrectDrift()
        {
            if (!_hasEpoch || _active == null || _active.clip == null || !_active.isPlaying ||
                Time.realtimeSinceStartup < _nextDriftCheck)
            {
                return;
            }

            _nextDriftCheck = Time.realtimeSinceStartup + DriftCheckSeconds;

            float length = _active.clip.length;
            if (length <= 0f)
            {
                return;
            }

            float expected = EpochOffset(_active.clip);
            float diff = Mathf.Abs(expected - _active.time);

            // Halka mesafesi: klibin sonu ile başı komşudur.
            if (diff > length * 0.5f)
            {
                diff = length - diff;
            }

            if (diff > MaxDriftSeconds)
            {
                _active.time = expected;
            }
        }

        // ------------------------------------------------------------------ sahne

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Katkı (additive) sahnesi haritayı değiştirmez → ortam sesine de dokunmaz.
            if (mode == LoadSceneMode.Additive)
            {
                return;
            }

            ApplyScene(scene.name);
        }

        private void ApplyScene(string sceneName)
        {
            // Çıpa başka bir sahneye aitse bu sahne için ortak faz bilinmiyor demektir
            // (sunucusuz oturum ya da henüz bildirim gelmemiş) → klip 0'dan başlar.
            if (!string.Equals(_epochScene, sceneName, StringComparison.Ordinal))
            {
                _hasEpoch = false;
            }

            MapDefinition map = FindMap(sceneName);
            Play(map != null ? map.AmbienceClip : null, map != null ? map.AmbienceVolume : 0f);
        }

        /// <summary>
        /// Sahnenin harita tanımını katalogdan çözer. Katalog <c>Resources</c>'tan bir kez
        /// yüklenir; Boot gibi harita tanımı olmayan sahnelerde null döner (sessizlik).
        /// </summary>
        private MapDefinition FindMap(string sceneName)
        {
            if (!_catalogLoaded)
            {
                _catalogLoaded = true;
                _catalog = Resources.Load<GameCatalog>("GameCatalog");

                if (_catalog == null)
                {
                    Debug.LogWarning(
                        "[SceneAmbience] GameCatalog bulunamadı (Resources/GameCatalog) — ortam sesi çalmayacak.");
                }
            }

            return _catalog != null ? _catalog.FindMap(sceneName) : null;
        }
    }
}
