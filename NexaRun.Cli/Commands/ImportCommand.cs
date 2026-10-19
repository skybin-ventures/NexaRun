using System.CommandLine;
using NexaRun.Shared.Config;
using NexaRun.Shared.Ipc;
using NexaRun.Shared.Models;
using Spectre.Console;

namespace NexaRun.Cli.Commands;

public static class ImportCommand
{
    public static Command Build(IpcClient client)
    {
        var fileArg = new Argument<string?>("file")
        {
            Description = "Path to nexarun-processes.json (or any NexaRun export .json). Omit when using --from-pm2.",
            Arity = ArgumentArity.ZeroOrOne
        };

        var fromPm2Opt = new Option<bool>("--from-pm2")
        {
            Description = "Import currently online PM2 apps (enriches from dump.pm2 when available)"
        };

        var onlyOpt = new Option<string?>("--only")
        {
            Description = "Import only this app name"
        };

        var startOpt = new Option<bool>("--start")
        {
            Description = "Start processes after importing them"
        };

        var cmd = new Command("import", "Import processes from a JSON file or from live PM2")
        {
            fileArg, fromPm2Opt, onlyOpt, startOpt
        };

        cmd.SetAction(async parseResult =>
        {
            var fromPm2 = parseResult.GetValue(fromPm2Opt);
            var file = parseResult.GetValue(fileArg);
            var only = parseResult.GetValue(onlyOpt);
            var start = parseResult.GetValue(startOpt);

            var hasFile = !string.IsNullOrWhiteSpace(file);
            if (fromPm2 == hasFile)
            {
                AnsiConsole.MarkupLine(
                    fromPm2
                        ? "[red]Specify either a JSON file or --from-pm2, not both.[/]"
                        : "[red]Specify a JSON file or --from-pm2.[/]");
                return 1;
            }

            try
            {
                List<StartOptions> options;
                string? clientSummary = null;

                if (fromPm2)
                {
                    var result = Pm2LiveImport.ReadOnline(only);
                    options = result.Options;
                    clientSummary = result.Summary;
                }
                else
                {
                    var path = Path.GetFullPath(file!);
                    if (!File.Exists(path))
                    {
                        AnsiConsole.MarkupLine($"[red]File not found: {Markup.Escape(path)}[/]");
                        return 1;
                    }

                    options = ProcessConfigTransfer.FromImportFile(path, only);
                }

                var response = await client.Send(new IpcRequest
                {
                    Command = "import",
                    BatchOptions = options,
                    StartAfterImport = start
                });

                if (response.Success)
                {
                    if (!string.IsNullOrWhiteSpace(clientSummary))
                        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(clientSummary)}[/]");
                    AnsiConsole.MarkupLine($"[green]{Markup.Escape(response.Message)}[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[red]{Markup.Escape(response.Message)}[/]");
                }

                return response.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
                return 1;
            }
        });

        return cmd;
    }
}
