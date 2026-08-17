using ModelContextProtocol;
using VsDebugMcp.Protocol;

namespace VsDebugMcp.Host.Tests;

public class McpToolsTests
{
    [Fact]
    public async Task ReturnsHealthFromBridgeService()
    {
        var expected = new HealthResponse
        {
            Status = "ok",
            UtcTimestamp = "2026-08-17T00:00:00.0000000Z"
        };
        var tools = new McpTools(new FakeBridgeService { Health = expected });

        var actual = await tools.GetHealthAsync(CancellationToken.None);

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task ReturnsCapabilitiesFromBridgeService()
    {
        var expected = new VsCapabilitiesResult
        {
            HostVersion = "0.1.0",
            BridgeVersion = "0.1.0",
            VisualStudioVersion = "18.9.0",
            VisualStudioProcessId = 1234,
            VsInstanceId = "1234",
            Capabilities =
            [
                new CapabilityDescriptor
                {
                    Name = "phase0.ipc",
                    Version = "0.1",
                    IsStub = true
                }
            ]
        };
        var tools = new McpTools(new FakeBridgeService { Capabilities = expected });

        var actual = await tools.GetCapabilitiesAsync(CancellationToken.None);

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task ReturnsProjectsFromBridgeService()
    {
        var expected = new GetProjectsInSolutionResponse
        {
            VsInstanceId = "1234",
            Solution = new SolutionInfo { IsOpen = true, Name = "Example", ProjectCount = 1 },
            Projects = { new SolutionProjectInfo { Id = "project-1", Name = "Example.Project" } }
        };
        var tools = new McpTools(new FakeBridgeService { Projects = expected });

        var actual = await tools.GetProjectsInSolutionAsync(CancellationToken.None);

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task ExposesOnlySanitizedBridgeError()
    {
        var tools = new McpTools(
            new FakeBridgeService
            {
                Error = new BridgeServiceException(
                    BridgeErrorCodes.BridgeUnavailable,
                    "The Visual Studio bridge is unavailable.",
                    true,
                    new InvalidOperationException("sensitive internal path"))
            });

        var exception = await Assert.ThrowsAsync<McpException>(
            () => tools.GetHealthAsync(CancellationToken.None));

        Assert.Contains(BridgeErrorCodes.BridgeUnavailable, exception.Message);
        Assert.DoesNotContain("sensitive internal path", exception.Message);
    }

    private sealed class FakeBridgeService : IBridgeService
    {
        public HealthResponse Health { get; init; } = new();

        public VsCapabilitiesResult Capabilities { get; init; } = new();

        public GetProjectsInSolutionResponse Projects { get; init; } = new();

        public BridgeServiceException? Error { get; init; }

        public Task<HealthResponse> GetHealthAsync(CancellationToken cancellationToken) =>
            Error is null ? Task.FromResult(Health) : Task.FromException<HealthResponse>(Error);

        public Task<VsCapabilitiesResult> GetCapabilitiesAsync(CancellationToken cancellationToken) =>
            Error is null ? Task.FromResult(Capabilities) : Task.FromException<VsCapabilitiesResult>(Error);

        public Task<GetProjectsInSolutionResponse> GetProjectsInSolutionAsync(CancellationToken cancellationToken) =>
            Error is null ? Task.FromResult(Projects) : Task.FromException<GetProjectsInSolutionResponse>(Error);
    }
}