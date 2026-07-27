using System;
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
    /// etiketleri, kamera hızı, duvar/çatı saydamlığı ve mini harita her operatörün kendi
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

        /// <summary>Sunucunun bildirdiği çevrimiçi admin sayısı (kendimiz dahil).</summary>
        public static int AdminCount { get; private set; }

        /// <summary>Son admin eyleminin duyurusu ("&lt;ad&gt;: &lt;eylem&gt;"); boş olabilir.</summary>
        public static string LastNotice { get; private set; } = "";

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
            bool changed = modeId != ModeId || sceneName != SceneName ||
                           msg.roundSeconds != RoundSeconds || msg.scoreLimit != ScoreLimit ||
                           msg.adminCount != AdminCount;

            ModeId = modeId;
            SceneName = sceneName;
            RoundSeconds = msg.roundSeconds;
            ScoreLimit = msg.scoreLimit;
            AdminCount = msg.adminCount;

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
            AdminCount = 0;
            LastNotice = "";
            Changed?.Invoke();
        }
    }
}
