using NexaRun.Shared.Config;

namespace NexaRun.Tests;

public class Pm2LiveImportTests
{
    private const string JlistSample = """
        [
          {
            "name": "api",
            "pm2_env": {
              "name": "api",
              "status": "online",
              "pm_exec_path": "C:/app/server.js",
              "exec_interpreter": "node",
              "pm_cwd": "C:/app",
              "args": ["--port", "3000"],
              "autorestart": true,
              "max_restarts": 5,
              "pm_out_log_path": "C:/logs/out.log",
              "pm_err_log_path": "C:/logs/err.log",
              "max_memory_restart": "512M",
              "env": {
                "NODE_ENV": "production",
                "pm_id": "0",
                "unique_id": "abc"
              }
            }
          },
          {
            "name": "api",
            "pm2_env": {
              "name": "api",
              "status": "online",
              "pm_exec_path": "C:/app/server.js",
              "exec_interpreter": "node",
              "pm_cwd": "C:/app"
            }
          },
          {
            "name": "worker",
            "pm2_env": {
              "name": "worker",
              "status": "stopped",
              "pm_exec_path": "C:/app/worker.js",
              "exec_interpreter": "node"
            }
          },
          {
            "name": "batch",
            "pm2_env": {
              "name": "batch",
              "status": "online",
              "pm_exec_path": "C:/tools/run.bat",
              "exec_interpreter": "none",
              "args": "/silent",
              "pm_cwd": "C:/tools"
            }
          }
        ]
        """;

    private const string DumpSample = """
        [
          {
            "name": "api",
            "pm2_env": {
              "name": "api",
              "pm_exec_path": "C:/saved/server.js",
              "exec_interpreter": "node",
              "pm_cwd": "C:/saved",
              "args": ["--port", "4000"],
              "autorestart": false,
              "max_restarts": 9,
              "pm_out_log_path": "C:/saved/out.log",
              "env": {
                "NODE_ENV": "staging",
                "APP_KEY": "secret"
              }
            }
          },
          {
            "name": "dump-only",
            "pm2_env": {
              "name": "dump-only",
              "pm_exec_path": "C:/other/app.js",
              "exec_interpreter": "node",
              "status": "online"
            }
          }
        ]
        """;

    [Fact]
    public void FromJlistAndDump_maps_online_apps_and_skips_stopped_and_cluster_dupes()
    {
        var result = Pm2LiveImport.FromJlistAndDump(JlistSample, dumpJson: null);

        Assert.Equal(2, result.Options.Count);
        Assert.Equal(1, result.ClusterDuplicatesSkipped);

        var api = result.Options.Single(o => o.Name == "api");
        Assert.Equal("node", api.ExecutablePath);
        Assert.Contains("server.js", api.Arguments);
        Assert.Contains("--port", api.Arguments);
        Assert.Contains("3000", api.Arguments);
        Assert.Equal("C:/app", api.WorkingDirectory);
        Assert.True(api.AutoRestart);
        Assert.Equal(5, api.MaxRestartAttempts);
        Assert.Equal(512, api.MaxMemoryMb);
        Assert.Equal("C:/logs/out.log", api.OutLogFile);
        Assert.Equal("production", api.Environment!["NODE_ENV"]);
        Assert.False(api.Environment.ContainsKey("pm_id"));
        Assert.False(api.Environment.ContainsKey("unique_id"));

        var batch = result.Options.Single(o => o.Name == "batch");
        Assert.Equal("C:/tools/run.bat", batch.ExecutablePath);
        Assert.Equal("/silent", batch.Arguments);
    }

    [Fact]
    public void FromJlistAndDump_prefers_dump_definition_for_online_names()
    {
        var result = Pm2LiveImport.FromJlistAndDump(JlistSample, DumpSample);

        Assert.Equal(2, result.Options.Count);
        Assert.Equal(1, result.EnrichedFromDump);
        Assert.DoesNotContain(result.Options, o => o.Name == "dump-only");

        var api = result.Options.Single(o => o.Name == "api");
        Assert.Equal("C:/saved", api.WorkingDirectory);
        Assert.Contains("C:/saved/server.js", api.Arguments.Replace('\\', '/'));
        Assert.Contains("4000", api.Arguments);
        Assert.False(api.AutoRestart);
        Assert.Equal(9, api.MaxRestartAttempts);
        Assert.Equal("staging", api.Environment!["NODE_ENV"]);
        Assert.Equal("secret", api.Environment["APP_KEY"]);
    }

    [Fact]
    public void FromJlistAndDump_falls_back_to_jlist_when_dump_has_no_match()
    {
        var result = Pm2LiveImport.FromJlistAndDump(JlistSample, DumpSample);
        var batch = result.Options.Single(o => o.Name == "batch");
        Assert.Equal("C:/tools/run.bat", batch.ExecutablePath);
        Assert.Equal("C:/tools", batch.WorkingDirectory);
    }

    [Fact]
    public void FromJlistAndDump_throws_when_no_online_apps()
    {
        var jlist = """
            [{ "name": "x", "pm2_env": { "status": "stopped", "pm_exec_path": "a.js", "exec_interpreter": "node" } }]
            """;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Pm2LiveImport.FromJlistAndDump(jlist, null));
        Assert.Contains("No running PM2 apps", ex.Message);
    }

    [Fact]
    public void FromJlistAndDump_ignores_invalid_dump()
    {
        var result = Pm2LiveImport.FromJlistAndDump(JlistSample, "{ not-json");
        Assert.Equal(2, result.Options.Count);
        Assert.Equal(0, result.EnrichedFromDump);
        Assert.Contains("jlist only", result.Summary);
    }
}
