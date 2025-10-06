# SpocR Quality Gates Script (moved to eng/)
# This script runs all quality checks before commits

param(
    [switch]$SkipTests,
    [switch]$SkipCoverage,
    [int]$CoverageThreshold = 0
)

Write-Host "SpocR Quality Gates" -ForegroundColor Cyan
Write-Host "===================" -ForegroundColor Cyan

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
dotnet build src/SpocR.csproj --configuration Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed" -ForegroundColor Red
    $exitCode = 1
} else {
    Write-Host "Build successful" -ForegroundColor Green
}

# 2. Self-Validation
Write-Host "`nRunning self-validation..." -ForegroundColor Yellow
dotnet run --project src/SpocR.csproj --configuration Release -- test --validate
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
        dotnet test tests/Tests.sln --configuration Release --collect:"XPlat Code Coverage" --results-directory $testResultsDir
    } else {
        dotnet test tests/Tests.sln --configuration Release --results-directory $testResultsDir
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
    reportgenerator -reports:"$testResultsDir/**/coverage.cobertura.xml" -targetdir:"$coverageDir" -reporttypes:"Html;Badges" | Out-Null
    $coverageSummary = Get-ChildItem -Path $coverageDir -Filter "Summary.xml" -Recurse | Select-Object -First 1
    if ($coverageSummary -and $CoverageThreshold -gt 0) {
        [xml]$xml = Get-Content $coverageSummary.FullName
        # Cobertura line-rate is attribute line-rate on coverage node (0..1)
        $lineRate = [double]$xml.coverage.'line-rate'
        $percent = [math]::Round($lineRate * 100, 2)
        Write-Host "Line Coverage: $percent%" -ForegroundColor Cyan
        if ($percent -lt $CoverageThreshold) {
            Write-Host "Coverage below threshold ($CoverageThreshold%)" -ForegroundColor Red
            $exitCode = 1
        }
    } else {
        Write-Host "Coverage report generated: $coverageDir/index.html" -ForegroundColor Cyan
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
