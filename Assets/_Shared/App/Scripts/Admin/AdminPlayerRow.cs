using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// Yan panellerdeki tek oyuncu satırı: takım şeridi, ad + <c>#id</c>, HP barı, K/D · batarya ·
    /// durum ve eylem düğmeleri (POV · TAKIM · KİMLİK · AT).
    /// <para>
    /// <b>Atma iki adımlıdır:</b> ilk tıklama düğmeyi "EMİN?" yapar ve
    /// <see cref="ConfirmSeconds"/> sonra kendiliğinden geri döner; oyuncuyu maçtan atmak tek
    /// yanlış tıklamayla olmamalı.
    /// </para>
    /// MonoBehaviour DEĞİL: HUD tarafından havuzlanan saf bir görünüm nesnesidir.
    /// </summary>
    public class AdminPlayerRow
    {
        /// <summary>Satır yüksekliği (px) — HUD yerleşimi bunu kullanır.</summary>
        public const float Height = 116f;

        /// <summary>"AT" düğmesinin onay penceresi (sn).</summary>
        private const float ConfirmSeconds = 3f;

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

        private int _playerId;
        private string _team = "";
        private float _kickArmedAt = -1f;

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

            // Eylem düğmeleri: 4 eşit sütun.
            const float buttonTop = 82f;
            const float buttonHeight = 26f;
            Button pov = UiKit.Button(card, "Pov", "POV", 16f, UiKit.Hex(0x2A303B, 0xFF), UiKit.Title,
                () => onPov?.Invoke(_playerId), out _);
            PlaceAction(pov, 0, buttonTop, buttonHeight);

            _teamButton = UiKit.Button(card, "Team", "TAKIM", 16f, UiKit.Hex(0x2A303B, 0xFF), UiKit.Title,
                ToggleTeam, out _teamLabel);
            PlaceAction(_teamButton, 1, buttonTop, buttonHeight);

            Button identify = UiKit.Button(card, "Identify", "KİMLİK", 16f, UiKit.Hex(0x2A303B, 0xFF), UiKit.Title,
                () => AdminCommands.Identify(_playerId), out _);
            PlaceAction(identify, 2, buttonTop, buttonHeight);

            _kickButton = UiKit.Button(card, "Kick", "AT", 16f, UiKit.Hex(0x2A303B, 0xFF), UiKit.Muted,
                PressKick, out _kickLabel);
            PlaceAction(_kickButton, 3, buttonTop, buttonHeight);
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

            Color team = UiKit.TeamColor(view.team);
            _stripe.color = view.alive ? team : UiKit.Dim(team, DeadColorScale);

            if (_border != null)
            {
                _border.color = selected ? UiKit.Accent : UiKit.Border;
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
            _teamLabel.text = view.team == "red" ? "MAVİYE" : view.team == "blue" ? "KIRMIZIYA" : "TAKIM";

            RefreshKickButton();
        }

        /// <summary>Onay penceresi doldu mu (HUD her karede çağırır).</summary>
        public void Tick()
        {
            if (_kickArmedAt >= 0f && Time.unscaledTime - _kickArmedAt > ConfirmSeconds)
            {
                _kickArmedAt = -1f;
                RefreshKickButton();
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
            var rect = (RectTransform)button.transform;
            rect.anchorMin = new Vector2(index * 0.25f, 1f);
            rect.anchorMax = new Vector2((index + 1) * 0.25f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(index == 0 ? 18f : 3f, -(top + height));
            rect.offsetMax = new Vector2(index == 3 ? -14f : -3f, -top);
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
