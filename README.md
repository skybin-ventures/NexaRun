# NexaRun

NexaRun is a process manager for Windows — keep your apps running forever. Start any process (Node.js, .NET, Python, anything), and NexaRun will monitor it, restart it if it crashes, and enforce CPU and memory limits. Manage everything from a clean system tray GUI or the command line.

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

To build the installer from source: run `installer\generate-icon.ps1` then `installer\build.ps1` (requires Inno Setup 6).

---

## Tray App

Click the NexaRun icon in the system tray to get started. Right-click for the menu.

### Add a Process

1. Right-click the tray icon → **Add Process**
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
| **Settings…** | Recovery interval, AWS SES email (API or SMTP), From/To addresses |
| **Add Process → Import JSON…** | Same import from the Add Process window |
| **Export JSON…** | Save definitions for another machine |
| **Processes window** | **Import JSON** / **Export JSON** toolbar buttons (same as tray) |
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
├── start <id|name|script> Start saved process or new script/executable
├── import <file.json>     Import processes from JSON
├── stop <id|name|all>     Stop a process (or all)
├── restart <id|name>      Restart a process
├── restart-all            Restart every process
├── delete <id|name|all>   Remove from managed list (or all)
├── clear-all              Stop and clear every process
├── list                   List all processes (shows id)
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

```powershell
nexarun start .\app.js --name worker
nexarun start dotnet --name myapi --args "run --project ." --cwd "C:\Projects\MyApi"
nexarun start npm --name mysite --args "run start" --cwd "C:\Projects\my-site"
nexarun start node --name worker --args "server.js" --cwd "C:\Projects\worker"
nexarun start python --name bot --args "app.py" --cwd "C:\Projects\bot"
```


### `nexarun import`

Import one or more processes from a JSON file. See [Process JSON](#process-json-nexarun-processesjson) below.

```powershell
nexarun import <file.json> [options]
```

| Option | Description |
|--------|-------------|
| `--only <name>` | Import only this app name |
| `--start` | Start processes after importing them |

```powershell
nexarun import nexarun-processes.json
nexarun import C:\deploy\processes.json --only api
nexarun import nexarun-processes.json --start
```

### `nexarun list`

```powershell
nexarun list
```

Shows id, name, status, pid, restarts, memory, and uptime.

### `nexarun stop` / `restart` / `delete` / `start` (by id or name)

Like PM2, use the **id** from `nexarun list` or the process **name**:

```powershell
nexarun list
nexarun stop 0
nexarun stop all
nexarun restart 3
nexarun delete myapi
nexarun delete all
nexarun start 0          # start a stopped saved process by id
nexarun start myapi      # start by name
nexarun describe myapi
nexarun save
nexarun resurrect
```

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
| Start | `nexarun start .\app.js` or `nexarun start dotnet --name api --args "run" --cwd C:\Api` |
| Import | `nexarun import nexarun-processes.json` |
| List | `nexarun list` |
| Describe | `nexarun describe api` |
| Stop | `nexarun stop 0` / `nexarun stop all` |
| Restart | `nexarun restart 0` |
| Delete | `nexarun delete 0` / `nexarun delete all` |
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
      "environment": { "ASPNETCORE_ENVIRONMENT": "Development" }
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
| `environment` | Env vars (object) |
| `url` | Optional link opened via **Open URL** in the Processes window |

**PM2 ecosystem subset:** when importing JSON you may also use `args`, `cwd`, `env`, `max_restarts`, `autorestart`, `error_file`, `out_file`, `log_file`. Unsupported: `instances`, `exec_mode`, `watch`, `cron_restart`, and `.js` ecosystem files.

---

## Process Logs

Process definitions and logs are stored under `%ProgramData%\NexaRun\` (`processes.json`, `history.json`, `logs\`). Legacy `%APPDATA%\NexaRun` and `%USERPROFILE%\.nexarun\` data is migrated automatically when ProgramData is empty.

By default, output is saved to `%ProgramData%\NexaRun\logs\<name>.log`. Configure log paths in the JSON file or with `--out` / `--error` / `--log` on `nexarun start`. If you set separate `outLogFile` and `errorLogFile`, NexaRun also writes daemon events to `<name>.log` in the same folder (not the same file as `outLogFile`). Log files rotate at 10 MB (`.log.1` backup). Use `logTimestamps` or `--time` for timestamp prefixes.

Launched apps inherit the **daemon service environment** (LocalSystem by default). Prefer absolute paths and per-process `environment` entries rather than relying on an interactive user profile or PATH.

---

## Auto-Restart

Crashed processes with auto-restart enabled relaunch with exponential backoff (1s, 2s, 4s, 8s, up to 30s max). By default NexaRun retries **3 times**; after that the process is marked **Errored**. Override with `--max-restarts`. If a process stays up for 30 seconds, the restart counter resets. With `--no-autorestart`, an exit is recorded as **Errored** (auto-restart off) so status and alerts stay accurate.

User **stop** cancels any pending crash-restart backoff. Stop tries a soft signal (CloseMainWindow / CTRL_BREAK when available), waits up to **8 seconds**, then force-kills the process tree. Windowless redirected children often have no console — force kill is the reliable path on Windows.

If you set a **per-process** CPU or memory limit, NexaRun restarts that process when it exceeds its own limit (not a total across all apps). If it cannot recover, status becomes **Errored** with a reason in the Processes/Dashboard UI, `nexarun list`, and the process log.

In **Settings**, configure failed-process recovery (default every **10 minutes**, minimum 10) and optional **AWS SES email alerts** when a process goes from Online to Errored. Credentials are stored in `%ProgramData%\NexaRun\settings.json` (restrict ACLs on shared machines).

When recovery is enabled, the daemon tries **one** start for each **Errored** process per interval (crash restart counter resets for that attempt).

---

## Copyright

Copyright © Skybin Technology Private Limited. All rights reserved.
