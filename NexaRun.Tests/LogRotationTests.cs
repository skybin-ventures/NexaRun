using NexaRun.Daemon;
using NexaRun.Shared;

namespace NexaRun.Tests;

public class LogRotationTests
{
    [Fact]
    public void RotateGenerations_keeps_configured_backups()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexarun-rot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "app.log");
            // Seed oversized current file and existing .1
            File.WriteAllBytes(path, new byte[ProcessDefaults.MaxLogFileBytes]);
            File.WriteAllText(path + ".1", "old1");

            LogFileHelper.RotateGenerations(path, generations: 3);

            Assert.False(File.Exists(path));
            Assert.True(File.Exists(path + ".1"));
            Assert.True(File.Exists(path + ".2"));
            Assert.Equal("old1", File.ReadAllText(path + ".2"));

            // Second rotate should push to .3 and drop oldest when max=3
            File.WriteAllBytes(path, new byte[ProcessDefaults.MaxLogFileBytes]);
            LogFileHelper.RotateGenerations(path, generations: 3);
            Assert.True(File.Exists(path + ".1"));
            Assert.True(File.Exists(path + ".2"));
            Assert.True(File.Exists(path + ".3"));

            File.WriteAllBytes(path, new byte[ProcessDefaults.MaxLogFileBytes]);
            LogFileHelper.RotateGenerations(path, generations: 3);
            Assert.False(File.Exists(path + ".4"));
            Assert.True(File.Exists(path + ".3"));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void DeleteRotated_removes_all_generations()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexarun-rot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "app.log");
            File.WriteAllText(path + ".1", "a");
            File.WriteAllText(path + ".2", "b");
            File.WriteAllText(path + ".3", "c");

            LogFileHelper.DeleteRotated(path);

            Assert.False(File.Exists(path + ".1"));
            Assert.False(File.Exists(path + ".2"));
            Assert.False(File.Exists(path + ".3"));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }
}
