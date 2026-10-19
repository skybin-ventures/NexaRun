# Publishes all three NexaRun projects as self-contained win-x64 single-file binaries
# then compiles the Inno Setup installer.
#
# Usage (from repo root or installer/):
#   .\installer\build.ps1
#   .\installer\build.ps1 -Version 1.0.11
#
# Version defaults to <Version> in Directory.Build.props (single source of truth).
#
# Requirements:
#   - .NET 10 SDK
#   - Inno Setup 6 or 7 (ISCC.exe on PATH, or default install folder)

param(
    [string]$Version = "",
    [string]$OutDir  = "$PSScriptRoot\output"
)

$ErrorActionPreference = "Stop"
$Root    = Resolve-Path "$PSScriptRoot\.."
$PubDir  = "$PSScriptRoot\publish"
$PropsPath = Join-Path $Root "Directory.Build.props"

function Get-VersionFromProps {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        throw "Directory.Build.props not found at $Path"
    }
    $raw = Get-Content -Path $Path -Raw
    if ($raw -notmatch '<Version>([^<]+)</Version>') {
        throw "Could not find <Version> in $Path"
    }
    return $Matches[1].Trim()
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-VersionFromProps -Path $PropsPath
}

Write-Host "==> Building NexaRun $Version" -ForegroundColor Cyan

Write-Host "==> Generating NexaRun.ico..." -ForegroundColor Yellow
& "$PSScriptRoot\generate-icon.ps1"
$SetupIcon = (Resolve-Path "$PSScriptRoot\assets\NexaRun.ico").Path
if (-not (Test-Path $SetupIcon)) {
    throw "Setup icon not found at $SetupIcon. Run installer\generate-icon.ps1 first."
}
Write-Host "    Setup icon: $SetupIcon" -ForegroundColor DarkGray

# Clean solution + previous publish (avoids stale DLLs in the installer)
Write-Host "==> Cleaning solution..." -ForegroundColor Yellow
dotnet clean "$Root\NexaRun.slnx" -c Release | Out-Null
if ($LASTEXITCODE -ne 0) { throw "dotnet clean failed" }

if (Test-Path $PubDir) { Remove-Item $PubDir -Recurse -Force }
New-Item $PubDir -ItemType Directory | Out-Null

$svc = Get-Service -Name NexaRunDaemon -ErrorAction SilentlyContinue
if ($svc -and $svc.Status -eq 'Running') {
    Write-Host "==> Stopping NexaRunDaemon service (unlocks build output)..." -ForegroundColor Yellow
    Stop-Service NexaRunDaemon -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}

Write-Host "==> Restoring + building solution (Release)..." -ForegroundColor Yellow
$VersionProps = @(
    "/p:Version=$Version",
    "/p:AssemblyVersion=$Version.0",
    "/p:FileVersion=$Version.0",
    "/p:InformationalVersion=$Version"
)
$BuildProps = @(
    "/p:WarningsNotAsErrors=NU1903",
    "/p:NuGetAuditLevel=critical"
) + $VersionProps
dotnet restore "$Root\NexaRun.slnx" @BuildProps
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed (exit $LASTEXITCODE). If you see NU1903, pull latest NexaRun/Directory.Build.props."
}
dotnet build "$Root\NexaRun.slnx" -c Release --no-restore @BuildProps
if ($LASTEXITCODE -ne 0) {
    throw @"
dotnet build failed (exit $LASTEXITCODE).
Typical causes:
  - NexaRun tray or daemon still running (close tray; stop NexaRunDaemon service)
  - NU1903 NuGet audit on Tmds.DBus.Protocol — update NexaRun/Directory.Build.props
  Re-run: dotnet build $Root\NexaRun.slnx -c Release
"@
}

$PublishArgs = @(
    "--configuration", "Release",
    "--runtime",       "win-x64",
    "--self-contained", "true",
    "/p:PublishSingleFile=true",
    "/p:IncludeNativeLibrariesForSelfExtract=true",
    "/p:EnableCompressionInSingleFile=true",
    "/p:DebugType=none"
) + $VersionProps

# Publish Daemon
Write-Host "==> Publishing Daemon..." -ForegroundColor Yellow
dotnet publish "$Root\NexaRun.Daemon" @PublishArgs --output "$PubDir\daemon"
if ($LASTEXITCODE -ne 0) { throw "Daemon publish failed" }

# Publish CLI
Write-Host "==> Publishing CLI..." -ForegroundColor Yellow
dotnet publish "$Root\NexaRun.Cli" @PublishArgs --output "$PubDir\cli"
if ($LASTEXITCODE -ne 0) { throw "CLI publish failed" }

# Publish Tray
Write-Host "==> Publishing Tray..." -ForegroundColor Yellow
dotnet publish "$Root\NexaRun.Tray" @PublishArgs --output "$PubDir\tray"
if ($LASTEXITCODE -ne 0) { throw "Tray publish failed" }

$expected = @(
    @{ Path = "$PubDir\daemon\NexaRun.Daemon.exe"; Label = "Daemon" },
    @{ Path = "$PubDir\cli\NexaRun.Cli.exe"; Label = "CLI" },
    @{ Path = "$PubDir\tray\NexaRun.exe"; Label = "Tray" }
)
foreach ($e in $expected) {
    if (-not (Test-Path $e.Path)) {
        throw "Publish missing $($e.Label): $($e.Path)"
    }
    $age = (Get-Item $e.Path).LastWriteTime
    Write-Host "    OK $($e.Label) ($([math]::Round((Get-Item $e.Path).Length/1MB, 2)) MB, $age)" -ForegroundColor DarkGray
}
if (-not (Test-Path "$Root\nexarun-processes.json")) {
    throw "Missing $Root\nexarun-processes.json (required for installer)"
}

# Block accidental Debug / project bin output in publish folder
foreach ($e in $expected) {
    $full = (Get-Item $e.Path).FullName
    if ($full -match '\\bin\\Debug\\' -or $full -match '\\bin\\Release\\net') {
        throw "Refusing to pack non-publish build: $full`nRun only via this script (dotnet publish -c Release to installer\publish)."
    }
}

if (-not (Test-Path $OutDir)) { New-Item $OutDir -ItemType Directory | Out-Null }
$PubDirResolved = (Resolve-Path $PubDir).Path -replace '\\', '/'
$OutDirResolved = (Resolve-Path $OutDir).Path -replace '\\', '/'

# Compile Inno Setup installer (Inno Setup 6 or 7)
function Find-Iscc {
    $cmd = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($path in $candidates) {
        if (Test-Path $path) { return $path }
    }
    return $null
}

$ISCC = Find-Iscc
if (-not ($ISCC)) {
    throw @"
ISCC.exe not found. Install Inno Setup 6 or 7, or add its folder to PATH.
Typical locations:
  C:\Program Files (x86)\Inno Setup 7\ISCC.exe
  C:\Program Files\Inno Setup 7\ISCC.exe
"@
}
Write-Host "    ISCC: $ISCC" -ForegroundColor DarkGray

Write-Host "==> Compiling installer..." -ForegroundColor Yellow
Push-Location $PSScriptRoot
try {
    Write-Host "    PubDir (Release publish): $PubDirResolved" -ForegroundColor DarkGray
    & $ISCC `
        "/DAppVersion=$Version" `
        "/DPubDir=$PubDirResolved" `
        "/DOutDir=$OutDirResolved" `
        "NexaRun.iss"
} finally {
    Pop-Location
}

if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed" }

Write-Host ""
$setup = Join-Path $OutDir "NexaRun-$Version-Setup.exe"
Write-Host ""
Write-Host "==> Done! Installer at:" -ForegroundColor Green
Write-Host "    $setup" -ForegroundColor Green
Write-Host "    Size: $([math]::Round((Get-Item $setup).Length/1MB, 2)) MB, built $(Get-Item $setup).LastWriteTime" -ForegroundColor DarkGray
Write-Host ""
Write-Host "On the server: uninstall old NexaRun, run the new setup, then restart the NexaRunDaemon service." -ForegroundColor Cyan
