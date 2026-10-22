using System.Diagnostics;
using NexaRun.Shared;
using NexaRun.Shared.Ipc;
using NexaRun.Shared.Models;
using Spectre.Console;

namespace NexaRun.Cli;

public static class CliIpc
{
    public static Task<IpcResponse> Send(
        this IpcClient client,
        IpcRequest request,
        bool showProgress = true)
    {
        if (!showProgress)
            return client.Send(request);

        return RunWithProgress(StatusMessage(request), () => client.Send(request));
    }

    public static Task<T> RunWithProgress<T>(string statusMessage, Func<Task<T>> action)
    {
        return AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("yellow"))
            .StartAsync(statusMessage, async ctx =>
            {
                var sw = Stopwatch.StartNew();
                var pending = action();

                while (!pending.IsCompleted)
                {
                    ctx.Status(
                        Markup.Escape($"{statusMessage} ({sw.Elapsed.TotalSeconds:F0}s elapsed — still working)"));
                    await Task.Delay(250);
                }

                return await pending;
            });
    }

    public static string StatusMessage(IpcRequest request)
    {
        var settleSec = (ProcessDefaults.RestartSettleMs + ProcessDefaults.StartSettleMs) / 1000;
        var startSettleSec = ProcessDefaults.StartSettleMs / 1000;

        return request.Command switch
        {
            "start" when request.Options != null =>
                $"Starting '{request.Options.Name}' (up to {startSettleSec}s settle after launch)",
            "start" when IsAll(request.ProcessName) =>
                $"Starting all stopped processes (~{startSettleSec}s settle between apps)",
            "start-all" =>
                $"Starting all stopped processes (~{startSettleSec}s settle between apps)",
            "start" =>
                $"Starting process {request.ProcessName}",
            "stop" when IsAll(request.ProcessName) => "Stopping all running processes",
            "stop" => $"Stopping '{request.ProcessName}'",
            "restart" => RestartStatus(request.ProcessName!),
            "restart-all" =>
                $"Restarting all processes (~{settleSec}s per app — this can take several minutes)",
            "delete" when IsAll(request.ProcessName) => "Stopping and deleting all processes",
            "delete-all" => "Stopping and deleting all processes",
            "delete" => $"Deleting '{request.ProcessName}'",
            "clear-all" => "Stopping and clearing all processes",
            "import" when request.BatchOptions != null =>
                ImportStatus(request.BatchOptions.Count, request.StartAfterImport),
            "list" => "Loading managed processes",
            "logs" => $"Reading logs for '{request.ProcessName}'",
            _ => $"Running {request.Command}"
        };
    }

    public static string RestartStatus(string processName)
    {
        var stopSec = ProcessDefaults.RestartSettleMs / 1000;
        var startSec = ProcessDefaults.StartSettleMs / 1000;
        return
            $"Restarting '{processName}' (stop → wait {stopSec}s → start → wait {startSec}s)";
    }

    public static string RestartAllItemStatus(int index, int total, int id, string processName, bool isLast)
    {
        var stopSec = ProcessDefaults.RestartSettleMs / 1000;
        var startSec = ProcessDefaults.StartSettleMs / 1000;
        var settleNote = isLast
            ? $"wait {startSec}s after start"
            : "skip post-start wait until last app";
        return
            $"[{index}/{total}] id {id} '{processName}' (stop → {stopSec}s → start → {settleNote})";
    }

    public static string StartAllItemStatus(int index, int total, int id, string processName, bool isLast)
    {
        var startSec = ProcessDefaults.StartSettleMs / 1000;
        var settleNote = isLast
            ? $"wait {startSec}s after start"
            : "skip post-start wait until last app";
        return
            $"[{index}/{total}] id {id} '{processName}' (start → {settleNote})";
    }

    private static bool IsAll(string? target) =>
        string.Equals(target, "all", StringComparison.OrdinalIgnoreCase);

    public static string ImportStatus(int count, bool startAfterImport)
    {
        var startOnlySec = ProcessDefaults.StartSettleMs / 1000;
        return startAfterImport
            ? $"Importing {count} process(es) and starting (about {startOnlySec}s settle between each)"
            : $"Importing {count} process(es)";
    }
}
