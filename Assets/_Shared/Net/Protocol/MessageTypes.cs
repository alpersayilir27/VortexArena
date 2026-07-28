namespace VortexArena.Protocol
{
    /// WS kontrol mesajı "type" string sabitleri (Docs/ArenaNet-Protokol.md §5).
    public static class MessageTypes
    {
        // İstemci → Sunucu
        public const string Hello = "hello";
        public const string Status = "status";
        public const string SetName = "set_name";
        public const string SetReady = "set_ready";
        public const string ShotFired = "shot_fired"; // sunucu relay'i de aynı type
        public const string HitReport = "hit_report";
        public const string ReviveRequest = "revive_request"; // free-roam canlanma talebi (§10.4)
        public const string SetCalibration = "set_calibration"; // başlık kendi hizalamasını bildirir (§10.6)

        // Yalnız admin → Sunucu
        public const string StartMatch = "start_match";
        public const string AbortMatch = "abort_match";
        public const string SetTeam = "set_team";
        public const string Kick = "kick";
        public const string Identify = "identify"; // sunucu → istemci yönü de aynı type
        public const string ReturnToLobby = "return_to_lobby"; // sunucu → istemci yönü de aynı type
        public const string SetSelection = "set_selection"; // ortak mod/harita seçimi (maçı başlatmaz)
        public const string ClearCalibration = "clear_calibration"; // kalibrasyonu sıfırla; playerId 0 = herkes (§10.6)

        // Sunucu → İstemci
        public const string Welcome = "welcome";
        public const string LobbyState = "lobby_state";
        public const string LoadMatch = "load_match";
        public const string Countdown = "countdown";
        public const string MatchState = "match_state";
        public const string HealthUpdate = "health_update";
        public const string KillEvent = "kill_event";
        public const string Respawn = "respawn";
        public const string MatchEnd = "match_end";
        public const string Ping = "ping";
        public const string Kicked = "kicked";
        public const string AdminState = "admin_state"; // yalnız adminlere: ortak seçim + duyuru
    }
}
