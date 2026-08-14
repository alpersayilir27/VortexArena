using System;
using UnityEngine;
using VortexArena.Core.Combat;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// Yerel oyuncunun kalibrasyon durumu — sunucu ile başlık arasındaki iki yönlü köprü (§10.6).
    /// <para>
    /// <b>Otorite sunucudadır.</b> "Kalibreli miyim" sorusunun cevabı <c>lobby_state</c>'ten gelir,
    /// sahnedeki <see cref="ArenaCalibrator"/>'ün kendi sayacından DEĞİL: operatör admin ekranından
    /// kalibrasyonu sıfırlayabilir ve o an başlık hâlâ kendini hizalı sanıyor olabilir.
    /// </para>
    /// <para>
    /// İki yön: (1) <see cref="ArenaCalibrator.Calibrated"/> → <c>set_calibration</c> ile sunucuya
    /// "hizalandım" denir; (2) operatör sıfırlayınca <c>clear_calibration</c> komutu gelir ve
    /// <see cref="ArenaCalibrator.Invalidate"/> çağrılır — hizalama fiilen bozuktur, kayıtlı anchor
    /// da silinmelidir (yoksa sonraki <c>load_match</c> bozuk hizalamayı sessizce geri yükler).
    /// Komut <b>koşulsuzdur</b>: operatör hangi aşamadaki oyuncuya basarsa bassın, başlık yarım
    /// kalmış sekans dahil her şeyi siler (§5.3).
    /// </para>
    /// <para>
    /// ⚠️ <b>Silmenin TEK kapısı o komuttur.</b> Roster'daki <c>calibrated:false</c> bir sıfırlama
    /// SİNYALİ DEĞİLDİR ve öyle okunmaz: sunucu her <c>hello</c>'da o alanı sıfırlıyor (§10.6), yani
    /// değer her yeniden bağlanışta — sıradan bir ağ dalgalanmasında bile — bir kez <c>false</c>
    /// yayınlanıyor. Ona bakıp hizalama silinseydi kopup dönen başlık <b>kayıtlı anchor'ını
    /// kaybederdi</b>. Roster bu sınıfta yalnız <b>ayna</b>dır (<see cref="ApplyServerState"/>).
    /// </para>
    /// <para>
    /// ⚠️ <b>Hiç bağlanılmamışsa kapı AÇIKTIR</b> (<see cref="IsCalibrated"/> = true,
    /// <see cref="ManualAllowed"/> = true): sunucusuz editör testinde silah ve elle kalibrasyon
    /// çalışmaya devam etsin. <c>PlayerCombatState.CanFire</c>'daki "_hasEverConnected" ile aynı gerekçe.
    /// </para>
    /// <para>
    /// Sahnede DURMAZ: kalıcı tekil olarak kendini önyükler (<see cref="PlayerCombatState"/>
    /// deseni) — her arenaya elle bir kurulum adımı eklememek için.
    /// </para>
    /// </summary>
    public class CalibrationState : MonoBehaviour
    {
        public static CalibrationState Instance { get; private set; }

        private static bool _hasEverConnected;
        private static bool _serverCalibrated;
        private static bool _localCalibrated;
        private static string _source = "";

        /// <summary>
        /// Sunucunun bildiği hizalama durumu. Kalibresizken oyuncu ateş EDEMEZ, hasar YEMEZ ve
        /// canlanamaz (§10.6 — üçünün de otoritesi sunucuda, bu yalnız istemci aynası).
        /// Hiç bağlanılmadıysa true (sunucusuz test akışı bozulmasın).
        /// </summary>
        public static bool IsCalibrated => !_hasEverConnected || _serverCalibrated;

        /// <summary>
        /// Kumandada ELLE kalibrasyon (A basılıyken B'ye çift basış) açık mı. Kalibreli durumdayken kapalıdır: oyuncu
        /// kendi hizalamasını kazara bozamasın, kapıyı yalnız operatör açsın (§10.6).
        /// </summary>
        public static bool ManualAllowed => !_hasEverConnected || !_serverCalibrated;

        /// <summary>Son bildirilen kaynak ("manual" | "anchor" | "cloud" | "").</summary>
        public static string Source => _source;

        /// <summary>
        /// Sunucunun yürürlükteki kalibre modu (<c>ArenaProtocol.CALIB_MODE_*</c>, §10.6); boş =
        /// hiç <c>welcome</c> alınmadı.
        /// <para>⚠️ Bağlantı kopunca SIFIRLANMAZ (son bilinen değer kalır): mod bir oturum
        /// durumu değil operatör kararıdır, kopan ağ onu unutturmaz. Yeni <c>welcome</c> üzerine
        /// yazar.</para>
        /// </summary>
        public static string Mode { get; private set; } = "";

        /// <summary>
        /// Başlık AÇILIŞTA diskteki <c>OVRSpatialAnchor</c> UUID'sinden hizalamayı geri
        /// yükleyebilir mi (§10.6).
        /// <para>
        /// Boş mod = hiç <c>welcome</c> alınmamış (sunucusuz sandbox) → geliştirme kolaylığı için
        /// geri yükleme serbest; <c>saved_anchor</c> zaten bunu ister.
        /// <c>two_anchor</c>'da yalnız <b>DİSK</b> geri yüklemesi kapanır: oturum-içi (bellekteki
        /// çapa) geri yükleme moddan BAĞIMSIZDIR — harita değişimi onunla taşınır ve o kapı
        /// kapanırsa her arena geçişinde oyuncu yeniden kalibre etmek zorunda kalırdı.
        /// </para>
        /// </summary>
        public static bool DiskRestoreAllowed =>
            string.IsNullOrEmpty(Mode) || Mode == ArenaProtocol.CALIB_MODE_SAVED_ANCHOR;

        /// <summary>Durum değiştiğinde (ana thread).</summary>
        public static event Action Changed;

        // Her bildirimde yeni DTO ayırmamak için tek örnek.
        private readonly SetCalibrationMsg _reportMsg = new SetCalibrationMsg();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var go = new GameObject("[CalibrationState]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<CalibrationState>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // Kalıcı tekiliz: obje devre dışı bırakılsa bile olaylar kaçmasın diye
            // OnEnable/OnDisable yerine Awake/OnDestroy'da abone oluruz.
            ArenaCalibrator.Calibrated += HandleLocalCalibrated;
            NetEvents.OnConnected += HandleConnected;
            NetEvents.OnLobbyState += HandleLobbyState;
            NetEvents.OnClearCalibration += HandleClearCalibration;
            NetEvents.OnReloadCalibration += HandleReloadCalibration;
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            ArenaCalibrator.Calibrated -= HandleLocalCalibrated;
            NetEvents.OnConnected -= HandleConnected;
            NetEvents.OnLobbyState -= HandleLobbyState;
            NetEvents.OnClearCalibration -= HandleClearCalibration;
            NetEvents.OnReloadCalibration -= HandleReloadCalibration;

            Instance = null;
        }

        // ------------------------------------------------------------- başlık → sunucu

        /// <summary>Başlık hizalandı (elle ya da kayıtlı anchor) → sunucuya bildir.</summary>
        private void HandleLocalCalibrated(string source)
        {
            _localCalibrated = true;
            _source = source ?? "";
            Report(true, _source);
        }

        private void Report(bool calibrated, string source)
        {
            ArenaClient client = ArenaClient.Instance;
            if (client == null || !client.IsConnected)
            {
                return; // sunucusuz oturum: bildirilecek kimse yok
            }

            _reportMsg.calibrated = calibrated;
            _reportMsg.source = source ?? "";
            // ⚠️ DTO tek örnektir: bayat bir gerekçe bir sonraki BAŞARILI bildirimi kirletirdi
            // (sunucu error doluysa hizalamayı yok sayıyor, §10.6) — BodyScaleState.Report'taki
            // aynı tuzak.
            _reportMsg.error = "";
            // Zemin sapması yalnız ÖLÇÜLDÜĞÜNDE anlamlıdır (§10.6): kalibrasyonun düştüğü yolda
            // ölçüm yoktur ve son ölçümü tekrar göndermek operatöre bayat bir uyarı gösterirdi.
            _reportMsg.floorOffset = calibrated ? ArenaCalibrator.LastFloorOffsetMeters : 0f;
            client.Send(_reportMsg);
        }

        // ------------------------------------------------------------- sunucu → başlık

        private void HandleConnected(WelcomeMsg msg)
        {
            _hasEverConnected = true;

            // Mod bağlantıda BİR KEZ gelir (§10.6): kapıladığı karar — açılışta diskten çapa geri
            // yüklenecek mi — tam da bu anda veriliyor, sonradan yayılsa uygulanacağı bir an olmazdı.
            Mode = msg != null ? msg.calibrationMode ?? "" : "";

            // Sunucu hello'da kalibrasyonu sıfırlar (§10.6) — yerelde hizalama duruyorsa
            // (ör. bağlantı koptu, anchor'dan geri yüklenmişti) onu yeniden bildir.
            _serverCalibrated = false;
            if (_localCalibrated)
            {
                Report(true, _source);
            }

            Raise();
        }

        /// <summary>
        /// Operatör kalibrasyonu sıfırladı (§10.6). <b>Koşulsuzdur:</b> yerelde hizalama olup
        /// olmadığına bakılmaz, çünkü silinecek şeyin bir kısmı hizalama DEĞİLDİR — yarım kalmış
        /// elle kalibrasyon (A alındı, B bekleniyor) tam da <see cref="_localCalibrated"/> hâlâ
        /// <c>false</c>'ken vardır. Kapıyı ona bağlamak, komutu var olma sebebi olan durumda
        /// işlevsiz bırakırdı.
        /// <para>
        /// <see cref="_serverCalibrated"/> de burada düşürülür: sunucunun bir sonraki
        /// <c>lobby_state</c>'i onu zaten <c>false</c> taşıyacak, ama elle kalibrasyon kapısının
        /// (<see cref="ManualAllowed"/>) o yayını beklemesi için bir sebep yok — oyuncu sekansa
        /// hemen başlayabilmeli.
        /// </para>
        /// <para>⚠️ Rig TAŞINMAZ — free-roam kuralı: oyuncu fiziksel olarak neredeyse orada kalır,
        /// yalnız hizalama geçersiz sayılır.</para>
        /// </summary>
        private void HandleClearCalibration()
        {
            _localCalibrated = false;
            _serverCalibrated = false;
            _source = "";

            ArenaCalibrator calibrator = FindFirstObjectByType<ArenaCalibrator>();
            if (calibrator != null)
            {
                calibrator.Invalidate();
            }

            Debug.Log("[CalibrationState] Operatör kalibrasyonu sıfırladı — yeniden kalibre edin (A basılıyken B×2).");
            Raise();
        }

        /// <summary>
        /// Operatör kayıtlı çapadan hizalamayı yeniden yükletti (§10.6). Sıfırlamanın zıddı
        /// DEĞİLDİR: burada hiçbir şey silinmez, kalibratöre yalnız <b>yeniden deneme</b> yaptırılır
        /// ve "hizalandım" işaretini yine başlık koyar.
        /// </summary>
        private void HandleReloadCalibration()
        {
            ArenaCalibrator.RequestReload(HandleReloadResult);
        }

        /// <summary>
        /// Yeniden yükleme denemesinin sonucu (boş gerekçe = başarılı).
        /// <para>⚠️ <b>Başarıda hiçbir şey gönderilmez:</b> başarılı yükleme zaten
        /// <see cref="ArenaCalibrator.Calibrated"/> olayından geçip <see cref="Report"/> ile
        /// bildiriliyor — buradan ikinci bir bildirim çift sonuç satırı üretirdi (§5.3).</para>
        /// <para>Başarısızlıkta gerekçe <c>set_calibration.error</c> ile gider ve
        /// <see cref="_localCalibrated"/>/<see cref="_source"/> <b>olduğu gibi</b> taşınır: durum
        /// değişmedi, deneme düştü. <c>floorOffset</c> <c>0</c>'dır — ölçüm yok.</para>
        /// </summary>
        private void HandleReloadResult(string error)
        {
            if (string.IsNullOrEmpty(error))
            {
                return;
            }

            ArenaClient client = ArenaClient.Instance;
            if (client == null || !client.IsConnected)
            {
                return; // sunucusuz oturum: bildirilecek kimse yok
            }

            Debug.LogWarning($"[CalibrationState] Kayıtlı hizalama yeniden yüklenemedi — {error}.", this);

            _reportMsg.calibrated = _localCalibrated;
            _reportMsg.source = _source ?? "";
            _reportMsg.floorOffset = 0f;
            _reportMsg.error = error;
            client.Send(_reportMsg);
        }

        /// <summary>
        /// Roster'daki kendi satırımız kalibrasyon durumunun TEK doğruluk kaynağıdır (§5.3).
        /// <para>
        /// ⚠️ <b>Bağlantı koptuğunda durum SIFIRLANMAZ</b> (bu yüzden bir <c>OnDisconnected</c>
        /// işleyicisi yoktur): sıfırlansaydı ağ kesildiği anda oyuncuya "Kalibrasyon gerekli"
        /// yazardık — asıl sorun ağ iken onu boşuna kalibrasyona gönderirdik. Yeniden bağlanınca
        /// sunucu <c>hello</c>'da zaten sıfırlıyor (§10.6) ve <see cref="HandleConnected"/>
        /// yerel durumu yeniden bildiriyor.
        /// </para>
        /// </summary>
        private void HandleLobbyState(LobbyStateMsg msg)
        {
            int selfId = PlayerCombatState.Instance != null ? PlayerCombatState.Instance.PlayerId : 0;
            if (msg == null || msg.players == null || selfId == 0)
            {
                return;
            }

            for (int i = 0; i < msg.players.Length; i++)
            {
                PlayerInfo info = msg.players[i];
                if (info == null || info.playerId != selfId)
                {
                    continue;
                }

                ApplyServerState(info.calibrated, info.calibrationSource);
                return;
            }
        }

        /// <summary>
        /// Roster'daki değeri yerel aynaya yazar — <b>yalnız bayrak</b>.
        /// <para>
        /// ⚠️ <b>Buradan hizalama SİLİNMEZ</b> (<see cref="ArenaCalibrator.Invalidate"/> çağrılmaz,
        /// <see cref="_localCalibrated"/> düşürülmez). Roster'ın <c>calibrated:false</c> taşıması
        /// "operatör sıfırladı" demek değildir: sunucu her <c>hello</c>'da alanı sıfırladığı için
        /// (§10.6) o değer <b>her yeniden bağlanışta</b> bir kez yayınlanıyor ve
        /// <see cref="HandleConnected"/>'in yeniden bildirimi ile aynı sokette yarışıyor.
        /// </para>
        /// <para>
        /// Silmeyi buraya bağlamanın bedeli sessizdir ve gecikmelidir: rig o an TAŞINMADIĞI için
        /// oturum düzgün görünür, ama kayıtlı <c>OVRSpatialAnchor</c> gitmiştir — sonraki
        /// <c>load_match</c>'te geri yüklenecek hizalama kalmaz ve oyuncu <b>herkese metrelerce
        /// kaymış</b> çizilir. Üstelik yeniden bildirim sunucuyu "kalibreli" yaptığı için
        /// <see cref="ManualAllowed"/> de kapanır: oyuncu kendi başına düzeltemez.
        /// </para>
        /// <para>Sıfırlamanın tek kapısı <c>clear_calibration</c>'dır
        /// (<see cref="HandleClearCalibration"/>): operatörün niyeti orada açıkça geliyor, burada
        /// yalnız bir alanın anlık değeri var.</para>
        /// </summary>
        private void ApplyServerState(bool calibrated, string source)
        {
            if (_serverCalibrated == calibrated)
            {
                return;
            }

            _serverCalibrated = calibrated;
            if (calibrated && !string.IsNullOrEmpty(source))
            {
                _source = source;
            }

            Raise();
        }

        private static void Raise()
        {
            Changed?.Invoke();
        }
    }
}
