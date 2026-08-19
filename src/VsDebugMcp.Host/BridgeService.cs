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

    Task<BuildTaskResponse> RunBuildAsync(
        string? configuration,
        string? platform,
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
            _registry.Resolve(vsInstanceId),
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
            _ => new(
                BridgeErrorCodes.InternalError,
                "The Visual Studio bridge request failed.",
                false,
                exception)
        };

    private static BridgeServiceException BuildError(BridgeRpcException exception, string message) =>
        new(exception.Code, message, exception.Retryable, exception);
}