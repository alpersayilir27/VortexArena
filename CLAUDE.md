# VortexArena — Proje Talimatları (CLAUDE.md)

Free-roam VR PvP arena ürünü (işletmelere kurulum / LBE; Meta Quest 3 & 3S, Unity 6000.3.20f1, URP).
Oyuncular fiziksel alanda 1:1 yürür; farklı boyutlarda arenalar (10x10, 12x12, işletmeye özel),
farklı oyun modları/haritalar/silahlar. VR build = player, Windows build = admin (yönetim + izleme).
Online haberleşme: kendi .NET sunucumuz (`Server/`, standalone exe, offline LAN) — Mirror/NGO YOK.

> Kurallar `.claude/rules/` altındadır. Uygulama planı: `plan/` (faz faz). Protokol: `Docs/ArenaNet-Protokol.md` (TEK doğruluk kaynağı).
> Sistemin tek sayfalık haritası (ne var, ağ nasıl çalışır, nasıl kullanılır): `Docs/Sistem-Ozeti.md`.

## Asset mimarisi (feature-first + asmdef)

- `Assets/_Shared/` — ortak. Ortak KOD yalnız bir asmdef altında: `Core/` (VortexArena.Core),
  `Net/Protocol` (VortexArena.Protocol — saf C#, server aynı dosyaları derler), `Net/Scripts`
  (VortexArena.Net), `App/Scripts` (VortexArena.App). Kod-dışı: `Arsenal/` (silah prefab+SO),
  `FX/`, `Environments/`, `Data/`, `Scenes/` (Boot, Lobby, AdminConsole).
  ⚠️ `_Shared` köküne asmdef'siz gevşek script koyMA (Assembly-CSharp'a düşer, kimse göremez).
- `Assets/Arenas/Standard/<AXxX veya TemaAdı>/` ve `Assets/Arenas/Venues/<İşletme>/` — arena kutuları:
  `{Scenes, Data, Prefabs}` (+ arenaya özel sanat varsa `Art/{Materials,Textures}`; ör. `Standard/IceWorld`).
  Arena = sahne + MapDefinition; arena-özel kod YAZILMAZ (marker bileşenleri Core'dan gelir).
  Bir arenanın ağa bağlanması için sahnede şunlar olmalı: `ArenaBoundary` (arena origin + halfExtent),
  `BaseZone`×2, `SpawnPoint`×(2×slot), `CalibrationManager`, `PoseSync`, `[ModeHud]`, BB Camera Rig.
- `Assets/Modes/<Mod>/` — mod kutuları: `{Scripts (VortexArena.Modes.<Ad>.asmdef), Data, UI}`.
  Modlar birbirini REFERANSLAMAZ.
- Üçüncü parti: `Assets/ThirdPartyPackages/`.

**Assembly grafiği** (bağımlılık hep aşağı):
Protocol (saf C#, noEngineReferences) ← Net ← Core ← App, Modes.<X>
Net oyun/sahne bilgisi içermez; olay yayınlar, App dinler. Editor asmdef'leri
`includePlatforms:["Editor"]` + kendi runtime'ını referanslar.

**İsimlendirme:** asmdef = `VortexArena.<Katman>`; namespace = asmdef adıyla birebir
(rootNamespace dolu); global namespace'te tip YOK; serialize edilen ikincil tipler kendi
dosyasında (`Team.cs` gibi). Sahne adı = katalog anahtarı (`load_match` string'i) → birebir eşleşme.

**Paylaşımlı-mı-modül-mü:** "İkinci bir mod/arena bunu aynen kullanır mı?" → evet=_Shared, hayır=kutu.

## XR / Meta politikası

- **Meta-first:** önce Meta Building Blocks + Meta XR SDK; yetmezse Unity XR Interaction Toolkit
  (XRI kurulu, yedek). Hedef YALNIZ Quest 3/3S. Sahnelerde BB Camera Rig kullanılır.
- **Umbrella paket YASAK** (`com.meta.xr.sdk.all` — Meta Project Setup Tool önerse bile ekleme):
  kullanılmayan voice@85, SDKTelemetry.aar ↔ OVRPlugin.aar Android namespace çakışmasıyla
  build kırar (vortexcosmos'ta yaşandı). Bireysel paketler: core + interaction + interaction.ovr
  @203.0.0, audio @85.0.0 (spatializer=Meta XR Audio olduğu için gerekli, pinli).
- Haptik: `OVRInput.SetControllerVibration` (core) — ayrı haptics paketi ekleme.
- XR loader: OpenXR (mevcut, çalışıyor) — değiştirme.

## Network (özet — detay Docs/ArenaNet-Protokol.md)

Portlar: UDP beacon 47820 · WS kontrol 47821 `/ws` · UDP state 47822 (cosmos 47800/1 ile çakışmaz).
Pozlar istemci-otoriter (kalibrasyon sonrası ARENA UZAYINDA, 20 Hz UDP); can/skor/kurallar/maç
fazları SUNUCU-otoriter (.NET `Server/`, mod kuralları `IGameMode`). Vuruş: atıcı raycast →
hit_report → server doğrular → health_update. **Free-roam respawn = konum değil DURUM değişimi**
(fiziksel oyuncu ışınlanamaz): ölüm → `RESPAWN_DELAY` → oyuncu kendi `BaseZone`'una fiziken girince
`revive_request` → sunucu canlandırır (istemci takılırsa `REVIVE_GRACE` ile zorla). Rig'i ASLA taşıma. Keşif: beacon + lobide elle IP:port (PlayerPrefs) +
StreamingAssets/arena.json fallback. DTO'lar `_Shared/Net/Protocol/` — saf C#, server csproj
aynı dosyaları derler; Unity API'si girerse server derlemesi kırılır (bilinçli bekçi).

## Akış

Boot(index 0) → Android: Lobby / Windows: AdminConsole (editor testi için override).
Lobby (VR): IP paneli, roster, ready/takım. AdminConsole: launcher ekranı (yalnız IP'ye bağlan;
sunucu bu ekrandan BAŞLATILMAZ, her zaman elle çalıştırılır) → dashboard (roster, mod+harita seç,
start, taktik üstten görünüm).
Arena sahneleri kendine yeten (kendi BB rig'i taşır).

## Yeni içerik ekleme reçeteleri

**Yeni arena:** `Tools > VortexArena > Create Arena From Template` → arenaId + sahne adı + boyut +
takım başına spawn + hedef (Standard / Venue). Sihirbaz: klasörleri (`{Scenes,Data,Prefabs}`) ve
sahne kopyasını üretir, duvar/zemin/taban/spawn'ları yeni boyuta göre ölçekler, MapDefinition
asset'ini yazar, GameCatalog + uyumlu ModeDefinition'lara ekler, Build Settings'e koyar
(sahne adı = katalog anahtarı). Duvar/cover sanat rötuşu ELDE; sonrasında
`Tools > VortexArena > Export Server Config` çalıştır (sunucu `maps.json` tazelensin).
**Yeni mod:** `Assets/Modes/<Ad>/Scripts/VortexArena.Modes.<Ad>.asmdef` (refs: Core, Net, Protocol;
mevcut moddan JSON kopyala, name değiştir, .meta KOPYALAMA) + server tarafında `Modes/<Ad>Mode.cs`
(IGameMode) + `Docs/ArenaNet-Protokol.md`'ye modId ekle.
**Yeni silah:** prefab `_Shared/Arsenal/Prefabs/` + WeaponDefinition SO `_Shared/Arsenal/Data/`
(weaponId protokolde string — iki tarafta da aynı) + gerekiyorsa `ModeDefinition.loadout` →
**`Tools > VortexArena > Export Server Config`** (hasarı sunucu `Server/config/weapons.json`'dan
uygular; sapmada sunucu kazanır). İçerik kataloğu: `_Shared/Data/GameCatalog.asset`
(ModeDefinition + MapDefinition listesi) — AdminConsole mod/harita seçicisi bunu okur.

**Editor araçları** (`VortexArena.Core.Editor`, `VortexArena.Net.Editor` — yalnız Editor):
`Tools > VortexArena > Export Server Config` (SO'lardan `Server/config/weapons.json` + `maps.json`;
deterministik, LF, BOM'suz — **JSON'ları elle düzenleme, export ezer**), `… > Create Arena From
Template`, `GameObject > VortexArena > Network Parent` (sahne objesine `NetIdentity` + benzersiz
`sceneId`; sahne kaydında SceneIdGuard 0/çakışan id'leri onarır — dinamik obje senkronu altyapısı).
