using System;
using System.Collections.Generic;
using UnityEngine;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// Adminler arası <b>ortak</b> durumun istemci tarafı aynası (§5.3 <c>admin_state</c>):
    /// bir sonraki maçın mod/harita seçimi, son admin eyleminin duyurusu ve çevrimiçi admin sayısı.
    /// <para>
    /// <b>Otorite sunucudadır.</b> Arayüzdeki mod/harita seçicileri yerel bir değişkeni değil
    /// <c>set_selection</c> ile sunucudaki seçimi değiştirir; sunucu onu tüm adminlere geri yayar
    /// ve buradan okunur. Bu yüzden iki operatör aynı ekranı görür — biri haritayı değiştirdiğinde
    /// diğerinin paneli (kapalı olsa bile) yeni değere döner ve yerel önizlemesi o arenayı açar.
    /// </para>
    /// <para>
    /// ⚠ <b>Görünüm tercihleri buraya GİRMEZ.</b> Kamera kipi, seçili oyuncu, halkalar, ad
    /// etiketleri, kamera hızı ve çatı saydamlığı her operatörün kendi
    /// ekranına aittir; onlar <see cref="AdminSession"/>'da yerel <c>PlayerPrefs</c> olarak kalır.
    /// İki operatörün kameralarını birbirine bağlamak yönetimi kolaylaştırmaz, imkânsızlaştırır.
    /// </para>
    /// <para>
    /// Durum ve olay <b>statiktir</b> (<see cref="AdminSession"/> deseni): dinleyiciler bileşenin
    /// ne zaman kurulduğunu bilmek zorunda kalmasın. Bileşenin kendisi yalnız ağ olayı pompasıdır
    /// ve <see cref="AdminSpectator"/> tarafından <c>AddComponent</c> ile kurulur.
    /// </para>
    /// </summary>
    public class AdminSelection : MonoBehaviour
    {
        /// <summary>Ortak seçim, duyuru veya admin sayısı değiştiğinde (ana thread).</summary>
        public static event Action Changed;

        /// <summary>Ortak seçim: mod kimliği (sunucudan gelir, hiç seçilmediyse boş).</summary>
        public static string ModeId { get; private set; } = "";

        /// <summary>Ortak seçim: harita sahne adı (sunucudan gelir, hiç seçilmediyse boş).</summary>
        public static string SceneName { get; private set; } = "";

        /// <summary>Ortak seçim: bir sonraki maçın raund süresi (sn); <c>0</c> = seçilmedi,
        /// modun varsayılanı kullanılacak (§5.2).</summary>
        public static int RoundSeconds { get; private set; }

        /// <summary>Ortak seçim: bir sonraki maçın skor limiti; <c>0</c> = modun varsayılanı.</summary>
        public static int ScoreLimit { get; private set; }

        /// <summary>Ortak seçim: geri sayımın uzunluğu (sn); <c>0</c> = protokol varsayılanı
        /// (<c>COUNTDOWN_SECONDS</c>). Tur tabanlı modlarda turlar arasındaki geri sayım da
        /// budur (§5.2).</summary>
        public static int CountdownSeconds { get; private set; }

        /// <summary>
        /// Dost ateşi anahtarının YÜRÜRLÜKTEKİ değeri (§5.2) — diğerleri gibi bir "seçim" değildir:
        /// koşan maçta da geçerlidir ve etkisi anlıktır.
        /// <para>Bu yüzden seçim kilidine (<c>AdminRoster.CanChangeSelection</c>) girmez: maç
        /// kuruluyken de değiştirilebilir.</para>
        /// </summary>
        public static bool FriendlyFire { get; private set; }

        /// <summary>
        /// Kalibre modunun YÜRÜRLÜKTEKİ değeri (<c>ArenaProtocol.CALIB_MODE_*</c>, §5.2) —
        /// <see cref="FriendlyFire"/> ile aynı sınıf: seçim değil anlık durum, seçim kilidine
        /// girmez. Boş = henüz <c>admin_state</c> gelmedi (arayüz sunucu varsayılanını gösterir).
        /// </summary>
        public static string CalibrationMode { get; private set; } = "";

        /// <summary>Sunucunun bildirdiği çevrimiçi admin sayısı (kendimiz dahil).</summary>
        public static int AdminCount { get; private set; }

        /// <summary>Son admin eyleminin duyurusu ("&lt;ad&gt;: &lt;eylem&gt;"); boş olabilir.</summary>
        public static string LastNotice { get; private set; } = "";

        /// <summary>Sunucunun bu oturumda açtığı mekan (§11); mekan ayrımı yoksa boş.</summary>
        public static string VenueId { get; private set; } = "";

        /// <summary>
        /// Bu mekanda oynatılabilen sahne adları — harita seçicisinin süzgeci.
        /// <para><b>Boş = süzme yok</b> (mekan ayrımı olmayan sunucu ya da henüz admin_state
        /// gelmedi). Katalog tüm projeyi tanır; hangi arenaların oynatılabildiğine sunucu karar
        /// verir, bu yüzden liste yerelde üretilmez.</para>
        /// </summary>
        public static IReadOnlyList<string> VenueScenes => _venueScenes;

        /// <summary>
        /// Mekan süzgecinin sürümü — <see cref="VenueScenes"/> her değiştiğinde artar.
        /// <para>
        /// Harita seçicisi listesini bu sayıya bakarak yeniden süzer. Gerekli, çünkü liste
        /// <b>bağlantıdan önce</b> kuruluyor (panel <c>Initialize</c>) ve o an süzgeç henüz boştur:
        /// ilk <c>admin_state</c> gelene kadar tüm projenin arenaları geçerli görünür. "Seçim
        /// değişti mi" sorusu bunu yakalamaz — mekan bilgisi seçimden bağımsız gelir.
        /// </para>
        /// </summary>
        public static int VenueVersion { get; private set; }

        private static string[] _venueScenes = Array.Empty<string>();

        /// <summary>Sahne bu mekanda oynatılabilir mi. Liste boşsa herkes geçer.</summary>
        public static bool IsInVenue(string sceneName)
        {
            if (_venueScenes.Length == 0 || string.IsNullOrEmpty(sceneName))
            {
                return true;
            }

            for (int i = 0; i < _venueScenes.Length; i++)
            {
                if (string.Equals(_venueScenes[i], sceneName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void OnEnable()
        {
            NetEvents.OnAdminState += HandleAdminState;
            NetEvents.OnDisconnected += HandleDisconnected;
        }

        private void OnDisable()
        {
            NetEvents.OnAdminState -= HandleAdminState;
            NetEvents.OnDisconnected -= HandleDisconnected;
        }

        private static void HandleAdminState(AdminStateMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            string modeId = msg.modeId ?? "";
            string sceneName = msg.sceneName ?? "";
            string calibrationMode = msg.calibrationMode ?? "";
            bool changed = modeId != ModeId || sceneName != SceneName ||
                           calibrationMode != CalibrationMode ||
                           msg.roundSeconds != RoundSeconds || msg.scoreLimit != ScoreLimit ||
                           msg.countdownSeconds != CountdownSeconds ||
                           msg.friendlyFire != FriendlyFire ||
                           msg.adminCount != AdminCount;

            string venueId = msg.venueId ?? "";
            string[] venueScenes = msg.venueScenes ?? Array.Empty<string>();
            bool venueChanged = venueId != VenueId || !SameScenes(venueScenes, _venueScenes);
            changed |= venueChanged;
            if (venueChanged)
            {
                VenueVersion++;
            }

            ModeId = modeId;
            SceneName = sceneName;
            RoundSeconds = msg.roundSeconds;
            ScoreLimit = msg.scoreLimit;
            CountdownSeconds = msg.countdownSeconds;
            FriendlyFire = msg.friendlyFire;
            CalibrationMode = calibrationMode;
            AdminCount = msg.adminCount;
            VenueId = venueId;
            _venueScenes = venueScenes;

            // Duyuru komutu GÖNDEREN admin'de de görünür: "kim ne yaptı" tek satırda toplansın
            // (AdminCommands.Status ile aynı yer — yerel "gönderildi" metnini sunucunun doğruladığı
            // metin ezer, doğru sıralama budur).
            if (!string.IsNullOrEmpty(msg.notice))
            {
                LastNotice = msg.notice;
                AdminCommands.Note(msg.notice);
                changed = true;
            }

            if (changed)
            {
                Changed?.Invoke();
            }
        }

        private static void HandleDisconnected()
        {
            // Bağlantı yokken ortak durum bilinmiyor; seçim değerlerini uydurmayız (son bilinen
            // kalır, panel yine de "bağlı değil" yazar), yalnız sayaç/duyuru temizlenir.
            // Mekan süzgeci de KORUNUR: bağlantı koptu diye seçiciyi tüm projeye açmak, operatöre
            // başka mekanların arenalarını gösterip yeniden bağlanınca geri almak olurdu.
            AdminCount = 0;
            LastNotice = "";
            Changed?.Invoke();
        }

        private static bool SameScenes(string[] a, string[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
