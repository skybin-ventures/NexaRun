using System.CommandLine;
using NexaRun.Cli;
using NexaRun.Shared.Ipc;

namespace NexaRun.Cli.Commands;

public static class ReloadCommand
{
    public static Command Build(IpcClient client)
    {
        var targetArg = CliCommands.TargetArgument(
            "reload",
            "or `all` — graceful stop+start (brief downtime; not PM2 cluster zero-downtime)");
        var cmd = new Command(
            "reload",
            "Gracefully recycle a process (soft stop then start). Not zero-downtime cluster reload.")
        {
            targetArg
        };

        cmd.SetAction(async parseResult =>
        {
            var target = parseResult.GetValue(targetArg)!;
            if (RestartAllRunner.IsAllTarget(target))
            {
                var response = await client.Send(CliCommands.TargetRequest("reload", "all"));
                return CliOutput.Exit(response);
            }

            var one = await client.Send(CliCommands.TargetRequest("reload", target));
            return CliOutput.Exit(one);
        });

        return cmd;
    }
}
