<#!
.SYNOPSIS
    Vergleicht generierte SpocR Modell-Klassen (C#) zwischen zwei Verzeichnisbäumen.
.DESCRIPTION
    Normalisiert Klassen-Dateien, indem Header-Kommentare, Timestamp-Zeilen und Whitespace vereinfacht
    sowie nur relevante Strukturen (Namespace, Klassenname, public Properties) extrahiert werden.
    Erzeugt ein JSON mit Added/Removed/Changed Dateien und Property-Diffs.
.PARAMETER CurrentPath
    Pfad zum aktuellen (neuen) Modell-Root (z.B. debug\SpocR)
.PARAMETER ReferencePath
    Pfad zum Referenz- (alten) Modell-Root.
.PARAMETER OutputJson
    Zielpfad für Ergebnis-JSON (Default: ./model-diff.json)
.EXAMPLE
    ./eng/compare-models.ps1 -CurrentPath .\debug\SpocR -ReferencePath D:\Ref\DataContext\Models
#>
param(
    [Parameter(Mandatory=$true)][string]$CurrentPath,
    [Parameter(Mandatory=$true)][string]$ReferencePath,
    [string]$OutputJson = "model-diff.json"
)

# Ensure debug output directory when relative name supplied
if([IO.Path]::GetFileName($OutputJson) -eq $OutputJson){
    $debugDir = Join-Path -Path (Get-Location) -ChildPath 'debug'
    if(-not (Test-Path $debugDir)){ New-Item -ItemType Directory -Path $debugDir | Out-Null }
    $OutputJson = Join-Path $debugDir $OutputJson
}

if (-not (Test-Path $CurrentPath)) { throw "CurrentPath not found: $CurrentPath" }
if (-not (Test-Path $ReferencePath)) { throw "ReferencePath not found: $ReferencePath" }

function Get-CSharpModelInfo {
    param([string]$Root)
    $files = Get-ChildItem -Path $Root -Recurse -Include *.cs -File
    $list = @()
    foreach($f in $files){
        $raw = Get-Content $f.FullName -Raw
        # Strip auto-generated headers & comments
        $norm = $raw -replace '(?s)/\*.*?\*/','' -replace '(?m)^//.*$',''
        # Collapse whitespace
        $norm = ($norm -replace '\r','' -replace '\n',' ' -replace '\s+',' ').Trim()
        # Extract namespace, class name
        $ns = if($norm -match 'namespace\s+([A-Za-z0-9_.]+)'){ $matches[1] } else { '' }
        $class = if($norm -match 'class\s+([A-Za-z0-9_]+)'){ $matches[1] } else { [System.IO.Path]::GetFileNameWithoutExtension($f.Name) }
        # Extract public auto-properties (very simple regex)
        $props = @()
        $propRegex = [regex]'public\s+([A-Za-z0-9_<>,\[\]?]+)\s+([A-Za-z0-9_]+)\s*{\s*get;\s*set;\s*}'
        foreach($m in $propRegex.Matches($raw)){
            $props += [pscustomobject]@{ Type=$m.Groups[1].Value; Name=$m.Groups[2].Value }
        }
        $hashInput = ($props | ForEach-Object { "${($_.Type)} ${($_.Name)}" }) -join ';'
        if($hashInput){
            $sha = [System.Security.Cryptography.SHA256]::Create()
            $bytes = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($hashInput))
            $sigHash = ($bytes | ForEach-Object { $_.ToString("x2") }) -join ''
            $sha.Dispose()
        } else {
            $sigHash = ''
        }
        $list += [pscustomobject]@{
            RelativePath = (Resolve-Path $f.FullName).Path.Substring((Resolve-Path $Root).Path.Length).TrimStart('/','\')
            Namespace   = $ns
            Class       = $class
            Properties  = $props
            PropertyHash= $sigHash
            FullPath    = $f.FullName
        }
    }
    return $list
}

Write-Host "Collecting current..." -ForegroundColor Cyan
$current = Get-CSharpModelInfo -Root $CurrentPath
Write-Host "Collecting reference..." -ForegroundColor Cyan
$reference = Get-CSharpModelInfo -Root $ReferencePath

# Index by relative path (case-insensitive)
$refMap = @{}
foreach($r in $reference){ $refMap[$r.RelativePath.ToLower()] = $r }
$curMap = @{}
foreach($c in $current){ $curMap[$c.RelativePath.ToLower()] = $c }

$added = @()
$removed = @()
$changed = @()
$unchanged = @()

# Detect added/changed
foreach($k in $curMap.Keys){
    if(-not $refMap.ContainsKey($k)){
        $added += $curMap[$k]
    } else {
        $refItem = $refMap[$k]
        $curItem = $curMap[$k]
        if($refItem.PropertyHash -ne $curItem.PropertyHash -or $refItem.Class -ne $curItem.Class){
            # Build property diff
            $refProps = @{}
            foreach($p in $refItem.Properties){ $refProps[$p.Name] = $p }
            $curProps = @{}
            foreach($p in $curItem.Properties){ $curProps[$p.Name] = $p }
            $propAdded = $curProps.Keys | Where-Object { -not $refProps.ContainsKey($_) }
            $propRemoved = $refProps.Keys | Where-Object { -not $curProps.ContainsKey($_) }
            $propChanged = @()
            foreach($pn in $curProps.Keys){
                if($refProps.ContainsKey($pn) -and $refProps[$pn].Type -ne $curProps[$pn].Type){
                    $propChanged += [pscustomobject]@{ Name=$pn; OldType=$refProps[$pn].Type; NewType=$curProps[$pn].Type }
                }
            }
            $changed += [pscustomobject]@{
                RelativePath = $curItem.RelativePath
                ClassChanged = ($refItem.Class -ne $curItem.Class)
                OldClass     = $refItem.Class
                NewClass     = $curItem.Class
                AddedProperties   = $propAdded
                RemovedProperties = $propRemoved
                ChangedProperties = $propChanged
            }
        } else {
            $unchanged += $curItem
        }
    }
}

# Detect removed
foreach($k in $refMap.Keys){ if(-not $curMap.ContainsKey($k)){ $removed += $refMap[$k] } }

$result = [pscustomobject]@{
    Timestamp = (Get-Date).ToUniversalTime().ToString("u")
    CurrentPath = (Resolve-Path $CurrentPath).Path
    ReferencePath = (Resolve-Path $ReferencePath).Path
    Summary = [pscustomobject]@{
        Added    = $added.Count
        Removed  = $removed.Count
        Changed  = $changed.Count
        Unchanged= $unchanged.Count
        TotalCurrent = $current.Count
        TotalReference = $reference.Count
    }
    Added    = $added | Select-Object RelativePath,Class,Namespace,Properties
    Removed  = $removed | Select-Object RelativePath,Class,Namespace,Properties
    Changed  = $changed
}

$result | ConvertTo-Json -Depth 8 | Out-File $OutputJson -Encoding UTF8

Write-Host "Done. Summary:" -ForegroundColor Green
$result.Summary | Format-List
Write-Host "Detailed diff in $OutputJson" -ForegroundColor Green
