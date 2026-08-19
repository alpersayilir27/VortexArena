using System;
using UnityEngine;

namespace VortexArena.App.Admin
{
    /// <summary>Admin gözlemci kamera kipi.</summary>
    public enum AdminCameraMode
    {
        /// <summary>Seçili oyuncunun baş (center eye) pozundan.</summary>
        Pov = 0,

        /// <summary>WASD + QE + sağ tuş fare ile serbest uçuş.</summary>
        Free = 1,

        /// <summary>Arenayı tepeden gören ortografik görünüm (halka + ad etiketi).</summary>
        TopDown = 2
    }

    /// <summary>Oyuncu halkalarının hangi kiplerde çizileceği.</summary>
    public enum AdminMarkerVisibility
    {
        Off = 0,

        /// <summary>Yalnız kuş bakışında (varsayılan).</summary>
        TopDownOnly = 1,

        /// <summary>POV dışındaki tüm kiplerde.</summary>
        Always = 2
    }

    /// <summary>
    /// Arena çatısının (<c>VortexArena.Core.Arena.ArenaRoof</c>) gözlemcide ne zaman gizleneceği.
    /// Çatı gizlense de gölgesini atmaya devam eder — bkz. bileşen dokümanı.
    /// </summary>
    public enum AdminRoofMode
    {
        /// <summary>Çatı hep görünür (oyuncunun gördüğü hâli).</summary>
        Visible = 0,

        /// <summary>Yalnız kuş bakışında gizlenir (varsayılan) — tepeden içerisi görülsün.</summary>
        HideInTopDown = 1,

        /// <summary>Her kipte gizli; POV/serbest kipte de tavan kapatmaz.</summary>
        Hidden = 2
    }

    /// <summary>Açık olan tam ekran panel (aynı anda en fazla biri).</summary>
    public enum AdminPanelKind
    {
        None = 0,
        Preferences = 1,
        Stats = 2
    }

    /// <summary>
    /// Admin gözlemci oturumunun seçimleri ve görünüm tercihleri — tek doğruluk noktası.
    /// HUD, kamera ve işaretçiler <b>hepsi buradan okur</b> ve <see cref="Changed"/> ile
    /// senkron kalır; hiçbiri diğerinin alanına yazmaz.
    /// <para>
    /// Görünüm tercihleri <c>PlayerPrefs</c>'te kalıcıdır: admin PC'sine özeldir, repoyu
    /// kirletmez (dev penceresindeki <c>EditorPrefs</c> kararıyla aynı gerekçe). Seçili oyuncu
    /// ve kamera kipi kalıcı DEĞİLDİR — oturum başına anlamlıdır.
    /// </para>
    /// </summary>
    public static class AdminSession
    {
        private const string Prefix = "VortexArena.Admin.";
        private const string KeyMarkers = Prefix + "Markers";
        private const string KeyNameplates = Prefix + "Nameplates";
        private const string KeyViolationSound = Prefix + "ViolationSound";
        private const string KeyFreeSpeed = Prefix + "FreeSpeed";
        private const string KeyRoof = Prefix + "Roof";
        private const string KeyFullScreen = Prefix + "FullScreen";
        private const string KeyAudioDevice = Prefix + "AudioDevice";

        /// <summary>Serbest kip taban hızı sınırları (m/sn) — tercih slider'ı bu aralıkta.</summary>
        public const float FreeSpeedMin = 1f;
        public const float FreeSpeedMax = 12f;

        /// <summary>Herhangi bir seçim/tercih değiştiğinde (ana thread).</summary>
        public static event Action Changed;

        private static AdminCameraMode _cameraMode = AdminCameraMode.TopDown;
        private static int _selectedPlayerId;
        private static AdminPanelKind _openPanel = AdminPanelKind.None;

        private static AdminMarkerVisibility _markers = AdminMarkerVisibility.TopDownOnly;
        private static bool _nameplates = true;
        private static bool _violationSound = true;
        private static float _freeSpeed = 4f;
        private static AdminRoofMode _roof = AdminRoofMode.HideInTopDown;
        private static bool _fullScreen = true;
        private static string _audioDevice = "";
        private static bool _loaded;

        /// <summary>
        /// Açılış kipi kuş bakışıdır: operatör önce "sahada kim nerede" görmek ister; POV bir
        /// oyuncu seçilmesini gerektirir, ilk karede seçili oyuncu henüz yoktur.
        /// </summary>
        public static AdminCameraMode CameraMode
        {
            get => _cameraMode;
            set
            {
                if (_cameraMode == value)
                {
                    return;
                }

                _cameraMode = value;
                Raise();
            }
        }

        /// <summary>Seçili oyuncunun playerId'si; 0 = seçim yok.</summary>
        public static int SelectedPlayerId
        {
            get => _selectedPlayerId;
            set
            {
                if (_selectedPlayerId == value)
                {
                    return;
                }

                _selectedPlayerId = value;
                Raise();
            }
        }

        public static AdminPanelKind OpenPanel
        {
            get => _openPanel;
            set
            {
                if (_openPanel == value)
                {
                    return;
                }

                _openPanel = value;
                Raise();
            }
        }

        public static AdminMarkerVisibility Markers
        {
            get { Load(); return _markers; }
            set
            {
                Load();
                if (_markers == value)
                {
                    return;
                }

                _markers = value;
                PlayerPrefs.SetInt(KeyMarkers, (int)value);
                Raise();
            }
        }

        public static bool Nameplates
        {
            get { Load(); return _nameplates; }
            set
            {
                Load();
                if (_nameplates == value)
                {
                    return;
                }

                _nameplates = value;
                PlayerPrefs.SetInt(KeyNameplates, value ? 1 : 0);
                Raise();
            }
        }

        /// <summary>
        /// Fiziksel ihlal başladığında uyarı sesi çalsın mı (§10.9). <b>Varsayılan açık:</b>
        /// operatör ekrana bakmıyor olabilir ve ihlalin tek işi ona ulaşmaktır.
        /// <para>⚠️ Bu bir EKRAN tercihidir, <c>AdminSelection</c>'a (yani <c>admin_state</c>'e)
        /// GİRMEZ: iki operatörün hoparlörünü birbirine bağlamak yönetimi kolaylaştırmaz —
        /// biri sesi kapatınca diğerininki çalmaya devam etmeli.</para>
        /// </summary>
        public static bool ViolationSound
        {
            get { Load(); return _violationSound; }
            set
            {
                Load();
                if (_violationSound == value)
                {
                    return;
                }

                _violationSound = value;
                PlayerPrefs.SetInt(KeyViolationSound, value ? 1 : 0);
                Raise();
            }
        }

        /// <summary>Serbest kip taban hızı (m/sn); Shift ×3, tekerlek bunu değiştirir.</summary>
        public static float FreeSpeed
        {
            get { Load(); return _freeSpeed; }
            set
            {
                Load();
                float clamped = Mathf.Clamp(value, FreeSpeedMin, FreeSpeedMax);
                if (Mathf.Approximately(_freeSpeed, clamped))
                {
                    return;
                }

                _freeSpeed = clamped;
                PlayerPrefs.SetFloat(KeyFreeSpeed, clamped);
                Raise();
            }
        }

        /// <summary>Arena çatısı gözlemcide ne zaman gizlensin (varsayılan: kuş bakışında).</summary>
        public static AdminRoofMode Roof
        {
            get { Load(); return _roof; }
            set
            {
                Load();
                if (_roof == value)
                {
                    return;
                }

                _roof = value;
                PlayerPrefs.SetInt(KeyRoof, (int)value);
                Raise();
            }
        }

        /// <summary>
        /// Admin penceresi tam ekran mı (aksi hâlde pencereli).
        /// <para>
        /// ⚠️ <b>Tek kapı budur:</b> yazan taraf (tercih paneli, F11) <c>Screen</c>'e dokunmaz —
        /// setter tercihi kaydeder, ekrana uygular ve <see cref="Changed"/> ile herkesi tazeler.
        /// İkinci bir yerden <c>Screen.fullScreenMode</c> yazılırsa tercih ile pencerenin gerçek
        /// hâli sessizce ayrışır ve panel yanlış değeri gösterir.
        /// </para>
        /// <para>
        /// ⚠️ Pencereli kip <c>Windowed</c>, tam ekran <c>FullScreenWindow</c>'dur (kenarlıksız):
        /// <c>ExclusiveFullScreen</c> çözünürlük değiştirir ve alt-tab'da admin'i saniyelerce
        /// kaybettirir — operatör aynı PC'de launcher/sunucu penceresine geçip duruyor.
        /// </para>
        /// </summary>
        public static bool FullScreen
        {
            get { Load(); return _fullScreen; }
            set
            {
                Load();
                if (_fullScreen == value)
                {
                    return;
                }

                _fullScreen = value;
                PlayerPrefs.SetInt(KeyFullScreen, value ? 1 : 0);
                ApplyScreenMode();
                Raise();
            }
        }

        /// <summary>Tam ekran ↔ pencereli (F11 ve tercih paneli aynı kapıdan geçer).</summary>
        public static void ToggleScreenMode()
        {
            FullScreen = !FullScreen;
        }

        /// <summary>
        /// Kayıtlı tercihi pencereye uygular. Setter zaten çağırır; ayrıca <b>admin etkinleşirken</b>
        /// bir kez çağrılır ki uygulama, build'in açılış kipiyle değil operatörün son seçimiyle
        /// açılsın. Editörde <c>Screen</c> yazımı yok sayılır (oyun görünümü kendi kipindedir).
        /// </summary>
        public static void ApplyScreenMode()
        {
            Load();
            FullScreenMode mode = _fullScreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            if (Screen.fullScreenMode != mode)
            {
                Screen.fullScreenMode = mode;
            }
        }

        /// <summary>
        /// Sesin çıkacağı Windows cihazının uç kimliği; <c>""</c> = <b>sistem varsayılanı</b>
        /// (hiçbir şeye dokunulmaz). Operatör admin PC'sine bağlı birden çok hoparlör/kulaklık
        /// arasından seçebilsin diye vardır.
        /// <para>
        /// ⚠️ <b>Bu bir EKRAN tercihidir</b> (<c>PlayerPrefs</c>), ihlal sesiyle aynı gerekçe:
        /// iki operatörün hoparlörünü birbirine bağlamak yönetimi kolaylaştırmaz. <c>AdminSelection</c>'a
        /// (yani <c>admin_state</c>'e) GİRMEZ.
        /// </para>
        /// <para>
        /// ⚠️ Saklanan şey <b>kimliktir, ad değil</b>: cihaz adları sürücü güncellemesiyle değişir,
        /// uç kimliği kalır. Ad yalnız panelde gösterilir ve her açılışta yeniden okunur.
        /// </para>
        /// <para>
        /// ⚠️ <b>Tek kapı budur</b> (<see cref="FullScreen"/> ile aynı sözleşme): yazan taraf
        /// Windows'a dokunmaz — setter tercihi kaydeder, cihazı uygular ve <see cref="Changed"/>
        /// ile herkesi tazeler.
        /// </para>
        /// </summary>
        public static string AudioOutputDeviceId
        {
            get { Load(); return _audioDevice; }
            set
            {
                Load();
                string id = value ?? "";
                if (_audioDevice == id)
                {
                    return;
                }

                _audioDevice = id;
                PlayerPrefs.SetString(KeyAudioDevice, id);
                ApplyAudioOutput();
                Raise();
            }
        }

        /// <summary>
        /// Kayıtlı ses cihazını Windows'un varsayılan çıkışı yapar. Setter zaten çağırır; ayrıca
        /// <b>admin etkinleşirken</b> bir kez çağrılır ki uygulama operatörün son seçimiyle açılsın
        /// (<see cref="ApplyScreenMode"/> ile aynı desen).
        /// <para>
        /// ⚠️ <b>Unity'de ses çıkış cihazı seçen bir API YOKTUR</b> — <c>AudioSettings</c> yalnız
        /// hız/tampon/hoparlör kipi verir, cihaz listelemez. Ses motoru her zaman <b>Windows'un
        /// varsayılan cihazını</b> kullanır; bu yüzden seçim, cihazın kendisini varsayılan yaparak
        /// uygulanır (<see cref="WindowsAudioDevices"/>). Doğrudan sonucu: <b>değişiklik sistem
        /// geneldir</b> — admin PC'sindeki diğer uygulamalar da o cihaza geçer. Adanmış bir
        /// operatör makinesinde istenen davranış budur.
        /// </para>
        /// <para>
        /// ⚠️ Cihaz gerçekten değiştiğinde <c>AudioSettings.Reset</c> ses motorunu yeniden kurar ve
        /// <b>çalan tüm <c>AudioSource</c>'lar durur</b> (Unity'nin kendi cihaz izleyicisi de aynı
        /// şeyi yapar). Ortam sesi bu yüzden kendini geri başlatır — bkz.
        /// <c>VortexArena.Core.Audio.SceneAmbience</c>. Zaten yürürlükte olan cihaz seçilirse
        /// hiçbir şey yapılmaz: gereksiz bir sıfırlama sesi boşuna kesintiye uğratırdı.
        /// </para>
        /// <para>Seçilen cihaz artık bağlı değilse tercih <b>SİLİNMEZ</b> — kulaklık geri
        /// takıldığında seçim yaşasın; yalnız bir uyarı düşülür.</para>
        /// </summary>
        public static void ApplyAudioOutput()
        {
            Load();

            if (string.IsNullOrEmpty(_audioDevice) || !WindowsAudioDevices.Supported)
            {
                return; // sistem varsayılanı geçerli
            }

            if (WindowsAudioDevices.GetDefaultId() == _audioDevice)
            {
                return;
            }

            if (!WindowsAudioDevices.SetDefault(_audioDevice))
            {
                Debug.LogWarning(
                    "[AdminSession] Seçili ses çıkış cihazı ayarlanamadı (bağlı değil olabilir) — " +
                    "sistem varsayılanı kullanılıyor. Tercih korundu.");
                return;
            }

            // Motor yeni varsayılana otursun. Yapılandırma DEĞİŞTİRİLMEZ, aynısı geri verilir:
            // amaç ayar değiştirmek değil, cihazı yeniden seçtirmek.
            AudioSettings.Reset(AudioSettings.GetConfiguration());
        }

        /// <summary>
        /// Çatıya uygulanacak alfa (1 = normal, 0 = çizilmez, gölge kalır). Tercih + o anki
        /// kamera kipinden türetilir; <c>ArenaRoof.ApplyAll</c> bunu alır.
        /// </summary>
        public static float RoofAlphaNow()
        {
            switch (Roof)
            {
                case AdminRoofMode.Hidden:
                    return 0f;
                case AdminRoofMode.HideInTopDown:
                    return CameraMode == AdminCameraMode.TopDown ? 0f : 1f;
                default:
                    return 1f;
            }
        }

        /// <summary>Halkalar/ad etiketleri şu anki kipte çizilmeli mi?</summary>
        public static bool MarkersVisibleNow()
        {
            switch (Markers)
            {
                case AdminMarkerVisibility.Off:
                    return false;
                case AdminMarkerVisibility.TopDownOnly:
                    return CameraMode == AdminCameraMode.TopDown;
                default:
                    // POV'da kendi kafasının etrafında halka görmek anlamsız.
                    return CameraMode != AdminCameraMode.Pov;
            }
        }

        /// <summary>Panelleri kapatır (Esc).</summary>
        public static void ClosePanel()
        {
            OpenPanel = AdminPanelKind.None;
        }

        /// <summary>Aynı paneli tekrar isteyince kapatır (düğme davranışı).</summary>
        public static void TogglePanel(AdminPanelKind panel)
        {
            OpenPanel = _openPanel == panel ? AdminPanelKind.None : panel;
        }

        /// <summary>Oturum durumunu sıfırlar (yalnız testler/rol değişimi için).</summary>
        public static void ResetSelection()
        {
            _cameraMode = AdminCameraMode.TopDown;
            _selectedPlayerId = 0;
            _openPanel = AdminPanelKind.None;
            Raise();
        }

        private static void Load()
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            _markers = (AdminMarkerVisibility)PlayerPrefs.GetInt(KeyMarkers, (int)AdminMarkerVisibility.TopDownOnly);
            _nameplates = PlayerPrefs.GetInt(KeyNameplates, 1) != 0;
            _violationSound = PlayerPrefs.GetInt(KeyViolationSound, 1) != 0;
            _freeSpeed = Mathf.Clamp(PlayerPrefs.GetFloat(KeyFreeSpeed, 4f), FreeSpeedMin, FreeSpeedMax);
            _roof = (AdminRoofMode)PlayerPrefs.GetInt(KeyRoof, (int)AdminRoofMode.HideInTopDown);

            // Varsayılan BOŞ: hiç seçim yapmamış bir kurulumda Windows'un varsayılanına
            // dokunulmaz (tam ekran tercihiyle aynı gerekçe — ilk dokunuşa kadar sessiz kal).
            _audioDevice = PlayerPrefs.GetString(KeyAudioDevice, "");

            // ⚠️ Varsayılan SABİT bir değer değil, pencerenin O ANKİ hâlidir: hiç seçim yapmamış
            // bir kurulumda tercih, build'in açılış kipini (Player Settings) ezmemeli — ilk
            // dokunuştan sonra seçim kalıcıdır.
            _fullScreen = PlayerPrefs.GetInt(KeyFullScreen, Screen.fullScreen ? 1 : 0) != 0;
        }

        private static void Raise()
        {
            Changed?.Invoke();
        }
    }
}
