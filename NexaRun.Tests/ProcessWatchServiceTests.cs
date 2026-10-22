using NexaRun.Daemon;

namespace NexaRun.Tests;

public class ProcessWatchServiceTests
{
    [Fact]
    public void ShouldIgnore_node_modules_and_git()
    {
        var ignores = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.True(ProcessWatchService.ShouldIgnore(
            Path.Combine("C:", "app", "node_modules", "pkg", "index.js"), ignores));
        Assert.True(ProcessWatchService.ShouldIgnore(
            Path.Combine("C:", "app", ".git", "HEAD"), ignores));
    }

    [Fact]
    public void ShouldIgnore_log_extension_and_configured_paths()
    {
        var logPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "nexarun-watch-ignore-" + Guid.NewGuid().ToString("N") + ".log"));
        var ignores = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { logPath };

        Assert.True(ProcessWatchService.ShouldIgnore(logPath, ignores));
        Assert.True(ProcessWatchService.ShouldIgnore(
            Path.Combine("C:", "app", "out.log"), ignores));
        Assert.False(ProcessWatchService.ShouldIgnore(
            Path.Combine("C:", "app", "src", "server.js"), ignores));
    }
}
