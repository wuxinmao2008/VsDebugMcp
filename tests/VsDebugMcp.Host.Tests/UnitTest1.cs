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

    [Fact]
    public async Task RunsQueriesAndCancelsBuildAgainstPipeServer()
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

            var runRequest = await PipeMessageFraming.ReadAsync<BridgeRequest>(server, cancellation.Token);
            Assert.Equal(BridgeMethods.RunBuild, runRequest.Method);
            var runPayload = BridgeJson.Deserialize<RunBuildRequest>(runRequest.PayloadJson!);
            Assert.Equal("Release", runPayload.Configuration);
            Assert.Equal("x64", runPayload.Platform);
            await PipeMessageFraming.WriteAsync(
                server,
                BridgeResponse.Success(runRequest.RequestId, CreateBuild(BuildStates.Running)),
                cancellation.Token);

            var statusRequest = await PipeMessageFraming.ReadAsync<BridgeRequest>(server, cancellation.Token);
            Assert.Equal(BridgeMethods.GetBuildStatus, statusRequest.Method);
            Assert.Equal("build-1", BridgeJson.Deserialize<GetBuildStatusRequest>(statusRequest.PayloadJson!).BuildTaskId);
            await PipeMessageFraming.WriteAsync(
                server,
                BridgeResponse.Success(statusRequest.RequestId, CreateBuild(BuildStates.Running)),
                cancellation.Token);

            var cancelRequest = await PipeMessageFraming.ReadAsync<BridgeRequest>(server, cancellation.Token);
            Assert.Equal(BridgeMethods.CancelBuild, cancelRequest.Method);
            Assert.Equal("build-1", BridgeJson.Deserialize<CancelBuildRequest>(cancelRequest.PayloadJson!).BuildTaskId);
            await PipeMessageFraming.WriteAsync(
                server,
                BridgeResponse.Success(
                    cancelRequest.RequestId,
                    new CancelBuildResponse { Accepted = true, Build = CreateBuild(BuildStates.Cancelling) }),
                cancellation.Token);
        }, cancellation.Token);

        await using var client = new BridgeClient(pipeName);
        await client.ConnectAsync(TimeSpan.FromSeconds(2), cancellation.Token);
        var started = await client.RunBuildAsync(
            new RunBuildRequest { Configuration = "Release", Platform = "x64" },
            cancellation.Token);
        var status = await client.GetBuildStatusAsync(started.BuildTaskId, cancellation.Token);
        var cancelled = await client.CancelBuildAsync(started.BuildTaskId, cancellation.Token);

        Assert.Equal(BuildStates.Running, status.State);
        Assert.True(cancelled.Accepted);
        Assert.Equal(BuildStates.Cancelling, cancelled.Build.State);
        await serverTask;
    }

    [Fact]
    public async Task GetsBuildDiagnosticsAgainstPipeServer()
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
            Assert.Equal(BridgeMethods.GetErrors, request.Method);
            var payload = BridgeJson.Deserialize<GetErrorsRequest>(request.PayloadJson!);
            Assert.Equal("build-any", payload.BuildTaskId);
            Assert.Equal(["error"], payload.Severities);
            Assert.Equal("Example.Project", payload.Project);
            Assert.Equal(@"src\Program.cs", payload.File);
            Assert.Equal(10, payload.MaxCount);

            await PipeMessageFraming.WriteAsync(
                server,
                BridgeResponse.Success(
                    request.RequestId,
                    new GetErrorsResponse
                    {
                        VsInstanceId = "1234",
                        BuildTaskId = payload.BuildTaskId,
                        SnapshotAtUtc = "2026-08-17T08:00:00.0000000Z",
                        TotalCount = 1,
                        ReturnedCount = 1,
                        Items = { new VisualStudioDiagnostic { Severity = "error", Code = "CS1002" } }
                    }),
                cancellation.Token);
        }, cancellation.Token);

        await using var client = new BridgeClient(pipeName);
        await client.ConnectAsync(TimeSpan.FromSeconds(2), cancellation.Token);
        var result = await client.GetErrorsAsync(
            new GetErrorsRequest
            {
                BuildTaskId = "build-any",
                Severities = ["error"],
                Project = "Example.Project",
                File = @"src\Program.cs",
                MaxCount = 10
            },
            cancellation.Token);

        Assert.Equal("build-any", result.BuildTaskId);
        Assert.Single(result.Items);
        Assert.Equal("CS1002", result.Items[0].Code);
        await serverTask;
    }

    private static BuildTaskResponse CreateBuild(string state) => new()
    {
        BuildTaskId = "build-1",
        VsInstanceId = "1234",
        State = state,
        Configuration = "Release",
        Platform = "x64",
        RequestedAtUtc = "2026-08-17T06:00:00.0000000Z"
    };
}