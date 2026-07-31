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

            // Çekirdek mod duraklamasını yalnız mod koyar (§10.1) — koşan tek mod da biziz.
            bool regrouping = combat.Phase == ArenaProtocol.PHASE_PAUSED &&
                              combat.PhaseReason == ArenaProtocol.PAUSE_REASON_MODE;

            if (!regrouping)
            {
                Leave();
                return;
            }

            if (!_active)
            {
                // Sunucu toplanmaya girerken TÜM ready bayraklarını temizliyor (§10.1) — yerel
                // başlangıç durumu onunla aynı olmalı ki ilk "tabandayım" bir KENAR olsun.
                _active = true;
                _reported = false;
            }

            // Taban takibi ÖLÜ olmasak da gerekiyor: toplanmada herkes tabanına döner.
            combat.RequestBaseTracking();

            // Sahnede açık taban bölgesi yoksa (kurulum eksik) oyuncuyu kilitleme — hazır say.
            bool inBase = combat.IsInsideOwnBase || !combat.HasOpenBaseZone;

            combat.SetModePrompt(inBase
                ? "Tabandasın — diğerleri bekleniyor"
                : "Yeni tur — tabanına dön");

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
