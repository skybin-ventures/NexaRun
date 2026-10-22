using System.CommandLine;
using NexaRun.Cli;
using NexaRun.Shared.Ipc;
using NexaRun.Shared.Models;

namespace NexaRun.Cli.Commands;

public static class DeleteAllCommand
{
    public static Command Build(IpcClient client)
    {
        var cmd = new Command("delete-all", "Stop and remove every managed process");

        cmd.SetAction(async _ =>
        {
            var response = await client.Send(new IpcRequest { Command = "delete-all" });
            return CliOutput.Exit(response);
        });

        return cmd;
    }
}
