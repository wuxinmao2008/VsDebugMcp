using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;

namespace VsDebugMcp_Vsix;

internal sealed class SharedHostProcessManager
{
    private const string LogSource = "VsDebugMcp";
    private readonly HostControlClient _client = new();

    public async Task<bool> EnsureStartedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _client.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (HostControlException exception)
        {
            ActivityLog.LogError(LogSource, exception.Code);
            return false;
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
        }

        var hostPath = Path.Combine(
            Path.GetDirectoryName(typeof(VsDebugMcp_VsixPackage).Assembly.Location) ?? string.Empty,
            "Host",
            "VsDebugMcp.Host.exe");
        if (!File.Exists(hostPath))
        {
            ActivityLog.LogError(LogSource, "host_executable_unavailable");
            return false;
        }

        try
        {
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
            return false;
        }

        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _client.GetStatusAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (HostControlException exception)
            {
                ActivityLog.LogError(LogSource, exception.Code);
                return false;
            }
            catch (Exception exception) when (IsUnavailable(exception))
            {
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
        }

        ActivityLog.LogError(LogSource, "host_start_timeout");
        return false;
    }

    private static bool IsUnavailable(Exception exception) =>
        exception is IOException or TimeoutException;
}