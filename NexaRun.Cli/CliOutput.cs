using NexaRun.Shared.Models;
using Spectre.Console;

namespace NexaRun.Cli;

public static class CliOutput
{
    public static int Exit(IpcResponse response, bool quiet = false)
    {
        if (!quiet)
        {
            if (response.Success)
                AnsiConsole.MarkupLine($"[green]{Markup.Escape(response.Message)}[/]");
            else
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(response.Message)}[/]");
        }

        if (response.Success)
            return CliExitCodes.Success;

        var msg = response.Message ?? string.Empty;
        if (msg.Contains("Daemon is not running", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("IPC error", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("Daemon returned empty", StringComparison.OrdinalIgnoreCase))
            return CliExitCodes.DaemonUnreachable;

        if (msg.StartsWith("No process with id", StringComparison.Ordinal) ||
            msg.StartsWith("No process named", StringComparison.Ordinal) ||
            msg.Contains("Process id or name required", StringComparison.OrdinalIgnoreCase))
            return CliExitCodes.UnknownProcess;

        return CliExitCodes.Failure;
    }
}
