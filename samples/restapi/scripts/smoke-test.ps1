<#
.SYNOPSIS
    Unified minimal smoke test (migrated from smoke.ps1).
.DESCRIPTION
    Builds, starts API and exercises the three sample endpoints:
        GET /
        GET /api/users
        POST /api/users
    Intended for local quick check & CI.
#>

param(
    [int]$Port = 5080,
    [string]$Project = "samples/restapi/RestApi.csproj",
    [string]$Framework = "net8.0",
    [int]$StartupTimeoutSeconds = 20,
    [switch]$VerboseOutput,
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $env:SPOCR_NAMESPACE) { $env:SPOCR_NAMESPACE = 'RestApi' }

$BaseUrl = "http://localhost:$Port"

function Step($m){ Write-Host "[STEP] $m" -ForegroundColor Cyan }
function Ok($m){ Write-Host "[OK] $m" -ForegroundColor Green }
function Fail($m){ Write-Host "[FAIL] $m" -ForegroundColor Red; if ($global:proc -and -not $global:proc.HasExited){ try { $global:proc.Kill() } catch {} }; exit 1 }

if (-not $NoBuild) {
    Step "Build"
    try {
        if ($Framework){ dotnet build $Project -c Debug -f $Framework --nologo | Out-Null } else { dotnet build $Project -c Debug --nologo | Out-Null }
        Ok "Build succeeded"
    } catch { Fail "Build failed: $_" }
}

Step "Start API ($BaseUrl)"
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = 'dotnet'
if ($Framework) { $psi.Arguments = "run --project $Project --framework $Framework --urls=$BaseUrl" } else { $psi.Arguments = "run --project $Project --urls=$BaseUrl" }
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError  = $true
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$global:proc = [System.Diagnostics.Process]::Start($psi)

$stdout = New-Object System.Text.StringBuilder
$stderr = New-Object System.Text.StringBuilder
$global:proc.add_OutputDataReceived({ if ($_.Data){ $null = $stdout.AppendLine($_.Data); if ($VerboseOutput){ Write-Host "[APP] $($_.Data)" } } })
$global:proc.add_ErrorDataReceived({ if ($_.Data){ $null = $stderr.AppendLine($_.Data); Write-Host "[APP-ERR] $($_.Data)" -ForegroundColor DarkRed } })
$global:proc.BeginOutputReadLine(); $global:proc.BeginErrorReadLine()

$deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
$ready = $false
while (-not $ready -and (Get-Date) -lt $deadline){
    Start-Sleep -Milliseconds 400
    try {
        $resp = Invoke-WebRequest -Method GET "$BaseUrl/" -UseBasicParsing -TimeoutSec 3
        if ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 500){ $ready = $true }
    } catch {}
}
if (-not $ready){ Fail "Startup timeout (${StartupTimeoutSeconds}s)" } else { Ok "API ready" }

function Invoke-Json($method,$url,$body){
    try {
        if ($null -ne $body){
            $json = $body | ConvertTo-Json -Depth 5
            Invoke-WebRequest -Method $method -Uri $url -Body $json -ContentType 'application/json' -UseBasicParsing
        } else {
            Invoke-WebRequest -Method $method -Uri $url -UseBasicParsing
        }
    } catch {
        $resp = $_.Exception.Response
        if ($resp){ return $resp }
        throw
    }
}

Step "GET /"
$root = Invoke-Json GET "$BaseUrl/" $null
if ($root.StatusCode -ne 200){ Fail "/ Status=$($root.StatusCode)" } else { Ok "/ ok" }

Step "GET /api/users"
$users = Invoke-Json GET "$BaseUrl/api/users" $null
if ($users.StatusCode -ne 200){ Write-Host ($users.Content | Out-String) -ForegroundColor DarkGray; Fail "/api/users Status=$($users.StatusCode)" } else { Ok "/api/users ok (len=$([Math]::Min($users.Content.Length,200)))" }

Step "POST /api/users"
$payload = @{ displayName = 'SmokeUser'; email = 'smoke@test.local' }
$created = Invoke-Json POST "$BaseUrl/api/users" $payload
if ($created.StatusCode -ne 201){ Write-Host ($created.Content | Out-String) -ForegroundColor DarkGray; Fail "POST /api/users Status=$($created.StatusCode)" } else { Ok "POST /api/users ok" }

Step "Stop"
try { if ($global:proc -and -not $global:proc.HasExited){ $global:proc.Kill() } } catch {}
Ok "Smoke test succeeded"
# Reset any inherited non-zero LASTEXITCODE (PowerShell may retain from prior native calls)
$global:LASTEXITCODE = 0
Write-Host "[INFO] Exiting with code 0"
exit 0
