# VortexArena — Uygulama Planı (Genel Bakış)

> Bu klasör, VortexArena projesinin dosya sistemi + network mimarisi kurulumunun **faz faz uygulama planıdır**.
> Uygulayıcı ajan: her fazı kendi dosyasından oku, sırayla uygula, fazın sonundaki **Doğrulama** bölümünü geçmeden sonraki faza geçme.

## Okuma sırası

| Dosya | İçerik | Durum |
|---|---|---|
| `README.md` | Genel mimari, kararlar, kurallar (önce bunu oku) | — |
| `protokol-v1.md` | Ağ protokolü tam referansı (Faz 0'da `Docs/`'a kopyalanır, Faz 1-3'te implement edilir) | — |
| `faz0-dosya-sistemi.md` | Klasör/asmdef göçü, paket trimi, CLAUDE.md + kurallar + doküman | ✅ (2026-07-24) |
| `faz1-server-ve-lobi.md` | .NET server iskeleti + Unity istemci + Lobi E2E | ✅ (2026-07-24 — loopback E2E geçti; Quest cihaz E2E kullanıcıda) |
| `faz2-poz-senkronu.md` | UDP poz akışı, uzak avatarlar, admin taktik görünüm, kalibrasyon | ✅ (2026-07-24 — loopback E2E: 2 PoseBot + editor admin/player geçti; 2-Quest fiziksel örtüşme testi kullanıcıda) |
| `faz3-mac-ve-savas.md` | Maç akışı, TDM modu, silah/can senkronu, respawn | ✅ (2026-07-25 — loopback TDM E2E geçti: faz makinesi, vuruş doğrulama/ret yolları, free-roam canlanma; 2-Quest saha raundu kullanıcıda) |
| `faz4-editor-sdk.md` | Editor araçları (Network Parent, arena şablonu), A12x12, işletme piloti | ✅ (2026-07-25 — sihirbazla A12x12 + DemoVenue 11×8 üretildi, loopback TDM raundu geçti; MJPEG izleme ertelendi, saha kurulum provası kullanıcıda) |

## Proje nedir?

**VortexArena** = işletmelere (LBE — location-based entertainment) kurulan **free-roam VR PvP arena** ürünü:

- Oyuncular fiziksel alanda **1:1 yürür** (lokomosyon yok); hedef cihaz **yalnız Quest 3 / Quest 3S**.
- Farklı boyutlarda arenalar: önce standart boyutlar (**10x10, 12x12** m), sonra **işletme başına özel sahneler**.
- Farklı **oyun modları**; mod başına farklı **haritalar** ve **silahlar** olabilir.
- **İki rol:** VR build = **player** (oynar), Windows masaüstü build = **admin** (yönetim + izleme). İkisi de aynı Unity projesinden çıkar.
- **Kendi .NET sunucumuz** (standalone exe, aynı repo `Server/` klasöründe) — tamamen **offline LAN**. Mirror/NGO gibi hazır netcode KULLANILMAZ; ama Unity tarafında Mirror-vari editor kolaylıkları (hiyerarşi sağ-tık "Network Parent" vb.) zamanla eklenecek.
- **Akış:** VR açılır → doğrudan **Lobi** → ayar panelinden sunucu **IP:port** girilir → diğer oyuncular ve admin ile aynı lobiye bağlanır → admin maçı başlatır.

## Kesinleşmiş kullanıcı kararları

1. **Oyun kuralları sunucuda (.NET) koşar** — server otoriter: skor/round/can/respawn/kazanma koşulu sunucuda, mod başına kural sınıfı .NET'te (`IGameMode`). Unity istemcileri sunum + girdi.
2. **Launcher = admin uygulamasının giriş ekranı** — ayrı launcher programı YOK. Masaüstü build'in açılış ekranı sunucu exe'sini başlatır (`Process.Start`) veya mevcut sunucuya bağlanır, config seçer, sonra yönetim paneline geçer.
3. **Meta umbrella paketi Faz 0'da alt paketlere çevrilir** (aşağıda "Paket politikası").

## Referans proje: `D:\games\vortexcosmos`

Mimari model. Oradan **desen** alınır (kod bire bir kopyalanmaz, uyarlanır):

- Feature-first + asmdef katmanlaması (`Assets/_Shared/` + modül kutuları; tüm bağımlılıklar Core'a doğru).
- `Server/` klasörü repo kökünde, .NET çözümü.
- `Docs/<Protokol>.md` = **tek doğruluk kaynağı** deseni.
- Kanıtlanmış ağ istemci desenleri: `Assets/_Shared/Network/Scripts/ClassroomClient.cs` (ClientWebSocket + ConcurrentQueue ana-thread köprüsü, kalıcı singleton), `ServerLocator.cs` (UDP beacon dinleme + Android MulticastLock + statik IP fallback), `CameraStreamer.cs` (Faz 4 opsiyonel MJPEG için).
- `.claude/rules/` çalışma kuralları.

## Hedef mimari (özet)

### Klasör ağacı (nihai hali)

```
D:\Games\vortexarena\
  CLAUDE.md                      (Faz 0)
  .claude\rules\                 (Faz 0: 3 kural)
  Docs\ArenaNet-Protokol.md      (Faz 0; kaynak: plan/protokol-v1.md)
  plan\                          (bu klasör)
  Server\                        (Faz 1: .NET çözümü — detay faz1 dosyasında)
  Assets\
    _Shared\
      Core\                      → VortexArena.Core.asmdef
        Arena\    ArenaBoundary, ArenaCalibrator, BaseZone, (Faz 3: SpawnPoint, MapDefinition)
        Combat\   Health, Weapon, WeaponAudio, Team.cs, (Faz 3: WeaponDefinition)
        Player\  UI\  Util\      (ihtiyaç oldukça)
      Net\
        Protocol\                → VortexArena.Protocol.asmdef (Faz 1; saf C#, noEngineReferences)
        Scripts\                 → VortexArena.Net.asmdef (Faz 1)
          Editor\                → VortexArena.Net.Editor.asmdef (Faz 4)
      App\
        Scripts\                 → VortexArena.App.asmdef (Faz 1)
        Prefabs\  UI\
      Arsenal\Prefabs\           AK47_Red, M4_Blue    Arsenal\Data\ (Faz 3: silah SO'ları)
      FX\                        FX_HitSpark
      Environments\  Data\       (paylaşımlı prefab/SO — ihtiyaç oldukça)
      Scenes\                    Boot, Lobby, AdminConsole (Faz 1)
    Arenas\
      Standard\A10x10\Scenes\Arena10x10.unity   (Faz 0'da mevcut sahne buraya taşınır)
      Standard\A10x10\{Data,Prefabs}\
      Standard\A12x12\           (Faz 4)
      Venues\<İşletmeAdı>\{Scenes,Data,Art}\    (Faz 4+)
    Modes\
      TeamDeathmatch\{Scripts,Data,UI}\          → VortexArena.Modes.Tdm.asmdef (Faz 3)
    ThirdPartyPackages\          (Faz 0'da typo düzeltilir; içinde "ithappy")
    Audio\ Materials\ Screenshots\ Settings\ Plugins\ Resources\
    StreamingAssets\ Oculus\ XR\ XRI\            (yerinde kalır, DOKUNMA)
```

### Assembly grafiği (bağımlılıklar hep aşağı; modlar birbirini REFERANSLAMAZ)

```
VortexArena.Protocol    saf C# (Unity API YOK) — server aynı dosyaları derler
VortexArena.Net         → Protocol                       (oyun bilgisi YOK)
VortexArena.Core        → Net, Protocol, Oculus.VR, Oculus.Interaction,
                          Unity.InputSystem, Unity.TextMeshPro, UnityEngine.UI
VortexArena.App         → Core, Net, Protocol, Oculus.VR, Unity.TextMeshPro, UnityEngine.UI
VortexArena.Modes.<X>   → Core, Net, Protocol
*.Editor                → kendi runtime'ı + gerekenler; "includePlatforms": ["Editor"]
```

Önemli: cosmos'ta Network→Core bağımlılığı vardı; **arena'da tersi** — Net katmanı olay yayınlar, App dinleyip sahne yükler. Net'e oyun/sahne bilgisi SIZMAZ.

### Network (özet — tam referans `protokol-v1.md`)

- **3 kanal:** UDP beacon **47820** (keşif) · WS kontrol **47821** `/ws` (JSON: lobi, maç akışı, vuruşlar) · UDP state **47822** (binary pozlar 20 Hz). Portlar cosmos'unkilerle (47800/47801) bilerek farklı — aynı LAN'de iki ürün koşabilir.
- **Otorite:** pozlar istemci-otoriter (fiziksel tracking = gerçek; kalibrasyon sonrası **arena uzayında** gönderilir), geri kalan her şey (can/skor/kurallar/maç fazları) **sunucu-otoriter**. Vuruş: atıcı istemcide raycast → `hit_report` → server doğrular → `health_update`/`kill_event` yayınlar.
- **Sapma önleme:** DTO'lar/sabitler `Assets/_Shared/Net/Protocol/`'de saf C#; server csproj **aynı .cs dosyalarını** `<Compile Include>` ile derler. `Docs/ArenaNet-Protokol.md` semantiğin tek doğruluk kaynağı.

## Proje genelinde geçerli kurallar (her fazda uy)

1. **Meta-first:** önce Meta Building Blocks + Meta XR SDK; yetmediği yerde Unity XR Interaction Toolkit. Hedef yalnız Quest 3/3S. Mevcut sahne BB rig'i kullanıyor — koru.
2. **Paket politikası:** `com.meta.xr.sdk.all` (umbrella) ASLA geri eklenmez (Meta Project Setup Tool önerse bile). Sebep: cosmos'ta umbrella'nın çektiği `voice@85` SDKTelemetry.aar'ı core'un OVRPlugin.aar'ı ile Android namespace çakışması yaratıp build'i kırdı; `audio@85` bu projede spatializer olarak KULLANILDIĞI için pinli kalır.
3. **Asset taşıma:** her taşıma `.cs/.prefab/.unity + .meta` **çift olarak** `git mv` ile → GUID korunur, sahne/prefab bağları kopmaz. Unity **kapalıyken** taşı (açıkken FS taşıması "Permission denied" verir — cosmos dersi).
4. **İsimlendirme:** asmdef adı = `VortexArena.<Katman>`; C# namespace = asmdef adıyla birebir (`rootNamespace` dolu); global namespace'te tip bırakma; serialize edilen ikincil tipler (enum/MonoBehaviour) **kendi dosyasında** (cosmos hard-won: gömülü sınıf bake/serialize'da "referenced script missing" üretebiliyor).
5. **Sahne adı = katalog anahtarı:** server `load_match`'te sahne adını string gönderir → build listesindeki adla boşluk/typo dahil birebir eşleşmeli.
6. **Paylaşımlı-mı-modül-mü:** "İkinci bir mod/arena bunu aynen kullanır mı?" → evet = `_Shared`; hayır = mod/arena kutusu.
7. **Toplu doğrulama:** derleme/build doğrulamasını fazın SONUNDA tek geçişte yap; her adımda build alma (`.claude/rules/batch-build-verification.md`).
8. **Kullanıcının yerel değişikliklerine dokunma:** `.vscode/settings.json` (değiştirilmiş) ve silinmiş `Assets/StreamingAssets/RuntimeActionBindings.json(.meta)` çalışma ağacında öylece duruyor — commit'lerine karıştırma, geri getirme.
9. **Commit politikası:** her fazın sonunda, doğrulama geçince tek commit (mesajda faz adı). Faz ortasında kırık durumda commit atma.

## Mevcut durum anlık görüntüsü (Faz 0 öncesi — 2026-07-24)

- Unity **6000.3.18f1**, URP 17.3.0, OpenXR loader (`Assets/XR/Loaders/OpenXRLoader.asset`) + XRI 3.3.2 (yedek olarak KALIR).
- Tek sahne: `Assets/Scenes/VortexArena.unity` (GUID `629f3342b038beb4d975f35ba6e2dbd6`, EditorBuildSettings'te index 0). Meta **Building Blocks** rig'li.
- 6 script `Assets/Scripts/` düzünde, tek asmdef `VortexArena.Gameplay` (refs: Oculus.VR, Oculus.Interaction, Unity.InputSystem). Hepsi **global namespace'te**; `Team` enum'u `Weapon.cs` içinde.
- 3 prefab: `Assets/Prefabs/{AK47_Red, M4_Blue, FX_HitSpark}.prefab`.
- `Packages/manifest.json`: **umbrella `com.meta.xr.sdk.all@203.0.2`** (lock'ta core/interaction/interaction.ovr@203.0.0, audio@85.0.0, voice@85.0.1, platform@203.0.1, haptics@203.0.0, mrutilitykit@203.0.0 çözülüyor). Ayrıca kullanılmayan `com.unity.multiplayer.center@1.0.1`.
- `ProjectSettings/AudioManager.asset`: spatializer = **Meta XR Audio** (→ audio paketi gerekli).
- Typo klasör: `Assets/ThirdPartyPackacges/` (içinde `ithappy`).
- `Assets/StreamingAssets/` boş. Kökte `install_game.bat` (adb APK kurulumu — kalır).
- CLAUDE.md / .claude / Docs / Server YOK.
- Haptik `OVRInput.SetControllerVibration` ile (core SDK) → ayrı haptics paketi GEREKMEZ. MRUK/platform/voice hiçbir scriptte kullanılmıyor.
