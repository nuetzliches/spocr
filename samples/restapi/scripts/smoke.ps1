<#!
.SYNOPSIS
  SpocR vNext Sample Smoke Test.
.DESCRIPTION
  Führt einen deterministischen Mini-End-to-End-Test aus:
    1. Schema rebuild (optional falls spocr tooling verfügbar)
    2. Build der Sample REST API
    3. Start der API (im Hintergrund)
    4. Health Check Endpoint
    5. GET /api/users
    6. POST /api/users (CreateUserWithOutput)
    7. Aufräumen
  Bricht bei Fehler (ExitCode != 0) ab.
.NOTES
  Erwartet eine laufende SQL Server Instanz und gültige Stored Procedures (init Skripte).
#>

param(
  [int]$StartupTimeoutSeconds = 20,
  [int]$DockerStartupTimeoutSeconds = 90,
  [string]$Project = "samples/restapi/RestApi.csproj",
  [string]$BaseUrl = "http://localhost:5080", # Override via param falls Port anders
  [switch]$SkipRebuild,
  [switch]$AllowUnhealthy, # Wenn gesetzt: 503 Health wird toleriert (DB optional), DB-Endpunkte werden übersprungen
  [switch]$VerboseOutput,
  [switch]$UseDocker, # Startet samples/mssql docker compose
  [switch]$WriteGolden, # Schreiben Golden Hash Datei
  [switch]$VerifyGolden, # Verifizieren gegen Golden Hash Datei
  [string]$GoldenFile = "samples/restapi/SpocR/golden-output-hash.json",
  [switch]$HealthVerbose, # Detaillierte Fehlerausgabe bei fehlgeschlagenem Health Check
  [string]$ProcedurePreflight = "samples.UserList" # Stored Procedure Name für Preflight-Check (leer = deaktiviert)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step($msg) { Write-Host "[STEP] $msg" -ForegroundColor Cyan }
function Fail($msg) { Write-Host "[FAIL] $msg" -ForegroundColor Red; if ($global:proc -and -not $global:proc.HasExited) { try { $global:proc.Kill() } catch {} }; exit 1 }
function Ok($msg) { Write-Host "[OK] $msg" -ForegroundColor Green }
function Warn($msg) { Write-Host "[WARN] $msg" -ForegroundColor Yellow }

function Mask-ConnectionString {
  param([string]$ConnectionString)
  if (-not $ConnectionString) { return "<empty>" }
  $parts = @{}
  foreach ($seg in $ConnectionString -split ';') {
    if (-not $seg) { continue }
    $kv = $seg.Split('=',2)
    if ($kv.Count -eq 2) { $parts[$kv[0].Trim()] = $kv[1].Trim() }
  }
  function _first([hashtable]$h, [string[]]$keys) {
    foreach ($k in $keys) { if ($h.ContainsKey($k) -and $h[$k]) { return $h[$k] } }
    return $null
  }
  $server = _first $parts @('Server','Data Source')
  $db = _first $parts @('Database','Initial Catalog')
  if (-not $server) { $server = '<unknown>' }
  if (-not $db) { $db = '<unknown>' }
  return "Server=$server;Database=$db;...(masked)"
}

function Compute-OutputHashes {
  param([string]$Root = 'samples/restapi/SpocR')
  if (-not (Test-Path $Root)) { throw "Output Ordner '$Root' nicht gefunden" }
  $result = @{}
  Get-ChildItem -Path $Root -Recurse -File | Where-Object { $_.FullName -ne (Resolve-Path $GoldenFile -ErrorAction SilentlyContinue) } | ForEach-Object {
    $rel = (Resolve-Path $_.FullName) -replace [regex]::Escape((Resolve-Path $Root)), ''
    if ($rel.StartsWith('\')) { $rel = $rel.Substring(1) }
    $hash = Get-FileHash -Path $_.FullName -Algorithm SHA256
    $result[$rel] = $hash.Hash.ToLowerInvariant()
  }
  return $result
}

function Write-GoldenFile {
  param([string]$Path, [hashtable]$Data)
  $json = ($Data.GetEnumerator() | Sort-Object Name | ForEach-Object { $_ }) | ConvertTo-Json -Depth 5
  $dir = Split-Path $Path -Parent
  if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
  Set-Content -Path $Path -Value $json -Encoding UTF8
  Ok "Golden Hash Datei aktualisiert: $Path"
}

function Read-GoldenFile {
  param([string]$Path)
  if (-not (Test-Path $Path)) { throw "Golden Datei '$Path' fehlt" }
  (Get-Content -Raw -Path $Path | ConvertFrom-Json) | ForEach-Object { $_ }
}

# 1. Optional Schema rebuild (nur wenn Tooling + Konfiguration vorhanden)
if (-not $SkipRebuild) {
  if ((Test-Path src/SpocR.csproj) -and (Test-Path samples/restapi/spocr.json)) {
    Write-Step "Rebuild schema & generate code (dual mode)"
    try {
      dotnet run --project src/SpocR.csproj -- rebuild -p samples/restapi/spocr.json --no-auto-update | Out-Null
      Ok "Schema rebuild abgeschlossen"
    }
    catch {
      Fail "Schema rebuild fehlgeschlagen: $_"
    }
  } else {
    Write-Step "Überspringe Rebuild (Voraussetzungen fehlen)"
  }
}

# 1b. Optional Docker Compose Start
if ($UseDocker) {
  Write-Step "Starte Docker SQL (samples/mssql)"
  if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { Fail "docker CLI nicht gefunden" }
  Push-Location samples/mssql
  try {
    docker compose up -d --build | Out-Null
  }
  catch { Pop-Location; Fail "docker compose up fehlgeschlagen: $_" }
  Pop-Location
  # Warten auf Health=healthy
  Write-Step "Warte auf Container Health"
  $deadline = (Get-Date).AddSeconds($DockerStartupTimeoutSeconds)
  $healthy = $false
  while ((Get-Date) -lt $deadline) {
    $state = (docker inspect spocr-sample-sql --format '{{json .State.Health.Status}}' 2>$null)
    if ($state -and $state -match 'healthy') { $healthy = $true; break }
    Start-Sleep -Seconds 3
  }
  if (-not $healthy) { Fail "SQL Container wurde nicht healthy innerhalb ${DockerStartupTimeoutSeconds}s" }
  Ok "SQL Container healthy"
  # Setze Connection String ENV falls nicht gesetzt
  if (-not $env:SPOCR_SAMPLE_RESTAPI_DB) {
    $pw = (Get-Content samples/mssql/.env | Select-String 'MSSQL_SA_PASSWORD=' | ForEach-Object { $_.ToString().Split('=')[1] })
    $pw = $pw.Trim()
    $env:SPOCR_SAMPLE_RESTAPI_DB = "Server=localhost,1433;Database=SpocRSample;User ID=sa;Password=$pw;Encrypt=True;TrustServerCertificate=True;"
    $env:SPOCR_SAMPLE_RESTAPI_DB = $env:SPOCR_SAMPLE_RESTAPI_DB -replace ';{2,}', ';'
  }
}

if ($env:SPOCR_SAMPLE_RESTAPI_DB) {
  Write-Step ("ConnectionString (masked): " + (Mask-ConnectionString $env:SPOCR_SAMPLE_RESTAPI_DB))
} else {
  Warn "Keine SPOCR_SAMPLE_RESTAPI_DB Variable gesetzt (fällt ggf. auf LocalDB im Code zurück)."
}

# 2. Build
Write-Step "Build API"
try {
  dotnet build $Project -c Debug --nologo | Out-Null
  Ok "Build erfolgreich"
}
catch { Fail "Build fehlgeschlagen: $_" }

# 3. Start API
Write-Step "Starte API"
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = "dotnet"
$psi.Arguments = "run --no-build --project $Project --urls=$BaseUrl"
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$global:proc = [System.Diagnostics.Process]::Start($psi)

# Async Output Capture (optional)
$stdout = New-Object System.Text.StringBuilder
$stderr = New-Object System.Text.StringBuilder
$global:proc.add_OutputDataReceived({ if ($_.Data) { $null = $stdout.AppendLine($_.Data); if ($VerboseOutput) { Write-Host "[APP] $($_.Data)" } } })
$global:proc.add_ErrorDataReceived({ if ($_.Data) { $null = $stderr.AppendLine($_.Data); Write-Host "[APP-ERR] $($_.Data)" -ForegroundColor DarkRed } })
$global:proc.BeginOutputReadLine()
$global:proc.BeginErrorReadLine()

# Wait for startup
$started = $false
$deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
while (-not $started -and (Get-Date) -lt $deadline) {
  Start-Sleep -Milliseconds 500
  try {
    $resp = Invoke-WebRequest -Method GET "$BaseUrl/" -UseBasicParsing -TimeoutSec 3
    if ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 500) { $started = $true }
  } catch { }
}
if (-not $started) {
  $global:proc.Kill()
  Fail "API Start Timeout nach $StartupTimeoutSeconds s"
}
Ok "API gestartet"

# 3b. Golden Hash Write/Verify (vor HTTP Aufrufen; falls Generation stattfand)
if ($WriteGolden -and $VerifyGolden) { Fail "-WriteGolden und -VerifyGolden dürfen nicht gleichzeitig gesetzt sein" }
if ($WriteGolden) {
  Write-Step "Schreibe Golden Hash Datei"
  try {
    $hashes = Compute-OutputHashes
    Write-GoldenFile -Path $GoldenFile -Data $hashes
  } catch { Fail "Golden Write Fehler: $_" }
}
elseif ($VerifyGolden) {
  Write-Step "Prüfe Golden Hash Datei"
  try {
    $expected = Get-Content -Raw -Path $GoldenFile | ConvertFrom-Json
    $current = Compute-OutputHashes
    $differences = @()
    foreach ($k in $expected.PSObject.Properties.Name) {
      if (-not $current.ContainsKey($k)) { $differences += "FEHLT: $k"; continue }
      if ($current[$k] -ne ($expected.$k).ToLower()) { $differences += "HASH: $k" }
    }
    foreach ($k in $current.Keys) {
      if (-not $expected.PSObject.Properties.Name -contains $k) { $differences += "NEU: $k" }
    }
    if ($differences.Count -gt 0) { Fail "Golden Hash Abweichungen: `n$($differences -join "`n")" } else { Ok "Golden Hash verifiziert" }
  } catch { Fail "Golden Verify Fehler: $_" }
}

# Helper HTTP
function Invoke-Json($method, $url, $bodyObj) {
  try {
    if ($null -ne $bodyObj) {
      $json = ($bodyObj | ConvertTo-Json -Depth 5)
      return Invoke-WebRequest -Method $method -Uri $url -Body $json -ContentType 'application/json' -UseBasicParsing
    } else {
      return Invoke-WebRequest -Method $method -Uri $url -UseBasicParsing
    }
  }
  catch {
    $resp = $_.Exception.Response
    if ($resp) { return $resp }
    throw
  }
}

# 4. Health
Write-Step "Health Check"
$health = $null
try { $health = Invoke-Json GET "$BaseUrl/spocr/health/db" $null } catch { $health = $_.Exception.Response }
if ($health -and $health.StatusCode -eq 200) {
  Ok "Health ok"
} else {
  if ($HealthVerbose) {
    Write-Host "--- Health Diagnose ---" -ForegroundColor DarkCyan
    if ($health -and $health.Content) { Write-Host ($health.Content | Out-String) }
    else { Write-Host "Keine Response Content verfügbar." }
    Write-Host "-----------------------" -ForegroundColor DarkCyan
  }
  if ($AllowUnhealthy) {
    Warn "Health nicht OK (Status=$($health.StatusCode)); überspringe DB-Endpunkte aufgrund -AllowUnhealthy"
    Write-Step "Beende API Prozess (kein DB Smoke)"
    try { $global:proc.Kill(); $global:proc.WaitForExit(3000) | Out-Null } catch {}
    Ok "Partial Success (API startbar ohne DB)"
    exit 0
  } else {
    Fail "Health Endpoint Status=$($health.StatusCode)"
  }
}

# 4b. Stored Procedure Preflight (nur wenn Health OK und konfiguriert)
if ($ProcedurePreflight -and $UseDocker) {
  Write-Step "Preflight Stored Procedure: $ProcedurePreflight"
  try {
    $pwLine = (Get-Content samples/mssql/.env | Select-String 'MSSQL_SA_PASSWORD=' | ForEach-Object { $_.ToString().Split('=')[1] })
    $pwLine = $pwLine.Trim()
    & docker exec spocr-sample-sql /opt/mssql-tools/bin/sqlcmd -C -S localhost -U sa -P $pwLine -d SpocRSample -Q "SET NOCOUNT ON; EXEC $ProcedurePreflight;" 2>&1 | ForEach-Object { if ($VerboseOutput) { Write-Host "[PRE] $_" } }
    if ($LASTEXITCODE -ne 0) { Fail "Preflight fehlgeschlagen (ExitCode=$LASTEXITCODE)" } else { Ok "Preflight OK" }
  } catch { Fail "Preflight Ausnahme: $_" }
} elseif ($ProcedurePreflight) {
  Write-Step "Preflight übersprungen (kein -UseDocker gesetzt)"
}

# 5. Users List
Write-Step "GET /api/users"
$users = Invoke-Json GET "$BaseUrl/api/users" $null
if ($users.StatusCode -ne 200) {
  $body = $null
  try { $body = $users.Content } catch {}
  Fail "UserList Status=$($users.StatusCode) Body=$body"
}
Ok "UserList ok"

# 6. Create User
Write-Step "POST /api/users"
$newUser = Invoke-Json POST "$BaseUrl/api/users" @{ displayName = "SmokeUser"; email = "smoke@test.local" }
if ($newUser.StatusCode -ne 201) { Fail "CreateUser Status=$($newUser.StatusCode)" }
Ok "CreateUser ok"

# 7. Cleanup
Write-Step "Beende API Prozess"
try { $global:proc.Kill(); $global:proc.WaitForExit(5000) | Out-Null } catch { }
Ok "Fertig"

exit 0
