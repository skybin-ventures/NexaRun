using NexaRun.Shared;

namespace NexaRun.Tests;

public class CrashRestartBackoffTests
{
    [Theory]
    [InlineData(1, 1000)]
    [InlineData(2, 2000)]
    [InlineData(3, 4000)]
    [InlineData(4, 8000)]
    [InlineData(5, 16000)]
    [InlineData(6, 30000)]
    [InlineData(7, 30000)]
    [InlineData(10, 30000)]
    public void CrashRestartBackoffMs_matches_expected_sequence(int restarts, int expectedMs)
    {
        Assert.Equal(expectedMs, ProcessDefaults.CrashRestartBackoffMs(restarts));
    }

    [Fact]
    public void CrashRestartBackoffMs_clamps_sub_one_to_first_step()
    {
        Assert.Equal(1000, ProcessDefaults.CrashRestartBackoffMs(0));
        Assert.Equal(1000, ProcessDefaults.CrashRestartBackoffMs(-1));
    }
}
