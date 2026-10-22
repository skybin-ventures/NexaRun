using System.CommandLine;
using NexaRun.Cli;
using NexaRun.Cli.Display;
using NexaRun.Shared.Ipc;
using NexaRun.Shared.Models;
using Spectre.Console;

namespace NexaRun.Cli.Commands;

public static class MonitCommand
{
    public static Command Build(IpcClient client)
    {
        var cmd = new Command("monit", "Live process table (Ctrl+C to exit). Prefer tray Dashboard for charts.");

        cmd.SetAction(async _ =>
        {
            using var cts = new CancellationTokenSource();
            ConsoleCancelEventHandler? handler = null;
            handler = (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };
            Console.CancelKeyPress += handler;

            AnsiConsole.MarkupLine("[grey]Refreshing every 2s. Ctrl+C to stop. Tray Dashboard has history charts.[/]");
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    var response = await client.Send(new IpcRequest { Command = "list" }, showProgress: false);
                    if (!response.Success)
                    {
                        AnsiConsole.MarkupLine($"[red]{Markup.Escape(response.Message)}[/]");
                        return CliExitCodes.Failure;
                    }

                    AnsiConsole.Clear();
                    AnsiConsole.MarkupLine($"[grey]{Markup.Escape(DateTime.Now.ToString("HH:mm:ss"))} — nexarun monit[/]");
                    ProcessTable.Render(response.Processes ?? []);

                    try
                    {
                        await Task.Delay(2000, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                return CliExitCodes.Success;
            }
            catch (OperationCanceledException)
            {
                return CliExitCodes.Success;
            }
            finally
            {
                Console.CancelKeyPress -= handler;
            }
        });

        return cmd;
    }
}
