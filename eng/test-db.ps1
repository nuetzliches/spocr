<#
.SYNOPSIS
  Simpler Verbindungs-/Liveness-Test für eine SQL Server DB mit Retry & klaren Exit Codes.
.DESCRIPTION
  Baut eine Verbindung auf Basis einer Connection String Quelle (Param, ENV oder .env Datei) auf.
  Exit Codes:
    0  Erfolg
    30 Verbindungsaufbau dauerhaft fehlgeschlagen
    31 Timeout (keine erfolgreiche Öffnung innerhalb Gesamtzeit)
.PARAMETER ConnectionString
  Optional expliziter Connection String; überschreibt alles andere.
.PARAMETER Retries
  Anzahl Wiederholungen (Default 5)
.PARAMETER DelaySeconds
  Basis Wartezeit zwischen Versuchen (exponentiell + jitter) Default 2
.PARAMETER TimeoutSeconds
  Gesamtzeitlimit in Sekunden (Default 45)
.EXAMPLE
  pwsh eng/test-db.ps1 -ConnectionString "Server=localhost,1433;User Id=sa;Password=Your!Passw0rd;TrustServerCertificate=true;" -Retries 8
.NOTES
  Verschoben aus scripts/test-db.ps1 (aufgeräumt, Pfad konsolidiert in eng/).
#>
[CmdletBinding()] param(
  [string]$ConnectionString,
  [int]$Retries = 5,
  [int]$DelaySeconds = 2,
  [int]$TimeoutSeconds = 45
)

$ErrorActionPreference = 'Stop'
$start = Get-Date

function Resolve-ConnectionString {
  param([string]$Explicit)
  if ($Explicit) { return $Explicit }
  # ENV Variablen Priorität
  $candidates = @(
    $env:SPOCR_SAMPLE_RESTAPI_DB,
    $env:SPOCR_CONNECTION_STRING,
    $env:ConnectionStrings__Default,
    $env:DefaultConnection
  ) | Where-Object { $_ }
  if ($candidates.Count -gt 0) { return $candidates[0] }
  # .env lesen (einfaches Parsing KEY=VALUE, Kommentare ignorieren)
  $envFile = Join-Path (Get-Location) '.env'
  if (Test-Path $envFile) {
    foreach ($line in Get-Content $envFile) {
      if ($line -match '^[#;]') { continue }
      if ($line -match '^\s*$') { continue }
      if ($line -match '^(?<k>[A-Za-z0-9_]+)=(?<v>.+)$') {
        $k = $Matches.k; $v = $Matches.v.Trim()
        if ($k -eq 'SPOCR_SAMPLE_RESTAPI_DB' -and $v) { return $v }
      }
    }
  }
  throw 'Keine ConnectionString Quelle gefunden (ENV oder .env oder Parameter).'
}

try { Import-Module Microsoft.Data.SqlClient -ErrorAction SilentlyContinue | Out-Null } catch {}

$connStr = Resolve-ConnectionString -Explicit $ConnectionString
Write-Host "[test-db] Verwende ConnectionString (maskiert Passwort):" -ForegroundColor Cyan
Write-Host ( $connStr -replace 'Password=[^;]+','Password=***' )

$attempt = 0
$rand = [System.Random]::new()
$connected = $false
while ($attempt -lt $Retries) {
  $elapsed = (Get-Date) - $start
  if ($elapsed.TotalSeconds -ge $TimeoutSeconds) {
    Write-Error "[test-db] Timeout nach $([int]$elapsed.TotalSeconds)s ohne Erfolg."; exit 31
  }
  $attempt++
  Write-Host "[test-db] Versuch $attempt/$Retries..." -ForegroundColor Yellow
  try {
    $csType = [Type]::GetType('Microsoft.Data.SqlClient.SqlConnection, Microsoft.Data.SqlClient')
    if (-not $csType) { $csType = [Type]::GetType('System.Data.SqlClient.SqlConnection, System.Data') }
    if (-not $csType) { throw 'Kein SqlClient Typ geladen.' }
    $conn = [Activator]::CreateInstance($csType, $connStr)
    $openTask = $conn.OpenAsync()
    $opened = $openTask.Wait([TimeSpan]::FromSeconds([Math]::Min(10, $TimeoutSeconds)))
    if (-not $opened) { throw 'OpenAsync Timeout' }
    # einfache Probe
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = 'SELECT 1'
    $scalar = $cmd.ExecuteScalar()
    if ($scalar -ne 1) { throw "Probe unerwartet: $scalar" }
    Write-Host "[test-db] Verbindung ok nach $([int]((Get-Date)-$start).TotalSeconds)s." -ForegroundColor Green
    $connected = $true
    $conn.Dispose()
    break
  } catch {
    Write-Warning "[test-db] Fehler: $($_.Exception.Message)"
    $sleep = $DelaySeconds * [Math]::Pow(1.4, $attempt-1)
    $sleep = [Math]::Min($sleep, 8) + ($rand.NextDouble()*0.5)
    Start-Sleep -Seconds $sleep
  }
}

if (-not $connected) {
  Write-Error "[test-db] Dauerhaft fehlgeschlagen nach $Retries Versuchen."; exit 30
}
exit 0
