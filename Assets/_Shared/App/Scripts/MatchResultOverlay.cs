using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using VortexArena.Core;
using VortexArena.Core.Combat;
using VortexArena.Core.UI;
using VortexArena.Net;
using VortexArena.Protocol;
using UiImage = UnityEngine.UI.Image;

namespace VortexArena.App
{
    /// <summary>
    /// Match end screen: on <c>match_end</c> hides gameplay HUDs and shows the <b>result card</b>
    /// (won / lost / draw) first, then the <b>scoreboard</b>; closes itself and restores the HUDs
    /// when the operator starts a new match or returns to lobby.
    /// <para>
    /// <b>Phase contract (§10.1):</b> this screen never closes on its own — leaving <c>finished</c>
    /// is the operator's choice (<c>load_match</c> / <c>return_to_lobby</c> / another
    /// <c>match_state</c>). Hence NO "close after a few seconds" timer here; the only timer is the
    /// result card → scoreboard transition.
    /// </para>
    /// <para>
    /// <b>Shares the CARD SHELL with <c>AdminStatsPanel</c>, NOT its layout.</b> This table is
    /// read-only; the admin panel is built from per-player action rows (<c>AdminStatsRow</c>). The
    /// split comes from the audience: a player reads a <b>result</b>, the operator manages a live
    /// <b>work list</b> whose buttons would be meaningless (and must be unpressable) on a player's
    /// screen.
    /// </para>
    /// <para>
    /// <b>Table shape:</b> one block per team, each block a header plate + cloned rows
    /// (<see cref="boardRowTemplate"/>). ⚠️ The older <c>\n</c>-joined column texts
    /// (<see cref="boardColumns"/>) are still wired and still work: a prefab VARIANT skinned around
    /// them only has to clear <see cref="boardRowTemplate"/> to get them back. Rows win in the
    /// generic screen because a per-row highlight (the player's own line) cannot be drawn behind one
    /// shared text block.
    /// </para>
    /// <para>
    /// <b>Mode agnostic.</b> The winner arrives on <c>match_end</c>'s two channels (team or player,
    /// §5.3) and table ordering is split by <see cref="ModeRuntime.IsTeamless"/> — no
    /// <c>if (modeId == "…")</c> chain here; a new mode gets this screen for free. The kids' modes
    /// drop the combat columns the same way, off <see cref="ModeRuntime.HidesCombatStats"/>.
    /// </para>
    /// <para>
    /// <b>Per-mode look:</b> <c>ModeDefinition.ResultScreenPrefab</c> may point at a VARIANT of this
    /// prefab; at match end the singleton swaps itself for it (<see cref="SwapTo"/>). The variant
    /// overrides only art and wording — bindings are inherited, so behaviour stays identical.
    /// </para>
    /// <para>
    /// Self-bootstrapping persistent singleton (<c>WeaponGranter</c> pattern): NOT placed in scenes,
    /// else every new arena would gain a manual setup step. Visuals live entirely in the prefab
    /// (<c>Resources/UI/MatchResultOverlay</c>) — this class only writes data.
    /// </para>
    /// <para>
    /// ⚠️ <b>Role gate is at display time, not bootstrap:</b> <c>AppSession.Role</c> resolves in the
    /// Boot scene and its ordering against <c>AfterSceneLoad</c> bootstrap is not guaranteed. On the
    /// admin spectator the winner and table are already drawn by <c>AdminHud</c>, so this screen
    /// never opens there.
    /// </para>
    /// </summary>
    public class MatchResultOverlay : MonoBehaviour
    {
        /// <summary>Prefab path inside <c>Resources</c> (no extension).</summary>
        public const string ResourcePath = "UI/MatchResultOverlay";

        // Row metrics (tema.css `.sc-row` / `.sc-team`). Shared with the prefab builder so the
        // template and the runtime stride can never drift apart. ⚠️ These are the DEFAULTS of the
        // serialized fields below — a skin with other row sizes (Lokanta) writes those instead.
        public const float RowHeight = 76f;
        public const float RowGap = 6f;

        /// <summary>First row's top inside a block: header plate (64) + flex gap (6).</summary>
        public const float RowsTop = 70f;

        /// <summary>Name cell's left edge inside a row (rank cell is 56 px).</summary>
        public const float NameCellX = 56f;

        /// <summary>CSS <c>.sc-row .nm { gap: 12px }</c>.</summary>
        public const float NameGap = 12f;

        /// <summary>Blocks per table — one per team; FFA splits its single ranking across both.</summary>
        public const int BlockCount = 2;

        /// <summary>Sorting offset (<see cref="UiDrawOnTop"/>) — above the HUD range.</summary>
        private const int ResultSortingOffset = 20;

        /// <summary>Column order, left to right — documents <see cref="CellText"/>'s <c>switch</c>
        /// order and the expected length of <see cref="boardColumns"/>. Header texts and widths live
        /// IN THE PREFAB (repo-wide UI contract: code only writes data).
        /// <para>⚠️ Adding a column here is NOT enough: a TMP object must also be created in the
        /// prefab and wired into the array, else the new column is silently never drawn.</para>
        /// <para>⚠️ The K and D headers are ICONS, not text, in the prefab (crosshair / skull) —
        /// like the admin card; those <c>Header</c> objects are intentionally blank.</para>
        /// <para>⚠️ Operator diagnostics (battery · controller · ping · status) are NOT here and are
        /// not added: players are never shown live device state at match end — that is admin
        /// information.</para></summary>
        private static readonly string[] ColumnOrder = { "OYUNCU", "TAKIM", "SKOR", "K", "D", "K/D" };

        private enum Stage
        {
            Hidden,
            Result,
            Scoreboard
        }

        private static MatchResultOverlay _instance;

        [Header("Paneller")]
        [Tooltip("Sonuç kartı (KAZANDIN / KAYBETTİN / BERABERE).")]
        [SerializeField] private GameObject resultPanel;
        [Tooltip("Skor tablosu kartı (AdminStatsPanel tasarımı).")]
        [SerializeField] private GameObject scoreboardPanel;

        [Tooltip("Sonuç kartından skor tablosuna geçiş süresi (sn).")]
        [SerializeField] private float resultSeconds = 6f;

        [Header("Sonuç kartı")]
        [SerializeField] private TextMeshProUGUI resultTitleText;
        [Tooltip("Kazananın adı/takımı — berabere bittiğinde boş kalır.")]
        [SerializeField] private TextMeshProUGUI resultWinnerText;
        [Tooltip("Takım skoru dışındaki skor satırı (bireysel skor, ko-op özeti).")]
        [SerializeField] private TextMeshProUGUI resultScoreText;

        [Tooltip("Takım skorlu modlarda resultScoreText yerine açılan skor plakası.")]
        [SerializeField] private GameObject resultScorebug;
        [SerializeField] private TextMeshProUGUI resultScoreRedText;
        [SerializeField] private TextMeshProUGUI resultScoreBlueText;

        [Header("Sonuç kartı — sonuç rengini taşıyan parçalar")]
        [Tooltip("Plakanın altından yükselen ışık (Radial_256).")]
        [SerializeField] private UiImage resultSlabGlow;
        [Tooltip("Plakanın 3 px alt çizgisi.")]
        [SerializeField] private UiImage resultSlabUnderline;
        [SerializeField] private UiShape resultSlabRuleLeft;
        [SerializeField] private UiShape resultSlabRuleRight;

        [Header("Skor tablosu")]
        [Tooltip("Kartın başlığı: kazanan satırı.")]
        [SerializeField] private TextMeshProUGUI boardHeadlineText;
        [Tooltip("Başlıktaki skor plakası — takım skorlu modlarda açılır.")]
        [SerializeField] private GameObject boardScorebug;
        [SerializeField] private TextMeshProUGUI boardScoreRedText;
        [SerializeField] private TextMeshProUGUI boardScoreBlueText;

        [Tooltip("Sol bloğun özet satırı (FFA'da tek blok kullanılır).")]
        [SerializeField] private TextMeshProUGUI boardTeamSummaryText;
        [Tooltip("Sağ bloğun özet satırı.")]
        [SerializeField] private TextMeshProUGUI boardTeamSummary2Text;
        [Tooltip("Alt bandın tek satırlık yedeği — dört istatistik kutusu kapalıyken açılır.")]
        [SerializeField] private TextMeshProUGUI boardMatchSummaryText;

        [Header("Skor tablosu — satır blokları")]
        [Tooltip("Takım blokları (kırmızı, mavi).")]
        [SerializeField] private GameObject[] boardBlocks = new GameObject[BlockCount];
        [Tooltip("Blok başlık plakaları — takım rengi buradan verilir.")]
        [SerializeField] private UiShape[] boardBlockPlates = new UiShape[BlockCount];
        [Tooltip("Blok başlığındaki takım adı.")]
        [SerializeField] private TextMeshProUGUI[] boardBlockTitles = new TextMeshProUGUI[BlockCount];
        [Tooltip("Blok başlığındaki SKOR hücresi (ko-op'ta KATKI olur).")]
        [SerializeField] private TextMeshProUGUI[] boardBlockScoreHeaders = new TextMeshProUGUI[BlockCount];
        [Tooltip("Blok başlığının K · D · K/D hücreleri (blok başına üç tane, sırayla).")]
        [SerializeField] private GameObject[] boardBlockCombatCells = new GameObject[BlockCount * 3];
        [Tooltip("Satırların kopyalandığı kök (blok başına bir tane).")]
        [SerializeField] private RectTransform[] boardRowParents = new RectTransform[BlockCount];
        [Tooltip("Pasif satır şablonu — boş bırakılırsa tablo kolon metinlerine döner.")]
        [SerializeField] private GameObject boardRowTemplate;

        [Header("Skor tablosu — alt bant")]
        [Tooltip("İstatistik kutuları (ÖLDÜRME · ÖLÜM · K/D · SKOR).")]
        [SerializeField] private GameObject footStatsGroup;
        [Tooltip("Kutuların değerleri — ÖLDÜRME, ÖLÜM, K/D, SKOR sırasında; ko-op derisinde beş " +
                 "kutu olur (KATKI, SIRA, MUTLU, MUTSUZ, EKİP SKORU).")]
        [SerializeField] private TextMeshProUGUI[] footStatValues = new TextMeshProUGUI[4];
        [Tooltip("\"SEN\" plakası — oyuncunun takım rengini alır.")]
        [SerializeField] private UiShape footWhoPlate;

        [Header("Skor tablosu — kolon metinleri (eski tablo, varyantlar için duruyor)")]
        [Tooltip("Tablo kolonları — ColumnOrder ile AYNI SIRADA ve aynı sayıda olmalı.")]
        [SerializeField] private TextMeshProUGUI[] boardColumns = new TextMeshProUGUI[ColumnOrder.Length];

        [Tooltip("Kolon kökleri (başlık + değerler) — ColumnOrder sırasında. Bağlanmazsa kolon " +
                 "gizlenirken yalnız değer metni kapanır.")]
        [SerializeField] private GameObject[] boardColumnRoots = new GameObject[ColumnOrder.Length];

        [Tooltip("Kolon başlık metinleri — ColumnOrder sırasında. Bağlanmazsa başlık yeniden " +
                 "adlandırılamaz (K/D başlıkları ikon olduğu için boş bırakılabilir).")]
        [SerializeField] private TextMeshProUGUI[] boardColumnHeaders = new TextMeshProUGUI[ColumnOrder.Length];

        [Header("Ko-op metin hedefleri (deri; boşsa eski birleşik satırlar çizer)")]
        [Tooltip("Sonuç kartındaki büyük ortak skor.")]
        [SerializeField] private TextMeshProUGUI coopSharedScoreText;
        [SerializeField] private TextMeshProUGUI coopHappyText;
        [SerializeField] private TextMeshProUGUI coopUnhappyText;
        [Tooltip("Oyuncunun kendi katkısı.")]
        [SerializeField] private TextMeshProUGUI coopSelfScoreText;
        [Tooltip("Oyuncunun sırası (\"1.\").")]
        [SerializeField] private TextMeshProUGUI coopRankText;
        [SerializeField] private TextMeshProUGUI boardHappyText;
        [SerializeField] private TextMeshProUGUI boardUnhappyText;
        [SerializeField] private TextMeshProUGUI boardSharedScoreText;
        [SerializeField] private TextMeshProUGUI boardSelfScoreText;
        [SerializeField] private TextMeshProUGUI boardRankText;

        [Header("Satır derisi (varsayılanlar = Girdap teması)")]
        [SerializeField] private float rowHeight = RowHeight;
        [SerializeField] private float rowGap = RowGap;
        [SerializeField] private float rowsTop = RowsTop;

        [Tooltip("Normal satırın dolgusu — havuzdan gelen satır buraya GERİ boyanır, yoksa bir " +
                 "önceki maçta \"benim\" olan satır vurgulu kalır.")]
        [SerializeField] private Color rowFillA = Girdap.Hex(0xAACDFF, 0.095f);
        [SerializeField] private Color rowFillB = Girdap.Hex(0xAACDFF, 0.03f);
        [SerializeField] private Color rowOutline = Girdap.Acc;
        [SerializeField] private float rowOutlineWidth;
        [SerializeField] private Color rowTextColor = Girdap.Text;
        [Tooltip("Sıra numarası ve \"#id\" rengi.")]
        [SerializeField] private Color rowSmallColor = Girdap.Faint;
        [SerializeField] private Color rowRankColor = Girdap.Faint;

        [Tooltip("Oyuncunun kendi satırı.")]
        [SerializeField] private Color ownRowFillA = Girdap.Hex(0x7DE3FF, 0.36f);
        [SerializeField] private Color ownRowFillB = Girdap.Hex(0x7DE3FF, 0.10f);
        [SerializeField] private Color ownRowOutline = Girdap.Acc;
        [SerializeField] private float ownRowOutlineWidth = 2f;
        [SerializeField] private Color ownRowGlow = Girdap.Hex(0x7DE3FF, 0.26f);
        [SerializeField] private float ownRowGlowWidth = 28f;
        [SerializeField] private Color ownRowTextColor = Color.white;
        [SerializeField] private Color ownRowSmallColor = Girdap.AccHi;
        [SerializeField] private Color ownRowRankColor = Girdap.AccHi;

        [Tooltip("Maçtan ayrılmış oyuncunun satır alfası.")]
        [SerializeField] private float leftRowAlpha = 0.45f;

        [Header("Kelimeler ve renkler (mod varyantı ezer)")]
        [Tooltip("Kapalıyken sonuç başlığının rengine/parıltısına dokunulmaz — derinin kendi " +
                 "kurdelesi kazandın/kaybettin tonunu taşımaz.")]
        [SerializeField] private bool tintTitleByOutcome = true;
        [Tooltip("Ko-op blok özeti: {0} oyuncu, {1} katkı toplamı, {2} katkı/10. Boşsa müşteri " +
                 "sayıları yazılır (eski satır).")]
        [SerializeField] private string coopBlockSummaryFormat = "";
        [SerializeField] private string wonTitle = "KAZANDIN";
        [SerializeField] private string lostTitle = "KAYBETTİN";
        [SerializeField] private string drawTitle = "BERABERE";
        [Tooltip("Ko-op sonuç başlığı — rakip yok, kazandın/kaybettin okunmaz.")]
        [SerializeField] private string coopTitle = "OYUN BİTTİ";
        [Tooltip("Skor kolonunun başlığı.")]
        [SerializeField] private string scoreHeader = "SKOR";
        [Tooltip("Ko-op'ta skor kolonunun başlığı (ortak toplama katkı).")]
        [SerializeField] private string coopScoreHeader = "KATKI";
        [Tooltip("Takımsız modda sol bloğun başlığı.")]
        [SerializeField] private string soloBlockTitle = "OYUNCULAR";
        [SerializeField] private Color wonColor = Girdap.Good;
        [SerializeField] private Color lostColor = Girdap.Bad;
        [SerializeField] private Color drawColor = Girdap.Acc;
        [SerializeField] private Color coopColor = Girdap.Acc;
        [Tooltip("Kırmızı takım adının yazı rengi (sonuç kartı + skor tablosu).")]
        [SerializeField] private Color redTeamColor = Girdap.RedInk;
        [Tooltip("Mavi takım adının yazı rengi (sonuç kartı + skor tablosu).")]
        [SerializeField] private Color blueTeamColor = Girdap.BlueInk;

        private readonly List<PlayerInfo> _ranked = new List<PlayerInfo>();
        private readonly StringBuilder _sb = new StringBuilder();

        /// <summary>Ranked players split per block; index = block.</summary>
        private readonly List<PlayerInfo>[] _blocks =
            { new List<PlayerInfo>(), new List<PlayerInfo>() };

        private readonly List<Row>[] _rows = { new List<Row>(), new List<Row>() };

        private PlayerInfo[] _roster = Array.Empty<PlayerInfo>();
        private MatchEndMsg _lastEnd;

        /// <summary>Last <c>modeState</c> line (§10.1) — the co-op customer counters live there and the
        /// server keeps publishing it through the <c>finished</c> phase.</summary>
        private string _modeState = "";

        private Stage _stage = Stage.Hidden;
        private float _scoreboardAt;

        /// <summary>Prefab this instance was spawned from; compared against the running mode's screen
        /// at match end.</summary>
        private MatchResultOverlay _source;

        /// <summary>
        /// Cell references of one cloned row. ⚠️ Looked up by child NAME once per clone instead of
        /// through a per-row MonoBehaviour: the row lives in the shared prefab, which this screen's
        /// mode variants inherit — a script on it would have to be wired in every variant too.
        /// </summary>
        private sealed class Row
        {
            public GameObject Go;
            public CanvasGroup Group;
            public UiShape Bg;
            public TextMeshProUGUI Rank;
            public TextMeshProUGUI Name;
            public RectTransform Tag;
            public TextMeshProUGUI Small;
            public TextMeshProUGUI Score;

            /// <summary>Optional icon next to the score (skin); hidden while the score is 0.</summary>
            public UiImage ScoreIcon;

            /// <summary>K · D · K/D — the cells the kids' modes drop.</summary>
            public readonly TextMeshProUGUI[] Combat = new TextMeshProUGUI[3];

            public static Row Bind(GameObject go)
            {
                Transform t = go.transform;
                var row = new Row
                {
                    Go = go,
                    Group = go.GetComponent<CanvasGroup>(),
                    Bg = Find<UiShape>(t, "Bg"),
                    Rank = Find<TextMeshProUGUI>(t, "Rank"),
                    Name = Find<TextMeshProUGUI>(t, "Name"),
                    Small = Find<TextMeshProUGUI>(t, "Small"),
                    Score = Find<TextMeshProUGUI>(t, "Score"),
                    ScoreIcon = Find<UiImage>(t, "ScoreIcon")
                };

                Transform tag = t.Find("Tag");
                row.Tag = tag != null ? tag as RectTransform : null;
                row.Combat[0] = Find<TextMeshProUGUI>(t, "Kills");
                row.Combat[1] = Find<TextMeshProUGUI>(t, "Deaths");
                row.Combat[2] = Find<TextMeshProUGUI>(t, "Kd");
                return row;
            }

            private static T Find<T>(Transform parent, string child) where T : Component
            {
                Transform t = parent.Find(child);
                return t != null ? t.GetComponent<T>() : null;
            }
        }

        /// <summary>Installs the singleton. ⚠️ <b>Unconditional</b> — "is it needed this session"
        /// is <see cref="AppSingletons"/>'s call (rationale lives there).</summary>
        internal static void Install()
        {
            if (_instance != null)
            {
                return;
            }

            MatchResultOverlay prefab = LoadGeneric();
            if (prefab == null)
            {
                Debug.LogError($"[MatchResultOverlay] '{ResourcePath}' prefabı bulunamadı — maç " +
                               "sonu ekranı çizilemeyecek.");
                return;
            }

            Spawn(prefab);
        }

        private static MatchResultOverlay LoadGeneric()
        {
            return Resources.Load<MatchResultOverlay>(ResourcePath);
        }

        private static MatchResultOverlay Spawn(MatchResultOverlay prefab)
        {
            MatchResultOverlay overlay = Instantiate(prefab);
            overlay.name = "[MatchResultOverlay]";
            overlay._source = prefab;
            DontDestroyOnLoad(overlay.gameObject);
            _instance = overlay;
            return overlay;
        }

        /// <summary>The running mode's own screen (<c>ModeDefinition.ResultScreenPrefab</c>), else the
        /// generic one. Resolved at match end, when the mode is certain.</summary>
        private static MatchResultOverlay ScreenForRunningMode()
        {
            GameCatalog catalog = Admin.AdminContent.Catalog;
            ModeDefinition mode = catalog != null ? catalog.FindMode(ModeRuntime.ModeId) : null;
            GameObject custom = mode != null ? mode.ResultScreenPrefab : null;
            if (custom == null)
            {
                return LoadGeneric();
            }

            MatchResultOverlay screen = custom.GetComponent<MatchResultOverlay>();
            if (screen == null)
            {
                Debug.LogError($"[MatchResultOverlay] '{mode.ModeId}' modunun maç sonu ekranında " +
                               "MatchResultOverlay bileşeni yok — genel ekran çiziliyor.");
                return LoadGeneric();
            }

            return screen;
        }

        /// <summary>Replaces this instance with one spawned from <paramref name="prefab"/>. Roster and
        /// modeState are carried over: both arrive only on change, so the new instance would not hear
        /// them again before its screen opens.</summary>
        private MatchResultOverlay SwapTo(MatchResultOverlay prefab)
        {
            // Frees the slot so the new instance's Awake keeps itself instead of self-destructing.
            _instance = null;
            MatchResultOverlay next = Spawn(prefab);
            next._roster = _roster;
            next._modeState = _modeState;

            // Deactivate first: OnDisable unsubscribes now, not at end of frame, so this instance
            // cannot act on a later event (e.g. HideAll releasing the HUD gate) before it is gone.
            gameObject.SetActive(false);
            Destroy(gameObject);
            return next;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;

            // Before any material is read (the title tint instances fontMaterial): walls and the
            // blackout quad must not cover the result screen.
            UiDrawOnTop.Apply(gameObject, ResultSortingOffset);

            HudFollow follow = GetComponent<HudFollow>();
            if (follow != null)
            {
                // Drawing on top of a NEARER wall is a stereo depth conflict; pulling the panel in
                // front of the wall keeps depth consistent. Safe here — no head-locked child.
                follow.AvoidWalls = true;
            }

            // Don't carry a stale "hidden" state over from a Play entry without domain reload.
            HideAll();
        }

        private void OnEnable()
        {
            NetEvents.OnMatchEnd += HandleMatchEnd;
            NetEvents.OnMatchState += HandleMatchState;
            NetEvents.OnLoadMatch += HandleLoadMatch;
            NetEvents.OnReturnToLobby += HandleReturnToLobby;
            NetEvents.OnLobbyState += HandleLobbyState;
            NetEvents.OnDisconnected += HandleDisconnected;
        }

        private void OnDisable()
        {
            NetEvents.OnMatchEnd -= HandleMatchEnd;
            NetEvents.OnMatchState -= HandleMatchState;
            NetEvents.OnLoadMatch -= HandleLoadMatch;
            NetEvents.OnReturnToLobby -= HandleReturnToLobby;
            NetEvents.OnLobbyState -= HandleLobbyState;
            NetEvents.OnDisconnected -= HandleDisconnected;
        }

        private void OnDestroy()
        {
            if (_instance != this)
            {
                return;
            }

            // Don't leave HUDs hidden if this screen is being destroyed.
            GameplayHudGate.SetHidden(false);
            _instance = null;
        }

        private void Update()
        {
            if (_stage != Stage.Result || Time.unscaledTime < _scoreboardAt)
            {
                return;
            }

            ShowScoreboard();
        }

        // -------------------------------------------------------- net event handlers

        private void HandleMatchEnd(MatchEndMsg msg)
        {
            if (msg == null || AppSession.Role != AppSession.RolePlayer)
            {
                return;
            }

            MatchResultOverlay screen = ScreenForRunningMode();
            if (screen != null && screen != _source)
            {
                SwapTo(screen).HandleMatchEnd(msg);
                return;
            }

            _lastEnd = msg;
            ShowResult(msg);
        }

        /// <summary>Leaving the <c>finished</c> phase closes the screen: new match loaded, countdown
        /// started, operator paused, or returned to lobby (§10.1).</summary>
        private void HandleMatchState(MatchStateMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            if (msg.phase != ArenaProtocol.PHASE_FINISHED)
            {
                HideAll();
            }

            // AFTER HideAll: the line must survive into the result screen, and HideAll clears it.
            _modeState = msg.modeState ?? "";

            if (_stage == Stage.Scoreboard)
            {
                RefreshScoreboard();
            }
        }

        private void HandleLoadMatch(LoadMatchMsg _)
        {
            HideAll();
        }

        private void HandleReturnToLobby(ReturnToLobbyMsg _)
        {
            HideAll();
        }

        private void HandleDisconnected()
        {
            HideAll();
        }

        /// <summary>Table is fed by the roster (§10.2): counters are server-authoritative and
        /// participants (including <c>left</c> ones) stay listed for the whole <c>finished</c> phase.
        /// ⚠️ NO filtering by connection state — same rationale as the admin table: a removed player
        /// must still appear in the final table.</summary>
        private void HandleLobbyState(LobbyStateMsg msg)
        {
            _roster = msg?.players ?? Array.Empty<PlayerInfo>();

            if (_stage == Stage.Scoreboard)
            {
                RefreshScoreboard();
            }
        }

        // ------------------------------------------------------------------ display

        private void ShowResult(MatchEndMsg msg)
        {
            SetPanel(resultPanel, true);
            SetPanel(scoreboardPanel, false);

            if (ModeRuntime.IsCoop)
            {
                // ⚠️ Co-op has no opponent: the server ends it with no winner, which the generic
                // branch would read as "BERABERE" — a draw against nobody. The shift simply ends.
                SetText(resultTitleText, coopTitle);
                ApplyResultTone(coopColor);

                SetText(resultWinnerText, $"EKİP SKORU {msg.scoreRed}");
                SetScoreLine(CoopResultLines(), msg);
                RefreshCoopCard(msg);
            }
            else
            {
                bool draw;
                bool won = Won(msg, out draw);

                SetText(resultTitleText, draw ? drawTitle : won ? wonTitle : lostTitle);
                ApplyResultTone(draw ? drawColor : won ? wonColor : lostColor);

                SetText(resultWinnerText, WinnerLine(msg));
                SetScoreLine(ScoreLine(msg), msg);
            }

            _stage = Stage.Result;
            _scoreboardAt = Time.unscaledTime + Mathf.Max(0f, resultSeconds);
            GameplayHudGate.SetHidden(true);
        }

        /// <summary>
        /// Result word's tone (won / lost / draw). ⚠️ The word is a GRADIENT (white → tone), so the
        /// tone must not be written to <c>color</c>: that multiplies the gradient and the white top
        /// half would turn green/red too.
        /// ⚠️ With <see cref="tintTitleByOutcome"/> off the word is left ALONE, material included: a
        /// skin whose title is a painted ribbon would lose it to the instanced font material.
        /// </summary>
        private void ApplyResultTone(Color tone)
        {
            if (resultTitleText != null && tintTitleByOutcome)
            {
                resultTitleText.color = Color.white;
                resultTitleText.enableVertexGradient = true;
                resultTitleText.colorGradient =
                    new VertexGradient(Color.white, Color.white, tone, tone);

                // The halo (material Underlay) follows the tone too. Instance material: the shared
                // preset keeps the builder's colour.
                Color halo = tone;
                halo.a = 0.55f;
                resultTitleText.fontMaterial.SetColor(ShaderUtilities.ID_UnderlayColor, halo);
            }

            if (resultSlabGlow != null)
            {
                Color glow = tone;
                glow.a = 0.55f;
                resultSlabGlow.color = glow;
            }

            if (resultSlabUnderline != null)
            {
                resultSlabUnderline.color = tone;
            }

            Color fade = tone;
            fade.a = 0f;

            if (resultSlabRuleLeft != null)
            {
                resultSlabRuleLeft.Fill(fade, tone, UiGradientMode.Horizontal);
            }

            if (resultSlabRuleRight != null)
            {
                resultSlabRuleRight.Fill(tone, fade, UiGradientMode.Horizontal);
            }
        }

        /// <summary>Co-op result card's separate number targets (skin). The composite strings above
        /// stay written, so a prefab with none of these bound is unaffected.</summary>
        private void RefreshCoopCard(MatchEndMsg msg)
        {
            SetText(coopSharedScoreText, (msg != null ? msg.scoreRed : 0).ToString());

            Admin.AdminModeState.TryCustomerCounts(_modeState, out int happy, out int unhappy);
            SetText(coopHappyText, happy.ToString());
            SetText(coopUnhappyText, unhappy.ToString());

            PlayerInfo self = FindSelf();
            SetText(coopSelfScoreText, self != null ? self.score.ToString() : "0");

            if (coopRankText != null)
            {
                // The card opens before the scoreboard is ever refreshed, so the ranking is built
                // here too; it only reads the roster, which is already in.
                RankPlayers();
                coopRankText.text = RankLine();
            }
        }

        /// <summary>Local player's 1-based place in <see cref="_ranked"/>; equal scores share the
        /// better rank. "-" while the own id is unknown — an invented "1." would read as a win.</summary>
        private string RankLine()
        {
            int self = ArenaCombat.LocalPlayerId;
            int index = -1;
            for (int i = 0; self != 0 && i < _ranked.Count; i++)
            {
                if (_ranked[i].playerId == self)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                return "-";
            }

            while (index > 0 && _ranked[index - 1].score == _ranked[index].score)
            {
                index--;
            }

            return $"{index + 1}.";
        }

        /// <summary>Team-scored modes read the score off the plate (two team names + two numbers);
        /// everything else has a single line (own score, co-op summary) and no plate.</summary>
        private void SetScoreLine(string line, MatchEndMsg msg)
        {
            bool plate = HasTeamScore && resultScorebug != null;
            SetPanel(resultScorebug, plate);

            if (plate)
            {
                SetText(resultScoreRedText, (msg != null ? msg.scoreRed : 0).ToString());
                SetText(resultScoreBlueText, (msg != null ? msg.scoreBlue : 0).ToString());
            }

            if (resultScoreText != null)
            {
                SetPanel(resultScoreText.gameObject, !plate);
                resultScoreText.text = line;
            }
        }

        private static bool HasTeamScore =>
            ModeRuntime.Scoring == ModeScoreKind.Team && !ModeRuntime.IsTeamless;

        private void ShowScoreboard()
        {
            SetPanel(resultPanel, false);
            SetPanel(scoreboardPanel, true);

            _stage = Stage.Scoreboard;
            GameplayHudGate.SetHidden(true);
            RefreshScoreboard();
        }

        private void HideAll()
        {
            SetPanel(resultPanel, false);
            SetPanel(scoreboardPanel, false);

            _stage = Stage.Hidden;
            _lastEnd = null;
            _modeState = "";
            GameplayHudGate.SetHidden(false);
        }

        private static void SetPanel(GameObject panel, bool visible)
        {
            if (panel != null && panel.activeSelf != visible)
            {
                panel.SetActive(visible);
            }
        }

        // ----------------------------------------------------------------- scoreboard

        private void RefreshScoreboard()
        {
            RankPlayers();
            SplitBlocks();
            RefreshColumnLayout();
            RefreshHeadline();
            RefreshTeamSummary();
            RefreshMatchSummary();
            RefreshCoopBoard();
            RefreshTable();
        }

        /// <summary>Scoreboard head's co-op numbers (skin). Same null-safe contract as
        /// <see cref="RefreshCoopCard"/>.</summary>
        private void RefreshCoopBoard()
        {
            SetText(boardSharedScoreText, (_lastEnd != null ? _lastEnd.scoreRed : 0).ToString());

            Admin.AdminModeState.TryCustomerCounts(_modeState, out int happy, out int unhappy);
            SetText(boardHappyText, happy.ToString());
            SetText(boardUnhappyText, unhappy.ToString());

            PlayerInfo self = FindSelf();
            SetText(boardSelfScoreText, self != null ? self.score.ToString() : "0");
            SetText(boardRankText, RankLine());
        }

        /// <summary>
        /// Ordering matches <c>AdminStatsPanel</c>: in team modes roster order (<c>playerId</c>) is
        /// kept so a player always finds themselves in the same row; in FFA score is the only
        /// criterion, sorted DESCENDING (stable via <c>playerId</c> on ties).
        /// <para>Departed (<c>left</c>) rows follow the Counter-Strike rule (§10.2): in a
        /// player-scored mode they leave the table (deathmatch), in a team mode they stay, dimmed
        /// (competitive). The admin table is not filtered — the operator must see everyone.</para>
        /// </summary>
        private void RankPlayers()
        {
            _ranked.Clear();

            for (int i = 0; i < _roster.Length; i++)
            {
                PlayerInfo info = _roster[i];
                if (info == null || info.role == "admin")
                {
                    continue;
                }

                if (ModeRuntime.IsTeamless && info.connection == ArenaProtocol.CONNECTION_LEFT)
                {
                    continue;
                }

                _ranked.Add(info);
            }

            if (!ModeRuntime.IsTeamless && ModeRuntime.IsWeaponless)
            {
                // Weaponless team mode: teams stay grouped, but inside a team score is the only
                // ranking left — there are no kills to read the row order from.
                _ranked.Sort(CompareByTeamThenScore);
                return;
            }

            // Co-op has no teams to hold a row in place either: the list IS the contribution ranking.
            if (!ModeRuntime.IsTeamless && !ModeRuntime.IsCoop)
            {
                return;
            }

            _ranked.Sort(CompareByScoreDescending);
        }

        /// <summary>Which block each ranked player belongs to: team in team modes, otherwise the
        /// ranking is cut in half so both halves of the card are used instead of one long column.
        /// ⚠️ A player with no team lands in the LEFT block — never dropped, a missing name on the
        /// final table reads as "my score was not counted".</summary>
        private void SplitBlocks()
        {
            _blocks[0].Clear();
            _blocks[1].Clear();

            if (!ModeRuntime.IsTeamless)
            {
                for (int i = 0; i < _ranked.Count; i++)
                {
                    _blocks[_ranked[i].team == "blue" ? 1 : 0].Add(_ranked[i]);
                }

                return;
            }

            int half = (_ranked.Count + 1) / 2;
            for (int i = 0; i < _ranked.Count; i++)
            {
                _blocks[i < half ? 0 : 1].Add(_ranked[i]);
            }
        }

        /// <summary>
        /// Which columns this mode's table carries. Kill/death cells are dropped where they can only
        /// print zeros (co-op, weaponless), and co-op's score column is the shared total's
        /// contribution, so it is renamed.
        /// <para>⚠️ Visibility is restored on every refresh, never only switched off: the overlay is a
        /// persistent singleton, so a column hidden by the kids' mode would stay hidden for the next
        /// shooter match.</para>
        /// </summary>
        private void RefreshColumnLayout()
        {
            bool coop = ModeRuntime.IsCoop;
            bool hideCombat = ModeRuntime.HidesCombatStats;
            bool rows = boardRowTemplate != null;

            if (!rows)
            {
                for (int c = 0; c < ColumnOrder.Length; c++)
                {
                    bool visible = c == 1 ? !coop : c < 3 || !hideCombat;
                    SetColumnVisible(c, visible);
                }

                SetColumnHeader(2, coop ? coopScoreHeader : scoreHeader);
                return;
            }

            // ⚠️ Only the column TEXTS are switched off, never through SetColumnVisible: the wired
            // headers ARE the left block's header cells, so hiding "a column" would strip the plate.
            for (int c = 0; boardColumns != null && c < boardColumns.Length; c++)
            {
                if (boardColumns[c] != null)
                {
                    SetPanel(boardColumns[c].gameObject, false);
                }
            }

            for (int b = 0; b < BlockCount; b++)
            {
                if (boardBlockScoreHeaders != null && b < boardBlockScoreHeaders.Length &&
                    boardBlockScoreHeaders[b] != null)
                {
                    boardBlockScoreHeaders[b].text = coop ? coopScoreHeader : scoreHeader;
                }
            }

            if (boardBlockCombatCells != null)
            {
                for (int i = 0; i < boardBlockCombatCells.Length; i++)
                {
                    SetPanel(boardBlockCombatCells[i], !hideCombat);
                }
            }
        }

        private void SetColumnVisible(int column, bool visible)
        {
            GameObject root = boardColumnRoots != null && column < boardColumnRoots.Length
                ? boardColumnRoots[column]
                : null;

            if (root != null)
            {
                SetPanel(root, visible);
                return;
            }

            // No column root wired: hide what we can reach — the values and, if it is a text header,
            // the header. An icon header left over is a cosmetic leftover, not a wrong number.
            if (boardColumns != null && column < boardColumns.Length && boardColumns[column] != null)
            {
                SetPanel(boardColumns[column].gameObject, visible);
            }

            TextMeshProUGUI header = HeaderOf(column);
            if (header != null)
            {
                SetPanel(header.gameObject, visible);
            }
        }

        private void SetColumnHeader(int column, string label)
        {
            TextMeshProUGUI header = HeaderOf(column);
            if (header != null && header.text != label)
            {
                header.text = label;
            }
        }

        private TextMeshProUGUI HeaderOf(int column)
        {
            return boardColumnHeaders != null && column < boardColumnHeaders.Length
                ? boardColumnHeaders[column]
                : null;
        }

        private static int CompareByTeamThenScore(PlayerInfo a, PlayerInfo b)
        {
            int byTeam = TeamOrder(a.team).CompareTo(TeamOrder(b.team));
            return byTeam != 0 ? byTeam : CompareByScoreDescending(a, b);
        }

        /// <summary>Red · blue · unassigned — the same left-to-right order the whole UI uses.</summary>
        private static int TeamOrder(string team)
        {
            return team == "red" ? 0 : team == "blue" ? 1 : 2;
        }

        private static int CompareByScoreDescending(PlayerInfo a, PlayerInfo b)
        {
            int byScore = b.score.CompareTo(a.score);
            return byScore != 0 ? byScore : a.playerId.CompareTo(b.playerId);
        }

        private void RefreshTable()
        {
            if (boardRowTemplate != null)
            {
                RefreshBlocks();
                return;
            }

            RefreshColumnTexts();
        }

        // --------------------------------------------------------------- row blocks

        private void RefreshBlocks()
        {
            bool teamless = ModeRuntime.IsTeamless;
            bool hideCombat = ModeRuntime.HidesCombatStats;
            int self = ArenaCombat.LocalPlayerId;

            for (int b = 0; b < BlockCount; b++)
            {
                List<PlayerInfo> list = _blocks[b];

                // The right block is the blue team (or the ranking's second half) — with nobody in it
                // an empty team plate would read as "the blue team lost everyone".
                SetPanel(BlockOf(b), list.Count > 0);

                string team = teamless ? "" : b == 0 ? "red" : "blue";
                if (boardBlockPlates != null && b < boardBlockPlates.Length &&
                    boardBlockPlates[b] != null)
                {
                    boardBlockPlates[b].Fill(Girdap.TeamHi(team), Girdap.TeamLo(team),
                        UiGradientMode.Horizontal, 0.46f);
                }

                if (boardBlockTitles != null && b < boardBlockTitles.Length &&
                    boardBlockTitles[b] != null)
                {
                    boardBlockTitles[b].text = teamless
                        ? b == 0 ? soloBlockTitle : ""
                        : b == 0 ? "KIRMIZI TAKIM" : "MAVİ TAKIM";
                }

                // Teamless blocks are two halves of ONE ranking: numbering continues on the right.

                int rankBase = teamless && b > 0 ? _blocks[0].Count : 0;

                for (int i = 0; i < list.Count; i++)
                {
                    Row row = RowAt(b, i);
                    if (row != null)
                    {
                        FillRow(row, list[i], rankBase + i + 1, self, hideCombat);
                    }
                }

                // Pooled leftovers stay alive but hidden: the next match may need them again.
                for (int i = list.Count; i < _rows[b].Count; i++)
                {
                    SetPanel(_rows[b][i].Go, false);
                }
            }
        }

        private GameObject BlockOf(int block)
        {
            return boardBlocks != null && block < boardBlocks.Length ? boardBlocks[block] : null;
        }

        private Row RowAt(int block, int index)
        {
            if (boardRowTemplate == null || boardRowParents == null ||
                block >= boardRowParents.Length || boardRowParents[block] == null)
            {
                return null;
            }

            List<Row> pool = _rows[block];
            while (pool.Count <= index)
            {
                GameObject clone = Instantiate(boardRowTemplate, boardRowParents[block]);
                clone.name = $"Row{pool.Count}";

                var rt = (RectTransform)clone.transform;
                rt.anchoredPosition = new Vector2(0f, -(pool.Count * (rowHeight + rowGap)));
                pool.Add(Row.Bind(clone));
            }

            return pool[index];
        }

        private void FillRow(Row row, PlayerInfo info, int rank, int selfId, bool hideCombat)
        {
            bool mine = selfId != 0 && info.playerId == selfId;
            bool left = info.connection == ArenaProtocol.CONNECTION_LEFT;

            SetPanel(row.Go, true);

            if (row.Group != null)
            {
                // Departed row: dimmed as a whole — the text stand-in for Counter-Strike's
                // disconnected icon is the "· ayrıldı" suffix below.
                row.Group.alpha = left ? leftRowAlpha : 1f;
            }

            if (row.Bg != null)
            {
                // ⚠️ The normal style is REPAINTED, not just skipped: rows are pooled, so last
                // match's own row would stay highlighted for whoever lands on it next.
                if (mine)
                {
                    row.Bg.Fill(ownRowFillA, ownRowFillB, UiGradientMode.Horizontal)
                        .Outline(ownRowOutlineWidth, ownRowOutline)
                        .InnerGlow(ownRowGlowWidth, ownRowGlow);
                }
                else
                {
                    row.Bg.Fill(rowFillA, rowFillB, UiGradientMode.Horizontal)
                        .Outline(rowOutlineWidth, rowOutline)
                        .InnerGlow(0f, ownRowGlow);
                }
            }

            Color faint = mine ? ownRowSmallColor : rowSmallColor;
            Color lead = mine ? ownRowTextColor : rowTextColor;
            Color sub = mine ? Girdap.AccHi : Girdap.Muted;

            SetCell(row.Rank, rank.ToString(), mine ? ownRowRankColor : rowRankColor);
            SetCell(row.Name, SafeName(info.name), lead);
            SetCell(row.Small, left ? $"#{info.playerId} · ayrıldı" : $"#{info.playerId}", faint);
            SetCell(row.Score, RawCellText(info, 2), lead);

            for (int c = 0; c < row.Combat.Length; c++)
            {
                SetCell(row.Combat[c], RawCellText(info, c + 3), sub);
                SetPanel(row.Combat[c] != null ? row.Combat[c].gameObject : null, !hideCombat);
            }

            if (row.Tag != null)
            {
                row.Tag.gameObject.SetActive(mine);
            }

            if (row.ScoreIcon != null)
            {
                // A burger icon next to a 0 reads as "one burger, zero points".
                row.ScoreIcon.gameObject.SetActive(info.score > 0);
            }

            LayoutNameCell(row, mine);
        }

        /// <summary>
        /// Name · "SEN" badge · "#id" sit in one row, so the badge and the id start where the name
        /// ends. ⚠️ Measured here instead of with a Layout Group: this screen is built on fixed
        /// anchors (repo-wide rule), and the width only changes when the table is refreshed.
        /// </summary>
        private static void LayoutNameCell(Row row, bool mine)
        {
            if (row.Name == null)
            {
                return;
            }

            float x = NameCellX + Mathf.Ceil(row.Name.GetPreferredValues(row.Name.text).x) + NameGap;

            if (mine && row.Tag != null)
            {
                row.Tag.anchoredPosition = new Vector2(x, row.Tag.anchoredPosition.y);
                x += row.Tag.sizeDelta.x + NameGap;
            }

            if (row.Small != null)
            {
                RectTransform rt = row.Small.rectTransform;
                rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);
            }
        }

        private static void SetCell(TextMeshProUGUI cell, string text, Color color)
        {
            if (cell == null)
            {
                return;
            }

            cell.text = text;
            cell.color = color;
        }

        // ------------------------------------------------------------ column texts

        /// <summary>Legacy <c>\n</c>-joined table, kept for variants skinned around it
        /// (<see cref="boardRowTemplate"/> empty). Columns are separate TMP objects joined by
        /// <c>\n</c>: TMP's default font is NOT monospaced, so space-aligned columns in one text
        /// block would drift.</summary>
        private void RefreshColumnTexts()
        {
            if (boardColumns == null)
            {
                return;
            }

            for (int c = 0; c < boardColumns.Length; c++)
            {
                if (boardColumns[c] == null)
                {
                    continue;
                }

                _sb.Clear();

                for (int i = 0; i < _ranked.Count; i++)
                {
                    if (i > 0)
                    {
                        _sb.AppendLine();
                    }

                    _sb.Append(CellText(_ranked[i], c));
                }

                boardColumns[c].text = _sb.ToString();
            }
        }

        /// <summary>Cell texts use the same format as their <c>AdminStatsRow</c> counterparts — the
        /// same player must not look different across the two tables. ⚠️ Not extracted into a shared
        /// helper: this class reads <c>PlayerInfo</c> (wire DTO) while the admin row reads
        /// <c>AdminPlayerView</c> (client mirror); a shared signature would cut one of them off from
        /// its natural source.</summary>
        private string CellText(PlayerInfo info, int column)
        {
            string text = RawCellText(info, column);
            if (info.connection != ArenaProtocol.CONNECTION_LEFT)
            {
                return text;
            }

            return column == 0
                ? $"<alpha=#66>{text} · ayrıldı<alpha=#FF>"
                : $"<alpha=#66>{text}<alpha=#FF>";
        }

        private string RawCellText(PlayerInfo info, int column)
        {
            switch (column)
            {
                // Rich text is ON for the columns (departed-row alpha tags) → a name must never open
                // a tag; '<' becomes a look-alike instead of being parsed.
                case 0: return $"{SafeName(info.name)} #{info.playerId}";
                // ⚠️ Column is NoWrap: a wrapped cell would desync the \n-joined rows.
                case 1: return info.team == "red" ? $"{Paint("Kırmızı", redTeamColor)} Takım"
                             : info.team == "blue" ? $"{Paint("Mavi", blueTeamColor)} Takım" : "-";
                case 2: return info.score.ToString();
                case 3: return info.kills.ToString();
                case 4: return info.deaths.ToString();
                case 5: return info.deaths > 0
                    ? (info.kills / (float)info.deaths).ToString("0.00")
                    : info.kills.ToString("0.00");
                // Columns wired in the prefab beyond ColumnOrder stay blank.
                default: return "";
            }
        }

        /// <summary>Card's headline: who won. The score itself lives on the plate next to it in
        /// team-scored modes; where there is no plate the headline carries the score part too (the
        /// leader in FFA, the shared total in co-op).</summary>
        private void RefreshHeadline()
        {
            bool plate = HasTeamScore && boardScorebug != null;
            SetPanel(boardScorebug, plate);

            if (plate)
            {
                SetText(boardScoreRedText, (_lastEnd != null ? _lastEnd.scoreRed : 0).ToString());
                SetText(boardScoreBlueText, (_lastEnd != null ? _lastEnd.scoreBlue : 0).ToString());
            }

            string winner = _lastEnd != null ? WinnerLine(_lastEnd) : "";

            if (plate)
            {
                // No winner next to a visible score plate would leave the line blank, and a blank
                // line next to "14 — 14" reads as a missing result rather than a draw.
                SetText(boardHeadlineText, winner.Length > 0 ? winner : drawTitle);
                return;
            }

            string score = SummaryScoreLine();
            if (winner.Length == 0)
            {
                SetText(boardHeadlineText, score);
                return;
            }

            SetText(boardHeadlineText, score.Length == 0 ? winner : $"{winner}   ·   {score}");
        }

        private string SummaryScoreLine()
        {
            // ⚠️ BEFORE the teamless branch: co-op is teamless too, but it has no leader — the shared
            // total is the only score, and a "LİDER" line would turn a co-op shift into a contest.
            if (ModeRuntime.IsCoop)
            {
                return _lastEnd != null ? $"EKİP SKORU {_lastEnd.scoreRed}" : "EKİP SKORU";
            }

            if (ModeRuntime.IsTeamless)
            {
                // No teams → the only meaningful headline is the leader. With no score yet
                // (match never started) we invent nothing.
                return _ranked.Count > 0 && _ranked[0].score > 0
                    ? $"LİDER: {SafeName(_ranked[0].name)} {_ranked[0].score}"
                    : "HERKES TEK";
            }

            return _lastEnd != null ? TeamScoreLine(_lastEnd) : "";
        }

        /// <summary>Per-block summary line under the rows. Its y follows the row count: the line sits
        /// right under the last row, as in the mockup, not at a fixed card position.</summary>
        private void RefreshTeamSummary()
        {
            for (int b = 0; b < BlockCount; b++)
            {
                TextMeshProUGUI target = b == 0 ? boardTeamSummaryText : boardTeamSummary2Text;
                if (target == null)
                {
                    continue;
                }

                List<PlayerInfo> list = _blocks[b];
                SetPanel(target.gameObject, list.Count > 0);
                target.text = BlockSummary(list, b);

                if (boardRowTemplate != null)
                {
                    RectTransform rt = target.rectTransform;
                    rt.anchoredPosition = new Vector2(rt.anchoredPosition.x,
                        -(rowsTop + list.Count * (rowHeight + rowGap) + 4f));
                }
            }
        }

        private string BlockSummary(List<PlayerInfo> list, int block)
        {
            if (list.Count == 0)
            {
                return "";
            }

            if (ModeRuntime.IsCoop)
            {
                if (!string.IsNullOrEmpty(coopBlockSummaryFormat))
                {
                    int contribution = 0;
                    for (int i = 0; i < list.Count; i++)
                    {
                        contribution += list[i].score;
                    }

                    return string.Format(coopBlockSummaryFormat, list.Count, contribution,
                        contribution / 10);
                }

                // Neither "canlı" nor kills belong here: nobody dies and nobody shoots in a co-op
                // shift — the customers are the only thing that happened.
                string customers = CustomerLine();
                return customers.Length > 0
                    ? $"{list.Count} oyuncu · {customers}"
                    : $"{list.Count} oyuncu";
            }

            if (ModeRuntime.IsWeaponless)
            {
                // Weaponless: the line is players + team points; kill totals would be zeros the
                // players have to learn to ignore.
                int score = _lastEnd == null ? 0 : block == 0 ? _lastEnd.scoreRed : _lastEnd.scoreBlue;
                return ModeRuntime.IsTeamless
                    ? $"{list.Count} oyuncu"
                    : $"{list.Count} oyuncu · TAKIM SKORU: {score}";
            }

            int kills = 0;
            int deaths = 0;
            for (int i = 0; i < list.Count; i++)
            {
                kills += list[i].kills;
                deaths += list[i].deaths;
            }

            return $"{list.Count} oyuncu · {kills} öldürme · {deaths} ölüm";
        }

        /// <summary>Card's bottom band: the player's own numbers. The four combat boxes are only
        /// filled where kills exist; a five-box skin carries the co-op set instead, and a mode with
        /// neither falls back to the single summary line.</summary>
        private void RefreshMatchSummary()
        {
            PlayerInfo self = FindSelf();
            int cells = footStatValues != null ? footStatValues.Length : 0;
            bool coopBoxes = self != null && ModeRuntime.IsCoop && cells >= 5;
            bool boxes = coopBoxes ||
                         (self != null && !ModeRuntime.HidesCombatStats && cells >= 4);

            SetPanel(footStatsGroup, boxes);

            if (coopBoxes)
            {
                // KATKI · SIRA · MUTLU · MUTSUZ · EKİP SKORU (labels are static in the prefab).
                Admin.AdminModeState.TryCustomerCounts(_modeState, out int happy, out int unhappy);
                SetText(footStatValues[0], self.score.ToString());
                SetText(footStatValues[1], RankLine());
                SetText(footStatValues[2], happy.ToString());
                SetText(footStatValues[3], unhappy.ToString());
                SetText(footStatValues[4], (_lastEnd != null ? _lastEnd.scoreRed : 0).ToString());
            }
            else if (boxes)
            {
                SetText(footStatValues[0], self.kills.ToString());
                SetText(footStatValues[1], self.deaths.ToString());
                SetText(footStatValues[2], RawCellText(self, 5));
                SetText(footStatValues[3], self.score.ToString());
            }

            if (footWhoPlate != null)
            {
                string team = self != null ? self.team : "";
                footWhoPlate.Fill(Girdap.TeamHi(team), Girdap.TeamLo(team), UiGradientMode.Vertical);
            }

            if (boardMatchSummaryText == null)
            {
                return;
            }

            SetPanel(boardMatchSummaryText.gameObject, !boxes);
            boardMatchSummaryText.text = self == null
                ? ""
                : ModeRuntime.IsCoop
                    ? $"SKOR: {self.score}"
                    : ModeRuntime.IsWeaponless
                        ? $"BİREYSEL SKOR: {self.score}"
                        : $"SEN: {self.kills} öldürme · {self.deaths} ölüm · K/D {CellText(self, 5)}";
        }

        // ----------------------------------------------------------------------- text

        /// <summary>Did the local player win. The winner arrives on two channels (§5.3):
        /// <c>winnerTeam</c> for team-scored modes, <c>winnerPlayerId</c> for player-scored ones;
        /// both empty = draw.</summary>
        private static bool Won(MatchEndMsg msg, out bool draw)
        {
            if (msg.winnerTeam == "red" || msg.winnerTeam == "blue")
            {
                draw = false;
                Team local = ArenaCombat.LocalTeam;
                return (msg.winnerTeam == "red" && local == Team.Red) ||
                       (msg.winnerTeam == "blue" && local == Team.Blue);
            }

            if (msg.winnerPlayerId > 0)
            {
                draw = false;
                int self = ArenaCombat.LocalPlayerId;
                return self != 0 && msg.winnerPlayerId == self;
            }

            draw = true;
            return false;
        }

        private string WinnerLine(MatchEndMsg msg)
        {
            if (msg.winnerTeam == "red")
            {
                return $"{RedTeam} KAZANDI";
            }

            if (msg.winnerTeam == "blue")
            {
                return $"{BlueTeam} KAZANDI";
            }

            return msg.winnerPlayerId > 0 ? $"{NameOf(msg.winnerPlayerId)} KAZANDI" : "";
        }

        /// <summary>Result card's score line, used where there is no team score plate: the player's
        /// own score in player-scored modes (<c>scoreRed</c>/<c>scoreBlue</c> are always 0 there,
        /// §10.2). ⚠️ Co-op never reaches here — it has its own card
        /// (<see cref="CoopResultLines"/>), because the team line would read "KIRMIZI TAKIM n — 0 MAVİ TAKIM".</summary>
        private string ScoreLine(MatchEndMsg msg)
        {
            if (HasTeamScore)
            {
                return TeamScoreLine(msg);
            }

            PlayerInfo self = FindSelf();
            return self != null ? $"SENİN SKORUN {self.score}" : "";
        }

        private string TeamScoreLine(MatchEndMsg msg) => $"{RedTeam} {msg.scoreRed} — {msg.scoreBlue} {BlueTeam}";

        // Only the colour word is painted; "TAKIM" keeps the text's own colour.
        private string RedTeam => $"{Paint("KIRMIZI", redTeamColor)} TAKIM";
        private string BlueTeam => $"{Paint("MAVİ", blueTeamColor)} TAKIM";

        /// <summary>Rich text is on for the texts carrying team colours → a player name must never
        /// open a tag; '&lt;' becomes a look-alike instead of being parsed.</summary>
        private static string SafeName(string name) => name?.Replace('<', '‹');

        /// <summary>Team-coloured span; the prefab enables rich text on the texts that carry it.</summary>
        private static string Paint(string text, Color color) =>
            $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{text}</color>";

        /// <summary>Co-op result card body: customer counters (when the mode publishes them) + the
        /// player's own contribution. Same wording as the in-match HUD — the numbers must not be
        /// renamed between the match and its result screen.</summary>
        private string CoopResultLines()
        {
            _sb.Clear();

            string customers = CustomerLine();
            if (customers.Length > 0)
            {
                _sb.AppendLine(customers);
            }

            PlayerInfo self = FindSelf();
            if (self != null)
            {
                _sb.Append($"Skor: {self.score}");
            }

            return _sb.ToString();
        }

        /// <summary>"Mutlu müşteri h · Mutsuz müşteri u", or empty when the running mode publishes no counters.
        /// ⚠️ Parsing is NOT duplicated here: <see cref="Admin.AdminModeState"/> is the single parser,
        /// so the operator and the player can never read two different numbers.</summary>
        private string CustomerLine()
        {
            return Admin.AdminModeState.TryCustomerCounts(_modeState, out int happy, out int unhappy)
                ? $"Mutlu müşteri {happy} · Mutsuz müşteri {unhappy}"
                : "";
        }

        private PlayerInfo FindSelf()
        {
            int self = ArenaCombat.LocalPlayerId;
            if (self == 0)
            {
                return null;
            }

            for (int i = 0; i < _roster.Length; i++)
            {
                if (_roster[i] != null && _roster[i].playerId == self)
                {
                    return _roster[i];
                }
            }

            return null;
        }

        /// <summary>playerId → name (from roster); falls back to a generic label.</summary>
        private string NameOf(int playerId)
        {
            for (int i = 0; i < _roster.Length; i++)
            {
                if (_roster[i] != null && _roster[i].playerId == playerId &&
                    !string.IsNullOrEmpty(_roster[i].name))
                {
                    return SafeName(_roster[i].name);
                }
            }

            return $"Oyuncu {playerId}";
        }

        private static void SetText(TextMeshProUGUI target, string value)
        {
            if (target != null)
            {
                target.text = value;
            }
        }
    }
}
