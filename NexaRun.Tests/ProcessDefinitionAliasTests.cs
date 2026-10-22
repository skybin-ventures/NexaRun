using System.Text.Json;
using NexaRun.Shared.Config;

namespace NexaRun.Tests;

public class ProcessDefinitionAliasTests
{
    [Fact]
    public void ApplyPm2Aliases_maps_ecosystem_fields()
    {
        var json = """
            {
              "name": "api",
              "script": "node",
              "args": "server.js",
              "cwd": "C:/app",
              "autorestart": false,
              "max_restarts": 7,
              "out_file": "out.log",
              "error_file": "err.log",
              "env": { "NODE_ENV": "production" }
            }
            """;

        var def = JsonSerializer.Deserialize<ProcessDefinition>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;
        def.ApplyPm2Aliases();

        Assert.Equal("server.js", def.Arguments);
        Assert.Equal("C:/app", def.WorkingDirectory);
        Assert.False(def.AutoRestart);
        Assert.Equal(7, def.MaxRestartAttempts);
        Assert.Equal("out.log", def.OutLogFile);
        Assert.Equal("err.log", def.ErrorLogFile);
        Assert.Equal("production", def.Environment!["NODE_ENV"]);
    }

    [Fact]
    public void Transfer_preserves_ready_fields()
    {
        var def = new ProcessDefinition
        {
            Name = "api",
            Script = "dotnet",
            ReadyPort = 5000,
            ReadyFile = @"C:\ready",
            ReadyTimeoutMs = 15_000,
            RestartDelayMs = 5_000
        };

        var options = ProcessConfigTransfer.ToStartOptions(def);
        Assert.Equal(5000, options.ReadyPort);
        Assert.Equal(@"C:\ready", options.ReadyFile);
        Assert.Equal(15_000, options.ReadyTimeoutMs);
        Assert.Equal(5_000, options.RestartDelayMs);

        var roundTrip = ProcessConfigTransfer.FromProcess(new NexaRun.Shared.Models.NexaProcess
        {
            Name = "api",
            ExecutablePath = "dotnet",
            ReadyPort = 5000,
            ReadyFile = @"C:\ready",
            ReadyTimeoutMs = 15_000,
            RestartDelayMs = 5_000
        });
        Assert.Equal(5000, roundTrip.ReadyPort);
        Assert.Equal(@"C:\ready", roundTrip.ReadyFile);
        Assert.Equal(15_000, roundTrip.ReadyTimeoutMs);
        Assert.Equal(5_000, roundTrip.RestartDelayMs);
    }

    [Fact]
    public void ApplyPm2Aliases_interpreter_and_node_args()
    {
        var json = """
            {
              "name": "web",
              "script": "server.js",
              "interpreter": "node",
              "node_args": "--inspect",
              "args": "--port 3000"
            }
            """;

        var def = JsonSerializer.Deserialize<ProcessDefinition>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;
        def.ApplyPm2Aliases();

        Assert.Equal("node", def.Script);
        Assert.Contains("server.js", def.Arguments, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--inspect", def.Arguments);
        Assert.Contains("--port 3000", def.Arguments);
    }

    [Fact]
    public void SelectNamedEnvironment_merges_env_production()
    {
        var json = """
            {
              "name": "api",
              "script": "node",
              "env": { "SHARED": "1", "NODE_ENV": "development" },
              "env_production": { "NODE_ENV": "production", "PORT": "80" }
            }
            """;

        var def = JsonSerializer.Deserialize<ProcessDefinition>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;
        def.ApplyPm2Aliases();
        def.SelectNamedEnvironment("production");

        Assert.Equal("1", def.Environment!["SHARED"]);
        Assert.Equal("production", def.Environment["NODE_ENV"]);
        Assert.Equal("80", def.Environment["PORT"]);
    }

    [Fact]
    public void ApplyPm2Aliases_maps_restart_delay()
    {
        var json = """
            {
              "name": "api",
              "script": "app.exe",
              "restart_delay": 4000
            }
            """;

        var def = JsonSerializer.Deserialize<ProcessDefinition>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;
        def.ApplyPm2Aliases();
        Assert.Equal(4000, def.RestartDelayMs);
    }

    [Fact]
    public void ApplyPm2Aliases_maps_watch_cron_instances()
    {
        var json = """
            {
              "name": "api",
              "script": "server.js",
              "interpreter": "node",
              "instances": 2,
              "watch": true,
              "watch_path": "C:/app",
              "cron_restart": "0 4 * * *"
            }
            """;

        var def = JsonSerializer.Deserialize<ProcessDefinition>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;
        def.ApplyPm2Aliases();

        Assert.Equal(2, def.Instances);
        Assert.True(def.Watch);
        Assert.Equal("C:/app", def.WatchPath);
        Assert.Equal("0 4 * * *", def.CronRestart);
        Assert.Equal("node", def.Script);
    }
}

public class NamedEnvironmentSelectorTests
{
    [Fact]
    public void Merge_finds_block_by_short_or_prefixed_name()
    {
        var blocks = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["env_staging"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["X"] = "1"
            }
        };

        var merged = NamedEnvironmentSelector.Merge(
            new Dictionary<string, string> { ["Y"] = "2" },
            blocks,
            "staging");

        Assert.Equal("1", merged!["X"]);
        Assert.Equal("2", merged["Y"]);
    }
}
