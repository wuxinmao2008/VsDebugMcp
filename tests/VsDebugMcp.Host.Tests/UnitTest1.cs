using System.IO.Pipes;
using VsDebugMcp.Protocol;

namespace VsDebugMcp.Host.Tests;

public class BridgeClientTests
{
    [Fact]
    public async Task CompletesHandshakeAgainstPipeServer()
    {
        var pipeName = $"VsDebugMcp.Tests.{Guid.NewGuid():N}";
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(cancellation.Token);
            var request = await PipeMessageFraming.ReadAsync<BridgeRequest>(server, cancellation.Token);
            Assert.Equal(BridgeMethods.Handshake, request.Method);

            var response = BridgeResponse.Success(
                request.RequestId,
                new HandshakeResponse
                {
                    BridgeVersion = "0.1.0",
                    VisualStudioVersion = "18.9.0",
                    VisualStudioProcessId = 1234,
                    VsInstanceId = "1234"
                });
            await PipeMessageFraming.WriteAsync(server, response, cancellation.Token);
        }, cancellation.Token);

        await using var client = new BridgeClient(pipeName);
        await client.ConnectAsync(TimeSpan.FromSeconds(2), cancellation.Token);
        var handshake = await client.HandshakeAsync(cancellation.Token);

        Assert.Equal("18.9.0", handshake.VisualStudioVersion);
        Assert.Equal(1234, handshake.VisualStudioProcessId);
        await serverTask;
    }

    [Fact]
    public async Task ReportsUnavailableBridgeAfterTimeout()
    {
        await using var client = new BridgeClient($"VsDebugMcp.Missing.{Guid.NewGuid():N}");

        var exception = await Assert.ThrowsAsync<BridgeRpcException>(
            () => client.ConnectAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None));

        Assert.Equal(BridgeErrorCodes.BridgeUnavailable, exception.Code);
        Assert.True(exception.Retryable);
    }

    [Fact]
    public async Task GetsProjectsInSolutionAgainstPipeServer()
    {
        var pipeName = $"VsDebugMcp.Tests.{Guid.NewGuid():N}";
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(cancellation.Token);
            var request = await PipeMessageFraming.ReadAsync<BridgeRequest>(server, cancellation.Token);
            Assert.Equal(BridgeMethods.GetProjectsInSolution, request.Method);

            var response = BridgeResponse.Success(
                request.RequestId,
                new GetProjectsInSolutionResponse
                {
                    VsInstanceId = "1234",
                    Solution = new SolutionInfo { IsOpen = true, Name = "Example", ProjectCount = 1 },
                    Projects = { new SolutionProjectInfo { Id = "project-1", Name = "Example.Project" } }
                });
            await PipeMessageFraming.WriteAsync(server, response, cancellation.Token);
        }, cancellation.Token);

        await using var client = new BridgeClient(pipeName);
        await client.ConnectAsync(TimeSpan.FromSeconds(2), cancellation.Token);
        var result = await client.GetProjectsInSolutionAsync(cancellation.Token);

        Assert.True(result.Solution.IsOpen);
        Assert.Single(result.Projects);
        Assert.Equal("Example.Project", result.Projects[0].Name);
        await serverTask;
    }
}