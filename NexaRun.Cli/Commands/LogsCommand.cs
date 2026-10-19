using System.CommandLine;
using NexaRun.Cli;
using NexaRun.Shared.Ipc;
using NexaRun.Shared.Models;
using Spectre.Console;

namespace NexaRun.Cli.Commands;

public static class LogsCommand
{
    public static Command Build(IpcClient client)
    {
        var targetArg = CliCommands.TargetArgument("logs");

        var linesOpt = new Option<int>("--lines")
        {
            Description = "Number of log lines to show",
            DefaultValueFactory = _ => 50
        };

        var outOpt = new Option<bool>("--out")
        {
            Description = "Show stdout log only (outLogFile)"
        };

        var errOpt = new Option<bool>("--err")
        {
            Description = "Show stderr log only (errorLogFile)"
        };

        var followOpt = new Option<bool>("--follow")
        {
            Description = "Stream new log bytes continuously (Ctrl+C to exit)"
        };

        var cmd = new Command("logs", "Show process logs by id or name") { targetArg, linesOpt, outOpt, errOpt, followOpt };

        cmd.SetAction(async parseResult =>
        {
            var target = parseResult.GetValue(targetArg)!;
            var lines = parseResult.GetValue(linesOpt);
            var stream = parseResult.GetValue(errOpt) ? "err"
                : parseResult.GetValue(outOpt) ? "out"
                : null;

            if (parseResult.GetValue(followOpt))
                return await FollowLogs(client, target, lines, stream);

            return await PrintLogs(client, target, lines, stream);
        });

        return cmd;
    }

    private static async Task<int> PrintLogs(IpcClient client, string target, int lines, string? stream)
    {
        var response = await client.Send(new IpcRequest
        {
            Command = "logs",
            ProcessName = target,
            LogLines = lines,
            LogStream = stream
        });

        if (!response.Success)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(response.Message)}[/]");
            return CliExitCodes.Failure;
        }

        AnsiConsole.WriteLine(response.Logs ?? string.Empty);
        return CliExitCodes.Success;
    }

    private static async Task<int> FollowLogs(IpcClient client, string target, int lines, string? stream)
    {
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        Console.CancelKeyPress += handler;

        try
        {
            var response = await client.Send(new IpcRequest
            {
                Command = "logs",
                ProcessName = target,
                LogLines = lines,
                LogStream = stream
            });

            if (!response.Success)
            {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(response.Message)}[/]");
                return CliExitCodes.Failure;
            }

            var path = response.LogPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                AnsiConsole.WriteLine(response.Logs ?? "(no logs yet)");
                AnsiConsole.MarkupLine("[yellow]Waiting for log file...[/]");
            }
            else
            {
                // Print the initial tail, then stream by byte offset.
                AnsiConsole.WriteLine(response.Logs ?? string.Empty);
            }

            long offset = 0;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                offset = new FileInfo(path).Length;
            }

            while (!cts.IsCancellationRequested)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    // Refresh path from daemon periodically until the file appears.
                    var meta = await client.Send(new IpcRequest
                    {
                        Command = "logs",
                        ProcessName = target,
                        LogLines = 1,
                        LogStream = stream
                    }, showProgress: false);

                    if (!meta.Success)
                    {
                        AnsiConsole.MarkupLine($"[red]{Markup.Escape(meta.Message)}[/]");
                        return CliExitCodes.Failure;
                    }

                    path = meta.LogPath;
                    await Task.Delay(1000, cts.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                    continue;
                }

                var info = new FileInfo(path);
                if (info.Length < offset)
                    offset = 0; // rotated or truncated

                if (info.Length > offset)
                {
                    await using var fs = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite);
                    fs.Seek(offset, SeekOrigin.Begin);
                    using var reader = new StreamReader(fs);
                    var chunk = await reader.ReadToEndAsync(cts.Token);
                    if (!string.IsNullOrEmpty(chunk))
                    {
                        Console.Write(chunk);
                        if (!chunk.EndsWith('\n') && !chunk.EndsWith('\r'))
                            Console.WriteLine();
                    }

                    offset = fs.Position;
                }

                await Task.Delay(500, cts.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
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
    }
}
