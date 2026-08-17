using System.IO;
using System.Runtime.Serialization;
using VsDebugMcp.Protocol;

namespace VsDebugMcp.Host;

public interface IBridgeService
{
    Task<HealthResponse> GetHealthAsync(CancellationToken cancellationToken);

    Task<VsCapabilitiesResult> GetCapabilitiesAsync(CancellationToken cancellationToken);

    Task<GetProjectsInSolutionResponse> GetProjectsInSolutionAsync(CancellationToken cancellationToken);
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
            _ => new(
                BridgeErrorCodes.InternalError,
                "The Visual Studio bridge request failed.",
                false,
                exception)
        };
}