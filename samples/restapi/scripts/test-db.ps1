<#
.SYNOPSIS
  Lightweight DB connectivity smoke probe for the sample.
.DESCRIPTION
  Attempts a single connection using either:
    1. $env:SPOCR_SAMPLE_RESTAPI_DB (preferred)
    2. Connection string from appsettings.Development.json (Fallback if parseable)
  Executes: SELECT 1; (or SELECT TOP 1 name FROM sys.databases) as a secondary check.
  Returns exit code:
    0 success
    2 connection failure
    3 query failure
    4 config missing
    5 client assembly/type unavailable
#>
param(
  [int]$TimeoutSeconds = 15,
  [switch]$Verbose
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Info($m){ Write-Host "[db-test] $m" -ForegroundColor Cyan }
function Fail($m,$code){ Write-Host "[FAIL] $m" -ForegroundColor Red; exit $code }
function Ok($m){ Write-Host "[OK] $m" -ForegroundColor Green }

$connString = $env:SPOCR_SAMPLE_RESTAPI_DB
if (-not $connString) {
  $cfgRoot = Join-Path $PSScriptRoot '..'
  $cfgPath = Join-Path $cfgRoot 'appsettings.Development.json'
  if (Test-Path $cfgPath) {
    try {
      $json = Get-Content -Raw -Path $cfgPath | ConvertFrom-Json
      $cs = $json.ConnectionStrings.DefaultConnection
      if ($cs) { $connString = $cs }
    } catch {
      if ($Verbose) { Write-Host "[db-test] Could not parse appsettings.Development.json: $_" -ForegroundColor DarkYellow }
    }
  }
}

if (-not $connString) { Fail "No connection string (env SPOCR_SAMPLE_RESTAPI_DB or appsettings)" 4 }

Info "Using connection: (masked)"
if ($Verbose) { Write-Host $connString -ForegroundColor DarkGray }

# Try Microsoft.Data.SqlClient first to retain modern keywords; fallback to System.Data.SqlClient with normalization
$useMicrosoft = $false
$msType = $null
try { $msType = [Microsoft.Data.SqlClient.SqlConnection] } catch {}
if (-not $msType) {
  try { Add-Type -AssemblyName Microsoft.Data.SqlClient -ErrorAction Stop | Out-Null; $msType = [Microsoft.Data.SqlClient.SqlConnection] } catch {}
}
if ($msType) {
  $useMicrosoft = $true
  Info "Using Microsoft.Data.SqlClient"
} else {
  # Normalize for System.Data.SqlClient
  $normalized = $connString -replace 'Trust Server Certificate','TrustServerCertificate'
  $normalized = $normalized -replace 'Encrypt\s*=\s*False','Encrypt=False'
  $normalized = ($normalized -split ';' | Where-Object { $_ -notmatch '^(Connect Retry Count|Connect Retry Interval)=' } ) -join ';'
  $normalized = ($normalized -split ';' | Where-Object { $_ -notmatch '^(Command Timeout|Application Name)=' } ) -join ';'
  $normalized = ($normalized -split ';' | Where-Object { $_ -notmatch '^(Authentication)=' } ) -join ';'
  $connString = $normalized
  # Ensure System.Data.SqlClient
  $sqlClientType = $null
  try { $sqlClientType = [System.Data.SqlClient.SqlConnection] } catch {}
  if (-not $sqlClientType) {
    foreach($asm in 'System.Data.SqlClient','System.Data','System.Data.Common'){
      try { Add-Type -AssemblyName $asm -ErrorAction Stop | Out-Null } catch {}
      try { $sqlClientType = [System.Data.SqlClient.SqlConnection]; if ($sqlClientType){ break } } catch {}
    }
  }
  if (-not $sqlClientType) { Fail "No suitable SqlClient (Microsoft.Data or System.Data) available" 5 }
}

if ($useMicrosoft) {
  $connection = New-Object Microsoft.Data.SqlClient.SqlConnection $connString
} else {
  $connection = New-Object System.Data.SqlClient.SqlConnection $connString
}

try {
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  $connection.Open()
  $sw.Stop()
  Ok ("Connected in {0}ms" -f $sw.ElapsedMilliseconds)
} catch {
  Fail "Connection failed: $($_.Exception.Message)" 2
}

try {
  $cmd = $connection.CreateCommand()
  $cmd.CommandText = 'SELECT 1;'
  $val = $cmd.ExecuteScalar()
  if ($val -eq 1) { Ok "Query returned 1" } else { Fail "Unexpected scalar result: $val" 3 }
} catch {
  Fail "Query failed: $($_.Exception.Message)" 3
} finally {
  try { $connection.Dispose() } catch {}
}

Ok "DB connectivity test passed"
exit 0
