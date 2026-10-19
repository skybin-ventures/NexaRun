# Extracts the latest section from CHANGELOG.md for GitHub Release notes.
#
# Usage:
#   .\installer\Get-ChangelogNotes.ps1 -OutFile release-notes.md
#   .\installer\Get-ChangelogNotes.ps1 -Version 1.0.11 -UpdateHeading -OutFile release-notes.md
#
# With -UpdateHeading, rewrites the first ## heading to:
#   ## YYYY-MM-DD — v{Version}
# (keeps an existing date in the heading when present; otherwise uses UTC today.)

param(
    [string]$ChangelogPath = "",
    [string]$OutFile = "",
    [string]$Version = "",
    [switch]$UpdateHeading
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ChangelogPath)) {
    $ChangelogPath = Join-Path (Resolve-Path "$PSScriptRoot\..").Path "CHANGELOG.md"
}

if (-not (Test-Path $ChangelogPath)) {
    throw "CHANGELOG.md not found at $ChangelogPath"
}

$raw = Get-Content -Path $ChangelogPath -Raw
if ([string]::IsNullOrWhiteSpace($raw)) {
    throw "CHANGELOG.md is empty."
}

# Normalize line endings for parsing
$normalized = $raw -replace "`r`n", "`n" -replace "`r", "`n"
$lines = $normalized -split "`n"

$firstHeadingIndex = -1
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match '^##\s+') {
        $firstHeadingIndex = $i
        break
    }
}

if ($firstHeadingIndex -lt 0) {
    throw "No '##' section found in CHANGELOG.md after the title."
}

$nextHeadingIndex = $lines.Length
for ($i = $firstHeadingIndex + 1; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match '^##\s+') {
        $nextHeadingIndex = $i
        break
    }
}

$bodyLines = @()
for ($i = $firstHeadingIndex + 1; $i -lt $nextHeadingIndex; $i++) {
    $bodyLines += $lines[$i]
}

$notes = ($bodyLines -join "`n").Trim()
if ([string]::IsNullOrWhiteSpace($notes)) {
    throw "Latest CHANGELOG.md section is empty. Add release notes before releasing."
}

if ($UpdateHeading) {
    if ([string]::IsNullOrWhiteSpace($Version)) {
        throw "-UpdateHeading requires -Version."
    }

    $heading = $lines[$firstHeadingIndex]
    $date = $null
    if ($heading -match '(\d{4}-\d{2}-\d{2})') {
        $date = $Matches[1]
    }
    else {
        $date = [DateTime]::UtcNow.ToString("yyyy-MM-dd")
    }

    $versionLabel = $Version.TrimStart('v', 'V')
    $lines[$firstHeadingIndex] = "## $date — v$versionLabel"

    $updated = ($lines -join "`n")
    if ($raw.Contains("`r`n")) {
        $updated = $updated -replace "`n", "`r`n"
    }
    # Preserve final newline if original had one
    if ($raw.EndsWith("`n") -and -not $updated.EndsWith("`n")) {
        $updated += if ($raw.Contains("`r`n")) { "`r`n" } else { "`n" }
    }

    Set-Content -Path $ChangelogPath -Value $updated -NoNewline -Encoding utf8
    Write-Host "Updated heading: $($lines[$firstHeadingIndex])"
}

if (-not [string]::IsNullOrWhiteSpace($OutFile)) {
    $outDir = Split-Path -Parent $OutFile
    if (-not [string]::IsNullOrWhiteSpace($outDir) -and -not (Test-Path $outDir)) {
        New-Item -ItemType Directory -Path $outDir | Out-Null
    }
    Set-Content -Path $OutFile -Value $notes -Encoding utf8
    Write-Host "Wrote release notes to $OutFile ($($notes.Length) chars)"
}
else {
    Write-Output $notes
}
