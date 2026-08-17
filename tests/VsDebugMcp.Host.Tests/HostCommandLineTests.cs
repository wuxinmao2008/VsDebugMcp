namespace VsDebugMcp.Host.Tests;

public class HostCommandLineTests
{
    [Fact]
    public void DefaultsToMcpMode()
    {
        var options = HostCommandLine.Parse([]);

        Assert.Equal(HostMode.Mcp, options.Mode);
        Assert.Equal(TimeSpan.FromSeconds(3), options.ConnectTimeout);
    }

    [Fact]
    public void ParsesSmokePipeAndTimeout()
    {
        var options = HostCommandLine.Parse(
            ["--smoke", "--pipe", "test-pipe", "--connect-timeout-seconds", "7"]);

        Assert.Equal(HostMode.Smoke, options.Mode);
        Assert.Equal("test-pipe", options.PipeName);
        Assert.Equal(TimeSpan.FromSeconds(7), options.ConnectTimeout);
    }

    [Fact]
    public void RejectsConflictingModes()
    {
        Assert.Throws<ArgumentException>(() => HostCommandLine.Parse(["--mcp", "--smoke"]));
    }
}