# Faz makinesinin yeniden tanımı: `paused · playing · finished` + lobi = mod

> Durum: **büyük kısmı yazıldı** (doküman + protokol + sunucu + istemci derleniyor, hatasız).
> Kalanlar aşağıda "Yapılacaklar" başlığında. Hepsi bitince bu dosya silinir; kalıcı bilgi
> `Docs/ArenaNet-Protokol.md` (protokol) ve `Docs/Sistem-Ozeti.md` (akış) altına işlendi.

## Yapılacaklar (kalan)

1. **`Assets/_Shared/Scenes/Lobby.unity` temizliği** — `LobbyController`'dan roster/hazır/takım
   ALANLARI ve metotları silindi; sahnedeki karşılıkları (roster metni, "HAZIR OL" düğmesi, takım
   düğmeleri) hâlâ duruyor ve `onClick`'leri artık var olmayan metotlara bakıyor. Sahne, editörde
   kaydedilmemiş başka bir sahne açıkken düzenlenmedi.
2. **Operatör duraklatma komutu YOK.** `phaseReason:"operator"` tel değeri ve sunucu tarafı hazır,
   ama bunu tetikleyecek bir admin komutu (`pause_match`/`resume_match`) ve düğmesi yazılmadı —
   bilinçli: tüketicisi olmayan kanca eklenmiyor. İstenirse ayrı bir iş.
3. **Duman testi** — sunucu + editör istemcisiyle: lobide ateş (hasarsız), `start_match`, geri
   sayım, `playing`'de hasar, `finished`, lobiye dönüş.
4. **Yeniden build** — `PROTOCOL_VERSION` 2→3 arttı: sahadaki APK ve admin build'leri yenilenmeli.

## Neden

Bugünkü faz makinesi `Lobby → Loading → Countdown → Live → End → Lobby` (`MatchDirector.cs`),
ve lobi **bir fazdır**. Bu iki sorunu doğuruyor:

1. **Faz birden çok iş yapıyor.** Aynı enum hem "hasar açık mı" hem "hangi ekran görünsün" hem
   "operatör ne yapabilir" sorularını cevaplıyor. Bir mod yeni bir ara durum isterse (turnuva:
   "herkes öldü, tabana dönülüyor") çekirdek enum'unu büyütmek gerekiyor — yani **her mod
   çekirdeği kirletiyor.**
2. **Lobi aslında bir tür.** "Hasar yok ama ateş serbest" bir maç durumu değil, lobinin tür
   özelliği. Faz olarak tutulunca turnuva da kendi fazını isteyecek.

## Hedef model — dört alan, dört ayrı sahip

| Alan | Sahibi | Anlamı | Değerler |
|---|---|---|---|
| `modeId` | operatör seçimi | Ne oynanıyor | `lobby` · `tdm` · `ffa` · (ileride `tournament`) |
| `phase` | çekirdek (`MatchDirector`) | Maçın genel durumu | `paused` · `playing` · `finished` |
| `phaseReason` | çekirdek | Neden duraklı | `""` · `lobby` · `loading` · `countdown` · `operator` · `mode` |
| `modeState` | mod (`IGameMode`) | Modun kendi ara durumu | serbest string (`round3`, `regroup`…) |

**Bağlayıcı kurallar:**

- ⚠️ **`phase`'in TEK yetkisi hasar kapısıdır:** `hit_report` yalnız `playing` fazında işlenir.
  Başka hiçbir kural doğrudan `phase`'e bakmaz.
- ⚠️ **`modeState` asla kural/hasar kapısı olamaz** — çekirdek onu okumaz, yalnız HUD okur. Mod
  duraklatmak isterse çekirdekten `phase = paused` + `phaseReason = "mode"` ister, gerekçeyi
  `modeState`'e yazar. Aksi hâlde ikinci bir otorite doğar.
- ⚠️ **Ateş serbestliği moda aittir, faza değil.** Yeni kural alanı `ModeRules.fireWhilePaused`
  (`teamMode`/`reviveAnchor`/`weaponSource` ile aynı yerde, `load_match.rules` ile gider,
  `ModeRuntime`'dan okunur). Lobide `true`, savaş modlarında `false`.
  İstemcide **`if (modeId == "lobby")` zinciri YAZILMAZ** (CLAUDE.md).
- `lobby` kayıtlı bir `IGameMode` **değildir** ve olmayacak: `start_match` onu reddeder. Lobi
  haritasında yalnız lobi türü seçilebilir — bu zaten `MapDefinition.supportedModeIds == ["lobby"]`
  ile sağlanıyor, yeni kural yazılmaz.

### Bugünkü fazlar nereye gidiyor

| Bugün | Yarın |
|---|---|
| `Lobby` | `phase=paused`, `phaseReason=lobby`, `modeId=lobby` |
| `Loading` | `phase=paused`, `phaseReason=loading` (kapı aynen kalır: tüm `set_ready` veya `LOADING_TIMEOUT`) |
| `Countdown` | `phase=paused`, `phaseReason=countdown` (`countdown` mesajı aynen kalır) |
| `Live` | `phase=playing` |
| `End` | `phase=finished` |
| — | `phase=paused`, `phaseReason=operator` — **yeni:** operatör koşan maçı dondurur |
| — | `phase=paused`, `phaseReason=mode` — **yeni:** mod duraklatma ister (turnuva toplanması) |

`paused`'da **hasar alma da verme de kapalıdır, başka ek kuralı yoktur.**

### Turnuva neden `phaseReason`'ı zorunlu kılıyor

Turnuva "herkes öldü → herkes tabana dönene kadar bekle" derken `phase=paused`,
`phaseReason=mode`, `modeState=regroup`. Operatör tam o sırada maçı duraklatırsa `phaseReason`
`operator` olur ama `modeState` `regroup` kalır — devam edilince mod kaldığı yerden sürer. Tek
alanla bu iki durum ayırt edilemez, HUD yanlış mesaj gösterirdi.

## İş sırası (kural: önce doküman, sonra kod)

### 1. Doküman
- `Docs/ArenaNet-Protokol.md`: §5.3 (`match_state`, `welcome.match`), §10.1 faz diyagramı,
  §10.3 (`hit_report` kapısı `Live` → `playing`), §10.5 (`ModeRules` yeni alan), §10.7 (lobi
  bölümü baştan yazılır: lobi artık faz değil tür).
- `Docs/Sistem-Ozeti.md` §3 (ağ akışı) + §4 (bileşen sözlüğü).
- `CLAUDE.md`: "Lobide savaşı kapatan şey kural değil fazdır" satırı yanlışlanıyor → düzelt.

### 2. Protokol katmanı (`_Shared/Net/Protocol`, sunucu aynı dosyaları derler)
- `MatchInfo` + `MatchStateMsg`: `phase` değerleri değişir, `phaseReason` ve `modeState` eklenir.
- `ModeRulesInfo`: `fireWhilePaused` eklenir.
- **`PROTOCOL_VERSION` artar** → ⚠️ sahadaki tüm APK ve admin build'leri yeniden alınmalı,
  eski istemci bağlanamaz.

### 3. Sunucu
- `Phase` enum → `Paused · Playing · Finished`; `_phaseReason` alanı.
- `EnterLobbyLocked` → `EnterPausedLocked(reason)`; `EnterEndLocked` → `finished`.
- Faz kapıları sadeleşir: bugün `_phase != Phase.Lobby` diye reddeden yerler
  (`StageSceneAsync`, `set_selection`, `start_match`) **`phase == Playing`** kapısına döner —
  böylece maç bitmişken (`finished`) operatör harita/mod değiştirebilir.
- `hit_report` kapısı: `Phase.Live` → `Phase.Playing`.
- `IGameMode`'a duraklatma kancası **şimdi eklenmez** (tüketicisi yok — CLAUDE.md kuralı);
  turnuva modu yazılırken eklenir. Tel formatı şimdiden hazır olur.

### 4. İstemci
- `PlayerCombatState`: 5 faz sabiti → 3; `CanFire` = `phase == playing || ModeRuntime.FireWhilePaused`.
- `WeaponAnimator`, `ModeHudBase` (faz/süre metinleri, geri sayım), `AdminHud` / `AdminRoster` /
  `AdminPreferencesPanel` faz göstergeleri ve düğme kapıları.
- `ModeRuntime`: yeni kural alanının okunması.

### 5. Aynı geçişte bitirilecek iki küçük iş
- **Kabuk `Lobby` daraltılır:** `LobbyController`'dan roster, "Hazır" düğmesi, takım düğmeleri ve
  `OnLobbyState` aboneliği çıkar; `Lobby.unity`'de karşılık gelen UI silinir. Kalan: durum metni,
  gizli IP paneli (A×2 → numpad), `kicked`, bekleme. Kabuk lobi **bağlanana kadar beklenen yer**
  ve sunucunun bildirdiği sahne yüklenemezse düşülen yerdir.
- **`set_team` yalnız admin:** `ClientConnection.cs` oyuncunun kendi takımını değiştirmesini
  reddetsin; `Docs/ArenaNet-Protokol.md` §5.2 satırı §10.7 ile hizalansın (bugün ikisi çelişiyor).
  Admin herkesin takımını **her fazda** değiştirebilir — bu bugün de çalışıyor, kısıtlanmayacak.

### 6. Doğrulama (tek geçiş, kural: batch)
`dotnet build` (sunucu) + Unity CLI `recompile` + `get_console_logs`.

### 7. Sunucu açılışında lobi sahnesi garantisi (fail-fast)

`MatchDirector` kurulurken açık sahne çözülemiyorsa (`server.json → lobbyScene` boş **ve**
`MapTable.ResolveLobbyScene()` mekanda `modes == ["lobby"]` olan harita bulamıyorsa, ya da
verilen `lobbyScene` `maps.json`'da yoksa) **sunucu açılmaz**: sebebi ve düzeltme yolunu yazıp
sıfırdan farklı bir çıkış koduyla kapanır.

Gerekçe: sunucunun her zaman açık bir sahnesi olması istemcinin tek yönlendirme kaynağı.
Çözülemiyorsa zaten yapılandırma hatası vardır ve oyuncu doğru oynayamaz — sessizce boş sahneyle
açılmak hatayı sahaya taşır.

## Kapsam dışı (bilinçli)

- `return_to_lobby` mesaj adı olduğu gibi kalır. Gerçekte "sahneyi sahnele" demek ve artık lobi bir
  faz olmadığı için adı yanıltıcı; ama yeniden adlandırma protokol gürültüsü ve davranışı
  değiştirmiyor. İstenirse `PROTOCOL_VERSION` zaten arttığı için maliyeti düşük.
- Turnuva modunun kendisi. Bu plan yalnız onun ihtiyaç duyacağı alanları açar.
