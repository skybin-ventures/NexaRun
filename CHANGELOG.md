# Changelog

## 2026-10-19
- CI: fix Release installer guard step exiting 1 after a successful “no tag/release” check (leftover pwsh `$LASTEXITCODE`)
- Release: single version source in `Directory.Build.props`; `installer\build.ps1` reads it and passes MSBuild + Inno versions; `.iss` requires `/DAppVersion`
- CI: manual `Release installer` workflow bumps patch, builds Windows setup, creates `v*` GitHub Release with the installer attached
- CI: release body comes from the latest `CHANGELOG.md` section (`installer\Get-ChangelogNotes.ps1`); top heading is labeled `— vX.Y.Z` and committed with the version bump
- CI: version bump uses two-digit patch/minor rollover (`installer\Get-NextVersion.ps1`: `1.0.99` → `1.1.0`, `1.99.99` → `2.0.0`)
- Docs: README Releasing section (Actions trigger + required workflow permissions / branch-protection notes)
- Feature: import currently online PM2 apps (`nexarun import --from-pm2`, tray **Import from PM2…**); enriches from `dump.pm2` when present; register-only by default
- Lifecycle: fix PID reattach on daemon Load (preserve + fingerprint); DesiredStatus boot restore; monitor AutoRestart=false exits; cancel crash-restart on user stop; soft-then-force stop (8s); data dir `%ProgramData%\NexaRun`; pipe ACL for Interactive users
- CLI: script-path `start`, incremental `logs --follow`, `describe` / `history` / `save` / `resurrect` / `daemon status` / `stop all` / `delete all`; exit codes 2/3; settle defaults 2s; PM2 JSON field subset on import
- Tray: Processes window search/status filter, empty/error states, scroll-preserving refresh; shared log viewer; soft-then-force stop UX across dialogs
- Tests: `NexaRun.Tests` lifecycle harness
- Docs: README + CLI-GUIDE aligned with behavior

## 2026-10-17
- Bump Skyb.Framework package refs / drop Directory.Build.props; refresh nexarun-processes.json
## 2026-09-08
- Point managed Next.js process ports and docs examples at the **31xx** frontend range (e.g. FormFuse 3101) to match PORTS.md

## 2026-09-01
- Bump Microsoft.Extensions.* and System packages to 10.0.11 across Cli, Daemon, Shared, and Tray
- Bump Avalonia 12.0.x â†’ 12.1.x, Spectre.Console 0.57.1 â†’ 0.57.2, System.CommandLine 2.0.9 â†’ 2.0.11, Serilog.Sinks.File 7.0.0 â†’ 8.0.0, and AWSSDK.SimpleEmailV2 4.0.100.1 â†’ 4.0.103.1

## 2026-07-23
- Align Microsoft.Extensions.* and related System packages to 10.0.10 across Cli, Daemon, Shared, and Tray

## 2026-07-03
- `Directory.Build.props` â€” add strong-name signing via `skybin.snk` (retains v1.0.10 version and NuGet audit settings)
- `NexaRun.Daemon` â€” bump `AWSSDK.SimpleEmailV2` 4.0.14.9 â†’ 4.0.100.1

## 2026-06-29
- `NexaRun.Cli` â€” bump `Spectre.Console` 0.57.0 â†’ 0.57.1
- `NexaRun.Daemon` â€” bump `AWSSDK.SimpleEmailV2` 4.0.14.8 â†’ 4.0.14.9

## [2026-06-24] â€” v1.0.10

### Added
- `clear-logs` IPC command and **Clear Log** button in Logs window and Dashboard logs tab (truncates log files while the process keeps running)

### Fixed
- `NexaRun.Tray` â€” replace Unicode toolbar glyphs (âš™, âŸ³, â†») with `PathIcon` vectors so Settings/Refresh icons render on Windows Server
- `ProcessLaunchHelper` / `ProcessManager` â€” decode child stdout/stderr as UTF-8 so log viewer shows symbols correctly (â–², âœ“) instead of mojibake

### Changed
- `NexaRun.Tray` â€” bump Avalonia + Avalonia.Desktop + Avalonia.Themes.Fluent 12.0.4â†’12.0.5; Avalonia.Controls.DataGrid 12.0.0â†’12.0.1

## [2026-06-24] â€” v1.0.9

### Fixed
- `NexaRun.Tray/Program.cs` â€” add `[STAThread]` so Win32 clipboard/COM works (fixes `CO_E_NOTINITIALIZED` crash on TextBox copy)
- `NexaRun.Tray` â€” load tray icon from embedded `.ico` after platform init; window-only fallback when notification area unavailable; single-instance mutex

## 2026-06-17
- `NexaRun.Daemon` â€” bump `AWSSDK.SimpleEmailV2` 4.0.14.6 â†’ 4.0.14.7

## 2026-06-16
- `NexaRun.Daemon/Properties/launchSettings.json` â€” set `launchBrowser: false` on the debug profile

## 2026-06-15
- `NexaRun.Daemon` â€” bump `AWSSDK.SimpleEmailV2` 4.0.14.5 â†’ 4.0.14.6
- `NexaRun.Daemon.csproj` â€” bump package dependency versions

## 2026-06-10
- All `.csproj` files â€” bump `Microsoft.EntityFrameworkCore` and `Microsoft.Extensions.Configuration.Json` from 10.0.8 â†’ 10.0.9

## [2026-06-06]

### Changed
- `NexaRun.Cli/NexaRun.Cli.csproj` â€” bump `Spectre.Console` 0.55.2â†’0.56.0
- `NexaRun.Daemon/NexaRun.Daemon.csproj` â€” bump `AWSSDK.SimpleEmailV2` 4.0.14.1â†’4.0.14.2

## [2026-06-05]

### Changed
- `NexaRun.Daemon` â€” upgrade `AWSSDK.SimpleEmail` to `AWSSDK.SimpleEmailV2` (v4.0.14) with API updates to use SendEmailV2 request structure
- `ProcessAlertService` â€” migrate from Amazon.SimpleEmail to Amazon.SimpleEmailV2; update SendEmailRequest to use V2 API (FromEmailAddress, EmailContent with nested Message structure)
- `NexaRun.Daemon/NexaRun.Daemon.csproj` â€” bump `AWSSDK.SimpleEmailV2` 4.0.14â†’4.0.14.1

## [2026-05-30] â€” v1.0.8

### Added
- `nexarun restart all` â€” PM2-style alias for restarting every managed process (same behavior as `nexarun restart-all`)
- `NexaRun.Cli/RestartAllRunner` â€” shared CLI orchestration for restart-all: per-app Spectre progress bar, id-based restarts, settle wait only on the last process

### Changed
- `nexarun restart` â€” accepts `all` as a target in addition to id or name; help text documents PM2-style usage
- `NexaRun.Tray/NexaRun.Tray.csproj` â€” bump Avalonia + Avalonia.Desktop + Avalonia.Themes.Fluent 12.0.3â†’12.0.4

## 2026-05-23
- **NexaRun.Tray:** Refactor Avalonia 12 window code-behind to typed patterns; align tray csproj packages

## [2026-05-23] â€” v1.0.7

### Changed
- `NexaRun.Daemon` â€” bump `AWSSDK.SimpleEmail` from 3.7.401 to 4.0.2.31

### Added
- `NexaRun.Shared/ProcessTarget` â€” PM2-style id-or-name resolution; all CLI commands and daemon handlers now accept a numeric id (from `nexarun list`) or a name string
- `NexaRun.Cli/CliCommands` â€” shared `TargetArgument` and `TargetRequest` helpers used by stop, restart, delete, logs, and start commands
- `NexaRun.Cli/CliIpc` â€” `IpcClient.Send` extension with optional Spectre.Console spinner and elapsed timer; `RunWithProgress` helper; per-command status messages
- `nexarun restart-all` â€” restarts every managed process one at a time with a Spectre progress bar; skips settle wait between processes, only waits after the last one
- `nexarun clear-all` â€” stop and remove all processes at once
- `nexarun version` â€” print assembly informational version
- `ProcessManager.StartExisting` â€” start a saved (stopped/errored) process by id or name without re-registering it
- `IpcRequest.SettleAfterStart` â€” lets restart-all skip the post-start wait between processes

### Changed
- `nexarun start` â€” unified target argument: pass an id/name to resume a saved process, or an executable with flags to register a new one
- `nexarun stop/restart/delete/logs` â€” all accept id or name (was name-only)
- `ProcessManager.Stop/Restart/Delete/GetLogs/GetHistory` â€” resolve via `ProcessTarget.TryResolve`; error messages include `[id] name`
- `ProcessManager.GetLogs` and `GetHistory` return result tuples with success flag; daemon surfaces errors instead of silently returning empty
- `IpcServer` â€” `"start"` dispatch splits: start-existing path when `Options.ExecutablePath` is absent; `HandleRestart` forwards `SettleAfterStart` flag

## [2026-05-22] â€” v1.0.6

### Added
- **Settings** window: configurable failed-process recovery interval (min 10 min), enable/disable recovery, **AWS SES** email when a process goes Online â†’ Errored
- Daemon **`restart-all`** â€” restart every process one by one without selecting a row in the tray

### Changed
- Installer: `ChangesEnvironment=yes` and post-install PATH update so `nexarun` works in new terminals (`{app}\bin`)
- Import no longer auto-starts processes (use **Start** or `nexarun import --start`)
- **Restart All** uses daemon `restart-all`; no row selection required
- Recovery check interval and email alerts read from `%APPDATA%\NexaRun\settings.json`

## [2026-05-21] â€” v1.0.5

### Added
- JSON import/export (`nexarun import`, tray, Processes window, **Import JSONâ€¦** on Add Process)
- PM2-style CLI (`start`, `stop`, `restart`, `delete`, `list`, `logs`, `daemon`)
- `nexarun-processes.json` for all 14 monorepo apps (`C:\repo\SSC.Nebula` paths)
- Dashboard **Logs** tab in the same window (optional **Live update**)
- Tray crash logging to Event Viewer (`NexaRun-Tray`) and `logs\tray-crash.log`
- `ProcessLaunchHelper` for windowless process launches on Windows

### Changed
- Tray app `WinExe` (no console window); simplified tray import menu
- Dashboard background refresh reduced (30s stats only; history/logs on demand)
- Installer: Inno Setup 7 detection, Release publish guard, event log source registration

## [2026-05-08]

### Changed
- Upgraded System.CommandLine from 2.0.0 to 2.0.7 for compatibility with latest API
- Removed SetHandler calls from all CLI commands; simplified command definitions to structure only (handlers to be wired separately)

## [2026-04-28] â€” v1.0.1

### Changed
- All four assemblies strong-name signed with `skybin.snk` (Skybin Technology Private Limited)
- Assembly names cleaned up: Tray â†’ `NexaRun.exe`, Daemon â†’ `NexaRun.Daemon.exe`, CLI â†’ `NexaRun.Cli.exe` (installed to PATH as `nexarun.exe`)
- Added `Product`, `Company`, and `Copyright` metadata to all exe projects â€” visible in Windows file properties
- Installer updated: copyright set to "Skybin Technology Private Limited", CLI bin copy renamed to `nexarun.exe`
- README rewritten as a user-facing guide â€” covers installation, tray app, CLI, examples, and auto-restart behaviour; removed developer/project structure content

## [2026-04-28] â€” v1.0.0

### Added
- `NexaRun.Daemon` â€” background Worker Service with named pipe IPC server, ProcessManager singleton, and DaemonWorker monitor loop
- `NexaRun.Cli` â€” CLI with `start`, `stop`, `restart`, `delete`, `list`, `logs`, and `daemon` commands using System.CommandLine and Spectre.Console
- `NexaRun.Tray` â€” cross-platform Avalonia 11.3 system tray app with Processes window, Dashboard window, Add/Edit Process form, Logs viewer, and Confirm dialog
- `NexaRun.Shared` â€” shared models (`NexaProcess`, `ProcessRun`, `StartOptions`, `IpcRequest`, `IpcResponse`), `IpcClient`, and `PipeConstants`
- Process list persisted to `~/.nexarun/processes.json`; run history (last 7 days) persisted to `~/.nexarun/history.json`
- stdout/stderr capture to `~/.nexarun/logs/<name>.log` with `FileShare.ReadWrite` to allow concurrent reading
- Auto-restart on crash with exponential backoff (1 s â†’ 2 s â†’ 4 s â†’ 8 s â†’ 30 s max)
- Resource-limit restarts: configurable Max CPU % and Max Memory MB per process; breach reason written to process log
- CPU usage calculated from `TotalProcessorTime` deltas across the 5-second monitor interval
- Executable resolved from PATH via `cmd /c` on Windows â€” no need for full paths (`npm`, `dotnet`, `node`, etc.)
- Dashboard with 7-day uptime bar chart (green â‰¥ 95 %, yellow â‰¥ 50 %, red < 50 %) and run history grid
- Edit Process form pre-filled from current process settings; running processes stopped and restarted on save
- `update` IPC command for editing a running process in place
- `history` IPC command returning last 7 days of `ProcessRun` records for a named process
- README.md with full usage guide, CLI reference, tray app guide, and changelog
