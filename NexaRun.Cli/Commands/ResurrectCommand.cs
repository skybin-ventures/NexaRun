using System.CommandLine;
using NexaRun.Cli;
using NexaRun.Shared.Ipc;
using NexaRun.Shared.Models;

namespace NexaRun.Cli.Commands;

public static class ResurrectCommand
{
    public static Command Build(IpcClient client)
    {
        var cmd = new Command("resurrect", "Start all processes with DesiredStatus Online");
        cmd.SetAction(async _ =>
        {
            var response = await client.Send(new IpcRequest { Command = "resurrect" });
            return CliOutput.Exit(response);
        });
        return cmd;
    }
}
