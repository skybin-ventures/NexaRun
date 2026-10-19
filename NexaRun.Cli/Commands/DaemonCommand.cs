using System.CommandLine;
using System.Diagnostics;
using NexaRun.Cli;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.ServiceProcess;
using NexaRun.Shared;
using NexaRun.Shared.Ipc;
using NexaRun.Shared.Models;
using Spectre.Console;

namespace NexaRun.Cli.Commands;

public static class DaemonCommand
{
    private const string ServiceName = "NexaRunDaemon";

    public static Command Build(IpcClient client)
    {
        var startCmd = new Command("start", "Start the NexaRun daemon");
        startCmd.SetAction(async _ => await StartDaemonAsync());

        var stopCmd = new Command("stop", "Stop the NexaRun daemon");
        stopCmd.SetAction(async _ => await StopDaemonAsync());

        var statusCmd = new Command("status", "Show daemon service and pipe status");
        statusCmd.SetAction(async _ => await StatusDaemonAsync(client));

        return new Command("daemon", "Control the NexaRun daemon service")
        {
            startCmd,
            stopCmd,
            statusCmd
        };
    }

    private static Task<int> StartDaemonAsync() =>
        CliIpc.RunWithProgress("Starting NexaRun daemon", () => Task.FromResult(StartDaemon()));

    private static Task<int> StopDaemonAsync() =>
        CliIpc.RunWithProgress("Stopping NexaRun daemon", () => Task.FromResult(StopDaemon()));

    private static async Task<int> StatusDaemonAsync(IpcClient client)
    {
        var serviceStatus = "not installed / unknown";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                using var sc = new ServiceController(ServiceName);
                serviceStatus = sc.Status.ToString();
            }
            catch (InvalidOperationException)
            {
                serviceStatus = "not installed";
            }
            catch (Exception ex)
            {
                serviceStatus = $"error: {ex.Message}";
            }
        }

        var pipe = await client.Send(new IpcRequest { Command = "daemon-info" }, showProgress: false);
        var reachable = pipe.Success;
        var dataDir = pipe.DataDir ?? NexaRunPaths.DataDir;

        AnsiConsole.WriteLine($"Service ({ServiceName}): {serviceStatus}");
        AnsiConsole.WriteLine($"Pipe reachable: {(reachable ? "yes" : "no")}");
        AnsiConsole.WriteLine($"Data directory: {dataDir}");
        if (!reachable && !string.IsNullOrWhiteSpace(pipe.Message))
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(pipe.Message)}[/]");

        return reachable ? CliExitCodes.Success : CliExitCodes.DaemonUnreachable;
    }

    private static int StartDaemon()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && TryServiceOperation(ServiceName, start: true, out var serviceMsg))
        {
            AnsiConsole.MarkupLine($"[green]{Markup.Escape(serviceMsg)}[/]");
            return 0;
        }

        var exe = FindDaemonExe();
        if (exe == null)
        {
            AnsiConsole.MarkupLine("[red]NexaRun.Daemon.exe not found. Install NexaRun or run from the solution build output.[/]");
            return 1;
        }

        Process.Start(new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        AnsiConsole.MarkupLine("[green]NexaRun daemon started.[/]");
        return 0;
    }

    private static int StopDaemon()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && TryServiceOperation(ServiceName, start: false, out var serviceMsg))
        {
            AnsiConsole.MarkupLine($"[green]{Markup.Escape(serviceMsg)}[/]");
            return 0;
        }

        AnsiConsole.MarkupLine("[red]Windows service not found. Stop the daemon process manually if running in dev mode.[/]");
        return 1;
    }

    [SupportedOSPlatform("windows")]
    private static bool TryServiceOperation(string serviceName, bool start, out string message)
    {
        message = string.Empty;
        try
        {
            using var sc = new ServiceController(serviceName);
            if (start)
            {
                if (sc.Status == ServiceControllerStatus.Running)
                {
                    message = "NexaRun daemon service is already running.";
                    return true;
                }

                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                message = "NexaRun daemon service started.";
                return true;
            }

            if (sc.Status == ServiceControllerStatus.Stopped)
            {
                message = "NexaRun daemon service is already stopped.";
                return true;
            }

            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            message = "NexaRun daemon service stopped.";
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }

    private static string? FindDaemonExe()
    {
        var cliDir = AppContext.BaseDirectory;
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "NexaRun.Daemon.exe" : "NexaRun.Daemon";

        var candidate = Path.Combine(cliDir, exeName);
        if (File.Exists(candidate)) return candidate;

        candidate = Path.Combine(cliDir, "..", exeName);
        candidate = Path.GetFullPath(candidate);
        if (File.Exists(candidate)) return candidate;

        candidate = Path.Combine(cliDir, "..", "..", "..", "..", "NexaRun.Daemon", "bin", "Debug", "net10.0", exeName);
        candidate = Path.GetFullPath(candidate);
        if (File.Exists(candidate)) return candidate;

        return null;
    }
}
