using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Serialization;
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
    private readonly SolutionProjectProvider _solutionProjectProvider;
    private readonly SolutionBuildProvider _solutionBuildProvider;
    private NamedPipeServerStream? _activePipe;
    private Task? _serverTask;

    public BridgeServer(AsyncPackage package, SolutionBuildProvider solutionBuildProvider)
    {
        _solutionProjectProvider = new SolutionProjectProvider(package);
        _solutionBuildProvider = solutionBuildProvider;
    }

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

    private async Task ProcessConnectionAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
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
            var handled = await HandleRequestAsync(request, cancellationToken).ConfigureAwait(false);
            var response = handled.Response;
            await PipeMessageFraming.WriteAsync(pipe, response, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            var result = response.Error?.Code ?? "ok";
            ActivityLog.LogInformation(
                LogSource,
                $"method={request.Method};requestId={request.RequestId};elapsedMs={stopwatch.ElapsedMilliseconds};result={result}");

            if (handled.CloseConnection)
            {
                break;
            }
        }
    }

    private async Task<(BridgeResponse Response, bool CloseConnection)> HandleRequestAsync(
        BridgeRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RequestId) || string.IsNullOrWhiteSpace(request.Method))
        {
            return (
                Failure(request.RequestId, BridgeErrorCodes.InvalidRequest, "Request ID and method are required.", false),
                false);
        }

        if (!string.Equals(request.ProtocolVersion, BridgeProtocol.Version, StringComparison.Ordinal))
        {
            return (
                Failure(
                    request.RequestId,
                    BridgeErrorCodes.ProtocolMismatch,
                    $"Protocol {request.ProtocolVersion} is not supported.",
                    false),
                false);
        }

        switch (request.Method)
        {
            case BridgeMethods.Handshake:
                return (BridgeResponse.Success(request.RequestId, CreateHandshake()), false);
            case BridgeMethods.Health:
                return (
                    BridgeResponse.Success(
                        request.RequestId,
                        new HealthResponse
                        {
                            Status = "ok",
                            UtcTimestamp = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                        }),
                    false);
            case BridgeMethods.Capabilities:
                return (BridgeResponse.Success(request.RequestId, CreateCapabilities()), false);
            case BridgeMethods.GetProjectsInSolution:
                try
                {
                    var result = await _solutionProjectProvider.GetProjectsAsync(cancellationToken);
                    return (BridgeResponse.Success(request.RequestId, result), false);
                }
                catch (SolutionStateUnavailableException)
                {
                    return (
                        Failure(
                            request.RequestId,
                            BridgeErrorCodes.SolutionStateUnavailable,
                            "The Visual Studio solution state is unavailable.",
                            true),
                        false);
                }
            case BridgeMethods.RunBuild:
                return await HandleBuildRequestAsync<RunBuildRequest, BuildTaskResponse>(
                    request,
                    payload => _solutionBuildProvider.RunBuildAsync(payload, cancellationToken));
            case BridgeMethods.GetBuildStatus:
                return HandleBuildRequest<GetBuildStatusRequest, BuildTaskResponse>(
                    request,
                    payload => _solutionBuildProvider.GetBuildStatus(payload.BuildTaskId));
            case BridgeMethods.CancelBuild:
                return await HandleBuildRequestAsync<CancelBuildRequest, CancelBuildResponse>(
                    request,
                    payload => _solutionBuildProvider.CancelBuildAsync(payload.BuildTaskId, cancellationToken));
            case BridgeMethods.Shutdown:
                return (BridgeResponse.Success(request.RequestId, new ShutdownResponse { Accepted = true }), true);
            default:
                return (
                    Failure(request.RequestId, BridgeErrorCodes.InvalidRequest, "Unknown bridge method.", false),
                    false);
        }
    }

    private static (BridgeResponse Response, bool CloseConnection) HandleBuildRequest<TRequest, TResponse>(
        BridgeRequest request,
        Func<TRequest, TResponse> action)
    {
        try
        {
            var payload = BridgeJson.Deserialize<TRequest>(request.PayloadJson ?? string.Empty);
            return (BridgeResponse.Success(request.RequestId, action(payload)), false);
        }
        catch (SerializationException)
        {
            return (Failure(request.RequestId, BridgeErrorCodes.InvalidRequest, "The build request is invalid.", false), false);
        }
        catch (BuildProviderException exception)
        {
            return (Failure(request.RequestId, exception.Code, GetBuildErrorMessage(exception.Code), exception.Retryable), false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return (
                Failure(
                    request.RequestId,
                    BridgeErrorCodes.BuildStateUnavailable,
                    GetBuildErrorMessage(BridgeErrorCodes.BuildStateUnavailable),
                    true),
                false);
        }
    }

    private static async Task<(BridgeResponse Response, bool CloseConnection)> HandleBuildRequestAsync<TRequest, TResponse>(
        BridgeRequest request,
        Func<TRequest, Task<TResponse>> action)
    {
        try
        {
            var payload = BridgeJson.Deserialize<TRequest>(request.PayloadJson ?? string.Empty);
            return (BridgeResponse.Success(request.RequestId, await action(payload)), false);
        }
        catch (SerializationException)
        {
            return (Failure(request.RequestId, BridgeErrorCodes.InvalidRequest, "The build request is invalid.", false), false);
        }
        catch (BuildProviderException exception)
        {
            return (Failure(request.RequestId, exception.Code, GetBuildErrorMessage(exception.Code), exception.Retryable), false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return (
                Failure(
                    request.RequestId,
                    BridgeErrorCodes.BuildStateUnavailable,
                    GetBuildErrorMessage(BridgeErrorCodes.BuildStateUnavailable),
                    true),
                false);
        }
    }

    private static string GetBuildErrorMessage(string code) => code switch
    {
        BridgeErrorCodes.SolutionNotOpen => "No Visual Studio solution is open.",
        BridgeErrorCodes.BuildInProgress => "A Visual Studio build is already in progress.",
        BridgeErrorCodes.InvalidBuildConfiguration => "The requested solution configuration or platform is invalid.",
        BridgeErrorCodes.BuildTaskNotFound => "The build task was not found.",
        BridgeErrorCodes.BuildNotActive => "The build task is not active.",
        BridgeErrorCodes.BuildCancelNotSupported => "The active Visual Studio build cannot be cancelled.",
        BridgeErrorCodes.BuildStartFailed => "Visual Studio could not start the build.",
        _ => "The Visual Studio build state is unavailable."
    };

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
            },
            new()
            {
                Name = "vs_get_projects_in_solution",
                Version = "0.1",
                IsStub = false
            },
            new()
            {
                Name = "vs_run_build",
                Version = "0.1",
                IsStub = false
            },
            new()
            {
                Name = "vs_get_build_status",
                Version = "0.1",
                IsStub = false
            },
            new()
            {
                Name = "vs_cancel_build",
                Version = "0.1",
                IsStub = false
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