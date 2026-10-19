using NexaRun.Shared.Models;

namespace NexaRun.Shared;

public static class NexaProcessDisplay
{
    public const string EmptyPlaceholder = "—";

    public static string StatusText(NexaProcess p) =>
        p.Status == ProcessStatus.Errored && !string.IsNullOrWhiteSpace(p.StatusReason)
            ? $"Errored — {p.StatusReason}"
            : p.Status.ToString();

    public static string ShortStatusText(NexaProcess p) =>
        p.Status == ProcessStatus.Errored && !string.IsNullOrWhiteSpace(p.StatusReason)
            ? "Errored"
            : p.Status.ToString();

    public static string FormatPid(int? pid) =>
        pid?.ToString() ?? EmptyPlaceholder;

    public static string FormatMemory(long bytes)
    {
        if (bytes <= 0) return EmptyPlaceholder;
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F0} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    public static string FormatCpu(double cpuPercent) =>
        cpuPercent > 0 ? $"{cpuPercent:F1}%" : EmptyPlaceholder;

    public static string FormatUptime(ProcessStatus status, DateTime startedAt)
    {
        if (status != ProcessStatus.Online) return EmptyPlaceholder;
        var span = DateTime.UtcNow - startedAt;
        if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d";
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h";
        if (span.TotalMinutes >= 1) return $"{(int)span.TotalMinutes}m";
        return $"{Math.Max(0, (int)span.TotalSeconds)}s";
    }
}
