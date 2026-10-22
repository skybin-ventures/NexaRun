using NexaRun.Shared;

namespace NexaRun.Tests;

public class ProcessReadyWaiterTests
{
    [Fact]
    public async Task Wait_succeeds_when_ready_file_appears()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexarun-ready-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "ready");
        try
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(200);
                await File.WriteAllTextAsync(file, "ok");
            });

            var (ready, error) = await ProcessReadyWaiter.Wait(null, file, timeoutMs: 5000);
            Assert.True(ready);
            Assert.Null(error);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Wait_times_out_when_file_missing()
    {
        var file = Path.Combine(Path.GetTempPath(), "nexarun-missing-" + Guid.NewGuid().ToString("N"));
        var (ready, error) = await ProcessReadyWaiter.Wait(null, file, timeoutMs: 400);
        Assert.False(ready);
        Assert.Contains("timed out", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Wait_no_check_returns_ready_immediately()
    {
        var (ready, error) = await ProcessReadyWaiter.Wait(null, null, timeoutMs: 100);
        Assert.True(ready);
        Assert.Null(error);
    }

    [Fact]
    public async Task Wait_returns_cancelled_when_token_cancelled()
    {
        var file = Path.Combine(Path.GetTempPath(), "nexarun-never-" + Guid.NewGuid().ToString("N"));
        using var cts = new CancellationTokenSource();
        var wait = ProcessReadyWaiter.Wait(null, file, timeoutMs: 10_000, cts.Token);
        await Task.Delay(100);
        cts.Cancel();

        var (ready, error) = await wait;
        Assert.False(ready);
        Assert.True(ProcessReadyWaiter.IsCancelledError(error));
    }
}
