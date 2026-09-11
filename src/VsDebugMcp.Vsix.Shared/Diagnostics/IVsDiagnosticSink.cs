using System;
using System.Threading.Tasks;

namespace VsDebugMcp_Vsix.Diagnostics;

public interface IVsDiagnosticSink
{
    void LogInfo(string message);

    void LogWarning(string message);

    void LogError(string message, string? errorCode = null);

    void ReportStatus(VsMcpServiceStatus status, string detail);

    void ShowErrorBanner(string message, string errorCode, Func<Task>? retryAction = null);

    void ClearErrorBanner();

    void ActivateOutputPane();
}
