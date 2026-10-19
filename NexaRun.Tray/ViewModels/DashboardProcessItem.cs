using Avalonia.Media;
using NexaRun.Shared;
using NexaRun.Shared.Models;

namespace NexaRun.Tray.ViewModels;

public class DashboardProcessItem(NexaProcess p)
{
    public string Name { get; } = p.Name;
    public string StatusLabel { get; } = NexaProcessDisplay.ShortStatusText(p);
    public string? StatusReason { get; } = p.StatusReason;
    public string StatusTooltip { get; } =
        !string.IsNullOrWhiteSpace(p.StatusReason) ? p.StatusReason! : NexaProcessDisplay.StatusText(p);
    public string StatusColor { get; } = ColorFor(p.Status);
    public IBrush StatusBrush { get; } = SolidColorBrush.Parse(ColorFor(p.Status));
    public string Memory { get; } = NexaProcessDisplay.FormatMemory(p.MemoryUsage);
    public string Cpu { get; } = NexaProcessDisplay.FormatCpu(p.CpuUsage);
    public string Pid { get; } = NexaProcessDisplay.FormatPid(p.Pid);
    public int Restarts { get; } = p.Restarts;
    public bool HasUrl { get; } = !string.IsNullOrWhiteSpace(p.Url);
    public NexaProcess Source { get; } = p;

    private static string ColorFor(ProcessStatus status) => status switch
    {
        ProcessStatus.Online   => "#22c55e",
        ProcessStatus.Errored  => "#ef4444",
        ProcessStatus.Starting => "#eab308",
        ProcessStatus.Stopping => "#f97316",
        _                      => "#6b7280"
    };
}
