using System;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Takes the local player's <b>body measurement</b> and reports it to the server (§10.8).
    /// <para>
    /// The measurement is the headset's eye height above the <b>arena</b> floor AT THAT MOMENT: plus
    /// <see cref="HeadTopAboveEyeMeters"/> it is taken as the player's stature and clamped to
    /// <see cref="MinStatureMeters"/>–<see cref="MaxStatureMeters"/>. The scale is that eye height over
    /// the character model's eye height in its <b>rest pose</b>
    /// (<see cref="LocalBodyAvatar.RestEyeHeightMeters"/>, read once from the prefab at scale 1). Body
    /// tracking is not consulted, so the measurement works while tracking is broken and does not depend
    /// on the headset's own floor guess.
    /// </para>
    /// <para>
    /// ⚠️ <b>No posture gate.</b> A stoop/motion heuristic rejected nearly every measurement in the field
    /// and children are handed the headset while standing — whoever triggers the measurement is the judge
    /// of the moment; a wrong value is corrected by measuring again.
    /// </para>
    /// <para>
    /// <b>Three triggers, one path:</b> the operator (<c>measure_body_scale</c>) at any time; automatically
    /// <see cref="AutoMeasureDelaySeconds"/> after every alignment (the player has stood up by then; a
    /// manual measurement cancels a pending automatic one); and, until the first measurement, the
    /// <see cref="DefaultStatureMeters"/> assumption reported on connect.
    /// </para>
    /// <para>
    /// ⚠️ <b>Never the character's LIVE eye height.</b> The retargeter drives the character's head to
    /// the tracked head, so player-eye over character-eye is <c>1</c> by construction — every player,
    /// whatever their height, measures <c>0.99…1.01</c>.
    /// </para>
    /// <para>
    /// ⚠️ <b>Not stored on the device.</b> A venue headset passes from hand to hand; every app start
    /// begins with the default stature. Within one process the last measurement is re-reported on
    /// reconnect, so a network blip does not cost a measurement.
    /// </para>
    /// <para>
    /// It does NOT live in the scene: it bootstraps itself as a persistent singleton (the
    /// <see cref="CalibrationState"/> pattern) — so that no manual setup step has to be added to every
    /// arena. With no rig (admin observer) it does nothing.
    /// </para>
    /// </summary>
    public class BodyScaleState : MonoBehaviour
    {
        /// <summary>Top of the head above the HMD's eye anchor (m). Approximate on purpose: it turns an
        /// eye height into the stature the clamp and <c>OVRBody</c>'s calibration hint want.</summary>
        public const float HeadTopAboveEyeMeters = 0.11f;

        /// <summary>Stature assumed until the first measurement (m).</summary>
        public const float DefaultStatureMeters = 1.80f;

        /// <summary>Smallest stature a measurement can yield (m) — a child; anything lower is taken as
        /// this. Doubles as the guard against a headset measured off a head.</summary>
        public const float MinStatureMeters = 1.00f;

        /// <summary>Largest stature a measurement can yield (m) — a headset held overhead is taken as
        /// this.</summary>
        public const float MaxStatureMeters = 2.20f;

        /// <summary>Delay from an alignment to the automatic measurement (s). The player is bent over to
        /// touch the controller to the floor at the alignment itself; this is the time to stand up.</summary>
        private const float AutoMeasureDelaySeconds = 10f;

        /// <summary>Sampling window (s). A single frame would write the SDK's solver noise into the
        /// measurement; the window's median is taken.</summary>
        private const float SampleSeconds = 0.5f;

        /// <summary>The smallest difference required to count a scale change as "a new value".</summary>
        private const float ScaleEpsilon = 0.0005f;

        public static BodyScaleState Instance { get; private set; }

        /// <summary>The scale the server knows about (<c>lobby_state</c>); <c>0</c> = unmeasured.</summary>
        public static float ServerScale { get; private set; }

        /// <summary>The player's stature as currently known (m): the last measurement, else
        /// <see cref="DefaultStatureMeters"/>. The body-tracking calibration hint reads this.</summary>
        public static float StatureMeters =>
            Instance != null && Instance._measuredStature > 0f ? Instance._measuredStature : DefaultStatureMeters;

        /// <summary>Raised when the state changes (main thread).</summary>
        public static event Action Changed;

        // A single instance so that no new DTO is allocated on every report (the CalibrationState pattern).
        private readonly SetBodyScaleMsg _reportMsg = new SetBodyScaleMsg();

        /// <summary>Last measurement in this process; 0 = none (the default stature stands in).</summary>
        private float _measuredScale;

        /// <inheritdoc cref="_measuredScale"/>
        private float _measuredStature;

        private OVRCameraRig _rig;
        private float _rigSearchTime = float.NegativeInfinity;
        private const float RigSearchIntervalSeconds = 0.5f;

        // ── Automatic measurement after alignment ──────────────────────────────────────
        private int _seenGeneration;
        private bool _autoPending;
        private float _autoMeasureAt;

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
            _seenGeneration = ArenaCalibrator.CalibrationGeneration;

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

        /// <summary>The operator requested a measurement: their judgement of the moment replaces the
        /// pending automatic one.</summary>
        private void HandleMeasureRequest()
        {
            _autoPending = false;
            BeginMeasurement();
        }

        private void BeginMeasurement()
        {
            _sampling = true;
            _sampleCount = 0;
            _sampleDeadline = Time.unscaledTime + SampleSeconds;
        }

        private void Update()
        {
            TickAutoMeasure();

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

        /// <summary>Every alignment (generation bump) schedules a measurement
        /// <see cref="AutoMeasureDelaySeconds"/> later; a newer alignment restarts the wait.
        /// <para>⚠️ Fires only on a player: an admin observer has no rig, and a failed measurement from
        /// it would be announced to every operator as a player's error.</para></summary>
        private void TickAutoMeasure()
        {
            int generation = ArenaCalibrator.CalibrationGeneration;
            if (generation != _seenGeneration)
            {
                _seenGeneration = generation;
                _autoPending = true;
                _autoMeasureAt = Time.unscaledTime + AutoMeasureDelaySeconds;
            }

            if (!_autoPending || Time.unscaledTime < _autoMeasureAt)
            {
                return;
            }

            _autoPending = false;

            // The alignment was not acknowledged (or no rig): there is no arena floor to measure against.
            if (!CalibrationState.IsCalibrated || ResolveRig() == null)
            {
                return;
            }

            Debug.Log($"[BodyScaleState] Hizalamadan {AutoMeasureDelaySeconds:F0} sn geçti — boy " +
                      "kendiliğinden ölçülüyor.", this);
            BeginMeasurement();
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

            eyeHeight = ArenaSpace.WorldToArena(rig.centerEyeAnchor.position).y;
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
        /// The window is full: take the median eye height, clamp the stature it implies, divide by the
        /// model's rest eye height and report.
        /// <para>⚠️ On a failed measurement <b>no scale is sent but the REASON is</b> (§10.8): the old
        /// scale stays, and the cause is both written to the console and delivered to the operator via
        /// <c>set_body_scale.error</c>. Staying silent would make the operator think the button does not
        /// work.</para>
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

            float stature = medianEye + HeadTopAboveEyeMeters;
            float clampedStature = Mathf.Clamp(stature, MinStatureMeters, MaxStatureMeters);
            if (!Mathf.Approximately(stature, clampedStature))
            {
                Debug.LogWarning(
                    $"[BodyScaleState] Ölçülen boy {stature:F2} m sınırların " +
                    $"({MinStatureMeters:F2}–{MaxStatureMeters:F2} m) dışında, {clampedStature:F2} m " +
                    "sayıldı — gözlük o an bir kafada değil miydi?", this);
            }

            float ratio = (clampedStature - HeadTopAboveEyeMeters) / restEye;
            float scale = Mathf.Clamp(ratio, ArenaProtocol.BODY_SCALE_MIN, ArenaProtocol.BODY_SCALE_MAX);
            if (!Mathf.Approximately(scale, ratio))
            {
                Debug.LogWarning(
                    $"[BodyScaleState] Ölçülen {ratio:F3} protokol aralığının " +
                    $"({ArenaProtocol.BODY_SCALE_MIN:F2}–{ArenaProtocol.BODY_SCALE_MAX:F2}) dışında, " +
                    $"{scale:F3} olarak kırpıldı — avatar oyuncunun boyuna tam yetişmeyecek.", this);
            }

            _measuredStature = clampedStature;
            _measuredScale = scale;
            Report(scale);
            Debug.Log($"[BodyScaleState] Gövde ölçeği {scale:F3} (boy {clampedStature:F2} m — göz " +
                      $"{medianEye:F2} m / model {restEye:F2} m, {_sampleCount} örnek).", this);

            // The stature is also what body tracking's own calibration wants to know.
            if (body != null)
            {
                body.SeedHeightHint();
            }
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
        /// <para>The clamping branches do NOT come here — a clamped measurement is a success and its
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

        /// <summary>What the server should know right after connecting: the last measurement of this
        /// process, else the default stature (§10.8) — so nobody is drawn at the model's height for want
        /// of a button press. Skipped without a rig (admin observer) or without the model reference.</summary>
        private void ReportKnownScale()
        {
            if (_measuredScale > 0f)
            {
                Report(_measuredScale);
                return;
            }

            if (ResolveRig() == null)
            {
                return;
            }

            LocalBodyAvatar body = LocalBodyAvatar.Instance;
            float restEye = body != null ? body.RestEyeHeightMeters : 0f;
            if (restEye <= 0f)
            {
                return;
            }

            float scale = Mathf.Clamp((DefaultStatureMeters - HeadTopAboveEyeMeters) / restEye,
                                      ArenaProtocol.BODY_SCALE_MIN, ArenaProtocol.BODY_SCALE_MAX);
            Report(scale);
            Debug.Log($"[BodyScaleState] Henüz ölçüm yok — varsayılan boy {DefaultStatureMeters:F2} m " +
                      $"bildirildi (ölçek {scale:F3}).", this);
        }

        // ------------------------------------------------------------- server → headset

        /// <summary>
        /// The server resets the scale on every <c>hello</c> (§10.8, the same rationale as for
        /// calibration) — the known scale is re-reported at once, so nobody waits for a measurement.
        /// </summary>
        private void HandleConnected(WelcomeMsg msg)
        {
            ServerScale = 0f;
            ReportKnownScale();
            Raise();
        }

        /// <summary>
        /// Our own row in the roster is the single source of truth for the scale (§5.3).
        /// <para>⚠️ If the server published <c>0</c>, <b>the local measurement is dropped too</b>: when
        /// the operator resets the calibration the scale drops as well, and if it were kept the player
        /// would bring it back by themselves on the next connection — the reset would have been
        /// silently undone. The next alignment measures again by itself.</para>
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
            if (scale <= 0f && _measuredScale > 0f)
            {
                _measuredScale = 0f;
                _measuredStature = 0f;
                Debug.Log("[BodyScaleState] Sunucu gövde ölçeğini sıfırladı — bir sonraki hizalamadan " +
                          $"{AutoMeasureDelaySeconds:F0} sn sonra yeniden ölçülecek.");
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
