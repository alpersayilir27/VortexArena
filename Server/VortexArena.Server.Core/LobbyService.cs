#nullable enable
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>Lobby semantics: hello→welcome, a FULL lobby_state snapshot to everyone on every roster
/// change, and handling of set_identity/set_ready/set_team/kick (§5).
/// <para>
/// Roster broadcasts leave through <b>a single publisher loop</b> (<c>MarkRosterDirty</c>) and each
/// one bumps <c>lobby_state.version</c>; a lagging <c>status.rosterVersion</c> gets a full snapshot
/// sent to that client only (§5.1/5.3).
/// </para>
/// <para>
/// Also owns the <b>state shared between admins</b> (§5.3 <c>admin_state</c>): the next match's
/// mode/map selection lives here — the admin UI pickers mutate this, not a local variable
/// (<c>set_selection</c>), and the server echoes the change to ALL admins so two operators see the
/// same screen. View preferences (camera, rings, transparency) do NOT belong here — they stay on
/// each admin's own machine.
/// </para></summary>
public sealed class LobbyService
{
    private readonly PlayerRegistry _registry;
    private readonly MatchDirector _director;

    /// <summary>Guards the shared selection; WS handlers can arrive on different threads.</summary>
    private readonly object _selectionGate = new();

    /// <summary>Shared selection (§5.3). ⚠️ <b>Never empty:</b> seeded in the constructor with the
    /// venue's lobby map (the open scene's initial value, §10.7), and it can never empty again
    /// because <see cref="ApplySelection"/> ignores empty fields. Without the seed the first
    /// <c>admin_state</c> would say "no map selected" and the panel would get the data for its venue
    /// filter late.</summary>
    private string _selectedModeId;
    private string _selectedSceneName;

    // ---- Roster publisher (§5.3) ----
    // SINGLE publisher guarantee: no two lobby_state productions run at once. Calling
    // BroadcastLobbyStateAsync directly from OnRegistryChanged would open one task per change; each
    // takes its own Snapshot() at a different instant and races for ClientConnection's send
    // semaphore. The semaphore keeps frames from interleaving but does NOT make the NEWER one win →
    // an older roster can be written last and "a kicked player stays online in the list".
    private readonly object _broadcastGate = new();
    private bool _rosterDirty;
    private bool _broadcasting;
    private int _rosterVersion;

    /// <summary>Shared parameters of the next match (§5.2); <c>0</c> = unset, the mode default is
    /// used. Travels on the SAME channel as mode/map: kept local, a match one operator believed was
    /// 5 min would start with the 30 min the other picked.</summary>
    private int _selectedRoundSeconds;

    /// <summary>Shared score/round limit: <c>0</c> = unset (mode default), <c>&gt; 0</c> = that
    /// value, <see cref="ArenaProtocol.SCORE_LIMIT_UNLIMITED"/> = unlimited.</summary>
    private int _selectedScoreLimit;

    /// <summary>Shared countdown selection (§5.2); 0 = unset → COUNTDOWN_SECONDS.</summary>
    private int _selectedCountdownSeconds;

    /// <summary>Calibration mode (§5.2/§10.6) — <b>effective state, not a selection</b>: same class
    /// as friendly fire, it bypasses the selection lock and can change during a running match. Read
    /// and written only under <c>_selectionGate</c> (same lock as the other shared fields).</summary>
    private string _calibrationMode = ArenaProtocol.CALIB_MODE_TWO_ANCHOR;

    public LobbyService(PlayerRegistry registry, MatchDirector director)
    {
        _registry = registry;
        _director = director;
        // Initial selection = the open scene's initial value (§10.7): the venue's lobby map. If the
        // server cannot resolve the lobby it does not start at all (§11 fail-fast), so this is never
        // empty in practice.
        _selectedSceneName = director.LobbyScene;
        _selectedModeId = director.LobbyScene.Length > 0 ? ArenaProtocol.LOBBY_MODE_ID : "";
        _registry.Changed += OnRegistryChanged;
    }

    private void OnRegistryChanged(PlayerState state, PlayerChangeKind kind)
    {
        MarkRosterDirty();

        // Admin arrived/left → adminCount changed, refresh the remaining admins.
        if (state.Role == "admin" && kind != PlayerChangeKind.Updated)
        {
            var verb = kind switch
            {
                PlayerChangeKind.Added => "bağlandı",
                PlayerChangeKind.Reconnected => "yeniden bağlandı",
                _ => "ayrıldı"
            };
            _ = BroadcastAdminStateAsync($"{state.Name} {verb}");
        }
    }

    /// <summary>hello → registration + welcome (carrying current match state; late-join sync §5.3).
    /// The lobby_state broadcast is triggered via Announce AFTER welcome is sent.</summary>
    public async Task HandleHelloAsync(ClientConnection connection, HelloMsg hello)
    {
        if (hello.protocolVersion != ArenaProtocol.PROTOCOL_VERSION)
            Console.WriteLine($"[Lobby] protokol sürüm uyumsuzluğu: istemci {hello.protocolVersion}, sunucu {ArenaProtocol.PROTOCOL_VERSION} — devam ediliyor.");

        if (!_registry.TryRegisterHello(hello, connection, out var state, out var kind))
        {
            Console.WriteLine($"[Lobby] playerId havuzu tükendi ({ArenaProtocol.PLAYER_ID_MAX}) — {hello.deviceName} reddedildi.");
            await SendSafeAsync(connection, JsonUtil.Serialize(new KickedMsg { reason = "Sunucu dolu" }), "(dolu)");
            // A rejection is a kick too (§5.4): an abortive close would drop `kicked` and put the
            // client into an endless reconnect loop.
            _ = connection.CloseAfterKickAsync();
            return;
        }
        connection.State = state;

        var welcome = new WelcomeMsg
        {
            protocolVersion = ArenaProtocol.PROTOCOL_VERSION,
            playerId = state.PlayerId,
            udpToken = state.UdpToken,
            // §10.6: the player gets the mode ONCE, here — the decision it gates (restore the anchor
            // from disk at startup?) is already made when welcome arrives, so there is no moment
            // where a live propagation could apply.
            calibrationMode = CurrentCalibrationMode(),
            match = _director.CurrentMatchInfo()
        };
        await SendSafeAsync(connection, JsonUtil.Serialize(welcome), state.Name);

        // §10.10: to THIS connection only — a late joiner must not see covers others already broke as
        // intact. null = the staged scene has no network objects.
        var worldState = _director.BuildWorldStateJson();
        if (worldState != null) await SendSafeAsync(connection, worldState, state.Name);

        // A late-joining admin gets the shared selection right after welcome (§5.3): its panel must
        // show the mode/map the other operator picked, not its own default.
        if (state.Role == "admin")
            await SendSafeAsync(connection, BuildAdminStateJson(""), state.Name);

        // The selected mode's team mode goes out REGARDLESS OF ROLE (§5.3): players draw the base
        // strips in the lobby from it and must see it correctly on connect — waiting for a broadcast
        // would leave it wrong until the next selection change.
        await SendSafeAsync(connection, BuildSelectionStateJson(), state.Name);

        // A late join also enters the match ledger (§10.2) — BEFORE Announce: otherwise the first
        // lobby_state row goes out as `inMatch:false` and the UI draws the end-of-match scope short
        // for one broadcast. No-op when no match is running.
        _director.MarkParticipantIfMatchRunning(state);

        _registry.Announce(state, kind); // console line + lobby_state broadcast

        // Violations already open replay to a late admin (§5.3) — AFTER Announce, so the feed can
        // name the player from the roster it just received.
        if (state.Role == "admin")
        {
            foreach (var json in _director.BuildOpenViolationJsons())
                await SendSafeAsync(connection, json, state.Name);
        }
    }

    /// <summary>status heartbeat (§5.1): updates device state and performs <b>roster
    /// reconciliation</b> — if the client's <c>rosterVersion</c> lags, a full lobby_state goes to
    /// THAT client only. A safety net, not the primary path: the control channel is TCP so
    /// broadcasts do not "get lost"; this closes the windows where a client could not apply one
    /// (scene transition, moment of disconnect).</summary>
    /// <summary>Serialised once: the reply never changes.</summary>
    private static readonly string HeartbeatJson = JsonUtil.Serialize(new HeartbeatMsg());

    public async Task HandleStatusAsync(ClientConnection connection, StatusMsg msg)
    {
        var state = connection.State;
        if (state == null) return;

        _registry.UpdateStatus(state.DeviceId, msg);

        // §8: the client's link watchdog needs a guaranteed periodic frame — every status is answered,
        // whether or not the roster below has anything to say.
        await SendSafeAsync(connection, HeartbeatJson, state.Name);

        if (msg.rosterVersion >= Volatile.Read(ref _rosterVersion)) return;

        await SendSafeAsync(connection, BuildLobbyStateJson(), state.Name);
    }

    /// <summary>set_identity (§5.1): name and/or jersey number. Authorization is in ClientConnection
    /// (a player only itself, an admin anyone). A rejected number is reported to the operator via
    /// admin_state.notice — swallowing it silently would hide a number they believe they set.</summary>
    public void HandleSetIdentity(ClientConnection connection, SetIdentityMsg msg)
    {
        if (connection.State == null) return;
        var playerId = msg.playerId != 0 ? msg.playerId : connection.State.PlayerId;

        if (_registry.SetIdentity(playerId, msg.name, msg.number, out var error))
        {
            if (_registry.TryGetByPlayerId(playerId, out var target))
            {
                var label = target.Number > 0 ? $"{target.Number} · {target.Name}" : target.Name;
                Console.WriteLine($"[Lobby] set_identity: playerId {playerId} -> {label}.");
                if (connection.IsAdmin) _ = BroadcastAdminStateAsync(Notice(connection, $"kimlik -> {label}"));
            }
            return;
        }

        if (string.IsNullOrEmpty(error)) return; // nothing changed — stay silent
        Console.WriteLine($"[Lobby] set_identity reddedildi (playerId {playerId}): {error}.");
        if (connection.IsAdmin) _ = BroadcastAdminStateAsync(Notice(connection, $"kimlik reddedildi — {error}"));
    }

    public void HandleSetReady(ClientConnection connection, SetReadyMsg msg)
    {
        if (connection.State == null) return;
        _registry.SetReady(connection.State.DeviceId, msg.ready);
    }

    public void HandleSetTeam(ClientConnection connection, SetTeamMsg msg)
    {
        if (msg.team != "red" && msg.team != "blue")
        {
            Console.WriteLine($"[Lobby] set_team geçersiz takım '{msg.team}' — yok sayıldı.");
            return;
        }
        if (!_registry.TryGetByPlayerId(msg.playerId, out var target))
        {
            Console.WriteLine($"[Lobby] set_team: playerId {msg.playerId} bulunamadı.");
            return;
        }
        if (target.Role != "player")
        {
            Console.WriteLine($"[Lobby] set_team: {target.Name} admin — takım atanmaz.");
            return;
        }
        _registry.SetTeam(msg.playerId, msg.team);
        if (connection.IsAdmin)
            _ = BroadcastAdminStateAsync(Notice(connection, $"{target.Name} -> {msg.team}"));
    }

    public async Task HandleKickAsync(ClientConnection connection, KickMsg msg)
    {
        // A kick DELETES the record from the roster (§5.4) — the only way it can also act on a
        // disconnected record; just closing the socket would leave a connection-less row in the list.
        // The participant flag is not cleared separately: the record is gone entirely, so a kicked
        // player is absent from the end-of-match table too (§10.2).
        if (!_registry.RemoveByPlayerId(msg.playerId, out var target, out var targetConnection))
        {
            Console.WriteLine($"[Lobby] kick: playerId {msg.playerId} bulunamadı.");
            return;
        }
        var how = targetConnection == null ? "bağlantısı yoktu, kayıt silindi" : "bağlantı kapatılıyor";
        Console.WriteLine($"[Lobby] kick: {target.Name} (playerId {target.PlayerId}) — {how}.");
        await BroadcastAdminStateAsync(Notice(connection, $"{target.Name} atıldı"));
        if (targetConnection == null) return;
        await SendSafeAsync(targetConnection, JsonUtil.Serialize(new KickedMsg { reason = "" }), target.Name);
        // ⚠️ NOT Abort: an RST can drop the `kicked` frame before the client reads it, and then the
        // kicked headset would treat the drop as an ordinary outage and reconnect (§5.4).
        // Not awaited: this must not tie up the (admin) connection's receive loop.
        _ = targetConnection.CloseAfterKickAsync(); // recv loop close → Offline + lobby_state broadcast
    }

    // ---- Calibration state (§10.6) ----

    /// <summary>set_calibration: the headset reports its OWN alignment (§5.1). It can only write its
    /// own record — no playerId on the wire, it is resolved from the connection.
    /// <para>If the reported floor offset exceeds the threshold, the operator is warned (§10.6).
    /// ⚠️ The warning is NOT tied to a roster change: recalibrating with the same offset leaves the
    /// record untouched, yet the operator must hear the result of every manual calibration.</para>
    /// <para>⚠️ A non-empty <c>error</c> means the saved alignment could NOT be reloaded:
    /// <c>calibrated</c>/<c>source</c>/<c>floorOffset</c> are IGNORED, the stored calibration stands,
    /// and the reason is written to the roster and announced to the operator (§10.6, exactly the
    /// <c>set_body_scale.error</c> contract).</para></summary>
    public void HandleSetCalibration(ClientConnection connection, SetCalibrationMsg msg)
    {
        var state = connection.State;
        if (state == null) return;

        var error = msg.error ?? "";
        if (error.Length > 0)
        {
            _registry.SetCalibrationError(state.PlayerId, error);
            Console.WriteLine($"[Lobby] set_calibration: {state.Name} yeniden yüklenemedi — {error}.");
            _ = BroadcastCalibrationResultAsync(state.PlayerId, false, error);
            // Not wrapped in Notice(): the actor is the player's headset, not an admin.
            _ = BroadcastAdminStateAsync($"⚠ Kalibrasyon {state.Name}: {error}");
            return;
        }

        if (_registry.SetCalibration(state.PlayerId, msg.calibrated, msg.source, msg.floorOffset))
        {
            var what = msg.calibrated ? $"kalibre oldu ({msg.source})" : "kalibrasyonunu bıraktı";
            Console.WriteLine($"[Lobby] set_calibration: {state.Name} {what}.");
        }

        // ⚠️ NOT tied to SetCalibration's return: for an already-calibrated player it returns `false`
        // (nothing changed in the roster) and that is exactly when the operator's button needs an
        // answer — otherwise a successful reload would show "loading" forever (§5.3).
        if (msg.calibrated) _ = BroadcastCalibrationResultAsync(state.PlayerId, true, "");

        if (!msg.calibrated || Math.Abs(msg.floorOffset) <= ArenaProtocol.CALIB_FLOOR_WARN_METERS) return;
        Console.WriteLine($"[Lobby] zemin sapması: {state.Name} {msg.floorOffset:F2} m " +
                          $"(eşik {ArenaProtocol.CALIB_FLOOR_WARN_METERS:F2} m) — alan verisi temizliği önerilir.");
        // ⚠️ Not wrapped in Notice(): that helper builds "<admin name>: <action>", but here the actor
        // is the player's headset, not the admin who sent a command.
        _ = BroadcastAdminStateAsync(
            $"⚠ {state.Name}: zemin sapması {msg.floorOffset:F2} m — gözlükte alan verisi temizliği önerilir");
    }

    /// <summary>
    /// clear_calibration: an admin resets a player's calibration (playerId 0 = EVERYONE) (§5.2).
    /// The admin can only RESET — the "calibrated" mark is set by the headset alone (§10.6), because
    /// only it knows the alignment landed.
    /// <para>
    /// Two things happen: (1) <c>calibrated</c> is lowered in the roster, (2) <c>clear_calibration</c>
    /// is forwarded to the target headset. Without the second the reset is incomplete — the headset
    /// is what erases the alignment, the saved anchor and a half-finished manual sequence.
    /// </para>
    /// <para>
    /// The forwarded payload carries NO <c>playerId</c> (the target is that connection) but does
    /// carry <c>keepSaved</c>: the operator's chosen mode must reach the headset — <c>true</c> only
    /// invalidates the alignment (the device anchor stays, so a following <c>reload_calibration</c>
    /// works), <c>false</c> deletes the device record too.
    /// </para>
    /// <para>
    /// ⚠️ <b>The roster side is IDENTICAL in both modes</b> (<see cref="PlayerRegistry.SetCalibration"/>
    /// with <c>calibrated:false</c>): the mode only concerns the record on the headset's device.
    /// </para>
    /// <para>
    /// ⚠️ <b>Forwarding is NOT tied to the player's state.</b>
    /// <see cref="PlayerRegistry.SetCalibration"/>'s return value is not a gate: <c>false</c> only
    /// means "nothing changed in the roster" (the guard against a useless <c>lobby_state</c>
    /// broadcast, §5.3). Some of what must be reset never shows in the roster — a <b>half-finished
    /// manual calibration</b> (A taken, B not) exists precisely while <c>calibrated</c> is still
    /// <c>false</c>. Returning early on that value would disable the command in the very case it
    /// exists for (§10.6).
    /// </para>
    /// </summary>
    public async Task HandleClearCalibrationAsync(ClientConnection connection, ClearCalibrationMsg msg)
    {
        // Server → client carries no `playerId` (the target is that connection) but does carry
        // `keepSaved`: the operator picks the mode and the headset learns it from the wire.
        var payload = JsonUtil.Serialize(new ClearCalibrationMsg { keepSaved = msg.keepSaved });
        // The two modes are distinguished in the console and the admin notice: the operator must be
        // able to tell which button was pressed from the result line too.
        var kindAll = msg.keepSaved
            ? "hizalamalar geçersiz kılındı (cihaz kayıtları duruyor)"
            : "hizalamalar sıfırlandı, cihaz kayıtları da silindi";
        var kindOne = msg.keepSaved
            ? "hizalaması geçersiz kılındı (cihaz kaydı duruyor)"
            : "kalibrasyonu sıfırlandı, cihaz kaydı da silindi";

        if (msg.playerId == 0)
        {
            var sent = 0;
            foreach (var state in _registry.Snapshot())
            {
                if (state.Role != "player") continue;
                // Uncalibrated players are NOT skipped: a player whose flag is already `false` may be
                // mid-sequence, and the bulk reset must reach them too.
                _registry.SetCalibration(state.PlayerId, false, null);
                if (state.Socket == null) continue;
                await SendSafeAsync(state.Socket, payload, state.Name);
                sent++;
            }

            Console.WriteLine($"[Lobby] clear_calibration: TÜM oyuncular — {kindAll} " +
                              $"({sent} başlığa iletildi) — {connection.State?.Name}.");
            await BroadcastAdminStateAsync(Notice(connection, $"tüm {kindAll} ({sent} oyuncu)"));
            return;
        }

        if (!_registry.TryGetByPlayerId(msg.playerId, out var target))
        {
            Console.WriteLine($"[Lobby] clear_calibration: playerId {msg.playerId} bulunamadı.");
            return;
        }
        if (target.Role != "player")
        {
            Console.WriteLine($"[Lobby] clear_calibration: {target.Name} admin — kalibrasyon yok, yok sayıldı.");
            return;
        }

        _registry.SetCalibration(target.PlayerId, false, null);
        if (target.Socket != null)
        {
            await SendSafeAsync(target.Socket, payload, target.Name);
        }
        var reach = target.Socket != null ? "iletildi" : "bağlantısı yok, yalnız kayıt sıfırlandı";
        Console.WriteLine($"[Lobby] clear_calibration: {target.Name} (playerId {target.PlayerId}) — " +
                          $"{kindOne}, {reach}, {connection.State?.Name}.");
        await BroadcastAdminStateAsync(Notice(connection, $"{target.Name} {kindOne}"));
    }

    /// <summary>
    /// reload_calibration: an admin makes a player's headset (playerId 0 = EVERYONE) reload its
    /// alignment from the anchor SAVED on the device (§5.2). The server computes nothing — it
    /// forwards a field-less reload_calibration, the headset attempts it and replies with
    /// <c>set_calibration</c> (same round-trip pattern as measure_body_scale).
    /// <para>⚠️ <b>The admin does NOT thereby declare "calibrated"</b> (§10.6 asymmetric writer
    /// table): it only starts the attempt, the mark is still set by the headset.</para>
    /// <para>⚠️ <b>Uncalibrated targets are NOT skipped</b> — measure_body_scale's calibration gate
    /// has NO counterpart here and must not gain one: the command exists precisely for players whose
    /// alignment is missing or broken, so a gate would disable it in its only use case.</para>
    /// </summary>
    public async Task HandleReloadCalibrationAsync(ClientConnection connection, ReloadCalibrationMsg msg)
    {
        // No fields in the server → client direction: the target is that connection.
        var payload = JsonUtil.Serialize(new ReloadCalibrationMsg());

        if (msg.playerId == 0)
        {
            var sent = 0;
            foreach (var state in _registry.Snapshot())
            {
                if (state.Role != "player" || state.Socket == null) continue;
                await SendSafeAsync(state.Socket, payload, state.Name);
                sent++;
            }

            Console.WriteLine($"[Lobby] reload_calibration: {sent} oyuncu — {connection.State?.Name}.");
            await BroadcastAdminStateAsync(Notice(connection, $"{sent} oyuncunun kalibrasyonu yeniden yükleniyor"));
            return;
        }

        if (!_registry.TryGetByPlayerId(msg.playerId, out var target) || target.Socket == null)
        {
            Console.WriteLine($"[Lobby] reload_calibration: playerId {msg.playerId} bulunamadı/bağlantısı yok.");
            return;
        }
        if (target.Role != "player")
        {
            Console.WriteLine($"[Lobby] reload_calibration: {target.Name} admin — kalibrasyon yok, yok sayıldı.");
            return;
        }

        await SendSafeAsync(target.Socket, payload, target.Name);
        Console.WriteLine($"[Lobby] reload_calibration: {target.Name} (playerId {target.PlayerId}) — {connection.State?.Name}.");
        await BroadcastAdminStateAsync(Notice(connection, $"{target.Name} kalibrasyonu yeniden yükleniyor"));
    }

    /// <summary>Sends the result of a reload attempt to connected admins ONLY (§5.3).
    /// <para>An EVENT, not state: state travels with the roster. The server keeps no pending-request
    /// ledger — every success/failure report from a headset produces one line and the admin UI does
    /// the matching.</para></summary>
    public async Task BroadcastCalibrationResultAsync(int playerId, bool ok, string error)
    {
        try
        {
            var admins = _registry.ConnectedAdminConnections();
            // Nobody watching, no serialization: the operator screen is the only consumer.
            if (admins.Count == 0) return;
            var json = JsonUtil.Serialize(new CalibrationResultMsg
            {
                playerId = playerId,
                ok = ok,
                error = error ?? ""
            });
            foreach (var connection in admins)
                await SendSafeAsync(connection, json, "(admin)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Lobby] calibration_result yayını hatası: {ex.Message}");
        }
    }

    // ---- Body scale (§10.8) ----

    /// <summary>set_body_scale: the headset reports its OWN body scale (§5.1). It can only write its
    /// own record — no playerId on the wire, it is resolved from the connection (same contract as
    /// set_calibration).
    /// <para>The server does not interpret the number; clamping lives in
    /// <see cref="PlayerRegistry.SetBodyScale"/>.</para>
    /// <para>⚠️ A non-empty <c>error</c> means the measurement failed: <c>scale</c> is IGNORED, the
    /// stored scale stands, and the reason is written to the roster and announced to the operator
    /// (§10.8). Swallowing it silently would leave the operator with "I pressed it, nothing
    /// happened".</para></summary>
    public void HandleSetBodyScale(ClientConnection connection, SetBodyScaleMsg msg)
    {
        var state = connection.State;
        if (state == null) return;

        var error = msg.error ?? "";
        if (error.Length > 0)
        {
            _registry.SetScaleError(state.PlayerId, error);
            Console.WriteLine($"[Lobby] set_body_scale: {state.Name} ölçülemedi — {error}.");
            // Not wrapped in Notice(): the actor is the player's headset, not an admin.
            _ = BroadcastAdminStateAsync($"⚠ Ölçüm {state.Name}: {error}");
            return;
        }

        if (!_registry.SetBodyScale(state.PlayerId, msg.scale)) return;
        Console.WriteLine($"[Lobby] set_body_scale: {state.Name} → {state.BodyScale:F3}.");
    }

    /// <summary>
    /// measure_body_scale: an admin has a player's body measured (playerId 0 = EVERYONE) (§5.2).
    /// The server computes nothing — it forwards a field-less measure_body_scale, the headset
    /// measures and replies with <c>set_body_scale</c> (same round-trip pattern as
    /// reload_calibration).
    /// <para>⚠️ <b>Not forwarded to an uncalibrated target:</b> the measurement is relative to the
    /// arena floor and an uncalibrated headset does not know the floor — the command would silently
    /// write a wrong scale. Skipped targets are announced to the operator; swallowing that means "I
    /// pressed it but nothing happened".</para>
    /// </summary>
    public async Task HandleMeasureBodyScaleAsync(ClientConnection connection, MeasureBodyScaleMsg msg)
    {
        // No fields in the server → client direction: the target is that connection.
        var payload = JsonUtil.Serialize(new MeasureBodyScaleMsg());

        if (msg.playerId == 0)
        {
            var sent = 0;
            var skipped = 0;
            foreach (var state in _registry.Snapshot())
            {
                if (state.Role != "player" || state.Socket == null) continue;
                if (!state.Calibrated) { skipped++; continue; }
                await SendSafeAsync(state.Socket, payload, state.Name);
                sent++;
            }

            Console.WriteLine($"[Lobby] measure_body_scale: {sent} oyuncu, {skipped} kalibresiz atlandı — {connection.State?.Name}.");
            var note = skipped > 0
                ? $"{sent} oyuncu ölçülüyor ({skipped} kalibresiz atlandı)"
                : $"{sent} oyuncu ölçülüyor";
            await BroadcastAdminStateAsync(Notice(connection, note));
            return;
        }

        if (!_registry.TryGetByPlayerId(msg.playerId, out var target) || target.Socket == null)
        {
            Console.WriteLine($"[Lobby] measure_body_scale: playerId {msg.playerId} bulunamadı/bağlantısı yok.");
            return;
        }
        if (target.Role != "player")
        {
            Console.WriteLine($"[Lobby] measure_body_scale: {target.Name} admin — gövdesi yok, yok sayıldı.");
            return;
        }
        if (!target.Calibrated)
        {
            Console.WriteLine($"[Lobby] measure_body_scale: {target.Name} kalibresiz — ölçüm gönderilmedi.");
            await BroadcastAdminStateAsync(Notice(connection, $"{target.Name} kalibresiz — önce kalibrasyon"));
            return;
        }

        await SendSafeAsync(target.Socket, payload, target.Name);
        Console.WriteLine($"[Lobby] measure_body_scale: {target.Name} (playerId {target.PlayerId}) — {connection.State?.Name}.");
        await BroadcastAdminStateAsync(Notice(connection, $"{target.Name} ölçülüyor"));
    }

    /// <summary>Forwards the body-tracking restart (§6.11); the server computes nothing and keeps no
    /// pending-request ledger.
    /// <para>⚠️ <b>Uncalibrated targets are NOT skipped</b> — measure_body_scale's calibration gate has
    /// no counterpart here and must not gain one: this repairs the headset's tracking service, which has
    /// nothing to do with arena alignment.</para>
    /// <para>⚠️ <b>No reply is awaited.</b> "Restarted" does not mean "the body is streaming"; the only
    /// meaningful answer is the stream itself and it is already visible on <c>0x07</c>.</para>
    /// </summary>
    public async Task HandleRestartBodyTrackingAsync(ClientConnection connection, RestartBodyTrackingMsg msg)
    {
        // No fields in the server → client direction: the target is that connection.
        var payload = JsonUtil.Serialize(new RestartBodyTrackingMsg());

        if (msg.playerId == 0)
        {
            var sent = 0;
            foreach (var state in _registry.Snapshot())
            {
                if (state.Role != "player" || state.Socket == null) continue;
                await SendSafeAsync(state.Socket, payload, state.Name);
                sent++;
            }

            Console.WriteLine($"[Lobby] restart_body_tracking: {sent} oyuncu — {connection.State?.Name}.");
            await BroadcastAdminStateAsync(Notice(connection, $"{sent} gözlükte gövde izlemesi yeniden başlatılıyor"));
            return;
        }

        if (!_registry.TryGetByPlayerId(msg.playerId, out var target) || target.Socket == null)
        {
            Console.WriteLine($"[Lobby] restart_body_tracking: playerId {msg.playerId} bulunamadı/bağlantısı yok.");
            return;
        }
        if (target.Role != "player")
        {
            Console.WriteLine($"[Lobby] restart_body_tracking: {target.Name} admin — gövdesi yok, yok sayıldı.");
            return;
        }

        await SendSafeAsync(target.Socket, payload, target.Name);
        Console.WriteLine($"[Lobby] restart_body_tracking: {target.Name} (playerId {target.PlayerId}) — {connection.State?.Name}.");
        await BroadcastAdminStateAsync(Notice(connection, $"{target.Name}: gövde izlemesi yeniden başlatılıyor"));
    }

    // ---- Shared selection (§5.2 set_selection / §5.3 admin_state) ----

    /// <summary>Shared mode/map selection for the next match. Does NOT start a match; an empty field
    /// keeps its current value. The change is broadcast to all admins so multiple operators see the
    /// same screen.
    /// <para>
    /// <b>Picking a map is also STAGING it (§10.7):</b> an arena selected while in the lobby is
    /// loaded on ALL clients immediately via <see cref="MatchDirector.StageSceneAsync"/>. The
    /// operator changes the map on the players' headsets, not just on their own screen.
    /// </para>
    /// <para>
    /// ⚠️ Hence <b>mode/map can only change while NO match is set up</b>
    /// (<see cref="MatchDirector.CanChangeSelection"/>): during lobby waiting and in <c>finished</c>
    /// — after a match the operator must be able to pick the next one. A set-up match (running,
    /// loading, counting down or paused) is closed: the scene command goes to everyone, so pulling
    /// the scene out from under a match would break it. Rejected fields are dropped and the rest of
    /// the command (duration/limit) is still processed — those are next-match parameters and load no
    /// scene.
    /// </para></summary>
    public async Task HandleSetSelectionAsync(ClientConnection connection, SetSelectionMsg msg)
    {
        var requestedModeId = msg.modeId ?? "";
        var requestedSceneName = msg.sceneName ?? "";

        // Phase gate (§10.7): a set-up match blocks — running, loading, counting down or paused.
        // `finished` and lobby waiting are open. Authority is server-side: the UI pickers are already
        // disabled by the same rule, this cuts off a stale/racing panel's command.
        var rejection = "";
        if (!_director.CanChangeSelection && (requestedModeId.Length > 0 || requestedSceneName.Length > 0))
        {
            rejection = "maç kurulu — harita/mod değiştirilemez, önce İPTAL";
            Console.WriteLine($"[Lobby] set_selection reddedildi ({connection.State?.Name}): {rejection}.");
            requestedModeId = "";
            requestedSceneName = "";
        }

        string previousModeId;
        lock (_selectionGate) previousModeId = _selectedModeId;

        var changed = ApplySelection(requestedModeId, requestedSceneName,
            msg.roundSeconds, msg.scoreLimit, msg.countdownSeconds);

        // Base strips depend on the selected mode's team mode (§10.7) — announced to everyone when
        // the MODE changes. Map/duration/limit changes do not produce this broadcast.
        bool modeChanged;
        lock (_selectionGate) modeChanged = _selectedModeId != previousModeId;
        if (modeChanged) await BroadcastSelectionStateAsync();

        // On rejection a broadcast goes out even when nothing changed: the sending panel may have
        // advanced its control optimistically and the server's value must pull it back (single truth
        // source, §5.3). A non-empty map field also prevents an early exit: even with an unchanged
        // selection that scene may not be OPEN (after returning to the lobby the selection still
        // shows the arena) — staging must be attempted.
        if (!changed && requestedSceneName.Length == 0 && rejection.Length == 0) return;

        string modeId, sceneName;
        int roundSeconds, scoreLimit;
        lock (_selectionGate)
        {
            modeId = _selectedModeId;
            sceneName = _selectedSceneName;
            roundSeconds = _selectedRoundSeconds;
            scoreLimit = _selectedScoreLimit;
        }

        // Staging (§10.7): a NON-EMPTY map field makes everyone load that arena. The criterion is
        // "was it requested", not "did the selection change": after returning to the lobby the
        // selection still points at that arena, so an operator re-picking it would otherwise see
        // nothing happen. If the requested scene is already open, StageSceneAsync returns Unchanged
        // (idempotent).
        // ⚠️ Hence the panel fills the map field ONLY when its picker moves (§5.2) — a client that
        // filled it on a duration/limit touch would drag everyone into the arena.
        var stageNote = "";
        if (requestedSceneName.Length > 0)
        {
            var staged = await _director.StageSceneAsync(sceneName);
            stageNote = staged.Outcome switch
            {
                StageOutcome.Staged => " (herkes yüklüyor)",
                StageOutcome.Rejected => $" — SAHNELENEMEDİ: {staged.Reason}",
                _ => ""
            };
        }

        var parameters = roundSeconds > 0 || scoreLimit != 0
            ? $", {(roundSeconds > 0 ? roundSeconds + " sn" : "mod süresi")} / " +
              $"{(scoreLimit != 0 ? "limit " + MatchDirector.DescribeScoreLimit(scoreLimit) : "mod limiti")}"
            : "";
        Console.WriteLine($"[Lobby] set_selection: mod '{modeId}', harita '{sceneName}'{parameters} ({connection.State?.Name}).");

        var action = rejection.Length > 0
            ? rejection
            : $"seçim -> {sceneName} / {modeId}{parameters}{stageNote}";
        await BroadcastAdminStateAsync(Notice(connection, action));
    }

    /// <summary>true = the selection really changed. An empty/null string and a <c>0</c> number keep
    /// the current value (§5.2) so the UI can fill only the field it changed.
    /// <para>⚠️ This is also the "the selection never empties" guarantee: after the constructor's
    /// lobby seed no command can blank the mode/map.</para>
    /// <para>⚠️ <paramref name="scoreLimit"/> is the EXCEPTION to that contract: <c>0</c> still means
    /// "untouched", but a negative value IS a choice (unlimited, §5.2), so its gate is non-zero
    /// rather than positive.</para></summary>
    private bool ApplySelection(string? modeId, string? sceneName, int roundSeconds, int scoreLimit,
        int countdownSeconds)
    {
        lock (_selectionGate)
        {
            var changed = false;
            if (!string.IsNullOrEmpty(modeId) && _selectedModeId != modeId)
            {
                _selectedModeId = modeId;
                changed = true;
            }
            if (!string.IsNullOrEmpty(sceneName) && _selectedSceneName != sceneName)
            {
                _selectedSceneName = sceneName;
                changed = true;
            }
            if (roundSeconds > 0 && _selectedRoundSeconds != roundSeconds)
            {
                _selectedRoundSeconds = roundSeconds;
                changed = true;
            }
            // ⚠️ The limit gate is "!= 0", NOT "> 0": SCORE_LIMIT_UNLIMITED is a choice too and would
            // silently fall through a positive-only gate because it is negative. Normalize collapses
            // every negative to one spelling, otherwise a client sending "-2" would count as a change.
            var normalizedLimit = ArenaProtocol.NormalizeScoreLimit(scoreLimit);
            if (normalizedLimit != 0 && _selectedScoreLimit != normalizedLimit)
            {
                _selectedScoreLimit = normalizedLimit;
                changed = true;
            }
            if (countdownSeconds > 0 && _selectedCountdownSeconds != countdownSeconds)
            {
                _selectedCountdownSeconds = countdownSeconds;
                changed = true;
            }
            return changed;
        }
    }

    // ---- Match commands (admin only; validation + broadcasts live in MatchDirector, §10.1) ----

    /// <summary>start_match also updates the shared selection so every admin panel shows the same
    /// mode/map once the match starts, whoever sent the command.</summary>
    public async Task HandleStartMatchAsync(ClientConnection connection, StartMatchMsg msg)
    {
        string previousModeId;
        lock (_selectionGate) previousModeId = _selectedModeId;

        ApplySelection(msg.modeId, msg.sceneName, msg.roundSeconds, msg.scoreLimit, msg.countdownSeconds);

        bool modeChanged;
        lock (_selectionGate) modeChanged = _selectedModeId != previousModeId;
        if (modeChanged) await BroadcastSelectionStateAsync();

        await BroadcastAdminStateAsync(Notice(connection, $"maç başlatılıyor: {msg.sceneName} / {msg.modeId}"));
        await _director.StartMatchAsync(msg.modeId, msg.sceneName, msg.roundSeconds, msg.scoreLimit,
            msg.countdownSeconds);
    }

    public async Task HandleAbortMatchAsync(ClientConnection connection)
    {
        await BroadcastAdminStateAsync(Notice(connection, "maç iptal edildi"));
        await _director.AbortMatchAsync();
    }

    public async Task HandleReturnToLobbyAsync(ClientConnection connection)
    {
        await BroadcastAdminStateAsync(Notice(connection, "lobiye dönülüyor"));
        await _director.ReturnToLobbyAsync();
    }

    /// <summary>end_match (§5.2) — ends the match normally (match_end + result screen), unlike
    /// abort_match. The notice follows the same rule as pause: only on an actual end.</summary>
    public async Task HandleEndMatchAsync(ClientConnection connection)
    {
        if (await _director.EndMatchAsync())
            await BroadcastAdminStateAsync(Notice(connection, "maç operatörce bitirildi"));
    }

    /// <summary>pause_match (§5.2). The notice is broadcast ONLY on an actual pause — a rejected
    /// command must not print "paused" on the other operators' screens.</summary>
    public async Task HandlePauseMatchAsync(ClientConnection connection)
    {
        if (await _director.PauseMatchAsync())
            await BroadcastAdminStateAsync(Notice(connection, "maç duraklatıldı"));
    }

    /// <summary>resume_match (§5.2) — resumes only a match the operator paused.</summary>
    public async Task HandleResumeMatchAsync(ClientConnection connection)
    {
        if (await _director.ResumeMatchAsync())
            await BroadcastAdminStateAsync(Notice(connection, "maç sürdürüldü"));
    }

    /// <summary>mode_continue (§5.2) — releases a mode that parked the round flow (the tournament's
    /// round review, §10.5). The notice follows the pause rule: only on an accepted command, so a press
    /// landing outside a hold does not print "devam" on the other operators' screens.</summary>
    public async Task HandleModeContinueAsync(ClientConnection connection)
    {
        if (_director.ModeContinue())
            await BroadcastAdminStateAsync(Notice(connection, "tur akışı sürdürüldü"));
    }

    /// <summary>
    /// <c>set_friendly_fire</c> (§5.2) — the friendly fire switch, <b>no phase gate</b>: valid during
    /// a running match and effective immediately.
    /// <para>If the value did not change the server does nothing; <c>admin_state</c> is still
    /// broadcast so an optimistic panel is pulled back to the server's value (§5.3 single truth
    /// source).</para>
    /// </summary>
    public async Task HandleSetFriendlyFireAsync(ClientConnection connection, SetFriendlyFireMsg msg)
    {
        var changed = await _director.SetFriendlyFireAsync(msg.enabled);
        var label = msg.enabled ? "AÇIK" : "kapalı";
        await BroadcastAdminStateAsync(changed
            ? Notice(connection, $"dost ateşi {label}")
            : "");
    }

    /// <summary>
    /// <c>set_calibration_mode</c> (§5.2/§10.6) — how headsets align AT STARTUP. Same class as
    /// <c>set_friendly_fire</c>: no phase gate and no selection lock.
    /// <para>⚠️ Unknown/empty values and <c>anchor_cloud</c> are <b>rejected</b> — the state does not
    /// change. The rule values' "coerce unknown to default" contract does not apply here: the mode is
    /// an operator decision, and silently falling back to the default would suggest the pressed
    /// button was applied.</para>
    /// <para>No notice if the value did not change; <c>admin_state</c> still goes out so an
    /// optimistic panel is pulled back to the server's value (§5.3 single truth source).</para>
    /// </summary>
    public async Task HandleSetCalibrationModeAsync(ClientConnection connection, SetCalibrationModeMsg msg)
    {
        var mode = msg.mode ?? "";
        if (mode == ArenaProtocol.CALIB_MODE_ANCHOR_CLOUD)
        {
            Console.WriteLine($"[Lobby] set_calibration_mode '{mode}' henüz uygulanmadı — yok sayıldı.");
            return;
        }
        if (mode != ArenaProtocol.CALIB_MODE_TWO_ANCHOR && mode != ArenaProtocol.CALIB_MODE_SAVED_ANCHOR)
        {
            Console.WriteLine($"[Lobby] set_calibration_mode geçersiz mod '{mode}' — yok sayıldı.");
            return;
        }

        bool changed;
        lock (_selectionGate)
        {
            changed = _calibrationMode != mode;
            _calibrationMode = mode;
        }
        if (changed) Console.WriteLine($"[Lobby] set_calibration_mode: {mode} ({connection.State?.Name}).");
        await BroadcastAdminStateAsync(changed
            ? Notice(connection, $"kalibre modu: {CalibrationModeLabel(mode)}")
            : "");
    }

    /// <summary>Calibration mode label shown to the operator (notice line).</summary>
    private static string CalibrationModeLabel(string mode) => mode switch
    {
        ArenaProtocol.CALIB_MODE_SAVED_ANCHOR => "Eski Kalibre",
        _ => "2 Çapa"
    };

    /// <summary>Effective calibration mode (§10.6) — welcome construction reads it under the lock.</summary>
    private string CurrentCalibrationMode()
    {
        lock (_selectionGate) return _calibrationMode;
    }

    /// <summary>Notice line: "<admin name>: <action>" — shown in every admin's status line.</summary>
    private static string Notice(ClientConnection connection, string action) =>
        $"{connection.State?.Name ?? "Admin"}: {action}";

    /// <summary>
    /// Sends the selected mode's team mode to <b>EVERYONE</b> (§5.3 <c>selection_state</c>).
    /// <para>
    /// ⚠️ It does not ride on <c>admin_state</c> because of the audience: that message carries
    /// roster/notice/telemetry and goes to admins only. The player needs a single presentation field
    /// — whether base strips are visible (§10.7).
    /// </para>
    /// <para>
    /// ⚠️ Call sites are kept narrow: after <c>welcome</c> and <b>when the selected MODE changes</b>.
    /// Broadcasting on a map/duration/limit touch would send a useless message to every player each
    /// time the operator moves a picker.
    /// </para>
    /// </summary>
    public async Task BroadcastSelectionStateAsync()
    {
        try
        {
            var connections = _registry.ConnectedConnections();
            if (connections.Count == 0) return;
            var json = BuildSelectionStateJson();
            foreach (var connection in connections)
                await SendSafeAsync(connection, json, "(selection)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Lobby] selection_state yayını hatası: {ex.Message}");
        }
    }

    private string BuildSelectionStateJson()
    {
        string modeId;
        lock (_selectionGate)
        {
            modeId = _selectedModeId;
        }

        return JsonUtil.Serialize(new SelectionStateMsg
        {
            modeId = modeId,
            teamMode = _director.TeamModeOf(modeId) == Modes.TeamMode.None ? "none" : "two"
        });
    }

    /// <summary>Sends the shared state to connected admins ONLY (§5.3).</summary>
    public async Task BroadcastAdminStateAsync(string notice)
    {
        try
        {
            var admins = _registry.ConnectedAdminConnections();
            if (admins.Count == 0) return;
            var json = BuildAdminStateJson(notice);
            foreach (var connection in admins)
                await SendSafeAsync(connection, json, "(admin)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Lobby] admin_state yayını hatası: {ex.Message}");
        }
    }

    // ---- net_stats publisher (§6.7): admins only, 1 Hz ----
    // ⚠️ It is a separate loop because of the rhythm: admin_state is EVENT driven (selection/command
    // changes), telemetry is periodic. Riding on admin_state would either make telemetry sparse or
    // produce a "notice" broadcast every second.

    private CancellationTokenSource? _netStatsCts;
    private Task? _netStatsLoop;

    public void Start()
    {
        if (_netStatsLoop is { IsCompleted: false }) return;
        _netStatsCts = new CancellationTokenSource();
        _netStatsLoop = Task.Run(() => NetStatsLoopAsync(_netStatsCts.Token));
    }

    /// <summary>Cancel → drain → dispose. Idempotent: a second call is a no-op.</summary>
    /// <remarks>Stopped FIRST during shutdown: a telemetry broadcast to admins whose sockets are
    /// about to close has no reader left.</remarks>
    public async Task StopAsync()
    {
        var cts = _netStatsCts;
        var loop = _netStatsLoop;
        _netStatsCts = null;
        _netStatsLoop = null;
        if (cts == null && loop == null) return;

        cts?.Cancel();
        await ServiceShutdown.DrainAsync("lobby", loop);
        cts?.Dispose();
    }

    /// <summary>Per-player ping/jitter/loss — values the CLIENT measures and reports via
    /// <c>status</c> (§6.7); the server only relays them.
    /// <para>⚠️ <b>Not a broadcast:</b> connected admins only. Sending to everyone would create a
    /// fan-out growing with the square of the player count — exactly the problem this telemetry
    /// exists to measure.</para>
    /// <para>Losing one is harmless: the next second brings a fresh set, no reconciliation needed.
    /// That is why it never enters the roster (<c>lobby_state</c>) — that has a <c>version</c> and a
    /// reconciliation protocol, which becomes meaningless if it turns over every second.</para></summary>
    private async Task NetStatsLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var entries = new List<NetStatsEntry>();

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(token)) break;
            }
            catch (OperationCanceledException) { break; }

            try
            {
                var admins = _registry.ConnectedAdminConnections();
                // Nobody watching, no serialization: the operator screen is telemetry's only
                // consumer and producing it for nobody is a wasted packet.
                if (admins.Count == 0) continue;

                entries.Clear();
                foreach (var state in _registry.Snapshot())
                {
                    if (state.Role != "player" || !state.IsConnected) continue;
                    entries.Add(new NetStatsEntry
                    {
                        playerId = state.PlayerId,
                        rttMs = state.RttMs,
                        jitterMs = state.JitterMs,
                        lossPct = state.LossPct
                    });
                }

                if (entries.Count == 0) continue;

                var json = JsonUtil.Serialize(new NetStatsMsg { players = entries.ToArray() });
                foreach (var connection in admins)
                    await SendSafeAsync(connection, json, "(admin)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Lobby] net_stats yayını hatası: {ex.Message}");
            }
        }
    }

    private string BuildAdminStateJson(string notice)
    {
        lock (_selectionGate)
        {
            return JsonUtil.Serialize(new AdminStateMsg
            {
                modeId = _selectedModeId,
                sceneName = _selectedSceneName,
                roundSeconds = _selectedRoundSeconds,
                scoreLimit = _selectedScoreLimit,
                countdownSeconds = _selectedCountdownSeconds,
                // NOT a selection, the effective state (§5.2): the switch is valid during a running
                // match, so it bypasses the selection lock (CanChangeSelection) and is sent as-is.
                friendlyFire = _director.FriendlyFire,
                // Same class as friendly fire (§10.6): effective state, not a selection, bypassing
                // the selection lock. Players do not get it here — they receive it once in welcome.
                calibrationMode = _calibrationMode,
                notice = notice,
                adminCount = _registry.ConnectedAdminCount(),
                // The venue is fixed for the session (chosen at startup) but travels with
                // admin_state: a late-joining admin learns which arenas it can see in its first message.
                venueId = _director.VenueId,
                venueScenes = _director.VenueScenes.ToArray()
            });
        }
    }

    /// <summary>Marks the roster dirty and starts the publisher if it is not running (§5.3).
    /// <para>Also a <b>coalescer</b>: N changes arriving while a broadcast is in flight collapse into
    /// one extra broadcast — 16 players connecting at once produce 2 full roster broadcasts, not
    /// 16.</para></summary>
    private void MarkRosterDirty()
    {
        lock (_broadcastGate)
        {
            _rosterDirty = true;
            if (_broadcasting) return;
            _broadcasting = true;
        }
        _ = RunRosterBroadcastLoopAsync();
    }

    /// <summary>Broadcasts while dirty, stops when clean. <b>Only one instance runs at a time</b> —
    /// that is where lobby_state's version monotonicity and ordering guarantee come from.</summary>
    private async Task RunRosterBroadcastLoopAsync()
    {
        try
        {
            while (true)
            {
                lock (_broadcastGate)
                {
                    if (!_rosterDirty)
                    {
                        _broadcasting = false;
                        return;
                    }
                    _rosterDirty = false;
                }
                await BroadcastLobbyStateAsync();
            }
        }
        catch (Exception ex)
        {
            // Reaching here is not expected (the broadcast swallows its own errors), but if it
            // happens, release the flag: otherwise _broadcasting sticks and the roster is NEVER
            // broadcast again.
            lock (_broadcastGate) _broadcasting = false;
            Console.WriteLine($"[Lobby] roster yayıncısı durdu: {ex.Message}");
        }
    }

    /// <summary>Sends a FULL roster snapshot to every connected socket (§5.3 lobby_state) and bumps
    /// the version. ⚠️ <b>Called only from the publisher loop</b> — a direct call means concurrent
    /// broadcasts and breaks the ordering guarantee.</summary>
    private async Task BroadcastLobbyStateAsync()
    {
        try
        {
            var snapshot = _registry.Snapshot();
            var msg = new LobbyStateMsg
            {
                version = Interlocked.Increment(ref _rosterVersion),
                players = snapshot.OrderBy(p => p.PlayerId).Select(p => p.ToPlayerInfo()).ToArray()
            };
            var json = JsonUtil.Serialize(msg);
            foreach (var state in snapshot)
            {
                var connection = state.Socket;
                if (connection == null || !state.IsConnected) continue;
                await SendSafeAsync(connection, json, state.Name);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Lobby] lobby_state yayını hatası: {ex.Message}");
        }
    }

    /// <summary>Roster for a single connection (reconciliation path). ⚠️ Does <b>not</b> bump the
    /// version: a lagging client gets the current version, not a newly minted one.</summary>
    private string BuildLobbyStateJson() => JsonUtil.Serialize(new LobbyStateMsg
    {
        version = Volatile.Read(ref _rosterVersion),
        players = _registry.Snapshot().OrderBy(p => p.PlayerId).Select(p => p.ToPlayerInfo()).ToArray()
    });

    private static async Task SendSafeAsync(ClientConnection connection, string json, string who)
    {
        try
        {
            await connection.SendTextAsync(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Lobby] gönderim başarısız ({who}): {ex.Message}");
        }
    }
}
