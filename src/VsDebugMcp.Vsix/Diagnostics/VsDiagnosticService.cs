using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace VsDebugMcp_Vsix.Diagnostics;

public sealed class VsDiagnosticService : IVsDiagnosticSink, IVsInfoBarUIEvents, IDisposable
{
    public static readonly Guid OutputPaneGuid = new("e8d25ca7-4226-4b4b-9721-a47738ee6858");
    public const string OutputPaneTitle = "VsDebugMcp";

    private readonly AsyncPackage _package;
    private IVsOutputWindowPane? _outputPane;
    private IVsStatusbar? _statusBar;
    private IVsInfoBarHost? _infoBarHost;
    private IVsInfoBarUIFactory? _infoBarFactory;
    private IVsInfoBarUIElement? _currentInfoBarElement;
    private uint _infoBarCookie;
    private Func<Task>? _retryAction;
    private bool _isDisposed;

    public VsDiagnosticService(AsyncPackage package)
    {
        _package = package ?? throw new ArgumentNullException(nameof(package));
    }

    public async Task InitializeAsync()
    {
        try
        {
            var outputWindow = await _package.GetServiceAsync(typeof(SVsOutputWindow)).ConfigureAwait(false) as IVsOutputWindow;
            if (outputWindow != null)
            {
                var paneGuid = OutputPaneGuid;
                outputWindow.GetPane(ref paneGuid, out var pane);
                if (pane == null)
                {
                    outputWindow.CreatePane(ref paneGuid, OutputPaneTitle, fInitVisible: 1, fClearWithSolution: 0);
                    outputWindow.GetPane(ref paneGuid, out pane);
                }

                _outputPane = pane;
            }

            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();

            _statusBar = await _package.GetServiceAsync(typeof(SVsStatusbar)) as IVsStatusbar;
            _infoBarFactory = await _package.GetServiceAsync(typeof(SVsInfoBarUIFactory)) as IVsInfoBarUIFactory;

            var shell = await _package.GetServiceAsync(typeof(SVsShell)) as IVsShell;
            if (shell != null &&
                ErrorHandler.Succeeded(shell.GetProperty((int)__VSSPROPID7.VSSPROPID_MainWindowInfoBarHost, out var hostObj)) &&
                hostObj is IVsInfoBarHost host)
            {
                _infoBarHost = host;
            }
        }
        catch (Exception ex)
        {
            ActivityLog.LogError("VsDebugMcp", "Failed to initialize diagnostic service: " + ex.Message);
        }
    }

    public void LogInfo(string message) => WriteLog("INFO", message);

    public void LogWarning(string message) => WriteLog("WARN", message);

    public void LogError(string message, string? errorCode = null)
    {
        var formatted = string.IsNullOrEmpty(errorCode)
            ? message
            : $"{message} [{errorCode}]";
        WriteLog("ERROR", formatted);
    }

    private void WriteLog(string level, string message)
    {
        var line = string.Format(
            CultureInfo.InvariantCulture,
            "[{0:HH:mm:ss}] [{1}] {2}\r\n",
            DateTime.Now,
            level,
            message);

        try
        {
            _outputPane?.OutputStringThreadSafe(line);
        }
        catch
        {
        }
    }

    public void ReportStatus(VsMcpServiceStatus status, string detail)
    {
        var statusText = status switch
        {
            VsMcpServiceStatus.Starting => "VsDebugMcp: 正在启动 MCP Host...",
            VsMcpServiceStatus.Ready => $"VsDebugMcp: 就绪 ({detail})",
            VsMcpServiceStatus.Error => $"VsDebugMcp: 异常 - {detail}",
            _ => "VsDebugMcp: 已停止"
        };

        _package.JoinableTaskFactory.RunAsync(async () =>
        {
            try
            {
                await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (_statusBar != null)
                {
                    _statusBar.FreezeOutput(0);
                    _statusBar.SetText(statusText);
                }
            }
            catch
            {
            }
        });
    }

    public void ShowErrorBanner(string message, string errorCode, Func<Task>? retryAction = null)
    {
        _retryAction = retryAction;

        _package.JoinableTaskFactory.RunAsync(async () =>
        {
            try
            {
                await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                ClearErrorBannerCore();

                if (_infoBarHost == null || _infoBarFactory == null)
                {
                    return;
                }

                var actionItems = new List<InfoBarActionItem>
                {
                    new InfoBarButton("查看输出日志", "view_logs")
                };

                if (retryAction != null)
                {
                    actionItems.Add(new InfoBarButton("重试启动", "retry"));
                }

                var fullMessage = $"VsDebugMcp: {message} ({errorCode})";
                var model = new InfoBarModel(
                    fullMessage,
                    actionItems,
                    KnownMonikers.StatusWarning,
                    isCloseButtonVisible: true);

                var element = _infoBarFactory.CreateInfoBar(model);
                element.Advise(this, out _infoBarCookie);
                _infoBarHost.AddInfoBar(element);
                _currentInfoBarElement = element;
            }
            catch (Exception ex)
            {
                ActivityLog.LogError("VsDebugMcp", "Failed to show InfoBar: " + ex.Message);
            }
        });
    }

    public void ClearErrorBanner()
    {
        _package.JoinableTaskFactory.RunAsync(async () =>
        {
            try
            {
                await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                ClearErrorBannerCore();
            }
            catch
            {
            }
        });
    }

    private void ClearErrorBannerCore()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_currentInfoBarElement != null)
        {
            if (_infoBarCookie != 0)
            {
                _currentInfoBarElement.Unadvise(_infoBarCookie);
                _infoBarCookie = 0;
            }

            _infoBarHost?.RemoveInfoBar(_currentInfoBarElement);
            _currentInfoBarElement = null;
        }

        _retryAction = null;
    }

    public void ActivateOutputPane()
    {
        _package.JoinableTaskFactory.RunAsync(async () =>
        {
            try
            {
                await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                _outputPane?.Activate();
            }
            catch
            {
            }
        });
    }

    public void OnClosed(IVsInfoBarUIElement infoBarUIElement)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (ReferenceEquals(infoBarUIElement, _currentInfoBarElement))
        {
            ClearErrorBannerCore();
        }
    }

    public void OnActionItemClicked(IVsInfoBarUIElement infoBarUIElement, IVsInfoBarActionItem actionItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var actionId = actionItem.ActionContext as string;

        if (string.Equals(actionId, "view_logs", StringComparison.OrdinalIgnoreCase))
        {
            ActivateOutputPane();
        }
        else if (string.Equals(actionId, "retry", StringComparison.OrdinalIgnoreCase))
        {
            var retry = _retryAction;
            ClearErrorBannerCore();
            if (retry != null)
            {
                _ = Task.Run(retry);
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _package.JoinableTaskFactory.Run(async () =>
        {
            try
            {
                await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                ClearErrorBannerCore();
            }
            catch
            {
            }
        });
    }
}
