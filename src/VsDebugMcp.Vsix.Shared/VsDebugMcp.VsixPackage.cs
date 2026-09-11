using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using VsDebugMcp.Protocol;
using VsDebugMcp_Vsix.Diagnostics;

namespace VsDebugMcp_Vsix;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[Guid(VsDebugMcp_VsixPackage.PackageGuidString)]
public sealed class VsDebugMcp_VsixPackage : AsyncPackage
{
    public const string PackageGuidString = "e34c6f9d-54f1-4947-a2c4-9538e401bba9";
    public static readonly Guid CommandSet = new("9b0f69a5-8e24-4f27-a068-d064cfb68181");
    public const int ClientConfigCommandId = 0x0100;

    private VsDiagnosticService? _diagnosticService;
    private BridgeServer? _bridgeServer;
    private SolutionBuildProvider? _solutionBuildProvider;
    private HostRegistrationManager? _hostRegistrationManager;

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await base.InitializeAsync(cancellationToken, progress).ConfigureAwait(false);
        var instance = VisualStudioInstanceContext.Create(this);

        _diagnosticService = new VsDiagnosticService(this);
        await _diagnosticService.InitializeAsync().ConfigureAwait(false);
        _diagnosticService.LogInfo($"VsDebugMcp 扩展已加载。当前实例: {instance.VsInstanceId} (PID: {instance.ProcessId})");
        _diagnosticService.ReportStatus(VsMcpServiceStatus.Starting, "正在初始化...");

        _solutionBuildProvider = new SolutionBuildProvider(this, instance.VsInstanceId);
        await _solutionBuildProvider.InitializeAsync(cancellationToken);
        _bridgeServer = new BridgeServer(this, _solutionBuildProvider, instance, _diagnosticService);
        _bridgeServer.Start();
        _diagnosticService.LogInfo($"BridgeServer 命名管道服务已启动: {instance.BridgePipeName}");

        _hostRegistrationManager = new HostRegistrationManager(instance, _diagnosticService);
        _hostRegistrationManager.Start();

        // 注册菜单命令
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commandService)
        {
            var menuCommandID = new CommandID(CommandSet, ClientConfigCommandId);
            var menuItem = new MenuCommand(ShowClientConfigWindow, menuCommandID);
            commandService.AddCommand(menuItem);
            _diagnosticService.LogInfo("已注册菜单命令: 扩展 -> VsDebugMcp");
        }
    }

    private void ShowClientConfigWindow(object? sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            var window = new ClientConfigWindow();
            var hwnd = GetDialogOwnerHwnd();
            if (hwnd != IntPtr.Zero)
            {
                new System.Windows.Interop.WindowInteropHelper(window) { Owner = hwnd };
            }
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            _diagnosticService?.LogError($"打开客户端配置指引窗口失败: {ex.Message}", "client_config_window_error");
            VsShellUtilities.ShowMessageBox(
                this,
                $"无法打开客户端配置指引窗口:\n{ex.Message}",
                "VsDebugMcp",
                OLEMSGICON.OLEMSGICON_CRITICAL,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }

    private IntPtr GetDialogOwnerHwnd()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            if (GetService(typeof(SVsUIShell)) is IVsUIShell uiShell &&
                uiShell.GetDialogOwnerHwnd(out var hwnd) == Microsoft.VisualStudio.VSConstants.S_OK)
            {
                return hwnd;
            }
        }
        catch
        {
        }
        return IntPtr.Zero;
    }

    protected override void Dispose(bool disposing)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (disposing)
        {
            _hostRegistrationManager?.Dispose();
            _hostRegistrationManager = null;
            _bridgeServer?.Dispose();
            _bridgeServer = null;
            _solutionBuildProvider?.Dispose();
            _solutionBuildProvider = null;
            _diagnosticService?.Dispose();
            _diagnosticService = null;
        }

        base.Dispose(disposing);
    }
}
