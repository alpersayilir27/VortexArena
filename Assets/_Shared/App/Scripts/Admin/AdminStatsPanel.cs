using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.Net;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// İstatistikler paneli (skor bandının ortasındaki chip ya da <c>I</c> tuşu açar).
    /// Kart yarı saydamdır, arkasına scrim koyulmaz — canlı sahne izlenmeye devam eder.
    ///
    /// <para><b>Tablo neden kolon kolon çiziliyor:</b> TMP varsayılan fontu eşit genişlikli
    /// DEĞİL, tek metin bloğunda boşlukla hizalama kayar. Bu yüzden her kolon kendi TMP'sidir ve
    /// satırlar <c>\n</c> ile birleştirilir — hizalama font metriğinden bağımsız kalır.</para>
    ///
    /// <para><b>Uydurma metrik yok:</b> yalnız protokolde gerçekten taşınan veriler gösterilir
    /// (K/D/HP sunucudan — §5.3 <c>lobby_state</c>; batarya/sahne <c>status</c>'tan; <b>ping</b>
    /// istemcinin ölçüp bildirdiği RTT — §6.7 <c>net_stats</c>). <b>Hasar ve isabet oranı protokolde
    /// YOK, bu yüzden tabloda da yok.</b></para>
    /// <para><b>Jitter ve paket kaybı bilinçli olarak tabloda DEĞİL</b> — ikisi de ölçülüyor ve
    /// <c>net_stats</c> ile geliyor, ama operatörün eyleme çevirebileceği tek sayı ping'dir. Hacim
    /// (bayt/sn, paket/sn) hiç gelmiyor: o sayılar sunucu konsolundadır (<c>[state]</c> satırı) ve
    /// oraya aittir.</para>
    /// <para>⚠️ <b>BATARYA hücresi zengin metin içerir</b> (eşiğin altındaki pil ve kumanda
    /// simgeleri token başına renklenir; tek TMP'nin tek <c>.color</c>'ı olduğu için başka yolu
    /// yok). Bayrağı <see cref="EnableBatteryColumnRichText"/> açar — prefabtaki değere
    /// güvenilmez, gerekçesi orada.</para>
    /// </summary>
    public class AdminStatsPanel : MonoBehaviour
    {
        private const float RefreshInterval = 0.5f;

        /// <summary>Kolon sırası — soldan sağa. <b>Tek işi <see cref="CellText"/>'in <c>switch</c>
        /// sırasını ve <see cref="_columns"/>'un beklenen uzunluğunu belgelemektir</b>; başlık
        /// metinleri ve genişlikler PREFABTA yaşar.
        /// <para>⚠️ Buraya kolon eklemek YETMEZ: prefabta da bir TMP objesi açıp <c>_columns</c>
        /// dizisine bağlamak gerekir, yoksa yeni kolon sessizce hiç çizilmez (dizi prefabtan gelir,
        /// buradaki uzunluk yalnız yeni örneklerin varsayılanıdır).</para>
        /// <para><c>SKOR</c> bireysel maç skorudur (§10.2) ve <c>K</c> ile aynı şey DEĞİLDİR: skoru
        /// mod yazar, öldürme başına 1 olmak zorunda değil.</para></summary>
        /// <remarks>PING <b>sona</b> eklendi, BATARYA'nın yanına değil: prefabtaki kolonlar
        /// <c>Header0..N</c>/<c>Column0..N</c> çiftleri hâlinde ve araya girmek mevcut dört objenin
        /// hepsini yeniden konumlandırmayı gerektirirdi. Sağ kenar bir teşhis kolonu için doğal yer.</remarks>
        private static readonly string[] ColumnTitles =
            { "OYUNCU", "TAKIM", "SKOR", "K", "D", "K/D", "HP", "BATARYA", "DURUM", "SAHNE", "PING" };

        /// <summary>Zengin metin içeren TEK kolon (<see cref="ColumnTitles"/> ile aynı indeks).</summary>
        private const int BatteryColumn = 7;

        // NOT: PanelWidth/PanelHeight/TableTop/ColumnWidths sabitleri KALDIRILDI — hiçbiri
        // okunmuyordu (panel kod tarafından çizildiği dönemden kalmışlar) ve durdukları yerde
        // "yerleşimi kod belirliyor" diye yalan söylüyorlardı. Kart ölçüsü ve kolon genişlikleri
        // prefabın kendi RectTransform'larındadır; tek doğruluk kaynağı orasıdır.

        /// <summary>FFA sıralaması için tampon — her tazelemede yeni liste ayırmamak adına.</summary>
        private readonly List<AdminPlayerView> _sorted = new List<AdminPlayerView>();

        // ⚠️ Alanlar [SerializeField] — görünüm PREFABTAN gelir
        // (`_Shared/App/Resources/UI/AdminStatsPanel.prefab`). Bu sınıf yalnız veri yazar.

        [Tooltip("Açılıp kapanan kart — panel kapalıyken bu obje devre dışı bırakılır.")]
        [SerializeField] private GameObject _root;
        [SerializeField] private Button _closeButton;
        [SerializeField] private TextMeshProUGUI _headline;
        [SerializeField] private TextMeshProUGUI _teamSummary;
        [SerializeField] private TextMeshProUGUI _matchSummary;

        [Tooltip("Tablo kolonları — ColumnTitles ile AYNI SIRADA ve aynı sayıda olmalı.")]
        [SerializeField] private TextMeshProUGUI[] _columns = new TextMeshProUGUI[ColumnTitles.Length];

        private readonly StringBuilder _sb = new StringBuilder();

        private float _nextRefresh;
        private bool _dirty = true;

        private void Start()
        {
            if (_closeButton != null)
            {
                // Prefabta kalıcı onClick kaydı YOKTUR (bkz. AdminPreferencesPanel.WireButtons).
                _closeButton.onClick.RemoveAllListeners();
                _closeButton.onClick.AddListener(AdminSession.ClosePanel);
            }

            if (_root != null)
            {
                _root.SetActive(false); // görünürlüğü Apply() belirler
            }

            EnableBatteryColumnRichText();
            Apply();
        }

        /// <summary>
        /// BATARYA kolonunun zengin metin kapısı.
        /// <para>
        /// ⚠️ <b>Bayrak prefabta değil BURADA açılır:</b> <c>&lt;color=…&gt;</c> etiketlerini
        /// <see cref="AdminPlayerRow.FormatBattery"/>/<see cref="AdminPlayerRow.FormatControllers"/>
        /// üretiyor, yani bayrak bir görünüm tercihi değil üretilen metnin sözleşmesidir.
        /// Prefabta kapalı kaldığında hücre <c>%73 K:&lt;color=#…&gt;~</c> gibi ham etiket çizer.
        /// </para>
        /// <para>
        /// ⚠️ <b>Yalnız bu kolon için.</b> Diğer kolonlar zengin metne KAPALI kalmalı: OYUNCU
        /// kolonunda oyuncunun kendi yazdığı ad var ve <c>&lt;b&gt;</c> içeren bir ad tabloyu
        /// bozardı.
        /// </para>
        /// </summary>
        private void EnableBatteryColumnRichText()
        {
            if (_columns != null && BatteryColumn < _columns.Length && _columns[BatteryColumn] != null)
            {
                _columns[BatteryColumn].richText = true;
            }
        }

        private void OnEnable()
        {
            AdminSession.Changed += MarkDirty;

            if (AdminRoster.Instance != null)
            {
                AdminRoster.Instance.Changed += MarkDirty;
            }
        }

        private void OnDisable()
        {
            AdminSession.Changed -= MarkDirty;

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
            }

            AdminRoster roster = AdminRoster.Instance;
            if (!open || roster == null)
            {
                return;
            }

            RefreshSummary(roster);
            RefreshTable(OrderedPlayers(roster));
            RefreshMatchInfo(roster);
        }

        /// <summary>
        /// Tablo sırası: takımlı modda roster sırası (playerId) korunur — operatör oyuncuyu hep
        /// aynı satırda arar. FFA'da tek sıralama ölçütü skordur, bu yüzden tablo skora göre
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

        /// <summary>⚠️ Tabloda bağlantı durumuna göre SÜZME YOKTUR ve eklenmez (§10.2): oyundan
        /// çıkarılmış (<c>left</c>) satır maç sonu tablosunda görünmeli — sunucu onu tam da bunun
        /// için maç bitene kadar roster'da tutuyor.</summary>
        private void RefreshTable(IReadOnlyList<AdminPlayerView> players)
        {
            for (int c = 0; c < _columns.Length; c++)
            {
                _sb.Clear();

                for (int i = 0; i < players.Count; i++)
                {
                    if (i > 0)
                    {
                        _sb.AppendLine();
                    }

                    _sb.Append(CellText(players[i], c));
                }

                _columns[c].text = _sb.ToString();
            }
        }

        private static string CellText(AdminPlayerView view, int column)
        {
            switch (column)
            {
                case 0: return $"{view.name} #{view.playerId}";
                case 1: return view.team == "red" ? "kırmızı" : view.team == "blue" ? "mavi" : "-";
                case 2: return view.score.ToString();
                case 3: return view.kills.ToString();
                case 4: return view.deaths.ToString();
                case 5: return view.deaths > 0
                    ? (view.kills / (float)view.deaths).ToString("0.00")
                    : view.kills.ToString("0.00");
                case 6: return Mathf.RoundToInt(view.hp).ToString();
                // Pil eşikleri/renkleri ve kumanda simgeleri satır kartıyla TEK kaynaktan gelir
                // (AdminPlayerRow) — aynı gözlük iki yerde farklı renkte görünmemeli.
                // ⚠️ Kumanda AYRI BİR KOLON DEĞİL, batarya hücresinin devamı: kolonlar prefabtaki
                // TMP objelerinden geliyor (bkz. ColumnTitles) ve yeni kolon prefab işidir. İki
                // değer karışmaz — kumanda tokeni kendi "K:" önekini taşır ve yüzde değildir.
                case BatteryColumn:
                {
                    string battery = AdminPlayerRow.FormatBattery(view);
                    string controllers = AdminPlayerRow.FormatControllers(view);
                    return string.IsNullOrEmpty(controllers) ? battery : $"{battery} {controllers}";
                }
                case 8: return StateText(view);
                case 9: return string.IsNullOrEmpty(view.scene) ? "-" : view.scene;
                // §6.7: -1 = ölçüm yok (henüz yoklama dönmedi ya da gözlükte eski sürüm var).
                // 0 yazmak "0 ms ping" gibi okunurdu, bu yüzden "-".
                case 10: return view.rttMs < 0 ? "-" : $"{view.rttMs} ms";
                // Prefabta ColumnTitles'tan FAZLA kolon bağlanmışsa boş kalsın — eskiden burada
                // `default: scene` vardı ve yeni kolon eklenince sessizce sahne adını tekrarlardı.
                default: return "";
            }
        }

        /// <summary>⚠️ "çevrimdışı" diye bir durum YOKTUR (§2) — satır ya geri bekleniyor
        /// (sayaçla) ya oyundan çıkarılmıştır.</summary>
        private static string StateText(AdminPlayerView view)
        {
            if (view.IsReconnecting)
            {
                return $"yeniden bağlanıyor ({view.ReconnectSecondsLeft} sn)";
            }

            if (view.HasLeft)
            {
                return "ayrıldı";
            }

            if (!view.alive)
            {
                float remaining = view.RespawnRemaining;
                return remaining > 0.1f ? $"ölü ({Mathf.CeilToInt(remaining)} sn)" : "tabanda bekliyor";
            }

            return view.ready ? "hazır" : "bekliyor";
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
    }
}
