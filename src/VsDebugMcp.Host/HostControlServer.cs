using System.IO.Pipes;
using System.Runtime.Serialization;
using VsDebugMcp.Protocol;

namespace VsDebugMcp.Host;

public sealed class HostControlServer
{
    private readonly VsHostOptions _options;
    private readonly VisualStudioInstanceRegistry _registry;
    private readonly Action _requestStop;

    public HostControlServer(
        VsHostOptions options,
        VisualStudioInstanceRegistry registry,
        Action requestStop)
    {
        _options = options;
        _registry = registry;
        _requestStop = requestStop;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    _options.ControlPipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                await ProcessRequestAsync(pipe, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"host_control_error: {exception.GetType().Name}");
            }
        }
    }

    private async Task ProcessRequestAsync(Stream pipe, CancellationToken cancellationToken)
    {
        BridgeRequest request;
        try
        {
            request = await PipeMessageFraming.ReadAsync<BridgeRequest>(pipe, cancellationToken).ConfigureAwait(false);
        }
        catch (EndOfStreamException)
        {
            return;
        }

        var handled = Handle(request);
        await PipeMessageFraming.WriteAsync(pipe, handled.Response, cancellationToken).ConfigureAwait(false);
        if (handled.StopAfterResponse)
        {
            _requestStop();
        }
    }

    private (BridgeResponse Response, bool StopAfterResponse) Handle(BridgeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RequestId) || string.IsNullOrWhiteSpace(request.Method))
        {
            return (Failure(request.RequestId, BridgeErrorCodes.InvalidRequest, "Request ID and method are required."), false);
        }

        if (!string.Equals(request.ProtocolVersion, BridgeProtocol.Version, StringComparison.Ordinal))
        {
            return (Failure(request.RequestId, BridgeErrorCodes.ProtocolMismatch, "The Host control protocol is incompatible."), false);
        }

        try
        {
            switch (request.Method)
            {
                case BridgeMethods.HostStatus:
                    return (
                        BridgeResponse.Success(
                            request.RequestId,
                            new HostStatusResponse
                            {
                                HostVersion = typeof(HostControlServer).Assembly.GetName().Version?.ToString() ?? "0.0.0",
                                Instances = _registry.List().ToList()
                            }),
                        false);
                case BridgeMethods.RegisterInstance:
                {
                    var payload = Deserialize<RegisterInstanceRequest>(request);
                    return (BridgeResponse.Success(request.RequestId, _registry.Register(payload.Instance)), false);
                }
                case BridgeMethods.HeartbeatInstance:
                {
                    var payload = Deserialize<HeartbeatInstanceRequest>(request);
                    return (BridgeResponse.Success(request.RequestId, _registry.Heartbeat(payload.Instance)), false);
                }
                case BridgeMethods.UnregisterInstance:
                {
                    var payload = Deserialize<UnregisterInstanceRequest>(request);
                    var result = _registry.Unregister(payload.VsInstanceId);
                    return (
                        BridgeResponse.Success(
                            request.RequestId,
                            new UnregisterInstanceResponse { Removed = result.Removed }),
                        result.ShouldStop);
                }
                default:
                    return (Failure(request.RequestId, BridgeErrorCodes.InvalidRequest, "Unknown Host control method."), false);
            }
        }
        catch (SerializationException)
        {
            return (Failure(request.RequestId, BridgeErrorCodes.InvalidRequest, "The Host control request is invalid."), false);
        }
        catch (ArgumentException)
        {
            return (Failure(request.RequestId, BridgeErrorCodes.RegistrationFailed, "The Visual Studio instance registration is invalid."), false);
        }
    }

    private static T Deserialize<T>(BridgeRequest request) =>
        BridgeJson.Deserialize<T>(request.PayloadJson ?? string.Empty);

    private static BridgeResponse Failure(string requestId, string code, string message) =>
        BridgeResponse.Failure(
            requestId ?? string.Empty,
            new BridgeError
            {
                Code = code,
                Message = message,
                Retryable = false
            });
}