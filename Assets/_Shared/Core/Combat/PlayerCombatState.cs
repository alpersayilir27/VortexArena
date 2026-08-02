using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core.Arena;
using VortexArena.Net;
using VortexArena.Protocol;

// Takım enum'u için takma ad: sınıfta aynı adlı bir ÖZELLİK (Team) olduğu için
// enum üyelerine bu alias üzerinden erişilir (isim belirsizliği kalmasın).
using CoreTeam = VortexArena.Core.Team;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// YEREL oyuncunun maç/savaş durumu (kalıcı tekil): takım, can, hayatta mı, ateş yetkisi
    /// ve free-roam canlanma akışı.
    /// <para>
    /// Sunucu-otoriter: can yalnız <c>health_update</c>'ten, faz yalnız
    /// <c>match_state</c>/<c>countdown</c>/<c>load_match</c>'ten gelir. Bu sınıf hasar
    /// uygulamaz, skor tutmaz, faz değiştirmez (Docs/ArenaNet-Protokol.md §10).
    /// </para>
    /// <para>
    /// ⚠️ FREE-ROAM KURALI: fiziksel oyuncu IŞINLANAMAZ. Canlanma bir konum değil
    /// DURUM değişimidir — bu sınıf hiçbir koşulda rig'i/kamerayı taşımaz. Aynısı harita
    /// değişimi için de geçerli: <c>load_match</c> kimseyi "yeniden doğurmaz" ve kalibrasyonu
    /// sıfırlamaz (§10.4). Ölünce dönülecek yer <see cref="BaseZone"/> (taban bölgesi);
    /// <see cref="SpawnPoint"/> yalnız maç öncesi yerleşim göstergesidir.
    /// </para>
    /// <para>
    /// Sahnede DURMAZ: <c>load_match</c> sahne yüklenmeden ÖNCE gelir, bu yüzden
    /// ArenaClient/SceneRouter deseniyle kendini önyükler ve DontDestroyOnLoad olur.
    /// </para>
    /// </summary>
    public class PlayerCombatState : MonoBehaviour
    {
        // Faz adları protokolde string taşınır (Docs §10.1). ÜÇ değer vardır: paused/playing/
        // finished. Sabitler ArenaProtocol'den gelir — tel değerinin tek yazıldığı yer orasıdır.
        private const string PhasePaused = ArenaProtocol.PHASE_PAUSED;
        private const string PhasePlaying = ArenaProtocol.PHASE_PLAYING;
        private const string PhaseFinished = ArenaProtocol.PHASE_FINISHED;

        /// <summary>Canlanma talebi tekrar aralığı (sunucu onaylayana dek, §10.4).</summary>
        private const float ReviveRepeatSeconds = 1f;

        /// <summary>Kendi tabanı bulunamadığında sahneyi yeniden tarama aralığı.</summary>
        private const float ZoneRescanSeconds = 1f;

        public static PlayerCombatState Instance { get; private set; }

        /// <summary>welcome'da atanan kimlik (0 = henüz bağlanılmadı).</summary>
        public int PlayerId { get; private set; }

        /// <summary>Takım: load_match.yourTeam (lobide lobby_state'ten de güncellenir).
        /// Başlangıç <see cref="CoreTeam.Neutral"/>'dır: takımsız modda oyuncu kendini kırmızı
        /// sanıp yanlış tabana yönlendirilmesin (§10.5).</summary>
        public Team Team
        {
            get => _team;
            private set
            {
                if (_team == value)
                {
                    return;
                }

                _team = value;
                LocalTeamChanged?.Invoke(value);
            }
        }

        private CoreTeam _team = CoreTeam.Neutral;

        /// <summary>Aktif maçın modId'si (load_match'ten).</summary>
        public string ModeId { get; private set; } = "";

        /// <summary>Sunucudan gelen son faz adı: <c>paused</c> | <c>playing</c> | <c>finished</c>
        /// (§10.1). Bilinmeyen bir değer gelirse duraklamış sayılır — hasar/ateş kapısı
        /// güvenli tarafta kalsın.</summary>
        public string Phase { get; private set; } = PhasePaused;

        /// <summary>Duraklamanın gerekçesi (§10.1 <c>phaseReason</c>): <c>lobby</c>/<c>loading</c>/
        /// <c>countdown</c>/<c>operator</c>/<c>mode</c>; duraklı değilken boş. Yalnız SUNUM
        /// içindir — hiçbir savaş kapısı buna bakmaz.</summary>
        public string PhaseReason { get; private set; } = ArenaProtocol.PAUSE_REASON_LOBBY;

        /// <summary>Modun kendi ara durumu (§10.1 <c>modeState</c>); çekirdek yorumlamaz, HUD okur.</summary>
        public string ModeState { get; private set; } = "";

        /// <summary>Yerel oyuncunun canı — YALNIZ health_update'ten set edilir.</summary>
        public float Hp { get; private set; } = ArenaProtocol.PLAYER_MAX_HP;

        /// <summary>Hayatta mı (hp &gt; 0; respawn mesajıyla da false olur).</summary>
        public bool IsAlive { get; private set; } = true;

        /// <summary>Ölüm/canlanma durum metni; canlıyken boş.</summary>
        public string StatusText { get; private set; } = "";

        /// <summary>
        /// Sahnede oyuncuya AÇIK (kendi takımı ya da <c>Neutral</c>) en az bir taban bölgesi var mı.
        /// <para>Yalnız takip açıkken anlamlıdır — bkz. <see cref="RequestBaseTracking"/>.</para>
        /// </summary>
        public bool HasOpenBaseZone { get; private set; }

        /// <summary>Oyuncu şu an kendine açık bir taban bölgesinin İÇİNDE mi.
        /// <para>
        /// ⚠️ <b>Bu bilgi canlanmadan bağımsızdır.</b> İki tüketicisi var ve şartları aynı değil:
        /// canlanma (yalnız <see cref="ModeReviveAnchor.OwnBase"/> kipinde, yalnız ölüyken) ve tur
        /// tabanlı modun toplanma kapısı (canlı/ölü fark etmez, kip <see cref="ModeReviveAnchor.None"/>
        /// olsa bile). Bölge eşleşme kuralı (§10.4) bu sınıfta TEK yerde durur ve kopyalanmaz.
        /// </para></summary>
        public bool IsInsideOwnBase { get; private set; }

        public event Action<float> HpChanged;
        public event Action<bool> AliveChanged;
        public event Action<string> StatusChanged;

        /// <summary>
        /// Yerel oyuncunun takımı değişti (<c>load_match</c> / <c>lobby_state</c>); yalnız DEĞER
        /// değişince tetiklenir.
        /// <para>
        /// ⚠️ Diğerlerinin aksine <b>statik</b>: dinleyicisi (<c>BaseZoneVisibility</c>) kendini
        /// önyükleyen kalıcı bir tekil ve <see cref="Instance"/>'tan ÖNCE doğabiliyor — örnek
        /// olayına abone olabilmek için önce örneğin doğmasını beklemesi gerekirdi.
        /// <c>ModeSelection.Changed</c> / <c>ModeRuntime.Changed</c> ile aynı desen.
        /// </para>
        /// </summary>
        public static event Action<CoreTeam> LocalTeamChanged;

        /// <summary>
        /// Silah tetiği çekilebilir mi: hayatta + (faz <c>playing</c> <b>veya</b> modun serbest
        /// atışı açık — <see cref="ModeRuntime.FireWhilePaused"/>, §10.5).
        /// <para>
        /// ⚠️ <b>Bu bir MOD kuralıdır, faz kuralı değil.</b> Lobi türünde serbest atış açıktır ve
        /// hedeflere ateş edilir; hasar yine yoktur çünkü onu sunucu <c>playing</c> kapısıyla
        /// kapatır (§10.3). Buraya <c>if (modeId == "lobby")</c> YAZILMAZ — yeni bir mod (turnuva
        /// ısınması gibi) aynı davranışı isterse kendi kuralında bildirir.
        /// </para>
        /// <para>
        /// Bağlantı koşulu: bir kez bağlandıysak bağlantı kopukken ateş kilitlenir
        /// (mesajlar zaten sunucuya ulaşmaz). Hiç bağlanılmamışsa (sunucusuz Editor
        /// testi) silah çalışmaya devam eder — sunucusuz yerel test akışı bozulmasın.
        /// </para>
        /// </summary>
        public bool CanFire
        {
            get
            {
                if (!IsAlive)
                {
                    return false;
                }

                // §10.6: kalibresiz oyuncu ateş edemez. Sunucu zaten hit_report'u reddediyor;
                // burada da kapatmak "tetiği çektim ama hiçbir şey olmadı" hissini engeller.
                if (!CalibrationState.IsCalibrated)
                {
                    return false;
                }

                if (Phase != PhasePlaying && !ModeRuntime.FireWhilePaused)
                {
                    return false;
                }

                if (!_hasEverConnected)
                {
                    return true;
                }

                ArenaClient client = ArenaClient.Instance;
                return client != null && client.IsConnected;
            }
        }

        // Her frame yeni DTO ayırmamak için tek örnek (alansız mesaj).
        private readonly ReviveRequestMsg _reviveMsg = new ReviveRequestMsg();

        private bool _hasEverConnected;
        private bool _awaitingRevive;
        private float _reviveAt;
        private float _nextReviveSendAt;

        private BaseZone[] _zones = Array.Empty<BaseZone>();
        private float _nextZoneScanAt;

        /// <summary>Taban takibinin geçerlilik süresi (<see cref="RequestBaseTracking"/>).
        /// Süreli olması bilinçli: talep eden bileşen (tur toplanma raporlayıcısı) yok olduğunda
        /// takip kendiliğinden kapanır, kimsenin "kapat" demeyi unutması mümkün olmaz.</summary>
        private float _baseTrackingUntil;

        /// <summary>Modun yazdığı durum yönergesi (<see cref="SetModePrompt"/>); boş = yok.</summary>
        private string _modePrompt = "";

        // StandStill canlanma çapası (§10.4/2). _hasHoldAnchor aynı zamanda "kafa izlenebiliyor mu"
        // demektir: kamera yoksa çapa kurulamaz ve sabit durma şartı hiç aranmaz.
        private Vector3 _holdAnchor;
        private bool _hasHoldAnchor;
        private float _holdSince;
        private Transform _head;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var go = new GameObject("[PlayerCombatState]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<PlayerCombatState>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // İkinci kopya (sahneye elle konmuş olabilir) kendini yok eder.
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // Kalıcı tekiliz: OnEnable/OnDisable yerine Awake/OnDestroy'da abone oluruz,
            // böylece obje devre dışı bırakılsa bile sunucu olayları kaçmaz.
            NetEvents.OnConnected += HandleConnected;
            NetEvents.OnDisconnected += HandleDisconnected;
            NetEvents.OnLobbyState += HandleLobbyState;
            NetEvents.OnLoadMatch += HandleLoadMatch;
            NetEvents.OnCountdown += HandleCountdown;
            NetEvents.OnMatchState += HandleMatchState;
            NetEvents.OnHealthUpdate += HandleHealthUpdate;
            NetEvents.OnRespawn += HandleRespawn;
            NetEvents.OnMatchEnd += HandleMatchEnd;
            NetEvents.OnReturnToLobby += HandleReturnToLobby;

            SceneManager.sceneLoaded += HandleSceneLoaded;
            ScanZones();
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            NetEvents.OnConnected -= HandleConnected;
            NetEvents.OnDisconnected -= HandleDisconnected;
            NetEvents.OnLobbyState -= HandleLobbyState;
            NetEvents.OnLoadMatch -= HandleLoadMatch;
            NetEvents.OnCountdown -= HandleCountdown;
            NetEvents.OnMatchState -= HandleMatchState;
            NetEvents.OnHealthUpdate -= HandleHealthUpdate;
            NetEvents.OnRespawn -= HandleRespawn;
            NetEvents.OnMatchEnd -= HandleMatchEnd;
            NetEvents.OnReturnToLobby -= HandleReturnToLobby;

            SceneManager.sceneLoaded -= HandleSceneLoaded;

            Instance = null;
        }

        private void Update()
        {
            if (Instance != this)
            {
                return;
            }

            TickHoldAnchor();
            RefreshZoneState();
            TickRevive();
            RefreshStatusText();
        }

        // ------------------------------------------------------- free-roam canlanma

        /// <summary>
        /// §10.4: gecikme dolduktan VE modun canlanma şartı sağlandıktan sonra
        /// <c>revive_request</c> gönderir; sunucu canlandırana (hp &gt; 0 health_update) dek
        /// saniyede bir tekrarlar. Şart <see cref="ModeRuntime.Revive"/>'dan gelir — bu sınıfta
        /// mod adına bakan hiçbir dal YOKTUR.
        /// </summary>
        private void TickRevive()
        {
            if (IsAlive || !_awaitingRevive || Time.time < _reviveAt)
            {
                return;
            }

            if (!IsReviveConditionMet())
            {
                return;
            }

            if (Time.time < _nextReviveSendAt)
            {
                return;
            }

            _nextReviveSendAt = Time.time + ReviveRepeatSeconds;
            ArenaClient.Instance?.Send(_reviveMsg);
        }

        /// <summary>
        /// StandStill kipinde ölüm anındaki HMD konumunu çapa alır ve
        /// <see cref="ArenaProtocol.REVIVE_HOLD_RADIUS"/>'u aşan her harekette çapayı da sayacı da
        /// sıfırlar. <b>Ölüm gecikmesi dolmadan da işler</b>: oyuncu beklerken sabit durduysa
        /// gecikme biter bitmez canlanır, ikinci bir bekleme dayatılmaz.
        /// </summary>
        private void TickHoldAnchor()
        {
            if (IsAlive || ModeRuntime.Revive != ModeReviveAnchor.StandStill)
            {
                _hasHoldAnchor = false;
                return;
            }

            Transform head = ResolveHead();
            if (head == null)
            {
                _hasHoldAnchor = false; // izlenemiyor → şart aranmayacak (bkz. IsReviveConditionMet)
                return;
            }

            Vector3 position = head.position;
            if (_hasHoldAnchor &&
                Vector3.Distance(position, _holdAnchor) <= ArenaProtocol.REVIVE_HOLD_RADIUS)
            {
                return;
            }

            _holdAnchor = position;
            _holdSince = Time.time;
            _hasHoldAnchor = true;
        }

        /// <summary>Modun canlanma şartı sağlandı mı (§10.5 <c>reviveAnchor</c>).
        /// <b>Şart ölçülemiyorsa sağlanmış sayılır</b> — sahnede açık taban bölgesi yok, kamera yok
        /// gibi durumlarda bu sınıf oyuncuyu kalıcı ölü bırakmaz; güvenlik ağı zaten sunucunun
        /// <c>REVIVE_GRACE</c>'idir.</summary>
        private bool IsReviveConditionMet()
        {
            // §10.5 reviveAnchor:"none" — tur tabanlı elemede canlanma YOKTUR. Sunucu zaten
            // reddediyor; burada kapatmak saniyede bir boşuna revive_request yollamayı ve ölüm
            // ekranında hiç gerçekleşmeyecek bir "canlanılıyor" metni göstermeyi engeller.
            if (ModeRuntime.Revive == ModeReviveAnchor.None)
            {
                return false;
            }

            if (ModeRuntime.Revive == ModeReviveAnchor.StandStill)
            {
                return !_hasHoldAnchor || HoldRemaining <= 0f;
            }

            return !HasOpenBaseZone || IsInsideOwnBase;
        }

        /// <summary>Sabit durma sayacında kalan saniye (0 = şart sağlandı).</summary>
        private float HoldRemaining =>
            !_hasHoldAnchor
                ? 0f
                : Mathf.Max(0f, ArenaProtocol.REVIVE_HOLD_SECONDS - (Time.time - _holdSince));

        /// <summary>HMD transformu (sahne değişiminde tazelenir); yoksa null.</summary>
        private Transform ResolveHead()
        {
            if (_head != null)
            {
                return _head;
            }

            Camera cam = Camera.main;
            _head = cam != null ? cam.transform : null;
            return _head;
        }

        private void RefreshStatusText()
        {
            string text = "";

            // §10.6 — ölüm metninden ÖNCE gelir: kalibresiz oyuncu zaten canlanamayacağı için
            // "Tabanına dön ve canlan" yazmak onu boşuna koşturmak olurdu.
            if (!CalibrationState.IsCalibrated)
            {
                text = "Kalibrasyon gerekli — sağ kumandada A basılıyken B×2";
            }
            else if (!string.IsNullOrEmpty(_modePrompt))
            {
                // Modun kendi yönergesi (ör. turlar arası toplanma). Ölüm metnini EZER: mod
                // duraklamasında canlanma diye bir şey yok, oyuncunun yapması gereken tek iş bu.
                text = _modePrompt;
            }
            else if (!IsAlive)
            {
                // §10.5 reviveAnchor:"none" — canlanma yok. Gecikme sayacı göstermek yalan olurdu;
                // oyuncu turun bitmesini bekliyor, bir süreyi değil.
                if (ModeRuntime.Revive == ModeReviveAnchor.None)
                {
                    text = "Elendin — takımın turu bitirene kadar bekle";
                }
                else
                {
                    float remaining = _reviveAt - Time.time;
                    if (remaining > 0f)
                    {
                        text = $"Öldün — canlanmaya {Mathf.CeilToInt(remaining)} sn";
                    }
                    else if (ModeRuntime.Revive == ModeReviveAnchor.StandStill)
                    {
                        float hold = HoldRemaining;
                        text = hold > 0f
                            ? $"Canlanmak için sabit dur — {Mathf.CeilToInt(hold)} sn"
                            : "Canlanılıyor...";
                    }
                    else
                    {
                        text = !HasOpenBaseZone || IsInsideOwnBase ? "Canlanılıyor..." : "Tabanına dön ve canlan";
                    }
                }
            }

            if (text == StatusText)
            {
                return;
            }

            StatusText = text;
            StatusChanged?.Invoke(text);
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ScanZones();
            _head = null; // yeni sahnenin kendi BB rig'i var; kamerayı yeniden çöz
        }

        private void ScanZones()
        {
            _zones = FindObjectsByType<BaseZone>(FindObjectsSortMode.None);
            _nextZoneScanAt = Time.time + ZoneRescanSeconds;
        }

        /// <summary>
        /// Taban bölgesi durumunu kare başına bir kez tazeler — <b>ölüyken</b> (canlanma şartı) ya
        /// da biri açıkça istediğinde (<see cref="RequestBaseTracking"/>, tur toplanması); başka
        /// hâllerde bayraklar temizlenir. Açık bölge bulunamazsa sahne saniyede bir yeniden
        /// taranır: bölge sonradan eklenmiş ya da takımımız <c>load_match</c> ile yeni gelmiş olabilir.
        /// </summary>
        private void RefreshZoneState()
        {
            // ⚠️ Takip ARTIK canlanma şartına bağlı DEĞİL. Eskiden "canlıysan ya da kip OwnBase
            // değilse hesaplama" deniyordu; tur tabanlı modun toplanma kapısı ikisini de ihlal
            // ediyor (canlı oyuncunun da tabanda olup olmadığı bilinmeli, üstelik kip "none").
            // Kapı yerine bir TALEP var: kimse istemiyorsa (lobi, FFA, canlı oyuncu) hesap yok.
            if (IsAlive && Time.time >= _baseTrackingUntil)
            {
                HasOpenBaseZone = false;
                IsInsideOwnBase = false;
                return;
            }

            EvaluateZonesNow();
        }

        /// <summary>Bölgeleri değerlendirir; açık bölge bulunamadıysa (ve tarama aralığı dolduysa)
        /// sahneyi bir kez yeniden tarayıp tekrar dener.</summary>
        private void EvaluateZonesNow()
        {
            EvaluateZones();
            if (HasOpenBaseZone || Time.time < _nextZoneScanAt)
            {
                return;
            }

            ScanZones();
            EvaluateZones();
        }

        /// <summary>
        /// Taban bölgesi takibini bir sonraki yarım saniye için açar; her karede çağrılması
        /// beklenir (kalp atışı deseni). Tur tabanlı modun toplanma raporlayıcısı bunu kullanır —
        /// oyuncu CANLI iken de "tabanımda mıyım" sorusunun cevabı gerekiyor.
        /// <para><b>Neden açık talep:</b> hesap sahnedeki tüm <see cref="BaseZone"/>'ları gezer ve
        /// bulunamadığında saniyede bir sahneyi yeniden tarar. Lobide ve taban bölgesi olmayan
        /// modlarda bunu koşulsuz yapmak kimsenin okumadığı bir iş olurdu.</para>
        /// </summary>
        public void RequestBaseTracking()
        {
            _baseTrackingUntil = Time.time + 0.5f;

            // ⚠️ Değerlendirme HEMEN yapılır, bir sonraki Update'e bırakılmaz. Bırakılsaydı
            // takibin açıldığı ilk karede bayraklar bir önceki (temizlenmiş) durumu gösterirdi:
            // HasOpenBaseZone=false → çağıran onu "sahnede taban yok" diye okur ve tabandan
            // metrelerce uzaktaki oyuncuyu hazır sayardı. Bileşen çalışma sırası bunu
            // öngörülemez kılıyordu.
            EvaluateZonesNow();
        }

        /// <summary>
        /// Modun kendi durum yönergesini yazar (ör. turlar arası "tabanına dön"); boş string
        /// temizler. Ölüm/canlanma metnini EZER, kalibrasyon uyarısını ezmez.
        /// <para>
        /// ⚠️ <b>Mod adına özel metin bu sınıfa YAZILMAZ</b> — istemcide <c>if (modeId == …)</c>
        /// zinciri doğmasın diye (§10.5). Bu sınıf yalnız <b>kuralın</b> (<c>reviveAnchor</c>)
        /// söylediğini yazar; modun kendi ara durumunun (<c>modeState</c>) ne anlama geldiğini
        /// yalnız mod bilir ve buradan yazar.
        /// </para>
        /// </summary>
        public void SetModePrompt(string prompt)
        {
            _modePrompt = prompt ?? "";
        }

        /// <summary>
        /// Bir taban bölgesi oyuncuya AÇIKTIR eğer takımı oyuncunun takımıyla aynıysa, bölge
        /// <see cref="CoreTeam.Neutral"/> ise (herkese açık joker) ya da oyuncunun takımı boşsa
        /// (takımsız mod, §10.5). Aynı takımdan birden çok bölge varsa <b>herhangi birine</b>
        /// girmek yeter.
        /// <para>
        /// Kapalı bileşen açık sayılmaz: <c>BaseZone.Update</c> koşmadığı için
        /// <c>IsPlayerInside</c> donar; açık saysaydık oyuncu bölgeye girse de canlanamaz,
        /// yalnız sunucunun <c>REVIVE_GRACE</c>'ini beklerdi.
        /// </para>
        /// </summary>
        private void EvaluateZones()
        {
            HasOpenBaseZone = false;
            IsInsideOwnBase = false;

            for (int i = 0; i < _zones.Length; i++)
            {
                BaseZone zone = _zones[i];
                if (zone == null || !zone.isActiveAndEnabled)
                {
                    continue;
                }

                if (zone.Team != this.Team && zone.Team != CoreTeam.Neutral && this.Team != CoreTeam.Neutral)
                {
                    continue;
                }

                HasOpenBaseZone = true;
                if (zone.IsPlayerInside)
                {
                    IsInsideOwnBase = true;
                    return;
                }
            }
        }

        // -------------------------------------------------------- olay işleyicileri

        private void HandleConnected(WelcomeMsg msg)
        {
            _hasEverConnected = true;

            if (msg == null)
            {
                return;
            }

            PlayerId = msg.playerId;

            // Geç katılım: welcome'daki durum bilgisiyle senkronla.
            string phase = msg.match != null && !string.IsNullOrEmpty(msg.match.phase) ? msg.match.phase : PhasePaused;
            if (msg.match != null)
            {
                PhaseReason = msg.match.phaseReason ?? "";
                ModeState = msg.match.modeState ?? "";
                if (!string.IsNullOrEmpty(msg.match.modeId))
                {
                    ModeId = msg.match.modeId;
                }
            }

            ResetCombat(phase);
        }

        private void HandleDisconnected()
        {
            PlayerId = 0;
            PhaseReason = ArenaProtocol.PAUSE_REASON_LOBBY;
            ModeState = "";
            ResetCombat(PhasePaused);
        }

        /// <summary>Takımı lobide de bilelim (canlanma bölgesi ve arayüz renkleri buna bakar).</summary>
        private void HandleLobbyState(LobbyStateMsg msg)
        {
            if (msg == null || msg.players == null || PlayerId == 0)
            {
                return;
            }

            for (int i = 0; i < msg.players.Length; i++)
            {
                PlayerInfo info = msg.players[i];
                if (info == null || info.playerId != PlayerId)
                {
                    continue;
                }

                // Boş takım da uygulanır: takımsız modda sunucu takımları TEMİZLER (§10.5),
                // eski değeri korumak oyuncuyu yanlış tabana yönlendirirdi.
                Team = ParseTeam(info.team);
                return;
            }
        }

        private void HandleLoadMatch(LoadMatchMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            Team = ParseTeam(msg.yourTeam);
            ModeId = msg.modeId ?? "";

            // Sahne yükleniyor: maç henüz BAŞLAMADI (§5.3) — faz paused/loading. Ateş kapalı
            // (lobi türünden çıkıldığı için fireWhilePaused da artık false), can tam, ölüm temiz.
            PhaseReason = ArenaProtocol.PAUSE_REASON_LOADING;
            ModeState = "";
            ResetCombat(PhasePaused);
        }

        private void HandleCountdown(CountdownMsg msg)
        {
            Phase = PhasePaused;
            PhaseReason = ArenaProtocol.PAUSE_REASON_COUNTDOWN;
        }

        private void HandleMatchState(MatchStateMsg msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.phase))
            {
                return;
            }

            Phase = msg.phase;
            PhaseReason = msg.phaseReason ?? "";
            ModeState = msg.modeState ?? "";
        }

        private void HandleHealthUpdate(HealthUpdateMsg msg)
        {
            if (msg == null || PlayerId == 0 || msg.playerId != PlayerId)
            {
                return;
            }

            SetHp(msg.hp);

            if (msg.hp > 0f)
            {
                // Sunucu canlandırdı (veya hasar aldık ama hayattayız).
                if (!IsAlive)
                {
                    _awaitingRevive = false;
                    SetAlive(true);
                }

                return;
            }

            // hp ≤ 0: respawn mesajı henüz gelmediyse modun gecikmesiyle ölüm durumu başlat
            // (sunucu respawn.delaySeconds'ta aynı değeri yolladığı için ikisi çakışmaz).
            BeginDeath(ModeRuntime.RespawnDelay, false);
        }

        private void HandleRespawn(RespawnMsg msg)
        {
            if (msg == null || PlayerId == 0 || msg.playerId != PlayerId)
            {
                return;
            }

            // Konum taşınmaz: respawn yalnız bir DURUM + gecikme bildirimidir (§10.4).
            BeginDeath(msg.delaySeconds, true);
        }

        private void HandleMatchEnd(MatchEndMsg msg)
        {
            Phase = PhaseFinished;
            PhaseReason = "";
        }

        private void HandleReturnToLobby(ReturnToLobbyMsg _)
        {
            PhaseReason = ArenaProtocol.PAUSE_REASON_LOBBY;
            ModeState = "";
            ResetCombat(PhasePaused);
        }

        // ---------------------------------------------------------------- yardımcı

        private void BeginDeath(float delaySeconds, bool authoritative)
        {
            float delay = Mathf.Max(0f, delaySeconds);

            if (IsAlive)
            {
                _reviveAt = Time.time + delay;
                _nextReviveSendAt = 0f;
                _awaitingRevive = true;
                _hasHoldAnchor = false; // StandStill sayacı ölümden itibaren başlar
                SetAlive(false);
                return;
            }

            // Zaten ölüyüz: yalnız sunucunun respawn mesajı zamanlayıcıyı güncelleyebilir.
            if (authoritative)
            {
                _reviveAt = Time.time + delay;
                _nextReviveSendAt = 0f;
                _awaitingRevive = true;
            }
        }

        private void ResetCombat(string phase)
        {
            Phase = string.IsNullOrEmpty(phase) ? PhasePaused : phase;
            _awaitingRevive = false;
            _nextReviveSendAt = 0f;
            _hasHoldAnchor = false;
            // Maç/sahne değişti: modun yönergesi artık geçersiz. Yazan bileşen (HUD prefabıyla
            // gelen mod raporlayıcısı) yok olmuş olabilir, temizliği ona bırakmayız.
            _modePrompt = "";
            SetHp(ArenaProtocol.PLAYER_MAX_HP);
            SetAlive(true);
            RefreshStatusText();
        }

        private void SetHp(float hp)
        {
            float clamped = Mathf.Clamp(hp, 0f, ArenaProtocol.PLAYER_MAX_HP);
            if (Mathf.Approximately(clamped, Hp))
            {
                return;
            }

            Hp = clamped;
            HpChanged?.Invoke(Hp);
        }

        private void SetAlive(bool alive)
        {
            if (IsAlive == alive)
            {
                return;
            }

            IsAlive = alive;
            AliveChanged?.Invoke(alive);
        }

        /// <summary>Protokoldeki "red"/"blue" değerini enum'a çevirir; <b>boş/tanımsız girdi
        /// <see cref="CoreTeam.Neutral"/> döner</b> — takımsız modda (§10.5) oyuncu kendini
        /// kırmızı sanmasın.</summary>
        private static Team ParseTeam(string team)
        {
            if (string.Equals(team, "red", StringComparison.OrdinalIgnoreCase))
            {
                return CoreTeam.Red;
            }

            if (string.Equals(team, "blue", StringComparison.OrdinalIgnoreCase))
            {
                return CoreTeam.Blue;
            }

            return CoreTeam.Neutral;
        }
    }
}
