using System.CommandLine;
using NexaRun.Cli;
using NexaRun.Shared.Ipc;

namespace NexaRun.Cli.Commands;

public static class StartAllCommand
{
    public static Command Build(IpcClient client)
    {
        var cmd = new Command("start-all", "Start every stopped/errored managed process (one at a time)");

        cmd.SetAction(async _ => await StartAllRunner.Run(client));

        return cmd;
    }
}
