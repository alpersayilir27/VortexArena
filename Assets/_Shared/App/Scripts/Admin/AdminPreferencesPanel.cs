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
    /// seçimi, başlat/iptal/lobiye dön), <b>görünüm tercihleri</b> ve <b>bağlantı</b>.
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
    /// notu" değil anlık bir sahne komutudur. Bunun doğrudan sonucu: <b>mod/harita yalnız faz
    /// <c>Lobby</c> iken değiştirilebilir</b> (<see cref="CanChangeSelection"/>); maç sürerken
    /// satırlar pasifleşir. Süre/limit her fazda açık kalır, onlar sahne yüklemez.
    /// </para>
    /// <para>
    /// <b>Neden dropdown/slider yok:</b> <c>TMP_Dropdown</c> ve <c>Slider</c> serialize edilmiş
    /// şablon hiyerarşisi ister (viewport, item template, handle); prosedürel kurulumda bu hem
    /// uzun hem kırılgandır. Yerine <c>[&lt;] değer [&gt;]</c> döngüleyicileri ve
    /// <c>[-] değer [+]</c> adımlayıcıları kullanılır: operatör için daha az hatalı, kod için
    /// çok daha az yüzey.
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
        /// Kabaca hesap: başlangıç 78 + her Section 34 + her Cycler 40 + maç düğmeleri 50.
        /// <para>⚠️ 1000 px, 1080p bir ekranda üstten/alttan ~40 px pay bırakır. Bir bölüm daha
        /// eklenecekse panel yükseltilemez — o noktada içeriğin kaydırılabilir olması gerekir.</para></summary>
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

        private GameObject _root;
        private TextMeshProUGUI _modeValue;
        private TextMeshProUGUI _mapValue;

        // MAÇ bölümünün kilitlenebilir parçaları (§10.7): harita seçmek TÜM istemcilere sahne
        // yükletiyor, bu yüzden maç sürerken bu dört düğme pasifleşir ve başlık sebebini yazar.
        private TextMeshProUGUI _matchSectionLabel;
        private Button _modePrev;
        private Button _modeNext;
        private Button _mapPrev;
        private Button _mapNext;

        private TextMeshProUGUI _durationValue;
        private TextMeshProUGUI _scoreLimitValue;
        private TextMeshProUGUI _statusText;
        private TextMeshProUGUI _clearAllLabel;

        /// <summary>Toplu kalibrasyon sıfırlamanın onay penceresi (sn) — AdminPlayerRow ile aynı
        /// gerekçe: herkesi savaş dışı bırakan bir eylem tek yanlış tıklamayla olmamalı.</summary>
        private const float ClearAllConfirmSeconds = 3f;
        private float _clearAllArmedAt = -1f;
        private TextMeshProUGUI _connectionText;

        // ---- Oyuncu kimliği (§2): SEÇİLİ oyuncunun adı + forma numarası ----
        // Hedef ayrı bir seçiciyle DEĞİL AdminSession.SelectedPlayerId ile belirlenir: satıra
        // tıklamak zaten seçim jestidir, ikinci bir liste iki ayrı "seçili oyuncu" kavramı üretirdi.
        private TMP_InputField _identityNameField;
        private TextMeshProUGUI _identityNumberValue;
        private TextMeshProUGUI _identityTargetText;

        /// <summary>Alanların hangi oyuncu için doldurulduğu — seçim değişince yeniden doldurulur.
        /// Operatör yazarken roster tazelenip yazdığını EZMESİN diye aynı oyuncuda tekrar dolmaz.</summary>
        private int _identityBoundPlayerId;
        private int _identityNumber;

        private TextMeshProUGUI _markersValue;
        private TextMeshProUGUI _nameplatesValue;
        private TextMeshProUGUI _speedValue;
        private TextMeshProUGUI _wallValue;
        private TextMeshProUGUI _roofValue;

        private readonly List<ModeDefinition> _modes = new List<ModeDefinition>();
        private readonly List<MapDefinition> _maps = new List<MapDefinition>();
        private int _modeIndex;
        private int _mapIndex;

        /// <summary>Bir sonraki maçın süresi/limiti (ORTAK — set_selection ile gider).
        /// Mod değişince o modun <see cref="ModeDefinition"/> varsayılanına döner.</summary>
        private int _roundSeconds;
        private int _scoreLimit;

        private bool _dirty = true;

        public void Initialize(RectTransform parent)
        {
            Build(parent);
            AdminContent.CollectModes(_modes);
            RefreshMapList();
            ResetMatchParametersToModeDefaults();
            Apply();
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

        // ------------------------------------------------------------------ kurulum

        private void Build(RectTransform parent)
        {
            Image card = UiKit.Panel(parent, "PreferencesPanel", UiKit.CardTranslucent, UiKit.Border);
            _root = card.transform.parent.gameObject;
            card.raycastTarget = true; // panel arkasındaki HUD düğmelerine tıklama sızmasın
            UiKit.Center((RectTransform)_root.transform, new Vector2(PanelWidth, PanelHeight));

            Transform body = card.transform;

            TextMeshProUGUI title = UiKit.Text(body, "Title", 30f, UiKit.Title, FontStyles.Bold,
                TextAlignmentOptions.TopLeft);
            UiKit.Block(title.rectTransform, 28f, 22f, 90f, 38f);
            title.text = "TERCİHLER";
            title.characterSpacing = 3f;

            Button close = UiKit.Button(body, "Close", "KAPAT", 18f, UiKit.Hex(0x2A303B, 0xFF),
                UiKit.Muted, AdminSession.ClosePanel, out _);
            UiKit.Corner((RectTransform)close.transform, new Vector2(1f, 1f),
                new Vector2(-24f, -24f), new Vector2(110f, 34f));

            float y = 78f;

            // "ortak" etiketi bilinçli: bu iki seçici tüm adminlerde aynı anda değişir (§5.3).
            y = Section(body, MatchSectionTitle, y, out _matchSectionLabel);
            y = Cycler(body, "Mod", y, CycleModePrev, CycleModeNext, out _modeValue,
                out _modePrev, out _modeNext);
            y = Cycler(body, "Harita", y, CycleMapPrev, CycleMapNext, out _mapValue,
                out _mapPrev, out _mapNext);
            y = Cycler(body, "Süre", y, DurationPrev, DurationNext, out _durationValue);
            y = Cycler(body, "Skor limiti", y, ScoreLimitDown, ScoreLimitUp, out _scoreLimitValue);
            y = MatchButtons(body, y);

            _statusText = UiKit.Text(body, "Status", 18f, UiKit.Accent, FontStyles.Normal,
                TextAlignmentOptions.TopLeft);
            UiKit.Block(_statusText.rectTransform, 28f, y, 28f, 24f);
            y += 34f;

            // §10.6 — toplu sıfırlama. Tek oyuncunun kalibrasyonu kendi satırındaki KAL
            // düğmesinden sıfırlanır; buradaki düğme maç öncesi "hepsini yeniden aldır" içindir.
            y = Section(body, "KALİBRASYON (TÜM ADMİNLERDE ORTAK)", y);

            Button clearAll = UiKit.Button(body, "ClearAllCalibration", "TÜM KALİBRASYONLARI SIFIRLA",
                18f, UiKit.Hex(0x2A303B, 0xFF), UiKit.Bad, ArmClearAllCalibration, out _clearAllLabel);
            UiKit.Corner((RectTransform)clearAll.transform, new Vector2(0f, 1f),
                new Vector2(28f, -y), new Vector2(340f, 36f));
            y += 46f;

            // §2 — ad + forma numarası. Hedef, listede SEÇİLİ olan oyuncudur.
            y = Section(body, "OYUNCU KİMLİĞİ (SEÇİLİ OYUNCU)", y);

            _identityTargetText = UiKit.Text(body, "IdentityTarget", 18f, UiKit.Muted,
                FontStyles.Normal, TextAlignmentOptions.TopLeft);
            UiKit.Block(_identityTargetText.rectTransform, 28f, y + 6f, 300f, 24f);

            _identityNameField = UiKit.Input(body, "IdentityName", "ad", 20f, 24, null);
            UiKit.Corner((RectTransform)_identityNameField.transform, new Vector2(1f, 1f),
                new Vector2(-28f, -y), new Vector2(266f, 32f));
            y += RowHeight;

            // Numara döngüleyicisi + UYGULA aynı satırda: döngüleyici sağ kenardan sabitlendiği
            // için (Cycler ile birebir aynı Corner değerleri) solda 240 px boş kalıyor. Ayrı bir
            // satır açmak paneli taşırırdı — PanelHeight notu.
            Button applyIdentity = UiKit.Button(body, "ApplyIdentity", "KİMLİĞİ UYGULA", 18f,
                UiKit.Accent, UiKit.OnAccent, ApplyIdentity, out _);
            UiKit.Corner((RectTransform)applyIdentity.transform, new Vector2(0f, 1f),
                new Vector2(28f, -y), new Vector2(240f, 32f));

            Button numberPrev = UiKit.Button(body, "Prev_Numara", "<", 20f, UiKit.Hex(0x2A303B, 0xFF),
                UiKit.Title, IdentityNumberDown, out _);
            UiKit.Corner((RectTransform)numberPrev.transform, new Vector2(1f, 1f),
                new Vector2(-238f, -y), new Vector2(32f, 32f));

            _identityNumberValue = UiKit.Text(body, "Value_Numara", 20f, UiKit.Title, FontStyles.Bold,
                TextAlignmentOptions.Center);
            UiKit.Corner(_identityNumberValue.rectTransform, new Vector2(1f, 1f),
                new Vector2(-64f, -(y + 3f)), new Vector2(170f, 26f));

            Button numberNext = UiKit.Button(body, "Next_Numara", ">", 20f, UiKit.Hex(0x2A303B, 0xFF),
                UiKit.Title, IdentityNumberUp, out _);
            UiKit.Corner((RectTransform)numberNext.transform, new Vector2(1f, 1f),
                new Vector2(-28f, -y), new Vector2(32f, 32f));

            y += RowHeight;

            y = Section(body, "GÖRÜNÜM (YALNIZ BU EKRAN)", y);
            y = Cycler(body, "Halkalar", y, PrevMarkers, NextMarkers, out _markersValue);
            y = Cycler(body, "Ad etiketleri", y, ToggleNameplates, ToggleNameplates, out _nameplatesValue);
            y = Cycler(body, "Kamera hızı", y, SpeedDown, SpeedUp, out _speedValue);
            y = Cycler(body, "Duvar saydamlığı", y, WallDown, WallUp, out _wallValue);
            y = Cycler(body, "Çatı", y, PrevRoof, NextRoof, out _roofValue);

            y = Section(body, "BAĞLANTI", y);

            _connectionText = UiKit.Text(body, "Connection", 18f, UiKit.Muted, FontStyles.Normal,
                TextAlignmentOptions.TopLeft);
            UiKit.Block(_connectionText.rectTransform, 28f, y, 28f, 24f);
            y += 30f;

            Button reconnect = UiKit.Button(body, "Reconnect", "YENİDEN BAĞLAN", 18f,
                UiKit.Accent, UiKit.OnAccent, AdminCommands.Reconnect, out _);
            UiKit.Corner((RectTransform)reconnect.transform, new Vector2(0f, 1f),
                new Vector2(28f, -y), new Vector2(230f, 36f));

            Button disconnect = UiKit.Button(body, "Disconnect", "BAĞLANTIYI KES", 18f,
                UiKit.Hex(0x2A303B, 0xFF), UiKit.Muted, AdminCommands.Disconnect, out _);
            UiKit.Corner((RectTransform)disconnect.transform, new Vector2(0f, 1f),
                new Vector2(268f, -y), new Vector2(230f, 36f));

            _root.SetActive(false);
        }

        private static float Section(Transform body, string label, float y)
        {
            return Section(body, label, y, out _);
        }

        /// <summary>Başlığı sonradan değiştirebilmek için metnini de veren biçim (MAÇ bölümü
        /// kilitlendiğinde sebebini yazıyor).</summary>
        private static float Section(Transform body, string label, float y, out TextMeshProUGUI text)
        {
            text = UiKit.Text(body, $"Section_{label}", 18f, UiKit.Faint,
                FontStyles.Bold, TextAlignmentOptions.TopLeft);
            UiKit.Block(text.rectTransform, 28f, y, 28f, 22f);
            text.text = label;
            text.characterSpacing = 4f;

            Image divider = UiKit.Solid(body, $"Divider_{label}", UiKit.Border);
            UiKit.Block(divider.rectTransform, 28f, y + 24f, 28f, 1f);

            return y + 34f;
        }

        /// <summary>
        /// `Etiket  [&lt;] değer [&gt;]` satırı. Düğmeler sağ kenardan sabitlenir
        /// (<see cref="UiKit.Corner"/>): panel genişliği değişse de hizalama bozulmaz.
        /// </summary>
        private static float Cycler(Transform body, string label, float y,
            UnityEngine.Events.UnityAction onPrev, UnityEngine.Events.UnityAction onNext,
            out TextMeshProUGUI value)
        {
            return Cycler(body, label, y, onPrev, onNext, out value, out _, out _);
        }

        /// <summary>Düğmeleri de veren biçim — sonradan <c>interactable</c> çevirebilmek için
        /// (yalnız kilitlenebilen satırlar kullanır).</summary>
        private static float Cycler(Transform body, string label, float y,
            UnityEngine.Events.UnityAction onPrev, UnityEngine.Events.UnityAction onNext,
            out TextMeshProUGUI value, out Button prevButton, out Button nextButton)
        {
            TextMeshProUGUI caption = UiKit.Text(body, $"Label_{label}", 20f, UiKit.Muted,
                FontStyles.Normal, TextAlignmentOptions.TopLeft);
            UiKit.Block(caption.rectTransform, 28f, y + 4f, 320f, 26f);
            caption.text = label;

            Button prev = UiKit.Button(body, $"Prev_{label}", "<", 20f, UiKit.Hex(0x2A303B, 0xFF),
                UiKit.Title, onPrev, out _);
            UiKit.Corner((RectTransform)prev.transform, new Vector2(1f, 1f),
                new Vector2(-238f, -y), new Vector2(32f, 32f));

            value = UiKit.Text(body, $"Value_{label}", 20f, UiKit.Title, FontStyles.Bold,
                TextAlignmentOptions.Center);
            UiKit.Corner(value.rectTransform, new Vector2(1f, 1f),
                new Vector2(-64f, -(y + 3f)), new Vector2(170f, 26f));
            value.textWrappingMode = TextWrappingModes.NoWrap;
            value.overflowMode = TextOverflowModes.Ellipsis;

            Button next = UiKit.Button(body, $"Next_{label}", ">", 20f, UiKit.Hex(0x2A303B, 0xFF),
                UiKit.Title, onNext, out _);
            UiKit.Corner((RectTransform)next.transform, new Vector2(1f, 1f),
                new Vector2(-28f, -y), new Vector2(32f, 32f));

            prevButton = prev;
            nextButton = next;
            return y + RowHeight;
        }

        /// <summary>Üç eşit maç düğmesi (oranlı anchor: panel genişliğinden bağımsız).</summary>
        private float MatchButtons(Transform body, float y)
        {
            Button start = UiKit.Button(body, "StartMatch", "BAŞLAT", 20f, UiKit.Good,
                UiKit.OnAccent, StartMatch, out _);
            PlaceThird((RectTransform)start.transform, 0, y);

            Button abort = UiKit.Button(body, "AbortMatch", "İPTAL", 20f, UiKit.Hex(0x2A303B, 0xFF),
                UiKit.Title, AdminCommands.AbortMatch, out _);
            PlaceThird((RectTransform)abort.transform, 1, y);

            Button lobby = UiKit.Button(body, "ReturnLobby", "LOBİYE DÖN", 20f,
                UiKit.Hex(0x2A303B, 0xFF), UiKit.Title, AdminCommands.ReturnToLobby, out _);
            PlaceThird((RectTransform)lobby.transform, 2, y);

            return y + 50f;
        }

        private static void PlaceThird(RectTransform rect, int index, float y)
        {
            rect.anchorMin = new Vector2(index / 3f, 1f);
            rect.anchorMax = new Vector2((index + 1) / 3f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(index == 0 ? 28f : 4f, -(y + 40f));
            rect.offsetMax = new Vector2(index == 2 ? -28f : -4f, -y);
        }

        // ------------------------------------------------------------------ eylemler

        private void StartMatch()
        {
            AdminCommands.StartMatch(SelectedModeId, SelectedSceneName, _roundSeconds, _scoreLimit);
        }

        private string SelectedModeId =>
            _modeIndex >= 0 && _modeIndex < _modes.Count ? _modes[_modeIndex].ModeId : "";

        private string SelectedSceneName =>
            _mapIndex >= 0 && _mapIndex < _maps.Count ? _maps[_mapIndex].SceneName : "";

        private ModeDefinition SelectedMode =>
            _modeIndex >= 0 && _modeIndex < _modes.Count ? _modes[_modeIndex] : null;

        private void CycleModePrev() { StepMode(-1); }
        private void CycleModeNext() { StepMode(1); }

        private void StepMode(int delta)
        {
            if (_modes.Count == 0 || !GuardSelectionChange())
            {
                return;
            }

            _modeIndex = (_modeIndex + delta + _modes.Count) % _modes.Count;
            RefreshMapList();
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

        private void CycleMapPrev() { StepMap(-1); }
        private void CycleMapNext() { StepMap(1); }

        private void StepMap(int delta)
        {
            if (_maps.Count == 0 || !GuardSelectionChange())
            {
                return;
            }

            _mapIndex = (_mapIndex + delta + _maps.Count) % _maps.Count;
            PublishSelection(mapChanged: true);
        }

        /// <summary>
        /// Mod/harita seçimi şu an değiştirilebilir mi (§10.7)? Harita seçmek TÜM istemcilere sahne
        /// yükletiyor — maç sürerken bunu yapmak maçı bozardı, o yüzden yalnız faz <c>Lobby</c>'de
        /// açıktır. Düğmeler zaten pasifleştiriliyor (<see cref="Apply"/>); bu kapı ikinci
        /// emniyettir ve reddi operatöre görünür kılar.
        /// <para>Otorite yine sunucudadır: aynı kural <c>set_selection</c> işlenirken de uygulanır,
        /// arayüz yalnız operatörü boşuna tıklatmamak için önden bilir.</para>
        /// </summary>
        private static bool GuardSelectionChange()
        {
            if (CanChangeSelection)
            {
                return true;
            }

            AdminCommands.Note("Maç sürerken harita/mod değiştirilemez — önce İPTAL ya da LOBİYE DÖN.");
            return false;
        }

        /// <summary>Faz Lobby mi? <see cref="AdminRoster"/> henüz yoksa (bağlanmadan önceki ilk
        /// kareler) engellenmez — sunucu zaten son sözü söylüyor.</summary>
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
        /// <param name="mapChanged">Bu yayın haritayı değiştirdi mi. Yerel yükleme YALNIZ o zaman
        /// yapılır: süre/limit dokunuşunda da yüklemek, sunucu kimseyi taşımadığı hâlde operatörü
        /// tek başına seçili arenaya atardı (maç sonrası herkes lobideyken en görünür hâli).</param>
        private void PublishSelection(bool mapChanged)
        {
            bool selectionOpen = CanChangeSelection;
            AdminCommands.SetSelection(
                selectionOpen ? SelectedModeId : "",
                selectionOpen ? SelectedSceneName : "",
                _roundSeconds, _scoreLimit);

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
        /// Katalogda olmayan bir seçim gelirse (sürüm farkı) imleç yerinde bırakılır.
        /// </summary>
        private void HandleSharedSelectionChanged()
        {
            _dirty = true;

            string sharedMode = AdminSelection.ModeId;
            string sharedScene = AdminSelection.SceneName;
            if (string.IsNullOrEmpty(sharedMode) && string.IsNullOrEmpty(sharedScene))
            {
                return; // sunucuda henüz seçim yok: yerel varsayılanı bozma
            }

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

            if (!string.IsNullOrEmpty(sharedScene) && sharedScene != SelectedSceneName)
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

        private void RefreshMapList()
        {
            string modeId = _modeIndex >= 0 && _modeIndex < _modes.Count ? _modes[_modeIndex].ModeId : "";
            AdminContent.CollectMaps(modeId, _maps);
            _mapIndex = 0;
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
        /// </summary>
        private void PreviewSelectedMap()
        {
            if (_mapIndex < 0 || _mapIndex >= _maps.Count || !CanChangeSelection)
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

            _modeValue.text = _modes.Count == 0
                ? "katalog yok"
                : DisplayOf(_modes[_modeIndex].DisplayName, _modes[_modeIndex].ModeId);

            _mapValue.text = _maps.Count == 0
                ? "harita yok"
                : DisplayOf(_maps[_mapIndex].DisplayName, _maps[_mapIndex].SceneName);

            ApplySelectionLock();

            // 0 = arayüz bir değer bilmiyor → sunucu modun varsayılanını kullanacak.
            _durationValue.text = _roundSeconds > 0
                ? AdminCommands.FormatDuration(_roundSeconds)
                : "mod varsayılanı";
            _scoreLimitValue.text = _scoreLimit > 0 ? _scoreLimit.ToString() : "mod varsayılanı";

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

        /// <summary>Maç sürerken mod/harita satırlarını pasif gösterir (§10.7): düğmeler
        /// tıklanmaz, değerler sönükleşir, bölüm başlığı sebebini yazar. Süre/limit satırları
        /// AÇIK kalır — onlar bir sonraki maçın parametreleridir, kimseye sahne yükletmezler.</summary>
        private void ApplySelectionLock()
        {
            bool open = CanChangeSelection;

            SetInteractable(_modePrev, open);
            SetInteractable(_modeNext, open);
            SetInteractable(_mapPrev, open);
            SetInteractable(_mapNext, open);

            Color valueColor = open ? UiKit.Title : UiKit.Faint;
            _modeValue.color = valueColor;
            _mapValue.color = valueColor;

            if (_matchSectionLabel != null)
            {
                _matchSectionLabel.text = open ? MatchSectionTitle : MatchSectionLockedTitle;
                _matchSectionLabel.color = open ? UiKit.Faint : UiKit.Bad;
            }
        }

        private static void SetInteractable(Button button, bool value)
        {
            if (button != null)
            {
                button.interactable = value;
            }
        }

        private static string DisplayOf(string displayName, string fallback)
        {
            return string.IsNullOrEmpty(displayName) ? fallback : displayName;
        }
    }
}
