# VortexArena Sunucusu

Free-roam VR PvP arenasını LAN'da yöneten bağımsız **.NET 10 konsol** sunucusu (offline/LAN, Mirror/NGO yok). VR (Quest) oyuncuları ve Windows admin istemcisi buna bağlanır. Pozlar istemci-otoriter (UDP 20 Hz, arena uzayında); can/skor/kurallar/maç fazları **sunucu-otoriter**dir.

> **Protokol tanımı:** `../Docs/ArenaNet-Protokol.md` (TEK doğruluk kaynağı). DTO'lar ve sabitler `../Assets/_Shared/Net/Protocol/` altındadır — `VortexArena.Server.Core.csproj` **aynı dosyaları** `<Compile Include>` ile derler. O dosyalara Unity API'si girerse bu build kırılır (bilinçli bekçi).

## Proje yapısı

```
Server/
  VortexArena.Server.sln
  VortexArena.Server.Core/    # Kestrel WS host, beacon, PlayerRegistry, LobbyService,
                              # StateHost (UDP), MatchDirector (faz makinesi + vuruş hattı),
                              # MapTable, KindTable, World/ (WorldObjectTable),
                              # Modes/ (IGameMode, TdmMode, FfaMode)
  VortexArena.Server.App/     # konsol exe (UI YOK — yönetim UI'ı Unity admin build'i)
  config/server.json          # portlar + mekan adı + tickHz + venue + lobbyScene + burger (ELLE)
  config/maps.json            # harita tablosu (sceneName + venue + gameType + modes + objects) + kinds[] — Unity export
  config/devices.json         # deviceId -> { ad, forma numarası }; otomatik doldurulur
  firewall-kur.cmd            # Windows Firewall kuralları (yönetici olarak çalıştırın)
```

## Çalıştırma

```powershell
dotnet run --project Server/VortexArena.Server.App
```

veya derlenmiş exe: `Server/VortexArena.Server.App/bin/Debug/net10.0/VortexArena.Server.App.exe`

İşletme dağıtımı için: `scripts\deploy-server.bat` → `deploy\server\VortexArena.Server.App.exe`
(self-contained, .NET kurulumu gerekmez; `config/` yanında gider).

> Sunucuyu **operatör launcher'ı da başlatabilir** (`--venue <mekan>` ile; bkz. `launcher/README.md`).
> Unity admin uygulaması başlatmaz — yalnız çalışan bir sunucuya bağlanır. Launcher da sunucuyu
> **kapatmaz**: sunucu maçın tek otoritesidir, ömrü operatör uygulamasının ömrüne bağlanmaz —
> kapatma sunucunun kendi penceresinde Ctrl+C'dir.

Açılışta:
- Kestrel `http://0.0.0.0:47821/ws` (WebSocket kontrol) dinler.
- UDP `47820`'ye her 2 sn beacon yayınlar → istemciler sunucuyu **kendiliğinden** bulur
  (elle girilen IP her zaman beacon'ı ezer).
- UDP `47822` state kanalını dinler: `0x00 UdpHello` kayıt + ack, `0x01 PoseUpdate` alımı,
  `0x02 Snapshot` yayını (20 Hz, kayıtlı tüm endpoint'lere — **her admin ayrı hedeftir**).
  16'dan fazla pozlu oyuncu varsa aynı tik MTU'ya sığan parçalara bölünür (istemcide birleştirme
  gerekmez). Poz akarken konsolda saniyede bir
  `[state] oyuncu N, pozlu N, snapshot N B [(K parça)], hedef N` özeti görünür.
- Maç tick döngüsü (10 Hz) çalışır: faz makinesi, geri sayım, süre.
- `config/` bulunamazsa exe yanında oluşturulur ve varsayılanlarla doldurulur
  (`server.json`; `maps.json` **üretilmez** — o Unity export'undan gelir).
- Konsolda bağlanan/kopan cihazlar ve çevrimiçi sayısı akar; **Ctrl+C** temiz kapatır.

Açılış başlığında `Modlar : tdm, ffa, tournament` ve `Haritalar : …` satırları kayıtlı mod/harita
tablosunu özetler (`maps.json` yoksa `Haritalar : yok (doğrulama kapalı)`); `Lobi :` satırı
yapılandırılmış lobi sahnesini gösterir ve o sahne `maps.json`'da yoksa uyarır; `Hasar : istemci
bildirir` satırı sunucuda silah tablosu ve hile denetimi olmadığını hatırlatır (§10.3).

## Kapanış

Kapanışı **üç olay** tetikler ve üçü de aynı yoldan geçer (sıra ve süre aynı):

- konsolda **Ctrl+C**,
- **konsol penceresinin kapatılması** (ayrıca oturum kapatma / Windows kapanışı),
- sürecin normal sonlanması (`ProcessExit`) — launcher'dan durdurma da buraya düşer.

İkinci bir tetikleyici gelirse kapanış **yeniden koşmaz** (tek seferliktir).

Servisler bu **sırayla** durdurulur; her adım bir öncekinin yazdığı kanalı kapatmadan önce onu
susturur:

`lobby` (net_stats telemetrisi) → `director` (maç tik'i) → `stateHost` (UDP state) → `beacon`
→ `control` (istemcilere WebSocket close frame) → oyuncu kaydı (bağlantı zamanlayıcısı).

- Her servis, döngüsü gerçekten bitene kadar **beklenir**; tavan **servis başına 2 sn**. Tavan
  Windows'un konsol kapatma işleyicisine tanıdığı ~5 sn'ye göre seçilmiştir.
- Süre dolarsa tek satır düşer ve kapanış devam eder:
  `[kapanış] <servis> 2 sn'de durmadı — zorla.`
- **"Kapandı." son satırdır** — ondan sonra hiçbir döngü log yazmaz veya paket yayınlamaz.
- Süreç normal koşulda 3 sn içinde çıkar; çıkış kodu `0` (temiz kapanış), `2` (açılış doğrulaması
  başarısız).
- Bağlı başlıklar close frame aldığı için yeniden bağlanma ekranına düşer.

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
  beacon yoksa istemci buna düşer; son kurtarma yolu lobide sağ kumandada **joystick 1 sn
  basılı tutularak** açılan gizli IP panelidir. (Admin istemcisi beacon kullanmaz — adresi launcher `--server-ip` ile geçer.)

## Config dosyaları

**server.json** — portlar ArenaProtocol sabitleriyle aynı varsayılanlardadır; mekana özel
kurulumda genelde yalnız `venueName` ve (kiosk kurulumunda) `venue` değişir:
```json
{ "controlPort": 47821, "beaconPort": 47820, "statePort": 47822, "venueName": "Dev",
  "tickHz": 20, "venue": "", "lobbyScene": "" }
```

`venue` = **açılışta oynatılacak mekan** (§11.1). **Boş bırakılırsa sunucu açılırken konsolda
sorar** — normal saha kullanımı budur:

```
Hangi mekan açılsın?
  1) <mekan adı>   (3 harita)
  2) <mekan adı>   (2 harita)
Seçim [1-2]:
```

Seçilen mekan o oturum boyunca sabittir: yalnız onun haritaları `start_match` ile başlatılabilir
ve admin panelinin harita seçicisinde yalnız onlar görünür. Mekan `maps.json`'daki `venue`
alanından gelir, o da Unity'deki klasör yerleşiminden (`Assets/Arenas/Venues/<İşletme>/…`).
Listede yalnız gerçek mekanlar çıkar: mekan klasörü dışındaki haritalar (referans şablonlar
dahil) export'a hiç girmez.
Soruyu atlamak için `venue` doldurulur ya da `--venue <ad>` argümanı verilir; konsol etkileşimli
değilse (servis/betik) sunucu **bloklanmaz**, ilk mekanla açılır ve bunu loglar.

`lobbyScene` = lobi sahnesi (§10.7). **Boş bırakılırsa seçilen mekanın lobi haritası
(`modes:["lobby"]`) otomatik bulunur** — normalde boş kalır. Maç koşmadığı sürece oyuncular ve
admin lobide durur: birbirlerini görürler, kalibrasyonlarını orada yaparlar, silah alıp
hedeflere ateş edebilirler — birbirlerine hasar veremeden (`hit_report` yalnız `playing` fazında
işlenir; ateş serbestliği lobi türünün kuralıdır, `rules.fireWhilePaused`).

`burger` = **Hamburgerci denge bloğu** (isteğe bağlı; blok yoksa aşağıdaki varsayılanlar geçerli):
```json
{ "burger": { "customerIntervalStart": 25, "customerIntervalEnd": 12,
              "patienceStart": 150, "patienceEnd": 90,
              "cookSeconds": 20, "burnSeconds": 40, "servePoints": 10 } }
```
Süreler **saniye**, `servePoints` doğru servisin puanıdır. **Rampa:** müşteri geliş aralığı ve
sabır süresi vardiya boyunca `Start` değerinden `End` değerine **doğrusal** iner — ilerleme
vardiyanın geçen süresi / toplam süresidir, yani vardiya kısaltılınca rampa da kısalır. Sabır
müşteri **doğduğu anda** dondurulur: bekleyen müşterinin sabrı sonradan kısalmaz. Bozuk değer
(≤ 0 süre, `burnSeconds ≤ cookSeconds`, negatif puan) **varsayılana çekilir** ve sebebi açılışta
konsola yazılır. Bu kuralların hiçbiri telde yoktur — protokol değişmez.

> ⚠️ **Açık sahne çözülemezse sunucu AÇILMAZ.** `lobbyScene` boş ve mekanda lobi haritası yoksa,
> ya da yazılan sahne `maps.json`'da bulunmuyorsa sunucu sebebi + çözümü yazıp **çıkış kodu 2** ile
> kapanır. Sebep: sunucunun açık sahnesi istemcinin tek yönlendirme kaynağıdır
> (`welcome.match.sceneName`) — çözülemiyorsa zaten yapılandırma hatası vardır ve oyuncu doğru
> oynayamaz. (`maps.json` hiç yoksa doğrulamanın tamamı kapalıdır; sunucu uyarıp açılır.)

> **`maps.json` Unity'den export edilir** — Unity'de `Tools > VortexArena > Server > Export Server Config`
> menüsü onu `MapDefinition` SO'larından üretir. **Elle düzenlemeyin: bir sonraki export
> değişikliğinizi ezer.** Tek doğruluk kaynağı Unity SO'larıdır; çıktı deterministiktir
> (alfabetik, LF, UTF-8 BOM'suz) → git diff'leri temiz kalır.

> **`weapons.json` YOKTUR** (§10.3). Sunucu silah tanımı tutmaz: hasarı
> istemci hesaplar, `hit_report.damage` ile bildirir ve sunucu aynen uygular. Denge sayıları
> yalnız Unity'deki `WeaponDefinition` SO'larında yaşar → **yeni silah eklerken sunucuya hiçbir
> şey tanıtılmaz ve export gerekmez** (balta, yay, bomba, tuzak, düşme hasarı… hepsi aynı yolu
> kullanır). Bedeli: denge değişikliği istemci build'i ister.

**maps.json** — harita tablosu (§10.1): `start_match`'te `sceneName`'in bilinen bir harita olup
olmadığı, o haritanın modu destekleyip desteklemediği ve modun oyun tipiyle haritanınkinin uyuşup
uyuşmadığı buradan doğrulanır.
```json
{
  "maps": [ { "sceneName": "<Arena>", "venue": "<Mekan>", "gameType": "quickbattle",
              "modes": ["ffa", "tdm"],
              "objects": [ { "sceneId": 12, "kind": "crate_wood" } ] } ],
  "kinds": [ { "kind": "crate_wood", "maxHp": 60 } ]
}
```
`modes` boş bırakılırsa harita tüm modları kabul eder. `gameType` (`"quickbattle"` | `"kids"`) bir
üst katmandır — haritayı hangi oyun ailesinin kullandığını söyler ve modun tipiyle uyuşmazsa
`start_match` reddedilir; boş bırakılan/eski export `"quickbattle"` sayılır. Ağ nesneleri (§10.10)
iki yerden gelir:
harita girdisindeki `objects[]` o sahnede hangi kimlikte (`sceneId`) hangi türün olduğunu, kökteki
`kinds[]` ise türün kurallarını (`maxHp`; `0` = hasar almaz) söyler — kimlik listesi sahneye, tür
kuralı içeriğe aittir. Bilinmeyen `kind` ya da aralık dışı `sceneId` tabloya alınmaz (konsolda tek
satır). **Dosya yoksa oluşturulmaz** (sunucunun
uyduracağı harita listesi yoktur): tablo boş kalır, harita doğrulaması devre dışı kalır ve açılış
özetinde `Haritalar : yok (doğrulama kapalı)` görünür.

> Sunucu sahne GEOMETRİSİNİ bilmez: konum/spawn noktası taşıyan bir alan ne bu tabloda ne de
> protokolde vardır. Oyuncular fiziksel olarak yürür (§10.4). **Arena ÖLÇÜSÜ de yoktur:** sunucu
> metre kullanmaz ve her işletmenin alanı farklı, çoğu kare/dikdörtgen bile değil — tek bir ölçü
> çifti arenayı tarif etmez. Ölçü yalnız istemcide yaşar: sahnedeki `ArenaBoundary`'ye bağlı
> boyut dosyasında (JSON, köşe listesi + kolonlar).

**devices.json** — `{ "<deviceId>": { "name": "ertu", "number": 7 } }` (§2). Bilinmeyen player
cihazı bağlanınca **ad** 20 kişilik havuzdan rastgele (kullanılmayanlar arasından), **numara**
1'den itibaren ilk boş değer olarak atanır ve dosyaya yazılır; `set_identity` ile değişen değerler
de buraya kalıcı yazılır. UTF-8, BOM'suz.

⚠️ **Numara tüm kayıtlı cihazlar arasında benzersizdir** — yalnız çevrimiçiler arasında değil.
Admin bir numarayı çevrimiçi bir oyuncudan isterse reddedilir; çevrimdışı kayıtlı bir cihazdan
isterse o cihaz aynı anda ilk boş numaraya taşınır. Dosya elle bozulup çift numara içerirse
sunucu açılışta yeniden numaralandırır ve bunu loglar.

⚠️ Eski (v1) `{ "<deviceId>": "Gözlük 07" }` biçimi **okunur** — numara `0` sayılır ve cihaz ilk
bağlantısında numara alır; dosya ilk yazımda yeni biçime yükseltilir.

⚠️ **Admin adları buraya YAZILMAZ** — admin `deviceId`'si oturumlukttur (aşağı), her açılış
dosyaya çöp bir satır eklerdi. Admin'e numara da atanmaz (`number: 0`).

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
  haritayı değiştirdiğinde diğerinin paneli de değişir. `start_match` de
  seçimi günceller. Her admin komutu `admin_state.notice` ile "kim ne yaptı" satırı üretir.
  Seçilen **mod** değiştiğinde ayrıca `selection_state` yayılır — bu mesaj **herkese** gider ve tek
  bir alan taşır (`teamMode`): oyuncular lobide taban şeritlerini ona göre çizer (§5.3/§10.7).
  Harita/süre/limit dokunuşu bu yayını üretmez.
- **Harita seçimi = sahneleme:** maç koşmuyorken seçilen arena TÜM istemcilere yüklenir
  (`return_to_lobby`, konsolda `[match] lobi sahnesi -> '<sahne>'`) — faz `paused` kalır, maç
  başlamaz. Bu yüzden **mod/harita yalnız `playing` DEĞİLKEN değiştirilebilir** (`paused` ve
  `finished` serbest, yani maç bitince operatör bir sonrakini seçebilir); koşan maçta komut
  reddedilir (`[Lobby] set_selection reddedildi: maç sürüyor`) ve admin panelinde o iki satır
  pasiftir. Süre/limit her durumda değiştirilebilir. Ayrıntı: `../Docs/ArenaNet-Protokol.md` §10.7.
- **Yerel kalanlar:** kamera kipi, seçili oyuncu, halkalar/ad etiketleri, kamera hızı, duvar ve
  çatı saydamlığı — bunlar protokole girmez, her operatörün kendi ekranına aittir.

## Maç akışı — konsolda ne görünür

Kural otoritesi tamamen sunucudadır (`MatchDirector` + `Modes/<X>Mode.cs`): istemci hasar
uygulamaz, skor tutmaz, faz değiştirmez. **Faz üç değerdir:** `paused` · `playing` · `finished`.
Duraklamanın gerekçesi ayrı bir alandır (`phaseReason`: `lobby`/`loading`/`countdown`/`operator`/
`mode`), modun kendi ara durumu da öyle (`modeState`). Akış:
`paused(lobby) → paused(loading) → paused(countdown 5) → playing → finished → paused(lobby)`
(detay: `../Docs/ArenaNet-Protokol.md` §10.1). **Lobi bir faz değil bir türdür** — `modeId:"lobby"`,
yalnız lobi haritasında ve o türdeyken maç başlatılamaz.

`finished`'dan lobiye dönüşü operatör seçer (`return_to_lobby`/`abort_match`/yeni `start_match`);
`MATCH_END_SECONDS` yalnız bir emniyettir ve **sonucunu operatöre bırakan modda hiç işlemez**
(`IGameMode.HoldsResultForOperator` — bugün `tournament`). Aynı mod turlar arasında da operatörü
bekler: tur biter, sonuç `modeState`'te asılı kalır ve akış `mode_continue` gelene kadar ilerlemez.
Konsolda görünen satırlar: `[tournament] tur N sonucu operatör incelemesinde …` ve dakikada bir
`… hâlâ incelemede`.

Admin `start_match` yolladığında sunucu şunları doğrular: mod kayıtlı mı, `sceneName`
`config/maps.json`'da var mı ve o harita bu modu destekliyor mu (tablo boşsa bu adım atlanır),
`sceneName` TÜM çevrimiçi oyuncuların `hello.scenes` listesinde mi. **Oyuncu sayısı şart
DEĞİLDİR:** hiç oyuncu yokken de başlatılabilir (konsolda uyarı) — admin gözlemcinin haritayı boş
arenada açması için. Geçerse takımlar dengelenir (2+ oyuncuda boş takım kalmaz; 0/1 oyuncuda
uyarıyla izin verilir)
ve her oyuncuya KİŞİSEL `load_match` (`yourTeam` + maçın `rules`'ü) gider.
**Çevrimiçi adminlere de bir kopya gider** (`yourTeam:""`) — admin gözlemci aynı sahneyi yükler; admin `set_ready`
GÖNDERMEDİĞİ için Loading kapısı etkilenmez (kapı yalnız `role=player` sayar).

**Oyuncusuz maç:** `load_match` yalnız adminlere gider, yükleme kapısında beklenecek `set_ready`
olmadığı için doğrudan geri sayıma geçilir. Oyuncularla BAŞLAMIŞ bir maçta yükleme sırasında son
oyuncu da düşerse sunucu açık sahneye döner; oyuncusuz BAŞLATILMIŞ maçta dönmez — çıkış
`abort_match` / `return_to_lobby`.

Ölüm ve canlanmadan sonra sunucu `lobby_state`'i bir kez tazeler: `kills`/`deaths`/`hp`/`alive`
alanları roster ile taşınıyor ve admin istatistik tablosunun sağlama noktası bu (§5.3).

`[match]` önekli konsol satırları:

| Satır | Anlamı |
|---|---|
| `durum paused/lobby → paused/loading` | her durum değişiminde (ayrıca herkese `match_state` yayınlanır) |
| `start_match: mod 'tdm', sahne '<Arena>' (12×12), 2 oyuncu (kırmızı 1 / mavi 1)` | maç kuruldu (boyut yalnız harita tablodaysa) |
| `start_match reddedildi: …` | doğrulama düştü, durum değişmedi (ör. `'<Arena>' harita tablosunda yok`, `'lobby' modu kayıtlı değil`) |
| `pause_match yok sayıldı: faz paused` | duraklatma yalnız koşan maçta iş yapar (§5.2) |
| `resume_match reddedildi: durum paused/countdown` | yalnız operatörün duraklattığı maç sürdürülür — her duraklamayı sahibi kaldırır |
| `takım dengeleme: 1 oyuncu 'blue' takımına taşındı` | boş takım kalmasın diye |
| `loading zaman aşımı (20 sn) — hazır olmayanlar: Gözlük 03` | sahne yükleme beklenmedi |
| `hit_report reddedildi (Gözlük 03 → 5): dost ateşi yok` | §10.3 tutarlılık kontrollerinden biri düştü |
| `öldürme: Gözlük 03 → Gözlük 05 (ak47) — skor kırmızı 4 : mavi 2` | doğrulanmış öldürme |
| `canlandı: Gözlük 05` | `revive_request` kabul edildi |
| `maç sonu — kazanan: blue (kırmızı 12 : mavi 30)` | `match_end` yayınlandı |

Kabul edilen vuruşların hasar satırı **yazılmaz** (saniyede onlarca satır olurdu); yalnız
öldürme + ret satırları loglanır. Ret satırları da atıcı başına **2 sn'de bir** yazılır (istemciler
ölü hedefe ateş etmeyi sürdürür); aradaki bastırılan retler yutulmaz, sayıları bir sonraki satırın
sonuna `(+N bastırıldı)` olarak eklenir. `revive_request` reddi tamamen sessizdir (istemci ~1 sn'de
bir tekrarlar).

**Free-roam respawn:** oyuncu ışınlanamaz → canlanma konum değil DURUM değişimidir. Ölünce
kurbana `respawn{delaySeconds}` gider (`delaySeconds` = modun `Rules.RespawnDelay`'i);
oyuncu süre dolduktan sonra **modun canlanma şartını** sağlayıp `revive_request` yollar; sunucu
doğrulayıp `health_update{hp:100, attackerId:0}` yayınlar. ⚠️ **Canlandırmanın TEK yolu bu taleptir:**
sunucunun zamanlayıcı tabanlı bir canlandırması da, operatörün elle canlandırma komutu da yoktur —
şartı sağlamayan oyuncu maçın sonuna kadar ölü kalır ve bu bilinçlidir. Talep kalibrasyon, engel,
`reviveAnchor:"none"` ve gecikme yasaklarına tabidir (§10.4).
⚠️ Şartın kendisi (**tabanda mı / sabit mi durdu**) sunucuda **doğrulanmaz** — sunucu hakemlik
değil defter tutar (§10.3 felsefesi); faz + ölü + gecikme kontrolüyle yetinir.

**Maç parametreleri:** `start_match.roundSeconds`/`scoreLimit`/`countdownSeconds` doluysa o maç bu
değerlerle koşar, boş/`0` ise modun varsayılanı (`DefaultRoundSeconds`/`DefaultScoreLimit`) —
geri sayımda protokolün varsayılanı (`COUNTDOWN_SECONDS` = 5) — kullanılır. Yani modun
sayıları **kilit değil varsayılandır** — operatör raundu kısaltıp uzatabilir.
`scoreLimit` ayrıca `SCORE_LIMIT_UNLIMITED` (`-1`) olabilir: **sınırsız** maç — varsayılana
düşmez, hiçbir limit dalı çalışmaz (kapıların tamamı `limit > 0` diye sorar) ve tur tabanlı modda
tur tavanı da kalkar. Konsol satırı bu maçlarda `limit sınırsız` yazar. Seçim mod/harita ile
aynı ortak kanaldan (`set_selection` → `admin_state`) gider, böylece iki operatör sapmaz.
`countdownSeconds` sunucuda **5–30 sn** aralığına kırpılır ve maçın HER geri sayımında kullanılır
(tur tabanlı modda turlar arasındaki bekleme de odur).

**Kayıtlı modlar** (`MatchDirector.RegisterModes()`; tanınmayan `modeId`'li `start_match` reddedilir):

| `modeId` | Sınıf | Şekli (`Rules`) | Varsayılan süre / limit |
|---|---|---|---|
| `tdm` | `Modes/TdmMode.cs` | Tümüyle varsayılan (`ModeRules.TeamDefault`): iki takım, takım skoru, kendi tabanında canlanma, sahnede duran silah, 5 sn gecikme | 300 sn / 30 |
| `ffa` | `Modes/FfaMode.cs` | Takımsız · bireysel skor · sabit durarak canlanma · silahı mod dağıtır · gecikme 0 | 300 sn / 20 |
| `tournament` | `Modes/TournamentMode.cs` | TDM varsayılanından tek farkı: **canlanma yok** (`Revive = None`, gecikme 0). Tur tabanlı takım elemesi | 120 sn (**turun** süresi) / 4 tur (operatör **sınırsız** da seçebilir) |
| `burger` | `Modes/BurgerMode.cs` | **Oyun tipi `kids`** · silah yok (`Weapons = None`, dolayısıyla hasar yok) · takımsız · canlanma yok (gecikme 0) · ortak skor (`PlayerAndShared`) · denge `server.json → burger` | 600 sn / **sınırsız** (limit yok) |
| `mole` | `Modes/MoleMode.cs` | **Oyun tipi `kids`** · silah yok (`Weapons = None`, dolayısıyla hasar yok) · **iki takım + takım skoru** · canlanma yok (gecikme 0) | 300 sn / **sınırsız** (limit yok) |

> `ffa` skoru `AddPlayerScore(killerId, 1)` ile yazar ve kazananı `TryGetLeader` ile bulur;
> eşitlikte `TryGetLeader` false döndüğü için maç berabere biter. Oyuncusuz başlatılan maçta
> (admin harita önizlemesi) lider yoktur — süre dolunca berabere kapanır, ek kod gerekmez.

> **`tournament` bir maçı TURLARA böler ama çekirdek tur diye bir şey bilmez.** `roundSeconds`
> **turun** süresidir, `scoreLimit` maçı kazanmak için gereken tur sayısıdır (tavan
> `2 × limit − 1` tur). Bir takımın tüm çevrimiçi oyuncuları ölünce tur biter; süre dolarsa
> **savaşabilir** (canlı **ve** kalibreli) sayısı fazla olan takım alır, eşitse kimseye puan yok.
> `scoreLimit` **sınırsız** seçilirse (`SCORE_LIMIT_UNLIMITED`) ne galibiyet limiti ne tur tavanı
> işler: turlar `abort_match`'e kadar sürer.

> **`burger` Çocuk Oyunları ailesindendir** (`GameType = "kids"`): `start_match` yalnız `gameType`
> `kids` olan haritayı kabul eder, `quickbattle` haritası reddedilir. Maçı bitiren tek koşul
> **süredir**; `match_end`'de kazanan alanlarının **ikisi de boş** kalır (§10.5 ortak skor kuralı) ve
> sonuç ekranı `HoldsResultForOperator` ile operatör kapatana kadar durur. Vardiyanın tamamı
> (müşteri, tarif, pişirme, servis doğrulaması, ortak skor) sunucudadır; istemci yalnız sunumdur.
> Denge sayıları `server.json → burger` bloğundan gelir (yukarı bak).

> **`kids` haritası sahnelenince lobi profili de SİLAHSIZDIR** (§10.7): operatör admin panelinden
> çocuk haritasını seçtiğinde açık sahnenin kuralı `Weapons = None` olur — maç başlamadan grip'e
> basan çocuğun eline silah gelmez, atış relay edilmez. Lobi haritasına dönüşte normal lobi profili
> (rastgele silah + serbest atış) geri gelir. Kural açık sahnenin `gameType`'ına bakar, seçili moda
> değil.

> **`mole` de Çocuk Oyunları ailesindendir** ama **yarışmalıdır**: iki takım + takım skoru, yani
> ailenin kazananı olan ilk oyunu. Ailenin değişmezleri durur (silah yok → hasar yok, canlanma yok,
> sonuç ekranı operatörü bekler). ⚠️ **Takımlı olması taban gerektirmez** — taban `base`
> canlanmasının aracıydı, burada canlanma yok. Maçı bitiren tek koşul **süredir**: skoru yüksek
> takım kazanır, eşitlik berabere. Delikler haritanın ağ nesneleridir (`mole_hole`) ve mod onları
> maç başında `ObjectIdsOfKind` ile öğrenir — **haritada delik yoksa konsola uyarı düşer ve köstebek
> çıkmaz**. Konsolda `[mole]` satırı delik sayısını yazar.

> Turlar arasında faz `paused`/`mode` olur (`modeState:"regroup:2/6"`), oyuncular fiziksel olarak
> kendi taban bölgelerine yürüyüp `set_ready{true}` yollar ve herkes toplanınca geri sayım başlar
> — **toplanmanın zaman aşımı YOKTUR**, çıkışı operatörün `kick`/`abort_match`'idir. **Geri sayım
> sırasında tabanından çıkan olursa geri sayım iptal edilir ve toplanmaya dönülür** — kural
> "tabanda bekle"dir ve iptalin **istisnası yoktur**. Çekirdek bunu dört API ile destekler —
> `TryPauseForMode` / `SetModeState` / `TryStartRound` / `TryCancelCountdownForMode` — ve
> `modeState`'i **hiç ayrıştırmaz**. Konsolda `[tournament]` satırları tur akışını anlatır.

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
   (reçete: `../Docs/Gelistirici/Yemek-Kitabi.md`).

`OnTick`/`OnHitApplied`/`OnKill` **varsayılan gövdelidir** — ilgilenmeyen mod hiç yazmaz. Yeni bir
kanca eklerken de varsayılan gövde kullan (mevcut modların hiçbiri değişmesin) ve **tüketicisi
olmayan kancayı hiç ekleme**.

## Sunucu bugün ne yapıyor

- **Keşif + kontrol:** UDP beacon yayını, WS kontrol kanalı, lobi (roster / ready / takım /
  kick), cihaz adı kalıcılığı.
- **Poz kanalı:** `0x01 PoseUpdate` alımı (kayıtlı endpoint + u16 seq sarmalama kontrolü) +
  `0x02 Snapshot` yayını (20 Hz; oyuncu sayısı sınırsız, datagram başına en fazla
  `SNAPSHOT_MAX_ENTRIES_PER_PACKET = 16` girdi ≈ 1382 B, fazlası aynı tik içinde ek datagramlara
  bölünür). Snapshot `flags` bit0 gerçek `alive` durumunu taşır.
- **Maç:** `MatchDirector` faz makinesi (`load_match` → geri sayım → `playing` → `finished` → lobi) +
  `Modes/TdmMode.cs` + `Modes/FfaMode.cs` (`IGameMode`) + vuruş hattı, can/skor yayını,
  free-roam canlanma.
- **Hasar modeli:** sunucuda silah tablosu YOKTUR; hasarı istemci hesaplar, sunucu aynen uygular
  (`weaponId` yalnız etiket) — `../Docs/ArenaNet-Protokol.md` §10.3.
