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