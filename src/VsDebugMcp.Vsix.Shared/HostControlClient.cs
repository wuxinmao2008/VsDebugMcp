using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using VsDebugMcp.Protocol;

namespace VsDebugMcp_Vsix;

internal sealed class HostControlClient
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(500);

    public Task<HostStatusResponse> GetStatusAsync(CancellationToken cancellationToken) =>
        CallAsync<HostStatusResponse>(BridgeMethods.HostStatus, null, cancellationToken);

    public Task<RegisterInstanceResponse> RegisterAsync(
        VisualStudioInstanceDescriptor instance,
        CancellationToken cancellationToken) =>
        CallAsync<RegisterInstanceResponse>(
            BridgeMethods.RegisterInstance,
            BridgeJson.Serialize(new RegisterInstanceRequest { Instance = instance }),
            cancellationToken);

    public Task<HeartbeatInstanceResponse> HeartbeatAsync(
        VisualStudioInstanceDescriptor instance,
        CancellationToken cancellationToken) =>
        CallAsync<HeartbeatInstanceResponse>(
            BridgeMethods.HeartbeatInstance,
            BridgeJson.Serialize(new HeartbeatInstanceRequest { Instance = instance }),
            cancellationToken);

    public Task<UnregisterInstanceResponse> UnregisterAsync(
        string vsInstanceId,
        CancellationToken cancellationToken) =>
        CallAsync<UnregisterInstanceResponse>(
            BridgeMethods.UnregisterInstance,
            BridgeJson.Serialize(new UnregisterInstanceRequest { VsInstanceId = vsInstanceId }),
            cancellationToken);

    private static async Task<TResponse> CallAsync<TResponse>(
        string method,
        string? payloadJson,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DefaultTimeout);
        using var pipe = new NamedPipeClientStream(
            ".",
            PipeNames.ForHostControl(),
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync((int)DefaultTimeout.TotalMilliseconds, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The shared Host control pipe did not respond.");
        }

        var request = new BridgeRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Method = method,
            PayloadJson = payloadJson
        };
        await PipeMessageFraming.WriteAsync(pipe, request, timeout.Token).ConfigureAwait(false);
        var response = await PipeMessageFraming.ReadAsync<BridgeResponse>(pipe, timeout.Token).ConfigureAwait(false);
        if (!string.Equals(response.RequestId, request.RequestId, StringComparison.Ordinal))
        {
            throw new HostControlException(BridgeErrorCodes.ProtocolMismatch);
        }

        if (response.Error is not null)
        {
            throw new HostControlException(response.Error.Code);
        }

        return BridgeJson.Deserialize<TResponse>(response.PayloadJson ?? string.Empty);
    }
}

internal sealed class HostControlException : Exception
{
    public HostControlException(string code)
        : base(code)
    {
        Code = code;
    }

    public string Code { get; }
}