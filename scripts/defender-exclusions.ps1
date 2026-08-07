<#
.SYNOPSIS
  VortexArena gelistirici makinesinde Windows Defender dislamalarini (exclusions)
  kurar / kaldirir / listeler.

.DESCRIPTION
  Sorun: IL2CPP build'i on binlerce .cpp ve .obj dosyasi uretir, Library/ klasoru
  surekli yazilip okunur, shader varyantlari paralel derlenir. Defender'in gercek
  zamanli korumasi HER dosya acilisinda araya girer ve cok cekirdekli derlemenin
  onunde kuyruk olusturur. Proje boyutuna gore build ve import surelerinde %20-40
  bandinda kazanc normaldir.

  Betik iki tur dislama yazar:
    * Yol (ExclusionPath)    - repo, Unity kurulumu, Unity/Hub cache'leri, paket
                               cache'leri (gradle, nuget, npm), IDE klasorleri.
    * Islem (ExclusionProcess) - build zincirindeki exe'ler (Unity, bee_backend,
                               il2cpp, cl, link, MSBuild, dotnet, java, adb...).

  Yollar betikten TURETILIR: repo koku betigin bir ustudur, Unity Editor koku
  Hub'in ikincil kurulum yolundan da okunur. Elle yol duzenlemek gerekmez;
  baska bir Unity projesi de dislanacaksa -ExtraPath ile gec.

  Idempotent: zaten var olan dislama tekrar eklenmez, "zaten var" diye gecilir.

  ASCII-CI CIKTI: konsol kod sayfasi (cp857) Turkce karakterleri bozdugu icin
  ciktida sapkasiz/noktasiz harf kullanilir. Yeni metin eklerken bu kurali koru.

  GUVENLIK NOTU: dislanan klasorler artik taranmaz. Oraya indirme yapma; asset
  store paketi / GitHub'dan cekilen arsiv once baska bir yere indirilip kontrol
  edilir. Islem dislamasi daha genis bir kapidir (o surecin DOKUNDUGU her dosya
  taranmaz) - listeye kendi basina exe ekleme.

.PARAMETER Remove
  Ayni listeyi geri alir (Remove-MpPreference).

.PARAMETER List
  Hicbir sey degistirmez; makinede kayitli TUM dislamalari basar. Yonetici yine
  gerekir: Defender listeyi yetkisiz oturuma vermez, yerine tek satirlik bir
  "N/A: Must be an administrator..." metni dondurur.

.PARAMETER EditorRoot
  Unity Editor kurulum koku/kokleri. Verilmezse "%ProgramFiles%\Unity\Hub\Editor"
  ve Hub'in ikincil kurulum yolu kullanilir.

.PARAMETER ExtraPath
  Listeye eklenecek fazladan klasor(ler) - ornegin diger Unity projelerinin koku.

.PARAMETER NoProcess
  Yalniz yol dislamalarini yazar, islem dislamalarina hic dokunmaz.

.EXAMPLE
  scripts\defender-exclusions.cmd
  (cift tiklanabilir sarmalayici - yonetici olarak calistirilir)

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\defender-exclusions.ps1 -ExtraPath "D:\games\digerproje"

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\defender-exclusions.ps1 -List
#>
[CmdletBinding()]
param(
  [switch]$Remove,
  [switch]$List,
  [string[]]$EditorRoot = @(),
  [string[]]$ExtraPath = @(),
  [switch]$NoProcess
)

$script:Hata = 0

function Baslik([string]$m) {
  Write-Host ''
  Write-Host ('=== ' + $m + ' ' + ('=' * [Math]::Max(3, 66 - $m.Length)))
}

# ---------------------------------------------------------------------------
# 1) Defender var mi, tek basina mi calisiyor?
# ---------------------------------------------------------------------------
if (-not (Get-Command Get-MpPreference -ErrorAction SilentlyContinue)) {
  Write-Host '[HATA] Defender PowerShell modulu (ConfigDefender) yok.'
  Write-Host '       Makinede ucuncu parti bir antivirus varsa dislamalari onun'
  Write-Host '       arayuzunden tanimlamalisin - bu betik yalniz Defender bilir.'
  exit 1
}

$durum = $null
try { $durum = Get-MpComputerStatus -ErrorAction Stop } catch { }
if ($durum) {
  Write-Host ('Defender: kip=' + $durum.AMRunningMode + '  gercek-zamanli=' + $durum.RealTimeProtectionEnabled)
  if ($durum.AMRunningMode -and $durum.AMRunningMode -ne 'Normal') {
    Write-Host '  [bilgi] Defender pasif kipte - taramayi baska bir antivirus yapiyor.'
    Write-Host '          Bu dislamalar yazilir ama build suresine etkisi olmaz;'
    Write-Host '          asil dislamalari o urunun arayuzunden tanimla.'
  }
  if ($durum.IsTamperProtected) {
    Write-Host '  [bilgi] Tamper Protection acik. Yerel yonetici icin sorun degil;'
    Write-Host '          makine Intune/grup ilkesiyle yonetiliyorsa komut erisim'
    Write-Host '          reddi verebilir - o durumda IT ile konus.'
  }
}

# ---------------------------------------------------------------------------
# 2) -List: yalniz oku, hicbir sey yazma (yonetici gerekmez)
# ---------------------------------------------------------------------------
if ($List) {
  $tercih = $null
  try { $tercih = Get-MpPreference -ErrorAction Stop } catch {
    Write-Host ('[HATA] Dislamalar okunamadi: ' + $_.Exception.Message)
    exit 1
  }
  $yolListe = @($tercih.ExclusionPath)
  $islemListe = @($tercih.ExclusionProcess)
  # Yonetici olmayan oturumda Defender dizi yerine tek bir uyari metni dondurur
  # ("N/A: Must be an administrator to view exclusions") - bunu liste sanma.
  if ($yolListe.Count -eq 1 -and "$($yolListe[0])" -like 'N/A*') {
    Write-Host ''
    Write-Host '[HATA] Dislamalar yalniz YONETICI olarak okunabilir.'
    Write-Host '       scripts\defender-exclusions.cmd -List -> sag tik -> Yonetici olarak calistir'
    exit 1
  }
  Baslik 'Yol dislamalari'
  if ($yolListe.Count -gt 0) { $yolListe | Sort-Object | ForEach-Object { Write-Host ('  ' + $_) } }
  else { Write-Host '  (yok)' }
  Baslik 'Islem dislamalari'
  if ($islemListe.Count -gt 0) { $islemListe | Sort-Object | ForEach-Object { Write-Host ('  ' + $_) } }
  else { Write-Host '  (yok)' }
  Write-Host ''
  exit 0
}

# ---------------------------------------------------------------------------
# 3) Yonetici mi?
# ---------------------------------------------------------------------------
$kimlik = [Security.Principal.WindowsIdentity]::GetCurrent()
$yetkili = (New-Object Security.Principal.WindowsPrincipal($kimlik)).IsInRole(
  [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $yetkili) {
  Write-Host '[HATA] Bu betik YONETICI olarak calistirilmali.'
  Write-Host '       scripts\defender-exclusions.cmd -> sag tik -> Yonetici olarak calistir'
  exit 1
}

# ---------------------------------------------------------------------------
# 4) Yol listesi - betigin konumundan turetilir
# ---------------------------------------------------------------------------
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$docsSite = Join-Path (Split-Path $repo -Parent) 'vortexarena-docs-site'

$editorKokleri = @()
if ($EditorRoot.Count -gt 0) {
  $editorKokleri += $EditorRoot
} else {
  $editorKokleri += (Join-Path $env:ProgramFiles 'Unity\Hub\Editor')
  # Hub, editorleri baska bir diske kurabilir; yolu bu dosyada tutar.
  $ikincil = Join-Path $env:APPDATA 'UnityHub\secondaryInstallPath.json'
  if (Test-Path $ikincil) {
    $alt = Get-Content $ikincil -Raw -ErrorAction SilentlyContinue
    if ($alt) { $alt = $alt.Trim().Trim('"') }
    if ($alt -and (Test-Path $alt)) { $editorKokleri += $alt }
  }
}

# Her zaman eklenir (klasor sonradan olussa da dislama gecerlidir).
$zorunlu = @(
  $repo,                                          # Library, Temp, deploy, Server, launcher hepsi burada
  (Join-Path $env:LOCALAPPDATA 'Unity'),          # asset/shader cache, Unity CLI (bin), lisans
  (Join-Path $env:APPDATA 'Unity'),
  (Join-Path $env:LOCALAPPDATA 'UnityHub'),
  (Join-Path $env:APPDATA 'UnityHub'),
  (Join-Path $env:TEMP 'Unity'),                  # shader compiler / il2cpp ara dosyalari
  (Join-Path $env:USERPROFILE '.gradle'),         # Android (APK) build zinciri
  (Join-Path $env:USERPROFILE '.nuget'),          # Server/ + launcher/ (.NET 10)
  (Join-Path $env:USERPROFILE '.dotnet')
)

# Yalnizca diskte varsa eklenir - kurulu olmayan bir programin klasorunu
# dislamak gereksiz bir acikliktir.
$pf86 = ${env:ProgramFiles(x86)}
if (-not $pf86) { $pf86 = $env:ProgramFiles }
$istege = @(
  $docsSite,                                                    # Quartz + node_modules (~365 paket)
  (Join-Path $env:ProgramFiles 'Unity Hub'),
  (Join-Path $env:ProgramFiles 'dotnet'),
  (Join-Path $env:LOCALAPPDATA 'Android\Sdk'),
  (Join-Path $env:USERPROFILE '.android'),                      # adb anahtarlari
  (Join-Path $env:ProgramFiles 'nodejs'),
  (Join-Path $env:APPDATA 'npm'),
  (Join-Path $env:APPDATA 'npm-cache'),
  (Join-Path $env:LOCALAPPDATA 'npm-cache'),
  (Join-Path $env:USERPROFILE '.vscode'),
  (Join-Path $env:APPDATA 'Code'),
  (Join-Path $env:LOCALAPPDATA 'Programs\Microsoft VS Code'),
  (Join-Path $env:LOCALAPPDATA 'Microsoft\VisualStudio'),
  (Join-Path $env:ProgramFiles 'Microsoft Visual Studio'),
  (Join-Path $pf86 'Microsoft Visual Studio'),
  (Join-Path $env:LOCALAPPDATA 'JetBrains'),
  (Join-Path $env:APPDATA 'JetBrains')
)

$yollar = @()
$yollar += $zorunlu
$yollar += ($editorKokleri | Where-Object { Test-Path $_ })
$yollar += ($istege | Where-Object { $_ -and (Test-Path $_) })
$yollar += $ExtraPath

# Buyuk/kucuk harf duyarsiz tekillestirme (Windows yollari)
$gorulen = @{}
$yollar = $yollar | Where-Object { $_ } | ForEach-Object {
  $y = $_.TrimEnd('\')
  $k = $y.ToLowerInvariant()
  if (-not $gorulen.ContainsKey($k)) { $gorulen[$k] = $true; $y }
}

# Build zincirinin exe'leri. Islem dislamasi = "bu surecin actigi dosyalari
# tarama"; liste bilerek build araclariyla sinirli tutulur.
$islemler = @(
  'Unity.exe', 'UnityHub.exe', 'UnityShaderCompiler.exe', 'UnityHelper.exe',
  'UnityPackageManager.exe', 'UnityCrashHandler64.exe', 'bee_backend.exe',
  'il2cpp.exe', 'cl.exe', 'link.exe', 'mspdbsrv.exe',
  'MSBuild.exe', 'VBCSCompiler.exe', 'csc.exe', 'dotnet.exe',
  'devenv.exe', 'Code.exe', 'rider64.exe',
  'git.exe', 'node.exe', 'java.exe', 'javaw.exe', 'gradle.exe',
  'adb.exe', 'aapt2.exe'
)

# ---------------------------------------------------------------------------
# 5) Uygula
# ---------------------------------------------------------------------------
$tercih = Get-MpPreference
$mevcutYol = @($tercih.ExclusionPath)
$mevcutIslem = @($tercih.ExclusionProcess)

$fiil = 'ekleniyor'
if ($Remove) { $fiil = 'kaldiriliyor' }

Write-Host ''
Write-Host ('=== VortexArena : Defender dislamalari ' + $fiil + ' ===')
Write-Host ('  Repo  : ' + $repo)
foreach ($e in $editorKokleri) { Write-Host ('  Unity : ' + $e) }

Baslik ('Yollar (' + $yollar.Count + ')')
foreach ($y in $yollar) {
  $var = $mevcutYol -contains $y
  try {
    if ($Remove) {
      if (-not $var) { Write-Host ('  .  yok        ' + $y); continue }
      Remove-MpPreference -ExclusionPath $y -ErrorAction Stop
      Write-Host ('  -  kaldirildi ' + $y)
    } else {
      if ($var) { Write-Host ('  =  zaten var  ' + $y); continue }
      Add-MpPreference -ExclusionPath $y -ErrorAction Stop
      Write-Host ('  +  eklendi    ' + $y)
    }
  } catch {
    Write-Host ('  !  HATA       ' + $y)
    Write-Host ('     ' + $_.Exception.Message)
    $script:Hata++
  }
}

if ($NoProcess) {
  Baslik 'Islemler'
  Write-Host '  (-NoProcess verildi, atlandi)'
} else {
  Baslik ('Islemler (' + $islemler.Count + ')')
  foreach ($i in $islemler) {
    $var = $mevcutIslem -contains $i
    try {
      if ($Remove) {
        if (-not $var) { Write-Host ('  .  yok        ' + $i); continue }
        Remove-MpPreference -ExclusionProcess $i -ErrorAction Stop
        Write-Host ('  -  kaldirildi ' + $i)
      } else {
        if ($var) { Write-Host ('  =  zaten var  ' + $i); continue }
        Add-MpPreference -ExclusionProcess $i -ErrorAction Stop
        Write-Host ('  +  eklendi    ' + $i)
      }
    } catch {
      Write-Host ('  !  HATA       ' + $i)
      Write-Host ('     ' + $_.Exception.Message)
      $script:Hata++
    }
  }
}

# ---------------------------------------------------------------------------
# 6) Ozet
# ---------------------------------------------------------------------------
$son = Get-MpPreference
Baslik 'Ozet'
Write-Host ('  Makinede kayitli yol dislamasi   : ' + @($son.ExclusionPath).Count)
Write-Host ('  Makinede kayitli islem dislamasi : ' + @($son.ExclusionProcess).Count)

if ($script:Hata -gt 0) {
  Write-Host ''
  Write-Host ('=== BASARISIZ (' + $script:Hata + ' islem yazilamadi) ===')
  Write-Host '  "Erisim reddedildi" goruyorsan: PowerShell yonetici mi, ve makine'
  Write-Host '  kurumsal ilkeyle (Intune / grup ilkesi) yonetiliyor mu? Ikincisinde'
  Write-Host '  dislamalar merkezden tanimlanir, yerelden yazilamaz - IT ile konus.'
  exit 1
}

if (-not $Remove) {
  Write-Host ''
  Write-Host '  UYARI: dislanan klasorler artik taranmiyor. Oraya indirme yapma;'
  Write-Host '  asset store paketi / GitHub arsivi once baska bir yere inip kontrol edilir.'
  Write-Host ''
  Write-Host '  Geri almak icin ayni betik -Remove ile calistirilir.'
  Write-Host '  Listeyi gormek icin        : -List'
}
Write-Host ''
Write-Host '=== TAMAM ==='
exit 0
