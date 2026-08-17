using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace VsDebugMcp_Vsix;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
[Guid(VsDebugMcp_VsixPackage.PackageGuidString)]
public sealed class VsDebugMcp_VsixPackage : AsyncPackage
{
    public const string PackageGuidString = "e34c6f9d-54f1-4947-a2c4-9538e401bba9";
    private BridgeServer? _bridgeServer;

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await base.InitializeAsync(cancellationToken, progress).ConfigureAwait(false);
        _bridgeServer = new BridgeServer();
        _bridgeServer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bridgeServer?.Dispose();
            _bridgeServer = null;
        }

        base.Dispose(disposing);
    }
}
