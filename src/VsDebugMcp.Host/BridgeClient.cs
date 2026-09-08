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

    public Task<GetFilesInProjectResponse> GetFilesInProjectAsync(
        GetFilesInProjectRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<GetFilesInProjectRequest, GetFilesInProjectResponse>(
            BridgeMethods.GetFilesInProject,
            request,
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

    public Task<DebuggerGetInfoResponse> DebuggerGetInfoAsync(CancellationToken cancellationToken) =>
        CallAsync<DebuggerGetInfoRequest, DebuggerGetInfoResponse>(
            BridgeMethods.DebuggerGetInfo,
            new DebuggerGetInfoRequest(),
            cancellationToken);

    public Task<DebuggerSetBreakpointsResponse> DebuggerSetBreakpointsAsync(
        DebuggerSetBreakpointsRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<DebuggerSetBreakpointsRequest, DebuggerSetBreakpointsResponse>(
            BridgeMethods.DebuggerSetBreakpoints,
            request,
            cancellationToken);

    public Task<DebuggerListBreakpointsResponse> DebuggerListBreakpointsAsync(
        DebuggerListBreakpointsRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<DebuggerListBreakpointsRequest, DebuggerListBreakpointsResponse>(
            BridgeMethods.DebuggerListBreakpoints,
            request,
            cancellationToken);

    public Task<DebuggerClearBreakpointsResponse> DebuggerClearBreakpointsAsync(
        DebuggerClearBreakpointsRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<DebuggerClearBreakpointsRequest, DebuggerClearBreakpointsResponse>(
            BridgeMethods.DebuggerClearBreakpoints,
            request,
            cancellationToken);

    public Task<DebuggerToggleBreakpointResponse> DebuggerToggleBreakpointAsync(
        DebuggerToggleBreakpointRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<DebuggerToggleBreakpointRequest, DebuggerToggleBreakpointResponse>(
            BridgeMethods.DebuggerToggleBreakpoint,
            request,
            cancellationToken);

    public Task<DebuggerGetCallStackResponse> DebuggerGetCallStackAsync(
        DebuggerGetCallStackRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<DebuggerGetCallStackRequest, DebuggerGetCallStackResponse>(
            BridgeMethods.DebuggerGetCallStack,
            request,
            cancellationToken);

    public Task<DebuggerEvaluateExprResponse> DebuggerEvaluateExprAsync(
        DebuggerEvaluateExprRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<DebuggerEvaluateExprRequest, DebuggerEvaluateExprResponse>(
            BridgeMethods.DebuggerEvaluateExpr,
            request,
            cancellationToken);

    public Task<DebuggerExecutionResponse> DebuggerStepOverAsync(
        DebuggerStepRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<DebuggerStepRequest, DebuggerExecutionResponse>(
            BridgeMethods.DebuggerStepOver,
            request,
            cancellationToken);

    public Task<DebuggerExecutionResponse> DebuggerStepIntoAsync(
        DebuggerStepRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<DebuggerStepRequest, DebuggerExecutionResponse>(
            BridgeMethods.DebuggerStepInto,
            request,
            cancellationToken);

    public Task<DebuggerExecutionResponse> DebuggerStepOutAsync(
        DebuggerStepRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<DebuggerStepRequest, DebuggerExecutionResponse>(
            BridgeMethods.DebuggerStepOut,
            request,
            cancellationToken);

    public Task<DebuggerExecutionResponse> DebuggerContinueAsync(
        DebuggerContinueRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<DebuggerContinueRequest, DebuggerExecutionResponse>(
            BridgeMethods.DebuggerContinue,
            request,
            cancellationToken);

    public Task<DebuggerExecutionResponse> DebuggerPauseAsync(
        DebuggerPauseRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<DebuggerPauseRequest, DebuggerExecutionResponse>(
            BridgeMethods.DebuggerPause,
            request,
            cancellationToken);

    public Task<DebuggerExecutionResponse> DebuggerStopAsync(
        DebuggerStopRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<DebuggerStopRequest, DebuggerExecutionResponse>(
            BridgeMethods.DebuggerStop,
            request,
            cancellationToken);

    public Task<DebuggerExecutionResponse> DebuggerStartAsync(
        DebuggerStartRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<DebuggerStartRequest, DebuggerExecutionResponse>(
            BridgeMethods.DebuggerStart,
            request,
            cancellationToken);

    public Task<DebuggerEvaluateExpressionsResponse> DebuggerEvaluateExpressionsAsync(
        DebuggerEvaluateExpressionsRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<DebuggerEvaluateExpressionsRequest, DebuggerEvaluateExpressionsResponse>(
            BridgeMethods.DebuggerEvaluateExpressions,
            request,
            cancellationToken);

    public Task<DebuggerGetLocalsResponse> DebuggerGetLocalsAsync(
        DebuggerGetLocalsRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<DebuggerGetLocalsRequest, DebuggerGetLocalsResponse>(
            BridgeMethods.DebuggerGetLocals,
            request,
            cancellationToken);

    public Task<GetTestsResponse> GetTestsAsync(
        GetTestsRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<GetTestsRequest, GetTestsResponse>(
            BridgeMethods.TestGet,
            request,
            cancellationToken);

    public Task<RunTestsResponse> RunTestsAsync(
        RunTestsRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<RunTestsRequest, RunTestsResponse>(
            BridgeMethods.TestRun,
            request,
            cancellationToken);

    public Task<TestRunStatusResponse> GetTestRunStatusAsync(
        GetTestRunStatusRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<GetTestRunStatusRequest, TestRunStatusResponse>(
            BridgeMethods.TestGetStatus,
            request,
            cancellationToken);

    public Task<CancelTestRunResponse> CancelTestRunAsync(
        CancelTestRunRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<CancelTestRunRequest, CancelTestRunResponse>(
            BridgeMethods.TestCancel,
            request,
            cancellationToken);

    public Task<DebugTestResponse> DebugTestAsync(
        DebugTestRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<DebugTestRequest, DebugTestResponse>(
            BridgeMethods.TestDebug,
            request,
            cancellationToken);

    public Task<DebuggerGetThreadsResponse> DebuggerGetThreadsAsync(
        DebuggerGetThreadsRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<DebuggerGetThreadsRequest, DebuggerGetThreadsResponse>(
            BridgeMethods.DebuggerGetThreads,
            request,
            cancellationToken);

    public Task<DebuggerGetExceptionInfoResponse> DebuggerGetExceptionInfoAsync(
        DebuggerGetExceptionInfoRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<DebuggerGetExceptionInfoRequest, DebuggerGetExceptionInfoResponse>(
            BridgeMethods.DebuggerGetExceptionInfo,
            request,
            cancellationToken);

    public Task<DebuggerGetProcessesResponse> DebuggerGetProcessesAsync(
        DebuggerGetProcessesRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<DebuggerGetProcessesRequest, DebuggerGetProcessesResponse>(
            BridgeMethods.DebuggerGetProcesses,
            request,
            cancellationToken);

    public Task<DebuggerAttachResponse> DebuggerAttachProcessAsync(
        DebuggerAttachRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<DebuggerAttachRequest, DebuggerAttachResponse>(
            BridgeMethods.DebuggerAttachProcess,
            request,
            cancellationToken);

    public Task<DebuggerDetachResponse> DebuggerDetachAsync(
        DebuggerDetachRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<DebuggerDetachRequest, DebuggerDetachResponse>(
            BridgeMethods.DebuggerDetach,
            request,
            cancellationToken);

    public Task<DebuggerGetModulesResponse> DebuggerGetModulesAsync(
        DebuggerGetModulesRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<DebuggerGetModulesRequest, DebuggerGetModulesResponse>(
            BridgeMethods.DebuggerGetModules,
            request,
            cancellationToken);

    public Task<GetActiveDocumentResponse> GetActiveDocumentAsync(
        GetActiveDocumentRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<GetActiveDocumentRequest, GetActiveDocumentResponse>(
            BridgeMethods.GetActiveDocument,
            request,
            cancellationToken);

    public Task<NavigateToResponse> NavigateToAsync(
        NavigateToRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<NavigateToRequest, NavigateToResponse>(
            BridgeMethods.NavigateTo,
            request,
            cancellationToken);

    public Task<GetSolutionConfigurationsResponse> GetSolutionConfigurationsAsync(
        GetSolutionConfigurationsRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<GetSolutionConfigurationsRequest, GetSolutionConfigurationsResponse>(
            BridgeMethods.GetSolutionConfigurations,
            request,
            cancellationToken);

    public Task<DebuggerThreadControlResponse> DebuggerFreezeThreadAsync(
        DebuggerThreadControlRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<DebuggerThreadControlRequest, DebuggerThreadControlResponse>(
            BridgeMethods.DebuggerFreezeThread,
            request,
            cancellationToken);

    public Task<DebuggerThreadControlResponse> DebuggerThawThreadAsync(
        DebuggerThreadControlRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<DebuggerThreadControlRequest, DebuggerThreadControlResponse>(
            BridgeMethods.DebuggerThawThread,
            request,
            cancellationToken);

    public Task<DebuggerSetNextStatementResponse> DebuggerSetNextStatementAsync(
        DebuggerSetNextStatementRequest request,
        CancellationToken cancellationToken) =>
        CallAsync<DebuggerSetNextStatementRequest, DebuggerSetNextStatementResponse>(
            BridgeMethods.DebuggerSetNextStatement,
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