# Faz 1 — Protokol Kodu + .NET Server İskeleti + Unity İstemci + Lobi E2E

**Amaç:** Faz sonunda masaüstü admin build'i (veya Editor'de admin modu) ile bir Quest (veya Editor'de player modu) **aynı lobide** görünür: bağlan → roster canlı güncellenir → ready/takım durumu akar. Poz senkronu YOK (Faz 2), maç YOK (Faz 3).

**Ön koşul:** Faz 0 tamam + commit'li. Protokol referansı: `Docs/ArenaNet-Protokol.md`.

---

## Adım 1 — Paylaşılan protokol kaynağı (`VortexArena.Protocol`)

Klasör: `Assets/_Shared/Net/Protocol/`

**`VortexArena.Protocol.asmdef`:**
```json
{
    "name": "VortexArena.Protocol",
    "rootNamespace": "VortexArena.Protocol",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": true
}
```

Dosyalar (hepsi `namespace VortexArena.Protocol`, **UnityEngine KULLANMA** — System.* serbest):

- **`ArenaProtocol.cs`** — tüm sabitler (protokol dokümanındaki tablo: sürüm, 3 port, aralıklar, timeout'lar, POSE_RATE_HZ, SNAPSHOT_RATE_HZ, INTERP_DELAY_MS, `WS_PATH="/ws"`, `APP_ID="VortexArena"`). *(Faz 1'de burada `MAX_PLAYERS = 16` da vardı; 2026-07-27'de kaldırıldı — oyuncu sayısı sınırı yok, yerini `PLAYER_ID_MAX = 255` + `SNAPSHOT_MAX_ENTRIES_PER_PACKET` aldı.)*
- **`ControlMessages.cs`** — tüm JSON DTO'ları: `MsgEnvelope {string type}`, `HelloMsg`, `WelcomeMsg` (+iç `MatchInfo`), `LobbyStateMsg` (+`PlayerInfo`), `StatusMsg`, `SetNameMsg`, `SetReadyMsg`, `SetTeamMsg`, `ShotFiredMsg`, `HitReportMsg`, `StartMatchMsg`, `AbortMatchMsg`, `KickMsg`, `IdentifyMsg`, `LoadMatchMsg`, `CountdownMsg`, `MatchStateMsg`, `HealthUpdateMsg`, `KillEventMsg`, `RespawnMsg`, `MatchEndMsg`, `ReturnToLobbyMsg`, `PingMsg`, `KickedMsg`. Kurallar: `[Serializable]`, **public alan** (property değil), Dictionary/polimorfizm yok, alan adları protokol dokümanındaki camelCase ile birebir. `[Serializable]` attribute'u `System.SerializableAttribute`'tır — Unity gerektirmez, serbest.
- **`StateMessages.cs`** — binary yaz/oku: `PoseData {float px,py,pz,qx,qy,qz,qw}` struct; `UdpHello (0x00)`, `PoseUpdate (0x01)`, `Snapshot (0x02)` için `Write(BinaryWriter)` / `Read(BinaryReader)` statik metotları. Format protokol dokümanı §6 ile birebir.
- **`MessageTypes.cs`** — `type` string sabitleri (`"hello"`, `"welcome"`, …) tek yerde.

## Adım 2 — .NET server çözümü (`Server/`)

```
Server/
  VortexArena.Server.sln
  VortexArena.Server.Core/VortexArena.Server.Core.csproj    (net8.0, classlib)
  VortexArena.Server.App/VortexArena.Server.App.csproj      (net8.0, console exe; Core'a proje ref)
  config/server.json          { "controlPort":47821, "beaconPort":47820, "statePort":47822,
                                "venueName":"Dev", "tickHz":20 }
  config/devices.json         (boş {} ile başlar; deviceId → ad, "Gözlük NN" otomatik)
  README.md                   (çalıştırma + firewall + AP kontrol listesi; cosmos Server/README.md şablon)
  firewall-kur.cmd            (yönetici; TCP 47821 + UDP 47820/47822 izin; App engelle kurallarını sil —
                               cosmos Server/firewall-kur.cmd birebir uyarlama)
```

**Paylaşılan kaynak bağlantısı** — `VortexArena.Server.Core.csproj` içine:
```xml
<ItemGroup>
  <Compile Include="..\..\Assets\_Shared\Net\Protocol\**\*.cs"
           Exclude="..\..\Assets\_Shared\Net\Protocol\**\*.meta"
           LinkBase="Protocol" />
</ItemGroup>
```
(asmdef .cs olmadığı için zaten derlenmez; Exclude yalnız emniyet.)

**Core sınıfları** (Kestrel WS host cosmos `ClassroomHost.cs` deseninden uyarlanır):

| Sınıf | Sorumluluk |
|---|---|
| `ControlHost` | Kestrel `http://0.0.0.0:47821`, `/ws` WebSocket kabul; bağlantı başına `ClientConnection` |
| `ClientConnection` | recv döngüsü (text=JSON), hello bekleme (10 sn yoksa kapat), tek-slot send kuyruğu |
| `BeaconService` | UDP 47820'ye 2 sn'de bir beacon JSON (tüm broadcast adreslerine) |
| `PlayerRegistry` | deviceId→PlayerState (playerId tahsisi 1..16, ad, rol, takım, ready, online, battery, udpToken, udpEndpoint); devices.json kalıcılığı |
| `LobbyService` | roster değişiminde `lobby_state` yayını; set_name/set_ready/set_team/kick/identify işleme |
| `StateHost` | UDP 47822 dinle: `0x00 UdpHello` → endpoint kaydet + ack; (Faz 2: 0x01 alım + snapshot yayını — bu fazda yalnız kayıt) |
| `MatchDirector` | Faz 3'te dolacak iskelet: `Phase` enum + `IGameMode` arayüzü tanımı şimdiden konur |
| `JsonUtil` | `System.Text.Json` options: `IncludeFields = true` (DTO'lar public alan!) |

**App (konsol):** config yükle → host'ları başlat → konsola durum satırları (bağlanan/kopan, roster boyutu) → Ctrl+C graceful shutdown. UI YOK (yönetim UI'ı Unity admin build'i).

## Adım 3 — Unity istemci katmanı (`VortexArena.Net`)

Klasör: `Assets/_Shared/Net/Scripts/` — **`VortexArena.Net.asmdef`**: rootNamespace `VortexArena.Net`, references: `["VortexArena.Protocol"]` (başka bir şey DEĞİL — Net'e oyun bilgisi sızmaz; UnityEngine serbest).

| Sınıf | Sorumluluk (desen kaynağı: cosmos `Assets/_Shared/Network/Scripts/`) |
|---|---|
| `ArenaClient` | Kalıcı singleton (`[RuntimeInitializeOnLoadMethod]` + `DontDestroyOnLoad`). `ClientWebSocket` arka plan Task + `ConcurrentQueue` ana-thread köprüsü (cosmos `ClassroomClient` deseni). API: `Connect(ip,port,role)`, `Disconnect()`, `Send<T>(msg)`, durum (`Disconnected/Discovering/Connecting/Connected`). hello/welcome/status döngüsü, reconnect 1→2→5 sn. **Sahne YÜKLEMEZ** — olay yayınlar. |
| `NetEvents` | Statik olay merkezi: `OnConnected(WelcomeMsg)`, `OnDisconnected`, `OnLobbyState(LobbyStateMsg)`, `OnLoadMatch`, `OnMatchState`, `OnCountdown`, `OnHealthUpdate`, `OnKillEvent`, `OnRespawn`, `OnMatchEnd`, `OnReturnToLobby`, `OnShotFired`, `OnIdentify`, `OnKicked`. App/Core dinler. |
| `ServerDiscovery` | Beacon dinleyici (cosmos `ServerLocator` portu: `UdpClient` + **Android MulticastLock** + `arena.json` fallback). Öncelik: elle girilen IP (PlayerPrefs `arena.serverIp/Port`) > beacon > arena.json. |
| `UdpStateChannel` | Bu fazda yalnız `0x00 UdpHello` kayıt + ack takibi (welcome'daki udpToken ile). Poz gönderimi Faz 2. |

**`Assets/link.xml`** oluştur (IL2CPP stripping emniyeti):
```xml
<linker>
  <assembly fullname="VortexArena.Protocol" preserve="all"/>
  <assembly fullname="VortexArena.Net" preserve="all"/>
</linker>
```

## Adım 4 — Uygulama kabuğu (`VortexArena.App`) + sahneler

Klasör: `Assets/_Shared/App/Scripts/` — **`VortexArena.App.asmdef`**: rootNamespace `VortexArena.App`, references: `["VortexArena.Core","VortexArena.Net","VortexArena.Protocol","Oculus.VR","Unity.TextMeshPro","UnityEngine.UI"]`.

**Scriptler:**
- `AppBoot` — Boot sahnesinde: rol tayini (`Application.platform == Android` → player/Lobby; Windows/Editor → `--role player` komut satırı veya `VORTEX_ROLE` env override ile test edilebilir; varsayılan masaüstü=admin/AdminConsole) → `SceneManager.LoadScene`.
- `SceneRouter` — `NetEvents.OnLoadMatch/OnReturnToLobby` dinler, sahne yükler (adı build listesinde doğrular, yoksa loglar). Kalıcı singleton. (Cosmos'un global `SceneManager`'ının namespace'li, gölgelemesiz karşılığı — `UnityEngine.SceneManagement.SceneManager`'ı gölgeleyecek AD KULLANMA.)
- `LobbyController` (VR) — ayar paneli (IP:port girişi — VR'da numpad butonlu world-space canvas; BB Poke/Ray etkileşimi), Bağlan/Kes, durum metni, roster listesi (ad/rol/takım/ready/batarya), kendi ready toggle'ı. Elle girilen IP `PlayerPrefs`'e yazılır.
- `AdminConsoleController` (masaüstü, screen-space UGUI) — **launcher giriş ekranı**: "Sunucu Başlat" (`Process.Start` ile `Server/VortexArena.Server.App/bin/.../VortexArena.Server.App.exe`; exe yolu `PlayerPrefs` + alan) VEYA "IP'ye Bağlan"; bağlanınca dashboard paneli: roster tablosu, set_team dropdown, kick/identify butonları. (Mod/harita seçimi + start Faz 3'te eklenir.)
- `IdentifyOverlay` — `OnIdentify`'da büyük playerId/ad overlay'i (cosmos deseni).

**Sahneler** (`Assets/_Shared/Scenes/`):
- `Boot.unity` — boş + `AppBoot`. **Build index 0.**
- `Lobby.unity` — Meta BB Camera Rig (mevcut `Arena10x10` sahnesindeki rig'den türet — kopyala değil, BB menüsünden yeni ekle) + zemin + `LobbyController` panelleri.
- `AdminConsole.unity` — 2D kamera + UGUI canvas + `AdminConsoleController`. XR başlatmaz (OpenXR loader'ı XR Management'ta yalnız Android'de işaretli olduğundan Windows'ta zaten pasif — **XR Management ayarını kontrol et:** PC sekmesinde OpenXR işaretliyse KALDIR; Android'de kalsın).

**Build listesi** (EditorBuildSettings): `Boot(0), Lobby(1), AdminConsole(2), Arena10x10(3)`.

## Adım 5 — Doğrulama (E2E, tek toplu geçiş)

1. `dotnet build Server/VortexArena.Server.sln` → **0 hata 0 uyarı**. (Protocol dosyalarına yanlışlıkla UnityEngine girerse burada kırılır — bilinçli bekçi.)
2. Unity derleme temiz (4 yeni asmdef çözülüyor).
3. **Loopback E2E (Editor):** `dotnet run --project Server/VortexArena.Server.App` başlat → Editor'de `Boot`'u `VORTEX_ROLE=admin` ile oynat → AdminConsole "Bağlan 127.0.0.1" → roster'da admin görünür. Durdur; `VORTEX_ROLE=player` ile tekrar oynat → Lobby'de IP gir → bağlan → server konsolunda + (ikinci bir admin örneğiyle bakılıyorsa) roster'da oyuncu görünür; set_ready akışı `lobby_state` yayınını tetikliyor.
4. **Cihaz testi:** APK build → Quest'te aç → Lobby → gerçek sunucu IP'sini gir → bağlanır, PC'deki admin roster'ında görünür; sunucuyu kapat-aç → istemci backoff ile kendiliğinden yeniden bağlanır; beacon varken IP alanı otomatik dolar (MulticastLock çalışıyor).
5. Firewall: `firewall-kur.cmd` yönetici olarak bir kez (kullanıcıya bırak — UAC ajan tarafından onaylanamaz; README'ye yazıldı).
6. Commit: `Faz 1: paylaşılan protokol + .NET server iskeleti + ArenaClient + Lobi E2E`

## Çıktı kontrol listesi

- [ ] `VortexArena.Protocol` asmdef (noEngineReferences) + 4 kaynak dosya; server csproj aynı dosyaları link'liyor
- [ ] Server çözümü derleniyor; beacon + WS + registry + lobby çalışıyor; konsol durum basıyor
- [ ] `ArenaClient`/`NetEvents`/`ServerDiscovery`/`UdpStateChannel(kayıt)` + `link.xml`
- [ ] Boot/Lobby/AdminConsole sahneleri + build listesi; XR Management PC'de loader kapalı
- [ ] Loopback E2E + Quest cihaz E2E geçti; reconnect ve elle-IP akışı doğrulandı
- [ ] Commit atılmış
