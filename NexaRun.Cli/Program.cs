using System.CommandLine;
using NexaRun.Cli.Commands;
using NexaRun.Shared.Ipc;

var client = new IpcClient();

var root = new RootCommand("NexaRun — process manager for Windows")
{
    StartCommand.Build(client),
    StartAllCommand.Build(client),
    ImportCommand.Build(client),
    StopCommand.Build(client),
    RestartCommand.Build(client),
    RestartAllCommand.Build(client),
    ClearAllCommand.Build(client),
    DeleteCommand.Build(client),
    DeleteAllCommand.Build(client),
    ListCommand.Build(client),
    DescribeCommand.Build(client),
    LogsCommand.Build(client),
    HistoryCommand.Build(client),
    SaveCommand.Build(client),
    ResurrectCommand.Build(client),
    DaemonCommand.Build(client),
    VersionCommand.Build()
};

return root.Parse(args).Invoke();
