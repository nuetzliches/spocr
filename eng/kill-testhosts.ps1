<#!
.SYNOPSIS
  Kills lingering test host / sample app processes to avoid file lock build errors.
.DESCRIPTION
  Targets common ephemeral processes spawned by test runs (testhost, VSTest.*) and
  optional sample RestApi instances (dotnet RestApi.dll) that can lock build outputs.
  Provides dry-run, filtering and graceful termination attempts before force killing.
  Invoke in CI before Restore/Build to reduce MSB3026/3027 copy failures.
.EXAMPLE
  pwsh eng/kill-testhosts.ps1 -Verbose
.EXAMPLE
  pwsh eng/kill-testhosts.ps1 -DryRun -IncludeSamples
#>
[CmdletBinding(SupportsShouldProcess)]
param(
  [string[]]$Patterns = @('testhost','SpocR','RestApi'),
  [switch]$IncludeSamples,
  [switch]$DryRun,
  [int]$GraceSeconds = 2
)

$ErrorActionPreference = 'Stop'

Write-Host "[kill-testhosts] Scanning processes..."

function Get-CandidateProcesses {
  # Use CIM to access CommandLine for more precise filtering
  $all = Get-CimInstance Win32_Process | Where-Object { $_.Name -ne $null }
  $candidates = @()
  foreach ($p in $all) {
    $name = $p.Name
    $cmd  = $p.CommandLine
    $matchName = $Patterns -contains $name
    $matchSample = $false
    if ($IncludeSamples) {
      if ($name -eq 'dotnet' -and $cmd -match 'RestApi\\.dll') { $matchSample = $true }
      if ($name -like 'RestApi*') { $matchSample = $true }
    }
    if ($matchName -or $matchSample) {
      # Exclude current process / script host
      if ($p.ProcessId -ne $PID) { $candidates += $p }
    }
  }
  return $candidates | Sort-Object Name, ProcessId
}

$procs = Get-CandidateProcesses
if (-not $procs -or $procs.Count -eq 0) {
  Write-Host "[kill-testhosts] None found."; exit 0
}

Write-Host ("[kill-testhosts] Found {0} candidate process(es)." -f $procs.Count)
$procs | ForEach-Object {
  Write-Host ("  - {0} (Id={1}) CmdLine='{2}'" -f $_.Name, $_.ProcessId, ($_.CommandLine -replace '\s+',' '))
}

if ($DryRun) { Write-Host "[kill-testhosts] DryRun specified – no processes will be terminated."; exit 0 }

foreach ($p in $procs) {
  $id = $p.ProcessId
  $name = $p.Name
  $desc = "{0} (Id={1})" -f $name, $id
  if ($PSCmdlet.ShouldProcess($desc, 'Terminate')) {
    try {
      Write-Host "[kill-testhosts] Stopping $desc (graceful)"
      Stop-Process -Id $id -ErrorAction SilentlyContinue
      Start-Sleep -Seconds $GraceSeconds
      if (Get-Process -Id $id -ErrorAction SilentlyContinue) {
        Write-Host "[kill-testhosts] Forcing $desc"
        Stop-Process -Id $id -Force -ErrorAction Stop
      }
    }
    catch {
      Write-Warning "[kill-testhosts] Failed to stop $desc: $_"
    }
  }
}

# Final report
$remaining = @()
foreach ($p in $procs) { if (Get-Process -Id $p.ProcessId -ErrorAction SilentlyContinue) { $remaining += $p } }
if ($remaining.Count -gt 0) {
  Write-Warning ("[kill-testhosts] {0} process(es) resisted termination." -f $remaining.Count)
  exit 1
}
Write-Host "[kill-testhosts] Done."
exit 0