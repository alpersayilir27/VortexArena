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
    /// "hizalandım" denir; (2) sunucu sıfırlayınca <see cref="ArenaCalibrator.Invalidate"/>
    /// çağrılır — hizalama fiilen bozuktur, kayıtlı anchor da silinmelidir (yoksa sonraki
    /// <c>load_match</c> bozuk hizalamayı sessizce geri yükler).
    /// </para>
    /// <para>
    /// İkinci yönün <b>asıl kapısı</b> <c>clear_calibration</c> komutudur (§5.3) ve
    /// <b>koşulsuzdur</b>: operatör hangi aşamadaki oyuncuya basarsa bassın, başlık yarım kalmış
    /// sekans dahil her şeyi siler. Roster'daki <c>calibrated:false</c> ikinci savunma hattıdır —
    /// tek başına yeterli değildir, çünkü yarım kalmış bir kalibrasyonda o alan <b>zaten</b>
    /// <c>false</c>'tur ve sıfırlamanın orada görünür bir deltası yoktur (§10.6).
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
            client.Send(_reportMsg);
        }

        // ------------------------------------------------------------- sunucu → başlık

        private void HandleConnected(WelcomeMsg msg)
        {
            _hasEverConnected = true;

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

        private void ApplyServerState(bool calibrated, string source)
        {
            // Sunucu sıfırladı ama yerelde hizalama duruyor → hizalama fiilen bozuktur,
            // işaretçileri gizle ve KAYITLI ANCHOR'I SİL. Anchor'ı bırakmak, sonraki load_match'te
            // bozuk hizalamanın geri yüklenmesi olurdu (§10.4). ⚠️ Rig TAŞINMAZ — free-roam kuralı.
            //
            // İKİNCİ savunma hattıdır: operatörün sıfırlaması zaten HandleClearCalibration'dan
            // geçti (§5.3, koşulsuz). Burası roster'ın başka bir yoldan `false` taşıdığı durumları
            // yakalar — hello'nun sıfırlaması ya da komutu iletmeyen eski bir sunucu.
            if (!calibrated && _localCalibrated)
            {
                _localCalibrated = false;
                _source = "";
                ArenaCalibrator calibrator = FindFirstObjectByType<ArenaCalibrator>();
                if (calibrator != null)
                {
                    calibrator.Invalidate();
                }

                Debug.Log("[CalibrationState] Sunucu kalibrasyonu sıfırladı — yeniden kalibre edin (A basılıyken B×2).");
            }

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
