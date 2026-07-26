# Kural: Editör doğrulaması Unity CLI (Pipeline) üzerinden

Derleme/konsol/build/test doğrulaması **Unity CLI** ile yapılır (`unity`, `%LOCALAPPDATA%\Unity\bin`).
Projede `com.unity.pipeline` kurulu; editör açıkken CLI ona bağlanır.

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

İkisi de resmi Unity ürünü; user scope'a kayıt YAPMA, `.mcp.json` tek kaynak.

| Kayıt | Komut | Tool seti | Kaynak |
|---|---|---|---|
| `unity-editor-mcp` | `unity mcp` | 140 komut — `unity list` ile birebir aynı | Unity CLI + `com.unity.pipeline` |
| `unity-mcp` | `${USERPROFILE}/.unity/relay/relay_win.exe --mcp` | 52 tool (sahne/prefab/asset/script düzenleme, asset generation, profiler, camera capture) | `com.unity.ai.assistant` → `Modules/Unity.AI.MCP.Editor/Tools/` |

- Relay binary'sini **Unity Editor açılışta kendisi kurar** (`~/.unity/relay/`) — repoya girmez,
  elle indirilmez. Editör kapalıyken relay bağlanır ama tool listesi zaman aşımına uğrar; normaldir.
- macOS/Linux'ta relay dosya adı farklı (`relay_mac_arm64…`); `.mcp.json`'daki Windows yolunu
  o makinede güncellemek gerekir.
- `Assets/…` içindeki Meta XR core'un kendi `Meta.MCPBridge`'i ayrı bir köprüdür, MCP kaydı değil.
- `com.coplaydev.unity-mcp` (manifest'te, üçüncü parti) hiçbir MCP kaydı tarafından
  kullanılmıyor — sunucusu Python/uv tabanlı ve kayıtlı değil. Silinirse UPM'in git ile
  GitHub'dan paket çekme ihtiyacı da ortadan kalkar.

## Yeni PC'de kurulum (repo klonlandıktan sonra)

1. **Unity Hub + Editor 6000.3.20f1** — Android Build Support + SDK/NDK + OpenJDK modülleriyle.
   CLI kuruluysa: `unity install 6000.3.20f1 -m android --cm --accept-eula`
   (`--cm` çocuk modülleri = SDK/NDK/OpenJDK de kurar).
2. **Unity CLI** (PowerShell): `$env:UNITY_CLI_CHANNEL='beta'; irm https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.ps1 | iex`
   → `%LOCALAPPDATA%\Unity\bin` PATH'e eklenir; `unity --version` ile doğrula, `unity auth login`.
3. **Git + Git LFS** (repo LFS kullanıyor, manifest'te git URL'li paket var): `git lfs install`.
4. Projeyi bir kez Unity'de aç → UPM paketleri (`com.unity.pipeline` dahil) manifest'ten otomatik
   iner, AI Assistant relay'i kurulur.
5. Claude Code projede ilk açıldığında `.mcp.json`'daki iki sunucu için **tek seferlik onay** ister.

