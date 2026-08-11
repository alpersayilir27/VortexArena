<#
.SYNOPSIS
  Basarisiz bir Unity batch-mode build'inin log'undan SEBEBI cikarir ve basar.

.DESCRIPTION
  deploy-admin-game.bat / deploy-player-apk.bat basarisizlik dalinda bunu cagirir.

  Neden ayri bir betik: eski hâlde iki .bat da log'un son 30 satirini basip ARDINDAN
  kosulsuz olarak "proje kilidi geciyorsa arka planda Unity.exe yasiyor" ipucunu
  yaziyordu. Unity'nin son 30 satiri her zaman kapanis gurultusudur (bellek sizinti
  JSON'u, licensing, physics cleanup), yani gercek sebep hicbir zaman gorunmuyordu;
  kilit ipucu ise hicbir Unity acik degilken bile basildigi icin her hatayi yanlis
  teshise goturuyordu. Burada iki sey de log'a BAKILARAK karara baglanir:
  kilit ipucu yalnizca log'da gercekten kilit izi varsa basilir.

.PARAMETER Log
  Unity'nin -logFile ile yazdigi log dosyasi.

.PARAMETER Tail
  Hicbir sebep satiri eslesmezse basilacak son satir sayisi (varsayilan 30).

.NOTES
  Cikti ASCII'dir (konsol kod sayfasi Turkce karakterleri bozuyor) - log'dan
  aynen alinan satirlar haric, onlar Unity ne yazdiysa odur.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$Log,
  [int]$Tail = 30
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Log)) {
  Write-Host ('  Log bulunamadi: ' + $Log)
  Write-Host '  Unity hic baslayamamis olabilir (yol/lisans/modul).'
  exit 0
}

try {
  $lines = [System.IO.File]::ReadAllLines($Log, [System.Text.Encoding]::UTF8)
} catch {
  Write-Host ('  Log okunamadi: ' + $_.Exception.Message)
  exit 0
}

# --- Kilit izi -------------------------------------------------------------
# watch-unity-build.ps1 icindeki $LockRule ile AYNI desen: iki yerde farkli
# davranmasi, izleyici "kilit yok" derken .bat "kilit olabilir" demesi olurdu.
$LockRule = 'Multiple Unity instances|another Unity instance is running'

# --- Sebep satirlari -------------------------------------------------------
# Sira onemli degil; hepsi taranir, log sirasinda basilir.
#
# ⚠️ Desenler DAR tutulur ve genisletilirken saglicak bir log'a karsi denenir:
# PowerShell'de -match varsayilan olarak buyuk/kucuk harf duyarsizdir, yani
# "EXECUTEMETHOD" komut satirindaki "-executeMethod" argumanini, "Licensing::.*Failed"
# ise saglikli bir Personal lisansin her acilista bastigi "has failed validation;
# ignoring" / "failed to update" satirlarini yakalar. Yanlis eslesen her satir
# gercek sebebi ekranda gurultuye gomer - bu betigin var olma sebebinin tam tersi.
$CauseRules = @(
  '\[PlayerBuildTool\]'
  'error CS[0-9]+'
  'Aborting batchmode due to failure'
  'Build Failed|BuildFailedException|Error building Player'
  'Unhandled Exception|UnityException'
  'executeMethod (method|class) .* could not be found'
  'No valid Unity Editor license|Unable to acquire license'
)

# Bir sebep satirindan SONRA gelen devam satirlari (Debug.LogError cok satirli
# yazildiginda mesajin govdesi burada durur - "hangi sahne eksik" tam olarak bu
# satirlarda). Yigin izi baslayinca kesilir: onlar sebep degil gurultu.
$StackRule = '^(UnityEngine|UnityEditor|VortexArena|System|Mono|Microsoft)\.|^\(Filename:|^\s*at\s'

$causes = New-Object System.Collections.Generic.List[string]
$lockSeen = $false
$follow = 0

for ($i = 0; $i -lt $lines.Length; $i++) {
  $line = $lines[$i]
  if ($line -match $LockRule) { $lockSeen = $true }

  $isCause = $false
  foreach ($re in $CauseRules) {
    if ($line -match $re) { $isCause = $true; break }
  }

  if ($isCause) {
    $causes.Add($line.TrimEnd())
    $follow = 6
    continue
  }

  if ($follow -gt 0) {
    $t = $line.Trim()
    if ($t -eq '' -or $line -match $StackRule) {
      $follow = 0
    } else {
      $causes.Add($line.TrimEnd())
      $follow--
    }
  }
}

# Ayni satir birden cok kez gecebilir (Unity hatayi hem konsola hem rapora yazar).
$causes = @($causes | Select-Object -Unique)

Write-Host ''
if ($causes.Count -gt 0) {
  Write-Host '  Sebep (log''dan):'
  $shown = $causes
  $more = 0
  if ($causes.Count -gt 40) {
    $shown = $causes[0..39]
    $more = $causes.Count - 40
  }
  foreach ($c in $shown) {
    $t = $c
    if ($t.Length -gt 300) { $t = $t.Substring(0, 300) + '...' }
    Write-Host ('    ' + $t)
  }
  if ($more -gt 0) {
    Write-Host ('    ... ve {0} satir daha - tamami log''da.' -f $more)
  }
} else {
  Write-Host ('  Tanidik bir hata satiri bulunamadi. Log''un son {0} satiri:' -f $Tail)
  $from = [Math]::Max(0, $lines.Length - $Tail)
  for ($i = $from; $i -lt $lines.Length; $i++) { Write-Host ('    ' + $lines[$i]) }
}

Write-Host ''
if ($lockSeen) {
  Write-Host '  [!] PROJE KILIDI: log''da baska bir Unity.exe''nin ayni projeyi acik'
  Write-Host '      tuttugu yaziyor. Editoru (ve arka planda kalmis Unity.exe''leri)'
  Write-Host '      kapatip tekrar deneyin.'
} else {
  Write-Host '  Not: log''da proje kilidi izi YOK - sebep yukaridaki satirlarda.'
  Write-Host '       Arka planda Unity.exe aramaya gerek yok.'
}
Write-Host ('  Tam log: ' + $Log)

exit 0
