# VortexArena Sunucusu

Free-roam VR PvP arenasını LAN'da yöneten bağımsız **.NET 8 konsol** sunucusu (offline/LAN, Mirror/NGO yok). VR (Quest) oyuncuları ve Windows admin istemcisi buna bağlanır. Pozlar istemci-otoriter (UDP 20 Hz, arena uzayında); can/skor/kurallar/maç fazları **sunucu-otoriter**dir.

> **Protokol tanımı:** `../Docs/ArenaNet-Protokol.md` (TEK doğruluk kaynağı). DTO'lar ve sabitler `../Assets/_Shared/Net/Protocol/` altındadır — `VortexArena.Server.Core.csproj` **aynı dosyaları** `<Compile Include>` ile derler. O dosyalara Unity API'si girerse bu build kırılır (bilinçli bekçi).

## Proje yapısı

```
Server/
  VortexArena.Server.sln
  VortexArena.Server.Core/    # Kestrel WS host, beacon, PlayerRegistry, LobbyService,
                              # StateHost (UDP), MatchDirector iskeleti, Modes/ (IGameMode)
  VortexArena.Server.App/     # konsol exe (UI YOK — yönetim UI'ı Unity admin build'i)
  VortexArena.PoseBot/        # sentetik oyuncu test istemcisi (poz senkronunu Quest'siz test eder)
  config/server.json          # portlar + mekan adı + tickHz
  config/devices.json         # deviceId -> dostane ad ("Gözlük NN"); otomatik doldurulur
  firewall-kur.cmd            # Windows Firewall kuralları (yönetici olarak çalıştırın)
```

## Çalıştırma

```powershell
dotnet run --project Server/VortexArena.Server.App
```

veya derlenmiş exe: `Server/VortexArena.Server.App/bin/Debug/net8.0/VortexArena.Server.App.exe`
(Admin build'in launcher ekranı da bu exe'yi başlatır.)

Açılışta:
- Kestrel `http://0.0.0.0:47821/ws` (WebSocket kontrol) dinler.
- UDP `47820`'ye her 2 sn beacon yayınlar → istemciler sunucuyu **kendiliğinden** bulur
  (elle girilen IP her zaman beacon'ı ezer).
- UDP `47822` state kanalını dinler: `0x00 UdpHello` kayıt + ack, `0x01 PoseUpdate` alımı,
  `0x02 Snapshot` yayını (20 Hz, kayıtlı tüm endpoint'lere — admin dahil). Poz akarken konsolda
  saniyede bir `[state] oyuncu N, pozlu N, snapshot N B, hedef N` özeti görünür.
- `config/` bulunamazsa exe yanında oluşturulur ve varsayılanlarla doldurulur.
- Konsolda bağlanan/kopan cihazlar ve çevrimiçi sayısı akar; **Ctrl+C** temiz kapatır.

## Portlar

| Port | Protokol | Amaç |
|---|---|---|
| 47820 | UDP | Keşif beacon'ı (sunucu → broadcast, 2 sn'de bir) |
| 47821 | TCP | WebSocket kontrol kanalı (`/ws`) |
| 47822 | UDP | State kanalı (UdpHello kaydı; Faz 2'de pozlar + snapshot) |

(cosmos'un 47800/47801'i ile bilerek çakışmaz.)

## Windows Firewall (ŞART — bir kez)

`Server/firewall-kur.cmd` dosyasına **sağ tık → "Yönetici olarak çalıştır"**. Bu betik:
1. Uygulama ilk açıldığında Windows'un otomatik eklediği **ENGELLE (Block)** kurallarını siler
   (bunlar gözlüklerin bağlanmasını engeller),
2. **TCP 47821** + **UDP 47820/47822** için **İZİN** kuralları ekler (Özel + Genel profil).

> Sunucuyu ilk kez firewall kuralları OLMADAN açarsanız Windows bir "izin ver?" sorusu gösterir.
> **"İzin ver"e** basın. İptal ederseniz Windows kalıcı bir engelle kuralı ekler → sonra
> `firewall-kur.cmd`'yi çalıştırıp düzeltin.

## Ağ (AP) kontrol listesi — gerçek arena

- Sunucu PC tercihen **kablolu (GbE)** + **statik IP**; ağ profili **Özel (Private)** olmalı.
- Erişim noktası: **5 GHz**, **client isolation KAPALI** (cihazlar sunucuyu görmeli),
  tercihen Wi-Fi 6, arenaya özel SSID; tüm gözlükler bu SSID'de.
- Beacon kesen/izole eden ağlarda: her gözlükte `Assets/StreamingAssets/arena.json` içine
  sunucunun statik IP'si yazılır (`{"serverIp":"192.168.x.y","serverPort":47821}`) —
  beacon yoksa istemci buna düşer; lobide elle IP:port girişi her zaman mümkündür.

## Config dosyaları

**server.json** — portlar ArenaProtocol sabitleriyle aynı varsayılanlardadır; mekana özel
kurulumda genelde yalnız `venueName` değişir:
```json
{ "controlPort": 47821, "beaconPort": 47820, "statePort": 47822, "venueName": "Dev", "tickHz": 20 }
```

**devices.json** — `{ "<deviceId>": "Gözlük 07" }`. Bilinmeyen player cihazı bağlanınca ilk boş
`Gözlük NN` atanır ve dosyaya yazılır; `set_name` ile değişen ad da buraya kalıcı yazılır.
UTF-8, BOM'suz.

## PoseBot — sentetik oyuncu (test)

Quest olmadan poz senkronunu uçtan uca denemek için:

```powershell
dotnet run --project Server/VortexArena.PoseBot -- 127.0.0.1 2   # 2 bot, yerel sunucuya
```

Her bot player rolüyle WS'e bağlanır, UDP kaydını yapar ve 20 Hz'de dairesel yürüyüş pozu
gönderir (bot başına farklı yarıçap/faz). Editor'de admin bağlanınca taktik görünümde,
player bağlanınca lobide hayalet avatar olarak görünürler. Botların yazdığı `devices.json`
girdilerini commit'lemeyin (test kirliliği).

## Faz durumu

- **Faz 1:** beacon + WS kontrol + lobi (roster/ready/takım/kick/identify) +
  UDP kayıt. Loopback E2E: sunucuyu başlat → Editor'de admin bağlan → roster'da görün.
- **Faz 2 (tamam):** `0x01 PoseUpdate` alımı (kayıtlı endpoint + u16 seq sarmalama kontrolü) +
  `0x02 Snapshot` yayını (20 Hz, tek pakette 16 oyuncu ≈ 1382 B) + PoseBot test istemcisi.
- **Faz 3:** MatchDirector maç akışı (`load_match` → countdown → Live → End) + `Modes/` altında
  `IGameMode` uygulamaları (ör. `TdmMode.cs`) + vuruş doğrulama/skor.
