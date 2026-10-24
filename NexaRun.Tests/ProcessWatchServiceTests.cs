using NexaRun.Daemon;
using NexaRun.Shared.Models;

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

    /// <summary>
    /// The ignore set must NOT include the parent directory of a configured log
    /// file. When a log sits directly under the watch root, excluding the parent
    /// (the root itself) would suppress every change in the watched tree.
    /// </summary>
    [Fact]
    public void BuildIgnorePaths_does_not_exclude_the_log_parent_directory()
    {
        var root = Path.GetTempPath(); // some real directory
        var logFile = Path.Combine(root, "myapp.log");
        var p = new NexaProcess { LogFile = logFile };

        var ignores = ProcessWatchService.BuildIgnorePaths(p);

        // The live log file and its rotated generations are ignored…
        Assert.Contains(logFile, ignores, StringComparer.OrdinalIgnoreCase);
        Assert.Contains($"{logFile}.1", ignores, StringComparer.OrdinalIgnoreCase);
        Assert.Contains($"{logFile}.2", ignores, StringComparer.OrdinalIgnoreCase);
        Assert.Contains($"{logFile}.3", ignores, StringComparer.OrdinalIgnoreCase);
        // …but the parent directory is NOT excluded, so a sibling source file
        // in the same directory still triggers a restart.
        Assert.DoesNotContain(root, ignores, StringComparer.OrdinalIgnoreCase);
        Assert.False(ProcessWatchService.ShouldIgnore(Path.Combine(root, "src.js"), ignores));
    }

    [Fact]
    public void ShouldIgnore_rotated_log_generations()
    {
        var logPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "nexarun-rotated-" + Guid.NewGuid().ToString("N") + ".log"));
        var ignores = ProcessWatchService.BuildIgnorePaths(new NexaProcess { LogFile = logPath });

        // Rotation renames the live log to .1/.2/.3; those must be ignored.
        Assert.True(ProcessWatchService.ShouldIgnore($"{logPath}.1", ignores));
        Assert.True(ProcessWatchService.ShouldIgnore($"{logPath}.3", ignores));
        Assert.False(ProcessWatchService.ShouldIgnore(Path.Combine("C:", "app", "main.js"), ignores));
    }
}

