# Kural: Unity işinde önce MCP tool'u, shell en son basamak

Editörle ilgili **her** iş (konsol logu, derleme, sahne/hiyerarşi, asset/prefab, build, test, menü
öğesi, oynatma kipi, ekran görüntüsü, paket) **önce MCP tool'u ile** denenir. Shell (`unity cmd …`,
`dotnet`, `adb`) bu işlerde ilk hamle DEĞİL, **fallback**'tir.

**Neden:** MCP tool'ları yapılandırılmış JSON döner (filtreli, kırpılmış, parse edilmiş), canlı
editöre bağlanır ve Windows tırnak/kaçış/timeout tuzaklarına girmez. Ham shell çıktısını ayıklamak
hem bağlamı şişirir hem de sessizce yanlış okunur.

## Basamaklar — ancak bir üstteki GERÇEKTEN düşünce aşağı inilir

1. **`mcp__unity-editor-mcp__*`** — varsayılan ilk durak. Bu sunucu **Unity CLI'nın kendisidir**
   (140 komut, `unity list` ile birebir aynı): `get_console_logs`, `recompile` /
   `recompile_status`, `build` / `build_status`, `run_tests` / `test_status`, `menu`,
   `editor_status`, `get_scene_hierarchy`, `find_gameobjects`, `capture_scene_view`…
   ⚠️ Yani "CLI lazım" demek "shell lazım" demek değildir — CLI da MCP'nin arkasında.
2. **`mcp__UnityMCP__*`** (MCP for Unity) — `manage_*` ailesi + `mcpforunity://` resource'ları.
   1. basamakta karşılığı olmayan işler için: `manage_profiler` (frame/sample analizi),
   üretken asset servisleri, semantik asset arama, `manage_ui`, `manage_vfx`. İlk kullanımda
   `mcpforunity://custom-tools` resource'una bak (projeye özel tool'lar orada listelenir).
3. **Shell** — `unity cmd <komut>` / `dotnet` / `adb`. Komut **Windows komutudur**
   ([[windows-shell]]): PowerShell aracı, `D:\` yolları, `/tmp` yok.
4. **Dosya sistemi** — `Logs/`, `Library/`, `Editor.log` okumak. Konsol logu için bu **son** çare:
   `get_console_logs` / `read_console` zaten seviyeye göre filtreli döner.

## "Tool düştü" ne demek, ne demek değil

- **Düştü sayılır:** bağlantı hatası, "no Unity instance connected", timeout, komut yok.
- **Düşme sayılmaz:** yanlış argüman/şema hatası ya da boş sonuç. Önce çağrıyı düzelt, tekrar dene;
  başarısızlığı doğrulamadan bir alt basamağa **inme**.
- Tool adı listede görünüyor ama şeması yüklü değilse (deferred) `ToolSearch("select:<ad>")` ile
  şemayı yükle — "böyle bir tool yok" deyip shell'e kaçma.
- Editör kapalıyken **her iki sunucu da** bağlanamaz. Bu durumda shell'e inmek doğrudur; önce
  `editor_status` ile teyit et, sonra in.
- Birden çok Unity örneği açıksa hedefi `set_active_instance` ile sabitle.

## Kapsam sınırı

- Bu kural **Unity editörü** işleri içindir. Repo dosyalarını okumak/yazmak/aramak için MCP değil
  yerleşik araçlar kullanılır (Read/Write/Edit/Grep/Glob); "bu nasıl çalışıyor / nerede" sorularında
  [[auggie-first-search]].
- MCP ucuz diye her adımda derleme/konsol alma — toplu doğrulama kuralı aynen geçerli
  ([[batch-build-verification]]).
- Hangi komut ne yapar, kayıtlar nasıl kurulur, hangi tuzakları var: [[unity-cli]]. Buradaki kural
  **hangi kapıdan girileceğidir**.

## Shell'e inildiyse kullanıcıya söyle

Fallback sessiz yapılmaz: tek cümleyle "şu tool şu sebeple düştü, shell'den koştum" denir. Aksi
hâlde bozuk MCP kaydı aylarca fark edilmez ve her oturum aynı yavaş yoldan gider.
