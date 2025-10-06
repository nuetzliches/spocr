<#!
.SYNOPSIS
  Kills lingering testhost / SpocR processes to avoid file lock build errors.
.DESCRIPTION
  Run manually or in CI before build/test if previous runs crashed or were aborted.
#>
$patterns = @('testhost', 'SpocR')
Write-Host "[kill-testhosts] Scanning processes..."
$procs = Get-Process | Where-Object { $patterns -contains $_.ProcessName } | Sort-Object ProcessName
if (-not $procs) {
  Write-Host "[kill-testhosts] None found."; exit 0
}
foreach ($p in $procs) {
  try {
    Write-Host "[kill-testhosts] Stopping $($p.ProcessName) ($($p.Id))"
    Stop-Process -Id $p.Id -Force -ErrorAction Stop
  }
  catch {
    Write-Warning "[kill-testhosts] Failed to stop $($p.Id): $_"
  }
}
Write-Host "[kill-testhosts] Done."