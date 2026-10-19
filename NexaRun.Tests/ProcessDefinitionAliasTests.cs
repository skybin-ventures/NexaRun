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
}
