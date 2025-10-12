param(
    [switch]$SkipRestore
)

Write-Host "[build-sample] Start" -ForegroundColor Cyan

$proj = Join-Path $PSScriptRoot "..\web-api.sln"
if (-not (Test-Path $proj)) {
    Write-Error "Solution nicht gefunden: $proj"
    exit 2
}

if (-not $SkipRestore) {
    dotnet restore $proj || exit 10
}

dotnet build $proj -c Debug --nologo || exit 11

Write-Host "[build-sample] OK" -ForegroundColor Green
