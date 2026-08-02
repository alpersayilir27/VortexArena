using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.Core;
using VortexArena.Core.Arena;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// Tercihler paneli — eski dashboard'un işi buraya taşındı: <b>maç kontrolü</b> (mod/harita
    /// seçimi, başlat/iptal, duraklat/devam), <b>görünüm tercihleri</b> ve <b>bağlantı</b>.
    /// <para>
    /// Kart yarı saydamdır ve <b>arkasına scrim koyulmaz</b> — panel açıkken canlı sahne
    /// izlenmeye devam eder (kullanıcının açık isteği). Maç/oyun DURMAZ; otorite sunucudadır.
    /// </para>
    /// <para>
    /// <b>MAÇ bölümü ORTAKtır (çoklu admin).</b> Mod/harita seçicileri yerel bir alana yazmaz:
    /// <c>set_selection</c> ile sunucudaki ortak seçimi değiştirir, sunucu da onu tüm adminlere
    /// <c>admin_state</c> ile geri yayar (<see cref="AdminSelection"/>). Bu yüzden bir operatör
    /// haritayı değiştirdiğinde diğerinin paneli — <b>kapalı olsa bile</b>, bu bileşen HUD kökünde
    /// hep etkin olduğu için — yeni değere döner ve yerel önizlemesi o arenayı açar. Tıklama
    /// anında yerel imleç de ilerletilir (iyimser güncelleme); sunucudan gelen değer son sözü söyler.
    /// <br/><b>GÖRÜNÜM bölümü YERELdir</b> — her operatörün kendi ekranı (bkz. <see cref="AdminSession"/>).
    /// </para>
    /// <para>
    /// ⚠️ <b>Harita seçmek OYUNCULARI DA taşır (§10.7 sahneleme).</b> Sunucu lobideyken seçilen
    /// arenayı <c>return_to_lobby</c> ile tüm istemcilere yükletir — burası "bir sonraki maçın
    /// notu" değil anlık bir sahne komutudur. Bunun doğrudan sonucu: <b>mod/harita ancak maç
    /// KURULMAMIŞKEN değiştirilebilir</b> (<see cref="CanChangeSelection"/>) — koşan maçta,
    /// yüklemede, geri sayımda ve duraklatmada seçiciler pasiftir. Süre/limit her fazda açık kalır,
    /// onlar sahne yüklemez.
    /// </para>
    /// <para>
    /// <b>Lobi ayrı bir düğme değil, harita seçicisinin ilk satırıdır</b> (<see cref="LobbyRowLabel"/>):
    /// seçilince <c>set_selection</c> değil <c>return_to_lobby</c> gider. Aynı kilit ona da uygulanır,
    /// yani kurulmuş bir maçı lobiye çevirmenin yolu İPTAL'dir (sunucuda ikisi de aynı iştir).
    /// </para>
    /// <para>
    /// ⚠️ <b>Harita seçicisinin imleci ORTAK SEÇİMİ değil AÇIK SAHNEYİ izler</b>
    /// (<see cref="ApplyOpenScene"/>). İkisi ayrışır: maç bitip ya da lobiye dönülüp herkes lobiye
    /// alındığında sunucudaki ortak seçim hâlâ son arenayı gösterir, açık sahne ise lobidir. İmleç
    /// ortak seçime bağlanırsa seçici o arenayı gösterirken lobi açık olur — ve operatör <b>o
    /// arenaya geri dönemez</b>: <see cref="TMP_Dropdown"/> seçili satıra tıklamayı olay saymaz
    /// (<c>value == m_Value</c> iken <c>onValueChanged</c> hiç ateşlenmez), yani sahneleme komutu
    /// hiç gönderilemez. Aynı sebeple seçicide <b>"zaten seçili" erken çıkışı yoktur</b>.
    /// </para>
    /// <para>
    /// <b>Liste seçimi dropdown, sayı adımlayıcı:</b> mod/harita gibi <b>liste tabanlı</b> seçimler
    /// <c>TMP_Dropdown</c>'dır — seçenek sayısı katalogla büyüyor ve ok tuşlarıyla gezmek operatörü
    /// aradığı haritaya varana kadar tıklatıyordu. Süre/skor limiti gibi <b>sayısal</b> değerler
    /// <c>[-] değer [+]</c> adımlayıcı kalır: onların gezilecek bir listesi yok, asıl jest komşu
    /// değere gitmektir. Dropdown'ın şablon hiyerarşisi (viewport, item, scrollbar) prefabta durur;
    /// bu sınıf yalnız seçenekleri doldurur ve imleci eşitler.
    /// </para>
    /// </summary>
    public class AdminPreferencesPanel : MonoBehaviour
    {
        private const float PanelWidth = 760f;

        /// <summary>Panel yüksekliği. ⚠️ İçerik <b>elle yığılan</b> bir <c>y</c> ile diziliyor
        /// (Layout Group yok, UiKit kararı) — yani yeni bir satır eklemek burayı da ilgilendirir.
        /// KALİBRASYON bölümü eklenince içerik 810 px'e çıkmıştı, yani paneli 10 px'e kadar
        /// doldurmuştu; bu değer 880'e çekildi ki bir sonraki satır sessizce taşmasın.
        /// OYUNCU KİMLİĞİ bölümü (+114) ile içerik ~924'e çıktı → 1000.
        /// DURAKLAT/DEVAM düğmesi (+50) ile ~974: <b>pay bitti.</b>
        /// Kabaca hesap: başlangıç 78 + her Section 34 + her Cycler 40 + her düğme satırı 50.
        /// <para>⚠️ 1000 px, 1080p bir ekranda üstten/alttan ~40 px pay bırakır ve içerik artık
        /// panelin dibinde. <b>Bir satır daha eklenirse panel yükseltilemez</b> — o noktada
        /// içeriğin kaydırılabilir olması gerekir.</para></summary>
        private const float PanelHeight = 1000f;
        private const float RowHeight = 40f;

        /// <summary>Skor limiti adımlayıcısının eşiği: bu değerin altında ±1, üstünde ±5 adımlar.
        /// İki düğmeli döngüleyiciyle hem düşük limitlerde hassasiyet hem yüksek limitlerde
        /// makul hız verir (ayrı bir dört düğmeli widget'a gerek kalmaz).</summary>
        private const int ScoreLimitFineThreshold = 20;

        private const int ScoreLimitMin = 1;
        private const int ScoreLimitMax = 999;

        /// <summary>MAÇ bölümünün normal başlığı; kilitliyken <see cref="MatchSectionLockedTitle"/>
        /// ile değişir ki operatör düğmelerin neden pasif olduğunu görsün.</summary>
        private const string MatchSectionTitle = "MAÇ (TÜM ADMİNLERDE ORTAK)";
        private const string MatchSectionLockedTitle = "MAÇ (SÜRÜYOR — HARİTA/MOD KİLİTLİ)";

        // ⚠️ Alanlar [SerializeField] — görünüm PREFABTAN gelir
        // (`_Shared/App/Resources/UI/AdminPreferencesPanel.prefab`). Bu sınıf yalnız veri
        // yazar; yerleşim/renk/punto prefabta düzenlenir. Prefabta bağlanmayan alan sessizce
        // çizilmez, bu yüzden öge SİLİNMEZ (gizlenecekse devre dışı bırakılır).

        [Header("Panel kökü")]
        [Tooltip("Açılıp kapanan kart — panel kapalıyken bu obje devre dışı bırakılır.")]
        [SerializeField] private GameObject _root;

        [SerializeField] private Button _closeButton;

        [Header("MAÇ bölümü (ortak)")]

        /// <summary>Mod seçici. ⚠️ Seçeneklerini KOD doldurur (katalogdan) — prefabtaki liste
        /// yalnız şablondur ve çalışırken temizlenir. Gösterilen metin dropdown'ın kendi
        /// <c>captionText</c>'idir, bu sınıf ona metin YAZMAZ.</summary>
        [SerializeField] private TMP_Dropdown _modeDropdown;

        /// <summary>Harita seçici — seçenekleri seçili moda + mekan süzgecine göre
        /// <see cref="RefreshMapList"/> her çalıştığında yeniden kurulur.</summary>
        [SerializeField] private TMP_Dropdown _mapDropdown;

        // MAÇ bölümünün kilitlenebilir parçası (§10.7): harita seçmek TÜM istemcilere sahne
        // yükletiyor, bu yüzden maç sürerken iki seçici de pasifleşir ve başlık sebebini yazar.
        [SerializeField] private TextMeshProUGUI _matchSectionLabel;

        [SerializeField] private TextMeshProUGUI _durationValue;
        [SerializeField] private Button _durationPrev;
        [SerializeField] private Button _durationNext;

        [SerializeField] private TextMeshProUGUI _scoreLimitValue;
        [SerializeField] private Button _scoreLimitPrev;
        [SerializeField] private Button _scoreLimitNext;

        // Geri sayım uzunluğu (§5.2 countdownSeconds). Tur tabanlı modlarda (tournament) turlar
        // ARASINDAKİ geri sayım da budur — oyuncular tabanlarında toplandıktan sonra bu kadar
        // beklenir. Diğer modlarda maç başında bir kez işler.
        [SerializeField] private TextMeshProUGUI _countdownValue;
        [SerializeField] private Button _countdownPrev;
        [SerializeField] private Button _countdownNext;

        // Dost ateşi (§5.2 set_friendly_fire). ⚠️ Diğer satırların aksine bu bir SEÇİM DEĞİL, anlık
        // komuttur: koşan maçta da geçerlidir ve etkisi bir sonraki mermide görülür. Bu yüzden
        // ApplySelectionLock onu kapsamaz — maç kuruluyken de basılabilir olması işin özü
        // (takım arkadaşlarını vuran oyuncu için operatör maçı iptal etmek zorunda kalmasın).
        // İki düğme de aynı işi yapar (aç/kapa), satır deseni diğerleriyle aynı kalsın diye.
        [SerializeField] private TextMeshProUGUI _friendlyFireValue;
        [SerializeField] private Button _friendlyFirePrev;
        [SerializeField] private Button _friendlyFireNext;

        [SerializeField] private Button _startButton;

        // ⚠️ Ayrı bir "LOBİYE DÖN" düğmesi YOKTUR ve geri eklenmez: lobi artık harita seçicisinin
        // ilk satırıdır (<see cref="LobbyRowLabel"/>). İkisi birden dururken operatör aynı işi iki
        // yerden yapabiliyordu ve satır kilitliyken düğme açık kalıyordu — kural tek kapıdan geçsin.
        // Koşan maçı bitirip lobiye dönmenin yolu İPTAL'dir (sunucuda ikisi de aynı iştir).
        [SerializeField] private Button _abortButton;

        /// <summary>Duraklat/devam düğmesi — etiketi ve gönderdiği komut faza göre değişir
        /// (<see cref="ApplyPauseButton"/>).</summary>
        [SerializeField] private Button _pauseButton;

        [SerializeField] private TextMeshProUGUI _pauseLabel;
        [SerializeField] private TextMeshProUGUI _statusText;

        [Header("Kalibrasyon")]
        [SerializeField] private Button _clearAllButton;
        [SerializeField] private TextMeshProUGUI _clearAllLabel;

        /// <summary>Toplu kalibrasyon sıfırlamanın onay penceresi (sn) — AdminPlayerRow ile aynı
        /// gerekçe: herkesi savaş dışı bırakan bir eylem tek yanlış tıklamayla olmamalı.</summary>
        private const float ClearAllConfirmSeconds = 3f;
        private float _clearAllArmedAt = -1f;

        [Header("Bağlantı")]
        [SerializeField] private TextMeshProUGUI _connectionText;
        [SerializeField] private Button _reconnectButton;
        [SerializeField] private Button _disconnectButton;

        // ---- Oyuncu kimliği (§2): SEÇİLİ oyuncunun adı + forma numarası ----
        // Hedef ayrı bir seçiciyle DEĞİL AdminSession.SelectedPlayerId ile belirlenir: satıra
        // tıklamak zaten seçim jestidir, ikinci bir liste iki ayrı "seçili oyuncu" kavramı üretirdi.
        [Header("Oyuncu kimliği")]
        [SerializeField] private TMP_InputField _identityNameField;
        [SerializeField] private TextMeshProUGUI _identityNumberValue;
        [SerializeField] private TextMeshProUGUI _identityTargetText;
        [SerializeField] private Button _identityNumberPrev;
        [SerializeField] private Button _identityNumberNext;
        [SerializeField] private Button _applyIdentityButton;

        /// <summary>Alanların hangi oyuncu için doldurulduğu — seçim değişince yeniden doldurulur.
        /// Operatör yazarken roster tazelenip yazdığını EZMESİN diye aynı oyuncuda tekrar dolmaz.</summary>
        private int _identityBoundPlayerId;
        private int _identityNumber;

        [Header("GÖRÜNÜM bölümü (yalnız bu ekran)")]
        [SerializeField] private TextMeshProUGUI _markersValue;
        [SerializeField] private Button _markersPrev;
        [SerializeField] private Button _markersNext;

        [SerializeField] private TextMeshProUGUI _nameplatesValue;
        [SerializeField] private Button _nameplatesPrev;
        [SerializeField] private Button _nameplatesNext;

        [SerializeField] private TextMeshProUGUI _speedValue;
        [SerializeField] private Button _speedPrev;
        [SerializeField] private Button _speedNext;

        [SerializeField] private TextMeshProUGUI _wallValue;
        [SerializeField] private Button _wallPrev;
        [SerializeField] private Button _wallNext;

        [SerializeField] private TextMeshProUGUI _roofValue;
        [SerializeField] private Button _roofPrev;
        [SerializeField] private Button _roofNext;

        private readonly List<ModeDefinition> _modes = new List<ModeDefinition>();
        private readonly List<MapDefinition> _maps = new List<MapDefinition>();
        private int _modeIndex;
        private int _mapIndex;

        /// <summary>Bir sonraki maçın süresi/limiti (ORTAK — set_selection ile gider).
        /// Mod değişince o modun <see cref="ModeDefinition"/> varsayılanına döner.</summary>
        private int _roundSeconds;
        private int _scoreLimit;

        /// <summary>Geri sayım uzunluğu (sn); <c>0</c> = protokol varsayılanı (§5.2).</summary>
        private int _countdownSeconds;

        private bool _dirty = true;

        /// <summary>Harita listesinin hangi mekan süzgeciyle kurulduğu
        /// (<see cref="AdminSelection.VenueVersion"/>). <c>-1</c> = hiç süzülmedi: panel
        /// bağlantıdan önce kurulduğu için ilk liste kaçınılmaz olarak süzgeçsizdir.</summary>
        private int _appliedVenueVersion = -1;

        private void Start()
        {
            WireButtons();

            if (_root != null)
            {
                _root.SetActive(false); // görünürlüğü Apply() belirler
            }

            AdminContent.CollectModes(_modes);
            RebuildModeOptions(); // mod listesi katalogdan gelir ve sonra değişmez → bir kez
            RefreshMapList();     // harita seçeneklerini kendi kurar (mod + mekan süzgeci)
            ResetMatchParametersToModeDefaults();
            Apply();
        }

        /// <summary>
        /// Prefabtaki düğme ve seçicilere davranışı bağlar.
        /// <para>
        /// ⚠️ <b>Prefabta kalıcı (persistent) <c>onClick</c>/<c>onValueChanged</c> kaydı YOKTUR ve
        /// olmamalıdır.</b> Buradaki geri çağrıların çoğu koşullu (kilitli satır, iki adımlı onay,
        /// faza göre değişen komut); inspector'dan bağlanan bir kayıt o koşulları atlar — ör. "TÜM
        /// KALİBRASYONLARI SIFIRLA" onay penceresini atlayıp doğrudan herkesi sıfırlardı.
        /// </para>
        /// </summary>
        private void WireButtons()
        {
            Wire(_closeButton, AdminSession.ClosePanel);

            WireDropdown(_modeDropdown, SelectMode);
            WireDropdown(_mapDropdown, SelectMap);
            Wire(_durationPrev, DurationPrev);
            Wire(_durationNext, DurationNext);
            Wire(_scoreLimitPrev, ScoreLimitDown);
            Wire(_scoreLimitNext, ScoreLimitUp);
            Wire(_countdownPrev, CountdownDown);
            Wire(_countdownNext, CountdownUp);
            Wire(_friendlyFirePrev, ToggleFriendlyFire);
            Wire(_friendlyFireNext, ToggleFriendlyFire);

            Wire(_startButton, StartMatch);
            Wire(_abortButton, AdminCommands.AbortMatch);
            Wire(_pauseButton, TogglePause);

            Wire(_clearAllButton, ArmClearAllCalibration);

            Wire(_applyIdentityButton, ApplyIdentity);
            Wire(_identityNumberPrev, IdentityNumberDown);
            Wire(_identityNumberNext, IdentityNumberUp);

            Wire(_markersPrev, PrevMarkers);
            Wire(_markersNext, NextMarkers);
            Wire(_nameplatesPrev, ToggleNameplates);
            Wire(_nameplatesNext, ToggleNameplates);
            Wire(_speedPrev, SpeedDown);
            Wire(_speedNext, SpeedUp);
            Wire(_wallPrev, WallDown);
            Wire(_wallNext, WallUp);
            Wire(_roofPrev, PrevRoof);
            Wire(_roofNext, NextRoof);

            Wire(_reconnectButton, AdminCommands.Reconnect);
            Wire(_disconnectButton, AdminCommands.Disconnect);
        }

        private static void Wire(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        private static void WireDropdown(TMP_Dropdown dropdown,
            UnityEngine.Events.UnityAction<int> action)
        {
            if (dropdown == null)
            {
                return;
            }

            dropdown.onValueChanged.RemoveAllListeners();
            dropdown.onValueChanged.AddListener(action);
        }

        private void OnEnable()
        {
            AdminSession.Changed += MarkDirty;
            AdminCommands.StatusChanged += MarkDirty;
            NetEvents.OnConnectionStateChanged += HandleConnectionState;

            // Ortak seçim başka bir admin'den değişmiş olabilir. Bu bileşen panel KAPALIYKEN de
            // etkindir (HUD kökünde durur, yalnız kartı gizlenir) — bu yüzden diğer operatörün
            // harita değişikliği panel açılmasa da yerel önizlemeye yansır.
            AdminSelection.Changed += HandleSharedSelectionChanged;

            // Açık sahne değişti (§10.7 sahneleme / maç yükleme) → bölüm başlığındaki "AÇIK:"
            // satırı tazelensin. Sahne komutu ortak seçimden bağımsız gelebilir (maç sonu
            // lobiye dönüş), o yüzden AdminSelection.Changed'e güvenilmez.
            NetEvents.OnReturnToLobby += HandleOpenSceneChanged;
            NetEvents.OnLoadMatch += HandleOpenSceneChanged;

            // Bağlanma/yeniden bağlanma: welcome açık sahneyi taşır. Gerekli, çünkü panel sonradan
            // bağlanan bir admin'de sahne komutlarını KAÇIRMIŞ olur — maç bitip lobiye dönülmüş bir
            // sunucuya bağlanan operatör, ortak seçim hâlâ son arenayı gösterdiği için seçicide o
            // arenayı görür ve onu tekrar seçemezdi (seçili satır olay üretmez).
            NetEvents.OnConnected += HandleWelcome;

            // Faz değişimi harita/mod seçicilerini kilitleyip açıyor (§10.7) — maç başlayınca
            // düğmeler bir sonraki tıklamayı beklemeden pasifleşmeli.
            if (AdminRoster.Instance != null)
            {
                AdminRoster.Instance.Changed += MarkDirty;
            }
        }

        private void OnDisable()
        {
            AdminSession.Changed -= MarkDirty;
            AdminCommands.StatusChanged -= MarkDirty;
            NetEvents.OnConnectionStateChanged -= HandleConnectionState;
            AdminSelection.Changed -= HandleSharedSelectionChanged;
            NetEvents.OnReturnToLobby -= HandleOpenSceneChanged;
            NetEvents.OnLoadMatch -= HandleOpenSceneChanged;
            NetEvents.OnConnected -= HandleWelcome;

            if (AdminRoster.Instance != null)
            {
                AdminRoster.Instance.Changed -= MarkDirty;
            }
        }

        private void Update()
        {
            // Onay penceresi kendiliğinden kapanır (AdminPlayerRow.Tick deseni).
            if (_clearAllArmedAt >= 0f && Time.unscaledTime - _clearAllArmedAt > ClearAllConfirmSeconds)
            {
                _clearAllArmedAt = -1f;
                _dirty = true;
            }

            if (_dirty)
            {
                _dirty = false;
                Apply();
            }
        }

        /// <summary>Tüm oyuncuların kalibrasyonunu sıfırlar — iki adımlı onay (§10.6).</summary>
        private void ArmClearAllCalibration()
        {
            if (_clearAllArmedAt < 0f)
            {
                _clearAllArmedAt = Time.unscaledTime;
                _dirty = true;
                return;
            }

            _clearAllArmedAt = -1f;
            AdminCommands.ClearCalibration(0);
            _dirty = true;
        }

        private void MarkDirty()
        {
            _dirty = true;
        }

        private void HandleConnectionState(ArenaConnectionState state)
        {
            _dirty = true;
        }

        /// <summary>Sunucu sahne komutu yolladı — açık sahne değişti (§10.7).</summary>
        private void HandleOpenSceneChanged(ReturnToLobbyMsg msg)
        {
            ApplyOpenScene(msg != null ? msg.sceneName : "");
        }

        private void HandleOpenSceneChanged(LoadMatchMsg msg)
        {
            ApplyOpenScene(msg != null ? msg.sceneName : "");
        }

        /// <summary>Bağlanıldı — <c>welcome</c> sunucunun o anki açık sahnesini taşır (§10.7).</summary>
        private void HandleWelcome(WelcomeMsg msg)
        {
            ApplyOpenScene(msg != null && msg.match != null ? msg.match.sceneName : "");
        }

        /// <summary>
        /// Harita seçicisinin imlecini sunucunun <b>AÇIK SAHNESİNE</b> taşır.
        /// <para>
        /// ⚠️ <b>Bu, ortak seçimden (<c>admin_state</c>) ayrı bir kaynaktır ve seçici için doğru
        /// olanıdır.</b> İkisi ayrışır: maç bitip ya da lobiye dönülüp herkes lobiye alındığında
        /// sunucudaki ortak seçim hâlâ son arenayı gösterir, açık sahne ise lobidir. İmleç ortak
        /// seçime bağlansaydı seçici "Arena12x12" gösterirken lobi açık olurdu — ve operatör aynı
        /// arenayı tekrar seçemezdi: <see cref="TMP_Dropdown"/> zaten <b>seçili satıra tıklamayı
        /// olay saymaz</b> (<c>value == m_Value</c> ise <c>onValueChanged</c> hiç ateşlenmez), yani
        /// sahneleme komutu hiç gönderilemezdi. Sunucu aynı arenayı tekrar sahnelemeyi bilerek
        /// destekliyor (§10.7: ölçüt "seçim değişti mi" değil "açık sahne bu mu") — istemcinin de
        /// bunu engellememesi gerekir.
        /// </para>
        /// <para>
        /// Yerel iyimser önizleme (<see cref="PreviewSelectedMap"/>) buraya UĞRAMAZ
        /// (<c>SceneRouter.LoadPreview</c> açık sahneyi değiştirmez), yani imleç sunucu onaylamadan
        /// önce oynamaz ve geri sekmez.
        /// </para>
        /// </summary>
        private void ApplyOpenScene(string sceneName)
        {
            _dirty = true;

            if (string.IsNullOrEmpty(sceneName))
            {
                return;
            }

            if (AdminContent.IsLobbyScene(sceneName))
            {
                _lobbyOpen = true;
                return;
            }

            _lobbyOpen = false;

            // Listede yoksa (başka bir modun arenası sahnelenmiş) imleç bırakılır — seçici boş
            // kalmasın; hangi sahnenin açık olduğunu bölüm başlığı zaten yazar.
            int index = IndexOfMap(sceneName);
            if (index >= 0)
            {
                _mapIndex = index;
            }
        }

        // ------------------------------------------------------------------ eylemler

        /// <summary>Maçı başlatır. ⚠️ <b>Lobi açıkken reddedilir:</b> sahnelenmiş bir arena yoktur
        /// ve sunucu lobi türünde maç başlatmaz (§10.7) — sessizce reddedilen bir komut yollamak
        /// yerine sebebi durum satırına yazılır.</summary>
        private void StartMatch()
        {
            if (_lobbyOpen)
            {
                AdminCommands.Note("Lobi açık — önce bir arena seç, sonra BAŞLAT.");
                return;
            }

            AdminCommands.StartMatch(SelectedModeId, SelectedSceneName, _roundSeconds, _scoreLimit,
                _countdownSeconds);
        }

        private const string PauseLabel = "DURAKLAT";
        private const string ResumeLabel = "DEVAM ET";

        /// <summary>
        /// Faz <c>playing</c> ise duraklatır, operatörün duraklattığı maçta ise sürdürür.
        /// <para>
        /// Karar YEREL bir bayrağa değil sunucudan gelen faza bakar: çoklu admin var (CLAUDE.md) ve
        /// duraklatmayı başkası da yapmış olabilir. Yerel bayrak tutulsaydı iki panel birbirine
        /// ters düşerdi.
        /// </para>
        /// </summary>
        private static void TogglePause()
        {
            if (IsOperatorPaused)
            {
                AdminCommands.ResumeMatch();
            }
            else
            {
                AdminCommands.PauseMatch();
            }
        }

        /// <summary>Maç koşuyor mu (§10.1: tek cevap <c>phase == playing</c>).</summary>
        private static bool IsMatchLive
        {
            get
            {
                AdminRoster roster = AdminRoster.Instance;
                return roster != null && roster.Phase == ArenaProtocol.PHASE_PLAYING;
            }
        }

        /// <summary>Maç OPERATÖR tarafından duraklatılmış mı. Modun/geri sayımın duraklaması
        /// buraya girmez — sunucu onları <c>resume_match</c> ile kaldırtmaz (§5.2).</summary>
        private static bool IsOperatorPaused
        {
            get
            {
                AdminRoster roster = AdminRoster.Instance;
                return roster != null &&
                       roster.Phase == ArenaProtocol.PHASE_PAUSED &&
                       roster.PhaseReason == ArenaProtocol.PAUSE_REASON_OPERATOR;
            }
        }

        private string SelectedModeId =>
            _modeIndex >= 0 && _modeIndex < _modes.Count ? _modes[_modeIndex].ModeId : "";

        private string SelectedSceneName =>
            _mapIndex >= 0 && _mapIndex < _maps.Count ? _maps[_mapIndex].SceneName : "";

        private ModeDefinition SelectedMode =>
            _modeIndex >= 0 && _modeIndex < _modes.Count ? _modes[_modeIndex] : null;

        /// <summary>
        /// Mod seçicisinden seçim geldi (<c>onValueChanged</c>).
        /// <para>
        /// ⚠️ Açılır liste <b>ilk iş olarak kapatılır</b>: hemen ardından harita önizlemesi
        /// yükleniyor (<see cref="PreviewSelectedMap"/>) ve açık kalan bir liste sahne değişirken
        /// ekranda asılı kalırdı.
        /// </para>
        /// <para>
        /// Seçim uygulanmazsa (maç sürüyor, indeks bayat) <see cref="Apply"/> çağrılır: dropdown
        /// kendi değerini zaten değiştirdi, imleci geri çekecek olan odur.
        /// </para>
        /// </summary>
        private void SelectMode(int index)
        {
            HideDropdown(_modeDropdown);

            if (index == _modeIndex || index < 0 || index >= _modes.Count || !GuardSelectionChange())
            {
                Apply();
                return;
            }

            _modeIndex = index;
            RefreshMapList();
            _lobbyOpen = false; // mod değişimi de bir arena sahneler (aşağıdaki PublishSelection)
            // Süre/limit her modun kendi varsayılanına döner: 10 dakikalık bir TDM ayarı,
            // 3 dakikalık olması gereken bir moda sessizce taşınmasın.
            ResetMatchParametersToModeDefaults();
            PublishSelection(mapChanged: true); // mod değişti → harita listesi başa döndü, seçili harita da değişti
        }

        // ---- maç parametreleri (ORTAK) ----

        /// <summary>Seçili modun <see cref="ModeDefinition"/> varsayılanlarına döner. Katalog
        /// yoksa protokol/mod tarafındaki değerler zaten sunucuda geçerlidir; burada yalnız
        /// arayüzün gösterdiği sayı sıfırlanır.</summary>
        private void ResetMatchParametersToModeDefaults()
        {
            ModeDefinition mode = SelectedMode;
            _roundSeconds = mode != null && mode.RoundSeconds > 0 ? mode.RoundSeconds : 0;
            _scoreLimit = mode != null ? Mathf.Clamp(mode.ScoreLimit, ScoreLimitMin, ScoreLimitMax) : 0;
            // Geri sayımın ModeDefinition'da karşılığı YOKTUR ve eklenmez: mod şekli değil maç
            // parametresidir (§5.2). 0 = protokol varsayılanı.
            _countdownSeconds = 0;
        }

        private void DurationPrev() { StepDuration(-1); }
        private void DurationNext() { StepDuration(1); }

        /// <summary>Süre seçenekleri arasında döner (§1 <c>ROUND_SECONDS_OPTIONS</c>). Mevcut
        /// değer listede yoksa (modun kendi varsayılanı listede olmayabilir) en yakın seçenekten
        /// devam eder — operatör iki tıkta kaybolmaz.</summary>
        private void StepDuration(int delta)
        {
            int[] options = ArenaProtocol.ROUND_SECONDS_OPTIONS;
            if (options == null || options.Length == 0)
            {
                return;
            }

            int index = (NearestDurationIndex(options, _roundSeconds) + delta + options.Length) % options.Length;
            _roundSeconds = options[index];
            PublishSelection(mapChanged: false);
        }

        private static int NearestDurationIndex(int[] options, int seconds)
        {
            int best = 0;
            int bestDistance = int.MaxValue;

            for (int i = 0; i < options.Length; i++)
            {
                int distance = Mathf.Abs(options[i] - seconds);
                if (distance >= bestDistance)
                {
                    continue;
                }

                best = i;
                bestDistance = distance;
            }

            return best;
        }

        private void ScoreLimitDown() { StepScoreLimit(-1); }
        private void ScoreLimitUp() { StepScoreLimit(1); }

        /// <summary>Skor limiti adımlayıcısı: düşük değerlerde ±1, eşiğin üstünde ±5.</summary>
        private void StepScoreLimit(int direction)
        {
            int step = _scoreLimit >= ScoreLimitFineThreshold ? 5 : 1;
            // Aşağı inerken eşiğin ALTINA düşmemek için adımı da eşiğe göre yeniden hesapla.
            if (direction < 0 && _scoreLimit - step < ScoreLimitFineThreshold)
            {
                step = _scoreLimit > ScoreLimitFineThreshold ? _scoreLimit - ScoreLimitFineThreshold : 1;
            }

            _scoreLimit = Mathf.Clamp(_scoreLimit + direction * step, ScoreLimitMin, ScoreLimitMax);
            PublishSelection(mapChanged: false);
        }

        private void CountdownDown() { StepCountdown(-1); }
        private void CountdownUp() { StepCountdown(1); }

        /// <summary>
        /// Geri sayım adımlayıcısı: ±1 sn, <c>[COUNTDOWN_SECONDS_MIN, COUNTDOWN_SECONDS_MAX]</c>
        /// aralığında. Aralık bir arayüz listesi değil, sunucunun da uyguladığı kısıttır (§5.2) —
        /// panelin gösteremediği bir değeri sunucu zaten kırpardı.
        /// <para>0 ("varsayılan") durumundan ilk dokunuşta alt sınıra çıkılır; geri dönüş yolu
        /// mod değiştirmektir (skor limiti seçicisiyle aynı sözleşme).</para>
        /// </summary>
        private void StepCountdown(int direction)
        {
            _countdownSeconds = Mathf.Clamp(_countdownSeconds + direction,
                ArenaProtocol.COUNTDOWN_SECONDS_MIN, ArenaProtocol.COUNTDOWN_SECONDS_MAX);
            PublishSelection(mapChanged: false);
        }

        /// <summary>
        /// Dost ateşini açar/kapatır (§5.2). <c>PublishSelection</c> KULLANILMAZ: bu değer ortak
        /// seçimin parçası değil, sunucu oturumunun anlık ayarıdır ve <c>set_selection</c>'ın
        /// "0/boş = değiştirme" sözleşmesine sığmaz.
        /// <para>Yerel bir alan tutulmaz — istenen durum sunucudakinin tersidir ve panel sunucunun
        /// <c>admin_state</c> ile geri yaydığı değeri gösterir (iki operatör sapmasın).</para>
        /// </summary>
        private void ToggleFriendlyFire()
        {
            AdminCommands.SetFriendlyFire(!AdminSelection.FriendlyFire);
        }

        /// <summary>
        /// Harita seçicisinden seçim geldi — gerekçeler <see cref="SelectMode"/>.
        /// <para>
        /// ⚠️ <b>İlk satır bir arena DEĞİL, lobidir</b> (<see cref="LobbyRowLabel"/>): eski
        /// "LOBİYE DÖN" düğmesinin yerini aldı ve seçilince <c>set_selection</c> değil
        /// <c>return_to_lobby</c> gider. İmleç orada <b>KALIR</b> — seçici "açık sahne"yi gösterir
        /// (<see cref="ApplyOpenScene"/>), yani lobiye dönüldüğünde lobiyi göstermesi doğrudur.
        /// Aksi hâlde seçici hâlâ eski arenayı gösterirdi ve operatör o arenaya geri dönemezdi:
        /// seçili satıra tıklamak <c>onValueChanged</c>'i ateşlemez.
        /// </para>
        /// <para>
        /// ⚠️ <b>"Zaten seçili" diye erken çıkış YOKTUR.</b> Operatör bir satıra bastıysa komut
        /// gider; sunucu aynı sahneyi tekrar sahnelemeyi zaten idempotent karşılıyor (§10.7).
        /// Buraya bir eşitlik kapısı koymak, açık sahne ile imlecin ayrıştığı her durumda
        /// (maç sonu, lobiye dönüş) operatörü kilitler.
        /// </para>
        /// </summary>
        private void SelectMap(int index)
        {
            HideDropdown(_mapDropdown);

            if (HasLobbyRow && index == LobbyRowIndex)
            {
                if (!GuardSelectionChange())
                {
                    Apply(); // imleç açık sahneye geri çekilir
                    return;
                }

                _lobbyOpen = true; // iyimser: sunucunun return_to_lobby'si aynı değeri doğrulayacak
                AdminCommands.ReturnToLobby();
                Apply();
                return;
            }

            int mapIndex = index - MapRowOffset;
            if (mapIndex < 0 || mapIndex >= _maps.Count || !GuardSelectionChange())
            {
                Apply();
                return;
            }

            _mapIndex = mapIndex;
            _lobbyOpen = false;
            PublishSelection(mapChanged: true);
        }

        private static void HideDropdown(TMP_Dropdown dropdown)
        {
            if (dropdown != null)
            {
                dropdown.Hide();
            }
        }

        /// <summary>
        /// Mod/harita seçimi şu an değiştirilebilir mi (§10.7)? Harita seçmek TÜM istemcilere sahne
        /// yükletiyor — <b>maç KURULMUŞ olduğu her durumda</b> bu yapılamaz (koşan maç, yükleme,
        /// geri sayım, duraklatma); ölçüt <see cref="AdminRoster.CanChangeSelection"/>. Seçiciler
        /// zaten pasifleştiriliyor (<see cref="ApplySelectionLock"/>); bu kapı ikinci emniyettir ve
        /// reddi operatöre görünür kılar.
        /// <para>⚠️ <b>Lobi satırı da bu kapıdan geçer</b> — o da bir sahne komutudur; kurulmuş bir
        /// maçı lobiye çevirmenin yolu İPTAL'dir.</para>
        /// <para>Otorite yine sunucudadır: aynı kural <c>set_selection</c> işlenirken de uygulanır,
        /// arayüz yalnız operatörü boşuna tıklatmamak için önden bilir.</para>
        /// </summary>
        private static bool GuardSelectionChange()
        {
            if (CanChangeSelection)
            {
                return true;
            }

            AdminCommands.Note("Maç kurulu — harita/mod değiştirilemez; önce İPTAL.");
            return false;
        }

        /// <summary>Maç kurulmamış mı? <see cref="AdminRoster"/> henüz yoksa (bağlanmadan önceki
        /// ilk kareler) engellenmez — sunucu zaten son sözü söylüyor.</summary>
        private static bool CanChangeSelection
        {
            get
            {
                AdminRoster roster = AdminRoster.Instance;
                return roster == null || roster.CanChangeSelection;
            }
        }

        /// <summary>
        /// Yerel imleci sunucuya bildirir (<c>set_selection</c>) ve iyimser olarak hemen uygular:
        /// önizleme + arayüz beklemeden tepki versin. Sunucu değişikliği tüm adminlere yayınca
        /// <see cref="HandleSharedSelectionChanged"/> aynı değeri görür ve iş yapmaz — döngü olmaz.
        /// Bağlantı yoksa komut sessizce düşer, yerel önizleme yine de çalışır.
        /// <para>
        /// ⚠️ <b>Harita alanı bir sonraki maçın notu değil, ANLIK bir sahne komutudur (§10.7):</b>
        /// sunucu lobideyken seçilen arenayı <c>return_to_lobby</c> ile tüm istemcilere yükletir.
        /// Mod/harita bu yüzden yalnız <see cref="CanChangeSelection"/> doğruyken gönderilir;
        /// süre/limit her fazda serbesttir (sahne yüklemezler).
        /// </para>
        /// </summary>
        /// <param name="mapChanged">Operatör mod/harita imlecini gerçekten oynattı mı.
        /// <b>Mod/harita alanları YALNIZ o zaman doldurulur</b> (§5.2): sunucu harita alanı dolu
        /// gelen her <c>set_selection</c>'da sahneleme dener, yani süre/limit dokunuşunda da
        /// doldurmak herkesi seçili arenaya taşırdı. Yerel önizleme de aynı kapıdan geçer —
        /// yoksa sunucu kimseyi taşımadığı hâlde operatör tek başına o arenaya düşerdi.</param>
        private void PublishSelection(bool mapChanged)
        {
            bool sendSelection = mapChanged && CanChangeSelection;
            AdminCommands.SetSelection(
                sendSelection ? SelectedModeId : "",
                sendSelection ? SelectedSceneName : "",
                _roundSeconds, _scoreLimit, _countdownSeconds);

            if (mapChanged)
            {
                PreviewSelectedMap();
            }

            Apply();
        }

        /// <summary>
        /// Sunucudan gelen ortak seçim (başka bir admin değiştirmiş olabilir): imleçleri ona
        /// taşır ve yerel önizlemeyi açar. Zaten aynıysa hiçbir şey yapmaz — kendi gönderdiğimiz
        /// <c>set_selection</c>'ın echo'su bu yüzden önizlemeyi tekrar tetiklemez.
        /// Seçicide karşılığı olmayan bir seçim gelirse imleç yerinde bırakılır. Bu <b>normal</b>
        /// bir durumdur, sürüm farkı değil: sunucu açılışta ortak seçimi mekanın **lobi**
        /// haritasıyla tohumlar (§5.3) ve lobi ne mod seçicisinde ne harita seçicisinde vardır —
        /// o an açık olanı bölüm başlığı yazar (<see cref="ApplySelectionLock"/>).
        /// </summary>
        private void HandleSharedSelectionChanged()
        {
            _dirty = true;

            // Mekan süzgeci SEÇİMDEN ÖNCE uygulanır ve seçimden BAĞIMSIZDIR: harita listesi
            // bağlantıdan önce kuruldu (Initialize), yani o an süzgeç boştu ve listede başka
            // işletmelerin arenaları da vardı. Sunucu mekanı ilk admin_state ile bildirir —
            // burada süzülmezse operatör mod düğmesine dokunana kadar oynatılamayacak arenalar
            // görünür kalırdı (seçtiğinde sunucu sahnelemeyi reddeder).
            if (_appliedVenueVersion != AdminSelection.VenueVersion)
            {
                _appliedVenueVersion = AdminSelection.VenueVersion;
                RefreshMapList();
            }

            string sharedMode = AdminSelection.ModeId;
            string sharedScene = AdminSelection.SceneName;

            bool changed = false;
            bool sceneChanged = false; // önizleme YALNIZ mod/harita değişince tazelenir

            if (!string.IsNullOrEmpty(sharedMode) && sharedMode != SelectedModeId)
            {
                int index = IndexOfMode(sharedMode);
                if (index >= 0)
                {
                    _modeIndex = index;
                    RefreshMapList(); // mod değişti → uyumlu harita listesi de değişti
                    ResetMatchParametersToModeDefaults();
                    changed = true;
                    sceneChanged = true;
                }
            }

            // ⚠️ Lobi sahnesi buraya DÜŞMEZ ve düşmemeli: seçicinin lobiyi gösterip göstermeyeceğine
            // ortak seçim değil AÇIK SAHNE karar verir (<see cref="ApplyOpenScene"/>). İkisi
            // ayrışır — maç bitip lobiye dönüldüğünde ortak seçim hâlâ son arenayı gösterir.
            if (!string.IsNullOrEmpty(sharedScene) && !AdminContent.IsLobbyScene(sharedScene) &&
                sharedScene != SelectedSceneName)
            {
                int index = IndexOfMap(sharedScene);
                if (index >= 0)
                {
                    _mapIndex = index;
                    changed = true;
                    sceneChanged = true;
                }
            }

            // Parametreler moddan SONRA uygulanır: mod değişimi yerel varsayılana çekiyor, son
            // sözü sunucunun bildirdiği ortak değer söylemeli (0 = sunucuda hiç seçilmemiş).
            if (AdminSelection.RoundSeconds > 0 && AdminSelection.RoundSeconds != _roundSeconds)
            {
                _roundSeconds = AdminSelection.RoundSeconds;
                changed = true;
            }

            if (AdminSelection.ScoreLimit > 0 && AdminSelection.ScoreLimit != _scoreLimit)
            {
                _scoreLimit = Mathf.Clamp(AdminSelection.ScoreLimit, ScoreLimitMin, ScoreLimitMax);
                changed = true;
            }

            if (AdminSelection.CountdownSeconds > 0 && AdminSelection.CountdownSeconds != _countdownSeconds)
            {
                _countdownSeconds = Mathf.Clamp(AdminSelection.CountdownSeconds,
                    ArenaProtocol.COUNTDOWN_SECONDS_MIN, ArenaProtocol.COUNTDOWN_SECONDS_MAX);
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            if (sceneChanged)
            {
                PreviewSelectedMap();
            }

            Apply();
        }

        private int IndexOfMode(string modeId)
        {
            for (int i = 0; i < _modes.Count; i++)
            {
                if (_modes[i] != null && _modes[i].ModeId == modeId)
                {
                    return i;
                }
            }

            return -1;
        }

        private int IndexOfMap(string sceneName)
        {
            for (int i = 0; i < _maps.Count; i++)
            {
                if (_maps[i] != null && _maps[i].SceneName == sceneName)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Harita listesini seçili moda + sunucunun bildirdiği mekana göre yeniden kurar.
        /// <b>Seçili harita hayatta kalıyorsa imleç onda bırakılır</b> — liste mekan süzgeci
        /// geldiğinde de yeniden kuruluyor ve o an operatörün seçimini başa atmak, panelin
        /// gösterdiği haritayı görünür bir sebep olmadan değiştirmek olurdu.</summary>
        private void RefreshMapList()
        {
            string modeId = _modeIndex >= 0 && _modeIndex < _modes.Count ? _modes[_modeIndex].ModeId : "";
            string previous = SelectedSceneName;

            AdminContent.CollectMaps(modeId, _maps);

            // Lobi satırı listeyle BİRLİKTE tazelenir: mekan süzgeci değiştiğinde (VenueVersion)
            // arenalar gibi lobi de değişir — her işletmenin kendi lobisi var (§10.7).
            _lobbyMap = AdminContent.ResolveLobbyMap();

            int index = string.IsNullOrEmpty(previous) ? -1 : IndexOfMap(previous);
            _mapIndex = index >= 0 ? index : 0;

            RebuildMapOptions();
        }

        // --------------------------------------------------------------- seçiciler

        /// <summary>Katalog boşken seçicide görünen metin. O durumda seçici pasifleşir
        /// (<see cref="ApplySelectionLock"/>) — açılıp tek satırlık bir liste gösteren seçici
        /// operatörü "tıkladım, bir şey olmadı" diye bırakırdı.</summary>
        private const string NoModesLabel = "katalog yok";

        private const string NoMapsLabel = "harita yok";

        /// <summary>Harita seçicisinin ilk satırı — eski "LOBİYE DÖN" düğmesinin yerini aldı.
        /// Seçilince <c>return_to_lobby</c> gider (<see cref="SelectMap"/>).</summary>
        private const string LobbyRowLabel = "Lobi";

        /// <summary>Lobi satırı listenin BAŞINDA durur ve orada kalır. Sona konsaydı indeksi
        /// mod/mekan değiştikçe kayardı; başta duran satırın yeri sabittir ve operatörün kas
        /// hafızası bozulmaz.</summary>
        private const int LobbyRowIndex = 0;

        /// <summary>Bu mekanın lobi haritası (§10.7) — katalogda yoksa ya da mekan süzgeci onu
        /// dışarıda bırakıyorsa null, o zaman lobi satırı hiç çizilmez.</summary>
        private MapDefinition _lobbyMap;

        /// <summary>Sunucunun açık sahnesi lobi mi (<see cref="ApplyOpenScene"/>). Doğruysa harita
        /// seçicisi lobi satırını gösterir ve BAŞLAT reddeder — sahnelenmiş arena yoktur.</summary>
        private bool _lobbyOpen;

        private bool HasLobbyRow => _lobbyMap != null;

        /// <summary>Seçici indeksinden <see cref="_maps"/> indeksine geçiş farkı: lobi satırı
        /// varken liste bir kayar.</summary>
        private int MapRowOffset => HasLobbyRow ? 1 : 0;

        /// <summary>Seçenek metinlerinin kurulduğu tampon — <see cref="TMP_Dropdown.AddOptions(List{string})"/>
        /// içeriği kendi listesine kopyaladığı için paylaşılabilir.</summary>
        private readonly List<string> _optionScratch = new List<string>();

        private void RebuildModeOptions()
        {
            _optionScratch.Clear();
            for (int i = 0; i < _modes.Count; i++)
            {
                _optionScratch.Add(DisplayOf(_modes[i].DisplayName, _modes[i].ModeId));
            }

            FillDropdown(_modeDropdown, _optionScratch, NoModesLabel);
            SyncDropdown(_modeDropdown, _modeIndex, _modes.Count);
        }

        private void RebuildMapOptions()
        {
            _optionScratch.Clear();
            if (HasLobbyRow)
            {
                _optionScratch.Add(LobbyRowLabel);
            }

            for (int i = 0; i < _maps.Count; i++)
            {
                _optionScratch.Add(DisplayOf(_maps[i].DisplayName, _maps[i].SceneName));
            }

            FillDropdown(_mapDropdown, _optionScratch, NoMapsLabel);
            SyncMapDropdown();
        }

        /// <summary>Harita seçicisini yerel imlece çeker — lobi satırı listeyi kaydırdığı için
        /// <see cref="SyncDropdown"/> doğrudan kullanılamaz.</summary>
        private void SyncMapDropdown()
        {
            int count = Mathf.Max(1, _maps.Count + MapRowOffset);

            if (_lobbyOpen && HasLobbyRow)
            {
                SyncDropdown(_mapDropdown, LobbyRowIndex, count);
                return;
            }

            if (_maps.Count == 0)
            {
                // Yalnız lobi satırı (ya da "harita yok") var: imleç zaten tek yerde durabilir.
                SyncDropdown(_mapDropdown, 0, count);
                return;
            }

            SyncDropdown(_mapDropdown, _mapIndex + MapRowOffset, count);
        }

        /// <summary>Seçenekleri yazar. Liste boşsa tek bir açıklama satırı konur ki seçicinin
        /// başlığı hiçbir zaman boş kalmasın.</summary>
        private static void FillDropdown(TMP_Dropdown dropdown, List<string> options, string emptyLabel)
        {
            if (dropdown == null)
            {
                return;
            }

            dropdown.ClearOptions();
            dropdown.AddOptions(options.Count > 0 ? options : new List<string> { emptyLabel });
        }

        /// <summary>
        /// Seçicinin imlecini yerel indekse çeker (tek doğruluk kaynağı yine sunucu → yerel liste).
        /// <para>
        /// ⚠️ <b><see cref="TMP_Dropdown.SetValueWithoutNotify"/> kullanılır:</b> <c>value</c>
        /// ataması <c>onValueChanged</c>'i tetikler, yani sunucudan gelen her <c>admin_state</c>
        /// tazelemesi yeni bir <c>set_selection</c> doğurur ve iki admin birbirini sonsuza kadar
        /// tetiklerdi.
        /// </para>
        /// </summary>
        private static void SyncDropdown(TMP_Dropdown dropdown, int index, int count)
        {
            if (dropdown == null)
            {
                return;
            }

            int target = count > 0 ? Mathf.Clamp(index, 0, count - 1) : 0;
            if (dropdown.value != target)
            {
                dropdown.SetValueWithoutNotify(target);
            }
        }

        /// <summary>
        /// Seçili arenayı bu ekranda hemen açar — <b>iyimser</b> bir yükleme.
        /// <para>
        /// ⚠️ Bu artık "yalnız admin görür" demek DEĞİLDİR: sunucu lobideyken seçilen haritayı
        /// <c>return_to_lobby</c> ile tüm istemcilere yükletiyor (§10.7 sahneleme) ve o mesaj
        /// admin'e de geliyor. Buradaki yerel yükleme yalnız <b>gecikmeyi gizler</b> (ve sunucusuz
        /// dev oturumunda tek yol odur); sunucu aynı sahneyi söylediğinde
        /// <see cref="SceneRouter"/> zaten o sahnede olduğumuzu görüp ikinci kez yüklemez.
        /// </para>
        /// Maç sürüyorsa dokunulmaz — sahne otoritesi sunucudadır.
        /// <para>⚠️ Lobi açıkken de dokunulmaz: lobiye dönüşü <c>return_to_lobby</c> sürer, burada
        /// arena önizlemek operatörü tek başına arenaya düşürürdü.</para>
        /// </summary>
        private void PreviewSelectedMap()
        {
            if (_lobbyOpen || _mapIndex < 0 || _mapIndex >= _maps.Count || !CanChangeSelection)
            {
                return;
            }

            string sceneName = _maps[_mapIndex].SceneName;
            if (SceneRouter.Instance == null || string.IsNullOrEmpty(sceneName))
            {
                return;
            }

            SceneRouter.Instance.LoadPreview(sceneName);
            AdminCommands.Note($"Harita: {sceneName} (herkes yükleniyor; maç başlatılmadı)");
        }

        private static void PrevMarkers() { StepMarkers(-1); }
        private static void NextMarkers() { StepMarkers(1); }

        private static void StepMarkers(int delta)
        {
            var next = (int)AdminSession.Markers + delta;
            if (next < 0) next = 2;
            if (next > 2) next = 0;
            AdminSession.Markers = (AdminMarkerVisibility)next;
        }

        private static void ToggleNameplates()
        {
            AdminSession.Nameplates = !AdminSession.Nameplates;
        }

        private static void SpeedDown() { AdminSession.FreeSpeed -= 0.5f; }
        private static void SpeedUp() { AdminSession.FreeSpeed += 0.5f; }

        private static void WallDown() { StepWall(-0.05f); }
        private static void WallUp() { StepWall(0.05f); }

        private static void StepWall(float delta)
        {
            AdminSession.WallAlpha += delta;
            if (AdminSpectator.Instance != null)
            {
                AdminSpectator.Instance.RefreshWallAlpha();
            }
        }

        private static void PrevRoof() { StepRoof(-1); }
        private static void NextRoof() { StepRoof(1); }

        /// <summary>Çatı kipi: görünür → kuş bakışında gizli → hep gizli (halkalarla aynı desen).</summary>
        private static void StepRoof(int delta)
        {
            var next = (int)AdminSession.Roof + delta;
            if (next < 0) next = 2;
            if (next > 2) next = 0;
            AdminSession.Roof = (AdminRoofMode)next;
            AdminSpectator.RefreshRoof(); // tercih anında görünsün, kip değişimini bekleme
        }

        // ------------------------------------------------------------ oyuncu kimliği (§2)

        private void IdentityNumberDown()
        {
            StepIdentityNumber(-1);
        }

        private void IdentityNumberUp()
        {
            StepIdentityNumber(1);
        }

        /// <summary>Numarayı 1..99 arasında döndürür. <c>0</c> ("değiştirme") adımlamada ATLANIR:
        /// numarayı korumak isteyen operatör alanı hiç kurcalamaz, sıfıra inmesi gerekmez.</summary>
        private void StepIdentityNumber(int delta)
        {
            int next = _identityNumber + delta;
            if (next < ArenaProtocol.PLAYER_NUMBER_MIN)
            {
                next = ArenaProtocol.PLAYER_NUMBER_MAX;
            }
            else if (next > ArenaProtocol.PLAYER_NUMBER_MAX)
            {
                next = ArenaProtocol.PLAYER_NUMBER_MIN;
            }

            _identityNumber = next;
            MarkDirty();
        }

        /// <summary>Seçili oyuncuya ad + numara gönderir. ⚠️ Yerel roster GÜNCELLENMEZ — otorite
        /// sunucudadır: kabul edilirse değişiklik <c>lobby_state</c> ile geri gelir, reddedilirse
        /// hiç gelmez ve sebebi durum satırında (<c>admin_state.notice</c>) görünür.</summary>
        private void ApplyIdentity()
        {
            int playerId = AdminSession.SelectedPlayerId;
            if (playerId <= 0)
            {
                return;
            }

            AdminCommands.SetIdentity(playerId, _identityNameField != null ? _identityNameField.text : "",
                _identityNumber);
        }

        /// <summary>Alanları SEÇİLİ oyuncudan doldurur. ⚠️ Aynı oyuncuda ikinci kez DOLDURMAZ:
        /// roster sürekli tazelendiği için her tazelemede doldurmak operatörün o an yazdığı adı
        /// ezerdi. Yeniden doldurma yalnız seçim değişince olur.</summary>
        private void RefreshIdentityFields()
        {
            int playerId = AdminSession.SelectedPlayerId;
            AdminRoster roster = AdminRoster.Instance;
            AdminPlayerView view = roster != null ? roster.Find(playerId) : null;

            if (view == null)
            {
                if (_identityBoundPlayerId != 0)
                {
                    _identityBoundPlayerId = 0;
                    _identityNumber = 0;
                    if (_identityNameField != null)
                    {
                        _identityNameField.text = "";
                    }
                }

                _identityTargetText.text = "oyuncu seçilmedi";
                _identityNumberValue.text = "-";
                return;
            }

            if (_identityBoundPlayerId != playerId)
            {
                _identityBoundPlayerId = playerId;
                _identityNumber = view.number;
                if (_identityNameField != null)
                {
                    _identityNameField.text = view.name ?? "";
                }
            }

            _identityTargetText.text = $"#{view.playerId} · {view.name}";
            _identityNumberValue.text = _identityNumber > 0 ? _identityNumber.ToString() : "yok";
        }

        // ------------------------------------------------------------------ tazeleme

        private void Apply()
        {
            if (_root == null)
            {
                return;
            }

            bool open = AdminSession.OpenPanel == AdminPanelKind.Preferences;
            if (_root.activeSelf != open)
            {
                _root.SetActive(open);
            }

            if (!open)
            {
                return;
            }

            // Seçicilerin GÖSTERDİĞİ metin dropdown'ın kendi captionText'idir (seçenekler
            // RebuildModeOptions/RebuildMapOptions'ta yazıldı); burada yalnız imleç eşitlenir —
            // ortak seçimi başka bir admin de değiştirmiş olabilir.
            SyncDropdown(_modeDropdown, _modeIndex, _modes.Count);
            SyncMapDropdown();

            ApplySelectionLock();

            // 0 = arayüz bir değer bilmiyor → sunucu modun varsayılanını kullanacak.
            _durationValue.text = _roundSeconds > 0
                ? AdminCommands.FormatDuration(_roundSeconds)
                : "mod varsayılanı";
            _scoreLimitValue.text = _scoreLimit > 0 ? _scoreLimit.ToString() : "mod varsayılanı";

            // Prefabı güncellenmemiş bir kurulumda alan boş olabilir; eksik bağ sessizce çizilmez
            // (panelin geri kalanı çalışmaya devam eder).
            if (_countdownValue != null)
            {
                _countdownValue.text = _countdownSeconds > 0
                    ? $"{_countdownSeconds} sn"
                    : $"varsayılan ({ArenaProtocol.COUNTDOWN_SECONDS} sn)";
            }

            if (_friendlyFireValue != null)
            {
                // Değer sunucudan okunur (yerel imleç yok): anahtar koşan maçta da değişebildiği
                // için "gönderdim" ile "yürürlükte" arasındaki fark operatöre yalan söylemesin.
                bool friendlyFire = AdminSelection.FriendlyFire;
                _friendlyFireValue.text = friendlyFire ? "AÇIK" : "kapalı";
                // Açıkken vurgulu: takım arkadaşının vurulabildiği bir maç, ekranda fark edilmeden
                // geçmemesi gereken bir durumdur.
                _friendlyFireValue.color = friendlyFire ? UiKit.Bad : UiKit.Title;
            }

            _statusText.text = AdminCommands.Status;

            if (_clearAllLabel != null)
            {
                bool armed = _clearAllArmedAt >= 0f;
                _clearAllLabel.text = armed ? "EMİN? HERKESİ SIFIRLA" : "TÜM KALİBRASYONLARI SIFIRLA";
                _clearAllLabel.color = armed ? UiKit.OnAccent : UiKit.Bad;
            }

            RefreshIdentityFields();

            _markersValue.text = AdminSession.Markers == AdminMarkerVisibility.Off ? "kapalı"
                : AdminSession.Markers == AdminMarkerVisibility.TopDownOnly ? "kuş bakışı" : "her zaman";
            _nameplatesValue.text = AdminSession.Nameplates ? "açık" : "kapalı";
            _speedValue.text = $"{AdminSession.FreeSpeed:0.0} m/sn";
            _wallValue.text = $"%{Mathf.RoundToInt(AdminSession.WallAlpha * 100f)}";
            _roofValue.text = AdminSession.Roof == AdminRoofMode.Visible ? "görünür"
                : AdminSession.Roof == AdminRoofMode.HideInTopDown ? "kuş bakışında gizli" : "hep gizli";

            ArenaClient client = ArenaClient.Instance;
            string endpoint = AppSession.HasServerEndpoint
                ? $"{AppSession.ServerIp}:{AppSession.ServerPort}"
                : "adres yok (launcher'dan başlatılmalı)";
            string state = client == null ? "istemci yok"
                : client.IsConnected ? "bağlı"
                : client.State == ArenaConnectionState.Connecting ? "bağlanılıyor" : "bağlı değil";

            // Bağlı admin sayısı: operatör yalnız olmadığını bilmeli (komutlar eş yetkilidir).
            string peers = AdminSelection.AdminCount > 1
                ? $" — {AdminSelection.AdminCount} admin bağlı"
                : "";
            _connectionText.text = $"{state} — {endpoint}{peers}";
        }

        /// <summary>Maç sürerken mod/harita satırlarını pasif gösterir (§10.7): seçiciler
        /// açılmaz, değerler sönükleşir, bölüm başlığı sebebini yazar. Süre/limit satırları
        /// AÇIK kalır — onlar bir sonraki maçın parametreleridir, kimseye sahne yükletmezler.</summary>
        private void ApplySelectionLock()
        {
            bool open = CanChangeSelection;

            // Liste boşken de pasif: açılıp yalnız "katalog yok" gösteren bir seçici, seçilecek
            // bir şey varmış gibi görünür. Lobi satırı tek başına da seçilebilir bir şeydir.
            SetInteractable(_modeDropdown, open && _modes.Count > 0);
            SetInteractable(_mapDropdown, open && (_maps.Count > 0 || HasLobbyRow));

            Color valueColor = open ? UiKit.Title : UiKit.Faint;
            SetCaptionColor(_modeDropdown, valueColor);
            SetCaptionColor(_mapDropdown, valueColor);

            if (_matchSectionLabel != null)
            {
                // Başlığa AÇIK SAHNE yazılır: harita satırı "bir sonraki maçın adayı"dır, sunucunun
                // açık sahnesi ise gerçekten yüklü olandır (§10.7) ve ikisi maç sonrası ayrışır
                // (herkes lobiye döner, imleç son arenada kalır). Operatör hangisinin ne olduğunu
                // tahmin etmek zorunda kalmasın.
                string openScene = SceneRouter.Instance != null ? SceneRouter.Instance.OpenScene : "";
                string title = open ? MatchSectionTitle : MatchSectionLockedTitle;
                _matchSectionLabel.text = string.IsNullOrEmpty(openScene)
                    ? title
                    : $"{title} · AÇIK: {openScene}";
                _matchSectionLabel.color = open ? UiKit.Faint : UiKit.Bad;
            }

            ApplyPauseButton();
        }

        /// <summary>Duraklat/devam düğmesini fazla hizalar: koşan maçta "DURAKLAT", operatörün
        /// duraklattığı maçta "DEVAM ET", diğer her durumda pasif (lobide, yüklemede, geri sayımda
        /// ve bitmiş maçta duraklatılacak/sürdürülecek bir şey yok — sunucu da reddeder).</summary>
        private void ApplyPauseButton()
        {
            if (_pauseButton == null) return;

            bool paused = IsOperatorPaused;
            SetInteractable(_pauseButton, paused || IsMatchLive);

            if (_pauseLabel != null)
            {
                _pauseLabel.text = paused ? ResumeLabel : PauseLabel;
                _pauseLabel.color = paused ? UiKit.Good : UiKit.Title;
            }
        }

        /// <summary>Düğme ve seçici için ortak (<see cref="Selectable"/> ikisinin de tabanı).</summary>
        private static void SetInteractable(Selectable selectable, bool value)
        {
            if (selectable != null)
            {
                selectable.interactable = value;
            }
        }

        /// <summary>Seçicinin başlık metnini soldurur. <see cref="Selectable"/>'ın kendi
        /// <c>disabledColor</c>'ı yalnız zemini tintler — metin de sönmezse kilitli satır
        /// açık görünmeye devam eder.</summary>
        private static void SetCaptionColor(TMP_Dropdown dropdown, Color color)
        {
            if (dropdown != null && dropdown.captionText != null)
            {
                dropdown.captionText.color = color;
            }
        }

        private static string DisplayOf(string displayName, string fallback)
        {
            return string.IsNullOrEmpty(displayName) ? fallback : displayName;
        }
    }
}
