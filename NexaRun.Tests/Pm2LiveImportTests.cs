using NexaRun.Shared.Config;

namespace NexaRun.Tests;

public class Pm2LiveImportTests
{
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
              "max_memory_restart": "512M",
              "env": {
                "NODE_ENV": "staging",
                "APP_KEY": "secret",
                "pm_id": "0",
                "unique_id": "abc"
              }
            }
          },
          {
            "name": "api",
            "pm2_env": {
              "name": "api",
              "pm_exec_path": "C:/saved/server.js",
              "exec_interpreter": "node",
              "pm_cwd": "C:/saved"
            }
          },
          {
            "name": "batch",
            "pm2_env": {
              "name": "batch",
              "pm_exec_path": "C:/tools/run.bat",
              "exec_interpreter": "none",
              "args": "/silent",
              "pm_cwd": "C:/tools"
            }
          },
          {
            "name": "pm2-logrotate",
            "pm2_env": {
              "name": "pm2-logrotate",
              "pm_exec_path": "C:/mod.js",
              "exec_interpreter": "node"
            }
          }
        ]
        """;

    [Fact]
    public void FromDump_maps_apps_skips_internal_and_cluster_dupes()
    {
        var result = Pm2LiveImport.FromDump(DumpSample);

        Assert.Equal(2, result.Options.Count);
        Assert.Equal(1, result.ClusterDuplicatesSkipped);
        Assert.Contains("dump.pm2", result.Summary);

        var api = result.Options.Single(o => o.Name == "api");
        Assert.Equal("node", api.ExecutablePath);
        Assert.Contains("server.js", api.Arguments);
        Assert.Contains("4000", api.Arguments);
        Assert.Equal("C:/saved", api.WorkingDirectory);
        Assert.False(api.AutoRestart);
        Assert.Equal(9, api.MaxRestartAttempts);
        Assert.Equal(512, api.MaxMemoryMb);
        Assert.Equal("staging", api.Environment!["NODE_ENV"]);
        Assert.Equal("secret", api.Environment["APP_KEY"]);
        Assert.False(api.Environment.ContainsKey("pm_id"));
        Assert.False(api.Environment.ContainsKey("unique_id"));

        var batch = result.Options.Single(o => o.Name == "batch");
        Assert.Equal("C:/tools/run.bat", batch.ExecutablePath);
        Assert.Equal("/silent", batch.Arguments);
        Assert.Equal("C:/tools", batch.WorkingDirectory);
    }

    [Fact]
    public void FromDump_throws_on_empty_list()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Pm2LiveImport.FromDump("[]"));
        Assert.Contains("No apps found", ex.Message);
    }

    [Fact]
    public void FromDump_throws_on_invalid_json()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Pm2LiveImport.FromDump("{ not-json"));
        Assert.Contains("Unexpected PM2 dump format", ex.Message);
    }

    [Fact]
    public void LooksLikeDumpPath_detects_pm2_extension()
    {
        Assert.True(Pm2LiveImport.LooksLikeDumpPath(@"C:\TEMP\dump.pm2"));
        Assert.False(Pm2LiveImport.LooksLikeDumpPath(@"C:\TEMP\nexarun-processes.json"));
    }
}
