using System.IO;
using System.Runtime.Serialization;
using VsDebugMcp.Protocol;

namespace VsDebugMcp.Host;

public interface IBridgeService
{
    Task<HealthResponse> GetHealthAsync(CancellationToken cancellationToken);

    Task<VsCapabilitiesResult> GetCapabilitiesAsync(CancellationToken cancellationToken);

    Task<GetProjectsInSolutionResponse> GetProjectsInSolutionAsync(CancellationToken cancellationToken);

    Task<BuildTaskResponse> RunBuildAsync(
        string? configuration,
        string? platform,
        CancellationToken cancellationToken);

    Task<BuildTaskResponse> GetBuildStatusAsync(string buildTaskId, CancellationToken cancellationToken);

    Task<CancelBuildResponse> CancelBuildAsync(string buildTaskId, CancellationToken cancellationToken);

    Task<GetErrorsResponse> GetErrorsAsync(
        string? buildTaskId,
        IReadOnlyList<string>? severities,
        string? project,
        string? file,
        int? maxCount,
        CancellationToken cancellationToken);

    Task<GetOutputWindowLogsResponse> GetOutputWindowLogsAsync(
        string? source,
        int? maxChars,
        CancellationToken cancellationToken);
}

public sealed class BridgeService : IBridgeService
{
    private readonly VsHostOptions _options;

    public BridgeService(VsHostOptions options)
    {
        _options = options;
    }

    public Task<HealthResponse> GetHealthAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(
            client => client.GetHealthAsync(cancellationToken),
            cancellationToken);

    public Task<VsCapabilitiesResult> GetCapabilitiesAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(
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

    public Task<GetProjectsInSolutionResponse> GetProjectsInSolutionAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(
            client => client.GetProjectsInSolutionAsync(cancellationToken),
            cancellationToken);

    public Task<BuildTaskResponse> RunBuildAsync(
        string? configuration,
        string? platform,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
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
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            client => client.GetBuildStatusAsync(buildTaskId, cancellationToken),
            cancellationToken);

    public Task<CancelBuildResponse> CancelBuildAsync(
        string buildTaskId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            client => client.CancelBuildAsync(buildTaskId, cancellationToken),
            cancellationToken);

    public Task<GetErrorsResponse> GetErrorsAsync(
        string? buildTaskId,
        IReadOnlyList<string>? severities,
        string? project,
        string? file,
        int? maxCount,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
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
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            client => client.GetOutputWindowLogsAsync(
                new GetOutputWindowLogsRequest
                {
                    Source = source,
                    MaxChars = maxChars
                },
                cancellationToken),
            cancellationToken);

    private async Task<T> ExecuteAsync<T>(
        Func<BridgeClient, Task<T>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var client = new BridgeClient(_options.PipeName);
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