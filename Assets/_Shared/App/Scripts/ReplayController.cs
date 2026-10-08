using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using VortexArena.Net;

namespace VortexArena.App
{
    /// <summary>
    /// Playback session driver (§12.4): reads the <c>.vxr</c> file, feeds it to
    /// <see cref="ReplayPlayer"/> every frame and carries the keyboard transport + a one-line
    /// status strip. <b>Installed only when <see cref="AppSession.IsReplay"/></b> — the decision
    /// belongs to <see cref="AppSingletons"/>.
    /// <para><b>The clock is engine time:</b> pause = <c>Time.timeScale</c> 0, speed =
    /// <c>Time.timeScale</c>. FX, physics and animators then run on the same clock as the
    /// recording, which is also what the phase-3 video export needs
    /// (<c>Time.captureDeltaTime</c>).</para>
    /// <para>File I/O lives HERE and not in the player: the player's source is a <c>byte[]</c> so a
    /// web viewer can hand it a download (plan §7).</para>
    /// <para>Keys: Space duraklat · ←/→ 10 sn · Shift+←/→ 60 sn · ↑/↓ hız · Home başa.
    /// ⚠️ Must not collide with <c>AdminSpectator</c> (1/2/3, Tab, F, P, I, Esc, F11) or the
    /// spectator camera (WASD/QE, mouse).</para>
    /// </summary>
    public class ReplayController : MonoBehaviour
    {
        private const int SeekStepMs = 10000;
        private const int SeekBigStepMs = 60000;

        /// <summary>Above the admin HUD (4000), below the connection card (5000).</summary>
        private const int CanvasSortingOrder = 4500;

        private static ReplayController _instance;

        private readonly ReplayPlayer _player = new ReplayPlayer();

        private TextMeshProUGUI _statusText;
        private TextMeshProUGUI _errorText;

        private bool _paused;

        /// <summary>Last drawn line — TMP is only touched when the text really changes.</summary>
        private string _shownStatus = "";

        internal static void Install()
        {
            if (_instance != null)
            {
                return;
            }

            var go = new GameObject("[ReplayController]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ReplayController>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            BuildOverlay();
        }

        private void Start()
        {
            // The scene load the first `load_match` starts parks the clock: without it the
            // recording would keep streaming into a scene that is not up yet.
            _player.HoldClock = () => SceneRouter.Instance != null && SceneRouter.Instance.IsLoading;

            LoadFile();
        }

        private void OnDestroy()
        {
            if (_instance != this)
            {
                return;
            }

            _instance = null;
            _player.Unload();
            Time.timeScale = 1f;
        }

        // --------------------------------------------------------------------- load

        private void LoadFile()
        {
            string path = AppSession.ReplayPath;
            byte[] bytes = null;

            try
            {
                if (File.Exists(path))
                {
                    bytes = File.ReadAllBytes(path);
                }
            }
            catch (Exception e)
            {
                ShowError($"Kayıt dosyası okunamadı:\n{path}\n{e.Message}");
                return;
            }

            if (bytes == null)
            {
                ShowError($"Kayıt dosyası bulunamadı:\n{path}");
                return;
            }

            if (!_player.Load(bytes))
            {
                ShowError(_player.Error);
                return;
            }

            ApplyTimeScale();
            Debug.Log($"[ReplayController] Kayıt yüklendi: '{path}' · {FormatTime(_player.DurationMs)}.");
        }

        // -------------------------------------------------------------------- frame

        private void Update()
        {
            if (!_player.IsLoaded)
            {
                return;
            }

            ReadKeys();

            // Scaled delta on purpose: pause and speed come from timeScale, so one knob drives the
            // clock and the scene together.
            _player.Tick(Time.deltaTime);
            RefreshStatus();
        }

        private void ReadKeys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                _paused = !_paused;
                ApplyTimeScale();
            }

            bool shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            int step = shift ? SeekBigStepMs : SeekStepMs;

            if (keyboard.rightArrowKey.wasPressedThisFrame)
            {
                _player.SeekBy(step);
            }
            else if (keyboard.leftArrowKey.wasPressedThisFrame)
            {
                _player.SeekBy(-step);
            }

            if (keyboard.upArrowKey.wasPressedThisFrame)
            {
                _player.Speed *= 2f;
                ApplyTimeScale();
            }
            else if (keyboard.downArrowKey.wasPressedThisFrame)
            {
                _player.Speed *= 0.5f;
                ApplyTimeScale();
            }

            if (keyboard.homeKey.wasPressedThisFrame)
            {
                _player.Restart();
            }
        }

        private void ApplyTimeScale()
        {
            Time.timeScale = _paused ? 0f : _player.Speed;
        }

        // --------------------------------------------------------------------- UI

        /// <summary>Procedural canvas (<see cref="UiKit"/>): the real transport UI is phase 2.</summary>
        private void BuildOverlay()
        {
            var root = new GameObject("[ReplayOverlay]");
            root.transform.SetParent(transform, false);
            Canvas canvas = UiKit.ScreenCanvas(root, CanvasSortingOrder);

            _statusText = UiKit.Text(canvas.transform, "Status", 26f, UiKit.Title, FontStyles.Bold,
                TextAlignmentOptions.Center);
            UiKit.Corner(_statusText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 28f),
                new Vector2(900f, 40f));

            _errorText = UiKit.Text(canvas.transform, "Error", 34f, UiKit.Bad, FontStyles.Bold,
                TextAlignmentOptions.Center);
            UiKit.Center(_errorText.rectTransform, new Vector2(1200f, 300f));
            _errorText.gameObject.SetActive(false);
        }

        private void ShowError(string message)
        {
            if (_errorText == null)
            {
                return;
            }

            _errorText.text = string.IsNullOrEmpty(message) ? "Kayıt açılamadı." : message;
            _errorText.gameObject.SetActive(true);

            if (_statusText != null)
            {
                _statusText.gameObject.SetActive(false);
            }
        }

        private void RefreshStatus()
        {
            if (_statusText == null)
            {
                return;
            }

            string state = _player.IsEnded ? "BİTTİ" : _paused ? "DURAKLADI" : "OYNUYOR";
            string line = $"{state}   {FormatTime(_player.CurrentMs)} / {FormatTime(_player.DurationMs)}   " +
                          $"{FormatSpeed(_player.Speed)}x";

            if (line == _shownStatus)
            {
                return;
            }

            _shownStatus = line;
            _statusText.text = line;
        }

        private static string FormatTime(int milliseconds)
        {
            int totalSeconds = Mathf.Max(0, milliseconds) / 1000;
            return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
        }

        private static string FormatSpeed(float speed)
        {
            return speed < 1f ? speed.ToString("0.##") : speed.ToString("0.#");
        }
    }
}
