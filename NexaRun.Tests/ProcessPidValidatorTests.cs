using System.Diagnostics;
using NexaRun.Shared;

namespace NexaRun.Tests;

public class ProcessPidValidatorTests
{
    [Fact]
    public void Matches_current_process_fingerprint()
    {
        using var self = Process.GetCurrentProcess();
        var ok = ProcessPidValidator.MatchesFingerprint(
            self,
            DateTime.UtcNow.AddMinutes(-5),
            self.MainModule?.FileName ?? self.ProcessName + ".exe");
        Assert.True(ok);
    }

    [Fact]
    public void Rejects_when_started_at_is_in_the_future_of_process()
    {
        using var self = Process.GetCurrentProcess();
        // Claim the managed process started long after the OS process — indicates PID reuse.
        var ok = ProcessPidValidator.MatchesFingerprint(
            self,
            DateTime.UtcNow.AddYears(1),
            "totally-unrelated-name.exe");
        Assert.False(ok);
    }
}
