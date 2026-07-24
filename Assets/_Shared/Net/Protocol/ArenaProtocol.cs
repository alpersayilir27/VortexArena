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
    }
}
