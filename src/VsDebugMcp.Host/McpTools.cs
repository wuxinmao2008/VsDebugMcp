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
    public Task<HealthResponse> GetHealthAsync(CancellationToken cancellationToken) =>
        InvokeAsync(() => _bridgeService.GetHealthAsync(cancellationToken));

    [McpServerTool(
        Name = "vs_capabilities",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Returns the connected Visual Studio instance metadata and currently available bridge capabilities.")]
    public Task<VsCapabilitiesResult> GetCapabilitiesAsync(CancellationToken cancellationToken) =>
        InvokeAsync(() => _bridgeService.GetCapabilitiesAsync(cancellationToken));

    [McpServerTool(
        Name = "vs_get_projects_in_solution",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Returns loaded projects in the currently open Visual Studio solution, excluding solution folders and unloaded projects.")]
    public Task<GetProjectsInSolutionResponse> GetProjectsInSolutionAsync(CancellationToken cancellationToken) =>
        InvokeAsync(() => _bridgeService.GetProjectsInSolutionAsync(cancellationToken));

    [McpServerTool(
        Name = "vs_run_build",
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Starts an asynchronous build of the currently open Visual Studio solution. Omitted configuration or platform values use the active solution setting.")]
    public Task<BuildTaskResponse> RunBuildAsync(
        [Description("Optional solution configuration name, such as Debug or Release.")] string? configuration,
        [Description("Optional solution platform name, such as Any CPU or x64.")] string? platform,
        CancellationToken cancellationToken) =>
        InvokeAsync(() => _bridgeService.RunBuildAsync(configuration, platform, cancellationToken));

    [McpServerTool(
        Name = "vs_get_build_status",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Returns the current state of the most recently retained Visual Studio build task.")]
    public Task<BuildTaskResponse> GetBuildStatusAsync(
        [Description("The build task ID returned by vs_run_build.")] string buildTaskId,
        CancellationToken cancellationToken) =>
        InvokeAsync(() => _bridgeService.GetBuildStatusAsync(buildTaskId, cancellationToken));

    [McpServerTool(
        Name = "vs_cancel_build",
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Requests cancellation of the active Visual Studio build task.")]
    public Task<CancelBuildResponse> CancelBuildAsync(
        [Description("The active build task ID returned by vs_run_build.")] string buildTaskId,
        CancellationToken cancellationToken) =>
        InvokeAsync(() => _bridgeService.CancelBuildAsync(buildTaskId, cancellationToken));

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
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.GetErrorsAsync(
            buildTaskId,
            severities,
            project,
            file,
            maxCount,
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
        CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _bridgeService.GetOutputWindowLogsAsync(source, maxChars, cancellationToken));

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