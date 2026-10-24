using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using NexaRun.Shared;
using NexaRun.Shared.Ipc;
using NexaRun.Shared.Models;

namespace NexaRun.Daemon;

public class IpcServer(ProcessManager processManager, ILogger<IpcServer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("IPC server listening on pipe '{Pipe}'", PipeConstants.PipeName);

        while (!stoppingToken.IsCancellationRequested)
        {
            var pipe = CreatePipe();

            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken);
                _ = HandleConnection(pipe, stoppingToken);
            }
            catch (OperationCanceledException) { pipe.Dispose(); break; }
            catch (Exception ex) { logger.LogError(ex, "IPC accept error"); pipe.Dispose(); }
        }
    }

    private static NamedPipeServerStream CreatePipe()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new NamedPipeServerStream(
                PipeConstants.PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
        }

        return NamedPipeServerStreamAcl.Create(
            PipeConstants.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: BuildPipeSecurity());
    }

    /// <summary>
    /// Build the pipe's PipeSecurity descriptor. The daemon runs as LocalSystem
    /// and exposes process-management and saved-secret (env) operations, so
    /// the pipe is granted only to LocalSystem (full control) and the local
    /// Administrators group (read/write + create new instance). No unprivileged
    /// principal (World, Authenticated Users, Users) is granted access. This is
    /// the primary access boundary; see docs/SERVICE-ACCOUNT.md.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static PipeSecurity BuildPipeSecurity()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));
        return security;
    }

    /// <summary>
    /// The single security gate: which commands a caller may run given whether
    /// the caller is privileged. Everything that touches process state, saved
    /// settings, logs, or environment is privileged-only; <c>daemon-info</c>
    /// is the only exception (the health probe used by <c>nexarun daemon
    /// status</c> — it returns reachability and the data directory, no process
    /// data or secrets).
    /// </summary>
    public static IpcResponse? CheckPrivilege(IpcRequest request, bool privileged)
    {
        if (privileged || request.Command == "daemon-info")
            return null; // allowed — continue to the command handler

        return new IpcResponse
        {
            Success = false,
            Message = "Access denied. NexaRun is a machine-wide System Admin service; run the CLI/tray elevated or connect as LocalSystem."
        };
    }

    /// <summary>
    /// Confirm the connected PIPE CLIENT is privileged (LocalSystem or a member
    /// of Administrators). This is defense in depth on top of the pipe ACL,
    /// which remains the primary access boundary.
    ///
    /// Impersonates the connected client via <c>ImpersonateNamedPipeClient</c>
    /// (advapi32.dll) so that <see cref="WindowsIdentity.GetCurrent"/> returns
    /// the client's token rather than the daemon's own LocalSystem token.
    /// <c>RevertImpersonation</c> (advapi32.dll) restores the daemon token in
    /// a finally block. Every failure path is fail-closed: any error returns
    /// <c>false</c> so privileged commands are denied rather than downgraded.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static bool IsPrivilegedClient(NamedPipeServerStream pipe)
    {
        if (!OperatingSystem.IsWindows())
            return true;

        var handle = pipe.SafePipeHandle.DangerousGetHandle();
        if (handle == IntPtr.Zero)
            return false;

        if (!ImpersonateNamedPipeClient(handle))
            return false;

        try
        {
            using var client = WindowsIdentity.GetCurrent();
            return IsPrivilegedGroups(client.Groups!);
        }
        catch
        {
            return false;
        }
        finally
        {
            RevertImpersonation();
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool IsPrivilegedGroups(IEnumerable<IdentityReference> groups) =>
        groups.Any(g => g.Value == WellKnownSidType.BuiltinAdministratorsSid.ToString()
                       || g.Value == WellKnownSidType.LocalSystemSid.ToString());

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool ImpersonateNamedPipeClient(IntPtr hNamedPipe);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool RevertImpersonation();

    private async Task HandleConnection(NamedPipeServerStream pipe, CancellationToken ct)
    {
        using (pipe)
        {
            try
            {
                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

                var line = await reader.ReadLineAsync(ct);
                if (line == null) return;

                var request = JsonSerializer.Deserialize<IpcRequest>(line);
                if (request == null) return;

                // Non-Windows dev builds have no named-pipe ACL, so the privilege
                // gate is a no-op there; on Windows every command except the
                // daemon-info health probe requires a privileged client. We
                // impersonate the connected client so the check reflects the
                // CLIENT's identity, not the daemon's own LocalSystem token.
                var privileged = OperatingSystem.IsWindows() ? IsPrivilegedClient(pipe) : true;
                var denied = IpcServer.CheckPrivilege(request, privileged);
                if (denied != null)
                {
                    logger.LogWarning(
                        "IPC request for '{Command}' rejected: caller lacks local admin / LocalSystem access",
                        request.Command);
                    var deniedJson = JsonSerializer.Serialize(denied);
                    await writer.WriteLineAsync(deniedJson.AsMemory(), ct);
                    return;
                }

                var response = await Dispatch(request);
                var responseJson = JsonSerializer.Serialize(response);
                await writer.WriteLineAsync(responseJson.AsMemory(), ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "IPC handler error");
            }
        }
    }

    private async Task<IpcResponse> Dispatch(IpcRequest request)
    {
        try
        {
            return request.Command switch
            {
                "start" when request.Options != null &&
                    !string.IsNullOrWhiteSpace(request.Options.ExecutablePath) => await HandleStart(request.Options),
                "start" when IsAllTarget(request.ProcessName) => await HandleStartAll(),
                "start-all" => await HandleStartAll(),
                "start" when !string.IsNullOrWhiteSpace(request.ProcessName) =>
                    await HandleStartExisting(request.ProcessName!, request.SettleAfterStart),
                "update" when request.Options != null => await HandleUpdate(request.Options),
                "stop" when IsAllTarget(request.ProcessName) => await HandleStopAll(),
                "stop" => await HandleStop(request.ProcessName),
                "restart" => await HandleRestart(request.ProcessName, request.SettleAfterStart),
                "restart-all" => await HandleRestartAll(),
                "reload" when IsAllTarget(request.ProcessName) => await HandleReloadAll(),
                "reload" => await HandleReload(request.ProcessName, request.SettleAfterStart),
                "reset" => await HandleReset(request.ProcessName),
                "delete" when IsAllTarget(request.ProcessName) => await HandleDeleteAll(),
                "delete-all" => await HandleDeleteAll(),
                "delete" => await HandleDelete(request.ProcessName),
                "clear-all" => await HandleClearAll(),
                "list" => await HandleList(),
                "describe" => await HandleDescribe(request.ProcessName),
                "logs" => await HandleLogs(request.ProcessName, request.LogLines ?? 50, request.LogStream),
                "clear-logs" => await HandleClearLogs(request.ProcessName),
                "flush" => await HandleClearLogs(request.ProcessName ?? "all"),
                "history" => await HandleHistory(request.ProcessName),
                "save" => await HandleSave(),
                "resurrect" => await HandleResurrect(),
                "daemon-info" => await HandleDaemonInfo(),
                "import" when request.BatchOptions != null => await HandleImport(request.BatchOptions, request.StartAfterImport),
                "get-settings" => await HandleGetSettings(),
                "set-settings" when request.Settings != null => await HandleSetSettings(request.Settings),
                _ => new IpcResponse { Success = false, Message = $"Unknown command: {request.Command}" }
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Dispatch error for command '{Command}'", request.Command);
            return new IpcResponse { Success = false, Message = ex.Message };
        }
    }

    private async Task<IpcResponse> HandleStart(StartOptions options)
    {
        var (success, message, process) = await processManager.Start(options);
        return new IpcResponse
        {
            Success = success,
            Message = message,
            Processes = process != null ? [process] : null
        };
    }

    private async Task<IpcResponse> HandleStartExisting(string target, bool settleAfterStart)
    {
        var (success, message, process) = await processManager.StartExisting(target, settleAfterStart);
        return new IpcResponse
        {
            Success = success,
            Message = message,
            Processes = process != null ? [process] : null
        };
    }

    private async Task<IpcResponse> HandleStartAll()
    {
        var (success, message) = await processManager.StartAllProcesses();
        var processes = await processManager.GetAll();
        return new IpcResponse { Success = success, Message = message, Processes = processes };
    }

    private static bool IsAllTarget(string? target) =>
        string.Equals(target, "all", StringComparison.OrdinalIgnoreCase);

    private async Task<IpcResponse> HandleStop(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return new IpcResponse { Success = false, Message = "Process id or name required." };
        var (success, message) = await processManager.Stop(target);
        return new IpcResponse { Success = success, Message = message };
    }

    private async Task<IpcResponse> HandleStopAll()
    {
        var (success, message) = await processManager.StopAll();
        return new IpcResponse { Success = success, Message = message };
    }

    private async Task<IpcResponse> HandleDeleteAll()
    {
        var (success, message) = await processManager.DeleteAll();
        return new IpcResponse { Success = success, Message = message };
    }

    private async Task<IpcResponse> HandleDescribe(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return new IpcResponse { Success = false, Message = "Process id or name required." };
        var (success, message, process) = await processManager.Describe(target);
        return new IpcResponse
        {
            Success = success,
            Message = message,
            Processes = process != null ? [process] : null
        };
    }

    private async Task<IpcResponse> HandleSave()
    {
        var (success, message) = await processManager.Save();
        return new IpcResponse { Success = success, Message = message, DataDir = NexaRunPaths.DataDir };
    }

    private async Task<IpcResponse> HandleResurrect()
    {
        var (success, message) = await processManager.Resurrect();
        return new IpcResponse { Success = success, Message = message };
    }

    private Task<IpcResponse> HandleDaemonInfo() =>
        Task.FromResult(new IpcResponse
        {
            Success = true,
            Message = "Daemon is reachable.",
            DataDir = NexaRunPaths.DataDir
        });

    private async Task<IpcResponse> HandleRestart(string? target, bool settleAfterStart)
    {
        if (string.IsNullOrWhiteSpace(target))
            return new IpcResponse { Success = false, Message = "Process id or name required." };
        var (success, message, process) = await processManager.Restart(target, settleAfterStart);
        return new IpcResponse
        {
            Success = success,
            Message = message,
            Processes = process != null ? [process] : null
        };
    }

    private async Task<IpcResponse> HandleRestartAll()
    {
        var (success, message) = await processManager.RestartAllProcesses();
        var processes = await processManager.GetAll();
        return new IpcResponse { Success = success, Message = message, Processes = processes };
    }

    private async Task<IpcResponse> HandleReload(string? target, bool settleAfterStart)
    {
        if (string.IsNullOrWhiteSpace(target))
            return new IpcResponse { Success = false, Message = "Process id or name required." };
        var (success, message, process) = await processManager.Reload(target, settleAfterStart);
        if (success && process != null)
            message = $"Process '{process.Name}' reloaded.";
        return new IpcResponse
        {
            Success = success,
            Message = message,
            Processes = process != null ? [process] : null
        };
    }

    private async Task<IpcResponse> HandleReloadAll()
    {
        var (success, message) = await processManager.RestartAllProcesses();
        if (success)
            message = message.Replace("Restart All", "Reload All", StringComparison.Ordinal);
        var processes = await processManager.GetAll();
        return new IpcResponse { Success = success, Message = message, Processes = processes };
    }

    private async Task<IpcResponse> HandleReset(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return new IpcResponse { Success = false, Message = "Process id or name (or all) required." };
        var (success, message, process) = await processManager.Reset(target);
        return new IpcResponse
        {
            Success = success,
            Message = message,
            Processes = process != null ? [process] : null
        };
    }

    private async Task<IpcResponse> HandleDelete(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return new IpcResponse { Success = false, Message = "Process id or name required." };
        var (success, message) = await processManager.Delete(target);
        return new IpcResponse { Success = success, Message = message };
    }

    private async Task<IpcResponse> HandleClearAll()
    {
        var (success, message, _) = await processManager.ClearAll();
        return new IpcResponse { Success = success, Message = message, Processes = [] };
    }

    private async Task<IpcResponse> HandleList()
    {
        var processes = await processManager.GetAll();
        return new IpcResponse { Success = true, Processes = processes };
    }

    private async Task<IpcResponse> HandleLogs(string? target, int lines, string? stream)
    {
        if (string.IsNullOrWhiteSpace(target))
            return new IpcResponse { Success = false, Message = "Process id or name required." };

        var logStream = stream?.ToLowerInvariant() switch
        {
            "out" => LogStream.Out,
            "err" or "error" => LogStream.Err,
            _ => LogStream.Combined
        };

        var (success, body, logPath) = await processManager.GetLogs(target, lines, logStream);
        return new IpcResponse
        {
            Success = success,
            Message = success ? string.Empty : body,
            Logs = success ? body : null,
            LogPath = logPath
        };
    }

    private async Task<IpcResponse> HandleClearLogs(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return new IpcResponse { Success = false, Message = "Process id or name required." };

        var (success, message) = await processManager.ClearLogs(target);
        return new IpcResponse { Success = success, Message = message };
    }

    private async Task<IpcResponse> HandleUpdate(StartOptions options)
    {
        // Stop if running, then re-start with new settings
        var all = await processManager.GetAll();
        var existing = all.FirstOrDefault(p => p.Name == options.Name);
        if (existing?.Status == ProcessStatus.Online)
            await processManager.Stop(existing.Id.ToString());

        var (success, message, process) = await processManager.Start(options);
        return new IpcResponse { Success = success, Message = message, Processes = process != null ? [process] : null };
    }

    private async Task<IpcResponse> HandleImport(List<StartOptions> options, bool start)
    {
        var (success, message) = await processManager.ImportBatch(options, start);
        return new IpcResponse { Success = success, Message = message };
    }

    private async Task<IpcResponse> HandleGetSettings()
    {
        var settings = await processManager.GetSettings();
        return new IpcResponse { Success = true, Settings = settings };
    }

    private async Task<IpcResponse> HandleSetSettings(NexaRun.Shared.Config.NexaRunSettings settings)
    {
        var (success, message) = await processManager.SaveSettings(settings);
        return new IpcResponse { Success = success, Message = message, Settings = settings };
    }

    private async Task<IpcResponse> HandleHistory(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return new IpcResponse { Success = false, Message = "Process id or name required." };
        var (success, message, history) = await processManager.GetHistory(target);
        return new IpcResponse { Success = success, Message = message, RunHistory = history };
    }
}
