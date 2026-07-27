# VortexArena Sunucusu

Free-roam VR PvP arenasını LAN'da yöneten bağımsız **.NET 10 konsol** sunucusu (offline/LAN, Mirror/NGO yok). VR (Quest) oyuncuları ve Windows admin istemcisi buna bağlanır. Pozlar istemci-otoriter (UDP 20 Hz, arena uzayında); can/skor/kurallar/maç fazları **sunucu-otoriter**dir.

> **Protokol tanımı:** `../Docs/ArenaNet-Protokol.md` (TEK doğruluk kaynağı). DTO'lar ve sabitler `../Assets/_Shared/Net/Protocol/` altındadır — `VortexArena.Server.Core.csproj` **aynı dosyaları** `<Compile Include>` ile derler. O dosyalara Unity API'si girerse bu build kırılır (bilinçli bekçi).

## Proje yapısı

```
Server/
  VortexArena.Server.sln
  VortexArena.Server.Core/    # Kestrel WS host, beacon, PlayerRegistry, LobbyService,
                              # StateHost (UDP), MatchDirector (faz makinesi + vuruş hattı),
                              # MapTable, Modes/ (IGameMode, TdmMode)
  VortexArena.Server.App/     # konsol exe (UI YOK — yönetim UI'ı Unity admin build'i)
  VortexArena.PoseBot/        # sentetik oyuncu test istemcisi (poz senkronunu Quest'siz test eder)
  config/server.json          # portlar + mekan adı + tickHz (ELLE)
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
  `0x02 Snapshot` yayını (20 Hz, kayıtlı tüm endpoint'lere — **her admin ayrı hedeftir**).
  16'dan fazla pozlu oyuncu varsa aynı tik MTU'ya sığan parçalara bölünür (istemcide birleştirme
  gerekmez). Poz akarken konsolda saniyede bir
  `[state] oyuncu N, pozlu N, snapshot N B [(K parça)], hedef N` özeti görünür.
- Maç tick döngüsü (10 Hz) çalışır: faz makinesi, geri sayım, süre, zorla canlandırma.
- `config/` bulunamazsa exe yanında oluşturulur ve varsayılanlarla doldurulur
  (`server.json`; `maps.json` **üretilmez** — o Unity export'undan gelir).
- Konsolda bağlanan/kopan cihazlar ve çevrimiçi sayısı akar; **Ctrl+C** temiz kapatır.

Açılış başlığında `Modlar : tdm` ve `Haritalar : Arena10x10` satırları kayıtlı mod/harita
tablosunu özetler (`maps.json` yoksa `Haritalar : yok (doğrulama kapalı)`); `Hasar : istemci
bildirir` satırı sunucuda silah tablosu ve hile denetimi olmadığını hatırlatır (§10.3).

## Portlar

| Port | Protokol | Amaç |
|---|---|---|
| 47820 | UDP | Keşif beacon'ı (sunucu → broadcast, 2 sn'de bir) |
| 47821 | TCP | WebSocket kontrol kanalı (`/ws`) |
| 47822 | UDP | State kanalı (UdpHello kaydı + pozlar/snapshot) |

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

> **`maps.json` Unity'den export edilir** — Unity'de `Tools > VortexArena > Export Server Config`
> menüsü onu `MapDefinition` SO'larından üretir. **Elle düzenlemeyin: bir sonraki export
> değişikliğinizi ezer.** Tek doğruluk kaynağı Unity SO'larıdır; çıktı deterministiktir
> (alfabetik, LF, UTF-8 BOM'suz) → git diff'leri temiz kalır.

> **`weapons.json` YOK** (v1'de vardı, kaldırıldı — §10.3). Sunucu silah tanımı tutmaz: hasarı
> istemci hesaplar, `hit_report.damage` ile bildirir ve sunucu aynen uygular. Denge sayıları
> yalnız Unity'deki `WeaponDefinition` SO'larında yaşar → **yeni silah eklerken sunucuya hiçbir
> şey tanıtılmaz ve export gerekmez** (balta, yay, bomba, tuzak, düşme hasarı… hepsi aynı yolu
> kullanır). Bedeli: denge değişikliği istemci build'i ister.

**maps.json** — harita tablosu (§10.1): `start_match`'te `sceneName`'in bilinen bir harita olup
olmadığı ve o haritanın modu destekleyip desteklemediği buradan doğrulanır; `spawnSlotsPerTeam`
ile `load_match.spawnSlot` sahnede gerçekten var olan slot aralığına sarılır (modulo).
```json
{ "maps": [ { "sceneName": "Arena10x10", "sizeX": 10, "sizeZ": 10,
              "spawnSlotsPerTeam": 4, "modes": ["tdm"] } ] }
```
`modes` boş bırakılırsa harita tüm modları kabul eder. **Dosya yoksa oluşturulmaz** (sunucunun
uyduracağı harita listesi yoktur): tablo boş kalır, harita doğrulaması ve slot sınırı devre dışı
kalır ve açılış özetinde `Haritalar : yok (doğrulama kapalı)` görünür.

**devices.json** — `{ "<deviceId>": "Gözlük 07" }`. Bilinmeyen player cihazı bağlanınca ilk boş
`Gözlük NN` atanır ve dosyaya yazılır; `set_name` ile değişen ad da buraya kalıcı yazılır.
UTF-8, BOM'suz. ⚠️ **Admin adları buraya YAZILMAZ** — admin `deviceId`'si oturumlukttur (aşağı),
her açılış dosyaya çöp bir satır eklerdi.

## Çoklu admin

Eşzamanlı admin sayısında **sınır yoktur** ve hepsi eş yetkilidir (birincil/ikincil admin
kavramı yok): `role=="admin"` olan her bağlantı §5.2'deki tüm komutları gönderebilir, son gelen
komut uygulanır.

- **Kimlik:** admin `deviceId`'si `<donanım>:admin:<oturum GUID'i>` — oturum başına benzersizdir.
  Aynı fiziksel PC'de iki admin penceresi açılabilsin diye: ortak kimlikle ikisi aynı kaydı
  paylaşır ve her `hello` diğerinin soketini kapatırdı (sonsuz kick döngüsü).
- **Kayıt kalıcılığı:** admin bağlantısı kopunca (ya da `OFFLINE_TIMEOUT` dolunca) kaydı
  **tümüyle silinir**, `playerId`'si havuza döner (konsolda `[-] … kaydı silindi`). Oyuncu kayıtları
  eskisi gibi çevrimdışı işaretlenir ama durur. Aynı PC'de iki admin varsa roster adları
  `Ofis-PC`, `Ofis-PC (2)` diye ayrıştırılır.
- **Ortak durum:** bir sonraki maçın mod/harita seçimi **sunucuda** yaşar. Admin arayüzü onu
  `set_selection` ile değiştirir, sunucu `admin_state` ile TÜM adminlere yayar → bir operatör
  haritayı değiştirdiğinde diğerinin paneli ve yerel önizlemesi de değişir. `start_match` de
  seçimi günceller. Her admin komutu `admin_state.notice` ile "kim ne yaptı" satırı üretir.
- **Yerel kalanlar:** kamera kipi, seçili oyuncu, halkalar/ad etiketleri, kamera hızı, duvar ve
  çatı saydamlığı, mini harita — bunlar protokole girmez, her operatörün kendi ekranına aittir.

## Maç akışı — konsolda ne görünür

Kural otoritesi tamamen sunucudadır (`MatchDirector` + `Modes/<X>Mode.cs`): istemci hasar
uygulamaz, skor tutmaz, faz değiştirmez. Faz makinesi
`Lobby → Loading → Countdown(5) → Live → End(10 sn) → Lobby` (detay: `../Docs/ArenaNet-Protokol.md` §10).

Admin `start_match` yolladığında sunucu şunları doğrular: mod kayıtlı mı, `sceneName`
`config/maps.json`'da var mı ve o harita bu modu destekliyor mu (tablo boşsa bu adım atlanır),
`sceneName` TÜM çevrimiçi oyuncuların `hello.scenes` listesinde mi. **Oyuncu sayısı şart
DEĞİLDİR:** hiç oyuncu yokken de başlatılabilir (konsolda uyarı) — admin gözlemcinin haritayı boş
arenada açması için. Geçerse takımlar dengelenir (2+ oyuncuda boş takım kalmaz; 0/1 oyuncuda
uyarıyla izin verilir)
ve her oyuncuya KİŞİSEL `load_match` (`yourTeam` + takım içi 0 tabanlı `spawnSlot`, harita
biliniyorsa `spawnSlotsPerTeam` ile modulo) gider. **Çevrimiçi adminlere de bir kopya gider**
(`yourTeam:""`, `spawnSlot:-1`) — admin gözlemci aynı sahneyi yükler; admin `set_ready`
GÖNDERMEDİĞİ için Loading kapısı etkilenmez (kapı yalnız `role=player` sayar).

**Oyuncusuz maç:** `load_match` yalnız adminlere gider, Loading'de beklenecek `set_ready`
olmadığı için faz doğrudan Countdown'a geçer. Oyuncularla BAŞLAMIŞ bir maçta Loading sırasında son
oyuncu da düşerse sunucu lobiye döner; oyuncusuz BAŞLATILMIŞ maçta dönmez — çıkış `abort_match` /
`return_to_lobby`.

Ölüm ve canlanmadan sonra sunucu `lobby_state`'i bir kez tazeler: `kills`/`deaths`/`hp`/`alive`
alanları roster ile taşınıyor ve admin istatistik tablosunun sağlama noktası bu (§5.3).

`[match]` önekli konsol satırları:

| Satır | Anlamı |
|---|---|
| `faz Lobby → Loading` | her faz değişiminde (ayrıca herkese `match_state` yayınlanır) |
| `start_match: mod 'tdm', sahne 'Arena10x10' (10×10, 4 slot/takım), 2 oyuncu (kırmızı 1 / mavi 1)` | maç kuruldu (parantez içi yalnız harita tablodaysa) |
| `start_match reddedildi: …` | doğrulama düştü, faz değişmedi (ör. `'Arena12x12' harita tablosunda yok`) |
| `takım dengeleme: 1 oyuncu 'blue' takımına taşındı` | boş takım kalmasın diye |
| `loading zaman aşımı (20 sn) — hazır olmayanlar: Gözlük 03` | sahne yükleme beklenmedi |
| `hit_report reddedildi (Gözlük 03 → 5): dost ateşi yok` | §10.3 tutarlılık kontrollerinden biri düştü |
| `öldürme: Gözlük 03 → Gözlük 05 (ak47) — skor kırmızı 4 : mavi 2` | doğrulanmış öldürme |
| `canlandı: Gözlük 05` / `zorla canlandırma: Gözlük 05` | `revive_request` / `REVIVE_GRACE` |
| `maç sonu — kazanan: blue (kırmızı 12 : mavi 30)` | `match_end` yayınlandı |

Kabul edilen vuruşların hasar satırı **yazılmaz** (saniyede onlarca satır olurdu); yalnız
öldürme + ret satırları loglanır. Ret satırları da atıcı başına **2 sn'de bir** yazılır (istemciler
ölü hedefe ateş etmeyi sürdürür); aradaki bastırılan retler yutulmaz, sayıları bir sonraki satırın
sonuna `(+N bastırıldı)` olarak eklenir. `revive_request` reddi tamamen sessizdir (istemci ~1 sn'de
bir tekrarlar; takılan istemciyi `REVIVE_GRACE` zorla canlandırma satırı yakalar).

**Free-roam respawn:** oyuncu ışınlanamaz → canlanma konum değil DURUM değişimidir. Ölünce
kurbana `respawn{spawnSlot, delaySeconds}` gider (`delaySeconds` = modun `Rules.RespawnDelay`'i);
oyuncu süre dolduktan sonra **modun canlanma şartını** sağlayıp `revive_request` yollar; sunucu
doğrulayıp `health_update{hp:100, attackerId:0}` yayınlar. Talep 20 sn (`REVIVE_GRACE`) gelmezse
sunucu zorla canlandırır (maç kilitlenmesin).
⚠️ Şartın kendisi (**tabanda mı / sabit mi durdu**) sunucuda **doğrulanmaz** — sunucu hakemlik
değil defter tutar (§10.3 felsefesi); faz + ölü + gecikme kontrolüyle yetinir.

**Maç parametreleri:** `start_match.roundSeconds`/`scoreLimit` doluysa o maç bu değerlerle koşar,
boş/`0` ise modun varsayılanı (`DefaultRoundSeconds`/`DefaultScoreLimit`) kullanılır. Yani modun
sayıları **kilit değil varsayılandır** — operatör raundu kısaltıp uzatabilir. Seçim mod/harita ile
aynı ortak kanaldan (`set_selection` → `admin_state`) gider, böylece iki operatör sapmaz.

**Yeni mod eklemek:**
1. `Modes/<Ad>Mode.cs` içinde `IGameMode` uygula.
2. **`Rules`** döndür — modun şekli (`ModeRules`): `Teams` (takımlı/takımsız), `Scoring` (takım
   skoru / bireysel), `FriendlyFire`, `Revive` (kendi tabanı / sabit dur), `Weapons`,
   `RespawnDelay`. Bugünkü TDM davranışı için `ModeRules.TeamDefault` tek satırdır; yalnız FARKLI
   olan alanı yaz. Bu kural `load_match.rules` ile istemciye gider (§10.5).
3. **`IsMatchOver(d, out MatchOutcome outcome)`** — kazanan takım (`MatchOutcome.Team("red")`)
   **veya** kazanan oyuncu (`MatchOutcome.Player(id)`), berabere için `MatchOutcome.Draw`.
   Hangisinin dolacağını `Rules.Scoring` belirler; ikisi birden doldurulmaz.
4. Skoru **yalnız director'ın skor defterinden** yaz: `AddScore(team, n)` (takım) /
   `AddPlayerScore(playerId, n)` (bireysel); okuma `ScoreRed`/`ScoreBlue`/`ScoreOf`/`TryGetLeader`.
5. `MatchDirector.RegisterModes()` içine `Register(new <Ad>Mode())` satırını ekle.
6. `../Docs/ArenaNet-Protokol.md`'ye modId işle → Unity tarafında `Assets/Modes/<Ad>/` kutusunu aç
   (CLAUDE.md reçetesi).

`OnTick`/`OnHitApplied`/`OnKill` **varsayılan gövdelidir** — ilgilenmeyen mod hiç yazmaz. Yeni bir
kanca eklerken de varsayılan gövde kullan (mevcut modların hiçbiri değişmesin) ve **tüketicisi
olmayan kancayı hiç ekleme**.

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
`hit_report` (ak47 etiketi, 34 hasar — sunucu doğrulamaz, bildirileni uygular) yollar, ölünce
`respawn.delaySeconds` + 1 sn sonra `revive_request` ile canlanır (free-roam "tabana dön"
akışının bot karşılığı). **Yalnız çift indeksli botlar ateş eder** (bot0, bot2…), tekler kurbandır;
böylece skor tek yönlü ve okunur ilerler. Konsolu boğmamak için maç akışı satırlarını yalnız
bot0 yazar. `--fight` verilmezse bot `set_ready` göndermez → maç Loading fazında `LOADING_TIMEOUT`
(20 sn) bekler; savaş testlerinde bayrağı hep verin.

**`--admin`** botlara ek olarak tek bir `role=admin` bağlantısı açar: roster'da 2+ çevrimiçi
oyuncu 2 sn kararlı kalınca kendiliğinden `start_match{tdm, Arena10x10}` gönderir, maç akışını
`[admin]` önekiyle yazar, konsolda `q` + Enter ile `abort_match` gönderir. Unity editörü **oyuncu**
rolündeyken ortamda başka admin kalmadığı için loopback denemelerinde bu bayrak şarttır.

> Botun bildirdiği `hello.scenes`, Build Settings listesidir (`Boot, Lobby,
> Arena10x10, Arena12x12, ArenaDemoVenue, IceWorld`) — sunucu `start_match`'te sahneyi tüm
> oyuncuların listesinde aradığı için yeni arena eklendiğinde PoseBot'taki `BuildScenes` sabiti de
> güncellenmelidir.

## Sunucu bugün ne yapıyor

- **Keşif + kontrol:** UDP beacon yayını, WS kontrol kanalı, lobi (roster / ready / takım /
  kick / identify), cihaz adı kalıcılığı.
- **Poz kanalı:** `0x01 PoseUpdate` alımı (kayıtlı endpoint + u16 seq sarmalama kontrolü) +
  `0x02 Snapshot` yayını (20 Hz; oyuncu sayısı sınırsız, datagram başına en fazla
  `SNAPSHOT_MAX_ENTRIES_PER_PACKET = 16` girdi ≈ 1382 B, fazlası aynı tik içinde ek datagramlara
  bölünür). Snapshot `flags` bit0 gerçek `alive` durumunu taşır.
- **Maç:** `MatchDirector` faz makinesi (`load_match` → Countdown → Live → End → lobi) +
  `Modes/TdmMode.cs` (`IGameMode`) + vuruş hattı, can/skor yayını, free-roam canlanma.
- **Hasar modeli:** sunucuda silah tablosu YOKTUR; hasarı istemci hesaplar, sunucu aynen uygular
  (`weaponId` yalnız etiket) — `../Docs/ArenaNet-Protokol.md` §10.3.
