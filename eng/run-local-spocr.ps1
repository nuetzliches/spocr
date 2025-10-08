<#!
.SYNOPSIS
  Convenience wrapper to run a local SpocR pull + generate cycle against a config.

.DESCRIPTION
  Ensures the project is built (optionally restores) and then invokes the SpocR CLI
  with typical development defaults. Targets modern mode automatically if the
  sample/web-api project targets net10. Pass-through extra CLI arguments with -SpocrArgs.

.PARAMETER Config
  Path to spocr.json (default: samples/web-api/spocr.json)

.PARAMETER SkipPull
  Skip the 'pull' phase (only run generation).

.PARAMETER SkipGenerate
  Skip the 'generate' phase (only pull definitions).

.PARAMETER NoRestore
  Do not run a dotnet restore before build.

.PARAMETER Configuration
  Build configuration (Debug|Release). Default Debug.

.PARAMETER SpocrArgs
  Additional raw arguments forwarded to each SpocR CLI invocation.

.EXAMPLE
  pwsh -File eng/run-local-spocr.ps1

.EXAMPLE
  pwsh -File eng/run-local-spocr.ps1 -Config ./my/spocr.json -SpocrArgs "--only inputs,models"

.NOTES
  Writes transient artifacts to the stabilized ./debug directory (repo root anchored).
#>
param(
    [string]$Config = "samples/web-api/spocr.json",
    [switch]$SkipPull,
    [switch]$SkipGenerate,
    [switch]$NoRestore,
    [string]$Configuration = "Debug",
    [string]$SpocrArgs
)

$ErrorActionPreference = 'Stop'

function Write-Info($msg){ Write-Host "[run-local] $msg" -ForegroundColor Cyan }
function Write-Step($msg){ Write-Host "==> $msg" -ForegroundColor Green }

# Resolve repo root (directory containing SpocR.sln)
$repoRoot = git rev-parse --show-toplevel 2>$null
if(-not $repoRoot){
  # Fallback: ascend looking for solution
  $probe = (Get-Location).Path
  while($probe -and -not (Test-Path (Join-Path $probe 'SpocR.sln'))){
    $parent = Split-Path $probe -Parent
    if($parent -eq $probe){ break }
    $probe = $parent
  }
  if(Test-Path (Join-Path $probe 'SpocR.sln')){ $repoRoot = $probe } else { $repoRoot = (Get-Location).Path }
}
Set-Location $repoRoot

if(-not (Test-Path $Config)){ throw "Config not found: $Config" }

$spocrProj = Join-Path $repoRoot 'src/SpocR.csproj'
if(-not (Test-Path $spocrProj)){ throw "SpocR.csproj not found under src/" }

Write-Step "Build CLI ($Configuration)"
if(-not $NoRestore){ dotnet restore $spocrProj | Out-Null }
dotnet build $spocrProj -c $Configuration --nologo

$cli = Join-Path $repoRoot 'src/bin' | Join-Path -ChildPath "$Configuration"
# Detect produced dll (multi-target maybe). Pick highest TFM folder.
$tfms = Get-ChildItem -Directory $cli | Select-Object -ExpandProperty Name | Sort-Object
if($tfms.Count -gt 0){ $selectedTfm = $tfms[-1]; $cliPath = Join-Path $cli $selectedTfm | Join-Path -ChildPath 'SpocR.dll' } else { $cliPath = Join-Path $cli 'SpocR.dll' }
if(-not (Test-Path $cliPath)){ throw "CLI assembly not found: $cliPath" }

$baseArgs = "-p $Config"
if($SpocrArgs){ $baseArgs = "$baseArgs $SpocrArgs" }

if(-not $SkipPull){
  Write-Step "Pull"
  # CommandLineUtils expects: dotnet <dll> pull [options]
  dotnet $cliPath pull $baseArgs
} else { Write-Info "SkipPull enabled" }

if(-not $SkipGenerate){
  Write-Step "Build"
  # Generation is performed by 'build' (there is no 'generate' command in current CLI)
  dotnet $cliPath build $baseArgs
} else { Write-Info "SkipGenerate enabled" }

Write-Step "Done"
