using System;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.Combat;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Takes the local player's <b>body measurement</b> and reports it to the server (§10.8).
    /// <para>
    /// The scale is the player's eye height above the <b>arena</b> floor over the character model's
    /// eye height in its <b>rest pose</b> (<see cref="LocalBodyAvatar.RestEyeHeightMeters"/>, read
    /// once from the prefab at scale 1). Body tracking is not consulted, so the measurement works
    /// while tracking is broken and does not depend on the headset's own floor guess.
    /// </para>
    /// <para>
    /// ⚠️ <b>Never the character's LIVE eye height.</b> The retargeter drives the character's head to
    /// the tracked head, so player-eye over character-eye is <c>1</c> by construction — every player,
    /// whatever their height, measures <c>0.99…1.01</c>.
    /// </para>
    /// <para>
    /// ⚠️ <b>The measurement is not triggered by TIME but by the operator</b>
    /// (<c>measure_body_scale</c>). A machine cannot know the right moment to measure — the moment the
    /// player stands upright; a measurement triggered automatically from calibration would measure
    /// while the player is <b>bent over</b> to touch the controller to the floor. A steady stoop
    /// passes the spread check, so the learned standing height gates it as well.
    /// </para>
    /// <para>
    /// It does NOT live in the scene: it bootstraps itself as a persistent singleton (the
    /// <see cref="CalibrationState"/> pattern) — so that no manual setup step has to be added to every
    /// arena. With no rig (admin observer) it does nothing.
    /// </para>
    /// </summary>
    public class BodyScaleState : MonoBehaviour
    {
        /// <summary>The key the scale is stored under on the device — so a reconnecting player does not
        /// have to be measured again.</summary>
        private const string ScalePrefsKey = "VortexArena.BodyScale";

        /// <summary>Sampling window (s). A single frame would write the SDK's solver noise into the
        /// measurement.</summary>
        private const float SampleSeconds = 0.5f;

        /// <summary>
        /// The largest accepted spread between samples (as a ratio of the median). If exceeded the
        /// measurement is <b>rejected</b>: it means the player was moving or bending at that moment.
        /// <para>Because a ratio is measured (numerator and denominator move together) this threshold
        /// catches real posture changes, not the normal sway of the head.</para>
        /// </summary>
        private const float MaxSpreadRatio = 0.05f;

        /// <summary>The eye height (m) below which the measurement is considered meaningless — the
        /// player is on the floor/crouching.</summary>
        private const float MinEyeHeightMeters = 0.8f;

        /// <summary>The largest dip below the learned standing eye height still accepted (ratio). The
        /// operator presses while the player stands, but a player holding a stoop steadily passes the
        /// spread check — this catches them.</summary>
        private const float MaxStoopRatio = 0.06f;

        /// <summary>The smallest difference required to count a scale change as "a new value".</summary>
        private const float ScaleEpsilon = 0.0005f;

        public static BodyScaleState Instance { get; private set; }

        /// <summary>The scale the server knows about (<c>lobby_state</c>); <c>0</c> = unmeasured.</summary>
        public static float ServerScale { get; private set; }

        /// <summary>Raised when the state changes (main thread).</summary>
        public static event Action Changed;

        // A single instance so that no new DTO is allocated on every report (the CalibrationState pattern).
        private readonly SetBodyScaleMsg _reportMsg = new SetBodyScaleMsg();

        /// <summary>The scale measured on this device; 0 = none. This value is reported on reconnect.</summary>
        private float _localScale;

        private OVRCameraRig _rig;
        private float _rigSearchTime = float.NegativeInfinity;
        private const float RigSearchIntervalSeconds = 0.5f;

        // ── Measurement window ──────────────────────────────────────────────────────────
        private bool _sampling;
        private float _sampleDeadline;

        // Samples are kept in a field: even though measuring happens at human speed, so that nothing is
        // allocated per frame.
        private readonly float[] _samples = new float[128];
        private int _sampleCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var go = new GameObject("[BodyScaleState]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<BodyScaleState>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            _localScale = PlayerPrefs.GetFloat(ScalePrefsKey, 0f);

            // We are a persistent singleton: we subscribe in Awake/OnDestroy instead of
            // OnEnable/OnDisable so that no event is missed even if the object is disabled.
            NetEvents.OnMeasureBodyScale += HandleMeasureRequest;
            NetEvents.OnConnected += HandleConnected;
            NetEvents.OnLobbyState += HandleLobbyState;
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            NetEvents.OnMeasureBodyScale -= HandleMeasureRequest;
            NetEvents.OnConnected -= HandleConnected;
            NetEvents.OnLobbyState -= HandleLobbyState;

            Instance = null;
        }

        // ------------------------------------------------------------- measurement

        /// <summary>The operator requested a measurement → opens the sampling window.</summary>
        private void HandleMeasureRequest()
        {
            _sampling = true;
            _sampleCount = 0;
            _sampleDeadline = Time.unscaledTime + SampleSeconds;
        }

        private void Update()
        {
            if (!_sampling)
            {
                return;
            }

            if (TrySampleEyeHeight(out float eyeHeight) && _sampleCount < _samples.Length)
            {
                _samples[_sampleCount++] = eyeHeight;
            }

            if (Time.unscaledTime < _sampleDeadline)
            {
                return;
            }

            _sampling = false;
            FinishMeasurement();
        }

        /// <summary>
        /// This frame's player eye height above the arena floor (m). <c>false</c> if it cannot be
        /// measured — that frame is silently skipped and the total sample count is checked at the end
        /// of the window.
        /// <para>⚠️ Arena space needs a calibration: without one "floor" is the headset's own guess,
        /// which is exactly the number the stale-space-data fault corrupts.</para>
        /// </summary>
        private bool TrySampleEyeHeight(out float eyeHeight)
        {
            eyeHeight = 0f;

            OVRCameraRig rig = ResolveRig();
            if (rig == null || rig.centerEyeAnchor == null || !CalibrationState.IsCalibrated)
            {
                return false;
            }

            float playerEyeY = ArenaSpace.WorldToArena(rig.centerEyeAnchor.position).y;
            if (playerEyeY < MinEyeHeightMeters)
            {
                return false;
            }

            eyeHeight = playerEyeY;
            return true;
        }

        /// <summary>Why no sample could be taken — the reason the operator sees. Only meaningful right
        /// after an empty window.</summary>
        private string DescribeSampleFailure()
        {
            LocalBodyAvatar body = LocalBodyAvatar.Instance;
            if (body == null || body.RestEyeHeightMeters <= 0f)
            {
                return "model göz referansı yok";
            }

            if (!CalibrationState.IsCalibrated)
            {
                return "kalibre yok";
            }

            return "göz hizası okunamadı";
        }

        /// <summary>
        /// The window is full: take the median, check spread and posture, divide by the model's rest
        /// eye height, clamp and report.
        /// <para>⚠️ On a failed measurement <b>no scale is sent but the REASON is</b> (§10.8): the old
        /// scale stays, and the cause is both written to the console and delivered to the operator via
        /// <c>set_body_scale.error</c>. Writing a wrong height is worse than writing none — only other
        /// people see the result; but staying silent is bad too, the operator who asked for the
        /// measurement would think the button does not work.</para>
        /// </summary>
        private void FinishMeasurement()
        {
            LocalBodyAvatar body = LocalBodyAvatar.Instance;
            float restEye = body != null ? body.RestEyeHeightMeters : 0f;

            if (_sampleCount == 0 || restEye <= 0f)
            {
                string reason = DescribeSampleFailure();
                Debug.LogWarning(
                    $"[BodyScaleState] Gövde ölçülemedi: {reason}. Model referansı için " +
                    "Resources/LocalBodyAvatar.prefab içindeki 'Eye Anchor' alanı kafa kemiğinin " +
                    "altındaki işaretçiyi göstermeli; kalibre için oyuncu önce hizalanmalı.", this);
                ReportError(reason);
                return;
            }

            Array.Sort(_samples, 0, _sampleCount);
            float medianEye = _samples[_sampleCount / 2];
            float spread = _samples[_sampleCount - 1] - _samples[0];

            if (medianEye <= 0f || spread / medianEye > MaxSpreadRatio)
            {
                Debug.LogWarning(
                    $"[BodyScaleState] Ölçüm reddedildi: {_sampleCount} örnekte yayılım " +
                    $"%{spread / Mathf.Max(medianEye, 0.0001f) * 100f:F1} (tavan " +
                    $"%{MaxSpreadRatio * 100f:F0}). Oyuncu ölçüm anında hareketli ya da eğilmiş — " +
                    "dik dururken tekrar ölçün.", this);
                ReportError("oyuncu hareketli/eğilmiş");
                return;
            }

            // A held stoop is steady enough to pass the spread check; the learned standing height is
            // the only reference that knows how tall this player stands.
            if (StandingHeightState.TryGet(out float standingEye) &&
                medianEye < standingEye * (1f - MaxStoopRatio))
            {
                Debug.LogWarning(
                    $"[BodyScaleState] Ölçüm reddedildi: göz {medianEye:F2} m, oyuncunun ayakta göz " +
                    $"hizası {standingEye:F2} m — oyuncu eğilmiş. Dik dururken tekrar ölçün.", this);
                ReportError("oyuncu eğilmiş");
                return;
            }

            float ratio = medianEye / restEye;
            float scale = Mathf.Clamp(ratio, ArenaProtocol.BODY_SCALE_MIN, ArenaProtocol.BODY_SCALE_MAX);
            if (!Mathf.Approximately(scale, ratio))
            {
                Debug.LogWarning(
                    $"[BodyScaleState] Ölçülen {ratio:F3} protokol aralığının " +
                    $"({ArenaProtocol.BODY_SCALE_MIN:F2}–{ArenaProtocol.BODY_SCALE_MAX:F2}) dışında, " +
                    $"{scale:F3} olarak kırpıldı — avatar oyuncunun boyuna tam yetişmeyecek.", this);
            }

            SetLocalScale(scale);
            Report(scale);
            Debug.Log($"[BodyScaleState] Gövde ölçeği {scale:F3} (göz {medianEye:F2} m / model " +
                      $"{restEye:F2} m, {_sampleCount} örnek).", this);
        }

        // ------------------------------------------------------------- headset → server

        private void Report(float scale)
        {
            ArenaClient client = ArenaClient.Instance;
            if (client == null || !client.IsConnected)
            {
                return; // serverless session: there is no one to report to
            }

            _reportMsg.scale = scale;
            // ⚠️ The DTO is a single instance: a stale error field would contaminate the next
            // SUCCESSFUL measurement (if it is non-empty the server ignores the scale, §10.8).
            _reportMsg.error = "";
            client.Send(_reportMsg);
        }

        /// <summary>
        /// The measurement failed: the REASON is sent instead of the scale (§10.8). <c>scale = 0</c>
        /// plus a non-empty <c>error</c> tells the server "do not change the stored scale, show the
        /// reason to the operator".
        /// <para>The clamping branch does NOT come here — a clamped measurement is a success and its
        /// value is written.</para>
        /// </summary>
        private void ReportError(string reason)
        {
            ArenaClient client = ArenaClient.Instance;
            if (client == null || !client.IsConnected)
            {
                return; // serverless session: there is no one to report to
            }

            _reportMsg.scale = 0f;
            _reportMsg.error = reason;
            client.Send(_reportMsg);
        }

        /// <summary>Writes the local record (persisted on the device). <c>0</c> = no measurement.</summary>
        private void SetLocalScale(float scale)
        {
            _localScale = scale;
            if (scale > 0f)
            {
                PlayerPrefs.SetFloat(ScalePrefsKey, scale);
            }
            else
            {
                PlayerPrefs.DeleteKey(ScalePrefsKey);
            }

            PlayerPrefs.Save();
        }

        // ------------------------------------------------------------- server → headset

        /// <summary>
        /// The server resets the scale on every <c>hello</c> (§10.8, the same rationale as for
        /// calibration) — if a measurement is stored on the device it is immediately re-reported, so
        /// the operator does not have to measure again.
        /// </summary>
        private void HandleConnected(WelcomeMsg msg)
        {
            ServerScale = 0f;
            if (_localScale > 0f)
            {
                Report(_localScale);
            }

            Raise();
        }

        /// <summary>
        /// Our own row in the roster is the single source of truth for the scale (§5.3).
        /// <para>⚠️ If the server published <c>0</c>, <b>the local record is deleted too</b>: when the
        /// operator resets the calibration the scale drops as well, and if it were not deleted the
        /// player would bring it back by themselves on the next connection — the reset would have been
        /// silently undone.</para>
        /// </summary>
        private void HandleLobbyState(LobbyStateMsg msg)
        {
            int selfId = PlayerCombatState.Instance != null ? PlayerCombatState.Instance.PlayerId : 0;
            if (msg == null || msg.players == null || selfId == 0)
            {
                return;
            }

            for (int i = 0; i < msg.players.Length; i++)
            {
                PlayerInfo info = msg.players[i];
                if (info == null || info.playerId != selfId)
                {
                    continue;
                }

                ApplyServerState(info.bodyScale);
                return;
            }
        }

        private void ApplyServerState(float scale)
        {
            if (scale <= 0f && _localScale > 0f)
            {
                SetLocalScale(0f);
                Debug.Log("[BodyScaleState] Sunucu gövde ölçeğini sıfırladı — yeniden ölçüm gerekiyor.");
            }

            if (Mathf.Abs(ServerScale - scale) < ScaleEpsilon)
            {
                return;
            }

            ServerScale = scale;
            Raise();
        }

        private static void Raise()
        {
            Changed?.Invoke();
        }

        /// <summary>Finds the active rig; the search is throttled (on an admin observer the rig never
        /// arrives — the same rationale as <see cref="LocalBodyAvatar.ResolveRig"/>).</summary>
        private OVRCameraRig ResolveRig()
        {
            if (_rig != null && _rig.isActiveAndEnabled)
            {
                return _rig;
            }

            if (Time.unscaledTime - _rigSearchTime < RigSearchIntervalSeconds)
            {
                return null;
            }

            _rigSearchTime = Time.unscaledTime;
            _rig = FindFirstObjectByType<OVRCameraRig>();
            return _rig;
        }
    }
}
