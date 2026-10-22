using System.CommandLine;
using NexaRun.Cli;
using NexaRun.Shared.Ipc;
using NexaRun.Shared.Models;

namespace NexaRun.Cli.Commands;

public static class ResetCommand
{
    public static Command Build(IpcClient client)
    {
        var targetArg = new Argument<string>("id|name|all")
        {
            Description = "Process id or name, or `all` to zero every restart counter"
        };

        var cmd = new Command("reset", "Reset restart counters without stopping the process")
        {
            targetArg
        };

        cmd.SetAction(async parseResult =>
        {
            var target = parseResult.GetValue(targetArg)!;
            var response = await client.Send(new IpcRequest
            {
                Command = "reset",
                ProcessName = target
            });
            return CliOutput.Exit(response);
        });

        return cmd;
    }
}
