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