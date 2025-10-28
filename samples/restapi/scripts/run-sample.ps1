Write-Host "== SpocR Sample Run (Next Mode) ==" -ForegroundColor Cyan

# Optional strict modes (uncomment to experiment)
# $env:SPOCR_STRICT_DIFF = '1'
# $env:SPOCR_STRICT_NULLABLE = '1'

# 1. Generate (if generation command needed, placeholder):
# dotnet run --project ../../src/SpocR.csproj generate

# 2. Build sample API
Write-Host "Building sample Web API..." -ForegroundColor Yellow
 dotnet build ../RestApi.sln
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed"; exit 1 }

# 3. Run (press Ctrl+C to stop)
Write-Host "Starting sample API (Development)..." -ForegroundColor Yellow
 dotnet run --project ../RestApi.csproj --no-launch-profile
