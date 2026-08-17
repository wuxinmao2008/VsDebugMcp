using System.Diagnostics;
using System.IO.Pipes;
using VsDebugMcp.Protocol;

namespace VsDebugMcp.Host;

public sealed class BridgeClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;

    public BridgeClient(string pipeName)
    {
        _pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    }

    public async Task ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(timeout);

        try
        {
            await _pipe.ConnectAsync(timeoutCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BridgeRpcException(
                BridgeErrorCodes.BridgeUnavailable,
                "The Visual Studio bridge is unavailable.",
                true);
        }
    }

    public Task<HandshakeResponse> HandshakeAsync(CancellationToken cancellationToken) =>
        CallAsync<HandshakeRequest, HandshakeResponse>(
            BridgeMethods.Handshake,
            new HandshakeRequest
            {
                HostVersion = typeof(BridgeClient).Assembly.GetName().Version?.ToString() ?? "0.0.0",
                HostProcessId = Process.GetCurrentProcess().Id
            },
            cancellationToken);

    public Task<HealthResponse> GetHealthAsync(CancellationToken cancellationToken) =>
        CallAsync<object, HealthResponse>(BridgeMethods.Health, new object(), cancellationToken);

    public Task<CapabilitiesResponse> GetCapabilitiesAsync(CancellationToken cancellationToken) =>
        CallAsync<object, CapabilitiesResponse>(BridgeMethods.Capabilities, new object(), cancellationToken);

    public Task<GetProjectsInSolutionResponse> GetProjectsInSolutionAsync(CancellationToken cancellationToken) =>
        CallAsync<object, GetProjectsInSolutionResponse>(
            BridgeMethods.GetProjectsInSolution,
            new object(),
            cancellationToken);

    public Task<BuildTaskResponse> RunBuildAsync(
        RunBuildRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<RunBuildRequest, BuildTaskResponse>(BridgeMethods.RunBuild, request, cancellationToken);

    public Task<BuildTaskResponse> GetBuildStatusAsync(
        string buildTaskId,
        CancellationToken cancellationToken) =>
        CallAsync<GetBuildStatusRequest, BuildTaskResponse>(
            BridgeMethods.GetBuildStatus,
            new GetBuildStatusRequest { BuildTaskId = buildTaskId },
            cancellationToken);

    public Task<CancelBuildResponse> CancelBuildAsync(
        string buildTaskId,
        CancellationToken cancellationToken) =>
        CallAsync<CancelBuildRequest, CancelBuildResponse>(
            BridgeMethods.CancelBuild,
            new CancelBuildRequest { BuildTaskId = buildTaskId },
            cancellationToken);

    public Task<GetErrorsResponse> GetErrorsAsync(
        GetErrorsRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<GetErrorsRequest, GetErrorsResponse>(BridgeMethods.GetErrors, request, cancellationToken);

    public Task<GetOutputWindowLogsResponse> GetOutputWindowLogsAsync(
        GetOutputWindowLogsRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<GetOutputWindowLogsRequest, GetOutputWindowLogsResponse>(
            BridgeMethods.GetOutputWindowLogs,
            request,
            cancellationToken);

    public Task<ShutdownResponse> ShutdownAsync(CancellationToken cancellationToken) =>
        CallAsync<object, ShutdownResponse>(BridgeMethods.Shutdown, new object(), cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _pipe.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<TResponse> CallAsync<TRequest, TResponse>(
        string method,
        TRequest payload,
        CancellationToken cancellationToken)
    {
        if (!_pipe.IsConnected)
        {
            throw new BridgeRpcException(
                BridgeErrorCodes.BridgeUnavailable,
                "The bridge is not connected.",
                true);
        }

        var request = new BridgeRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Method = method,
            PayloadJson = BridgeJson.Serialize(payload)
        };

        await PipeMessageFraming.WriteAsync(_pipe, request, cancellationToken).ConfigureAwait(false);
        var response = await PipeMessageFraming.ReadAsync<BridgeResponse>(_pipe, cancellationToken)
            .ConfigureAwait(false);

        if (!string.Equals(response.RequestId, request.RequestId, StringComparison.Ordinal))
        {
            throw new BridgeRpcException(
                BridgeErrorCodes.InvalidRequest,
                "The response request ID does not match.",
                false);
        }

        if (response.Error is not null)
        {
            throw new BridgeRpcException(response.Error.Code, response.Error.Message, response.Error.Retryable);
        }

        return BridgeJson.Deserialize<TResponse>(response.PayloadJson ?? string.Empty);
    }
}

public sealed class BridgeRpcException : Exception
{
    public BridgeRpcException(string code, string message, bool retryable)
        : base(message)
    {
        Code = code;
        Retryable = retryable;
    }

    public string Code { get; }

    public bool Retryable { get; }
}