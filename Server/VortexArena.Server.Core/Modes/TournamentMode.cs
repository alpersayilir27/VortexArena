#nullable enable
namespace VortexArena.Server.Core.Modes;

/// <summary>
/// Turnuva: <b>tur tabanlı takım elemesi</b> ("Search &amp; Destroy"ın bombasız hâli). Tur içinde
/// canlanma YOKTUR; bir takımın tüm çevrimiçi oyuncuları ölünce tur biter ve kazanan takıma
/// <b>+1 tur</b> yazılır. Maç, bir takım <see cref="MatchDirector.ScoreLimit"/> tura ulaşınca biter.
///
/// <para><b>Kurallar TDM varsayılanından TEK noktada ayrılır</b> (§10.5):
/// <see cref="ReviveAnchor.None"/>. Tur kavramı bir kural alanı DEĞİLDİR ve
/// <see cref="ModeRules"/>'a girmez — turlar bu sınıfın iç durumudur, çekirdek onları bilmez
/// (§10.1 "tur tabanlı modlar"). Telde görünen tek izleri <c>modeState</c> ve tur başındaki
/// <c>health_update</c>'lerdir.</para>
///
/// <para><b>Skorun anlamı bu modda değişir:</b> <c>scoreRed</c>/<c>scoreBlue</c> öldürme değil
/// <b>kazanılan tur</b> sayar; <c>roundSeconds</c> ise <b>turun</b> süresidir, maçın değil.</para>
/// </summary>
public sealed class TournamentMode : IGameMode
{
    /// <summary>
    /// Toplanma uzarsa konsola bu aralıkla "kim eksik" satırı düşer.
    /// <para>⚠️ <b>Bir zaman aşımı DEĞİLDİR</b> — turu başlatmaz, yalnız teşhis basar: toplanmanın
    /// tek çıkışı herkesin tabanına girmesi ya da operatörün oyuncuyu atması/maçı iptal etmesidir
    /// (§10.1). Sunucu penceresi operatörün kimin takıldığını görebildiği tek yer.</para>
    /// </summary>
    private const double RegroupReportIntervalSeconds = 30.0;

    /// <summary>
    /// Turlar arası akışın nerede olduğu. ⚠️ Çekirdeğin duraklama gerekçesinin KOPYASI değildir:
    /// mod, faz <c>Playing</c> değilken üç ayrı durumda tik alabiliyor (maçın ilk geri sayımı,
    /// kendi toplanmamız, kendi geri sayımımız) ve üçünde yapılacak iş farklı. Çekirdekten
    /// <c>PauseReason</c> okumak yerine kendi kararımızı hatırlıyoruz — duraklamayı koyan biziz.
    /// </summary>
    private enum RoundStage
    {
        /// <summary>Turlar arası akış çalışmıyor: maçın ilk geri sayımı ya da tur içi.</summary>
        None,

        /// <summary>Kendi koyduğumuz duraklama — herkesin tabanına dönmesi bekleniyor.</summary>
        Regroup,

        /// <summary>Toplanma bitti, tur geri sayımı işliyor (henüz geri alınabilir).</summary>
        Countdown
    }

    private RoundStage _stage;

    private int _round;

    /// <summary>Tur şu an savaşta mı — eleme taraması yalnız o zaman koşar. Geri sayım sırasında
    /// <c>false</c>'tur ki henüz başlamamış bir tur "herkes ölü" diye bitmesin.</summary>
    private bool _roundLive;

    private bool _matchOver;
    private MatchOutcome _outcome = MatchOutcome.Draw;

    /// <summary>Bir sonraki "toplanma bekleniyor" konsol satırının zamanı (teşhis, bkz.
    /// <see cref="RegroupReportIntervalSeconds"/>).</summary>
    private DateTime _nextRegroupReportAt;

    public string ModeId => "tournament";

    /// <summary>
    /// <c>Revive = None</c>: tur içinde canlanma yoktur (§10.4). <c>RespawnDelay = 0</c> bunun
    /// tamamlayıcısıdır — canlanma hiç olmayacağına göre istemciye "canlanmaya 5 sn" diye
    /// gerçekleşmeyecek bir geri sayım göstertmenin anlamı yok.
    /// <para>Geri kalan her alan bilinçli olarak varsayılandır: iki takım, takım skoru, dost ateşi
    /// kapalı, sahnede duran silah (turnuvada şarjör/yedek şarjör muhasebesi işlesin diye —
    /// <c>RandomGrant</c>'te reload kapalıdır).</para>
    /// </summary>
    public ModeRules Rules => new()
    {
        Revive = ReviveAnchor.None,
        RespawnDelay = 0f
    };

    /// <summary>⚠️ <b>TURUN</b> süresi (saniye), maçın değil.</summary>
    public int DefaultRoundSeconds => 120;

    /// <summary>Maçı kazanmak için gereken TUR sayısı → best-of-7.</summary>
    public int DefaultScoreLimit => 4;

    public void OnMatchStart(MatchDirector director)
    {
        // Mod örneği sunucu ömrü boyunca tektir ve maçlar arasında yeniden kullanılır → durum
        // BURADA sıfırlanır. (OnMatchStart maç başına bir kez çağrılır; turlar onu tetiklemez.)
        _round = 1;
        _roundLive = false;
        _matchOver = false;
        _outcome = MatchOutcome.Draw;
        _stage = RoundStage.None;

        Console.WriteLine($"[tournament] maç başladı — tur {director.RoundSeconds} sn, " +
                          $"{director.ScoreLimit} tur galibiyet (en fazla {MaxRounds(director.ScoreLimit)} tur).");
    }

    public void OnRoundStart(MatchDirector director)
    {
        _roundLive = true;
        _stage = RoundStage.None;
        director.SetModeState($"round:{_round}");
        Console.WriteLine($"[tournament] tur {_round} başladı " +
                          $"(kırmızı {director.ScoreRed} : mavi {director.ScoreBlue}).");
    }

    // ⚠️ OnKill YAZILMAZ ve gerekmez: turnuvada puanı öldürme değil TUR kazandırır, bireysel
    // K/D'yi ise sunucu zaten sayıyor (§10.2). Eleme kontrolü de burada değil OnTick'tedir —
    // gerekçesi EvaluateRound'da.

    public void OnTick(MatchDirector director, float deltaSeconds)
    {
        if (_matchOver) return;

        if (director.CurrentPhase == Phase.Playing)
        {
            if (_roundLive) EvaluateRound(director);
            return;
        }

        // Faz Playing değil. Çekirdek moda burada üç ayrı durumda tik verebiliyor ve üçünde
        // yapılacak iş farklı — ayrımı kendi aşamamızdan yapıyoruz (bkz. RoundStage).
        switch (_stage)
        {
            case RoundStage.Regroup:
                TickRegroup(director);
                break;
            case RoundStage.Countdown:
                TickCountdownWatch(director);
                break;
            // RoundStage.None: buraya düşen tek durum maçın İLK geri sayımıdır (yükleme kapısından
            // geliyor, toplanmadan değil). Turlar arası akış henüz başlamadı; burada toplanma
            // yoklamak ilk tur başlamadan HUD'a "TOPLANMA" yazdırırdı.
        }
    }

    /// <summary>⚠️ <b><c>TimeRemaining &lt;= 0</c> burada maçı BİTİRMEZ</b> (TDM/FFA'dan ayrıldığı
    /// yer): bu modda <c>timeRemaining</c> <b>turun</b> sayacıdır. Süre dolunca tur biter, maç
    /// değil. Maç kararı yalnız <see cref="EndRound"/>'da verilir ve bu metod onu taşır.</summary>
    public bool IsMatchOver(MatchDirector director, out MatchOutcome outcome)
    {
        outcome = _outcome;
        return _matchOver;
    }

    // ---------------------------------------------------------------- tur akışı

    /// <summary>
    /// Turun bitip bitmediğini ölçer (10 Hz).
    /// <para>
    /// ⚠️ <b>Neden <see cref="OnKill"/> değil de tik:</b> bir takım yalnız öldürmeyle boşalmaz,
    /// <b>bağlantı kopmasıyla</b> da boşalır — ve o yolda <c>OnKill</c> hiç çağrılmaz. İki ayrı
    /// tetikleyici iki ayrı kod yolu (ve kaçınılmaz olarak biri unutulmuş bir kural) demekti;
    /// tek tarama tek doğruluk kaynağıdır. Maliyeti ihmal edilebilir (≤ birkaç düzine oyuncu).
    /// </para>
    /// </summary>
    private void EvaluateRound(MatchDirector director)
    {
        int redOnline = 0, blueOnline = 0;
        int redAlive = 0, blueAlive = 0;
        // "Savaşabilir" = canlı VE kalibreli. Kalibresiz oyuncu ne ateş eder ne hasar yer (§10.6),
        // yani sahada yok sayılır.
        int redFit = 0, blueFit = 0;

        foreach (var player in director.OnlinePlayers())
        {
            var isRed = player.Team == "red";
            var isBlue = player.Team == "blue";
            if (!isRed && !isBlue) continue; // takımı atanmamış geç katılan — tura dahil değil

            if (isRed) redOnline++; else blueOnline++;
            if (!player.Alive) continue;
            if (isRed) redAlive++; else blueAlive++;
            if (!player.Calibrated) continue;
            if (isRed) redFit++; else blueFit++;
        }

        // Tek taraflı maç (admin harita önizlemesi ya da bir takımın tamamen düşmesi): tur
        // ilerlemez, yoksa sunucu boş bir arenada saniyede bir tur dağıtırdı. Çıkış operatörün
        // abort_match'idir — §10.1'deki "oyuncusuz maç" kararının aynısı.
        if (redOnline == 0 || blueOnline == 0) return;

        // ELEME: kalibrasyona BAKILMAZ — kalibresiz oyuncu ölü değildir, takımını ayakta tutar.
        // Sonucu doğru: o tur süreye gider ve aşağıdaki kıyas onu zaten dışarıda bırakır.
        if (redAlive == 0 && blueAlive == 0)
        {
            EndRound(director, "", "karşılıklı eleme");
            return;
        }
        if (blueAlive == 0)
        {
            EndRound(director, "red", "mavi takım elendi");
            return;
        }
        if (redAlive == 0)
        {
            EndRound(director, "blue", "kırmızı takım elendi");
            return;
        }

        if (director.TimeRemaining > 0f) return;

        // Süre doldu: çok kişi ayakta kalan kazanır, eşitlikte kimseye puan yok.
        var winner = redFit > blueFit ? "red" : blueFit > redFit ? "blue" : "";
        EndRound(director, winner, $"süre doldu ({redFit} - {blueFit} savaşabilir ayakta)");
    }

    /// <summary>Turu kapatır: puanı yazar, maç bitti mi diye bakar, bitmediyse toplanmaya geçer.</summary>
    private void EndRound(MatchDirector director, string winnerTeam, string reason)
    {
        _roundLive = false;
        if (winnerTeam.Length > 0) director.AddScore(winnerTeam, 1);

        Console.WriteLine($"[tournament] tur {_round} bitti — {reason}; " +
                          $"{(winnerTeam.Length > 0 ? winnerTeam + " +1" : "puan yok")} " +
                          $"(kırmızı {director.ScoreRed} : mavi {director.ScoreBlue}).");

        var limit = director.ScoreLimit;
        if (limit > 0 && director.ScoreRed >= limit)
        {
            Decide(MatchOutcome.Team("red"), "skor limiti");
            return;
        }
        if (limit > 0 && director.ScoreBlue >= limit)
        {
            Decide(MatchOutcome.Team("blue"), "skor limiti");
            return;
        }

        // Tur tavanı: berabere biten turlar yüzünden kimse limite ulaşamayabilir — maç sonsuza
        // kadar sürmesin. Tavanda yüksek skor kazanır, eşitse berabere.
        if (_round >= MaxRounds(limit))
        {
            var red = director.ScoreRed;
            var blue = director.ScoreBlue;
            Decide(red > blue ? MatchOutcome.Team("red")
                : blue > red ? MatchOutcome.Team("blue")
                : MatchOutcome.Draw, "tur tavanı");
            return;
        }

        _round++;

        // Toplanma: herkes fiziksel olarak kendi tabanına yürüyecek (free-roam — kimse
        // ışınlanmaz, §10.4).
        if (!director.TryPauseForMode("regroup:0/0"))
        {
            // Araya abort_match/operatör duraklatması girmiş olabilir. Faz artık Playing
            // değilse tur akışı zaten bizim elimizde değildir; sessiz kalmak yerine yazıyoruz.
            Console.WriteLine("[tournament] toplanmaya geçilemedi (faz değişmiş) — tur akışı durdu.");
            return;
        }

        _stage = RoundStage.Regroup;
        ScheduleRegroupReport();
    }

    /// <summary>
    /// Turlar arası toplanma (faz <c>paused</c>/<c>mode</c>): herkes kendi taban bölgesine girip
    /// <c>set_ready{true}</c> yollayınca yeni tur geri sayımı başlar.
    /// <para><b>Kapı yeni bir protokol mesajı kullanmaz</b> — <c>ready</c> bayrağı yükleme
    /// kapısında zaten "hazırım" demek (§10.1). "Tabanda mıyım" kararı istemcinindir; sunucu
    /// hakemlik değil defter tutar (§10.3 felsefesi, <c>reviveAnchor</c> ile aynı sözleşme).</para>
    /// <para>
    /// ⚠️ <b>Zorunlu başlatma YOKTUR ve eklenmez:</b> eksik oyuncuyla açılan tur, tam kadro
    /// beklemenin varlık sebebini çiğniyordu (elenmiş sayılan oyuncu sahada, hazır olmayan oyuncu
    /// tur ortasında). Takılan bir başlık maçı süresiz bekletebilir — çıkışı operatörün
    /// <c>kick</c>'i ya da <c>abort_match</c>'idir. Atılan oyuncu <see cref="CountInBase"/>'in
    /// toplamından da düştüğü için tur, kalanlar hazırsa o tik başlar.
    /// </para>
    /// </summary>
    private void TickRegroup(MatchDirector director)
    {
        var (ready, total) = CountInBase(director);

        director.SetModeState($"regroup:{ready}/{total}"); // yalnız DEĞİŞTİYSE yayınlar

        if (total == 0) return; // kimse kalmadı — operatörün abort_match'ini bekle

        if (ready < total)
        {
            ReportRegroupWaiting(director, ready, total);
            return;
        }

        if (!director.TryStartRound()) return;

        _stage = RoundStage.Countdown;
        Console.WriteLine($"[tournament] tur {_round} geri sayımı başlıyor (herkes tabanında).");
    }

    /// <summary>Toplanma uzarsa periyodik teşhis satırı: kim eksik. Operatörün sunucu penceresinden
    /// göreceği tek bilgi budur; roster'daki HAZIR/bekliyor ile aynı kaynaktan gelir.</summary>
    private void ReportRegroupWaiting(MatchDirector director, int ready, int total)
    {
        var now = DateTime.UtcNow;
        if (now < _nextRegroupReportAt) return;

        _nextRegroupReportAt = now.AddSeconds(RegroupReportIntervalSeconds);

        var missing = string.Join(", ", director.OnlinePlayers()
            .Where(p => !p.Ready)
            .Select(p => p.Name));
        Console.WriteLine($"[tournament] toplanma bekleniyor ({ready}/{total}) — " +
                          $"tabanına dönmeyenler: {missing}");
    }

    /// <summary>Teşhis satırının ilk zamanını kurar (toplanmaya her girişte).</summary>
    private void ScheduleRegroupReport() =>
        _nextRegroupReportAt = DateTime.UtcNow.AddSeconds(RegroupReportIntervalSeconds);

    /// <summary>
    /// Geri sayım gözcüsü: toplanma şartı geri sayım BOYUNCA da geçerlidir. Tabanından çıkan tek
    /// oyuncu turu erteler — geri sayım iptal edilir ve toplanmaya dönülür (sayaç sıfırdan başlar).
    /// <para>Şart "girişte bir kez" ölçülseydi oyuncu tabana bir saniye değip çıkabilir, tur onu
    /// sahanın ortasında yakalardı; kural "tabanda BEKLE"dir, "tabana uğra" değil.</para>
    /// <para>⚠️ İptalin <b>istisnası yoktur</b>: geri sayım her zaman toplanma şartıyla açıldığı
    /// için (zorunlu başlatma kaldırıldı) her koşulda geri alınabilir.</para>
    /// </summary>
    private void TickCountdownWatch(MatchDirector director)
    {
        var (ready, total) = CountInBase(director);
        if (total == 0 || ready >= total) return;

        // Sayaç bu tikte dolduysa tur çoktan başlamıştır → false döner ve geri almayız.
        if (!director.TryCancelCountdownForMode($"regroup:{ready}/{total}")) return;

        _stage = RoundStage.Regroup;
        ScheduleRegroupReport();

        Console.WriteLine($"[tournament] geri sayım İPTAL — tabanından çıkan var ({ready}/{total}); " +
                          "toplanmaya dönüldü.");
    }

    /// <summary>Kaç oyuncu tabanında (<c>ready</c>) ve toplam kaç oyuncu çevrimiçi.
    /// <para><c>ready</c> bayrağının bu moddaki anlamı "şu anda kendi tabanımdayım"dır ve
    /// istemci onu <b>her iki yönde</b> günceller (§10.1) — kapı da gözcü de aynı sayacı okur.</para></summary>
    private static (int ready, int total) CountInBase(MatchDirector director)
    {
        var total = 0;
        var ready = 0;
        foreach (var player in director.OnlinePlayers())
        {
            total++;
            if (player.Ready) ready++;
        }

        return (ready, total);
    }

    private void Decide(MatchOutcome outcome, string reason)
    {
        _outcome = outcome;
        _matchOver = true;
        Console.WriteLine($"[tournament] maç kararı ({reason}) — {_round}. turda bitti.");
    }

    /// <summary>Best-of tavanı: <c>2 × limit − 1</c> tur. Limit yoksa tavan da yoktur (maçı
    /// operatör bitirir).</summary>
    private static int MaxRounds(int scoreLimit) =>
        scoreLimit > 0 ? 2 * scoreLimit - 1 : int.MaxValue;
}
