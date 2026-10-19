using System.CommandLine;
using NexaRun.Cli;
using NexaRun.Shared.Ipc;

namespace NexaRun.Cli.Commands;

public static class StopCommand
{
    public static Command Build(IpcClient client)
    {
        var targetArg = new Argument<string>("id|name|all")
        {
            Description = "Process id, name, or 'all'"
        };
        var cmd = new Command("stop", "Stop a running process by id or name (or all)") { targetArg };

        cmd.SetAction(async parseResult =>
        {
            var target = parseResult.GetValue(targetArg)!;
            var response = await client.Send(CliCommands.TargetRequest("stop", target));
            return CliOutput.Exit(response);
        });

        return cmd;
    }
}
