using NexaRun.Daemon;
using NexaRun.Shared;
using NexaRun.Shared.Models;

namespace NexaRun.Tests;

public class ProcessWatchPathTests
{
    /// <summary>
    /// A relative WatchPath must resolve against the process WorkingDirectory,
    /// never the daemon's current directory. Absolute WatchPaths are used as-is.
    /// </summary>
    [Fact]
    public void ResolveWatchPath_relative_watchpath_uses_working_directory()
    {
        var p = new NexaProcess
        {
            Id = 1,
            Name = "app",
            WorkingDirectory = @"C:\Projects\app",
            WatchPath = "src",
        };

        Assert.Equal(@"C:\Projects\app\src", ProcessWatchService.ResolveWatchPath(p));
    }

    [Fact]
    public void ResolveWatchPath_absolute_watchpath_ignores_working_directory()
    {
        var p = new NexaProcess
        {
            Id = 2,
            Name = "app",
            WorkingDirectory = @"C:\Projects\app",
            WatchPath = @"C:\Other\watch",
        };

        Assert.Equal(@"C:\Other\watch", ProcessWatchService.ResolveWatchPath(p));
    }

    [Fact]
    public void ResolveWatchPath_blank_watchpath_uses_working_directory()
    {
        var p = new NexaProcess
        {
            Id = 3,
            Name = "app",
            WorkingDirectory = @"C:\Projects\app",
        };

        Assert.Equal(@"C:\Projects\app", ProcessWatchService.ResolveWatchPath(p));
    }

    [Fact]
    public void ResolveWatchPath_relative_watchpath_with_blank_cwd_skips_watch()
    {
        var p = new NexaProcess
        {
            Id = 4,
            Name = "app",
            WatchPath = "src",
        };

        Assert.Null(ProcessWatchService.ResolveWatchPath(p));
    }

    [Fact]
    public void ResolveWatchPath_no_watchpath_no_cwd_skips_watch()
    {
        var p = new NexaProcess { Id = 5, Name = "app" };
        Assert.Null(ProcessWatchService.ResolveWatchPath(p));
    }
}
