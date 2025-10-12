param(
    [int]$Port = 5000,
    [string]$Path = '/',
    [int]$StartupTimeoutSeconds = 25,
    [int]$RetryIntervalMillis = 500,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

Write-Host "[smoke] Starting sample smoke test (port=$Port path=$Path)" -ForegroundColor Cyan

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
$solution = Join-Path $PSScriptRoot '..\RestApi.sln'

if (-not $NoBuild) {
    & $PSScriptRoot/build-sample.ps1 || exit 20
}

$env:ASPNETCORE_URLS = "http://localhost:$Port"

$runLog = New-Item -ItemType File -Path (Join-Path $PSScriptRoot 'smoke-run.log') -Force

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = 'dotnet'
$psi.Arguments = 'run --project ..\RestApi.csproj'
$psi.WorkingDirectory = (Join-Path $PSScriptRoot '..')
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false
$psi.Environment['ASPNETCORE_URLS'] = $env:ASPNETCORE_URLS

$proc = New-Object System.Diagnostics.Process
$proc.StartInfo = $psi
[void]$proc.Start()

Start-Sleep -Milliseconds 200

$deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
$ok = $false
$targetUrl = "http://localhost:$Port$Path"

while((Get-Date) -lt $deadline) {
    try {
        $resp = Invoke-WebRequest -Uri $targetUrl -UseBasicParsing -TimeoutSec 5
        if ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 300) {
            Write-Host "[smoke] SUCCESS $($resp.StatusCode) $targetUrl" -ForegroundColor Green
            $ok = $true
            break
        }
    } catch {
        # ignore until timeout
    }
    Start-Sleep -Milliseconds $RetryIntervalMillis
}

if (-not $ok) {
    Write-Host "[smoke] FAILED to reach $targetUrl within $StartupTimeoutSeconds s" -ForegroundColor Red
}

try {
    if (-not $proc.HasExited) {
        $proc.Kill()
        $proc.WaitForExit(5000) | Out-Null
    }
} catch {}

$stdout = $proc.StandardOutput.ReadToEnd()
$stderr = $proc.StandardError.ReadToEnd()
Add-Content -Path $runLog.FullName -Value "STDOUT:\n$stdout"
Add-Content -Path $runLog.FullName -Value "STDERR:\n$stderr"

if (-not $ok) { exit 30 }

Write-Host "[smoke] Completed successfully" -ForegroundColor Green
exit 0
