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
            Description = "Path to nexarun-processes.json, or a PM2 dump.pm2 file.",
            Arity = ArgumentArity.ZeroOrOne
        };

        var fromPm2Opt = new Option<bool>("--from-pm2")
        {
            Description = "Treat <file> as a PM2 dump.pm2 (required with this flag)."
        };

        var onlyOpt = new Option<string?>("--only")
        {
            Description = "Import only this app name"
        };

        var startOpt = new Option<bool>("--start")
        {
            Description = "Start processes after importing them"
        };

        var envNameOpt = new Option<string?>("--env-name")
        {
            Description = "JSON only: merge env_<name> (e.g. production → env_production) over base env"
        };

        var cmd = new Command("import", "Import processes from a JSON file or PM2 dump.pm2")
        {
            fileArg, fromPm2Opt, onlyOpt, startOpt, envNameOpt
        };

        cmd.SetAction(async parseResult =>
        {
            var fromPm2 = parseResult.GetValue(fromPm2Opt);
            var file = parseResult.GetValue(fileArg);
            var only = parseResult.GetValue(onlyOpt);
            var start = parseResult.GetValue(startOpt);
            var envName = parseResult.GetValue(envNameOpt);

            var hasFile = !string.IsNullOrWhiteSpace(file);
            if (!hasFile)
            {
                AnsiConsole.MarkupLine(
                    fromPm2
                        ? "[red]Specify a dump.pm2 path: nexarun import --from-pm2 C:\\TEMP\\dump.pm2[/]"
                        : "[red]Specify a JSON or dump.pm2 file.[/]");
                return 1;
            }

            var isDumpFile = fromPm2 || Pm2LiveImport.LooksLikeDumpPath(file!);
            if (fromPm2 && !Pm2LiveImport.LooksLikeDumpPath(file!))
            {
                AnsiConsole.MarkupLine(
                    "[red]With --from-pm2, <file> must be a dump.pm2 path.[/]");
                return 1;
            }

            try
            {
                List<StartOptions> options;
                string? clientSummary = null;

                if (isDumpFile)
                {
                    if (!string.IsNullOrWhiteSpace(envName))
                    {
                        AnsiConsole.MarkupLine(
                            "[yellow]--env-name applies to JSON ecosystem imports (env_production). dump.pm2 already has merged env; ignoring.[/]");
                    }

                    var result = Pm2LiveImport.ReadDumpFile(file!, only);
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

                    options = ProcessConfigTransfer.FromImportFile(path, only, envName);
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
