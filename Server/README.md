# VortexArena Sunucusu

Free-roam VR PvP arenasını LAN'da yöneten bağımsız **.NET 10 konsol** sunucusu (offline/LAN, Mirror/NGO yok). VR (Quest) oyuncuları ve Windows admin istemcisi buna bağlanır. Pozlar istemci-otoriter (UDP 20 Hz, arena uzayında); can/skor/kurallar/maç fazları **sunucu-otoriter**dir.

> **Protokol tanımı:** `../Docs/ArenaNet-Protokol.md` (TEK doğruluk kaynağı). DTO'lar ve sabitler `../Assets/_Shared/Net/Protocol/` altındadır — `VortexArena.Server.Core.csproj` **aynı dosyaları** `<Compile Include>` ile derler. O dosyalara Unity API'si girerse bu build kırılır (bilinçli bekçi).

## Proje yapısı

```
Server/
  VortexArena.Server.sln
  VortexArena.Server.Core/    # Kestrel WS host, beacon, PlayerRegistry, LobbyService,
                              # StateHost (UDP), MatchDirector (faz makinesi + vuruş hattı),
                              # WeaponTable, MapTable, Modes/ (IGameMode, TdmMode)
  VortexArena.Server.App/     # konsol exe (UI YOK — yönetim UI'ı Unity admin build'i)
  VortexArena.PoseBot/        # sentetik oyuncu test istemcisi (poz senkronunu Quest'siz test eder)
  config/server.json          # portlar + mekan adı + tickHz (ELLE)
  config/weapons.json         # sunucu-otoriter silah tablosu (weaponId + damage + rpm) — Unity export
  config/maps.json            # harita tablosu (sceneName + boyut + slot + modes) — Unity export
  config/devices.json         # deviceId -> dostane ad ("Gözlük NN"); otomatik doldurulur
  firewall-kur.cmd            # Windows Firewall kuralları (yönetici olarak çalıştırın)
```

## Çalıştırma

```powershell
dotnet run --project Server/VortexArena.Server.App
```

veya derlenmiş exe: `Server/VortexArena.Server.App/bin/Debug/net10.0/VortexArena.Server.App.exe`

İşletme dağıtımı için: `scripts\deploy-server.bat` → `deploy\server\VortexArena.Server.App.exe`
(self-contained, .NET kurulumu gerekmez; `config/` yanında gider).

> Sunucu **her zaman elle** başlatılır. Ne Unity admin uygulaması ne Flutter launcher sunucuyu
> başlatır — ikisi de yalnız çalışan bir sunucuya bağlanır. Sebep: sunucu maçın tek otoritesidir,
> ömrü operatör uygulamasının ömrüne bağlanmamalıdır.

Açılışta:
- Kestrel `http://0.0.0.0:47821/ws` (WebSocket kontrol) dinler.
- UDP `47820`'ye her 2 sn beacon yayınlar → istemciler sunucuyu **kendiliğinden** bulur
  (elle girilen IP her zaman beacon'ı ezer).
- UDP `47822` state kanalını dinler: `0x00 UdpHello` kayıt + ack, `0x01 PoseUpdate` alımı,
  `0x02 Snapshot` yayını (20 Hz, kayıtlı tüm endpoint'lere — admin dahil). Poz akarken konsolda
  saniyede bir `[state] oyuncu N, pozlu N, snapshot N B, hedef N` özeti görünür.
- Maç tick döngüsü (10 Hz) çalışır: faz makinesi, geri sayım, süre, zorla canlandırma.
- `config/` bulunamazsa exe yanında oluşturulur ve varsayılanlarla doldurulur
  (`server.json` + `weapons.json`; `maps.json` **üretilmez** — o Unity export'undan gelir).
- Konsolda bağlanan/kopan cihazlar ve çevrimiçi sayısı akar; **Ctrl+C** temiz kapatır.

Açılış başlığında `Modlar : tdm`, `Silahlar : ak47, m4` ve `Haritalar : Arena10x10` satırları
kayıtlı mod/silah/harita tablosunu özetler (`maps.json` yoksa `Haritalar : yok (doğrulama kapalı)`).

## Portlar

| Port | Protokol | Amaç |
|---|---|---|
| 47820 | UDP | Keşif beacon'ı (sunucu → broadcast, 2 sn'de bir) |
| 47821 | TCP | WebSocket kontrol kanalı (`/ws`) |
| 47822 | UDP | State kanalı (UdpHello kaydı; Faz 2'de pozlar + snapshot) |

(cosmos'un 47800/47801'i ile bilerek çakışmaz.)

## Ağ kurulumu + Windows Firewall (ŞART — bir kez)

`Server/firewall-kur.cmd` dosyasına **sağ tık → "Yönetici olarak çalıştır"**. Bu betik:

1. **Ağ profilini Private yapar** (`Public` olan bağlantıları çevirir). Public profilde Defender
   gelen broadcast'i ve çoğu inbound'u keser → beacon hiç ulaşmaz.
2. Windows'un uygulama için otomatik eklediği **ENGELLE (Block)** kurallarını siler.
3. **UDP 47820** + **TCP 47821** + **UDP 47822** için **İZİN** kuralları ekler (Private + Domain).
   Sunucu exe'si derlenmişse ayrıca **programa özel** izin kuralı ekler (Windows'un yeniden Block
   kuralı üretmesini önler). Outbound Windows'ta zaten varsayılan serbesttir.
4. **Teşhis basar:** aktif adaptörler (birden fazlaysa uyarır), IPv4 adresleri, dinlenen portlar.

> **Bu betiği admin console çalıştıran DİĞER PC'lerde de çalıştırın.** Beacon bir *broadcast*
> paketidir; stateful UDP eşleşmesine takılmaz, istemcide inbound izin yoksa Windows onu sessizce
> düşürür ve sunucu listede görünmez.

Betiğin **yapamadıkları** (elle):
- **IP sabitleme** — router'da DHCP rezervasyonu (tercih) veya statik IP. IP değişirse
  `StreamingAssets/arena.json` ve gözlüklerdeki kayıtlı adres bozulur.
- **Tek aktif arayüz** — Ethernet + Wi-Fi aynı anda bağlıysa (veya VPN / Hyper-V / VMware / WSL
  sanal adaptörü varsa) beacon yanlış arayüzden yayılır ve gözlükler sunucuyu bulamaz.
  Kullanılmayanları `Disable-NetAdapter -Name "<Ad>"` ile kapatın.
- **AP ayarları** — 5 GHz, sabit kanal, client/AP isolation KAPALI.

**Bind doğrulaması** (sunucu çalışırken):
```powershell
netstat -ano | findstr 4782
```
`0.0.0.0:47821` **görmelisiniz**. `127.0.0.1:47821` görürseniz sunucu yalnız loopback'e bind
olmuştur ve dışarıdan hiçbir cihaz bağlanamaz.

> Sunucuyu ilk kez firewall kuralları OLMADAN açarsanız Windows bir "izin ver?" sorusu gösterir.
> **"İzin ver"e** basın. İptal ederseniz Windows kalıcı bir engelle kuralı ekler → sonra
> `firewall-kur.cmd`'yi çalıştırıp düzeltin.

## Ağ (AP) kontrol listesi — gerçek arena

- Sunucu PC tercihen **kablolu (GbE)** + **statik IP**; ağ profili **Özel (Private)** olmalı.
- Erişim noktası: **5 GHz**, **client isolation KAPALI** (cihazlar sunucuyu görmeli),
  tercihen Wi-Fi 6, arenaya özel SSID; tüm gözlükler bu SSID'de.
- Beacon kesen/izole eden ağlarda: her gözlükte `Assets/StreamingAssets/arena.json` içine
  sunucunun statik IP'si yazılır (`{"serverIp":"192.168.x.y","serverPort":47821}`) —
  beacon yoksa istemci buna düşer; son kurtarma yolu lobide sağ kumandada **A×2** ile açılan
  gizli IP panelidir. (Admin istemcisi beacon kullanmaz — adresi launcher `--server-ip` ile geçer.)

## Config dosyaları

**server.json** — portlar ArenaProtocol sabitleriyle aynı varsayılanlardadır; mekana özel
kurulumda genelde yalnız `venueName` değişir:
```json
{ "controlPort": 47821, "beaconPort": 47820, "statePort": 47822, "venueName": "Dev", "tickHz": 20 }
```

> **`weapons.json` ve `maps.json` Unity'den export edilir** — Unity'de
> `Tools > VortexArena > Export Server Config` menüsü bu iki dosyayı `WeaponDefinition` /
> `MapDefinition` SO'larından üretir. **Elle düzenlemeyin: bir sonraki export değişikliğinizi
> ezer.** Tek doğruluk kaynağı Unity SO'larıdır; çıktı deterministiktir (alfabetik, LF,
> UTF-8 BOM'suz) → git diff'leri temiz kalır.

**weapons.json** — sunucu-otoriter silah tablosu (§10.3). Hasar HER ZAMAN buradan uygulanır;
istemcinin bildirdiği değer saparsa uyumsuzluk konsola yazılır ve tablo kazanır (export unutulmuşsa
bu satır yakalar). `rpm`, `hit_report` hız denetiminde kullanılır (iki kabul edilen vuruş arası
≥ `60/rpm × 0.8` sn). Dosya yoksa varsayılanlarla oluşturulur:
```json
{ "weapons": [ { "weaponId": "ak47", "damage": 34, "rpm": 600 },
               { "weaponId": "m4", "damage": 22, "rpm": 800 } ] }
```
Yeni silah eklerken: prefab + `WeaponDefinition` SO (Unity) → **export'u çalıştırın**.

**maps.json** — harita tablosu (§10.1): `start_match`'te `sceneName`'in bilinen bir harita olup
olmadığı ve o haritanın modu destekleyip desteklemediği buradan doğrulanır; `spawnSlotsPerTeam`
ile `load_match.spawnSlot` sahnede gerçekten var olan slot aralığına sarılır (modulo).
```json
{ "maps": [ { "sceneName": "Arena10x10", "sizeX": 10, "sizeZ": 10,
              "spawnSlotsPerTeam": 4, "modes": ["tdm"] } ] }
```
`modes` boş bırakılırsa harita tüm modları kabul eder. **Dosya yoksa oluşturulmaz** (sunucunun
uyduracağı harita listesi yoktur): tablo boş kalır, harita doğrulaması ve slot sınırı devre dışı
kalır (Faz 3 davranışı) ve açılış özetinde `Haritalar : yok (doğrulama kapalı)` görünür.

**devices.json** — `{ "<deviceId>": "Gözlük 07" }`. Bilinmeyen player cihazı bağlanınca ilk boş
`Gözlük NN` atanır ve dosyaya yazılır; `set_name` ile değişen ad da buraya kalıcı yazılır.
UTF-8, BOM'suz.

## Maç akışı (Faz 3) — konsolda ne görünür

Kural otoritesi tamamen sunucudadır (`MatchDirector` + `Modes/<X>Mode.cs`): istemci hasar
uygulamaz, skor tutmaz, faz değiştirmez. Faz makinesi
`Lobby → Loading → Countdown(5) → Live → End(10 sn) → Lobby` (detay: `../Docs/ArenaNet-Protokol.md` §10).

Admin `start_match` yolladığında sunucu şunları doğrular: mod kayıtlı mı, `sceneName`
`config/maps.json`'da var mı ve o harita bu modu destekliyor mu (tablo boşsa bu adım atlanır),
en az 1 çevrimiçi oyuncu var mı, `sceneName` TÜM çevrimiçi oyuncuların `hello.scenes` listesinde
mi. Geçerse takımlar dengelenir (2+ oyuncuda boş takım kalmaz; tek oyuncuda uyarıyla izin verilir)
ve herkese KİŞİSEL `load_match` (`yourTeam` + takım içi 0 tabanlı `spawnSlot`, harita biliniyorsa
`spawnSlotsPerTeam` ile modulo) gider — `load_match` yalnız `role=player`'a; admin fazı
`match_state`'ten öğrenir.

`[match]` önekli konsol satırları:

| Satır | Anlamı |
|---|---|
| `faz Lobby → Loading` | her faz değişiminde (ayrıca herkese `match_state` yayınlanır) |
| `start_match: mod 'tdm', sahne 'Arena10x10' (10×10, 4 slot/takım), 2 oyuncu (kırmızı 1 / mavi 1)` | maç kuruldu (parantez içi yalnız harita tablodaysa) |
| `start_match reddedildi: …` | doğrulama düştü, faz değişmedi (ör. `'Arena12x12' harita tablosunda yok`) |
| `takım dengeleme: 1 oyuncu 'blue' takımına taşındı` | boş takım kalmasın diye |
| `loading zaman aşımı (20 sn) — hazır olmayanlar: Gözlük 03` | sahne yükleme beklenmedi |
| `hit_report reddedildi (Gözlük 03 → 5): dost ateşi yok` | §10.3 doğrulamalarından biri düştü |
| `hasar uyumsuz: … tablo uygulandı` | istemci `damage`'ı weapons.json ile uyuşmuyor |
| `öldürme: Gözlük 03 → Gözlük 05 (ak47) — skor kırmızı 4 : mavi 2` | doğrulanmış öldürme |
| `canlandı: Gözlük 05` / `zorla canlandırma: Gözlük 05` | `revive_request` / `REVIVE_GRACE` |
| `maç sonu — kazanan: blue (kırmızı 12 : mavi 30)` | `match_end` yayınlandı |

Kabul edilen vuruşların hasar satırı **yazılmaz** (saniyede onlarca satır olurdu); yalnız
öldürme + ret satırları loglanır. Ret satırları da atıcı başına **2 sn'de bir** yazılır (istemciler
ölü hedefe ateş etmeyi sürdürür); aradaki bastırılan retler yutulmaz, sayıları bir sonraki satırın
sonuna `(+N bastırıldı)` olarak eklenir. `revive_request` reddi tamamen sessizdir (istemci ~1 sn'de
bir tekrarlar; takılan istemciyi `REVIVE_GRACE` zorla canlandırma satırı yakalar).

**Free-roam respawn:** oyuncu ışınlanamaz → canlanma konum değil DURUM değişimidir. Ölünce
kurbana `respawn{spawnSlot, delaySeconds:5}` gider; oyuncu süre dolduktan sonra kendi tabanına
fiziken girip `revive_request` yollar; sunucu doğrulayıp `health_update{hp:100, attackerId:0}`
yayınlar. Talep 20 sn (`REVIVE_GRACE`) gelmezse sunucu zorla canlandırır (maç kilitlenmesin).

**Yeni mod eklemek:** `Modes/<Ad>Mode.cs` içinde `IGameMode` uygula → `MatchDirector` ctor'undaki
`Register(new <Ad>Mode())` satırına ekle → `../Docs/ArenaNet-Protokol.md`'ye modId işle →
Unity tarafında `Assets/Modes/<Ad>/` kutusunu aç (CLAUDE.md reçetesi).

## PoseBot — sentetik oyuncu (test)

Quest olmadan poz senkronunu uçtan uca denemek için:

```powershell
dotnet run --project Server/VortexArena.PoseBot -- 127.0.0.1 2                  # 2 bot, yalnız poz
dotnet run --project Server/VortexArena.PoseBot -- 127.0.0.1 4 --fight          # 4 bot, savaşarak
dotnet run --project Server/VortexArena.PoseBot -- 127.0.0.1 2 --fight --admin  # + maçı başlatan admin
```

Her bot player rolüyle WS'e bağlanır, UDP kaydını yapar ve 20 Hz'de dairesel yürüyüş pozu
gönderir (bot başına farklı yarıçap/faz). Editor'de admin bağlanınca taktik görünümde,
player bağlanınca lobide hayalet avatar olarak görünürler. Botların yazdığı `devices.json`
girdilerini commit'lemeyin (test kirliliği). Kullanım: `PoseBot [ip] [botSayısı] [--fight] [--admin]`
(bayrak sırası serbest, `--help` kısa kullanım basar).

**`--fight`** botları maça da katar: `load_match` gelince 0.5–1.5 sn "sahne yükleniyor"
simülasyonundan sonra `set_ready` gönderir, faz `Live` olunca saniyede 2 kez `shot_fired` +
`hit_report` (ak47, 34 hasar — `config/weapons.json` ile aynı olmalı) yollar, ölünce
`respawn.delaySeconds` + 1 sn sonra `revive_request` ile canlanır (free-roam "tabana dön"
akışının bot karşılığı). **Yalnız çift indeksli botlar ateş eder** (bot0, bot2…), tekler kurbandır;
böylece skor tek yönlü ve okunur ilerler. Konsolu boğmamak için maç akışı satırlarını yalnız
bot0 yazar. `--fight` verilmezse bot `set_ready` göndermez → maç Loading fazında `LOADING_TIMEOUT`
(20 sn) bekler; savaş testlerinde bayrağı hep verin.

**`--admin`** botlara ek olarak tek bir `role=admin` bağlantısı açar: roster'da 2+ çevrimiçi
oyuncu 2 sn kararlı kalınca kendiliğinden `start_match{tdm, Arena10x10}` gönderir, maç akışını
`[admin]` önekiyle yazar, konsolda `q` + Enter ile `abort_match` gönderir. Unity editörü **oyuncu**
rolündeyken ortamda admin kalmadığı için E2E'nin bu ayağında şarttır.

> Botun bildirdiği `hello.scenes`, Build Settings listesidir (`Boot, Lobby, AdminConsole,
> Arena10x10, Arena12x12, ArenaDemoVenue, IceWorld`) — sunucu `start_match`'te sahneyi tüm
> oyuncuların listesinde aradığı için yeni arena eklendiğinde PoseBot'taki `BuildScenes` sabiti de
> güncellenmelidir.

## Faz durumu

- **Faz 1:** beacon + WS kontrol + lobi (roster/ready/takım/kick/identify) +
  UDP kayıt. Loopback E2E: sunucuyu başlat → Editor'de admin bağlan → roster'da görün.
- **Faz 2 (tamam):** `0x01 PoseUpdate` alımı (kayıtlı endpoint + u16 seq sarmalama kontrolü) +
  `0x02 Snapshot` yayını (20 Hz, tek pakette 16 oyuncu ≈ 1382 B) + PoseBot test istemcisi.
- **Faz 3:** MatchDirector faz makinesi (`load_match` → countdown → Live → End → lobi) +
  `Modes/TdmMode.cs` (`IGameMode`) + `config/weapons.json` ile sunucu-otoriter vuruş doğrulama,
  can/skor yayını ve free-roam canlanma. Snapshot `flags` bit0 artık gerçek `alive` durumunu taşır.
