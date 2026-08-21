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
    /// detail line (score · battery · controllers · ping · state) and actions (SIFIRLA · rename ·
    /// AT · ÖLÇ · KALİBRE).
    /// <para>
    /// <b>Why a sibling of <see cref="AdminPlayerRow"/> but a separate class:</b> the side card is
    /// narrow and belongs to scene control (POV/team/identity); this row is wide, table-like
    /// and belongs to the operator's <i>record keeping</i> screen. Merging them means a "which
    /// screen am I on" branch in every <c>Bind</c>, where a fix for one screen silently breaks the
    /// other.
    /// </para>
    /// <para>
    /// <b>Look comes from the prefab</b> (<c>Assets/_Shared/App/Resources/UI/</c>); this class is
    /// behaviour only. Unbound prefab fields silently draw nothing.
    /// </para>
    /// <para>
    /// <b>Two prefabs, one class:</b> <c>AdminStatsRow</c> is the full-width row, its variant
    /// <c>AdminStatsRowNarrow</c> the one the split (team) columns use — same fields, stacked
    /// layout and icon buttons (<see cref="iconButtons"/>). ⚠️ The <b>variant</b> is what keeps
    /// them in sync: everything but the rects and the glyphs is inherited, so a change made here
    /// reaches both. Do not fork it into a second prefab.
    /// </para>
    /// <para>
    /// ⚠️ <b>HP, scene and violations are absent by DESIGN</b> — HP lives on the side panel card as
    /// a bar, violations blink live on the HUD strip and card border, and the scene name is the
    /// same for every headset so per-row repetition was only noise.
    /// </para>
    /// <para>
    /// ⚠️ <b>SIFIRLA is ONE button carrying BOTH reset modes</b> (<see cref="HoldButton"/>): a tap
    /// voids the current alignment and keeps the headset's saved anchor, a 1 s hold wipes that
    /// anchor too — afterwards KALİBRE fails and the player must redo the A/B sequence by hand.
    /// Severity comes from press DURATION, never from picking the right neighbour: two separate
    /// buttons made the operator choose a severity before knowing they needed one, and put the
    /// destructive one a mis-click away. Same contract as the panel's bulk bar
    /// (<see cref="AdminStatsPanel"/>) and the side card's KAL (<see cref="AdminPlayerRow"/>).
    /// </para>
    /// <para>
    /// ⚠️ <b>KALİBRE is the OPPOSITE action and stays its own button:</b> it <i>reloads</i>
    /// alignment from the saved anchor and destroys nothing. The two sit at opposite ends of the
    /// button strip so a mis-aimed click cannot land on the other.
    /// </para>
    /// </summary>
    public class AdminStatsRow : MonoBehaviour
    {
        /// <summary>Row height <b>fallback</b> (px); real height is read from the prefab's
        /// <see cref="RectTransform"/> (<see cref="AdminStatsPanel"/>) so resizing it in the prefab
        /// reflows the list.</summary>
        public const float Height = 74f;

        /// <summary>Confirm window (s) of the AT button — same as <see cref="AdminPlayerRow"/>.
        /// ⚠️ SIFIRLA does NOT use it: its friction is the hold (<see cref="HoldButton"/>).</summary>
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
        private const string LabelReset = "SIFIRLA";

        /// <summary>SIFIRLA while the hard-reset hold is running. ⚠️ Names what is being DESTROYED,
        /// not "keep holding": the operator must be able to abort by reading the button.</summary>
        private const string LabelPurging = "SİLİNİYOR";

        /// <summary>Confirmation shown right after the device record is wiped, in both label modes.
        /// ⚠️ <b>Required:</b> the hold has no other ending — without it the button snaps back to
        /// SIFIRLA and a completed wipe reads exactly like one aborted by sliding off.</summary>
        private const string LabelPurged = "SİLİNDİ";
        private const string LabelMeasure = "ÖLÇ";
        private const string LabelMeasureFailed = "ÖLÇÜLEMEDİ";
        private const string LabelCalibrate = "KALİBRE";
        private const string LabelCalibrateUncalibrated = "KALİBRE !";
        private const string LabelCalibrateLoading = "YÜKLENİYOR";
        private const string LabelCalibrateOk = "TAMAM";
        private const string LabelCalibrateFailed = "HATA";

        // ⚠️ Icon mode (<see cref="iconButtons"/>) labels: the glyph names the ACTION, so the word
        // shrinks to a STATE badge and is EMPTY while idle. A word wide enough for the wide row
        // does not fit an icon button — but dropping the state entirely would take the indicator
        // half of ÖLÇ/KALİBRE with it.
        private const string IconIdle = "";
        private const string IconConfirm = "EMİN?";
        private const string IconOk = "TAMAM";
        private const string IconFailed = "HATA";

        /// <summary>⚠️ Three dots, not "…": TMP's default font does not guarantee the ellipsis
        /// glyph and a missing one draws □ (same rule as the labels above).</summary>
        private const string IconLoading = "...";

        /// <summary>Icon-mode badge while the hard-reset hold runs. Shorter than
        /// <see cref="LabelPurging"/> — the icon button has no room for the word.</summary>
        private const string IconPurging = "SİL";

        private const string IconUncalibrated = "!";

        /// <summary>Reason shown when no reply ever arrives — a bare "HATA" without a cause would
        /// leave the operator guessing.</summary>
        private const string TimeoutReason = "başlıktan yanıt gelmedi";

        private const float DeadColorScale = 0.5f;

        /// <summary>Row dimming (same grades as <see cref="AdminPlayerRow"/>): a device expected
        /// back must not look like one removed from the game.</summary>
        private const float ReconnectingAlpha = 0.7f;

        /// <inheritdoc cref="ReconnectingAlpha"/>
        private const float LeftAlpha = 0.45f;

        /// <summary>Idle fill shared by the two-step buttons; armed they invert to
        /// <see cref="UiKit.Bad"/>.</summary>
        private static readonly Color ConfirmIdleFill = UiKit.Hex(0x2A303B, 0xFF);

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
        [Tooltip("Kalibrasyonu sıfırlar — TEK düğme: kısa basış o anki hizalamayı düşürür, " +
                 "1 sn basılı tutmak gözlükteki KAYITLI çapayı da sildirir (ardından KALİBRE iş " +
                 "görmez, oyuncu elle A/B almak zorunda kalır).")]
        [SerializeField] private Button purgeButton;
        [SerializeField] private TextMeshProUGUI purgeLabel;
        [Tooltip("Gövde ölçüsünü aldırır (§10.8). Etiketi aynı zamanda GÖSTERGEDİR.")]
        [SerializeField] private Button measureButton;
        [SerializeField] private TextMeshProUGUI measureLabel;
        [Tooltip("Gözlükteki KAYITLI çapa verisinden kalibrasyonu yeniden yükletir (sıfırlamaz).")]
        [SerializeField] private Button calibrateButton;
        [SerializeField] private TextMeshProUGUI calibrateLabel;

        [Header("İkon kipi (dar sütun varyantı)")]
        [Tooltip("Düğmeler yazı yerine ikon taşıyor: etiket boş kalır, yalnız durum rozeti " +
                 "yazar (EMİN? · TAMAM · HATA · ! · ×ölçek). Yalnız dar varyant prefabında açık.")]
        [SerializeField] private bool iconButtons;
        [Tooltip("Aşağıdaki ikonlar yalnız ikon kipinde bağlanır; rengi durumu taşır, " +
                 "etiketin rengiyle birebir aynı.")]
        [SerializeField] private Image renameIcon;
        [SerializeField] private Image kickIcon;
        [SerializeField] private Image purgeIcon;
        [SerializeField] private Image measureIcon;
        [SerializeField] private Image calibrateIcon;

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

        /// <summary>Press-duration gate of SIFIRLA; null when the button is unwired.</summary>
        private HoldButton _resetHold;

        /// <summary>Was SIFIRLA painted in its "holding" look last frame — without it the button
        /// would stay red after the press ends (<see cref="Tick"/> repaints only while pressed).</summary>
        private bool _resetHoldPainted;

        /// <summary>When the device record was wiped (<c>Time.unscaledTime</c>); &lt; 0 = no
        /// confirmation showing. Held for <see cref="ResultHoldSeconds"/>, like TAMAM/HATA.</summary>
        private float _purgedAt = -1f;

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
            // ⚠️ NOT Wire(): onClick fires on pointer-up whatever the duration, so a completed hold
            // would also send the soft reset — the player would be reset twice.
            _resetHold = HoldButton.Attach(purgeButton, PressReset, PressPurge);
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
            RefreshPurgeButton();
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
                _resetHold?.Cancel();
                _purgedAt = -1f;
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
            // ⚠️ Purging needs the headset ONLINE: the server only forwards the command to a live
            // connection, so on a left row it would look sent and delete nothing.
            SetInteractable(purgeButton, !view.HasLeft);

            RefreshMeasureButton(view);
            RefreshKickButton();
            RefreshPurgeButton();
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

            if (_resetHold != null && (_resetHold.IsPressed || _resetHoldPainted))
            {
                RefreshPurgeButton();
            }

            if (_purgedAt >= 0f)
            {
                // ⚠️ The window starts when the FINGER LIFTS — see AdminPlayerRow.Tick for why.
                if (_resetHold != null && _resetHold.IsPressed)
                {
                    _purgedAt = Time.unscaledTime;
                }
                else if (Time.unscaledTime - _purgedAt > ResultHoldSeconds)
                {
                    _purgedAt = -1f;
                    RefreshPurgeButton();
                }
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

                // ⚠️ Half-armed confirms die with the row too: a hidden row gets no Tick (the panel
                // only ticks visible ones), so an armed window would survive the hide and fire the
                // destructive command on the FIRST click after the row comes back.
                _kickArmedAt = -1f;
                _resetHold?.Cancel();
                _purgedAt = -1f;
                RefreshKickButton();
                RefreshPurgeButton();
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
        /// TAP: voids the player's current alignment and KEEPS the anchor saved on the headset
        /// (§5.2 <c>clear_calibration</c>, soft mode) — KALİBRE puts them back in one click. This
        /// is the everyday reset, so it fires on the first press: the press-duration grammar puts
        /// the friction on the destructive variant, not on this one.
        /// </summary>
        private void PressReset()
        {
            // A soft reset clears a standing "SİLİNDİ": the newer, weaker command is what happened.
            _purgedAt = -1f;
            AdminCommands.ClearCalibration(_playerId, keepSaved: true);
            RefreshPurgeButton();
        }

        /// <summary>
        /// HOLD (1 s): the same reset plus the anchor SAVED on that headset (hard mode) — the row's
        /// most destructive command, since it also destroys what KALİBRE would read and recovery
        /// becomes the manual A/B sequence in the headset.
        /// <para>The hold IS the confirmation; there is no two-step window on top of it
        /// (<see cref="HoldButton"/>).</para>
        /// </summary>
        private void PressPurge()
        {
            _purgedAt = Time.unscaledTime;
            AdminCommands.ClearCalibration(_playerId, keepSaved: false);
            RefreshPurgeButton();
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

            bool failed = !string.IsNullOrEmpty(view.scaleError);
            bool measured = view.bodyScale > 0f;
            Color color = !usable ? UiKit.Faint
                : failed ? UiKit.Bad
                : measured ? UiKit.Good : UiKit.Muted;

            // ⚠️ The measured scale survives icon mode: it is the INDICATOR half of the button,
            // not a caption. Only the idle word ("ÖLÇ") gives way to the glyph.
            string text = iconButtons
                ? (failed ? IconFailed : measured ? $"×{view.bodyScale:0.00}" : IconIdle)
                : (failed ? LabelMeasureFailed : measured ? $"×{view.bodyScale:0.00}" : LabelMeasure);

            ApplyState(measureLabel, measureIcon, text, color);
        }

        private void RefreshKickButton()
        {
            ApplyConfirmButton(kickButton, kickLabel, kickIcon, _kickArmedAt >= 0f,
                LabelConfirm, LabelKick, UiKit.Muted);
        }

        /// <summary>SIFIRLA look. Idle is NEUTRAL (not red like the old device-wipe button): the
        /// tap is the everyday reset. The fill walks to red as the hold completes — that ramp IS
        /// the progress bar, so the operator sees how much of the wipe is left and can still slide
        /// off the button.</summary>
        private void RefreshPurgeButton()
        {
            // ⚠️ The RESULT outranks the press: the wipe lands at the threshold, so from that
            // moment the button says SİLİNDİ even though the finger is still down.
            bool purged = _purgedAt >= 0f;
            bool holding = !purged && _resetHold != null && _resetHold.IsPressed;
            _resetHoldPainted = holding || purged;

            // Faint on a left row: a live label on a disabled button keeps pulling the operator's
            // eye to a command that cannot fire (same grade as ÖLÇ).
            bool usable = purgeButton == null || purgeButton.interactable;
            string text = holding ? (iconButtons ? IconPurging : LabelPurging)
                : purged ? LabelPurged
                : iconButtons ? IconIdle : LabelReset;

            // ⚠️ The wipe confirmation is GREEN even though the command was destructive: it reports
            // "the thing you asked for happened" — the same grammar as KALİBRE's TAMAM.
            ApplyState(purgeLabel, purgeIcon, text,
                holding ? UiKit.OnAccent : purged ? UiKit.Good : usable ? UiKit.Muted : UiKit.Faint);

            if (purgeButton != null && purgeButton.targetGraphic is Image image)
            {
                // Back to the plain fill once wiped: the red ramp meant "still filling", and
                // leaving it up would read as a press that never finished.
                image.color = holding
                    ? Color.Lerp(ConfirmIdleFill, UiKit.Bad, _resetHold.HoldProgress)
                    : ConfirmIdleFill;
            }
        }

        /// <summary>Two-step button look (AT): idle keeps the row fill with its own label colour,
        /// armed inverts (red fill) — the operator reads what the second press will do off the
        /// button itself. ⚠️ SIFIRLA does NOT go through here: its severity comes from press
        /// duration, so it paints a progress ramp instead (<see cref="RefreshPurgeButton"/>).</summary>
        private void ApplyConfirmButton(Button button, TextMeshProUGUI label, Image icon, bool armed,
            string armedText, string idleText, Color idleColor)
        {
            string text = iconButtons
                ? (armed ? IconConfirm : IconIdle)
                : (armed ? armedText : idleText);

            ApplyState(label, icon, text, armed ? UiKit.OnAccent : idleColor);

            if (button != null && button.targetGraphic is Image image)
            {
                image.color = armed ? UiKit.Bad : ConfirmIdleFill;
            }
        }

        /// <summary>Writes a button's state to its label and glyph at once — the icon tint IS the
        /// label colour, so the state reads the same whichever half is visible.</summary>
        private static void ApplyState(TextMeshProUGUI label, Image icon, string text, Color color)
        {
            if (label != null)
            {
                label.text = text;
                label.color = color;
            }

            if (icon != null)
            {
                icon.color = color;
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

            switch (_loadState)
            {
                case LoadState.Loading:
                    ApplyState(calibrateLabel, calibrateIcon,
                        iconButtons ? IconLoading : LabelCalibrateLoading, UiKit.Muted);
                    return;
                case LoadState.Ok:
                    ApplyState(calibrateLabel, calibrateIcon,
                        iconButtons ? IconOk : LabelCalibrateOk, UiKit.Good);
                    return;
                case LoadState.Failed:
                    ApplyState(calibrateLabel, calibrateIcon,
                        iconButtons ? IconFailed : LabelCalibrateFailed, UiKit.Bad);
                    return;
            }

            bool floorDrift = _calibrated &&
                              Mathf.Abs(_floorOffset) > ArenaProtocol.CALIB_FLOOR_WARN_METERS;

            string text = _calibrated
                ? (iconButtons ? IconIdle : LabelCalibrate)
                : (iconButtons ? IconUncalibrated : LabelCalibrateUncalibrated);
            // Drift = Accent, uncalibrated = Bad: a drifting player can play, an uncalibrated one
            // cannot (same tone as AdminPlayerRow).
            ApplyState(calibrateLabel, calibrateIcon, text,
                !_calibrated ? UiKit.Bad : floorDrift ? UiKit.Accent : UiKit.Good);
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
