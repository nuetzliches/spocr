# SpocR Quality Gates Script (moved to eng/)
# This script runs all quality checks before commits

param(
    [switch]$SkipTests,
    [switch]$SkipCoverage,
    [int]$CoverageThreshold = 80,
    [string]$Configuration = 'Release'
)

Write-Host "SpocR Quality Gates" -ForegroundColor Cyan
Write-Host "===================" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor DarkGray
if (-not $SkipCoverage) { Write-Host "Coverage Threshold: $CoverageThreshold%" -ForegroundColor DarkGray }

$exitCode = 0

# Ensure artifacts directories exist
$artifactRoot = ".artifacts"
$testResultsDir = Join-Path $artifactRoot "test-results"
$coverageDir = Join-Path $artifactRoot "coverage"

New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
New-Item -ItemType Directory -Force -Path $testResultsDir | Out-Null
New-Item -ItemType Directory -Force -Path $coverageDir | Out-Null

function Ensure-ReportGenerator {
    $toolList = dotnet tool list -g 2>$null
    if ($toolList -notmatch "reportgenerator") {
        Write-Host "Installing reportgenerator global tool..." -ForegroundColor Yellow
        dotnet tool install -g dotnet-reportgenerator-globaltool | Out-Null
    }
}

# 1. Build Check
Write-Host "`nBuilding project..." -ForegroundColor Yellow
dotnet build src/SpocR.csproj --configuration $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed" -ForegroundColor Red
    $exitCode = 1
} else {
    Write-Host "Build successful" -ForegroundColor Green
}

# 2. Self-Validation
Write-Host "`nRunning self-validation..." -ForegroundColor Yellow
dotnet run --project src/SpocR.csproj --configuration $Configuration -- test --validate
if ($LASTEXITCODE -ne 0) {
    Write-Host "Self-validation failed" -ForegroundColor Red
    $exitCode = 1
} else {
    Write-Host "Self-validation passed" -ForegroundColor Green
}

# 3. Tests
if (-not $SkipTests) {
    Write-Host "`nRunning tests..." -ForegroundColor Yellow
    if (-not $SkipCoverage) {
        dotnet test tests/Tests.sln --configuration $Configuration --collect:"XPlat Code Coverage" --results-directory $testResultsDir --logger "trx;LogFileName=tests.trx"
    } else {
        dotnet test tests/Tests.sln --configuration $Configuration --results-directory $testResultsDir --logger "trx;LogFileName=tests.trx"
    }
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Tests failed" -ForegroundColor Red
        $exitCode = 1
    } else {
        Write-Host "All tests passed" -ForegroundColor Green
    }
}

# 4. Coverage Analysis
if (-not $SkipCoverage -and -not $SkipTests) {
    Write-Host "`nAnalyzing code coverage..." -ForegroundColor Yellow
    Ensure-ReportGenerator
    reportgenerator -reports:"$testResultsDir/**/coverage.cobertura.xml" -targetdir:"$coverageDir" -reporttypes:"Html;Badges;Cobertura" | Out-Null
    $coverageSummary = Get-ChildItem -Path $coverageDir -Filter "Summary.xml" -Recurse | Select-Object -First 1
    $summaryLog = Join-Path $coverageDir 'coverage-summary.log'
    if ($coverageSummary) {
        [xml]$xml = Get-Content $coverageSummary.FullName
        $lineRate = [double]$xml.coverage.'line-rate'
        $percent = [math]::Round($lineRate * 100, 2)
        $msg = "Line Coverage: $percent% (Threshold: $CoverageThreshold%)"
        Write-Host $msg -ForegroundColor Cyan
        Set-Content -Path $summaryLog -Value $msg
        if ($CoverageThreshold -gt 0 -and $percent -lt $CoverageThreshold) {
            Write-Host "Coverage below threshold ($CoverageThreshold%)" -ForegroundColor Red
            $exitCode = 1
        }
    } else {
        $warn = "No coverage summary produced (tests may have failed before collection)."
        Write-Warning $warn
        Set-Content -Path $summaryLog -Value $warn
    }
}

# Summary
Write-Host "`nQuality Gates Summary" -ForegroundColor Cyan
Write-Host "=====================" -ForegroundColor Cyan

if ($exitCode -eq 0) {
    Write-Host "All quality gates passed!" -ForegroundColor Green
    Write-Host "Ready for commit/push" -ForegroundColor Green
} else {
    Write-Host "Some quality gates failed!" -ForegroundColor Red
    Write-Host "Please fix issues before committing" -ForegroundColor Red
}

exit $exitCode
