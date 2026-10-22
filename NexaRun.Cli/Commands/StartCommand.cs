using System.CommandLine;
using NexaRun.Cli;
using NexaRun.Shared;
using NexaRun.Shared.Ipc;
using NexaRun.Shared.Models;

namespace NexaRun.Cli.Commands;

public static class StartCommand
{
    public static Command Build(IpcClient client)
    {
        var targetArg = new Argument<string>("target")
        {
            Description = "Process id/name, `all` (start every stopped app), or script/executable path (register and start)"
        };

        var nameOpt = new Option<string?>("--name")
        {
            Description = "Process name when starting a new executable (defaults to executable name)"
        };

        var argsOpt = new Option<string?>("--args")
        {
            Description = "Arguments passed to the executable"
        };

        var cwdOpt = new Option<string?>("--cwd")
        {
            Description = "Working directory"
        };

        var noRestartOpt = new Option<bool>("--no-autorestart")
        {
            Description = "Disable auto-restart on crash"
        };

        var maxRestartsOpt = new Option<int?>("--max-restarts")
        {
            Description = "Max automatic restart attempts after crash (default: 3)"
        };

        var maxCpuOpt = new Option<double?>("--max-cpu")
        {
            Description = "Restart when CPU usage exceeds this percent"
        };

        var maxMemOpt = new Option<long?>("--max-memory")
        {
            Description = "Restart when memory exceeds this value in MB"
        };

        var outLogOpt = new Option<string?>("--out")
        {
            Description = "Stdout log file path"
        };

        var errLogOpt = new Option<string?>("--error")
        {
            Description = "Stderr log file path"
        };

        var logOpt = new Option<string?>("--log")
        {
            Description = "Combined log file path"
        };

        var timeOpt = new Option<bool>("--time")
        {
            Description = "Prefix log lines with timestamps"
        };

        var forwardArgs = new Argument<string[]>("appArgs")
        {
            Arity = ArgumentArity.ZeroOrMore,
            Description = "Extra arguments forwarded to the executable (after --)"
        };

        var cmd = new Command("start", "Start a process by id/name/all (PM2-style) or register a new script/executable")
        {
            targetArg, nameOpt, argsOpt, cwdOpt, noRestartOpt,
            maxRestartsOpt, maxCpuOpt, maxMemOpt, outLogOpt, errLogOpt, logOpt, timeOpt, forwardArgs
        };

        cmd.SetAction(async parseResult =>
        {
            var target = parseResult.GetValue(targetArg)!;
            if (StartAllRunner.IsAllTarget(target))
                return await StartAllRunner.Run(client);

            var forwarded = parseResult.GetValue(forwardArgs) ?? [];
            var hasOptions = WantsNewExecutable(parseResult, argsOpt, cwdOpt, nameOpt, outLogOpt, errLogOpt, logOpt,
                noRestartOpt, maxRestartsOpt, maxCpuOpt, maxMemOpt, timeOpt) || forwarded.Length > 0;

            // Ask daemon for current names when deciding existing vs new (best-effort).
            var list = await client.Send(new IpcRequest { Command = "list" }, showProgress: false);
            var names = list.Processes?.Select(p => p.Name).ToList() ?? [];
            var idExists = (int id) => list.Processes?.Any(p => p.Id == id) == true;

            var kind = StartTargetResolver.Resolve(target, hasOptions, names, idExists);

            if (kind == StartTargetKind.NewExecutable ||
                (kind == StartTargetKind.Existing && hasOptions && StartTargetResolver.LooksLikeExecutableOrScript(target)
                 && !names.Any(n => n.Equals(target, StringComparison.Ordinal))
                 && !(int.TryParse(target, out var tid) && idExists(tid))))
            {
                var name = parseResult.GetValue(nameOpt) ?? Path.GetFileNameWithoutExtension(target);
                var args = parseResult.GetValue(argsOpt) ?? string.Empty;
                if (forwarded.Length > 0)
                {
                    var joined = string.Join(' ', forwarded.Select(QuoteIfNeeded));
                    args = string.IsNullOrWhiteSpace(args) ? joined : $"{args} {joined}";
                }

                return await StartNewAsync(client, name, target,
                    args,
                    parseResult.GetValue(cwdOpt) ?? string.Empty,
                    !parseResult.GetValue(noRestartOpt),
                    parseResult.GetValue(maxRestartsOpt),
                    parseResult.GetValue(maxCpuOpt),
                    parseResult.GetValue(maxMemOpt),
                    parseResult.GetValue(outLogOpt),
                    parseResult.GetValue(errLogOpt),
                    parseResult.GetValue(logOpt),
                    parseResult.GetValue(timeOpt));
            }

            if (StartTargetResolver.LooksLikeExecutableOrScript(target) &&
                !names.Any(n => n.Equals(target, StringComparison.Ordinal)) &&
                !(int.TryParse(target, out var id2) && idExists(id2)))
            {
                var name = parseResult.GetValue(nameOpt) ?? Path.GetFileNameWithoutExtension(target);
                return await StartNewAsync(client, name, target,
                    parseResult.GetValue(argsOpt) ?? string.Empty,
                    parseResult.GetValue(cwdOpt) ?? string.Empty,
                    !parseResult.GetValue(noRestartOpt),
                    parseResult.GetValue(maxRestartsOpt),
                    parseResult.GetValue(maxCpuOpt),
                    parseResult.GetValue(maxMemOpt),
                    parseResult.GetValue(outLogOpt),
                    parseResult.GetValue(errLogOpt),
                    parseResult.GetValue(logOpt),
                    parseResult.GetValue(timeOpt));
            }

            var response = await client.Send(CliCommands.TargetRequest("start", target));
            return CliOutput.Exit(response);
        });

        return cmd;
    }

    private static string QuoteIfNeeded(string value) =>
        value.Contains(' ') ? $"\"{value}\"" : value;

    private static bool WantsNewExecutable(
        ParseResult parseResult,
        Option<string?> argsOpt,
        Option<string?> cwdOpt,
        Option<string?> nameOpt,
        Option<string?> outLogOpt,
        Option<string?> errLogOpt,
        Option<string?> logOpt,
        Option<bool> noRestartOpt,
        Option<int?> maxRestartsOpt,
        Option<double?> maxCpuOpt,
        Option<long?> maxMemOpt,
        Option<bool> timeOpt)
    {
        if (!string.IsNullOrWhiteSpace(parseResult.GetValue(argsOpt))) return true;
        if (!string.IsNullOrWhiteSpace(parseResult.GetValue(cwdOpt))) return true;
        if (!string.IsNullOrWhiteSpace(parseResult.GetValue(nameOpt))) return true;
        if (!string.IsNullOrWhiteSpace(parseResult.GetValue(outLogOpt))) return true;
        if (!string.IsNullOrWhiteSpace(parseResult.GetValue(errLogOpt))) return true;
        if (!string.IsNullOrWhiteSpace(parseResult.GetValue(logOpt))) return true;
        if (parseResult.GetValue(noRestartOpt)) return true;
        if (parseResult.GetValue(maxRestartsOpt).HasValue) return true;
        if (parseResult.GetValue(maxCpuOpt).HasValue) return true;
        if (parseResult.GetValue(maxMemOpt).HasValue) return true;
        if (parseResult.GetValue(timeOpt)) return true;
        return false;
    }

    private static async Task<int> StartNewAsync(
        IpcClient client,
        string name,
        string executable,
        string args,
        string cwd,
        bool autoRestart,
        int? maxRestarts,
        double? maxCpu,
        long? maxMem,
        string? outLog,
        string? errLog,
        string? combinedLog,
        bool logTimestamps)
    {
        var response = await client.Send(new IpcRequest
        {
            Command = "start",
            Options = new StartOptions
            {
                Name = name,
                ExecutablePath = executable,
                Arguments = args,
                WorkingDirectory = cwd,
                AutoRestart = autoRestart,
                MaxRestartAttempts = maxRestarts ?? ProcessDefaults.MaxRestartAttempts,
                MaxCpuPercent = maxCpu,
                MaxMemoryMb = maxMem,
                OutLogFile = outLog,
                ErrorLogFile = errLog,
                CombinedLogFile = combinedLog,
                LogTimestamps = logTimestamps
            }
        });

        return CliOutput.Exit(response);
    }
}
