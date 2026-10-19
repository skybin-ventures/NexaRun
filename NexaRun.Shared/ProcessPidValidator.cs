using System.Diagnostics;

namespace NexaRun.Shared;

/// <summary>Validates whether a live OS process still matches a previously managed NexaRun process.</summary>
public static class ProcessPidValidator
{
    /// <summary>
    /// Returns true when <paramref name="osProc"/> is a plausible continuation of the saved process.
    /// Primary signal: OS process start time is not earlier than saved StartedAt (with skew allowance).
    /// Secondary: process image name matches executable or known wrappers (cmd, powershell).
    /// </summary>
    public static bool MatchesFingerprint(Process osProc, DateTime startedAtUtc, string executablePath)
    {
        if (osProc.HasExited)
            return false;

        try
        {
            osProc.Refresh();
            var startedLocal = osProc.StartTime.ToUniversalTime();
            // Allow clock skew / scheduling delay of a few seconds before StartedAt was recorded.
            if (startedLocal < startedAtUtc.AddSeconds(-5))
                return false;
        }
        catch
        {
            // Some processes deny StartTime; fall through to name check only.
        }

        var expected = Path.GetFileNameWithoutExtension(executablePath);
        if (string.IsNullOrWhiteSpace(expected))
            return true;

        string image;
        try
        {
            image = osProc.ProcessName;
        }
        catch
        {
            return false;
        }

        if (image.Equals(expected, StringComparison.OrdinalIgnoreCase))
            return true;

        // cmd / powershell wrappers used for .bat/.cmd/.ps1 launches
        if (image.Equals("cmd", StringComparison.OrdinalIgnoreCase) ||
            image.Equals("powershell", StringComparison.OrdinalIgnoreCase) ||
            image.Equals("pwsh", StringComparison.OrdinalIgnoreCase) ||
            image.Equals("dotnet", StringComparison.OrdinalIgnoreCase) ||
            image.Equals("node", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
