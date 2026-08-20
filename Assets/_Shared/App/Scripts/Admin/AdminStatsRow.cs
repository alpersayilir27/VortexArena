using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using VortexArena.Protocol;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// Single player row in the stats panel: team stripe, name + <c>#id</c>, K/D/KD cells, one
    /// detail line (score · battery · controllers · ping · state) and actions (rename · AT · ÖLÇ ·
    /// KALİBRE).
    /// <para>
    /// <b>Why a sibling of <see cref="AdminPlayerRow"/> but a separate class:</b> the side card is
    /// narrow and belongs to scene control (POV/team/revive); this row is wide, table-like
    /// and belongs to the operator's <i>record keeping</i> screen. Merging them means a "which
    /// screen am I on" branch in every <c>Bind</c>, where a fix for one screen silently breaks the
    /// other.
    /// </para>
    /// <para>
    /// <b>Look comes from the prefab</b> (<c>Assets/_Shared/App/Resources/UI/</c>); this class is
    /// behaviour only. Unbound prefab fields silently draw nothing.
    /// </para>
    /// <para>
    /// ⚠️ <b>HP, scene and violations are absent by DESIGN</b> — HP lives on the side panel card as
    /// a bar, violations blink live on the HUD strip and card border, and the scene name is the
    /// same for every headset so per-row repetition was only noise.
    /// </para>
    /// <para>
    /// ⚠️ <b>No calibration RESET here:</b> the KALİBRE button <i>reloads</i> from the anchor saved
    /// on the headset. Reset (takes the player out of the fight) is the side panel's KAL button;
    /// putting two opposite actions side by side misleads the operator.
    /// </para>
    /// </summary>
    public class AdminStatsRow : MonoBehaviour
    {
        /// <summary>Row height <b>fallback</b> (px); real height is read from the prefab's
        /// <see cref="RectTransform"/> (<see cref="AdminStatsPanel"/>) so resizing it in the prefab
        /// reflows the list.</summary>
        public const float Height = 74f;

        /// <summary>Confirm window (s) for "AT" — same as <see cref="AdminPlayerRow"/>.</summary>
        private const float ConfirmSeconds = 3f;

        /// <summary>How long the result label ("TAMAM"/"HATA") stays up (s). Permanent would show
        /// a stale result until the next attempt.</summary>
        private const float ResultHoldSeconds = 2f;

        /// <summary>
        /// Longest wait for a reload reply (s).
        /// <para>⚠️ <b>Required:</b> if the headset is closed or frozen <c>calibration_result</c>
        /// never arrives and the button would hang on "YÜKLENİYOR" forever — no result, no retry.</para>
        /// <para>⚠️ Kept clearly LONGER than the headset's own retry window
        /// (<c>ArenaCalibrator.RestoreAttempts</c> × <c>RestoreRetryDelayMs</c> ≈ 10 s, plus each
        /// attempt's load/localize wait): too short and the button says "no reply" seconds before
        /// the real result, making the operator read a success as a failure. If that window grows,
        /// this one grows too.</para>
        /// </summary>
        private const float LoadTimeoutSeconds = 25f;

        // ⚠️ No symbols/emoji in labels: TMP's default font does not guarantee them and a missing
        // glyph draws □ (same rule as AdminPlayerRow and UiKit). Colour + "!" carry the state.
        private const string LabelConfirm = "EMİN?";
        private const string LabelKick = "AT";
        private const string LabelMeasure = "ÖLÇ";
        private const string LabelMeasureFailed = "ÖLÇÜLEMEDİ";
        private const string LabelCalibrate = "KALİBRE";
        private const string LabelCalibrateUncalibrated = "KALİBRE !";
        private const string LabelCalibrateLoading = "YÜKLENİYOR";
        private const string LabelCalibrateOk = "TAMAM";
        private const string LabelCalibrateFailed = "HATA";

        /// <summary>Reason shown when no reply ever arrives — a bare "HATA" without a cause would
        /// leave the operator guessing.</summary>
        private const string TimeoutReason = "başlıktan yanıt gelmedi";

        private const float DeadColorScale = 0.5f;

        /// <summary>Row dimming (same grades as <see cref="AdminPlayerRow"/>): a device expected
        /// back must not look like one removed from the game.</summary>
        private const float ReconnectingAlpha = 0.7f;

        /// <inheritdoc cref="ReconnectingAlpha"/>
        private const float LeftAlpha = 0.45f;

        /// <summary>KALİBRE button state — drives its label and interactability.</summary>
        private enum LoadState
        {
            Idle,
            Loading,
            Ok,
            Failed
        }

        [Header("Kart")]
        [Tooltip("Kartın dış (kenarlık) görseli — seçim ve kalibrasyon vurgusu bunun rengiyle verilir.")]
        [SerializeField] private Image border;
        [Tooltip("Kartın iç dolgusu; satırı seçen düğme de bunun üstündedir.")]
        [SerializeField] private Image background;
        [SerializeField] private Button selectButton;
        [Tooltip("Sol kenardaki takım şeridi.")]
        [SerializeField] private Image stripe;

        [Header("Kimlik")]
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI idText;
        [Tooltip("Düzenleme kipinde açılan grup (giriş alanı + onay/iptal). Kapalıyken ad okunur.")]
        [SerializeField] private GameObject nameEditRoot;
        [SerializeField] private TMP_InputField nameInput;
        [SerializeField] private Button nameApplyButton;
        [SerializeField] private Button nameCancelButton;

        [Header("Eylem düğmeleri")]
        [Tooltip("Satırı ad düzenleme kipine alır (kalem).")]
        [SerializeField] private Button renameButton;
        [Tooltip("Oyuncuyu maçtan atar — iki adımlı onay.")]
        [SerializeField] private Button kickButton;
        [SerializeField] private TextMeshProUGUI kickLabel;
        [Tooltip("Gövde ölçüsünü aldırır (§10.8). Etiketi aynı zamanda GÖSTERGEDİR.")]
        [SerializeField] private Button measureButton;
        [SerializeField] private TextMeshProUGUI measureLabel;
        [Tooltip("Gözlükteki KAYITLI çapa verisinden kalibrasyonu yeniden yükletir (sıfırlamaz).")]
        [SerializeField] private Button calibrateButton;
        [SerializeField] private TextMeshProUGUI calibrateLabel;

        [Header("İstatistik hücreleri")]
        [SerializeField] private TextMeshProUGUI killsText;
        [SerializeField] private TextMeshProUGUI deathsText;
        [SerializeField] private TextMeshProUGUI kdText;
        [Tooltip("SKOR · pil · kumanda · ping · durum — zengin metin içerir (koddan açılır).")]
        [SerializeField] private TextMeshProUGUI detailText;

        private RectTransform _rect;
        private Action<int> _onSelect;
        private Action<int, string> _onPopup;

        private int _playerId;
        private bool _calibrated = true;
        private bool _hasLeft;
        private float _floorOffset;
        private float _kickArmedAt = -1f;

        /// <summary>Name edit mode. While on, <see cref="Bind"/> leaves the name alone — hence a
        /// field rather than a local.</summary>
        private bool _editing;

        private LoadState _loadState = LoadState.Idle;

        /// <summary>When the load/result state was entered (<c>Time.unscaledTime</c>); timeout and
        /// result hold are measured from it.</summary>
        private float _loadStateAt = -1f;

        public int PlayerId => _playerId;

        private RectTransform Rect => _rect != null ? _rect : _rect = (RectTransform)transform;

        /// <summary>
        /// Wires button callbacks. ⚠️ No <c>onClick</c> entries in the prefab: the target player
        /// changes on every <see cref="Bind"/>, so a persistent entry would command the wrong one.
        /// </summary>
        /// <param name="onSelect">Row selected (panel updates the selected player).</param>
        /// <param name="onPopup">Calibration reload failed — the panel shows the reason.</param>
        public void Initialize(Action<int> onSelect, Action<int, string> onPopup)
        {
            _onSelect = onSelect;
            _onPopup = onPopup;

            EnableDetailRichText();

            Wire(selectButton, () => _onSelect?.Invoke(_playerId));
            Wire(renameButton, BeginNameEdit);
            Wire(nameApplyButton, ApplyNameEdit);
            Wire(nameCancelButton, CancelNameEdit);
            Wire(kickButton, PressKick);
            // ⚠️ One step (no confirm): measuring is undoable — a stray press is fixed by measuring
            // again (same reason as AdminPlayerRow).
            Wire(measureButton, () => AdminCommands.MeasureBodyScale(_playerId));
            Wire(calibrateButton, PressCalibrate);

            if (nameInput != null)
            {
                // Enter = apply: the operator should not have to reach for the mouse mid-edit.
                nameInput.onSubmit.RemoveAllListeners();
                nameInput.onSubmit.AddListener(_ => ApplyNameEdit());
            }

            SetNameEditActive(false);
            RefreshKickButton();
            RefreshCalibrateButton();
        }

        /// <summary>
        /// Rich text gate of the detail cell.
        /// <para>⚠️ <b>Enabled HERE, not in the prefab:</b> <c>&lt;color=…&gt;</c> tags come from
        /// <see cref="AdminPlayerRow.FormatBattery"/>/<see cref="AdminPlayerRow.FormatControllers"/>,
        /// so the flag is a contract of the generated text, not a look preference — off in the
        /// prefab the cell draws raw tags, a silent breakage.</para>
        /// <para>⚠️ <b>Only this cell.</b> <see cref="nameText"/> stays rich-text OFF: names come
        /// from outside and a <c>&lt;b&gt;</c> in one would break the row.</para>
        /// </summary>
        private void EnableDetailRichText()
        {
            if (detailText != null)
            {
                detailText.richText = true;
            }
        }

        private static void Wire(Button button, UnityAction action)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        /// <summary>Binds the row to a player (called on every refresh).</summary>
        public void Bind(AdminPlayerView view, bool selected)
        {
            if (view == null)
            {
                return;
            }

            // ⚠️ Open modes are CLOSED when the row rebinds to another player: a kept edit mode
            // would send one player's typed name to another, and a pending load would show the
            // previous player's result here.
            if (_playerId != view.playerId)
            {
                SetNameEditActive(false);
                _kickArmedAt = -1f;
                SetLoadState(LoadState.Idle);
            }

            _playerId = view.playerId;
            _calibrated = view.calibrated;
            _hasLeft = view.HasLeft;
            _floorOffset = view.floorOffset;

            Color team = UiKit.TeamColor(view.team);
            if (stripe != null)
            {
                stripe.color = view.alive ? team : UiKit.Dim(team, DeadColorScale);
            }

            // Uncalibrated rows stand out in red even when unselected (§10.6).
            // ⚠️ No violation blink here: the live channel is the HUD card, and a border blinking
            // on two screens at once focuses the operator's eye nowhere.
            if (border != null)
            {
                border.color = selected ? UiKit.Accent
                    : view.NeedsCalibration ? UiKit.Bad : UiKit.Border;
            }

            // Graded dimming: connected 1.0, expected back 0.7, left 0.45 — "may return" and "gone"
            // are two different operator decisions.
            float alpha = view.IsConnected ? 1f : view.IsReconnecting ? ReconnectingAlpha : LeftAlpha;

            if (nameText != null && !_editing)
            {
                // ⚠️ Never touch the name while editing: the roster refreshes several times a
                // second and would silently overwrite what the operator typed.
                // Name in TEAM COLOUR so the same player looks the same in the scene, the kill feed
                // and here; teamless carries no colour information, title colour stays.
                Color nameColor = IsTeamPlayer(view.team) ? team : UiKit.Title;
                nameText.color = UiKit.WithAlpha(
                    view.alive ? nameColor : UiKit.Dim(nameColor, DeadColorScale), alpha);
                // Number BEFORE the name (same format as the avatar plate): names are not unique,
                // the number is what separates them. 0 = unassigned → name only.
                nameText.text = view.number > 0 ? $"{view.number} · {view.name}" : view.name;
            }

            if (idText != null)
            {
                idText.text = $"#{view.playerId}";
            }

            if (killsText != null)
            {
                killsText.text = view.kills.ToString();
                killsText.color = view.IsConnected ? UiKit.Title : UiKit.Faint;
            }

            if (deathsText != null)
            {
                deathsText.text = view.deaths.ToString();
                deathsText.color = view.IsConnected ? UiKit.Title : UiKit.Faint;
            }

            if (kdText != null)
            {
                // The ratio is undefined without deaths; printing the kill count in the same format
                // gives a readable value instead of a division by zero.
                kdText.text = view.deaths > 0
                    ? (view.kills / (float)view.deaths).ToString("0.00")
                    : view.kills.ToString("0.00");
                kdText.color = view.IsConnected ? UiKit.Muted : UiKit.Faint;
            }

            if (detailText != null)
            {
                // ⚠️ BASE colour here, token colours via rich text: a TMP has one `.color`, but
                // battery and controllers must colour independently.
                detailText.text = BuildDetailLine(view);
                detailText.color = view.IsConnected ? UiKit.Muted : UiKit.Faint;
            }

            // A left row has no command target (§10.2): the player is out, the row only carries
            // match stats. A dead button would feel like "sent but nothing happened".
            SetInteractable(kickButton, !view.HasLeft);
            SetInteractable(renameButton, !view.HasLeft);

            RefreshMeasureButton(view);
            RefreshKickButton();
            RefreshCalibrateButton();
        }

        /// <summary>Expires the confirm window, load timeout and result hold (panel calls every
        /// frame).</summary>
        public void Tick()
        {
            if (_kickArmedAt >= 0f && Time.unscaledTime - _kickArmedAt > ConfirmSeconds)
            {
                _kickArmedAt = -1f;
                RefreshKickButton();
            }

            if (_loadState == LoadState.Loading &&
                Time.unscaledTime - _loadStateAt > LoadTimeoutSeconds)
            {
                // No reply at all is treated as a failure: dropping it silently would read as
                // "the command went through".
                ApplyCalibrationResult(false, TimeoutReason);
                return;
            }

            if ((_loadState == LoadState.Ok || _loadState == LoadState.Failed) &&
                Time.unscaledTime - _loadStateAt > ResultHoldSeconds)
            {
                SetLoadState(LoadState.Idle);
            }
        }

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf == visible)
            {
                return;
            }

            if (!visible)
            {
                // ⚠️ Never leave edit mode on a hidden row: after a relayout the same row lands on
                // another player and the typed name would go to them.
                SetNameEditActive(false);
            }

            gameObject.SetActive(visible);
        }

        /// <summary>Places the row at the given top offset inside the list.</summary>
        public void Place(float top, float height)
        {
            UiKit.Block(Rect, 0f, top, 0f, height);
        }

        /// <summary>
        /// Puts the button into "loading". The <b>bulk action</b> (TÜMÜNÜ KALİBRE ET) calls this
        /// BEFORE sending: if the command returned its result immediately, the row would not be in
        /// loading state yet and the result would be swallowed unseen.
        /// </summary>
        public void BeginCalibrationLoad()
        {
            SetLoadState(LoadState.Loading);
        }

        /// <summary>
        /// Result of a reload attempt (§5.3 <c>calibration_result</c> or timeout).
        /// <para>No popup on success — the "TAMAM" state is enough; on failure the reason goes to
        /// the panel, since a narrow button cannot carry more than "HATA".</para>
        /// </summary>
        public void ApplyCalibrationResult(bool ok, string error)
        {
            SetLoadState(ok ? LoadState.Ok : LoadState.Failed);

            if (!ok)
            {
                _onPopup?.Invoke(_playerId,
                    string.IsNullOrEmpty(error) ? "kalibrasyon yüklenemedi" : error);
            }
        }

        // ---------------------------------------------------------------- internals

        /// <summary>
        /// Esc leaves edit mode.
        /// <para>⚠️ Read through <b>Input System</b>: the project is Input System-only and the old
        /// <c>Input.GetKeyDown</c> throws at runtime. No keyboard → skipped silently.</para>
        /// </summary>
        private void Update()
        {
            if (!_editing)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                CancelNameEdit();
            }
        }

        private static bool IsTeamPlayer(string team)
        {
            return team == "red" || team == "blue";
        }

        private static void SetInteractable(Button button, bool value)
        {
            if (button != null && button.interactable != value)
            {
                button.interactable = value;
            }
        }

        /// <summary>
        /// Opens name edit mode: the read-only name hides, the input field fills with the current
        /// name and takes focus. While open, roster refreshes leave the name alone (see
        /// <see cref="Bind"/>).
        /// </summary>
        private void BeginNameEdit()
        {
            AdminPlayerView view = AdminRoster.Instance != null
                ? AdminRoster.Instance.Find(_playerId)
                : null;
            if (view == null || view.HasLeft)
            {
                return;
            }

            SetNameEditActive(true);

            if (nameInput != null)
            {
                nameInput.text = view.name;
                nameInput.ActivateInputField();
            }
        }

        /// <summary>Sends the new name. Number <c>0</c> means "do NOT change the number" (§5.1):
        /// the server assigns shirt numbers and no UI overrides them.</summary>
        private void ApplyNameEdit()
        {
            if (!_editing)
            {
                return;
            }

            string typed = nameInput != null ? nameInput.text : "";
            SetNameEditActive(false);
            AdminCommands.SetIdentity(_playerId, typed, 0);
        }

        /// <summary>Cancels the edit — nothing is sent, the next <see cref="Bind"/> restores the
        /// name the server knows.</summary>
        private void CancelNameEdit()
        {
            SetNameEditActive(false);
        }

        private void SetNameEditActive(bool editing)
        {
            _editing = editing;

            if (nameEditRoot != null)
            {
                nameEditRoot.SetActive(editing);
            }

            if (nameText != null)
            {
                nameText.gameObject.SetActive(!editing);
            }
        }

        private void PressKick()
        {
            if (_kickArmedAt < 0f)
            {
                _kickArmedAt = Time.unscaledTime;
                RefreshKickButton();
                return;
            }

            _kickArmedAt = -1f;
            AdminCommands.Kick(_playerId);
            RefreshKickButton();
        }

        /// <summary>
        /// Reloads calibration from the anchor SAVED on the headset (§5.3).
        /// <para>One step: it cannot take the player out of the fight, worst case nothing happens —
        /// the two-step lock is for irreversible commands like reset.</para>
        /// </summary>
        private void PressCalibrate()
        {
            if (_loadState == LoadState.Loading)
            {
                return; // double send: the button is already disabled, this is the second line
            }

            BeginCalibrationLoad();
            AdminCommands.ReloadCalibration(_playerId);
        }

        private void SetLoadState(LoadState state)
        {
            _loadState = state;
            _loadStateAt = Time.unscaledTime;
            RefreshCalibrateButton();
        }

        /// <summary>
        /// ÖLÇ is both COMMAND and INDICATOR (§10.8): "ÖLÇ" when unmeasured, the scale itself when
        /// measured. Disabled while uncalibrated — the server rejects it anyway.
        /// <para>⚠️ On failure (<c>scaleError</c> set) the label says so and the button stays
        /// ENABLED: re-measuring is exactly the job.</para>
        /// </summary>
        private void RefreshMeasureButton(AdminPlayerView view)
        {
            bool usable = view.IsPlayer && view.calibrated && !view.HasLeft;
            SetInteractable(measureButton, usable);

            if (measureLabel != null)
            {
                bool failed = !string.IsNullOrEmpty(view.scaleError);
                measureLabel.text = failed ? LabelMeasureFailed
                    : view.bodyScale > 0f ? $"×{view.bodyScale:0.00}" : LabelMeasure;
                measureLabel.color = !usable ? UiKit.Faint
                    : failed ? UiKit.Bad
                    : view.bodyScale > 0f ? UiKit.Good : UiKit.Muted;
            }
        }

        private void RefreshKickButton()
        {
            bool armed = _kickArmedAt >= 0f;

            if (kickLabel != null)
            {
                kickLabel.text = armed ? LabelConfirm : LabelKick;
                kickLabel.color = armed ? UiKit.OnAccent : UiKit.Muted;
            }

            if (kickButton != null && kickButton.targetGraphic is Image image)
            {
                image.color = armed ? UiKit.Bad : UiKit.Hex(0x2A303B, 0xFF);
            }
        }

        /// <summary>
        /// While idle the KALİBRE button is an INDICATOR: green when calibrated, warning colour
        /// over the floor drift threshold (§10.6 — alignment valid, headset space data stale), red
        /// with "!" when uncalibrated. Disabled while loading; the result shows briefly, then idle.
        /// </summary>
        private void RefreshCalibrateButton()
        {
            bool loading = _loadState == LoadState.Loading;
            SetInteractable(calibrateButton, !loading && !_hasLeft);

            if (calibrateLabel == null)
            {
                return;
            }

            switch (_loadState)
            {
                case LoadState.Loading:
                    calibrateLabel.text = LabelCalibrateLoading;
                    calibrateLabel.color = UiKit.Muted;
                    return;
                case LoadState.Ok:
                    calibrateLabel.text = LabelCalibrateOk;
                    calibrateLabel.color = UiKit.Good;
                    return;
                case LoadState.Failed:
                    calibrateLabel.text = LabelCalibrateFailed;
                    calibrateLabel.color = UiKit.Bad;
                    return;
            }

            bool floorDrift = _calibrated &&
                              Mathf.Abs(_floorOffset) > ArenaProtocol.CALIB_FLOOR_WARN_METERS;

            calibrateLabel.text = _calibrated ? LabelCalibrate : LabelCalibrateUncalibrated;
            // Drift = Accent, uncalibrated = Bad: a drifting player can play, an uncalibrated one
            // cannot (same tone as AdminPlayerRow).
            calibrateLabel.color = !_calibrated ? UiKit.Bad
                : floorDrift ? UiKit.Accent : UiKit.Good;
        }

        /// <summary>
        /// Detail cell: score · battery · controllers · ping · state.
        /// <para>Battery thresholds/colours and controller glyphs come from
        /// <see cref="AdminPlayerRow"/> — the same headset must not differ between screens.</para>
        /// </summary>
        private static string BuildDetailLine(AdminPlayerView view)
        {
            string battery = AdminPlayerRow.FormatBattery(view);
            string controllers = AdminPlayerRow.FormatControllers(view);
            // §6.7: -1 = no measurement. "-" so it does not read as "0 ms ping".
            string ping = view.rttMs < 0 ? "-" : $"{view.rttMs} ms";
            string state = StateText(view);

            // Drop the controller token when it carries nothing (both hands unreported).
            string line = string.IsNullOrEmpty(controllers)
                ? $"SKOR {view.score} · {battery} · {ping} · {state}"
                : $"SKOR {view.score} · {battery} · {controllers} · {ping} · {state}";

            // ⚠️ The last reload failure reason STAYS on the row (§10.6): the popup closes itself
            // after a few seconds and without a trace the operator is left with "something
            // happened, but what". The field is held by the SERVER, so a later-joining operator
            // sees the same reason; a successful calibration clears it, so the row heals itself.
            return string.IsNullOrEmpty(view.calibrationError)
                ? line
                : $"{line} · <color=#{ColorUtility.ToHtmlStringRGB(UiKit.Bad)}>{view.calibrationError}</color>";
        }

        /// <summary>⚠️ There is no "offline" state (§2) — a row is either expected back (with a
        /// countdown) or out of the game.</summary>
        private static string StateText(AdminPlayerView view)
        {
            if (view.IsReconnecting)
            {
                return $"yeniden bağlanıyor ({view.ReconnectSecondsLeft} sn)";
            }

            if (view.HasLeft)
            {
                return "ayrıldı";
            }

            if (!view.alive)
            {
                float remaining = view.RespawnRemaining;
                return remaining > 0.1f ? $"ölü ({Mathf.CeilToInt(remaining)} sn)" : "tabanda bekliyor";
            }

            return view.ready ? "hazır" : "bekliyor";
        }
    }
}
