using System;
using UnityEngine;
using VortexArena.Core.Combat;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Core.Arena
{
    /// <summary>Local calibration state — the two-way bridge between server and headset (§10.6).</summary>
    /// <remarks>
    /// The server is authoritative: "am I calibrated" is answered by <c>lobby_state</c>, NOT by the
    /// scene <see cref="ArenaCalibrator"/>'s own counter — the operator can clear while the headset
    /// still believes it is aligned.
    /// <para>
    /// Two directions: (1) <see cref="ArenaCalibrator.Calibrated"/> → <c>set_calibration</c>;
    /// (2) <c>clear_calibration</c> → <see cref="ArenaCalibrator.ApplyOperatorClear"/>. The command's
    /// <c>keepSaved</c> splits soft mode (device anchor KEPT, so a following
    /// <c>reload_calibration</c> has something to load) from hard mode. In soft mode what stops the
    /// alignment coming back silently is the auto-restore gate in <c>ArenaCalibrator</c>, not
    /// erasure. The command is unconditional — it also drops a half-finished sequence (§5.3).
    /// </para>
    /// <para>
    /// ⚠️ That command is the ONLY clear gate. <c>calibrated:false</c> in the roster is NOT a clear
    /// signal: the server resets that field on every <c>hello</c> (§10.6), so it is published once
    /// on every reconnect — clearing on it would make a briefly disconnected headset lose its saved
    /// anchor. Here the roster is only a MIRROR (<see cref="ApplyServerState"/>).
    /// </para>
    /// <para>
    /// ⚠️ With no connection ever made the gate is OPEN (<see cref="IsCalibrated"/> and
    /// <see cref="ManualAllowed"/> both true) so weapons and manual calibration keep working in
    /// server-less editor tests — same rationale as <c>PlayerCombatState.CanFire</c>.
    /// </para>
    /// <para>
    /// Not placed in the scene: self-bootstrapping persistent singleton
    /// (<see cref="PlayerCombatState"/> pattern), so no arena gains a manual setup step.
    /// </para>
    /// </remarks>
    public class CalibrationState : MonoBehaviour
    {
        public static CalibrationState Instance { get; private set; }

        private static bool _hasEverConnected;
        private static bool _serverCalibrated;
        private static bool _localCalibrated;
        private static string _source = "";

        /// <summary>Alignment state as the server knows it; uncalibrated players cannot fire, take
        /// damage or revive (§10.6 — all three server-authoritative, this is only the mirror). True
        /// when never connected, so server-less tests keep working.</summary>
        public static bool IsCalibrated => !_hasEverConnected || _serverCalibrated;

        /// <summary>Is the MANUAL controller gesture (hold A, double-tap B) open. Closed while
        /// calibrated so the player cannot break their own alignment by accident — only the operator
        /// reopens it (§10.6).</summary>
        public static bool ManualAllowed => !_hasEverConnected || !_serverCalibrated;

        /// <summary>Last reported source ("manual" | "anchor" | "cloud" | "").</summary>
        public static string Source => _source;

        /// <summary>The server's calibration mode (<c>ArenaProtocol.CALIB_MODE_*</c>, §10.6); empty =
        /// no <c>welcome</c> received.
        /// <para>⚠️ NOT reset on disconnect: the mode is an operator decision, not session state. A
        /// new <c>welcome</c> overwrites it.</para></summary>
        public static string Mode { get; private set; } = "";

        /// <summary>May the headset restore the alignment from the on-disk
        /// <c>OVRSpatialAnchor</c> UUID AT LAUNCH (§10.6).
        /// <para>Empty mode = no <c>welcome</c> yet (server-less sandbox) → allowed for convenience.
        /// <c>two_anchor</c> closes only the DISK restore; the in-session (in-memory anchor) restore
        /// is mode-INDEPENDENT — map changes ride on it, and closing that gate would force a
        /// recalibration on every arena switch.</para></summary>
        public static bool DiskRestoreAllowed =>
            string.IsNullOrEmpty(Mode) || Mode == ArenaProtocol.CALIB_MODE_SAVED_ANCHOR;

        /// <summary>Raised on state change (main thread).</summary>
        public static event Action Changed;

        // Single instance so no DTO is allocated per report.
        private readonly SetCalibrationMsg _reportMsg = new SetCalibrationMsg();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var go = new GameObject("[CalibrationState]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<CalibrationState>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // Persistent singleton: subscribe in Awake/OnDestroy rather than OnEnable/OnDisable so
            // events are not missed if the object is deactivated.
            ArenaCalibrator.Calibrated += HandleLocalCalibrated;
            NetEvents.OnConnected += HandleConnected;
            NetEvents.OnLobbyState += HandleLobbyState;
            NetEvents.OnClearCalibration += HandleClearCalibration;
            NetEvents.OnReloadCalibration += HandleReloadCalibration;
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            ArenaCalibrator.Calibrated -= HandleLocalCalibrated;
            NetEvents.OnConnected -= HandleConnected;
            NetEvents.OnLobbyState -= HandleLobbyState;
            NetEvents.OnClearCalibration -= HandleClearCalibration;
            NetEvents.OnReloadCalibration -= HandleReloadCalibration;

            Instance = null;
        }

        // ------------------------------------------------------------- headset → server

        /// <summary>Headset aligned (manually or from a saved anchor) → report to the server.</summary>
        private void HandleLocalCalibrated(string source)
        {
            _localCalibrated = true;
            _source = source ?? "";
            Report(true, _source);
        }

        private void Report(bool calibrated, string source)
        {
            ArenaClient client = ArenaClient.Instance;
            if (client == null || !client.IsConnected)
            {
                return; // server-less session: nobody to report to
            }

            _reportMsg.calibrated = calibrated;
            _reportMsg.source = source ?? "";
            // ⚠️ The DTO is a single instance: a stale reason would poison the next SUCCESSFUL report
            // (a non-empty error makes the server ignore the alignment, §10.6) — same trap as in
            // BodyScaleState.Report.
            _reportMsg.error = "";
            // Floor offset only means something when MEASURED (§10.6); resending the last one would
            // show the operator a stale warning.
            _reportMsg.floorOffset = calibrated ? ArenaCalibrator.LastFloorOffsetMeters : 0f;
            client.Send(_reportMsg);
        }

        // ------------------------------------------------------------- server → headset

        private void HandleConnected(WelcomeMsg msg)
        {
            _hasEverConnected = true;

            // The mode arrives ONCE on connect (§10.6): the decision it gates — restore the anchor
            // from disk at launch? — is taken exactly now, so a later broadcast would have no moment
            // to be applied.
            Mode = msg != null ? msg.calibrationMode ?? "" : "";

            // The server resets calibration on hello (§10.6) — if a local alignment still stands
            // (e.g. reconnect after a drop), report it again.
            _serverCalibrated = false;
            if (_localCalibrated)
            {
                Report(true, _source);
            }

            Raise();
        }

        /// <summary>Operator cleared the calibration (§10.6).</summary>
        /// <remarks>
        /// Unconditional: part of what is dropped is NOT an alignment — a half-finished manual
        /// sequence (A captured, B pending) exists precisely while
        /// <see cref="_localCalibrated"/> is still <c>false</c>, so gating on it would disable the
        /// command in the case it exists for.
        /// <para><see cref="_serverCalibrated"/> is dropped here too: the next <c>lobby_state</c>
        /// would carry <c>false</c> anyway, but the manual gate must not wait for that broadcast.</para>
        /// <para>⚠️ The rig is NOT moved — free-roam rule: only the alignment is invalidated.</para>
        /// <para><paramref name="keepSaved"/> = <c>true</c> keeps the device anchor and UUID so a
        /// following <c>reload_calibration</c> has data; <c>false</c> is hard mode.</para>
        /// <para>⚠️ The calibrator is NOT searched in the scene — the work goes to
        /// <see cref="ArenaCalibrator.ApplyOperatorClear"/>, since a hard clear must erase the device
        /// record and close auto-restore even with no calibrator present.</para>
        /// </remarks>
        private void HandleClearCalibration(bool keepSaved)
        {
            _localCalibrated = false;
            _serverCalibrated = false;
            _source = "";

            ArenaCalibrator.ApplyOperatorClear(keepSaved);

            Debug.Log(keepSaved
                ? "[CalibrationState] Operatör hizalamayı geçersiz kıldı — cihazdaki kayıt duruyor, " +
                  "yeniden yüklenebilir ya da elle kalibre edilebilir (A basılıyken B×2)."
                : "[CalibrationState] Operatör kalibrasyonu sıfırladı ve cihaz kaydını sildi — " +
                  "yeniden kalibre edin (A basılıyken B×2).");
            Raise();
        }

        /// <summary>Operator asked to reload the alignment from the saved anchor (§10.6). NOT the
        /// inverse of a clear: nothing is erased, the calibrator merely retries and the headset still
        /// raises the "aligned" flag itself.</summary>
        private void HandleReloadCalibration()
        {
            ArenaCalibrator.RequestReload(HandleReloadResult);
        }

        /// <summary>Result of a reload attempt (empty reason = success).
        /// <para>⚠️ Nothing is sent on success: it already went through
        /// <see cref="ArenaCalibrator.Calibrated"/> → <see cref="Report"/>, and a second report would
        /// produce a duplicate result line (§5.3).</para>
        /// <para>On failure the reason goes as <c>set_calibration.error</c> while
        /// <see cref="_localCalibrated"/>/<see cref="_source"/> are carried unchanged (the state did
        /// not change, the attempt failed); <c>floorOffset</c> is 0 — nothing measured.</para></summary>
        private void HandleReloadResult(string error)
        {
            if (string.IsNullOrEmpty(error))
            {
                return;
            }

            ArenaClient client = ArenaClient.Instance;
            if (client == null || !client.IsConnected)
            {
                return; // server-less session: nobody to report to
            }

            Debug.LogWarning($"[CalibrationState] Kayıtlı hizalama yeniden yüklenemedi — {error}.", this);

            _reportMsg.calibrated = _localCalibrated;
            _reportMsg.source = _source ?? "";
            _reportMsg.floorOffset = 0f;
            _reportMsg.error = error;
            client.Send(_reportMsg);
        }

        /// <summary>Our own row in the roster is the single source of truth for calibration state
        /// (§5.3).
        /// <para>⚠️ State is NOT reset on disconnect (hence no <c>OnDisconnected</c> handler):
        /// resetting would tell the player "calibration required" the moment the network drops and
        /// send them recalibrating for nothing. On reconnect the server resets it on <c>hello</c>
        /// (§10.6) and <see cref="HandleConnected"/> re-reports the local state.</para></summary>
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

                ApplyServerState(info.calibrated, info.calibrationSource);
                return;
            }
        }

        /// <summary>Writes the roster value into the local mirror — FLAG ONLY.</summary>
        /// <remarks>
        /// ⚠️ No alignment is erased here (<see cref="ArenaCalibrator.ApplyOperatorClear"/> is not
        /// called, <see cref="_localCalibrated"/> is not dropped). <c>calibrated:false</c> in the
        /// roster does not mean "the operator cleared": the server resets the field on every
        /// <c>hello</c> (§10.6), so it is published once per reconnect and races
        /// <see cref="HandleConnected"/>'s re-report on the same socket.
        /// <para>
        /// The cost of erasing here would be silent and delayed: the rig is not moved, so the session
        /// looks fine, but the saved <c>OVRSpatialAnchor</c> is gone — the next <c>load_match</c> has
        /// nothing to restore and the player is drawn meters off for everyone. Worse, the re-report
        /// marks the server "calibrated", closing <see cref="ManualAllowed"/> so the player cannot
        /// fix it themselves.
        /// </para>
        /// <para>The only clear gate is <c>clear_calibration</c>
        /// (<see cref="HandleClearCalibration"/>), where the operator's intent is explicit.</para>
        /// </remarks>
        private void ApplyServerState(bool calibrated, string source)
        {
            if (_serverCalibrated == calibrated)
            {
                return;
            }

            _serverCalibrated = calibrated;
            if (calibrated && !string.IsNullOrEmpty(source))
            {
                _source = source;
            }

            Raise();
        }

        private static void Raise()
        {
            Changed?.Invoke();
        }
    }
}
