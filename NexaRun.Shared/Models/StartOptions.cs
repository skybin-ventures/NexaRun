using NexaRun.Shared;

namespace NexaRun.Shared.Models;

public class StartOptions
{
    public string Name { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public bool AutoRestart { get; set; } = true;
    public int MaxRestartAttempts { get; set; } = ProcessDefaults.MaxRestartAttempts;
    public double? MaxCpuPercent { get; set; }   // e.g. 80.0 = restart above 80% CPU
    public long? MaxMemoryMb { get; set; }         // e.g. 512 = restart above 512 MB
    public string? OutLogFile { get; set; }
    public string? ErrorLogFile { get; set; }
    public string? CombinedLogFile { get; set; }
    public bool LogTimestamps { get; set; }
    public Dictionary<string, string>? Environment { get; set; }
    public string? Url { get; set; }
    /// <summary>Optional: wait until TCP 127.0.0.1:port accepts before Online.</summary>
    public int? ReadyPort { get; set; }
    /// <summary>Optional: wait until this file exists before Online.</summary>
    public string? ReadyFile { get; set; }
    /// <summary>Ready-check timeout in ms (default <see cref="ProcessDefaults.ReadyTimeoutMs"/>).</summary>
    public int? ReadyTimeoutMs { get; set; }
    /// <summary>
    /// Optional minimum crash-restart delay (ms). Actual delay is max(exponential backoff, this value).
    /// </summary>
    public int? RestartDelayMs { get; set; }
    public bool Watch { get; set; }
    public string? WatchPath { get; set; }
    /// <summary>Standard 5-field cron (local time) for scheduled restart.</summary>
    public string? CronRestart { get; set; }
    /// <summary>Import-only: expand into named clones (<c>name</c>, <c>name-1</c>, …). Not persisted.</summary>
    public int? Instances { get; set; }
}
