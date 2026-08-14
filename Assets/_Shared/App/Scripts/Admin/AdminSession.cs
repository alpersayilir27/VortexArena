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
        }

        private static void Raise()
        {
            Changed?.Invoke();
        }
    }
}
