# NexaRun service identity (System Admin)

NexaRun is a **machine-wide** process manager for **System Administrators**. It is **not** a per-user daemon (there is no PM2-style `~/.pm2` per login, and no plan to add one).

- One Windows service: **NexaRunDaemon**
- One data root: `%ProgramData%\NexaRun` (shared by service, elevated CLI, and tray)
- Intended operators: admins who install the service and manage apps for the host

## Who can reach the daemon (pipe security)

The named pipe is granted **only to LocalSystem and the Administrators group** — never to unprivileged local users. Every process-management and secret-reading IPC command (`start`, `stop`, `delete`, `list`, `describe`, `env`, `save`, `set-settings`, …) is additionally **verified in the daemon handler by impersonating the connected pipe client** and checking its token for Administrators/LocalSystem membership. This is defense in depth: a named-pipe ACL alone is not the reliable control for a LocalSystem daemon that can control LocalSystem-launched processes and read saved environment variables.

Practical effect:

- Run the CLI and tray **elevated** (or as LocalSystem) to drive the daemon.
- `nexarun daemon status` is the one exception: it uses the `daemon-info` health probe and works without elevation, but it only reports reachability and the data directory — no process data or secrets.
- Unprivileged local users cannot start/stop/delete managed processes or read saved `env` values through the daemon.

## Data-directory security model

`%ProgramData%\NexaRun` (data root, `processes.json`, `settings.json`, and `logs\`) is secured to **LocalSystem + Administrators only** — no broad Authenticated-Users / Users / World write access. The daemon's `NexaRunPaths.ApplyDataSecurity()` runs at startup as LocalSystem and rewrites the data-root and log-directory DACLs to strip any stale permissive ACEs. The Inno installer applies the same model at install/upgrade time, so existing installations are also secured.

Optional dedicated **service** account: if the service's **Log on as** is set to a non-LocalSystem account, that account must also be granted **Modify** on `%ProgramData%\NexaRun` — the ACL model is LocalSystem + Administrators, and a custom service identity is not auto-detected. Grant it manually in the Advanced Security Settings, or keep the default LocalSystem.

## Tray elevation

The tray app (`NexaRun.exe`) ships with a **`requireAdministrator`** manifest. This is the
intended behavior: the daemon pipe is admin-only, so the tray must run elevated to control it.

Consequences:

- **Startup shortcut** (all-users, `C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Startup`):
  the installer creates it with the UAC **runasadmin** verb, so it launches elevated at every
  user's logon. A UAC prompt appears for each user at logon — the standard, expected tradeoff for
  a machine-wide elevated app.
- **Manual launch** (Explorer or Start menu): a UAC prompt appears because of the manifest.
  This is correct and not a bug — the tray *cannot* reach the daemon unelevated.
- If a user deliberately does not want the UAC prompt at every logon, they can uncheck the
  **Start at Windows login** task on reinstall; they lose machine-wide auto-start and must
  launch the tray manually (still elevated when they want it).

## Default: LocalSystem

Keep **LocalSystem** (Session 0) unless you have a clear reason to change it. Children inherit the **service** environment — not an interactive admin’s profile, mapped drives (`Z:`), or HKCU.

Configure apps with:

- Absolute paths (or UNC) for interpreters and working directories
- Per-process `--env` / JSON `environment` (include `PATH` when needed)
- Machine-wide tool installs under Program Files, not per-user `nvm`/profile paths

## Optional: dedicated service account (admin-owned)

If apps need network credentials or a locked-down identity that is still **admin-provisioned** (not each developer’s interactive login), set the service **Log on as** a dedicated domain/local **service** account:

1. **services.msc** → **NexaRunDaemon** → **Properties** → **Log On** → **This account**
2. Grant that account **Log on as a service** and **Modify** on `%ProgramData%\NexaRun`
3. Restart the service (`nexarun daemon stop` / `start` or Services → Restart)

Do **not** point the service at a day-to-day user profile to “make PATH work” — use absolute paths and per-app env instead.

## Out of scope

- Per-user / per-login daemons
- `CreateProcessAsUser` per app (all children share the service identity)
- Interactive desktop apps in Session 0
