namespace VortexArena.Protocol
{
    /// WS control message "type" string constants (Docs/ArenaNet-Protokol.md §5).
    public static class MessageTypes
    {
        // Client → Server
        public const string Hello = "hello";
        public const string Status = "status";
        public const string SetIdentity = "set_identity"; // name + jersey number; a player sets their own, an admin anyone's (§5.1)
        public const string SetReady = "set_ready";
        // shot_fired was REMOVED in v4 → UDP 0x03/0x04 (§6.4/6.5); 10 shots/s/player was drowning the
        // authoritative WS channel.
        public const string HitReport = "hit_report";
        public const string ReviveRequest = "revive_request"; // free-roam revive request (§10.4)
        public const string SetCalibration = "set_calibration"; // the headset reports its own alignment (§10.6)
        public const string SetBodyScale = "set_body_scale"; // the headset reports its own body scale (§10.8)

        // Admin only → Server
        public const string StartMatch = "start_match";
        public const string AbortMatch = "abort_match";
        // Ends the match NORMALLY (match_end + result screen) — not abort_match, which skips both (§5.2)
        public const string EndMatch = "end_match";
        public const string PauseMatch = "pause_match"; // freezes a running match (§5.2)
        public const string ResumeMatch = "resume_match"; // lifts the operator pause only
        public const string SetTeam = "set_team";
        public const string Kick = "kick";
        public const string ReturnToLobby = "return_to_lobby"; // the server → client direction uses the same type
        public const string SetSelection = "set_selection"; // shared mode/map selection (does not start a match)
        // Invalidates the alignment; keepSaved=false (default) also deletes the anchor on the device.
        // playerId 0 = everyone (§10.6). The server → client direction uses the same type.
        public const string ClearCalibration = "clear_calibration";
        // Makes the alignment reload from the saved anchor; playerId 0 = everyone (§10.6).
        // The server → client direction uses the same type.
        public const string ReloadCalibration = "reload_calibration";
        // Starts the body measurement; playerId 0 = everyone (§10.8). The server → client direction uses the same type.
        public const string MeasureBodyScale = "measure_body_scale";
        // Restarts body tracking on the headset; playerId 0 = everyone (§6.11). The server → client
        // direction uses the same type. There is no reply — the only meaningful answer is the stream itself.
        public const string RestartBodyTracking = "restart_body_tracking";
        public const string SetFriendlyFire = "set_friendly_fire"; // friendly fire switch; NO phase gate, takes effect instantly (§5.2)
        // How headsets align AT STARTUP; same class as set_friendly_fire: instant, does not enter the
        // selection lock (§5.2/§10.6).
        public const string SetCalibrationMode = "set_calibration_mode";
        // Server → Client
        public const string Welcome = "welcome";
        public const string LobbyState = "lobby_state";
        public const string LoadMatch = "load_match";
        public const string Countdown = "countdown";
        public const string MatchState = "match_state";
        public const string HealthUpdate = "health_update";
        public const string KillEvent = "kill_event";
        public const string Respawn = "respawn";
        public const string MatchEnd = "match_end";
        public const string Ping = "ping"; // "send me a status" trigger — MEASURES NO LATENCY (UDP 0x06 does)
        public const string Kicked = "kicked";
        public const string AdminState = "admin_state"; // admins only: shared selection + announcement
        public const string SelectionState = "selection_state"; // TO EVERYONE: team mode of the selected mode (§5.3)
        public const string RulesUpdate = "rules_update"; // TO EVERYONE: the rule shape of the running match changed (§5.3)
        public const string NetStats = "net_stats"; // admins only: per-player ping/jitter/loss
        public const string Violation = "violation"; // admins only: edge notification of the violation log (§5.3)
        // Admins only: the answer to the reload_calibration button — an event, not a state (§5.3).
        public const string CalibrationResult = "calibration_result";
    }
}
