using NexaRun.Shared;
using NexaRun.Shared.Models;

namespace NexaRun.Tray.ViewModels;

public class ProcessRow(NexaProcess p)
{
    public int Id { get; } = p.Id;
    public string Name { get; } = p.Name;
    public string Status { get; } = NexaProcessDisplay.ShortStatusText(p);
    public string StatusTooltip { get; } = BuildStatusTooltip(p);
    public string? StatusReason { get; } = p.StatusReason;
    public ProcessStatus StatusValue { get; } = p.Status;
    public string Url { get; } = string.IsNullOrWhiteSpace(p.Url) ? NexaProcessDisplay.EmptyPlaceholder : p.Url;
    public bool HasUrl { get; } = !string.IsNullOrWhiteSpace(p.Url);
    public string Pid { get; } = NexaProcessDisplay.FormatPid(p.Pid);
    public int Restarts { get; } = p.Restarts;
    public string Cpu { get; } = NexaProcessDisplay.FormatCpu(p.CpuUsage);
    public string Memory { get; } = NexaProcessDisplay.FormatMemory(p.MemoryUsage);
    public string Uptime { get; } = NexaProcessDisplay.FormatUptime(p.Status, p.StartedAt);
    public bool IsOnline { get; } = p.Status == ProcessStatus.Online;
    public bool CanStart { get; } = p.Status is ProcessStatus.Stopped or ProcessStatus.Errored;
    public NexaProcess Source { get; } = p;

    private static string BuildStatusTooltip(NexaProcess p)
    {
        if (!string.IsNullOrWhiteSpace(p.StatusReason))
            return p.StatusReason!;
        return NexaProcessDisplay.StatusText(p);
    }
}
