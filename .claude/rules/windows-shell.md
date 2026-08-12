> ⛔ **Önce kapı:** `UnityMCP` ayakta değilse **Unity verisine dayanan** iş yapılmaz (tek çıktı
> **"MCP'yi çalıştır."**); dokunmayan iş sürer → [[unitymcp-zorunlu]]

# Kural: Geliştirici makinesi HER ZAMAN Windows

Bu projede geliştirme makinesi istisnasız **Windows**'tur (Quest APK'sı da Windows'tan build
ediliyor, admin build'i zaten Windows). Linux/macOS varsayımıyla komut yazma — `ls -la`, `cat`,
`grep`, `/tmp/...`, `export VAR=`, `&&` zinciri, `.sh` betiği bu makinede ya patlar ya sessizce
yanlış çalışır.

- **Varsayılan shell aracı PowerShell.** Bash aracı da var (Git Bash, POSIX sh) ama `unity`,
  `dotnet`, `adb` çağrıları ve Windows yolları için PowerShell tercih edilir. İkisi **ayrı
  sözdizimidir**; hangi araca yazdığını bilerek yaz, karıştırma.
- **PowerShell 5.1 tuzakları:** `&&` / `||` YOK → `A; if ($?) { B }`. Ternary / `??` / `?.` yok.
  `head`/`tail`/`which`/`touch`/`wc` yok → `Get-Content -TotalCount/-Tail`, `(Get-Command x).Source`,
  `New-Item`. `2>/dev/null` → `2>$null`. `VAR=x cmd` yok → `$env:VAR='x'; cmd`. Native exe'de
  `2>&1` kullanma (stderr zaten yakalanıyor, `$?`'i bozar).
- **Yollar:** `D:\games\vortexarena\…`. Boşluklu yol tırnaklanır
  (`"Assets/Low Poly AR Weapon Pack 1"`). `/tmp` yoktur — geçici dosya **scratchpad** dizinine
  yazılır, repoya değil.
- **Dosya işi shell'e yaptırılmaz:** okuma/yazma/arama için Read · Write · Edit · Grep · Glob
  (`Get-Content` / `Set-Content` / `Select-String` / `Get-ChildItem -Recurse` değil).
- **Betikler `.bat` ya da `.ps1`** olarak yazılır (`scripts/` altında, çift tıklanabilir);
  `.sh` yazılmaz.
- Unity işinde shell **zaten son basamaktır** ([[unity-mcp-first]]) — oraya inildiğinde komut
  Windows komutu olur; `unity cmd …` örnekleri [[unity-cli]] içinde.
