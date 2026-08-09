using UnityEngine;
using VortexArena.Core;
using VortexArena.Core.Combat;
using VortexArena.Core.Player;
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
    /// <b>Tur içinde ölen oyuncu da yönlendirilir</b> (canlanma yok — <c>reviveAnchor:none</c>):
    /// faz <c>playing</c> + yerel oyuncu ölüyken yönerge "Öldün — X tabanına dön" yazar. Ölünün
    /// tek işi tabana yürümektir ve erken yönlendirme toplanmayı kısaltır. Bu akışta
    /// <c>set_ready</c> GÖNDERİLMEZ — bayrağın toplanmadaki anlamı (§10.1) tur içinde kirlenmez;
    /// sunucu toplanmaya girerken bayrakları zaten sıfırlıyor, erken gönderim boşa yayın olurdu.
    /// </para>
    ///
    /// <para>
    /// <b>Tabana giriş elden de onaylanır:</b> kendi bölgesine GİRİŞ kenarında iki kumandaya üç
    /// kısa darbe (<see cref="ControllerHaptics"/>) — göz o an yönergede olmasa da "doğru
    /// yerdesin" bilgisi ulaşır. Ölçüt fail-open'lı "hazır sayıldı" değil GERÇEK bölge girişidir:
    /// taban hiç bulunamadığında titreşim yalan olurdu.
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

        /// <summary>Toplanma raporlaması açık mı (yalnız <c>paused/mode</c> ve ondan gelen geri
        /// sayım) — <c>set_ready</c> yalnız bu bayrak açıkken gönderilir.</summary>
        private bool _active;

        private bool _reported;

        /// <summary>Yönergeyi şu an biz mi yazıyoruz (toplanma YA DA tur içi ölüm kılavuzu) —
        /// <see cref="Leave"/> yalnız kendi yazdığını temizlesin.</summary>
        private bool _guiding;

        /// <summary>Bir önceki karede kendi tabanının İÇİNDE miydi — giriş kenarında titreşim.</summary>
        private bool _wasInsideBase;

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

            // Tur içi ölüm: sunucu turu bitirene kadar oyuncu ölü bekler (canlanma yok) —
            // yönlendirme o beklemede de yazılır (sınıf dokümanı).
            bool deadInRound = combat.Phase == ArenaProtocol.PHASE_PLAYING && !combat.IsAlive;

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
            else if (countdown && _active)
            {
                // ⚠️ Geri sayımda YALNIZ zaten toplanmadan geliyorsak sürdürürüz. Maçın İLK geri
                // sayımı da `paused/countdown`tur ama ondan önce toplanma olmadığı için _active
                // false'tur ve aşağıdaki dallara düşer — orada kimseyi tabana çağırmıyoruz.
            }
            else if (deadInRound)
            {
                _active = false; // yönerge var, set_ready yok (sınıf dokümanı)
            }
            else
            {
                Leave();
                return;
            }

            _guiding = true;

            // Taban takibi ÖLÜ olmasak da gerekiyor: toplanmada herkes tabanına döner.
            combat.RequestBaseTracking();

            // Sahnede açık taban bölgesi yoksa (kurulum eksik) oyuncuyu kilitleme — hazır say.
            bool inBase = combat.IsInsideOwnBase || !combat.HasOpenBaseZone;

            // ⚠️ Yönerge takımı ADIYLA söyler ("MAVİ tabanına dön"). Oyuncunun kendi takımını
            // öğrenebileceği başka hiçbir yer YOKTUR: kendi gövdesini görmez, HUD skor satırı iki
            // takımı da yazar ve iki şerit arenanın zıt uçlarındadır — takımı yazmayan bir yönerge
            // oyuncuyu en YAKIN şeride yürütür, orası rakibin tabanıysa kapı hiç açılmaz ve bu
            // sunucuda "toplanma bekleniyor" diye görünür (hatasız, sebepsiz).
            string baseName = BaseLabel(combat.Team);
            if (_active)
            {
                combat.SetModePrompt(countdown
                    ? (inBase ? "Tur başlıyor — tabandan çıkma" : $"{baseName} dön — geri sayım iptal oluyor")
                    : (inBase ? "Tabandasın — diğerleri bekleniyor" : $"Yeni tur — {baseName} dön"));
            }
            else
            {
                combat.SetModePrompt(inBase
                    ? "Tabandasın — turun bitmesini bekle"
                    : $"Öldün — {baseName} dön, yeni tur orada başlayacak");
            }

            // Tabana GİRİŞ kenarı: iki kumandaya üç darbe (sınıf dokümanı — ölçüt GERÇEK giriş).
            bool insideBase = combat.IsInsideOwnBase;
            if (insideBase && !_wasInsideBase)
            {
                ControllerHaptics.PulseBoth(this);
            }
            _wasInsideBase = insideBase;

            if (!_active)
            {
                return; // tur içi ölüm kılavuzu: raporlanacak bir şey yok
            }

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

            // Kapının TAM durumu tek satırda: takım + iki bayrak + gönderilen değer. Kenarda
            // bastığı için tur başına birkaç satır. Bunsuz "toplanma bekleniyor" arızasının üç
            // ayrı sebebi (yanlış şerit · taban hiç bulunamadı · bayrak sunucuya ulaşmadı)
            // gözlükte birbirinden ayırt edilemiyor — üçü de aynı görünüyor.
            Debug.Log($"[Regroup] takım={combat.Team} kendiTabanında={combat.IsInsideOwnBase} " +
                      $"açıkTabanVar={combat.HasOpenBaseZone} → set_ready({inBase})");
        }

        /// <summary>Oyuncunun gideceği tabanın adı ("KIRMIZI tabanına"). Takım atanmamışsa
        /// (<see cref="Team.Neutral"/>) renk yazılmaz — her taban ona açıktır
        /// (<c>PlayerCombatState.EvaluateZones</c>).</summary>
        private static string BaseLabel(Team team)
        {
            switch (team)
            {
                case Team.Red: return "KIRMIZI tabanına";
                case Team.Blue: return "MAVİ tabanına";
                default: return "tabanına";
            }
        }

        /// <summary>Akıştan çıkış: yönergeyi temizler, bir sonraki girişe temiz başlanır.</summary>
        private void Leave()
        {
            _active = false;
            _reported = false;
            _wasInsideBase = false;

            if (!_guiding)
            {
                return;
            }

            _guiding = false;
            PlayerCombatState.Instance?.SetModePrompt("");
        }
    }
}
