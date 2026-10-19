using NexaRun.Shared;

namespace NexaRun.Shared.Models;

public class NexaProcess
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public ProcessStatus Status { get; set; }
    /// <summary>
    /// Intended runtime state across daemon restarts. User Start → Online; user Stop/Delete → Stopped.
    /// Crashes do not clear this; boot restore starts processes still Desired Online.
    /// </summary>
    public ProcessStatus DesiredStatus { get; set; } = ProcessStatus.Stopped;
    /// <summary>Why the process is Errored (crash limit, per-process CPU/memory limit, launch failure).</summary>
    public string? StatusReason { get; set; }
    public int? Pid { get; set; }
    public DateTime StartedAt { get; set; }
    public int Restarts { get; set; }
    public int MaxRestartAttempts { get; set; } = ProcessDefaults.MaxRestartAttempts;
    public double CpuUsage { get; set; }
    public long MemoryUsage { get; set; }
    public bool AutoRestart { get; set; } = true;
    public string LogFile { get; set; } = string.Empty;
    public string? OutLogFile { get; set; }
    public string? ErrorLogFile { get; set; }
    public bool LogTimestamps { get; set; }
    public Dictionary<string, string>? Environment { get; set; }
    public double? MaxCpuPercent { get; set; }
    public long? MaxMemoryMb { get; set; }
    /// <summary>Optional URL opened from the Processes window (e.g. http://localhost:3101).</summary>
    public string? Url { get; set; }
}
