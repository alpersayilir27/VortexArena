# Kural: Unity erişimi — kapı, basamaklar, komutlar, Windows

> # ⛔ HER ŞEYDEN ÖNCE: UnityMCP kapısı
> Her oturumun ilk adımı **`UnityMCP` ayakta mı** kontrolüdür; kapı kapalıyken üretilen "Unity
> cevabı" YAML tahminine dayanır ve sessizce yanlış olur. Bu yüzden kapı diğer bütün kuralların
> ÜSTÜNDEDİR.

## 1. Kapı kontrolü

Kullanıcının isteği ne olursa olsun, önce **okuma-yazma yapmayan hafif bir çağrı**:
`mcp__UnityMCP__manage_editor` → `action: "telemetry_status"` (şema yüklü değilse
`ToolSearch("select:mcp__UnityMCP__manage_editor")`). Başarılıysa iş §4 basamaklarıyla sürer.

## 2. Kapı düştüyse: önce SEBEBİ ayır

"Unity kapalı" diye varsayma — süreçlere bak (MCP karşılığı yok, shell meşru):
`Get-Process Unity, unity-mcp-server, relay_win -ErrorAction SilentlyContinue | Select-Object Name, Id`

| Bulgu | Anlamı | Ne yapılır |
|---|---|---|
| Hiç `Unity.exe` yok | Editör gerçekten kapalı | §3 (işe göre karar) |
| `Unity.exe` var ama MCP düşüyor | Köprü arızası (8080 ayakta değil / başka örneğe pinli) | Sessizce esnetme: **kullanıcıya söyle**, `set_active_instance`'ı ve köprüyü kontrol et |

## 3. Editör kapalıysa kararı AJAN verir: iş Unity verisine dokunuyor mu?

- **Dokunuyorsa dur.** (Prefab/sahne/asset içeriği, bileşen alanı, hiyerarşi, materyal/shader,
  editor tool'u çalıştırma, konsol logu, "sahnede ne var"…) Tek çıktı: **"MCP'yi çalıştır."** +
  neyin engellendiği tek satır. Tahmin yürütme, YAML **grep'leyerek** cevap uydurma.
- **Dokunmuyorsa devam et, sorma.** (git · `Docs/` · `plan/` · `.claude/` · `Server/` ve
  `launcher/` C# kaynağı · `scripts/` · `updater/` · tasarım sorusu · repo içi Read/Grep/Edit ile
  hallolan kod işi.) Cevabın başına **tek satır**: *"UnityMCP kapalı — bu iş Unity verisine
  dokunmuyor, devam ettim."*
- **Sınırdaysa** dokunmayan kısmı bitir, dokunanı **yapma**, neyin MCP beklediğini yaz.
- **"Zorla devam et"** gibi **açık** talimatta kural düşer: dokunan iş de yapılır, ama her varsayım
  **açıkça yazılır** ("şu alanın adının `x` olduğunu varsaydım"), iş **"Unity açılınca
  doğrulanacaklar"** listesiyle biter, izin yalnız o iş içindir. ⚠️ Yasak yine yasaktır: YAML
  okuyarak "teyit ettim" denmez, derleme/build/test yine ajana kapalıdır ([[is-akisi]]).

## 4. Basamaklar — ancak bir üstteki GERÇEKTEN düşünce aşağı inilir

Editörle ilgili **her** iş önce MCP ile denenir: MCP filtreli/parse edilmiş JSON döner ve Windows
tırnak/kaçış/timeout tuzaklarına girmez.

1. **`mcp__unity-editor-mcp__*`** — varsayılan ilk durak; bu sunucu **Unity CLI'nın kendisidir**
   (140 komut, `unity list` ile birebir aynı): `get_console_logs`, `recompile`/`recompile_status`,
   `build`/`build_status`, `run_tests`/`test_status`, `menu`, `editor_status`,
   `get_scene_hierarchy`, `find_gameobjects`, `capture_scene_view`… ⚠️ "CLI lazım" demek "shell
   lazım" demek değildir.
2. **`mcp__UnityMCP__*`** (MCP for Unity) — `manage_*` + `mcpforunity://` resource'ları; 1.
   basamakta karşılığı olmayan işler: `manage_profiler`, üretken asset servisleri, semantik asset
   arama, `manage_ui`, `manage_vfx`. İlk kullanımda `mcpforunity://custom-tools`'a bak.
3. **Shell** — `unity cmd <komut>` / `dotnet` / `adb`; komut **Windows komutudur** (§7).
4. **Dosya sistemi** — `Logs/`, `Library/`, `Editor.log`. Konsol logu için **son** çare:
   `get_console_logs` / `read_console` zaten seviyeye göre filtreli döner.

⚠️ Basamaklar "hangi kapıdan" sorusunu cevaplar, "yapılır mı" sorusunu değil: derleme, build, test
ve oynatma kipi ajana KAPALIDIR → [[is-akisi]].

**"Tool düştü":** bağlantı hatası, "no Unity instance connected", timeout, komut yok.
**Düşme sayılmaz:** yanlış argüman/şema hatası ya da boş sonuç — çağrıyı düzelt, başarısızlığı
doğrulamadan alt basamağa **inme**. Şeması yüklü değilse (deferred) `ToolSearch("select:<ad>")`;
"böyle bir tool yok" deyip shell'e kaçma. Birden çok Unity örneğinde hedefi `set_active_instance`
ile sabitle. **Shell'e inildiyse kullanıcıya söyle** ("şu tool şu sebeple düştü, shell'den
koştum") — yoksa bozuk MCP kaydı aylarca fark edilmez.

## 5. ⚠️ Shell SON basamaktır — MCP ya da yerleşik araç varken açılmaz

Ölçüt: **aynı işi bir MCP tool'u ya da yerleşik araç yapabiliyorsa shell ÇALIŞTIRILMAZ.**

| İş | Doğru kapı | Shell'de YAPMA |
|---|---|---|
| Dosya okuma | `Read` | `cat`, `sed -n`, `Get-Content` |
| Dosya arama (ada göre) | `Glob` | `find`, `ls -R`, `Get-ChildItem -Recurse` |
| İçerik arama | `Grep` | `grep`, `rg`, `Select-String` |
| Dosya yazma/düzenleme | `Write` / `Edit` | `echo >`, `sed -i`, `Set-Content` |
| Prefab/sahne/asset içeriği, bileşen alanı, hiyerarşi | `mcp__UnityMCP__manage_prefabs` · `manage_gameobject` · `manage_asset` · `manage_scriptable_object` | prefab/asset **YAML'ını grep'lemek** |
| Konsol, seçim, editör durumu, menü öğesi | `mcp__unity-editor-mcp__*` | — |
| "Bu nasıl çalışıyor / nerede" | `mcp__auggie__codebase-retrieval` | — |

**Shell'in meşru kaldığı yerler:** `git`, `adb`, `dotnet` ve MCP gerçekten düştüğünde `unity cmd …`.

- ⚠️ **YAML grep'lemek özellikle yanlıştır:** fileID referansları, gömülü prefab örnekleri ve
  stripped transform'lar sorunun cevabını taşımaz; MCP çözülmüş hiyerarşiyi ve alan değerlerini
  döner.
- ⚠️ **Geçici python/node betiği YAZILMAZ** (ikinci, hatalı olabilecek bir uygulama üretir).
- ⚠️ **"Tek satır, shell daha hızlı" bir gerekçe DEĞİLDİR.**
- Repo dosyaları yerleşik araçlarla okunur/yazılır; Unity'nin **kendi** verisi istisnadır (kapı
  MCP'dir, dosyanın kendisi değil). MCP ucuz diye her adımda konsol/derleme alma ([[is-akisi]]).

## 6. Komutlar ve MCP kayıtları

`com.unity.pipeline` kurulu; editör açıkken CLI ona bağlanır (`unity`, `%LOCALAPPDATA%\Unity\bin`).

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

⚠️ Bunları ajan kendiliğinden çalıştırmaz ([[is-akisi]]); burası kullanıcı istediğinde **nasıl**
koşulacağıdır.

- `unity cmd …` **yalnız editör açıkken** çalışır.
- Menü öğesi: `unity cmd menu --path "Tools/VortexArena/…"` (pozisyonel argüman DEĞİL, `--path`;
  yanlış verilirse tüm menüyü listeler). ⚠️ **Modal dialog açan öğelerde dikkat:**
  `ServerConfigExporter` sonda `EditorUtility.DisplayDialog` gösteriyor → dialog kapanana kadar ana
  thread kilitlenir, Pipeline "Main thread operation timed out" verir ve tekrar denemek **yeni
  dialog kuyruğu** üretir. Timeout görürsen dialog'u kapat, tekrar deneme.
- `unity build` / `unity test` ayrı bir batch-mode editör başlatır → proje açıkken **proje kilidine
  takılır**; editör açıkken in-editor `build` / `run_tests` kullanılır.

**MCP sunucuları — proje scope, `.mcp.json`'da.** User scope'a ya da `~/.claude.json` içindeki
`projects[...].mcpServers` altına kayıt YAPMA.

| Kayıt | Komut | Tool seti |
|---|---|---|
| `unity-editor-mcp` | `unity mcp` | 140 komut — `unity list` ile birebir aynı (Unity CLI + `com.unity.pipeline`) |
| `auggie` | `cmd /c auggie --mcp` | 1 tool: `codebase-retrieval` (Augment CLI, `npm i -g @augmentcode/auggie`) |
| `UnityMCP` | HTTP `http://127.0.0.1:8080/mcp` | ~55 tool: `manage_*` + `mcpforunity://` (`com.coplaydev.unity-mcp`) |
| `blender` | `uvx blender-mcp` | Blender köprüsü — Blender'da eklenti etkin + *Start MCP Server* tıklanmış olmalı (localhost:9876) |

- `UnityMCP` HTTP transport olduğu için köprü 8080'de ayakta ve **editör açık** olmalıdır.
- → kayıt ayrıntıları (`auggie`'nin `cmd /c` gerekçesi, relay kaydının neden olmadığı, CLI'da
  karşılığı olmayan tool'lar) ve **yeni PC kurulumu**: `Docs/Gelistirici/Ortam-Kurulumu.md`

## 7. Geliştirici makinesi HER ZAMAN Windows

Quest APK'sı da Windows'tan build ediliyor. Linux/macOS varsayımıyla komut yazma — `ls -la`, `cat`,
`grep`, `/tmp/...`, `export VAR=`, `&&` zinciri, `.sh` betiği ya patlar ya sessizce yanlış çalışır.

- **Varsayılan shell aracı PowerShell** (Bash aracı da var — Git Bash/POSIX sh — ama `unity`,
  `dotnet`, `adb` ve Windows yolları için PowerShell tercih edilir). İkisi **ayrı sözdizimidir**;
  hangi araca yazdığını bilerek yaz.
- **PowerShell 5.1:** `&&` / `||` YOK → `A; if ($?) { B }`. Ternary / `??` / `?.` yok.
  `head`/`tail`/`which`/`touch`/`wc` yok → `Get-Content -TotalCount/-Tail`,
  `(Get-Command x).Source`, `New-Item`. `2>/dev/null` → `2>$null`. `VAR=x cmd` yok →
  `$env:VAR='x'; cmd`. Native exe'de `2>&1` kullanma ($? bozulur, stderr zaten yakalanıyor).
- **Yollar:** `D:\games\vortexarena\…`; boşluklu yol tırnaklanır. `/tmp` yoktur — geçici dosya
  **scratchpad** dizinine yazılır, repoya değil.
- **Dosya işi shell'e yaptırılmaz** (§5). **Betikler `.bat` ya da `.ps1`** olur (`scripts/`
  altında, çift tıklanabilir); `.sh` yazılmaz.
