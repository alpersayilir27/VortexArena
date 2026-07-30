namespace VortexArena.Protocol
{
    /// WS kontrol mesajı "type" string sabitleri (Docs/ArenaNet-Protokol.md §5).
    public static class MessageTypes
    {
        // İstemci → Sunucu
        public const string Hello = "hello";
        public const string Status = "status";
        public const string SetIdentity = "set_identity"; // ad + forma numarası; oyuncu kendini, admin herkesi (§5.1)
        public const string SetReady = "set_ready";
        // shot_fired v4'te KALDIRILDI → UDP 0x03/0x04 (§6.4/6.5); 10 atış/sn/oyuncu otoriter WS
        // kanalını boğuyordu.
        public const string HitReport = "hit_report";
        public const string ReviveRequest = "revive_request"; // free-roam canlanma talebi (§10.4)
        public const string SetCalibration = "set_calibration"; // başlık kendi hizalamasını bildirir (§10.6)

        // Yalnız admin → Sunucu
        public const string StartMatch = "start_match";
        public const string AbortMatch = "abort_match";
        public const string PauseMatch = "pause_match"; // koşan maçı dondurur (§5.2)
        public const string ResumeMatch = "resume_match"; // yalnız operatör duraklatmasını kaldırır
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
        public const string Ping = "ping"; // "bana status yolla" tetiği — GECİKME ÖLÇMEZ (UDP 0x06 ölçer)
        public const string Kicked = "kicked";
        public const string AdminState = "admin_state"; // yalnız adminlere: ortak seçim + duyuru
        public const string NetStats = "net_stats"; // yalnız adminlere: oyuncu başına ping/jitter/kayıp
    }
}
