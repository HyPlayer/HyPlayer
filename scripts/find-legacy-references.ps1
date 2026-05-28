# find-legacy-references.ps1
# Searches for legacy model references in the HyPlayer codebase.
# Excludes specific allowed types from the Music namespace search.

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$results = [System.Collections.ArrayList]::new()

function Add-Result {
    param(
        [string]$Pattern,
        [string]$FilePath,
        [int]$LineNumber,
        [string]$LineContent
    )
    $relativePath = $FilePath.Replace("$repoRoot\", "").Replace("$repoRoot/", "")
    [void]$script:results.Add([PSCustomObject]@{
        Pattern    = $Pattern
        File       = $relativePath
        Line       = $LineNumber
        Content    = $LineContent.Trim()
    })
}

# 1. Search for 'using HyPlayer.Domain.Music' excluding allowed types
$allowedTypes = @("SimpleListItem", "SongListQueueScope", "MusicResource")
$csFiles = Get-ChildItem -Path $repoRoot -Recurse -Include "*.cs" -File

foreach ($file in $csFiles) {
    $lines = Get-Content -Path $file.FullName -ErrorAction SilentlyContinue
    if (-not $lines) { continue }
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match 'using\s+HyPlayer\.Domain\.Music') {
            # Check if line contains any of the allowed type imports
            $isAllowed = $false
            foreach ($type in $allowedTypes) {
                if ($line -match $type) {
                    $isAllowed = $true
                    break
                }
            }
            # A plain 'using HyPlayer.Domain.Music;' is a legacy reference
            # 'using HyPlayer.Domain.Music.SimpleListItem;' etc. are allowed
            if ($line -match 'using\s+HyPlayer\.Domain\.Music\s*;') {
                # This is the legacy blanket import
                Add-Result -Pattern "using HyPlayer.Domain.Music (legacy)" -FilePath $file.FullName -LineNumber ($i + 1) -LineContent $line
            }
            elseif (-not $isAllowed) {
                # Specific type in the Music namespace that isn't allowed
                Add-Result -Pattern "using HyPlayer.Domain.Music (specific)" -FilePath $file.FullName -LineNumber ($i + 1) -LineContent $line
            }
        }
    }
}

# 2. Search for legacy NC* model types
$legacyPatterns = @(
    @{ Pattern = '\bNCPlayList\b'; Name = "NCPlayList" },
    @{ Pattern = '\bNCArtist\b';  Name = "NCArtist" },
    @{ Pattern = '\bNCAlbum\b';   Name = "NCAlbum" },
    @{ Pattern = '\bNCUser\b';    Name = "NCUser" },
    @{ Pattern = '\bNCRadio\b';   Name = "NCRadio" },
    @{ Pattern = '\bNCMlog\b';    Name = "NCMlog" },
    @{ Pattern = '\bNCMFile\b';   Name = "NCMFile" }
)

foreach ($file in $csFiles) {
    $lines = Get-Content -Path $file.FullName -ErrorAction SilentlyContinue
    if (-not $lines) { continue }
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        foreach ($lp in $legacyPatterns) {
            if ($line -match $lp.Pattern) {
                Add-Result -Pattern $lp.Name -FilePath $file.FullName -LineNumber ($i + 1) -LineContent $line
            }
        }
    }
}

# 3. Search for HyPlayItemType
foreach ($file in $csFiles) {
    $lines = Get-Content -Path $file.FullName -ErrorAction SilentlyContinue
    if (-not $lines) { continue }
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match '\bHyPlayItemType\b') {
            Add-Result -Pattern "HyPlayItemType" -FilePath $file.FullName -LineNumber ($i + 1) -LineContent $line
        }
    }
}

# 4. Search for Infrastructure.Netease references
foreach ($file in $csFiles) {
    $lines = Get-Content -Path $file.FullName -ErrorAction SilentlyContinue
    if (-not $lines) { continue }
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match 'Infrastructure\.Netease') {
            Add-Result -Pattern "Infrastructure.Netease" -FilePath $file.FullName -LineNumber ($i + 1) -LineContent $line
        }
    }
}

# Output results
Write-Host "=========================================="
Write-Host "Legacy Reference Search Results"
Write-Host "=========================================="
Write-Host ""

# Summary by pattern
$grouped = $results | Group-Object -Property Pattern | Sort-Object -Property Name
Write-Host "--- Summary ---"
foreach ($group in $grouped) {
    Write-Host "$($group.Name): $($group.Count) references"
}
Write-Host ""
Write-Host "Total references found: $($results.Count)"
Write-Host ""

# Detailed results grouped by file
Write-Host "--- Detailed Results (by file) ---"
$byFile = $results | Group-Object -Property File | Sort-Object -Property Name
foreach ($fileGroup in $byFile) {
    Write-Host ""
    Write-Host "FILE: $($fileGroup.Name)"
    foreach ($item in ($fileGroup.Group | Sort-Object -Property Line)) {
        Write-Host "  Line $($item.Line) [$($item.Pattern)]: $($item.Content)"
    }
}

# Also output as flat list for easy parsing
Write-Host ""
Write-Host "--- Flat Output (file:line: content) ---"
foreach ($item in ($results | Sort-Object -Property File, Line)) {
    Write-Host "$($item.File):$($item.Line): $($item.Content)"
}
