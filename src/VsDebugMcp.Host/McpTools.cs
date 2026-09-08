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
        [Description("Optional condition evaluation mode: 'whenTrue' (default) or 'whenChanged'.")] string? conditionType = null,
        [Description("Optional hit count target integer (e.g. 5).")] int? hitCountTarget = null,
        [Description("Optional hit count condition type: 'equal' (default), 'greaterOrEqual', or 'multiple'.")] string? hitCountType = null,
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
                    ConditionType = conditionType,
                    HitCountTarget = hitCountTarget,
                    HitCountType = hitCountType,
                    Enabled = enabled ?? true
                }
            },
            clearExisting ?? false,
            vsInstanceId,
            cancellationToken));

    [McpServerTool(
        Name = "vs_debugger_list_breakpoints",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Lists breakpoints currently set in Visual Studio, including file paths, lines, conditions, hit counts, enabled status, and bound state.")]
    public Task<DebuggerListBreakpointsResponse> DebuggerListBreakpointsAsync(
        [Description("Optional source file path to filter breakpoints by.")] string? filePath = null,
        [Description("Optional flag to return only currently enabled breakpoints. Defaults to false.")] bool? enabledOnly = null,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.DebuggerListBreakpointsAsync(
            filePath,
            enabledOnly ?? false,
            vsInstanceId,
            cancellationToken));

    [McpServerTool(
        Name = "vs_debugger_clear_breakpoints",
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Clears breakpoints in Visual Studio. For safety, at least one of clearAll (set to true), filePath, or breakpointId must be specified.")]
    public Task<DebuggerClearBreakpointsResponse> DebuggerClearBreakpointsAsync(
        [Description("Optional flag to clear all breakpoints across the solution. Must be explicitly set to true if filePath and breakpointId are omitted.")] bool? clearAll = null,
        [Description("Optional source file path to clear breakpoints from.")] string? filePath = null,
        [Description("Optional 1-based line number to clear when filePath is specified. If omitted with filePath, clears all breakpoints in the file.")] int? line = null,
        [Description("Optional specific breakpoint ID (e.g. 'full/path.cpp:42') to clear.")] string? breakpointId = null,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.DebuggerClearBreakpointsAsync(
            clearAll ?? false,
            filePath,
            line,
            breakpointId,
            vsInstanceId,
            cancellationToken));

    [McpServerTool(
        Name = "vs_debugger_toggle_breakpoint",
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Toggles or explicitly sets the enabled status of an existing breakpoint by breakpointId or filePath + line.")]
    public Task<DebuggerToggleBreakpointResponse> DebuggerToggleBreakpointAsync(
        [Description("Optional specific breakpoint ID (e.g. 'full/path.cpp:42') to toggle.")] string? breakpointId = null,
        [Description("Optional source file path of the breakpoint to toggle.")] string? filePath = null,
        [Description("Optional 1-based line number of the breakpoint to toggle. Required if filePath is specified without breakpointId.")] int? line = null,
        [Description("Optional target enabled state. If omitted, the current enabled state is inverted (toggled).")] bool? enabled = null,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.DebuggerToggleBreakpointAsync(
            breakpointId,
            filePath,
            line,
            enabled,
            vsInstanceId,
            cancellationToken));

    [McpServerTool(
        Name = "vs_debugger_get_call_stack",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Returns the call stack of the active or specified thread when the debugger is paused in break mode, including source file paths, line and column numbers, module names, and user-code flags.")]
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

    [McpServerTool(
        Name = "vs_debugger_step_over",
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Steps over the next statement or function call while paused in break mode.")]
    public Task<DebuggerExecutionResponse> DebuggerStepOverAsync(
        [Description("Optional flag whether to wait for the step to complete and enter break mode. Defaults to true.")] bool? waitForBreak = null,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.DebuggerStepOverAsync(
            waitForBreak ?? true,
            vsInstanceId,
            cancellationToken));

    [McpServerTool(
        Name = "vs_debugger_step_into",
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Steps into the next statement or function call while paused in break mode.")]
    public Task<DebuggerExecutionResponse> DebuggerStepIntoAsync(
        [Description("Optional flag whether to wait for the step to complete and enter break mode. Defaults to true.")] bool? waitForBreak = null,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.DebuggerStepIntoAsync(
            waitForBreak ?? true,
            vsInstanceId,
            cancellationToken));

    [McpServerTool(
        Name = "vs_debugger_step_out",
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Steps out of the current function to its caller while paused in break mode.")]
    public Task<DebuggerExecutionResponse> DebuggerStepOutAsync(
        [Description("Optional flag whether to wait for the step to complete and enter break mode. Defaults to true.")] bool? waitForBreak = null,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.DebuggerStepOutAsync(
            waitForBreak ?? true,
            vsInstanceId,
            cancellationToken));

    [McpServerTool(
        Name = "vs_debugger_continue",
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Resumes program execution from break mode until the next breakpoint or process termination.")]
    public Task<DebuggerExecutionResponse> DebuggerContinueAsync(
        [Description("Optional flag whether to wait for the program to enter break mode before returning. Defaults to false.")] bool? waitForBreak = null,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.DebuggerContinueAsync(
            waitForBreak ?? false,
            vsInstanceId,
            cancellationToken));

    [McpServerTool(
        Name = "vs_debugger_pause",
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Pauses (breaks) the currently executing debug target.")]
    public Task<DebuggerExecutionResponse> DebuggerPauseAsync(
        [Description("Optional flag whether to wait for the debugger to enter break mode before returning. Defaults to true.")] bool? waitForBreak = null,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.DebuggerPauseAsync(
            waitForBreak ?? true,
            vsInstanceId,
            cancellationToken));

    [McpServerTool(
        Name = "vs_debugger_stop",
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Stops the active debugging session and returns Visual Studio to design mode.")]
    public Task<DebuggerExecutionResponse> DebuggerStopAsync(
        [Description("Optional flag whether to wait for debugging to terminate before returning. Defaults to false.")] bool? waitForStop = null,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.DebuggerStopAsync(
            waitForStop ?? false,
            vsInstanceId,
            cancellationToken));

    [McpServerTool(
        Name = "vs_debugger_start",
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Starts debugging the active startup project in the open solution (equivalent to F5).")]
    public Task<DebuggerExecutionResponse> DebuggerStartAsync(
        [Description("Optional flag whether to wait for the program to enter break mode before returning. Defaults to true.")] bool? waitForBreak = null,
        [Description("Optional timeout in milliseconds to wait for a breakpoint or pause when waitForBreak is true. Defaults to 5000.")] int? timeoutMs = null,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.DebuggerStartAsync(
            waitForBreak ?? true,
            timeoutMs,
            vsInstanceId,
            cancellationToken));

    [McpServerTool(
        Name = "vs_debugger_evaluate_expressions",
        ReadOnly = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Evaluates multiple expressions or variables in batch within the context of the current or specified stack frame while paused in break mode.")]
    public Task<DebuggerEvaluateExpressionsResponse> DebuggerEvaluateExpressionsAsync(
        [Description("The list of expressions or variables to evaluate.")] List<string> expressions,
        [Description("Optional stack frame index, where 0 is the top/current frame. Defaults to 0.")] int? frameIndex = null,
        [Description("Optional evaluation timeout per expression in milliseconds from 100 to 10000. Defaults to 2000.")] int? timeoutMs = null,
        [Description("Optional flag whether side effects are allowed during evaluation. Defaults to false.")] bool? allowSideEffects = null,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.DebuggerEvaluateExpressionsAsync(
            expressions,
            frameIndex,
            timeoutMs,
            allowSideEffects ?? false,
            vsInstanceId,
            cancellationToken));

    [McpServerTool(
        Name = "vs_debugger_get_locals",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Retrieves the arguments and local variables available in the context of the current or specified stack frame while paused in break mode.")]
    public Task<DebuggerGetLocalsResponse> DebuggerGetLocalsAsync(
        [Description("Optional stack frame index, where 0 is the top/current frame. Defaults to 0.")] int? frameIndex = null,
        [Description("Optional maximum number of variables to retrieve from 1 to 200. Defaults to 50.")] int? maxCount = null,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.DebuggerGetLocalsAsync(
            frameIndex,
            maxCount,
            vsInstanceId,
            cancellationToken));

    [McpServerTool(
        Name = "vs_get_tests",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Discovers and returns unit tests in the current solution known to Test Explorer, with optional filtering.")]
    public Task<GetTestsResponse> GetTestsAsync(
        [Description("Optional project name substring to filter tests by project or container.")] string? projectName = null,
        [Description("Optional search text to filter tests by display name or fully qualified name.")] string? filter = null,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.GetTestsAsync(
            vsInstanceId,
            projectName,
            filter,
            cancellationToken));

    [McpServerTool(
        Name = "vs_run_tests",
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Asynchronously triggers unit test execution for specified test IDs or all discovered tests in the solution.")]
    public Task<RunTestsResponse> RunTestsAsync(
        [Description("Optional list of specific test IDs (GUIDs) to execute. If omitted or empty, all discovered tests in the solution will be run.")] IReadOnlyList<string>? testIds = null,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.RunTestsAsync(
            vsInstanceId,
            testIds,
            cancellationToken));

    [McpServerTool(
        Name = "vs_get_test_run_status",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Gets the current execution progress, state, and detailed outcomes of a test run.")]
    public Task<TestRunStatusResponse> GetTestRunStatusAsync(
        [Description("Optional test run ID. If omitted, the active or most recent test run will be queried.")] string? testRunId = null,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.GetTestRunStatusAsync(
            vsInstanceId,
            testRunId,
            cancellationToken));

    [McpServerTool(
        Name = "vs_cancel_test_run",
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Cancels an active unit test run.")]
    public Task<CancelTestRunResponse> CancelTestRunAsync(
        [Description("Optional test run ID. If omitted, the currently running test run will be cancelled.")] string? testRunId = null,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.CancelTestRunAsync(
            vsInstanceId,
            testRunId,
            cancellationToken));

    [McpServerTool(
        Name = "vs_debug_test_by_id",
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Asynchronously triggers debugging for a specific unit test in Test Explorer, with optional breakpoint landing wait.")]
    public Task<DebugTestResponse> DebugTestByIdAsync(
        [Description("The test ID (GUID) to debug.")] string testId,
        [Description("Optional flag whether to wait for the debugger to pause at a breakpoint or exception before returning. Defaults to true.")] bool? waitForBreak = null,
        [Description("Optional timeout in milliseconds to wait for break mode when waitForBreak is true. Defaults to 10000.")] int? timeoutMs = null,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.DebugTestAsync(
            vsInstanceId,
            testId,
            waitForBreak ?? true,
            timeoutMs,
            cancellationToken));

    [McpServerTool(
        Name = "vs_debugger_get_threads",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Retrieves the list of threads and their state in the current debugging target process.")]
    public Task<DebuggerGetThreadsResponse> DebuggerGetThreadsAsync(
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.DebuggerGetThreadsAsync(
            vsInstanceId,
            cancellationToken));

    [McpServerTool(
        Name = "vs_debugger_get_exception_info",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Returns details of the active exception (type, message, HResult, stack trace, inner exception) when the debugger is paused in break mode.")]
    public Task<DebuggerGetExceptionInfoResponse> DebuggerGetExceptionInfoAsync(
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.DebuggerGetExceptionInfoAsync(
            vsInstanceId,
            cancellationToken));

    [McpServerTool(
        Name = "vs_debugger_get_processes",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Retrieves the list of running processes available for attaching or currently being debugged by Visual Studio.")]
    public Task<DebuggerGetProcessesResponse> DebuggerGetProcessesAsync(
        [Description("Optional process name filter (case-insensitive substring match).")] string? processName = null,
        [Description("Optional exact process ID to query.")] int? processId = null,
        [Description("Optional flag to only return processes currently being debugged by this instance. Defaults to false.")] bool? onlyDebugged = null,
        [Description("Optional maximum number of processes to return (1-200, defaults to 50).")] int? maxCount = null,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.DebuggerGetProcessesAsync(
            vsInstanceId,
            processName,
            processId,
            onlyDebugged ?? false,
            maxCount,
            cancellationToken));

    [McpServerTool(
        Name = "vs_debugger_attach_process",
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Attaches the Visual Studio debugger to a running process by process ID or process name.")]
    public Task<DebuggerAttachResponse> DebuggerAttachProcessAsync(
        [Description("Optional target process ID to attach to. One of processId or processName must be specified.")] int? processId = null,
        [Description("Optional process name to find and attach to if processId is omitted.")] string? processName = null,
        [Description("Optional flag whether to wait for the debugger to pause at a breakpoint or exception. Defaults to false.")] bool? waitForBreak = null,
        [Description("Optional timeout in milliseconds to wait for break mode when waitForBreak is true. Defaults to 3000.")] int? breakTimeoutMs = null,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.DebuggerAttachProcessAsync(
            vsInstanceId,
            processId,
            processName,
            waitForBreak ?? false,
            breakTimeoutMs,
            cancellationToken));

    [McpServerTool(
        Name = "vs_debugger_detach",
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Detaches the Visual Studio debugger from a debugged process, allowing it to continue running independently.")]
    public Task<DebuggerDetachResponse> DebuggerDetachAsync(
        [Description("Optional specific process ID to detach from. Omit to detach all debugged processes.")] int? processId = null,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.DebuggerDetachAsync(
            vsInstanceId,
            processId,
            cancellationToken));

    [McpServerTool(
        Name = "vs_debugger_get_modules",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Retrieves the list of modules (DLLs/EXEs) loaded by the active debugged process, including load addresses and symbol (PDB) status.")]
    public Task<DebuggerGetModulesResponse> DebuggerGetModulesAsync(
        [Description("Optional specific process ID. Defaults to current active debugged process.")] int? processId = null,
        [Description("Optional case-insensitive module name filter.")] string? nameFilter = null,
        [Description("Optional flag to only return user code modules (Just My Code). Defaults to false.")] bool? userCodeOnly = null,
        [Description("Optional maximum number of modules to return (1-500, defaults to 100).")] int? maxCount = null,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.DebuggerGetModulesAsync(
            vsInstanceId,
            processId,
            nameFilter,
            userCodeOnly ?? false,
            maxCount,
            cancellationToken));

    [McpServerTool(
        Name = "vs_get_active_document",
        ReadOnly = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Retrieves the currently active document in Visual Studio, including file path, cursor position, selection range, and selected text.")]
    public Task<GetActiveDocumentResponse> GetActiveDocumentAsync(
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.GetActiveDocumentAsync(
            vsInstanceId,
            cancellationToken));

    [McpServerTool(
        Name = "vs_navigate_to",
        ReadOnly = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Opens a source code file in Visual Studio and navigates the cursor to the specified line and column.")]
    public Task<NavigateToResponse> NavigateToAsync(
        [Description("The absolute or solution-relative physical path of the file to navigate to.")] string filePath,
        [Description("Optional 1-based target line number. Defaults to 1.")] int? line = null,
        [Description("Optional 1-based target column number. Defaults to 1.")] int? column = null,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.NavigateToAsync(
            vsInstanceId,
            filePath,
            line,
            column,
            false,
            cancellationToken));

    [McpServerTool(
        Name = "vs_get_solution_configurations",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Retrieves all available build configurations and platforms in the open solution, indicating the currently active configuration.")]
    public Task<GetSolutionConfigurationsResponse> GetSolutionConfigurationsAsync(
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.GetSolutionConfigurationsAsync(
            vsInstanceId,
            cancellationToken));

    [McpServerTool(
        Name = "vs_debugger_freeze_thread",
        ReadOnly = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Freezes (suspends) a specific thread during debugging so it will not run until thawed.")]
    public Task<DebuggerThreadControlResponse> DebuggerFreezeThreadAsync(
        [Description("The ID of the thread to freeze.")] int threadId,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.DebuggerFreezeThreadAsync(
            threadId,
            vsInstanceId,
            cancellationToken));

    [McpServerTool(
        Name = "vs_debugger_thaw_thread",
        ReadOnly = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Thaws (resumes) a previously frozen thread during debugging.")]
    public Task<DebuggerThreadControlResponse> DebuggerThawThreadAsync(
        [Description("The ID of the thread to thaw.")] int threadId,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.DebuggerThawThreadAsync(
            threadId,
            vsInstanceId,
            cancellationToken));

    [McpServerTool(
        Name = "vs_debugger_set_next_statement",
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Sets the next instruction to be executed by the debugger (instruction pointer) to the specified line/column in a file or the active document.")]
    public Task<DebuggerSetNextStatementResponse> DebuggerSetNextStatementAsync(
        [Description("Optional target source file path. If omitted, uses the currently active document in Visual Studio.")] string? filePath = null,
        [Description("Optional 1-based target line number. Required if filePath is provided; if omitted without filePath, uses the cursor position.")] int? line = null,
        [Description("Optional 1-based target column number. Defaults to 1.")] int? column = null,
        [Description("Optional target Visual Studio instance ID. It may be omitted when exactly one instance is registered.")] string? vsInstanceId = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.DebuggerSetNextStatementAsync(
            filePath,
            line,
            column,
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