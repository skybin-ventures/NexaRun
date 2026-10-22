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

    /// <summary>Maximum crash-restart backoff delay (ms).</summary>
    public const int MaxCrashRestartBackoffMs = 30_000;

    /// <summary>
    /// Exponential crash-restart backoff: 1s, 2s, 4s, 8s, 16s, then capped at <see cref="MaxCrashRestartBackoffMs"/>.
    /// </summary>
    /// <param name="restarts">1-based restart attempt count after increment.</param>
    public static int CrashRestartBackoffMs(int restarts)
    {
        if (restarts < 1)
            restarts = 1;
        // Exponent up to 5 → 2^5 * 1000 = 32000, then Min with 30s.
        var exponent = Math.Min(restarts - 1, 5);
        return Math.Min((int)Math.Pow(2, exponent) * 1000, MaxCrashRestartBackoffMs);
    }

    /// <summary>Number of rotated log generations to keep (.1 … .N).</summary>
    public const int MaxLogGenerations = 3;

    /// <summary>Default wait for optional ready-port / ready-file before marking Online.</summary>
    public const int ReadyTimeoutMs = 30_000;

    /// <summary>Debounce window for file-watch restarts (ms).</summary>
    public const int WatchDebounceMs = 1_500;
}
