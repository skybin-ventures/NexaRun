using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NexaRun.Daemon;
using NexaRun.Shared;
using NexaRun.Shared.Config;
using NexaRun.Shared.Models;

namespace NexaRun.Tests;

public class ProcessManagerLifecycleTests : IDisposable
{
    private readonly string _tempDir;

    public ProcessManagerLifecycleTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "nexarun-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        NexaRunPaths.DataDirOverride = _tempDir;
        NexaRunPaths.EnsureDirectories();
    }

    public void Dispose()
    {
        NexaRunPaths.DataDirOverride = null;
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            /* best effort */
        }
    }

    private static ProcessManager CreateManager()
    {
        var alerts = new ProcessAlertService(NullLogger<ProcessAlertService>.Instance);
        return new ProcessManager(NullLogger<ProcessManager>.Instance, alerts);
    }

    [Fact]
    public async Task Load_reattaches_running_pid()
    {
        using var child = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -Command Start-Sleep -Seconds 60",
            UseShellExecute = false,
            CreateNoWindow = true
        })!;

        try
        {
            var saved = new List<NexaProcess>
            {
                new()
                {
                    Id = 0,
                    Name = "sleeper",
                    ExecutablePath = "powershell.exe",
                    Arguments = "-NoProfile -Command Start-Sleep -Seconds 60",
                    Status = ProcessStatus.Online,
                    DesiredStatus = ProcessStatus.Online,
                    Pid = child.Id,
                    StartedAt = DateTime.UtcNow.AddSeconds(-1),
                    AutoRestart = true
                }
            };
            await File.WriteAllTextAsync(
                NexaRunPaths.ProcessesFile,
                JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }));

            var mgr = CreateManager();
            await mgr.Load();
            var all = await mgr.GetAll();
            var p = Assert.Single(all);
            Assert.Equal(ProcessStatus.Online, p.Status);
            Assert.Equal(child.Id, p.Pid);

            var (ok, _) = await mgr.Stop("sleeper");
            Assert.True(ok);
        }
        finally
        {
            try
            {
                if (!child.HasExited)
                    child.Kill(entireProcessTree: true);
            }
            catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Load_rejects_stale_pid()
    {
        var saved = new List<NexaProcess>
        {
            new()
            {
                Id = 0,
                Name = "gone",
                ExecutablePath = "missing-app.exe",
                Status = ProcessStatus.Online,
                DesiredStatus = ProcessStatus.Online,
                Pid = 1, // almost certainly not our app / wrong fingerprint
                StartedAt = DateTime.UtcNow,
                AutoRestart = true
            }
        };
        await File.WriteAllTextAsync(
            NexaRunPaths.ProcessesFile,
            JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }));

        var mgr = CreateManager();
        await mgr.Load();
        var all = await mgr.GetAll();
        var p = Assert.Single(all);
        Assert.Equal(ProcessStatus.Stopped, p.Status);
        Assert.Null(p.Pid);
        Assert.Equal(ProcessStatus.Online, p.DesiredStatus);
    }

    [Fact]
    public async Task AutoRestart_false_marks_errored_on_exit()
    {
        var mgr = CreateManager();
        await mgr.Load();

        var (ok, _, proc) = await mgr.Start(new StartOptions
        {
            Name = "quick-exit",
            ExecutablePath = "powershell.exe",
            Arguments = "-NoProfile -Command exit 7",
            AutoRestart = false,
            MaxRestartAttempts = 3
        });
        Assert.True(ok);
        Assert.NotNull(proc);

        // Wait for child to exit, then run crash check.
        await Task.Delay(1500);
        await mgr.CheckAndRestartCrashed();

        var all = await mgr.GetAll();
        var p = Assert.Single(all);
        Assert.Equal(ProcessStatus.Errored, p.Status);
        Assert.Contains("auto-restart off", p.StatusReason ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Stop_during_crash_backoff_cancels_relaunch()
    {
        // Use a process that exits immediately with AutoRestart and a long first backoff... 
        // Restarts=1 → delay 1s. We'll stop during that window.
        var mgr = CreateManager();
        await mgr.Load();

        var (ok, _, _) = await mgr.Start(new StartOptions
        {
            Name = "crash-loop",
            ExecutablePath = "powershell.exe",
            Arguments = "-NoProfile -Command exit 1",
            AutoRestart = true,
            MaxRestartAttempts = 5
        });
        Assert.True(ok);

        await Task.Delay(800);
        // Kick crash detection; it will begin backoff Delay(1000).
        var crashTask = mgr.CheckAndRestartCrashed();
        await Task.Delay(200);
        var (stopOk, _) = await mgr.Stop("crash-loop");
        Assert.True(stopOk);
        await crashTask;

        await Task.Delay(1500);
        var all = await mgr.GetAll();
        var p = Assert.Single(all);
        Assert.Equal(ProcessStatus.Stopped, p.Status);
        Assert.Equal(ProcessStatus.Stopped, p.DesiredStatus);
        Assert.Null(p.Pid);
    }

    [Fact]
    public async Task Ready_timeout_marks_errored()
    {
        var mgr = CreateManager();
        await mgr.Load();

        var (ok, message, proc) = await mgr.Start(new StartOptions
        {
            Name = "ready-timeout",
            ExecutablePath = "powershell.exe",
            Arguments = "-NoProfile -Command Start-Sleep -Seconds 60",
            AutoRestart = false,
            ReadyPort = 1, // almost never listening
            ReadyTimeoutMs = 500
        });

        Assert.False(ok);
        Assert.NotNull(proc);
        Assert.Equal(ProcessStatus.Errored, proc!.Status);
        Assert.Contains("ready", message, StringComparison.OrdinalIgnoreCase);

        await mgr.Delete("ready-timeout");
    }

    [Fact]
    public async Task Stop_during_ready_cancels_to_stopped()
    {
        var mgr = CreateManager();
        await mgr.Load();

        var startTask = mgr.Start(new StartOptions
        {
            Name = "ready-cancel",
            ExecutablePath = "powershell.exe",
            Arguments = "-NoProfile -Command Start-Sleep -Seconds 60",
            AutoRestart = false,
            ReadyPort = 1,
            ReadyTimeoutMs = 15_000
        });

        await Task.Delay(300);
        var (stopOk, _) = await mgr.Stop("ready-cancel");
        Assert.True(stopOk);

        var (ok, _, proc) = await startTask;
        Assert.False(ok);
        Assert.NotNull(proc);
        Assert.Equal(ProcessStatus.Stopped, proc!.Status);
        Assert.Equal(ProcessStatus.Stopped, proc.DesiredStatus);
    }

    [Fact]
    public async Task Invalid_cron_rejected_on_start()
    {
        var mgr = CreateManager();
        await mgr.Load();

        var (ok, message, _) = await mgr.Start(new StartOptions
        {
            Name = "bad-cron",
            ExecutablePath = "powershell.exe",
            Arguments = "-NoProfile -Command Start-Sleep -Seconds 5",
            CronRestart = "not a cron"
        });

        Assert.False(ok);
        Assert.Contains("cron", message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await mgr.GetAll());
    }

    [Fact]
    public async Task Cron_fires_once_per_minute_key()
    {
        var mgr = CreateManager();
        await mgr.Load();

        var (ok, _, _) = await mgr.Start(new StartOptions
        {
            Name = "cron-app",
            ExecutablePath = "powershell.exe",
            Arguments = "-NoProfile -Command Start-Sleep -Seconds 120",
            AutoRestart = false,
            CronRestart = "* * * * *"
        });
        Assert.True(ok);

        await mgr.CheckWatchAndCron();
        var afterFirst = await mgr.GetAll();
        var firstStarted = Assert.Single(afterFirst).StartedAt;

        await mgr.CheckWatchAndCron();
        var afterSecond = await mgr.GetAll();
        Assert.Equal(firstStarted, Assert.Single(afterSecond).StartedAt);

        await mgr.Stop("cron-app");
        await mgr.Delete("cron-app");
    }

    [Fact]
    public async Task Import_named_clones_then_delete()
    {
        var mgr = CreateManager();
        await mgr.Load();

        var clones = InstanceCloneExpander.Expand(
        [
            new StartOptions
            {
                Name = "api",
                ExecutablePath = "powershell.exe",
                Arguments = "-NoProfile -Command Start-Sleep -Seconds 1",
                Instances = 3,
                AutoRestart = false
            }
        ]);

        Assert.Equal(3, clones.Count);
        var (importOk, _) = await mgr.ImportBatch(clones, start: false);
        Assert.True(importOk);

        var all = await mgr.GetAll();
        Assert.Equal(3, all.Count);
        Assert.Contains(all, p => p.Name == "api");
        Assert.Contains(all, p => p.Name == "api-1");
        Assert.Contains(all, p => p.Name == "api-2");

        await mgr.Delete("api-1");
        all = await mgr.GetAll();
        Assert.Equal(2, all.Count);
        Assert.DoesNotContain(all, p => p.Name == "api-1");
    }

    [Fact]
    public async Task Flush_while_running_succeeds()
    {
        var mgr = CreateManager();
        await mgr.Load();

        var (ok, _, proc) = await mgr.Start(new StartOptions
        {
            Name = "flush-me",
            ExecutablePath = "powershell.exe",
            Arguments = "-NoProfile -Command Start-Sleep -Seconds 60",
            AutoRestart = false
        });
        Assert.True(ok);
        Assert.NotNull(proc);

        var (flushOk, flushMsg) = await mgr.ClearLogs("flush-me");
        Assert.True(flushOk);
        Assert.Contains("Cleared", flushMsg);
        Assert.Equal(ProcessStatus.Online, (await mgr.GetAll()).Single().Status);

        await mgr.Stop("flush-me");
        await mgr.Delete("flush-me");
    }
}
