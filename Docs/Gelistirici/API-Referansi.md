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
| UI | `VortexArena.Core.UI` | HUD tabanı + "Girdap" arayüz kiti |
| Admin arayüzü | `VortexArena.App.Admin` | Operatör ekranı, roster, akışlar |
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
| ✅ `CanFire` | `bool` | **Ateş etmeden önce bunu sor.** Hayatta + faz Lobby/Live + bağlantı açık + oyuncunun kendisi engelde değil + **alanın dışında değil**. Hiç bağlanılmadıysa `true` (yerel test bozulmasın). ⚠️ Alan-dışı kapısı tamamen yereldir, protokolde karşılığı yoktur: alanın dışına çıkıp içeri ateş etmek geriye kalan tek fiziksel hile yoluydu |
| ✅ `IsAlive` | `bool` | Yerel oyuncu hayatta mı (sunucu-otoriter) |
| ✅ `LocalHp` | `float` | Yerel can, `0..100` |
| ✅ `LocalPlayerId` | `int` | Sunucu kimliği; bağlanmadıysa `0` |
| ✅ `LocalTeam` | `Team` | `Red`/`Blue`/**`Neutral`** (takımsız modda Neutral) |
| ✅ `IsConnected` | `bool` | Mesajlar gerçekten gidiyor mu |

### Hedef çözme

| Metot | Döner | Açıklama |
|---|---|---|
| ✅ `TryGetTargetPlayerId(Collider, out int playerId)` | `bool` | Çarpılan collider'ın arkasında ağ oyuncusu var mı. `false` → oyuncu değil; hedef bir **ağ nesnesi** olabilir, sırada o var |
| ✅ `TryGetTargetNetId(Collider, out int netId)` | `bool` | Çarpılan collider'ın arkasında ağ nesnesi var mı (kırılabilir siper, hedef tahtası). İkisi de `false` → **hasar diye bir şey yok**: o hedefin canı hiçbir yerde tutulmuyor |
| ✅ `IsHeadshot(Collider)` | `bool` | Kafa kutusuna mı isabet etti. **Çarpanı sen uygularsın** |
| ✅ `GetHitZone(Collider)` | `HitZone` | İsabet bölgesi (`Body`/`Head`/`Stomach`/`Leg`); ağ oyuncusu değilse `Body`. Çarpanı `WeaponDefinition.GetZoneMultiplier(zone)` verir, **uygulamak sana ait** |

### Bildirme

| Metot | Ne yapar |
|---|---|
| ✅ `ReportShot(Vector3 worldMuzzlePos, Vector3 worldDir, string weaponId)` | Atışı diğer oyunculara relay ettirir (namlu alevi/ses). Hasarla ilgisi yok, sunucu doğrulamaz |
| ⚠️ `ReportHit(int targetPlayerId, Vector3 worldHitPoint, float damage, string weaponId)` | Vuruşu bildirir. **Hasarı sen belirlersin**, sunucu aynen uygular. Canı yerelde düşürme |
| ⚠️ `ReportObjectHit(int targetNetId, Vector3 worldHitPoint, float damage, string weaponId)` | Ağ nesnesine vuruşu bildirir (aynı `hit_report`, yalnız hedef alanı farklı). Kırılma kararı **sunucunun**; `Hp`/`Flags`'i yerelde yazma |
| ✅ `ReportRaycastHit(in RaycastHit, float damage, string weaponId)` | Hitscan kısayolu: hedef **oyuncu da ağ nesnesi de** olsa kendiliğinden raporlar. ⚠️ Dönüş değeri yalnız *"ağ oyuncusu muydu"* sorusunun cevabıdır (gövde efekti mi, duvar efekti mi) — ağ nesnesine hasar gider ama `false` döner |
| ⚠️ `ReportAreaHit(Vector3 center, float radius, float damage, string weaponId, float edgeScale = 0.25f, int layerMask = ~0, bool requireLineOfSight = false)` | Yarıçaptaki her oyuncuya **ve her ağ nesnesine** ayrı vuruş; merkeze uzaklıkla doğrusal düşer. Oyuncular gövde kapsülüyle ölçülür (arena zemini→kafa), `layerMask` yalnız ağ nesnelerini bulur. Dönen sayı **yalnız oyuncu** isabetidir. Duvar arkası kontrolü varsayılan olarak **kapalı** |
| ⚠️ `ReportAreaSelfHit(Vector3 center, float radius, float damage, string weaponId, float edgeScale = 0.25f, bool requireLineOfSight = false)` | Patlamanın **yerel oyuncuya** düşen payı — ayrı kapıdır çünkü yerel oyuncunun `RemoteAvatar`'ı yoktur, `ReportAreaHit` onu asla bulamaz; ölçüm aynı gövde kapsülüdür. Dost ateşi kapalıysa hiç göndermez |

**Hasar geçerlilik kuralı:** pozitif ve sonlu olmalı. `NaN`/`∞`/negatif hem burada hem sunucuda
reddedilir (NaN'a düşen can bir daha 0'ın altına inemez → oyuncu ölümsüz kalırdı).

**İsabet göstergesi hazır gelir:** `ReportHit` ve `ReportObjectHit` (dolayısıyla
`ReportRaycastHit`/`ReportAreaHit`)
vuruş noktasında bir X çizer (`HitMarker`) ve onu **yalnız vuran oyuncu görür** — kendi
göstergeni kurma, aynı vuruşta iki X çizilir. Gösterge *bildirimin yapıldığını* söyler, hasarın
uygulandığını değil (sunucu vuruşu reddedebilir: dost ateşi kapalı, faz `playing` değil).

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
| ✅ `OnKicked` | `KickedMsg` | Bağlantıdan atıldık |
| ⛔ `OnRulesUpdate` | `RulesUpdateMsg` | Koşan maçın kural şekli değişti (bugün: operatör dost ateşini çevirdi). **Sen dinleme** — `ModeRuntimePump` uygular, sen `ModeRuntime`'dan okursun |
| ⛔ `OnAdminState` | `AdminStateMsg` | Yalnız admin arayüzü içindir |
| ⛔ `OnViolation` | `ViolationMsg` | Bir oyuncunun engel/alan-dışı ihlali başladı ya da bitti. **Yalnız admin bağlantısına gelir** ve kenar tetiklidir — halka/işaretçi buna DEĞİL snapshot bitlerine bağlanır (kaybolan mesaj yalnız log kaybıdır) |

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

`ModeRulesInfo` maçın kural şeklini taşır (`teamMode` · `allies` · `scoring` · `friendlyFire` ·
`reviveAnchor` · `weaponSource` · `limitedReserve` · `respawnDelay` · `fireWhilePaused`); okuma
noktası `ModeRuntime`,
alanların tam semantiği ArenaNet-Protokol.md mod kuralları bölümündedir. ⚠️ `allies` yalnız
`teamMode:"none"`da okunur.

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
| ✅ `LocalTeamChanged` | **statik** olay | `Team` — yalnız değer değişince. Statik olmasının sebebi: dinleyicileri kendini önyükleyen kalıcı tekiller ve `Instance`'tan önce doğabiliyorlar |
| ✅ `LocalAliveChanged` | **statik** olay | `bool` — `AliveChanged` ile aynı anda, aynı statik-olma gerekçesiyle (`LocalTeamChanged`) |
| ✅ `TryGetOpenBaseFloor(out int floor)` | `bool` | Oyuncuya **açık** taban bölgelerinin en alçağının katı; hiçbiri açık değilse `false`. Ölümde gidilecek kat budur (`FloorState`) — en alçağı seçilir, çünkü kat 0 her arenada var olan tek seviyedir. `FloorState` hedefi ayrıca bulunduğu katla sınırlar (`min(taban katı, bulunduğu kat)`): ölüm yukarı taşımaz |

> ⛔ Bu sınıf hasar uygulamaz, skor tutmaz, faz değiştirmez — ve **hiçbir koşulda rig'i taşımaz.**

---

## ModeRuntime

`VortexArena.Core.ModeRuntime` — **statik**. Aktif maçın kurallarının tek okuma noktası.

| Üye | Tip | Değerler |
|---|---|---|
| ✅ `ModeId` | `string` | `"tdm"`, `"ffa"`, … |
| ✅ `Teams` | `ModeTeamMode` | `TwoTeams` \| `None` |
| ✅ `IsTeamless` | `bool` | `Teams == None` kısayolu |
| ✅ `Allies` | `bool` | **Yalnız takımsız modda okunur:** `false` = herkes tek (FFA), `true` = herkes AYNI ekip (kooperatif). `TwoTeams`'te yok sayılır. Sunum değişmez (nötr avatar, takım skor paneli yok) — değiştirdiği şey müttefiklik ilişkisidir. ⚠️ Doğrudan sorma, `IsAlly`/`AlliesAll` üzerinden oku |
| ✅ `IsAlly(Team local, Team other)` | `bool` | "Bu oyuncu benim tarafımda mı" sorusunun **tek** cevabı: takımsızda `Allies`, takımlıda `local != Neutral && local == other`. ⚠️ Ad etiketi ve öldürme duyurusu gibi tüketiciler takımı kendileri KARŞILAŞTIRMAZ — boş takım iki modda iki farklı cevap verir |
| ✅ `AlliesAll` | `bool` | `IsTeamless && Allies` kısayolu — "bu modda herkes aynı ekipte mi" |
| ✅ `Scoring` | `ModeScoreKind` | `Team` \| `Player` |
| ✅ `FriendlyFire` | `bool` | ⚠️ Modun değil **operatörün** anahtarı: maç ORTASINDA değişebilir (`rules_update`), `Changed`'i dinle |
| ✅ `Revive` | `ModeReviveAnchor` | `OwnBase` \| `StandStill` |
| ✅ `Weapons` | `ModeWeaponSource` | `WeaponCanvas` (sahnede elle konmuş silah, çerçeveden seçilir, tükenmez) \| `RandomGrant` (mod dağıtır). ⚠️ Tek başına "kurulmuş maç var" demek değildir — aşağı bak |
| ✅ `LimitedReserve` | `bool` | Yedek mermi sınırlı mı. `false` (varsayılan) = **sonsuz yedek**: gösterge `/∞`, şarjör değiştirme hep açık, yedek düşmez. `true` = yedek silahın `spareMagazines`'i kadar. Yalnız **sunum** kuralı; maç ORTASINDA değişebilir → göstergeyi `Changed` ile tazele |
| ✅ `FireWhilePaused` | `bool` | Maç kurulmamışken ateş serbest mi (lobi profili). `RandomGrant` ile **birlikte** okunur: `random` + `FireWhilePaused` = serbest alan, yalnız `random` = mod silah dağıtıyor |
| ✅ `RespawnDelay` | `float` | ⚠️ **`0` geçerlidir** (anında canlanma) |
| ✅ `Changed` | olay | Kurallar değişti |
| ✅ `FindDefinition()` | `ModeDefinition` | Aktif modun katalog tanımı (`GameCatalog.FindMode(ModeId)`); bilinmeyen modda `null`. Kural değil **içerik** (HUD, mod gövdesi) sormak içindir — kural alanları yine yukarıdakilerden okunur |
| ⛔ `Apply` / `ApplyFromCatalog` / `Reset` | | Besleme sistemin işi |

> ⚠️ **`if (modeId == "…")` zinciri yazma.** Yeni mod eklemek senin kodunu değiştirmemeli.

> ⚠️ **Sahnelenen arena lobi profiliyle koşar** (operatör lobideyken bir arena seçtiğinde herkes o
> arenaya geçer ama maç kurulmaz): orada `Weapons == RandomGrant`'tir. "Mod silah dağıtıyor mu"
> sorusunun cevabı bu yüzden `Weapons == RandomGrant && !FireWhilePaused`'dur —
> `Docs/ArenaNet-Protokol.md` §10.7. ⚠️ Serbest alanda tezgâhın/panonun gizlenip gizlenmeyeceğini
> **seçili mod** söyler (`ModeSelection.GrantsRandomWeapon`), koşan kural değil.

> ⚠️ **Serialize edilen mod enum'larına yeni değer SONA eklenir.** Unity enum'ları sayısal indeksle
> saklar; başa/ortaya ekleme sahnelerdeki tüm değerleri kaydırır. Aynı kural `Team` için de geçerli
> (`Neutral` bu yüzden sonda).

---

## Arena (koordinat, sınır, taban bölgesi)

`VortexArena.Core.Arena`

### ArenaSpace — statik

| Metot | Açıklama |
|---|---|
| ✅ `WorldToArena(Vector3 / Quaternion / Pose)` | Ağa göndermeden önce |
| ✅ `ArenaToWorld(Vector3 / Quaternion / Pose)` | Ağdan aldıktan sonra |
| ✅ `WorldToArenaDirection(Vector3)` | Yön için — sonucu **normalize eder**, sıfır/NaN girdide `Vector3.forward` döner |

> **Arena uzayı = dünya uzayı** (origin dünya (0,0,0), rotasyon kimlik), yani konum/rotasyon
> dönüşümleri kimliktir. Yine de doğrudan ham `transform.position` gönderme: çağrıyı `ArenaSpace`
> üzerinden yap, koordinat çerçevesi tek yerde tanımlı kalsın.

> ⚠️ Yön bir nokta değildir — iki noktanın farkını `WorldToArena` ile çevirme, `WorldToArenaDirection`
> kullan: protokol her olayda bir **birim** yön taşıyor
> ([reçete 16](Yemek-Kitabi.md#16-bir-konumu-ağ-üzerinden-paylaşmak-arena-uzayı)).

> ⚠️ Arena geometrisi **dünya orijinine göre** kurulur: zemin dünya y=0'da, arena merkezi dünya
> (0,0,0) civarında. Sahneyi topluca kaydırmak/döndürmek arenadaki tüm oyuncuların ağ koordinatını
> kaydırır. Orijin varsayılan yerleşimdir: hazır bir environment'ın içinde bölge oynatılırken
> `VA_ArenaBoundary` (maketiyle birlikte) o bölgenin üstüne taşınır — koordinatlar dünya uzayında
> kaldığı ve tüm build'ler aynı sahneyi taşıdığı için tutarlıdır.

### ArenaBoundary

Fiziksel sınır uyarısını sürer: kenara `warnDistance` kala karartma quad'ında hafif bir rampa
başlar (`warnFadeAlpha`), sınır aşıldığı **an** ekran kademesiz olarak **tam siyah** olur + uyarı
yazısı belirir + iki kumanda **çıkış başına 3 darbe** atar (darbe başına 1 sn düz titreşim,
aralarında 0,5 sn; sonra dışarıda kalınsa da susar — dışarıda süren titreşim kumanda pilini
tüketir). ⚠️ Sınır aşıldıktan sonra ikinci bir mesafe rampası (ve
ayarlanabilir bir karartma tavanı) **YOKTUR**: alanın dışı, engelin içiyle aynı sorudur — yüzde
birkaçlık saydamlık bile perdenin öbür yüzünü okunabilir bırakır ve dışarıdan içeri bakmak
istismarın kendisidir. İçeri girmek kalan darbeleri iptal eder, yeniden çıkmak diziyi baştan
başlatır. Titreşim `ControllerHaptics` hakeminden geçer (`ReportSteady`, ortak darbe genliği),
motor doğrudan sürülmez.
Sahneye
**`VA_ArenaBoundary`** prefabının örneği olarak konur ve sahnede **bir tane** olmalı. **Ağ koordinatlarının sıfırı bu bileşende DEĞİLDİR** (o dünya orijinidir): muhafazayı
büyütmek/kaydırmak koordinatları oynatmaz.

> ⛔ **Duvar Renderer'ı alanı YOKTUR.** Yarı saydam muhafaza duvarı kaldırıldı ve environment'ın
> gerçek duvarlarına bağlanamaz: alfa yazımı yalnız Transparent malzemede iş görür ve mekanizma
> alfa düşünce Renderer'ı kapatırdı. Uyarı bu yüzden HMD'ye bağlı karartma quad'ında, arena
> geometrisinden bağımsız. ⚠️ Bedeli bir kurulum kuralıdır: **sanat duvarları fiziksel sınırla
> çakışmalıdır.**

| Üye | Açıklama |
|---|---|
| ✅ `Active` | *(statik)* Sahnedeki muhafaza örneği — yoksa `null`. Alan-dışı durumunu tele koyan poz gönderimi ve ateş kapısı bunu okur; ölçüm bileşende kalır, ikinci bir hesap açılmaz |
| ✅ `IsOutOfBounds` | Yerel HMD alan dışında mı. ⚠️ Gözlemci kipinde ve **plansız** muhafazada `false`'a kilitlidir: ölçüyü bilmeden "dışarıda" demek sessiz bir yalancı pozitif olurdu |
| ✅ `HalfExtents` | Arena yarı ölçüsü — plandaki çokgenin sınırlayıcı kutusundan gelir (plan yoksa sıfır) |
| ✅ `LocalCenter` | O kutunun yerel merkezi — admin kuş bakışı kadrajı bunu okur. ⚠️ Ölçü genellikle bir köşeden alınır, yani kutu transformun tam ortasında DEĞİLDİR: kadrajlarken `HalfExtents` tek başına yetmez |
| ✅ `TopDownHeight` | Admin kuş bakışı kamerasının zeminden yüksekliği (boyut dosyasının `topViewHeight`'ı; 0 = kamera kendi varsayılanını kullanır). Ortografik kamerada kadrajı DEĞİL yalnız çatının/yüksek objelerin üstünde kalmayı belirler. Kamera dosyayı kendisi açmaz — JSON'u çözen tek yer bu bileşendir |
| ✅ `SetSpectatorMode(bool)` | Muhafazayı susturur (karartma + uyarı + titreşim kapanır) ama bileşeni ayakta tutar — kuş bakışı kadrajı `HalfExtents`/`LocalCenter`'ı okumaya devam ediyor |
| ✅ `Plan` | Çözülmüş planın **salt okunur** görünümü (`ArenaDimensions`); plan yoksa `null`. Bugünkü tüketicisi mekan ölçümünün şablon çerçevesidir. ⚠️ **Kopya DEĞİLDİR — üstünde değişiklik yapma:** plan önbelleklidir, yazılan bir düzeltme muhafazanın mesafe hesabını sessizce başka bir arenaya çevirir |
| ✅ `TryGetCalibrationMarks(out Vector3 a, out Vector3 b)` | Zemin bandının iki noktası, **dünya** uzayında ve zemin seviyesinde. Dosyada nokta yoksa `false`. `ArenaCalibrator` işaretçilerini bununla konumlandırır — boyut dosyasını iki kere çözen ikinci bir okuyucu olmasın diye |

Ölçünün **tek kaynağı** `dimensionsJson` alanına bağlanan boyut dosyasıdır (`ArenaDimensions`);
bileşen ölçü tutan başka bir alan taşımaz. Plan çözüldüğünde kenar mesafesi çokgene, kolonlara ve
sahnedeki `ArenaObstacle`'lara olan mesafenin **en küçüğü** olur. Dosya kare başına ayrıştırılmaz
(referans değişmedikçe önbellek).

> ⛔ **Boyut dosyası ZORUNLUDUR.** Bağlı değilse ya da çözülemiyorsa bileşen bir kez
> `Debug.LogError` basar ve **kendini devre dışı bırakır** — yaklaşma rampası, karartma ve
> alan-dışı uyarısı çalışmaz. Açık başarısızlık bilinçli: ölçüsü bilinmeyen arenada doğru muhafaza zaten
> üretilemez, her karede ekranı karartmak ise oyunu tümden oynanamaz kılardı.

### ArenaDimensions

**Arena ölçüsünün tek doğruluk kaynağı** — elle yazılabilir bir JSON dosyası olarak yaşayan saf
veri sınıfı: `name`, `plane` (tabanın kapalı köşe halkası; ilk nokta sona tekrarlanmaz),
`columns[]` (`name`/`height`/`points` — her kolon kendi kapalı halkası), `calibration` (`{a, b}` —
zemin bandının iki noktası), `defaultColumnHeight`, `topViewHeight` (admin kuş bakışı kamerasının
zeminden yüksekliği; 0 = kameranın varsayılanı).
Koordinatlar metre ve `ArenaBoundary`'yi taşıyan transformun **yerel
XZ**'sindedir — JSON'daki `y` dünya **Z**'sidir. Dosya **mekan başınadır**; o mekanın bütün
sahneleri aynı dosyayı gösterir.

> ⛔ **Parçalardan birleştirme (union) YOKTUR:** taban da kolon da tek halkadır. İçbükeylik bunun
> için engel değil. Birleşim `ArenaBoundary` yüzünden çalışma anında da koşmak zorunda kalırdı ve
> karşılığını mekan başına yalnız bir kez verirdi.
> ⛔ **Dikdörtgen alan için ayrı bir kip de YOKTUR:** alan tam kare bile olsa dört köşeli bir halka
> olarak yazılır. Aynı ölçünün iki ayrı ifadesi kaçınılmaz olarak birbirinden saptığı için ikinci
> temsil (bileşen üstünde yarım ölçü + merkez alanları) kaldırıldı ve geri eklenmez.
> ⛔ **`wallHeight` alanı YOKTUR:** duvar üretimi de muhafazanın duvar göstergesi de kaldırıldı.
> ⚠️ Kolondaki `{"points": […]}` sarmalayıcısı zorunludur — `JsonUtility` iç içe dizi
> (`Vector2[][]`) serialize etmiyor; `plane` düz `Vector2[]`'dir.

| Üye | Açıklama |
|---|---|
| ✅ `Parse(string, out string error)` | Metinden çözer. **Exception FIRLATMAZ** — bozuk girdide `null` + hata metni döner (çağıran yer sahne yükleme yolu; bir yazım hatası sahneyi düşürmemeli) |
| ✅ `FromTextAsset(TextAsset, out string error)` | Aynısı `TextAsset` üzerinden; asset `null` ise sessizce `null` |
| ✅ `LocalBounds()` | Çokgenin yerel XZ sınırlayıcı kutusu (muhafaza ölçüsü + kuş bakışı kadrajı bundan türer) |
| ✅ `HasCalibration` | İki kalibrasyon noktası yazılmış ve aralarında en az `MinCalibrationSpan` (0,5 m) var mı. ⚠️ `IsValid`'in parçası DEĞİLDİR: noktasız bir dosya muhafazayı çalıştırmaya yeter |
| ✅ `ToJson(bool pretty)` | Planı metne çevirir — editör araçları dosyayı bununla yazar. Sayılar milimetreye yuvarlanır (`0.###`); `topViewHeight` yalnız sıfırdan büyükse yazılır |

`JsonUtility.FromJsonOverwrite` kullanılır: **JSON'da yazılmayan alan varsayılanında kalır**
(`FromJson` ile eksik bir `defaultColumnHeight` sessizce 0 olurdu = hiç çizilmeyen kolonlar).

> ⚠️ Dosya **çalışma anında** okunur → bir sahneden referanslanmalıdır. `Assets/` altında durup
> kimsenin referanslamadığı bir `TextAsset` build'e **girmez**.

### Polygon2D

Saf 2B halka matematiği (`Core/Arena`, statik). Halkalara sorulan her geometrik sorunun tek yeri;
hem `ArenaBoundary` hem editör araçları kullanır. Halka **kapalıdır**, sarım yönü önemsizdir ve
metotlar **tahsis yapmaz** (muhafaza her karede çağırıyor).

| Üye | Açıklama |
|---|---|
| ✅ `Contains(ring, point)` | Ray casting — içbükeyde de doğru |
| ✅ `DistanceToRing(ring, point)` | En yakın **kenar parçasına** işaretsiz mesafe (köşe yakınında da doğru) |
| ✅ `SignedDistance(ring, point)` | **Alan sözleşmesi:** içeride +, dışarıda − |
| ✅ `ObstacleDistance(ring, point)` | **Engel sözleşmesi:** dışarıda +, içeride − |
| ✅ `Bounds(ring)` · `SignedArea(ring)` · `Centroid(ring)` | Ölçü; `Centroid` maket kolonlarının pivotu |
| ✅ `IsSelfIntersecting(ring)` | Yalnız doğrulama — köşe sırası yanlış yazılmış halkayı yakalar |

> İki mesafe sözleşmesinin sebebi, muhafazanın ikisini tek bir `Mathf.Min` ile birleştirmesidir:
> her ikisinde de "artı = güvenli pay". Alan için güvenli olan içerisi, engel için dışarısıdır.

### ArenaDimensionMesh · DimensionPolygon · DimensionAnchor

Ölçü maketinin işaretçileri (`Core/Arena`, runtime asmdef — sahne objesi editör-only tipe referans
veremez). `ArenaDimensionMesh` kökte durur: mekan adı, kaynak `TextAsset` ve geri yazarken korunan
taşıyıcı alan (`DefaultColumnHeight`). `DimensionPolygon` her çokgende
durur ve **yalnız** `Kind { Plane, Column }` taşır. `DimensionAnchor` kalibrasyon küplerinde durur
ve **yalnız** `AnchorKind { A, B }` taşır; obje adı tek kaynaktan gelir
(`ArenaCalibrator.AnchorAName`/`AnchorBName` = `anchor_a`/`anchor_b`).

> ⛔ İşaretçilerde nokta/ad/yükseklik **tutulmaz**: noktaların kaynağı mesh (kalibrasyon
> küpünde transform), ad `GameObject`'in adı, yükseklik mesh'in Y aralığıdır. Kopyalamak, sahnede
> düzenlenen değerden sessizce sapan ikinci bir kaynak üretirdi.
> ⚠️ **Sahnenin kalibrasyon işaretçileri bu küplerdir** — ikinci bir işaretçi ailesi yoktur ve
> açılmaz; `ArenaCalibrator` onları `DimensionAnchor` + `AnchorKind` üzerinden çözer, ad araması
> yalnız maketi olmayan eski sahneler için son basamaktır.
> ⚠️ Maketin **kökü ve kalibrasyon küpleri build'e girer** (`EditorOnly` etiketlenmez):
> işaretçiler çalışma anında gerekir. **Görsel dal (`Plane` + `Columns`) gerçek build'e hiç
> girmez** — `DimensionMeshBuildStripper` (`IProcessSceneWithReport`) onu build'e giden geçici
> sahne kopyasından siler; gerekçe boyut değil bağımlılıktır (`ProBuilderMesh` runtime'a
> `Unity.ProBuilder`'ı sokardı) ve sahne dosyası değişmez.
> **Editör Play kipinde** her şey sahnededir: `ArenaDimensionMesh.Awake` yalnız `Plane`/`Columns`
> altındaki `Renderer.enabled`'ı false yapar — obje kapatılmaz (kapalı bir kökün altındaki
> işaretçiler bulunamazdı) ve işaretçilerin `Renderer`'larına dokunulmaz (görünürlükleri
> kalibratörün işidir).
> ⚠️ Kök **`ArenaBoundary`'nin altına**, yerel konum/dönüş sıfır ve 1 ölçekte kurulur (sahnede
> muhafaza yoksa sahne köküne, dünya orijininde ve dönüşsüz). Arenayı yerleştirmek = muhafazayı
> taşımak/döndürmek; maket ve işaretçiler onu izler. Çıkarım maketin KENDİ kökünün yerel uzayına
> göre yapıldığı için taşınmış/döndürülmüş maket de doğru çevrilir. **Ölçeği değiştirilmez**: plan
> metre cinsindendir.

> ⛔ **Muhafazayı susturmak için bileşeni kapatma** — kapalı bileşen karartmayı son değerinde
> dondurur **ve planı çözmeyi bırakır** (kuş bakışı kadrajı ona bağlı). Doğrusu
> `SetSpectatorMode(true)`.

### ArenaCalibrator — kalibresiz ön-hizalama

Kalibrasyonun kendisi (iki nokta → 6DOF hizalama + `OVRSpatialAnchor` kalıcılığı) operatör
akışıdır; burada yalnız kod yazarken önemli olan yan davranış: kayıtlı hizalaması **olmayan** bir
başlıkta rig, kafası arenanın A-B ortasında ve A→B'ye bakar olacak biçimde **tahminen**
yerleştirilir (yükseklik `uncalibratedHeadHeight`, varsayılan 1,8 m, zeminden). Tetikleyici iki
durumdur: PlayerPrefs'te anchor UUID'si hiç yok, ya da geri yükleme tüm denemelerde düştü —
**ikisinde de kullanılabilir bir oturum kaydı yoksa** (aşağıda).

> ⚠️ **Bu bir kalibrasyon DEĞİLDİR** ve öyle raporlanmaz: yakalama sayacı artmaz, `Calibrated`
> yayınlanmaz, anchor kaydedilmez, elle kalibrasyon kapısı açık kalır. Amacı görünürlüktür —
> hizalanmamış rig oyuncuyu `ArenaBoundary` karartmasının içinde bırakırsa elle kalibre etmesi
> gereken oyuncu hiçbir şey göremez.
> ⚠️ `CalibrationGeneration` **artar**: taşınma meşrudur, kök sıçraması bastıran emniyetler bunu
> arıza saymamalıdır.
> Koşmadığı durumlar: kayıtlı anchor geri yüklendiyse, oyuncu jeste başladıysa, operatör
> sıfırladıysa, rig kökü kapalıysa (admin gözlemci) ve işaretçiler yok/aynıysa.

**Oturum kaydı ve çapa kaydının sonucu:**

| Üye | Tip | Açıklama |
|---|---|---|
| ✅ `ArenaCalibrator.SourceSession` | `const string` = `"session"` | `set_calibration.source` etiketi: hizalama bellekteki **oturum kaydından** geri yüklendi (çapa yok ya da yüklenemedi). Kayıt, hizalamanın referans pozunu **takip uzayında** tutar; harita değişiminde ve operatörün yeniden yüklemesinde kullanılır, uygulama kapanınca gider |
| ✅ `CalibrationState.ReportAnchorSaveFailure(string reason)` | `static void` | Çapa oluşturulamadı/kaydedilemedi/zaman aşımına uğradı → gerekçeyi saklar, uyarı basar ve `set_calibration{error}` ile operatöre bildirir. Hizalama **geçersiz kılınmaz** |
| ✅ `CalibrationState.ClearAnchorSaveFailure()` | `static void` | Kayıt sonradan tuttu → gerekçeyi düşürür ve satırın temizlenmesi için güncel durumu yeniden bildirir |

> ⚠️ Çapa kaydı hizalamadan **ayrı bir adımdır ve kendi başına düşebilir** — bu yüzden sonucu
> bildiren bir kanal (yukarıdaki iki çağrı) ve çapaya bağlı olmayan bir harita-değişimi yolu
> (oturum kaydı) birlikte gerekir; gerekçe `Docs/Sistem-Ozeti.md` §7 "Tuzaklar".

**Dev hizalama — yalnız editör:** kalibratörün editöre özel parçası `ArenaCalibrator.Dev.cs`'dir
(sınıf `partial`, dosyanın tamamı `#if UNITY_EDITOR`). Ana dosya yalnız **gövdesiz `partial void`
kancalar** taşır; build'e tek satır girmez.

| Üye | Tip | Açıklama |
|---|---|---|
| ✅ `ArenaCalibrator.SourceDev` | `const string` = `"dev"` | `set_calibration.source` etiketi (`SourceManual`, `SourceAnchor`, `SourceSession` yanında) |
| ✅ `ArenaCalibrator.DevSkipRequested` | `static bool` | Dev penceresinin "Kalibrasyonu atla" seçimi; `DevSession.ApplySelection` yazar |
| ✅ `ArenaCalibrator.IsDevAligned` | `static bool` | Dev hizalama şu an yürürlükte mi — çubukların ve `BodyScaleState`'in otomatik ölçüm atlamasının kapısı |

Kancalar: `DevStart` (sahne açılışı; geri yükleme ve ön-hizalamadan ÖNCE) · `DevUpdate` (her kare,
jestten önce) · `DevAlignmentReplaced` (`AlignRig`, `AlignRigToAnchorPose`, `ResetAlignmentState`) ·
`DevReload` (operatörün `reload_calibration` düğmesi).

> ⚠️ Dev hizalama bir **yerel ezme değildir**: tamamlanmayı yine `Calibrated` olayından bildirir
> (`CalibrationState` → `set_calibration{source:"dev"}`) ve `IsCalibrated` / `ManualAllowed`
> ezilmez. Çapa kaydına, yakalama sayacına ve oturum-içi UUID'ye dokunmaz.
> Kullanımı ve sınırları: `Docs/Gelistirici/Ilk-Adimlar.md`.

**Kat ofseti — rig'i taşımanın tek meşru yolu (ürün kodunda):**

| Üye | Tip | Açıklama |
|---|---|---|
| ✅ `ArenaCalibrator.FloorLiftMeters` | `static float` | Rig köküne uygulanmış dikey sanal ofset (m) |
| ✅ `ArenaCalibrator.SetFloorLift(float meters)` | `static void` | Ofseti yazar ve farkı rig köküne uygular; her hizalama yolunun kuyruğunda yeniden uygulanır |

> ⛔ **Bunu doğrudan çağırma** — katın sahibi `FloorState`'tir (`Request` / `MoveWithFade`), yoksa
> rig'in ofseti ile oyuncunun bildirdiği kat ayrışır. Başka hiçbir kod rig kökünü oynatmaz ve
> ürün kodunda **yatayda hiçbir kod oynatamaz** (`Docs/Sistem-Ozeti.md` §3.13); editördeki ikinci
> yol yukarıdaki dev hizalamadır.

### ArenaObstacle

Elle konan engel (kolon, kasa, direk): `ArenaBoundary` onu muhafaza hesabına katar, oyuncu
yaklaşınca uyarı alır. Ölçü `Size` alanından gelir, transform scale'inden değil.

> ⛔ **Collider DEĞİLDİR, fizik YAPMAZ.** Free-roam'da oyuncuyu durduran şey gerçek dünyadaki
> nesnedir; bu bileşenin tek işi uyarı üretmek.

### BaseZone (taban bölgesi)

Arenadaki kırmızı/mavi şerit. Ölen oyuncu buraya fiziken girince canlanır (`reviveAnchor:"base"`).

**Alanı çizilen şerit belirler:** bölgenin altındaki Renderer'ların (gizlenmiş olanlar dahil)
kapladığı dikdörtgen, bölgenin kendi yerel XZ'sinde ölçülür — Inspector'da ölçü alanı yoktur.
Bölgeyi büyütmek/döndürmek/kaydırmak = şerit mesh'ini büyütmek/döndürmek/kaydırmak. Ölçü `Awake`'te
bir kez alınır (şerit statiktir), yükseklik yok sayılır ve dikdörtgen pivota göre kaymış olabilir
(merkez varsayılmaz). Editörde bölgeyi seçince algılama dikdörtgeni Gizmo olarak çizilir.

| Üye | Açıklama |
|---|---|
| ✅ `BaseZone.Team` | Bölgeyi kim kullanabilir; `Team.Neutral` = **herkes** |
| ✅ `BaseZone.IsPlayerInside` | Yerel oyuncunun HMD'si bölgede mi (bileşen kapalıyken DONAR) |
| ✅ `BaseZone.onPlayerEntered` / `.onPlayerExited` | UnityEvent — iyileşme/tazeleme buraya takılır |

Eşleşme kuralı: bölge açıktır eğer takımı oyuncununkiyle aynıysa, bölge `Neutral` ise ya da
oyuncunun takımı boşsa (takımsız mod). Aynı takımdan birden çok bölge konabilir —
**herhangi birine** girmek yeter.

> ⚠️ Gizlemek gerekiyorsa **bileşeni** kapat (`zone.enabled = false`) ve görsel şeridi ayrıca
> gizle. Kapalı bölge canlanma için açık sayılmaz.

> ⛔ **Şeridi silme, Renderer'sız bırakma.** Ölçü alınamayan bölge bir kez hata basıp kendini
> kapatır (açık başarısızlık); `PlayerCombatState` bunu "açık taban yok" diye okur ve fail-open'ı
> devreye girer — belirti "taban çalışmıyor" değil, herkesin her yerde canlanmasıdır.

| Üye | Açıklama |
|---|---|
| ✅ `BaseZone.Floor` | Bölgenin katı — Y'sinden türer, kat listesinin sürümüne göre önbelleklidir. İçeride sayılmak için `FloorState.Local == Floor` olmalıdır (`Docs/Sistem-Ozeti.md` §3.13) |

### ArenaFloors — statik

Arenanın kat seviyelerinin tek kaynağı. Kat 0 = dünya y 0; üstü sahnedeki `FloorPortal`'lardan
türer. ⛔ **Elle seviye listesi yok** — sayı yazacak bir alan aranmaz.

| Üye | Tip | Açıklama |
|---|---|---|
| ✅ `Count` | `int` | Kat sayısı; portal yoksa `1` |
| ✅ `Version` | `int` | Her yeniden kurulumda artar — türettiğin katı buna karşı önbelleğe al |
| ✅ `HeightOf(int floor)` | `float` | Katın dünya yüksekliği (m); indeks aralığa kırpılır |
| ✅ `FloorAt(float worldY)` | `int` | Bu yükseklikteki bir objenin katı (her seviyenin altındaysa `0`) |
| ✅ `LevelToleranceMeters` | `const float` | `0.25` — bir objenin Y'si bir kat zeminine bu kadar yakınsa o katta sayılır |
| ⛔ `MarkDirty()` | | Portal ve sahne yükleme çağırır; listeyi elle bayatlatma |

> Kat sayısı 1'den büyükse konsola `[ArenaFloors] n kat: 0.00 m / 3.00 m` düşer — kurulumun denetim
> yolu bu satırdır.

### FloorState — statik

Yerel oyuncunun katı + roster aynası. Kendini önyükleyen kalıcı tekil; **sahneye koyma.**

| Üye | Tip | Açıklama |
|---|---|---|
| ✅ `Local` | `int` | Yerel oyuncunun katı (set dışarıya kapalı) |
| ✅ `LiftMeters` | `float` | O katın zemin yüksekliği = rig kökünün dikey ofseti |
| ✅ `Changed` | olay | Yerel kat değişti |
| ✅ `PlayerFloor(int playerId)` | `int` | Herhangi bir oyuncunun roster katı (kendisi dahil); bilinmiyorsa `0` |
| ✅ `ViewerHasFloor` | `bool` | İzleyenin bir katı var mı — admin gözlemcide `false`, o yüzden kat silüeti ve kat etiketi soneki çizilmez |
| ✅ `Request(int floor, string reason)` | `bool` | Katı hemen uygular + `set_floor` yollar; `false` = aralık dışı. `reason` yalnız logda çağrı yerini adlandırır |
| ✅ `MoveWithFade(int floor, string reason, float fadeOut, float hold, float fadeIn)` | `bool` | Aynı iş karartmanın arkasında; `false` = geçersiz kat ya da zaten o kat (ses/durum harcamadan önce buna bak). Süren bir geçiş varken: karartma tepesine ulaşılmadıysa **hedef güncellenir**, ulaşıldıysa **kuyruğa alınır** |

> ⚠️ **Onay beklenir:** `FLOOR_CONFIRM_SECONDS` içinde kendi roster satırında yankı gelmezse yerel
> kat geri alınır. Sunucu yalnız defter tutar — `Docs/ArenaNet-Protokol.md` §10.6 "Kat modeli".

### FloorPortal

İki kat arasındaki geçiş noktası (prefabı `VA_FloorPortal`). Kural tümüyle istemci tarafındadır:
kafa kendi katındaki çemberin içinde 2 sn kalınca diğer kata geçilir, aynı anda tek oyuncu geçer.

| Üye | Tip | Açıklama |
|---|---|---|
| ✅ `UpperHeight` | `float` | Inspector'daki `upperHeight` — **üst katın yüksekliğini TANIMLAYAN ölçü** (min 0,5) |
| ✅ `LowerFloor` / `UpperFloor` | `int` | Kökün oturduğu kat ve onun bir üstü |
| ✅ `LowerDisc` / `UpperDisc` | `Transform` | Alt/üst çember kökleri (`Üst` konumunu `OnValidate` yazar) |

> ⛔ **Çemberlere collider koyma** — kapı kafanın XZ mesafesiyle çalışır; collider maskesiz atış
> ışınını yer.

> ⚠️ Kökü hiçbir kat zeminine oturmayan portal uyarı basıp **kendini kapatır** ve kat listesine
> girmez.

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

## Ağ nesneleri (`netId`)

Oyuncu olmayan varlıklar: durumu sunucuda, sunumu sende. Kural
`Docs/ArenaNet-Protokol.md` §10.10; reçeteler `Yemek-Kitabi.md` §11.4-§11.6.

### NetObject

`VortexArena.Net.NetObject` — sahne/dinamik objenin ağa bakan yüzü.

| Üye | Tip | Açıklama |
|---|---|---|
| ✅ `NetId` / `Kind` | `int` / `NetObjectKind` | Kimlik ve tür |
| ✅ `Hp` / `MaxHp` / `HealthRatio` | `float` | `MaxHp == 0` = hasar almaz; oran `0..1` |
| ✅ `Flags` / `IsBroken` / `IsHeld` / `HeldByRightHand` / `IsAwake` | `int` / `bool` | Çekirdek bitler (bit0-3) |
| ✅ `HasKindFlag(string)` | `bool` | **Türe özel** bayrak (bit4+) — ⚠️ bit numarasıyla değil **adla** okunur |
| ✅ `Owner` / `IsMine` | `int` / `bool` | Objeyi tutan `playerId` (`0` = kimse) |
| ✅ `Stage` | `int` | Türe özel aşama; `0` her türde "başlangıç". ⛔ İstemci yazmaz |
| ✅ `RestPosition` / `RestRotation` / `HasRestPose` | `Vector3` / `Quaternion` / `bool` | Dinlenme pozu — ⚠️ **arena uzayında** |
| ✅ `StateChanged` | `Action<NetObject, NetStateOrigin>` | Sunumun tek kancası; `Snapshot` kaynağında efekt oynatma |
| ⚠️ `OwnerChanged` | `Action<NetObject, int>` | **`StateChanged`'den ÖNCE** tetiklenir: iyimser kavramanın geri alınması sunum tepki vermeden olsun diye |
| ✅ `EventReceived` | `Action<ObjectEventMsg>` | Sunucudan relay edilen **kozmetik** olay |
| ⛔ `BindDynamicId(int)` | `bool` | `NetObjectSpawner`'ın işi — sahne objesinin kimliği bake'lidir |

### NetObjectSync — yukarı yön

`VortexArena.Net.NetObjectSync` — **statik**. Bağlantı yokken sessizce no-op.

| Metot | Ne yapar |
|---|---|
| ✅ `SendGrab(int netId, bool rightHand)` | Kavramayı bildirir. **Cevabı yoktur** — sonucu yayınlanan `object_state.owner` söyler; kavramayı yerelde hemen yap, sahip sen değilsen `OwnerChanged`'de geri al |
| ⚠️ `SendRelease(int netId, Vector3 arenaPos, Quaternion arenaRot)` | **Obje ELDEN ÇIKTI:** `Held` düşer, `Awake` kalkar, **sahiplik sürer** (uçuş penceresi başlar) |
| ⚠️ `SendRest(int netId, Vector3 arenaPos, Quaternion arenaRot)` | **Obje DURDU:** `Awake` düşer, `owner = 0`, bildirilen poz dinlenme pozu olur |
| ✅ `SendEvent(int netId, string name, int[] i = null, float[] f = null, string s = null)` | Objeye özel etkileşim; `name` türün izinli listesinde yoksa sunucu reddeder |
| ⛔ `SpawnRequested` / `DespawnRequested` / `RegisterSpawned` | `NetObjectSpawner`'ın yüzeyi |

> ⚠️ **İkisi karıştırılmaz:** `SendRelease` elden çıkış, `SendRest` durmadır. Yalnız `SendRelease`
> yollanırsa obje sahipli kalır ve uçuşun son karesinde **havada donar**; yalnız `SendRest`
> yollanırsa uçuş boyunca tel "obje elde" der ve o sırada bağlanan oyuncu objeyi bir elin ucunda
> görür. İkisini de **sen yollamak zorunda değilsin**: `NetObjectPoseSender` durmayı ölçüp
> `SendRest`'i, `NetObjectGrabBridge` bırakmayı görüp `SendRelease`'i kendisi yollar.

### Poz kanalı ve uzak pozlar

| Üye | Açıklama |
|---|---|
| ⚠️ `UdpStateChannel.SendObjectPose(int netId, Pose arenaPose)` | `0x09`; yalnız **sahip** + **uyanık** + **tutulmuyor**. Tutulan obje poz paketi ÜRETMEZ — el zaten akıyor, obje ona kanonik pozla bağlı |
| ✅ `RemoteObjectRegistry.Instance.TryGetInterpolatedPose(int netId, out Pose arenaPose)` | Uzak objenin yumuşatılmış pozu (oyuncu pozuyla **aynı saat**). Kendi sahip olduğun objede `false` |
| ✅ `RemoteObjectRegistry.Instance.IsStreaming(int netId)` | O obje için canlı akış var mı |
| ⛔ `IngestFromNetThread` | Ağ katmanının işi |

### NetObjectKind — tür sorguları

| Üye | Açıklama |
|---|---|
| ✅ `Kind` / `MaxHp` / `IsDamageable` | Telde giden ad ve can |
| ✅ `Grab` / `IsGrabbable` | `None` (varsayılan) / `Anyone` |
| ✅ `Events` | İzinli olay listesi (`NetObjectEventRule`: `Name` · `Policy` · `PhaseGate`) |
| ✅ `FindEvent(string)` | Tek kuralı bulur (yoksa `null`) |
| ✅ `TryGetFlagBit` / `TryGetFlagMask` | Türe özel bayrağın adı → bit/maske (bit4+) |

---

## Elle tutulan eşya (kavrama eksenleri)

`VortexArena.Core.Combat` — eşyanın nasıl yaşadığını **üç bağımsız eksen** söyler; hepsi
`ItemDefinition`'da serialize edilir ve ⚠️ **0. indeksleri bugünkü davranıştır**.

| Üye | Değerler | Açıklama |
|---|---|---|
| ✅ `ItemDefinition.GrabPath` | `DistanceGrab` · `ProximitySocket` · `WristHolster` · `None` | Eşya ele **nasıl gelir**. ⚠️ `DistanceGrab` değilse prefabda mesafeli kavrama bileşeni bulunmaz (`Yapma-Listesi`) |
| ✅ `ItemDefinition.Instancing` / `IsWorldSingle` | `PerViewerClone` · `WorldSingle` | **Ne gelir:** her bakanın kendi kopyası mı, tek örnek mi. `WorldSingle`'da eşya baytı `0` kalır |
| ✅ `ItemDefinition.ReleaseMode` | `Return` · `Physics` | Bırakılınca yerine mi oturur, serbest mi düşer |
| ✅ `ItemDefinition.HasSecondaryGrip` / `Weapon.ForegripAuthored` | `true` · `false` | Ön kabza kaydı **yazılmış mı** — iki elli tutuşun TEK kapısı (`WeaponGranter.ResolveSecondaryHand` = `gripHeld && ForegripAuthored`). ⚠️ Mesafe/kabul yarıçapı yoktur ve eklenmez (`Yapma-Listesi`); yazılmamış kayıtta bağ kapalı kalır ve tanım başına bir uyarı düşer |
| ✅ `ItemDefinition.ShowGrabIndicator` | `true` (varsayılan) · `false` | **Yakınlık soketinin** gösterge küresi çizilsin mi (silahın ön kabzası küre çizmez). Serialize alan **tersten** (`hideGrabIndicator`) yazılır — yazılmamış alan `0` okunur ve `0` bugünkü davranış olmak zorunda. ⚠️ **Yalnız görseli** susturur: kabul yarıçapı ve alma kapısı aynı kalır |

`VortexArena.Core.Combat.GripSocket` — yakınlık kavrama soketi (eşyanın **nereden** alındığı).

| Üye | Açıklama |
|---|---|
| ✅ `AcceptRadius` | Kabul yarıçapı (m, taban 1 cm) — ⚠️ oyuncunun gördüğü küre **bu** hacimdir |
| ✅ `Accepts(bool rightHand)` | Bu soket o eli kabul ediyor mu |
| ✅ `TryMeasure(OVRInput.Controller, out float distance)` | Kumanda **anchor'ından** uzaklık (bilekten değil) |
| ✅ `IsInside(OVRInput.Controller)` / `TryResolveHand(out …)` | Yarıçapın içindeki el (iki el varsa **yakın olan**) |
| ✅ `Tick(bool available)` / `Hide()` | Göstergenin bir karesi / gizlenmesi |
| ⚠️ `Configure(GameObject indicatorPrefab, float radius, bool acceptsLeft, bool acceptsRight)` | Soketi **kodla** süren yol (`WristHolster` bunu kullanır) — ikinci bir yakınlık uygulaması yazma |

> Kavrama pozu (elin nasıl duracağı) burada DEĞİL `ItemDefinition`'ın kavrama kayıtlarındadır ve
> stüdyoda yazılır. Soket "nereden alınır", kayıt "alınınca nasıl durur" sorusunun cevabıdır.

`VortexArena.Core.Combat.GrabArbiter` — yakınlık grip basışının hakemi (statik). Basışa doğrudan
kavramayla cevap veren bileşen yazılmaz (`Yapma-Listesi`).

| Üye | Açıklama |
|---|---|
| ✅ `IGrabClaimant.CommitGrab(bool rightHand)` | Hakemin **kazanana** yaptığı geri çağrı — kavrama burada yapılır |
| ✅ `GrabArbiter.Submit(IGrabClaimant claimant, bool rightHand, float distance)` | O karenin adaylığı. `distance` **`GripSocket.TryMeasure`** sonucudur — alma kapısıyla aynı sayı |
| ⚠️ `GrabArbiter.Resolve()` | El başına tek kazanan (**en yakın**; beraberlikte önceki talep kalır). Yalnız `GrabArbiterPump` çağırır — ikinci bir çağıran talepleri karenin ortasında tüketir |
| ✅ `GrabArbiter.Reset()` | Bekleyen talepleri düşürür |

> `GrabArbiterPump` kendini önyükleyen DDOL tekildir, sahneye KONMAZ. ⚠️ Execution order **40** bir
> sözleşmedir: talep sahipleri 0'da yazar, `HandGripPoser` 100'de eli kilitler — aralığın dışına
> taşınan bir çözüm kavramayı bir kare geciktirir ve obje elde sıçrar. 50 değil 40: `BurgerCarrier`
> 50'dedir ve aynı sırayı paylaşan iki bileşenin koşma düzeni tanımsızdır.

---

## Sunucu: ağ nesnesi kancaları

`VortexArena.Server.Core` — yalnız **mod** kodundan çağrılır.

| Üye | Açıklama |
|---|---|
| ⚠️ `IGameMode.OnObjectEvent(MatchDirector director, int playerId, int netId, string kind, ObjectEventMsg msg)` | Bütün kapılardan geçmiş bir `object_event`. **Varsayılan gövdesi vardır** (mevcut modlar değişmez). Dönüş yalnız **relay** sorusunu cevaplar: `true` = "ben hallettim, relay etme"; `false` = kozmetik → aynı olay herkese relay edilir |
| ✅ `MatchDirector.SpawnObject(string kind, PoseData pose, int owner = 0, bool rightHand = false, string payload = null)` | Çalışma zamanında obje doğurur; dönen `netId` `0` ise reddedilmiştir (bilinmeyen `kind` / tükenen aralık). `owner` verilirse obje **doğrudan o elde** doğar (`Held` + gerekirse `HeldRight`) |
| ✅ `MatchDirector.DespawnObject(int netId)` | Objeyi kaldırır; sahne objesinde `false` (kimliği sahnede bake'li) |
| ✅ `MatchDirector.SetObjectStage(int netId, int stage)` | Türe özel aşamayı yazar ve sonucu **kendisi yayınlar** |
| ✅ `MatchDirector.SetObjectFlags(int netId, int setMask, int clearMask)` | Bayrak bitleri; aynı şekilde kendi yayınını yapar |
| ✅ `MatchDirector.SetObjectPayload(int netId, string payload)` | Örnek verisi (`object_state.s`); aynı şekilde kendi yayınını yapar |
| ✅ `MatchDirector.TryReadObject(int netId, out string kind, out int stage, out int owner, out int flags, out PoseData pose, out bool hasPose)` | Modun tabloyu **okuma** kapısı; kopya değer döner (kanca kilit dışında koşar, `NetObjectEntry` referansı dışarı verilmez) |
| ✅ `MatchDirector.AddSharedScore(int playerId, int amount)` | `scoring:"shared"` skorunun **yazan** yolu: bireysel katkı + ortak toplam tek çağrıdan; `scoreBlue`'ya dokunulmaz |

> ⛔ Mod `WorldObjectTable`'a doğrudan dokunmaz; yazma yolu yukarıdaki metotlardır.
> ⛔ **İstemci spawn isteyemez** — doğuşun iki kaynağı moddur ve türün kuralıdır.
> ⚠️ **Duyuran = yazan.** Bir olay birden çok objeyi değiştirebilir (doğru servis müşteriyi, malzemeleri
> ve skoru birden değiştirir); "olayın objesini yayınla" kısayolu yalnız birini duyururdu.

---

## ModeHudBase

`VortexArena.Core.UI.ModeHudBase` — mod HUD'larının takım-agnostik tabanı.

| Üye | Tür | Açıklama |
|---|---|---|
| ✅ `ScoreLine(MatchStateMsg)` | `abstract` | **Zorunlu.** Skor satırı |
| ✅ `WinnerLine(MatchEndMsg)` | `abstract` | **Zorunlu.** Maç sonu başlığı |
| ✅ `EndScoreLine(MatchEndMsg)` | `virtual` | `null` → son değer korunur |
| ✅ `OnLobbyStateApplied(LobbyStateMsg)` | `virtual` | Bireysel skor tabloları için |
| ✅ `OnMatchStateApplied(MatchStateMsg)` | `virtual` | Taban faz/süre/skoru çizdikten sonra: modun **kendi** panelleri buradan beslenir; alt sınıfın `modeState`'i etiket üretmeden görebildiği tek yer |
| ✅ `NameOf(int playerId)` | yardımcı | `playerId` → ad |
| ✅ `FindSelf(LobbyStateMsg)` | yardımcı | Kendi roster satırın |
| ✅ `LocalPlayerId` | yardımcı | Kendi `playerId`'in; bağlantı yokken `0` |
| ✅ `SetText(TMP_Text, string)` | yardımcı | Null-güvenli |
| ✅ `SetCenterNotice(string)` | `public` | Ekranın ortasındaki büyük tek satır; boş string temizler |

Tabandan **hazır** gelenler: faz/süre, geri sayım, can + can barı, ölüm ekranı, durum metni,
merkez bildirimi, kill-feed, kendi öldürme/ölüm sayacın. Takım skoru paneli ve tur sonucu şeridi
tabanda **değildir** (aşağıdaki nota bak).

> **Ölüm ekranı da moda ait DEĞİLDİR:** görseli `_Shared/App/Resources/UI/DeathHud.prefab`'da
> durur, HUD prefabının altına iç içe konur ve taban açıp kapatır. Katil satırını
> (`<ad> tarafından öldürüldün!` · `Engelde kaldın` · `Öldün`) ve canlanma sayacını taban yazar —
> alt sınıfın yapacağı iş yoktur, prefab bağları yeterlidir.

> **Can barı da moda ait DEĞİLDİR:** görseli `_Shared/App/Resources/UI/HealthHud.prefab`'da durur
> ve o da HUD prefabının altına iç içe konur. Taban iki alanı sürer: `healthFill`
> (`Backdrop/Fill`, `Image.type = Filled`) ve `healthText` (`Backdrop/Value`). Alt sınıfın işi
> yoktur.

> **Maç saati de aynı prefabtadır** ve iki alan ister: `timeText` (`Clock/Panel/Time`) ile
> `timeFrame` (`Clock` — kutunun kökü). Taban süreyi yazarken kutuyu açar, süre boşalınca
> (lobi) kapatır; `timeFrame` bağlanmazsa yalnız metin silinir ve arkadaki panel asılı kalır.

> **Merkez bildirimi de moda ait DEĞİLDİR:** görseli
> `_Shared/App/Resources/UI/RoundNoticeHud.prefab`'da durur, o da HUD prefabının altına iç içe konur
> (**en son kardeş**). Taban geri sayım sayısını oraya kendisi yazar; modun kendi başlığı
> `SetCenterNotice("…")` ile gelir ve **aynı ögeyi paylaşır** — öncelik geri sayımındadır, metin bu
> yüzden kısa olmalıdır (ayrıntı `statusText`'e). Karartma yalnız geri sayımda açılır ve onu da taban
> yönetir. `deathOverlaySeconds` (prefab alanı) ölüm ekranının açık kalma süresidir: `0` = canlanana
> kadar, canlanması olmayan modda **3** verilir ki ekran kapansın ve bildirim görünsün.

> ⚠️ Takıma ait hiçbir şey tabanda değildir (bazı modlarda takım yoktur) — renk ve kolon alt sınıfın işi.

> **Takım skoru paneli ve tur sonucu şeridi de tabanda DEĞİLDİR** (aynı sebep: taban takım-agnostik).
> İkisi `HealthHud.prefab`'ın altında hazır durur ve referansları **alt sınıfın** `[SerializeField]`
> alanlarıdır:
>
> | Bileşen | Çağrılar |
> |---|---|
> | `VortexArena.Core.UI.TeamScorePanel` | `SetScore(int red, int blue)` · `SetRoundLabel(string)` (tur kavramı yoksa hiç çağrılmaz) · `Clear()` (lobiye dönüşte) |
> | `VortexArena.Core.UI.RoundResultBanner` | `Show(string text, RoundOutcome, bool sticky = false)` — `Won`/`Lost`/`Draw` yalnız **tonu** seçer, metin modundur; `sticky` şeridi sayaçsız açık bırakır (sunucunun telde TUTTUĞU sonuç için — süresi bilinmeyen bir bekleme okuma süresine sığmaz), indiren `Hide()` olur · `Hide()` |
>
> Panel takımsız modda kendini gizler (`ModeRuntime.IsTeamless`), yani takımsız bir HUD'da alanı boş
> bırakmak yeterlidir. Şerit süresini kendi tutar (prefab alanı, bugün 3 sn) — modun kapatması gerekmez.

> **Maç sonu ekranının (KAZANDIN/KAYBETTİN + skor tablosu) MANTIĞI moda ait DEĞİLDİR** ve yeni mod
> için kod işi yoktur: `MatchResultOverlay` mod ADINI bilmez, maç bitince HUD'ı kendisi gizler.
> Ekranın dalları (kazanansız kooperatif kartı, gizlenen K/D kolonları) **modun kurallarından**
> türer (`ModeRuntime.IsCoop` · `HidesCombatStats`) — modun kuralı doğruysa ekranı da doğrudur.
> **GÖRÜNÜMÜ ise moda ait olabilir** ve isteğe bağlıdır: `ModeDefinition.resultScreenPrefab`'a
> `MatchResultOverlay.prefab`'ın bir varyantı bağlanırsa maç sonunda o çizilir (reçete:
> `Yemek-Kitabi.md` "Moda özel maç sonu ekranı"). `WinnerLine`/`EndScoreLine` yine de yazılır —
> onlar HUD'ın kendi satırlarıdır (ekran kapandığında görünen değerler).

> ⚠️ Yaşam döngüsü metotlarını override edersen `base.` çağır.

---

## Arayüz kiti — "Girdap"

`VortexArena.Core.UI`. Admin ekranlarının ve maç sonu ekranının tüm görsel parçaları. Yerleşim ve
üretim **builder'ın** işidir (`Arayuz-Tasarimi.md`); burada çalışırken çağrılanlar var.

| Tip | Üye | Açıklama |
|---|---|---|
| `UiShape` | ✅ `Chamfer(c)` / `Chamfer(tl,tr,br,bl)` · `Slant(l,r)` · `Fill(c)` / `Fill(a,b,UiGradientMode,end)` · `Outline(w,a,b)` · `Glow(w,c,offset)` · `InnerGlow(w,c)` · `Antialias(bool)` | Akıcı API, hepsi `UiShape` döner. `OutlineWidth` okunur. `Glow`'un `w`'si CSS blur yarıçapıdır (σ = w/2) ve profil Gauss'tur: kalın şekil kenarında alfanın yarısını alır, 2 px çizgi soluk parlar — CSS `box-shadow` ile aynı |
| `UiPolygonGraphic` | ⚠️ `ChamferTopLeft`…`ChamferBottomLeft` · `SlantLeft`/`SlantRight` · statik `SignedArea` · `Offset` · `ClipHalfPlane` · `Fan` · `Ring` | Taban; statikler kendi poligon grafiğini yazanlar için. ⚠️ Türev kendi `[RequireComponent(typeof(CanvasRenderer))]`'ını taşır |
| `UiStripes` | ✅ `Stripes(angle, width, period, color)` · `AngleDeg` · `StripeWidth` · `Period` · `StripeColor` | Poligona kırpılı çapraz şeritler |
| `UiSegmentBar` | ✅ `SetFill(0..1)` · `SetFillColors(a,b)` · `SetTrack(c)` · `SetMetrics(segment, gap, skewDeg)` · `Fill` | Dilimli eğik çubuk (can, kumanda tiki) |
| `UiButtonStyle` | ✅ `SetKind(UiButtonKind)` · `SetInteractable(bool)` · `SetLabel(string)` · `SetHold(0..1)` · `Apply()` · `Kind` · `Interactable` · `TargetButton` · `Label` · `Shape` ⛔ `Bind(...)` | Düğmenin TÜM görünümü. ⚠️ Renk/zemin elle boyanmaz; `Bind` builder'ındır |
| `UiChip` | ✅ `Set(text, UiChipKind, iconName = null)` · `Root` · `Label` · sabitler `Height`/`Padding`/`IconSize`/`IconGap` ⛔ `Bind(...)` | Rozet; genişliğini `Set` ölçüp yazar |
| `Girdap` | ✅ `Hex(rgb[, a])` · `Rgba(r,g,b,a)` · `TeamHi/TeamLo/TeamInk(team)` · `Spacing(em)` · `Upper(s)` · `Font(GirdapFont)` · `Icon(name)` · `Assets` + palet alanları | **Statik.** Renk literali çağrı yerinde yazılmaz; `Upper` tr-TR'dir (`i → İ`) |
| `GirdapAssets` | ✅ `Font(GirdapFont)` · `Icon(name)` · `InvalidateIconLookup()` | `Resources/UI/Girdap.asset`; ikon çıplak adla istenir (`"Skull"` → `Ic_Skull`) |

> ⚠️ `UiButtonKind` · `UiChipKind` · `UiGradientMode` · `GirdapFont` prefablara **serileşir** —
> yeni değer sona eklenir (→ **[Yapma Listesi](Yapma-Listesi.md)**, "Serialize edilen veriler").

---

## Admin arayüzü

`VortexArena.App.Admin`. Oyun kodundan değil, admin ekranının kendi içinden çağrılır; hepsi
`Resources/UI/` altındaki üretilmiş prefablara oturur.

| Tip | Üye | Açıklama |
|---|---|---|
| `AdminHud` | ✅ `ResourcePath` | Üst şerit + takım sütunları + iki akış + alt şerit. Dışa açık metodu yoktur: veriyi `AdminRoster`'dan okur, panelleri `AdminSession.OpenPanel` açar |
| `AdminPlayerRow` | ✅ `Height` · `Initialize(Action<int> onSelect, Action<int> onPov)` · `Bind(AdminPlayerView, bool selected)` · `Tick()` · `SetVisible(bool)` · `Place(float top, float height)` ⛔ `EditorWire(...)` | Yan sütun kartı; havuzlanır. Yalnız YÜKSEKLİK sabittir, genişlik sütundan gelir |
| `AdminStatsRow` | ✅ `Height` · `PlayerId` · `Initialize(Action<int> onSelect, Action<int,string> onPopup)` · `Bind(AdminPlayerView, bool selected)` · `Tick()` · `SetVisible(bool)` · `Place(float top, float height)` · `BeginCalibrationLoad()` · `ApplyCalibrationResult(bool ok, string error)` | İstatistik tablosunun satırı; KALİBRE · ÖLÇ · ad · SIFIRLA · AT düğmelerini kendi sürer |
| `AdminStatsPanel` · `AdminPreferencesPanel` · `AdminMatchControls` · `AdminFloorControls` | — | Dışa açık API'leri **yoktur**; roster/oturum durumuna abonedirler. Panel görünürlüğü `AdminSession`, kat şeridi `ArenaFloors.Version` ile tazelenir |
| `AdminKillFeedView` | ✅ `Bind(IReadOnlyList<AdminKillFeedEntry> feed, int version)` | Havuzlu satırları (`AdminKillFeedRow`) kurar; `version` değişmedikçe hiçbir şey yapmaz |
| `AdminViolationFeedView` | ✅ `Bind(IReadOnlyList<AdminViolationFeedEntry> feed, int version)` | Aynı desen; canlı ihlal satırı daha yüksektir |
| `AdminKillFeedRow` | ✅ `Height` · `NewHeight` · `Bind(entry, weapon, newest, alpha, top)` | Vuran plakası · silah · vurulan plakası |
| `AdminViolationFeedRow` | ✅ `Height` · `LiveHeight` · `IsLive` · `Bind(entry, live, top)` · `TickBlink()` | Canlı ihlal satırı yanıp söner |
| `AdminViolations` | ✅ `Of(int playerId)` · `Kind(string)` · `Label(AdminViolationKind)` / `Label(string)` · `Blink(kind)` · `Tint(kind)` | **Statik.** İhlal türü → etiket ve Girdap rengi |
| `AdminRoster` | ✅ `Instance` · `Changed` · `CalibrationResult` · `Red`/`Blue`/`Players` · `KillEvents`/`ViolationEvents` · `KillFeedVersion`/`ViolationFeedVersion` · `KillFeed`/`ViolationFeed` · `Find`/`NameOf`/`NextPlayerId` · `TeamTotals(team, out kills, out deaths, out alive)` · `Phase`/`PhaseReason`/`ModeState` · `TimeRemaining`/`ScoreRed`/`ScoreBlue`/`CountdownSeconds` · `WinnerTeam`/`WinnerPlayerId` · `ModeId`/`SceneName`/`ScoreLimit`/`RoundSeconds` · `IsFfa`/`AdminCount`/`SnapshotAge`/`CanChangeSelection` | Admin tarafının tek veri kaynağı; satır tipi `AdminPlayerView`, öldürme türü `AdminKillKind` |

> ⚠️ `AdminKillKind` (`Kill` · `Suicide` · `Obstacle` · `Death`) ve `AdminViolationKind` serileşmez
> ama **akış cümlesini** seçer; yeni değer yine sona eklenir.

---

## MatchResultOverlay

`VortexArena.App.MatchResultOverlay` — oyuncunun maç sonu ekranı. Kendini örnekleyen kalıcı
tekildir, sahneye konmaz; çağrılacak bir metodu yoktur.

| Üye | Açıklama |
|---|---|
| ✅ `ResourcePath` | `Resources` içindeki yol |
| ⚠️ `RowHeight` · `RowGap` · `RowsTop` | Skor tablosu satır adımı — builder'ın şablonu ve çalışırken dizilen satırlar **aynı** sabitleri okur |
| ⚠️ `NameCellX` · `NameGap` | Ad hücresinin sol kenarı ve SEN çipinin boşluğu |
| ⚠️ `BlockCount` | Tablo bloğu sayısı (takım başına bir blok; takımsız kipte tek sıralama ikiye bölünür) |

> Moda özel görünüm bir **prefab varyantıdır** (`ModeDefinition.resultScreenPrefab`); mantık
> değişmez — reçete: `Yemek-Kitabi.md` "Moda özel maç sonu ekranı".

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
| `MATCH_END_SECONDS` | `999` | Maç sonu ekranının **emniyet** süresi — kazanan ekranını normalde operatörün seçimi kapatır |
| `RESPAWN_DELAY` | `5` | Varsayılan canlanma gecikmesi (mod ezebilir) |
| `REVIVE_HOLD_SECONDS` | `5` | "Sabit dur" canlanmasında bekleme |
| `REVIVE_HOLD_RADIUS` | `1` | Sabit durma toleransı (m) |
| `POSE_RATE_HZ` / `SNAPSHOT_RATE_HZ` | `20` | Poz gönderim/yayın hızı |
| `INTERP_DELAY_MS` | `100` | Uzak poz interpolasyon gecikmesi |
| `PLAYER_ID_MAX` | `255` | `playerId` UDP'de `u8` |
| `MAX_FLOOR_INDEX` | `7` | En yüksek geçerli kat indeksi — sunucunun `set_floor` aralık kapısı; arenanın gerçek kat sayısını **`ArenaFloors.Count`** verir |
| `FLOOR_CONFIRM_SECONDS` | `1` | `set_floor`'un roster'da yankılanması için beklenen süre; dolarsa istemci yerel katı geri alır |
| `LOADING_TIMEOUT` | `20` | Sahne yükleme kapısı |
| `OBJECT_POSE_RATE_HZ` | `20` | Sahibin obje pozu gönderim hızı (oyuncu pozuyla aynı; interpolasyon gecikmesine iki örnek payı) |
| `OBJECT_REST_SPEED` / `OBJECT_REST_SECONDS` | `0.05` m/s / `0.3` | Objenin "durdu" eşiği ve altında kesintisiz kalması gereken süre — ⚠️ tek karelik durma yeterli değildir (sekmenin tepesinde hız sıfırlanır) |
| `NET_ID_SCENE_MIN`/`_MAX` · `NET_ID_DYNAMIC_MIN`/`_MAX` | `1`/`32767` · `32768`/`65535` | Sahne kimlikleri ve sunucunun çalışma zamanında dağıttıkları; ikisi asla çakışmaz |

> **Eşzamanlı oyuncu kotası YOKTUR.** Tek tavan `PLAYER_ID_MAX` ve o bir ürün kararı değil,
> protokol sonucudur.

---

## Editör araçları

| Menü | Ne yapar |
|---|---|
| `Tools > VortexArena > Development > Dev` | Rol (player · admin) · sunucu hedefi · Play başlangıcı · sunucusuz sandbox. Kısayol **Ctrl+Alt+R** (iki rolü çevirir) |
| `Tools > VortexArena > Items > Kavrama Pozu Stüdyosu` | **Elde tutulan her eşyanın** (silah, bomba, ileride mutfak eşyası) elde nasıl duracağını **gözlüksüz** yazar (`GripPoseStudio`). Hedef bir **eşya tanımıdır**; prefabı prefab kipinde aç → *Ana/Ön Kabza Ellerini Oluştur* → kumanda çerçevelerini kabzalara oturt → el modelini o kumandanın üstüne yerleştir (taşı **ve çevir** — eşya kımıldamaz) → parmakları o eşyaya göre rigle (penceredeki eklem listesinden seç, Scene View'da çevir) → **Kaydet**. Tanım iki yoldan çözülür: prefabın üstünde tanımı taşıyan bileşen (`IItemHolder` — `Weapon`), yoksa `prefab` alanı bu prefabı gösteren `ItemDefinition` (**ters arama** — bombanın yolu budur, `Throwable` tanımını çalışma zamanında alır). ⚠️ Aynı prefabı gösteren iki tanım varsa stüdyo **yazmaz**, tanımı elle seçmeni ister. Kayıt tanım asset'ine gider (kumanda anchor'ının eşyaya göre KONUMU + el modelinin kumandaya göre POZU + riglenmiş parmak eklemleri); prefaba hiçbir şey yazılmaz, eller stage'in ayrı kökleridir. *Kopya Al* elin GÖRSELİNİ (yerleşim + parmak rigi) başka bir eşyadan aynen alır — listede yalnız aynı kavrama noktasının aynı eli yazılmış eşyalar çıkar, eşyanın kumandaya göre yeri kopyalanmaz. Kaydet ayrıca **eşitleme koşturur** (`Configure All Build Elements`'a gitmeye gerek yok): silahsa silah kiti, değilse yalnız eşya kataloğu; kit açık prefabı yeniden yazdığı için tezgâhtaki eller kalkabilir, *Elleri Oluştur* onları kayıttan aynı yere getirir. ⚠️ Kumanda kökü yalnız TAŞINIR — anchor kaydı dönüş taşımaz, silahın dönüşü ana kumandadan gelir (çevrilen kök geri hizalanır); dönüş yazılabilen tek şey **el modelidir** ve o silahı çevirmez |
| `Tools > VortexArena > Arena > Template Temellerini Yükle` | Aktif sahneye altyapı prefab ÖRNEKLERİ + `ArenaCalibrator` ve `ArenaBoundary`'nin rig alanlarını bağlama + boyut dosyası bağlama; idempotent (`TemplateBasicsLoader`). ⚠️ Kalibrasyon işaretçisi koymaz — onlar maketle gelir. **Kat sayısı** (1–4) 1'den büyükken her ek kat için bir `VA_FloorPortal` örneği koyar (yerini ve `upperHeight`'ını tasarımcı ayarlar); sahnede zaten portal varsa hiç dokunmaz |
| `Tools > VortexArena > Arena > Kat Portalı Kitini Üret` | Kat geçişinin malzemelerini (`M_FloorPortal` · `M_FloorPortalFill`, URP Unlit saydam) ve `VA_FloorPortal.prefab`'ını üretir/günceller (`FloorPortalKitBuilder`); yeniden çalıştırılabilir, sahnedeki örnekler bağını korur |
| `Tools > VortexArena > Arena > JSON'dan DimensionMesh Üret` | Boyut dosyasından ölçü maketi (`Plane` + `Columns/*` + kalibrasyon işaretçileri `anchor_a`/`anchor_b`), **`ArenaBoundary`'nin altına yerel-kimlikte** (muhafaza yoksa sahne köküne, dönüşsüz); idempotent (`DimensionMeshBuilder`). ⚠️ Her arenada zorunlu: sahnenin kalibrasyon işaretçilerinin tek kaynağı budur |
| `Tools > VortexArena > Arena > DimensionMesh'i JSON'a Çevir` | Maketi (köşeler + kalibrasyon işaretçileri) okuyup kaynak boyut dosyasının üstüne yazar; doğrulanamayan çıktıda dosyaya dokunmaz, işaretçi yoksa `calibration` korunur (`DimensionMeshReader`) |
| `Tools > VortexArena > Build > Configure All Build Elements` | **Hepsini Çalıştır** (tek düğme): aktif sahne bir arena kutusuysa önce onun `MapDefinition`'ını yazar, sonra her durumda `GameCatalog` + dolu `ModeDefinition.maps` + `ModeDefinition.loadout` (rastgele silah havuzu) + Build Settings + silah kiti + net eşya kataloğu + `maps.json`'ı `Venues/*/Scenes/*/` ağacına göre eşitler (fazla/ölü kayıt silinir, eksik olan uyarı olur), HMD katmanlarını yalnız bayatsa kurar, sonda sağlık raporu basar. Sahne açık olmadan da çalışır (`BuildElementsConfigurator`). *Ağ nesneleri* satırının **Onar** düğmesi tüm arena sahnelerini açıp `sceneId` onarır ve obje listelerini tazeler — Hepsini Çalıştır'a dahil DEĞİL, merge/kopya sonrası elle basılır (`SceneIdBatchRepair`) |
| `Tools > VortexArena > Server > Export Server Config` | `MapDefinition` SO'larından `Server/config/maps.json` — girdi başına yalnız `sceneName` + `modes` (arena ölçüsü sunucuya gitmez). ⚠️ JSON'u elle düzenleme, export ezer |
| `GameObject > VortexArena > Arena Roof` | Çatı geometrisini işaretler (admin kuş bakışında gizlenir) |
| `GameObject > VortexArena > Network Parent` | Sahne objesine `NetIdentity` + benzersiz `sceneId`. Prefab örneğinde kimlik **override** olarak sahnede kalır, prefaba Apply edilmez; prefab asset'i atlanır. Bileşen zaten varsa yalnız `Ctrl+S` yeter (`SceneIdGuard`) |

> ⚠️ Rol/IP **dev penceresinden** seçilir; sahneye `[SerializeField]` override **koyulmaz**.
> Seçim `EditorPrefs`'te kişisel kalır, hedef listesi `dev-targets.json`'da commit'lidir.
