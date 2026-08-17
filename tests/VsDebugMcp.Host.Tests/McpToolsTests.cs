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
    public async Task RunsQueriesAndCancelsBuildThroughBridgeService()
    {
        var build = new BuildTaskResponse
        {
            BuildTaskId = "build-1",
            State = BuildStates.Running,
            Configuration = "Release",
            Platform = "x64"
        };
        var cancel = new CancelBuildResponse { Accepted = true, Build = build };
        var service = new FakeBridgeService { Build = build, Cancel = cancel };
        var tools = new McpTools(service);

        var started = await tools.RunBuildAsync("Release", "x64", CancellationToken.None);
        var status = await tools.GetBuildStatusAsync("build-1", CancellationToken.None);
        var cancelled = await tools.CancelBuildAsync("build-1", CancellationToken.None);

        Assert.Same(build, started);
        Assert.Same(build, status);
        Assert.Same(cancel, cancelled);
        Assert.Equal("Release", service.Configuration);
        Assert.Equal("x64", service.Platform);
        Assert.Equal("build-1", service.BuildTaskId);
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

    [Fact]
    public async Task ReturnsBuildDiagnosticsAndForwardsFilters()
    {
        var expected = new GetErrorsResponse
        {
            VsInstanceId = "1234",
            BuildTaskId = "not-a-real-build",
            TotalCount = 1,
            ReturnedCount = 1,
            Items = { new VisualStudioDiagnostic { Severity = "warning", Code = "C4996" } }
        };
        var service = new FakeBridgeService { Errors = expected };
        var tools = new McpTools(service);

        var actual = await tools.GetErrorsAsync(
            "not-a-real-build",
            ["warning"],
            "Example.Project",
            @"src\main.cpp",
            10,
            CancellationToken.None);

        Assert.Same(expected, actual);
        Assert.Equal("not-a-real-build", service.DiagnosticsBuildTaskId);
        Assert.Equal(["warning"], service.Severities);
        Assert.Equal("Example.Project", service.Project);
        Assert.Equal(@"src\main.cpp", service.File);
        Assert.Equal(10, service.MaxCount);
    }

    [Fact]
    public async Task SanitizesDiagnosticsUnavailableError()
    {
        var tools = new McpTools(
            new FakeBridgeService
            {
                Error = new BridgeServiceException(
                    BridgeErrorCodes.DiagnosticsUnavailable,
                    "The Visual Studio diagnostics snapshot is unavailable.",
                    true,
                    new InvalidOperationException(@"sensitive C:\source\file.cpp"))
            });

        var exception = await Assert.ThrowsAsync<McpException>(
            () => tools.GetErrorsAsync(null, null, null, null, null, CancellationToken.None));

        Assert.Contains(BridgeErrorCodes.DiagnosticsUnavailable, exception.Message);
        Assert.DoesNotContain("sensitive", exception.Message);
        Assert.DoesNotContain(@"C:\source", exception.Message);
    }

    private sealed class FakeBridgeService : IBridgeService
    {
        public HealthResponse Health { get; init; } = new();

        public VsCapabilitiesResult Capabilities { get; init; } = new();

        public GetProjectsInSolutionResponse Projects { get; init; } = new();

        public BuildTaskResponse Build { get; init; } = new();

        public CancelBuildResponse Cancel { get; init; } = new();

        public GetErrorsResponse Errors { get; init; } = new();

        public string? Configuration { get; private set; }

        public string? Platform { get; private set; }

        public string? BuildTaskId { get; private set; }

        public string? DiagnosticsBuildTaskId { get; private set; }

        public IReadOnlyList<string>? Severities { get; private set; }

        public string? Project { get; private set; }

        public string? File { get; private set; }

        public int? MaxCount { get; private set; }

        public BridgeServiceException? Error { get; init; }

        public Task<HealthResponse> GetHealthAsync(CancellationToken cancellationToken) =>
            Error is null ? Task.FromResult(Health) : Task.FromException<HealthResponse>(Error);

        public Task<VsCapabilitiesResult> GetCapabilitiesAsync(CancellationToken cancellationToken) =>
            Error is null ? Task.FromResult(Capabilities) : Task.FromException<VsCapabilitiesResult>(Error);

        public Task<GetProjectsInSolutionResponse> GetProjectsInSolutionAsync(CancellationToken cancellationToken) =>
            Error is null ? Task.FromResult(Projects) : Task.FromException<GetProjectsInSolutionResponse>(Error);

        public Task<BuildTaskResponse> RunBuildAsync(
            string? configuration,
            string? platform,
            CancellationToken cancellationToken)
        {
            Configuration = configuration;
            Platform = platform;
            return Error is null ? Task.FromResult(Build) : Task.FromException<BuildTaskResponse>(Error);
        }

        public Task<BuildTaskResponse> GetBuildStatusAsync(
            string buildTaskId,
            CancellationToken cancellationToken)
        {
            BuildTaskId = buildTaskId;
            return Error is null ? Task.FromResult(Build) : Task.FromException<BuildTaskResponse>(Error);
        }

        public Task<CancelBuildResponse> CancelBuildAsync(
            string buildTaskId,
            CancellationToken cancellationToken)
        {
            BuildTaskId = buildTaskId;
            return Error is null ? Task.FromResult(Cancel) : Task.FromException<CancelBuildResponse>(Error);
        }

        public Task<GetErrorsResponse> GetErrorsAsync(
            string? buildTaskId,
            IReadOnlyList<string>? severities,
            string? project,
            string? file,
            int? maxCount,
            CancellationToken cancellationToken)
        {
            DiagnosticsBuildTaskId = buildTaskId;
            Severities = severities;
            Project = project;
            File = file;
            MaxCount = maxCount;
            return Error is null ? Task.FromResult(Errors) : Task.FromException<GetErrorsResponse>(Error);
        }
    }
}