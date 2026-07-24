namespace VortexArena.Protocol
{
    /// Protokol sabitleri — tek doğruluk kaynağı: Docs/ArenaNet-Protokol.md §1.
    public static class ArenaProtocol
    {
        public const int PROTOCOL_VERSION = 1;
        public const string APP_ID = "VortexArena";

        public const int UDP_BEACON_PORT = 47820;
        public const int CONTROL_PORT = 47821;
        public const int STATE_PORT = 47822;
        public const string WS_PATH = "/ws";

        // Aralıklar/timeout'lar saniye cinsinden.
        public const float BEACON_INTERVAL = 2f;
        public const float DISCOVERY_TIMEOUT = 5f;
        public const float STATUS_INTERVAL = 5f;
        public const float OFFLINE_TIMEOUT = 15f;
        public const float HELLO_TIMEOUT = 10f; // §8: hello'suz bağlantı bu süre içinde kapatılır

        // Yeniden bağlanma backoff dizisi; son eleman tavandır.
        public static readonly float[] RECONNECT_BACKOFF = { 1f, 2f, 5f };

        public const int POSE_RATE_HZ = 20;
        public const int SNAPSHOT_RATE_HZ = 20;
        public const int INTERP_DELAY_MS = 100;

        public const int MAX_PLAYERS = 16;

        // ---- Maç akışı + savaş (Docs/ArenaNet-Protokol.md §10) ----

        /// <summary>Oyuncu tam canı; sunucu-otoriter (health_update bunu aşamaz).</summary>
        public const float PLAYER_MAX_HP = 100f;

        /// <summary>Countdown fazının uzunluğu (saniye, tam sayı — countdown mesajı saniye sayar).</summary>
        public const int COUNTDOWN_SECONDS = 5;

        /// <summary>End fazı: match_end sonrası otomatik return_to_lobby'ye kadar geçen süre.</summary>
        public const int MATCH_END_SECONDS = 10;

        /// <summary>Loading fazında tüm oyuncuların "sahne yüklendi" (set_ready) bildirimi
        /// beklenir; bu süre dolunca eksik olsa da Countdown'a geçilir.</summary>
        public const float LOADING_TIMEOUT = 20f;

        /// <summary>Ölüm → en erken canlanma süresi (respawn.delaySeconds).</summary>
        public const float RESPAWN_DELAY = 5f;

        /// <summary>Free-roam canlanma: süre dolduktan sonra oyuncu kendi tabanına girip
        /// revive_request gönderir. Bu süre (ölümden itibaren) dolduğunda sunucu yine de
        /// canlandırır — takılan/bildirim gönderemeyen istemci kalıcı ölü kalmasın.</summary>
        public const float REVIVE_GRACE = 20f;

        /// <summary>hit_report atış hızı denetimi: iki vuruş arası en az 60/rpm × bu kadar
        /// saniye olmalı (ağ jitter'ı için tolerans).</summary>
        public const float FIRE_RATE_TOLERANCE = 0.8f;
    }
}
