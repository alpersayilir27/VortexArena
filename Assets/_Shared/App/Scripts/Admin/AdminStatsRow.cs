using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using VortexArena.Protocol;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// İstatistik panelindeki tek oyuncu satırı: takım şeridi, ad + <c>#id</c>, K/D/KD hücreleri,
    /// tek satırlık ayrıntı (skor · pil · kumanda · ping · durum) ve eylem düğmeleri
    /// (kalem · AT · ÖLÇ · KALİBRE).
    /// <para>
    /// <b>Neden <see cref="AdminPlayerRow"/>'un kardeşi ama AYRI bir sınıf:</b> yan panel kartı dar
    /// ve POV/takım/kimlik/can düğmeleriyle sahne kontrolüne ait; bu satır geniş, tablo hücreli ve
    /// operatörün oturup <i>kayıt işi</i> yaptığı ekrana ait (ad düzenleme, ölçüm, kalibrasyon
    /// yeniden yükleme). İkisini tek sınıfa sıkıştırmak her <c>Bind</c>'da "hangi ekrandayım"
    /// dallanması demektir ve bir ekrana yapılan her düzeltme diğerini sessizce bozar.
    /// </para>
    /// <para>
    /// <b>Görünüm prefabtan gelir</b> (<c>Assets/_Shared/App/Resources/UI/</c>). Bu sınıf yalnız
    /// <b>davranış</b>tır: yerleşim/punto/statik renkler prefabta, durum renkleri koddan. Prefabta
    /// bağlanmayan alan sessizce çizilmez.
    /// </para>
    /// <para>
    /// ⚠️ <b>HP, SAHNE ve İHLAL bu satırda YOKTUR ve eklenmez</b> — eksiklik değil karardır: HP yan
    /// paneldeki oyuncu kartında (bar ile) duruyor, ihlal akışı HUD'ın kendi şeridinde ve satır
    /// kenarlığında canlı çiziliyor, sahne adı ise tüm başlıklarda aynı olduğu için satır başına
    /// tekrarlandığında yalnız gürültü üretiyordu.
    /// </para>
    /// <para>
    /// ⚠️ <b>Kalibrasyon SIFIRLAMA bu satırda YOKTUR:</b> buradaki KALİBRE düğmesi gözlükte KAYITLI
    /// çapa verisinden <i>yeniden yükleme</i>dir. Sıfırlama (savaş dışı bırakır) yan paneldeki KAL
    /// düğmesindedir; iki zıt işi yan yana koymak operatörü yanıltır.
    /// </para>
    /// </summary>
    public class AdminStatsRow : MonoBehaviour
    {
        /// <summary>Satır yüksekliğinin <b>yedek</b> değeri (px). Gerçek yükseklik prefabın
        /// <see cref="RectTransform"/>'undan okunur (<see cref="AdminStatsPanel"/>) — sanatçı
        /// satırı büyütürse liste yerleşimi kendiliğinden uyar.</summary>
        public const float Height = 64f;

        /// <summary>"AT" düğmesinin onay penceresi (sn) — <see cref="AdminPlayerRow"/> ile aynı.</summary>
        private const float ConfirmSeconds = 3f;

        /// <summary>Sonuç etiketinin ("TAMAM"/"HATA") ekranda kaldığı süre (sn). Kalıcı olsaydı
        /// satır bir sonraki denemeye kadar eski sonucu gösterirdi.</summary>
        private const float ResultHoldSeconds = 2f;

        /// <summary>
        /// Yeniden yükleme yanıtının beklendiği en uzun süre (sn).
        /// <para>⚠️ <b>Zorunludur:</b> başlık kapanmış ya da donmuşsa <c>calibration_result</c> HİÇ
        /// gelmez ve düğme sonsuza kadar "YÜKLENİYOR" olarak asılı kalırdı — operatör hem sonucu
        /// öğrenemez hem tekrar deneyemez.</para>
        /// </summary>
        private const float LoadTimeoutSeconds = 15f;

        // ⚠️ Etiketlerde sembol/emoji YOK: TMP varsayılan fontunda garantisi yok, eksik glif □
        // çizilir (AdminPlayerRow ve UiKit'te aynı kural). Durum renkle + ünlem işaretiyle anlatılır.
        private const string LabelConfirm = "EMİN?";
        private const string LabelKick = "AT";
        private const string LabelMeasure = "ÖLÇ";
        private const string LabelMeasureFailed = "ÖLÇÜLEMEDİ";
        private const string LabelCalibrate = "KALİBRE";
        private const string LabelCalibrateUncalibrated = "KALİBRE !";
        private const string LabelCalibrateLoading = "YÜKLENİYOR";
        private const string LabelCalibrateOk = "TAMAM";
        private const string LabelCalibrateFailed = "HATA";

        /// <summary>Yanıt hiç gelmediğinde operatöre gösterilen gerekçe — boş bir hata metni
        /// "HATA" deyip sebebi söylememek olurdu.</summary>
        private const string TimeoutReason = "başlıktan yanıt gelmedi";

        private const float DeadColorScale = 0.5f;

        /// <summary>Satır sönüklüğü (<see cref="AdminPlayerRow"/> ile aynı kademe): geri beklenen
        /// cihaz ile oyundan çıkarılmış cihaz aynı görünmemeli.</summary>
        private const float ReconnectingAlpha = 0.7f;

        /// <inheritdoc cref="ReconnectingAlpha"/>
        private const float LeftAlpha = 0.45f;

        /// <summary>KALİBRE düğmesinin durumu — etiketi ve etkinliğini bu belirler.</summary>
        private enum LoadState
        {
            Idle,
            Loading,
            Ok,
            Failed
        }

        [Header("Kart")]
        [Tooltip("Kartın dış (kenarlık) görseli — seçim ve kalibrasyon vurgusu bunun rengiyle verilir.")]
        [SerializeField] private Image border;
        [Tooltip("Kartın iç dolgusu; satırı seçen düğme de bunun üstündedir.")]
        [SerializeField] private Image background;
        [SerializeField] private Button selectButton;
        [Tooltip("Sol kenardaki takım şeridi.")]
        [SerializeField] private Image stripe;

        [Header("Kimlik")]
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI idText;
        [Tooltip("Düzenleme kipinde açılan grup (giriş alanı + onay/iptal). Kapalıyken ad okunur.")]
        [SerializeField] private GameObject nameEditRoot;
        [SerializeField] private TMP_InputField nameInput;
        [SerializeField] private Button nameApplyButton;
        [SerializeField] private Button nameCancelButton;

        [Header("Eylem düğmeleri")]
        [Tooltip("Satırı ad düzenleme kipine alır (kalem).")]
        [SerializeField] private Button renameButton;
        [Tooltip("Oyuncuyu maçtan atar — iki adımlı onay.")]
        [SerializeField] private Button kickButton;
        [SerializeField] private TextMeshProUGUI kickLabel;
        [Tooltip("Gövde ölçüsünü aldırır (§10.8). Etiketi aynı zamanda GÖSTERGEDİR.")]
        [SerializeField] private Button measureButton;
        [SerializeField] private TextMeshProUGUI measureLabel;
        [Tooltip("Gözlükteki KAYITLI çapa verisinden kalibrasyonu yeniden yükletir (sıfırlamaz).")]
        [SerializeField] private Button calibrateButton;
        [SerializeField] private TextMeshProUGUI calibrateLabel;

        [Header("İstatistik hücreleri")]
        [SerializeField] private TextMeshProUGUI killsText;
        [SerializeField] private TextMeshProUGUI deathsText;
        [SerializeField] private TextMeshProUGUI kdText;
        [Tooltip("SKOR · pil · kumanda · ping · durum — zengin metin içerir (koddan açılır).")]
        [SerializeField] private TextMeshProUGUI detailText;

        private RectTransform _rect;
        private Action<int> _onSelect;
        private Action<int, string> _onPopup;

        private int _playerId;
        private bool _calibrated = true;
        private bool _hasLeft;
        private float _floorOffset;
        private float _kickArmedAt = -1f;

        /// <summary>Ad düzenleme kipi. Açıkken <see cref="Bind"/> ada DOKUNMAZ (bkz. sınıf içi
        /// gerekçe) — bu yüzden kip bir alan olarak taşınır.</summary>
        private bool _editing;

        private LoadState _loadState = LoadState.Idle;

        /// <summary>Yükleme/sonuç durumuna girildiği an (<c>Time.unscaledTime</c>);
        /// zaman aşımı ve sonuç bekletmesi bunun üstünden ölçülür.</summary>
        private float _loadStateAt = -1f;

        public int PlayerId => _playerId;

        private RectTransform Rect => _rect != null ? _rect : _rect = (RectTransform)transform;

        /// <summary>
        /// Düğme geri çağrılarını bağlar. Prefabta <c>onClick</c> kaydı YOKTUR ve olmamalıdır:
        /// hedef oyuncu her <see cref="Bind"/> ile değişiyor, kalıcı (persistent) bir kayıt yanlış
        /// oyuncuya komut gönderirdi.
        /// </summary>
        /// <param name="onSelect">Satır seçildi (panel seçili oyuncuyu günceller).</param>
        /// <param name="onPopup">Kalibrasyon yüklemesi başarısız — gerekçeyi panel gösterir.</param>
        public void Initialize(Action<int> onSelect, Action<int, string> onPopup)
        {
            _onSelect = onSelect;
            _onPopup = onPopup;

            EnableDetailRichText();

            Wire(selectButton, () => _onSelect?.Invoke(_playerId));
            Wire(renameButton, BeginNameEdit);
            Wire(nameApplyButton, ApplyNameEdit);
            Wire(nameCancelButton, CancelNameEdit);
            Wire(kickButton, PressKick);
            // ⚠️ Tek adımlı (onay penceresi YOK): ölçüm geri alınabilir bir eylemdir — yanlışlıkla
            // basılırsa yeniden ölçülür (AdminPlayerRow ile aynı gerekçe).
            Wire(measureButton, () => AdminCommands.MeasureBodyScale(_playerId));
            Wire(calibrateButton, PressCalibrate);

            if (nameInput != null)
            {
                // Enter = onayla: operatör ada dokunurken elini fareye götürmek zorunda kalmasın.
                nameInput.onSubmit.RemoveAllListeners();
                nameInput.onSubmit.AddListener(_ => ApplyNameEdit());
            }

            SetNameEditActive(false);
            RefreshKickButton();
            RefreshCalibrateButton();
        }

        /// <summary>
        /// Ayrıntı hücresinin zengin metin kapısı.
        /// <para>⚠️ <b>Bayrak prefabta değil BURADA açılır:</b> <c>&lt;color=…&gt;</c> etiketlerini
        /// <see cref="AdminPlayerRow.FormatBattery"/>/<see cref="AdminPlayerRow.FormatControllers"/>
        /// üretiyor, yani bayrak bir görünüm tercihi değil üretilen metnin sözleşmesidir. Prefabta
        /// kapalı kaldığında hücre <c>%73 K:&lt;color=#…&gt;~</c> gibi ham etiket çizer ve bu sessiz
        /// bir bozulmadır.</para>
        /// <para>⚠️ <b>Yalnız bu hücre için.</b> <see cref="nameText"/> zengin metne KAPALI kalır:
        /// ad dışarıdan gelir ve <c>&lt;b&gt;</c> içeren bir ad satırın biçimini bozardı.</para>
        /// </summary>
        private void EnableDetailRichText()
        {
            if (detailText != null)
            {
                detailText.richText = true;
            }
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

            // ⚠️ Satır başka bir oyuncuya bağlanıyorsa açık kipler KAPATILIR: düzenleme kipi
            // kalsaydı bir oyuncuya yazılan ad diğerine gönderilirdi, bekleyen yükleme kalsaydı
            // önceki oyuncunun sonucu bu satırda görünürdü.
            if (_playerId != view.playerId)
            {
                SetNameEditActive(false);
                _kickArmedAt = -1f;
                SetLoadState(LoadState.Idle);
            }

            _playerId = view.playerId;
            _calibrated = view.calibrated;
            _hasLeft = view.HasLeft;
            _floorOffset = view.floorOffset;

            Color team = UiKit.TeamColor(view.team);
            if (stripe != null)
            {
                stripe.color = view.alive ? team : UiKit.Dim(team, DeadColorScale);
            }

            // Kalibresiz satır seçili olmasa da kırmızı kenarlıkla ayrışır (§10.6).
            // ⚠️ İhlal yanıp sönmesi bu satırda YOK: ihlalin canlı kanalı HUD kartıdır ve iki
            // ekranda birden yanıp sönen bir kenarlık operatörün gözünü hiçbir yere odaklamaz.
            if (border != null)
            {
                border.color = selected ? UiKit.Accent
                    : view.NeedsCalibration ? UiKit.Bad : UiKit.Border;
            }

            // Sönüklük kademeli: bağlı 1.0, geri beklenen 0.7, ayrılmış 0.45 — "geri gelebilir" ile
            // "gitti" operatör için farklı iki karardır.
            float alpha = view.IsConnected ? 1f : view.IsReconnecting ? ReconnectingAlpha : LeftAlpha;

            if (nameText != null && !_editing)
            {
                // ⚠️ Düzenleme kipindeyken ada DOKUNULMAZ: roster saniyede birkaç kez tazeleniyor ve
                // operatörün yazdığı metnin üstüne yazmak onu sessizce kaybettirir.
                // Ad TAKIM RENGİNDE yazılır — aynı oyuncu sahnede, kill feed'de ve burada aynı
                // renkte görünsün; takımsız oyuncuda renk bilgi taşımaz, başlık rengi kalır.
                Color nameColor = IsTeamPlayer(view.team) ? team : UiKit.Title;
                nameText.color = UiKit.WithAlpha(
                    view.alive ? nameColor : UiKit.Dim(nameColor, DeadColorScale), alpha);
                // Numara adın ÖNÜNE (avatar plakasıyla aynı biçim): adlar benzersiz değil,
                // operatörün ayırt ettiği şey numaradır. 0 = atanmamış → yalnız ad.
                nameText.text = view.number > 0 ? $"{view.number} · {view.name}" : view.name;
            }

            if (idText != null)
            {
                idText.text = $"#{view.playerId}";
            }

            if (killsText != null)
            {
                killsText.text = view.kills.ToString();
                killsText.color = view.IsConnected ? UiKit.Title : UiKit.Faint;
            }

            if (deathsText != null)
            {
                deathsText.text = view.deaths.ToString();
                deathsText.color = view.IsConnected ? UiKit.Title : UiKit.Faint;
            }

            if (kdText != null)
            {
                // Ölümsüz oyuncuda oran tanımsız olurdu; öldürme sayısını aynı biçimde yazmak
                // "sıfıra bölme" yerine okunabilir bir değer verir (tablo dönemindeki davranış).
                kdText.text = view.deaths > 0
                    ? (view.kills / (float)view.deaths).ToString("0.00")
                    : view.kills.ToString("0.00");
                kdText.color = view.IsConnected ? UiKit.Muted : UiKit.Faint;
            }

            if (detailText != null)
            {
                // ⚠️ Satırın TABAN rengi burada, token renkleri zengin metinle: tek TMP'nin tek
                // `.color`'ı var, oysa pil ve kumanda kendi durumlarına göre ayrı ayrı renklenmeli.
                detailText.text = BuildDetailLine(view);
                detailText.color = view.IsConnected ? UiKit.Muted : UiKit.Faint;
            }

            // Ayrılmış satırda komutun hedefi yok (§10.2): oyuncu oyundan çıkarıldı, satır yalnız
            // maç istatistiği için duruyor. Tepkisiz bir düğme "gönderdim ama olmadı" hissi üretir.
            SetInteractable(kickButton, !view.HasLeft);
            SetInteractable(renameButton, !view.HasLeft);

            RefreshMeasureButton(view);
            RefreshKickButton();
            RefreshCalibrateButton();
        }

        /// <summary>Onay penceresi, yükleme zaman aşımı ve sonuç bekletmesi (panel her karede
        /// çağırır).</summary>
        public void Tick()
        {
            if (_kickArmedAt >= 0f && Time.unscaledTime - _kickArmedAt > ConfirmSeconds)
            {
                _kickArmedAt = -1f;
                RefreshKickButton();
            }

            if (_loadState == LoadState.Loading &&
                Time.unscaledTime - _loadStateAt > LoadTimeoutSeconds)
            {
                // Yanıt hiç gelmedi: başarısızlık gibi ele alınır — sessizce boşa düşürmek
                // operatöre "komut gitti ve tuttu" izlenimi verirdi.
                ApplyCalibrationResult(false, TimeoutReason);
                return;
            }

            if ((_loadState == LoadState.Ok || _loadState == LoadState.Failed) &&
                Time.unscaledTime - _loadStateAt > ResultHoldSeconds)
            {
                SetLoadState(LoadState.Idle);
            }
        }

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf == visible)
            {
                return;
            }

            if (!visible)
            {
                // ⚠️ Gizlenen satırda düzenleme kipi bırakılmaz: liste yeniden dizildiğinde aynı
                // satır başka bir oyuncuya düşer ve yazılan ad ona gider.
                SetNameEditActive(false);
            }

            gameObject.SetActive(visible);
        }

        /// <summary>Satırı listenin içinde verilen üst ofsete yerleştirir.</summary>
        public void Place(float top, float height)
        {
            UiKit.Block(Rect, 0f, top, 0f, height);
        }

        /// <summary>
        /// Düğmeyi "yükleniyor" kipine alır. <b>Toplu eylem</b> (TÜMÜNÜ KALİBRE ET) bunu komutu
        /// göndermeden ÖNCE çağırır: komut önce gidip sonucu hemen dönerse satır henüz yükleme
        /// kipine girmemiş olurdu ve sonuç görünmeden yutulurdu.
        /// </summary>
        public void BeginCalibrationLoad()
        {
            SetLoadState(LoadState.Loading);
        }

        /// <summary>
        /// Yeniden yükleme denemesinin sonucu (§5.3 <c>calibration_result</c> ya da zaman aşımı).
        /// <para>Başarıda popup YOKTUR — düğmenin "TAMAM" durumu yeterli; başarısızlıkta gerekçe
        /// panele verilir, çünkü dar düğme "HATA"dan fazlasını taşıyamaz.</para>
        /// </summary>
        public void ApplyCalibrationResult(bool ok, string error)
        {
            SetLoadState(ok ? LoadState.Ok : LoadState.Failed);

            if (!ok)
            {
                _onPopup?.Invoke(_playerId,
                    string.IsNullOrEmpty(error) ? "kalibrasyon yüklenemedi" : error);
            }
        }

        // ---------------------------------------------------------------- iç işler

        /// <summary>
        /// Esc = düzenlemeden çık.
        /// <para>⚠️ Okuma <b>Input System</b> ile: proje Input System-only, eski
        /// <c>Input.GetKeyDown</c> çalışma anında istisna atar. Klavye yoksa
        /// (<c>Keyboard.current == null</c>) sessizce geçilir.</para>
        /// </summary>
        private void Update()
        {
            if (!_editing)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                CancelNameEdit();
            }
        }

        private static bool IsTeamPlayer(string team)
        {
            return team == "red" || team == "blue";
        }

        private static void SetInteractable(Button button, bool value)
        {
            if (button != null && button.interactable != value)
            {
                button.interactable = value;
            }
        }

        /// <summary>
        /// Ad düzenleme kipini açar: okunur ad gizlenir, giriş alanı oyuncunun mevcut adıyla
        /// dolar ve odağı alır. Kip açıkken roster tazelemesi ada dokunmaz (bkz. <see cref="Bind"/>).
        /// </summary>
        private void BeginNameEdit()
        {
            AdminPlayerView view = AdminRoster.Instance != null
                ? AdminRoster.Instance.Find(_playerId)
                : null;
            if (view == null || view.HasLeft)
            {
                return;
            }

            SetNameEditActive(true);

            if (nameInput != null)
            {
                nameInput.text = view.name;
                nameInput.ActivateInputField();
            }
        }

        /// <summary>Yeni adı gönderir. Numara <c>0</c> geçilir = "numarayı DEĞİŞTİRME" (§5.1):
        /// forma numarasını sunucu atar ve onu elle değiştiren bir arayüz YOKTUR.</summary>
        private void ApplyNameEdit()
        {
            if (!_editing)
            {
                return;
            }

            string typed = nameInput != null ? nameInput.text : "";
            SetNameEditActive(false);
            AdminCommands.SetIdentity(_playerId, typed, 0);
        }

        /// <summary>Düzenlemeyi iptal eder — hiçbir şey gönderilmez, satır bir sonraki
        /// <see cref="Bind"/> ile sunucunun bildiği ada döner.</summary>
        private void CancelNameEdit()
        {
            SetNameEditActive(false);
        }

        private void SetNameEditActive(bool editing)
        {
            _editing = editing;

            if (nameEditRoot != null)
            {
                nameEditRoot.SetActive(editing);
            }

            if (nameText != null)
            {
                nameText.gameObject.SetActive(!editing);
            }
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
        /// Kalibrasyonu gözlükteki KAYITLI çapa verisinden yeniden yükletir (§5.3).
        /// <para>Tek adımlıdır: oyuncuyu savaş dışı bırakmaz, en kötü ihtimalle hiçbir şey olmaz —
        /// iki adımlı kilit sıfırlama gibi geri alınamaz komutlar içindir.</para>
        /// </summary>
        private void PressCalibrate()
        {
            if (_loadState == LoadState.Loading)
            {
                return; // çift gönderim: düğme zaten pasif, bu ikinci savunma hattı
            }

            BeginCalibrationLoad();
            AdminCommands.ReloadCalibration(_playerId);
        }

        private void SetLoadState(LoadState state)
        {
            _loadState = state;
            _loadStateAt = Time.unscaledTime;
            RefreshCalibrateButton();
        }

        /// <summary>
        /// ÖLÇ düğmesi hem KOMUT hem GÖSTERGEDİR (§10.8): ölçülmemişse "ÖLÇ", ölçülmüşse çarpanın
        /// kendisi yazar. Kalibresiz oyuncuda pasiftir — sunucu o komutu zaten kesiyor.
        /// <para>⚠️ Son ölçüm başarısızsa (<c>scaleError</c> dolu) etiket çarpan yerine
        /// başarısızlığı yazar ve düğme ETKİN kalır: yapılacak iş tam da yeniden ölçmektir.</para>
        /// </summary>
        private void RefreshMeasureButton(AdminPlayerView view)
        {
            bool usable = view.IsPlayer && view.calibrated && !view.HasLeft;
            SetInteractable(measureButton, usable);

            if (measureLabel != null)
            {
                bool failed = !string.IsNullOrEmpty(view.scaleError);
                measureLabel.text = failed ? LabelMeasureFailed
                    : view.bodyScale > 0f ? $"×{view.bodyScale:0.00}" : LabelMeasure;
                measureLabel.color = !usable ? UiKit.Faint
                    : failed ? UiKit.Bad
                    : view.bodyScale > 0f ? UiKit.Good : UiKit.Muted;
            }
        }

        private void RefreshKickButton()
        {
            bool armed = _kickArmedAt >= 0f;

            if (kickLabel != null)
            {
                kickLabel.text = armed ? LabelConfirm : LabelKick;
                kickLabel.color = armed ? UiKit.OnAccent : UiKit.Muted;
            }

            if (kickButton != null && kickButton.targetGraphic is Image image)
            {
                image.color = armed ? UiKit.Bad : UiKit.Hex(0x2A303B, 0xFF);
            }
        }

        /// <summary>
        /// KALİBRE düğmesi boşta GÖSTERGEDİR: kalibreliyse yeşil, zemin sapması eşiği aşıyorsa
        /// uyarı rengi (§10.6 — hizalama geçerli ama gözlüğün alan verisi bayat), kalibresizse
        /// kırmızı ve ünlemli. Yüklerken pasiftir; sonuç kısa süre gösterilip boş duruma dönülür.
        /// </summary>
        private void RefreshCalibrateButton()
        {
            bool loading = _loadState == LoadState.Loading;
            SetInteractable(calibrateButton, !loading && !_hasLeft);

            if (calibrateLabel == null)
            {
                return;
            }

            switch (_loadState)
            {
                case LoadState.Loading:
                    calibrateLabel.text = LabelCalibrateLoading;
                    calibrateLabel.color = UiKit.Muted;
                    return;
                case LoadState.Ok:
                    calibrateLabel.text = LabelCalibrateOk;
                    calibrateLabel.color = UiKit.Good;
                    return;
                case LoadState.Failed:
                    calibrateLabel.text = LabelCalibrateFailed;
                    calibrateLabel.color = UiKit.Bad;
                    return;
            }

            bool floorDrift = _calibrated &&
                              Mathf.Abs(_floorOffset) > ArenaProtocol.CALIB_FLOOR_WARN_METERS;

            calibrateLabel.text = _calibrated ? LabelCalibrate : LabelCalibrateUncalibrated;
            // Sapma uyarısı Accent, kalibresizlik Bad: sapmalı oyuncu oynayabilir, kalibresiz
            // oynayamaz — iki durum renkten ayrışmalı (AdminPlayerRow ile aynı ton).
            calibrateLabel.color = !_calibrated ? UiKit.Bad
                : floorDrift ? UiKit.Accent : UiKit.Good;
        }

        /// <summary>
        /// Ayrıntı hücresi: SKOR · pil · kumanda · ping · durum.
        /// <para>Pil eşikleri/renkleri ve kumanda simgeleri <see cref="AdminPlayerRow"/>'dan gelir —
        /// aynı gözlük iki ekranda farklı renkte görünmemeli.</para>
        /// </summary>
        private static string BuildDetailLine(AdminPlayerView view)
        {
            string battery = AdminPlayerRow.FormatBattery(view);
            string controllers = AdminPlayerRow.FormatControllers(view);
            // §6.7: -1 = ölçüm yok. "0 ms ping" gibi okunmasın diye "-".
            string ping = view.rttMs < 0 ? "-" : $"{view.rttMs} ms";
            string state = StateText(view);

            // Kumanda tokeni hiç bilgi taşımıyorsa (iki el de "bildirilmedi") satır onunla şişmez.
            string line = string.IsNullOrEmpty(controllers)
                ? $"SKOR {view.score} · {battery} · {ping} · {state}"
                : $"SKOR {view.score} · {battery} · {controllers} · {ping} · {state}";

            // ⚠️ Son yeniden yükleme denemesinin gerekçesi satırda KALICI durur (§10.6): uyarı
            // penceresi birkaç saniye sonra kendini kapatıyor ve kapandığında hiçbir iz kalmasaydı
            // operatör "bir şey oldu ama neydi" sorusuyla baş başa kalırdı. Alanı SUNUCU tutuyor,
            // yani sonradan bağlanan ikinci operatör de aynı gerekçeyi görür; başarılı bir
            // kalibrasyon onu temizlediği için satır kendi kendini toparlar.
            return string.IsNullOrEmpty(view.calibrationError)
                ? line
                : $"{line} · <color=#{ColorUtility.ToHtmlStringRGB(UiKit.Bad)}>{view.calibrationError}</color>";
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
    }
}
