using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using VsDebugMcp.Protocol;

namespace VsDebugMcp.Host;

[McpServerToolType]
public sealed class McpTools
{
    private readonly IBridgeService _bridgeService;

    public McpTools(IBridgeService bridgeService)
    {
        _bridgeService = bridgeService;
    }

    [McpServerTool(
        Name = "vs_health",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Checks whether the local Visual Studio bridge is available and healthy.")]
    public Task<VsHealthResult> GetHealthAsync(
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.GetHealthAsync(vsInstanceId, cancellationToken));

    [McpServerTool(
        Name = "vs_capabilities",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Returns the connected Visual Studio instance metadata and currently available bridge capabilities.")]
    public Task<VsCapabilitiesResult> GetCapabilitiesAsync(
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.GetCapabilitiesAsync(vsInstanceId, cancellationToken));

    [McpServerTool(
        Name = "vs_list_instances",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Lists Visual Studio instances currently registered with the shared Host.")]
    public Task<VsInstancesResult> ListInstancesAsync(CancellationToken cancellationToken) =>
        InvokeAsync(() => _bridgeService.ListInstancesAsync(cancellationToken));

    [McpServerTool(
        Name = "vs_find_instances",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Finds registered Visual Studio instances by instance ID, process ID, solution name, or solution path.")]
    public Task<VsInstancesResult> FindInstancesAsync(
        [Description("Optional case-insensitive search text. Omit it to return all registered instances.")] string? query = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.FindInstancesAsync(query, cancellationToken));

    [McpServerTool(
        Name = "vs_get_projects_in_solution",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Returns loaded projects in the currently open Visual Studio solution, excluding solution folders and unloaded projects.")]
    public Task<GetProjectsInSolutionResponse> GetProjectsInSolutionAsync(
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.GetProjectsInSolutionAsync(vsInstanceId, cancellationToken));

    [McpServerTool(
        Name = "vs_get_files_in_project",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Returns the files belonging to one or all loaded projects in the Visual Studio solution, including relative paths and filter classifications.")]
    public Task<GetFilesInProjectResponse> GetFilesInProjectAsync(
        [Description("Optional project ID, name, or project file path to inspect. Omit it to retrieve files across all loaded projects.")] string? projectId = null,
        [Description("Optional semicolon or comma separated extension filter, e.g. '.cpp;.h' or 'cs'.")] string? extensionFilter = null,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.GetFilesInProjectAsync(projectId, extensionFilter, vsInstanceId, cancellationToken));

    [McpServerTool(
        Name = "vs_run_build",
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Starts an asynchronous build of the currently open Visual Studio solution. Omitted configuration or platform values use the active solution setting.")]
    public Task<BuildTaskResponse> RunBuildAsync(
        [Description("Optional solution configuration name, such as Debug or Release.")] string? configuration = null,
        [Description("Optional solution platform name, such as Any CPU or x64.")] string? platform = null,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.RunBuildAsync(configuration, platform, vsInstanceId, cancellationToken));

    [McpServerTool(
        Name = "vs_get_build_status",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Returns the current state of the most recently retained Visual Studio build task.")]
    public Task<BuildTaskResponse> GetBuildStatusAsync(
        [Description("The build task ID returned by vs_run_build.")] string buildTaskId,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.GetBuildStatusAsync(buildTaskId, vsInstanceId, cancellationToken));

    [McpServerTool(
        Name = "vs_cancel_build",
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Requests cancellation of the active Visual Studio build task.")]
    public Task<CancelBuildResponse> CancelBuildAsync(
        [Description("The active build task ID returned by vs_run_build.")] string buildTaskId,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.CancelBuildAsync(buildTaskId, vsInstanceId, cancellationToken));

    [McpServerTool(
        Name = "vs_get_errors",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Returns the current unfiltered Visual Studio Error Table snapshot for explicit build diagnostics. The optional build task ID is echoed only and does not select historical diagnostics.")]
    public Task<GetErrorsResponse> GetErrorsAsync(
        [Description("Optional build task ID to echo in the response; it is not validated or used to select diagnostics.")] string? buildTaskId = null,
        [Description("Optional severities: error, warning, or message. Defaults to error and warning.")] IReadOnlyList<string>? severities = null,
        [Description("Optional case-insensitive exact project name filter.")] string? project = null,
        [Description("Optional full path or case-insensitive path suffix filter without glob syntax.")] string? file = null,
        [Description("Optional maximum result count from 1 through 1000. Defaults to 200.")] int? maxCount = null,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.GetErrorsAsync(
            buildTaskId,
            severities,
            project,
            file,
            maxCount,
            vsInstanceId,
            cancellationToken));

    [McpServerTool(
        Name = "vs_get_output_window_logs",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Returns the tail of the current Visual Studio Output window pane. The initial version supports the build pane only.")]
    public Task<GetOutputWindowLogsResponse> GetOutputWindowLogsAsync(
        [Description("Optional output source. The only supported value is build, which is also the default.")] string? source = null,
        [Description("Optional maximum number of trailing characters from 1 through 500000. Defaults to 20000.")] int? maxChars = null,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.GetOutputWindowLogsAsync(source, maxChars, vsInstanceId, cancellationToken));

    [McpServerTool(
        Name = "vs_debugger_get_info",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Returns current Visual Studio debugger status, including debug mode (design, running, break), active process, thread, breakpoint count, and last break reason.")]
    public Task<DebuggerGetInfoResponse> DebuggerGetInfoAsync(
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.DebuggerGetInfoAsync(vsInstanceId, cancellationToken));

    [McpServerTool(
        Name = "vs_debugger_set_breakpoints",
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Sets, updates, or clears a breakpoint at a specified source line in the Visual Studio solution.")]
    public Task<DebuggerSetBreakpointsResponse> DebuggerSetBreakpointsAsync(
        [Description("The source file path to set the breakpoint in.")] string filePath,
        [Description("Line number to set the breakpoint at.")] int line,
        [Description("Optional column number. Defaults to 1.")] int? column = null,
        [Description("Optional conditional expression for the breakpoint.")] string? condition = null,
        [Description("Optional flag whether the breakpoint is enabled. Defaults to true.")] bool? enabled = null,
        [Description("Optional flag whether to clear existing breakpoints in this file first. Defaults to false.")] bool? clearExisting = null,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.DebuggerSetBreakpointsAsync(
            filePath,
            new List<BreakpointSpec>
            {
                new()
                {
                    Line = line,
                    Column = column,
                    Condition = condition,
                    Enabled = enabled ?? true
                }
            },
            clearExisting ?? false,
            vsInstanceId,
            cancellationToken));

    [McpServerTool(
        Name = "vs_debugger_get_call_stack",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Returns the call stack of the active or specified thread when the debugger is paused in break mode.")]
    public Task<DebuggerGetCallStackResponse> DebuggerGetCallStackAsync(
        [Description("Optional thread ID. Omit it to retrieve the call stack for the current active thread.")] int? threadId = null,
        [Description("Optional maximum number of frames to retrieve from 1 to 200. Defaults to 50.")] int? maxFrames = null,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.DebuggerGetCallStackAsync(threadId, maxFrames, vsInstanceId, cancellationToken));

    [McpServerTool(
        Name = "vs_debugger_evaluate_expr",
        ReadOnly = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Evaluates an expression or variable in the context of the current or specified stack frame while the debugger is paused in break mode.")]
    public Task<DebuggerEvaluateExprResponse> DebuggerEvaluateExprAsync(
        [Description("The variable or expression to evaluate.")] string expression,
        [Description("Optional stack frame index, where 0 is the top/current frame. Defaults to 0.")] int? frameIndex = null,
        [Description("Optional evaluation timeout in milliseconds from 100 to 10000. Defaults to 2000.")] int? timeoutMs = null,
        [Description("Optional flag whether side effects are allowed during evaluation. Defaults to false.")] bool? allowSideEffects = null,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.DebuggerEvaluateExprAsync(
            expression,
            frameIndex,
            timeoutMs,
            allowSideEffects ?? false,
            vsInstanceId,
            cancellationToken));

    private static async Task<T> InvokeAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (BridgeServiceException exception)
        {
            throw new McpException($"{exception.Code}: {exception.Message}");
        }
    }
}