using NexaRun.Shared.Config;
using NexaRun.Shared.Models;

namespace NexaRun.Tests;

public class InstanceCloneExpanderTests
{
    [Fact]
    public void Expand_creates_named_clones_with_instance_env()
    {
        var expanded = InstanceCloneExpander.Expand(
        [
            new StartOptions
            {
                Name = "api",
                ExecutablePath = "node",
                Arguments = "server.js",
                Instances = 3,
                Environment = new Dictionary<string, string> { ["NODE_ENV"] = "prod" }
            }
        ]);

        Assert.Equal(3, expanded.Count);
        Assert.Equal(["api", "api-1", "api-2"], expanded.Select(o => o.Name).ToArray());
        Assert.Equal("0", expanded[0].Environment!["NODE_APP_INSTANCE"]);
        Assert.Equal("1", expanded[1].Environment!["NODE_APP_INSTANCE"]);
        Assert.Equal("2", expanded[2].Environment!["NODE_APP_INSTANCE"]);
        Assert.Equal("prod", expanded[0].Environment!["NODE_ENV"]);
        Assert.All(expanded, o => Assert.Null(o.Instances));
    }

    [Fact]
    public void Expand_leaves_single_instance_unchanged()
    {
        var expanded = InstanceCloneExpander.Expand(
        [
            new StartOptions { Name = "web", ExecutablePath = "dotnet", Instances = 1 }
        ]);

        Assert.Single(expanded);
        Assert.Equal("web", expanded[0].Name);
    }
}
