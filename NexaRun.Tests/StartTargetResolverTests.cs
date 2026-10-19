using NexaRun.Shared;

namespace NexaRun.Tests;

public class StartTargetResolverTests
{
    [Fact]
    public void Existing_name_wins()
    {
        var kind = StartTargetResolver.Resolve(
            "myapi",
            hasNewAppOptions: false,
            existingNames: ["myapi"],
            idExists: _ => false);
        Assert.Equal(StartTargetKind.Existing, kind);
    }

    [Fact]
    public void Script_path_is_new_executable()
    {
        var kind = StartTargetResolver.Resolve(
            "app.js",
            hasNewAppOptions: false,
            existingNames: [],
            idExists: _ => false);
        Assert.Equal(StartTargetKind.NewExecutable, kind);
    }

    [Fact]
    public void Options_force_new_executable()
    {
        var kind = StartTargetResolver.Resolve(
            "node",
            hasNewAppOptions: true,
            existingNames: [],
            idExists: _ => false);
        Assert.Equal(StartTargetKind.NewExecutable, kind);
    }

    [Fact]
    public void LooksLikeExecutableOrScript_detects_extensions()
    {
        Assert.True(StartTargetResolver.LooksLikeExecutableOrScript("server.js"));
        Assert.True(StartTargetResolver.LooksLikeExecutableOrScript(@".\bin\app.exe"));
        Assert.False(StartTargetResolver.LooksLikeExecutableOrScript("myapi"));
    }
}
