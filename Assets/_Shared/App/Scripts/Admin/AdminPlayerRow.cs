using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using VortexArena.Protocol;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// Yan panellerdeki tek oyuncu satırı: takım şeridi, ad + <c>#id</c>, HP barı, K/D · batarya ·
    /// kumanda · durum ve eylem düğmeleri (POV · KAL · ÖLÇ · TAKIM · KİMLİK · CAN · AT).
    /// <c>CAN</c> ölü oyuncuyu operatörün elle canlandırmasıdır (§10.4) ve modun canlanma şartını
    /// geçer — şartı sağlayamayan oyuncu (donmuş istemci, tabanına yürüyemeyen oyuncu) aksi hâlde
    /// maçın sonuna kadar ölü kalır.
    /// <para>
    /// <b>Görünüm prefabtan gelir:</b> <c>Assets/_Shared/App/Resources/UI/AdminPlayerRow.prefab</c>.
    /// Bu sınıf yalnız <b>davranış</b>tır — yerleşim, punto ve statik renkler prefabta düzenlenir;
    /// <b>durum renkleri</b> (takım, can, seçim, kalibrasyon) koddan sürülür. Alanların
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
    /// <para>
    /// <b>ÖLÇ düğmesi</b> gövde ölçüsünü aldırır (§10.8) ve KAL gibi hem komut hem göstergedir:
    /// etiketi ölçülmüş çarpanı yazar. Tek adımlıdır (ölçüm geri alınabilir), kalibresiz oyuncuda
    /// pasiftir — sunucu o komutu zaten keser.
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

        /// <summary>Kalibreli ama zemin sapması eşiği aşan oyuncu (§10.6) — hizalama geçerli,
        /// gözlüğün alan verisi bayat. "?" bilinçli: "!" kalibresizliğin işareti
        /// (<see cref="LabelUncalibrated"/>), sembol/emoji ise etiketlerde yasak — şüphe
        /// soru işaretiyle anlatılır, o da her fontta var.</summary>
        private const string LabelFloorDrift = LabelCalibrated + " ?";

        /// <summary>Henüz ölçülmemiş oyuncunun ÖLÇ düğmesi.</summary>
        private const string LabelMeasure = "ÖLÇ";

        /// <summary>Son ölçümü başarısız olan oyuncunun ÖLÇ düğmesi (§10.8): çarpan yerine
        /// başarısızlığın kendisi yazar — operatör "bastım ama bir şey olmadı" sanmasın.</summary>
        private const string LabelMeasureFailed = "ÖLÇÜLEMEDİ";

        /// <summary>Canlandırma düğmesi. ⚠️ Diğer etiketlerle aynı kural: sembol/emoji YOK (TMP
        /// varsayılan fontunda garantisi yok) ve satır dar olduğu için kısa.</summary>
        private const string LabelRevive = "CAN";

        private const float DeadColorScale = 0.5f;

        /// <summary>Gözlük pili için uyarı eşikleri (0..1): altında sarı, kritiğin altında kırmızı.
        /// Eşik bir OPERATÖR kararıdır — %25 "seansı bitirmeden değiştir", %50 "gözün üstünde
        /// olsun". Aynı iki eşik istatistik panelinde de kullanılır
        /// (<see cref="FormatBattery"/> tek kaynaktır).</summary>
        private const float BatteryCritical = 0.25f;

        /// <inheritdoc cref="BatteryCritical"/>
        private const float BatteryLow = 0.5f;

        // ⚠️ Kumanda simgeleri ASCII: kalibrasyon etiketlerindeki (yukarı bak) ve kill feed'deki
        // gerekçenin aynısı — TMP varsayılan fontunda ●/◐/✕ garantisi yok, eksik glif □ çizilir ve
        // operatör üç durumu birbirinden ayıramaz. Ayrımı asıl taşıyan zaten RENKTİR; harf yalnız
        // renk körü bir operatör için yedek.
        private const string GlyphControllerOk = "+";
        private const string GlyphControllerUntracked = "~";
        private const string GlyphControllerLost = "X";
        private const string GlyphControllerUnknown = "-";

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
        [Tooltip("Gövde ölçüsünü aldırır (§10.8). Etiketi aynı zamanda GÖSTERGEDİR: " +
                 "ölçülmemişse 'ÖLÇ', ölçülmüşse çarpan.")]
        [SerializeField] private Button measureButton;
        [SerializeField] private TextMeshProUGUI measureLabel;
        [Tooltip("Ölü oyuncuyu canlandırır (§10.4). Yalnız ölü OYUNCU satırında etkindir.")]
        [SerializeField] private Button reviveButton;
        [Tooltip("Etiketin rengi durumu taşır (kullanılabilir/pasif), bu yüzden koddan sürülür.")]
        [SerializeField] private TextMeshProUGUI reviveLabel;
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

        /// <summary>Bağlı oyuncunun zemin sapması (§10.6) — KAL düğmesi onay penceresi dolduğunda
        /// da (<see cref="Tick"/>) yeniden çizildiği için <see cref="_calibrated"/> gibi satırda
        /// saklanır.</summary>
        private float _floorOffset;

        /// <summary>Kenarlığın ihlal DIŞINDAKİ rengi (seçim / kalibresizlik / normal).
        /// <para>⚠️ Saklanması zorunlu: ihlal yanıp sönerken kenarlığa her karede yazıyoruz ve
        /// ihlal bitince geri dönülecek bir renk gerekiyor — <see cref="Bind"/> ancak bir sonraki
        /// tazelemede koşar, o ana kadar satır kırmızı donmuş görünürdü.</para></summary>
        private Color _baseBorderColor = UiKit.Border;

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

            EnableStatsRichText();

            Wire(selectButton, () => _onSelect?.Invoke(_playerId));
            Wire(povButton, () => _onPov?.Invoke(_playerId));
            Wire(calibButton, PressCalibration);
            // ⚠️ Tek adımlı (onay penceresi YOK): ölçüm geri alınabilir bir eylemdir — yanlışlıkla
            // basılırsa yeniden ölçülür. "AT"/"KAL" gibi savaş dışı bırakan bir komut değildir.
            Wire(measureButton, () => AdminCommands.MeasureBodyScale(_playerId));
            // ⚠️ Tek adımlı (onay penceresi YOK): iki adımlı kilit ("AT"/"KAL") oyuncuyu savaş dışı
            // bırakan komutlar içindir. Canlandırma bunun TERSİDİR — oyuncuyu savaşa geri sokar ve
            // yanlış basış bir sonraki ölümle kendini düzeltir.
            Wire(reviveButton, () => AdminCommands.RevivePlayer(_playerId));
            Wire(teamButton, ToggleTeam);
            Wire(identifyButton, () => AdminCommands.Identify(_playerId));
            Wire(kickButton, PressKick);
        }

        /// <summary>
        /// İstatistik satırının zengin metin kapısı.
        /// <para>
        /// ⚠️ <b>Bayrak prefabta değil BURADA açılır:</b> <c>&lt;color=…&gt;</c> etiketlerini bu
        /// sınıf üretiyor (<see cref="BuildStatsLine"/>), yani bayrak bir görünüm tercihi değil
        /// üretilen metnin sözleşmesidir. Prefabta kapalı kaldığında kart pil/kumanda yerine
        /// <c>K:&lt;color=#…&gt;+</c> gibi ham etiket çizer ve bu sessiz bir bozulmadır — arayüzü
        /// düzenleyen kişi o kutucuğun ne işe yaradığını bilmek zorunda kalmasın.
        /// </para>
        /// <para>
        /// ⚠️ <b>Yalnız <see cref="statsText"/> içindir.</b> <see cref="nameText"/> zengin metne
        /// KAPALI kalır: oyuncu adı dışarıdan gelir ve <c>&lt;b&gt;</c> içeren bir ad satırın
        /// biçimini bozardı (aynı gerekçe <c>UiKit.Text</c>'te de yazılı).
        /// </para>
        /// </summary>
        private void EnableStatsRichText()
        {
            if (statsText != null)
            {
                statsText.richText = true;
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

            _playerId = view.playerId;
            _team = view.team;
            _calibrated = view.calibrated;
            _floorOffset = view.floorOffset;
            RefreshMeasureButton(view);
            RefreshReviveButton(view);

            Color team = UiKit.TeamColor(view.team);
            if (stripe != null)
            {
                stripe.color = view.alive ? team : UiKit.Dim(team, DeadColorScale);
            }

            // Kalibresiz satır seçili olmasa da kırmızı kenarlıkla ayrışır: operatör
            // listeye baktığında ilgilenmesi gereken satırı hemen görmeli (§10.6).
            _baseBorderColor = selected ? UiKit.Accent
                : view.NeedsCalibration ? UiKit.Bad : UiKit.Border;

            if (border != null)
            {
                border.color = _baseBorderColor;
            }

            // Sönüklük kademeli: bağlı 1.0, geri beklenen 0.7, ayrılmış 0.45. Ayrım görsel olarak
            // taşınmalı — "geri gelebilir" ile "gitti" operatör için farklı iki karardır.
            float alpha = view.IsConnected ? 1f : view.IsReconnecting ? ReconnectingAlpha : LeftAlpha;
            if (nameText != null)
            {
                // Ad TAKIM RENGİNDE yazılır. Kolon başlığı takımı zaten söylüyor ama operatör aynı
                // adı sahnedeki etikette, kuş bakışı işaretçisinde ve kill feed'de de görüyor;
                // rengin her yerde aynı olması "bu hangi takım" sorusunu okumadan cevaplatır.
                // ⚠️ Takımsız (FFA) oyuncuda rengin taşıyacağı bilgi YOKTUR — orada başlık rengi
                // kalır, çünkü nötr gri bir ad yalnız okunaksızlık üretirdi.
                // Canlılık yine SÖNÜKLÜKLE anlatılır: renk kanalı takıma ayrıldığı için ölü satır
                // aynı rengin karartılmışını kullanır (şeritle ve işaretçiyle aynı davranış).
                Color nameColor = IsTeamPlayer(view.team) ? team : UiKit.Title;
                nameText.color = UiKit.WithAlpha(
                    view.alive ? nameColor : UiKit.Dim(nameColor, DeadColorScale), alpha);
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
                // ⚠️ Satırın TABAN rengi burada, token renkleri zengin metinle: tek TMP'nin tek
                // `.color`'ı var, oysa pil ve kumanda kendi durumlarına göre ayrı ayrı renklenmeli.
                // Etiketlerin yorumlanmasını sağlayan bayrağı `EnableStatsRichText` açar.
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

        /// <summary>Oyuncunun bir takımı var mı — takım rengi yalnız burada anlamlıdır
        /// (FFA/atanmamış oyuncuda takım anahtarı boş gelir).</summary>
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
        /// Kenarlığın ihlal vurgusu (§10.9). Kuş bakışı halkaları varsayılan olarak yalnız kuş
        /// bakışında çizilir (<see cref="AdminMarkerVisibility.TopDownOnly"/>), yani POV/serbest
        /// kipteki operatör ihlali başka hiçbir yerde göremez — satır o boşluğu kapatır.
        /// <para>
        /// <b>Kenarlık önceliği: ihlal &gt; seçim &gt; kalibresiz &gt; normal.</b> İhlal seçimin
        /// önündedir çünkü seçim zaten iki yerde daha anlatılıyor (halka boyu, alt şerit) ve ihlal
        /// operatörün <i>şu an</i> ilgilenmesi gereken şeydir; kalibresizlik sahada tek seferlik
        /// bir iştir, ihlal ise sürüyor.
        /// </para>
        /// <para>⚠️ İhlal bitince <see cref="_baseBorderColor"/>'a GERİ DÖNÜLÜR — dönülmezse satır
        /// bir sonraki <see cref="Bind"/>'a kadar kırmızı donar.</para>
        /// <para>Renk yanıp söndüğü için tazeleme aralığına (<c>0.25 sn</c>) bırakılmaz: kare
        /// başına tek bayrak okuması + tek renk ataması yapar.</para>
        /// </summary>
        private void Update()
        {
            if (border == null)
            {
                return;
            }

            AdminViolationKind violation = AdminViolations.Of(_playerId);
            border.color = violation != AdminViolationKind.None
                ? AdminViolations.Blink(violation)
                : _baseBorderColor;
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

        /// <summary>
        /// ÖLÇ düğmesi hem KOMUT hem GÖSTERGEDİR (§10.8): ölçülmemişse "ÖLÇ", ölçülmüşse çarpanın
        /// kendisi yazar — operatör kimin ölçüldüğünü listeye bakarak görmeli, tek tek deneyerek
        /// değil.
        /// <para>Kalibresiz oyuncuda düğme <b>pasiftir</b>: sunucu o komutu zaten kesiyor (ölçü
        /// arena zeminine göre alınır), tepkisiz bir düğmeye bastırmak "gönderdim ama olmadı"
        /// hissi üretirdi. Ayrılmış satırda da hedef yoktur.</para>
        /// <para>⚠️ Son ölçüm <b>başarısızsa</b> (<c>scaleError</c> dolu) etiket çarpan yerine
        /// başarısızlığı yazar ve düğme ETKİN kalır — yapılacak iş tam da yeniden ölçmektir.
        /// Gerekçenin kendisi duyuru satırındadır; dar kart yalnız "bir sorun var"ı taşır.</para>
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

        /// <summary>
        /// CAN düğmesi yalnız CANLANDIRILACAK biri varken etkindir: rolü <c>player</c>, ölü ve
        /// ayrılmamış. Canlı satırda düğmenin yapacağı bir şey yoktur, ayrılmış satırda ise hedef
        /// yoktur (§10.2) — tepkisiz bir düğmeye bastırmak "gönderdim ama olmadı" hissi üretir.
        /// <para>Kalibresiz oyuncuda düğme <b>pasiftir</b> — <see cref="RefreshMeasureButton"/> ile
        /// aynı gerekçe: sunucu kalibresiz canlandırmayı zaten kesiyor (§10.6), etkin bir düğme
        /// operatöre gönderilmiş ama işlememiş bir komut hissi verirdi. Sebebi satır ayrıca
        /// söylüyor: kalibresiz kartın kenarlığı kırmızıdır ve KAL düğmesi ünlemli yanar.</para>
        /// <para>Engel kapısı (§10.9) burada YOKTUR ve eklenemez: ihlal bayrağı roster'da taşınmıyor.
        /// O kapıyı yalnız sunucu bilir, reddini kendi konsoluna gerekçesiyle yazar.</para>
        /// </summary>
        private void RefreshReviveButton(AdminPlayerView view)
        {
            bool usable = view.IsPlayer && !view.alive && view.calibrated && !view.HasLeft;
            SetInteractable(reviveButton, usable);

            if (reviveLabel != null)
            {
                reviveLabel.text = LabelRevive;
                reviveLabel.color = usable ? UiKit.Good : UiKit.Faint;
            }
        }

        /// <summary>
        /// KAL düğmesinin durumu. Kalibreli ama <b>zemin sapması eşiği aşan</b> oyuncu ayrı bir
        /// durumdur (§10.6): hizalama kabul edilmiştir, ama gözlüğün alan verisi bayattır ve
        /// sahada yapılacak bir iş vardır (veriyi temizleyip yeniden kalibre etmek). Yeşil tik
        /// bunu gizlerdi; sapma bu yüzden uyarı rengiyle işaretlenir.
        /// </summary>
        private void RefreshCalibrationButton()
        {
            bool armed = _calibArmedAt >= 0f;
            bool floorDrift = _calibrated &&
                              Mathf.Abs(_floorOffset) > ArenaProtocol.CALIB_FLOOR_WARN_METERS;

            if (calibLabel != null)
            {
                calibLabel.text = armed ? LabelConfirm
                    : !_calibrated ? LabelUncalibrated
                    : floorDrift ? LabelFloorDrift : LabelCalibrated;
                // Sapma uyarısı Accent, kalibresizlik Bad: repodaki "uyarı ama hata değil" tonu
                // Accent'tir (düşük pil, izlenmeyen kumanda) ve iki durum renkten ayrışmalı —
                // sapmalı oyuncu oynayabilir, kalibresiz oynayamaz.
                calibLabel.color = armed ? UiKit.OnAccent
                    : !_calibrated ? UiKit.Bad
                    : floorDrift ? UiKit.Accent : UiKit.Good;
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
            string battery = FormatBattery(view);
            string controllers = FormatControllers(view);
            string state = BuildState(view);

            // Kumanda tokeni hiç bilgi taşımıyorsa (iki el de "bildirilmedi") satır onunla
            // şişmez — dar kartta yer, okunmayan bir sütuna değil duruma ayrılır.
            return string.IsNullOrEmpty(controllers)
                ? $"{view.kills}/{view.deaths} · {battery} · {state}"
                : $"{view.kills}/{view.deaths} · {battery} · {controllers} · {state}";
        }

        /// <summary>
        /// GÖZLÜK pilinin metni; eşiğin altındaysa zengin metinle renklendirilmiş olarak döner.
        /// <c>-</c> = bilinmiyor (<c>battery &lt; 0</c>) ve renklenmez: bilinmeyen bir değeri
        /// alarm gibi göstermek operatörü boşuna koşturur.
        /// <para>İstatistik paneli de bunu çağırır — eşikler iki yerde ayrı yazılsaydı biri
        /// değiştiğinde aynı pil kartta kırmızı, tabloda sarı görünürdü.</para>
        /// </summary>
        internal static string FormatBattery(AdminPlayerView view)
        {
            if (view.battery < 0f)
            {
                return "-";
            }

            float level = Mathf.Clamp01(view.battery);
            string text = $"%{Mathf.RoundToInt(level * 100f)}";

            return level < BatteryCritical ? Colored(text, UiKit.Bad)
                : level < BatteryLow ? Colored(text, UiKit.Accent)
                : text;
        }

        /// <summary>
        /// Sol ve sağ kumandanın durumu tek token hâlinde (<c>K:</c> + iki simge), HER simge kendi
        /// rengiyle. İki el de bildirilmemişse <b>boş</b> döner (bkz. <see cref="BuildStatsLine"/>).
        /// <para>⚠️ Burada YÜZDE gösterilemez: kumandanın şarjı Quest'te OpenXR altında okunamıyor
        /// (§5.1). Operatörün eyleme çevirebileceği bilgi "bu el düştü mü"dür.</para>
        /// </summary>
        internal static string FormatControllers(AdminPlayerView view)
        {
            if (view.ctrlL == ArenaProtocol.CONTROLLER_UNKNOWN &&
                view.ctrlR == ArenaProtocol.CONTROLLER_UNKNOWN)
            {
                return "";
            }

            return $"K:{ControllerGlyph(view.ctrlL)}{ControllerGlyph(view.ctrlR)}";
        }

        private static string ControllerGlyph(int state)
        {
            switch (state)
            {
                case ArenaProtocol.CONTROLLER_OK:
                    return Colored(GlyphControllerOk, UiKit.Muted);
                case ArenaProtocol.CONTROLLER_UNTRACKED:
                    return Colored(GlyphControllerUntracked, UiKit.Accent);
                case ArenaProtocol.CONTROLLER_LOST:
                    return Colored(GlyphControllerLost, UiKit.Bad);
                default:
                    return Colored(GlyphControllerUnknown, UiKit.Faint);
            }
        }

        /// <summary>Tek token'ı TMP zengin metniyle renklendirir.
        /// <para>⚠️ Etiket <c>RGB</c>'dir, <c>RGBA</c> değil: TMP renklendirilen aralığın alfasını
        /// zaten tam opak yapar, dolayısıyla token başına alfa vermek yalnız görünmez bir metin
        /// üretme riski taşır. Renklenmeyen tokenler (normal pil, durum metni)
        /// <c>statsText.color</c>'ı olduğu gibi kullanmaya devam eder.</para></summary>
        private static string Colored(string text, Color color)
        {
            return $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{text}</color>";
        }

        /// <summary>Satırın durum metni. ⚠️ "çevrimdışı" diye bir durum YOKTUR (§2): kopan cihaz
        /// ya geri bekleniyor ya oyundan çıkarılmıştır — sayaç operatöre hangisi olduğunu söyler.
        /// <para>Canlı oyuncunun İHLALİ "HAZIR"/"bekliyor"un önüne geçer (§10.9): dar kartta tek
        /// durum yuvası var ve ikisinden operatörün eyleme çevirebileceği olan ihlaldir. Satır
        /// yalnız tazeleme aralığında yeniden yazılır — ihlalin canlı kanalı kenarlıktır
        /// (<see cref="Update"/>), bu yazı onun türünü söyler.</para>
        /// <para>⚠️ Ölü/kopmuş satırda gösterilmez: kenarlık ve halka ile aynı kural — ceza
        /// durmuştur, operatörün yapacağı bir şey yoktur.</para></summary>
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

            string violation = AdminViolations.Label(AdminViolations.Of(view.playerId));
            if (!string.IsNullOrEmpty(violation))
            {
                return violation;
            }

            return view.ready ? "HAZIR" : "bekliyor";
        }
    }
}
