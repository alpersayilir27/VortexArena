<#
.SYNOPSIS
  Lists the player APK versions currently published on the update server.

.DESCRIPTION
  deploy-player-apk.bat calls this right BEFORE it asks for a version number.
  The number goes into the APK name and the Android package id, so overwriting a
  published version - or skipping a number - used to be a silent mistake. With the
  list on screen the choice stops being guesswork.

  Single source of data: the GET /versions endpoint of updater_uploader_main.py on
  the server (published game_v<N>.apk files, newest first). The local deploy\player\
  folder is deliberately NOT read: an APK sitting there may never have been
  published, and headsets only ever see what is on the server.

.PARAMETER Url
  Full address of the /versions endpoint (e.g. http://server:8091/versions).

.PARAMETER TimeoutSec
  Connect/read timeout. Kept short so a dead server does not stall the start of a
  build for minutes.

.PARAMETER Top
  How many newest versions to print; the rest collapse into one summary line.

.NOTES
  This script NEVER fails the caller (exit 0 on every path): the listing is a
  convenience, a down server must not block a build.
  Output is ASCII - the console code page mangles Turkish characters.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$Url,
  [int]$TimeoutSec = 8,
  [int]$Top = 12
)

$ErrorActionPreference = 'Stop'

Write-Host ('  Sunucudaki yayinlanmis surumler ({0}):' -f $Url)

try {
  $response = Invoke-RestMethod -Uri $Url -Method Get -TimeoutSec $TimeoutSec
} catch {
  Write-Host '  [UYARI] Surum listesi alinamadi - liste olmadan devam ediliyor.'
  Write-Host ('          Sebep: ' + $_.Exception.Message)
  Write-Host '          Sunucuda updater_uploader_main.py calismiyor olabilir.'
  Write-Host '          Build bundan etkilenmez; girilen surum yine yayinlanmayi dener.'
  exit 0
}

$items = @()
if ($null -ne $response -and $null -ne $response.versions) {
  $items = @($response.versions)
}

if ($items.Count -eq 0) {
  Write-Host '    (sunucuda yayinlanmis surum yok)'
  exit 0
}

# The endpoint already sorts newest first; re-sort anyway so a hand-edited folder
# cannot push a stale entry to the top and make the "next version" hint wrong.
$items = @($items | Sort-Object -Property @{ Expression = { [int]$_.version } } -Descending)
$shown = @($items | Select-Object -First $Top)

foreach ($item in $shown) {
  $megabytes = 0.0
  if ($item.size) { $megabytes = [double]$item.size / 1MB }
  $stamp = ''
  if ($item.modified) { $stamp = [string]$item.modified }
  Write-Host ('    v{0,-8} {1,7:N1} MB   {2}' -f $item.version, $megabytes, $stamp)
}

if ($items.Count -gt $shown.Count) {
  Write-Host ('    ... ve {0} eski surum daha (toplam {1}).' -f ($items.Count - $shown.Count), $items.Count)
}

# Two different "latest" values on purpose, and NO "enter N+1" hint: a stray high
# number (test upload) makes highest+1 nonsense, while the newest upload is what a
# person actually continues from. Showing both lets the operator decide.
$highest = [int]$items[0].version
$newest = @($items | Sort-Object -Property modified -Descending)[0]
Write-Host ''
Write-Host ('  En yuksek numara   : v{0}' -f $highest)
Write-Host ('  En son yuklenen    : v{0}  ({1})' -f $newest.version, $newest.modified)
Write-Host '  Listedeki bir numarayi girmek yayindaki APK''nin uzerine yazar.'

exit 0
