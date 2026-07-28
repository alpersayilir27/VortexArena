# ArenaNet Protokol Referansı (v1) — TEK DOĞRULUK KAYNAĞI

> Unity `VortexArena.Protocol` asmdef'i ile .NET sunucu aynı C# kaynaklarını derler (yapısal sapma imkânsız); bu doküman **semantiğin** tek doğruluk kaynağıdır. İki taraftan biri davranış değiştirecekse ÖNCE burası güncellenir.

## 1. Sabitler

Tümü paylaşılan `ArenaProtocol` statik sınıfında tanımlanır (`Assets/_Shared/Net/Protocol/ArenaProtocol.cs`).

| Sabit | Değer | Açıklama |
|---|---|---|
| `PROTOCOL_VERSION` | `1` | hello/welcome'da taşınır; uyumsuzlukta log uyarısı (bağlantı kesilmez) |
| `UDP_BEACON_PORT` | `47820` | Sunucu → broadcast (cosmos 47800/47801 ile bilerek çakışmaz) |
| `CONTROL_PORT` | `47821` | WS TCP, endpoint `/ws` |
| `STATE_PORT` | `47822` | UDP poz kanalı |
| `BEACON_INTERVAL` | 2 sn | Beacon yayın aralığı |
| `DISCOVERY_TIMEOUT` | 5 sn | Beacon gelmezse statik IP fallback (`StreamingAssets/arena.json`); komut satırı adresi ve elle girilen IP beacon'ın **üstündedir** (zincirin tamamı §4) |
| `STATUS_INTERVAL` | 5 sn | İstemci status kalp atışı |
| `OFFLINE_TIMEOUT` | 15 sn | Status gelmezse cihaz çevrimdışı sayılır, bağlantı kapatılır |
| `RECONNECT_BACKOFF` | 1 → 2 → 5 sn (tavan 5) | Kopunca sonsuz yeniden deneme; her denemede discovery baştan |
| `POSE_RATE_HZ` | `20` | İstemci poz gönderim frekansı |
| `SNAPSHOT_RATE_HZ` | `20` | Sunucu snapshot yayın frekansı |
| `INTERP_DELAY_MS` | `100` | Uzak avatar interpolasyon tamponu |
| `PLAYER_ID_MAX` | `255` | `playerId` tahsis tavanı. **Ürün kotası değil, tel formatı tavanıdır** — `playerId` UDP paketlerinde `u8`. Eşzamanlı oyuncu/admin sayısına başka sınır YOKTUR (kota ileride lisanslamayla gelecek) |
| `SNAPSHOT_MAX_ENTRIES_PER_PACKET` | `16` | Tek snapshot datagramına yazılan en fazla oyuncu; fazlası ek pakete taşar (§6.3). 6 + 16×86 = 1382 B < MTU |
| `PLAYER_MAX_HP` | `100` | Oyuncu tam canı (sunucu-otoriter; §10) |
| `COUNTDOWN_SECONDS` | `5` | Countdown fazının uzunluğu |
| `MATCH_END_SECONDS` | `10` | End fazı → otomatik `return_to_lobby` |
| `LOADING_TIMEOUT` | 20 sn | Loading'de tüm `set_ready` beklenmezse yine de Countdown'a geçilir |
| `RESPAWN_DELAY` | 5 sn | Ölüm → en erken canlanma (`respawn.delaySeconds`) **varsayılanı**; mod `rules.respawnDelay` ile ezebilir (§10.5) |
| `REVIVE_GRACE` | 20 sn | `revive_request` gelmezse sunucu ölümden bu kadar sonra zorla canlandırır |
| `REVIVE_HOLD_SECONDS` | 3 sn | `reviveAnchor:"standstill"` (§10.5): ölü oyuncunun canlanmak için kesintisiz sabit durması gereken süre |
| `REVIVE_HOLD_RADIUS` | 1 m | `reviveAnchor:"standstill"`: ölüm anındaki çapadan bu yarıçapı aşan hareket sayacı sıfırlar |
| `ROUND_SECONDS_OPTIONS` | `150, 300, 600, 900, 1200, 1800, 3600` | Admin arayüzünün maç süresi seçenekleri (2.5 · 5 · 10 · 15 · 20 · 30 dk · 1 saat). **Protokol kısıtı değil, arayüz listesidir** — sunucu `start_match.roundSeconds`'ta her pozitif değeri kabul eder |

## 2. Roller ve kimlik

- `role`: `"player"` (VR/Quest) veya `"admin"` (Windows masaüstü). Admin oynamaz; lobi rosterinde görünür, komut yetkisi vardır.
- **Admin sahne olarak oyuncuları takip eder:** `load_match` / `welcome.match` / `return_to_lobby` admin istemcisinde de sahne yükler (gözlemci görünümü). İki fark: admin `set_ready` **göndermez** (Loading kapısını yalnız `role=player` besler) ve poz **göndermez** (`0x01 PoseUpdate` yok), ama `0x00` ile UDP kaydı yapıp snapshot'ları alır.
- `deviceId` — **role göre iki ayrı semantik:**
  - `player`: `SystemInfo.deviceUniqueIdentifier`, **kalıcı** kimlik. Sunucu `devices.json`'da ada eşler ("Gözlük NN" otomatik adlandırma), kayıt bağlantı kopsa da durur (aynı gözlük geri gelince adı/kimliği korunur).
  - `admin`: `<deviceUniqueIdentifier>:admin:<oturum GUID'i>` — **oturum başına benzersiz**. Sebep: aynı fiziksel PC'de iki admin penceresi açılabilsin. Ortak deviceId ile ikisi aynı kaydı paylaşır, her `hello` diğerinin soketini kapatır ve sonsuz kick döngüsü olurdu. GUID süreç ömrü boyunca sabittir (yeniden bağlanma aynı kaydı bulur), uygulama kapanınca ölür.
- **Admin kayıtları kalıcı DEĞİLDİR:** admin bağlantısı koptuğunda (veya `OFFLINE_TIMEOUT` dolduğunda) kaydı registry'den **tümüyle silinir** ve `playerId`'si havuza döner; adı `devices.json`'a **yazılmaz**. Oyuncu kayıtları eskisi gibi çevrimdışı işaretlenir ama durur. Böylece admin'i her açıp kapatma roster'da hayalet satır bırakmaz.
- **Admin sayısı sınırsız ve hepsi eş yetkilidir.** Birincil/ikincil admin kavramı yoktur: `role=="admin"` olan her bağlantı §5.2'deki tüm komutları gönderebilir, son gelen komut uygulanır. Operatörlerin birbirini ezmemesi için ortak seçim `admin_state` ile senkronlanır (§5.3) ve her komut `admin_state.notice` ile diğerlerine duyurulur.
- `playerId` = sunucunun `welcome`'da atadığı **1..`PLAYER_ID_MAX`** arası küçük tamsayı (UDP paketlerinde 1 bayt). Admin'e de atanır (poz göndermez). Havuz dolarsa `kicked{reason:"Sunucu dolu"}` ile reddedilir — bu bir ürün kotası değil, `u8` tel formatının tavanıdır.
- Aynı `deviceId` ikinci kez bağlanırsa eski bağlantı kapatılır, yenisi kabul edilir (cihaz yeniden bağlanmıştır).

## 3. Koordinat çerçevesi — ARENA UZAYI

Tüm ağ pozları **arena-yerel uzaydadır**: origin = arena zemin merkezi, eksenler arena duvarlarına hizalı. Her Quest, `ArenaCalibrator` (2-nokta + OVRSpatialAnchor) ile fiziksel alana hizalandığı için bütün cihazlar aynı fiziksel çerçeveyi paylaşır. Dönüşüm istemcide yapılır (rig-world → arena-local); sunucu ve admin görünümü ham arena koordinatı kullanır.

## 4. UDP Beacon (sunucu → 47820 broadcast, her 2 sn)

Hem `255.255.255.255` hem her arayüzün subnet-broadcast adresine gönderilir:

```json
{ "app": "VortexArena", "ver": 1, "ip": "192.168.1.10", "controlPort": 47821, "statePort": 47822, "serverId": "GUID-string" }
```

İstemci `app == "VortexArena"` doğrular. Android'de beacon dinlemek için **MulticastLock** gerekir (cosmos `ServerLocator.cs` çözümü port edilir).

**Rol başına keşif akışı (istemci davranışı):**

| Rol | Adres nereden gelir |
|---|---|
| `player` (Quest) | **komut satırı `--server-ip <ip> [--server-port <port>]`** > PlayerPrefs (elle girilmiş) > **beacon** > `StreamingAssets/arena.json`. Bulunan adrese **otomatik bağlanılır**; oyuncuya sorulmaz. VR build'ine argüman geçilmediği için pratikte beacon kazanır. Hiçbiri yoksa lobide sağ kumandada **A×2** ile gizli IP paneli açılır ve elle girilen değer beacon'ı ezer (PlayerPrefs'e kalıcı yazılır). |
| `admin` (Windows) | **Yalnız komut satırı:** `--server-ip <ip> [--server-port <port>]` — Flutter launcher geçer. Beacon/PlayerPrefs kullanılmaz, kullanıcıya IP sorulmaz. Argüman yoksa bağlanmaz ve ekranda sebebini yazar. |

> **Zincir rolden bağımsızdır:** `AppBoot` komut satırı adresini **her rolde** okur; verilmişse keşfin en üstünde yer alır (açıkça verilen adres kazanır, gelen beacon onu ezmez). **Editörde** rol ve adres komut satırı yerine `Tools > VortexArena > Dev` penceresinden gelir (`EditorPrefs` — sahnede rol/IP override alanı YOKTUR); aynı pencere yalnız-editör bir **sentetik `load_match` enjeksiyonu** da yapabilir (bir arena sahnesinden doğrudan Play). Bu bir test kancasıdır (`NetEvents.InjectLoadMatch`, `#if UNITY_EDITOR`) — **yeni mesaj/alan değildir**, mevcut `load_match` (§5.3) kullanılır ve protokol yüzeyi değişmez.

> **Bağlantı kurulamazsa:** istemci bağlantısızlık ~3 sn sürdüğünde tasarımlı bir hata ekranı gösterir (`ConnectionOverlay`, VR + masaüstü): adres biliniyorsa "SUNUCUYA BAĞLANILAMIYOR" + adres + deneme sayacı + son hata, adres hiç yoksa "SUNUCU BULUNAMADI". Sunum katmanıdır, protokolü etkilemez; yeniden deneme kuralı `RECONNECT_BACKOFF`'tur (§1).

## 5. WS kontrol mesajları (JSON, text)

**Zarf kuralı:** her mesajda `"type"` alanı. Alıcı önce yalnız `{"type":"..."}` parse eder, sonra tipe göre tam DTO'ya parse eder. **Bilinmeyen type → logla ve yok say** (ileri sürüm uyumluluğu).

### 5.1 İstemci → Sunucu

**`hello`** — bağlantı açılır açılmaz, bir kez:
```json
{ "type": "hello", "protocolVersion": 1, "role": "player",
  "deviceId": "...", "deviceName": "...", "appVersion": "0.1.0",
  "currentScene": "Lobby", "scenes": ["Boot","Lobby","Arena10x10","IceWorld"] }
```
`scenes` = build listesinden runtime'da toplanır (`SceneUtility.GetScenePathByBuildIndex`) → admin katalog doğrulaması bunu kullanır.

**`status`** — her 5 sn: `{ "type":"status", "scene":"Arena10x10", "battery":0.87, "fps":71.6 }`

**`set_name`** `{ "type":"set_name", "name":"Oyuncu 3" }`
**`set_ready`** `{ "type":"set_ready", "ready":true }` (yalnız player)
**`set_team`** `{ "type":"set_team", "playerId":5, "team":"blue" }` (`"red"|"blue"`) — oyuncu yalnız
KENDİ `playerId`'si için gönderebilir (lobide takım seçimi); admin herkes için. Aksi loglanıp yok sayılır.

**`shot_fired`** — atış anında (uzak VFX/SFX + sayım için; vuruş AYRI rapor edilir):
```json
{ "type":"shot_fired", "seq":123, "weaponId":"ak47",
  "muzzlePos":[1.2,1.4,-3.0], "muzzleDir":[0.1,0.0,0.99] }
```
`muzzlePos`/`muzzleDir` **arena uzayındadır** (§3) — alıcı istemci kendi dünyasına çevirir.

**`hit_report`** — istemci bir oyuncuya hasar verdiğinde (mermi, balta, ok, patlama, çevre — kaynağı fark etmez):
```json
{ "type":"hit_report", "seq":124, "targetPlayerId":5, "weaponId":"ak47",
  "damage":25.0, "hitPos":[0.4,1.5,2.2] }
```
`hitPos` arena uzayında. **`damage` istemcinin hesapladığı değerdir ve sunucu onu aynen uygular** — sunucuda silah tablosu YOKTUR (§10.3). `weaponId` yalnız bir etikettir (kill feed / istatistik), doğrulanmaz: yeni bir silah/hasar kaynağı eklemek için sunucuya hiçbir şey tanıtmak gerekmez. Sunucu yalnız durum tutarlılığını kontrol eder (faz, atıcı/hedef canlı mı, dost ateşi). Geçerse hasarı uygular ve `health_update` yayınlar. **İstemci hasarı yerel uygulamaz** — `health_update` bekler.

Alan etkisi (bomba, el bombası) ayrı bir mesaj tipi gerektirmez: patlamayı gören istemci **etkilenen her hedef için bir `hit_report`** yollar, mesafeye göre düşen hasarı kendisi hesaplar. Aynı şekilde yaydaki çekiş gücü, kafa vuruşu çarpanı veya düşme hasarı da istemci tarafında hesaplanıp `damage` alanına yazılır.

**`revive_request`** `{ "type":"revive_request" }` — ölü oyuncu, `respawn.delaySeconds` dolduktan **ve** modun canlanma şartını sağladıktan (taban bölgesine girme ya da sabit durma) sonra gönderir; sunucu koşulları doğrulayıp canlandırır (§10.4). Free-roam'da oyuncu ışınlanamadığı için canlanma bir **konum değişimi değil, durum değişimidir**.

### 5.2 Yalnız admin → Sunucu

- **`start_match`** `{ "type":"start_match", "modeId":"tdm", "sceneName":"Arena10x10", "roundSeconds":600, "scoreLimit":30 }`
  `roundSeconds`/`scoreLimit` **o maça özeldir**: `≤ 0` ya da eksikse modun kendi varsayılanı (`IGameMode.DefaultRoundSeconds`/`DefaultScoreLimit`) kullanılır. Operatörün arayüzde seçtiği süre/limit buradan geçer; `ROUND_SECONDS_OPTIONS` yalnız arayüz listesidir, sunucu her pozitif değeri kabul eder.
- **`abort_match`** `{ "type":"abort_match" }`
- **`kick`** `{ "type":"kick", "playerId":5 }`
- **`identify`** `{ "type":"identify", "playerId":5 }` → o cihazda kimlik overlay'i (cosmos deseni)
- **`return_to_lobby`** `{ "type":"return_to_lobby" }`
- **`set_selection`** `{ "type":"set_selection", "modeId":"tdm", "sceneName":"Arena10x10", "roundSeconds":600, "scoreLimit":30 }` — bir sonraki maçın **ortak** mod/harita/süre/limit seçimi. Maçı BAŞLATMAZ; yalnız sunucudaki seçimi günceller ve sunucu bunu `admin_state` ile tüm adminlere yayar (çoklu admin senkronu, §5.3). Boş string veya `0` bırakılan alan mevcut değerini korur. Seçim sunucuda faz Lobby'ye dönerken sıfırlanmaz — operatör aynı haritayı tekrar başlatabilsin.
  **Neden maç parametreleri de ortak:** iki operatör aynı ekranı görmezse biri 5 dk sandığı maçı 30 dk başlatır. Süre/limit *operasyonel* durumdur, görünüm tercihi değil (§5.3 son madde).

Sunucu, `role != "admin"` bağlantıdan gelen admin komutunu loglayıp yok sayar.

### 5.3 Sunucu → İstemci

**`welcome`** — hello yanıtı:
```json
{ "type":"welcome", "protocolVersion":1, "playerId":3, "udpToken":123456789,
  "match": { "phase":"Lobby", "modeId":"", "sceneName":"", "timeRemaining":0,
             "scoreRed":0, "scoreBlue":0,
             "rules": { "teamMode":"two", "scoring":"team", "friendlyFire":false,
                        "reviveAnchor":"base", "weaponSource":"rack", "respawnDelay":5.0 } } }
```
`match.phase` boş/`"Lobby"` değilse **geç katılım senkronu**: istemci `sceneName`'i yükleyip maça katılır.
`match.rules` = koşan maçın kural şekli (§10.5) — geç katılan istemci/admin kendini aynı kurallara göre kurar.

**`lobby_state`** — roster her değiştiğinde **ve maç sayaçları değiştiğinde** (ölüm/canlanma) TAM anlık görüntü:
```json
{ "type":"lobby_state", "players":[
  { "playerId":3, "name":"Gözlük 03", "role":"player", "team":"red",
    "ready":true, "online":true, "battery":0.87, "scene":"Arena10x10",
    "kills":4, "deaths":2, "hp":72.0, "alive":true, "score":7 } ] }
```
`kills`/`deaths`/`hp`/`alive`/`score` **sunucu-otoriter** maç sayaçlarıdır (§10.2) ve admin gözlemci
arayüzünün tek doğruluk kaynağıdır: yalnız `kill_event`/`health_update` sayılsa admin yeniden
bağlandığında tablo sıfırlanırdı. Lobby fazında `hp=PLAYER_MAX_HP`, `alive=true`, sayaçlar 0.
Admin olmayan istemciler bu alanları yok sayabilir.

`score` = **bireysel** maç skoru (`rules.scoring == "player"` olan modlarda anlamlı; takım
skoru `match_state.scoreRed`/`scoreBlue`'da kalır — §10.5). Bireysel skorun değiştiği an =
öldürmenin olduğu an = roster'ın zaten tazelendiği an, bu yüzden ayrı bir mesaj tipi yoktur.

**`load_match`** `{ "type":"load_match", "modeId":"tdm", "sceneName":"Arena10x10", "roundSeconds":300, "scoreLimit":30, "yourTeam":"red", "rules":{ … } }`
→ istemci sahneyi yükler, `status`'ta yeni sahne görünür. Sahne yüklenince istemci `set_ready` (loading tamam anlamında) gönderir; herkes hazır olunca sunucu `countdown` başlatır.
**Oyuncu ışınlanmaz ve kalibrasyon SIFIRLANMAZ** — harita değişimi oyuncu için yalnız bir sahne değişimidir, fiziksel duruşu ve hizalaması kaldığı yerden devam eder (§10.4).
**Adminlere de gönderilir** (gözlemci sahneyi yüklesin diye) ama `yourTeam:""` ile — admin oynamadığı için takım anlamsızdır ve admin `set_ready` göndermez.
`rules` = bu maçın kural şekli (§10.5). İstemci kendini **buna** göre kurar: takımsız modda `yourTeam` boş gelir, canlanma şartı `reviveAnchor`'dan okunur. İstemcide `if (modeId == "...")` zinciri YOKTUR — mod eklemek istemci kodunu değiştirmez.

**`countdown`** `{ "type":"countdown", "seconds":5 }` — 0'a inince faz Live.
**`match_state`** — faz değişimlerinde + Live'da saniyede 1:
```json
{ "type":"match_state", "phase":"Live", "timeRemaining":287.5, "scoreRed":3, "scoreBlue":5 }
```
Fazlar: `Lobby → Loading → Countdown → Live → End → Lobby`.

**`shot_fired`** (relay) `{ "type":"shot_fired", "playerId":4, "weaponId":"ak47", "muzzlePos":[...], "muzzleDir":[...] }` — diğer istemciler uzak namlu alevi/ses oynatır (atan hariç herkese).
**`health_update`** `{ "type":"health_update", "playerId":5, "hp":75.0, "attackerId":3 }`
**`kill_event`** `{ "type":"kill_event", "killerId":3, "victimId":5, "weaponId":"ak47" }`
**`respawn`** `{ "type":"respawn", "playerId":5, "delaySeconds":5.0 }` — istemci `delaySeconds` sonra, modun canlanma şartını sağlayınca canlanır (§10.4). Sunucu sahne geometrisini bilmez; canlanma yeri diye bir alan taşınmaz.
**`match_end`** `{ "type":"match_end", "winnerTeam":"blue", "winnerPlayerId":0, "scoreRed":12, "scoreBlue":30 }`
Kazanan **iki kanaldan biriyle** ifade edilir (`rules.scoring`, §10.5): takım skorlu modlarda `winnerTeam` (`"red"|"blue"|""`), bireysel skorlu modlarda `winnerPlayerId` (`0` = yok/berabere). Bir mod ikisini de doldurmaz; okuyan istemci dolu olana bakar.
**`return_to_lobby`** `{ "type":"return_to_lobby" }` — herkes Lobby sahnesine döner.
**`ping`** `{ "type":"ping" }` — istemci `status` ile yanıtlar (ayrı pong yok).
**`identify`** `{ "type":"identify" }` — istemci büyük kimlik overlay'i gösterir (playerId + ad).
**`kicked`** `{ "type":"kicked", "reason":"" }` — istemci bağlantıyı kapatır, lobi bağlantı ekranına döner.

**`admin_state`** — **yalnız `role=admin` bağlantılara**; adminler arası ortak durumun tek doğruluk kaynağı:
```json
{ "type":"admin_state", "modeId":"tdm", "sceneName":"Arena10x10",
  "roundSeconds":600, "scoreLimit":30,
  "notice":"Ofis-PC: harita -> Arena10x10", "adminCount":2 }
```
- Gönderim anları: admin `hello` yanıtında (welcome'dan hemen sonra, geç katılan admin senkron başlasın), her `set_selection`'da, her admin komutunda (`start_match`/`abort_match`/`return_to_lobby`/`kick`/`identify`/`set_team`) ve admin bağlanıp ayrıldığında.
- `modeId`/`sceneName` = ortak seçim. Admin arayüzü **kendi yerel seçimini değil bunu gösterir**; gelen değer arayüzdeki mod/harita seçicisini ve yerel harita önizlemesini günceller. Yani bir operatör haritayı değiştirdiğinde diğerinin ekranı da (paneli açık olmasa bile) o haritaya döner.
- `roundSeconds`/`scoreLimit` = bir sonraki maçın ortak parametreleri (`0` = hiç seçilmedi, modun varsayılanı kullanılacak). Mod/harita ile aynı kanaldan gider — sebebi §5.2 `set_selection` notunda.
- `notice` = son admin eyleminin insan okuyabilir özeti (`"<admin adı>: <eylem>"`), tüm adminlerin durum satırında görünür. Boş olabilir.
- `adminCount` = o an çevrimiçi admin sayısı.
- **Yalnız operasyonel durum senkronlanır.** Görünüm tercihleri (kamera kipi, seçili oyuncu, halka/ad etiketi, kamera hızı, duvar/çatı saydamlığı, mini harita) her admin'in **kendi ekranına** aittir, protokole girmez ve `PlayerPrefs`'te yerel kalır.

## 6. UDP state mesajları (binary, little-endian)

### 6.1 Kayıt: `0x00 UdpHello` (istemci → sunucu, welcome'dan sonra)

```
[u8 0x00][u8 playerId][u32 udpToken]
```
Sunucu `playerId↔udpToken` eşleşirse istemcinin UDP endpoint'ini kaydeder ve aynı 6 baytı geri yollar (ack). İstemci ack gelene dek 1 sn arayla tekrarlar. Pozlar yalnız kayıtlı endpoint'ten kabul edilir (yanlış eşleşme koruması; güvenlik amaçlı değil — LAN).

### 6.2 `0x01 PoseUpdate` (istemci → sunucu, 20 Hz; yalnız player)

```
[u8 0x01][u8 playerId][u16 seq][u32 clientTimeMs]
[head : f32 px,py,pz, qx,qy,qz,qw]   (28 B)
[handL: aynı düzen]                   (28 B)
[handR: aynı düzen]                   (28 B)
Toplam: 8 + 84 = 92 B  → 20 Hz'de ~14.7 kbps/oyuncu
```
Pozlar **arena uzayında**. `seq` sarmalanır (u16); eski `seq` gelirse paket atılır (son gelen kazanır). v1'de quaternion sıkıştırma YOK (basitlik); v2 rezervi: smallest-three.

### 6.3 `0x02 Snapshot` (sunucu → tüm istemciler, 20 Hz)

```
[u8 0x02][u8 playerCount][u32 serverTick]
oyuncu başına: [u8 playerId][u8 flags][92B'lik PoseUpdate'in poz kısmı = 84 B]
```
`flags` bit0 = alive. İstemci kendi pozunu snapshot'tan ÇİZMEZ (yerelden çizer); uzak oyuncuları `INTERP_DELAY_MS` tamponuyla interpole eder. Admin'e de aynı snapshot gider (taktik görünüm bundan beslenir).

**Parçalama (MTU):** pozlu oyuncu sayısı `SNAPSHOT_MAX_ENTRIES_PER_PACKET`'i aşarsa sunucu aynı tik'i **birden çok datagrama böler**; her datagram kendi `playerCount`'unu taşır, hepsi aynı `serverTick`'i taşır ve aynı hedeflere yollanır. 16 girdi = 6 + 16×86 = **1382 B** (MTU 1500 altı). **İstemcide birleştirme mantığı YOKTUR ve gerekmez:** her paket taşıdığı girdileri bağımsız olarak uygular, oyuncu düşürme kararı "bu pakette yok" değil ~1.5 sn'lik zaman aşımıdır. Bu yüzden parçalama tel formatını değiştirmez — ek başlık alanı yoktur, eski okuyucu da doğru çalışır.

**İçerik kuralı:** snapshot'a yalnız *online* olup en az bir `PoseUpdate`'i alınmış `role=player` girişleri konur (admin hiç girmez — poz göndermez; ama UDP kaydı yaptığı için snapshot ALIR, ve birden çok admin varsa her biri ayrı hedeftir). Kopan oyuncu (WS kapanışı/OFFLINE_TIMEOUT) bir sonraki tikten itibaren düşer; `playerCount=0` snapshot yine yayınlanır (istemciler bayat avatarı böyle temizler). **Yayın hedefi:** UDP kaydı yapılmış tüm online endpoint'ler (admin dahil). **İstemci düşürme kuralı:** bir `playerId` snapshot'larda ~1.5 sn görünmezse uzak avatarı kaldırılır (paket kaybı toleransı; sunucunun 15 sn'lik OFFLINE_TIMEOUT'unu beklemez).

## 7. DTO tasarım kuralları

- **Paylaşılan kaynak:** tüm DTO'lar + `ArenaProtocol` sabitleri + binary yazıcı/okuyucular `Assets/_Shared/Net/Protocol/` altında **saf C#** (`UnityEngine`'e referans YASAK — asmdef `noEngineReferences:true`; server csproj aynı dosyaları `<Compile Include>` ile derler, Unity API kullanılırsa server derlemesi kırılır = otomatik bekçi).
- **JsonUtility kısıtları** (Unity tarafı bunları kullanır): Dictionary YOK, polimorfizm YOK, property değil **public alan**, sınıflar `[Serializable]`. Binary tarafında `BinaryWriter/BinaryReader` yerine elle offset'li `Span<byte>`/`BitConverter` KULLANMA tartışması yok — v1'de basit `BinaryWriter/BinaryReader` (little-endian garanti: `BinaryWriter` zaten LE).
- Unity DTO'larında `[UnityEngine.Scripting.Preserve]` KULLANILMAZ (saf C# dosyaları Unity attribute'u içeremez); IL2CPP stripping'e karşı **`Assets/link.xml`**'de `VortexArena.Protocol` ve `VortexArena.Net` assembly'leri preserve edilir.
- .NET sunucu JSON için `System.Text.Json` + `JsonSerializerOptions { IncludeFields = true }` (DTO'lar public ALAN olduğu için şart) kullanır; alan adları camelCase birebir aynı.

## 8. Bağlantı yaşam döngüsü

```
İstemci: aç → discovery (komut satırı --server-ip verildiyse onu kullan; yoksa elle girilmiş IP
         (PlayerPrefs); yoksa beacon dinle 5 sn; yoksa StreamingAssets/arena.json statik IP)
       → ws://ip:47821/ws bağlan → hello → welcome (playerId + udpToken + match durumu)
       → UDP kayıt (0x00, ack'e dek tekrar) → geç katılımsa sahne senkronu
       → StatusLoop (5 sn) + (player ise, Live/Lobby fark etmez) PoseLoop (20 Hz)
Kopma  → 1→2→5 sn backoff ile discovery'den itibaren baştan (sonsuz)
       → bağlantısızlık ~3 sn sürerse istemci hata ekranı gösterir (sunum; §4 notu)
Sunucu : hello'suz bağlantıyı 10 sn içinde kapat; deviceId çakışmasında eskisini kapat
       → 15 sn status yoksa Offline işaretle + bağlantıyı kapat + lobby_state yayınla
```

## 9. Güvenlik (v1)

LAN-içi, auth YOK. `hello`'ya ileride `token` alanı eklenebilir (rezerve). Sunucu yalnız özel ağda; Windows Firewall kuralları `Server/firewall-kur.cmd` ile (TCP 47821 + UDP 47820/47822).

## 10. Maç akışı + savaş kuralları (sunucu-otoriter)

Tüm kural otoritesi sunucudadır (`MatchDirector` + `Modes/<X>Mode.cs : IGameMode`). İstemci **sunum + girdi**dir: hasar uygulamaz, skor tutmaz, faz değiştirmez.

### 10.1 Faz makinesi

```
Lobby ──start_match──► Loading ──herkes set_ready | LOADING_TIMEOUT──► Countdown(5 sn)
   ▲                                                                       │
   └──── return_to_lobby ◄── End (MATCH_END_SECONDS) ◄── match sonu ◄──── Live
```

- **`start_match` doğrulaması** (sırayla): `modeId` sunucudaki `IGameMode` kayıtlarında var; `sceneName` boş değil; **`sceneName` `config/maps.json` harita tablosunda var ve o harita `modeId`'yi destekliyor** (harita girdisindeki `modes` boşsa kısıt yok; **tablo boşsa — maps.json yoksa — bu adım tümüyle atlanır**); `sceneName` tüm çevrimiçi oyuncuların `hello.scenes` listesinde var. Geçmezse komut reddedilir ve konsola sebep yazılır (faz değişmez). İki oyuncu+ varken takımlar dengelenir (boş takım kalmaz); tek oyuncuyla ve **hiç oyuncu yokken** başlatmaya izin verilir (konsolda uyarı) — ikincisi admin gözlemcinin haritayı boş arenada açması için vardır.
- **Oyuncusuz maç (yalnız admin):** `load_match` yalnız adminlere gider, Loading'de beklenecek `set_ready` olmadığı için faz doğrudan Countdown'a geçer ve maç normal işler (skor 0, süre akar). Ayrım şu: **oyuncularla başlamış** bir maçta Loading sırasında son oyuncu da düşerse sunucu maçı bırakıp Lobby'ye döner; oyuncusuz **başlatılmış** maçta dönmez — çıkış operatörün `abort_match`/`return_to_lobby` komutudur.
- **Mod/harita/parametre seçimi sunucuda yaşar (çoklu admin):** admin arayüzündeki seçiciler yerel bir değişkeni değil, `set_selection` ile sunucudaki ortak seçimi değiştirir; sunucu `admin_state` ile hepsine geri yayar. `start_match` kendi `modeId`/`sceneName`'i ile gelmeye devam eder (protokol yüzeyi genişledi ama kırılmadı) ama sunucu onu aynı zamanda ortak seçime yazar — böylece maç başladığında tüm admin panelleri aynı değeri gösterir. Seçim yalnız bir niyet beyanıdır: doğrulama `start_match` anında yapılır.
- **Maç parametreleri admin'den gelebilir:** `start_match.roundSeconds`/`scoreLimit` doluysa (`> 0`) o maç bu değerlerle koşar; boş/`0` ise modun varsayılanı (`IGameMode.DefaultRoundSeconds`/`DefaultScoreLimit`) kullanılır. Yani `ModeDefinition`/`IGameMode` üzerindeki sayılar **varsayılandır, kilit değil** — operatör raundu kısaltıp uzatabilir. Değer `load_match`/`match_state` üzerinden istemcilere zaten gidiyor, ek bir kanal doğmaz.
- **`load_match` kişiselleştirilir:** her oyuncuya kendi `yourTeam`'i gider; **takımsız modda** (`rules.teamMode == "none"`, §10.5) takım boş gider. Faz Loading'e geçerken tüm `ready` bayrakları sıfırlanır. **Çevrimiçi adminlere de bir kopya gider** (`yourTeam:""`) — admin gözlemci aynı sahneyi yükler.
- **Loading:** istemci sahneyi yükleyince `set_ready{ready:true}` gönderir ("sahne yüklendi" anlamında). Tüm çevrimiçi **oyuncular** hazır olunca (veya `LOADING_TIMEOUT` dolunca) Countdown başlar. Kapı yalnız `role=player` bağlantılarını sayar: admin sahneyi yüklese de `set_ready` göndermez, geri sayımı ne hızlandırır ne geciktirir.
- **Countdown:** saniyede bir `countdown{seconds}` (5→1); 0'da faz Live.
- **Live:** `match_state` 1 Hz; `timeRemaining` sunucuda azalır; `IGameMode.OnTick` çağrılır.
- **End:** `match_end` yayınlanır, `MATCH_END_SECONDS` sonra `return_to_lobby` + faz Lobby (skorlar/canlar sıfırlanır, oyuncular Lobby sahnesine döner).
- **`abort_match`** her fazda Lobby'ye düşürür (`return_to_lobby` yayınlanır); `return_to_lobby` doğrudan aynı işi yapar.

### 10.2 Oyuncu maç durumu (sunucuda)

Oyuncu başına: `hp` (0..`PLAYER_MAX_HP`), `alive`, `team`, `kills`, `deaths`, `score`, ölüm zamanı. Live'a girerken herkes `hp=PLAYER_MAX_HP`, `alive=1`. Snapshot'taki `SnapshotEntry.flags` bit0 (`FLAG_ALIVE`) bu `alive` alanından beslenir — Lobby fazında herkes canlı sayılır.

`score` = **bireysel maç skoru**. Yazarı yalnız `IGameMode`'dur (`MatchDirector`'ın skor defteri üzerinden); `kills` ile aynı şey DEĞİLDİR — bir mod öldürme başına 1, bir başkası objektif başına 5 yazabilir, Silah Yarışı'nda aynı alan "seviye" anlamına gelir. Maç kurulurken ve Lobby'ye dönerken 0'lanır.

`hp`/`alive`/`kills`/`deaths`/`score` **`lobby_state` ile de yayınlanır** (§5.3): ölüm işlendikten sonra roster bir kez tazelenir, böylece admin istatistik tablosu sunucudaki sayaçla birebir kalır ve admin yeniden bağlandığında geçmişi kaybetmez. Anlık akış (her vuruş) yine `health_update`/`kill_event` üzerinden gider — `lobby_state` sağlama noktasıdır, sıcak yol değil.

### 10.3 Vuruş hattı — genel hasar modeli

**Hile koruması yoktur ve bilinçli olarak eklenmez.** Ürün, gözetim altındaki özel alanlarda
(işletme kurulumu, turnuva) çalışır; hile yapmanın kimseye faydası olmadığı bir ortamda hile
denetimi yalnız meşru vuruşları yiyen bir tuzaktır. Bu yüzden hasar hesabı **tamamen istemcide**
yapılır, sunucu hakemlik değil **defter tutar**: canı düşürür, ölümü ilan eder, skoru işler.

`hit_report` şu sırayla kontrol edilir; **herhangi biri düşerse paket sessizce reddedilir**
(konsola tek satır log, istemciye yanıt yok). Bunlar hile denetimi değil, **durum tutarlılığı**
kontrolleridir — kaldırılırlarsa çift ölüm / maç dışı hasar gibi hatalar üretilir:

1. Faz `Live` mi?
2. Atıcı çevrimiçi + `role=player` + `alive` mi?
3. Hedef var, çevrimiçi, `alive` mi? (aynı karede gelen iki ölümcül vuruş çift `kill_event` yazmasın)
4. Hedef atıcının kendisi değil ve **takım arkadaşı değil** mi? Kural: `rules.friendlyFire == false` iken *takım arkadaşı* vurulamaz, ve **boş takım asla takım arkadaşı sayılmaz** — takımsız modda (§10.5 `teamMode:"none"`) herkesin takımı `""` olduğu için `"" == ""` karşılaştırması tüm vuruşları reddederdi. `friendlyFire == true` ise bu adım hiç uygulanmaz.
5. `damage` sonlu ve pozitif bir sayı mı? (NaN/∞ canı kalıcı bozar; sayı denetimi, hile denetimi değil)

Geçerse: `hp -= damage` (istemcinin bildirdiği değer) → `health_update{playerId, hp, attackerId}`
**herkese** yayınlanır. `hp ≤ 0` ise `alive=0`, `kill_event{killerId, victimId, weaponId}` +
`IGameMode.OnKill` (skor) + kurbana `respawn{delaySeconds:RESPAWN_DELAY}`.

Atış hızı denetimi, `weaponId` beyaz listesi ve sunucu-otoriter silah tablosu **YOKTUR**
(v1'de vardı, kaldırıldı): pompalı saçması, bomba parçası ve ok yaylımı gibi meşru "hızlı
art arda vuruş" örüntülerini sessizce düşürüyordu.

`shot_fired` sunucuda **doğrulanmaz**, yalnız relay edilir (atan hariç herkese, `playerId`
eklenerek) — ölü/maç dışı oyuncunun `shot_fired`'ı relay EDİLMEZ.

> **Denge sayıları istemcide yaşar.** Hasar/atış hızı/menzil tek kaynak olarak Unity'deki
> `WeaponDefinition` SO'larındadır; sunucuya export edilmez, `config/weapons.json` diye bir dosya
> yoktur. Bedeli bilinçlidir: denge değişikliği istemci build'i gerektirir. Karşılığında yeni bir
> silah/hasar kaynağı (balta, yay, bomba, tuzak, düşme hasarı) eklemek **sıfır sunucu işi**dir.

### 10.4 Free-roam respawn (canlanma)

Fiziksel oyuncu ışınlanamaz → **respawn = konum değil durum değişimi**:

1. Ölünce sunucu `respawn{playerId, delaySeconds}` gönderir (`delaySeconds` = `rules.respawnDelay`, §10.5); istemci ölüm ekranı gösterir, silah ateşlemez, avatar yarı saydam.
2. `delaySeconds` dolduktan **ve modun canlanma şartı sağlandıktan** sonra istemci `revive_request` gönderir (canlanana dek ~1 sn'de bir tekrarlar). Şart `rules.reviveAnchor` ile seçilir:
   - **`"base"`** (varsayılan, TDM): oyuncu bir **taban bölgesine** (`BaseZone` — arenadaki kırmızı/mavi şerit) fiziken girer. Ölüm ekranı "Tabanına dön ve canlan" der.
   - **`"standstill"`**: oyuncu ölüm anındaki HMD konumunu çapa alır ve `REVIVE_HOLD_RADIUS` içinde `REVIVE_HOLD_SECONDS` boyunca kesintisiz sabit durur; çapadan çıkınca sayaç ve çapa sıfırlanır. Taban bölgesi olmayan modlar (FFA) bunu kullanır.
3. Sunucu doğrular (faz Live, oyuncu ölü, gecikme dolmuş) → `hp=PLAYER_MAX_HP`, `alive=1` → `health_update{hp:100, attackerId:0}`.
4. Ölümden `REVIVE_GRACE` geçtiği hâlde talep gelmediyse sunucu **zorla** canlandırır (istemci takılmışsa maç kilitlenmesin).

> **`reviveAnchor` sunucuda DOĞRULANMAZ.** §10.3 felsefesinin aynısı: sunucu hakemlik değil defter tutar. "Tabanda mı / sabit mi durdu" kararı istemcinindir; sunucu faz + ölü + gecikme kontrolüyle yetinir. `REVIVE_GRACE` güvenlik ağı her iki şartta da aynen işler.

**Taban bölgesi eşleşmesi (istemci):** bir `BaseZone` oyuncuya açıktır eğer takımı oyuncunun takımıyla aynıysa, **ya da** bölge `Neutral` işaretliyse, **ya da** oyuncunun takımı boşsa (takımsız mod). Aynı takıma ait birden çok bölge varsa **herhangi birine** girmek yeter. Sahnede hiç açık bölge yoksa şart aranmaz — oyuncu kalıcı ölü kalmasın (güvenlik ağı yine `REVIVE_GRACE`).

**Konum diye bir alan protokolde YOKTUR.** Ne `load_match` ne `respawn` bir spawn noktası/slotu taşır; sunucu sahne geometrisini bilmez. Arena başına sahnedeki tek `SpawnPoint` marker'ı yalnız **yerleşim göstergesidir** (maç öncesi operatörün oyuncuyu yönlendirdiği fiziksel nokta) ve hiçbir kod tarafından okunmaz — rig'i taşıyan bir mekanizma yoktur.

**Harita değişimi kalibrasyonu sıfırlamaz.** `load_match` oyuncu için yalnız bir sahne değişimidir: kimse "yeniden doğmaz", rig taşınmaz. Yeni sahnenin `ArenaCalibrator`'ı `Start`'ta kayıtlı `OVRSpatialAnchor`'dan hizalamayı geri yükler, oyuncu fiziksel olarak nerede duruyorsa orada kalır. Hizalama geri gelene kadar `PlayerPoseTracker` poz göndermez (yanlış uzayda poz göndermektense kısa bir boşluk yeğdir).

### 10.5 Mod kuralları (`ModeRules` / `rules`)

Bir modun **ne tür bir oyun olduğunu** anlatan, **sunucu-otoriter** şekil tanımı. Her `IGameMode`
kendi `Rules`'ünü döner; sunucu bunu `load_match.rules` ve `welcome.match.rules` ile istemciye
yollar. Amaç tek: **istemci modun ne olduğunu TAHMİN ETMESİN.** Kural telden gelirse istemcide
`if (modeId == "ffa")` zinciri hiç doğmaz — yeni mod eklemek istemci kodunu değiştirmez.

```json
"rules": { "teamMode":"two", "scoring":"team", "friendlyFire":false,
           "reviveAnchor":"base", "weaponSource":"rack", "respawnDelay":5.0 }
```

| Alan | Değerler | Varsayılan | Anlamı |
|---|---|---|---|
| `teamMode` | `"two"` \| `"none"` | `"two"` | `"two"`: kırmızı/mavi, sunucu takımları dengeler, slot takım içi. `"none"`: takım yok (`team:""`), slot tek havuzdan |
| `scoring` | `"team"` \| `"player"` | `"team"` | Skor kime yazılır: `match_state.scoreRed/scoreBlue` mi, `lobby_state → PlayerInfo.score` mü (§10.2) |
| `friendlyFire` | `true` \| `false` | `false` | `false` = takım arkadaşı vurulamaz (§10.3/4). Boş takım asla takım arkadaşı sayılmaz |
| `reviveAnchor` | `"base"` \| `"standstill"` | `"base"` | Canlanma şartı (§10.4/2) |
| `weaponSource` | `"rack"` \| `"random"` | `"rack"` | Silah nereden gelir: sahnedeki raf mı, mod mu dağıtır. **Tümüyle istemci sunumu** — sunucuda karşılığı yok (§10.3: silah tablosu yoktur) |
| `respawnDelay` | saniye | `RESPAWN_DELAY` (5) | `respawn.delaySeconds` ve sunucudaki `revive_request` gecikme eşiği. **`0` geçerli bir değerdir** (anında canlanma) ve varsayılana çekilmez — alan hiç gönderilmezse DTO'nun kendi başlangıcı geçerli olduğu için "yazılmadı" ile "sıfır yazıldı" karışmaz |

- **Varsayılan = bugünkü TDM.** Bir mod hiçbir alan yazmazsa bugünkü davranışı alır; yani yeni mod
  yalnız *farklı* olduğu alanları belirtir.
- **Bilinmeyen/boş değer varsayılana düşer.** Değerler bilerek string: eski istemci yeni sunucudan
  tanımadığı bir `teamMode` görürse takımlı TDM gibi davranır, bağlantı kopmaz. Bu yüzden yeni bir
  kural değeri eklemek `PROTOCOL_VERSION`'ı **artırmaz**.
- **Kazanan ifadesi `scoring`'e bağlıdır:** `"team"` → `match_end.winnerTeam`, `"player"` →
  `match_end.winnerPlayerId`.
**Kayıtlı modlar** (sunucuda `MatchDirector.RegisterModes()`; `start_match.modeId` bunlardan biri
olmalı, tanınmayan `modeId` reddedilir):

| `modId` | Ad | `teamMode` | `scoring` | `friendlyFire` | `reviveAnchor` | `weaponSource` | `respawnDelay` | Varsayılan süre / limit |
|---|---|---|---|---|---|---|---|---|
| `tdm` | Takım Ölüm Maçı | `two` | `team` | `false` | `base` | `rack` | `5` | 300 sn / 30 |
| `ffa` | Herkes Tek | `none` | `player` | `false` | `standstill` | `random` | `0` | 300 sn / 20 |

> `ffa` satırı kuralların somut örneğidir: **takım yok** (`team:""` gelir, `winnerPlayerId`
> dolar), ölünce 5 sn'lik gecikme yerine **sabit durma** şartı işler (`REVIVE_HOLD_SECONDS` = 3 sn,
> `REVIVE_HOLD_RADIUS` = 1 m) ve silah sahnedeki raftan değil **istemcinin dağıtımından** gelir.
> `friendlyFire:false` FFA'yı kilitlemez — boş takım asla takım arkadaşı sayılmadığı için
> (§10.3/4) kapı hiç kapanmaz; `false` bırakılması "bu modda dost kavramı yok" demektir.
> **`weaponSource` sunucuyu hiç ilgilendirmez** (§10.3: silah tablosu yok) — telde yalnız
> istemciye "silahı nasıl vereceksin" diye taşınır.

- **3+ takım bugün YOK.** Geldiğinde yol açık: `PlayerInfo.team` zaten serbest string
  (`"green"`/`"yellow"` bugün de geçer) ve `match_state`'e `teamScores:[{team,score}]` eklenir;
  `scoreRed`/`scoreBlue` iki takımlı modlar için kısayol olarak kalır. Karar **o mod gelince**
  verilir — şimdi yapılırsa tüketicisi olmayan bir soyutlama için TDM ve admin arayüzü baştan yazılır.

**İstemcide tek okuma noktası:** `VortexArena.Core.ModeRuntime` (statik). `load_match`/`welcome`
onu besler; canlanma (`PlayerCombatState`), skor satırı (`ModeHudBase`) ve admin takım kipi
(`AdminRoster`) yalnız oradan okur. Dördü ayrı ayrı `load_match` dinlerse dördü ayrı ayrı bayatlar.
Sunucusuz editör oturumunda (dev penceresi sentetik maç) kurallar `ModeDefinition`'dan okunur;
**sapmada sunucu kazanır** — `ModeDefinition`'daki kural alanları yalnız önizleme/editör içindir.

## 11. Sunucu config dosyaları

`Server/config/` altındaki üç dosya; kaynakları FARKLIDIR:

| Dosya | Kaynağı | Not |
|---|---|---|
| `server.json` | **Elle** | Portlar + `venueName` + `tickHz`; yoksa varsayılanlarla oluşturulur (§1 sabitleri). |
| `devices.json` | **Sunucu üretir** | `deviceId → "Gözlük NN"`; ilk bağlantıda ve `set_name`'de yazılır (§2). UTF-8, BOM'suz. |
| `maps.json` | **Unity export** | `MapDefinition` SO'larından: `sceneName`, `sizeX`/`sizeZ`, `modes` (§10.1). |

> **`weapons.json` KALDIRILDI** (v1'de vardı): sunucu artık silah tanımı tutmaz, hasarı istemci
> bildirir (§10.3). Silah istatistikleri yalnız Unity'deki `WeaponDefinition` SO'larındadır.
>
> **`maps.json` ELLE DÜZENLENMEZ** — `Tools > VortexArena > Export Server Config` üretir ve bir sonraki export elle yapılan değişikliği **ezer**. Tek doğruluk kaynağı Unity SO'larıdır; çıktı deterministiktir (alfabetik, LF, UTF-8 BOM'suz) → git diff'i temiz kalır. Harita ekleyip export'u çalıştırmayı unutursanız bilinmeyen `sceneName` → `start_match` reddedilir. `maps.json` hiç yoksa sunucu harita doğrulamasını **atlar** (geriye dönük uyumlu davranış).
