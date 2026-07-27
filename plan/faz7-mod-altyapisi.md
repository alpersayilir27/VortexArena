# Faz 7 — Mod altyapısı (tek moddan çok moda geçiş)

> **Durum:** 📋 planlandı (2026-07-27) — uygulanmadı.
> **Önkoşul:** Faz 6 ✅.
> **Bu faz davranış DEĞİŞTİRMEZ:** sonunda TDM birebir bugünkü gibi oynanır. Tek çıktısı,
> ikinci ve sonraki modların (Faz 8 **FFA**; sonra Turnuva / Silah Yarışı / Zombi / Çocuk
> oyunları) mevcut kodu kırmadan takılabileceği **yüzey**dir.
> ⚠️ **Faz 7 tek başına doğrulanamaz** (tüketicisi yok). Doğrulama Faz 8 ile **tek geçişte**
> yapılır; iki faz ayrı dokümandır ama tek commit çiftidir (7 → 8, arada kırık durum bırakma).

## Bağlam — bugün neden yalnız TDM çalışıyor

Sistem "çok modlu" diye tasarlandı (`IGameMode`, `ModeDefinition`, `Assets/Modes/<Mod>/` kutuları)
ama **iki takım varsayımı katmanların içine gömülü**. İkinci bir mod eklemek bugün şu 12 noktada
duvara toslar:

| # | Sorun | Yer | Sonuç |
|---|---|---|---|
| S1 | Dost ateşi kontrolü `target.Team == shooter.Team` | `MatchDirector.cs:545` | **HARD BLOCKER:** takımsız modda herkesin takımı `""` → `"" == ""` → **tüm vuruşlar reddedilir**, kimse kimseyi vuramaz |
| S2 | `BalanceTeams` her zaman iki dolu takım kurar | `MatchDirector.cs:744` | Takımsız modda oyuncular zorla kırmızı/maviye bölünür |
| S3 | Skor iki takıma sabit: `_scoreRed`/`_scoreBlue` | `MatchDirector.cs:76-77`, `AddScore` `:139` | Bireysel skor tutulacak yer yok |
| S4 | `IsMatchOver(out string winnerTeam)` | `IGameMode.cs:37` | Kazanan yalnız takım olabiliyor; "kazanan oyuncu" ifade edilemiyor |
| S5 | `MatchStateMsg`/`MatchEndMsg`/`MatchInfo` yalnız `scoreRed`/`scoreBlue` taşır | `ControlMessages.cs:205,242,136` | Bireysel skor tele çıkamıyor |
| S6 | `AdminRoster.IsFfa` **sezgisel**: "hiçbir çevrimiçi oyuncunun takımı yok" | `AdminRoster.cs:512` | Lobby fazında takımı henüz atanmamış TDM maçı da FFA görünür; otorite sunucuda değil |
| S7 | `Team` enum yalnız `Red`/`Blue` | `Team.cs:3` | Nötr/takımsız ifade edilemiyor (`BaseZone`, `SpawnPoint`, `Weapon` bu enum'u serialize ediyor) |
| S8 | `PlayerCombatState.Team` varsayılanı `Red` | `PlayerCombatState.cs:53` | Takımsız oyuncu kendini kırmızı sanar → yanlış tabana yönlendirilir |
| S9 | Canlanma tek kurala sabit: "kendi `BaseZone`'una gir" | `PlayerCombatState.cs:265-295` | "Sabit dur", "anında canlan", "canlanma yok (hayatta kalma)" ifade edilemiyor |
| S10 | `SpawnPoint.Find(team, slot)` yalnız takım-içi slot | `SpawnPoint.cs:55` | Takımsız modda tek havuz yok |
| S11 | Silah kaynağı sahneye gömülü (raf silahları) | Arena sahneleri | Mod "silahı ben veririm" diyemiyor |
| S12 | HUD ortak kodu yok — `TdmClientController` 370 satır, ~%80'i mod-bağımsız | `TdmClientController.cs` | Her yeni mod kill-feed/can/geri sayımı yeniden yazar |

Ek olarak `start_match` **maç parametresi taşımıyor** (`ControlMessages.cs:87-93`): süre ve skor
limiti hep mod varsayılanıdır (`MatchDirector.cs:399-400`), operatör raundu kısaltıp uzatamaz.

## Kullanıcı kararları (kesinleşmiş)

| Karar | Sonuç |
|---|---|
| Ortak HUD tabanı **her modda olabilecek** şeyleri taşır (kim ne kadar öldürdü, can, ölüm, geri sayım) | `ModeHudBase` kurulur |
| **Takım ile ilgili hiçbir şey tabana girmez** — bazı modlarda takım olmayacak | Skor satırı/takım rengi/kolonu alt sınıfın işi |
| Bireysel skor `PlayerInfo.score` ile taşınır; takım skoru `match_state.scoreRed/scoreBlue` olarak **aynen kalır** | TDM sıfır değişiklik; 2 yeni protokol alanı |
| Admin maç başlatırken **süre ve skor limitini o maça özel** seçebilir; SO'daki değerler yalnız varsayılan | `start_match` + ortak seçim parametre taşır |
| Süre seçenekleri: **2.5 dk · 5 dk · 10 dk · 15 dk · 20 dk · 30 dk · 1 saat** | `ROUND_SECONDS_OPTIONS = {150, 300, 600, 900, 1200, 1800, 3600}` |

## Uygulama kararları (bana bırakılanlar — gerekçeli)

| # | Karar | Gerekçe |
|---|---|---|
| K1 | Mod, davranışını anlatan bir **`ModeRules` şekil tanımı** döner; bu `load_match`/`welcome` ile istemciye taşınır | Bugün istemci ve admin modun ne olduğunu **tahmin ediyor** (S6). Tahmin, mod sayısı arttıkça yanlışlanır. Kural sunucudan gelirse istemcide `if (modeId == "ffa")` zinciri hiç doğmaz — yeni mod eklemek istemci kodunu değiştirmez. |
| K2 | Protokolde kurallar **iç içe `rules` nesnesi** olarak, değerler **string** | DTO kuralı (§7): `[Serializable]`, düz public alan, Dictionary/polimorfizm yok. İç içe sınıf zaten var (`WelcomeMsg.match`). String enum, sürüm uyumu için sayısal enum'dan güvenli: bilinmeyen değer varsayılana düşer. |
| K3 | Yeni `IGameMode` kancaları **default interface method** ile eklenir | .NET 10 destekliyor ve `IGameMode` yalnız sunucuda derleniyor. Böylece Turnuva/Zombi için kanca eklemek **mevcut modların hiçbirini değiştirmez**. Aynı gerekçeyle `OnTick`/`OnHitApplied` şimdi boş varsayılan gövdeye çevrilir → `TdmMode`'daki iki boş metod silinir. |
| K4 | **Tüketicisi olmayan kanca EKLENMEZ** | Faz 7 yalnız FFA'nın ihtiyacını açar. Turnuva/Zombi/Silah Yarışı kancalarının **imzaları** bu dokümanda sabitlenir ama koda girmez — ölü kanca, her modun boş uygulamak zorunda kaldığı vergidir. K3 sayesinde sonradan eklemek ücretsiz. |
| K5 | `Team` enum'a `Neutral` **sona** (`= 2`) eklenir | `BaseZone`/`SpawnPoint`/`Weapon` bu enum'u serialize ediyor. Başa/ortaya ekleme mevcut sahnelerdeki değerleri kaydırır ve **her arenanın taban/spawn takımları bozulur**. Sona ekleme güvenlidir. |
| K6 | İstemcide aktif kuralları **`ModeRuntime` statiği** tutar | `PlayerCombatState` (canlanma), `WeaponGranter` (silah), HUD (skor), `AdminRoster` (takım kipi) aynı bilgiyi ister. Dördü ayrı ayrı `load_match` dinlerse dördü ayrı ayrı bayatlar. Tek okuma noktası + tek besleme noktası. |
| K7 | Sunucusuz editör testinde kurallar `ModeDefinition`'dan okunur | Faz 5'in dev penceresi sentetik maç kurabiliyor; kural boş kalırsa FFA editörde denenemez. `ModeDefinition`'daki kural alanları **yalnız önizleme/editör** içindir — sapmada **sunucu kazanır** (bugün `roundSeconds`/`scoreLimit` için geçerli olan sözleşmenin aynısı, `ModeDefinition.cs:11-14`). |
| K8 | Maç parametreleri (süre/skor limiti) **ortak seçim kanalından** (`set_selection` → `admin_state`) geçer | İki operatör aynı ekranı görsün diye mod/harita zaten bu kanaldan gidiyor (`AdminSelection.cs:9-16`). Parametreleri yerel tutmak, bir operatörün 5 dk sandığı maçın 30 dk başlamasına yol açar. |
| K9 | Dost ateşi kararı `AreTeammates()` yardımcısına taşınır; **boş takım asla takım arkadaşı sayılmaz** | S1'in kökü bu: `"" == ""` mantıken doğru ama oyun kuralı olarak yanlış. Tek yardımcı hem FFA'yı çözer hem takımlı modlarda bugünkü davranışı korur. |
| K10 | `MatchDirector`'a **skor defteri** eklenir, faz makinesi büyütülmez | Sınıf zaten 876 satır. Skor okuma/yazma (`AddScore`, `AddPlayerScore`, `Leader`) tek bölümde toplanır ve modlar yalnız oradan geçer; ileride ayrı `Scoreboard` sınıfına çıkarmak mekanik bir taşıma olur. |

---

## 1. `ModeRules` — modun şekli

Modun "ne tür bir oyun olduğunu" anlatan, **sunucu-otoriter** tanım. Her `IGameMode` bunu döner;
sunucu `load_match` ve `welcome.match` ile istemciye yollar.

```csharp
// Server/VortexArena.Server.Core/Modes/ModeRules.cs
public enum TeamMode     { None, TwoTeams }          // takımsız | kırmızı-mavi
public enum ScoreKind    { Team, Player }            // skor kime yazılır
public enum ReviveAnchor { OwnBase, StandStill }     // canlanma şartı
public enum WeaponSource { Rack, RandomGrant }       // silah nereden gelir

public sealed record ModeRules
{
    public TeamMode     Teams        { get; init; } = TeamMode.TwoTeams;
    public ScoreKind    Scoring      { get; init; } = ScoreKind.Team;
    public bool         FriendlyFire { get; init; }                  // false = aynı takım vuramaz
    public ReviveAnchor Revive       { get; init; } = ReviveAnchor.OwnBase;
    public WeaponSource Weapons      { get; init; } = WeaponSource.Rack;
    public float        RespawnDelay { get; init; } = ArenaProtocol.RESPAWN_DELAY;

    /// <summary>Bugünkü TDM davranışı — yeni mod bir alanı belirtmezse buraya düşer.</summary>
    public static readonly ModeRules TeamDefault = new();

    public ModeRulesInfo ToInfo() => /* enum → protokol string'i */;
}
```

**Varsayılan = bugünkü TDM.** Bir mod hiçbir alan yazmazsa bugünkü davranışı alır; yani bu tip
eklendiğinde `TdmMode` için `Rules => ModeRules.TeamDefault;` tek satırdır.

### Kuralların tükettiği yerler

| Kural | Sunucu | İstemci |
|---|---|---|
| `Teams` | `BalanceTeams` vs `ClearTeams`; spawn slot havuzu | `AdminRoster.IsFfa`, avatar rengi, HUD skor satırı |
| `Scoring` | `OnKill` skoru nereye yazar | HUD skor satırı |
| `FriendlyFire` | `hit_report` dost ateşi kapısı | — |
| `Revive` | `respawn.delaySeconds` + `revive_request` kabul şartı | `PlayerCombatState` canlanma akışı |
| `Weapons` | — (tümüyle istemci sunumu) | `WeaponGranter`: raf gizleme + rastgele silah |
| `RespawnDelay` | `respawn.delaySeconds`, `HandleReviveRequestAsync` eşiği | ölüm ekranı geri sayımı |

---

## 2. Skor modeli

İki kanal, ikisi de **sunucu-otoriter**:

| Kanal | Taşıyıcı | Kim kullanır |
|---|---|---|
| **Takım skoru** | `match_state.scoreRed` / `scoreBlue` (**değişmez**) | TDM, ileride Zombi (insan/zombi), bölge kontrolü |
| **Bireysel skor** | `lobby_state → PlayerInfo.score` (**yeni tek alan**) | FFA, ileride Silah Yarışı (= seviye), turnuva sıralaması |

**Neden `lobby_state`:** sunucu zaten her ölümde roster'ı tazeliyor (`_rosterRefreshFor`,
`MatchDirector.cs:578`) ve `kills/deaths/hp/alive` oradan gidiyor (§10.2). Bireysel skorun
değiştiği an = öldürmenin olduğu an = roster'ın zaten tazelendiği an. Yeni mesaj tipi, yeni
yayın döngüsü, yeni bayatlama yolu **doğmaz**.

Kazanan: `match_end.winnerTeam` (takımlı) **veya** yeni `match_end.winnerPlayerId` (bireysel).
Sunucuda ikisi tek tipte toplanır:

```csharp
// Server/VortexArena.Server.Core/Modes/MatchOutcome.cs
public readonly record struct MatchOutcome(string WinnerTeam, int WinnerPlayerId)
{
    public static readonly MatchOutcome Draw = new("", 0);
}
```

> **3+ takım ne olacak?** (çocuk oyunları) Bugün eklenmiyor. Geldiğinde yol açık:
> `PlayerInfo.team` zaten serbest string (`"green"`/`"yellow"` bugün de geçer) ve `match_state`'e
> `teamScores: [{team, score}]` dizisi eklenir; `scoreRed`/`scoreBlue` iki takımlı modlar için
> kısayol olarak kalır. Bu kararı **o mod gelince** ver — şimdi yapılırsa TDM, `AdminHud` ve
> `AdminRoster` tüketicisi olmayan bir soyutlama için baştan yazılır.

---

## 3. Protokol değişiklikleri

> **Sıra kuralı** (`.claude/rules/docs-sync.md`): ağ davranışı değişiyor → **önce**
> `Docs/ArenaNet-Protokol.md`, **sonra** iki taraf (Unity `_Shared/Net/Protocol` + `Server/`).

### 3.1 §1 Sabitler — yeni

```csharp
/// <summary>ReviveAnchor.StandStill: ölü oyuncunun canlanmak için sabit durması gereken süre.</summary>
public const float REVIVE_HOLD_SECONDS = 3f;

/// <summary>ReviveAnchor.StandStill: bu yarıçapı (metre) aşan hareket sayacı sıfırlar.</summary>
public const float REVIVE_HOLD_RADIUS = 1f;

/// <summary>Admin maç süresi seçenekleri (saniye): 2.5 · 5 · 10 · 15 · 20 · 30 dk · 1 saat.</summary>
public static readonly int[] ROUND_SECONDS_OPTIONS = { 150, 300, 600, 900, 1200, 1800, 3600 };
```

### 3.2 Yeni DTO: `ModeRulesInfo`

```csharp
/// Modun ŞEKLİ (§10.5) — sunucu-otoriter. Bilinmeyen/boş değer varsayılana (takımlı TDM) düşer.
[Serializable]
public class ModeRulesInfo
{
    public string teamMode     = "two";   // "none" | "two"
    public string scoring      = "team";  // "team" | "player"
    public bool   friendlyFire;
    public string reviveAnchor = "base";  // "base" | "standstill"
    public string weaponSource = "rack";  // "rack" | "random"
    public float  respawnDelay = ArenaProtocol.RESPAWN_DELAY;
}
```

### 3.3 Değişen mesajlar

| Mesaj | Yeni alan | Anlam |
|---|---|---|
| `load_match` | `rules: ModeRulesInfo` | İstemci kendini bu maça göre kurar |
| `welcome.match` (`MatchInfo`) | `rules: ModeRulesInfo` | Geç katılım aynı kurallarla bağlanır |
| `lobby_state` (`PlayerInfo`) | `score: int` | Bireysel skor (sunucu-otoriter, §10.2) |
| `match_end` | `winnerPlayerId: int` | Bireysel kazanan; 0 = yok/berabere |
| `start_match` | `roundSeconds: int`, `scoreLimit: int` | 0/eksik = mod varsayılanı |
| `set_selection` | `roundSeconds: int`, `scoreLimit: int` | 0 = mevcut ortak değeri koru |
| `admin_state` | `roundSeconds: int`, `scoreLimit: int` | Ortak parametrelerin yayını |

**Geriye dönük uyum:** hepsi ek alandır. `JsonUtility`/`System.Text.Json` eksik alanı varsayılanla
bırakır, fazlasını yok sayar → eski istemci yeni sunucuya, yeni istemci eski sunucuya bağlanır.
`PROTOCOL_VERSION` **artmaz**.

### 3.4 Doküman güncellemesi

`Docs/ArenaNet-Protokol.md`:
- §1'e üç yeni sabit
- §5.2'ye `start_match`/`set_selection` yeni alanları
- §5.3'e `load_match.rules`, `welcome.match.rules`, `PlayerInfo.score`, `match_end.winnerPlayerId`, `admin_state` alanları
- §10.2'ye `score` sayacı
- **Yeni §10.5 "Mod kuralları (`ModeRules`)"**: beş kuralın tam semantiği + varsayılanları +
  `ReviveAnchor.StandStill` akışı (§10.4'ün yanına ikinci canlanma yolu olarak)
- §10.1'e "maç parametreleri admin'den gelebilir" notu

---

## 4. Sunucu değişiklikleri

### 4.1 `Modes/IGameMode.cs`

```csharp
public interface IGameMode
{
    string ModeId { get; }
    ModeRules Rules { get; }                          // YENİ
    int DefaultRoundSeconds { get; }
    int DefaultScoreLimit { get; }

    void OnMatchStart(MatchDirector d);
    void OnKill(MatchDirector d, int killerId, int victimId, string weaponId);
    bool IsMatchOver(MatchDirector d, out MatchOutcome outcome);   // DEĞİŞTİ (S4)

    // K3: varsayılan gövdeli → ilgilenmeyen mod hiç yazmaz
    void OnTick(MatchDirector d, float deltaSeconds) { }
    void OnHitApplied(MatchDirector d, int attackerId, int targetId, float damage, bool killed) { }
}
```

### 4.2 `Modes/TdmMode.cs`

- `Rules => ModeRules.TeamDefault;` eklenir
- Boş `OnTick`/`OnHitApplied` **silinir** (K3)
- `IsMatchOver` yeni imzaya geçer; iç mantık **aynen korunur** (limit → o takım, süre → yüksek
  skor, eşitlik → `MatchOutcome.Draw`)

### 4.3 `PlayerState.cs`

```csharp
/// <summary>Bireysel maç skoru (§10.2). Yazarı MatchDirector, _gate altında.</summary>
public int Score { get; set; }
```
`ToPlayerInfo()` → `score = Score`.

### 4.4 `MatchDirector.cs`

| Bölge | Değişiklik |
|---|---|
| Mod kaydı | `Register(new TdmMode())` → tek `RegisterModes()` metodu (Faz 8 buraya `FfaMode` ekler) |
| Alanlar | `private ModeRules _rules = ModeRules.TeamDefault;` |
| **Skor defteri** (K10) | Mevcut `AddScore(team,…)` + **yeni** `AddPlayerScore(int playerId, int amount)`, `ScoreOf(int playerId)`, `TryGetLeader(out int playerId, out int score)` — hepsi `_gate` güvenli, modlar yalnız buradan geçer |
| `StartMatchAsync` | İmza → `(string? modeId, string? sceneName, int roundSeconds, int scoreLimit)`. `roundSeconds/scoreLimit ≤ 0` → mod varsayılanı. `_rules = mode.Rules`. |
| Takım kurulumu | `_rules.Teams == TwoTeams` → bugünkü `BalanceTeams`; `None` → **yeni** `ClearTeams(players)` (herkese `SetTeam(id, "")`, kilit dışında — S2) |
| Spawn slot | Takımlıda bugünkü `slot % spawnSlotsPerTeam`; takımsızda **tek havuz**: `slot % (spawnSlotsPerTeam * 2)` (sahnedeki iki tabanın slotları birleşir — S10) |
| Dost ateşi (S1) | `if (target.Team == shooter.Team)` → `if (!_rules.FriendlyFire && AreTeammates(shooter, target))`.<br>`static bool AreTeammates(a,b) => !string.IsNullOrEmpty(a.Team) && a.Team == b.Team;` (K9) |
| `respawn` | `delaySeconds = _rules.RespawnDelay` (sabit `RESPAWN_DELAY` yerine) |
| `HandleReviveRequestAsync` | Eşik `_rules.RespawnDelay`. **`ReviveAnchor` sunucuda doğrulanmaz** — §10.3 felsefesi gereği ("sunucu hakemlik değil defter tutar"); "tabanda mı / sabit mi durdu" kararı istemcinindir, sunucu faz + ölü + gecikme kontrolüyle yetinir. `REVIVE_GRACE` zorla canlandırma güvenlik ağı **aynen kalır**. |
| `ResetMatchStateLocked` | `keepScore == false` iken `player.Score = 0` |
| `BuildMatchStateLocked` / `BuildMatchInfoLocked` | `MatchInfo.rules = _rules.ToInfo()` |
| `EnterEndLocked` / `EnterEndAsync` | `string winnerTeam` → `MatchOutcome outcome`; `MatchEndMsg.winnerPlayerId = outcome.WinnerPlayerId` |
| `TickAsync` | `IsMatchOver(this, out var outcome)` |
| `EnterLobbyLocked` | Skor sıfırlamaya `Score` da girer |

### 4.5 `LobbyService.cs` — ortak maç parametreleri (K8)

`_selectionGate` altına `_selectedRoundSeconds` / `_selectedScoreLimit` eklenir.
`ApplySelection` bunları da uygular (`0` = mevcut değeri koru).
`BroadcastAdminStateAsync` → `AdminStateMsg`'e iki alan.
`HandleStartMatchAsync` → `_director.StartMatchAsync(msg.modeId, msg.sceneName, msg.roundSeconds, msg.scoreLimit)`.

---

## 5. Unity değişiklikleri — Core

### 5.1 `Core/Combat/Team.cs`

```csharp
public enum Team { Red, Blue, Neutral }   // Neutral SONA (K5) — mevcut sahneler kaymasın
```

### 5.2 Yeni: `Core/ModeRuntime.cs` (K6)

Aktif maçın kurallarının **tek okuma noktası**. Statik durum + statik olay (`AdminSelection`
deseni), bir `MonoBehaviour` pompası `load_match` / `welcome` dinler.

```csharp
public static class ModeRuntime
{
    public static string ModeId { get; }
    public static ModeTeamMode  Teams   { get; }   // None | TwoTeams
    public static ModeScoreKind Scoring { get; }   // Team | Player
    public static bool  FriendlyFire { get; }
    public static ModeReviveAnchor Revive  { get; }
    public static ModeWeaponSource Weapons { get; }
    public static float RespawnDelay { get; }
    public static event Action Changed;

    /// Sunucusuz editör testi (K7): kurallar GameCatalog'daki ModeDefinition'dan okunur.
    public static void ApplyFromCatalog(string modeId);
}
```

**Tüketiciler:** `PlayerCombatState` (canlanma), `WeaponGranter` (silah), `ModeHudBase` (skor),
`AdminRoster` (takım kipi).

### 5.3 `Core/ModeDefinition.cs`

`ModeRules`'un beş alanı **önizleme kopyası** olarak eklenir (K7), mevcut
`roundSeconds`/`scoreLimit` ile aynı sözleşmeyle: *"Kural OTORİTESİ SUNUCUDADIR; buradaki
değerler yalnız arayüz/ön izleme içindir"* (`ModeDefinition.cs:11-14`).

### 5.4 `Core/Arena/SpawnPoint.cs`

```csharp
/// <summary>Takımsız modlar (§10.5 TeamMode.None): tüm noktalar tek havuzdur.
/// Kayıt listesi (team, slot) sırasına dizilip verilen indeks alınır; liste boşsa null.</summary>
public static SpawnPoint FindGlobal(int slot);
```
Mevcut `Find(team, slot)` **dokunulmaz** (TDM aynen çalışır).

### 5.5 `Core/Combat/PlayerCombatState.cs`

- `Team` başlangıcı `Red` → `Neutral` (S8); `ParseTeam` boş/tanımsız girdide `Neutral` döner
- Canlanma akışı `ModeRuntime.Revive`'a göre ikiye ayrılır:
  - **`OwnBase`** (TDM): bugünkü `FindOwnBaseZone()` + `IsPlayerInside` yolu, **değişmez**
  - **`StandStill`** (FFA, yeni): ölüm anında HMD konumu **çapa** alınır; her karede
    `Vector3.Distance(head, anchor) > REVIVE_HOLD_RADIUS` ise çapa ve sayaç sıfırlanır.
    Sayaç `REVIVE_HOLD_SECONDS`'a ulaşınca `revive_request` gönderilir (canlanana dek ~1 sn'de bir tekrar).
- `StatusText`: `StandStill` kipinde *"Canlanmak için sabit dur — N sn"* / *"Hareket etme"*
- `_reviveAt` gecikmesi `ModeRuntime.RespawnDelay`'den beslenir (sunucu `respawn.delaySeconds`
  ile aynı değeri yolladığı için ikisi çakışmaz)

### 5.6 Yeni: `Core/UI/ModeHudBase.cs` (S12)

Mod HUD'larının **takım-agnostik** ortak tabanı. Modlar birbirini referanslamaz kuralı korunur:
ortak kod **Core**'dadır, her mod asmdef'i Core'u zaten referanslar.

| Base'de VAR (her modda anlamlı) | Base'de YOK (alt sınıfın işi) |
|---|---|
| Faz etiketi + kalan süre | **Skor satırı** |
| Geri sayım (`countdown`) | **Takım rengi / takım kolonu** |
| Can barı + `CAN n` | Moda özel göstergeler (seviye, dalga, tur…) |
| Ölüm ekranı + durum metni (`PlayerCombatState`) | |
| Kill-feed (ad çözümü `lobby_state`'ten) | |
| **Kendi öldürme/ölüm sayacın** | |
| Maç sonu satırı (kazanan metnini alt sınıf verir) | |

```csharp
public abstract class ModeHudBase : MonoBehaviour
{
    protected abstract string ScoreLine(MatchStateMsg msg);      // "KIRMIZI 5 — 3 MAVİ" / "SEN 7 · LİDER 9"
    protected abstract string WinnerLine(MatchEndMsg msg);       // "MAVİ KAZANDI" / "AHMET KAZANDI"
    protected virtual  void   OnLobbyStateApplied(LobbyStateMsg msg) { }  // FFA lider tablosu buradan
}
```

`TdmClientController` bu tabana taşınır; **görsel çıktısı ve `TdmHud.prefab` alanları birebir
korunur** (regresyon yüzeyi: prefabdaki `[SerializeField]` referansları taban sınıfa taşındığı
için Unity'nin alan eşlemesi sürer — ad değişmediği sürece prefab bağı kopmaz).

---

## 6. Unity değişiklikleri — App / Admin

| Dosya | Değişiklik |
|---|---|
| `AdminRoster.cs:89,512` | `IsFfa` sezgiseli (S6) → `ModeRuntime.Teams == None`. Lobby fazında (maç yüklenmemişken) ortak seçimin modundan okunur (`AdminSelection.ModeId` → `GameCatalog.FindMode(...)`). Sezgisel **fallback olarak kalır** (katalog eksikse arayüz boş kalmasın). |
| `AdminRoster` | `AdminPlayerView.score` alanı + `lobby_state`'ten doldurma; `kill_event`'te yerel `+1` (mevcut `kills` deseniyle aynı — sunucu bir sonraki `lobby_state` ile ezer) |
| `AdminStatsPanel.cs:30-34` | Kolonlara **`SKOR`** eklenir; FFA'da tablo skora göre azalan sıralanır |
| `AdminHud.cs:365-368,396,420` | FFA dalı bugün skoru **boş** basıyor → lider tablosu (ilk 3: `ad · skor`) yazar |
| `AdminPreferencesPanel.cs:116-119` | `MAÇ` bölümüne iki döngüleyici: **Süre** (`ROUND_SECONDS_OPTIONS`, etiket "2.5 dk"…"1 saat") ve **Skor limiti** (adımlayıcı, `±1` / `±5`). Mod değişince ikisi o modun `ModeDefinition` varsayılanına döner. Değişiklik `set_selection` ile gider (K8). |
| `AdminCommands.cs` | `StartMatch` → `roundSeconds`/`scoreLimit` taşır; `SetSelection` yeni alanları gönderir |
| `AdminSelection.cs` | `RoundSeconds`/`ScoreLimit` statikleri + `admin_state`'ten besleme |

---

## 7. Gelecek modlar — genişleme noktaları

Faz 7'nin asıl ürünü bu tablo: **her planlı mod hangi mevcut yüzeye oturuyor, neyi yeni istiyor.**

| Mod | Faz 7'nin verdiği | Ek gerekecek (o faz) |
|---|---|---|
| **FFA** (Faz 8) | `TeamMode.None`, `ScoreKind.Player`, `ReviveAnchor.StandStill`, `WeaponSource.RandomGrant`, `PlayerInfo.score` | **Hiçbir protokol eki yok** — Faz 8 saf içerik + `FfaMode.cs` |
| **Silah Yarışı** | `PlayerInfo.score` = **seviye**; `WeaponGranter` altyapısı; `OnKill` | `WeaponSource.Ladder`; yeni mesaj `set_loadout{playerId, weaponId}` (sunucu kimin hangi silahta olduğunu söyler); `IGameMode.LoadoutFor(playerId)` |
| **Turnuva** | `MatchOutcome`, admin maç parametreleri, ortak seçim kanalı | `OnRoundStart/OnRoundEnd(d, roundIndex)` kancaları (K3 ile ücretsiz); `bracket_state` mesajı; oyuncu havuzu + sıra yönetimi (lobide "sıradaki maç" listesi) |
| **Zombi** | `TeamMode.TwoTeams` (insan/zombi), `OnKill` ile taraf değiştirme, `FriendlyFire` | `OnRevive(d, playerId)` kancası (ölen insan zombi olarak canlanır); **NPC**: `playerId` havuzu (1..`PLAYER_ID_MAX`) gerçek oyuncu ile bot arasında nasıl paylaşılacak — ayrı aralık mı, ortak havuz + `flags` biti mi — o fazda kararlaştırılır (**sabit bir oyuncu sayısı sınırı YOKTUR**, tek tavan `u8`); sunucuda `INpcDirector` |
| **Çocuk oyunları** | `FriendlyFire`, `TeamMode`, hasarsız modlar için `ScoreKind` | 3+ takım → `match_state.teamScores[]` (§2 notu); `ScoreKind.Objective`; hasarsız etkileşim olayı (`objective_event`) |

**Kanca ekleme politikası (K3/K4):** yeni kanca `IGameMode`'a **varsayılan gövdeyle** eklenir →
mevcut modların hiçbiri değişmez, yalnız ilgilenen mod override eder. Bu yüzden yukarıdaki
kancaların hiçbiri Faz 7'de koda girmez.

---

## Görev listesi

| # | Görev | Dosya | Bağımlılık |
|---|---|---|---|
| 7.1 | **Protokol dokümanı** (§1 sabitler, §5.2/§5.3 alanlar, §10.2 `score`, yeni §10.5) | `Docs/ArenaNet-Protokol.md` | — |
| 7.2 | Sabitler | `_Shared/Net/Protocol/ArenaProtocol.cs` | 7.1 |
| 7.3 | `ModeRulesInfo` + 7 mesaja alan ekleme | `_Shared/Net/Protocol/ControlMessages.cs` | 7.1 |
| 7.4 | `ModeRules` + `MatchOutcome` | `Server/…/Modes/` (2 yeni dosya) | 7.3 |
| 7.5 | `IGameMode` yeni yüzey + `TdmMode` uyarlaması | `Server/…/Modes/` | 7.4 |
| 7.6 | `PlayerState.Score` | `Server/…/PlayerState.cs` | 7.3 |
| 7.7 | `MatchDirector`: skor defteri, `_rules`, dost ateşi (S1), takım kurulumu (S2), spawn havuzu (S10), parametreli `StartMatchAsync`, `MatchOutcome` | `Server/…/MatchDirector.cs` | 7.4-7.6 |
| 7.8 | `LobbyService`: ortak maç parametreleri | `Server/…/LobbyService.cs` | 7.7 |
| 7.9 | `Team.Neutral` | `_Shared/Core/Combat/Team.cs` | — |
| 7.10 | `ModeRuntime` + pompası | `_Shared/Core/ModeRuntime.cs` (yeni) | 7.3 |
| 7.11 | `ModeDefinition` kural alanları | `_Shared/Core/ModeDefinition.cs` | 7.10 |
| 7.12 | `SpawnPoint.FindGlobal` | `_Shared/Core/Arena/SpawnPoint.cs` | 7.9 |
| 7.13 | `PlayerCombatState`: `Neutral` başlangıç + iki canlanma yolu | `_Shared/Core/Combat/PlayerCombatState.cs` | 7.10, 7.12 |
| 7.14 | `ModeHudBase` | `_Shared/Core/UI/ModeHudBase.cs` (yeni) | 7.10 |
| 7.15 | `TdmClientController` → `ModeHudBase` alt sınıfı (**görsel çıktı birebir aynı**) | `Assets/Modes/TeamDeathmatch/Scripts/` | 7.14 |
| 7.16 | `AdminRoster`: otoriter `IsFfa` + `score` | `_Shared/App/Scripts/Admin/AdminRoster.cs` | 7.10 |
| 7.17 | `AdminSelection` + `AdminCommands`: maç parametreleri | `_Shared/App/Scripts/Admin/` | 7.8 |
| 7.18 | `AdminPreferencesPanel`: Süre + Skor limiti seçicileri | `_Shared/App/Scripts/Admin/AdminPreferencesPanel.cs` | 7.17 |
| 7.19 | `AdminStatsPanel` `SKOR` kolonu + `AdminHud` FFA lider tablosu | `_Shared/App/Scripts/Admin/` | 7.16 |
| 7.20 | Dev penceresi: sentetik maç parametrelerine mod kuralı önizlemesi | `_Shared/App/Scripts/Editor/DevWindow.cs` | 7.11 |

---

## Doğrulama

> ⚠️ Faz 7 **tek başına doğrulanmaz** (`.claude/rules/batch-build-verification.md`): tüm
> implementasyon bitince Faz 8 ile **tek geçiş**. Aşağıdakiler o geçişin Faz 7 kısmıdır.

| # | Kontrol | Beklenen |
|---|---|---|
| D1 | `dotnet build Server/` | 0 hata / 0 uyarı |
| D2 | `unity cmd recompile` + `get_console_logs` | 0 hata / 0 uyarı |
| D3 | **TDM regresyonu (en önemli):** loopback'te 2 bot + admin ile tam TDM raundu | Faz makinesi, takım dengeleme, dost ateşi reddi, taban canlanması, skor bandı, kill-feed **Faz 6'daki gibi** |
| D4 | `TdmHud.prefab` alan bağları | `ModeHudBase`'e taşınan `[SerializeField]`'ler kopmamış (HUD boş metin göstermiyor) |
| D5 | Admin'de Süre = "10 dk", Skor limiti = 15 seçilip maç başlatılır | `load_match.roundSeconds == 600`, `scoreLimit == 15`; ikinci admin panelinde **aynı değerler** görünür |
| D6 | İki admin bağlıyken biri süreyi değiştirir | Diğerinin paneli `admin_state` ile senkron döner (K8) |
| D7 | Eski `start_match` (parametresiz) simülasyonu | Mod varsayılanına düşer, reddedilmez |

## Dokümana yansıyacaklar (aynı commit — `.claude/rules/docs-sync.md`)

| Doküman | Ne yazılacak |
|---|---|
| `Docs/ArenaNet-Protokol.md` | Görev 7.1'in tamamı (**kodun ÖNÜNDE**) |
| `Docs/Sistem-Ozeti.md` | §2 yeni dosyalar (`ModeRules`, `MatchOutcome`, `ModeRuntime`, `ModeHudBase`), §3 mod kuralı akışı, §4 bileşen sözlüğü, §8 durum |
| `CLAUDE.md` | "Yeni mod" reçetesi: `IGameMode.Rules` + `ModeHudBase` alt sınıfı adımları; `Team.Neutral` |
| `Server/README.md` | "Yeni mod eklemek" bölümüne `Rules` + `MatchOutcome` |
| `plan/README.md` | Faz 7 satırı |
