---
title: Ortam Kurulumu
---

# Ortam Kurulumu

Yeni bir bilgisayarda projeyi çalışır hâle getirme ve **araç zincirinin** (Unity CLI + MCP
sunucuları) kurulumu. Ömründe bir kez okunur; günlük çalışma kuralları `.claude/rules/` altındadır.

> Kurulum bitince oyun tarafına giriş: [İlk Adımlar](Ilk-Adimlar.md).

---

## 1. Yeni bilgisayarda kurulum (repo klonlandıktan sonra)

| # | Ne | Nasıl |
|---|---|---|
| 1 | **Unity Hub + Editor 6000.3.20f1** | Android Build Support + SDK/NDK + OpenJDK modülleriyle. CLI kuruluysa: `unity install 6000.3.20f1 -m android --cm --accept-eula` (`--cm` = çocuk modülleri de kurulur) |
| 2 | **Unity CLI** | PowerShell: `$env:UNITY_CLI_CHANNEL='beta'; irm https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.ps1 \| iex` → `%LOCALAPPDATA%\Unity\bin` PATH'e girer. Doğrula: `unity --version`, sonra `unity auth login` |
| 3 | **Git + Git LFS** | `git lfs install` — repo LFS kullanıyor, manifest'te git URL'li paket var |
| 4 | **.NET 10 SDK** | Sunucuyu ve launcher'ı derlemek için |
| 5 | **Defender dışlamaları** | `scripts\defender-exclusions.cmd` → sağ tık, *Yönetici olarak çalıştır*. **Projeyi ilk kez açmadan önce** |
| 6 | Projeyi Unity'de bir kez aç | UPM paketleri (`com.unity.pipeline` dahil) manifest'ten iner |
| 7 | `scripts\docs-setup.bat` | Bu dokümanı yerel olarak sunmak için (bir kez); sonrası `docs-serve.bat` |
| 8 | Claude Code'u projede aç | `.mcp.json`'daki sunucular için **tek seferlik onay** ister |

Sunucu tarafını hiç derlemeyeceksen 4. adımı atlayabilirsin — ama o zaman **hiç maç kuramazsın**:
maç verisini yalnız sunucu üretir ve maçı yalnız bir admin başlatır.

**5. adım neden ilk açılıştan önce:** ilk import ve ilk IL2CPP build'i projenin en çok dosya üreten
adımlarıdır — on binlerce `.cpp`/`.obj` üretilir ve `Library/` sürekli okunur; Defender'ın gerçek
zamanlı koruması her dosya açılışında araya girip paralel derlemenin önünde kuyruk oluşturur, build
ve import sürelerinde %20-40 bandında fark eder. Betik repo kökünü, Unity kurulumunu, Unity/Hub ve
paket cache'lerini (`.gradle`, `.nuget`, npm) ve build zincirinin exe'lerini dışlar; yolları kendi
konumundan türetir — elle yol ekleme, gerekiyorsa `-ExtraPath` geç. Geri alma `-Remove`, liste
`-List`; ayrıntı ve Dev Drive alternatifi `scripts/README.md`.

> ⚠️ Dışlanan klasörler **artık taranmıyor** — oraya indirme yapma. Asset store paketini ya da
> GitHub'dan çektiğin arşivi önce başka bir yere indirip kontrol et.

> ⚠️ **Smart App Control açıksa dışlamalar işe yaramaz.** SAC bir antivirüs ayarı değil, Code
> Integrity politikasıdır: dışlama listesini okumaz. Unity **Burst**'ün `Library/BurstCache/JIT/`
> altına ürettiği **imzasız** DLL'i engeller (uyarıyı veren budur; `CodeIntegrity` olayı 3077) ve
> imzasız `deploy\*.exe` çıktılarımızı da engelleyebilir. Betik açılışta uyarır; kapatması
> *Windows Güvenliği → Uygulama ve tarayıcı denetimi → Akıllı Uygulama Denetimi*, ⚠️ **geri
> açılamaz**.

---

## 2. MCP sunucularının kaydı

Üç kayıt da **proje scope'undadır**: tek kaynak repo kökündeki `.mcp.json`. User scope'a ya da
`~/.claude.json` içindeki `projects[...].mcpServers` altına kayıt açılmaz.

### `auggie` Windows'ta neden `cmd /c` ile çağrılır

`.mcp.json`'daki satır `cmd /c auggie --mcp` biçimindedir. Sebebi npm shim'inin `.cmd` olmasıdır:
doğrudan `spawn("auggie")` **ENOENT** verir, `spawn("auggie.cmd")` ise Node'un CVE-2024-27980
kısıtına takılıp **EINVAL** verir. macOS/Linux'ta `command: "auggie", args: ["--mcp"]` yeterlidir.
Banner satırları stderr'e gider, stdout temiz JSON-RPC kalır (protokolü kirletmez); ilk çağrı
workspace'i indekslediği için gecikebilir.

### `unity-mcp` (AI Assistant relay) kaydı neden yok

`com.unity.ai.assistant` içindeki bridge onayı **canlı bağlantı başına** tutuyor
(`TransportStore.ApprovalState`). Bir kez `Denied` olan transport, Project Settings > AI >
Unity MCP'den Accept'lense bile geri dönmüyor — `ConnectionItemControl.OnAccept` yalnız *bekleyen*
onayı tamamlıyor — ve her çağrı "Connection revoked" veriyor.

Yine de gerekiyorsa reçetesi: `.mcp.json`'a `${USERPROFILE}/.unity/relay/relay_win.exe --mcp`
olarak yazılır, editörde onaylanır ve **onaydan sonra bağlantı yenilenir** (`/mcp` → Reconnect).

### Unity CLI'da karşılığı olmayan tool'lar

`unity-editor-mcp` (140 komut) editör/asset/sahne/script/paket/konsol/capture işlerinin tamamını
kapsar, çoğunu daha ince taneli olarak. Relay setinde olup CLI'da **karşılığı olmayanlar**:

| Eksik | Ne yapar |
|---|---|
| `AssetGeneration_*` | Unity AI üretken asset servisleri (metin→texture/sprite/material/ses/animasyon; puan harcar) |
| `Profiler_*` (12 tool) | Frame/sample bazlı süre ve GC analizi — CLI'da yalnız toplu `get_performance_stats` var |
| `FindProjectAssets`'in semantik/görsel kipi | Doğal dille / görsele benzerlikle asset arama |
| `AudioClip_Edit` | Ses klibi düzenleme |
| `SceneView_CaptureMultiAngleSceneView` | Çok açılı yakalama — CLI'da tek açı `capture_scene_view`, döngüyle taklit edilir |

Bunlara ihtiyaç doğarsa relay yukarıdaki reçeteyle geçici olarak geri eklenir.

> `Assets/…` içindeki Meta XR core'un kendi `Meta.MCPBridge`'i ayrı bir köprüdür, MCP kaydı değil.

---

**Günlük kullanım:** hangi kapıdan hangi iş yapılır, `unity cmd …` komutları ve Windows shell
tuzakları `.claude/rules/unity-erisim.md`'dedir.
