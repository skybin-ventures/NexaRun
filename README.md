# NexaRun

NexaRun is a process manager for Windows — keep your apps running forever. Start any process (Node.js, .NET, Python, anything), and NexaRun will monitor it, restart it if it crashes, and enforce CPU and memory limits. Manage everything from a clean system tray GUI or the command line.

It is a **machine-wide** Windows Service for **System Administrators** (LocalSystem / `%ProgramData%\NexaRun`). It is not a per-user daemon.

---

## Installation

Run `NexaRun-Setup.exe` and follow the wizard.

The installer will:
- Install the **NexaRun Daemon** as a Windows Service that starts automatically with Windows
- Copy the CLI to `C:\Program Files\NexaRun\bin\nexarun.exe` and append that folder to the **system PATH**
- Launch the **NexaRun tray app** so you can start managing processes immediately
- Ship a sample `nexarun-processes.json` for JSON import

After install, **open a new Command Prompt or PowerShell window** (existing terminals do not pick up PATH until restarted). Then run:

```powershell
nexarun list
```

If `nexarun` is still not found:

1. **Close all terminals** and open a new one (PATH is read when the shell starts).
2. Check system PATH includes `C:\Program Files\NexaRun\bin` (Settings → System → About → Advanced system settings → Environment Variables → System `Path`).
3. **Repair PATH** (Administrator PowerShell):

```powershell
& "C:\Program Files\NexaRun\fix-path.ps1"
```

Or reinstall / run the latest `NexaRun-Setup.exe` (the installer runs `fix-path.ps1` automatically).

Until PATH works, use the full path:

```powershell
& "C:\Program Files\NexaRun\bin\nexarun.exe" list
```

To build the installer from source: run `installer\generate-icon.ps1` then `installer\build.ps1` (requires Inno Setup 6). The app version is defined once in `Directory.Build.props`; `build.ps1` reads it and passes it to the installer.

### Releasing

Patch releases are created manually via GitHub Actions:

1. Update the **latest** section in [`CHANGELOG.md`](CHANGELOG.md) with notes for this release (that section becomes the GitHub Release body).
2. Open **Actions** → **Release installer** → **Run workflow** (on `main`).
3. The workflow bumps the version in `Directory.Build.props` (patch and minor are two digits: `1.0.10` → `1.0.11`, `1.0.99` → `1.1.0`, `1.99.99` → `2.0.0`), builds `NexaRun-<version>-Setup.exe`, labels the top changelog heading with `— v<version>`, commits both, and creates a GitHub Release tagged `v<version>` with the installer attached and notes from that changelog section.
4. Re-running after a successful release fails safely if that tag or release already exists.

**Repository settings required**

- Actions enabled; workflow permissions set to **Read and write** (Settings → Actions → General) so `GITHUB_TOKEN` can push the version commit and create the release.
- If `main` blocks bot pushes, allow `github-actions[bot]` to bypass branch protection (or use a fine-scoped PAT as `RELEASE_TOKEN` — not required when the default token can push).
- Only users with write access can run `workflow_dispatch`.

---

## Tray App

Click the NexaRun icon in the system tray to get started. Right-click for the menu.

### Add a Process

1. Right-click the tray icon → **Add Process** (optional **Environment & readiness** expander for `KEY=VALUE` env, ready-port/file)
2. Fill in the form:

| Field | Description |
|---|---|
| **Name** | A unique name to identify this process |
| **Executable** | The command to run — works just like a terminal (`npm`, `dotnet`, `node`, `python`, etc.) |
| **Arguments** | Arguments passed to the executable |
| **Working Directory** | The folder to run the process in |
| **Auto-restart on crash** | Automatically restart if the process exits unexpectedly |
| **Max CPU %** | Restart if CPU usage exceeds this value (e.g. `80`) |
| **Max Memory MB** | Restart if memory exceeds this value (e.g. `512`) |

3. Click **Save & Start**

### Examples

| App | Executable | Arguments | Working Directory |
|---|---|---|---|
| Next.js site | `npm` | `run start` | `C:\Projects\my-site` |
| Next.js dev | `npm` | `run dev` | `C:\Projects\my-site` |
| .NET API | `dotnet` | `run --project .` | `C:\Projects\MyApi` |
| Node server | `node` | `server.js` | `C:\Projects\worker` |
| Python script | `python` | `app.py` | `C:\Projects\bot` |

### Tray menu — import / export

| Menu item | Action |
|---|---|
| **Import JSON…** | Pick a `.json` file and register processes (does not start them; use Start in Processes) |
| **Import from PM2…** | Pick a `dump.pm2` file and register apps (definitions only) |
| **Settings…** | Recovery interval, AWS SES email (API or SMTP), From/To addresses |
| **Add Process → Import JSON…** | Same import from the Add Process window |
| **Export JSON…** | Save definitions for another machine |
| **Processes window** | **Import JSON** / **Import from PM2** / **Export JSON** (same as tray) |
| **Open data folder** | Opens `%ProgramData%\NexaRun` |

### Processes Window

Right-click tray → **Processes** to see all running processes. The list auto-refreshes every 45 seconds and keeps your selection. Select a process to use the action buttons:

| Button | Action |
|---|---|
| **Stop All** | Stop every running process |
| **Restart All** | Restart every process in the list one by one (no row selection required) |
| **Clear All** | Stop running apps, remove every entry, and save an empty process list |
| **Settings** | Recovery interval, enable/disable recovery, AWS SES email alerts |
| **Open URL** | Open the process URL in your browser (if configured) |
| **Stop** | Soft-stop attempt, then force-kill the process tree after a timeout |
| **Restart** | Stop and start again |
| **Logs** | View live output from the process |
| **Edit** | Change settings (takes effect immediately) |
| **Delete** | Remove from the list |

### Dashboard

Right-click tray → **Dashboard** to see a 7-day history for any process — uptime bar chart, run history grid, and a **Logs** tab (same window). **Restart** and **Logs** buttons in the stats bar restart the selected process or open the log viewer window. Use **↻ Refresh** for a full reload; background updates refresh the process list every 45 seconds only.

Tray crashes are written to **Event Viewer** (Application → **NexaRun-Tray**) and `%ProgramData%\NexaRun\logs\tray-crash.log`.

---

## CLI

The `nexarun` command is on your PATH after install. It talks to the **NexaRun Daemon** (Windows service). If the daemon is not running, commands fail with:

```text
Daemon is not running. Start it with: nexarun daemon start
```

**Data folder:** `%ProgramData%\NexaRun\` (`processes.json`, `history.json`, `logs\`, `settings.json`). Shared by the LocalSystem daemon, CLI, and tray. Legacy `%APPDATA%\NexaRun` and `%USERPROFILE%\.nexarun` are migrated automatically when ProgramData is empty.

**Dev usage:** `dotnet run --project NexaRun.Cli -- <command>`

### Command overview

```text
nexarun
├── start <id|name|all|script> Start saved process, all stopped, or new script
├── start-all              Start every stopped/errored process
├── import <file.json>     Import processes from JSON
├── stop <id|name|all>     Stop a process (or all)
├── restart <id|name|all>  Restart a process (or all)
├── restart-all            Restart every process
├── reload <id|name|all>   Graceful stop+start (brief downtime)
├── reset <id|name|all>    Zero restart counters
├── flush <id|name|all>    Clear log files
├── delete <id|name|all>   Remove from managed list (or all)
├── delete-all             Stop and remove every process
├── clear-all              Stop and clear every process
├── list [--json]          List all processes (shows id)
├── describe <id|name>     Full process details (JSON)
├── logs <id|name>         Show process logs
├── history <id|name>      Recent run history
├── save                   Persist process list / desired state
├── resurrect              Start all DesiredStatus=Online processes
├── version                Print CLI version
└── daemon
    ├── start              Start daemon (service or dev exe)
    ├── stop               Stop daemon service
    └── status             Service + pipe + data directory
```

### `nexarun start`

Start a **saved** process by id/name (`nexarun start 0`), or register and run a **new** script/executable. Paths and known extensions (`.js`, `.exe`, `.cmd`, …) register as new apps without requiring extra flags:

```powershell
nexarun start .\server.js
nexarun start <id|name|executable> [options] [-- app args...]
```


| Option | Description |
|--------|-------------|
| `--name <name>` | Process name (default: executable name) |
| `--args "<args>"` | Arguments for the executable |
| `--cwd <path>` | Working directory |
| `--no-autorestart` | Do not restart after crash |
| `--max-restarts <n>` | Max crash retries (default: **3**) |
| `--max-cpu <percent>` | Restart if CPU exceeds this % |
| `--max-memory <mb>` | Restart if memory exceeds this MB |
| `--log <path>` | Combined log file |
| `--out <path>` | Stdout log file |
| `--error <path>` | Stderr log file |
| `--time` | Timestamp each log line |
| `--env KEY=VALUE` | Environment variable (repeatable) |
| `--ready-port <n>` | Wait until TCP listen before Online (also after crash relaunch) |
| `--ready-file <path>` | Wait until file exists before Online (also after crash relaunch) |
| `--ready-timeout <ms>` | Ready-check timeout (default 30000) |
| `--restart-delay <ms>` | Minimum crash-restart delay (`max` with exponential backoff) |
| `--watch` / `--watch-path` | Restart on file changes (prefer a source dir; ignores `node_modules`, `.git`, `*.log`, process logs) |
| `--cron-restart "0 3 * * *"` | 5-field cron (local time); DOM+DOW both set → Vixie OR |

Script paths (`.js`, `.py`, `.ps1`, `.dll`) are launched via `node` / `python` / `pwsh` / `dotnet` automatically.

```powershell
nexarun start .\app.js --name worker
nexarun start .\app.js --name worker --env NODE_ENV=production
nexarun start .\api.exe --name api --ready-port 5000
nexarun start dotnet --name myapi --args "run --project ." --cwd "C:\Projects\MyApi"
nexarun start npm --name mysite --args "run start" --cwd "C:\Projects\my-site"
nexarun start node --name worker --args "server.js" --cwd "C:\Projects\worker"
nexarun start python --name bot --args "app.py" --cwd "C:\Projects\bot"
```


### `nexarun import`

Import processes from a JSON file or a PM2 `dump.pm2`. See [Process JSON](#process-json-nexarun-processesjson) below.

```powershell
nexarun import <file.json> [options]
nexarun import --from-pm2 <dump.pm2> [options]
```

| Option | Description |
|--------|-------------|
| `--from-pm2` | Treat `<file>` as a PM2 `dump.pm2` (path required) |
| `--only <name>` | Import only this app name |
| `--start` | Start processes after importing them |
| `--env-name <name>` | JSON only: merge `env_<name>` over base `env` (e.g. `production`) |

```powershell
nexarun import nexarun-processes.json
nexarun import C:\deploy\processes.json --only api --env-name production
nexarun import nexarun-processes.json --start
nexarun import C:\TEMP\dump.pm2
nexarun import --from-pm2 C:\TEMP\dump.pm2
nexarun import --from-pm2 C:\TEMP\dump.pm2 --only api
```

### `nexarun list` / `env`

```powershell
nexarun list
nexarun list --json
nexarun env myapi
nexarun env myapi --json
```

Shows id, name, status, pid, restarts, memory, and uptime. `--json` includes environment, ready fields, and `restartDelayMs`. `env` prints saved variables and may expose secrets.

### `nexarun stop` / `restart` / `reload` / `reset` / `flush` / `delete` / `start` (by id or name)

Like PM2, use the **id** from `nexarun list` or the process **name**:

```powershell
nexarun list
nexarun stop 0
nexarun stop all
nexarun restart 3
nexarun restart all
nexarun reload myapi
nexarun reset myapi
nexarun flush all
nexarun delete myapi
nexarun delete all
nexarun delete-all
nexarun start 0          # start a stopped saved process by id
nexarun start myapi      # start by name
nexarun start all        # start every stopped/errored process
nexarun start-all
nexarun describe myapi
nexarun save
nexarun resurrect
```

`reload` gracefully recycles a process (brief downtime — not PM2 cluster zero-downtime).

Saved processes remember **DesiredStatus**. User start → desired Online; user stop → desired Stopped. When the Windows service starts, NexaRun reattaches live PIDs and starts any remaining Desired Online apps (boot restore). `nexarun daemon start` alone does not invent desired state.

### `nexarun logs`

```powershell
nexarun logs <id|name> [options]
```

| Option | Default | Description |
|--------|---------|-------------|
| `--lines <n>` | `50` | Lines to show |
| `--out` | — | Stdout log only |
| `--err` | — | Stderr log only |
| `--follow` | — | Append new log bytes (Ctrl+C exits 0) |

```powershell
nexarun logs myapi
nexarun logs myapi --lines 200 --err
nexarun logs myapi --follow
```

Default log path: `%ProgramData%\NexaRun\logs\<name>.log`

### `nexarun daemon`

```powershell
nexarun daemon start
nexarun daemon stop
nexarun daemon status
```

Starts or stops the Windows service `NexaRunDaemon` (or the dev daemon exe when not installed). `status` reports service state, named-pipe reachability, and the data directory.

### Quick reference

| Command | Example |
|---------|---------|
| Start | `nexarun start .\app.js` or `nexarun start all` / `start-all` |
| Import | `nexarun import nexarun-processes.json` |
| List | `nexarun list` / `nexarun list --json` |
| Describe | `nexarun describe api` |
| Stop | `nexarun stop 0` / `nexarun stop all` |
| Restart / reload | `nexarun restart 0` / `nexarun reload myapi` |
| Reset / flush | `nexarun reset myapi` / `nexarun flush all` |
| Delete | `nexarun delete 0` / `nexarun delete all` / `delete-all` |
| Save / resurrect | `nexarun save` / `nexarun resurrect` |
| Logs | `nexarun logs 0 --follow` |
| Daemon | `nexarun daemon status` |

```powershell
nexarun --help
nexarun start --help
nexarun import --help
```

More detail: **[CLI-GUIDE.md](CLI-GUIDE.md)**

## Process JSON (`nexarun-processes.json`)

Import/export uses **JSON only** (`nexarun-processes.json`). Example in the repo root.

```json
{
  "version": 1,
  "apps": [
    {
      "name": "api",
      "script": "dotnet",
      "arguments": "run --project Api",
      "workingDirectory": "C:\\Projects\\MyApi",
      "autoRestart": true,
      "maxRestartAttempts": 3,
      "maxMemoryMb": 512,
      "outLogFile": "./logs/api-out.log",
      "errorLogFile": "./logs/api-err.log",
      "logFile": "./logs/api-combined.log",
      "logTimestamps": true,
      "environment": { "ASPNETCORE_ENVIRONMENT": "Development", "PATH": "C:\\\\Tools\\\\node;%PATH%" },
      "readyPort": 5000,
      "readyFile": null,
      "readyTimeoutMs": 30000
    }
  ]
}
```

| Field | Description |
|---|---|
| `name` | Process name |
| `script` | Executable or command |
| `arguments` | Command-line arguments |
| `workingDirectory` | Working folder |
| `autoRestart` | Restart on crash |
| `maxRestartAttempts` | Max crash retries (default 3) |
| `maxMemoryMb` / `maxCpuPercent` | Restart when exceeded |
| `outLogFile` / `errorLogFile` / `logFile` | Log paths |
| `logTimestamps` | Timestamp each log line |
| `environment` | Env vars (object). Include `PATH` to help the daemon find interpreters under LocalSystem |
| `readyPort` / `readyFile` / `readyTimeoutMs` | Optional readiness gate before Online (same as CLI `--ready-*`) |
| `restartDelayMs` | Optional minimum crash-restart delay (`max` with exponential backoff) |
| `interpreter` / `nodeArgs` | PM2-style: run `script` via interpreter (also aliases `node_args`) |
| `instances` | Expand to named clones (`name`, `name-1`, …) with `NODE_APP_INSTANCE` — **not** Node cluster / ZDT. Re-import with a smaller count does not prune leftover clones |
| `watch` / `watchPath` | Restart on file changes (default path: working directory). Prefer a source folder to avoid log/self-trigger loops |
| `cronRestart` | 5-field cron (local time). When both day-of-month and day-of-week are constrained, either may match (Vixie OR). Invalid expressions are rejected |
| `url` | Optional link opened via **Open URL** in the Processes window |

**PM2 ecosystem subset:** `args`, `cwd`, `env`, `env_*` (with `--env-name`), `max_restarts`, `autorestart`, log paths, `interpreter`, `node_args`, `restart_delay`, `instances` (named clones, not cluster), `watch`, `watch_path`, `cron_restart`. Unsupported: `exec_mode` cluster semantics, `.js` ecosystem evaluation, live `jlist`, per-user daemon.

**Import PM2 dump:** `nexarun import C:\TEMP\dump.pm2` maps `restart_delay` / `watch_path` / watch / cron and expands same-name cluster rows to `name` / `name-1` / … clones. No live `pm2 jlist`. Convert ecosystem `.js` to JSON or `dump.pm2` offline.

**Service identity:** Machine-wide for **System Admins** (LocalSystem by default). Not a per-user daemon — see [docs/SERVICE-ACCOUNT.md](docs/SERVICE-ACCOUNT.md). Live metrics: tray **Dashboard**, or `nexarun monit` (Ctrl+C to exit). `reload` is soft stop+start (brief downtime), not zero-downtime cluster reload.

---

## Process Logs

Process definitions and logs are stored under `%ProgramData%\NexaRun\` (`processes.json`, `history.json`, `logs\`). Legacy `%APPDATA%\NexaRun` and `%USERPROFILE%\.nexarun\` data is migrated automatically when ProgramData is empty.

By default, output is saved to `%ProgramData%\NexaRun\logs\<name>.log`. Configure log paths in the JSON file or with `--out` / `--error` / `--log` on `nexarun start`. If you set separate `outLogFile` and `errorLogFile`, NexaRun also writes daemon events to `<name>.log` in the same folder (not the same file as `outLogFile`). Log files rotate at 10 MB and keep up to **3** backups (`.log.1` … `.log.3`). Use `nexarun flush` to truncate logs and delete rotated files. Use `logTimestamps` or `--time` for timestamp prefixes.

Launched apps inherit the **daemon service environment** (LocalSystem / Session 0 by default). Prefer absolute paths, UNC shares (not mapped drive letters), and per-process `--env` / JSON `environment`. NexaRun is operated by System Admins on the host — not as a per-user tool. An admin may optionally set a dedicated **service** account (see [docs/SERVICE-ACCOUNT.md](docs/SERVICE-ACCOUNT.md)). After a daemon recycle, PIDs may reattach for monitoring; live stdout/stderr capture resumes only after a restart.

---

## Auto-Restart

Crashed processes with auto-restart enabled relaunch with exponential backoff (1s, 2s, 4s, 8s, 16s, then 30s max). During backoff the status is **Starting** (not Online with a null PID). By default NexaRun retries **3 times** (delays 1s/2s/4s); the 30s delay only appears with a higher `--max-restarts`. After that the process is marked **Errored**. Ready-port/file gates apply to crash relaunches the same as initial start. If a process stays up for 30 seconds, the restart counter resets. With `--no-autorestart`, an exit is recorded as **Errored** (auto-restart off) so status and alerts stay accurate.

User **stop** cancels any pending crash-restart backoff and ready wait. Stop tries a soft signal (CloseMainWindow / CTRL_BREAK when available), waits up to **8 seconds**, then force-kills the process tree and terminates the Job Object. Job Objects are not kill-on-close, so PIDs can survive a daemon recycle for reattach. Windowless redirected children often have no console — force kill is the reliable path on Windows.

If you set a **per-process** CPU or memory limit, NexaRun restarts that process when it exceeds its own limit (not a total across all apps). If it cannot recover, status becomes **Errored** with a reason in the Processes/Dashboard UI, `nexarun list`, and the process log.

In **Settings**, configure failed-process recovery (default every **10 minutes**, minimum 10) and optional **AWS SES email alerts** when a process goes from Online to Errored. Credentials are stored in `%ProgramData%\NexaRun\settings.json` (restrict ACLs on shared machines).

When recovery is enabled, the daemon tries **one** start for each **Errored** process per interval (crash restart counter resets for that attempt).

---

## Copyright

Copyright © Skybin Technology Private Limited. All rights reserved.
