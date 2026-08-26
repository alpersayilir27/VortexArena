using System;
using VortexArena.Protocol;

namespace VortexArena.Net
{
    /// <summary>
    /// The static event hub: ArenaClient publishes server messages from here (all raised on the MAIN
    /// thread) and App/Core listen. The Net layer loads no scenes and holds no game knowledge — it does
    /// not know who processes the events or how.
    /// </summary>
    public static class NetEvents
    {
        public static event Action<WelcomeMsg> OnConnected;
        public static event Action OnDisconnected;
        public static event Action<ArenaConnectionState> OnConnectionStateChanged;
        public static event Action<LobbyStateMsg> OnLobbyState;
        public static event Action<LoadMatchMsg> OnLoadMatch;
        public static event Action<MatchStateMsg> OnMatchState;
        public static event Action<CountdownMsg> OnCountdown;
        public static event Action<HealthUpdateMsg> OnHealthUpdate;
        public static event Action<KillEventMsg> OnKillEvent;
        public static event Action<RespawnMsg> OnRespawn;
        public static event Action<MatchEndMsg> OnMatchEnd;

        /// <summary>Goes TO EVERYONE (§5.3): a network object's server-authoritative state changed
        /// (§10.10). <c>NetObjectSync</c> listens and writes it to the scene object.</summary>
        public static event Action<ObjectStateMsg> OnObjectState;

        /// <summary>Every network object of a scene at once (§10.10). ⚠️ It can arrive BEFORE the scene
        /// load — <c>NetObjectSync</c> buffers it, listeners must not assume the scene is loaded.</summary>
        public static event Action<WorldStateMsg> OnWorldState;

        /// <summary>A network object was BORN at runtime (§10.10). The body is an
        /// <see cref="ObjectStateMsg"/> — ⚠️ separate from <see cref="OnObjectState"/> because of what an
        /// unknown <c>netId</c> MEANS: there it is a drift to log, here it is the truth itself.</summary>
        public static event Action<ObjectStateMsg> OnObjectSpawn;

        /// <summary>A dynamic network object left the world (§10.10). Scene objects are never despawned.</summary>
        public static event Action<ObjectDespawnMsg> OnObjectDespawn;

        /// <summary>A COSMETIC object event relayed by the server (§10.10). A state-changing event never
        /// arrives here — its result is <c>object_state</c>, and announcing the same fact twice would
        /// play the presentation twice.</summary>
        public static event Action<ObjectEventMsg> OnObjectEvent;
        /// <summary>Return to lobby (§10.7). The message carries the lobby scene + profile; a listener
        /// that does not care ignores the parameter.</summary>
        public static event Action<ReturnToLobbyMsg> OnReturnToLobby;
        public static event Action<KickedMsg> OnKicked;

        /// <summary>The operator had this headset's body measured (§10.8) — players only.
        /// <c>BodyScaleState</c> listens, measures and answers with <c>set_body_scale</c>.
        /// <para>Fieldless server → client, so the event is parameterless too: the target is this
        /// connection.</para></summary>
        public static event Action OnMeasureBodyScale;

        /// <summary>The operator asked this headset to restart body tracking (§6.11) — players only.
        /// <c>LocalBodyAvatar</c> listens and runs the same repair its watchdog runs.
        /// <para>Fieldless server → client, so the event is parameterless too: the target is this
        /// connection.</para>
        /// <para>⚠️ <b>Nothing is answered.</b> The command's outcome is whether the body streams, and
        /// that already travels on <c>0x07</c> — a reply would only report that a function was
        /// called.</para></summary>
        public static event Action OnRestartBodyTracking;

        /// <summary>The operator reset this headset's calibration (§10.6) — players only.
        /// <c>CalibrationState</c> listens and invalidates <c>ArenaCalibrator</c>.
        /// <para>The argument is <c>keepSaved</c>: <c>true</c> = alignment invalidated but the device
        /// anchor + UUID KEPT (so a following <c>reload_calibration</c> works), <c>false</c> = deleted
        /// too. <c>playerId</c> is not forwarded (the target is this connection) but the mode is —
        /// hence the parameter.</para>
        /// <para>⚠️ <b>Completes the roster's <c>calibrated</c> field, does not replace it:</b> the
        /// field is state, this is the command. In a half-finished manual calibration the field is
        /// already <c>false</c>, so the reset is heard only here (§5.3).</para></summary>
        public static event Action<bool> OnClearCalibration;

        /// <summary>The operator had this headset reload alignment from the saved anchor (§10.6) —
        /// players only. <c>CalibrationState</c> listens, makes <c>ArenaCalibrator</c> load it and
        /// answers with <c>set_calibration</c>.
        /// <para>Fieldless server → client, so the event is parameterless (the target is this
        /// connection).</para>
        /// <para>⚠️ NOT the opposite of <c>OnClearCalibration</c>: that deletes the alignment, this
        /// <b>tries</b> to restore it — the headset still sets the "aligned" mark.</para></summary>
        public static event Action OnReloadCalibration;

        /// <summary>Admin connections only (§5.3): a player's saved-alignment reload attempt finished.
        /// <b>An event, not state</b> — state lives in the roster; this only answers the button the
        /// operator pressed.
        /// <para>The server keeps no pending-request ledger: the listening UI matches result to button
        /// (and ignores one with no pending row).</para></summary>
        public static event Action<CalibrationResultMsg> OnCalibrationResult;

        /// <summary>A remote player's shot/throw event (UDP 0x04 EventBatch, §6.5) — it replaced the WS
        /// <c>shot_fired</c> in v4. Published by <c>UdpStateChannel</c>, NOT <c>ArenaClient</c>, but on
        /// the MAIN thread like the others. Our own events are filtered out in the channel.</summary>
        public static event Action<RemoteFireEvent> OnRemoteFireEvent;

        /// <summary>Admin connections only (§5.3): the shared mode/map selection between admins + the
        /// last-action notice. In the App layer <c>AdminSelection</c> listens.</summary>
        public static event Action<AdminStateMsg> OnAdminState;

        /// <summary>Goes TO EVERYONE (§5.3): the rule shape of the running match CHANGED. <b>It is a
        /// real rule message</b> and is applied to <c>ModeRuntime</c> (<c>ModeRuntimePump</c> listens).
        /// Its only trigger today is the operator's friendly-fire switch (§5.2).</summary>
        public static event Action<RulesUpdateMsg> OnRulesUpdate;

        /// <summary>Goes TO EVERYONE (§5.3): the team mode of the selected mode. <b>Not a rule</b> —
        /// never applied to <c>ModeRuntime</c>; its only consumer is base strip visibility. Core's
        /// <c>ModeRuntimePump</c> listens and writes to <c>ModeSelection</c>.</summary>
        public static event Action<SelectionStateMsg> OnSelectionState;

        /// <summary>Admin connections only (§6.7), 1 Hz: per-player ping/jitter/loss, measured by the
        /// CLIENTS and only carried by the server. App's <c>AdminRoster</c> listens.
        /// <para>Losing one is harmless — a new one arrives next second, no reconciliation.</para></summary>
        public static event Action<NetStatsMsg> OnNetStats;

        /// <summary>Admin connections only (§5.3): a player's physical violation started or ended
        /// (obstacle / out of bounds). The server keeps the ledger; this is only the edge notification.
        /// <para>Losing one is harmless — the visual counterpart (the top-down ring) feeds off the
        /// snapshot bits, so a lost message costs one feed row.</para></summary>
        public static event Action<ViolationMsg> OnViolation;

        internal static void RaiseConnected(WelcomeMsg msg) { OnConnected?.Invoke(msg); }
        internal static void RaiseDisconnected() { OnDisconnected?.Invoke(); }
        internal static void RaiseConnectionStateChanged(ArenaConnectionState state) { OnConnectionStateChanged?.Invoke(state); }
        internal static void RaiseLobbyState(LobbyStateMsg msg) { OnLobbyState?.Invoke(msg); }
        internal static void RaiseLoadMatch(LoadMatchMsg msg) { OnLoadMatch?.Invoke(msg); }
        internal static void RaiseMatchState(MatchStateMsg msg) { OnMatchState?.Invoke(msg); }
        internal static void RaiseCountdown(CountdownMsg msg) { OnCountdown?.Invoke(msg); }
        internal static void RaiseHealthUpdate(HealthUpdateMsg msg) { OnHealthUpdate?.Invoke(msg); }
        internal static void RaiseKillEvent(KillEventMsg msg) { OnKillEvent?.Invoke(msg); }
        internal static void RaiseRespawn(RespawnMsg msg) { OnRespawn?.Invoke(msg); }
        internal static void RaiseMatchEnd(MatchEndMsg msg) { OnMatchEnd?.Invoke(msg); }
        internal static void RaiseObjectState(ObjectStateMsg msg) { OnObjectState?.Invoke(msg); }
        internal static void RaiseWorldState(WorldStateMsg msg) { OnWorldState?.Invoke(msg); }
        internal static void RaiseObjectSpawn(ObjectStateMsg msg) { OnObjectSpawn?.Invoke(msg); }
        internal static void RaiseObjectDespawn(ObjectDespawnMsg msg) { OnObjectDespawn?.Invoke(msg); }
        internal static void RaiseObjectEvent(ObjectEventMsg msg) { OnObjectEvent?.Invoke(msg); }
        internal static void RaiseReturnToLobby(ReturnToLobbyMsg msg) { OnReturnToLobby?.Invoke(msg); }
        // Passed with `in` so a 40+ byte struct is not copied at 10 events/s (it is not modified while
        // publishing). A copy is made anyway when handing it to the delegate; the gain here is the call
        // path.
        internal static void RaiseRemoteFireEvent(in RemoteFireEvent evt) { OnRemoteFireEvent?.Invoke(evt); }
        internal static void RaiseKicked(KickedMsg msg) { OnKicked?.Invoke(msg); }
        internal static void RaiseMeasureBodyScale() { OnMeasureBodyScale?.Invoke(); }
        internal static void RaiseRestartBodyTracking() { OnRestartBodyTracking?.Invoke(); }
        internal static void RaiseClearCalibration(bool keepSaved) { OnClearCalibration?.Invoke(keepSaved); }
        internal static void RaiseReloadCalibration() { OnReloadCalibration?.Invoke(); }
        internal static void RaiseCalibrationResult(CalibrationResultMsg msg) { OnCalibrationResult?.Invoke(msg); }
        internal static void RaiseAdminState(AdminStateMsg msg) { OnAdminState?.Invoke(msg); }
        internal static void RaiseSelectionState(SelectionStateMsg msg) { OnSelectionState?.Invoke(msg); }
        internal static void RaiseRulesUpdate(RulesUpdateMsg msg) { OnRulesUpdate?.Invoke(msg); }
        internal static void RaiseNetStats(NetStatsMsg msg) { OnNetStats?.Invoke(msg); }
        internal static void RaiseViolation(ViolationMsg msg) { OnViolation?.Invoke(msg); }
    }
}
