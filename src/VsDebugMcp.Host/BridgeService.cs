using System.IO;
using System.Runtime.Serialization;
using VsDebugMcp.Protocol;

namespace VsDebugMcp.Host;

public interface IBridgeService
{
    Task<VsHealthResult> GetHealthAsync(string? vsInstanceId, CancellationToken cancellationToken);

    Task<VsCapabilitiesResult> GetCapabilitiesAsync(string? vsInstanceId, CancellationToken cancellationToken);

    Task<VsInstancesResult> ListInstancesAsync(CancellationToken cancellationToken);

    Task<VsInstancesResult> FindInstancesAsync(string? query, CancellationToken cancellationToken);

    Task<GetProjectsInSolutionResponse> GetProjectsInSolutionAsync(
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<GetFilesInProjectResponse> GetFilesInProjectAsync(
        string? projectId,
        string? extensionFilter,
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<BuildTaskResponse> RunBuildAsync(
        string? configuration,
        string? platform,
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<BuildTaskResponse> RunBuildAndWaitAsync(
        string? configuration,
        string? platform,
        int timeoutSeconds,
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<BuildTaskResponse> GetBuildStatusAsync(
        string buildTaskId,
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<CancelBuildResponse> CancelBuildAsync(
        string buildTaskId,
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<GetErrorsResponse> GetErrorsAsync(
        string? buildTaskId,
        IReadOnlyList<string>? severities,
        string? project,
        string? file,
        int? maxCount,
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<GetOutputWindowLogsResponse> GetOutputWindowLogsAsync(
        string? source,
        int? maxChars,
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<DebuggerGetInfoResponse> DebuggerGetInfoAsync(
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<DebuggerSetBreakpointsResponse> DebuggerSetBreakpointsAsync(
        string filePath,
        List<BreakpointSpec> breakpoints,
        bool clearExisting,
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<DebuggerListBreakpointsResponse> DebuggerListBreakpointsAsync(
        string? filePath,
        bool enabledOnly,
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<DebuggerClearBreakpointsResponse> DebuggerClearBreakpointsAsync(
        bool clearAll,
        string? filePath,
        int? line,
        string? breakpointId,
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<DebuggerToggleBreakpointResponse> DebuggerToggleBreakpointAsync(
        string? breakpointId,
        string? filePath,
        int? line,
        bool? enabled,
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<DebuggerGetCallStackResponse> DebuggerGetCallStackAsync(
        int? threadId,
        int? maxFrames,
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<DebuggerEvaluateExprResponse> DebuggerEvaluateExprAsync(
        string expression,
        int? frameIndex,
        int? timeoutMs,
        bool allowSideEffects,
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<DebuggerExecutionResponse> DebuggerStepOverAsync(
        bool waitForBreak,
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<DebuggerExecutionResponse> DebuggerStepIntoAsync(
        bool waitForBreak,
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<DebuggerExecutionResponse> DebuggerStepOutAsync(
        bool waitForBreak,
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<DebuggerExecutionResponse> DebuggerContinueAsync(
        bool waitForBreak,
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<DebuggerExecutionResponse> DebuggerPauseAsync(
        bool waitForBreak,
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<DebuggerExecutionResponse> DebuggerStopAsync(
        bool waitForStop,
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<DebuggerExecutionResponse> DebuggerStartAsync(
        bool waitForBreak,
        int? timeoutMs,
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<DebuggerEvaluateExpressionsResponse> DebuggerEvaluateExpressionsAsync(
        List<string> expressions,
        int? frameIndex,
        int? timeoutMs,
        bool allowSideEffects,
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<DebuggerGetLocalsResponse> DebuggerGetLocalsAsync(
        int? frameIndex,
        int? maxCount,
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<GetTestsResponse> GetTestsAsync(
        string? vsInstanceId,
        string? projectName,
        string? filter,
        CancellationToken cancellationToken);

    Task<RunTestsResponse> RunTestsAsync(
        string? vsInstanceId,
        IReadOnlyList<string>? testIds,
        CancellationToken cancellationToken);

    Task<RunTestsResponse> RunTestsAndWaitAsync(
        string? vsInstanceId,
        IReadOnlyList<string>? testIds,
        int timeoutSeconds,
        CancellationToken cancellationToken);

    Task<TestRunStatusResponse> GetTestRunStatusAsync(
        string? vsInstanceId,
        string? testRunId,
        CancellationToken cancellationToken);

    Task<CancelTestRunResponse> CancelTestRunAsync(
        string? vsInstanceId,
        string? testRunId,
        CancellationToken cancellationToken);

    Task<DebugTestResponse> DebugTestAsync(
        string? vsInstanceId,
        string testId,
        bool? waitForBreak,
        int? timeoutMs,
        CancellationToken cancellationToken);

    Task<DebuggerGetThreadsResponse> DebuggerGetThreadsAsync(
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<DebuggerGetExceptionInfoResponse> DebuggerGetExceptionInfoAsync(
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<DebuggerGetProcessesResponse> DebuggerGetProcessesAsync(
        string? vsInstanceId,
        string? processName,
        int? processId,
        bool onlyDebugged,
        int? maxCount,
        CancellationToken cancellationToken);

    Task<DebuggerAttachResponse> DebuggerAttachProcessAsync(
        string? vsInstanceId,
        int? processId,
        string? processName,
        List<string>? engines,
        bool waitForBreak,
        int? breakTimeoutMs,
        CancellationToken cancellationToken);

    Task<DebuggerFindSolutionProcessesResponse> DebuggerFindSolutionProcessesAsync(
        bool startupOnly,
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<DebuggerAutoAttachResponse> DebuggerAutoAttachAsync(
        bool startupOnly,
        List<string>? processNames,
        List<string>? engines,
        bool waitForBreak,
        int? breakTimeoutMs,
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<DebuggerGetSnapshotResponse> DebuggerGetSnapshotAsync(
        bool includeCallStack,
        int? maxFrames,
        bool includeLocals,
        int? maxLocals,
        bool includeRecentLogs,
        int? recentLogLines,
        string? logSource,
        bool includeExceptionInfo,
        bool includeThreads,
        int? maxThreads,
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<DebuggerReadMemoryResponse> DebuggerReadMemoryAsync(
        string address,
        int byteCount,
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<DebuggerDetachResponse> DebuggerDetachAsync(
        string? vsInstanceId,
        int? processId,
        CancellationToken cancellationToken);

    Task<DebuggerGetModulesResponse> DebuggerGetModulesAsync(
        string? vsInstanceId,
        int? processId,
        string? nameFilter,
        bool userCodeOnly,
        int? maxCount,
        CancellationToken cancellationToken);

    Task<GetActiveDocumentResponse> GetActiveDocumentAsync(
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<NavigateToResponse> NavigateToAsync(
        string? vsInstanceId,
        string filePath,
        int? line,
        int? column,
        bool preview,
        CancellationToken cancellationToken);

    Task<GetSolutionConfigurationsResponse> GetSolutionConfigurationsAsync(
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<SetSolutionConfigurationResponse> SetSolutionConfigurationAsync(
        string configuration,
        string? platform,
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<GetOutputPanesResponse> GetOutputPanesAsync(
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<DebuggerThreadControlResponse> DebuggerFreezeThreadAsync(
        int threadId,
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<DebuggerThreadControlResponse> DebuggerThawThreadAsync(
        int threadId,
        string? vsInstanceId,
        CancellationToken cancellationToken);

    Task<DebuggerSetNextStatementResponse> DebuggerSetNextStatementAsync(
        string? filePath,
        int? line,
        int? column,
        string? vsInstanceId,
        CancellationToken cancellationToken);
}

public sealed class BridgeService : IBridgeService
{
    private readonly VsHostOptions _options;
    private readonly VisualStudioInstanceRegistry _registry;

    public BridgeService(VsHostOptions options, VisualStudioInstanceRegistry registry)
    {
        _options = options;
        _registry = registry;
    }

    public async Task<VsHealthResult> GetHealthAsync(
        string? vsInstanceId,
        CancellationToken cancellationToken)
    {
        var selected = _registry.Resolve(vsInstanceId);
        var health = await ExecuteAsync(
            selected,
            client => client.GetHealthAsync(cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return new VsHealthResult
        {
            HostVersion = typeof(BridgeService).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            UtcTimestamp = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            InstanceCount = _registry.List().Count,
            SelectedInstance = selected,
            Bridge = health
        };
    }

    public Task<VsCapabilitiesResult> GetCapabilitiesAsync(
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            async client =>
            {
                var handshake = await client.HandshakeAsync(cancellationToken).ConfigureAwait(false);
                var capabilities = await client.GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
                return new VsCapabilitiesResult
                {
                    HostVersion = typeof(BridgeService).Assembly.GetName().Version?.ToString() ?? "0.0.0",
                    BridgeVersion = handshake.BridgeVersion,
                    VisualStudioVersion = handshake.VisualStudioVersion,
                    VisualStudioProcessId = handshake.VisualStudioProcessId,
                    VsInstanceId = handshake.VsInstanceId,
                    ProtocolVersion = capabilities.ProtocolVersion,
                    Capabilities = capabilities.Capabilities
                };
            },
            cancellationToken);

    public Task<VsInstancesResult> ListInstancesAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new VsInstancesResult { Instances = _registry.List() });

    public Task<VsInstancesResult> FindInstancesAsync(string? query, CancellationToken cancellationToken) =>
        Task.FromResult(new VsInstancesResult { Instances = _registry.Find(query) });

    public Task<GetProjectsInSolutionResponse> GetProjectsInSolutionAsync(
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.GetProjectsInSolutionAsync(cancellationToken),
            cancellationToken);

    public Task<GetFilesInProjectResponse> GetFilesInProjectAsync(
        string? projectId,
        string? extensionFilter,
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId, projectId),
            client => client.GetFilesInProjectAsync(
                new GetFilesInProjectRequest
                {
                    ProjectId = projectId,
                    ExtensionFilter = extensionFilter
                },
                cancellationToken),
            cancellationToken);

    public Task<BuildTaskResponse> RunBuildAsync(
        string? configuration,
        string? platform,
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.RunBuildAsync(
                new RunBuildRequest
                {
                    Configuration = configuration,
                    Platform = platform
                },
                cancellationToken),
            cancellationToken);

    public async Task<BuildTaskResponse> RunBuildAndWaitAsync(
        string? configuration,
        string? platform,
        int timeoutSeconds,
        string? vsInstanceId,
        CancellationToken cancellationToken)
    {
        var build = await RunBuildAsync(configuration, platform, vsInstanceId, cancellationToken).ConfigureAwait(false);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 600));

        while (!BuildStates.IsTerminal(build.State))
        {
            if (sw.Elapsed >= timeout)
            {
                build.DurationSeconds = Math.Round(sw.Elapsed.TotalSeconds, 2);
                return build;
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            build = await GetBuildStatusAsync(build.BuildTaskId, vsInstanceId, cancellationToken).ConfigureAwait(false);
        }

        build.DurationSeconds = Math.Round(sw.Elapsed.TotalSeconds, 2);

        if (build.Succeeded == false)
        {
            try
            {
                var errorsResp = await GetErrorsAsync(
                    build.BuildTaskId,
                    new[] { "error" },
                    null,
                    null,
                    10,
                    vsInstanceId,
                    cancellationToken).ConfigureAwait(false);

                if (errorsResp != null && errorsResp.Items != null)
                {
                    build.ErrorCount = errorsResp.TotalCount > 0 ? errorsResp.TotalCount : errorsResp.Items.Count;
                    build.TopErrors = errorsResp.Items
                        .Take(5)
                        .Select(e => $"{e.FilePath ?? e.Project ?? "build"}({e.Line}): {e.Code} {e.Message}")
                        .ToList();
                }
            }
            catch { }
        }

        return build;
    }

    public Task<BuildTaskResponse> GetBuildStatusAsync(
        string buildTaskId,
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.GetBuildStatusAsync(buildTaskId, cancellationToken),
            cancellationToken);

    public Task<CancelBuildResponse> CancelBuildAsync(
        string buildTaskId,
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.CancelBuildAsync(buildTaskId, cancellationToken),
            cancellationToken);

    public Task<GetErrorsResponse> GetErrorsAsync(
        string? buildTaskId,
        IReadOnlyList<string>? severities,
        string? project,
        string? file,
        int? maxCount,
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId, file ?? project),
            client => client.GetErrorsAsync(
                new GetErrorsRequest
                {
                    BuildTaskId = buildTaskId,
                    Severities = severities?.ToList(),
                    Project = project,
                    File = file,
                    MaxCount = maxCount
                },
                cancellationToken),
            cancellationToken);

    public Task<GetOutputWindowLogsResponse> GetOutputWindowLogsAsync(
        string? source,
        int? maxChars,
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.GetOutputWindowLogsAsync(
                new GetOutputWindowLogsRequest
                {
                    Source = source,
                    MaxChars = maxChars
                },
                cancellationToken),
            cancellationToken);

    public Task<DebuggerGetInfoResponse> DebuggerGetInfoAsync(
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.DebuggerGetInfoAsync(cancellationToken),
            cancellationToken);

    public Task<DebuggerSetBreakpointsResponse> DebuggerSetBreakpointsAsync(
        string filePath,
        List<BreakpointSpec> breakpoints,
        bool clearExisting,
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId, filePath),
            client => client.DebuggerSetBreakpointsAsync(
                new DebuggerSetBreakpointsRequest
                {
                    FilePath = filePath,
                    Breakpoints = breakpoints,
                    ClearExisting = clearExisting
                },
                cancellationToken),
            cancellationToken);

    public Task<DebuggerListBreakpointsResponse> DebuggerListBreakpointsAsync(
        string? filePath,
        bool enabledOnly,
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId, filePath),
            client => client.DebuggerListBreakpointsAsync(
                new DebuggerListBreakpointsRequest
                {
                    FilePath = filePath,
                    EnabledOnly = enabledOnly,
                    VsInstanceId = vsInstanceId
                },
                cancellationToken),
            cancellationToken);

    public Task<DebuggerClearBreakpointsResponse> DebuggerClearBreakpointsAsync(
        bool clearAll,
        string? filePath,
        int? line,
        string? breakpointId,
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId, filePath),
            client => client.DebuggerClearBreakpointsAsync(
                new DebuggerClearBreakpointsRequest
                {
                    ClearAll = clearAll,
                    FilePath = filePath,
                    Line = line,
                    BreakpointId = breakpointId,
                    VsInstanceId = vsInstanceId
                },
                cancellationToken),
            cancellationToken);

    public Task<DebuggerToggleBreakpointResponse> DebuggerToggleBreakpointAsync(
        string? breakpointId,
        string? filePath,
        int? line,
        bool? enabled,
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId, filePath),
            client => client.DebuggerToggleBreakpointAsync(
                new DebuggerToggleBreakpointRequest
                {
                    BreakpointId = breakpointId,
                    FilePath = filePath,
                    Line = line,
                    Enabled = enabled,
                    VsInstanceId = vsInstanceId
                },
                cancellationToken),
            cancellationToken);

    public Task<DebuggerGetCallStackResponse> DebuggerGetCallStackAsync(
        int? threadId,
        int? maxFrames,
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.DebuggerGetCallStackAsync(
                new DebuggerGetCallStackRequest
                {
                    ThreadId = threadId,
                    MaxFrames = maxFrames
                },
                cancellationToken),
            cancellationToken);

    public Task<DebuggerEvaluateExprResponse> DebuggerEvaluateExprAsync(
        string expression,
        int? frameIndex,
        int? timeoutMs,
        bool allowSideEffects,
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.DebuggerEvaluateExprAsync(
                new DebuggerEvaluateExprRequest
                {
                    Expression = expression,
                    FrameIndex = frameIndex,
                    TimeoutMs = timeoutMs,
                    AllowSideEffects = allowSideEffects
                },
                cancellationToken),
            cancellationToken);

    public Task<DebuggerExecutionResponse> DebuggerStepOverAsync(
        bool waitForBreak,
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.DebuggerStepOverAsync(
                new DebuggerStepRequest { WaitForBreak = waitForBreak },
                cancellationToken),
            cancellationToken);

    public Task<DebuggerExecutionResponse> DebuggerStepIntoAsync(
        bool waitForBreak,
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.DebuggerStepIntoAsync(
                new DebuggerStepRequest { WaitForBreak = waitForBreak },
                cancellationToken),
            cancellationToken);

    public Task<DebuggerExecutionResponse> DebuggerStepOutAsync(
        bool waitForBreak,
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.DebuggerStepOutAsync(
                new DebuggerStepRequest { WaitForBreak = waitForBreak },
                cancellationToken),
            cancellationToken);

    public Task<DebuggerExecutionResponse> DebuggerContinueAsync(
        bool waitForBreak,
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.DebuggerContinueAsync(
                new DebuggerContinueRequest { WaitForBreak = waitForBreak },
                cancellationToken),
            cancellationToken);

    public Task<DebuggerExecutionResponse> DebuggerPauseAsync(
        bool waitForBreak,
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.DebuggerPauseAsync(
                new DebuggerPauseRequest { WaitForBreak = waitForBreak },
                cancellationToken),
            cancellationToken);

    public Task<DebuggerExecutionResponse> DebuggerStopAsync(
        bool waitForStop,
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.DebuggerStopAsync(
                new DebuggerStopRequest { WaitForStop = waitForStop },
                cancellationToken),
            cancellationToken);

    public Task<DebuggerExecutionResponse> DebuggerStartAsync(
        bool waitForBreak,
        int? timeoutMs,
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.DebuggerStartAsync(
                new DebuggerStartRequest
                {
                    WaitForBreak = waitForBreak,
                    TimeoutMs = timeoutMs
                },
                cancellationToken),
            cancellationToken);

    public Task<DebuggerEvaluateExpressionsResponse> DebuggerEvaluateExpressionsAsync(
        List<string> expressions,
        int? frameIndex,
        int? timeoutMs,
        bool allowSideEffects,
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.DebuggerEvaluateExpressionsAsync(
                new DebuggerEvaluateExpressionsRequest
                {
                    Expressions = expressions,
                    FrameIndex = frameIndex,
                    TimeoutMs = timeoutMs,
                    AllowSideEffects = allowSideEffects
                },
                cancellationToken),
            cancellationToken);

    public Task<DebuggerGetLocalsResponse> DebuggerGetLocalsAsync(
        int? frameIndex,
        int? maxCount,
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.DebuggerGetLocalsAsync(
                new DebuggerGetLocalsRequest
                {
                    FrameIndex = frameIndex,
                    MaxCount = maxCount
                },
                cancellationToken),
            cancellationToken);

    public Task<GetTestsResponse> GetTestsAsync(
        string? vsInstanceId,
        string? projectName,
        string? filter,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.GetTestsAsync(
                new GetTestsRequest
                {
                    ProjectName = projectName,
                    Filter = filter
                },
                cancellationToken),
            cancellationToken);

    public Task<RunTestsResponse> RunTestsAsync(
        string? vsInstanceId,
        IReadOnlyList<string>? testIds,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.RunTestsAsync(
                new RunTestsRequest
                {
                    TestIds = testIds != null ? new List<string>(testIds) : null
                },
                cancellationToken),
            cancellationToken);

    public async Task<RunTestsResponse> RunTestsAndWaitAsync(
        string? vsInstanceId,
        IReadOnlyList<string>? testIds,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var runResp = await RunTestsAsync(vsInstanceId, testIds, cancellationToken).ConfigureAwait(false);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 600));

        while (!TestRunStates.IsTerminal(runResp.State))
        {
            if (sw.Elapsed >= timeout)
            {
                runResp.DurationSeconds = Math.Round(sw.Elapsed.TotalSeconds, 2);
                return runResp;
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            var status = await GetTestRunStatusAsync(vsInstanceId, runResp.TestRunId, cancellationToken).ConfigureAwait(false);
            runResp.State = status.State;
            runResp.TotalCount = status.TotalCount;
            runResp.PassedCount = status.PassedCount;
            runResp.FailedCount = status.FailedCount;
            runResp.SkippedCount = status.SkippedCount;
            runResp.CompletedAt = DateTime.UtcNow.ToString("O");

            if (status.Results != null)
            {
                runResp.FailedTestNames = status.Results
                    .Where(r => string.Equals(r.Outcome, "failed", StringComparison.OrdinalIgnoreCase))
                    .Select(r => !string.IsNullOrWhiteSpace(r.DisplayName) ? r.DisplayName : r.TestId)
                    .ToList();
            }
        }

        runResp.DurationSeconds = Math.Round(sw.Elapsed.TotalSeconds, 2);
        return runResp;
    }

    public Task<TestRunStatusResponse> GetTestRunStatusAsync(
        string? vsInstanceId,
        string? testRunId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.GetTestRunStatusAsync(
                new GetTestRunStatusRequest
                {
                    TestRunId = testRunId
                },
                cancellationToken),
            cancellationToken);

    public Task<CancelTestRunResponse> CancelTestRunAsync(
        string? vsInstanceId,
        string? testRunId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.CancelTestRunAsync(
                new CancelTestRunRequest
                {
                    TestRunId = testRunId
                },
                cancellationToken),
            cancellationToken);

    public Task<DebugTestResponse> DebugTestAsync(
        string? vsInstanceId,
        string testId,
        bool? waitForBreak,
        int? timeoutMs,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.DebugTestAsync(
                new DebugTestRequest
                {
                    TestId = testId,
                    WaitForBreak = waitForBreak,
                    TimeoutMs = timeoutMs
                },
                cancellationToken),
            cancellationToken);

    public Task<DebuggerGetThreadsResponse> DebuggerGetThreadsAsync(
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.DebuggerGetThreadsAsync(
                new DebuggerGetThreadsRequest(),
                cancellationToken),
            cancellationToken);

    public Task<DebuggerGetExceptionInfoResponse> DebuggerGetExceptionInfoAsync(
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.DebuggerGetExceptionInfoAsync(
                new DebuggerGetExceptionInfoRequest(),
                cancellationToken),
            cancellationToken);

    public Task<DebuggerGetProcessesResponse> DebuggerGetProcessesAsync(
        string? vsInstanceId,
        string? processName,
        int? processId,
        bool onlyDebugged,
        int? maxCount,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.DebuggerGetProcessesAsync(
                new DebuggerGetProcessesRequest
                {
                    ProcessName = processName,
                    ProcessId = processId,
                    OnlyDebugged = onlyDebugged,
                    MaxCount = maxCount
                },
                cancellationToken),
            cancellationToken);

    public Task<DebuggerAttachResponse> DebuggerAttachProcessAsync(
        string? vsInstanceId,
        int? processId,
        string? processName,
        List<string>? engines,
        bool waitForBreak,
        int? breakTimeoutMs,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.DebuggerAttachProcessAsync(
                new DebuggerAttachRequest
                {
                    ProcessId = processId,
                    ProcessName = processName,
                    Engines = engines,
                    WaitForBreak = waitForBreak,
                    BreakTimeoutMs = breakTimeoutMs
                },
                cancellationToken),
            cancellationToken);

    public Task<DebuggerFindSolutionProcessesResponse> DebuggerFindSolutionProcessesAsync(
        bool startupOnly,
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.DebuggerFindSolutionProcessesAsync(
                new DebuggerFindSolutionProcessesRequest
                {
                    StartupOnly = startupOnly
                },
                cancellationToken),
            cancellationToken);

    public Task<DebuggerAutoAttachResponse> DebuggerAutoAttachAsync(
        bool startupOnly,
        List<string>? processNames,
        List<string>? engines,
        bool waitForBreak,
        int? breakTimeoutMs,
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.DebuggerAutoAttachAsync(
                new DebuggerAutoAttachRequest
                {
                    StartupOnly = startupOnly,
                    ProcessNames = processNames,
                    Engines = engines,
                    WaitForBreak = waitForBreak,
                    BreakTimeoutMs = breakTimeoutMs
                },
                cancellationToken),
            cancellationToken);

    public Task<DebuggerGetSnapshotResponse> DebuggerGetSnapshotAsync(
        bool includeCallStack,
        int? maxFrames,
        bool includeLocals,
        int? maxLocals,
        bool includeRecentLogs,
        int? recentLogLines,
        string? logSource,
        bool includeExceptionInfo,
        bool includeThreads,
        int? maxThreads,
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.DebuggerGetSnapshotAsync(
                new DebuggerGetSnapshotRequest
                {
                    IncludeCallStack = includeCallStack,
                    MaxFrames = maxFrames,
                    IncludeLocals = includeLocals,
                    MaxLocals = maxLocals,
                    IncludeRecentLogs = includeRecentLogs,
                    RecentLogLines = recentLogLines,
                    LogSource = logSource,
                    IncludeExceptionInfo = includeExceptionInfo,
                    IncludeThreads = includeThreads,
                    MaxThreads = maxThreads,
                    VsInstanceId = vsInstanceId
                },
                cancellationToken),
            cancellationToken);

    public Task<DebuggerReadMemoryResponse> DebuggerReadMemoryAsync(
        string address,
        int byteCount,
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.DebuggerReadMemoryAsync(
                new DebuggerReadMemoryRequest
                {
                    Address = address,
                    ByteCount = byteCount
                },
                cancellationToken),
            cancellationToken);

    public Task<DebuggerDetachResponse> DebuggerDetachAsync(
        string? vsInstanceId,
        int? processId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.DebuggerDetachAsync(
                new DebuggerDetachRequest
                {
                    ProcessId = processId
                },
                cancellationToken),
            cancellationToken);

    public Task<DebuggerGetModulesResponse> DebuggerGetModulesAsync(
        string? vsInstanceId,
        int? processId,
        string? nameFilter,
        bool userCodeOnly,
        int? maxCount,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.DebuggerGetModulesAsync(
                new DebuggerGetModulesRequest
                {
                    ProcessId = processId,
                    NameFilter = nameFilter,
                    UserCodeOnly = userCodeOnly,
                    MaxCount = maxCount
                },
                cancellationToken),
            cancellationToken);

    public Task<GetActiveDocumentResponse> GetActiveDocumentAsync(
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.GetActiveDocumentAsync(
                new GetActiveDocumentRequest(),
                cancellationToken),
            cancellationToken);

    public Task<NavigateToResponse> NavigateToAsync(
        string? vsInstanceId,
        string filePath,
        int? line,
        int? column,
        bool preview,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId, filePath),
            client => client.NavigateToAsync(
                new NavigateToRequest
                {
                    FilePath = filePath,
                    Line = line,
                    Column = column,
                    Preview = preview
                },
                cancellationToken),
            cancellationToken);

    public Task<GetSolutionConfigurationsResponse> GetSolutionConfigurationsAsync(
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.GetSolutionConfigurationsAsync(
                new GetSolutionConfigurationsRequest(),
                cancellationToken),
            cancellationToken);

    public Task<SetSolutionConfigurationResponse> SetSolutionConfigurationAsync(
        string configuration,
        string? platform,
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.SetSolutionConfigurationAsync(
                new SetSolutionConfigurationRequest
                {
                    Configuration = configuration,
                    Platform = platform,
                    VsInstanceId = vsInstanceId
                },
                cancellationToken),
            cancellationToken);

    public Task<GetOutputPanesResponse> GetOutputPanesAsync(
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.GetOutputPanesAsync(
                new GetOutputPanesRequest
                {
                    VsInstanceId = vsInstanceId
                },
                cancellationToken),
            cancellationToken);

    public Task<DebuggerThreadControlResponse> DebuggerFreezeThreadAsync(
        int threadId,
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.DebuggerFreezeThreadAsync(
                new DebuggerThreadControlRequest
                {
                    ThreadId = threadId,
                    VsInstanceId = vsInstanceId
                },
                cancellationToken),
            cancellationToken);

    public Task<DebuggerThreadControlResponse> DebuggerThawThreadAsync(
        int threadId,
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId),
            client => client.DebuggerThawThreadAsync(
                new DebuggerThreadControlRequest
                {
                    ThreadId = threadId,
                    VsInstanceId = vsInstanceId
                },
                cancellationToken),
            cancellationToken);

    public Task<DebuggerSetNextStatementResponse> DebuggerSetNextStatementAsync(
        string? filePath,
        int? line,
        int? column,
        string? vsInstanceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            _registry.Resolve(vsInstanceId, filePath),
            client => client.DebuggerSetNextStatementAsync(
                new DebuggerSetNextStatementRequest
                {
                    FilePath = filePath,
                    Line = line ?? 0,
                    Column = column,
                    VsInstanceId = vsInstanceId
                },
                cancellationToken),
            cancellationToken);

    private async Task<T> ExecuteAsync<T>(
        VisualStudioInstanceDescriptor instance,
        Func<BridgeClient, Task<T>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var client = new BridgeClient(instance.BridgePipeName);
            await client.ConnectAsync(_options.ConnectTimeout, cancellationToken).ConfigureAwait(false);
            return await action(client).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BridgeRpcException exception)
        {
            throw BridgeServiceException.FromBridge(exception);
        }
        catch (IOException exception)
        {
            throw new BridgeServiceException(
                BridgeErrorCodes.BridgeUnavailable,
                "The Visual Studio bridge is unavailable.",
                true,
                exception);
        }
        catch (SerializationException exception)
        {
            throw new BridgeServiceException(
                BridgeErrorCodes.InternalError,
                "The Visual Studio bridge returned an invalid response.",
                false,
                exception);
        }
    }
}

public sealed class BridgeServiceException : Exception
{
    public BridgeServiceException(
        string code,
        string message,
        bool retryable,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Retryable = retryable;
    }

    public string Code { get; }

    public bool Retryable { get; }

    public static BridgeServiceException FromBridge(BridgeRpcException exception) =>
        exception.Code switch
        {
            BridgeErrorCodes.BridgeUnavailable => new(
                exception.Code,
                "The Visual Studio bridge is unavailable.",
                true,
                exception),
            BridgeErrorCodes.InstanceNotFound => new(
                exception.Code,
                "The requested Visual Studio instance is not registered.",
                true,
                exception),
            BridgeErrorCodes.AmbiguousInstance => new(
                exception.Code,
                "Multiple Visual Studio instances are registered; specify vsInstanceId.",
                false,
                exception),
            BridgeErrorCodes.ProtocolMismatch => new(
                exception.Code,
                "The Visual Studio bridge protocol is incompatible with this host.",
                false,
                exception),
            BridgeErrorCodes.InvalidRequest => new(
                exception.Code,
                "The Visual Studio bridge rejected the request.",
                false,
                exception),
            BridgeErrorCodes.Timeout => new(
                exception.Code,
                "The Visual Studio bridge request timed out.",
                true,
                exception),
            BridgeErrorCodes.Cancelled => new(
                exception.Code,
                "The Visual Studio bridge request was cancelled.",
                true,
                exception),
            BridgeErrorCodes.SolutionStateUnavailable => new(
                exception.Code,
                "The Visual Studio solution state is unavailable.",
                exception.Retryable,
                exception),
            BridgeErrorCodes.SolutionNotOpen => BuildError(exception, "No Visual Studio solution is open."),
            BridgeErrorCodes.BuildInProgress => BuildError(exception, "A Visual Studio build is already in progress."),
            BridgeErrorCodes.InvalidBuildConfiguration => BuildError(
                exception,
                "The requested solution configuration or platform is invalid."),
            BridgeErrorCodes.BuildTaskNotFound => BuildError(exception, "The build task was not found."),
            BridgeErrorCodes.BuildNotActive => BuildError(exception, "The build task is not active."),
            BridgeErrorCodes.BuildCancelNotSupported => BuildError(
                exception,
                "The active Visual Studio build cannot be cancelled."),
            BridgeErrorCodes.BuildStartFailed => BuildError(exception, "Visual Studio could not start the build."),
            BridgeErrorCodes.BuildStateUnavailable => BuildError(
                exception,
                "The Visual Studio build state is unavailable."),
            BridgeErrorCodes.DiagnosticsUnavailable => new(
                exception.Code,
                "The Visual Studio diagnostics snapshot is unavailable.",
                exception.Retryable,
                exception),
            BridgeErrorCodes.OutputUnavailable => new(
                exception.Code,
                "The Visual Studio output window is unavailable.",
                exception.Retryable,
                exception),
            BridgeErrorCodes.DebuggerNotPaused => new(
                exception.Code,
                exception.Message,
                false,
                exception),
            BridgeErrorCodes.DebuggerUnavailable => new(
                exception.Code,
                exception.Message,
                false,
                exception),
            BridgeErrorCodes.DebuggerEvaluationFailed => new(
                exception.Code,
                exception.Message,
                false,
                exception),
            BridgeErrorCodes.DebuggerBusy => new(
                exception.Code,
                exception.Message,
                true,
                exception),
            BridgeErrorCodes.DebuggerNotRunning => new(
                exception.Code,
                exception.Message,
                false,
                exception),
            BridgeErrorCodes.DebuggerNotDebugging => new(
                exception.Code,
                exception.Message,
                false,
                exception),
            BridgeErrorCodes.DebuggerAlreadyRunning => new(
                exception.Code,
                exception.Message,
                false,
                exception),
            BridgeErrorCodes.ProcessNotFound => new(
                exception.Code,
                exception.Message,
                false,
                exception),
            BridgeErrorCodes.TestRunBusy => new(
                exception.Code,
                exception.Message,
                false,
                exception),
            BridgeErrorCodes.TestRunNotFound => new(
                exception.Code,
                exception.Message,
                false,
                exception),
            BridgeErrorCodes.TestNotFound => new(
                exception.Code,
                exception.Message,
                false,
                exception),
            BridgeErrorCodes.TestWindowUnavailable => new(
                exception.Code,
                exception.Message,
                true,
                exception),
            BridgeErrorCodes.FileNotFound => new(
                exception.Code,
                exception.Message,
                false,
                exception),
            BridgeErrorCodes.InvalidNavigationTarget => new(
                exception.Code,
                exception.Message,
                false,
                exception),
            BridgeErrorCodes.ActiveDocumentUnavailable => new(
                exception.Code,
                exception.Message,
                false,
                exception),
            BridgeErrorCodes.DebuggerRunningCannotBuild => new(
                exception.Code,
                exception.Message,
                false,
                exception),
            BridgeErrorCodes.ThreadNotFound => new(
                exception.Code,
                exception.Message,
                false,
                exception),
            BridgeErrorCodes.InvalidNextStatement => new(
                exception.Code,
                exception.Message,
                false,
                exception),
            BridgeErrorCodes.BreakpointNotFound => new(
                exception.Code,
                exception.Message,
                false,
                exception),
            BridgeErrorCodes.InvalidBreakpointTarget => new(
                exception.Code,
                exception.Message,
                false,
                exception),
            BridgeErrorCodes.ConfigurationNotFound => new(
                exception.Code,
                exception.Message,
                false,
                exception),
            BridgeErrorCodes.OutputPaneNotFound => new(
                exception.Code,
                exception.Message,
                false,
                exception),
            BridgeErrorCodes.CannotSwitchConfigurationWhileDebugging => new(
                exception.Code,
                exception.Message,
                false,
                exception),
            BridgeErrorCodes.EngineNotFound => new(
                exception.Code,
                exception.Message,
                false,
                exception),
            BridgeErrorCodes.NoSolutionProcessesFound => new(
                exception.Code,
                exception.Message,
                false,
                exception),
            BridgeErrorCodes.InvalidMemoryAddress => new(
                exception.Code,
                exception.Message,
                false,
                exception),
            BridgeErrorCodes.MemoryReadFailed => new(
                exception.Code,
                exception.Message,
                false,
                exception),
            _ => new(
                BridgeErrorCodes.InternalError,
                "The Visual Studio bridge request failed.",
                false,
                exception)
        };

    private static BridgeServiceException BuildError(BridgeRpcException exception, string message) =>
        new(exception.Code, message, exception.Retryable, exception);
}