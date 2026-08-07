#nullable enable
namespace VortexArena.Server.Core.Modes;

/// <summary>Herkes Tek (Free For All): her doğrulanmış öldürme ÖLDÜRENE +1 <b>bireysel</b> puan
/// yazar; takım yoktur. Maç, bir oyuncu skor limitine ulaşınca ya da süre bitince biter; süre
/// bitiminde en yüksek skorlu oyuncu kazanır, tepede eşitlik varsa berabere
/// (§10.5 <see cref="ScoreKind.Player"/>).
/// <para>Kurallar TDM varsayılanından beş noktada ayrılır (§10.5): takım yok, skor bireysel,
/// canlanma "sabit dur", silahı mod dağıtır, canlanma gecikmesi 0. Geri kalan her şey
/// <see cref="ModeRules"/> varsayılanıdır — yani bu modun eklenmesi TDM'in tek satırını bile
/// değiştirmez.</para></summary>
public sealed class FfaMode : IGameMode
{
    public string ModeId => "ffa";

    /// <summary>⚠️ <c>FriendlyFire</c> BURADA yazılmaz (§5.2): o bir mod kuralı değil, operatörün
    /// sunucu oturumu ayarıdır ve <c>MatchDirector</c> her kural şekline onu kendisi damgalar.
    /// FFA'da anahtarın görünür etkisi zaten yoktur: dost ateşi kapısı boş takımı asla takım
    /// arkadaşı saymaz (§10.3/4) ve bu modda herkesin takımı <c>""</c>'tir, yani kapı hiç kapanmaz.
    /// <para><c>RespawnDelay = 0</c> bilinçlidir: bekleme süresi yerine <see cref="ReviveAnchor.StandStill"/>
    /// şartı işler (istemci <c>REVIVE_HOLD_SECONDS</c> boyunca sabit durmayı bekler), yani toplam
    /// bekleme yine ~5 sn'dir ama oyuncunun elindedir.</para></summary>
    public ModeRules Rules => new()
    {
        Teams = TeamMode.None,
        Scoring = ScoreKind.Player,
        Revive = ReviveAnchor.StandStill,
        Weapons = WeaponSource.RandomGrant,
        RespawnDelay = 0f
    };

    public int DefaultRoundSeconds => 300;

    public int DefaultScoreLimit => 20;

    public void OnMatchStart(MatchDirector director) =>
        Console.WriteLine($"[ffa] maç başladı — {director.RoundSeconds} sn, skor limiti {director.ScoreLimit}.");

    public void OnKill(MatchDirector director, int killerId, int victimId, string weaponId)
    {
        // Çevre/intihar ölümü puanlanmaz: sahipsiz öldürmede killerId 0 gelir, kendi kendini
        // öldürene puan yazmak skoru ödüle çevirirdi.
        if (killerId <= 0 || killerId == victimId) return;
        director.AddPlayerScore(killerId, 1);
    }

    public bool IsMatchOver(MatchDirector director, out MatchOutcome outcome)
    {
        var limit = director.ScoreLimit;
        // TryGetLeader EŞİTLİKTE false döner (tek kazanan yok) — hem limit hem süre dalı bunu
        // "kazanan belli değil" olarak okur; sessizce ilk oyuncuyu seçmek yanlış kazanan ilan ederdi.
        var hasLeader = director.TryGetLeader(out var leaderId, out var leaderScore);

        if (limit > 0 && hasLeader && leaderScore >= limit)
        {
            outcome = MatchOutcome.Player(leaderId);
            return true;
        }

        if (director.TimeRemaining <= 0f)
        {
            // Tepede tek oyuncu → o kazanır; eşitlik ya da hiç oyuncu yok (admin harita
            // önizlemesi, §10.1) → berabere.
            outcome = hasLeader ? MatchOutcome.Player(leaderId) : MatchOutcome.Draw;
            return true;
        }

        outcome = MatchOutcome.Draw;
        return false;
    }
}
