using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using VsDebugMcp.Protocol;

namespace VsDebugMcp_Vsix;

internal sealed class BridgeServer : IDisposable
{
    private const string LogSource = "VsDebugMcp";
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _sync = new();
    private NamedPipeServerStream? _activePipe;
    private Task? _serverTask;

    public void Start()
    {
        _serverTask = Task.Run(() => RunAsync(_shutdown.Token));
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        lock (_sync)
        {
            _activePipe?.Dispose();
            _activePipe = null;
        }

        _shutdown.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipe = CreatePipe();
                lock (_sync)
                {
                    _activePipe = pipe;
                }

                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                await ProcessConnectionAsync(pipe, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                ActivityLog.LogError(LogSource, BridgeErrorCodes.InternalError);
            }
            finally
            {
                lock (_sync)
                {
                    _activePipe = null;
                }
            }
        }
    }

    private static async Task ProcessConnectionAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        while (pipe.IsConnected && !cancellationToken.IsCancellationRequested)
        {
            BridgeRequest request;
            try
            {
                request = await PipeMessageFraming.ReadAsync<BridgeRequest>(pipe, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (EndOfStreamException)
            {
                break;
            }
            catch (IOException)
            {
                break;
            }

            var stopwatch = Stopwatch.StartNew();
            var response = HandleRequest(request, out var closeConnection);
            await PipeMessageFraming.WriteAsync(pipe, response, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            var result = response.Error?.Code ?? "ok";
            ActivityLog.LogInformation(
                LogSource,
                $"method={request.Method};requestId={request.RequestId};elapsedMs={stopwatch.ElapsedMilliseconds};result={result}");

            if (closeConnection)
            {
                break;
            }
        }
    }

    private static BridgeResponse HandleRequest(BridgeRequest request, out bool closeConnection)
    {
        closeConnection = false;
        if (string.IsNullOrWhiteSpace(request.RequestId) || string.IsNullOrWhiteSpace(request.Method))
        {
            return Failure(request.RequestId, BridgeErrorCodes.InvalidRequest, "Request ID and method are required.", false);
        }

        if (!string.Equals(request.ProtocolVersion, BridgeProtocol.Version, StringComparison.Ordinal))
        {
            return Failure(
                request.RequestId,
                BridgeErrorCodes.ProtocolMismatch,
                $"Protocol {request.ProtocolVersion} is not supported.",
                false);
        }

        switch (request.Method)
        {
            case BridgeMethods.Handshake:
                return BridgeResponse.Success(request.RequestId, CreateHandshake());
            case BridgeMethods.Health:
                return BridgeResponse.Success(
                    request.RequestId,
                    new HealthResponse
                    {
                        Status = "ok",
                        UtcTimestamp = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                    });
            case BridgeMethods.Capabilities:
                return BridgeResponse.Success(request.RequestId, CreateCapabilities());
            case BridgeMethods.Shutdown:
                closeConnection = true;
                return BridgeResponse.Success(request.RequestId, new ShutdownResponse { Accepted = true });
            default:
                return Failure(request.RequestId, BridgeErrorCodes.InvalidRequest, "Unknown bridge method.", false);
        }
    }

    private static HandshakeResponse CreateHandshake()
    {
        var process = Process.GetCurrentProcess();
        return new HandshakeResponse
        {
            BridgeVersion = GetBridgeVersion(),
            VisualStudioVersion = GetVisualStudioVersion(process),
            VisualStudioProcessId = process.Id,
            VsInstanceId = process.Id.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static CapabilitiesResponse CreateCapabilities() => new()
    {
        BridgeVersion = GetBridgeVersion(),
        VisualStudioVersion = GetVisualStudioVersion(Process.GetCurrentProcess()),
        Capabilities = new List<CapabilityDescriptor>
        {
            new()
            {
                Name = "phase0.ipc",
                Version = "0.1",
                IsStub = true
            }
        }
    };

    private static string GetBridgeVersion() =>
        typeof(BridgeServer).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    private static string GetVisualStudioVersion(Process process)
    {
        try
        {
            return process.MainModule?.FileVersionInfo.FileVersion ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    private static BridgeResponse Failure(string requestId, string code, string message, bool retryable) =>
        BridgeResponse.Failure(
            requestId ?? string.Empty,
            new BridgeError
            {
                Code = code,
                Message = message,
                Retryable = retryable
            });

    private static NamedPipeServerStream CreatePipe()
    {
        var user = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("The current Windows user SID is unavailable.");
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(
            new PipeAccessRule(
                user,
                PipeAccessRights.FullControl,
                AccessControlType.Allow));

        return new NamedPipeServerStream(
            PipeNames.ForCurrentUser(),
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            64 * 1024,
            64 * 1024,
            security);
    }
}