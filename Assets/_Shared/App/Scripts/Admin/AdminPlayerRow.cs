using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// Yan panellerdeki tek oyuncu satırı: takım şeridi, ad + <c>#id</c>, HP barı, K/D · batarya ·
    /// durum ve eylem düğmeleri (POV · KAL · TAKIM · KİMLİK · AT).
    /// <para>
    /// <b>Görünüm prefabtan gelir:</b> <c>Assets/_Shared/App/Resources/UI/AdminPlayerRow.prefab</c>.
    /// Bu sınıf yalnız <b>davranış</b>tır — yerleşim/renk/punto prefabta düzenlenir. Alanların
    /// hepsi <c>[SerializeField]</c>; prefabta bağlanmayan alan sessizce çizilmez, bu yüzden
    /// prefab düzenlenirken bağlantılar korunmalıdır.
    /// </para>
    /// <para>
    /// <b>Atma ve kalibrasyon sıfırlama iki adımlıdır:</b> ilk tıklama düğmeyi "EMİN?" yapar ve
    /// <see cref="ConfirmSeconds"/> sonra kendiliğinden geri döner; oyuncuyu maçtan atmak ya da
    /// savaş dışı bırakmak tek yanlış tıklamayla olmamalı.
    /// </para>
    /// <para>
    /// <b>KAL düğmesi</b> kalibrasyon durumunu hem GÖSTERİR (yeşil / kırmızı) hem sıfırlar
    /// (§10.6). Yalnız sıfırlar: kalibrasyonu geri açmayı yalnız başlığın kendisi yapabilir,
    /// çünkü hizalamanın gerçekten oturduğunu yalnız o bilir. Kalibresiz satırın kenarlığı da
    /// kırmızıya döner — operatör listeye bakınca hemen görsün.
    /// </para>
    /// </summary>
    public class AdminPlayerRow : MonoBehaviour
    {
        /// <summary>
        /// Satır yüksekliğinin <b>yedek</b> değeri (px). Gerçek yükseklik prefabın
        /// <see cref="RectTransform"/>'undan okunur (<see cref="AdminHud"/>) — sanatçı prefabta
        /// satırı büyütürse kolon yerleşimi kendiliğinden uyar.
        /// </summary>
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

        /// <summary>Satır sönüklüğü: geri beklenen cihaz ile oyundan çıkarılmış cihaz aynı
        /// görünmemeli (§2) — ilki dönebilir, ikincisi için yapılacak bir şey yoktur.</summary>
        private const float ReconnectingAlpha = 0.7f;

        /// <inheritdoc cref="ReconnectingAlpha"/>
        private const float LeftAlpha = 0.45f;

        [Header("Kart")]
        [Tooltip("Kartın dış (kenarlık) görseli — seçim ve kalibrasyon vurgusu bunun rengiyle verilir.")]
        [SerializeField] private Image border;
        [Tooltip("Kartın iç dolgusu; satırı seçen düğme de bunun üstündedir.")]
        [SerializeField] private Image background;
        [SerializeField] private Button selectButton;
        [Tooltip("Sol kenardaki takım şeridi.")]
        [SerializeField] private Image stripe;

        [Header("Metinler")]
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI idText;
        [SerializeField] private TextMeshProUGUI hpText;
        [SerializeField] private TextMeshProUGUI statsText;

        [Header("Can barı")]
        [Tooltip("Barın DOLGU görseli (zemin değil) — genişliği anchorMax.x ile sürülür.")]
        [SerializeField] private Image hpFill;

        [Header("Eylem düğmeleri")]
        [SerializeField] private Button povButton;
        [SerializeField] private Button calibButton;
        [SerializeField] private TextMeshProUGUI calibLabel;
        [SerializeField] private Button teamButton;
        [SerializeField] private TextMeshProUGUI teamLabel;
        [SerializeField] private Button identifyButton;
        [SerializeField] private Button kickButton;
        [SerializeField] private TextMeshProUGUI kickLabel;

        private RectTransform _rect;
        private Action<int> _onSelect;
        private Action<int> _onPov;

        private int _playerId;
        private string _team = "";
        private float _kickArmedAt = -1f;
        private float _calibArmedAt = -1f;
        private bool _calibrated = true;

        private RectTransform Rect => _rect != null ? _rect : _rect = (RectTransform)transform;

        /// <summary>
        /// Düğme geri çağrılarını bağlar. Prefabta <c>onClick</c> kaydı YOKTUR ve olmamalıdır:
        /// hedef oyuncu her <see cref="Bind"/> ile değişiyor, kalıcı (persistent) bir kayıt
        /// yanlış oyuncuya komut gönderirdi.
        /// </summary>
        public void Initialize(Action<int> onSelect, Action<int> onPov)
        {
            _onSelect = onSelect;
            _onPov = onPov;

            Wire(selectButton, () => _onSelect?.Invoke(_playerId));
            Wire(povButton, () => _onPov?.Invoke(_playerId));
            Wire(calibButton, PressCalibration);
            Wire(teamButton, ToggleTeam);
            Wire(identifyButton, () => AdminCommands.Identify(_playerId));
            Wire(kickButton, PressKick);
        }

        private static void Wire(Button button, UnityAction action)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
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
            if (stripe != null)
            {
                stripe.color = view.alive ? team : UiKit.Dim(team, DeadColorScale);
            }

            if (border != null)
            {
                // Kalibresiz satır seçili olmasa da kırmızı kenarlıkla ayrışır: operatör
                // listeye baktığında ilgilenmesi gereken satırı hemen görmeli (§10.6).
                border.color = selected ? UiKit.Accent
                    : view.NeedsCalibration ? UiKit.Bad : UiKit.Border;
            }

            // Sönüklük kademeli: bağlı 1.0, geri beklenen 0.7, ayrılmış 0.45. Ayrım görsel olarak
            // taşınmalı — "geri gelebilir" ile "gitti" operatör için farklı iki karardır.
            float alpha = view.IsConnected ? 1f : view.IsReconnecting ? ReconnectingAlpha : LeftAlpha;
            if (nameText != null)
            {
                nameText.color = UiKit.WithAlpha(view.alive ? UiKit.Title : UiKit.Muted, alpha);
                // Numara adın ÖNÜNE yazılır (avatar plakasıyla aynı biçim): adlar benzersiz değil,
                // operatörün iki "ertu"yu ayırdığı şey numara. 0 = atanmamış → yalnız ad.
                nameText.text = view.number > 0 ? $"{view.number} · {view.name}" : view.name;
            }

            if (idText != null)
            {
                idText.text = $"#{view.playerId}";
            }

            UiKit.SetBarFill(hpFill, view.HpNormalized);
            if (hpFill != null)
            {
                hpFill.color = view.HpNormalized > 0.5f ? UiKit.Good
                    : view.HpNormalized > 0.2f ? UiKit.Accent : UiKit.Bad;
            }

            if (hpText != null)
            {
                hpText.text = $"{Mathf.RoundToInt(view.hp)} HP";
            }

            if (statsText != null)
            {
                statsText.text = BuildStatsLine(view);
                statsText.color = view.IsConnected ? UiKit.Muted : UiKit.Faint;
            }

            // Takım düğmesi karşı takımı gösterir (ne olacağını yazar, ne olduğunu değil).
            // ⚠️ "MAVİYE"/"KIRMIZIYA" değil: beş düğmeli satırda ~70 px kalıyor ve uzun olan
            // ellipsis'e giriyordu ("KIRMIZ…"). Hâl eki düşürüldü, anlam korundu.
            if (teamLabel != null)
            {
                teamLabel.text = view.team == "red" ? "MAVİ" : view.team == "blue" ? "KIRMIZI" : "TAKIM";
            }

            // Ayrılmış satırda komutun hedefi yok: oyuncu oyundan çıkarıldı, satır yalnız maç
            // istatistiği için duruyor (§10.2). Düğmeler kapatılır ki operatör tepkisiz bir
            // düğmeye basıp komutun gittiğini sanmasın.
            SetInteractable(teamButton, !view.HasLeft);
            SetInteractable(identifyButton, !view.HasLeft);
            SetInteractable(kickButton, !view.HasLeft);

            RefreshKickButton();
            RefreshCalibrationButton();
        }

        private static void SetInteractable(Button button, bool value)
        {
            if (button != null && button.interactable != value)
            {
                button.interactable = value;
            }
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
            if (gameObject.activeSelf != visible)
            {
                gameObject.SetActive(visible);
            }
        }

        /// <summary>Satırı kolonun içinde verilen üst ofsete yerleştirir.</summary>
        public void Place(float top, float height)
        {
            UiKit.Block(Rect, 0f, top, 0f, height);
        }

        // ---------------------------------------------------------------- iç işler

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
        /// bir eylem tek yanlış tıklamayla olmamalı.
        /// <para>
        /// ⚠️ <b>Satır kalibresiz görünürken de komut gönderilir</b> ve bu kapı geri konmaz: kırmızı
        /// satır tek bir durum değil İKİ durum gösterir — hiç kalibre olmamış oyuncu ve elle
        /// kalibrasyonun ortasında kalmış oyuncu (A'sını almış, B'sini almamış). İkincisinin telde
        /// izi yoktur (<c>calibrated</c> ikisinde de <c>false</c>), yani arayüz "zaten kalibresiz"
        /// diye eleyince operatör tam da sıfırlaması gereken oyuncuya ulaşamaz. Komut her iki
        /// durumu da aynı yere götürür: başlık yarım sekans dahil her şeyi siler (§10.6).
        /// </para>
        /// <para>Yön yine tek taraflıdır — kalibrasyonu geri açmayı yalnız başlık yapabilir.</para>
        /// </summary>
        private void PressCalibration()
        {
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

            if (calibLabel != null)
            {
                calibLabel.text = armed ? LabelConfirm : _calibrated ? LabelCalibrated : LabelUncalibrated;
                calibLabel.color = armed ? UiKit.OnAccent : _calibrated ? UiKit.Good : UiKit.Bad;
            }

            if (calibButton != null && calibButton.targetGraphic is Image image)
            {
                image.color = armed ? UiKit.Bad : UiKit.Hex(0x2A303B, 0xFF);
            }
        }

        private void RefreshKickButton()
        {
            bool armed = _kickArmedAt >= 0f;

            if (kickLabel != null)
            {
                kickLabel.text = armed ? LabelConfirm : "AT";
                kickLabel.color = armed ? UiKit.OnAccent : UiKit.Muted;
            }

            if (kickButton != null && kickButton.targetGraphic is Image image)
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

        /// <summary>Satırın durum metni. ⚠️ "çevrimdışı" diye bir durum YOKTUR (§2): kopan cihaz
        /// ya geri bekleniyor ya oyundan çıkarılmıştır — sayaç operatöre hangisi olduğunu söyler.</summary>
        private static string BuildState(AdminPlayerView view)
        {
            if (view.IsReconnecting)
            {
                return $"yeniden bağlanıyor · {view.ReconnectSecondsLeft} sn";
            }

            if (view.HasLeft)
            {
                return "ayrıldı";
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
