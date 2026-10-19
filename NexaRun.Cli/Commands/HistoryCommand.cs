using System.CommandLine;
using NexaRun.Cli;
using NexaRun.Shared.Ipc;
using Spectre.Console;

namespace NexaRun.Cli.Commands;

public static class HistoryCommand
{
    public static Command Build(IpcClient client)
    {
        var targetArg = CliCommands.TargetArgument("history");
        var cmd = new Command("history", "Show recent run history for a process") { targetArg };

        cmd.SetAction(async parseResult =>
        {
            var target = parseResult.GetValue(targetArg)!;
            var response = await client.Send(CliCommands.TargetRequest("history", target));
            if (!response.Success)
                return CliOutput.Exit(response);

            var runs = response.RunHistory ?? [];
            if (runs.Count == 0)
            {
                AnsiConsole.MarkupLine("[grey]No run history in the last 7 days.[/]");
                return CliExitCodes.Success;
            }

            var table = new Table();
            table.AddColumn("Started (UTC)");
            table.AddColumn("Ended (UTC)");
            table.AddColumn("Outcome");
            table.AddColumn("Exit");
            foreach (var r in runs)
            {
                table.AddRow(
                    r.StartedAt.ToString("u"),
                    r.EndedAt?.ToString("u") ?? "-",
                    r.Outcome.ToString(),
                    r.ExitCode?.ToString() ?? "-");
            }

            AnsiConsole.Write(table);
            return CliExitCodes.Success;
        });

        return cmd;
    }
}
