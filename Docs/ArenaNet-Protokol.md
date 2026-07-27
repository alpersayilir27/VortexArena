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
| `MAX_PLAYERS` | `16` | Snapshot tek UDP paketine sığar (aşağıda hesap) |
| `PLAYER_MAX_HP` | `100` | Oyuncu tam canı (sunucu-otoriter; §10) |
| `COUNTDOWN_SECONDS` | `5` | Countdown fazının uzunluğu |
| `MATCH_END_SECONDS` | `10` | End fazı → otomatik `return_to_lobby` |
| `LOADING_TIMEOUT` | 20 sn | Loading'de tüm `set_ready` beklenmezse yine de Countdown'a geçilir |
| `RESPAWN_DELAY` | 5 sn | Ölüm → en erken canlanma (`respawn.delaySeconds`) |
| `REVIVE_GRACE` | 20 sn | `revive_request` gelmezse sunucu ölümden bu kadar sonra zorla canlandırır |
| `FIRE_RATE_TOLERANCE` | `0.8` | `hit_report` hız denetimi: iki vuruş arası ≥ `60/rpm × 0.8` sn |

## 2. Roller ve kimlik

- `role`: `"player"` (VR/Quest) veya `"admin"` (Windows masaüstü). Admin oynamaz; lobi rosterinde görünür, komut yetkisi vardır.
- **Admin sahne olarak oyuncuları takip eder:** `load_match` / `welcome.match` / `return_to_lobby` admin istemcisinde de sahne yükler (gözlemci görünümü). İki fark: admin `set_ready` **göndermez** (Loading kapısını yalnız `role=player` besler) ve poz **göndermez** (`0x01 PoseUpdate` yok), ama `0x00` ile UDP kaydı yapıp snapshot'ları alır.
- `deviceId` = `SystemInfo.deviceUniqueIdentifier` — kalıcı kimlik (sunucu `devices.json`'da ada eşler, "Gözlük NN" otomatik adlandırma).
- `playerId` = sunucunun `welcome`'da atadığı **1..MAX_PLAYERS** arası küçük tamsayı (UDP paketlerinde 1 bayt). Admin'e de atanır (poz göndermez).
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

**`hit_report`** — atıcının raycast'i bir oyuncuya değdiğinde:
```json
{ "type":"hit_report", "seq":124, "targetPlayerId":5, "weaponId":"ak47",
  "damage":25.0, "hitPos":[0.4,1.5,2.2] }
```
`hitPos` arena uzayında. Sunucu doğrular: hedef hayatta mı, atıcı hayatta mı, farklı takım mı, silahın atış hızına göre makul mü (rate-limit), `damage` `weapons.json`'daki değerle uyuşuyor mu (§10.3). Geçerse hasar uygular ve `health_update` yayınlar. **İstemci hasarı yerel uygulamaz** — `health_update` bekler.

**`revive_request`** `{ "type":"revive_request" }` — ölü oyuncu, `respawn.delaySeconds` dolduktan **ve** fiziksel olarak kendi taban bölgesine (`BaseZone`) girdikten sonra gönderir; sunucu koşulları doğrulayıp canlandırır (§10.4). Free-roam'da oyuncu ışınlanamadığı için canlanma bir **konum değişimi değil, durum değişimidir**.

### 5.2 Yalnız admin → Sunucu

- **`start_match`** `{ "type":"start_match", "modeId":"tdm", "sceneName":"Arena10x10" }`
- **`abort_match`** `{ "type":"abort_match" }`
- **`kick`** `{ "type":"kick", "playerId":5 }`
- **`identify`** `{ "type":"identify", "playerId":5 }` → o cihazda kimlik overlay'i (cosmos deseni)
- **`return_to_lobby`** `{ "type":"return_to_lobby" }`

Sunucu, `role != "admin"` bağlantıdan gelen admin komutunu loglayıp yok sayar.

### 5.3 Sunucu → İstemci

**`welcome`** — hello yanıtı:
```json
{ "type":"welcome", "protocolVersion":1, "playerId":3, "udpToken":123456789,
  "match": { "phase":"Lobby", "modeId":"", "sceneName":"", "timeRemaining":0,
             "scoreRed":0, "scoreBlue":0 } }
```
`match.phase` boş/`"Lobby"` değilse **geç katılım senkronu**: istemci `sceneName`'i yükleyip maça katılır.

**`lobby_state`** — roster her değiştiğinde **ve maç sayaçları değiştiğinde** (ölüm/canlanma) TAM anlık görüntü:
```json
{ "type":"lobby_state", "players":[
  { "playerId":3, "name":"Gözlük 03", "role":"player", "team":"red",
    "ready":true, "online":true, "battery":0.87, "scene":"Arena10x10",
    "kills":4, "deaths":2, "hp":72.0, "alive":true } ] }
```
`kills`/`deaths`/`hp`/`alive` **sunucu-otoriter** maç sayaçlarıdır (§10.2) ve admin gözlemci
arayüzünün tek doğruluk kaynağıdır: yalnız `kill_event`/`health_update` sayılsa admin yeniden
bağlandığında tablo sıfırlanırdı. Lobby fazında `hp=PLAYER_MAX_HP`, `alive=true`, sayaçlar 0.
Admin olmayan istemciler bu alanları yok sayabilir.

**`load_match`** `{ "type":"load_match", "modeId":"tdm", "sceneName":"Arena10x10", "roundSeconds":300, "scoreLimit":30, "yourTeam":"red", "spawnSlot":2 }`
→ istemci sahneyi yükler, kendi takım tarafındaki `spawnSlot` numaralı `SpawnPoint`'te başlar, `status`'ta yeni sahne görünür. Sahne yüklenince istemci `set_ready` (loading tamam anlamında) gönderir; herkes hazır olunca sunucu `countdown` başlatır.
**Adminlere de gönderilir** (gözlemci sahneyi yüklesin diye) ama `yourTeam:""` ve `spawnSlot:-1` ile — admin oynamadığı için takım/slot anlamsızdır ve admin `set_ready` göndermez.

**`countdown`** `{ "type":"countdown", "seconds":5 }` — 0'a inince faz Live.
**`match_state`** — faz değişimlerinde + Live'da saniyede 1:
```json
{ "type":"match_state", "phase":"Live", "timeRemaining":287.5, "scoreRed":3, "scoreBlue":5 }
```
Fazlar: `Lobby → Loading → Countdown → Live → End → Lobby`.

**`shot_fired`** (relay) `{ "type":"shot_fired", "playerId":4, "weaponId":"ak47", "muzzlePos":[...], "muzzleDir":[...] }` — diğer istemciler uzak namlu alevi/ses oynatır (atan hariç herkese).
**`health_update`** `{ "type":"health_update", "playerId":5, "hp":75.0, "attackerId":3 }`
**`kill_event`** `{ "type":"kill_event", "killerId":3, "victimId":5, "weaponId":"ak47" }`
**`respawn`** `{ "type":"respawn", "playerId":5, "spawnSlot":1, "delaySeconds":5.0 }` — istemci `delaySeconds` sonra kendi takım tarafındaki slotta canlanır (slot çözümü yerel `SpawnPoint` marker'larından; v1'de sunucuya harita dosyası gerekmez).
**`match_end`** `{ "type":"match_end", "winnerTeam":"blue", "scoreRed":12, "scoreBlue":30 }`
**`return_to_lobby`** `{ "type":"return_to_lobby" }` — herkes Lobby sahnesine döner.
**`ping`** `{ "type":"ping" }` — istemci `status` ile yanıtlar (ayrı pong yok).
**`identify`** `{ "type":"identify" }` — istemci büyük kimlik overlay'i gösterir (playerId + ad).
**`kicked`** `{ "type":"kicked", "reason":"" }` — istemci bağlantıyı kapatır, lobi bağlantı ekranına döner.

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
`flags` bit0 = alive. 16 oyuncu: 6 + 16×86 = **1382 B** → tek UDP paketi (MTU 1500 altı; MAX_PLAYERS=16 bu yüzden). İstemci kendi pozunu snapshot'tan ÇİZMEZ (yerelden çizer); uzak oyuncuları `INTERP_DELAY_MS` tamponuyla interpole eder. Admin'e de aynı snapshot gider (taktik görünüm bundan beslenir).

**İçerik kuralı:** snapshot'a yalnız *online* olup en az bir `PoseUpdate`'i alınmış `role=player` girişleri konur (admin hiç girmez — poz göndermez). Kopan oyuncu (WS kapanışı/OFFLINE_TIMEOUT) bir sonraki tikten itibaren düşer; `playerCount=0` snapshot yine yayınlanır (istemciler bayat avatarı böyle temizler). **Yayın hedefi:** UDP kaydı yapılmış tüm online endpoint'ler (admin dahil). **İstemci düşürme kuralı:** bir `playerId` snapshot'larda ~1.5 sn görünmezse uzak avatarı kaldırılır (paket kaybı toleransı; sunucunun 15 sn'lik OFFLINE_TIMEOUT'unu beklemez).

## 7. DTO tasarım kuralları

- **Paylaşılan kaynak:** tüm DTO'lar + `ArenaProtocol` sabitleri + binary yazıcı/okuyucular `Assets/_Shared/Net/Protocol/` altında **saf C#** (`UnityEngine`'e referans YASAK — asmdef `noEngineReferences:true`; server csproj aynı dosyaları `<Compile Include>` ile derler, Unity API kullanılırsa server derlemesi kırılır = otomatik bekçi).
- **JsonUtility kısıtları** (Unity tarafı bunları kullanır): Dictionary YOK, polimorfizm YOK, property değil **public alan**, sınıflar `[Serializable]`. Binary tarafında `BinaryWriter/BinaryReader` yerine elle offset'li `Span<byte>`/`BitConverter` KULLANMA tartışması yok — v1'de basit `BinaryWriter/BinaryReader` (little-endian garanti: `BinaryWriter` zaten LE).
- Unity DTO'larında `[UnityEngine.Scripting.Preserve]` KULLANILMAZ (saf C# dosyaları Unity attribute'u içeremez); IL2CPP stripping'e karşı **`Assets/link.xml`**'de `VortexArena.Protocol` ve `VortexArena.Net` assembly'leri preserve edilir (Faz 1'de eklenir).
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
- **`load_match` kişiselleştirilir:** her oyuncuya kendi `yourTeam` + `spawnSlot`'u (takım içi 0 tabanlı sıra) gider. Harita tablosunda `spawnSlotsPerTeam` biliniyorsa slot bu sayıya göre **modulo** alınır (sahnede olmayan slota atama yapılmaz; kalabalık takımda slotlar paylaşılır). Faz Loading'e geçerken tüm `ready` bayrakları sıfırlanır. **Çevrimiçi adminlere de bir kopya gider** (`yourTeam:""`, `spawnSlot:-1`) — admin gözlemci aynı sahneyi yükler.
- **Loading:** istemci sahneyi yükleyince `set_ready{ready:true}` gönderir ("sahne yüklendi" anlamında). Tüm çevrimiçi **oyuncular** hazır olunca (veya `LOADING_TIMEOUT` dolunca) Countdown başlar. Kapı yalnız `role=player` bağlantılarını sayar: admin sahneyi yüklese de `set_ready` göndermez, geri sayımı ne hızlandırır ne geciktirir.
- **Countdown:** saniyede bir `countdown{seconds}` (5→1); 0'da faz Live.
- **Live:** `match_state` 1 Hz; `timeRemaining` sunucuda azalır; `IGameMode.OnTick` çağrılır.
- **End:** `match_end` yayınlanır, `MATCH_END_SECONDS` sonra `return_to_lobby` + faz Lobby (skorlar/canlar sıfırlanır, oyuncular Lobby sahnesine döner).
- **`abort_match`** her fazda Lobby'ye düşürür (`return_to_lobby` yayınlanır); `return_to_lobby` doğrudan aynı işi yapar.

### 10.2 Oyuncu maç durumu (sunucuda)

Oyuncu başına: `hp` (0..`PLAYER_MAX_HP`), `alive`, `team`, `spawnSlot`, `kills`, `deaths`, son vuruş zamanı (rate-limit), ölüm zamanı. Live'a girerken herkes `hp=PLAYER_MAX_HP`, `alive=1`. Snapshot'taki `SnapshotEntry.flags` bit0 (`FLAG_ALIVE`) bu `alive` alanından beslenir — Lobby fazında herkes canlı sayılır.

`hp`/`alive`/`kills`/`deaths` **`lobby_state` ile de yayınlanır** (§5.3): ölüm işlendikten sonra roster bir kez tazelenir, böylece admin istatistik tablosu sunucudaki sayaçla birebir kalır ve admin yeniden bağlandığında geçmişi kaybetmez. Anlık akış (her vuruş) yine `health_update`/`kill_event` üzerinden gider — `lobby_state` sağlama noktasıdır, sıcak yol değil.

### 10.3 Vuruş hattı

`hit_report` şu sırayla doğrulanır; **herhangi biri düşerse paket sessizce reddedilir** (konsola tek satır log, istemciye yanıt yok):

1. Faz `Live` mi?
2. Atıcı çevrimiçi + `role=player` + `alive` mi?
3. Hedef var, çevrimiçi, `alive` mi?
4. Takımlar farklı mı? (aynı takım = dost ateşi YOK)
5. `weaponId` `config/weapons.json`'da var mı?
6. Atış hızı: aynı atıcının son kabul edilen vuruşundan bu yana ≥ `60/rpm × FIRE_RATE_TOLERANCE` sn geçmiş mi?
7. `damage`, `weapons.json`'daki değere eşit mi (±%1)? Değilse tablodaki değer kullanılır ve uyumsuzluk loglanır.

Geçerse: `hp -= damage` → `health_update{playerId, hp, attackerId}` **herkese** yayınlanır. `hp ≤ 0` ise `alive=0`, `kill_event{killerId, victimId, weaponId}` + `IGameMode.OnKill` (skor) + kurbana `respawn{spawnSlot, delaySeconds:RESPAWN_DELAY}`.

`shot_fired` sunucuda **doğrulanmaz**, yalnız relay edilir (atan hariç herkese, `playerId` eklenerek) — ölü/maç dışı oyuncunun `shot_fired`'ı relay EDİLMEZ.

> `config/weapons.json` (`{ "weapons":[{ "weaponId":"ak47","damage":34,"rpm":600 }, …] }`) **Unity'den üretilir** (`Tools > VortexArena > Export Server Config`, Faz 4) — tek doğruluk kaynağı `WeaponDefinition` SO'larıdır, dosya elle düzenlenmez (export ezer). Buna rağmen hasar **her zaman sunucu tablosundan** uygulanır: istemci farklı bildirirse uyumsuzluk loglanır ve tablo kazanır (export unutulduğunda sapmayı bu satır yakalar).

### 10.4 Free-roam respawn (canlanma)

Fiziksel oyuncu ışınlanamaz → **respawn = konum değil durum değişimi**:

1. Ölünce sunucu `respawn{playerId, spawnSlot, delaySeconds}` gönderir; istemci ölüm ekranı gösterir ("tabanına dön"), silah ateşlemez, avatar yarı saydam.
2. `delaySeconds` dolduktan **ve** oyuncu kendi `BaseZone`'una fiziken girdikten sonra istemci `revive_request` gönderir (canlanana dek ~1 sn'de bir tekrarlar).
3. Sunucu doğrular (faz Live, oyuncu ölü, gecikme dolmuş) → `hp=PLAYER_MAX_HP`, `alive=1` → `health_update{hp:100, attackerId:0}`.
4. Ölümden `REVIVE_GRACE` geçtiği hâlde talep gelmediyse sunucu **zorla** canlandırır (istemci takılmışsa maç kilitlenmesin).

`spawnSlot` yalnızca "hangi tabana/slota gideceğin" göstergesidir; slot çözümü istemcide sahnedeki `SpawnPoint(team, slot)` marker'larından yapılır — sunucu sahne geometrisini bilmez, `maps.json`'dan yalnızca `spawnSlotsPerTeam`'i okuyup slot numarasını geçerli aralığa sarar.

## 11. Sunucu config dosyaları

`Server/config/` altındaki dört dosya; kaynakları FARKLIDIR:

| Dosya | Kaynağı | Not |
|---|---|---|
| `server.json` | **Elle** | Portlar + `venueName` + `tickHz`; yoksa varsayılanlarla oluşturulur (§1 sabitleri). |
| `devices.json` | **Sunucu üretir** | `deviceId → "Gözlük NN"`; ilk bağlantıda ve `set_name`'de yazılır (§2). UTF-8, BOM'suz. |
| `weapons.json` | **Unity export** | `WeaponDefinition` SO'larından (§10.3). |
| `maps.json` | **Unity export** | `MapDefinition` SO'larından: `sceneName`, `sizeX`/`sizeZ`, `spawnSlotsPerTeam`, `modes` (§10.1). |

> **`weapons.json` ve `maps.json` ELLE DÜZENLENMEZ** — `Tools > VortexArena > Export Server Config` üretir ve bir sonraki export elle yapılan değişikliği **ezer**. Tek doğruluk kaynağı Unity SO'larıdır; çıktı deterministiktir (alfabetik, LF, UTF-8 BOM'suz) → git diff'i temiz kalır. Silah/harita ekleyip export'u çalıştırmayı unutursanız: bilinmeyen `weaponId` → `hit_report` reddedilir, bilinmeyen `sceneName` → `start_match` reddedilir. `maps.json` hiç yoksa sunucu harita doğrulamasını **atlar** (geriye dönük uyumlu davranış), `weapons.json` yoksa varsayılan v1 silah tablosuyla oluşturulur.
