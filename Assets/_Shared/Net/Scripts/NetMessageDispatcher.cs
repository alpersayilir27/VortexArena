using System;
using UnityEngine;
using VortexArena.Protocol;

namespace VortexArena.Net
{
    /// <summary>
    /// The PURE half of the server message switch (§5.3): parse → publish, no connection side
    /// effects. Shared by the live socket and the replay player, so a recording takes the exact same
    /// path the admin took live (§12.4).
    /// <para>Side-effect messages (<c>welcome</c>, <c>kicked</c>, <c>ping</c>,
    /// <c>version_mismatch</c>) stay in <see cref="ArenaClient"/> — they drive the connection and are
    /// never recorded.</para>
    /// </summary>
    internal sealed class NetMessageDispatcher
    {
        /// <summary>Where a publish is handed to: live = main-thread queue, replay = run now.</summary>
        private readonly Action<Action> _post;

        /// <summary>The last APPLIED <c>lobby_state.version</c> (§5.3); written on the net thread,
        /// read on the main thread → volatile.</summary>
        private volatile int _lastRosterVersion;

        /// <summary>Last applied roster version — <c>status</c> reports it back for reconciliation
        /// (§5.1).</summary>
        internal int LastRosterVersion => _lastRosterVersion;

        internal NetMessageDispatcher(Action<Action> post)
        {
            _post = post;
        }

        /// <summary>New session (welcome) or replay restart: a server counting versions from 0 again
        /// would otherwise have every roster dropped as old.</summary>
        internal void ResetRosterVersion()
        {
            _lastRosterVersion = 0;
        }

        /// <summary>Handles one message; false means "not a pure case" and the caller decides.</summary>
        internal bool TryDispatch(string type, string json)
        {
            switch (type)
            {
                case MessageTypes.LobbyState:
                {
                    LobbyStateMsg msg = JsonUtility.FromJson<LobbyStateMsg>(json);
                    // §5.3: an old snapshot must NOT overwrite a newer one. A second safety net even
                    // though the server broadcasts from one publisher; the symptom of a stale roster
                    // would be "a kicked player still listed as online".
                    if (msg == null || msg.version <= _lastRosterVersion)
                    {
                        return true;
                    }

                    _lastRosterVersion = msg.version;
                    _post(() => NetEvents.RaiseLobbyState(msg));
                    return true;
                }

                case MessageTypes.LoadMatch:
                {
                    LoadMatchMsg msg = JsonUtility.FromJson<LoadMatchMsg>(json);
                    _post(() => NetEvents.RaiseLoadMatch(msg));
                    return true;
                }

                case MessageTypes.Countdown:
                {
                    CountdownMsg msg = JsonUtility.FromJson<CountdownMsg>(json);
                    _post(() => NetEvents.RaiseCountdown(msg));
                    return true;
                }

                case MessageTypes.MatchState:
                {
                    MatchStateMsg msg = JsonUtility.FromJson<MatchStateMsg>(json);
                    _post(() => NetEvents.RaiseMatchState(msg));
                    return true;
                }

                case MessageTypes.HealthUpdate:
                {
                    HealthUpdateMsg msg = JsonUtility.FromJson<HealthUpdateMsg>(json);
                    _post(() => NetEvents.RaiseHealthUpdate(msg));
                    return true;
                }

                case MessageTypes.KillEvent:
                {
                    KillEventMsg msg = JsonUtility.FromJson<KillEventMsg>(json);
                    _post(() => NetEvents.RaiseKillEvent(msg));
                    return true;
                }

                case MessageTypes.Respawn:
                {
                    RespawnMsg msg = JsonUtility.FromJson<RespawnMsg>(json);
                    _post(() => NetEvents.RaiseRespawn(msg));
                    return true;
                }

                case MessageTypes.MatchEnd:
                {
                    MatchEndMsg msg = JsonUtility.FromJson<MatchEndMsg>(json);
                    _post(() => NetEvents.RaiseMatchEnd(msg));
                    return true;
                }

                case MessageTypes.ObjectState:
                {
                    ObjectStateMsg msg = JsonUtility.FromJson<ObjectStateMsg>(json);
                    _post(() => NetEvents.RaiseObjectState(msg));
                    return true;
                }

                case MessageTypes.ObjectSpawn:
                {
                    // Same body as object_state (§5.3) — only the TYPE separates a spawn from a
                    // drifted id, so it must not be merged into the case above.
                    ObjectStateMsg msg = JsonUtility.FromJson<ObjectStateMsg>(json);
                    _post(() => NetEvents.RaiseObjectSpawn(msg));
                    return true;
                }

                case MessageTypes.ObjectDespawn:
                {
                    ObjectDespawnMsg msg = JsonUtility.FromJson<ObjectDespawnMsg>(json);
                    _post(() => NetEvents.RaiseObjectDespawn(msg));
                    return true;
                }

                case MessageTypes.ObjectEvent:
                {
                    ObjectEventMsg msg = JsonUtility.FromJson<ObjectEventMsg>(json);
                    _post(() => NetEvents.RaiseObjectEvent(msg));
                    return true;
                }

                case MessageTypes.WorldState:
                {
                    WorldStateMsg msg = JsonUtility.FromJson<WorldStateMsg>(json);
                    _post(() => NetEvents.RaiseWorldState(msg));
                    return true;
                }

                case MessageTypes.ReturnToLobby:
                {
                    ReturnToLobbyMsg msg = JsonUtility.FromJson<ReturnToLobbyMsg>(json);
                    _post(() => NetEvents.RaiseReturnToLobby(msg));
                    return true;
                }

                // v4: `shot_fired` was REMOVED — shots/throws ride UDP 0x03/0x04 (§6.4/6.5) and are
                // published by UdpStateChannel. This type never arrives on WS.

                case MessageTypes.MeasureBodyScale:
                {
                    // Sent to players only (§10.8); no listener on admin. The measurement reads the
                    // rig/character, so it needs the Unity API → main thread.
                    _post(NetEvents.RaiseMeasureBodyScale);
                    return true;
                }

                case MessageTypes.RestartBodyTracking:
                {
                    // Sent to players only (§6.11); no listener on admin. The repair toggles a
                    // MonoBehaviour and calls OVRPlugin → main thread.
                    _post(NetEvents.RaiseRestartBodyTracking);
                    return true;
                }

                case MessageTypes.ClearCalibration:
                {
                    // The operator reset calibration (§10.6). Players only; no `playerId` (the target
                    // is this connection) but `keepSaved` does ride: soft keeps the device anchor,
                    // hard deletes it; a missing field reads `false` = hard. ⚠️ The roster's
                    // `calibrated` field is NOT consulted — in a half-finished calibration it is
                    // already `false`, so the reset has no visible delta there (§5.3). Touches
                    // scene/anchor → main thread.
                    ClearCalibrationMsg msg = JsonUtility.FromJson<ClearCalibrationMsg>(json);
                    bool keepSaved = msg != null && msg.keepSaved;
                    _post(() => NetEvents.RaiseClearCalibration(keepSaved));
                    return true;
                }

                case MessageTypes.ReloadCalibration:
                {
                    // The operator asked for an alignment reload from the saved anchor (§10.6).
                    // Players only, fieldless: the target is this connection. An uncalibrated target
                    // is NOT skipped — that is exactly who the command is for. Touches anchor/rig →
                    // main thread.
                    _post(NetEvents.RaiseReloadCalibration);
                    return true;
                }

                case MessageTypes.CalibrationResult:
                {
                    // Admin connections only; on a player there is no listener.
                    CalibrationResultMsg msg = JsonUtility.FromJson<CalibrationResultMsg>(json);
                    _post(() => NetEvents.RaiseCalibrationResult(msg));
                    return true;
                }

                case MessageTypes.VenueSurveyResult:
                {
                    // Only the player who uploaded gets one (§10.11); elsewhere no listener.
                    VenueSurveyResultMsg msg = JsonUtility.FromJson<VenueSurveyResultMsg>(json);
                    _post(() => NetEvents.RaiseVenueSurveyResult(msg));
                    return true;
                }

                case MessageTypes.AdminState:
                {
                    // Admin connections only; on a player there is no listener.
                    AdminStateMsg msg = JsonUtility.FromJson<AdminStateMsg>(json);
                    _post(() => NetEvents.RaiseAdminState(msg));
                    return true;
                }

                case MessageTypes.RulesUpdate:
                {
                    // Goes to everyone (§5.3). Unlike SelectionState this IS a real rule: the running
                    // match's shape changed (today only the friendly-fire switch) → ModeRuntime.
                    RulesUpdateMsg msg = JsonUtility.FromJson<RulesUpdateMsg>(json);
                    _post(() => NetEvents.RaiseRulesUpdate(msg));
                    return true;
                }

                case MessageTypes.SelectionState:
                {
                    // Goes to everyone (§5.3). Presentation info, NOT a rule — never applied to
                    // ModeRuntime; written to ModeSelection (base strips).
                    SelectionStateMsg msg = JsonUtility.FromJson<SelectionStateMsg>(json);
                    _post(() => NetEvents.RaiseSelectionState(msg));
                    return true;
                }

                case MessageTypes.NetStats:
                {
                    // Admin connections only; on a player there is no listener.
                    NetStatsMsg msg = JsonUtility.FromJson<NetStatsMsg>(json);
                    _post(() => NetEvents.RaiseNetStats(msg));
                    return true;
                }

                case MessageTypes.Violation:
                {
                    // Admin connections only; on a player there is no listener.
                    ViolationMsg msg = JsonUtility.FromJson<ViolationMsg>(json);
                    _post(() => NetEvents.RaiseViolation(msg));
                    return true;
                }

                default:
                    return false;
            }
        }
    }
}
