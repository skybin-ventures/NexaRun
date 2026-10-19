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
├── start <id|name|script> Start saved process or new script/executable
├── import <file.json>     Import processes from JSON
├── stop <id|name|all>     Stop a process (or all)
├── restart <id|name>      Restart a process
├── restart-all            Restart every process
├── delete <id|name|all>   Remove from managed list (or all)
├── clear-all              Stop and clear every process
├── list                   List all processes
├── describe <id|name>     Full process details (JSON)
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

```powershell
nexarun start .\server.js --name worker
nexarun start 0
nexarun start myapi
nexarun start dotnet --name api --args "run" --cwd C:\Api
```

---

## `nexarun import`

```powershell
nexarun import <file.json> [--only <name>] [--start]
```

| Option | Description |
|--------|-------------|
| `--only <name>` | Import only this app name (does not start unless `--start`) |
| `--start` | Start processes after importing |

Default is register/update definitions only (stopped). Online apps keep running until restarted — definitions update under them.

Native JSON fields: `name`, `script`, `arguments`, `workingDirectory`, `autoRestart`, `maxRestartAttempts`, log paths, `environment`, `url`.

PM2 ecosystem **subset** aliases also accepted: `args`, `cwd`, `env`, `max_restarts`, `autorestart`, `error_file`, `out_file`, `log_file`. Not supported: `instances`, `exec_mode`, `watch`, `cron_restart`, ecosystem `.js`.

---

## `nexarun stop` / `restart` / `delete`

Stop attempts a soft signal, waits up to **8 seconds**, then force-kills the process tree. Windows has no POSIX SIGTERM; windowless redirected children usually need the force-kill path.

```powershell
nexarun stop myapi
nexarun stop all
nexarun restart 0
nexarun delete myapi
nexarun delete all
```

User stop sets **DesiredStatus=Stopped** and cancels any pending crash-restart backoff.

---

## `nexarun list` / `describe` / `history`

```powershell
nexarun list
nexarun describe myapi
nexarun history myapi
```

---

## `nexarun logs`

```powershell
nexarun logs <id|name> [--lines 50] [--out|--err] [--follow]
```

`--follow` appends new bytes from the log file (handles rotation). Ctrl+C exits with code 0.

Default log path: `%ProgramData%\NexaRun\logs\<name>.log`

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

- Crash backoff: 1s → 2s → 4s → 8s → 16s (cap **30s**). No extra 30s settle stacked on crash restarts.
- User restart settle defaults: **2s** after stop and **2s** after start (batch spacing).
- Default **3** crash retries; then **Errored**.
- `--no-autorestart`: exit → **Errored** (auto-restart off), history records Crashed.
- Counter resets after **30 seconds** of stable uptime.

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
