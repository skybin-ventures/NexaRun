namespace NexaRun.Shared;

public static class ProcessDefaults
{
    public const int MaxRestartAttempts = 3;

    /// <summary>After this many seconds online, the crash restart counter resets.</summary>
    public const int MinUptimeSecondsToResetRestarts = 30;

    /// <summary>Wait after killing a process before starting again (port/log handle release).</summary>
    public const int RestartSettleMs = 2_000;

    /// <summary>Wait after a successful start before launching the next process in a batch.</summary>
    public const int StartSettleMs = 2_000;

    /// <summary>Soft-stop wait before force-killing the process tree.</summary>
    public const int StopTimeoutMs = 8_000;

    /// <summary>Minimum minutes between failed-process recovery checks (configurable in Settings).</summary>
    public const int MinFailedRecoveryIntervalMinutes = 10;

    /// <summary>Per-process limit must be exceeded this many checks in a row before restart.</summary>
    public const int ResourceLimitBreachCount = 3;

    /// <summary>Rotate log files when they exceed this size (bytes).</summary>
    public const long MaxLogFileBytes = 10 * 1024 * 1024;
}
