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
        public static event Action<ShotFiredMsg> OnShotFired;
        public static event Action<IdentifyMsg> OnIdentify;
        public static event Action<KickedMsg> OnKicked;

        /// <summary>Yalnız admin bağlantılarına gelir (§5.3): adminler arası ortak mod/harita
        /// seçimi + son eylem duyurusu. App katmanında <c>AdminSelection</c> dinler.</summary>
        public static event Action<AdminStateMsg> OnAdminState;

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
        internal static void RaiseShotFired(ShotFiredMsg msg) { OnShotFired?.Invoke(msg); }
        internal static void RaiseIdentify(IdentifyMsg msg) { OnIdentify?.Invoke(msg); }
        internal static void RaiseKicked(KickedMsg msg) { OnKicked?.Invoke(msg); }
        internal static void RaiseAdminState(AdminStateMsg msg) { OnAdminState?.Invoke(msg); }

#if UNITY_EDITOR
        /// <summary>
        /// YALNIZ EDITOR — dev enjeksiyonu. Sunucudan gelmiş gibi load_match yayınlar.
        /// <para>
        /// Neden var: tek bir arena sahnesine doğrudan Play'e basıldığında takım ve spawn
        /// slot bilgisi <c>PlayerCombatState</c>/<c>ModeHudSpawner</c>/<c>SceneRouter</c>'a
        /// bu olaydan gider. Dev'e özel ikinci bir API açmak yerine <b>gerçek kod yolunu</b>
        /// kullanırız — böylece editörde denenen akış sahada koşan akışla aynı kalır.
        /// </para>
        /// Çağıran: <c>VortexArena.App.DevSession</c>. <c>#if UNITY_EDITOR</c> olduğu için
        /// hiçbir build'e girmez.
        /// </summary>
        public static void InjectLoadMatch(LoadMatchMsg msg) { OnLoadMatch?.Invoke(msg); }
#endif
    }
}
