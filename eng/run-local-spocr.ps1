param(
  [Parameter(Mandatory=$true, Position=0)]
  [ValidateSet('create','pull','build','rebuild','remove','project','schema','sp','snapshot','version','config')]
  [string]$Command,

  [Parameter(Mandatory=$true, Position=1)]
  [string]$ConfigPath,

  [Parameter(Position=2, ValueFromRemainingArguments=$true)]
  [string[]]$Args
)

$ErrorActionPreference = 'Stop'

function Get-RepoRoot {
  $here = Split-Path -Parent $MyInvocation.MyCommand.Path
  # eng folder → repo root
  return (Split-Path -Parent $here)
}

function Get-TargetFrameworkFromConfig([string]$configPath) {
  if (-not (Test-Path $configPath)) { throw "Config not found: $configPath" }
  $json = Get-Content -Raw -Path $configPath | ConvertFrom-Json
  $tfm = $json.TargetFramework
  if ([string]::IsNullOrWhiteSpace($tfm)) { return 'net10.0' }
  return $tfm
}

function Invoke-SpocR {
  param(
    [string]$tfm,
    [string]$command,
    [string]$config,
    [string[]]$extra
  )
  $root = Get-RepoRoot
  $proj = Join-Path $root 'src/SpocR.csproj'
  $forward = @('--no-auto-update') + $extra
  $dotnetArgs = @('run','--project', $proj, '--framework', $tfm, '--', $command, '-p', $config) + $forward
  Write-Host "dotnet $($dotnetArgs -join ' ')" -ForegroundColor Cyan
  & dotnet @dotnetArgs
}

try {
  $root = Get-RepoRoot
  if (-not (Test-Path $ConfigPath)) {
    # allow relative to repo root
    $candidate = Join-Path $root $ConfigPath
    if (Test-Path $candidate) { $ConfigPath = $candidate }
  }

  $tfm = Get-TargetFrameworkFromConfig -configPath $ConfigPath
  Invoke-SpocR -tfm $tfm -command $Command -config $ConfigPath -extra $Args
}
catch {
  Write-Host $_.Exception.Message -ForegroundColor Red
  exit 1
}

