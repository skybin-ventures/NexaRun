using NexaRun.Cli.Display;
using NexaRun.Shared.Ipc;
using NexaRun.Shared.Models;
using Spectre.Console;

namespace NexaRun.Cli;

public static class StartAllRunner
{
    public static bool IsAllTarget(string target) =>
        target.Equals("all", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> Run(IpcClient client)
    {
        var listResponse = await client.Send(new IpcRequest { Command = "list" }, showProgress: false);
        if (!listResponse.Success)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(listResponse.Message)}[/]");
            return 1;
        }

        var processes = listResponse.Processes?
            .Where(p => p.Status is not (ProcessStatus.Online or ProcessStatus.Starting))
            .OrderBy(p => p.Id)
            .ToList() ?? [];

        if (processes.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]No stopped processes to start.[/]");
            return 0;
        }

        var succeeded = 0;
        var failed = 0;

        await AnsiConsole.Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(
            [
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn()
            ])
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[yellow]Start all[/]", maxValue: processes.Count);

                for (var i = 0; i < processes.Count; i++)
                {
                    var proc = processes[i];
                    var index = i + 1;
                    var isLast = i == processes.Count - 1;
                    task.Description = Markup.Escape(
                        CliIpc.StartAllItemStatus(index, processes.Count, proc.Id, proc.Name, isLast));

                    var response = await client.Send(
                        new IpcRequest
                        {
                            Command = "start",
                            ProcessName = proc.Id.ToString(),
                            SettleAfterStart = isLast
                        },
                        showProgress: false);

                    if (response.Success)
                        succeeded++;
                    else
                    {
                        failed++;
                        AnsiConsole.MarkupLine(
                            $"[red]  {index}/{processes.Count} [{proc.Id}] '{Markup.Escape(proc.Name)}': {Markup.Escape(response.Message)}[/]");
                    }

                    task.Increment(1);
                }
            });

        var summary =
            $"Start All finished: {succeeded} succeeded, {failed} failed (of {processes.Count}).";
        if (failed == 0)
            AnsiConsole.MarkupLine($"[green]{Markup.Escape(summary)}[/]");
        else
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(summary)}[/]");

        var finalList = await client.Send(new IpcRequest { Command = "list" }, showProgress: false);
        if (finalList.Processes is { Count: > 0 })
            ProcessTable.Render(finalList.Processes);

        return failed == processes.Count ? 1 : 0;
    }
}
