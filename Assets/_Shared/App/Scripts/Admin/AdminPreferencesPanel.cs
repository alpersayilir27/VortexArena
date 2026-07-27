using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.Core;
using VortexArena.Core.Arena;
using VortexArena.Net;

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
        private const float PanelHeight = 700f;
        private const float RowHeight = 40f;

        private GameObject _root;
        private TextMeshProUGUI _modeValue;
        private TextMeshProUGUI _mapValue;
        private TextMeshProUGUI _statusText;
        private TextMeshProUGUI _connectionText;

        private TextMeshProUGUI _markersValue;
        private TextMeshProUGUI _nameplatesValue;
        private TextMeshProUGUI _speedValue;
        private TextMeshProUGUI _wallValue;
        private TextMeshProUGUI _miniMapValue;

        private readonly List<ModeDefinition> _modes = new List<ModeDefinition>();
        private readonly List<MapDefinition> _maps = new List<MapDefinition>();
        private int _modeIndex;
        private int _mapIndex;

        private bool _dirty = true;

        public void Initialize(RectTransform parent)
        {
            Build(parent);
            AdminContent.CollectModes(_modes);
            RefreshMapList();
            Apply();
        }

        private void OnEnable()
        {
            AdminSession.Changed += MarkDirty;
            AdminCommands.StatusChanged += MarkDirty;
            NetEvents.OnConnectionStateChanged += HandleConnectionState;
        }

        private void OnDisable()
        {
            AdminSession.Changed -= MarkDirty;
            AdminCommands.StatusChanged -= MarkDirty;
            NetEvents.OnConnectionStateChanged -= HandleConnectionState;
        }

        private void Update()
        {
            if (_dirty)
            {
                _dirty = false;
                Apply();
            }
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

            y = Section(body, "MAÇ", y);
            y = Cycler(body, "Mod", y, CycleModePrev, CycleModeNext, out _modeValue);
            y = Cycler(body, "Harita", y, CycleMapPrev, CycleMapNext, out _mapValue);
            y = MatchButtons(body, y);

            _statusText = UiKit.Text(body, "Status", 18f, UiKit.Accent, FontStyles.Normal,
                TextAlignmentOptions.TopLeft);
            UiKit.Block(_statusText.rectTransform, 28f, y, 28f, 24f);
            y += 34f;

            y = Section(body, "GÖRÜNÜM", y);
            y = Cycler(body, "Halkalar", y, PrevMarkers, NextMarkers, out _markersValue);
            y = Cycler(body, "Ad etiketleri", y, ToggleNameplates, ToggleNameplates, out _nameplatesValue);
            y = Cycler(body, "Kamera hızı", y, SpeedDown, SpeedUp, out _speedValue);
            y = Cycler(body, "Duvar saydamlığı", y, WallDown, WallUp, out _wallValue);
            y = Cycler(body, "Mini harita", y, ToggleMiniMap, ToggleMiniMap, out _miniMapValue);

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
            TextMeshProUGUI text = UiKit.Text(body, $"Section_{label}", 18f, UiKit.Faint,
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
            ModeDefinition mode = _modeIndex >= 0 && _modeIndex < _modes.Count ? _modes[_modeIndex] : null;
            MapDefinition map = _mapIndex >= 0 && _mapIndex < _maps.Count ? _maps[_mapIndex] : null;
            AdminCommands.StartMatch(mode != null ? mode.ModeId : "", map != null ? map.SceneName : "");
        }

        private void CycleModePrev() { StepMode(-1); }
        private void CycleModeNext() { StepMode(1); }

        private void StepMode(int delta)
        {
            if (_modes.Count == 0)
            {
                return;
            }

            _modeIndex = (_modeIndex + delta + _modes.Count) % _modes.Count;
            RefreshMapList();
            PreviewSelectedMap(); // mod değişti → harita listesi başa döndü, seçili harita da değişti
            Apply();
        }

        private void CycleMapPrev() { StepMap(-1); }
        private void CycleMapNext() { StepMap(1); }

        private void StepMap(int delta)
        {
            if (_maps.Count == 0)
            {
                return;
            }

            _mapIndex = (_mapIndex + delta + _maps.Count) % _maps.Count;
            PreviewSelectedMap();
            Apply();
        }

        private void RefreshMapList()
        {
            string modeId = _modeIndex >= 0 && _modeIndex < _modes.Count ? _modes[_modeIndex].ModeId : "";
            AdminContent.CollectMaps(modeId, _maps);
            _mapIndex = 0;
        }

        /// <summary>
        /// Seçili harita değişti: maç BAŞLAMAMIŞSA (faz Lobby) o arenayı hemen yerel olarak açar.
        /// Operatör haritayı seçerken görmek ister; sunucuya komut gönderilmez, oyuncular
        /// etkilenmez. Maç sürüyorsa dokunulmaz — seçim yalnız sonraki `start_match` için.
        /// </summary>
        private void PreviewSelectedMap()
        {
            if (_mapIndex < 0 || _mapIndex >= _maps.Count)
            {
                return;
            }

            AdminRoster roster = AdminRoster.Instance;
            if (roster != null && roster.Phase != "Lobby")
            {
                return; // maç sürüyor: sahne otoritesi sunucuda
            }

            string sceneName = _maps[_mapIndex].SceneName;
            if (SceneRouter.Instance == null || string.IsNullOrEmpty(sceneName))
            {
                return;
            }

            SceneRouter.Instance.LoadPreview(sceneName);
            AdminCommands.Note($"Önizleme: {sceneName} (maç başlatılmadı)");
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

        private static void ToggleMiniMap()
        {
            AdminSession.MiniMap = !AdminSession.MiniMap;
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

            _statusText.text = AdminCommands.Status;

            _markersValue.text = AdminSession.Markers == AdminMarkerVisibility.Off ? "kapalı"
                : AdminSession.Markers == AdminMarkerVisibility.TopDownOnly ? "kuş bakışı" : "her zaman";
            _nameplatesValue.text = AdminSession.Nameplates ? "açık" : "kapalı";
            _speedValue.text = $"{AdminSession.FreeSpeed:0.0} m/sn";
            _wallValue.text = $"%{Mathf.RoundToInt(AdminSession.WallAlpha * 100f)}";
            _miniMapValue.text = AdminSession.MiniMap ? "açık" : "kapalı";

            ArenaClient client = ArenaClient.Instance;
            string endpoint = AppSession.HasServerEndpoint
                ? $"{AppSession.ServerIp}:{AppSession.ServerPort}"
                : "adres yok (launcher'dan başlatılmalı)";
            string state = client == null ? "istemci yok"
                : client.IsConnected ? "bağlı"
                : client.State == ArenaConnectionState.Connecting ? "bağlanılıyor" : "bağlı değil";
            _connectionText.text = $"{state} — {endpoint}";
        }

        private static string DisplayOf(string displayName, string fallback)
        {
            return string.IsNullOrEmpty(displayName) ? fallback : displayName;
        }
    }
}
