using NexaRun.Shared;

namespace NexaRun.Tests;

public class StartEnvParseTests
{
    [Fact]
    public void Parse_maps_key_value_pairs()
    {
        var env = EnvironmentPairParser.Parse(["FOO=bar", "PATH=C:\\tools", "EMPTY="]);
        Assert.NotNull(env);
        Assert.Equal("bar", env!["FOO"]);
        Assert.Equal("C:\\tools", env["PATH"]);
        Assert.Equal("", env["EMPTY"]);
    }

    [Fact]
    public void Parse_skips_invalid_pairs()
    {
        Assert.Null(EnvironmentPairParser.Parse([]));
        Assert.Null(EnvironmentPairParser.Parse(["=nope", "novalue"]));
    }
}
