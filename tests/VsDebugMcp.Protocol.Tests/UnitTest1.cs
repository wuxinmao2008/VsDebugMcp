using System.Text;

namespace VsDebugMcp.Protocol.Tests;

public class PipeMessageFramingTests
{
    [Fact]
    public void UsesFixedMcpHostPipeName()
    {
        Assert.Equal("VsDebugMcp.Host.v1", PipeNames.ForMcpHost());
    }

    [Fact]
    public async Task RoundTripsBridgeRequest()
    {
        var expected = new BridgeRequest
        {
            RequestId = "request-1",
            Method = BridgeMethods.Health,
            PayloadJson = BridgeJson.Serialize(new object())
        };
        using var stream = new MemoryStream();

        await PipeMessageFraming.WriteAsync(stream, expected, CancellationToken.None);
        stream.Position = 0;
        var actual = await PipeMessageFraming.ReadAsync<BridgeRequest>(stream, CancellationToken.None);

        Assert.Equal(expected.ProtocolVersion, actual.ProtocolVersion);
        Assert.Equal(expected.RequestId, actual.RequestId);
        Assert.Equal(expected.Method, actual.Method);
        Assert.Equal(expected.PayloadJson, actual.PayloadJson);
    }

    [Fact]
    public async Task RejectsInvalidMessageLength()
    {
        using var stream = new MemoryStream(new byte[4]);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => PipeMessageFraming.ReadAsync<BridgeRequest>(stream, CancellationToken.None));

        Assert.Contains("Invalid message length", exception.Message);
    }

    [Fact]
    public void SerializesCapabilityContract()
    {
        var expected = new CapabilitiesResponse
        {
            BridgeVersion = "0.1.0",
            VisualStudioVersion = "18.9.0",
            Capabilities =
            {
                new CapabilityDescriptor
                {
                    Name = "phase0.ipc",
                    Version = "0.1",
                    IsStub = true
                }
            }
        };

        var json = BridgeJson.Serialize(expected);
        var actual = BridgeJson.Deserialize<CapabilitiesResponse>(json);

        Assert.Equal(BridgeProtocol.Version, actual.ProtocolVersion);
        Assert.Single(actual.Capabilities);
        Assert.Equal("phase0.ipc", actual.Capabilities[0].Name);
    }

    [Fact]
    public void RoundTripsProjectsInSolutionContractWithWarnings()
    {
        var expected = new GetProjectsInSolutionResponse
        {
            VsInstanceId = "1234",
            Solution = new SolutionInfo
            {
                IsOpen = true,
                Name = "Example",
                FilePath = @"C:\source\Example.slnx",
                Directory = @"C:\source",
                ProjectCount = 1
            },
            Projects =
            {
                new SolutionProjectInfo
                {
                    Id = "project-1",
                    Name = "Example.Project",
                    ProjectFilePath = string.Empty,
                    ProjectGuid = "11111111-1111-1111-1111-111111111111",
                    TypeGuid = string.Empty,
                    Kind = "unknown",
                    IsLoaded = true,
                    IsUnsupported = true
                }
            },
            Warnings =
            {
                new BridgeWarning
                {
                    Code = "project_path_unavailable",
                    Message = "The project file path is unavailable.",
                    ProjectId = "project-1"
                }
            }
        };

        var json = BridgeJson.Serialize(expected);
        var actual = BridgeJson.Deserialize<GetProjectsInSolutionResponse>(json);

        Assert.True(actual.Solution.IsOpen);
        Assert.Equal(1, actual.Solution.ProjectCount);
        Assert.Single(actual.Projects);
        Assert.True(actual.Projects[0].IsUnsupported);
        Assert.Single(actual.Warnings);
        Assert.Equal("project-1", actual.Warnings[0].ProjectId);
    }

    [Fact]
    public void RoundTripsClosedSolutionContract()
    {
        var expected = new GetProjectsInSolutionResponse
        {
            VsInstanceId = "1234",
            Solution = new SolutionInfo { IsOpen = false }
        };

        var json = BridgeJson.Serialize(expected);
        var actual = BridgeJson.Deserialize<GetProjectsInSolutionResponse>(json);

        Assert.False(actual.Solution.IsOpen);
        Assert.Empty(actual.Projects);
        Assert.Empty(actual.Warnings);
    }

    [Fact]
    public void RoundTripsBuildTaskContract()
    {
        var expected = new BuildTaskResponse
        {
            BuildTaskId = "build-1",
            VsInstanceId = "1234",
            State = BuildStates.Succeeded,
            Configuration = "Release",
            Platform = "x64",
            RequestedAtUtc = "2026-08-17T06:00:00.0000000Z",
            StartedAtUtc = "2026-08-17T06:00:01.0000000Z",
            CompletedAtUtc = "2026-08-17T06:00:10.0000000Z",
            Succeeded = true
        };

        var json = BridgeJson.Serialize(expected);
        var actual = BridgeJson.Deserialize<BuildTaskResponse>(json);

        Assert.Equal("build-1", actual.BuildTaskId);
        Assert.Equal(BuildStates.Succeeded, actual.State);
        Assert.True(actual.Succeeded);
        Assert.Equal("x64", actual.Platform);
    }

    [Fact]
    public void RoundTripsOptionalBuildRequestAndCancelResponse()
    {
        var request = BridgeJson.Deserialize<RunBuildRequest>(BridgeJson.Serialize(new RunBuildRequest()));
        var expected = new CancelBuildResponse
        {
            Accepted = true,
            Build = new BuildTaskResponse
            {
                BuildTaskId = "build-1",
                State = BuildStates.Cancelling,
                CancelRequested = true
            }
        };

        var actual = BridgeJson.Deserialize<CancelBuildResponse>(BridgeJson.Serialize(expected));

        Assert.Null(request.Configuration);
        Assert.Null(request.Platform);
        Assert.True(actual.Accepted);
        Assert.True(actual.Build.CancelRequested);
        Assert.Equal(BuildStates.Cancelling, actual.Build.State);
    }

    [Fact]
    public void RoundTripsDiagnosticsRequestAndResponse()
    {
        var request = new GetErrorsRequest
        {
            BuildTaskId = "build-1",
            Severities = ["error", "warning"],
            Project = "Example.Project",
            File = @"src\Program.cs",
            MaxCount = 25
        };
        var response = new GetErrorsResponse
        {
            VsInstanceId = "1234",
            BuildTaskId = "build-1",
            SnapshotAtUtc = "2026-08-17T08:00:00.0000000Z",
            TotalCount = 2,
            ReturnedCount = 1,
            Truncated = true,
            Items =
            {
                new VisualStudioDiagnostic
                {
                    Severity = "error",
                    Code = "CS1002",
                    Message = "; expected",
                    Project = "Example.Project",
                    FilePath = @"C:\source\Example\src\Program.cs",
                    Line = 12,
                    Column = 8,
                    BuildTool = "C#"
                }
            }
        };

        var actualRequest = BridgeJson.Deserialize<GetErrorsRequest>(BridgeJson.Serialize(request));
        var actualResponse = BridgeJson.Deserialize<GetErrorsResponse>(BridgeJson.Serialize(response));

        Assert.Equal("build-1", actualRequest.BuildTaskId);
        Assert.Equal(["error", "warning"], actualRequest.Severities);
        Assert.Equal(25, actualRequest.MaxCount);
        Assert.True(actualResponse.Truncated);
        Assert.Equal(2, actualResponse.TotalCount);
        Assert.Single(actualResponse.Items);
        Assert.Equal(12, actualResponse.Items[0].Line);
        Assert.Equal("CS1002", actualResponse.Items[0].Code);
    }

    [Fact]
    public void RoundTripsOutputWindowLogsContract()
    {
        var request = new GetOutputWindowLogsRequest { Source = "build", MaxChars = 4096 };
        var response = new GetOutputWindowLogsResponse
        {
            VsInstanceId = "1234",
            Source = "build",
            CapturedAtUtc = "2026-08-17T09:30:00.0000000Z",
            TotalChars = 12000,
            ReturnedChars = 4096,
            Truncated = true,
            Text = "error C3861: identifier not found"
        };

        var actualRequest = BridgeJson.Deserialize<GetOutputWindowLogsRequest>(BridgeJson.Serialize(request));
        var actualResponse = BridgeJson.Deserialize<GetOutputWindowLogsResponse>(BridgeJson.Serialize(response));

        Assert.Equal("build", actualRequest.Source);
        Assert.Equal(4096, actualRequest.MaxChars);
        Assert.True(actualResponse.Truncated);
        Assert.Equal(4096, actualResponse.ReturnedChars);
        Assert.Contains("C3861", actualResponse.Text);
    }
}