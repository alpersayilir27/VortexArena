using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// Yan panellerdeki tek oyuncu satırı: takım şeridi, ad + <c>#id</c>, HP barı, K/D · batarya ·
    /// durum ve eylem düğmeleri (POV · KAL · TAKIM · KİMLİK · AT).
    /// <para>
    /// <b>Atma ve kalibrasyon sıfırlama iki adımlıdır:</b> ilk tıklama düğmeyi "EMİN?" yapar ve
    /// <see cref="ConfirmSeconds"/> sonra kendiliğinden geri döner; oyuncuyu maçtan atmak ya da
    /// savaş dışı bırakmak tek yanlış tıklamayla olmamalı.
    /// </para>
    /// <para>
    /// <b>KAL düğmesi</b> kalibrasyon durumunu hem GÖSTERİR (✓ yeşil / ✗ kırmızı) hem sıfırlar
    /// (§10.6). Yalnız sıfırlar: kalibrasyonu geri açmayı yalnız başlığın kendisi yapabilir,
    /// çünkü hizalamanın gerçekten oturduğunu yalnız o bilir. Kalibresiz satırın kenarlığı da
    /// kırmızıya döner — operatör listeye bakınca hemen görsün.
    /// </para>
    /// MonoBehaviour DEĞİL: HUD tarafından havuzlanan saf bir görünüm nesnesidir.
    /// </summary>
    public class AdminPlayerRow
    {
        /// <summary>Satır yüksekliği (px) — HUD yerleşimi bunu kullanır.</summary>
        public const float Height = 116f;

        /// <summary>"AT" ve "KAL" düğmelerinin onay penceresi (sn).</summary>
        private const float ConfirmSeconds = 3f;

        // ⚠️ Kalibrasyon etiketlerinde ✓/✗ gibi sembol KULLANILMAZ: TMP varsayılan fontunda
        // garantisi yok, eksik glif □ çizilir (UiKit sınıf dokümanı). Durum renkle (yeşil/kırmızı)
        // + ünlem işaretiyle anlatılır; ikisi de her fontta vardır.
        private const string LabelCalibrated = "KAL";
        private const string LabelUncalibrated = "KAL !";
        private const string LabelConfirm = "EMİN?";

        private const float DeadColorScale = 0.5f;

        private readonly RectTransform _root;
        private readonly Image _border;
        private readonly Image _background;
        private readonly Image _stripe;
        private readonly TextMeshProUGUI _nameText;
        private readonly TextMeshProUGUI _idText;
        private readonly Image _hpFill;
        private readonly TextMeshProUGUI _hpText;
        private readonly TextMeshProUGUI _statsText;
        private readonly Button _teamButton;
        private readonly TextMeshProUGUI _teamLabel;
        private readonly Button _kickButton;
        private readonly TextMeshProUGUI _kickLabel;
        private readonly Button _calibButton;
        private readonly TextMeshProUGUI _calibLabel;

        private int _playerId;
        private string _team = "";
        private float _kickArmedAt = -1f;
        private float _calibArmedAt = -1f;
        private bool _calibrated = true;

        public GameObject GameObject => _root != null ? _root.gameObject : null;

        public AdminPlayerRow(Transform parent, Action<int> onSelect, Action<int> onPov)
        {
            _background = UiKit.Panel(parent, "PlayerRow", UiKit.CardTranslucent, UiKit.Border);
            _root = (RectTransform)_background.transform.parent; // Panel: kenar > dolgu
            _border = _root.GetComponent<Image>();               // seçim vurgusu kenar rengiyle
            _background.raycastTarget = true;

            var selectButton = _background.gameObject.AddComponent<Button>();
            selectButton.targetGraphic = _background;
            ColorBlock colors = selectButton.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            colors.pressedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
            colors.fadeDuration = 0.06f;
            selectButton.colors = colors;
            selectButton.onClick.AddListener(() => onSelect?.Invoke(_playerId));

            Transform card = _background.transform;

            // Sol kenarda takım şeridi (seçim vurgusu kartın KENAR rengiyle verilir, şeridi ezmesin).
            _stripe = UiKit.Solid(card, "TeamStripe", UiKit.TeamNeutral);
            UiKit.Corner(_stripe.rectTransform, new Vector2(0f, 1f), Vector2.zero, new Vector2(6f, Height - 8f));

            _nameText = UiKit.Text(card, "Name", 24f, UiKit.Title, FontStyles.Bold, TextAlignmentOptions.TopLeft);
            UiKit.Block(_nameText.rectTransform, 18f, 8f, 74f, 28f);
            _nameText.textWrappingMode = TextWrappingModes.NoWrap;
            _nameText.overflowMode = TextOverflowModes.Ellipsis;

            _idText = UiKit.Text(card, "Id", 20f, UiKit.Faint, FontStyles.Normal, TextAlignmentOptions.TopRight);
            UiKit.Block(_idText.rectTransform, 18f, 10f, 14f, 24f);

            Image hpBar = UiKit.Bar(card, "HpBar", UiKit.Hex(0x2A303B, 0xFF), UiKit.Good);
            UiKit.Block(((RectTransform)hpBar.transform.parent), 18f, 40f, 96f, 12f);
            _hpFill = hpBar;

            _hpText = UiKit.Text(card, "Hp", 18f, UiKit.Muted, FontStyles.Normal, TextAlignmentOptions.TopRight);
            UiKit.Block(_hpText.rectTransform, 18f, 36f, 14f, 22f);

            _statsText = UiKit.Text(card, "Stats", 18f, UiKit.Muted, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            UiKit.Block(_statsText.rectTransform, 18f, 58f, 14f, 22f);
            _statsText.textWrappingMode = TextWrappingModes.NoWrap;
            _statsText.overflowMode = TextOverflowModes.Ellipsis;

            // Eylem düğmeleri: 5 eşit sütun. ⚠️ Punto 16 DEĞİL 14: sütun 380 px ve beşe bölününce
            // düğme başına ~70 px kalıyor; 16 puntoda "KİMLİK" ve takım etiketi ellipsis'e giriyor.
            const float buttonTop = 82f;
            const float buttonHeight = 26f;
            const float buttonFont = 14f;
            Button pov = UiKit.Button(card, "Pov", "POV", buttonFont, UiKit.Hex(0x2A303B, 0xFF), UiKit.Title,
                () => onPov?.Invoke(_playerId), out _);
            PlaceAction(pov, 0, buttonTop, buttonHeight);

            // Kalibrasyon: hem gösterge hem sıfırlama düğmesi (§10.6).
            _calibButton = UiKit.Button(card, "Calib", LabelCalibrated, buttonFont, UiKit.Hex(0x2A303B, 0xFF),
                UiKit.Good, PressCalibration, out _calibLabel);
            PlaceAction(_calibButton, 1, buttonTop, buttonHeight);

            _teamButton = UiKit.Button(card, "Team", "TAKIM", buttonFont, UiKit.Hex(0x2A303B, 0xFF), UiKit.Title,
                ToggleTeam, out _teamLabel);
            PlaceAction(_teamButton, 2, buttonTop, buttonHeight);

            Button identify = UiKit.Button(card, "Identify", "KİMLİK", buttonFont, UiKit.Hex(0x2A303B, 0xFF), UiKit.Title,
                () => AdminCommands.Identify(_playerId), out _);
            PlaceAction(identify, 3, buttonTop, buttonHeight);

            _kickButton = UiKit.Button(card, "Kick", "AT", buttonFont, UiKit.Hex(0x2A303B, 0xFF), UiKit.Muted,
                PressKick, out _kickLabel);
            PlaceAction(_kickButton, 4, buttonTop, buttonHeight);
        }

        /// <summary>Satırı verilen oyuncuya bağlar (her tazelemede çağrılır).</summary>
        public void Bind(AdminPlayerView view, bool selected)
        {
            if (view == null)
            {
                return;
            }

            _playerId = view.playerId;
            _team = view.team;
            _calibrated = view.calibrated;

            Color team = UiKit.TeamColor(view.team);
            _stripe.color = view.alive ? team : UiKit.Dim(team, DeadColorScale);

            if (_border != null)
            {
                // Kalibresiz satır seçili olmasa da kırmızı kenarlıkla ayrışır: operatör
                // listeye baktığında ilgilenmesi gereken satırı hemen görmeli (§10.6).
                _border.color = selected ? UiKit.Accent
                    : view.NeedsCalibration ? UiKit.Bad : UiKit.Border;
            }

            float alpha = view.online ? 1f : 0.45f;
            _nameText.color = UiKit.WithAlpha(view.alive ? UiKit.Title : UiKit.Muted, alpha);
            _nameText.text = view.name;
            _idText.text = $"#{view.playerId}";

            UiKit.SetBarFill(_hpFill, view.HpNormalized);
            _hpFill.color = view.HpNormalized > 0.5f ? UiKit.Good
                : view.HpNormalized > 0.2f ? UiKit.Accent : UiKit.Bad;
            _hpText.text = $"{Mathf.RoundToInt(view.hp)} HP";

            _statsText.text = BuildStatsLine(view);
            _statsText.color = view.online ? UiKit.Muted : UiKit.Faint;

            // Takım düğmesi karşı takımı gösterir (ne olacağını yazar, ne olduğunu değil).
            // ⚠️ "MAVİYE"/"KIRMIZIYA" değil: beş düğmeli satırda ~70 px kalıyor ve uzun olan
            // ellipsis'e giriyordu ("KIRMIZ…"). Hâl eki düşürüldü, anlam korundu.
            _teamLabel.text = view.team == "red" ? "MAVİ" : view.team == "blue" ? "KIRMIZI" : "TAKIM";

            RefreshKickButton();
            RefreshCalibrationButton();
        }

        /// <summary>Onay pencereleri doldu mu (HUD her karede çağırır).</summary>
        public void Tick()
        {
            if (_kickArmedAt >= 0f && Time.unscaledTime - _kickArmedAt > ConfirmSeconds)
            {
                _kickArmedAt = -1f;
                RefreshKickButton();
            }

            if (_calibArmedAt >= 0f && Time.unscaledTime - _calibArmedAt > ConfirmSeconds)
            {
                _calibArmedAt = -1f;
                RefreshCalibrationButton();
            }
        }

        public void SetVisible(bool visible)
        {
            if (_root != null && _root.gameObject.activeSelf != visible)
            {
                _root.gameObject.SetActive(visible);
            }
        }

        /// <summary>Satırı kolonun içinde verilen üst ofsete yerleştirir.</summary>
        public void Place(float top)
        {
            UiKit.Block(_root, 0f, top, 0f, Height);
        }

        // ---------------------------------------------------------------- iç işler

        private void PlaceAction(Button button, int index, float top, float height)
        {
            const int lastIndex = 4; // POV · KAL · TAKIM · KİMLİK · AT
            const float slot = 1f / (lastIndex + 1);
            var rect = (RectTransform)button.transform;
            rect.anchorMin = new Vector2(index * slot, 1f);
            rect.anchorMax = new Vector2((index + 1) * slot, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(index == 0 ? 18f : 3f, -(top + height));
            rect.offsetMax = new Vector2(index == lastIndex ? -14f : -3f, -top);
        }

        private void ToggleTeam()
        {
            // Takımsız (FFA/yeni) oyuncuyu kırmızıya al: sunucu zaten dengeliyor, buradaki amaç
            // operatörün elle müdahalesi.
            string next = _team == "red" ? "blue" : "red";
            AdminCommands.SetTeam(_playerId, next);
        }

        private void PressKick()
        {
            if (_kickArmedAt < 0f)
            {
                _kickArmedAt = Time.unscaledTime;
                RefreshKickButton();
                return;
            }

            _kickArmedAt = -1f;
            AdminCommands.Kick(_playerId);
            RefreshKickButton();
        }

        /// <summary>
        /// Kalibrasyonu sıfırlar — iki adımlı, "AT" ile aynı gerekçe: oyuncuyu savaş dışı bırakan
        /// bir eylem tek yanlış tıklamayla olmamalı. <b>Zaten kalibresizse hiçbir şey yapmaz</b>
        /// (düğme o hâlde salt göstergedir; geri açmayı yalnız başlık yapabilir, §10.6).
        /// </summary>
        private void PressCalibration()
        {
            if (!_calibrated)
            {
                return;
            }

            if (_calibArmedAt < 0f)
            {
                _calibArmedAt = Time.unscaledTime;
                RefreshCalibrationButton();
                return;
            }

            _calibArmedAt = -1f;
            AdminCommands.ClearCalibration(_playerId);
            RefreshCalibrationButton();
        }

        private void RefreshCalibrationButton()
        {
            bool armed = _calibArmedAt >= 0f;
            _calibLabel.text = armed ? LabelConfirm : _calibrated ? LabelCalibrated : LabelUncalibrated;
            _calibLabel.color = armed ? UiKit.OnAccent : _calibrated ? UiKit.Good : UiKit.Bad;

            if (_calibButton.targetGraphic is Image image)
            {
                image.color = armed ? UiKit.Bad : UiKit.Hex(0x2A303B, 0xFF);
            }
        }

        private void RefreshKickButton()
        {
            bool armed = _kickArmedAt >= 0f;
            _kickLabel.text = armed ? "EMİN?" : "AT";
            _kickLabel.color = armed ? UiKit.OnAccent : UiKit.Muted;

            if (_kickButton.targetGraphic is Image image)
            {
                image.color = armed ? UiKit.Bad : UiKit.Hex(0x2A303B, 0xFF);
            }
        }

        private static string BuildStatsLine(AdminPlayerView view)
        {
            string battery = view.battery < 0f ? "-" : $"%{Mathf.RoundToInt(Mathf.Clamp01(view.battery) * 100f)}";
            string state = BuildState(view);
            return $"{view.kills}/{view.deaths} · {battery} · {state}";
        }

        private static string BuildState(AdminPlayerView view)
        {
            if (!view.online)
            {
                return "çevrimdışı";
            }

            if (!view.alive)
            {
                float remaining = view.RespawnRemaining;
                return remaining > 0.1f
                    ? $"ÖLÜ {Mathf.CeilToInt(remaining)} sn"
                    : "TABANDA BEKLENİYOR";
            }

            return view.ready ? "HAZIR" : "bekliyor";
        }
    }
}
