using System.CommandLine;
using System.Text.Json;
using NexaRun.Cli;
using NexaRun.Shared.Ipc;
using Spectre.Console;

namespace NexaRun.Cli.Commands;

public static class EnvCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Command Build(IpcClient client)
    {
        var targetArg = CliCommands.TargetArgument("env");
        var jsonOpt = new Option<bool>("--json")
        {
            Description = "Emit environment as JSON (may include secrets)"
        };

        var cmd = new Command("env", "Show saved environment variables for a process (may include secrets)")
        {
            targetArg, jsonOpt
        };

        cmd.SetAction(async parseResult =>
        {
            var target = parseResult.GetValue(targetArg)!;
            var response = await client.Send(CliCommands.TargetRequest("describe", target));
            if (!response.Success)
                return CliOutput.Exit(response);

            var process = response.Processes?.FirstOrDefault();
            if (process == null)
            {
                AnsiConsole.MarkupLine("[red]No process data returned.[/]");
                return CliExitCodes.Failure;
            }

            var env = process.Environment;
            if (parseResult.GetValue(jsonOpt))
            {
                Console.WriteLine(JsonSerializer.Serialize(env ?? new Dictionary<string, string>(), JsonOptions));
                return CliExitCodes.Success;
            }

            if (env == null || env.Count == 0)
            {
                AnsiConsole.MarkupLine($"[grey]No environment variables saved for '{Markup.Escape(process.Name)}'.[/]");
                return CliExitCodes.Success;
            }

            foreach (var (key, value) in env.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                Console.WriteLine($"{key}={value}");

            return CliExitCodes.Success;
        });

        return cmd;
    }
}
