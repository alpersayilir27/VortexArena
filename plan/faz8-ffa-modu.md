# Faz 8 — Herkes Tek (Free For All / FFA) modu

> **Durum:** 📋 planlandı (2026-07-27) — uygulanmadı.
> **Önkoşul:** **Faz 7** (mod altyapısı). Faz 7 olmadan bu faz yazılamaz — takımsız modda
> `hit_report` dost ateşi kapısı **tüm vuruşları reddeder** (Faz 7 / S1).
>
> ✅ **Faz 8 protokole hiçbir yeni alan eklemez.** Tamamı içerik + iki yeni dosya + bir bileşen.
> Bu, Faz 7'nin doğru kesildiğinin kanıtıdır: üçüncü mod da aynı ucuzlukta gelmeli.

## Modun tanımı (kullanıcı kararları — kesinleşmiş)

| Konu | Karar |
|---|---|
| Takım | **Yok.** Herkes herkese karşı; herkes herkesi vurabilir |
| Skor | **Bireysel** — her öldürme öldürene +1 (`PlayerInfo.score`) |
| Kazanan | Skor limitine ilk ulaşan; süre biterse en yüksek skor; eşitlikte berabere |
| Varsayılan parametreler | **300 sn (5 dk) · 20 öldürme** — admin maç başlatırken değiştirebilir (Faz 7 / K8) |
| Canlanma | **Sabit durma:** ölünce **3 sn boyunca 1 m'den fazla hareket etmezsen** canlanırsın. Hareket edersen sayaç sıfırlanır. Taban zorunluluğu **yok**; 5 sn'lik `RESPAWN_DELAY` bu modda **yoktur** (toplam ~3 sn) |
| Silah rafı | **Yok.** Arena sahnelerindeki raf silahları ve taban bölgeleri FFA'da gizlenir |
| Silah kaynağı | **Hold (grip) basılı tutunca elde rastgele silah belirir.** Bırakınca silah yok olur; tekrar basınca **yeni** bir rastgele silah gelir |
| Şarjör | Reload **yok** — şarjör bitince silahı bırakıp yenisini çekmek oyuncunun işi |
| Harita | Mevcut arenalar **olduğu gibi** kullanılır (yeni sahne/geometri işi yok) |

### `ModeRules` karşılığı

```csharp
public ModeRules Rules => new()
{
    Teams        = TeamMode.None,
    Scoring      = ScoreKind.Player,
    FriendlyFire = false,                   // takım olmadığı için etkisiz (aşağıdaki nota bak)
    Revive       = ReviveAnchor.StandStill,
    Weapons      = WeaponSource.RandomGrant,
    RespawnDelay = 0f,
};
```

> **`FriendlyFire = false` neden FFA'yı kilitlemez:** Faz 7 / K9'daki `AreTeammates()` **boş
> takımı asla takım arkadaşı saymaz**. FFA'da herkesin takımı `""` olduğu için kapı hiç
> kapanmaz. `FriendlyFire` bayrağı burada anlamsızdır; `true` yapmak da aynı sonucu verirdi ama
> `false` bırakmak "bu modda dost kavramı yok" niyetini doğru anlatır.

## Uygulama kararları (bana bırakılanlar — gerekçeli)

| # | Karar | Gerekçe |
|---|---|---|
| K1 | Rastgele silah **el başına bağımsız** verilir (iki eli de doldurabilirsin) | Çapraz-el durumu tutmak gerekmez; kural "hold basılıysa o elde silah var" olarak tek cümlede kalır. İstenirse tek satırla tek silaha indirilir (`_granted[other] == null` koşulu). |
| K2 | Verilen silah **el anchor'ının altına Instantiate** edilir, ISDK kavraması zorlanmaz | `Grabbable`'ı programla "seçili" hâle getirmek ISDK'nın iç durumuna girmek demek; kırılgan ve sürüm bağımlı. Silah zaten tanım gereği tutuluyor — kavrama sistemine hiç sokmamak daha sağlam. |
| K3 | `Weapon`'a **`GrantedHold`** bayrağı eklenir; `IsHeld` onu da sayar | K2'nin zorunlu sonucu: `IsHeld` bugün yalnız `grabbable.SelectingPointsCount`'a bakıyor (`Weapon.cs:75`) → verilen silah **ateş edemez**. Tek özellik + tek `\|\|` ile çözülür, TDM yolu değişmez. |
| K4 | `IsTwoHanded` verilen silahta **hep false** | İki elle sabitleme ISDK kavramasına bağlı (`Weapon.cs:76`). Verilen silah tek elle saçılım/geri tepme değerleriyle çalışır — FFA'nın hızlı temposuna zaten uygun. |
| K5 | Taban bölgesi **iki adımda** gizlenir: `zone.enabled = false` + **şerit görselinin** kapatılması. GameObject **kapatılmaz** | ⚠️ `BaseZone`'un çocukları iki türlüdür: `SpawnPoint` marker'ları ve **görsel taban şeridi** (Renderer taşıyan doğrudan çocuk — `ArenaTemplateWizard.cs:585-600,617`). GameObject kapatılırsa `SpawnPoint.OnDisable` kayıttan düşer (`SpawnPoint.cs:46`) → maç başı spawn göstergesi çöker. Yalnız bileşen kapatılırsa **şerit görünür kalır** (kullanıcı kararı "ikisi de gizlensin" ihlal edilir). Doğrusu: bileşeni kapat + `SpawnPoint` taşımayan Renderer'lı çocukları `SetActive(false)`. |
| K6 | Raf silahları **GameObject olarak** gizlenir, verilen silahlar süpürmeden muaf tutulur | Sahne süpürmesi `Weapon` bileşeni arar; verilen örnek de bir `Weapon`'dır. `GrantedHold` işaretli olanlar atlanır. |
| K7 | Ölünce elindeki verilen silah **yok edilir**; ölüyken hold silah vermez | `CanFire` zaten false (`PlayerCombatState.cs:86`), ama elde duran silah "hâlâ oynuyorum" hissi verir. Canlanınca oyuncu yeniden hold'a basar — bu, sabit durma kuralıyla da tutarlı (koşarken silah çekemezsin). |
| K8 | Grip girdisi **`OVRInput`** ile okunur | Meta-first politikası (CLAUDE.md); haptikler zaten `OVRInput` kullanıyor. `InputSystem_Actions`'a yeni bir "Grip" action eklemek de olurdu — tutarsızlık çıkarsa yedek yol budur. |
| K9 | FFA loadout'u bugünkü **AK47 + M4** prefablarıdır | İkisi takım renginde (`AK47_Red`, `M4_Blue`) ve bu FFA'da anlamsız ama **engel değil** — `Weapon.team` yalnız kozmetik, ateş yetkisi `PlayerCombatState`'ten gelir (`Weapon.cs:73`). Nötr renkli varyant bir **sanat işidir**, bu fazın kapsamı dışında; not olarak bırakılır. |

---

## 1. Sunucu — `Modes/FfaMode.cs`

Tek yeni dosya. Faz 7'nin yüzeyine oturur, `MatchDirector`'a dokunmaz.

```csharp
/// <summary>Herkes Tek: her doğrulanmış öldürme ÖLDÜRENE +1 bireysel puan yazar (takım yok).
/// Maç, bir oyuncu skor limitine ulaşınca ya da süre bitince biter; süre bitiminde en yüksek
/// skorlu oyuncu kazanır, tepede eşitlik varsa berabere (§10.5 ScoreKind.Player).</summary>
public sealed class FfaMode : IGameMode
{
    public string ModeId => "ffa";
    public ModeRules Rules => /* yukarıdaki blok */;
    public int DefaultRoundSeconds => 300;
    public int DefaultScoreLimit   => 20;

    public void OnMatchStart(MatchDirector d) => Console.WriteLine(
        $"[ffa] maç başladı — {d.RoundSeconds} sn, skor limiti {d.ScoreLimit}.");

    public void OnKill(MatchDirector d, int killerId, int victimId, string weaponId)
    {
        if (killerId <= 0 || killerId == victimId) return;   // çevre/intihar ölümü puanlanmaz
        d.AddPlayerScore(killerId, 1);
    }

    public bool IsMatchOver(MatchDirector d, out MatchOutcome outcome) { /* aşağıdaki kural */ }
}
```

**`IsMatchOver` kuralı** (Faz 7'nin `TryGetLeader` defterini kullanır):

1. `ScoreLimit > 0` ve liderin skoru `>= ScoreLimit` → `outcome = new("", liderId)`
2. `TimeRemaining <= 0`:
   - tepede **tek** oyuncu varsa → `new("", liderId)`
   - tepede **birden fazla** oyuncu (eşitlik) veya hiç oyuncu yoksa → `MatchOutcome.Draw`
3. Aksi hâlde `false`

> **Oyuncusuz maç:** admin harita önizlemesi (§10.1) FFA'da da çalışır — lider yoktur, süre akar,
> maç süre bitince berabere biter. Ek kod gerekmez.

**Kayıt:** `MatchDirector.RegisterModes()` → `Register(new FfaMode());` (tek satır, Faz 7 / 7.7).

---

## 2. Unity — mod kutusu `Assets/Modes/FreeForAll/`

```
Assets/Modes/FreeForAll/
  Scripts/VortexArena.Modes.Ffa.asmdef     refs: VortexArena.Core, .Net, .Protocol
          FfaClientController.cs           : ModeHudBase
  Data/FFA.asset                           ModeDefinition
  UI/FfaHud.prefab                         TdmHud'dan türetilir
```

⚠️ asmdef üretimi: **mevcut moddan JSON kopyala, `name`'i değiştir, `.meta`'yı KOPYALAMA**
(CLAUDE.md reçetesi — kopyalanan `.meta` GUID çakışması yapar).

### 2.1 `FfaClientController : ModeHudBase`

Faz 7'nin tabanı kill-feed / can / ölüm ekranı / geri sayım / faz+süre'yi zaten verir. Bu sınıf
yalnız **skor sunumunu** yazar:

| Üye | İçerik |
|---|---|
| `ScoreLine(MatchStateMsg)` | `SEN 7 · LİDER 9 (Gözlük 04)` — kendi skorun `lobby_state`'ten, lider en yüksek `score` |
| `WinnerLine(MatchEndMsg)` | `winnerPlayerId == PlayerId` → `KAZANDIN`; `> 0` → `<ad> KAZANDI`; `0` → `BERABERE` |
| `OnLobbyStateApplied(LobbyStateMsg)` | İlk 3 sıralamayı tazeler (`ad · skor`, azalan) |

Takım rengi/kolonu **yoktur** (kullanıcı kararı: takım şeyleri tabana da alt sınıfa da
zorlanmaz — FFA'da hiç çizilmez).

### 2.2 `Data/FFA.asset` (`ModeDefinition`)

| Alan | Değer |
|---|---|
| `modeId` | `ffa` |
| `displayName` | `Herkes Tek` |
| `roundSeconds` / `scoreLimit` | `300` / `20` (varsayılan — admin maç başında değiştirebilir) |
| `maps` | A10x10, A12x12, IceWorld, DemoVenue (TDM ile aynı liste) |
| `loadout` | AK47 + M4 `WeaponDefinition` (K9) — **rastgele verme havuzu budur** |
| `hudPrefab` | `FfaHud.prefab` |
| Kural alanları (Faz 7 / 7.11) | `teamMode=none`, `scoring=player`, `reviveAnchor=standstill`, `weaponSource=random`, `respawnDelay=0` |

### 2.3 `UI/FfaHud.prefab`

`TdmHud.prefab`'ın kopyası; skor metni tek satıra düşer, altına 3 satırlık sıralama alanı gelir.
Takım renkli öğeler (kırmızı/mavi vurgular) nötr tona çekilir.

---

## 3. Rastgele silah verme — `_Shared/Core/Combat/WeaponGranter.cs`

Yeni bileşen, **Core**'da (ikinci bir mod da kullanacak: Silah Yarışı `WeaponSource.Ladder`).

**Yerleşim:** kendini önyükleyen kalıcı tekil (`RuntimeInitializeOnLoadMethod` +
`DontDestroyOnLoad`) — `PlayerCombatState.cs:121-132` deseni. Sahneye bileşen konmaz; aksi hâlde
**her yeni arenaya elle ek adım** doğardı (admin gözlemcide bilinçle kaçınılan tuzak) ve CLAUDE.md'deki
arena kurulum listesi büyürdü.

### Davranış

```
ModeRuntime.Weapons != RandomGrant           → bileşen tümüyle pasif (TDM'de hiçbir şey yapmaz)

sahne yüklendiğinde (RandomGrant ise):
  • sahnedeki her Weapon (GrantedHold olmayan) → gameObject.SetActive(false)     [raf silahları]
  • sahnedeki her BaseZone                     → zone.enabled = false             [K5: bileşen!]
      └ zone'un SpawnPoint TAŞIMAYAN, Renderer'lı çocukları → SetActive(false)    [taban şeridi]
        (SpawnPoint çocukları DOKUNULMAZ — kayıtta kalmalı)

her karede (RandomGrant + PlayerCombatState.IsAlive):
  el ∈ {sol, sağ}:
    grip basılı  ve o el boş  → loadout'tan rastgele WeaponDefinition
                                → def.Prefab'ı el anchor'ının altına Instantiate
                                → GrantedHold = true, Grabbable/Rigidbody devre dışı
    grip bırakıldı ve el dolu → örneği Destroy

oyuncu öldüğünde (K7)                        → iki eldeki verilen silah da Destroy, verme durur
```

| Ayrıntı | Çözüm |
|---|---|
| El anchor'ı | `FindFirstObjectByType<OVRCameraRig>()` → `leftHandAnchor` / `rightHandAnchor`. İsimle arama YAPILMAZ (kırılgan); Core zaten `Oculus.VR` referanslıyor |
| Grip girdisi | `OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch / RTouch)` (K8) |
| Rastgelelik | `loadout` boşsa uyarı + no-op. Ard arda aynı silahın gelmesi **engellenmez** (gerçek rastgelelik; "asla aynısı gelmesin" kuralı 2 silahlık havuzda sırayla-dağıtıma dönüşür) |
| Loadout kaynağı | `GameCatalog.FindMode(ModeRuntime.ModeId).Loadout` (`Resources.Load<GameCatalog>("GameCatalog")`) |
| Süpürmeden muafiyet | `Weapon.GrantedHold == true` olanlar atlanır (K6) |
| Sahne değişimi | `SceneManager.sceneLoaded` → süpürme yeniden uygulanır; verilen silahlar temizlenir |

### `_Shared/Core/Combat/Weapon.cs` değişikliği (K3)

```csharp
/// <summary>WeaponSource.RandomGrant: silah kumandaya doğrudan verildi (ISDK kavraması yok).
/// Tutuş sayılır; iki elli sayılmaz (K4). WeaponGranter set eder.</summary>
public bool GrantedHold { get; set; }

public bool IsHeld => GrantedHold || (grabbable != null && grabbable.SelectingPointsCount > 0);
// IsTwoHanded DEĞİŞMEZ → verilen silah tek elli davranır
```

Ayrıca **reload kapatılır**: `GrantedHold` iken şarjör değiştirme yolu çalışmaz (kullanıcı kararı:
"şarjör bitince bırak, yenisini çek"). Boş şarjörle tetiğe basınca boş-tık geri bildirimi kalır.

---

## 4. İçerik ve katalog

| # | İş | Nerede |
|---|---|---|
| 4.1 | `FFA.asset` kataloğa eklenir | `_Shared/Data/Resources/GameCatalog.asset` → `modes[]` |
| 4.2 | Mevcut `MapDefinition`'ların `supportedModeIds`'ine **`ffa`** eklenir | A10x10, A12x12, IceWorld, DemoVenue |
| 4.3 | **`Tools > VortexArena > Export Server Config`** çalıştırılır | `Server/config/maps.json` tazelenir — atlanırsa `start_match` "harita bu modu desteklemiyor" der (`MatchDirector.cs:353-357`) |
| 4.4 | `ArenaTemplateWizard` yeni arenayı **uyumlu tüm modlara** ekliyor → FFA otomatik gelir | Kod değişikliği gerekmez |

> ⚠️ **`maps.json` elle düzenlenmez** — export ezer (§11).

---

## 5. Tuzaklar (uygulayan ajan için)

| # | Tuzak | Kaçınma |
|---|---|---|
| T1 | `BaseZone`'un **GameObject**'ini kapatmak | `SpawnPoint`'ler onun çocuğu → kayıttan düşer, spawn göstergesi çöker. Bileşeni kapat + şerit görselini ayrıca gizle (K5) |
| T1b | Yalnız `zone.enabled = false` deyip bitirmek | Taban şeridi **görünür kalır**; "raf da taban da gizlensin" kararı yarım uygulanmış olur (K5) |
| T2 | `ArenaBoundary`'yi kapatmak | `ArenaSpace.ClearOrigin` → tüm uzak avatarlar yanlış yere düşer (`Docs/Sistem-Ozeti.md` §7). FFA'da **dokunulmaz** |
| T3 | Verilen silahın ateş etmemesi | `IsHeld` yalnız `grabbable`'a bakıyor → `GrantedHold` eklenmeden hiçbir atış çıkmaz (K3) |
| T4 | Sahne süpürmesinin verilen silahı yok etmesi | `GrantedHold` işaretlilerini atla (K6) |
| T5 | `.meta` kopyalayarak asmdef üretmek | GUID çakışması; JSON'u kopyala, `.meta`'yı Unity üretsin |
| T6 | Export'u unutmak | 4.3 — `start_match` sessizce reddedilir, konsolda tek satır |
| T7 | FFA'da takımın `""` yerine `Red` kalması | Faz 7 / S8 — `PlayerCombatState.Team` varsayılanı `Neutral`'a çekilmiş olmalı |

---

## Görev listesi

| # | Görev | Dosya | Bağımlılık |
|---|---|---|---|
| 8.1 | `FfaMode` | `Server/…/Modes/FfaMode.cs` (yeni) | Faz 7 ✅ |
| 8.2 | Mod kaydı | `Server/…/MatchDirector.cs` (`RegisterModes`) | 8.1 |
| 8.3 | `Weapon.GrantedHold` + `IsHeld` + reload kapısı | `_Shared/Core/Combat/Weapon.cs` | Faz 7 ✅ |
| 8.4 | `WeaponGranter` | `_Shared/Core/Combat/WeaponGranter.cs` (yeni) | 8.3 |
| 8.5 | asmdef + `FfaClientController` | `Assets/Modes/FreeForAll/Scripts/` | Faz 7 (7.14) |
| 8.6 | `FfaHud.prefab` | `Assets/Modes/FreeForAll/UI/` | 8.5 |
| 8.7 | `FFA.asset` (`ModeDefinition`) | `Assets/Modes/FreeForAll/Data/` | 8.6 |
| 8.8 | Katalog + harita uyumluluğu + export | `GameCatalog.asset`, 4 `MapDefinition`, `maps.json` | 8.7 |
| 8.9 | `Docs/ArenaNet-Protokol.md`'ye `modId: "ffa"` işlenir | `Docs/…` | 8.1 |

---

## Derleme kapısı

Uygulama bitince tek geçiş (`.claude/rules/batch-build-verification.md`):

| # | Kontrol | Beklenen |
|---|---|---|
| D1 | `dotnet build Server/` | 0 hata / 0 uyarı |
| D2 | `unity cmd recompile` + `get_console_logs` | 0 hata / 0 uyarı |

## Dokümana yansıyacaklar (aynı commit — `.claude/rules/docs-sync.md`)

| Doküman | Ne yazılacak |
|---|---|
| `Docs/ArenaNet-Protokol.md` | `modId: "ffa"` + §10.5'e FFA satırı (kuralların somut örneği) |
| `Docs/Sistem-Ozeti.md` | §2 `Assets/Modes/FreeForAll/` + `WeaponGranter`, §4 bileşen sözlüğü, §7 tuzaklar (T1/T3), §8 durum |
| `CLAUDE.md` | Mod listesine FFA; `WeaponGranter` + "silah rafsız mod" notu |
| `Server/README.md` | Kayıtlı modlar: `tdm`, `ffa` |
| `Docs/Kullanim-Kilavuzu.md` | Operatör dili: "Herkes Tek" modu, süre/limit seçimi, silahın hold ile gelmesi |
| `plan/` | Faz 8 dosyası silinir, `plan/README.md`'den satırı çıkarılır |
