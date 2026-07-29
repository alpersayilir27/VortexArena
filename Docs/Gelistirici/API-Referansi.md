---
title: API Referansı
---

# API Referansı

Oyun kodundan çağırabileceğin her şey. Sıralama **kullanım sıklığına** göre.

> **Okuma anahtarı:** ✅ = serbestçe çağır · ⚠️ = kuralına dikkat et · ⛔ = çağırma, sistem yapar.

| Katman | Namespace | Ne için |
|---|---|---|
| Savaş | `VortexArena.Core.Combat` | Vuruş bildirme, can, ateş yetkisi |
| Ağ olayları | `VortexArena.Net` | Sunucudan gelen her şey |
| Mod kuralları | `VortexArena.Core` | Modun şekli, katalog |
| Arena | `VortexArena.Core.Arena` | Koordinat, sınır, taban bölgesi, başlangıç noktası |
| UI | `VortexArena.Core.UI` | HUD tabanı |
| DTO'lar | `VortexArena.Protocol` | Olay parametrelerinin tipleri |

Assembly bağımlılığı hep aşağı akar: `Protocol ← Net ← Core ← App, Modes.<X>`.
Mod assembly'leri **birbirini referanslamaz**; ortak kod `Core`'a konur.

---

## ArenaCombat

`VortexArena.Core.Combat.ArenaCombat` — **statik**. Oyun kodunun ağa açılan tek kapısı.
Hepsi bağlantı yokken sessizce no-op'tur.

### Durum

| Üye | Tip | Açıklama |
|---|---|---|
| ✅ `CanFire` | `bool` | **Ateş etmeden önce bunu sor.** Hayatta + faz Lobby/Live + bağlantı açık. Hiç bağlanılmadıysa `true` (yerel test bozulmasın) |
| ✅ `IsAlive` | `bool` | Yerel oyuncu hayatta mı (sunucu-otoriter) |
| ✅ `LocalHp` | `float` | Yerel can, `0..100` |
| ✅ `LocalPlayerId` | `int` | Sunucu kimliği; bağlanmadıysa `0` |
| ✅ `LocalTeam` | `Team` | `Red`/`Blue`/**`Neutral`** (takımsız modda Neutral) |
| ✅ `IsConnected` | `bool` | Mesajlar gerçekten gidiyor mu |

### Hedef çözme

| Metot | Döner | Açıklama |
|---|---|---|
| ✅ `TryGetTargetPlayerId(Collider, out int playerId)` | `bool` | Çarpılan collider'ın arkasında ağ oyuncusu var mı. `false` → **hasar yok** (istemcide can tutulmaz) |
| ✅ `IsHeadshot(Collider)` | `bool` | Kafa kutusuna mı isabet etti. **Çarpanı sen uygularsın** |

### Bildirme

| Metot | Ne yapar |
|---|---|
| ✅ `ReportShot(Vector3 worldMuzzlePos, Vector3 worldDir, string weaponId)` | Atışı diğer oyunculara relay ettirir (namlu alevi/ses). Hasarla ilgisi yok, sunucu doğrulamaz |
| ⚠️ `ReportHit(int targetPlayerId, Vector3 worldHitPoint, float damage, string weaponId)` | Vuruşu bildirir. **Hasarı sen belirlersin**, sunucu aynen uygular. Canı yerelde düşürme |
| ✅ `ReportRaycastHit(in RaycastHit, float damage, string weaponId)` | Hitscan kısayolu. `false` → hedef ağ oyuncusu değil, **hasar uygulanmaz**; dönüş değeri yalnız sunum kararı içindir (gövde efekti mi, duvar efekti mi) |
| ⚠️ `ReportAreaHit(Vector3 center, float radius, float damage, string weaponId, float edgeScale = 0.25f, int layerMask = ~0)` | Yarıçaptaki her oyuncuya ayrı vuruş; merkeze uzaklıkla doğrusal düşer. **Duvar arkası kontrolü yok** |

**Hasar geçerlilik kuralı:** pozitif ve sonlu olmalı. `NaN`/`∞`/negatif hem burada hem sunucuda
reddedilir (NaN'a düşen can bir daha 0'ın altına inemez → oyuncu ölümsüz kalırdı).

---

## NetEvents

`VortexArena.Net.NetEvents` — **statik olaylar**. Sunucudan gelen her şey buradan akar.
Statik olmalarının sebebi: dinleyicinin bağlantının ne zaman kurulduğunu bilmek zorunda kalmaması.

| Olay | Parametre | Ne zaman |
|---|---|---|
| ✅ `OnConnected` | `WelcomeMsg` | Sunucuya bağlanıldı; `playerId` ve (geç katılımda) koşan maç bilgisi |
| ✅ `OnDisconnected` | — | Bağlantı koptu |
| ✅ `OnConnectionStateChanged` | `ArenaConnectionState` | Bağlantı durumu değişti |
| ✅ `OnLobbyState` | `LobbyStateMsg` | Roster tazelendi: adlar, takımlar, K/D, **bireysel skor**, can |
| ✅ `OnLoadMatch` | `LoadMatchMsg` | Maç kuruluyor — **sahne yüklenmeden ÖNCE gelir** |
| ✅ `OnCountdown` | `CountdownMsg` | Geri sayım (5,4,3,2,1) |
| ✅ `OnMatchState` | `MatchStateMsg` | **Saniyede bir**: faz, kalan süre, takım skorları |
| ✅ `OnHealthUpdate` | `HealthUpdateMsg` | Birinin canı değişti (`attackerId == 0` → canlanma) |
| ✅ `OnKillEvent` | `KillEventMsg` | Öldürme (`killerId == 0` → çevre ölümü) |
| ✅ `OnRespawn` | `RespawnMsg` | **Yalnız ölen oyuncuya**: canlanma gecikmesi (konum taşımaz) |
| ✅ `OnMatchEnd` | `MatchEndMsg` | Maç bitti; kazanan takım **veya** oyuncu |
| ✅ `OnReturnToLobby` | `ReturnToLobbyMsg` | Herkes lobiye dönüyor. Mesaj lobi sahnesini + profilini taşır (§10.7); ilgilenmiyorsan parametreyi yok say |
| ✅ `OnShotFired` | `ShotFiredMsg` | **Başkası** ateş etti (atana gönderilmez). Pozlar arena uzayında |
| ✅ `OnIdentify` | `IdentifyMsg` | Admin "bu cihazı tanıt" dedi |
| ✅ `OnKicked` | `KickedMsg` | Bağlantıdan atıldık |
| ⛔ `OnAdminState` | `AdminStateMsg` | Yalnız admin arayüzü içindir |

> ⚠️ **`OnDisable`'da abonelikten çık.** Statik olay, ölü nesneyi tutar → `MissingReferenceException`.

> ⚠️ **`OnLoadMatch` sahne yüklenmeden önce gelir.** Sahnedeki bir bileşende dinlersen kaçırırsın.
> Sahneye özel iş için `SceneRouter.Instance.LastModeId` / `LastMatchScene`'i `Start`'ta oku.

### DTO alanları (hızlı bakış)

```csharp
MatchStateMsg   { string phase; float timeRemaining; int scoreRed; int scoreBlue; }
HealthUpdateMsg { int playerId; float hp; int attackerId; }
KillEventMsg    { int killerId; int victimId; string weaponId; }
RespawnMsg      { int playerId; float delaySeconds; }
MatchEndMsg     { string winnerTeam; int winnerPlayerId; int scoreRed; int scoreBlue; }
CountdownMsg    { int seconds; }
ShotFiredMsg    { int playerId; string weaponId; float[] muzzlePos; float[] muzzleDir; }
LoadMatchMsg    { string modeId; string sceneName; int roundSeconds; int scoreLimit;
                  string yourTeam; ModeRulesInfo rules; }
PlayerInfo      { int playerId; string name; string role; string team; bool ready; bool online;
                  float battery; string scene; int kills; int deaths; float hp; bool alive; int score; }
```

Tam protokol → [ArenaNet Protokolü](../ArenaNet-Protokol.md).

---

## PlayerCombatState

`VortexArena.Core.Combat.PlayerCombatState` — **yerel** oyuncunun maç durumu.
Kalıcı tekil, kendini önyükler (`Instance`). Sahneye koyma.

| Üye | Tip | Açıklama |
|---|---|---|
| ✅ `Instance` | `PlayerCombatState` | ⚠️ `Awake`'te henüz `null` olabilir |
| ✅ `PlayerId` | `int` | Sunucu kimliği |
| ✅ `Team` | `Team` | Takımsız modda `Neutral` |
| ✅ `ModeId` | `string` | Aktif mod |
| ✅ `Phase` | `string` | `"Lobby"`/`"Loading"`/`"Countdown"`/`"Live"`/`"End"` |
| ✅ `Hp` | `float` | Yalnız `health_update`'ten set edilir |
| ✅ `IsAlive` | `bool` | |
| ✅ `StatusText` | `string` | Ölüm/canlanma metni; ⚠️ kendi metnini yazma |
| ✅ `CanFire` | `bool` | |
| ✅ `HpChanged` / `AliveChanged` / `StatusChanged` | olay | `float` / `bool` / `string` |

> ⛔ Bu sınıf hasar uygulamaz, skor tutmaz, faz değiştirmez — ve **hiçbir koşulda rig'i taşımaz.**

---

## ModeRuntime

`VortexArena.Core.ModeRuntime` — **statik**. Aktif maçın kurallarının tek okuma noktası.

| Üye | Tip | Değerler |
|---|---|---|
| ✅ `ModeId` | `string` | `"tdm"`, `"ffa"`, … |
| ✅ `Teams` | `ModeTeamMode` | `TwoTeams` \| `None` |
| ✅ `IsTeamless` | `bool` | `Teams == None` kısayolu |
| ✅ `Scoring` | `ModeScoreKind` | `Team` \| `Player` |
| ✅ `FriendlyFire` | `bool` | |
| ✅ `Revive` | `ModeReviveAnchor` | `OwnBase` \| `StandStill` |
| ✅ `Weapons` | `ModeWeaponSource` | `Rack` \| `RandomGrant` |
| ✅ `RespawnDelay` | `float` | ⚠️ **`0` geçerlidir** (anında canlanma) |
| ✅ `Changed` | olay | Kurallar değişti |
| ⛔ `Apply` / `ApplyFromCatalog` / `Reset` | | Besleme sistemin işi |

> ⚠️ **`if (modeId == "…")` zinciri yazma.** Yeni mod eklemek senin kodunu değiştirmemeli.

> ⚠️ **Serialize edilen mod enum'larına yeni değer SONA eklenir.** Unity enum'ları sayısal indeksle
> saklar; başa/ortaya ekleme sahnelerdeki tüm değerleri kaydırır. Aynı kural `Team` için de geçerli
> (`Neutral` bu yüzden sonda).

---

## Arena (koordinat, sınır, taban bölgesi, başlangıç noktası)

`VortexArena.Core.Arena`

### ArenaSpace — statik

| Metot | Açıklama |
|---|---|
| ✅ `WorldToArena(Vector3 / Quaternion / Pose)` | Ağa göndermeden önce |
| ✅ `ArenaToWorld(Vector3 / Quaternion / Pose)` | Ağdan aldıktan sonra |
| ✅ `HasOrigin` | Arena orijini kayıtlı mı (Lobby'de değildir) — origin'i `SpawnPoint` kaydeder |

> ⚠️ Yön vektörü için iki noktayı çevirip farkını al — [reçete 16](Yemek-Kitabi.md#16-bir-konumu-ağ-üzerinden-paylaşmak-arena-uzayı).

> ⚠️ Origin yokken dönüşümler **kimlik** davranır (dünya = arena) ve `ArenaSpace` sahne başına bir
> kez uyarı basar. Lobide bu normaldir; arena sahnesinde `SpawnPoint` eksik demektir.

### ArenaBoundary

Fiziksel sınır uyarısını çizer (duvar alfası + karartma). Sahnede **bir tane** olmalı.
**Arena orijinini KAYDETMEZ** — o `SpawnPoint`'in işidir.

| Üye | Açıklama |
|---|---|
| ✅ `IsOutOfBounds` | Yerel HMD alan dışında mı |
| ✅ `HalfExtents` | Arena yarı ölçüsü — plandaki çokgenin sınırlayıcı kutusundan gelir (plan yoksa sıfır) |
| ✅ `LocalCenter` | O kutunun yerel merkezi — admin kuş bakışı kadrajı bunu okur. ⚠️ Ölçü genellikle bir köşeden alınır, yani kutu transformun tam ortasında DEĞİLDİR: kadrajlarken `HalfExtents` tek başına yetmez |
| ✅ `SetSpectatorMode(bool, float)` | Muhafazayı susturur, duvarları çizili bırakır |

Ölçünün **tek kaynağı** `dimensionsJson` alanına bağlanan boyut dosyasıdır (`ArenaDimensions`);
bileşen ölçü tutan başka bir alan taşımaz. Plan çözüldüğünde kenar mesafesi çokgene, kolonlara ve
sahnedeki `ArenaObstacle`'lara olan mesafenin **en küçüğü** olur. Dosya kare başına ayrıştırılmaz
(referans değişmedikçe önbellek).

> ⛔ **Boyut dosyası ZORUNLUDUR.** Bağlı değilse ya da çözülemiyorsa bileşen bir kez
> `Debug.LogError` basar ve **kendini devre dışı bırakır** — duvar alfası, karartma ve alan-dışı
> uyarısı çalışmaz. Açık başarısızlık bilinçli: ölçüsü bilinmeyen arenada doğru muhafaza zaten
> üretilemez, her karede ekranı karartmak ise oyunu tümden oynanamaz kılardı.

### ArenaDimensions

**Arena ölçüsünün tek doğruluk kaynağı** — elle yazılabilir bir JSON dosyası olarak yaşayan saf
veri sınıfı: `name`, `outline` (kapalı çokgen; ilk nokta sona tekrarlanmaz), `wallHeight`,
`columns[]` (`name`/`center`/`size`/`yaw`/`height`), `defaultColumnHeight`, `columnsBlockPlayer`.
Koordinatlar metre ve `ArenaBoundary`'yi taşıyan transformun **yerel XZ**'sindedir — JSON'daki
`y` dünya **Z**'sidir.

> ⛔ **Dikdörtgen alan için ayrı bir kip YOKTUR:** alan tam kare bile olsa dört köşeli bir `outline`
> olarak yazılır. Aynı ölçünün iki ayrı ifadesi kaçınılmaz olarak birbirinden saptığı için ikinci
> temsil (bileşen üstünde yarım ölçü + merkez alanları) kaldırıldı ve geri eklenmez.

| Üye | Açıklama |
|---|---|
| ✅ `Parse(string, out string error)` | Metinden çözer. **Exception FIRLATMAZ** — bozuk girdide `null` + hata metni döner (çağıran yer sahne yükleme yolu; bir yazım hatası sahneyi düşürmemeli) |
| ✅ `FromTextAsset(TextAsset, out string error)` | Aynısı `TextAsset` üzerinden; asset `null` ise sessizce `null` |
| ✅ `LocalBounds()` | Çokgenin yerel XZ sınırlayıcı kutusu (muhafaza ölçüsü + kuş bakışı kadrajı bundan türer) |
| ✅ `ToJson(bool pretty)` | Planı metne çevirir — editör araçları dosyayı bununla yazar |

`JsonUtility.FromJsonOverwrite` kullanılır: **JSON'da yazılmayan alan varsayılanında kalır**
(`FromJson` ile eksik bir `wallHeight` sessizce 0 olurdu = duvarsız arena).

> ⚠️ Dosya **çalışma anında** okunur → bir sahneden referanslanmalıdır. `Assets/` altında durup
> kimsenin referanslamadığı bir `TextAsset` build'e **girmez**.

> ⛔ **Muhafazayı susturmak için bileşeni kapatma** — kapalı bileşen duvar alfasını son değerinde
> dondurur, karartma açık kalabilir. Doğrusu `SetSpectatorMode(true)`.

### ArenaObstacle

Elle konan engel (kolon, kasa, direk): `ArenaBoundary` onu muhafaza hesabına katar, oyuncu
yaklaşınca uyarı alır. Ölçü `Size` alanından gelir, transform scale'inden değil.

> ⛔ **Collider DEĞİLDİR, fizik YAPMAZ.** Free-roam'da oyuncuyu durduran şey gerçek dünyadaki
> nesnedir; bu bileşenin tek işi uyarı üretmek.

### BaseZone (taban bölgesi)

Arenadaki kırmızı/mavi şerit. Ölen oyuncu buraya fiziken girince canlanır (`reviveAnchor:"base"`).

| Üye | Açıklama |
|---|---|
| ✅ `BaseZone.Team` | Bölgeyi kim kullanabilir; `Team.Neutral` = **herkes** |
| ✅ `BaseZone.IsPlayerInside` | Yerel oyuncunun HMD'si bölgede mi (bileşen kapalıyken DONAR) |
| ✅ `BaseZone.onPlayerEntered` / `.onPlayerExited` | UnityEvent — iyileşme/tazeleme buraya takılır |

Eşleşme kuralı: bölge açıktır eğer takımı oyuncununkiyle aynıysa, bölge `Neutral` ise ya da
oyuncunun takımı boşsa (takımsız mod). Aynı takımdan birden çok bölge konabilir —
**herhangi birine** girmek yeter.

> ⚠️ **GameObject'ini kapatma** — altına konmuş marker'lar kayıttan düşer. Gerekiyorsa **bileşeni**
> kapat (`zone.enabled = false`). Kapalı bölge canlanma için açık sayılmaz.

### SpawnPoint (tek başlangıç noktası — **arena origin'i**)

Arena başına **bir tane**. Takımı ve slotu yoktur. `GameObject > VortexArena > Spawn Point` ile
eklenir, elle yerleştirilir. İki işi vardır: maç öncesi yerleşim göstergesi **ve arena uzayının
sıfırı** (`ArenaSpace.SetOrigin` buradan çağrılır).

| Üye | Açıklama |
|---|---|
| ✅ `SpawnPoint.Current` | Sahnedeki nokta; yoksa `null`. **Yalnız Play kipinde** dolar. Origin buna bağlanır |
| ✅ `SpawnPoint.All` | Play kipinde kayıtlı noktalar (normalde 0 ya da 1) |

> ⛔ Bu bir *gösterge*dir, hedef değil. **Hiçbir kod oyuncuyu oraya taşımaz** — ne maç başında,
> ne canlanmada, ne harita değişiminde. Protokolde konum/slot taşıyan bir alan yoktur.

> ⛔ **Yerleştirdikten sonra oynatma.** Bir metre kaydırmak arenadaki **herkesin** koordinatını bir
> metre kaydırır; hata ancak iki başlık aynı sahnede buluşunca görünür.

> ⚠️ **Zemin seviyesinde durmalı:** uzak avatarların bastığı zemin
> `ArenaSpace.ArenaToWorld(Vector3.zero).y`'den gelir (`ThreePointBodyIK`) — marker havadaysa
> avatarların ayakları da havada kalır.

> ⚠️ Editör aracı yazıyorsan `SpawnPoint.All` yerine `FindObjectsByType<SpawnPoint>` kullan:
> kayıt `OnEnable`'da dolar, edit kipinde `OnEnable` çalışmaz.

---

## RemotePlayerRegistry

`VortexArena.Net.RemotePlayerRegistry` — uzak oyuncuların pozları.

| Üye | Açıklama |
|---|---|
| ✅ `Instance` | Tekil |
| ✅ `GetInterpolatedPose(int playerId, out Pose head, out Pose handL, out Pose handR)` | **Arena uzayında** yumuşatılmış poz |
| ✅ `IsAlive(int playerId)` | |
| ✅ `GetActivePlayerIds(List<int> buffer)` | Tampon verilir — çöp üretmez |
| ✅ `OnRemoteJoined` / `OnRemoteLeft` | `Action<int>` |
| ⛔ `IngestFromNetThread` | Ağ katmanının işi |

> ⚠️ Ölü oyuncuları eleme — bedenleri sahada durmaya devam eder (çarpışma riski).

---

## ModeHudBase

`VortexArena.Core.UI.ModeHudBase` — mod HUD'larının takım-agnostik tabanı.

| Üye | Tür | Açıklama |
|---|---|---|
| ✅ `ScoreLine(MatchStateMsg)` | `abstract` | **Zorunlu.** Skor satırı |
| ✅ `WinnerLine(MatchEndMsg)` | `abstract` | **Zorunlu.** Maç sonu başlığı |
| ✅ `EndScoreLine(MatchEndMsg)` | `virtual` | `null` → son değer korunur |
| ✅ `OnLobbyStateApplied(LobbyStateMsg)` | `virtual` | Bireysel skor tabloları için |
| ✅ `NameOf(int playerId)` | yardımcı | `playerId` → ad |
| ✅ `FindSelf(LobbyStateMsg)` | yardımcı | Kendi roster satırın |
| ✅ `SetText(TMP_Text, string)` | yardımcı | Null-güvenli |

Tabandan **hazır** gelenler: faz/süre, geri sayım, can + can barı, ölüm ekranı, durum metni,
kill-feed, kendi öldürme/ölüm sayacın.

> ⚠️ Takıma ait hiçbir şey tabanda değildir (bazı modlarda takım yoktur) — renk ve kolon alt sınıfın işi.

> ⚠️ Yaşam döngüsü metotlarını override edersen `base.` çağır.

---

## ArenaClient

`VortexArena.Net.ArenaClient` — WebSocket bağlantısı. Oyun kodunda **nadiren** gerekir.

| Üye | Açıklama |
|---|---|
| ✅ `Instance` / `IsConnected` / `State` | Durum |
| ✅ `PlayerId` / `ServerIp` / `ServerPort` / `LastError` | Bilgi |
| ⚠️ `Send<T>(T msg)` | Ham DTO gönderimi — **vuruş/atış için `ArenaCombat` kullan** |
| ⛔ `Connect` / `Disconnect` | Akışı `AppBoot`/`SceneRouter` yönetir |

---

## Sabitler — ArenaProtocol

`VortexArena.Protocol.ArenaProtocol`. Sayıyı koda gömme, buradan oku.

| Sabit | Değer | Anlamı |
|---|---|---|
| `PLAYER_MAX_HP` | `100` | Tam can |
| `COUNTDOWN_SECONDS` | `5` | Maç öncesi geri sayım |
| `MATCH_END_SECONDS` | `10` | Maç sonu ekranı süresi |
| `RESPAWN_DELAY` | `5` | Varsayılan canlanma gecikmesi (mod ezebilir) |
| `REVIVE_HOLD_SECONDS` | `3` | "Sabit dur" canlanmasında bekleme |
| `REVIVE_HOLD_RADIUS` | `1` | Sabit durma toleransı (m) |
| `REVIVE_GRACE` | `20` | Sunucunun zorla canlandırma emniyeti |
| `POSE_RATE_HZ` / `SNAPSHOT_RATE_HZ` | `20` | Poz gönderim/yayın hızı |
| `INTERP_DELAY_MS` | `100` | Uzak poz interpolasyon gecikmesi |
| `PLAYER_ID_MAX` | `255` | `playerId` UDP'de `u8` |
| `LOADING_TIMEOUT` | `20` | Sahne yükleme kapısı |

> **Eşzamanlı oyuncu kotası YOKTUR.** Tek tavan `PLAYER_ID_MAX` ve o bir ürün kararı değil,
> protokol sonucudur.

---

## Editör araçları

| Menü | Ne yapar |
|---|---|
| `Tools > VortexArena > Dev` | Rol · sunucu hedefi · Play başlangıcı · derle. Kısayol **Ctrl+Alt+R** (rol çevirir) |
| `Tools > VortexArena > Create Arena From Template` | Yeni arena sihirbazı — geometri kaynağı ZORUNLU: boyut dosyası ya da TestMesh kökü |
| `Tools > VortexArena > Build Arena From Dimensions` | Seçili boyut dosyasından zemin/duvar/kolon üretir — `ArenaGeometry` dalı altına, idempotent |
| `Tools > VortexArena > Build Arena From TestMesh` | Kaba blok yığınından bir boyut dosyası çıkarıp diske yazar, geometriyi o dosyadan üretir |
| `Tools > VortexArena > Export Server Config` | `MapDefinition` SO'larından `Server/config/maps.json` — girdi başına yalnız `sceneName` + `modes` (arena ölçüsü sunucuya gitmez). ⚠️ JSON'u elle düzenleme, export ezer |
| `GameObject > VortexArena > Spawn Point` | Arenanın **tek** başlangıç noktasını üretir (yerleştirme elle) |
| `GameObject > VortexArena > Arena Roof` | Çatı geometrisini işaretler (admin kuş bakışında gizlenir) |
| `GameObject > VortexArena > Network Parent` | Sahne objesine `NetIdentity` + benzersiz `sceneId` |

> ⚠️ Rol/IP **dev penceresinden** seçilir; sahneye `[SerializeField]` override **koyulmaz**.
> Seçim `EditorPrefs`'te kişisel kalır, hedef listesi `dev-targets.json`'da commit'lidir.
