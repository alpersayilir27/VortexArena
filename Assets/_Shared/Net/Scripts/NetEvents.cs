using System;
using VortexArena.Protocol;

namespace VortexArena.Net
{
    /// <summary>
    /// Statik olay merkezi: ArenaClient sunucu mesajlarını buradan yayınlar
    /// (hepsi ANA thread'de tetiklenir), App/Core dinler. Net katmanı sahne
    /// yüklemez ve oyun bilgisi içermez — olayları kim nasıl işler bilmez.
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
        /// <summary>Lobiye dönüş (§10.7). Mesaj lobi sahnesini + profilini taşır; ilgilenmeyen
        /// dinleyici parametreyi yok sayar.</summary>
        public static event Action<ReturnToLobbyMsg> OnReturnToLobby;
        public static event Action<IdentifyMsg> OnIdentify;
        public static event Action<KickedMsg> OnKicked;

        /// <summary>Uzak bir oyuncunun atış/atma olayı (UDP 0x04 EventBatch, §6.5) — v4'te WS
        /// <c>shot_fired</c>'ın yerini aldı. <c>ArenaClient</c> DEĞİL <c>UdpStateChannel</c>
        /// yayınlar, ama diğerleri gibi ANA thread'de. Kendi olaylarımız kanalda süzülür.</summary>
        public static event Action<RemoteFireEvent> OnRemoteFireEvent;

        /// <summary>Yalnız admin bağlantılarına gelir (§5.3): adminler arası ortak mod/harita
        /// seçimi + son eylem duyurusu. App katmanında <c>AdminSelection</c> dinler.</summary>
        public static event Action<AdminStateMsg> OnAdminState;

        /// <summary>Yalnız admin bağlantılarına gelir (§6.7), 1 Hz: oyuncu başına ping/jitter/kayıp.
        /// Değerleri İSTEMCİLER ölçer, sunucu taşır. App katmanında <c>AdminRoster</c> dinler.
        /// <para>Kaybı zararsızdır — bir sonraki saniye yenisi gelir, uzlaştırma yoktur.</para></summary>
        public static event Action<NetStatsMsg> OnNetStats;

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
        internal static void RaiseReturnToLobby(ReturnToLobbyMsg msg) { OnReturnToLobby?.Invoke(msg); }
        // `in` ile geçilir: 10 olay/sn'de 40+ baytlık struct'ı kopyalamamak için (yayın sırasında
        // değiştirilmiyor). Delegate'e verilirken zaten bir kopya çıkar; buradaki kazanç çağrı yolu.
        internal static void RaiseRemoteFireEvent(in RemoteFireEvent evt) { OnRemoteFireEvent?.Invoke(evt); }
        internal static void RaiseIdentify(IdentifyMsg msg) { OnIdentify?.Invoke(msg); }
        internal static void RaiseKicked(KickedMsg msg) { OnKicked?.Invoke(msg); }
        internal static void RaiseAdminState(AdminStateMsg msg) { OnAdminState?.Invoke(msg); }
        internal static void RaiseNetStats(NetStatsMsg msg) { OnNetStats?.Invoke(msg); }
    }
}
