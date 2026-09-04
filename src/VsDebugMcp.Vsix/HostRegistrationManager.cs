using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using VsDebugMcp.Protocol;
using VsDebugMcp_Vsix.Diagnostics;

namespace VsDebugMcp_Vsix;

internal sealed class HostRegistrationManager : IDisposable
{
    private const string LogSource = "VsDebugMcp";
    private readonly VisualStudioInstanceContext _instance;
    private readonly IVsDiagnosticSink? _diagnostics;
    private readonly SharedHostProcessManager _hostManager;
    private readonly HostControlClient _client = new();
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _startupTask;
    private Task? _heartbeatTask;

    public HostRegistrationManager(VisualStudioInstanceContext instance, IVsDiagnosticSink? diagnostics = null)
    {
        _instance = instance;
        _diagnostics = diagnostics;
        _hostManager = new SharedHostProcessManager(diagnostics);
    }

    public void Start()
    {
        _startupTask = Task.Run(() => StartAsync(_shutdown.Token));
    }

    public async Task RetryRegistrationAsync()
    {
        _diagnostics?.LogInfo("收到手动重试请求，正在重新探测并注册 Host...");
        _diagnostics?.ReportStatus(VsMcpServiceStatus.Starting, "重试连接中...");
        await StartAsync(_shutdown.Token).ConfigureAwait(false);
    }

    private async Task StartAsync(CancellationToken cancellationToken)
    {
        _diagnostics?.ReportStatus(VsMcpServiceStatus.Starting, "正在注册实例...");
        _diagnostics?.LogInfo($"开始向 Host 注册当前 VS 实例: {_instance.VsInstanceId}");

        try
        {
            if (!await EnsureRegisteredAsync(cancellationToken).ConfigureAwait(false))
            {
                ActivityLog.LogError(LogSource, BridgeErrorCodes.RegistrationFailed);
                _diagnostics?.ReportStatus(VsMcpServiceStatus.Error, "注册失败");
                _diagnostics?.LogError("实例向 Host 注册失败，请检查输出日志以排查 Host 是否正常运行。", BridgeErrorCodes.RegistrationFailed);
                _diagnostics?.ShowErrorBanner("MCP 实例注册失败", BridgeErrorCodes.RegistrationFailed, RetryRegistrationAsync);
                return;
            }

            _diagnostics?.ReportStatus(VsMcpServiceStatus.Ready, "http://127.0.0.1:43260");
            _diagnostics?.LogInfo($"实例注册成功。MCP 服务端点已就绪: http://127.0.0.1:43260 (vsInstanceId: {_instance.VsInstanceId})");
            _diagnostics?.ClearErrorBanner();

            if (_heartbeatTask == null || _heartbeatTask.IsCompleted)
            {
                _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(_shutdown.Token));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ActivityLog.LogError(LogSource, BridgeErrorCodes.RegistrationFailed);
            _diagnostics?.ReportStatus(VsMcpServiceStatus.Error, "注册发生异常");
            _diagnostics?.LogError("注册流程发生异常: " + ex.Message, BridgeErrorCodes.RegistrationFailed);
            _diagnostics?.ShowErrorBanner("MCP 实例注册异常", BridgeErrorCodes.RegistrationFailed, RetryRegistrationAsync);
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
                    _diagnostics?.LogWarning("心跳被 Host 拒绝（可能 Host 已重启），尝试重新注册实例...");
                    var registerResponse = await _client.RegisterAsync(descriptor, cancellationToken).ConfigureAwait(false);
                    if (registerResponse.Accepted)
                    {
                        _diagnostics?.LogInfo("实例重新注册成功。");
                        _diagnostics?.ReportStatus(VsMcpServiceStatus.Ready, "http://127.0.0.1:43260");
                        _diagnostics?.ClearErrorBanner();
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or TimeoutException or HostControlException)
            {
                _diagnostics?.LogWarning($"心跳与 Host 通信中断 ({exception.GetType().Name}: {exception.Message})，尝试重新拉起并注册...");
                _diagnostics?.ReportStatus(VsMcpServiceStatus.Starting, "连接中断重试中...");
                try
                {
                    if (!await EnsureRegisteredAsync(cancellationToken).ConfigureAwait(false))
                    {
                        ActivityLog.LogError(LogSource, BridgeErrorCodes.RegistrationFailed);
                        _diagnostics?.ReportStatus(VsMcpServiceStatus.Error, "心跳重连失败");
                        _diagnostics?.LogError("心跳重连失败，Host 可能已终止。", BridgeErrorCodes.RegistrationFailed);
                        _diagnostics?.ShowErrorBanner("MCP 心跳丢失且重连失败", BridgeErrorCodes.RegistrationFailed, RetryRegistrationAsync);
                    }
                    else
                    {
                        _diagnostics?.ReportStatus(VsMcpServiceStatus.Ready, "http://127.0.0.1:43260");
                        _diagnostics?.LogInfo("Host 恢复连接并重新注册成功。");
                        _diagnostics?.ClearErrorBanner();
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    ActivityLog.LogError(LogSource, BridgeErrorCodes.RegistrationFailed);
                    _diagnostics?.ReportStatus(VsMcpServiceStatus.Error, "重连发生异常");
                    _diagnostics?.LogError("重连流程发生异常: " + ex.Message, BridgeErrorCodes.RegistrationFailed);
                    _diagnostics?.ShowErrorBanner("MCP 重连发生异常", BridgeErrorCodes.RegistrationFailed, RetryRegistrationAsync);
                }
            }
        }
    }

    private async Task<bool> EnsureRegisteredAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await _hostManager.EnsureStartedAsync(cancellationToken).ConfigureAwait(false))
                {
                    var descriptor = await _instance.CreateDescriptorAsync(cancellationToken).ConfigureAwait(false);
                    var response = await _client.RegisterAsync(descriptor, cancellationToken).ConfigureAwait(false);
                    if (response.Accepted)
                    {
                        return true;
                    }

                    _diagnostics?.LogWarning($"Host 拒绝了注册请求 (第 {attempt + 1} 次尝试)。");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or TimeoutException or HostControlException)
            {
                _diagnostics?.LogWarning($"注册尝试失败 ({exception.GetType().Name}: {exception.Message})，准备重试...");
            }

            if (attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            }
        }

        return false;
    }

    private async Task UnregisterAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try
        {
            await _client.UnregisterAsync(_instance.VsInstanceId, timeout.Token).ConfigureAwait(false);
            _diagnostics?.LogInfo($"已向 Host 注销实例: {_instance.VsInstanceId}");
            _diagnostics?.ReportStatus(VsMcpServiceStatus.Stopped, "已注销");
        }
        catch
        {
        }
    }
}