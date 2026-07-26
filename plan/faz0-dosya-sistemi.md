# Faz 0 — Dosya Sistemi Göçü + Paket Trimi + Dokümantasyon

**Amaç:** Projeyi feature-first + asmdef mimarisine taşımak, Meta umbrella paketini alt paketlere çevirmek, CLAUDE.md / .claude/rules / Docs kurmak. Bu fazda **hiç yeni oyun kodu yazılmaz** — yalnız taşıma, yeniden adlandırma, namespace ekleme ve dokümantasyon.

> ⚠️ **ÖN KOŞUL: Unity Editor KAPALI olmalı.** Açıkken dosya taşıması "Permission denied" verir ve klasör kilitleri yarım taşıma bırakabilir. Başlamadan kullanıcıya sor/doğrula.

> ⚠️ Tüm taşımalar `git mv` ile ve **asset + .meta çift olarak** yapılır. `.meta` GUID taşır; çift taşındığı sürece sahne/prefab bağları kopmaz.

---

## Adım 1 — Klasör ağacını oluştur

Yalnız bu fazda içi dolacak klasörler (boş klasör bırakma — Unity meta üretir ama VCS'te gürültü olur; Net/App/Modes klasörleri kendi fazlarında açılır):

```
Assets/_Shared/Core/Arena/
Assets/_Shared/Core/Combat/
Assets/_Shared/Arsenal/Prefabs/
Assets/_Shared/FX/
Assets/Arenas/Standard/A10x10/Scenes/
Docs/
.claude/rules/
```

## Adım 2 — git mv taşımaları (her satır: dosya + .meta)

| Kaynak | Hedef |
|---|---|
| `Assets/Scripts/ArenaBoundary.cs` | `Assets/_Shared/Core/Arena/ArenaBoundary.cs` |
| `Assets/Scripts/ArenaCalibrator.cs` | `Assets/_Shared/Core/Arena/ArenaCalibrator.cs` |
| `Assets/Scripts/BaseZone.cs` | `Assets/_Shared/Core/Arena/BaseZone.cs` |
| `Assets/Scripts/Health.cs` | `Assets/_Shared/Core/Combat/Health.cs` |
| `Assets/Scripts/Weapon.cs` | `Assets/_Shared/Core/Combat/Weapon.cs` |
| `Assets/Scripts/WeaponAudio.cs` | `Assets/_Shared/Core/Combat/WeaponAudio.cs` |
| `Assets/Scripts/VortexArena.Gameplay.asmdef` | `Assets/_Shared/Core/VortexArena.Core.asmdef` |
| `Assets/Prefabs/AK47_Red.prefab` | `Assets/_Shared/Arsenal/Prefabs/AK47_Red.prefab` |
| `Assets/Prefabs/M4_Blue.prefab` | `Assets/_Shared/Arsenal/Prefabs/M4_Blue.prefab` |
| `Assets/Prefabs/FX_HitSpark.prefab` | `Assets/_Shared/FX/FX_HitSpark.prefab` |
| `Assets/Scenes/VortexArena.unity` | `Assets/Arenas/Standard/A10x10/Scenes/Arena10x10.unity` |
| `Assets/ThirdPartyPackacges` (klasör + meta) | `Assets/ThirdPartyPackages` |

Sonra boşalan `Assets/Scripts`, `Assets/Scenes`, `Assets/Prefabs` klasörlerini **metalarıyla** sil (`git rm`).

Not: asmdef taşınıp adı değiştirilir ama **GUID'i korunur** (meta ile taşındığı için) — sahnedeki script bağları asmdef adından bağımsızdır, güvenli.

## Adım 3 — asmdef'i güncelle

`Assets/_Shared/Core/VortexArena.Core.asmdef` içeriğini şu hale getir (eski `VortexArena.Gameplay` içeriğinin üstüne):

```json
{
    "name": "VortexArena.Core",
    "rootNamespace": "VortexArena.Core",
    "references": [
        "Oculus.VR",
        "Oculus.Interaction",
        "Unity.InputSystem",
        "Unity.TextMeshPro",
        "UnityEngine.UI"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

(TMP + UGUI şimdiden eklenir — Faz 1+ UI kodu Core'a geldiğinde tekrar dokunmamak için; isimle referans yeterli.)

## Adım 4 — Namespace'ler + Team enum'unun ayrılması

1. `Weapon.cs` içindeki `public enum Team { Red, Blue }` satırını SİL ve **yeni dosya** oluştur: `Assets/_Shared/Core/Combat/Team.cs`:
   ```csharp
   namespace VortexArena.Core
   {
       public enum Team { Red, Blue }
   }
   ```
   (Yeni dosyanın .meta'sını Unity üretecek — Unity ilk açılışta otomatik; elle meta yazma.)
2. Namespace sarmalama (sınıf gövdesi DEĞİŞMEZ, yalnız `namespace` bloğu eklenir + gerekirse `using VortexArena.Core;`):
   - `ArenaBoundary`, `ArenaCalibrator`, `BaseZone` → `namespace VortexArena.Core.Arena`
   - `Health`, `Weapon`, `WeaponAudio` → `namespace VortexArena.Core.Combat`
   - `Team`, `VortexArena.Core` kökünde olduğu için `.Arena` ve `.Combat` içinden **using'siz** görünür (üst namespace çözümü). `BaseZone` ve `Weapon`'daki `Team` kullanımı derlenmeye devam eder.
3. Güvenlidir çünkü: Unity script bağı **GUID** iledir (namespace/assembly'den bağımsız); enum alanları int serialize edilir; UnityEvent kalıcı bağları hedef-obje referanslıdır.

## Adım 5 — EditorBuildSettings sahne path'i

`ProjectSettings/EditorBuildSettings.asset` içinde:

```yaml
  - enabled: 1
    path: Assets/Scenes/VortexArena.unity          # ESKİ
    path: Assets/Arenas/Standard/A10x10/Scenes/Arena10x10.unity   # YENİ
    guid: 629f3342b038beb4d975f35ba6e2dbd6         # AYNI KALIR
```

## Adım 6 — Paket manifesti trimi

`Packages/manifest.json`:

- **ÇIKAR:** `"com.meta.xr.sdk.all": "203.0.2"` ve `"com.unity.multiplayer.center": "1.0.1"`
- **EKLE** (alfabetik sıraya uygun yere):
  ```json
  "com.meta.xr.sdk.audio": "85.0.0",
  "com.meta.xr.sdk.core": "203.0.0",
  "com.meta.xr.sdk.interaction": "203.0.0",
  "com.meta.xr.sdk.interaction.ovr": "203.0.0",
  ```
- **DOKUNMA:** `com.unity.xr.interaction.toolkit` (XRI yedek), `com.unity.xr.openxr`, `com.unity.xr.management`, diğer her şey.

Gerekçe (CLAUDE.md'ye de yazılacak): umbrella, kullanılmayan `voice@85`'i çeker → cosmos'ta Android build'i `SDKTelemetry.aar ↔ OVRPlugin.aar` namespace çakışmasıyla kırdı. `audio@85.0.0` bu projede **spatializer olarak aktif** (`AudioManager.asset: Meta XR Audio`) → bugün lock'ta çözülen sürümle birebir pinlenir, davranış değişmez. Haptik `OVRInput.SetControllerVibration` = core → ayrı haptics paketi gerekmez. `Packages/packages-lock.json`'a elle dokunma — Unity ilk açılışta yeniden çözer.

## Adım 7 — StreamingAssets şablonu

`Assets/StreamingAssets/arena.json` oluştur:
```json
{ "serverIp": "", "serverPort": 47821 }
```
(Statik IP son-çare fallback'i; lobi ayar panelinden girilen değer PlayerPrefs ile bunu ezer. Çalışma ağacında silinmiş görünen `RuntimeActionBindings.json` ile İLGİSİZDİR — ona dokunma.)

## Adım 8 — `.claude/rules/` (3 dosya)

**`.claude/rules/ai-memory-scope.md`:**
```markdown
# Kural: AI hafızası yalnızca proje scope'unda

Bu proje ile ilgili hiçbir bilgi user/global scope'a kaydedilmez (`~/.claude/**/memory/` dahil).
- Hatırlanması gereken her şey repo içinde kalır: kök `CLAUDE.md` ve `.claude/rules/`.
- "Şunu hatırla / not al" denince hedef her zaman proje scope'udur (bu repo).
- Yeni kalıcı kural → `.claude/rules/` altına yeni `.md`; kalıcı proje bilgisi/mimari → `CLAUDE.md`.
```

**`.claude/rules/delegate-to-subagents.md`:**
```markdown
# Kural: İşi alt-ajanlara devret, ana bağlamı yalın tut

Uygulama işini (script yazımı, editor tool'ları — saf dosya yazımı) mümkün olduğunca
sub-agent'lara ver; kısa özet döndürsünler.
- Unity MCP orkestrasyonunu (derle / build / doğrula) ana thread'de, toplu yap.
- Aynı dosyaları düzenleyen ajanları sıralı çalıştır.
- Neden: ana bağlam sahne dump'ları ve tool çıktılarıyla hızla dolar; ağır iş ajanlarda
  kalınca ana bağlam orkestrasyon ve kararlara kalır.
```

**`.claude/rules/batch-build-verification.md`:**
```markdown
# Kural: Build/doğrulamayı batch'le — her işlemden sonra alma

Unity derleme/build/play doğrulaması tüm task'lar bitene kadar çalıştırılmaz.
- Tüm implementasyonu önce yaz; sonda TEK birleşik doğrulama geçişi yap.
- Ara doğrulamayı yalnız gerçek bir blocker için kullan, rutin teyit için değil.
```

## Adım 9 — `Docs/ArenaNet-Protokol.md`

`plan/protokol-v1.md` içeriğini kopyala; en üstteki blockquote'u (plan notu) şu satırla değiştir:

> Unity `VortexArena.Protocol` asmdef'i ile .NET sunucu aynı C# kaynaklarını derler (yapısal sapma imkânsız); bu doküman **semantiğin** tek doğruluk kaynağıdır. İki taraftan biri davranış değiştirecekse ÖNCE burası güncellenir.

## Adım 10 — `CLAUDE.md` (repo kökü)

Aşağıdaki içerikle oluştur (gerekirse dil/akıcılık rötuşu serbest, **teknik içerik sabit**):

```markdown
# VortexArena — Proje Talimatları (CLAUDE.md)

Free-roam VR PvP arena ürünü (işletmelere kurulum / LBE; Meta Quest 3 & 3S, Unity 6000.3.20f1, URP).
Oyuncular fiziksel alanda 1:1 yürür; farklı boyutlarda arenalar (10x10, 12x12, işletmeye özel),
farklı oyun modları/haritalar/silahlar. VR build = player, Windows build = admin (yönetim + izleme).
Online haberleşme: kendi .NET sunucumuz (`Server/`, standalone exe, offline LAN) — Mirror/NGO YOK.

> Kurallar `.claude/rules/` altındadır. Uygulama planı: `plan/` (faz faz). Protokol: `Docs/ArenaNet-Protokol.md` (TEK doğruluk kaynağı).

## Asset mimarisi (feature-first + asmdef)

- `Assets/_Shared/` — ortak. Ortak KOD yalnız bir asmdef altında: `Core/` (VortexArena.Core),
  `Net/Protocol` (VortexArena.Protocol — saf C#, server aynı dosyaları derler), `Net/Scripts`
  (VortexArena.Net), `App/Scripts` (VortexArena.App). Kod-dışı: `Arsenal/` (silah prefab+SO),
  `FX/`, `Environments/`, `Data/`, `Scenes/` (Boot, Lobby, AdminConsole).
  ⚠️ `_Shared` köküne asmdef'siz gevşek script koyMA (Assembly-CSharp'a düşer, kimse göremez).
- `Assets/Arenas/Standard/<AXxX>/` ve `Assets/Arenas/Venues/<İşletme>/` — arena kutuları:
  `{Scenes, Data, Prefabs}`. Arena = sahne + MapDefinition; arena-özel kod YAZILMAZ
  (marker bileşenleri Core'dan gelir).
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
hit_report → server doğrular → health_update. Keşif: beacon + lobide elle IP:port (PlayerPrefs) +
StreamingAssets/arena.json fallback. DTO'lar `_Shared/Net/Protocol/` — saf C#, server csproj
aynı dosyaları derler; Unity API'si girerse server derlemesi kırılır (bilinçli bekçi).

## Akış

Boot(index 0) → Android: Lobby / Windows: AdminConsole (editor testi için override).
Lobby (VR): IP paneli, roster, ready/takım. AdminConsole: launcher ekranı (server exe başlat
veya bağlan) → dashboard (roster, mod+harita seç, start, taktik üstten görünüm).
Arena sahneleri kendine yeten (kendi BB rig'i taşır).

## Yeni içerik ekleme reçeteleri

**Yeni arena:** `Assets/Arenas/Standard/<AdXxX>/{Scenes,Data,Prefabs}` → sahneyi mevcut arenadan
kopyala (Faz 4 sonrası: Tools > VortexArena > Create Arena From Template) → MapDefinition boyut/
spawn güncelle → Build Settings'e ekle (sahne adı = katalog anahtarı).
**Yeni mod:** `Assets/Modes/<Ad>/Scripts/VortexArena.Modes.<Ad>.asmdef` (refs: Core, Net, Protocol;
mevcut moddan JSON kopyala, name değiştir, .meta KOPYALAMA) + server tarafında `Modes/<Ad>Mode.cs`
(IGameMode) + `Docs/ArenaNet-Protokol.md`'ye modId ekle.
**Yeni silah:** prefab `_Shared/Arsenal/Prefabs/` + WeaponDefinition SO `_Shared/Arsenal/Data/`
(weaponId protokolde string — iki tarafta da aynı).
```

## Adım 11 — Doğrulama (TEK toplu geçiş; batch-build kuralı)

1. `git status` temiz mi kontrol et: yalnız beklenen taşımalar/eklemeler; kullanıcının `.vscode/settings.json` değişikliği ve silinmiş `RuntimeActionBindings.json` **staging dışında** kalsın.
2. Kullanıcıdan Unity'yi açmasını iste (veya Unity MCP varsa aç/refresh):
   - Import + derleme **0 hata** (asmdef `VortexArena.Core` çözülüyor; eski `VortexArena.Gameplay.csproj` kökte bayat kalır — Unity yeniden üretir, silmek gerekmiyor).
   - `Arena10x10` sahnesini aç: **missing script YOK**, prefab bağları sağlam (AK47/M4 sahne örnekleri Arsenal'daki prefaba bağlı kalmalı).
   - `Packages/packages-lock.json` yeniden çözüldü: `voice`/`platform`/`mrutilitykit`/`haptics`/`sdk.all` YOK; `core/interaction/interaction.ovr@203.0.0` + `audio@85.0.0` VAR.
   - Console'da Meta Project Setup Tool "sdk.all ekle" önerisi çıkarsa REDDET.
3. **Android build (APK)** al — cosmos dersi: editor derlemesi geçse bile asıl sınav Gradle. Build başarısızsa önce manifest trimini şüphelen: `manifest.json`'ı geri alıp yeniden dene, sonucu raporla (trim geri alınabilir tek dosya).
4. Smoke test (opsiyonel ama önerilen): Editor Play — sahne hatasız açılıyor, silah scriptleri exception atmıyor.
5. Commit: `Faz 0: feature-first klasör yapısı + asmdef katmanları + Meta paket trimi + CLAUDE.md/kurallar/protokol dokümanı`

## Çıktı kontrol listesi

- [ ] 6 script `_Shared/Core/{Arena,Combat}` altında, namespace'li; `Team.cs` ayrı dosya
- [ ] `VortexArena.Core.asmdef` (eski Gameplay'den, GUID korunmuş, TMP+UGUI refli)
- [ ] Prefablar `Arsenal/Prefabs` + `FX/`; sahne `Arenas/Standard/A10x10/Scenes/Arena10x10.unity`
- [ ] EditorBuildSettings path güncel; `ThirdPartyPackages` typo düzeltilmiş
- [ ] manifest trimlenmiş; lock'ta voice/platform yok; Android build GEÇTİ
- [ ] `CLAUDE.md`, `.claude/rules/×3`, `Docs/ArenaNet-Protokol.md`, `StreamingAssets/arena.json` mevcut
- [ ] Boş `Scripts/Scenes/Prefabs` klasörleri metalarıyla silinmiş
- [ ] Commit atılmış
