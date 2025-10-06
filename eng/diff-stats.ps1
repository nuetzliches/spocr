param(
    [string]$DiffFile = "model-diff.json"
)

# If just a filename, resolve inside debug directory
if([IO.Path]::GetFileName($DiffFile) -eq $DiffFile){
    $debugDir = Join-Path -Path (Get-Location) -ChildPath 'debug'
    if(-not (Test-Path $debugDir)){ New-Item -ItemType Directory -Path $debugDir | Out-Null }
    $DiffFile = Join-Path $debugDir $DiffFile
}
if(-not (Test-Path $DiffFile)){ throw "Diff file not found: $DiffFile" }
$d = Get-Content $DiffFile -Raw | ConvertFrom-Json
$added = $d.Added
$addedJson = @($added | Where-Object { $_.RelativePath -like '*AsJson.cs' })
$addedNonJson = @($added | Where-Object { $_.RelativePath -notlike '*AsJson.cs' })

$stats = [pscustomobject]@{
    TotalAdded = $added.Count
    AddedJson = $addedJson.Count
    AddedNonJson = $addedNonJson.Count
    JsonPercentage = if($added.Count){ [math]::Round(($addedJson.Count / $added.Count)*100,2) } else { 0 }
    NonJsonTop10 = @($addedNonJson | Select-Object -First 10 | Select-Object -ExpandProperty RelativePath)
}
$stats | Format-List

# Emit machine readable JSON as well
$statsJsonPath = Join-Path (Split-Path $DiffFile -Parent) 'diff-stats.json'
$stats | ConvertTo-Json -Depth 4 | Out-File $statsJsonPath -Encoding UTF8
Write-Host "Stats written to $statsJsonPath" -ForegroundColor Green