# Computes the next NexaRun version with two-digit patch/minor rollover.
#
# Rules:
#   patch += 1
#   if patch > 99 → patch = 0, minor += 1
#   if minor > 99 → minor = 0, major += 1
#
# Usage:
#   .\installer\Get-NextVersion.ps1 -Version 1.0.99
#   .\installer\Get-NextVersion.ps1   # reads Directory.Build.props

param(
    [string]$Version = "",
    [string]$PropsPath = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Version)) {
    if ([string]::IsNullOrWhiteSpace($PropsPath)) {
        $PropsPath = Join-Path (Resolve-Path "$PSScriptRoot\..").Path "Directory.Build.props"
    }
    if (-not (Test-Path $PropsPath)) {
        throw "Directory.Build.props not found at $PropsPath"
    }
    $raw = Get-Content -Path $PropsPath -Raw
    if ($raw -notmatch '<Version>([^<]+)</Version>') {
        throw "Could not find <Version> in $PropsPath"
    }
    $Version = $Matches[1].Trim()
}

if ($Version -notmatch '^(\d+)\.(\d+)\.(\d+)$') {
    throw "Version '$Version' is not major.minor.patch"
}

$major = [int]$Matches[1]
$minor = [int]$Matches[2]
$patch = [int]$Matches[3]

if ($minor -gt 99 -or $patch -gt 99) {
    throw "Version '$Version' is invalid: minor and patch must be 0–99."
}

$patch++
if ($patch -gt 99) {
    $patch = 0
    $minor++
}
if ($minor -gt 99) {
    $minor = 0
    $major++
}

$next = "$major.$minor.$patch"
Write-Output $next
