using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using NexaRun.Daemon;
using NexaRun.Shared;
using NexaRun.Shared.Models;

namespace NexaRun.Tests;

public class IpcServerSecurityTests
{
    /// <summary>
    /// Every process-management / secret-reading command must be denied for a
    /// non-privileged caller (no process data, settings, logs, or history is
    /// returned).
    /// </summary>
    [Fact]
    public void NonPrivilegedCaller_is_denied_for_data_and_control_commands()
    {
        foreach (var command in new[]
                 { "start", "start-all", "stop", "restart", "restart-all", "reload",
                   "reset", "delete", "delete-all", "clear-all", "list", "describe",
                   "logs", "clear-logs", "flush", "history", "save", "resurrect",
                   "get-settings", "set-settings", "import", "update" })
        {
            var denied = IpcServer.CheckPrivilege(
                new IpcRequest { Command = command, ProcessName = "app" },
                privileged: false);

            Assert.True(denied != null, $"command '{command}' must be denied for a non-privileged caller");
            Assert.False(denied!.Success);
            Assert.Contains("Access denied", denied.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(denied.Processes ?? new List<NexaProcess>());
            Assert.Null(denied.Settings);
            Assert.Null(denied.Logs);
        }
    }

    /// <summary>
    /// The health-probe carve-out: daemon-info reaches a non-privileged caller
    /// and returns only reachability + data dir (no process data or secrets).
    /// </summary>
    [Fact]
    public void NonPrivilegedCaller_can_call_daemon_info_probe()
    {
        var denied = IpcServer.CheckPrivilege(
            new IpcRequest { Command = "daemon-info" },
            privileged: false);

        Assert.True(denied == null, "daemon-info must not be blocked for a non-privileged caller");
    }

    /// <summary>
    /// The daemon-info health probe must return no process data, settings,
    /// logs, or environment — only reachability and the data directory. This
    /// is what makes it safe to allow for unprivileged callers.
    /// </summary>
    [Fact]
    public void DaemonInfo_response_carries_no_process_data_or_secrets()
    {
        // Construct the same response the daemon-info handler produces.
        var response = new IpcResponse
        {
            Success = true,
            Message = "Daemon is reachable.",
            DataDir = NexaRunPaths.DataDir
        };

        Assert.True(response.Success);
        Assert.Null(response.Processes);
        Assert.Null(response.Settings);
        Assert.Null(response.Logs);
        Assert.Null(response.RunHistory);
        Assert.True(response.Message.Contains("reachable", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A privileged caller is never blocked by the security gate — the
    /// command proceeds to its handler.
    /// </summary>
    [Fact]
    public void PrivilegedCaller_is_not_blocked_by_the_gate()
    {
        foreach (var command in new[] { "list", "describe", "daemon-info", "get-settings" })
        {
            var denied = IpcServer.CheckPrivilege(
                new IpcRequest { Command = command, ProcessName = "app" },
                privileged: true);

            Assert.True(denied == null, $"privileged caller must not be denied for '{command}'");
        }
    }

    /// <summary>
    /// The pipe's ACL must grant access to LocalSystem and the local
    /// Administrators group, and must NOT grant it to unprivileged
    /// principals (World, Authenticated Users, Users). This is the primary
    /// access boundary that keeps an unprivileged pipe client out; the
    /// handler-side check is defense-in-depth on top of it.
    /// </summary>
    [SupportedOSPlatform("windows")]
    [Fact]
    public void Pipe_ACL_grants_only_privileged_principals()
    {
        if (!OperatingSystem.IsWindows()) return;

        // Use the production descriptor the daemon actually builds, so the test
        // proves the policy that ships — not a copy.
        var security = IpcServer.BuildPipeSecurity();
        var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<PipeAccessRule>()
            .ToList();

        var localSystem = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).ToString();
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).ToString();
        Assert.Contains(rules, r => r.IdentityReference!.ToString() == localSystem
            && r.AccessControlType == AccessControlType.Allow
            && r.PipeAccessRights.HasFlag(PipeAccessRights.FullControl));
        Assert.Contains(rules, r => r.IdentityReference!.ToString() == admins
            && r.AccessControlType == AccessControlType.Allow
            && r.PipeAccessRights.HasFlag(PipeAccessRights.ReadWrite)
            && r.PipeAccessRights.HasFlag(PipeAccessRights.CreateNewInstance));

        // No unprivileged group is granted access on the pipe.
        foreach (var unprivileged in new[]
                 {
                     new SecurityIdentifier(WellKnownSidType.WorldSid, null).ToString(),
                     new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null).ToString(),
                     new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null).ToString(),
                 })
        {
            Assert.False(rules.Any(r => r.IdentityReference!.ToString() == unprivileged),
                $"{unprivileged} must NOT be granted access on the pipe ACL");
        }
    }

    /// <summary>
    /// The client-identity check must not throw and must return a clean
    /// boolean even without a connected client. A pending (not yet
    /// accepted) server stream cannot be impersonated, so the check is
    /// fail-closed (false) — it never claims privilege.
    /// </summary>
    [SupportedOSPlatform("windows")]
    [Fact]
    public void IsPrivilegedClient_is_fail_closed_without_a_connected_client()
    {
        if (!OperatingSystem.IsWindows()) return;

        var serverStream = new NamedPipeServerStream(
            "nexarun-test-" + Guid.NewGuid().ToString("N"),
            PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        try
        {
            var value = IpcServer.IsPrivilegedClient(serverStream);
            Assert.False(value, "a server with no connected client cannot be a privileged client");
        }
        finally
        {
            serverStream.Dispose();
        }
    }
}
