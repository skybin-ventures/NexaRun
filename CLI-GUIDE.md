# NexaRun CLI Guide

`nexarun` talks to the **NexaRun Daemon** (Windows service `NexaRunDaemon`) over a local named pipe.

## Prerequisites

| Requirement | Notes |
|-------------|--------|
| **Daemon running** | Installed service, or `nexarun daemon start` / `dotnet run --project NexaRun.Daemon` |
| **CLI on PATH** | After install: `nexarun`. Dev: `dotnet run --project NexaRun.Cli -- <command>` |
| **Data folder** | `%ProgramData%\NexaRun\` — `processes.json`, `history.json`, `logs\`, `settings.json` |

If the daemon is down:

```text
Daemon is not running. Start it with: nexarun daemon start
```

---

## Command overview

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
├── list [--json]          List all processes
├── monit                  Live-refreshing process table
├── describe <id|name>     Full process details (JSON)
├── env <id|name> [--json] Saved env vars (may include secrets)
├── logs <id|name>         Show process logs
├── history <id|name>      Recent run history
├── save                   Persist process list / desired state
├── resurrect              Start DesiredStatus=Online processes
├── version                Print CLI version
└── daemon
    ├── start              Start daemon
    ├── stop               Stop daemon
    └── status             Service + pipe + data dir
```

---

## `nexarun start`

```powershell
nexarun start <id|name|executable> [options] [-- app args...]
```

Resolution order:

1. Numeric id or exact saved name → start existing
2. Path-like target, existing file, or known extension (`.js`, `.exe`, `.cmd`, `.bat`, `.ps1`, …) → register and start new
3. Otherwise treat as saved name (error if missing)

| Option | Description |
|--------|-------------|
| `--name <name>` | Process name for a new executable |
| `--args "<args>"` | Arguments for the executable |
| `--cwd <path>` | Working directory |
| `--no-autorestart` | Do not restart after crash |
| `--max-restarts <n>` | Max crash retries (default: **3**) |
| `--max-cpu <percent>` | Restart if CPU exceeds this % |
| `--max-memory <mb>` | Restart if memory exceeds this MB |
| `--log` / `--out` / `--error` | Log paths |
| `--time` | Timestamp each log line |
| `--env KEY=VALUE` | Environment variable (repeatable) |
| `--ready-port <n>` | Wait until TCP `127.0.0.1:n` accepts before Online (also applies after crash relaunch) |
| `--ready-file <path>` | Wait until file exists before Online (also after crash relaunch) |
| `--ready-timeout <ms>` | Ready-check timeout (default **30000**) |
| `--restart-delay <ms>` | Minimum crash-restart delay (actual = max(exponential backoff, this)) |
| `--watch` / `--watch-path` | Restart when files change (default path: cwd). Prefer a source folder; ignores `node_modules`, `.git`, `*.log`, and configured log files (+ their rotated `.1`–`.3`). A relative `--watch-path` resolves against `--cwd`; if `--cwd` is blank it is skipped (a warning is logged) |
| `--cron-restart "m h dom mon dow"` | Scheduled restart (5-field cron, local time). When both day-of-month and day-of-week are set (neither `*`), either may match (Vixie OR) |

Script paths (`.js`, `.py`, `.ps1`, `.dll`, `.cmd`/`.bat`) are wrapped with the usual interpreter under the daemon (`node`, `python`, `pwsh`/`powershell`, `dotnet`, `cmd`). Prefer absolute interpreter paths or `--env PATH=...` when the LocalSystem PATH is incomplete.

```powershell
nexarun start .\server.js --name worker
nexarun start .\server.js --name worker --env NODE_ENV=production --env PORT=3000
nexarun start .\api.exe --name api --ready-port 5000
nexarun start 0
nexarun start myapi
nexarun start all
nexarun start-all
nexarun start dotnet --name api --args "run" --cwd C:\Api
```

`start all` / `start-all` starts every **stopped** or **errored** process one at a time (skips already online). Settle wait applies only after the last app.

---

## `nexarun import`

```powershell
nexarun import <file.json> [--only <name>] [--start] [--env-name production]
nexarun import C:\TEMP\dump.pm2 [--only <name>] [--start]
nexarun import --from-pm2 C:\TEMP\dump.pm2 [--only <name>] [--start]
```

| Option | Description |
|--------|-------------|
| `--from-pm2` | Treat `<file>` as a PM2 `dump.pm2` (path required). |
| `--only <name>` | Import only this app name (does not start unless `--start`) |
| `--start` | Start processes after importing |
| `--env-name <name>` | JSON only: merge `env_<name>` (e.g. `production` → `env_production`) over base `env` |

Default is register/update definitions only (stopped).

**PM2 dump only:** paths ending in `.pm2` expand same-name cluster rows to named clones (`api`, `api-1`, …). No live `pm2` / `jlist`. `--env-name` is ignored for dumps.

Native JSON fields include `instances` (named clones), `watch` / `watchPath`, `cronRestart`, ready/env/restartDelay, `interpreter` / `nodeArgs`.

| Capability | Native JSON | `dump.pm2` (`pm2_env`) |
|------------|-------------|-------------------------|
| Watch | `watch`, `watchPath` | `watch`, `watch_path` |
| Cron | `cronRestart` | `cron_restart` |
| Restart delay | `restartDelayMs` | `restart_delay` |
| Ready gate | `readyPort` / `readyFile` / `readyTimeoutMs` | not invented from PM2 `wait_ready` |
| Instances | `instances` → named clones | same-name rows → `name` / `name-1` / … |

PM2 ecosystem aliases: `args`, `cwd`, `env`, `env_*`, `max_restarts`, `autorestart`, log paths, `interpreter`, `node_args`, `restart_delay`, `instances`, `watch`, `cron_restart`. Not supported: `exec_mode` cluster semantics, ecosystem `.js` evaluation, live `jlist`, per-user daemon.

**Instances footgun:** re-importing a smaller `instances` count does **not** delete leftover `name-N` clones — remove them with `nexarun delete` if needed.

System Admin / LocalSystem (not per-user): see [docs/SERVICE-ACCOUNT.md](docs/SERVICE-ACCOUNT.md).

---

## `nexarun stop` / `restart` / `reload` / `delete`

Stop attempts a soft signal, waits up to **8 seconds**, then force-kills the process tree and terminates the per-app Job Object. Job Objects do **not** use kill-on-close, so a daemon recycle can still reattach surviving PIDs. Windows has no POSIX SIGTERM; windowless redirected children usually need the force-kill path.

Stop also cancels pending crash-restart backoff and any in-flight ready-port/file wait.

```powershell
nexarun stop myapi
nexarun stop all
nexarun restart 0
nexarun restart all
nexarun reload myapi
nexarun reload all
nexarun delete myapi
nexarun delete all
nexarun delete-all
```

`reload` is a graceful recycle (soft stop + start). It is **not** PM2 cluster zero-downtime reload — expect brief downtime on a single Windows process.

`delete all` / `delete-all` stops and removes every managed process (same effect as `clear-all`).

User stop sets **DesiredStatus=Stopped** and cancels any pending crash-restart backoff.

---

## `nexarun reset` / `flush`

```powershell
nexarun reset myapi      # zero restart counter (process keeps running)
nexarun reset all
nexarun flush myapi      # truncate logs + delete .1…\.N backups
nexarun flush all
```

---

## `nexarun list` / `describe` / `env` / `history`

```powershell
nexarun list
nexarun list --json
nexarun describe myapi
nexarun env myapi
nexarun env myapi --json
nexarun history myapi
```

`list --json` includes `environment`, ready fields, and `restartDelayMs` (same subset as describe for day-to-day ops). `env` prints saved `KEY=VALUE` lines and **may expose secrets** — avoid piping to shared logs.

---

## `nexarun monit`

```powershell
nexarun monit
```

Live-refreshes the process table every **2 seconds** (Spectre table). Press **Ctrl+C** to exit with code **0**. For CPU/memory history charts, use the tray **Dashboard** instead.

---

## `nexarun logs`

```powershell
nexarun logs <id|name> [--lines 50] [--out|--err] [--follow]
```

`--follow` appends new bytes from the **current** log file (handles truncate/rotate of that file; does not stream `.1` archives). Ctrl+C exits with code 0.

Default log path: `%ProgramData%\NexaRun\logs\<name>.log`

After a daemon recycle, NexaRun may **reattach** a live PID. Existing log files remain readable, but live stdout/stderr capture resumes only after a **restart** (Windows cannot reattach anonymous pipes).

---

## `nexarun save` / `resurrect`

```powershell
nexarun save        # write processes.json now
nexarun resurrect   # start all DesiredStatus=Online apps
```

DesiredStatus is also updated on user start/stop and restored when the daemon service boots (reattach live PIDs, then start remaining desired online apps). Starting the Windows service is not the same as inventing desired state.

---

## `nexarun daemon`

```powershell
nexarun daemon start
nexarun daemon stop
nexarun daemon status
```

---

## Auto-restart behavior

- Crash backoff: 1s → 2s → 4s → 8s → 16s → **30s** (capped). Status is **Starting** (not Online) during backoff. The 30s step only appears when `--max-restarts` is high enough (default **3** reaches 1s/2s/4s only). Optional `--restart-delay` / JSON `restartDelayMs` raises the floor (`max(backoff, delay)`). No extra settle stacked on crash restarts.
- Ready-port/file gates apply to crash relaunches the same as initial start.
- User restart settle defaults: **2s** after stop and **2s** after start (batch spacing).
- Default **3** crash retries; then **Errored**. `--max-restarts` is attempt count — not the same as the 30s backoff cap.
- `--no-autorestart`: exit → **Errored** (auto-restart off), history records Crashed.
- Counter resets after **30 seconds** of stable uptime, or via `nexarun reset`.

---

## System Admin / LocalSystem

NexaRun is a **machine-wide** service for **System Administrators** — not a per-user daemon. The Windows service runs as **LocalSystem** by default (Session 0). Children inherit the **service** environment — not an interactive profile, mapped drives (`Z:`), or HKCU. Prefer:

- Absolute paths for interpreters and working directories (or UNC shares)
- Per-process `--env` / JSON `environment` (include `PATH` so interpreter resolve finds `node`/`python` under the service)
- Tray **Add/Edit Process → Environment & readiness** for the same fields without CLI

Optional dedicated **service** account (admin-owned only): [docs/SERVICE-ACCOUNT.md](docs/SERVICE-ACCOUNT.md).

Tray **Add/Edit Process → Environment & readiness** also covers watch, watch path, cron restart, and restart delay (same fields as CLI/JSON).

Data lives under `%ProgramData%\NexaRun` (shared by daemon, CLI, and tray). The data root is secured to **LocalSystem + Administrators only** (the daemon's `NexaRunPaths.ApplyDataSecurity()` and the installer strip the legacy permissive Authenticated-Users ACE on every boot/install). See [docs/SERVICE-ACCOUNT.md](docs/SERVICE-ACCOUNT.md#data-directory-security-model).

### Elevation / who can reach the daemon

The daemon pipe is granted only to **LocalSystem** and the **Administrators** group, and every process-management / secret-reading command (`start`, `stop`, `delete`, `list`, `describe`, `env`, `save`, `set-settings`, …) is additionally verified in the handler by **impersonating the connected pipe client** (`ImpersonateNamedPipeClient`) and checking that client's token for Administrators/LocalSystem membership. Run the CLI or tray **elevated** (or as LocalSystem) to drive the daemon. The one carve-out is `nexarun daemon status`, which uses the `daemon-info` health probe and so works without elevation — it only reports reachability and the data directory and exposes no process data or secrets.

The tray app (`NexaRun.exe`) ships with a **requireAdministrator** manifest, so it always prompts UAC on launch. The Inno installer creates the all-users **Startup** and desktop shortcuts with the UAC `runasadmin` verb so the tray starts elevated automatically at each user's logon. Details: [docs/SERVICE-ACCOUNT.md](docs/SERVICE-ACCOUNT.md).

---

## Exit codes

| Code | Meaning |
|------|---------|
| `0` | Success |
| `1` | General failure |
| `2` | Daemon unreachable |
| `3` | Unknown process id/name |

---

## Help

```powershell
nexarun --help
nexarun start --help
```
