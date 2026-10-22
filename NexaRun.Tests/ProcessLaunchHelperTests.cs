using NexaRun.Shared;

namespace NexaRun.Tests;

public class ProcessLaunchHelperTests
{
    [Theory]
    [InlineData("app.js", "node")]
    [InlineData("app.mjs", "node")]
    [InlineData("app.cjs", "node")]
    [InlineData("app.py", "python")]
    [InlineData("lib.dll", "dotnet")]
    public void ResolveLaunchTarget_wraps_script_with_interpreter(string scriptName, string expectedInterpreter)
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexarun-launch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var script = Path.Combine(dir, scriptName);
            File.WriteAllText(script, "// test");

            var (fileName, args) = ProcessLaunchHelper.ResolveLaunchTarget(script, "a b", dir);

            Assert.Contains(expectedInterpreter, fileName, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(scriptName, args, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("a b", args);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ResolveLaunchTarget_wraps_cmd_with_cmd_exe()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexarun-launch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var script = Path.Combine(dir, "run.cmd");
            File.WriteAllText(script, "@echo off");

            var (fileName, args) = ProcessLaunchHelper.ResolveLaunchTarget(script, "x", dir);

            Assert.Contains("cmd", fileName, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("/c ", args);
            Assert.Contains("run.cmd", args, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ResolveLaunchTarget_leaves_exe_unchanged()
    {
        var (fileName, args) = ProcessLaunchHelper.ResolveLaunchTarget(
            "C:\\Windows\\System32\\notepad.exe",
            "file.txt",
            @"C:\Windows");

        Assert.Equal("C:\\Windows\\System32\\notepad.exe", fileName);
        Assert.Equal("file.txt", args);
    }

    [Fact]
    public void QuoteIfNeeded_uses_createprocess_doubled_quotes()
    {
        Assert.Equal("plain", ProcessLaunchHelper.QuoteIfNeeded("plain"));
        Assert.Equal("\"a b\"", ProcessLaunchHelper.QuoteIfNeeded("a b"));
        Assert.Equal("\"say \"\"hi\"\"\"", ProcessLaunchHelper.QuoteIfNeeded("say \"hi\""));
    }

    [Fact]
    public void ResolveExecutable_uses_path_overlay()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexarun-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var fakeNode = Path.Combine(dir, "node.exe");
            File.WriteAllText(fakeNode, "");

            var resolved = ProcessLaunchHelper.ResolveExecutable("node", workingDirectory: dir, pathOverlay: dir);
            Assert.True(string.Equals(fakeNode, resolved, StringComparison.OrdinalIgnoreCase), resolved);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void CreateStartInfo_resolves_interpreter_via_env_PATH()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexarun-envpath-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var script = Path.Combine(dir, "app.js");
            File.WriteAllText(script, "console.log(1)");
            var fakeNode = Path.Combine(dir, "node.exe");
            File.WriteAllText(fakeNode, "");

            var psi = ProcessLaunchHelper.CreateStartInfo(
                script,
                "",
                dir,
                redirectOutput: false,
                environment: new Dictionary<string, string> { ["PATH"] = dir });

            Assert.True(string.Equals(fakeNode, psi.FileName, StringComparison.OrdinalIgnoreCase), psi.FileName);
            Assert.Contains("app.js", psi.Arguments, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ResolveLaunchTarget_quotes_script_path_with_spaces()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexarun launch " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var script = Path.Combine(dir, "my app.js");
            File.WriteAllText(script, "// test");
            var fakeNode = Path.Combine(dir, "node.exe");
            File.WriteAllText(fakeNode, "");

            var (fileName, args) = ProcessLaunchHelper.ResolveLaunchTarget(script, "", dir, pathOverlay: dir);

            Assert.True(string.Equals(fakeNode, fileName, StringComparison.OrdinalIgnoreCase), fileName);
            Assert.StartsWith("\"", args.TrimStart());
            Assert.Contains("my app.js", args, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ResolveLaunchTarget_wraps_ps1()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexarun-launch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var script = Path.Combine(dir, "task.ps1");
            File.WriteAllText(script, "Write-Host hi");

            var (fileName, args) = ProcessLaunchHelper.ResolveLaunchTarget(script, "", dir);

            Assert.True(
                fileName.Contains("pwsh", StringComparison.OrdinalIgnoreCase) ||
                fileName.Contains("powershell", StringComparison.OrdinalIgnoreCase),
                $"Expected powershell host, got: {fileName}");
            Assert.Contains("-File", args, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("task.ps1", args, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }
}
