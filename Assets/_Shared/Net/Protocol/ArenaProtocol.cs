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

        /// <summary>
        /// <c>playerId</c> tahsis tavanı. <b>Ürün kotası DEĞİL, tel formatı tavanıdır</b> —
        /// playerId UDP paketlerinde <c>u8</c> taşınır (0 ayrılmıştır). Eşzamanlı oyuncu/admin
        /// sayısına başka bir sınır yoktur; kota ileride lisanslama katmanıyla gelecek.
        /// </summary>
        public const int PLAYER_ID_MAX = 255;

        /// <summary>
        /// Tek snapshot datagramına yazılan en fazla oyuncu girdisi. Fazlası aynı tik içinde
        /// ek datagramlara taşar (§6.3) — 6 + 16×86 = 1382 B, MTU 1500'ün altında kalır.
        /// İstemcide birleştirme mantığı gerekmez: her paket kendi girdilerini bağımsız uygular.
        /// </summary>
        public const int SNAPSHOT_MAX_ENTRIES_PER_PACKET = 16;

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

        /// <summary>Ölüm → en erken canlanma süresinin VARSAYILANI (respawn.delaySeconds).
        /// Mod bunu ModeRules.RespawnDelay ile ezebilir (§10.5).</summary>
        public const float RESPAWN_DELAY = 5f;

        /// <summary>Free-roam canlanma: süre dolduktan sonra oyuncu modun canlanma şartını
        /// sağlayıp revive_request gönderir. Bu süre (ölümden itibaren) dolduğunda sunucu yine de
        /// canlandırır — takılan/bildirim gönderemeyen istemci kalıcı ölü kalmasın.</summary>
        public const float REVIVE_GRACE = 20f;

        /// <summary>reviveAnchor="standstill" (§10.5): ölü oyuncunun canlanmak için kesintisiz
        /// sabit durması gereken süre.</summary>
        public const float REVIVE_HOLD_SECONDS = 3f;

        /// <summary>reviveAnchor="standstill": ölüm anındaki çapadan bu yarıçapı (metre) aşan
        /// hareket sayacı ve çapayı sıfırlar.</summary>
        public const float REVIVE_HOLD_RADIUS = 1f;

        /// <summary>
        /// Admin arayüzünün maç süresi seçenekleri (saniye): 2.5 · 5 · 10 · 15 · 20 · 30 dk · 1 saat.
        /// <para><b>Protokol kısıtı DEĞİL, arayüz listesidir</b> — sunucu start_match.roundSeconds
        /// alanında her pozitif değeri kabul eder (§5.2).</para>
        /// </summary>
        public static readonly int[] ROUND_SECONDS_OPTIONS = { 150, 300, 600, 900, 1200, 1800, 3600 };

        // NOT: FIRE_RATE_TOLERANCE kaldırıldı (§10.3). Sunucuda atış hızı denetimi ve silah
        // tablosu yoktur — hasarı istemci hesaplar, sunucu aynen uygular. Ürün gözetimli özel
        // alanda çalıştığı için hile koruması bilinçli olarak eklenmez.
    }
}
