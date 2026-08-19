using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using VsDebugMcp.Protocol;

namespace VsDebugMcp_Vsix;

internal sealed class HostRegistrationManager : IDisposable
{
    private const string LogSource = "VsDebugMcp";
    private readonly VisualStudioInstanceContext _instance;
    private readonly SharedHostProcessManager _hostManager = new();
    private readonly HostControlClient _client = new();
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _startupTask;
    private Task? _heartbeatTask;

    public HostRegistrationManager(VisualStudioInstanceContext instance)
    {
        _instance = instance;
    }

    public void Start()
    {
        _startupTask = Task.Run(() => StartAsync(_shutdown.Token));
    }

    private async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!await _hostManager.EnsureStartedAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            var descriptor = await _instance.CreateDescriptorAsync(cancellationToken).ConfigureAwait(false);
            var response = await _client.RegisterAsync(descriptor, cancellationToken).ConfigureAwait(false);
            if (!response.Accepted)
            {
                ActivityLog.LogError(LogSource, BridgeErrorCodes.RegistrationFailed);
                return;
            }

            _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(_shutdown.Token));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            ActivityLog.LogError(LogSource, BridgeErrorCodes.RegistrationFailed);
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _ = Task.Run(UnregisterAsync);
        _shutdown.Dispose();
    }

    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                var descriptor = await _instance.CreateDescriptorAsync(cancellationToken).ConfigureAwait(false);
                var response = await _client.HeartbeatAsync(descriptor, cancellationToken).ConfigureAwait(false);
                if (!response.Accepted)
                {
                    await _client.RegisterAsync(descriptor, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or TimeoutException or HostControlException)
            {
                try
                {
                    if (await _hostManager.EnsureStartedAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var descriptor = await _instance.CreateDescriptorAsync(cancellationToken).ConfigureAwait(false);
                        await _client.RegisterAsync(descriptor, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch
                {
                    ActivityLog.LogError(LogSource, BridgeErrorCodes.RegistrationFailed);
                }
            }
        }
    }

    private async Task UnregisterAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try
        {
            await _client.UnregisterAsync(_instance.VsInstanceId, timeout.Token).ConfigureAwait(false);
        }
        catch
        {
        }
    }
}