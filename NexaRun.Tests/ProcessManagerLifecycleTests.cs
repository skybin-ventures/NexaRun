using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NexaRun.Daemon;
using NexaRun.Shared;
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
}
