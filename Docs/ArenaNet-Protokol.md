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
| `DISCOVERY_TIMEOUT` | 5 sn | Beacon gelmezse statik IP fallback (`StreamingAssets/arena.json` → elle girilen IP her zaman öncelikli) |
| `STATUS_INTERVAL` | 5 sn | İstemci status kalp atışı |
| `OFFLINE_TIMEOUT` | 15 sn | Status gelmezse cihaz çevrimdışı sayılır, bağlantı kapatılır |
| `RECONNECT_BACKOFF` | 1 → 2 → 5 sn (tavan 5) | Kopunca sonsuz yeniden deneme; her denemede discovery baştan |
| `POSE_RATE_HZ` | `20` | İstemci poz gönderim frekansı |
| `SNAPSHOT_RATE_HZ` | `20` | Sunucu snapshot yayın frekansı |
| `INTERP_DELAY_MS` | `100` | Uzak avatar interpolasyon tamponu |
| `MAX_PLAYERS` | `16` | Snapshot tek UDP paketine sığar (aşağıda hesap) |

## 2. Roller ve kimlik

- `role`: `"player"` (VR/Quest) veya `"admin"` (Windows masaüstü). Admin oynamaz; lobi rosterinde görünür, komut yetkisi vardır.
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

İstemci `app == "VortexArena"` doğrular. Beacon **yalnızca otomatik doldurma kolaylığıdır** — kullanıcı akışı gereği lobide elle IP:port girişi her zaman mümkündür ve elle girilen değer beacon'ı ezer (PlayerPrefs'e kalıcı yazılır). Android'de beacon dinlemek için **MulticastLock** gerekir (cosmos `ServerLocator.cs` çözümü port edilir).

## 5. WS kontrol mesajları (JSON, text)

**Zarf kuralı:** her mesajda `"type"` alanı. Alıcı önce yalnız `{"type":"..."}` parse eder, sonra tipe göre tam DTO'ya parse eder. **Bilinmeyen type → logla ve yok say** (ileri sürüm uyumluluğu).

### 5.1 İstemci → Sunucu

**`hello`** — bağlantı açılır açılmaz, bir kez:
```json
{ "type": "hello", "protocolVersion": 1, "role": "player",
  "deviceId": "...", "deviceName": "...", "appVersion": "0.1.0",
  "currentScene": "Lobby", "scenes": ["Boot","Lobby","AdminConsole","Arena10x10"] }
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

**`hit_report`** — atıcının raycast'i bir oyuncuya değdiğinde:
```json
{ "type":"hit_report", "seq":124, "targetPlayerId":5, "weaponId":"ak47",
  "damage":25.0, "hitPos":[0.4,1.5,2.2] }
```
Sunucu doğrular: hedef hayatta mı, atıcı hayatta mı, farklı takım mı, silahın atış hızına göre makul mü (rate-limit). Geçerse hasar uygular ve `health_update` yayınlar. **İstemci hasarı yerel uygulamaz** — `health_update` bekler.

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

**`lobby_state`** — roster her değiştiğinde TAM anlık görüntü:
```json
{ "type":"lobby_state", "players":[
  { "playerId":3, "name":"Gözlük 03", "role":"player", "team":"red",
    "ready":true, "online":true, "battery":0.87, "scene":"Lobby" } ] }
```

**`load_match`** `{ "type":"load_match", "modeId":"tdm", "sceneName":"Arena10x10", "roundSeconds":300, "scoreLimit":30, "yourTeam":"red", "spawnSlot":2 }`
→ istemci sahneyi yükler, kendi takım tarafındaki `spawnSlot` numaralı `SpawnPoint`'te başlar, `status`'ta yeni sahne görünür. Sahne yüklenince istemci `set_ready` (loading tamam anlamında) gönderir; herkes hazır olunca sunucu `countdown` başlatır.

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
İstemci: aç → discovery (elle girilmiş IP varsa onu kullan; yoksa beacon dinle 5 sn;
         yoksa StreamingAssets/arena.json statik IP)
       → ws://ip:47821/ws bağlan → hello → welcome (playerId + udpToken + match durumu)
       → UDP kayıt (0x00, ack'e dek tekrar) → geç katılımsa sahne senkronu
       → StatusLoop (5 sn) + (player ise, Live/Lobby fark etmez) PoseLoop (20 Hz)
Kopma  → 1→2→5 sn backoff ile discovery'den itibaren baştan (sonsuz)
Sunucu : hello'suz bağlantıyı 10 sn içinde kapat; deviceId çakışmasında eskisini kapat
       → 15 sn status yoksa Offline işaretle + bağlantıyı kapat + lobby_state yayınla
```

## 9. Güvenlik (v1)

LAN-içi, auth YOK. `hello`'ya ileride `token` alanı eklenebilir (rezerve). Sunucu yalnız özel ağda; Windows Firewall kuralları `Server/firewall-kur.cmd` ile (TCP 47821 + UDP 47820/47822).
