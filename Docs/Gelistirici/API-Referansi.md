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
| ✅ `CanFire` | `bool` | **Ateş etmeden önce bunu sor.** Hayatta + faz Lobby/Live + bağlantı açık + oyuncunun kendisi engelde değil + **alanın dışında değil**. Hiç bağlanılmadıysa `true` (yerel test bozulmasın). ⚠️ Alan-dışı kapısı tamamen yereldir, protokolde karşılığı yoktur: alanın dışına çıkıp içeri ateş etmek geriye kalan tek fiziksel hile yoluydu |
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
| ✅ `GetHitZone(Collider)` | `HitZone` | İsabet bölgesi (`Body`/`Head`/`Stomach`/`Leg`); ağ oyuncusu değilse `Body`. Çarpanı `WeaponDefinition.GetZoneMultiplier(zone)` verir, **uygulamak sana ait** |

### Bildirme

| Metot | Ne yapar |
|---|---|
| ✅ `ReportShot(Vector3 worldMuzzlePos, Vector3 worldDir, string weaponId)` | Atışı diğer oyunculara relay ettirir (namlu alevi/ses). Hasarla ilgisi yok, sunucu doğrulamaz |
| ⚠️ `ReportHit(int targetPlayerId, Vector3 worldHitPoint, float damage, string weaponId)` | Vuruşu bildirir. **Hasarı sen belirlersin**, sunucu aynen uygular. Canı yerelde düşürme |
| ✅ `ReportRaycastHit(in RaycastHit, float damage, string weaponId)` | Hitscan kısayolu. `false` → hedef ağ oyuncusu değil, **hasar uygulanmaz**; dönüş değeri yalnız sunum kararı içindir (gövde efekti mi, duvar efekti mi) |
| ⚠️ `ReportAreaHit(Vector3 center, float radius, float damage, string weaponId, float edgeScale = 0.25f, int layerMask = ~0)` | Yarıçaptaki her oyuncuya ayrı vuruş; merkeze uzaklıkla doğrusal düşer. **Duvar arkası kontrolü yok** |

**Hasar geçerlilik kuralı:** pozitif ve sonlu olmalı. `NaN`/`∞`/negatif hem burada hem sunucuda
reddedilir (NaN'a düşen can bir daha 0'ın altına inemez → oyuncu ölümsüz kalırdı).

**İsabet göstergesi hazır gelir:** `ReportHit` (dolayısıyla `ReportRaycastHit`/`ReportAreaHit`)
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
| ✅ `FriendlyFire` | `bool` | ⚠️ Modun değil **operatörün** anahtarı: maç ORTASINDA değişebilir (`rules_update`), `Changed`'i dinle |
| ✅ `Revive` | `ModeReviveAnchor` | `OwnBase` \| `StandStill` |
| ✅ `Weapons` | `ModeWeaponSource` | `WeaponCanvas` (sahnede elle konmuş silah, çerçeveden seçilir, tükenmez) \| `RandomGrant` (mod dağıtır). ⚠️ Tek başına "kurulmuş maç var" demek değildir — aşağı bak |
| ✅ `FireWhilePaused` | `bool` | Maç kurulmamışken ateş serbest mi (lobi profili). `RandomGrant` ile **birlikte** okunur: `random` + `FireWhilePaused` = serbest alan, yalnız `random` = mod silah dağıtıyor |
| ✅ `RespawnDelay` | `float` | ⚠️ **`0` geçerlidir** (anında canlanma) |
| ✅ `Changed` | olay | Kurallar değişti |
| ⛔ `Apply` / `ApplyFromCatalog` / `Reset` | | Besleme sistemin işi |

> ⚠️ **`if (modeId == "…")` zinciri yazma.** Yeni mod eklemek senin kodunu değiştirmemeli.

> ⚠️ **Sahnelenen arena lobi profiliyle koşar** (operatör lobideyken bir arena seçtiğinde herkes o
> arenaya geçer ama maç kurulmaz): orada `Weapons == RandomGrant`'tir. "Mod silah dağıtıyor mu"
> sorusunun cevabı bu yüzden `Weapons == RandomGrant && !FireWhilePaused`'dur —
> `Docs/ArenaNet-Protokol.md` §10.7.

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
yazısı belirir + kumandalar nabız atar. ⚠️ Sınır aşıldıktan sonra ikinci bir mesafe rampası (ve
ayarlanabilir bir karartma tavanı) **YOKTUR**: alanın dışı, engelin içiyle aynı sorudur — yüzde
birkaçlık saydamlık bile perdenin öbür yüzünü okunabilir bırakır ve dışarıdan içeri bakmak
istismarın kendisidir. Titreşim `ControllerHaptics` hakeminden geçer, motor doğrudan sürülmez.
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
| ✅ `ToJson(bool pretty)` | Planı metne çevirir — editör araçları dosyayı bununla yazar |

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
durumdur: PlayerPrefs'te anchor UUID'si hiç yok, ya da geri yükleme tüm denemelerde düştü.

> ⚠️ **Bu bir kalibrasyon DEĞİLDİR** ve öyle raporlanmaz: yakalama sayacı artmaz, `Calibrated`
> yayınlanmaz, anchor kaydedilmez, elle kalibrasyon kapısı açık kalır. Amacı görünürlüktür —
> hizalanmamış rig oyuncuyu `ArenaBoundary` karartmasının içinde bırakırsa elle kalibre etmesi
> gereken oyuncu hiçbir şey göremez.
> ⚠️ `CalibrationGeneration` **artar**: taşınma meşrudur, kök sıçraması bastıran emniyetler bunu
> arıza saymamalıdır.
> Koşmadığı durumlar: kayıtlı anchor geri yüklendiyse, oyuncu jeste başladıysa, operatör
> sıfırladıysa, rig kökü kapalıysa (admin gözlemci) ve işaretçiler yok/aynıysa.

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
| ✅ `LocalPlayerId` | yardımcı | Kendi `playerId`'in; bağlantı yokken `0` |
| ✅ `SetText(TMP_Text, string)` | yardımcı | Null-güvenli |

Tabandan **hazır** gelenler: faz/süre, geri sayım, can + can barı, ölüm ekranı, durum metni,
kill-feed, kendi öldürme/ölüm sayacın.

> **Ölüm ekranı da moda ait DEĞİLDİR:** görseli `_Shared/App/Resources/UI/DeathHud.prefab`'da
> durur, HUD prefabının altına iç içe konur ve taban açıp kapatır. Katil satırını
> (`<ad> tarafından öldürüldün!` · `Engelde kaldın` · `Öldün`) ve canlanma sayacını taban yazar —
> alt sınıfın yapacağı iş yoktur, prefab bağları yeterlidir.

> **Can barı da moda ait DEĞİLDİR:** görseli `_Shared/App/Resources/UI/HealthHud.prefab`'da durur
> ve o da HUD prefabının altına iç içe konur. Taban iki alanı sürer: `healthFill`
> (`Backdrop/Fill`, `Image.type = Filled`) ve `healthText` (`Backdrop/Value`). Alt sınıfın işi
> yoktur.

> ⚠️ Takıma ait hiçbir şey tabanda değildir (bazı modlarda takım yoktur) — renk ve kolon alt sınıfın işi.

> **Maç sonu ekranı (KAZANDIN/KAYBETTİN + skor tablosu) moda ait DEĞİLDİR** ve yeni mod için
> yapılacak hiçbir iş yoktur: `MatchResultOverlay` mod-agnostiktir, maç bitince HUD'ı kendisi
> gizler. `WinnerLine`/`EndScoreLine` yine de yazılır — onlar HUD'ın kendi satırlarıdır (ekran
> kapandığında görünen değerler).

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
| `MATCH_END_SECONDS` | `999` | Maç sonu ekranının **emniyet** süresi — kazanan ekranını normalde operatörün seçimi kapatır |
| `RESPAWN_DELAY` | `5` | Varsayılan canlanma gecikmesi (mod ezebilir) |
| `REVIVE_HOLD_SECONDS` | `5` | "Sabit dur" canlanmasında bekleme |
| `REVIVE_HOLD_RADIUS` | `1` | Sabit durma toleransı (m) |
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
| `Tools > VortexArena > Development > Dev` | Rol (player · admin) · sunucu hedefi · Play başlangıcı · sunucusuz sandbox. Kısayol **Ctrl+Alt+R** (iki rolü çevirir) |
| `Tools > VortexArena > Weapons > Kavrama Pozu Stüdyosu` | Silahın elde nasıl duracağını **gözlüksüz** yazar (`GripPoseStudio`). `WPN_*`'ı prefab kipinde aç → *Ana/Ön Kabza Ellerini Oluştur* → kumanda çerçevelerini kabzalara oturt → el modelini o kumandanın üstüne yerleştir (taşı **ve çevir** — silah kımıldamaz) → parmakları o silaha göre rigle (penceredeki eklem listesinden seç, Scene View'da çevir) → **Kaydet**. Kayıt `WD_*.asset`'e gider (kumanda anchor'ının eşyaya göre KONUMU + el modelinin kumandaya göre POZU + riglenmiş parmak eklemleri); prefaba hiçbir şey yazılmaz, eller stage'in ayrı kökleridir. *Kopya Al* elin GÖRSELİNİ (yerleşim + parmak rigi) başka bir silahtan aynen alır — listede yalnız aynı kavrama noktasının aynı eli yazılmış silahlar çıkar, silahın kumandaya göre yeri kopyalanmaz. Kaydet ayrıca **silah kitini eşitler** (`Configure All Build Elements`'a gitmeye gerek yok); kit açık prefabı yeniden yazdığı için tezgâhtaki eller kalkabilir, *Elleri Oluştur* onları kayıttan aynı yere getirir. ⚠️ Kumanda kökü yalnız TAŞINIR — anchor kaydı dönüş taşımaz, silahın dönüşü ana kumandadan gelir (çevrilen kök geri hizalanır); dönüş yazılabilen tek şey **el modelidir** ve o silahı çevirmez |
| `Tools > VortexArena > Arena > Template Temellerini Yükle` | Aktif sahneye altyapı prefab ÖRNEKLERİ + `ArenaCalibrator` ve `ArenaBoundary`'nin rig alanlarını bağlama + boyut dosyası bağlama; idempotent (`TemplateBasicsLoader`). ⚠️ Kalibrasyon işaretçisi koymaz — onlar maketle gelir |
| `Tools > VortexArena > Arena > JSON'dan DimensionMesh Üret` | Boyut dosyasından ölçü maketi (`Plane` + `Columns/*` + kalibrasyon işaretçileri `anchor_a`/`anchor_b`), **`ArenaBoundary`'nin altına yerel-kimlikte** (muhafaza yoksa sahne köküne, dönüşsüz); idempotent (`DimensionMeshBuilder`). ⚠️ Her arenada zorunlu: sahnenin kalibrasyon işaretçilerinin tek kaynağı budur |
| `Tools > VortexArena > Arena > DimensionMesh'i JSON'a Çevir` | Maketi (köşeler + kalibrasyon işaretçileri) okuyup kaynak boyut dosyasının üstüne yazar; doğrulanamayan çıktıda dosyaya dokunmaz, işaretçi yoksa `calibration` korunur (`DimensionMeshReader`) |
| `Tools > VortexArena > Build > Configure All Build Elements` | **Hepsini Çalıştır** (tek düğme): aktif sahne bir arena kutusuysa önce onun `MapDefinition`'ını yazar, sonra her durumda `GameCatalog` + dolu `ModeDefinition.maps` + `ModeDefinition.loadout` (rastgele silah havuzu) + Build Settings + silah kiti + net eşya kataloğu + `maps.json`'ı `Venues/*/Scenes/*/` ağacına göre eşitler (fazla/ölü kayıt silinir, eksik olan uyarı olur), HMD katmanlarını yalnız bayatsa kurar, sonda sağlık raporu basar. Sahne açık olmadan da çalışır (`BuildElementsConfigurator`) |
| `Tools > VortexArena > Server > Export Server Config` | `MapDefinition` SO'larından `Server/config/maps.json` — girdi başına yalnız `sceneName` + `modes` (arena ölçüsü sunucuya gitmez). ⚠️ JSON'u elle düzenleme, export ezer |
| `GameObject > VortexArena > Arena Roof` | Çatı geometrisini işaretler (admin kuş bakışında gizlenir) |
| `GameObject > VortexArena > Network Parent` | Sahne objesine `NetIdentity` + benzersiz `sceneId` |

> ⚠️ Rol/IP **dev penceresinden** seçilir; sahneye `[SerializeField]` override **koyulmaz**.
> Seçim `EditorPrefs`'te kişisel kalır, hedef listesi `dev-targets.json`'da commit'lidir.
