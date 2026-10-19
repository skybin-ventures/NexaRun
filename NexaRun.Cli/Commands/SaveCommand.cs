using System.CommandLine;
using NexaRun.Cli;
using NexaRun.Shared.Ipc;
using NexaRun.Shared.Models;

namespace NexaRun.Cli.Commands;

public static class SaveCommand
{
    public static Command Build(IpcClient client)
    {
        var cmd = new Command("save", "Persist the current process list (and desired online state)");
        cmd.SetAction(async _ =>
        {
            var response = await client.Send(new IpcRequest { Command = "save" });
            return CliOutput.Exit(response);
        });
        return cmd;
    }
}
