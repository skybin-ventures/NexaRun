using NexaRun.Shared.Models;

namespace NexaRun.Shared.Config;

/// <summary>
/// Expands <c>instances</c> into separately named process definitions (not a shared cluster).
/// </summary>
public static class InstanceCloneExpander
{
    /// <summary>
    /// For each option with <see cref="StartOptions.Instances"/> &gt; 1, emit
    /// <c>name</c>, <c>name-1</c>, … <c>name-(n-1)</c> with <c>NODE_APP_INSTANCE</c> set.
    /// </summary>
    public static List<StartOptions> Expand(IEnumerable<StartOptions> options)
    {
        var result = new List<StartOptions>();
        foreach (var opt in options)
        {
            var n = opt.Instances is > 1 ? opt.Instances.Value : 1;
            if (n > 32)
                n = 32; // hard cap — avoid accidental huge imports

            if (n <= 1)
            {
                opt.Instances = null;
                result.Add(opt);
                continue;
            }

            for (var i = 0; i < n; i++)
            {
                var clone = Clone(opt);
                clone.Instances = null;
                clone.Name = i == 0 ? opt.Name : $"{opt.Name}-{i}";
                clone.Environment ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                clone.Environment["NODE_APP_INSTANCE"] = i.ToString();
                result.Add(clone);
            }
        }

        return result;
    }

    private static StartOptions Clone(StartOptions o) =>
        new()
        {
            Name = o.Name,
            ExecutablePath = o.ExecutablePath,
            Arguments = o.Arguments,
            WorkingDirectory = o.WorkingDirectory,
            AutoRestart = o.AutoRestart,
            MaxRestartAttempts = o.MaxRestartAttempts,
            MaxCpuPercent = o.MaxCpuPercent,
            MaxMemoryMb = o.MaxMemoryMb,
            OutLogFile = o.OutLogFile,
            ErrorLogFile = o.ErrorLogFile,
            CombinedLogFile = o.CombinedLogFile,
            LogTimestamps = o.LogTimestamps,
            Environment = o.Environment == null
                ? null
                : new Dictionary<string, string>(o.Environment, StringComparer.OrdinalIgnoreCase),
            Url = o.Url,
            ReadyPort = o.ReadyPort,
            ReadyFile = o.ReadyFile,
            ReadyTimeoutMs = o.ReadyTimeoutMs,
            RestartDelayMs = o.RestartDelayMs,
            Watch = o.Watch,
            WatchPath = o.WatchPath,
            CronRestart = o.CronRestart,
            Instances = o.Instances
        };
}
