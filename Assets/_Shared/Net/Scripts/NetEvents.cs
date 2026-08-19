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

        /// <summary>Operatör bu başlığın gövde ölçüsünü aldırdı (§10.8) — yalnız player'a gelir.
        /// <c>BodyScaleState</c> dinler, ölçer ve <c>set_body_scale</c> ile döner.
        /// <para>Mesajın sunucu → istemci yönünde alanı yoktur, bu yüzden olay da parametresizdir:
        /// hedef zaten bu bağlantıdır (<c>identify</c> ile aynı desen).</para></summary>
        public static event Action OnMeasureBodyScale;

        /// <summary>Operatör bu başlığın kalibrasyonunu sıfırladı (§10.6) — yalnız player'a gelir.
        /// <c>CalibrationState</c> dinler ve <c>ArenaCalibrator</c>'ı geçersiz kılar.
        /// <para>Argüman <c>keepSaved</c>'dir: <c>true</c> = yalnız hizalama geçersiz kılınır,
        /// cihazdaki çapa ve UUID KORUNUR (ardından gelen <c>reload_calibration</c> çalışabilsin);
        /// <c>false</c> = cihaz kaydı da silinir. Hedef zaten bu bağlantı olduğu için mesajın
        /// <c>playerId</c>'si iletilmez, ama kip iletilir — bu yüzden olay parametresiz DEĞİLDİR.</para>
        /// <para>⚠️ <b>Roster'daki <c>calibrated</c> alanının yerine geçmez, onu tamamlar:</b> alan
        /// durumu taşır, bu olay komutu. Yarım kalmış elle kalibrasyonda alan zaten <c>false</c>
        /// olduğu için sıfırlama yalnız bu olaydan duyulur (§5.3).</para></summary>
        public static event Action<bool> OnClearCalibration;

        /// <summary>Operatör bu başlığa kayıtlı çapadan hizalamayı yeniden yükletti (§10.6) —
        /// yalnız player'a gelir. <c>CalibrationState</c> dinler, <c>ArenaCalibrator</c>'a yükletir
        /// ve sonucu <c>set_calibration</c> ile döner.
        /// <para>Mesajın sunucu → istemci yönünde alanı yoktur, bu yüzden olay da parametresizdir:
        /// hedef zaten bu bağlantıdır (<c>identify</c> ile aynı desen).</para>
        /// <para>⚠️ <c>OnClearCalibration</c>'ın zıddı DEĞİLDİR: o hizalamayı siler, bu onu geri
        /// getirmeyi <b>dener</b> — "hizalandım" işaretini yine başlık koyar.</para></summary>
        public static event Action OnReloadCalibration;

        /// <summary>Yalnız admin bağlantılarına gelir (§5.3): bir oyuncunun kayıtlı hizalamayı
        /// yeniden yükleme denemesi sonuçlandı. <b>Olaydır, durum değildir</b> — durumu roster
        /// taşır; bu yalnız operatörün bastığı düğmenin cevabıdır.
        /// <para>Sunucu bekleyen istek defteri tutmaz: hangi sonucun hangi düğmeye ait olduğunu
        /// dinleyen arayüz bilir (bekleyen satırı yoksa yok sayar).</para></summary>
        public static event Action<CalibrationResultMsg> OnCalibrationResult;

        /// <summary>Uzak bir oyuncunun atış/atma olayı (UDP 0x04 EventBatch, §6.5) — v4'te WS
        /// <c>shot_fired</c>'ın yerini aldı. <c>ArenaClient</c> DEĞİL <c>UdpStateChannel</c>
        /// yayınlar, ama diğerleri gibi ANA thread'de. Kendi olaylarımız kanalda süzülür.</summary>
        public static event Action<RemoteFireEvent> OnRemoteFireEvent;

        /// <summary>Yalnız admin bağlantılarına gelir (§5.3): adminler arası ortak mod/harita
        /// seçimi + son eylem duyurusu. App katmanında <c>AdminSelection</c> dinler.</summary>
        public static event Action<AdminStateMsg> OnAdminState;

        /// <summary>HERKESE gelir (§5.3): koşan maçın kural şekli DEĞİŞTİ. <b>Gerçek bir kural
        /// mesajıdır</b> ve <c>ModeRuntime</c>'a uygulanır (<c>ModeRuntimePump</c> dinler).
        /// Bugün tek tetikleyicisi operatörün dost ateşi anahtarıdır (§5.2).</summary>
        public static event Action<RulesUpdateMsg> OnRulesUpdate;

        /// <summary>HERKESE gelir (§5.3): seçili modun takım kipi. <b>Kural değildir</b> —
        /// <c>ModeRuntime</c>'a uygulanmaz; tek tüketicisi taban şeritlerinin görünürlüğüdür.
        /// Core katmanında <c>ModeRuntimePump</c> dinleyip <c>ModeSelection</c>'a yazar.</summary>
        public static event Action<SelectionStateMsg> OnSelectionState;

        /// <summary>Yalnız admin bağlantılarına gelir (§6.7), 1 Hz: oyuncu başına ping/jitter/kayıp.
        /// Değerleri İSTEMCİLER ölçer, sunucu taşır. App katmanında <c>AdminRoster</c> dinler.
        /// <para>Kaybı zararsızdır — bir sonraki saniye yenisi gelir, uzlaştırma yoktur.</para></summary>
        public static event Action<NetStatsMsg> OnNetStats;

        /// <summary>Yalnız admin bağlantılarına gelir (§5.3): bir oyuncunun fiziksel ihlali başladı
        /// ya da bitti (engel / alan dışı). Defteri sunucu tutar, bu yalnız kenar bildirimidir.
        /// <para>Kaybı zararsızdır — ihlalin görsel karşılığı (kuş bakışı halkası) snapshot
        /// bitlerinden besleniyor, kaybolan mesaj yalnız bir feed satırı eksiltir.</para></summary>
        public static event Action<ViolationMsg> OnViolation;

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
        internal static void RaiseMeasureBodyScale() { OnMeasureBodyScale?.Invoke(); }
        internal static void RaiseClearCalibration(bool keepSaved) { OnClearCalibration?.Invoke(keepSaved); }
        internal static void RaiseReloadCalibration() { OnReloadCalibration?.Invoke(); }
        internal static void RaiseCalibrationResult(CalibrationResultMsg msg) { OnCalibrationResult?.Invoke(msg); }
        internal static void RaiseAdminState(AdminStateMsg msg) { OnAdminState?.Invoke(msg); }
        internal static void RaiseSelectionState(SelectionStateMsg msg) { OnSelectionState?.Invoke(msg); }
        internal static void RaiseRulesUpdate(RulesUpdateMsg msg) { OnRulesUpdate?.Invoke(msg); }
        internal static void RaiseNetStats(NetStatsMsg msg) { OnNetStats?.Invoke(msg); }
        internal static void RaiseViolation(ViolationMsg msg) { OnViolation?.Invoke(msg); }
    }
}
