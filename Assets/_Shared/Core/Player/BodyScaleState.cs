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
    /// The scale is the ratio of the player's eye height to the character's <b>current</b> eye height;
    /// both are read in arena space, in the <b>same frame</b>. Since the character is already driven
    /// from body tracking it is in the same pose as the player — so the posture difference cancels out
    /// of the ratio by itself and there is no need to keep a fixed "model eye height" number (that
    /// number would go silently stale whenever the model changed).
    /// </para>
    /// <para>
    /// ⚠️ <b>The measurement is not triggered by TIME but by the operator</b>
    /// (<c>measure_body_scale</c>). A machine cannot know the right moment to measure — the moment the
    /// player stands upright; a measurement triggered automatically from calibration would measure
    /// while the player is <b>bent over</b> to touch the controller to the floor.
    /// </para>
    /// <para>
    /// ⚠️ <b>The scale is not applied to the LOCAL character</b> (only to remote avatars, §10.8). The
    /// repeatability of the measurement depends on this: if the local character were scaled too, a
    /// second measurement would read an already-scaled reference and drag the multiplier toward
    /// <c>1</c>.
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

        /// <summary>The eye height (m) below which the measurement is considered meaningless — it means
        /// the player or the character is on the floor/crouching.</summary>
        private const float MinEyeHeightMeters = 0.8f;

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

            if (TrySampleRatio(out float ratio) && _sampleCount < _samples.Length)
            {
                _samples[_sampleCount++] = ratio;
            }

            if (Time.unscaledTime < _sampleDeadline)
            {
                return;
            }

            _sampling = false;
            FinishMeasurement();
        }

        /// <summary>
        /// This frame's ratio: the player's eye / the character's eye, both in arena space (floor
        /// y=0). <c>false</c> if it cannot be measured — that frame is silently skipped and the total
        /// sample count is checked at the end of the window.
        /// </summary>
        private bool TrySampleRatio(out float ratio)
        {
            ratio = 0f;

            OVRCameraRig rig = ResolveRig();
            LocalBodyAvatar body = LocalBodyAvatar.Instance;
            if (rig == null || rig.centerEyeAnchor == null || body == null)
            {
                return false;
            }

            Transform avatarEye = body.EyeAnchor;
            if (avatarEye == null || !body.IsBodyPoseValid)
            {
                return false;
            }

            float playerEyeY = ArenaSpace.WorldToArena(rig.centerEyeAnchor.position).y;
            float avatarEyeY = ArenaSpace.WorldToArena(avatarEye.position).y;
            if (playerEyeY < MinEyeHeightMeters || avatarEyeY < MinEyeHeightMeters)
            {
                return false;
            }

            ratio = playerEyeY / avatarEyeY;
            return true;
        }

        /// <summary>
        /// The window is full: take the median, check the spread, clamp and report.
        /// <para>⚠️ On a failed measurement <b>no scale is sent but the REASON is</b> (§10.8): the old
        /// scale stays, and the cause is both written to the console and delivered to the operator via
        /// <c>set_body_scale.error</c>. Writing a wrong height is worse than writing none — only other
        /// people see the result; but staying silent is bad too, the operator who asked for the
        /// measurement would think the button does not work.</para>
        /// </summary>
        private void FinishMeasurement()
        {
            if (_sampleCount == 0)
            {
                Debug.LogWarning(
                    "[BodyScaleState] Gövde ölçülemedi: gövde pozu yok ya da göz işaretçisi bağlı " +
                    "değil. Resources/LocalBodyAvatar.prefab içindeki 'Eye Anchor' alanı kafa " +
                    "kemiğinin altındaki işaretçiyi göstermeli.", this);
                ReportError("gövde pozu yok");
                return;
            }

            Array.Sort(_samples, 0, _sampleCount);
            float median = _samples[_sampleCount / 2];
            float spread = _samples[_sampleCount - 1] - _samples[0];

            if (median <= 0f || spread / median > MaxSpreadRatio)
            {
                Debug.LogWarning(
                    $"[BodyScaleState] Ölçüm reddedildi: {_sampleCount} örnekte yayılım " +
                    $"%{spread / Mathf.Max(median, 0.0001f) * 100f:F1} (tavan " +
                    $"%{MaxSpreadRatio * 100f:F0}). Oyuncu ölçüm anında hareketli ya da eğilmiş — " +
                    "dik dururken tekrar ölçün.", this);
                ReportError("oyuncu hareketli/eğilmiş");
                return;
            }

            float scale = Mathf.Clamp(median, ArenaProtocol.BODY_SCALE_MIN, ArenaProtocol.BODY_SCALE_MAX);
            if (!Mathf.Approximately(scale, median))
            {
                Debug.LogWarning(
                    $"[BodyScaleState] Ölçülen {median:F3} protokol aralığının " +
                    $"({ArenaProtocol.BODY_SCALE_MIN:F2}–{ArenaProtocol.BODY_SCALE_MAX:F2}) dışında, " +
                    $"{scale:F3} olarak kırpıldı — avatar oyuncunun boyuna tam yetişmeyecek.", this);
            }

            SetLocalScale(scale);
            Report(scale);
            Debug.Log($"[BodyScaleState] Gövde ölçeği {scale:F3} ({_sampleCount} örnek).", this);
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
