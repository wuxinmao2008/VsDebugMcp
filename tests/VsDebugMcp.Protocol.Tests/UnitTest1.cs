using System.Text;

namespace VsDebugMcp.Protocol.Tests;

public class PipeMessageFramingTests
{
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
}