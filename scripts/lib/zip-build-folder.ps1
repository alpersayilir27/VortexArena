<#
.SYNOPSIS
  Packs a version build folder into <kind>_v<N>.zip for publishing.

.DESCRIPTION
  Called by the deploy scripts right after a successful build (admin build,
  server publish). The archive ROOT holds the build folder's CONTENTS - no
  wrapper directory: the launcher extracts the archive straight into
  <kind>_versions\<kind>_v<N>\ and expects the exe at the top.

  Entries are added one by one through ZipArchive instead of
  ZipFile::CreateFromDirectory, for two reasons: *_DoNotShip folders (Burst
  debug symbols, hundreds of MB, never shipped) must be skipped, and progress
  has to be printable while a multi-GB archive is written.

  The archive is written to a .tmp and renamed at the end, so an interrupted run
  cannot leave a half archive that looks publishable.

.PARAMETER SourceDir
  The build folder (deploy\<kind>\<kind>_v<N>).

.PARAMETER Destination
  Target archive path (deploy\<kind>\<kind>_v<N>.zip). Overwritten if it exists.

.NOTES
  Exit codes: 0 = done, 1 = failure (the caller treats it as a build failure).
  Zip64 is relied on: the archive can pass 2 GB. ZipArchive on a seekable
  stream switches to Zip64 by itself.
  Output is ASCII - the console code page mangles Turkish characters.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$SourceDir,
  [Parameter(Mandatory = $true)][string]$Destination
)

$ErrorActionPreference = 'Stop'

try {
  Add-Type -AssemblyName System.IO.Compression
  Add-Type -AssemblyName System.IO.Compression.FileSystem
} catch {
  Write-Host '[HATA] System.IO.Compression yuklenemedi - .NET Framework 4.5+ gerekir.'
  Write-Host ('        Sebep: ' + $_.Exception.Message)
  exit 1
}

if (-not (Test-Path -LiteralPath $SourceDir -PathType Container)) {
  Write-Host ('[HATA] Build klasoru yok: ' + $SourceDir)
  exit 1
}

$root = (Resolve-Path -LiteralPath $SourceDir).ProviderPath.TrimEnd('\')
$tmp = $Destination + '.tmp'

Write-Host ('  Zip hazirlaniyor: ' + $Destination)

# File list first: the count is needed for the progress line, and a listing error
# must surface before a single byte is written.
$entries = New-Object System.Collections.Generic.List[object]
$skipped = 0
try {
  foreach ($file in (Get-ChildItem -LiteralPath $root -Recurse -File -Force)) {
    $rel = $file.FullName.Substring($root.Length + 1)
    $parts = $rel -split '\\'
    $skip = $false
    for ($i = 0; $i -lt $parts.Count - 1; $i++) {
      if ($parts[$i] -like '*_DoNotShip') { $skip = $true; break }
    }
    if ($skip) { $skipped++; continue }
    $entries.Add([pscustomobject]@{
      Full  = $file.FullName
      Entry = ($rel -replace '\\', '/')
    })
  }
} catch {
  Write-Host '[HATA] Build klasoru listelenemedi.'
  Write-Host ('        Sebep: ' + $_.Exception.Message)
  exit 1
}

if ($entries.Count -eq 0) {
  Write-Host ('[HATA] Build klasoru bos: ' + $root)
  exit 1
}

if ($skipped -gt 0) {
  Write-Host ('    *_DoNotShip klasorleri atlandi (' + $skipped + ' dosya).')
}

Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue

$stream = $null
$zip = $null
$written = 0
try {
  $stream = [System.IO.File]::Open(
    $tmp, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
  $zip = New-Object System.IO.Compression.ZipArchive(
    $stream, [System.IO.Compression.ZipArchiveMode]::Create, $false)

  $step = [Math]::Max(1, [int]($entries.Count / 10))
  foreach ($e in $entries) {
    $null = [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
      $zip, $e.Full, $e.Entry, [System.IO.Compression.CompressionLevel]::Optimal)
    $written++
    if (($written % $step) -eq 0 -or $written -eq $entries.Count) {
      Write-Host ('    %{0} ({1}/{2} dosya)' -f [int](100.0 * $written / $entries.Count), $written, $entries.Count)
    }
  }
} catch {
  Write-Host '[HATA] Zip yazilamadi.'
  Write-Host ('        Sebep: ' + $_.Exception.Message)
  if ($zip) { try { $zip.Dispose() } catch { } }
  if ($stream) { try { $stream.Dispose() } catch { } }
  Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
  exit 1
}

# Dispose BEFORE the rename: the central directory is written on Dispose, so a
# failure here (e.g. disk full) means a corrupt archive - never publish it.
try {
  $zip.Dispose()
  $stream.Dispose()
} catch {
  Write-Host '[HATA] Zip kapatilamadi (merkez dizin yazilamadi).'
  Write-Host ('        Sebep: ' + $_.Exception.Message)
  try { $stream.Dispose() } catch { }
  Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
  exit 1
}

try {
  Remove-Item -LiteralPath $Destination -Force -ErrorAction SilentlyContinue
  [System.IO.File]::Move($tmp, $Destination)
} catch {
  Write-Host ('[HATA] Zip tasinamadi: ' + $Destination)
  Write-Host ('        Sebep: ' + $_.Exception.Message)
  Write-Host '        Dosya baska bir surec tarafindan tutuluyor olabilir.'
  Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
  exit 1
}

$megabytes = (Get-Item -LiteralPath $Destination).Length / 1MB
Write-Host ('    Zip hazir: {0:N1} MB, {1} dosya' -f $megabytes, $entries.Count)

exit 0
