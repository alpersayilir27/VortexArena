using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// İstatistikler paneli (skor bandının ortasındaki chip ya da <c>I</c> tuşu açar).
    /// Kart yarı saydamdır, arkasına scrim koyulmaz — canlı sahne izlenmeye devam eder.
    ///
    /// <para><b>Neden satır tabanlı:</b> panel yalnız okunan bir tablo değil, operatörün oturup
    /// <i>kayıt işi</i> yaptığı ekrandır (ad düzeltme, gövde ölçümü, kalibrasyon yeniden yükleme).
    /// Metin kolonlarında bir oyuncuya dokunmanın yolu yoktu ve TMP varsayılan fontu eşit genişlikli
    /// olmadığı için hizalama da kolon başına ayrı TMP gerektiriyordu. Her oyuncu artık kendi
    /// satırıdır (<see cref="AdminStatsRow"/>): hizalama yerleşimden gelir, eylem satırın üstündedir
    /// ve liste ScrollRect ile kaydırılır — <b>kap YOKTUR</b>, herkes çizilir.</para>
    ///
    /// <para><b>Uydurma metrik yok:</b> yalnız protokolde gerçekten taşınan veriler gösterilir
    /// (K/D/skor sunucudan — §5.3 <c>lobby_state</c>; batarya/kumanda <c>status</c>'tan; <b>ping</b>
    /// istemcinin ölçüp bildirdiği RTT — §6.7 <c>net_stats</c>). <b>Hasar ve isabet oranı protokolde
    /// YOK, bu yüzden panelde de yok.</b> Jitter ve paket kaybı ölçülüyor ama gösterilmiyor:
    /// operatörün eyleme çevirebileceği tek sayı ping'dir.</para>
    ///
    /// <para>⚠️ <b>HP, SAHNE ve İHLAL satırda bilinçli olarak YOKTUR</b> — gerekçesi
    /// <see cref="AdminStatsRow"/> sınıf dokümanındadır; "eksik" sanıp geri koyma.</para>
    ///
    /// <para><b>Alt bardaki iki kalibrasyon düğmesi zıt işlerdir:</b> <c>TÜMÜNÜ KALİBRE ET</c>
    /// kayıtlı çapadan <i>yeniden yükleme</i>dir (oyuncuyu oyuna geri sokmayı dener),
    /// <c>TÜM KALİBRASYONLARI SIFIRLA</c> ise herkesi savaş dışı bırakır. Yan yana durdukları için
    /// ayrımı <b>görünüm ve sürtünme</b> taşır: sıfırlama kırmızı yazılır ve iki adımlı onay ister,
    /// yeniden yüklemede onay yoktur (geri alınabilir). Oyuncu başına sıfırlama yan paneldeki
    /// oyuncu kartındadır (<see cref="AdminPlayerRow"/>, KAL).</para>
    ///
    /// <para><b>Görünüm prefabtan gelir</b> (<c>_Shared/App/Resources/UI/AdminStatsPanel.prefab</c>);
    /// bu sınıf yalnız veri yazar ve satırları <see cref="UiKit.Block"/> ile yerleştirir.</para>
    /// </summary>
    public class AdminStatsPanel : MonoBehaviour
    {
        private const float RefreshInterval = 0.5f;

        /// <summary>Uyarı penceresinin kendiliğinden kapanma süresi (sn). Operatör kapatmayı
        /// unutursa pencere listenin üstünde kalıcı olarak durmasın.</summary>
        private const float PopupSeconds = 6f;

        /// <summary>Toplu sıfırlamanın onay penceresi (sn): ilk basıştan sonra bu kadar süre içinde
        /// ikinci basış gelmezse düğme dinlenme haline döner.</summary>
        private const float ClearAllConfirmSeconds = 3f;

        /// <summary>Alt bardaki düğmelerin ortak dinlenme zemini — sıfırlama düğmesi yalnız onay
        /// bekliyorken bunun yerine <see cref="UiKit.Bad"/> ile dolar.</summary>
        private static readonly Color ClearAllIdleFill = UiKit.Hex(0x334557, 0xF2);

        /// <summary>FFA sıralaması için tampon — her tazelemede yeni liste ayırmamak adına.</summary>
        private readonly List<AdminPlayerView> _sorted = new List<AdminPlayerView>();

        /// <summary>Satır havuzu: örnekler yok edilmez, fazlası gizlenir (<see cref="AdminHud"/>
        /// ile aynı desen) — her tazelemede Instantiate/Destroy yapmak GC ve düzen sıçraması üretir.</summary>
        private readonly List<AdminStatsRow> _rows = new List<AdminStatsRow>();

        // ⚠️ Alanlar [SerializeField] — görünüm PREFABTAN gelir. Bu sınıf yalnız veri yazar.

        [Tooltip("Açılıp kapanan kart — panel kapalıyken bu obje devre dışı bırakılır.")]
        [SerializeField] private GameObject _root;
        [SerializeField] private Button _closeButton;
        [SerializeField] private TextMeshProUGUI _headline;
        [SerializeField] private TextMeshProUGUI _teamSummary;
        [SerializeField] private TextMeshProUGUI _matchSummary;

        [Header("Oyuncu listesi")]
        [SerializeField] private AdminStatsRow _rowPrefab;
        [Tooltip("ScrollRect'in content'i — satırlar buranın altına kurulur, yüksekliği koddan sürülür.")]
        [SerializeField] private RectTransform _rowContainer;
        [SerializeField] private ScrollRect _scroll;
        [SerializeField] private float _rowGap = 6f;

        [Header("Toplu eylemler")]
        [SerializeField] private Button _calibrateAllButton;
        [SerializeField] private Button _measureAllButton;
        [SerializeField] private Button _clearAllButton;
        [SerializeField] private TextMeshProUGUI _clearAllLabel;

        [Header("Uyarı penceresi")]
        [SerializeField] private GameObject _popupRoot;
        [SerializeField] private TextMeshProUGUI _popupText;
        [SerializeField] private Button _popupCloseButton;

        private readonly StringBuilder _sb = new StringBuilder();

        private float _nextRefresh;
        private bool _dirty = true;

        /// <summary>Görünür (bağlı) satır sayısı — <see cref="Tick"/> ve toplu eylemler yalnız
        /// bunlarla ilgilenir.</summary>
        private int _visibleRows;

        /// <summary>Satır yüksekliği prefabtan okunur (yedeği <see cref="AdminStatsRow.Height"/>);
        /// sanatçı satırı büyütürse liste yerleşimi uyar.</summary>
        private float _rowHeight = -1f;

        /// <summary>Uyarı penceresinin kapanma anı (<c>Time.unscaledTime</c>); &lt; 0 = kapalı.</summary>
        private float _popupUntil = -1f;

        /// <summary>Toplu sıfırlamanın ilk basış anı (<c>Time.unscaledTime</c>); &lt; 0 = onay
        /// beklenmiyor.</summary>
        private float _clearAllArmedAt = -1f;

        private float RowHeight
        {
            get
            {
                if (_rowHeight > 1f)
                {
                    return _rowHeight;
                }

                float fromPrefab = _rowPrefab != null
                    ? ((RectTransform)_rowPrefab.transform).rect.height
                    : 0f;
                _rowHeight = fromPrefab > 1f ? fromPrefab : AdminStatsRow.Height;
                return _rowHeight;
            }
        }

        private void Start()
        {
            // Prefabta kalıcı onClick kaydı YOKTUR (bkz. AdminPreferencesPanel.WireButtons).
            Wire(_closeButton, AdminSession.ClosePanel);
            Wire(_calibrateAllButton, ReloadAllCalibrations);
            Wire(_measureAllButton, () => AdminCommands.MeasureBodyScale(0));
            Wire(_clearAllButton, ArmClearAllCalibration);
            Wire(_popupCloseButton, HidePopup);

            if (_root != null)
            {
                _root.SetActive(false); // görünürlüğü Apply() belirler
            }

            HidePopup();
            Apply();
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

        private void OnEnable()
        {
            AdminSession.Changed += MarkDirty;
            AdminRoster.CalibrationResult += HandleCalibrationResult;

            if (AdminRoster.Instance != null)
            {
                AdminRoster.Instance.Changed += MarkDirty;
            }
        }

        private void OnDisable()
        {
            AdminSession.Changed -= MarkDirty;
            AdminRoster.CalibrationResult -= HandleCalibrationResult;

            if (AdminRoster.Instance != null)
            {
                AdminRoster.Instance.Changed -= MarkDirty;
            }
        }

        private void Update()
        {
            bool tick = Time.unscaledTime >= _nextRefresh;
            if (tick)
            {
                _nextRefresh = Time.unscaledTime + RefreshInterval;
            }

            if (_dirty || tick)
            {
                _dirty = false;
                Apply();
            }

            // Onay pencereleri ve kalibrasyon zaman aşımı KARE başına ilerler: tazeleme aralığına
            // bırakılsalardı sayaç yarım saniyelik adımlarla seğirirdi.
            for (int i = 0; i < _visibleRows && i < _rows.Count; i++)
            {
                _rows[i].Tick();
            }

            if (_popupUntil >= 0f && Time.unscaledTime >= _popupUntil)
            {
                HidePopup();
            }

            if (_clearAllArmedAt >= 0f && Time.unscaledTime - _clearAllArmedAt > ClearAllConfirmSeconds)
            {
                _clearAllArmedAt = -1f;
                _dirty = true;
            }
        }

        private void MarkDirty()
        {
            _dirty = true;
        }

        // ------------------------------------------------------------------ tazeleme

        private void Apply()
        {
            if (_root == null)
            {
                return;
            }

            bool open = AdminSession.OpenPanel == AdminPanelKind.Stats;
            if (_root.activeSelf != open)
            {
                _root.SetActive(open);

                // Panel her açılışta listenin BAŞINDAN başlar: kaydırma konumu oturumlar arası
                // taşınırsa operatör paneli açtığında boş bir alt boşluğa bakıyor olabilir
                // (aradaki oyuncular çıkmış, liste kısalmıştır).
                if (open && _scroll != null)
                {
                    _scroll.verticalNormalizedPosition = 1f;
                }
            }

            // Kapalı panelde yarım kalmış onay penceresi taşınmaz: operatör paneli kapatıp
            // açtığında düğme "EMİN?" halinde beklerse tek tıklama herkesin kalibrasyonunu siler.
            if (!open)
            {
                _clearAllArmedAt = -1f;
            }

            // Roster'dan ÖNCE: sıfırlama düğmesi oyuncu listesi gelmemişken de doğru görünmeli.
            ApplyClearAllButton();

            AdminRoster roster = AdminRoster.Instance;
            if (!open || roster == null)
            {
                return;
            }

            RefreshSummary(roster);
            RefreshRows(OrderedPlayers(roster));
            RefreshMatchInfo(roster);
        }

        /// <summary>
        /// Liste sırası: takımlı modda roster sırası (playerId) korunur — operatör oyuncuyu hep
        /// aynı satırda arar. FFA'da tek sıralama ölçütü skordur, bu yüzden liste skora göre
        /// AZALAN dizilir (eşitlikte playerId ile kararlı kalır).
        /// </summary>
        private IReadOnlyList<AdminPlayerView> OrderedPlayers(AdminRoster roster)
        {
            if (!roster.IsFfa)
            {
                return roster.Players;
            }

            _sorted.Clear();
            for (int i = 0; i < roster.Players.Count; i++)
            {
                _sorted.Add(roster.Players[i]);
            }

            _sorted.Sort(CompareByScoreDescending);
            return _sorted;
        }

        private static int CompareByScoreDescending(AdminPlayerView a, AdminPlayerView b)
        {
            int byScore = b.score.CompareTo(a.score);
            return byScore != 0 ? byScore : a.playerId.CompareTo(b.playerId);
        }

        private void RefreshSummary(AdminRoster roster)
        {
            if (roster.IsFfa)
            {
                // Takım yok → tek anlamlı başlık lider. Skor hiç yazılmadıysa (maç başlamadı)
                // uydurma yapmayız, "herkes tek" der geçeriz.
                IReadOnlyList<AdminPlayerView> ranked = OrderedPlayers(roster);
                _headline.text = ranked.Count > 0 && ranked[0].score > 0
                    ? $"LİDER: {ranked[0].name} {ranked[0].score}"
                    : "HERKES TEK";
                _teamSummary.text = $"{roster.Players.Count} oyuncu · {AliveCount(roster.Players)} canlı";
                return;
            }

            _headline.text = $"KIRMIZI {roster.ScoreRed} — {roster.ScoreBlue} MAVİ";

            roster.TeamTotals("red", out int redKills, out int redDeaths, out int redAlive);
            roster.TeamTotals("blue", out int blueKills, out int blueDeaths, out int blueAlive);

            _sb.Clear();
            _sb.AppendLine($"KIRMIZI: {roster.Red.Count} oyuncu · {redAlive} canlı · {redKills} öldürme · {redDeaths} ölüm");
            _sb.Append($"MAVİ: {roster.Blue.Count} oyuncu · {blueAlive} canlı · {blueKills} öldürme · {blueDeaths} ölüm");
            _teamSummary.text = _sb.ToString();
        }

        /// <summary>
        /// Satır havuzunu listeye uydurur ve yerleştirir.
        /// <para>⚠️ Bağlantı durumuna göre SÜZME YOKTUR ve eklenmez (§10.2): oyundan çıkarılmış
        /// (<c>left</c>) satır maç sonu tablosunda görünmeli — sunucu onu tam da bunun için maç
        /// bitene kadar roster'da tutuyor.</para>
        /// <para>⚠️ Satır SAYISI da kapanmaz: yan paneldeki kolonların aksine burada kırpma yoktur,
        /// content yüksekliği sürülür ve fazlasını ScrollRect kaydırır. Kırpılan bir istatistik
        /// tablosu operatörün aradığı oyuncuyu gizlerdi.</para>
        /// </summary>
        private void RefreshRows(IReadOnlyList<AdminPlayerView> players)
        {
            if (_rowContainer == null)
            {
                return;
            }

            int count = players != null ? players.Count : 0;

            while (_rows.Count < count)
            {
                if (_rowPrefab == null)
                {
                    Debug.LogWarning("[AdminStatsPanel] _rowPrefab atanmadı; oyuncu satırları çizilemiyor.");
                    break;
                }

                AdminStatsRow row = Instantiate(_rowPrefab, _rowContainer);
                row.Initialize(HandleRowSelected, ShowPopup);
                _rows.Add(row);
            }

            count = Mathf.Min(count, _rows.Count);
            _visibleRows = count;

            float height = RowHeight;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (i >= count)
                {
                    _rows[i].SetVisible(false);
                    continue;
                }

                _rows[i].SetVisible(true);
                _rows[i].Place(i * (height + _rowGap), height);
                _rows[i].Bind(players[i], players[i].playerId == AdminSession.SelectedPlayerId);
            }

            // Content yüksekliği koddan sürülür: ContentSizeFitter/Layout Group KULLANILMIYOR
            // (UiKit yerleşim kuralı) — kaydırma çubuğunun aralığı yalnız buradan gelir.
            _rowContainer.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                count > 0 ? count * (height + _rowGap) : 0f);
        }

        private void RefreshMatchInfo(AdminRoster roster)
        {
            ArenaClient client = ArenaClient.Instance;
            string endpoint = client != null && client.IsConnected
                ? $"{client.ServerIp}:{client.ServerPort}"
                : "bağlı değil";
            float age = roster.SnapshotAge;
            string mode = string.IsNullOrEmpty(roster.ModeId) ? "-" : AdminContent.ModeDisplayName(roster.ModeId);
            string map = string.IsNullOrEmpty(roster.SceneName) ? "-" : roster.SceneName;

            _sb.Clear();
            _sb.AppendLine($"Faz: {roster.Phase} · kalan {FormatTime(roster.TimeRemaining)} · " +
                           $"mod {mode} · harita {map}" +
                           // Süre/limit o maça özel olabildiği için (§5.2) operatör seçtiği
                           // değerin gerçekten uygulandığını buradan doğrular.
                           (roster.RoundSeconds > 0 ? $" · raund {AdminCommands.FormatDuration(roster.RoundSeconds)}" : "") +
                           (roster.ScoreLimit > 0 ? $" · skor limiti {roster.ScoreLimit}" : ""));
            _sb.Append($"Sunucu: {endpoint} · poz akışı " +
                       (age >= 0f ? $"{age:0.0} sn önce" : "yok") +
                       $" · bağlı admin {roster.AdminCount}");
            _matchSummary.text = _sb.ToString();
        }

        private static int AliveCount(IReadOnlyList<AdminPlayerView> players)
        {
            int count = 0;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].IsConnected && players[i].alive)
                {
                    count++;
                }
            }

            return count;
        }

        private static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }

        // ------------------------------------------------------------- toplu eylem

        /// <summary>
        /// Herkesin kalibrasyonunu gözlükteki kayıttan yeniden yükletir.
        /// <para>⚠️ <b>Sıra önemli:</b> önce satırlar yükleme kipine alınır, SONRA komut gider.
        /// Ters sırada, sonucu ışık hızıyla dönen bir başlığın yanıtı satır daha kipe girmeden
        /// gelirdi ve sonuç görünmeden yutulurdu.</para>
        /// <para>Onay penceresi YOKTUR: yeniden yükleme kimseyi savaş dışı bırakmaz, tutmazsa
        /// oyuncu eskisi gibi kalır — geri alınabilir bir denemedir (sıfırlamanın tersine).</para>
        /// </summary>
        private void ReloadAllCalibrations()
        {
            for (int i = 0; i < _visibleRows && i < _rows.Count; i++)
            {
                _rows[i].BeginCalibrationLoad();
            }

            AdminCommands.ReloadCalibration(0);
        }

        /// <summary>
        /// Herkesin kalibrasyonunu sıfırlar — <b>iki adımlı</b>, <see cref="AdminPlayerRow"/>'un
        /// yıkıcı düğmeleriyle aynı sözleşme: ilk basış onay penceresini açar, pencere açıkken gelen
        /// ikinci basış komutu gönderir.
        /// <para>Sürtünmenin gerekçesi komşusuyla karşıtlığıdır: yanındaki yeniden yükleme geri
        /// alınabilir bir denemedir, bu ise tek tıklamayla sahadaki herkesi savaş dışı bırakır ve
        /// kalibrasyonu yalnız başlıklar geri açabilir (§10.6).</para>
        /// </summary>
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

        /// <summary>Sıfırlama düğmesinin etiketini ve zeminini onay durumuna göre boyar: dinlenmede
        /// kırmızı yazı + alt barın ortak zemini, onay beklerken tam ters (kırmızı zemin) — operatör
        /// ikinci basışın ne yapacağını düğmeye bakarak görür.</summary>
        private void ApplyClearAllButton()
        {
            bool armed = _clearAllArmedAt >= 0f;

            if (_clearAllLabel != null)
            {
                _clearAllLabel.text = armed ? "EMİN? HERKESİ SIFIRLA" : "TÜM KALİBRASYONLARI SIFIRLA";
                _clearAllLabel.color = armed ? UiKit.OnAccent : UiKit.Bad;
            }

            if (_clearAllButton != null && _clearAllButton.targetGraphic is Image image)
            {
                image.color = armed ? UiKit.Bad : ClearAllIdleFill;
            }
        }

        // ------------------------------------------------------------- geri çağrı

        private void HandleRowSelected(int playerId)
        {
            AdminSession.SelectedPlayerId = playerId;
        }

        /// <summary>Sonucu <b>yalnız ilgili satıra</b> verir: mesaj bir olaydır, listeyi yeniden
        /// çizmeye gerek yok.</summary>
        private void HandleCalibrationResult(CalibrationResultMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            for (int i = 0; i < _visibleRows && i < _rows.Count; i++)
            {
                if (_rows[i].PlayerId == msg.playerId)
                {
                    _rows[i].ApplyCalibrationResult(msg.ok, msg.error);
                    return;
                }
            }
        }

        /// <summary>
        /// Başarısız bir kalibrasyon yüklemesinin gerekçesini gösterir (dar düğme yalnız "HATA"
        /// taşıyabiliyor).
        /// <para>⚠️ Arkasına scrim KOYULMAZ ve panel kilitlenmez — bu paneldeki genel kural:
        /// canlı sahne izlenmeye devam eder ve operatör pencere açıkken de çalışabilir.</para>
        /// </summary>
        private void ShowPopup(int playerId, string error)
        {
            if (_popupRoot == null)
            {
                return;
            }

            if (_popupText != null)
            {
                AdminRoster roster = AdminRoster.Instance;
                string who = roster != null ? roster.NameOf(playerId) : $"Oyuncu {playerId}";
                _popupText.text = $"{who} · {error}";
                _popupText.color = UiKit.Bad;
            }

            _popupRoot.SetActive(true);
            _popupUntil = Time.unscaledTime + PopupSeconds;
        }

        private void HidePopup()
        {
            _popupUntil = -1f;

            if (_popupRoot != null && _popupRoot.activeSelf)
            {
                _popupRoot.SetActive(false);
            }
        }
    }
}
