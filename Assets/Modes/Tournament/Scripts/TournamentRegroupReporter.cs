using UnityEngine;
using VortexArena.Core.Combat;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Modes.Tournament
{
    /// <summary>
    /// Turlar arası <b>toplanma bildirimi</b>: oyuncu kendi taban bölgesine girdiğinde sunucuya
    /// <c>set_ready{true}</c>, çıktığında <c>set_ready{false}</c> yollar. Sunucu herkesi hazır
    /// görünce yeni turun geri sayımını başlatır (§10.1 "tur tabanlı modlar").
    ///
    /// <para>
    /// <b>Bildirim geri sayım boyunca da sürer.</b> Kural "tabanda BEKLE"dir, "tabana uğra" değil:
    /// geri sayım sırasında tabanından çıkan tek oyuncu turu erteler (sunucu geri sayımı iptal
    /// edip toplanmaya döner). Bu yüzden bileşen <c>paused/mode</c> ile birlikte, ondan gelen
    /// <c>paused/countdown</c>'da da çalışır — maçın İLK geri sayımında ise çalışmaz.
    /// </para>
    ///
    /// <para>
    /// <b>Yeni bir protokol mesajı YOKTUR:</b> <c>ready</c> bayrağı yükleme kapısında zaten
    /// "hazırım" demek. Yan faydası, operatörün admin roster'ında kimin tabanına döndüğünü
    /// doğrudan görmesidir.
    /// </para>
    /// <para>
    /// <b>Neden HUD sınıfında değil:</b> <see cref="Core.UI.ModeHudBase"/> bir SUNUM bileşenidir —
    /// girdi toplamaz, sunucuya bir şey bildirmez. Ayrı bileşen aynı prefabın kökünde durduğu için
    /// yine de <b>sahne kurulumu gerektirmez</b> ve HUD ile aynı ömre sahiptir: HUD yalnız
    /// <c>role=player</c> için örneklendiğinden admin gözlemci hiç <c>set_ready</c> göndermez ve
    /// toplanma kapısını bozmaz.
    /// </para>
    /// <para>
    /// "Tabanda mıyım" kararı istemcinindir — sunucu hakemlik değil defter tutar (§10.3 felsefesi,
    /// <c>reviveAnchor</c> ile aynı sözleşme). Sunucunun emniyeti kendi zaman aşımıdır.
    /// </para>
    /// </summary>
    public class TournamentRegroupReporter : MonoBehaviour
    {
        // DTO'yu her karede yeniden ayırmamak için tek örnek.
        private readonly SetReadyMsg _msg = new SetReadyMsg();

        private bool _active;
        private bool _reported;

        private void OnDisable()
        {
            // Sahne/HUD gidiyor: yönergeyi bırakma, kalıcı tekilde asılı kalır.
            Leave();
        }

        private void Update()
        {
            PlayerCombatState combat = PlayerCombatState.Instance;
            if (combat == null)
            {
                return;
            }

            bool paused = combat.Phase == ArenaProtocol.PHASE_PAUSED;

            // Çekirdek mod duraklamasını yalnız mod koyar (§10.1) — koşan tek mod da biziz.
            bool modePause = paused && combat.PhaseReason == ArenaProtocol.PAUSE_REASON_MODE;
            bool countdown = paused && combat.PhaseReason == ArenaProtocol.PAUSE_REASON_COUNTDOWN;

            if (modePause)
            {
                if (!_active)
                {
                    // Sunucu toplanmaya girerken TÜM ready bayraklarını temizliyor (§10.1) — yerel
                    // başlangıç durumu onunla aynı olmalı ki ilk "tabandayım" bir KENAR olsun.
                    _active = true;
                    _reported = false;
                }
            }
            else if (!(countdown && _active))
            {
                // ⚠️ Geri sayımda YALNIZ zaten toplanmadan geliyorsak sürdürürüz. Maçın İLK geri
                // sayımı da `paused/countdown`tur ama ondan önce toplanma olmadığı için _active
                // false'tur ve bu dala düşer — orada kimseyi tabana çağırmıyoruz.
                Leave();
                return;
            }

            // Taban takibi ÖLÜ olmasak da gerekiyor: toplanmada herkes tabanına döner.
            combat.RequestBaseTracking();

            // Sahnede açık taban bölgesi yoksa (kurulum eksik) oyuncuyu kilitleme — hazır say.
            bool inBase = combat.IsInsideOwnBase || !combat.HasOpenBaseZone;

            combat.SetModePrompt(countdown
                ? (inBase ? "Tur başlıyor — tabandan çıkma" : "Tabanına dön — geri sayım iptal oluyor")
                : (inBase ? "Tabandasın — diğerleri bekleniyor" : "Yeni tur — tabanına dön"));

            // ⚠️ Yalnız KENARDA gönderilir, periyodik tekrar YOK: her set_ready sunucuda bir TAM
            // lobby_state yayını tetikliyor (oyuncu sayısıyla çarpan fan-out). Kanal WS/TCP
            // olduğu için tekrara gerek de yok — kaybolmaz.
            if (inBase == _reported)
            {
                return;
            }

            _reported = inBase;
            _msg.ready = inBase;
            ArenaClient.Instance?.Send(_msg);
        }

        /// <summary>Toplanmadan çıkış: yönergeyi temizler, bir sonraki toplanmaya temiz girilir.</summary>
        private void Leave()
        {
            if (!_active)
            {
                return;
            }

            _active = false;
            _reported = false;
            PlayerCombatState.Instance?.SetModePrompt("");
        }
    }
}
