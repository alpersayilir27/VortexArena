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
        private const string KeyFreeSpeed = Prefix + "FreeSpeed";
        private const string KeyWallAlpha = Prefix + "WallAlpha";
        private const string KeyMiniMap = Prefix + "MiniMap";

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
        private static float _freeSpeed = 4f;
        private static float _wallAlpha = 0.25f;
        private static bool _miniMap = true;
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

        /// <summary>Arena duvarlarının gözlemci kipindeki sabit saydamlığı (0..1).</summary>
        public static float WallAlpha
        {
            get { Load(); return _wallAlpha; }
            set
            {
                Load();
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(_wallAlpha, clamped))
                {
                    return;
                }

                _wallAlpha = clamped;
                PlayerPrefs.SetFloat(KeyWallAlpha, clamped);
                Raise();
            }
        }

        /// <summary>Sağ altta küçük taktik harita (POV/serbest kipte konum farkındalığı).</summary>
        public static bool MiniMap
        {
            get { Load(); return _miniMap; }
            set
            {
                Load();
                if (_miniMap == value)
                {
                    return;
                }

                _miniMap = value;
                PlayerPrefs.SetInt(KeyMiniMap, value ? 1 : 0);
                Raise();
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
            _freeSpeed = Mathf.Clamp(PlayerPrefs.GetFloat(KeyFreeSpeed, 4f), FreeSpeedMin, FreeSpeedMax);
            _wallAlpha = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyWallAlpha, 0.25f));
            _miniMap = PlayerPrefs.GetInt(KeyMiniMap, 1) != 0;
        }

        private static void Raise()
        {
            Changed?.Invoke();
        }
    }
}
