using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using VsDebugMcp.Protocol;

namespace VsDebugMcp_Vsix;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
[Guid(VsDebugMcp_VsixPackage.PackageGuidString)]
public sealed class VsDebugMcp_VsixPackage : AsyncPackage
{
    public const string PackageGuidString = "e34c6f9d-54f1-4947-a2c4-9538e401bba9";
    private BridgeServer? _bridgeServer;
    private SolutionBuildProvider? _solutionBuildProvider;
    private HostRegistrationManager? _hostRegistrationManager;

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await base.InitializeAsync(cancellationToken, progress).ConfigureAwait(false);
        var instance = VisualStudioInstanceContext.Create(this);
        _solutionBuildProvider = new SolutionBuildProvider(this, instance.VsInstanceId);
        await _solutionBuildProvider.InitializeAsync(cancellationToken);
        _bridgeServer = new BridgeServer(this, _solutionBuildProvider, instance);
        _bridgeServer.Start();
        _hostRegistrationManager = new HostRegistrationManager(instance);
        _hostRegistrationManager.Start();
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
        }

        base.Dispose(disposing);
    }
}
