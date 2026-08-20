using System;
using UnityEngine;
using VortexArena.Core.Audio;

namespace VortexArena.App.Admin
{
    /// <summary>Admin spectator camera mode.</summary>
    public enum AdminCameraMode
    {
        /// <summary>From the selected player's head (center eye) pose.</summary>
        Pov = 0,

        /// <summary>Free flight with WASD + QE + right-button mouse look.</summary>
        Free = 1,

        /// <summary>Orthographic top-down view of the arena (rings + nameplates).</summary>
        TopDown = 2
    }

    /// <summary>In which modes player rings are drawn.</summary>
    public enum AdminMarkerVisibility
    {
        Off = 0,

        /// <summary>Top-down only (default).</summary>
        TopDownOnly = 1,

        /// <summary>In every mode except POV.</summary>
        Always = 2
    }

    /// <summary>
    /// When the arena roof (<c>VortexArena.Core.Arena.ArenaRoof</c>) hides for the spectator. A
    /// hidden roof still casts its shadow — see the component doc.
    /// </summary>
    public enum AdminRoofMode
    {
        /// <summary>Always visible (what the player sees).</summary>
        Visible = 0,

        /// <summary>Hidden in top-down only (default), so the interior is visible from above.</summary>
        HideInTopDown = 1,

        /// <summary>Hidden in every mode; the ceiling does not block POV/free either.</summary>
        Hidden = 2
    }

    /// <summary>Open full-screen panel (at most one at a time).</summary>
    public enum AdminPanelKind
    {
        None = 0,
        Preferences = 1,
        Stats = 2
    }

    /// <summary>
    /// Selections and view preferences of the admin spectator session — the single source of truth.
    /// HUD, camera and markers all read from here and stay in sync through <see cref="Changed"/>;
    /// none writes into another's state.
    /// <para>View preferences persist in <c>PlayerPrefs</c>: they belong to the admin PC and do not
    /// pollute the repo (same reasoning as <c>EditorPrefs</c> in the dev window). Selected player
    /// and camera mode are NOT persisted — they are per session.</para>
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

        // ⚠️ Key suffix is the channel NAME, not its numeric index: appending/reordering
        // AudioChannel would otherwise move a stored preference onto another channel.
        private const string KeyAudioLevel = Prefix + "AudioLevel.";
        private const string KeyAudioMuted = Prefix + "AudioMuted.";

        /// <summary>Free mode base speed bounds (m/s) — the preference slider spans this range.</summary>
        public const float FreeSpeedMin = 1f;
        public const float FreeSpeedMax = 12f;

        /// <summary>Level stepper increment — 10 steps span the full range.</summary>
        public const float AudioLevelStep = 0.1f;

        /// <summary>Raised on any selection/preference change (main thread).</summary>
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
        private static readonly float[] _audioLevels = new float[AudioMix.ChannelCount];
        private static readonly bool[] _audioMuted = new bool[AudioMix.ChannelCount];
        private static bool _loaded;

        /// <summary>
        /// Starts in top-down: the operator first wants "who is where", and POV needs a selected
        /// player, which does not exist on the first frame.
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

        /// <summary>Selected player's id; 0 = no selection.</summary>
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
        /// Play a warning sound when a physical violation starts (§10.9). <b>Default on:</b> the
        /// operator may not be looking at the screen.
        /// <para>⚠️ A PER-SCREEN preference — it does NOT go into <c>AdminSelection</c> (i.e.
        /// <c>admin_state</c>): tying two operators' speakers together helps nobody; muting one
        /// must leave the other audible.</para>
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

        /// <summary>Free mode base speed (m/s); Shift ×3, the wheel changes it.</summary>
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

        /// <summary>When the arena roof hides for the spectator (default: in top-down).</summary>
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
        /// Is the admin window full screen (otherwise windowed).
        /// <para>⚠️ <b>The only gate:</b> writers (preferences panel, F11) never touch
        /// <c>Screen</c> — the setter stores the preference, applies it and raises
        /// <see cref="Changed"/>. A second writer to <c>Screen.fullScreenMode</c> would silently
        /// desync preference and window, and the panel would show the wrong value.</para>
        /// <para>⚠️ Windowed is <c>Windowed</c>, full screen is <c>FullScreenWindow</c>
        /// (borderless): <c>ExclusiveFullScreen</c> changes resolution and loses the admin for
        /// seconds on alt-tab, and the operator keeps switching to the launcher/server window on
        /// the same PC.</para>
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

        /// <summary>Full screen ↔ windowed (F11 and the preferences panel share this gate).</summary>
        public static void ToggleScreenMode()
        {
            FullScreen = !FullScreen;
        }

        /// <summary>
        /// Applies the stored preference to the window. The setter already calls it; also called
        /// once <b>when admin activates</b> so the app opens with the operator's last choice rather
        /// than the build's startup mode.
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
        /// Endpoint id of the Windows output device; <c>""</c> = <b>system default</b> (nothing is
        /// touched). Lets the operator pick between several speakers/headsets on the admin PC.
        /// <para>⚠️ <b>A PER-SCREEN preference</b> (<c>PlayerPrefs</c>), same reasoning as the
        /// violation sound — it does NOT go into <c>AdminSelection</c> / <c>admin_state</c>.</para>
        /// <para>⚠️ The stored value is the <b>id, not the name</b>: names change with driver
        /// updates, endpoint ids do not. The name is only displayed and re-read every startup.</para>
        /// <para>⚠️ <b>The only gate</b> (same contract as <see cref="FullScreen"/>): writers never
        /// touch Windows — the setter stores, applies and raises <see cref="Changed"/>.</para>
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
        /// Makes the stored device the Windows default output. The setter already calls it; also
        /// called once <b>when admin activates</b> (same pattern as <see cref="ApplyScreenMode"/>).
        /// <para>⚠️ <b>Unity has NO API to pick an audio output device</b> — the engine always uses
        /// the <b>Windows default</b>, so the choice is applied by making that device the default
        /// (<see cref="WindowsAudioDevices"/>). Consequence: <b>the change is system wide</b> and
        /// other apps on the admin PC follow. On a dedicated operator machine that is intended.</para>
        /// <para>⚠️ On a real change <c>AudioSettings.Reset</c> rebuilds the engine and <b>stops
        /// every playing <c>AudioSource</c></b> (Unity's own device watcher does the same); ambience
        /// restarts itself — see <c>VortexArena.Core.Audio.SceneAmbience</c>. Selecting the device
        /// already in effect does nothing: a pointless reset would cut audio for no reason.</para>
        /// <para>A device that is no longer connected does NOT clear the preference — the choice
        /// should survive replugging the headset; only a warning is logged.</para>
        /// </summary>
        public static void ApplyAudioOutput()
        {
            Load();

            if (string.IsNullOrEmpty(_audioDevice) || !WindowsAudioDevices.Supported)
            {
                return; // system default is in effect
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

            // Re-seat the engine on the new default. The configuration is handed back UNCHANGED:
            // the goal is re-picking the device, not changing settings.
            AudioSettings.Reset(AudioSettings.GetConfiguration());
        }

        // ------------------------------------------------------------------ audio mix

        // ⚠️ A PER-SCREEN preference: the mix does NOT go into AdminSelection (i.e. admin_state) and
        // never reaches the players — same reasoning as the violation sound and the output device:
        // what one operator turns down must stay audible for the other and must never touch a
        // player's headset.
        // ⚠️ ONE writer only: AdminSession → AudioMix. A second writer would silently split the
        // stored preference from what is heard.

        /// <summary>Stored level of the channel (0..1) — unchanged by muting.</summary>
        public static float AudioLevel(AudioChannel channel)
        {
            Load();
            int i = (int)channel;
            return InRange(i) ? _audioLevels[i] : 1f;
        }

        /// <summary>
        /// Is the channel muted. ⚠️ Muting does NOT change the level: it is an independent flag, so
        /// unmuting restores the old level exactly and no "previous level" needs storing.
        /// </summary>
        public static bool AudioMuted(AudioChannel channel)
        {
            Load();
            int i = (int)channel;
            return InRange(i) && _audioMuted[i];
        }

        /// <summary>What actually reaches the engine: 0 while muted, the stored level otherwise.</summary>
        public static float EffectiveAudioLevel(AudioChannel channel)
        {
            return AudioMuted(channel) ? 0f : AudioLevel(channel);
        }

        public static void SetAudioLevel(AudioChannel channel, float level)
        {
            Load();
            int i = (int)channel;
            if (!InRange(i))
            {
                return;
            }

            float clamped = Mathf.Clamp01(level);
            if (Mathf.Approximately(_audioLevels[i], clamped))
            {
                return;
            }

            _audioLevels[i] = clamped;
            PlayerPrefs.SetFloat(KeyAudioLevel + channel, clamped);
            ApplyAudioMix();
            Raise();
        }

        /// <summary>
        /// Moves the level one step (<paramref name="direction"/> = ±1). ⚠️ Stepping while muted
        /// does NOT unmute — the stored level moves and the output stays silent; the panel shows
        /// "sessiz (%70)" so the operator sees it.
        /// </summary>
        public static void StepAudioLevel(AudioChannel channel, int direction)
        {
            Load();
            int i = (int)channel;
            if (!InRange(i) || direction == 0)
            {
                return;
            }

            // Snapped to the step grid so float drift never turns 70% into 69.9999%.
            float snapped = Mathf.Round(_audioLevels[i] / AudioLevelStep) * AudioLevelStep;
            SetAudioLevel(channel, snapped + direction * AudioLevelStep);
        }

        public static void SetAudioMuted(AudioChannel channel, bool muted)
        {
            Load();
            int i = (int)channel;
            if (!InRange(i) || _audioMuted[i] == muted)
            {
                return;
            }

            _audioMuted[i] = muted;
            PlayerPrefs.SetInt(KeyAudioMuted + channel, muted ? 1 : 0);
            ApplyAudioMix();
            Raise();
        }

        public static void ToggleAudioMute(AudioChannel channel)
        {
            SetAudioMuted(channel, !AudioMuted(channel));
        }

        /// <summary>
        /// Writes the stored mix into the engine. The setters already call it; also called once
        /// <b>when admin activates</b> (same pattern as <see cref="ApplyScreenMode"/>).
        /// </summary>
        public static void ApplyAudioMix()
        {
            Load();
            for (int i = 0; i < AudioMix.ChannelCount; i++)
            {
                var channel = (AudioChannel)i;
                AudioMix.Set(channel, EffectiveAudioLevel(channel));
            }
        }

        private static bool InRange(int channelIndex)
        {
            return channelIndex >= 0 && channelIndex < AudioMix.ChannelCount;
        }

        /// <summary>
        /// Alpha for the roof (1 = normal, 0 = not drawn, shadow stays), derived from the
        /// preference and the current camera mode; consumed by <c>ArenaRoof.ApplyAll</c>.
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

        /// <summary>Should rings/nameplates be drawn in the current mode?</summary>
        public static bool MarkersVisibleNow()
        {
            switch (Markers)
            {
                case AdminMarkerVisibility.Off:
                    return false;
                case AdminMarkerVisibility.TopDownOnly:
                    return CameraMode == AdminCameraMode.TopDown;
                default:
                    // A ring around your own head in POV is meaningless.
                    return CameraMode != AdminCameraMode.Pov;
            }
        }

        /// <summary>Closes the panels (Esc).</summary>
        public static void ClosePanel()
        {
            OpenPanel = AdminPanelKind.None;
        }

        /// <summary>Requesting the open panel again closes it (button behaviour).</summary>
        public static void TogglePanel(AdminPanelKind panel)
        {
            OpenPanel = _openPanel == panel ? AdminPanelKind.None : panel;
        }

        /// <summary>Resets session state (tests / role switch only).</summary>
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

            // Default EMPTY: on an installation that never chose, Windows' default is left alone
            // (same reasoning as the full screen preference — stay silent until first touch).
            _audioDevice = PlayerPrefs.GetString(KeyAudioDevice, "");

            // ⚠️ Default is the window's CURRENT state, not a constant: with no stored choice the
            // preference must not override the build's startup mode (Player Settings).
            _fullScreen = PlayerPrefs.GetInt(KeyFullScreen, Screen.fullScreen ? 1 : 0) != 0;

            // Default: full level, not muted.
            for (int i = 0; i < AudioMix.ChannelCount; i++)
            {
                var channel = (AudioChannel)i;
                _audioLevels[i] = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyAudioLevel + channel, 1f));
                _audioMuted[i] = PlayerPrefs.GetInt(KeyAudioMuted + channel, 0) != 0;
            }
        }

        private static void Raise()
        {
            Changed?.Invoke();
        }
    }
}
