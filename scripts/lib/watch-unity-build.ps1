<#
.SYNOPSIS
  Runs the Unity batch-mode build and prints live progress by tailing the log file
  (phase, percentage, stall warning).

.DESCRIPTION
  Problem: "Unity.exe -batchmode -logFile <file>" prints NOTHING to the console. You
  stare at an empty screen for 20 minutes with no way to tell stalled from working.
  This script starts Unity, reads the log in shared mode (while Unity writes it) and
  shows a single live status line:

    [04:12 / ~12:30] Scriptler derleniyor | %53 (1450/2714) | Csc Meta.XR.Editor.dll | log 2.4 MB | cpu +9.8 sn -

  On a phase change the line is frozen (kept as history) and a new one starts. If the
  log stops growing and no CPU is burned, a "stalled?" warning is printed.

  ASCII-ONLY OUTPUT: the console code page (cp857) mangles Turkish characters, so
  output text uses undotted/unaccented letters. Keep this rule for new text.

.PARAMETER ReplayLog
  Re-runs an existing log through the same parser (does not start Unity).
  For post-mortem: "which phase burned how many lines, were there errors".

.EXAMPLE
  powershell -NoProfile -File watch-unity-build.ps1 -Unity "C:\...\Unity.exe" `
    -Project "D:\games\vortexarena" -OutDir "D:\games\vortexarena\deploy\admin" `
    -Log "D:\games\vortexarena\deploy\admin-build.log"

.EXAMPLE
  powershell -NoProfile -File watch-unity-build.ps1 -ReplayLog deploy\admin-build.log
#>
[CmdletBinding()]
param(
  [string]$Unity,
  [string]$Project,
  [string]$OutDir,
  [string]$Log,
  [string]$Method = 'VortexArena.Core.Editor.PlayerBuildTool.BuildWindowsAdmin',
  [string]$UnityBuildTarget,
  # Player build only; PlayerBuildTool refuses to build without it.
  [string]$BuildVersion,
  # Optional venue name, forwarded as -tenant (empty = every tenant).
  [string]$Tenant,
  [int]$HeartbeatSeconds = 15,
  [int]$StallSeconds = 180,
  [string]$ReplayLog
)

# ---------------------------------------------------------------------------
# Phase rules. Rank = progress order, group = how far back we may fall:
#   group 0 (rank 1-2) startup - group 1 (rank 3-5) preparation - group 2 (rank 6+) build.
# Inside preparation phases swing freely (Unity really does oscillate between
# import <-> compile <-> domain reload), but once the build group is entered
# there is NO way back to preparation; otherwise a late "[Licensing::]" line
# would throw the status back to the start. "Bitiriyor" is the last stop.
# ---------------------------------------------------------------------------
$PhaseRules = @(
  @{ Rank = 1; Label = 'Lisans dogrulaniyor';                     Re = '^\[Licensing::' }
  @{ Rank = 2; Label = 'Paketler cozumleniyor';                   Re = '\[Package Manager\]|Registering packages|Packages were changed' }
  @{ Rank = 3; Label = 'Asset veritabani yenileniyor';            Re = 'AssetDatabase Initial Refresh|Asset Pipeline Refresh|Refreshing native plugins' }
  @{ Rank = 4; Label = 'Assetler ice aktariliyor';                Re = 'Start importing |ImportAndPostprocessOutOfDateAssets|Import Asset' }
  @{ Rank = 4; Label = 'Platform degistiriliyor';                 Re = 'Switching to |SwitchActiveBuildTarget|switching active build target|Reloading assemblies after switching' }
  @{ Rank = 5; Label = 'Scriptler derleniyor';                    Re = 'DisplayProgressbar: Compiling|ScriptCompilationBuildProgram|script compilation time' }
  @{ Rank = 5; Label = 'Domain reload';                           Re = 'Begin MonoManager ReloadAssembly|resetting the current domain|Reloading assemblies after finishing' }
  @{ Rank = 6; Label = 'Player build hazirligi';   Gate = $true; Re = '\[PlayerBuildTool\] (Hedef|Sahneler)' }
  @{ Rank = 7; Label = 'Player build';             Gate = $true; Re = 'DisplayProgressbar: Build|PlayerBuildProgram|Building Player|Start building Player' }
  @{ Rank = 7; Label = 'Shader varyantlari derleniyor';           Re = 'Compiling shader |Compiled shader |shader variants' }
  @{ Rank = 8; Label = 'IL2CPP / native derleme';  Gate = $true; Re = 'il2cpp\.exe|IL2CPP Conversion|Building native binary|UnityLinker' }
  # Android-only last phase: after IL2CPP, Gradle packages the APK.
  # DO NOT put a BARE "Gradle" in the pattern: it also appears in the Windows
  # build log (the perf-test package's metadata JSON has "AndroidBuildSystem":"Gradle",
  # plus OVRGradleGeneration.cs stack-trace paths) and since the rule is Gated and
  # falling back to a lower rank in the same group is forbidden, one false match
  # locks the label to "Gradle / APK" until the end of the build. Only markers that
  # appear in real Gradle OUTPUT are used.
  @{ Rank = 8; Label = 'Gradle / APK paketleniyor'; Gate = $true; Re = 'Building Gradle project|Gradle Daemon|> Task :|assembleRelease|assembleDebug|Packaging APK|aapt2|gradleOut' }
  @{ Rank = 9; Label = 'Bitiriyor (rapor + kapanis)'; Gate = $true; Re = '\[PlayerBuildTool\] Build |Total build time|Exiting batchmode' }
)

function Get-PhaseGroup([int]$rank) {
  if ($rank -le 2) { return 0 }
  if ($rank -le 5) { return 1 }
  return 2
}

# Lines printed to the screen at once.
$ErrorRules = @(
  'Multiple Unity instances cannot open the same project'
  'It looks like another Unity instance is running'
  'Aborting batchmode due to failure'
  'error CS[0-9]+'
  'Build Failed|BuildFailedException|Error building Player'
  'Unhandled Exception|UnityException'
)
$LockRule  = 'Multiple Unity instances|another Unity instance is running'
$InfoRule  = '\[PlayerBuildTool\]'
# The BODY of a multi-line Debug.Log message is not on the first line but on the
# ones after it (e.g. the "1 scene missing from disk" heading is one line, the
# scene PATH another). Without the follow-up lines the screen names the error but
# not its address. Followed for $InfoFollowMax lines; cut on a blank line or a stack trace.
$InfoFollowMax = 6
$StackRule = '^(UnityEngine|UnityEditor|VortexArena|System|Mono|Microsoft)\.|^\(Filename:|^\s*at\s'
$MaxErrors = 20

# Activity measurement: the Unity process we started + the build chain's children.
# The generic "Unity" name is DELIBERATELY absent - with another editor open its CPU
# would make a stalled build look alive.
$CpuNames = @('bee_backend', 'il2cpp', 'netcorerun', 'UnityShaderCompiler', 'cl', 'link', 'csc')

# ---------------------------------------------------------------------------
# State
# ---------------------------------------------------------------------------
$script:Phase        = 'Baslatiliyor'
$script:PhaseRank    = 0
$script:PhaseChanged = $false
$script:Detail       = ''
$script:BeeDone      = 0
$script:BeeTotal     = 0
$script:BeeAt        = -999.0
$script:Imports      = 0
$script:Shaders      = 0
$script:LastLine     = ''
$script:LineNo       = 0
$script:Errors       = New-Object System.Collections.Generic.List[string]
$script:InfoFollow   = 0
$script:LockSeen     = $false
$script:Replay       = $false

$script:Reader       = $null
$script:Stream       = $null
$script:Buf          = ''
$script:LogBytes     = 0

$script:LivePending  = $false
$script:LiveLen      = 0
$script:Interactive  = $false
$script:Width        = 100
$script:Sw           = $null

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
function Get-ElapsedSeconds {
  if ($script:Sw) { return $script:Sw.Elapsed.TotalSeconds }
  return 0.0
}

function Format-Clock([double]$seconds) {
  if ($seconds -lt 0) { $seconds = 0 }
  $t = [TimeSpan]::FromSeconds([Math]::Floor($seconds))
  if ($t.TotalHours -ge 1) { return ('{0}:{1:00}:{2:00}' -f [int]$t.TotalHours, $t.Minutes, $t.Seconds) }
  return ('{0:00}:{1:00}' -f $t.Minutes, $t.Seconds)
}

function Format-Size([double]$bytes) {
  if ($bytes -ge 1073741824) { return ('{0:N1} GB' -f ($bytes / 1073741824)) }
  if ($bytes -ge 1048576)    { return ('{0:N1} MB' -f ($bytes / 1048576)) }
  if ($bytes -ge 1024)       { return ('{0:N0} KB' -f ($bytes / 1024)) }
  return ('{0:N0} B' -f $bytes)
}

function Get-Leaf([string]$path) {
  if ([string]::IsNullOrWhiteSpace($path)) { return '' }
  $leaf = ($path.Trim() -split '[\\/]')[-1]
  if ($leaf.Length -gt 34) { $leaf = $leaf.Substring(0, 31) + '...' }
  return $leaf
}

function Get-CpuSeconds([System.Diagnostics.Process]$main) {
  $sum = 0.0
  if ($main) {
    try {
      $main.Refresh()
      if (-not $main.HasExited) { $sum += $main.TotalProcessorTime.TotalSeconds }
    } catch { }
  }
  foreach ($n in $CpuNames) {
    $procs = Get-Process -Name $n -ErrorAction SilentlyContinue
    foreach ($p in $procs) {
      try { $sum += $p.TotalProcessorTime.TotalSeconds } catch { }
    }
  }
  return $sum
}

function Close-Live {
  if ($script:LivePending) {
    [Console]::Write("`r`n")
    $script:LivePending = $false
    $script:LiveLen = 0
  }
}

function Write-Note([string]$text) {
  Close-Live
  Write-Host $text
}

function Write-Live([string]$text) {
  if (-not $script:Interactive) { Write-Host $text; return }
  if ($text.Length -gt $script:Width) { $text = $text.Substring(0, $script:Width) }
  $pad = ''
  if ($script:LiveLen -gt $text.Length) { $pad = ' ' * ($script:LiveLen - $text.Length) }
  [Console]::Write("`r" + $text + $pad)
  $script:LiveLen = $text.Length
  $script:LivePending = $true
}

# ---------------------------------------------------------------------------
# Log parsing
# ---------------------------------------------------------------------------
function Update-State([string]$line) {
  $script:LineNo++
  if ([string]::IsNullOrWhiteSpace($line)) { return }
  if ($line.Length -lt 400) { $script:LastLine = $line }

  # Phase: the HIGHEST ranked match on the line wins; the group rule limits
  # falling back (see the $PhaseRules header).
  $bestRank = -1
  $bestLabel = $null
  $curGroupNow = Get-PhaseGroup $script:PhaseRank
  foreach ($rule in $PhaseRules) {
    # ENTERING the build group happens only through "Gate" rules. Shader compilation
    # also happens editor-side; left ungated it would jump into the build group before
    # preparation is over and the import phase would never show again.
    if ((Get-PhaseGroup $rule.Rank) -eq 2 -and $curGroupNow -lt 2 -and -not $rule.Gate) { continue }
    if ($line -match $rule.Re) {
      if ($rule.Rank -gt $bestRank) { $bestRank = $rule.Rank; $bestLabel = $rule.Label }
    }
  }
  if ($null -ne $bestLabel -and $bestLabel -ne $script:Phase) {
    $newGroup = Get-PhaseGroup $bestRank
    $curGroup = Get-PhaseGroup $script:PhaseRank
    $allow = $false
    if ($newGroup -gt $curGroup) {
      $allow = $true
    } elseif ($newGroup -eq $curGroup) {
      # The preparation group swings freely; in the build group there is NO way back.
      # Reason: IL2CPP's Cpp lines carry the "WinPlayerBuildProgram" path, so left free
      # the phase flickers between IL2CPP <-> Player build.
      if ($newGroup -eq 2) { $allow = ($bestRank -ge $script:PhaseRank) } else { $allow = $true }
    }
    if ($allow) {
      $script:Phase = $bestLabel
      $script:PhaseRank = $bestRank
      $script:PhaseChanged = $true
    }
  }

  # Bee/Tundra progress line: "[ 152/2714  0s] Csc Library/.../Foo.dll"
  if ($line -match '^\[\s*(\d+)/(\d+)\s') {
    $script:BeeDone = [int]$Matches[1]
    $script:BeeTotal = [int]$Matches[2]
    $script:BeeAt = Get-ElapsedSeconds
    if ($line -match '^\[[^\]]+\]\s+(\S+)\s*(.*)$') {
      $script:Detail = ($Matches[1] + ' ' + (Get-Leaf $Matches[2])).Trim()
    }
    return
  }

  # Running sub-tool: "Starting: C:\...\bee_backend.exe --ipc ..."
  if ($line -match '^Starting:\s+(.*?\.exe)') {
    $script:Detail = Get-Leaf $Matches[1]
    return
  }

  # Import lines may carry a worker prefix: "[Worker3] Start importing ..."
  if ($line -match 'Start importing ')  { $script:Imports++ }
  if ($line -match 'Compiling shader ') { $script:Shaders++ }

  # "Asset Pipeline Refresh (id=..): Total: 54.481 seconds - Initiated by .."
  if ($line -match 'Asset Pipeline Refresh .*Total:\s*([\d.]+) seconds') {
    $script:Detail = ('refresh {0:N0} sn' -f [double]$Matches[1])
    return
  }

  # Unity's own progress bar title is the best coarse hint.
  if ($line -match '^DisplayProgressbar:\s*(.+)$') { $script:Detail = $Matches[1].Trim() }

  if ($line -match $LockRule) { $script:LockSeen = $true }

  foreach ($re in $ErrorRules) {
    if ($line -match $re) {
      $text = $line.Trim()
      if ($text.Length -gt 200) { $text = $text.Substring(0, 200) + '...' }
      $script:Errors.Add(('satir {0}: {1}' -f $script:LineNo, $text))
      if (-not $script:Replay -and $script:Errors.Count -le $MaxErrors) {
        Write-Note ('  [HATA] ' + $text)
      }
      break
    }
  }

  if (-not $script:Replay -and $line -match $InfoRule) {
    $text = $line.Trim()
    if ($text.Length -gt 200) { $text = $text.Substring(0, 200) + '...' }
    Write-Note ('  > ' + $text)
    $script:InfoFollow = $InfoFollowMax
    return
  }

  # Continuation of the previous [PlayerBuildTool] line? (see the $InfoFollowMax note above)
  if (-not $script:Replay -and $script:InfoFollow -gt 0) {
    if ($line.Trim() -eq '' -or $line -match $StackRule) {
      $script:InfoFollow = 0
    } else {
      $text = $line.TrimEnd()
      if ($text.Length -gt 200) { $text = $text.Substring(0, 200) + '...' }
      Write-Note ('  >   ' + $text.TrimStart())
      $script:InfoFollow--
    }
  }
}

# Reads the log in shared mode (Unity keeps the file open); returns only COMPLETE
# lines, the trailing half line waits in the buffer.
function Read-NewLines {
  if ($null -eq $script:Reader) {
    if (-not (Test-Path -LiteralPath $Log)) { return @() }
    try {
      $share = [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete
      $script:Stream = New-Object System.IO.FileStream(
        $Log, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, $share)
      $script:Reader = New-Object System.IO.StreamReader($script:Stream, [System.Text.Encoding]::UTF8)
    } catch {
      return @()
    }
  }

  # If Unity recreates the log (FileMode.Create) the file shrinks; our cursor stays
  # past EOF and we would never read anything again. On a shrink, rewind to the start -
  # otherwise it looks like "nothing is progressing".
  try {
    if ($script:Stream.Length -lt $script:Stream.Position) {
      [void]$script:Stream.Seek(0, [System.IO.SeekOrigin]::Begin)
      $script:Reader.DiscardBufferedData()
      $script:Buf = ''
    }
  } catch { }

  $chunk = $script:Reader.ReadToEnd()
  if ([string]::IsNullOrEmpty($chunk)) { return @() }
  $script:LogBytes += [System.Text.Encoding]::UTF8.GetByteCount($chunk)

  $script:Buf += $chunk
  $parts = $script:Buf -split "`n"
  $script:Buf = $parts[$parts.Count - 1]
  if ($parts.Count -le 1) { return @() }
  return $parts[0..($parts.Count - 2)] | ForEach-Object { $_.TrimEnd("`r") }
}

# ---------------------------------------------------------------------------
# Replay mode: run an existing log through the same parser, print a phase map.
# ---------------------------------------------------------------------------
if ($ReplayLog) {
  if (-not (Test-Path -LiteralPath $ReplayLog)) {
    Write-Host ('[HATA] Log bulunamadi: ' + $ReplayLog)
    exit 1
  }
  $script:Replay = $true
  Write-Host ('=== Log analizi: ' + $ReplayLog + ' ===')
  # To suppress oscillation (import <-> compile): never repeat the same label and
  # keep at least 50 lines between two entries.
  $prevPhase = ''
  $prevAt = -999
  Get-Content -LiteralPath $ReplayLog | ForEach-Object {
    Update-State $_
    if ($script:PhaseChanged) {
      $script:PhaseChanged = $false
      if ($script:Phase -ne $prevPhase -and ($script:LineNo - $prevAt) -ge 50) {
        Write-Host ('  satir {0,7} : {1}' -f $script:LineNo, $script:Phase)
        $prevPhase = $script:Phase
        $prevAt = $script:LineNo
      }
    }
  }
  Write-Host ''
  Write-Host ('  Toplam satir      : {0}' -f $script:LineNo)
  Write-Host ('  Son asama         : {0}' -f $script:Phase)
  Write-Host ('  Bee ilerlemesi    : {0}/{1}' -f $script:BeeDone, $script:BeeTotal)
  Write-Host ('  Ice aktarim/shader: {0} / {1}' -f $script:Imports, $script:Shaders)
  Write-Host ('  Hata satiri       : {0}' -f $script:Errors.Count)
  foreach ($e in $script:Errors) { Write-Host ('    - ' + $e) }
  $tail = $script:LastLine
  if ($tail.Length -gt 140) { $tail = $tail.Substring(0, 140) + '...' }
  Write-Host ('  Son satir         : {0}' -f $tail)
  exit 0
}

# ---------------------------------------------------------------------------
# Run mode
# ---------------------------------------------------------------------------
foreach ($pair in @(@('Unity', $Unity), @('Project', $Project), @('OutDir', $OutDir), @('Log', $Log))) {
  if ([string]::IsNullOrWhiteSpace($pair[1])) {
    Write-Host ('[HATA] Eksik parametre: -' + $pair[0])
    exit 2
  }
}
if (-not (Test-Path -LiteralPath $Unity)) {
  Write-Host ('[HATA] Unity bulunamadi: ' + $Unity)
  exit 2
}

try { $script:Interactive = -not [Console]::IsOutputRedirected } catch { $script:Interactive = $false }
try { if ([Console]::WindowWidth -gt 40) { $script:Width = [Console]::WindowWidth - 1 } } catch { }

# Previous successful build duration = reference. Shown in the status line as
# "/ ~mm:ss" so the user can tell "how much is left".
# Per-tenant reference file: a single-venue build is much shorter than the full one,
# a shared file would make the estimate lie in both directions.
if ([string]::IsNullOrWhiteSpace($Tenant)) {
  $refFile = [System.IO.Path]::ChangeExtension($Log, 'last')
} else {
  $refFile = [System.IO.Path]::ChangeExtension($Log, ($Tenant + '.last'))
}
$refSeconds = 0.0
if (Test-Path -LiteralPath $refFile) {
  try {
    $raw = (Get-Content -LiteralPath $refFile -TotalCount 1).Trim()
    $parsed = 0.0
    if ([double]::TryParse($raw, [ref]$parsed)) { $refSeconds = $parsed }
  } catch { }
}

$argList = @(
  '-batchmode'
  '-quit'
  '-projectPath'; ('"{0}"' -f $Project)
)
# Switching platform from INSIDE -executeMethod does not work: SwitchActiveBuildTarget
# triggers a domain reload and the running method is cut off. We START Unity on the
# right platform instead.
if (-not [string]::IsNullOrWhiteSpace($UnityBuildTarget)) {
  $argList += @('-buildTarget'; $UnityBuildTarget)
}
$argList += @(
  '-executeMethod'; $Method
  '-buildOutput'; ('"{0}"' -f $OutDir)
)
if (-not [string]::IsNullOrWhiteSpace($BuildVersion)) {
  $argList += @('-buildVersion'; $BuildVersion)
}
if (-not [string]::IsNullOrWhiteSpace($Tenant)) {
  $argList += @('-tenant'; $Tenant)
}
$argList += @(
  '-logFile'; ('"{0}"' -f $Log)
)

# Stale log = wrong diagnosis: if the old file is still there we read it and think
# "done". The .bat already deletes it; the watcher repeats it because it can be run alone.
Remove-Item -LiteralPath $Log -Force -ErrorAction SilentlyContinue

$script:Sw = [System.Diagnostics.Stopwatch]::StartNew()
$proc = Start-Process -FilePath $Unity -ArgumentList $argList -PassThru -NoNewWindow
# Touching Handle once is mandatory: otherwise PowerShell releases the handle and
# $proc.ExitCode comes back empty ($null) after exit -> every build looks "successful".
$null = $proc.Handle

Write-Host ('  Unity PID : {0}' -f $proc.Id)
if ($refSeconds -gt 1) {
  Write-Host ('  Referans  : onceki basarili build {0}' -f (Format-Clock $refSeconds))
}
Write-Host '  Durum satiri her saniye guncellenir; asama degisince sabitlenir.'
Write-Host ''

$spin = @('-', '\', '|', '/')
$spinIx = 0
$lastGrowthAt = 0.0
$lastRenderAt = -99.0
$lastHistoryAt = -99.0
$lastStallWarnAt = 0.0
$cpuSampleAt = 0.0
$cpuLast = Get-CpuSeconds $proc
$cpuDelta = 0.0
$drained = $false
$exitCode = 0

try {
  while ($true) {
    $lines = @(Read-NewLines)
    if ($lines.Count -gt 0) {
      foreach ($l in $lines) { Update-State $l }
      $lastGrowthAt = Get-ElapsedSeconds
    }

    $now = Get-ElapsedSeconds

    # CPU sample: the only honest answer to "is it working" while the log is silent.
    if (($now - $cpuSampleAt) -ge 5.0) {
      $cpuNow = Get-CpuSeconds $proc
      $cpuDelta = $cpuNow - $cpuLast
      $cpuLast = $cpuNow
      $cpuSampleAt = $now
      if ($cpuDelta -gt 2.0) { $lastGrowthAt = $now }
    }

    $exited = $proc.HasExited
    if ($exited -and -not $drained) {
      # Unity may still write its last blocks while shutting down: read one more round.
      Start-Sleep -Milliseconds 400
      $tail = @(Read-NewLines)
      foreach ($l in $tail) { Update-State $l }
      $drained = $true
    }

    $renderEvery = 1.0
    if (-not $script:Interactive) { $renderEvery = [double]$HeartbeatSeconds }
    if ($script:PhaseChanged -or ($now - $lastRenderAt) -ge $renderEvery -or $exited) {
      # On a phase change, freeze the live line (keep it as history). Since the
      # import <-> compile oscillation can produce dozens of lines per minute we
      # freeze at most once every 10 seconds; the live line still updates instantly.
      if ($script:PhaseChanged) {
        $script:PhaseChanged = $false
        if (($now - $lastHistoryAt) -ge 10.0) { Close-Live; $lastHistoryAt = $now }
      }
      $lastRenderAt = $now

      $head = '  [' + (Format-Clock $now)
      if ($refSeconds -gt 1) { $head += ' / ~' + (Format-Clock $refSeconds) }
      $head += ']'

      $parts = New-Object System.Collections.Generic.List[string]
      $parts.Add($script:Phase)

      # The percentage comes only from a FRESH Bee/Tundra counter; the old DAG's
      # "2714/2714" lies in the next phase, so it goes stale after 30 s.
      if ($script:BeeTotal -gt 0 -and ($now - $script:BeeAt) -lt 30.0) {
        $pct = [int](100.0 * $script:BeeDone / $script:BeeTotal)
        $parts.Add(('%{0} ({1}/{2})' -f $pct, $script:BeeDone, $script:BeeTotal))
      } elseif ($script:Shaders -gt 0 -and $script:PhaseRank -ge 7) {
        $parts.Add(('{0} shader' -f $script:Shaders))
      } elseif ($script:Imports -gt 0 -and $script:PhaseRank -le 5) {
        $parts.Add(('{0} asset' -f $script:Imports))
      }

      if ($script:Detail) { $parts.Add($script:Detail) }
      $parts.Add('log ' + (Format-Size $script:LogBytes))

      $idle = $now - $lastGrowthAt
      if ($idle -gt 20.0) {
        $parts.Add(('hareket yok {0} sn' -f [int]$idle))
      } else {
        $spinIx = ($spinIx + 1) % 4
        $parts.Add(('cpu +{0:N0} sn {1}' -f $cpuDelta, $spin[$spinIx]))
      }

      Write-Live ($head + ' ' + [string]::Join(' | ', $parts))
    }

    # Stall warning: neither the log grew nor CPU was burned.
    $idle = $now - $lastGrowthAt
    if ($idle -ge $StallSeconds -and ($now - $lastStallWarnAt) -ge $StallSeconds) {
      $lastStallWarnAt = $now
      Write-Note ''
      Write-Note ('  [!] {0} sn dir ne log buyudu ne CPU harcandi (PID {1} hala ayakta).' -f [int]$idle, $proc.Id)
      Write-Note ('      Son satir: ' + $script:LastLine)
      Write-Note '      Muhtemel sebep: editor/arka plan Unity.exe proje kilidini tutuyor.'
      Write-Note '      Ctrl+C ile iptal edip Unity.exe sureclerini kapattiktan sonra tekrar deneyin.'
    }

    if ($exited -and $drained) { break }
    Start-Sleep -Milliseconds 500
  }

  try { $proc.WaitForExit() } catch { }
  $exitCode = 1
  try { if ($null -ne $proc.ExitCode) { $exitCode = [int]$proc.ExitCode } } catch { }
} finally {
  # Ctrl+C: do not leave Unity behind when the watcher dies (the project lock stays with it).
  if ($proc -and -not $proc.HasExited) {
    Close-Live
    Write-Host '  Iptal edildi - Unity sureci kapatiliyor...'
    try { $proc.Kill() } catch { }
    $exitCode = 1
  }
  if ($script:Reader) { try { $script:Reader.Dispose() } catch { } }
  if ($script:Stream) { try { $script:Stream.Dispose() } catch { } }
  Close-Live
}

$total = Get-ElapsedSeconds
if ($exitCode -eq 0) {
  try { Set-Content -LiteralPath $refFile -Value ([int]$total) -Encoding ascii } catch { }
}

Write-Host ''
Write-Host ('  Sure: {0}  |  Unity cikis kodu: {1}  |  log satiri: {2}' -f (Format-Clock $total), $exitCode, $script:LineNo)
if ($script:LockSeen) {
  Write-Host '  [!] Log''da PROJE KILIDI mesaji var: baska bir Unity.exe ayni projeyi acik tutuyor.'
}
if ($script:Errors.Count -gt $MaxErrors) {
  Write-Host ('  [!] {0} hata satiri bulundu (ilk {1} tanesi yukarida). Tamami log''da.' -f $script:Errors.Count, $MaxErrors)
}

exit $exitCode
