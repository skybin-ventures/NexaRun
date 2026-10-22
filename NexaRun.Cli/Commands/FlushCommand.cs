using System.CommandLine;
using NexaRun.Cli;
using NexaRun.Shared.Ipc;
using NexaRun.Shared.Models;

namespace NexaRun.Cli.Commands;

public static class FlushCommand
{
    public static Command Build(IpcClient client)
    {
        var targetArg = new Argument<string>("id|name|all")
        {
            Description = "Process id or name, or `all` to clear every process log (PM2 flush)"
        };

        var cmd = new Command("flush", "Clear process log files (truncate + delete rotated backups)")
        {
            targetArg
        };

        cmd.SetAction(async parseResult =>
        {
            var target = parseResult.GetValue(targetArg)!;
            var response = await client.Send(new IpcRequest
            {
                Command = "flush",
                ProcessName = target
            });
            return CliOutput.Exit(response);
        });

        return cmd;
    }
}
