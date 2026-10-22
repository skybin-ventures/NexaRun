using System.CommandLine;
using System.Text.Json;
using NexaRun.Cli;
using NexaRun.Cli.Display;
using NexaRun.Shared.Ipc;
using NexaRun.Shared.Models;
using Spectre.Console;

namespace NexaRun.Cli.Commands;

public static class ListCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Command Build(IpcClient client)
    {
        var jsonOpt = new Option<bool>("--json")
        {
            Description = "Emit process list as JSON (PM2 jlist-style)"
        };

        var cmd = new Command("list", "List all managed processes and error reasons")
        {
            jsonOpt
        };

        cmd.SetAction(async parseResult =>
        {
            var response = await client.Send(new IpcRequest { Command = "list" }, showProgress: false);
            if (!response.Success)
            {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(response.Message)}[/]");
                return 1;
            }

            var processes = response.Processes ?? [];
            if (parseResult.GetValue(jsonOpt))
            {
                var payload = processes.Select(p => new
                {
                    id = p.Id,
                    name = p.Name,
                    pid = p.Pid,
                    status = p.Status.ToString(),
                    desiredStatus = p.DesiredStatus.ToString(),
                    statusReason = p.StatusReason,
                    restarts = p.Restarts,
                    maxRestartAttempts = p.MaxRestartAttempts,
                    restartDelayMs = p.RestartDelayMs,
                    cpu = p.CpuUsage,
                    memory = p.MemoryUsage,
                    startedAt = p.StartedAt,
                    executablePath = p.ExecutablePath,
                    arguments = p.Arguments,
                    workingDirectory = p.WorkingDirectory,
                    url = p.Url,
                    environment = p.Environment,
                    readyPort = p.ReadyPort,
                    readyFile = p.ReadyFile,
                    readyTimeoutMs = p.ReadyTimeoutMs,
                    watch = p.Watch,
                    watchPath = p.WatchPath,
                    cronRestart = p.CronRestart
                });
                Console.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
                return 0;
            }

            ProcessTable.Render(processes);
            return 0;
        });

        return cmd;
    }
}
