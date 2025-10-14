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
  [string]$Framework = "net8.0", # Explizites Target Framework (multi-target project erfordert --framework)
  [int]$PostStartDelayMs = 800, # kleine Wartezeit nach API Start bevor DB Zugriffe
  [int]$DbPingRetries = 3,
  [int]$DbPingRetryDelayMs = 1200,
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

# Ensure explicit namespace required by vNext (no auto-derivation)
if (-not $env:SPOCR_NAMESPACE) { $env:SPOCR_NAMESPACE = 'RestApi' }

# Reduce user list payload during smoke runs unless explicitly provided
if (-not $env:SPOCR_SAMPLE_USERLIST_LIMIT) { $env:SPOCR_SAMPLE_USERLIST_LIMIT = '50' }

function Write-Step($msg) { Write-Host "[STEP] $msg" -ForegroundColor Cyan }
function Fail($msg) {
  Write-Host "[FAIL] $msg" -ForegroundColor Red
  if ($stdout -and $stdout.Length -gt 0) {
    Write-Host "--- APP STDOUT (tail) ---" -ForegroundColor DarkCyan
    ($stdout.ToString().TrimEnd() -split "`n" | Select-Object -Last 60) | ForEach-Object { Write-Host $_ }
    Write-Host "--------------------------" -ForegroundColor DarkCyan
  }
  if ($stderr -and $stderr.Length -gt 0) {
    Write-Host "--- APP STDERR (tail) ---" -ForegroundColor DarkYellow
    ($stderr.ToString().TrimEnd() -split "`n" | Select-Object -Last 60) | ForEach-Object { Write-Host $_ }
    Write-Host "--------------------------" -ForegroundColor DarkYellow
  }
  if ($global:proc -and -not $global:proc.HasExited) { try { $global:proc.Kill() } catch {} }
  exit 1
}
function Ok($msg) { Write-Host "[OK] $msg" -ForegroundColor Green }
function Warn($msg) { Write-Host "[WARN] $msg" -ForegroundColor Yellow }

function Stop-ExistingSampleProcesses {
  Write-Step "Process cleanup (existing dotnet RestApi)"
  try {
    $procs = Get-Process dotnet -ErrorAction SilentlyContinue | Where-Object { $_.Path -and ($_.Path -match 'dotnet') }
    $killed = 0
    foreach ($p in $procs) {
      try {
        # Inspect command line via WMI (best-effort)
        $wmi = Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)" -ErrorAction SilentlyContinue
        $cmd = $wmi.CommandLine
        if ($cmd -and $cmd -match 'samples/restapi/RestApi.csproj') {
          Write-Host "[INFO] Killing stale sample process PID=$($p.Id)" -ForegroundColor DarkGray
          $p.Kill(); $killed++
        }
      } catch {}
    }
    Ok "Process cleanup done (killed=$killed)"
  } catch { Warn "Process cleanup failed: $_" }
}

function Test-PortFree {
  param([string]$Url)
  try {
    $u = [System.Uri]$Url
    $port = $u.Port
  } catch { Write-Host "[WARN] Cannot parse BaseUrl '$Url' for port check: $_" -ForegroundColor Yellow; return }
  Write-Step "Port check (port=$port)"
  try {
    $listeners = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
  } catch { $listeners = @() }
  if (-not $listeners -or $listeners.Count -eq 0) { Ok "Port $port free"; return }
  foreach ($ln in $listeners) {
  $ownPid = $ln.OwningProcess
  $proc = Get-Process -Id $ownPid -ErrorAction SilentlyContinue
    if ($proc) {
      $cmd = (Get-CimInstance Win32_Process -Filter "ProcessId=$pid" -ErrorAction SilentlyContinue).CommandLine
  Write-Host "[INFO] Port $port in use by PID=$ownPid Name=$($proc.ProcessName)" -ForegroundColor DarkGray
      if ($proc.ProcessName -eq 'dotnet') {
        # Heuristik: falls unsere Sample DLL geladen ist -> kill
        if ($cmd -and $cmd -match 'RestApi.dll') {
          Write-Host "[INFO] Killing dotnet process using RestApi.dll (PID=$ownPid)" -ForegroundColor DarkGray
          try { $proc.Kill(); Start-Sleep -Milliseconds 500 } catch { Write-Host "[WARN] Kill failed: $_" -ForegroundColor Yellow }
        } else {
          Write-Host "[WARN] Dotnet Prozess belegt Port aber keine RestApi.dll im CmdLine -> versuche dennoch Kill" -ForegroundColor Yellow
          try { $proc.Kill(); Start-Sleep -Milliseconds 500 } catch {}
        }
      } else {
  Write-Host "[WARN] Nicht-dotnet Prozess blockiert Port $port (PID=$ownPid). Wähle alternativen Port." -ForegroundColor Yellow
        $newPort = $port + 5
        $global:BaseUrlOverride = "$($u.Scheme)://$($u.Host):$newPort"
        Write-Host "[INFO] BaseUrl geändert auf $global:BaseUrlOverride" -ForegroundColor DarkCyan
        return
      }
    }
  }
  # Re-check
  try { $listeners = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue } catch { $listeners=@() }
  if (-not $listeners -or $listeners.Count -eq 0) { Ok "Port $port frei nach Cleanup"; return }
  # As fallback choose new port
  $fallback = $port + 3
  $global:BaseUrlOverride = "$($u.Scheme)://$($u.Host):$fallback"
  Write-Host "[WARN] Port $port weiterhin belegt. Weiche auf $global:BaseUrlOverride aus." -ForegroundColor Yellow
}

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

# 1. Optional Docker Compose Start (jetzt vor Rebuild, damit Generator gegen Container-DB laufen kann)
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

# 2. Optional Schema rebuild (jetzt nach Docker Startup)
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

if ($env:SPOCR_SAMPLE_RESTAPI_DB) {
  Write-Step ("ConnectionString (masked): " + (Mask-ConnectionString $env:SPOCR_SAMPLE_RESTAPI_DB))
} else {
  Warn "Keine SPOCR_SAMPLE_RESTAPI_DB Variable gesetzt (fällt ggf. auf LocalDB im Code zurück)."
}

# 3. Build
Write-Step "Build API"
try {
  if ($Framework) {
    Write-Host "[INFO] Verwende Framework '$Framework'" -ForegroundColor DarkGray
    dotnet build $Project -c Debug -f $Framework --nologo | Out-Null
  } else {
    dotnet build $Project -c Debug --nologo | Out-Null
  }
  Ok "Build erfolgreich"
}
catch { Fail "Build fehlgeschlagen: $_" }

# 4. Start API
Stop-ExistingSampleProcesses
Test-PortFree -Url $BaseUrl
if (Get-Variable -Name BaseUrlOverride -Scope Global -ErrorAction SilentlyContinue) {
  if ($global:BaseUrlOverride) { $BaseUrl = $global:BaseUrlOverride }
}
Write-Step "Starte API (BaseUrl=$BaseUrl)"
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = "dotnet"
if ($Framework) { $psi.Arguments = "run --project $Project --framework $Framework --urls=$BaseUrl" } else { $psi.Arguments = "run --project $Project --urls=$BaseUrl" }
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$global:proc = [System.Diagnostics.Process]::Start($psi)

# Early crash detection (process might exit before we attach async readers)
Start-Sleep -Milliseconds 250
if ($global:proc.HasExited) {
  $earlyOut = try { $global:proc.StandardOutput.ReadToEnd() } catch { '' }
  $earlyErr = try { $global:proc.StandardError.ReadToEnd() } catch { '' }
  if ($earlyOut) { Write-Host "--- EARLY STDOUT ---`n$earlyOut`n-------------------" -ForegroundColor DarkCyan }
  if ($earlyErr) { Write-Host "--- EARLY STDERR ---`n$earlyErr`n-------------------" -ForegroundColor DarkYellow }
  Fail "API process crashed during startup (ExitCode=$($global:proc.ExitCode))"
}

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

# Optionale kurze Verzögerung um DB / pooling readiness abzuwarten
if ($PostStartDelayMs -gt 0) { Start-Sleep -Milliseconds $PostStartDelayMs }

# 4b. Golden Hash Write/Verify (vor HTTP Aufrufen; falls Generation stattfand)
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

# 4. Ping then Health
Write-Step "Ping check"
try {
  $ping = Invoke-WebRequest -Method GET "$BaseUrl/api/ping" -UseBasicParsing -TimeoutSec 5
  if ($ping.StatusCode -ge 200 -and $ping.StatusCode -lt 300) { Ok "Ping ok" } else { Fail "Ping failed Status=$($ping.StatusCode)" }
} catch { Fail "Ping request failed: $($_.Exception.Message)" }

Write-Step "DB ping check"
Write-Step "DbContext DI check"
try {
  $di = Invoke-WebRequest -Method GET "$BaseUrl/api/dbcontext/di" -UseBasicParsing -TimeoutSec 5
  if ($di.StatusCode -ge 200 -and $di.StatusCode -lt 300) { Ok "DbContext DI ok" } else { Fail "DbContext DI failed Status=$($di.StatusCode)" }
} catch { Fail "DbContext DI request failed: $($_.Exception.Message)" }
function Invoke-DbPingWithRetry {
  param([int]$Attempts,[int]$DelayMs)
  for ($i=1; $i -le $Attempts; $i++) {
    Write-Host "[INFO] DB ping attempt $i/$Attempts" -ForegroundColor DarkGray
    try {
      $resp = Invoke-WebRequest -Method GET "$BaseUrl/api/dbping" -UseBasicParsing -TimeoutSec 12
      if ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 300) { return @{ ok = $true; resp = $resp } }
      Write-Host "[INFO] DB ping non-success Status=$($resp.StatusCode) BodyLen=$(($resp.Content).Length)" -ForegroundColor DarkGray
    } catch {
      Write-Host "[INFO] DB ping exception: $($_.Exception.Message)" -ForegroundColor DarkGray
    }
    if ($i -lt $Attempts) { Start-Sleep -Milliseconds $DelayMs }
  }
  return @{ ok = $false }
}
$dbPingResult = Invoke-DbPingWithRetry -Attempts $DbPingRetries -DelayMs $DbPingRetryDelayMs
if (-not $dbPingResult.ok) {
  Write-Step "Fetch endpoint list for diagnostics (dbping failure)"
  try {
    $eps = Invoke-WebRequest -Method GET "$BaseUrl/_debug/endpoints" -UseBasicParsing -TimeoutSec 5
    Write-Host "[INFO] Endpoints Response Status=$($eps.StatusCode)" -ForegroundColor DarkGray
    Write-Host ($eps.Content | Out-String)
  } catch { Write-Host "[INFO] Could not retrieve /_debug/endpoints: $($_.Exception.Message)" -ForegroundColor DarkGray }
  Fail "DB ping failed after $DbPingRetries attempts"
} else {
  Ok "DB ping ok"
}

Write-Step "DB Health check"
$health = $null
try {
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  $health = Invoke-Json GET "$BaseUrl/spocr/health/db" $null
  $sw.Stop()
  Write-Host "[INFO] Health latency $($sw.ElapsedMilliseconds)ms" -ForegroundColor DarkGray
} catch {
  Write-Host "[INFO] Health exception: $($_.Exception.GetType().Name) $($_.Exception.Message)" -ForegroundColor DarkGray
  $health = $null
}
if ($health -and $health.StatusCode -eq 200) {
  Ok "Health ok"
} else {
  if ($HealthVerbose) {
    Write-Host "--- Health diagnostics ---" -ForegroundColor DarkCyan
    if ($health -and $health.Content) { Write-Host ($health.Content | Out-String) }
    else { Write-Host "No response content available." }
    Write-Host "-----------------------" -ForegroundColor DarkCyan
  }
  if ($AllowUnhealthy) {
    Warn "Health not OK (Status=$($health.StatusCode)); skipping DB endpoints due to -AllowUnhealthy"
    Write-Step "Stop API process (no DB smoke)"
    try { $global:proc.Kill(); $global:proc.WaitForExit(3000) | Out-Null } catch {}
    Ok "Partial success (API reachable without DB)"
    exit 0
  } else {
    Fail "Health endpoint Status=$($health.StatusCode)"
  }
}

# 4b. Stored Procedure Preflight (nur wenn Health OK und konfiguriert)
if ($ProcedurePreflight -and $UseDocker) {
  Write-Step "Preflight stored procedure: $ProcedurePreflight"
  try {
    $pwLine = (Get-Content samples/mssql/.env | Select-String 'MSSQL_SA_PASSWORD=' | ForEach-Object { $_.ToString().Split('=')[1] })
    $pwLine = $pwLine.Trim()
    & docker exec spocr-sample-sql /opt/mssql-tools/bin/sqlcmd -C -S localhost -U sa -P $pwLine -d SpocRSample -Q "SET NOCOUNT ON; EXEC $ProcedurePreflight;" 2>&1 | ForEach-Object { if ($VerboseOutput) { Write-Host "[PRE] $_" } }
    if ($LASTEXITCODE -ne 0) { Fail "Preflight failed (ExitCode=$LASTEXITCODE)" } else { Ok "Preflight OK" }
  } catch { Fail "Preflight exception: $_" }
} elseif ($ProcedurePreflight) {
  Write-Step "Preflight skipped (no -UseDocker)"
}

# 5. Users List
Write-Step "List endpoints (pre users)"
try {
  $eps = Invoke-WebRequest -Method GET "$BaseUrl/_debug/endpoints" -UseBasicParsing -TimeoutSec 5
  Write-Host "[DIAG] endpoints count=$((($eps.Content | ConvertFrom-Json).list).Count)" -ForegroundColor DarkGray
} catch { Write-Host "[DIAG] endpoints fetch failed: $($_.Exception.Message)" -ForegroundColor Yellow }

Write-Step "Users namespace ping"
try {
  $up = Invoke-WebRequest -Method GET "$BaseUrl/api/users/ping" -UseBasicParsing -TimeoutSec 5
  Write-Host "[DIAG] users/ping status=$($up.StatusCode) body=$($up.Content)" -ForegroundColor DarkGray
} catch { Write-Host "[DIAG] users/ping failed: $($_.Exception.Message)" -ForegroundColor Yellow }

Write-Step "Users count endpoint"
try {
  $uc = Invoke-WebRequest -Method GET "$BaseUrl/api/users/count" -UseBasicParsing -TimeoutSec 15
  Write-Host "[DIAG] users/count status=$($uc.StatusCode) body=$($uc.Content.Substring(0, [Math]::Min(120,$uc.Content.Length)))" -ForegroundColor DarkGray
} catch { Write-Host "[DIAG] users/count failed: $($_.Exception.Message)" -ForegroundColor Yellow }

Write-Step "GET /api/users"
if ($global:proc -and $global:proc.HasExited) {
  Write-Host "[DIAG] API Prozess vor /api/users bereits beendet ExitCode=$($global:proc.ExitCode)" -ForegroundColor Yellow
  if ($stdout.Length -gt 0) { Write-Host "--- STDOUT (tail) ---" -ForegroundColor DarkCyan; ($stdout.ToString().TrimEnd() -split "`n" | Select-Object -Last 80) | ForEach-Object { Write-Host $_ }; Write-Host "---------------------" -ForegroundColor DarkCyan }
  if ($stderr.Length -gt 0) { Write-Host "--- STDERR (tail) ---" -ForegroundColor DarkYellow; ($stderr.ToString().TrimEnd() -split "`n" | Select-Object -Last 80) | ForEach-Object { Write-Host $_ }; Write-Host "---------------------" -ForegroundColor DarkYellow }
  Fail "API process exited unerwartet vor UserList"
}
$users = Invoke-Json GET "$BaseUrl/api/users" $null
if ($users.StatusCode -ne 200) {
  $raw = try { $users.Content } catch { '' }
  $len = 0
  if (-not [string]::IsNullOrEmpty($raw)) { $len = $raw.Length }
  if ($len -gt 0) {
    Write-Host "[DIAG] /api/users body (first 500 chars):" -ForegroundColor DarkGray
    Write-Host ($raw.Substring(0, [Math]::Min(500, $raw.Length))) -ForegroundColor DarkGray
  } else {
    Write-Host "[DIAG] /api/users empty body" -ForegroundColor DarkGray
  }
  Fail "UserList Status=$($users.StatusCode) BodyLength=$len LimitEnv=$($env:SPOCR_SAMPLE_USERLIST_LIMIT)"
}
Ok "UserList ok"

# 6. Create User
Write-Step "POST /api/users"
$newUser = Invoke-Json POST "$BaseUrl/api/users" @{ displayName = "SmokeUser"; email = "smoke@test.local" }
if ($newUser.StatusCode -ne 201) { Fail "CreateUser Status=$($newUser.StatusCode)" }
Ok "CreateUser ok"

# 7. Cleanup
Write-Step "Stop API process"
try { $global:proc.Kill(); $global:proc.WaitForExit(5000) | Out-Null } catch { }
Ok "Done"

exit 0
