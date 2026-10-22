# NexaRun service identity (System Admin)

NexaRun is a **machine-wide** process manager for **System Administrators**. It is **not** a per-user daemon (there is no PM2-style `~/.pm2` per login, and no plan to add one).

- One Windows service: **NexaRunDaemon**
- One data root: `%ProgramData%\NexaRun` (shared by service, elevated CLI, and tray)
- Intended operators: admins who install the service and manage apps for the host

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
