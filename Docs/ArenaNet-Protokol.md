# ArenaNet Protokol Referansı (v1) — TEK DOĞRULUK KAYNAĞI

> Unity `VortexArena.Protocol` asmdef'i ile .NET sunucu aynı C# kaynaklarını derler (yapısal sapma imkânsız); bu doküman **semantiğin** tek doğruluk kaynağıdır. İki taraftan biri davranış değiştirecekse ÖNCE burası güncellenir.

## 1. Sabitler

Tümü paylaşılan `ArenaProtocol` statik sınıfında tanımlanır (`Assets/_Shared/Net/Protocol/ArenaProtocol.cs`).

| Sabit | Değer | Açıklama |
|---|---|---|
| `PROTOCOL_VERSION` | `3` | hello/welcome'da taşınır; uyumsuzlukta log uyarısı (bağlantı kesilmez). **v3:** faz makinesi `paused`/`playing`/`finished`'a indi, `phaseReason` + `modeState` eklendi, lobi faz olmaktan çıkıp **tür** oldu, `set_team` yalnız admin (§10.1). **v2:** `set_name` kaldırıldı (→ `set_identity`), `lobby_state.version` + `status.rosterVersion` + `PlayerInfo.number` eklendi |
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
| `PLAYER_NUMBER_MIN` / `PLAYER_NUMBER_MAX` | `1` / `99` | Forma numarası aralığı (§2). `0` = atanmamış ve aralığın dışındadır. Numara **tüm kayıtlı cihazlar** arasında benzersizdir |
| `SNAPSHOT_MAX_ENTRIES_PER_PACKET` | `16` | Tek snapshot datagramına yazılan en fazla oyuncu; fazlası ek pakete taşar (§6.3). 6 + 16×86 = 1382 B < MTU |
| `PLAYER_MAX_HP` | `100` | Oyuncu tam canı (sunucu-otoriter; §10) |
| `COUNTDOWN_SECONDS` | `5` | Geri sayımın uzunluğu (`phaseReason:"countdown"`) |
| `MATCH_END_SECONDS` | `10` | `finished` → otomatik `return_to_lobby` |
| `LOADING_TIMEOUT` | 20 sn | Yükleme kapısında (`phaseReason:"loading"`) tüm `set_ready` beklenmezse yine de geri sayıma geçilir |
| `RESPAWN_DELAY` | 5 sn | Ölüm → en erken canlanma (`respawn.delaySeconds`) **varsayılanı**; mod `rules.respawnDelay` ile ezebilir (§10.5) |
| `REVIVE_GRACE` | 20 sn | `revive_request` gelmezse sunucu ölümden bu kadar sonra zorla canlandırır |
| `REVIVE_HOLD_SECONDS` | 3 sn | `reviveAnchor:"standstill"` (§10.5): ölü oyuncunun canlanmak için kesintisiz sabit durması gereken süre |
| `REVIVE_HOLD_RADIUS` | 1 m | `reviveAnchor:"standstill"`: ölüm anındaki çapadan bu yarıçapı aşan hareket sayacı sıfırlar |
| `ROUND_SECONDS_OPTIONS` | `150, 300, 600, 900, 1200, 1800, 3600` | Admin arayüzünün maç süresi seçenekleri (2.5 · 5 · 10 · 15 · 20 · 30 dk · 1 saat). **Protokol kısıtı değil, arayüz listesidir** — sunucu `start_match.roundSeconds`'ta her pozitif değeri kabul eder |

## 2. Roller ve kimlik

- `role`: `"player"` (VR/Quest) veya `"admin"` (Windows masaüstü). Admin oynamaz; lobi rosterinde görünür, komut yetkisi vardır.
- **Admin sahne olarak oyuncuları takip eder:** `load_match` / `welcome.match` / `return_to_lobby` admin istemcisinde de sahne yükler (gözlemci görünümü). İki fark: admin `set_ready` **göndermez** (Loading kapısını yalnız `role=player` besler) ve poz **göndermez** (`0x01 PoseUpdate` yok), ama `0x00` ile UDP kaydı yapıp snapshot'ları alır.
- `deviceId` — **role göre iki ayrı semantik:**
  - `player`: `SystemInfo.deviceUniqueIdentifier`, **kalıcı** kimlik. Sunucu `devices.json`'da **ad + numara** çiftine eşler (ikisi de otomatik atanır, aşağıda), kayıt bağlantı kopsa da durur (aynı gözlük geri gelince adı/numarası/kimliği korunur).
  - `admin`: `<deviceUniqueIdentifier>:admin:<oturum GUID'i>` — **oturum başına benzersiz**. Sebep: aynı fiziksel PC'de iki admin penceresi açılabilsin. Ortak deviceId ile ikisi aynı kaydı paylaşır, her `hello` diğerinin soketini kapatır ve sonsuz kick döngüsü olurdu. GUID süreç ömrü boyunca sabittir (yeniden bağlanma aynı kaydı bulur), uygulama kapanınca ölür.
- **Admin kayıtları kalıcı DEĞİLDİR:** admin bağlantısı koptuğunda (veya `OFFLINE_TIMEOUT` dolduğunda) kaydı registry'den **tümüyle silinir** ve `playerId`'si havuza döner; adı `devices.json`'a **yazılmaz**. Oyuncu kayıtları eskisi gibi çevrimdışı işaretlenir ama durur. Böylece admin'i her açıp kapatma roster'da hayalet satır bırakmaz.
- **Admin sayısı sınırsız ve hepsi eş yetkilidir.** Birincil/ikincil admin kavramı yoktur: `role=="admin"` olan her bağlantı §5.2'deki tüm komutları gönderebilir, son gelen komut uygulanır. Operatörlerin birbirini ezmemesi için ortak seçim `admin_state` ile senkronlanır (§5.3) ve her komut `admin_state.notice` ile diğerlerine duyurulur.
- `playerId` = sunucunun `welcome`'da atadığı **1..`PLAYER_ID_MAX`** arası küçük tamsayı (UDP paketlerinde 1 bayt). Admin'e de atanır (poz göndermez). Havuz dolarsa `kicked{reason:"Sunucu dolu"}` ile reddedilir — bu bir ürün kotası değil, `u8` tel formatının tavanıdır.
- **Ad ve numara = CİHAZ kimliğidir, oturum kimliği değil.** İkisi de oyuncunun ilk bağlantısında otomatik atanır ve `devices.json`'a `deviceId` başına kalıcı yazılır; admin `set_identity` ile ikisini de değiştirebilir (§5.1). Roster'da `name` + `number` olarak taşınır (§5.3).
  - **Ad:** 20 kişilik havuzdan **rastgele** — `umut, alper, ertu, yunus, resul, enver, enes, nisa, ceren, tuğba, elif, pınar, taner, yasemin, hüseyin, deniz, selin, kaan, burcu, emre`. Henüz hiçbir kayıtlı cihazın kullanmadığı adlar arasından seçilir; hepsi kullanımdaysa havuzun tamamından. **Adlar benzersiz DEĞİLDİR** (21. cihazdan sonra tekrar eder) — ayırt edici alan numaradır.
  - **Numara:** `1..99`, **1'den itibaren ilk boş** değer (sıralı, rastgele değil). `0` = atanmamış. **Değişmez kural: `devices.json` içinde iki cihaz aynı numarayı ASLA taşımaz** — benzersizlik çevrimiçilerle sınırlı değil, **tüm kayıtlı cihazlar** arasında geçerlidir, böylece bir gözlük numarasını kalıcı korur.
  - Admin bir numarayı **çevrimiçi** bir oyuncudan isterse **reddedilir** (`admin_state.notice` ile bildirilir). **Çevrimdışı kayıtlı** bir cihazdan isterse kabul edilir ve o cihaz **aynı anda** 1'den itibaren ilk boş numaraya taşınıp diske yazılır. ⚠️ Çevrimdışına karşı da reddetmek operatörü kilitlerdi (numarayı tutan cihaz roster'da görünmez, serbest bırakılamaz); yeniden numaralamayı sonraki bağlantıya ertelemek ise dosyayı o süre boyunca çift numaralı bırakırdı.
  - `1..99` havuzu dolarsa (100+ kayıtlı cihaz) yeni cihaz `0` alır + konsol logu; operatör elle numaralar. 16 gözlüklü bir işletme bunu görmez.
  - **Admin'e ad atanır ama numara ATANMAZ** (`number:0`): admin oynamaz. Admin adı havuzdan değil `hello.deviceName`'den (PC adı) gelir ve diske yazılmaz — admin `deviceId`'si oturumluktur.
- Aynı `deviceId` ikinci kez bağlanırsa eski bağlantı kapatılır, yenisi kabul edilir (cihaz yeniden bağlanmıştır).

## 3. Koordinat çerçevesi — ARENA UZAYI

Tüm ağ pozları **arena-yerel uzaydadır**: origin = sahnedeki tek `SpawnPoint` marker'ının transformu (zemin seviyesinde, eksenler arena duvarlarına hizalı olacak şekilde yerleştirilir). Her Quest, `ArenaCalibrator` (2-nokta + OVRSpatialAnchor) ile fiziksel alana hizalandığı için bütün cihazlar aynı fiziksel çerçeveyi paylaşır. Dönüşüm istemcide yapılır (rig-world → arena-local); sunucu ve admin görünümü ham arena koordinatı kullanır.

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
| `admin` (Windows) | **Yalnız komut satırı:** `--server-ip <ip> [--server-port <port>]` — operatör launcher'ı geçer. Beacon/PlayerPrefs kullanılmaz, kullanıcıya IP sorulmaz. Argüman yoksa bağlanmaz ve ekranda sebebini yazar. |

> **Zincir rolden bağımsızdır:** `AppBoot` komut satırı adresini **her rolde** okur; verilmişse keşfin en üstünde yer alır (açıkça verilen adres kazanır, gelen beacon onu ezmez). **Editörde** rol ve adres komut satırı yerine `Tools > VortexArena > Dev` penceresinden gelir (`EditorPrefs` — sahnede rol/IP override alanı YOKTUR). Maç verisi (mod / takım / süre / limit) **yalnız sunucudan** gelir: editörün enjekte ettiği bir yol yoktur.

> **Bağlantı kurulamazsa:** istemci bağlantısızlık ~3 sn sürdüğünde tasarımlı bir hata ekranı gösterir (`ConnectionOverlay`, VR + masaüstü): adres biliniyorsa "SUNUCUYA BAĞLANILAMIYOR" + adres + deneme sayacı + son hata, adres hiç yoksa "SUNUCU BULUNAMADI". Sunum katmanıdır, protokolü etkilemez; yeniden deneme kuralı `RECONNECT_BACKOFF`'tur (§1).

## 5. WS kontrol mesajları (JSON, text)

**Zarf kuralı:** her mesajda `"type"` alanı. Alıcı önce yalnız `{"type":"..."}` parse eder, sonra tipe göre tam DTO'ya parse eder. **Bilinmeyen type → logla ve yok say** (ileri sürüm uyumluluğu).

### 5.1 İstemci → Sunucu

**`hello`** — bağlantı açılır açılmaz, bir kez:
```json
{ "type": "hello", "protocolVersion": 1, "role": "player",
  "deviceId": "...", "deviceName": "...", "appVersion": "0.1.0",
  "currentScene": "Lobby", "scenes": ["Boot","Lobby","Arena12x12","IceWorld"] }
```
`scenes` = build listesinden runtime'da toplanır (`SceneUtility.GetScenePathByBuildIndex`) → admin katalog doğrulaması bunu kullanır.

**`status`** — her 5 sn: `{ "type":"status", "scene":"Arena12x12", "battery":0.87, "fps":71.6, "rosterVersion":42 }`

`rosterVersion` = istemcinin **uyguladığı son** `lobby_state.version`'ı (§5.3). Sunucu istemci geride
kalmışsa — ve **yalnız o bağlantıya** — tam bir `lobby_state` yollar; güncelse hiçbir şey yapmaz.
Bu bir **yedek uzlaştırma ağıdır, birincil yol değil**: kontrol kanalı WS/TCP olduğu için bir
`lobby_state` "ping yüzünden düşmez", ya sırayla teslim edilir ya bağlantı ölür. Alan, istemcinin bir
yayını uygulayamadığı pencereleri (sahne geçişi, kopma anı) kapatır. Alanı hiç göndermeyen istemci
`0` yollar → sunucu tam snapshot ile yanıtlar.

**`set_ready`** `{ "type":"set_ready", "ready":true }` (yalnız player)
**`set_team`** — **yalnız admin** (§5.2'ye taşındı). Oyuncu kendi takımını seçemez.

**`set_identity`** `{ "type":"set_identity", "playerId":5, "name":"ertu", "number":7 }` — oyuncunun
**adı ve/veya numarası** (§2). Boş string ya da `0` bırakılan alan **mevcut değerini korur**
(`set_selection` ile aynı konvansiyon) → "yalnız numarayı değiştir" tek mesajdır. Yetki: oyuncu
yalnız KENDİ `playerId`'si için, admin herkes için (`playerId:0` = "kendim"). Numara `1..99`
dışındaysa veya **çevrimiçi** bir oyuncuda kullanılıyorsa reddedilir (§2); değer `devices.json`'a
kalıcı yazılır.
> ⚠️ v1'deki **`set_name` KALDIRILDI** — ad ve numara tek kapıdan yönetilir (`PROTOCOL_VERSION` 2).

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

**`set_calibration`** `{ "type":"set_calibration", "calibrated":true, "source":"manual" }` (yalnız player) — başlık **kendi** hizalama durumunu bildirir (§10.6). `source` ∈ `"manual"` (kumandada A+B) · `"anchor"` (kayıtlı `OVRSpatialAnchor`'dan geri yükleme) · `"cloud"` (ileride: paylaşılan uzamsal anchor). **`source` doğrulanmaz**, yalnız kaydedilip roster'da yayılır — `weaponId` gibi serbest etikettir, yeni bir kaynak eklemek sunucuda iş çıkarmaz. `calibrated:false` de gönderilebilir (başlık kendi hizalamasını geçersiz kıldıysa).

### 5.2 Yalnız admin → Sunucu

- **`start_match`** `{ "type":"start_match", "modeId":"tdm", "sceneName":"Arena12x12", "roundSeconds":600, "scoreLimit":30 }`
  `roundSeconds`/`scoreLimit` **o maça özeldir**: `≤ 0` ya da eksikse modun kendi varsayılanı (`IGameMode.DefaultRoundSeconds`/`DefaultScoreLimit`) kullanılır. Operatörün arayüzde seçtiği süre/limit buradan geçer; `ROUND_SECONDS_OPTIONS` yalnız arayüz listesidir, sunucu her pozitif değeri kabul eder.
- **`abort_match`** `{ "type":"abort_match" }`
- **`pause_match`** `{ "type":"pause_match" }` — koşan maçı dondurur: `playing` → `paused` + `phaseReason:"operator"` (§10.1). Süre durur, hasar kapanır, skorlar ve `modeState` **korunur**. **Yalnız `playing` iken iş yapar**; başka fazda loglanıp yok sayılır (duraklı bir maçı duraklatmanın anlamı yok).
- **`resume_match`** `{ "type":"resume_match" }` — `paused`/`operator`'dan `playing`'e döner; süre kaldığı yerden akar, canlar/skorlar sıfırlanmaz. ⚠️ **Yalnız operatörün duraklattığı maç sürdürülebilir:** `phaseReason` `loading`/`countdown`/`mode`/`lobby` iken reddedilir. Sebep: o duraklamaların sahibi operatör değildir — modun istediği duraklamayı (`mode`) operatörün kaldırması modun ara durumunu bozar, geri sayımı elle bitirmek de yükleme kapısını atlar. Her duraklamayı kendi sahibi kaldırır.
- **`set_team`** `{ "type":"set_team", "playerId":5, "team":"blue" }` (`"red"|"blue"`) — hedef oyuncunun takımı. **Faz kapısı YOKTUR:** operatör `playing` dahil her fazda, sunucuya bağlı herkesin takımını değiştirebilir; değişiklik `lobby_state` ile yayılır ve istemcide anında geçerlidir (taban bölgesi, arayüz renkleri). Hedef admin ise reddedilir. Oyuncudan gelen `set_team` loglanıp yok sayılır — **oyuncu kendi takımını seçemez, bunun için protokol mesajı YOKTUR ve eklenmeyecektir.**
- **`kick`** `{ "type":"kick", "playerId":5 }`
- **`identify`** `{ "type":"identify", "playerId":5 }` → o cihazda kimlik overlay'i (cosmos deseni)
- **`clear_calibration`** `{ "type":"clear_calibration", "playerId":5 }` — o oyuncunun kalibrasyonunu **sıfırlar** (§10.6). **`playerId:0` = TÜM oyuncular** (toplu sıfırlama). Admin kalibrasyonu yalnız SIFIRLAYABİLİR, "kalibre oldu" diye işaretleyemez — hizalamanın gerçekten oturduğunu yalnız başlık bilir (§10.6).
- **`return_to_lobby`** `{ "type":"return_to_lobby" }`
- **`set_selection`** `{ "type":"set_selection", "modeId":"tdm", "sceneName":"Arena12x12", "roundSeconds":600, "scoreLimit":30 }` — bir sonraki maçın **ortak** mod/harita/süre/limit seçimi. Maçı BAŞLATMAZ; yalnız sunucudaki seçimi günceller ve sunucu bunu `admin_state` ile tüm adminlere yayar (çoklu admin senkronu, §5.3). Boş string veya `0` bırakılan alan mevcut değerini korur. Seçim maç bitiminde sıfırlanmaz — operatör aynı haritayı tekrar başlatabilsin.
  ⚠️ **`sceneName` yalnız operatör harita/mod imlecini gerçekten oynattığında doldurulur** (süre/limit dokunuşunda boş gider): dolu harita alanı sahnelemeyi tetikler (§10.7), yani süre değiştirmek herkesi bir arenaya taşırdı.
  **Neden maç parametreleri de ortak:** iki operatör aynı ekranı görmezse biri 5 dk sandığı maçı 30 dk başlatır. Süre/limit *operasyonel* durumdur, görünüm tercihi değil (§5.3 son madde).
  ⚠️ **`sceneName` yalnız bir not değil, anlık bir sahne komutudur:** harita değiştiğinde sunucu o arenayı **sahneler** — TÜM istemciler (oyuncular + adminler) oraya geçer (§10.7). Bu yüzden **`modeId`/`sceneName` yalnız faz `playing` DEĞİLKEN kabul edilir** (yani `paused` ve `finished`'da serbest); `playing` iken ikisi de **düşürülür** (komutun süre/limit kısmı yine işlenir), konsola sebep yazılır ve `admin_state` mevcut seçimle geri yayınlanır — iyimser davranan panelin imleci sunucunun değerine çekilsin. Koşan maçın ortasında harita değişimi diye bir şey yoktur; yeni harita `start_match` ile gelir.

Sunucu, `role != "admin"` bağlantıdan gelen admin komutunu loglayıp yok sayar.

### 5.3 Sunucu → İstemci

**`welcome`** — hello yanıtı:
```json
{ "type":"welcome", "protocolVersion":3, "playerId":3, "udpToken":123456789,
  "match": { "phase":"paused", "phaseReason":"lobby", "modeId":"lobby", "modeState":"",
             "sceneName":"Lobby12x12", "timeRemaining":0, "scoreRed":0, "scoreBlue":0,
             "rules": { "teamMode":"two", "scoring":"team", "friendlyFire":false,
                        "reviveAnchor":"base", "weaponSource":"rack", "respawnDelay":5.0,
                        "fireWhilePaused":true } } }
```
**`match.sceneName` HER ZAMAN doludur ve istemcinin tek yönlendirme kaynağıdır** (§10.1): bağlanan
istemci koşulsuz o sahneyi yükler. Sunucu açık sahnesini çözemiyorsa zaten **açılmaz** (§11) — boş
`sceneName` yalnız bozuk/eski bir sunucudan gelebilir, o durumda istemci kabuk `Lobby` sahnesinde
bekler ve sebebi konsola yazar.

`match.rules` = o an geçerli kural şekli (§10.5) — geç katılan istemci/admin kendini aynı kurallara
göre kurar. `phase`/`phaseReason`/`modeState` anlamları §10.1'de.

**`lobby_state`** — roster her değiştiğinde **ve maç sayaçları değiştiğinde** (ölüm/canlanma) TAM anlık görüntü:
```json
{ "type":"lobby_state", "version":42, "players":[
  { "playerId":3, "number":7, "name":"ertu", "role":"player", "team":"red",
    "ready":true, "online":true, "battery":0.87, "scene":"Arena12x12",
    "kills":4, "deaths":2, "hp":72.0, "alive":true, "score":7,
    "calibrated":true, "calibrationSource":"anchor" } ] }
```

`version` = **monoton artan** roster sürümü (sunucu ömrü boyunca; sunucu yeniden başlarsa `0`'dan).
İstemci `version <= uyguladığı son sürüm` olan mesajı **atar** ve sürümü her `welcome`'da sıfırlar.
Sunucuda yayın **tek bir yayıncı** üzerinden gittiği için sıra zaten korunur; bu guard ikinci
emniyettir. ⚠️ Gerekçesi ucuz bir "her ihtimale karşı" değil: sürümsüz ve ateşle-unut yayında eski
bir anlık görüntü yeniyi ezebilir ve roster bir sonraki değişikliğe kadar bayat kalır — belirtisi
**"atılan oyuncu hâlâ listede online görünüyor"**dur.

`number` = oyuncunun **1..99 forma numarası** (§2); `0` = atanmamış, admin'de daima `0`. **Ad benzersiz
değildir, ayırt edici alan budur** — arayüzlerde `"7 · ertu"` biçiminde gösterilir.
`kills`/`deaths`/`hp`/`alive`/`score` **sunucu-otoriter** maç sayaçlarıdır (§10.2) ve admin gözlemci
arayüzünün tek doğruluk kaynağıdır: yalnız `kill_event`/`health_update` sayılsa admin yeniden
bağlandığında tablo sıfırlanırdı. Maç dışında (`paused`/`lobby`) `hp=PLAYER_MAX_HP`, `alive=true`, sayaçlar 0.
Admin olmayan istemciler bu alanları yok sayabilir.

`score` = **bireysel** maç skoru (`rules.scoring == "player"` olan modlarda anlamlı; takım
skoru `match_state.scoreRed`/`scoreBlue`'da kalır — §10.5). Bireysel skorun değiştiği an =
öldürmenin olduğu an = roster'ın zaten tazelendiği an, bu yüzden ayrı bir mesaj tipi yoktur.

`calibrated`/`calibrationSource` = başlığın hizalama durumu (§10.6). **Aynı gerekçeyle ayrı bir
`calibration_changed` mesajı YOKTUR:** durumun değiştiği an roster'ın zaten tazelendiği andır
(hem `set_calibration` hem `clear_calibration` registry'yi değiştirir → `lobby_state` yayınlanır).
Admin'de her ikisi de `false`/`""` kalır — admin kalibre olmaz, arayüzde "kalibresiz" sayılmaz.

**`load_match`** `{ "type":"load_match", "modeId":"tdm", "sceneName":"Arena12x12", "roundSeconds":300, "scoreLimit":30, "yourTeam":"red", "rules":{ … } }`
→ istemci sahneyi yükler, `status`'ta yeni sahne görünür. Sahne yüklenince istemci `set_ready` (yükleme tamam anlamında) gönderir; herkes hazır olunca sunucu `countdown` başlatır. Bu süre boyunca faz `paused`'dur (`phaseReason` sırayla `loading` → `countdown`); **`load_match`'in gelmesi maçın başladığı anlamına GELMEZ** — maç `phase:"playing"` ile başlar.
**Oyuncu ışınlanmaz ve kalibrasyon SIFIRLANMAZ** — harita değişimi oyuncu için yalnız bir sahne değişimidir, fiziksel duruşu ve hizalaması kaldığı yerden devam eder (§10.4).
**Adminlere de gönderilir** (gözlemci sahneyi yüklesin diye) ama `yourTeam:""` ile — admin oynamadığı için takım anlamsızdır ve admin `set_ready` göndermez.
`rules` = bu maçın kural şekli (§10.5). İstemci kendini **buna** göre kurar: takımsız modda `yourTeam` boş gelir, canlanma şartı `reviveAnchor`'dan okunur. İstemcide `if (modeId == "...")` zinciri YOKTUR — mod eklemek istemci kodunu değiştirmez.

**`countdown`** `{ "type":"countdown", "seconds":5 }` — 0'a inince faz `playing`.
**`match_state`** — faz/gerekçe değişimlerinde + `playing`'de saniyede 1:
```json
{ "type":"match_state", "phase":"playing", "phaseReason":"", "modeState":"",
  "timeRemaining":287.5, "scoreRed":3, "scoreBlue":5 }
```
Fazlar ve alanların anlamı §10.1'de. `phase` yalnız üç değer alır: `paused` · `playing` · `finished`.

**`shot_fired`** (relay) `{ "type":"shot_fired", "playerId":4, "weaponId":"ak47", "muzzlePos":[...], "muzzleDir":[...] }` — diğer istemciler uzak namlu alevi/ses oynatır (atan hariç herkese). Relay kapısı **`playing` VEYA `rules.fireWhilePaused`**'tur (§10.3) — lobide hedef atışı da görünür.
**`health_update`** `{ "type":"health_update", "playerId":5, "hp":75.0, "attackerId":3 }`
**`kill_event`** `{ "type":"kill_event", "killerId":3, "victimId":5, "weaponId":"ak47" }`
**`respawn`** `{ "type":"respawn", "playerId":5, "delaySeconds":5.0 }` — istemci `delaySeconds` sonra, modun canlanma şartını sağlayınca canlanır (§10.4). Sunucu sahne geometrisini bilmez; canlanma yeri diye bir alan taşınmaz.
**`match_end`** `{ "type":"match_end", "winnerTeam":"blue", "winnerPlayerId":0, "scoreRed":12, "scoreBlue":30 }`
Kazanan **iki kanaldan biriyle** ifade edilir (`rules.scoring`, §10.5): takım skorlu modlarda `winnerTeam` (`"red"|"blue"|""`), bireysel skorlu modlarda `winnerPlayerId` (`0` = yok/berabere). Bir mod ikisini de doldurmaz; okuyan istemci dolu olana bakar.
**`return_to_lobby`** `{ "type":"return_to_lobby", "modeId":"lobby", "sceneName":"Lobby12x12", "rules":{ … } }` — herkesi sunucunun **açık sahnesine** taşır. Şekli `load_match` ile aynıdır (§10.7): `sceneName` o an açık olan sahne, `modeId`/`rules` o sahnenin profili. Adı tarihseldir — yalnız "lobiye dön" değil, operatörün seçtiği arenayı sahnelemek için de kullanılır (§10.7 Sahneleme).
Aynı mesaj **lobi sahnelemesini** de taşır (§10.7): operatör lobideyken harita seçtiğinde `sceneName` o arenadır. İstemci için ikisi de aynı şeydir — *"lobideyiz, şu sahneyi yükle"* — bu yüzden ayrı bir mesaj tipi YOKTUR. `modeId` her iki durumda da `"lobby"` kalır: sahnenin arena olması fazı değiştirmez.
**`ping`** `{ "type":"ping" }` — istemci `status` ile yanıtlar (ayrı pong yok).
**`identify`** `{ "type":"identify" }` — istemci büyük kimlik overlay'i gösterir (playerId + ad).
**`kicked`** `{ "type":"kicked", "reason":"" }` — istemci bağlantıyı kapatır, lobi bağlantı ekranına döner.

**`admin_state`** — **yalnız `role=admin` bağlantılara**; adminler arası ortak durumun tek doğruluk kaynağı:
```json
{ "type":"admin_state", "modeId":"tdm", "sceneName":"Arena12x12",
  "venueId":"Outdoor12x12", "venueScenes":["Arena12x12","IceWorld","Lobby12x12"],
  "roundSeconds":600, "scoreLimit":30,
  "notice":"Ofis-PC: harita -> Arena12x12", "adminCount":2 }
```
- Gönderim anları: admin `hello` yanıtında (welcome'dan hemen sonra, geç katılan admin senkron başlasın), her `set_selection`'da, her admin komutunda (`start_match`/`abort_match`/`pause_match`/`resume_match`/`return_to_lobby`/`kick`/`identify`/`set_team`) ve admin bağlanıp ayrıldığında. ⚠️ `pause_match`/`resume_match` için duyuru **yalnız komut gerçekten uygulandıysa** yayılır — reddedilen komut diğer operatörlerin ekranına olmamış bir eylemi yazmamalı.
- `modeId`/`sceneName` = ortak seçim. ⚠️ **Hiçbir zaman boş değildir:** sunucu açılışta seçimi **mekanın lobi haritasıyla** tohumlar (`modeId:"lobby"`, `sceneName:<mekanın lobisi>` — §10.7 açık sahnenin açılış değeri), sonrasında da boş alan mevcut değeri koruduğu için seçim bir daha boşalamaz. Böylece ilk `admin_state`'i alan admin de "hiç harita seçilmemiş" bir durum görmez. Admin arayüzü **kendi yerel seçimini değil bunu gösterir**; gelen değer arayüzdeki mod/harita seçicisini günceller. Yani bir operatör haritayı değiştirdiğinde diğerinin ekranı da (paneli açık olmasa bile) o haritaya döner — sahneyi zaten `return_to_lobby` sahnelemesi taşır (§10.7), `admin_state` yalnız seçiciyi hizalar.
- `roundSeconds`/`scoreLimit` = bir sonraki maçın ortak parametreleri (`0` = hiç seçilmedi, modun varsayılanı kullanılacak). Mod/harita ile aynı kanaldan gider — sebebi §5.2 `set_selection` notunda.
- `notice` = son admin eyleminin insan okuyabilir özeti (`"<admin adı>: <eylem>"`), tüm adminlerin durum satırında görünür. Boş olabilir.
- `adminCount` = o an çevrimiçi admin sayısı.
- `venueId`/`venueScenes` = sunucunun açılışta seçtiği mekan ve o mekanın sahne adları (§11.1). Oturum boyunca değişmez ama her `admin_state`'te taşınır ki geç bağlanan admin de ilk mesajda hangi arenaları görebileceğini öğrensin. **Admin harita seçicisi kendi yerel kataloğunu bununla süzer**: katalog tüm projeyi tanır, oynatılabilir olana sunucu karar verir. Boş gelirse süzme yapılmaz.
- **Yalnız operasyonel durum senkronlanır.** Görünüm tercihleri (kamera kipi, seçili oyuncu, halka/ad etiketi, kamera hızı, duvar/çatı saydamlığı) her admin'in **kendi ekranına** aittir, protokole girmez ve `PlayerPrefs`'te yerel kalır.

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
`flags` bit0 = alive. İstemci kendi pozunu snapshot'tan ÇİZMEZ (yerelden çizer); uzak oyuncuları `INTERP_DELAY_MS` tamponuyla interpole eder. Admin'e de aynı snapshot gider (gözlemci avatarları/işaretçileri bundan beslenir).

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
       → StatusLoop (5 sn; status.rosterVersion ile roster uzlaştırması) + (player ise,
         Live/Lobby fark etmez) PoseLoop (20 Hz)
Kopma  → 1→2→5 sn backoff ile discovery'den itibaren baştan (sonsuz)
       → bağlantısızlık ~3 sn sürerse istemci hata ekranı gösterir (sunum; §4 notu)
Sunucu : hello'suz bağlantıyı 10 sn içinde kapat; deviceId çakışmasında eskisini kapat
       → roster değişince TEK yayıncı üzerinden lobby_state (version artar); status.rosterVersion
         geride kalan istemciye YALNIZ ona tam snapshot yollatır
       → 15 sn status yoksa Offline işaretle + bağlantıyı kapat + lobby_state yayınla
```

## 9. Güvenlik (v1)

LAN-içi, auth YOK. `hello`'ya ileride `token` alanı eklenebilir (rezerve). Sunucu yalnız özel ağda; Windows Firewall kuralları `Server/firewall-kur.cmd` ile (TCP 47821 + UDP 47820/47822).

## 10. Maç akışı + savaş kuralları (sunucu-otoriter)

Tüm kural otoritesi sunucudadır (`MatchDirector` + `Modes/<X>Mode.cs : IGameMode`). İstemci **sunum + girdi**dir: hasar uygulamaz, skor tutmaz, faz değiştirmez.

### 10.1 Durum modeli — dört alan, dört ayrı sahip

Maçın durumu **tek bir enum değildir.** Dört alan taşınır (`welcome.match` + `match_state`), her
birinin tek sahibi vardır:

| Alan | Sahibi | Değerler | Anlamı |
|---|---|---|---|
| `modeId` | operatör seçimi | `lobby` · `tdm` · `ffa` · … | **Ne oynanıyor.** Lobi de bir türdür (§10.7) |
| `phase` | çekirdek (`MatchDirector`) | `paused` · `playing` · `finished` | **Maçın genel durumu** |
| `phaseReason` | çekirdek | `""` · `lobby` · `loading` · `countdown` · `operator` · `mode` | **Neden** duraklı (yalnız `paused` iken dolu) |
| `modeState` | mod (`IGameMode`) | serbest string (`round3`, `regroup`, …) | **Modun kendi ara durumu.** Çekirdek yorumlamaz |

⚠️ **`phase`'in TEK yetkisi hasar kapısıdır:** `hit_report` yalnız `playing` fazında işlenir
(§10.3). Başka hiçbir kural doğrudan `phase`'e bakmaz — "ateş edebilir miyim", "silahım nereden
gelir", "hangi HUD" sorularının cevabı **moddan** gelir (§10.5).

⚠️ **`modeState` asla bir kural/hasar kapısı olamaz.** Çekirdek onu okumaz, yalnız HUD okur. Bir mod
oyunu durdurmak isterse çekirdekten `phase = paused` + `phaseReason = "mode"` ister ve gerekçesini
`modeState`'e yazar. Aksi hâlde ikinci bir otorite doğar ve "maç koşuyor mu" sorusunun iki cevabı olur.

```
              start_match                 herkes set_ready | LOADING_TIMEOUT
paused ─────────────────────► paused ──────────────────────────────► paused
(lobby)                       (loading)                              (countdown)
   ▲                                                                    │ 0'a indi
   │ return_to_lobby                                                    ▼
   └──────────── finished ◄──── maç sonu ◄──────────────────────────  playing
                (MATCH_END_SECONDS sonra otomatik)                    ▲    │
                                                                      │    │ operatör duraklattı
                                                                      └────┘ / mod istedi
                                                                    paused(operator|mode)
```

**`paused`'da hasar alma da verme de kapalıdır — başka ek kuralı yoktur.** Süre işlemez, skor
değişmez. `finished` de aynı şekilde hasarsızdır; farkı skorların kesinleşmiş olmasıdır.

**Neden lobi bir faz değil:** `phase` sadece hasar kapısına indirgenince "hasar yok ama ateş
serbest" bir maç durumu olmaktan çıkıp bir **tür özelliği** olur. Lobi faz olarak dursaydı,
turnuva gibi kendi ara durumu olan her yeni mod da çekirdek enum'unu büyütmek zorunda kalırdı —
modlar çekirdeğe dokunamaz. Bunun yerine lobi `modeId:"lobby"` + `rules.fireWhilePaused:true`
ile tanımlanır (§10.5, §10.7).

- **`start_match` doğrulaması** (sırayla): `modeId` sunucudaki `IGameMode` kayıtlarında var; `sceneName` boş değil; **`sceneName` `config/maps.json` harita tablosunda var ve o harita `modeId`'yi destekliyor** (harita girdisindeki `modes` boşsa kısıt yok; **tablo boşsa — maps.json yoksa — bu adım tümüyle atlanır**); `sceneName` tüm çevrimiçi oyuncuların `hello.scenes` listesinde var. Geçmezse komut reddedilir ve konsola sebep yazılır (durum değişmez). İki oyuncu+ varken takımlar dengelenir (boş takım kalmaz); tek oyuncuyla ve **hiç oyuncu yokken** başlatmaya izin verilir (konsolda uyarı) — ikincisi admin gözlemcinin haritayı boş arenada açması için vardır.
  ⚠️ **`lobby` bir `IGameMode` DEĞİLDİR** → `start_match{"lobby"}` "bilinmeyen mod" diye reddedilir. Yani **lobi türü seçiliyken maç başlatılamaz** (§10.7); bunun için ayrı bir kural yazılmaz, kayıtlı olmaması yeterlidir.
- **Oyuncusuz maç (yalnız admin):** `load_match` yalnız adminlere gider, yükleme kapısında beklenecek `set_ready` olmadığı için doğrudan geri sayıma geçilir ve maç normal işler (skor 0, süre akar). Ayrım şu: **oyuncularla başlamış** bir maçta yükleme sırasında son oyuncu da düşerse sunucu maçı bırakıp açık sahneye döner; oyuncusuz **başlatılmış** maçta dönmez — çıkış operatörün `abort_match`/`return_to_lobby` komutudur.
- **Mod/harita/parametre seçimi sunucuda yaşar (çoklu admin):** admin arayüzündeki seçiciler yerel bir değişkeni değil, `set_selection` ile sunucudaki ortak seçimi değiştirir; sunucu `admin_state` ile hepsine geri yayar. `start_match` kendi `modeId`/`sceneName`'i ile gelmeye devam eder (protokol yüzeyi genişledi ama kırılmadı) ama sunucu onu aynı zamanda ortak seçime yazar — böylece maç başladığında tüm admin panelleri aynı değeri gösterir. Seçim yalnız bir niyet beyanıdır: doğrulama `start_match` anında yapılır.
- **Maç parametreleri admin'den gelebilir:** `start_match.roundSeconds`/`scoreLimit` doluysa (`> 0`) o maç bu değerlerle koşar; boş/`0` ise modun varsayılanı (`IGameMode.DefaultRoundSeconds`/`DefaultScoreLimit`) kullanılır. Yani `ModeDefinition`/`IGameMode` üzerindeki sayılar **varsayılandır, kilit değil** — operatör raundu kısaltıp uzatabilir. Değer `load_match`/`match_state` üzerinden istemcilere zaten gidiyor, ek bir kanal doğmaz.
- **`load_match` kişiselleştirilir:** her oyuncuya kendi `yourTeam`'i gider; **takımsız modda** (`rules.teamMode == "none"`, §10.5) takım boş gider. Yükleme kapısına girerken tüm `ready` bayrakları sıfırlanır. **Çevrimiçi adminlere de bir kopya gider** (`yourTeam:""`) — admin gözlemci aynı sahneyi yükler.
- **`phaseReason:"loading"`:** istemci sahneyi yükleyince `set_ready{ready:true}` gönderir ("sahne yüklendi" anlamında). Tüm çevrimiçi **oyuncular** hazır olunca (veya `LOADING_TIMEOUT` dolunca) geri sayım başlar. Kapı yalnız `role=player` bağlantılarını sayar: admin sahneyi yüklese de `set_ready` göndermez, geri sayımı ne hızlandırır ne geciktirir.
- **`phaseReason:"countdown"`:** saniyede bir `countdown{seconds}` (5→1); 0'da faz `playing`.
- **`playing`:** `match_state` 1 Hz; `timeRemaining` sunucuda azalır; `IGameMode.OnTick` çağrılır. **Hasar yalnız burada işlenir.**
- **`finished`:** `match_end` yayınlanır, `MATCH_END_SECONDS` sonra `return_to_lobby` + faz `paused`/`lobby` (skorlar/canlar sıfırlanır, oyuncular açık sahneye döner). `finished` iken operatör harita/mod seçebilir ve yeni maç başlatabilir.
- **`abort_match`** her durumdan `paused`/`lobby`'ye düşürür (`return_to_lobby` yayınlanır); `return_to_lobby` doğrudan aynı işi yapar.
- **Duraklatma (`phaseReason:"operator"` / `"mode"`):** `playing` iken duraklatılan maç `paused`'a geçer — süre durur, hasar kapanır, `modeState` **korunur** (mod kaldığı yerden sürer). Devam edilince `playing`'e döner. ⚠️ Operatörün duraklatması ile modun duraklatması aynı fazı üretir ama gerekçeleri ayrıdır: turnuva "herkes tabana dönsün" derken (`mode`) operatör de duraklatırsa (`operator`) HUD'un doğru mesajı gösterebilmesi için ikisi karışmamalıdır.
  - Operatörün kapısı `pause_match` / `resume_match`'tir (§5.2) ve **yalnız kendi duraklatmasını kaldırabilir** (`phaseReason == "operator"`). `mode` gerekçesini kaldırma yetkisi modundur; `loading`/`countdown` zaten kendi koşullarıyla biter.
  - `abort_match` duraklı maçta da çalışır: duraklatmak maçtan çıkmak değildir, çıkış hâlâ `abort_match`/`return_to_lobby`'dir.

### 10.2 Oyuncu maç durumu (sunucuda)

Oyuncu başına: `hp` (0..`PLAYER_MAX_HP`), `alive`, `team`, `kills`, `deaths`, `score`, ölüm zamanı. `playing`'e girerken herkes `hp=PLAYER_MAX_HP`, `alive=1`. Snapshot'taki `SnapshotEntry.flags` bit0 (`FLAG_ALIVE`) bu `alive` alanından beslenir — maç dışında (`paused`/`lobby`) herkes canlı sayılır.

`score` = **bireysel maç skoru**. Yazarı yalnız `IGameMode`'dur (`MatchDirector`'ın skor defteri üzerinden); `kills` ile aynı şey DEĞİLDİR — bir mod öldürme başına 1, bir başkası objektif başına 5 yazabilir, Silah Yarışı'nda aynı alan "seviye" anlamına gelir. Maç kurulurken ve açık sahneye dönerken 0'lanır.

`hp`/`alive`/`kills`/`deaths`/`score` **`lobby_state` ile de yayınlanır** (§5.3): ölüm işlendikten sonra roster bir kez tazelenir, böylece admin istatistik tablosu sunucudaki sayaçla birebir kalır ve admin yeniden bağlandığında geçmişi kaybetmez. Anlık akış (her vuruş) yine `health_update`/`kill_event` üzerinden gider — `lobby_state` sağlama noktasıdır, sıcak yol değil.

⚠️ `calibrated`/`calibrationSource` (§10.6) bu listeye **dahil değildir**: maç durumu değil cihaz durumudur, yazarı `MatchDirector` değil `PlayerRegistry`'dir (`Team` ile aynı desen — registry kilidinde yazılır, director kilidinde okunur; `bool` okuması atomik olduğu için iki kilidi birbirine bağlamaya gerek yoktur) ve maç sıfırlamalarında **korunur**.

### 10.3 Vuruş hattı — genel hasar modeli

**Hile koruması yoktur ve bilinçli olarak eklenmez.** Ürün, gözetim altındaki özel alanlarda
(işletme kurulumu, turnuva) çalışır; hile yapmanın kimseye faydası olmadığı bir ortamda hile
denetimi yalnız meşru vuruşları yiyen bir tuzaktır. Bu yüzden hasar hesabı **tamamen istemcide**
yapılır, sunucu hakemlik değil **defter tutar**: canı düşürür, ölümü ilan eder, skoru işler.

`hit_report` şu sırayla kontrol edilir; **herhangi biri düşerse paket sessizce reddedilir**
(konsola tek satır log, istemciye yanıt yok). Bunlar hile denetimi değil, **durum tutarlılığı**
kontrolleridir — kaldırılırlarsa çift ölüm / maç dışı hasar gibi hatalar üretilir:

1. Faz `playing` mi? (**tek hasar kapısı budur**, §10.1)
2. Atıcı çevrimiçi + `role=player` + `alive` + **`calibrated`** mi? (§10.6: kalibresiz oyuncu ateş edemez)
3. Hedef var, çevrimiçi, `alive` + **`calibrated`** mi? (aynı karede gelen iki ölümcül vuruş çift `kill_event` yazmasın; kalibresiz oyuncu hasar YEMEZ — §10.6)
4. Hedef atıcının kendisi değil ve **takım arkadaşı değil** mi? Kural: `rules.friendlyFire == false` iken *takım arkadaşı* vurulamaz, ve **boş takım asla takım arkadaşı sayılmaz** — takımsız modda (§10.5 `teamMode:"none"`) herkesin takımı `""` olduğu için `"" == ""` karşılaştırması tüm vuruşları reddederdi. `friendlyFire == true` ise bu adım hiç uygulanmaz.
5. `damage` sonlu ve pozitif bir sayı mı? (NaN/∞ canı kalıcı bozar; sayı denetimi, hile denetimi değil)

Geçerse: `hp -= damage` (istemcinin bildirdiği değer) → `health_update{playerId, hp, attackerId}`
**herkese** yayınlanır. `hp ≤ 0` ise `alive=0`, `kill_event{killerId, victimId, weaponId}` +
`IGameMode.OnKill` (skor) + kurbana `respawn{delaySeconds:RESPAWN_DELAY}`.

Atış hızı denetimi, `weaponId` beyaz listesi ve sunucu-otoriter silah tablosu **YOKTUR** ve
eklenmez: pompalı saçması, bomba parçası ve ok yaylımı gibi meşru "hızlı art arda vuruş"
örüntülerini sessizce düşürürler.

`shot_fired` sunucuda **doğrulanmaz**, yalnız relay edilir (atan hariç herkese, `playerId`
eklenerek) — ölü/**kalibresiz** oyuncunun `shot_fired`'ı relay EDİLMEZ. Kapısı
**`phase == playing` VEYA `rules.fireWhilePaused`**'tur: lobide hedef atışı yapılabildiği için
(§10.7) başkalarının namlu alevini görmesi doğrudur. Yükleme/geri sayım/duraklatma sırasında
(`fireWhilePaused:false` olan modlarda) relay yoktur.

⚠️ **`hit_report`'un kapısı bundan AYRIDIR ve yalnız `playing`'dir** — lobide, yüklemede,
duraklatmada oyuncuya hasar verilemez. İki kapı bilerek ayrı: atış bir sunum olayı, vuruş bir
durum değişimidir. Bu yüzden "ateş edebilir miyim" moda (`fireWhilePaused`), "hasar var mı"
çekirdeğe (`phase`) bağlıdır.

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
3. Sunucu doğrular (faz `playing`, oyuncu ölü, gecikme dolmuş, **kalibreli**) → `hp=PLAYER_MAX_HP`, `alive=1` → `health_update{hp:100, attackerId:0}`.
4. Ölümden `REVIVE_GRACE` geçtiği hâlde talep gelmediyse sunucu **zorla** canlandırır (istemci takılmışsa maç kilitlenmesin).

> **`reviveAnchor` sunucuda DOĞRULANMAZ.** §10.3 felsefesinin aynısı: sunucu hakemlik değil defter tutar. "Tabanda mı / sabit mi durdu" kararı istemcinindir; sunucu faz + ölü + gecikme kontrolüyle yetinir. `REVIVE_GRACE` güvenlik ağı her iki şartta da aynen işler.

> ⚠️ **`REVIVE_GRACE`'in TEK istisnası kalibrasyondur** (§10.6): kalibresiz oyuncu ne talep üzerine
> ne de zorla canlandırılır — grace döngüsü onu atlar. Aksi hâlde "kalibresiz oyuncu canlanamaz"
> kuralı işlevsiz olurdu (birkaç saniye sonra kendiliğinden canlanırdı). Kalibresiz ölü oyuncu
> **kalibre olana dek ölü kalır**; kalibrasyon gelince grace zaten dolmuş olduğu için ilk tik'te
> kendiliğinden canlanır.

**Taban bölgesi eşleşmesi (istemci):** bir `BaseZone` oyuncuya açıktır eğer takımı oyuncunun takımıyla aynıysa, **ya da** bölge `Neutral` işaretliyse, **ya da** oyuncunun takımı boşsa (takımsız mod). Aynı takıma ait birden çok bölge varsa **herhangi birine** girmek yeter. Sahnede hiç açık bölge yoksa şart aranmaz — oyuncu kalıcı ölü kalmasın (güvenlik ağı yine `REVIVE_GRACE`).

**Konum diye bir alan protokolde YOKTUR.** Ne `load_match` ne `respawn` bir spawn noktası/slotu taşır; sunucu sahne geometrisini bilmez. Arena başına sahnedeki tek `SpawnPoint` marker'ı **yerleşim göstergesidir** (maç öncesi operatörün oyuncuyu yönlendirdiği fiziksel nokta) ve rig'i oraya taşıyan bir mekanizma yoktur. Protokolde karşılığı olmayan ama istemcide bağlayıcı olan ikinci bir işi vardır: **arena-yerel uzayın origin'idir** (bkz. koordinat uzayı bölümü) — yani sahnede yerleştirildiği yer, telde taşınan tüm koordinatların sıfırıdır.

**Harita değişimi kalibrasyonu sıfırlamaz.** `load_match` oyuncu için yalnız bir sahne değişimidir: kimse "yeniden doğmaz", rig taşınmaz. Yeni sahnenin `ArenaCalibrator`'ı `Start`'ta kayıtlı `OVRSpatialAnchor`'dan hizalamayı geri yükler, oyuncu fiziksel olarak nerede duruyorsa orada kalır. **Poz gönderimi hizalamayı beklemez:** `PlayerPoseTracker` baştan kaydolur, hizalama gelene dek gönderilen pozlar arena ile örtüşmez (rig ofsetli) ama akar — oyuncunun bağlı ve hareket hâlinde olduğu ağdan görülebilsin diye. Sunucu bu ayrımı bilmez; pozlar her hâlde `PoseUpdate` olarak kabul edilir ve snapshot'a girer.

### 10.5 Mod kuralları (`ModeRules` / `rules`)

Bir modun **ne tür bir oyun olduğunu** anlatan, **sunucu-otoriter** şekil tanımı. Her `IGameMode`
kendi `Rules`'ünü döner; sunucu bunu `load_match.rules` ve `welcome.match.rules` ile istemciye
yollar. Amaç tek: **istemci modun ne olduğunu TAHMİN ETMESİN.** Kural telden gelirse istemcide
`if (modeId == "ffa")` zinciri hiç doğmaz — yeni mod eklemek istemci kodunu değiştirmez.

```json
"rules": { "teamMode":"two", "scoring":"team", "friendlyFire":false,
           "reviveAnchor":"base", "weaponSource":"rack", "respawnDelay":5.0,
           "fireWhilePaused":false }
```

| Alan | Değerler | Varsayılan | Anlamı |
|---|---|---|---|
| `teamMode` | `"two"` \| `"none"` | `"two"` | `"two"`: kırmızı/mavi, sunucu takımları dengeler, slot takım içi. `"none"`: takım yok (`team:""`), slot tek havuzdan |
| `scoring` | `"team"` \| `"player"` | `"team"` | Skor kime yazılır: `match_state.scoreRed/scoreBlue` mi, `lobby_state → PlayerInfo.score` mü (§10.2) |
| `friendlyFire` | `true` \| `false` | `false` | `false` = takım arkadaşı vurulamaz (§10.3/4). Boş takım asla takım arkadaşı sayılmaz |
| `reviveAnchor` | `"base"` \| `"standstill"` | `"base"` | Canlanma şartı (§10.4/2) |
| `weaponSource` | `"rack"` \| `"random"` | `"rack"` | Silah nereden gelir: sahnedeki raf mı, mod mu dağıtır. **Tümüyle istemci sunumu** — sunucuda karşılığı yok (§10.3: silah tablosu yoktur) |
| `respawnDelay` | saniye | `RESPAWN_DELAY` (5) | `respawn.delaySeconds` ve sunucudaki `revive_request` gecikme eşiği. **`0` geçerli bir değerdir** (anında canlanma) ve varsayılana çekilmez — alan hiç gönderilmezse DTO'nun kendi başlangıcı geçerli olduğu için "yazılmadı" ile "sıfır yazıldı" karışmaz |
| `fireWhilePaused` | `true` \| `false` | `false` | Faz `playing` değilken silah ateşlenebilir mi. `true` = lobi gibi serbest atış alanı: namlu alevi/ses relay edilir (§10.3) ama **hasar yine yoktur** (`hit_report` kapısı `playing`). Bu alan sayesinde istemcide `if (modeId == "lobby")` zinciri doğmaz |

- **Varsayılan = bugünkü TDM.** Bir mod hiçbir alan yazmazsa bugünkü davranışı alır; yani yeni mod
  yalnız *farklı* olduğu alanları belirtir.
- **Bilinmeyen/boş değer varsayılana düşer.** Değerler bilerek string: eski istemci yeni sunucudan
  tanımadığı bir `teamMode` görürse takımlı TDM gibi davranır, bağlantı kopmaz. Bu yüzden yeni bir
  kural değeri eklemek `PROTOCOL_VERSION`'ı **artırmaz**.
- **Kazanan ifadesi `scoring`'e bağlıdır:** `"team"` → `match_end.winnerTeam`, `"player"` →
  `match_end.winnerPlayerId`.
**Kayıtlı modlar** (sunucuda `MatchDirector.RegisterModes()`; `start_match.modeId` bunlardan biri
olmalı, tanınmayan `modeId` reddedilir):

| `modId` | Ad | `teamMode` | `scoring` | `friendlyFire` | `reviveAnchor` | `weaponSource` | `respawnDelay` | `fireWhilePaused` | Varsayılan süre / limit |
|---|---|---|---|---|---|---|---|---|---|
| `tdm` | Takım Ölüm Maçı | `two` | `team` | `false` | `base` | `rack` | `5` | `false` | 300 sn / 30 |
| `ffa` | Herkes Tek | `none` | `player` | `false` | `standstill` | `random` | `0` | `false` | 300 sn / 20 |

> ⚠️ **`lobby` bu tabloda YOKTUR ve olmayacaktır.** Lobi bir **tür**dür ama `IGameMode` değildir
> (§10.7): sunucuda kaydı olmadığı için `start_match{"lobby"}` "bilinmeyen mod" diye reddedilir —
> yani lobi türü seçiliyken maç başlatılamaz. Kural şekli yine de tanımlıdır ve telde taşınır:
> `fireWhilePaused:true`, geri kalanı varsayılan. Lobi `modeId`'si istemcide silah loadout'unu,
> HUD'u ve ateş serbestliğini çözer.

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
Kurallar telde gelmediğinde (`rules == null` — kuralları taşımayan bir sunucu) `ModeDefinition`'ın
önizleme alanları devralır; **sapmada sunucu kazanır** — `ModeDefinition`'daki kural alanları
yalnız önizleme/fallback içindir.

### 10.6 Kalibrasyon durumu (sunucu-otoriter)

Oyuncu başına `calibrated` (bool) + `calibrationSource` (string) sunucuda tutulur ve `lobby_state`
ile yayılır (§5.3). Amaç operasyoneldir: sahada bir başlığın hizalaması kayar, o oyuncunun avatarı
fiziksel konumundan sapar — operatörün onu **maçtan çıkarmadan** savaş dışı bırakıp yeniden
kalibre ettirebilmesi gerekir.

**İki yazar, ama asimetrik:**

| Yazar | Mesaj | Ne yapabilir |
|---|---|---|
| Başlık | `set_calibration` (§5.1) | `true` **ve** `false` |
| Admin | `clear_calibration` (§5.2) | **yalnız `false`** |

**Admin neden "kalibre oldu" diyemez:** hizalamanın gerçekten oturduğunu yalnız başlık bilir.
Admin elle işaretleyebilseydi, sunucunun hizalı sandığı ama fiilen kaymış bir oyuncuya ateş ve
hasar açılırdı — bu sistemin önlemek için var olduğu durumun ta kendisi.

**Kalibresiz oyuncunun durumu:**

1. `hit_report`'u **reddedilir** (ateş edemez) — §10.3/2
2. Ona gelen `hit_report` **reddedilir** (hasar yemez) — §10.3/3
3. `shot_fired`'ı **relay edilmez** — §10.3
4. `revive_request`'i reddedilir **ve `REVIVE_GRACE` zorla canlandırması onu atlar** — §10.4
5. Maç sayaçları (`hp`/`kills`/`deaths`/`score`) **korunur** — kalibrasyon geri gelince oyuncu
   kaldığı yerden devam eder; bu bir cezalandırma değil, geçici bir dondurmadır.

**İstemci tarafı** (protokolün zorunlu kıldığı değil, beklenen davranış): kalibresizken tetik
kilitlenir, uzak avatar **parlar** ve vuruş kutuları kapanır, kumandada A+B ile elle kalibrasyon
**açılır**. Kalibreli durumdayken A+B **kilitlidir** — oyuncu kendi hizalamasını kazara bozamaz,
kapıyı yalnız operatör açar.

**`hello`'da `calibrated` sıfırlanır.** Sunucu yeniden bağlanan bir başlığın hizalama durumunu
bilemez (uygulama yeniden başlamış olabilir); başlık kayıtlı anchor'dan geri yükleyince zaten
`set_calibration{source:"anchor"}` ile yeniden bildirir.

⚠️ **`load_match` kalibrasyonu SIFIRLAMAZ** (§10.4). Harita değişimi oyuncu için yalnız bir sahne
değişimidir; sunucu `calibrated`'i korur. Yanlışlıkla sıfırlanırsa her harita değişimi tüm
oyuncuları savaş dışı bırakır.

⚠️ **Poz gönderimi kalibrasyona BAĞLI DEĞİLDİR.** Kalibresiz oyuncu `PoseUpdate` göndermeye devam
eder (pozları arena ile örtüşmez ama akar). Bu bilinçlidir: operatörün "avatar kaymış" teşhisini
koyabilmesi ve parlayan avatarın hareket ettiğini görebilmesi için pozun akıyor olması gerekir.

**Bulut kalibrasyonu (ileride).** Paylaşılan uzamsal anchor ile toplu hizalama geldiğinde protokol
değişmez: `source:"cloud"` zaten geçerli bir değer, `clear_calibration{playerId:0}` zaten toplu
sıfırlama yapıyor. Grup/oturum kimliği taşıyan alanlar **o iş gelene kadar eklenmez**.

### 10.7 Lobi (tür + sahne + profil)

Lobi bir **türdür** (`modeId:"lobby"`), bir faz değildir ve bir maç değildir. Boş bir bekleme
durumu da değildir: işletmenin kendi lobi sahnesi vardır, oyuncular orada birbirini görür,
**kalibrasyonunu orada yapar**, silah rafından silah alıp hedeflere ateş eder — birbirlerine hasar
veremeden.

| Soru | Cevap |
|---|---|
| Lobi sahnesi hangisi? | Sunucu söyler: `server.json → lobbyScene`, boşsa mekanın tek lobi haritası (§11). Çözülemezse **sunucu açılmaz** (§11) |
| Faz ne olur? | `paused` + `phaseReason:"lobby"` (§10.1). Lobi diye bir faz YOKTUR |
| Oyuncuya hasar? | **İmkânsız** — `hit_report` yalnız `playing` fazında işlenir (§10.3) |
| Atış görünür mü? | Evet — `rules.fireWhilePaused:true` olduğu için `shot_fired` relay edilir (§10.3) |
| Silah nereden gelir? | Sahnedeki raf. Loadout'u istemci `modeId:"lobby"` ile kendi katalogundan çözer |
| Canlanma / skor / süre? | Yok. Herkes canlı (`hp=PLAYER_MAX_HP`), sayaçlar 0 (§5.3) |
| Takım? | Vardır ve **yalnız admin atar** (`set_team`, §5.2) — her fazda, sunucuya bağlı herkes için. Oyuncu kendi takımını seçemez; bunun için protokol mesajı YOKTUR ve eklenmeyecektir |
| Maç başlatılabilir mi? | **Hayır.** `lobby` kayıtlı bir `IGameMode` olmadığı için `start_match` reddedilir (§10.1) |

**Lobi türünün iki kilidi birbirini tamamlar:**

1. **Lobi haritasında yalnız lobi türü oynanır** — `MapDefinition.supportedModeIds = ["lobby"]`,
   yani `start_match` başka bir modu o sahnede kabul etmez.
2. **Lobi türü yalnız lobi haritasında olur** — gerçek arenalar `lobby`'yi listelemediği için lobi
   profili başka haritaya sızamaz.

İkisi de `maps.json`'daki `modes` alanından gelir; **ayrıca bir kural yazılmaz.**

**`modeId:"lobby"` neden var?** İstemcide silah loadout'u, HUD ve (artık) ateş serbestliği `modeId`
üzerinden çözülen kural şeklinden geliyor. Lobi türü `rules.fireWhilePaused:true` taşır; geri kalan
alanlar varsayılandır (§10.5). Böylece **savaşı kapatan şey faz** (`hit_report` yalnız `playing`),
**ateşe izin veren şey mod** olur — ikisi ayrı kapı olduğu için lobi "hasarsız atış alanı" olabilir.

> ⚠️ **Lobi bir maç yapılMAZ.** `playing`'e taşınsaydı hasar kapısı açılır, ayrıca yükleme/geri
> sayım/tur sayacı/`finished` yaşam döngüsü ve `return_to_lobby`'nin kendini çağırması gibi lobide
> karşılığı olmayan bir makine devralınırdı. "Maç koşuyor mu?" sorusunun tek cevabı
> `phase == playing`'dir.

#### Sahneleme — operatörün seçtiği harita herkese açılır

**Sunucunun her zaman bir "açık sahnesi" vardır** ve istemcinin tek yönlendirme kaynağı odur.
Açılışta bu, mekanın lobi haritasıdır — **ortak seçim de (§5.3 `admin_state`) aynı değerle
tohumlanır**, yani sunucu ayakta olduğu sürece "harita seçilmemiş" diye bir durum yoktur.
Operatör admin panelinden başka bir harita seçtiğinde
(`set_selection`, §5.2) sunucu o arenayı **sahneler**:
`return_to_lobby{ modeId:"lobby", sceneName:<seçilen arena> }` TÜM istemcilere gider ve herkes o
sahneyi yükler. Amaç saha akışıdır — oyuncular maç başlamadan arenaya girer, kalibrasyonunu orada
yapar, yerini alır; operatör bunu tek tek anlatmak zorunda kalmaz.

| | |
|---|---|
| Faz | **Değişmez, `paused` kalır** — hasar kapısı (§10.3) kapalı, `set_ready` yok, süre/skor işlemez |
| Ne zaman olur | `set_selection`'ın **`sceneName` alanı dolu geldiğinde** — istenen sahne zaten açıksa hiçbir şey olmaz (idempotent). Süre/limit dokunuşunda alan boş gider, kimse taşınmaz. ⚠️ Ölçüt "seçim değişti mi" DEĞİL "açık sahne bu mu": maç bitip lobiye dönüldüğünde seçim hâlâ o arenayı gösterir, operatör aynı arenayı tekrar seçtiğinde sahnelenebilmelidir |
| Ne zaman OLMAZ | Faz `playing` iken. Sahne komutu herkese gittiği için koşan maçın ortasında harita değişimi maçı bozardı. `finished` iken serbesttir — operatör maç bitince yeni haritayı seçebilsin |
| Doğrulama | `start_match` ile aynı (§10.1): sahne harita tablosunda olmalı **ve** tüm çevrimiçi oyuncuların build listesinde bulunmalı. Geçmezse sahneleme yapılmaz, seçim yine kaydedilir ve sebep `admin_state.notice` ile operatöre yazılır |
| Geç katılan | `welcome.match.sceneName` sahnelenen arenadır → doğrudan oraya düşer |
| Maç bitince | `return_to_lobby` normal yolundan gelir ve açık sahne **işletmenin lobi haritasına** döner; sahneleme kalıcı değildir |

> ⚠️ **`modeId` sahnelemede de `"lobby"` kalır.** Seçili maç modunu yazmak maç HUD'unu ve maç
> loadout'unu maç başlamadan açardı; sahnenin arena olması türü değiştirmez. Tür ancak
> `start_match` ile değişir.

## 11. Sunucu config dosyaları

`Server/config/` altındaki üç dosya; kaynakları FARKLIDIR:

| Dosya | Kaynağı | Not |
|---|---|---|
| `server.json` | **Elle** | Portlar + `venueName` + `tickHz` + `venue` + `lobbyScene`; yoksa varsayılanlarla oluşturulur (§1 sabitleri). `venue` = açılışta seçilecek mekan (boş = konsolda sorulur). `lobbyScene` = lobi sahnesi (§10.7); **boş = seçilen mekanın lobi haritası otomatik bulunur**. ⚠️ Çözülemezse sunucu **açılmaz** (aşağı). |
| `devices.json` | **Sunucu üretir** | `deviceId → { "name":"ertu", "number":7 }`; ilk bağlantıda ve `set_identity`'de yazılır (§2). Eski v1 biçimi (`deviceId → "ad"`) okunur — numara `0` sayılır — ve ilk yazımda yeni biçime yükseltilir. UTF-8, BOM'suz. |
| `maps.json` | **Unity export** | `MapDefinition` SO'larından: `sceneName`, `venue`, `modes` (§10.1, §11.1). Arena ölçüsü YOKTUR — sunucu metre kullanmaz, ölçü istemcide sahnenin `ArenaBoundary`'sinde kalır. |

> **`weapons.json` YOKTUR:** sunucu silah tanımı tutmaz, hasarı istemci bildirir (§10.3). Silah
> istatistikleri yalnız Unity'deki `WeaponDefinition` SO'larındadır.
>
> ⚠️ **Açık sahne çözülemezse sunucu AÇILMAZ (fail-fast).** `lobbyScene` boş **ve** seçilen mekanda
> `modes == ["lobby"]` olan harita yoksa, ya da verilen `lobbyScene` `maps.json`'da bulunmuyorsa
> sunucu sebebi + düzeltme yolunu yazıp sıfırdan farklı bir çıkış koduyla kapanır. Gerekçe: sunucunun
> açık sahnesi istemcinin **tek** yönlendirme kaynağıdır (§5.3) — çözülemiyorsa zaten bir
> yapılandırma hatası vardır ve oyuncu doğru oynayamaz; sessizce boş sahneyle açılmak hatayı sahaya
> taşır. (`maps.json` hiç yoksa doğrulama atlanır, aşağıdaki maddeye bakın.)
>
> **`maps.json` ELLE DÜZENLENMEZ** — `Tools > VortexArena > Export Server Config` üretir ve bir sonraki export elle yapılan değişikliği **ezer**. Tek doğruluk kaynağı Unity SO'larıdır; çıktı deterministiktir (alfabetik, LF, UTF-8 BOM'suz) → git diff'i temiz kalır. Harita ekleyip export'u çalıştırmayı unutursanız bilinmeyen `sceneName` → `start_match` reddedilir. `maps.json` hiç yoksa sunucu harita doğrulamasını **atlar** (geriye dönük uyumlu davranış).

### 11.1 Mekan seçimi (açılışta)

Bir sunucu kurulumu **tek bir işletmeye** hizmet eder, ama içerik projesi tüm işletmeleri tanır.
Bu yüzden sunucu açılırken **hangi mekanın oynatılacağı seçilir** ve o oturum boyunca sabit kalır.

```
Hangi mekan açılsın?
  1) Outdoor12x12  (3 harita)
  2) VortexAntep   (2 harita)
Seçim [1-2]:
```

**Mekan asset yolundan gelir, ayrı bir alan YOKTUR.** Export şu kuralı uygular:
`Assets/Arenas/Venues/<İşletme>/…` → o işletme. Klasör yerleşimi zaten mekanı anlatıyor; ikinci bir
alan eklemek onu unutulabilir hâle getirirdi. Bir haritayı yanlış mekana yazmanın tek yolu onu
yanlış klasöre koymaktır, o da gözle görülür.

⚠️ **Mekan klasörü dışındaki haritalar export'a HİÇ girmez.** `Assets/Arenas/Template/` altındakiler
(sihirbaz şablonları) sessizce atlanır; başka bir yerdeki `MapDefinition` ise uyarı basılarak
atlanır. Sebep: bu listenin her satırı operatörün açılışta seçebileceği gerçek bir işletmedir —
şablonlar ya da yanlış yere konmuş bir harita orada var olmayan bir mekan satırı açardı.

Seçim sırası: `--venue <ad>` → `server.json → venue` → tek mekan varsa o → **konsolda sor**.
Soru yalnız konsol etkileşimliyse sorulur; girdi yönlendirilmişse (servis, betik) sunucu
**bloklanmaz**, ilk mekanla açılır ve bunu loglar. Yazılan ad tanınmazsa yine sorulur — sessizce
başka bir mekanı açmak, operatörün yanlış arenaları görmesi demek olurdu.

⚠️ **Bu "ilk mekan" yolu bir emniyet subabıdır, kullanılacak yol değildir** — hangi mekanın
açıldığı yalnız logda görünür ve sahada kimse logu okumaz. Operatör launcher'ı bu yüzden mekan
seçilmeden sunucuyu **hiç başlatmaz** ve her açılışta `--venue` geçer; betikten/servisten
kaldırılacaksa `server.json → venue` doldurulur.

Seçimin üç sonucu:

| Nereye | Ne olur |
|---|---|
| `start_match` doğrulaması | Harita tablosu o mekana daraltılır → başka mekanın sahnesi "harita tablosunda yok" diye reddedilir. Ayrı bir kontrol yazılmaz |
| Admin harita seçicisi | `admin_state.venueId` + `venueScenes` ile bildirilir; admin **kendi yerel kataloğunu bununla süzer**. Katalog tüm projeyi tanır, oynatılabilir olana sunucu karar verir |
| Lobi | `server.json → lobbyScene` boşsa mekanın kendi lobi haritası (`modes:["lobby"]`) otomatik seçilir (§10.7) |

> ⚠️ **Mekan çalışırken DEĞİŞMEZ.** Başka bir mekana geçmek sunucuyu yeniden başlatmak demektir —
> bilinçli: kalibrasyon, lobi ve harita listesi hep birlikte o fiziksel odaya aittir, maç ortasında
> hepsini birden takas etmenin güvenli bir anlamı yok.
