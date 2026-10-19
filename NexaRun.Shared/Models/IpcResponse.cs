using NexaRun.Shared.Config;

namespace NexaRun.Shared.Models;

public class IpcResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<NexaProcess>? Processes { get; set; }
    public string? Logs { get; set; }
    /// <summary>Absolute path of the log file read by the last logs request (for CLI --follow).</summary>
    public string? LogPath { get; set; }
    public List<ProcessRun>? RunHistory { get; set; }
    public NexaRunSettings? Settings { get; set; }
    public string? DataDir { get; set; }
}
