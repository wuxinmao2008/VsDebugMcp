using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using VsDebugMcp.Protocol;

namespace VsDebugMcp_Vsix;

internal sealed class VisualStudioInstanceContext
{
    private readonly AsyncPackage _package;
    private readonly System.Diagnostics.Process _process;

    private VisualStudioInstanceContext(AsyncPackage package, System.Diagnostics.Process process)
    {
        _package = package;
        _process = process;
        ProcessStartTimeUtcTicks = process.StartTime.ToUniversalTime().Ticks;
        VsInstanceId = VisualStudioInstanceIds.Create(process.Id, ProcessStartTimeUtcTicks);
        BridgePipeName = PipeNames.ForVisualStudioInstance(VsInstanceId);
    }

    public int ProcessId => _process.Id;

    public long ProcessStartTimeUtcTicks { get; }

    public string VsInstanceId { get; }

    public string BridgePipeName { get; }

    public static VisualStudioInstanceContext Create(AsyncPackage package) =>
        new(package, System.Diagnostics.Process.GetCurrentProcess());

    public async Task<VisualStudioInstanceDescriptor> CreateDescriptorAsync(CancellationToken cancellationToken)
    {
        var solutionName = string.Empty;
        var solutionFilePath = string.Empty;
        try
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var dte = await _package.GetServiceAsync(typeof(DTE)) as DTE2;
            solutionFilePath = dte?.Solution?.FullName ?? string.Empty;
            solutionName = string.IsNullOrWhiteSpace(solutionFilePath)
                ? string.Empty
                : Path.GetFileNameWithoutExtension(solutionFilePath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
        }

        return new VisualStudioInstanceDescriptor
        {
            VsInstanceId = VsInstanceId,
            VisualStudioProcessId = ProcessId,
            ProcessStartTimeUtcTicks = ProcessStartTimeUtcTicks,
            VisualStudioVersion = GetVisualStudioVersion(),
            SolutionName = solutionName,
            SolutionFilePath = solutionFilePath,
            BridgePipeName = BridgePipeName
        };
    }

    private string GetVisualStudioVersion()
    {
        try
        {
            return _process.MainModule?.FileVersionInfo.FileVersion ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}