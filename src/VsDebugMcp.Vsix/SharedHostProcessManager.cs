using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using VsDebugMcp_Vsix.Diagnostics;

namespace VsDebugMcp_Vsix;

internal sealed class SharedHostProcessManager
{
    private const string LogSource = "VsDebugMcp";
    private readonly HostControlClient _client = new();
    private readonly IVsDiagnosticSink? _diagnostics;

    public SharedHostProcessManager(IVsDiagnosticSink? diagnostics = null)
    {
        _diagnostics = diagnostics;
    }

    public async Task<bool> EnsureStartedAsync(CancellationToken cancellationToken)
    {
        try
        {
            _diagnostics?.LogInfo("正在探测 Host 控制管道状态...");
            await _client.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            _diagnostics?.LogInfo("检测到已运行的 Host 实例，状态正常。");
            return true;
        }
        catch (HostControlException exception)
        {
            ActivityLog.LogError(LogSource, exception.Code);
            _diagnostics?.LogError("Host 控制管道响应异常: " + exception.Code, exception.Code);
            return false;
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            _diagnostics?.LogInfo("未检测到活跃的 Host 进程，准备拉起内置 Host。");
        }

        var hostPath = Path.Combine(
            Path.GetDirectoryName(typeof(VsDebugMcp_VsixPackage).Assembly.Location) ?? string.Empty,
            "Host",
            "VsDebugMcp.Host.exe");
        if (!File.Exists(hostPath))
        {
            ActivityLog.LogError(LogSource, "host_executable_unavailable");
            _diagnostics?.LogError($"未在扩展目录中找到 Host 可执行文件: {hostPath}", "host_executable_unavailable");
            _diagnostics?.ShowErrorBanner("未找到 Host 可执行文件，请确认 VSIX 是否完整构建部署", "host_executable_unavailable");
            return false;
        }

        try
        {
            _diagnostics?.LogInfo($"正在启动 Host 进程: {hostPath}");
            Process.Start(new ProcessStartInfo
            {
                FileName = hostPath,
                WorkingDirectory = Path.GetDirectoryName(hostPath),
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ActivityLog.LogError(LogSource, "host_start_failed");
            _diagnostics?.LogError("启动 Host 进程失败: " + exception.Message, "host_start_failed");
            _diagnostics?.ShowErrorBanner("启动 Host 进程失败", "host_start_failed");
            return false;
        }

        _diagnostics?.LogInfo("Host 进程已拉起，正在等待控制管道就绪...");
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _client.GetStatusAsync(cancellationToken).ConfigureAwait(false);
                _diagnostics?.LogInfo($"Host 控制管道已就绪（耗时约 {(attempt + 1) * 250}ms）。");
                return true;
            }
            catch (HostControlException exception)
            {
                ActivityLog.LogError(LogSource, exception.Code);
                _diagnostics?.LogError("Host 控制管道响应异常: " + exception.Code, exception.Code);
                return false;
            }
            catch (Exception exception) when (IsUnavailable(exception))
            {
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
        }

        ActivityLog.LogError(LogSource, "host_start_timeout");
        _diagnostics?.LogError("等待 Host 上线超时（5秒）。可能原因：端口 43260 被占用或 Host 异常退出。", "host_start_timeout");
        _diagnostics?.ShowErrorBanner("Host 上线超时，请检查端口 43260 是否被占用", "host_start_timeout");
        return false;
    }

    private static bool IsUnavailable(Exception exception) =>
        exception is IOException or TimeoutException;
}