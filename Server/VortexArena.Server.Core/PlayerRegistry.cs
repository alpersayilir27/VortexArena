#nullable enable
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>deviceId → PlayerState registry: playerId allocation (1..PLAYER_ID_MAX), devices.json
/// name persistence (random pool name + 1..99 jersey number, both automatic), team balancing and
/// connection sweeping (HEARTBEAT_TIMEOUT → RECONNECT_GRACE).
/// <para>
/// <b>Persistence differs per role (§2):</b> a player record is persistent (stable deviceId, becomes
/// Reconnecting on drop and is waited for, name written to devices.json). An admin record is
/// PER-SESSION — its deviceId is unique each session, the record is deleted entirely on drop and the
/// name never hits disk; otherwise every admin restart would leave a ghost roster row and burn a
/// playerId. ⚠️ Hence an admin NEVER enters Reconnecting: a returning admin arrives with a new
/// identity, so a "reconnecting" row would be a lie.
/// </para>
/// <para>
/// <b>Record lifetime (§2/§8):</b> socket drops → <c>Reconnecting</c> → RECONNECT_GRACE expires →
/// <c>Left</c> if a match participant (record stays until match end, §10.2), otherwise the record is
/// DELETED and the playerId returns to the pool. No separate playerId reservation ledger is needed:
/// while a <c>Left</c> record sits in <c>_players</c>, <see cref="NextFreePlayerIdLocked"/> already
/// skips it.
/// </para></summary>
public sealed class PlayerRegistry : IDisposable
{
    private static readonly JsonSerializerOptions DevicesJsonOptions = new()
    {
        WriteIndented = true,
        // DeviceRecord exposes PROPERTIES (not fields) → camelCase policy gives {"name":…,"number":…}.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // keep non-ASCII readable in the file
    };
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>Player name pool (§2) — picked RANDOMLY on first connection. Names are not unique:
    /// they repeat once the pool is exhausted, the distinguishing field is the number. Admins do NOT
    /// use this pool (their name comes from the PC name and is never persisted).</summary>
    private static readonly string[] NamePool =
    {
        "umut", "alper", "ertu", "yunus", "resul", "enver", "enes", "nisa", "ceren", "tuğba",
        "elif", "pınar", "taner", "yasemin", "hüseyin", "deniz", "selin", "kaan", "burcu", "emre"
    };

    private readonly ConcurrentDictionary<string, PlayerState> _players = new();
    private readonly object _gate = new();
    private readonly Timer _connectionTimer;
    private readonly string _devicesPath;

    /// <summary>In-memory copy of devices.json (deviceId → name + number). THE truth source for
    /// number ownership: a device that never connected still has a row here, so conflict lookups go
    /// against this map, not <c>_players</c>.</summary>
    private Dictionary<string, DeviceRecord> _devices = new();

    /// <summary>Raised from worker threads. NOT raised for TryRegisterHello — LobbyService calls
    /// Announce after sending welcome, so welcome always precedes lobby_state.</summary>
    public event Action<PlayerState, PlayerChangeKind>? Changed;

    public PlayerRegistry(string devicesJsonPath)
    {
        _devicesPath = devicesJsonPath;
        LoadDevices();
        _connectionTimer = new Timer(_ => CheckConnections(), null,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public IReadOnlyList<PlayerState> Snapshot() => _players.Values.ToList();

    public bool TryGet(string deviceId, out PlayerState state) => _players.TryGetValue(deviceId, out state!);

    public bool TryGetByPlayerId(int playerId, out PlayerState state)
    {
        foreach (var s in _players.Values)
        {
            if (s.PlayerId == playerId)
            {
                state = s;
                return true;
            }
        }
        state = null!;
        return false;
    }

    /// <summary>Connected admin count (admin_state.adminCount).</summary>
    public int ConnectedAdminCount()
    {
        var count = 0;
        foreach (var state in _players.Values)
            if (state.IsConnected && state.Role == "admin") count++;
        return count;
    }

    /// <summary>ALL connected sockets regardless of role (snapshot copy for selection_state).
    /// For admin-only targets see <see cref="ConnectedAdminConnections"/>.</summary>
    public List<ClientConnection> ConnectedConnections()
    {
        var result = new List<ClientConnection>();
        foreach (var state in _players.Values)
        {
            if (!state.IsConnected) continue;
            var socket = state.Socket;
            if (socket != null) result.Add(socket);
        }
        return result;
    }

    /// <summary>Sockets of connected admins (snapshot copy for admin_state broadcast).</summary>
    public List<ClientConnection> ConnectedAdminConnections()
    {
        var result = new List<ClientConnection>();
        foreach (var state in _players.Values)
        {
            if (!state.IsConnected || state.Role != "admin") continue;
            var socket = state.Socket;
            if (socket != null) result.Add(socket);
        }
        return result;
    }

    /// <summary>false = playerId pool exhausted (1..PLAYER_ID_MAX). Not a product quota, it is the
    /// u8 wire format ceiling. If the same deviceId reconnects, the old socket is closed and the
    /// playerId is kept (Docs/ArenaNet-Protokol.md §2).
    /// <para>⚠️ Whatever state an existing record returns from (<c>Reconnecting</c> or <c>Left</c>)
    /// it is pulled to Connected and <b>name/number/team/kills/deaths/score/MatchParticipant are
    /// PRESERVED</b>: that is the "resume where you left off, in your old row" rule (§2) — resetting
    /// would make a mid-match returner look like a second identity.</para></summary>
    public bool TryRegisterHello(HelloMsg hello, ClientConnection connection, out PlayerState state, out PlayerChangeKind kind)
    {
        ClientConnection? stale = null;
        lock (_gate)
        {
            if (_players.TryGetValue(hello.deviceId, out var existing))
            {
                state = existing;
                kind = PlayerChangeKind.Reconnected;
                if (existing.Socket != null && !ReferenceEquals(existing.Socket, connection))
                    stale = existing.Socket;
            }
            else
            {
                var playerId = NextFreePlayerIdLocked();
                if (playerId == 0)
                {
                    state = null!;
                    kind = PlayerChangeKind.Added;
                    return false;
                }
                state = new PlayerState { DeviceId = hello.deviceId, PlayerId = playerId };
                _players[hello.deviceId] = state;
                kind = PlayerChangeKind.Added;
            }

            state.Role = hello.role == "admin" ? "admin" : "player";
            ResolveIdentityLocked(state, hello.deviceName);
            state.Team = state.Role == "player"
                ? (string.IsNullOrEmpty(state.Team) ? SmallerTeamLocked() : state.Team)
                : ""; // admins do not play
            state.Scene = hello.currentScene ?? "";
            state.Scenes = hello.scenes != null ? new List<string>(hello.scenes) : new List<string>();
            state.Ready = false;
            // §10.6: the server cannot know a reconnecting headset's alignment (the app may have
            // restarted). The headset re-reports once it restores from the saved anchor.
            state.Calibrated = false;
            state.CalibrationSource = "";
            // Diagnostic fields belong to that alignment and go with it: the offset described that
            // alignment and the error that session — carrying them over shows a solved problem.
            state.FloorOffset = 0f;
            state.ScaleError = "";
            state.CalibrationError = "";
            // §10.8: body scale depends on the alignment, so it counts as unknown too. The headset
            // re-reports it from its own record (set_body_scale), so no re-measuring is needed.
            state.BodyScale = 0f;
            state.Connection = PlayerConnection.Connected;
            state.DisconnectedAt = default;
            state.LastSeen = DateTime.UtcNow;
            state.Socket = connection;
            // Every welcome carries a new udpToken; a stale UDP endpoint becomes invalid (the client
            // re-registers with 0x00 UdpHello).
            state.UdpToken = NextUdpToken();
            state.UdpEndpoint = null;
            // Do not carry a stale pose into the new session; the snapshot reader checks HasPose
            // under PoseGate, so the reset takes the same lock (never take _gate from inside PoseGate).
            lock (state.PoseGate)
            {
                state.HasPose = false;
                state.LastSeq = 0;
                // §6.9: the skeleton ledger resets WITH the pose ledger. A restarted client counts
                // from 0 again; against a stale high LastSkeletonSeq the u16 wrap check would
                // silently reject every frame for minutes — "name label fine, body frozen".
                state.HasSkeleton = false;
                state.LastSkeletonSeq = 0;
                state.LastSkeleton = null;
                // §6.4: the event dedup ledger too — a stale LastEventSeq matching the restarted
                // client's first seq would swallow that shot as a "duplicate" and log one bogus
                // loss gap. Normally recv-thread-only fields; safe here because UdpEndpoint is
                // null until the client re-registers, so no event can race this write.
                state.HasEventSeq = false;
                state.LastEventSeq = 0;
                // §10.9: stale violation flags are not carried over either. The freshness gate
                // (LastPoseAt) already covers this; the fields are cleared anyway so a "reconnected
                // but still shown inside the wall" intermediate state can never appear.
                state.InObstacle = false;
                state.OutOfBounds = false;
            }
        }

        stale?.Abort();
        return true;
    }

    /// <summary>Deferred notification for TryRegisterHello — LobbyService calls it AFTER welcome.</summary>
    public void Announce(PlayerState state, PlayerChangeKind kind) => Changed?.Invoke(state, kind);

    /// <summary>status heartbeat (§5.1). ⚠️ <b>Does NOT raise Changed unconditionally</b> — only a
    /// field VISIBLE in the roster (scene/battery/ctrlL/ctrlR/connection) triggers a broadcast.
    /// Unconditional broadcasting would turn every status into a full roster JSON: 18 clients × once
    /// per 5 s × 18 receivers ≈ 65 broadcasts a second with nothing changing. <c>Fps</c> is not
    /// carried in PlayerInfo, so it triggers nothing.</summary>
    public void UpdateStatus(string deviceId, StatusMsg status)
    {
        if (!_players.TryGetValue(deviceId, out var state)) return;
        bool changed;
        lock (_gate)
        {
            var scene = status.scene ?? state.Scene;
            // Controller state belongs with Scene/Battery: a DISCRETE device state (one of three
            // values), not a number that moves every status — it rarely triggers a broadcast but
            // must show on the operator's screen the moment it drops.
            changed = !state.IsConnected || state.Scene != scene || state.Battery != status.battery
                      || state.CtrlL != status.ctrlL || state.CtrlR != status.ctrlR;
            state.Scene = scene;
            state.Battery = status.battery;
            state.CtrlL = status.ctrlL;
            state.CtrlR = status.ctrlR;
            state.Fps = status.fps;
            // ⚠️ Network telemetry does NOT enter `changed` — same reason as Fps (§6.7): constantly
            // moving numbers would turn every status into a full roster broadcast. They reach admins
            // on a separate channel (net_stats) and never enter the roster.
            state.RttMs = status.rttMs;
            state.JitterMs = status.jitterMs;
            state.LossPct = status.lossPct;
            state.LastSeen = DateTime.UtcNow;
            state.Connection = PlayerConnection.Connected;
            state.DisconnectedAt = default;
        }
        if (changed) Changed?.Invoke(state, PlayerChangeKind.Updated);
    }

    /// <summary>set_identity (§5.1): name and/or jersey number. <b>An empty name and number
    /// <c>0</c> keep the current value</b> (set_selection convention) — "change only the number" is
    /// a single call.
    /// <para>
    /// Number uniqueness is enforced here and a <b>disconnected holder is moved in the same
    /// step</b>: if the holder is connected the request is rejected (the operator can see and
    /// resolve it in the roster), if it is disconnected that device is moved to the first free
    /// number from 1. Deferring the move to the holder's next connection would leave devices.json
    /// with a duplicate meanwhile; rejecting against a disconnected holder would deadlock the
    /// operator (the holding device is not visible in the roster and cannot be freed).
    /// </para>
    /// <para>false = nothing changed or the request was rejected; a non-empty <paramref name="error"/>
    /// means rejected and the text is shown to the operator via <c>admin_state.notice</c>.</para></summary>
    public bool SetIdentity(int playerId, string? name, int number, out string error)
    {
        error = "";
        if (!TryGetByPlayerId(playerId, out var state))
        {
            error = $"playerId {playerId} bulunamadı";
            return false;
        }

        var wantedName = name?.Trim();
        var setName = !string.IsNullOrEmpty(wantedName);
        var setNumber = number != 0;
        if (!setName && !setNumber) return false; // both fields say "keep" — exit silently

        if (setNumber)
        {
            if (number < ArenaProtocol.PLAYER_NUMBER_MIN || number > ArenaProtocol.PLAYER_NUMBER_MAX)
            {
                error = $"numara {number} geçersiz ({ArenaProtocol.PLAYER_NUMBER_MIN}-{ArenaProtocol.PLAYER_NUMBER_MAX})";
                return false;
            }
            if (state.Role == "admin")
            {
                error = "admin'e numara atanmaz";
                return false;
            }
        }

        var changed = false;
        lock (_gate)
        {
            if (setNumber && number != state.Number)
            {
                var holder = FindNumberHolderLocked(number, state.DeviceId);
                if (holder != null)
                {
                    if (_players.TryGetValue(holder, out var owner) && owner.IsConnected)
                    {
                        error = $"{number} numara {owner.Name}'da";
                        return false;
                    }
                    // Disconnected holder: move it BEFORE state takes the number, so the first-free
                    // lookup (with devices.json still showing the old holder) cannot pick `number`.
                    var moved = NextFreeNumberLocked();
                    if (_devices.TryGetValue(holder, out var record)) record.Number = moved;
                    if (_players.TryGetValue(holder, out var absent)) absent.Number = moved;
                }
                state.Number = number;
                changed = true;
            }

            if (setName && state.Name != wantedName)
            {
                state.Name = wantedName!;
                changed = true;
            }

            // Admin deviceId is per-session (§2) — persisting it would fill devices.json with junk.
            if (changed && state.Role != "admin")
            {
                _devices[state.DeviceId] = new DeviceRecord { Name = state.Name, Number = state.Number };
                SaveDevicesLocked();
            }
        }

        if (changed) Changed?.Invoke(state, PlayerChangeKind.Updated);
        return changed;
    }

    /// <summary>Writes the ready flag. <b>No broadcast if unchanged</b> (<see cref="SetIdentity"/>
    /// pattern): each <c>Changed</c> is a FULL <c>lobby_state</c> broadcast, i.e. a fan-out scaling
    /// with player count. Both users of this flag can resend the same value repeatedly — the
    /// client's <c>set_ready</c> at the load gate and the player's base report at round gather
    /// (§10.1).</summary>
    public void SetReady(string deviceId, bool ready)
    {
        if (!_players.TryGetValue(deviceId, out var state)) return;

        bool changed;
        lock (_gate)
        {
            changed = state.Ready != ready;
            state.Ready = ready;
        }

        if (changed) Changed?.Invoke(state, PlayerChangeKind.Updated);
    }

    public bool SetTeam(int playerId, string team)
    {
        if (!TryGetByPlayerId(playerId, out var state)) return false;
        lock (_gate) state.Team = team;
        Changed?.Invoke(state, PlayerChangeKind.Updated);
        return true;
    }

    /// <summary>Writes calibration state (§10.6). Changed → lobby_state broadcast; there is NO
    /// separate calibration message, the state travels with the roster (§5.3).
    /// <para>Admins do not calibrate: <c>role != "player"</c> is rejected silently, otherwise the
    /// admin would count itself as "uncalibrated" in its own UI.</para></summary>
    public bool SetCalibration(int playerId, bool calibrated, string? source, float floorOffset = 0f)
    {
        if (!TryGetByPlayerId(playerId, out var state)) return false;
        if (state.Role != "player") return false;

        var nextSource = calibrated ? source ?? "" : "";
        // The offset belongs to the alignment (§10.6): it falls when the alignment falls.
        var nextOffset = calibrated ? floorOffset : 0f;
        lock (_gate)
        {
            // No broadcast if nothing changed. On a map change every headset restores from its saved
            // anchor and re-reports the same value; without this guard that is N players × N
            // receivers = N² useless lobby_state messages (256 at 16 players).
            // ⚠️ The offset is part of the comparison: a player recalibrating with the same source
            // would otherwise keep the old offset in the roster.
            // ⚠️ Clearing the error counts as a change too (SetBodyScale's errorCleared pattern): for
            // a player who reloads a saved alignment and reports the same value this field is the
            // only real delta, and leaving it out would keep a resolved warning stuck on the row.
            var errorCleared = calibrated && state.CalibrationError.Length > 0;
            if (state.Calibrated == calibrated && state.CalibrationSource == nextSource
                && Math.Abs(state.FloorOffset - nextOffset) < 0.0001f && !errorCleared) return false;
            state.Calibrated = calibrated;
            state.CalibrationSource = nextSource;
            state.FloorOffset = nextOffset;
            // The reason belongs to the alignment: a successful report invalidates it, and a dropped
            // alignment does not carry it either.
            state.CalibrationError = "";

            // §10.8: body scale falls with the alignment — it was measured against the arena floor.
            // ⚠️ THIS is the gate, not clear_calibration: the headset's own set_calibration{false}
            // has the same effect, and covering only one path would make the rule useless.
            if (!calibrated)
            {
                state.BodyScale = 0f;
                // The measurement error was information about that alignment too; left behind, a
                // reset row would still show the old reason.
                state.ScaleError = "";
            }
        }
        Changed?.Invoke(state, PlayerChangeKind.Updated);
        return true;
    }

    /// <summary>
    /// Writes body scale (§10.8). The server does NOT produce the number, it only clamps it to
    /// <c>[BODY_SCALE_MIN, BODY_SCALE_MAX]</c>: the client measures, but the result reaches
    /// everyone's screen.
    /// <para>There is deliberately NO calibration gate: a reconnecting headset reports calibration
    /// and scale at the same time, and a gate depending on their order would sometimes drop the
    /// scale silently. If the alignment is really invalid, <see cref="SetCalibration"/> already
    /// clears the scale.</para>
    /// <para>No broadcast if unchanged — same reason as <see cref="SetCalibration"/>.</para>
    /// <para>⚠️ <b>A successful measurement also clears <see cref="PlayerState.ScaleError"/></b>, in
    /// the SAME broadcast: split into two calls, one measurement would produce two full roster
    /// broadcasts (§10.8).</para>
    /// </summary>
    public bool SetBodyScale(int playerId, float scale)
    {
        if (!TryGetByPlayerId(playerId, out var state)) return false;
        if (state.Role != "player") return false;

        var clamped = Math.Clamp(scale, ArenaProtocol.BODY_SCALE_MIN, ArenaProtocol.BODY_SCALE_MAX);
        lock (_gate)
        {
            var scaleChanged = Math.Abs(state.BodyScale - clamped) >= 0.0001f;
            var errorCleared = state.ScaleError.Length > 0;
            if (!scaleChanged && !errorCleared) return false;
            state.BodyScale = clamped;
            state.ScaleError = "";
        }
        Changed?.Invoke(state, PlayerChangeKind.Updated);
        return true;
    }

    /// <summary>Writes the reason for a failed body measurement (§10.8). <b>Does NOT touch the
    /// scale</b> — a failed measurement does not invalidate the stored value, it only tells the
    /// operator why it did not happen.
    /// <para>An empty reason clears the field; no broadcast if unchanged (same reason as
    /// <see cref="SetCalibration"/>).</para></summary>
    public bool SetScaleError(int playerId, string? error)
    {
        if (!TryGetByPlayerId(playerId, out var state)) return false;
        if (state.Role != "player") return false;

        var next = error ?? "";
        lock (_gate)
        {
            if (state.ScaleError == next) return false;
            state.ScaleError = next;
        }
        Changed?.Invoke(state, PlayerChangeKind.Updated);
        return true;
    }

    /// <summary>Writes the reason for a failed <c>reload_calibration</c> attempt (§10.6).
    /// <b>Does NOT touch calibration</b> — a failed attempt does not invalidate the stored
    /// alignment, it only tells the operator why it did not happen (exactly the
    /// <see cref="SetScaleError"/> contract).
    /// <para>An empty reason clears the field; no broadcast if unchanged.</para></summary>
    public bool SetCalibrationError(int playerId, string? error)
    {
        if (!TryGetByPlayerId(playerId, out var state)) return false;
        if (state.Role != "player") return false;

        var next = error ?? "";
        lock (_gate)
        {
            if (state.CalibrationError == next) return false;
            state.CalibrationError = next;
        }
        Changed?.Invoke(state, PlayerChangeKind.Updated);
        return true;
    }

    // ⚠️ Bulk reset (clear_calibration playerId:0) lives in LobbyService, NOT here, and is not moved
    // back: the real work of a reset is FORWARDING the command to every headset (§10.6), not
    // lowering a flag, and the registry sees no sockets. The "skip the already-uncalibrated"
    // shortcut was born exactly here — a player mid-way through manual calibration already has the
    // flag `false`, so the only skipped group was the one that needed the command most.

    /// <summary>0x00 UdpHello validation: on a playerId↔udpToken match the endpoint is registered
    /// (§6.1). Poses are accepted only from a registered endpoint (StateHost 0x01 intake).</summary>
    public bool TryRegisterUdpEndpoint(byte playerId, uint udpToken, IPEndPoint endpoint)
    {
        if (!TryGetByPlayerId(playerId, out var state)) return false;
        lock (_gate)
        {
            if (udpToken == 0 || state.UdpToken != udpToken) return false;
            state.UdpEndpoint = endpoint;
        }
        return true;
    }

    /// <summary>Called when a connection's recv loop closes. No-op if the device already moved to a
    /// newer connection (guards the reconnect race).
    /// <para>A player drops to <c>Reconnecting</c> and is awaited for RECONNECT_GRACE; an admin is
    /// deleted entirely via <see cref="RetireLocked"/> (§2).</para></summary>
    public void NotifyDisconnected(ClientConnection connection)
    {
        PlayerState? affected = null;
        var removed = false;
        lock (_gate)
        {
            foreach (var state in _players.Values)
            {
                if (ReferenceEquals(state.Socket, connection))
                {
                    state.Socket = null;
                    state.Connection = PlayerConnection.Reconnecting;
                    state.DisconnectedAt = DateTime.UtcNow;
                    state.Ready = false;
                    affected = state;
                    removed = RetireLocked(state);
                    break;
                }
            }
        }
        if (affected != null)
            Changed?.Invoke(affected, removed ? PlayerChangeKind.Removed : PlayerChangeKind.Reconnecting);
    }

    /// <summary>Adds/removes an entry in the running match's ledger (§10.2). Broadcasts only on
    /// change — <c>inMatch</c> is VISIBLE in the roster, so unconditional broadcasting would turn
    /// every call into a full lobby_state.</summary>
    public void SetMatchParticipant(int playerId, bool value)
    {
        if (!TryGetByPlayerId(playerId, out var state)) return;
        lock (_gate)
        {
            if (state.MatchParticipant == value) return;
            state.MatchParticipant = value;
        }
        Changed?.Invoke(state, PlayerChangeKind.Updated);
    }

    /// <summary>Marks every CONNECTED player as a participant when a match is set up (§10.2);
    /// returns the number of affected records.
    /// <para>⚠️ <b>One</b> <c>Updated</c> is raised, not one per change: a per-player broadcast would
    /// be N full roster JSONs all carrying the same snapshot.</para></summary>
    public int MarkConnectedPlayersAsParticipants() => SetParticipantsForAll(true);

    /// <summary>Clears the ledger when a match closes (§10.2); returns the number of affected
    /// records. Same single-broadcast rule as <see cref="MarkConnectedPlayersAsParticipants"/>.</summary>
    public int ClearMatchParticipants() => SetParticipantsForAll(false);

    private int SetParticipantsForAll(bool value)
    {
        var affected = 0;
        PlayerState? last = null;
        lock (_gate)
        {
            foreach (var state in _players.Values)
            {
                if (state.Role != "player" || state.MatchParticipant == value) continue;
                // Only CONNECTED players enter the ledger when marking (§10.2); everyone on clear.
                if (value && !state.IsConnected) continue;
                state.MatchParticipant = value;
                last = state;
                affected++;
            }
        }
        if (last != null) Changed?.Invoke(last, PlayerChangeKind.Updated);
        return affected;
    }

    /// <summary>Deletes <c>Left</c> records at match close (their playerIds return to the pool);
    /// returns the number of deleted records.
    /// <para>⚠️ The call timing is binding (§10.2): the ledger stands through the WHOLE
    /// <c>finished</c> phase and closes only <b>on the way back to the lobby</b>, AFTER the
    /// <c>return_to_lobby</c> broadcast. Deleting at <c>match_end</c> would empty the end-of-match
    /// table exactly while it is being read — showing departed players there is why this exists.</para></summary>
    public int PurgeLeftParticipants()
    {
        var purged = new List<PlayerState>();
        lock (_gate)
        {
            foreach (var state in _players.Values)
            {
                if (state.Connection != PlayerConnection.Left) continue;
                _players.TryRemove(state.DeviceId, out _);
                purged.Add(state);
            }
        }
        // Outside the lock: each Changed triggers a lobby_state broadcast (never send under a lock).
        foreach (var state in purged) Changed?.Invoke(state, PlayerChangeKind.Removed);
        return purged.Count;
    }

    /// <summary>
    /// Kick (§5.4): <b>deletes the record entirely</b> from the roster and hands its connection (if
    /// any) to the caller, whose job is to close it. That is the difference from a drop — a dropped
    /// device <b>stays</b> in the list as <c>reconnecting</c> (so the same headset keeps its playerId
    /// and name), a kicked device leaves it. Otherwise kicking would do nothing for a disconnected
    /// record and the operator would keep pressing "KICK" on a row that stays.
    /// <para>⚠️ Kicking <b>also removes participation</b> (§10.2): the record is gone, so the
    /// <c>MatchParticipant</c> flag goes with it — no extra clearing needed. A deliberately kicked
    /// player is not listed in the end-of-match table either.</para>
    /// <para>⚠️ Does NOT touch <c>devices.json</c>: a kick is not a ban, the device keeps its name
    /// and jersey number on reconnect (the playerId returns to the pool and a new one is given).</para>
    /// </summary>
    public bool RemoveByPlayerId(int playerId, out PlayerState state, out ClientConnection? connection)
    {
        lock (_gate)
        {
            if (!TryGetByPlayerId(playerId, out state!))
            {
                connection = null;
                return false;
            }

            connection = state.Socket;
            state.Socket = null;
            state.Connection = PlayerConnection.Left;
            state.Ready = false;
            state.MatchParticipant = false;
            _players.TryRemove(state.DeviceId, out _);
        }

        Changed?.Invoke(state, PlayerChangeKind.Removed);
        return true;
    }

    /// <summary>
    /// Connection sweep (§8) — <b>two stages</b>:
    /// (a) <c>Connected</c> but no status for HEARTBEAT_TIMEOUT → the socket is considered dead, it
    /// is aborted and the device drops to <c>Reconnecting</c>;
    /// (b) <c>Reconnecting</c> lasted RECONNECT_GRACE → the player leaves the game: <c>Left</c> if a
    /// match participant (record stays until match end, §10.2), otherwise the record is deleted and
    /// the playerId returns to the pool.
    /// <para>An admin is deleted entirely in both stages (§2 — <see cref="RetireLocked"/>).</para>
    /// <para>⚠️ No send and no event under the lock: the work list is collected first, <c>Abort</c>
    /// and <c>Changed</c> run outside it.</para>
    /// </summary>
    private void CheckConnections()
    {
        var now = DateTime.UtcNow;
        var heartbeat = TimeSpan.FromSeconds(ArenaProtocol.HEARTBEAT_TIMEOUT);
        var grace = TimeSpan.FromSeconds(ArenaProtocol.RECONNECT_GRACE);
        var pending = new List<(PlayerState State, ClientConnection? Socket, PlayerChangeKind Kind)>();
        lock (_gate)
        {
            foreach (var state in _players.Values)
            {
                if (state.IsConnected)
                {
                    if (now - state.LastSeen <= heartbeat) continue;

                    state.Connection = PlayerConnection.Reconnecting;
                    state.DisconnectedAt = now;
                    state.Ready = false;
                    var socket = state.Socket;
                    state.Socket = null;
                    pending.Add((state, socket,
                        RetireLocked(state) ? PlayerChangeKind.Removed : PlayerChangeKind.Reconnecting));
                    continue;
                }

                if (state.Connection != PlayerConnection.Reconnecting) continue;
                if (now - state.DisconnectedAt <= grace) continue;

                if (state.MatchParticipant)
                {
                    // The record STAYS: its name and counters must show in the end-of-match table (§10.2).
                    state.Connection = PlayerConnection.Left;
                    pending.Add((state, null, PlayerChangeKind.Left));
                    continue;
                }

                _players.TryRemove(state.DeviceId, out _);
                pending.Add((state, null, PlayerChangeKind.Removed));
            }
        }
        foreach (var (state, socket, kind) in pending)
        {
            socket?.Abort();
            Changed?.Invoke(state, kind);
        }
    }

    /// <summary>
    /// Retires a dropped record. <b>An admin record is deleted entirely</b> (its deviceId is
    /// per-session — §2: a returning admin arrives with a new identity, so the old row would linger
    /// as a ghost and its playerId would never return to the pool). A player record STAYS: its
    /// deviceId is persistent and the same headset must keep its playerId and name.
    /// <para>true = deleted. The caller must hold the <c>_gate</c> lock.</para>
    /// </summary>
    private bool RetireLocked(PlayerState state)
    {
        if (state.Role != "admin") return false;
        _players.TryRemove(state.DeviceId, out _);
        return true;
    }

    public void Dispose() => _connectionTimer.Dispose();

    // ---- playerId / team / token allocation ----

    private int NextFreePlayerIdLocked()
    {
        var used = new HashSet<int>();
        foreach (var state in _players.Values) used.Add(state.PlayerId);
        for (var id = 1; id <= ArenaProtocol.PLAYER_ID_MAX; id++)
            if (!used.Contains(id)) return id;
        return 0; // u8 pool exhausted (wire format ceiling, not a product quota)
    }

    /// <summary>Puts a new player on the smaller team (red on a tie); admin overrides via set_team.</summary>
    private string SmallerTeamLocked()
    {
        int red = 0, blue = 0;
        foreach (var state in _players.Values)
        {
            if (state.Role != "player") continue;
            if (state.Team == "red") red++;
            else if (state.Team == "blue") blue++;
        }
        return red <= blue ? "red" : "blue";
    }

    private static uint NextUdpToken()
    {
        Span<byte> bytes = stackalloc byte[4];
        uint token;
        do
        {
            Random.Shared.NextBytes(bytes);
            token = BitConverter.ToUInt32(bytes);
        } while (token == 0);
        return token;
    }

    // ---- devices.json identity persistence (name + number; UTF-8 without BOM) ----

    /// <summary>Reads devices.json. <b>Accepts both shapes:</b> v1 <c>deviceId → "name"</c> (number
    /// treated as 0, assigned on first connection) and v2 <c>deviceId → {name, number}</c>.
    /// Called only from the constructor (single thread).</summary>
    private void LoadDevices()
    {
        _devices = new();
        if (!File.Exists(_devicesPath)) return;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_devicesPath));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

            foreach (var entry in doc.RootElement.EnumerateObject())
            {
                switch (entry.Value.ValueKind)
                {
                    case JsonValueKind.String: // v1 — name only, number assigned later
                        _devices[entry.Name] = new DeviceRecord { Name = entry.Value.GetString() ?? "" };
                        break;
                    case JsonValueKind.Object: // v2 — name + number
                        _devices[entry.Name] = new DeviceRecord
                        {
                            Name = entry.Value.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                                ? n.GetString() ?? ""
                                : "",
                            Number = entry.Value.TryGetProperty("number", out var num) && num.TryGetInt32(out var parsed)
                                ? parsed
                                : 0
                        };
                        break;
                }
            }
            ResolveDuplicateNumbersLocked();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PlayerRegistry] devices.json okunamadı ({ex.Message}) — boş kimlik haritasıyla başlanıyor.");
            _devices = new();
        }
    }

    /// <summary>Resolves duplicate numbers at load: the first record keeps the number, later ones
    /// move to the first free one. The reason is hand-edited files — the "two devices never share a
    /// number" invariant belongs to this class, not to the file, so it is enforced on entry.</summary>
    private void ResolveDuplicateNumbersLocked()
    {
        var seen = new HashSet<int>();
        var repaired = 0;
        foreach (var record in _devices.Values)
        {
            if (record.Number == 0) continue;
            if (seen.Add(record.Number)) continue;

            record.Number = NextFreeNumberLocked();
            if (record.Number != 0) seen.Add(record.Number);
            repaired++;
        }
        if (repaired == 0) return;

        Console.WriteLine($"[PlayerRegistry] devices.json'da {repaired} çift numara bulundu — yeniden numaralandı.");
        SaveDevicesLocked();
    }

    /// <summary>
    /// <b>Player:</b> with a devices.json record, name+number come from there (persistent identity);
    /// otherwise the name is picked RANDOMLY from the pool, the number is the first free one from 1,
    /// and both are persisted. A record upgraded from v1 (no number) gets one on first sight.
    /// <para>
    /// <b>Admin:</b> name from `hello.deviceName` (PC name; "Admin" if empty), NO number (0) —
    /// admins do not play. NOT persisted: the admin deviceId is per-session, so every launch would
    /// add a junk row. If another connected admin uses the same name, " (2)", " (3)"… is appended.
    /// </para>
    /// </summary>
    private void ResolveIdentityLocked(PlayerState state, string? fallbackDeviceName)
    {
        if (state.Role == "admin")
        {
            state.Name = UniqueAdminNameLocked(state.DeviceId, fallbackDeviceName);
            state.Number = 0;
            return;
        }

        if (_devices.TryGetValue(state.DeviceId, out var record) && !string.IsNullOrWhiteSpace(record.Name))
        {
            state.Name = record.Name;
            state.Number = record.Number;
            if (state.Number != 0) return;

            state.Number = NextFreeNumberLocked(); // record upgraded from v1
            record.Number = state.Number;
            SaveDevicesLocked();
            return;
        }

        state.Name = PickPoolNameLocked();
        state.Number = NextFreeNumberLocked();
        _devices[state.DeviceId] = new DeviceRecord { Name = state.Name, Number = state.Number };
        SaveDevicesLocked();
    }

    /// <summary>RANDOM name from the pool (§2): first among names no registered device uses, and if
    /// all are taken, from the whole pool. <b>Names are not unique</b> — the number is the
    /// distinguishing field, so an exhausted pool is normal operation, not an error.</summary>
    private string PickPoolNameLocked()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in _devices.Values)
            if (!string.IsNullOrEmpty(record.Name)) taken.Add(record.Name);

        var free = new List<string>(NamePool.Length);
        foreach (var candidate in NamePool)
            if (!taken.Contains(candidate)) free.Add(candidate);

        return free.Count > 0
            ? free[Random.Shared.Next(free.Count)]
            : NamePool[Random.Shared.Next(NamePool.Length)];
    }

    /// <summary>First number from 1 that no REGISTERED device uses (§2). Sequential, not random, so
    /// the venue works with small memorable numbers. <c>0</c> = pool full (100+ registered devices) —
    /// the device stays numberless and the operator assigns one manually.</summary>
    private int NextFreeNumberLocked()
    {
        var used = new HashSet<int>();
        foreach (var record in _devices.Values)
            if (record.Number != 0) used.Add(record.Number);

        for (var n = ArenaProtocol.PLAYER_NUMBER_MIN; n <= ArenaProtocol.PLAYER_NUMBER_MAX; n++)
            if (!used.Contains(n)) return n;

        Console.WriteLine($"[PlayerRegistry] {ArenaProtocol.PLAYER_NUMBER_MIN}-{ArenaProtocol.PLAYER_NUMBER_MAX} " +
                          $"numara havuzu dolu ({_devices.Count} kayıtlı cihaz) — yeni cihaz numarasız (0) kalıyor.");
        return 0;
    }

    /// <summary>deviceId holding the given number (excluding itself), or null. The lookup goes
    /// against <c>_devices</c>, NOT <c>_players</c>: the number may be held by a device that never
    /// connected (no in-memory PlayerState).</summary>
    private string? FindNumberHolderLocked(int number, string exceptDeviceId)
    {
        foreach (var entry in _devices)
            if (entry.Value.Number == number && entry.Key != exceptDeviceId) return entry.Key;
        return null;
    }

    /// <summary>First name no other record uses ("Ofis-PC", "Ofis-PC (2)", …).</summary>
    private string UniqueAdminNameLocked(string deviceId, string? fallbackDeviceName)
    {
        var baseName = string.IsNullOrWhiteSpace(fallbackDeviceName) ? "Admin" : fallbackDeviceName!.Trim();

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var state in _players.Values)
        {
            // Our own record (reconnect) frees its name; player names must not clash either.
            if (state.DeviceId == deviceId) continue;
            if (!string.IsNullOrEmpty(state.Name)) taken.Add(state.Name);
        }

        if (!taken.Contains(baseName)) return baseName;
        for (var n = 2; ; n++)
        {
            var candidate = $"{baseName} ({n})";
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    private void SaveDevicesLocked()
    {
        try
        {
            var dir = Path.GetDirectoryName(_devicesPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_devicesPath, JsonSerializer.Serialize(_devices, DevicesJsonOptions), Utf8NoBom);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PlayerRegistry] devices.json yazılamadı: {ex.Message}");
        }
    }
}
