using System.CommandLine;
using System.Text.Json;
using NexaRun.Cli;
using NexaRun.Shared.Ipc;
using Spectre.Console;

namespace NexaRun.Cli.Commands;

public static class DescribeCommand
{
    public static Command Build(IpcClient client)
    {
        var targetArg = CliCommands.TargetArgument("describe");
        var cmd = new Command("describe", "Show full details for a process") { targetArg };

        cmd.SetAction(async parseResult =>
        {
            var target = parseResult.GetValue(targetArg)!;
            var response = await client.Send(CliCommands.TargetRequest("describe", target));
            if (!response.Success)
                return CliOutput.Exit(response);

            var process = response.Processes?.FirstOrDefault();
            if (process == null)
            {
                AnsiConsole.MarkupLine("[red]No process data returned.[/]");
                return CliExitCodes.Failure;
            }

            var json = JsonSerializer.Serialize(process, new JsonSerializerOptions { WriteIndented = true });
            AnsiConsole.WriteLine(json);
            return CliExitCodes.Success;
        });

        return cmd;
    }
}
