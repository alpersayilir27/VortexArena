> ⛔ **Önce kapı:** `UnityMCP` ayakta değilse **Unity verisine dayanan** iş yapılmaz (tek çıktı
> **"MCP'yi çalıştır."**); dokunmayan iş sürer → [[unitymcp-zorunlu]]

# Kural: Editör doğrulaması Unity CLI (Pipeline) üzerinden

Derleme/konsol/build/test doğrulaması **Unity CLI** ile yapılır (`unity`, `%LOCALAPPDATA%\Unity\bin`).
Projede `com.unity.pipeline` kurulu; editör açıkken CLI ona bağlanır.

⚠️ **Giriş kapısı shell DEĞİL, MCP'dir** ([[unity-mcp-first]]): aynı 140 komut
`mcp__unity-editor-mcp__*` tool'ları olarak duruyor — `get_console_logs`, `recompile`, `build`,
`run_tests`, `menu` … Aşağıdaki komut satırları o tool'un **karşılığı**dır; MCP çağrısı gerçekten
düştüğünde (bağlantı yok / timeout) shell'den koşulur. Shell'e inildiğinde komut Windows
komutudur → [[windows-shell]].

```bash
unity status                       # bağlı editör (port/PID/versiyon)
unity list                         # 140 komut
unity cmd recompile                # editör minimize/odaksız olsa da derler
unity cmd recompile_status         # idle | compiling | completed
unity cmd get_console_logs --json  # hata/uyarı
unity cmd build / build_status     # in-editor build + tam BuildReport
unity cmd run_tests / test_status
unity shell                        # warm REPL (çok komut tek process)
```

⚠️ **Bu tabloyu ajan kendiliğinden çalıştırmaz:** `recompile` / `build` / `run_tests` (ve shell
karşılıkları) kullanıcıya aittir → [[derleme-kullaniciya-aittir]]. Buradaki komutlar kullanıcı
açıkça istediğinde **nasıl** koşulacağını anlatır.

- `unity cmd …` **yalnız editör açıkken** çalışır.
- `unity cmd menu --path "Tools/VortexArena/…"` ile menü öğesi çalıştırılır (path pozisyonel
  argüman olarak DEĞİL, `--path` bayrağıyla verilir; yanlış verilirse tüm menüyü listeler).
  ⚠️ **Modal dialog açan menü öğelerinde dikkat:** `ServerConfigExporter` sonda
  `EditorUtility.DisplayDialog` gösteriyor → dialog kapatılana kadar Unity ana thread'i kilitlenir,
  Pipeline "Main thread operation timed out" verir ve komutu tekrar denemek **yeni dialog kuyruğu**
  üretir. Timeout görürsen önce editördeki dialog'u kapat, tekrar deneme.
- `unity build` / `unity test` ayrı bir batch-mode editör başlatır → proje açıkken **proje kilidine
  takılır**. Editör açıkken in-editor `build` / `run_tests` komutlarını kullan.
- Toplu doğrulama kuralı geçerli ([[batch-build-verification]]): tüm implementasyon bitince tek geçiş.

## MCP sunucuları — **proje scope**, repo'daki `.mcp.json`'da

User scope'a kayıt YAPMA, `.mcp.json` tek kaynak.

| Kayıt | Komut | Tool seti | Kaynak |
|---|---|---|---|
| `unity-editor-mcp` | `unity mcp` | 140 komut — `unity list` ile birebir aynı | Unity CLI + `com.unity.pipeline` |
| `auggie` | `cmd /c auggie --mcp` | 1 tool: `codebase-retrieval` (Augment indeksinden doğal dille kod bağlamı) | Augment CLI (`npm i -g @augmentcode/auggie`) |
| `UnityMCP` | HTTP `http://127.0.0.1:8080/mcp` | ~55 tool: `manage_*` ailesi + `mcpforunity://` resource'ları (custom-tools, instances, editor/state) | `com.coplaydev.unity-mcp` (MCP for Unity) |

- `auggie` Windows'ta **`cmd /c` ile** çağrılır: npm shim `.cmd` olduğu için doğrudan
  `spawn("auggie")` ENOENT, `spawn("auggie.cmd")` ise Node'un CVE-2024-27980 kısıtıyla EINVAL verir.
  macOS/Linux'ta `command: "auggie", args: ["--mcp"]` yeterli. Banner satırları stderr'e gidiyor,
  stdout temiz JSON-RPC (protokolü kirletmiyor). İlk çağrıda workspace'i indeksler.
- **`unity-mcp` (AI Assistant relay) kaydı YOKTUR.** Sebep: `com.unity.ai.assistant`
  içindeki bridge, onayı **canlı bağlantı başına** tutuyor (`TransportStore.ApprovalState`);
  bir kez `Denied` olan transport, Project Settings > AI > Unity MCP'den Accept'lense bile geri
  dönmüyor (`ConnectionItemControl.OnAccept` yalnız *bekleyen* onayı tamamlıyor + kalıcı kaydı
  günceller) → her çağrı "Connection revoked" veriyor. Eklenecekse: `.mcp.json`'a
  `${USERPROFILE}/.unity/relay/relay_win.exe --mcp` olarak yaz, editörde onayla ve **onaydan
  sonra bağlantıyı yenile** (`/mcp` → Reconnect).
- Kapsam farkı (52 tool'luk `unity-mcp` seti vs 140 komutluk CLI seti): editör/asset/sahne/script/
  paket/konsol/capture işlerinin tamamı CLI'da var (çoğu daha ince taneli). CLI'da **karşılığı
  OLMAYANLAR**: (1) `AssetGeneration_*` — Unity AI üretken asset servisleri (metin→texture/sprite/
  material/ses/animasyon, puan harcar), (2) `Profiler_*` 12 tool — frame/sample bazlı süre ve GC
  analizi (CLI'da yalnız toplu `get_performance_stats` var), (3) `FindProjectAssets`'in semantik/
  görsel arama kipi, (4) `AudioClip_Edit`, (5) `SceneView_CaptureMultiAngleSceneView` (CLI'da
  `capture_scene_view` tek açı; döngüyle taklit edilir). Bunlara ihtiyaç doğarsa relay'i yukarıdaki
  reçeteyle geçici olarak geri ekle.
- `Assets/…` içindeki Meta XR core'un kendi `Meta.MCPBridge`'i ayrı bir köprüdür, MCP kaydı değil.
- `UnityMCP` kaydı **proje scope'undadır** — `.mcp.json`'da; `~/.claude.json` içindeki
  `projects[...].mcpServers` altına local scope kaydı açılmaz (tek kaynak `.mcp.json`).
  HTTP transport olduğu için köprü (`com.coplaydev.unity-mcp`, Python/uv tabanlı) 8080'de
  ayakta olmalı; editör kapalıyken tool'lar bağlanamaz. Birden çok Unity örneği açıksa
  `set_active_instance` ile hedefi sabitle.

## Yeni PC'de kurulum (repo klonlandıktan sonra)

1. **Unity Hub + Editor 6000.3.20f1** — Android Build Support + SDK/NDK + OpenJDK modülleriyle.
   CLI kuruluysa: `unity install 6000.3.20f1 -m android --cm --accept-eula`
   (`--cm` çocuk modülleri = SDK/NDK/OpenJDK de kurar).
2. **Unity CLI** (PowerShell): `$env:UNITY_CLI_CHANNEL='beta'; irm https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.ps1 | iex`
   → `%LOCALAPPDATA%\Unity\bin` PATH'e eklenir; `unity --version` ile doğrula, `unity auth login`.
3. **Git + Git LFS** (repo LFS kullanıyor, manifest'te git URL'li paket var): `git lfs install`.
4. **Defender dışlamaları** — `scripts\defender-exclusions.cmd`, sağ tık → *Yönetici olarak
   çalıştır*. **Projeyi ilk kez açmadan önce** yapılır: ilk import ve ilk IL2CPP build'i en çok
   dosya üreten adımlardır. Repo kökünü, Unity kurulumunu, Unity/Hub + paket cache'lerini ve build
   zincirinin exe'lerini dışlar (yolları kendi konumundan türetir). Geri alma `-Remove`, liste
   `-List`; ayrıntı ve Dev Drive alternatifi `scripts/README.md`.
   ⚠️ Dışlanan klasöre indirme yapılmaz — oralar artık taranmıyor.
5. Projeyi bir kez Unity'de aç → UPM paketleri (`com.unity.pipeline` dahil) manifest'ten otomatik iner.
6. Claude Code projede ilk açıldığında `.mcp.json`'daki sunucular (`unity-editor-mcp`, `auggie`,
   `UnityMCP`) için **tek seferlik onay** ister.

