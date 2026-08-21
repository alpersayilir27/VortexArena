using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using VortexArena.Protocol;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// Single player row in the side panels: team stripe, name + <c>#id</c>, HP bar, stats and
    /// action buttons (POV · KAL · ÖLÇ · TAKIM · AT).
    /// <para>
    /// <b>Look comes from the prefab</b> (<c>Assets/_Shared/App/Resources/UI/AdminPlayerRow.prefab</c>);
    /// this class is behaviour only. Unbound fields silently draw nothing — keep prefab wiring intact.
    /// </para>
    /// <para>Kick is two-step (<see cref="ConfirmSeconds"/>): taking a player out of the fight must
    /// not happen on one stray click.</para>
    /// <para>KAL both SHOWS calibration state and clears it (§10.6) — one way only, since only the
    /// headset knows when alignment really settled. It is the row's ONLY reset: a tap voids the
    /// current alignment, a hold also wipes the headset's saved anchor (<see cref="HoldButton"/>).</para>
    /// </summary>
    public class AdminPlayerRow : MonoBehaviour
    {
        /// <summary>Row height <b>fallback</b> (px); real height is read from the prefab's
        /// <see cref="RectTransform"/> (<see cref="AdminHud"/>) so resizing it in the prefab
        /// reflows the column.</summary>
        public const float Height = 116f;

        /// <summary>Confirm window (s) of the "AT" button. ⚠️ KAL does NOT use it — its friction
        /// is the hold, see <see cref="HoldButton"/>.</summary>
        private const float ConfirmSeconds = 3f;

        // ⚠️ No ✓/✗ symbols in labels: TMP's default font does not guarantee them and a missing
        // glyph draws □. State is carried by colour + an exclamation mark, present in every font.
        private const string LabelCalibrated = "KAL";
        private const string LabelUncalibrated = "KAL !";
        private const string LabelConfirm = "EMİN?";

        /// <summary>Calibrated but floor drift over threshold (§10.6): alignment valid, headset
        /// space data stale. "?" not a symbol — "!" already means uncalibrated.</summary>
        private const string LabelFloorDrift = LabelCalibrated + " ?";

        /// <summary>KAL while the hard-reset hold is running. ⚠️ Says what is being DESTROYED, not
        /// "keep holding": the operator must be able to abort by reading the button.</summary>
        private const string LabelPurging = "SİLİNİYOR";

        /// <summary>Confirmation shown right after the device record is wiped. ⚠️ <b>Required:</b>
        /// the hold has no other ending — without it the button snaps back to KAL and the operator
        /// cannot tell a completed wipe from one they aborted by sliding off.</summary>
        private const string LabelPurged = "SİLİNDİ";

        /// <summary>How long <see cref="LabelPurged"/> stays up (s) — same as the stats row's
        /// TAMAM/HATA hold, so a result reads the same on both screens.</summary>
        private const float PurgedHoldSeconds = 2f;

        /// <summary>ÖLÇ button for a not-yet-measured player.</summary>
        private const string LabelMeasure = "ÖLÇ";

        /// <summary>Last measurement failed (§10.8): says so instead of a scale, so the operator
        /// does not read "pressed it and nothing happened".</summary>
        private const string LabelMeasureFailed = "ÖLÇÜLEMEDİ";

        private const float DeadColorScale = 0.5f;

        /// <summary>Headset battery warning thresholds (0..1) — an OPERATOR call: 25% "swap before
        /// the session ends", 50% "keep an eye on it". Shared with the stats panel through
        /// <see cref="FormatBattery"/> so the same battery never differs between screens.</summary>
        private const float BatteryCritical = 0.25f;

        /// <inheritdoc cref="BatteryCritical"/>
        private const float BatteryLow = 0.5f;

        // ⚠️ ASCII controller glyphs, same reason as the calibration labels: ●/◐/✕ are not
        // guaranteed in TMP's default font. COLOUR carries the distinction; the letter is only a
        // fallback for a colour-blind operator.
        private const string GlyphControllerOk = "+";
        private const string GlyphControllerUntracked = "~";
        private const string GlyphControllerLost = "X";
        private const string GlyphControllerUnknown = "-";

        /// <summary>Row dimming: a device expected back must not look like one removed from the
        /// game (§2) — the first may return, the second needs no action.</summary>
        private const float ReconnectingAlpha = 0.7f;

        /// <inheritdoc cref="ReconnectingAlpha"/>
        private const float LeftAlpha = 0.45f;

        [Header("Kart")]
        [Tooltip("Kartın dış (kenarlık) görseli — seçim ve kalibrasyon vurgusu bunun rengiyle verilir.")]
        [SerializeField] private Image border;
        [Tooltip("Kartın iç dolgusu; satırı seçen düğme de bunun üstündedir.")]
        [SerializeField] private Image background;
        [SerializeField] private Button selectButton;
        [Tooltip("Sol kenardaki takım şeridi.")]
        [SerializeField] private Image stripe;

        [Header("Metinler")]
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI idText;
        [SerializeField] private TextMeshProUGUI hpText;
        [SerializeField] private TextMeshProUGUI statsText;

        [Header("Can barı")]
        [Tooltip("Barın DOLGU görseli (zemin değil) — genişliği anchorMax.x ile sürülür.")]
        [SerializeField] private Image hpFill;

        [Header("Eylem düğmeleri")]
        [SerializeField] private Button povButton;
        [Tooltip("Kalibrasyonu sıfırlar — TEK düğme: kısa basış o anki hizalamayı düşürür, " +
                 "1 sn basılı tutmak gözlükteki KAYITLI çapayı da sildirir (§10.6).")]
        [SerializeField] private Button calibButton;
        [SerializeField] private TextMeshProUGUI calibLabel;
        [Tooltip("Gövde ölçüsünü aldırır (§10.8). Etiketi aynı zamanda GÖSTERGEDİR: " +
                 "ölçülmemişse 'ÖLÇ', ölçülmüşse çarpan.")]
        [SerializeField] private Button measureButton;
        [SerializeField] private TextMeshProUGUI measureLabel;
        [SerializeField] private Button teamButton;
        [SerializeField] private TextMeshProUGUI teamLabel;
        [SerializeField] private Button kickButton;
        [SerializeField] private TextMeshProUGUI kickLabel;

        private RectTransform _rect;
        private Action<int> _onSelect;
        private Action<int> _onPov;

        private int _playerId;
        private string _team = "";
        private float _kickArmedAt = -1f;
        private bool _calibrated = true;

        /// <summary>Press-duration gate of the KAL button; null when the button is unwired.</summary>
        private HoldButton _calibHold;

        /// <summary>Was the button painted in its "holding" look last frame. Without it the button
        /// would stay red after the press ends: <see cref="Tick"/> repaints only while pressed.</summary>
        private bool _calibHoldPainted;

        /// <summary>When the device record was wiped (<c>Time.unscaledTime</c>); &lt; 0 = no
        /// confirmation showing.</summary>
        private float _calibPurgedAt = -1f;

        /// <summary>Bound player's floor drift (§10.6); cached like <see cref="_calibrated"/>
        /// because <see cref="Tick"/> redraws the KAL button outside <see cref="Bind"/>.</summary>
        private float _floorOffset;

        /// <summary>Border colour outside violations (selected / uncalibrated / normal).
        /// <para>⚠️ Must be cached: the violation blink writes to the border every frame and needs
        /// a colour to return to — <see cref="Bind"/> only runs on the next refresh, so the row
        /// would stay frozen red.</para></summary>
        private Color _baseBorderColor = UiKit.Border;

        private RectTransform Rect => _rect != null ? _rect : _rect = (RectTransform)transform;

        /// <summary>
        /// Wires button callbacks. ⚠️ No <c>onClick</c> entries in the prefab: the target player
        /// changes on every <see cref="Bind"/>, so a persistent entry would command the wrong one.
        /// </summary>
        public void Initialize(Action<int> onSelect, Action<int> onPov)
        {
            _onSelect = onSelect;
            _onPov = onPov;

            EnableStatsRichText();

            Wire(selectButton, () => _onSelect?.Invoke(_playerId));
            Wire(povButton, () => _onPov?.Invoke(_playerId));
            // ⚠️ NOT Wire(): the reset is a duration gate, and onClick would fire after a
            // completed hold too — the player would be reset twice, once hard once soft.
            _calibHold = HoldButton.Attach(calibButton, PressCalibrationClear, PressCalibrationPurge);
            // ⚠️ One step (no confirm): measuring is undoable — a stray press is fixed by measuring
            // again. It is not an "out of the fight" command like AT/KAL.
            Wire(measureButton, () => AdminCommands.MeasureBodyScale(_playerId));
            Wire(teamButton, ToggleTeam);
            Wire(kickButton, PressKick);
        }

        /// <summary>
        /// Rich text gate of the stats line.
        /// <para>⚠️ <b>Enabled HERE, not in the prefab:</b> <see cref="BuildStatsLine"/> emits
        /// <c>&lt;color=…&gt;</c> tags, so the flag is a contract of the generated text, not a look
        /// preference — off in the prefab the card draws raw tags, a silent breakage.</para>
        /// <para>⚠️ <b>Only for <see cref="statsText"/>.</b> <see cref="nameText"/> stays rich-text
        /// OFF: names come from outside and a <c>&lt;b&gt;</c> in one would break the row.</para>
        /// </summary>
        private void EnableStatsRichText()
        {
            if (statsText != null)
            {
                statsText.richText = true;
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

            // ⚠️ A pooled row rebinds to ANOTHER player: a leftover "SİLİNDİ" would credit the
            // wipe to whoever lands here next.
            if (_playerId != view.playerId)
            {
                _calibPurgedAt = -1f;
                _calibHold?.Cancel();
            }

            _playerId = view.playerId;
            _team = view.team;
            _calibrated = view.calibrated;
            _floorOffset = view.floorOffset;
            RefreshMeasureButton(view);

            Color team = UiKit.TeamColor(view.team);
            if (stripe != null)
            {
                stripe.color = view.alive ? team : UiKit.Dim(team, DeadColorScale);
            }

            // Uncalibrated rows stand out in red even when unselected: the operator must spot the
            // row needing attention at a glance (§10.6).
            _baseBorderColor = selected ? UiKit.Accent
                : view.NeedsCalibration ? UiKit.Bad : UiKit.Border;

            if (border != null)
            {
                border.color = _baseBorderColor;
            }

            // Graded dimming: connected 1.0, expected back 0.7, left 0.45 — "may return" and "gone"
            // are two different operator decisions.
            float alpha = view.IsConnected ? 1f : view.IsReconnecting ? ReconnectingAlpha : LeftAlpha;
            if (nameText != null)
            {
                // Name drawn in TEAM COLOUR: the same name also appears on the scene label, the
                // top-down marker and the kill feed, and one colour everywhere answers "which team"
                // without reading. ⚠️ Teamless (FFA) carries no colour information — title colour
                // stays, neutral grey would only be unreadable. Aliveness is still carried by
                // DIMMING, since the colour channel belongs to the team.
                Color nameColor = IsTeamPlayer(view.team) ? team : UiKit.Title;
                nameText.color = UiKit.WithAlpha(
                    view.alive ? nameColor : UiKit.Dim(nameColor, DeadColorScale), alpha);
                // Number BEFORE the name (same format as the avatar plate): names are not unique,
                // the number is what separates two identical ones. 0 = unassigned → name only.
                nameText.text = view.number > 0 ? $"{view.number} · {view.name}" : view.name;
            }

            if (idText != null)
            {
                idText.text = $"#{view.playerId}";
            }

            UiKit.SetBarFill(hpFill, view.HpNormalized);
            if (hpFill != null)
            {
                hpFill.color = view.HpNormalized > 0.5f ? UiKit.Good
                    : view.HpNormalized > 0.2f ? UiKit.Accent : UiKit.Bad;
            }

            if (hpText != null)
            {
                hpText.text = $"{Mathf.RoundToInt(view.hp)} HP";
            }

            if (statsText != null)
            {
                // ⚠️ BASE colour here, token colours via rich text: a TMP has one `.color`, but
                // battery and controllers must colour independently. Flag: EnableStatsRichText.
                statsText.text = BuildStatsLine(view);
                statsText.color = view.IsConnected ? UiKit.Muted : UiKit.Faint;
            }

            // Team button names the OTHER team (what will happen, not what is).
            // ⚠️ Not "MAVİYE"/"KIRMIZIYA": the narrow button row ellipsised the longer
            // one ("KIRMIZ…"). Suffix dropped, meaning kept.
            if (teamLabel != null)
            {
                teamLabel.text = view.team == "red" ? "MAVİ" : view.team == "blue" ? "KIRMIZI" : "TAKIM";
            }

            // A left row has no command target (§10.2): the player is out, the row only carries
            // match stats. Buttons go off so a dead press is not mistaken for a sent command.
            SetInteractable(teamButton, !view.HasLeft);
            SetInteractable(kickButton, !view.HasLeft);

            RefreshKickButton();
            RefreshCalibrationButton();
        }

        /// <summary>Does the player have a team — team colour is meaningful only then.</summary>
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
        /// Violation highlight on the border (§10.9). Top-down rings default to top-down only
        /// (<see cref="AdminMarkerVisibility.TopDownOnly"/>), so in POV/free mode the row is the
        /// operator's only channel.
        /// <para><b>Border priority: violation &gt; selection &gt; uncalibrated &gt; normal.</b>
        /// Selection is already told twice (ring size, bottom strip) and a violation is what needs
        /// attention <i>now</i>; being uncalibrated is a one-off field job.</para>
        /// <para>⚠️ Returns to <see cref="_baseBorderColor"/> when the violation ends — otherwise
        /// the row stays frozen red until the next <see cref="Bind"/>.</para>
        /// <para>Per frame rather than on the refresh tick (0.25 s) because the colour blinks;
        /// costs one flag read plus one colour assignment.</para>
        /// </summary>
        private void Update()
        {
            if (border == null)
            {
                return;
            }

            AdminViolationKind violation = AdminViolations.Of(_playerId);
            border.color = violation != AdminViolationKind.None
                ? AdminViolations.Blink(violation)
                : _baseBorderColor;
        }

        /// <summary>Expires the kick confirm window and paints the reset hold (HUD calls every
        /// frame — the hold fill would otherwise step at the roster's 4 Hz).</summary>
        public void Tick()
        {
            if (_kickArmedAt >= 0f && Time.unscaledTime - _kickArmedAt > ConfirmSeconds)
            {
                _kickArmedAt = -1f;
                RefreshKickButton();
            }

            if (_calibHold != null && (_calibHold.IsPressed || _calibHoldPainted))
            {
                RefreshCalibrationButton();
            }

            if (_calibPurgedAt >= 0f)
            {
                // ⚠️ The window starts when the FINGER LIFTS. The wipe fires at the threshold while
                // the press is still on, so a window started there would expire under the operator's
                // thumb and drop the button back to "SİLİNİYOR" — reading as if it had not landed.
                if (_calibHold != null && _calibHold.IsPressed)
                {
                    _calibPurgedAt = Time.unscaledTime;
                }
                else if (Time.unscaledTime - _calibPurgedAt > PurgedHoldSeconds)
                {
                    _calibPurgedAt = -1f;
                    RefreshCalibrationButton();
                }
            }
        }

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf == visible)
            {
                return;
            }

            // ⚠️ A hold in flight is dropped: hidden rows stop ticking, and the pooled row comes
            // back bound to ANOTHER player — the press would land on them.
            if (!visible)
            {
                _calibHold?.Cancel();
                _calibPurgedAt = -1f;
            }

            gameObject.SetActive(visible);
        }

        /// <summary>Places the row at the given top offset inside the column.</summary>
        public void Place(float top, float height)
        {
            UiKit.Block(Rect, 0f, top, 0f, height);
        }

        // ---------------------------------------------------------------- internals

        private void ToggleTeam()
        {
            // Teamless (FFA/new) player goes red: the server already balances, this is the
            // operator's manual override.
            string next = _team == "red" ? "blue" : "red";
            AdminCommands.SetTeam(_playerId, next);
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
        /// TAP: invalidates the current alignment and keeps the anchor SAVED on the headset
        /// (<c>keepSaved: true</c>), so KALİBRE puts the player back in one click. This is the
        /// everyday reset.
        /// <para>⚠️ <b>Sent even while the row looks uncalibrated</b>, and this gate stays open: a
        /// red row means TWO states — never calibrated, and stuck mid-sequence (A taken, B not).
        /// The wire cannot tell them apart (<c>calibrated</c> is false in both), so filtering
        /// "already uncalibrated" would block exactly the player who needs the reset; the headset
        /// wipes half sequences too (§10.6).</para>
        /// <para>Direction stays one-way — only the headset can re-enable calibration.</para>
        /// </summary>
        private void PressCalibrationClear()
        {
            // A soft reset clears a standing "SİLİNDİ": the newer, weaker command is what happened.
            _calibPurgedAt = -1f;
            AdminCommands.ClearCalibration(_playerId, keepSaved: true);
            RefreshCalibrationButton();
        }

        /// <summary>
        /// HOLD (1 s): the same reset plus the headset's SAVED anchor (<c>keepSaved: false</c>) — venue
        /// maintenance, and the only way back is the manual A/B sequence in the headset.
        /// <para>⚠️ The two modes share ONE button on purpose: they answer the same operator
        /// question ("this player's alignment is wrong") and only differ in how far back they go.
        /// Two neighbouring buttons made the operator pick a severity before knowing they needed
        /// one, and the destructive one was a mis-click away.</para>
        /// </summary>
        private void PressCalibrationPurge()
        {
            _calibPurgedAt = Time.unscaledTime;
            AdminCommands.ClearCalibration(_playerId, keepSaved: false);
            RefreshCalibrationButton();
        }

        /// <summary>
        /// ÖLÇ is both COMMAND and INDICATOR (§10.8): "ÖLÇ" when unmeasured, the scale itself when
        /// measured — the operator reads who is measured off the list, not by trial.
        /// <para>Disabled for an uncalibrated player (the server rejects it anyway, measurement is
        /// relative to the arena floor) and for a left row — a dead button feels like "sent but
        /// nothing happened".</para>
        /// <para>⚠️ On failure (<c>scaleError</c> set) the label says so and the button stays
        /// ENABLED: re-measuring is exactly the job. The reason itself lives in the notice line.</para>
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

        /// <summary>
        /// KAL button state. Calibrated but over the floor drift threshold is its own state
        /// (§10.6): alignment accepted, headset space data stale, field work pending — a green tick
        /// would hide it.
        /// </summary>
        private void RefreshCalibrationButton()
        {
            // ⚠️ The RESULT outranks the press: the wipe lands at the threshold, so from that
            // moment the button says SİLİNDİ even though the finger is still down.
            bool purged = _calibPurgedAt >= 0f;
            bool holding = !purged && _calibHold != null && _calibHold.IsPressed;
            float progress = holding ? _calibHold.HoldProgress : 0f;
            _calibHoldPainted = holding || purged;

            bool floorDrift = _calibrated &&
                              Mathf.Abs(_floorOffset) > ArenaProtocol.CALIB_FLOOR_WARN_METERS;

            if (calibLabel != null)
            {
                calibLabel.text = holding ? LabelPurging
                    : purged ? LabelPurged
                    : !_calibrated ? LabelUncalibrated
                    : floorDrift ? LabelFloorDrift : LabelCalibrated;
                // Drift = Accent (the repo's "warning, not error" tone), uncalibrated = Bad: a
                // drifting player can play, an uncalibrated one cannot. ⚠️ The wipe confirmation is
                // GREEN even though the command was destructive: it reports "the thing you asked
                // for happened", the same grammar as the stats row's TAMAM.
                calibLabel.color = holding ? UiKit.OnAccent
                    : purged ? UiKit.Good
                    : !_calibrated ? UiKit.Bad
                    : floorDrift ? UiKit.Accent : UiKit.Good;
            }

            if (calibButton != null && calibButton.targetGraphic is Image image)
            {
                // The fill IS the progress bar: the background walks to red as the hold completes,
                // so the operator sees how much of the wipe is left without a second element.
                image.color = holding
                    ? Color.Lerp(UiKit.Hex(0x2A303B, 0xFF), UiKit.Bad, progress)
                    : UiKit.Hex(0x2A303B, 0xFF);
            }
        }

        private void RefreshKickButton()
        {
            bool armed = _kickArmedAt >= 0f;

            if (kickLabel != null)
            {
                kickLabel.text = armed ? LabelConfirm : "AT";
                kickLabel.color = armed ? UiKit.OnAccent : UiKit.Muted;
            }

            if (kickButton != null && kickButton.targetGraphic is Image image)
            {
                image.color = armed ? UiKit.Bad : UiKit.Hex(0x2A303B, 0xFF);
            }
        }

        private static string BuildStatsLine(AdminPlayerView view)
        {
            string battery = FormatBattery(view);
            string controllers = FormatControllers(view);
            string state = BuildState(view);

            // Drop the controller token when it carries nothing (both hands unreported): narrow
            // card space goes to state, not to an unread column.
            return string.IsNullOrEmpty(controllers)
                ? $"{view.kills}/{view.deaths} · {battery} · {state}"
                : $"{view.kills}/{view.deaths} · {battery} · {controllers} · {state}";
        }

        /// <summary>
        /// HEADSET battery text, rich-text coloured below the thresholds. <c>-</c> = unknown
        /// (<c>battery &lt; 0</c>) and stays uncoloured: alarming on an unknown value sends the
        /// operator running for nothing.
        /// <para>Shared with the stats panel — split thresholds would show the same battery red on
        /// one screen and yellow on the other.</para>
        /// </summary>
        internal static string FormatBattery(AdminPlayerView view)
        {
            if (view.battery < 0f)
            {
                return "-";
            }

            float level = Mathf.Clamp01(view.battery);
            string text = $"%{Mathf.RoundToInt(level * 100f)}";

            return level < BatteryCritical ? Colored(text, UiKit.Bad)
                : level < BatteryLow ? Colored(text, UiKit.Accent)
                : text;
        }

        /// <summary>
        /// Both controllers as one token (<c>K:</c> + two glyphs), each glyph coloured. Empty when
        /// neither hand is reported (see <see cref="BuildStatsLine"/>).
        /// <para>⚠️ No percentage possible: controller charge is unreadable under OpenXR on Quest
        /// (§5.1). The actionable fact is "did this hand drop".</para>
        /// </summary>
        internal static string FormatControllers(AdminPlayerView view)
        {
            if (view.ctrlL == ArenaProtocol.CONTROLLER_UNKNOWN &&
                view.ctrlR == ArenaProtocol.CONTROLLER_UNKNOWN)
            {
                return "";
            }

            return $"K:{ControllerGlyph(view.ctrlL)}{ControllerGlyph(view.ctrlR)}";
        }

        private static string ControllerGlyph(int state)
        {
            switch (state)
            {
                case ArenaProtocol.CONTROLLER_OK:
                    return Colored(GlyphControllerOk, UiKit.Muted);
                case ArenaProtocol.CONTROLLER_UNTRACKED:
                    return Colored(GlyphControllerUntracked, UiKit.Accent);
                case ArenaProtocol.CONTROLLER_LOST:
                    return Colored(GlyphControllerLost, UiKit.Bad);
                default:
                    return Colored(GlyphControllerUnknown, UiKit.Faint);
            }
        }

        /// <summary>Colours one token with TMP rich text.
        /// <para>⚠️ Tag is <c>RGB</c>, not <c>RGBA</c>: TMP forces the coloured range fully opaque
        /// anyway, so a per-token alpha only risks invisible text.</para></summary>
        private static string Colored(string text, Color color)
        {
            return $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{text}</color>";
        }

        /// <summary>Row state text. ⚠️ There is no "offline" state (§2): a dropped device is either
        /// expected back (with a countdown) or out of the game.
        /// <para>For a live player a VIOLATION outranks "HAZIR"/"bekliyor" (§10.9): one state slot,
        /// and the violation is the actionable one. Written only on the refresh tick — the live
        /// channel is the border (<see cref="Update"/>), this text names the kind.</para>
        /// <para>⚠️ Hidden on dead/dropped rows: the penalty has stopped, nothing to act on.</para>
        /// </summary>
        private static string BuildState(AdminPlayerView view)
        {
            if (view.IsReconnecting)
            {
                return $"yeniden bağlanıyor · {view.ReconnectSecondsLeft} sn";
            }

            if (view.HasLeft)
            {
                return "ayrıldı";
            }

            if (!view.alive)
            {
                float remaining = view.RespawnRemaining;
                return remaining > 0.1f
                    ? $"ÖLÜ {Mathf.CeilToInt(remaining)} sn"
                    : "TABANDA BEKLENİYOR";
            }

            string violation = AdminViolations.Label(AdminViolations.Of(view.playerId));
            if (!string.IsNullOrEmpty(violation))
            {
                return violation;
            }

            return view.ready ? "HAZIR" : "bekliyor";
        }
    }
}
