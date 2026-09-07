using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using VsDebugMcp.Protocol;

namespace VsDebugMcp_Vsix;

internal sealed class OutputWindowProvider
{
    private const int DefaultMaxChars = 20000;
    private const int MaximumMaxChars = 500000;
    private readonly AsyncPackage _package;
    private readonly string _vsInstanceId;

    public OutputWindowProvider(AsyncPackage package, string vsInstanceId)
    {
        _package = package;
        _vsInstanceId = vsInstanceId;
    }

    public async Task<GetOutputWindowLogsResponse> GetLogsAsync(
        GetOutputWindowLogsRequest request,
        CancellationToken cancellationToken)
    {
        var requestedSource = request.Source;
        var source = string.IsNullOrWhiteSpace(requestedSource)
            ? "build"
            : requestedSource!.Trim();
        var maxChars = request.MaxChars ?? DefaultMaxChars;
        if (maxChars < 1 || maxChars > MaximumMaxChars)
        {
            throw OutputWindowProviderException.InvalidRequest();
        }

        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        try
        {
            var dte = await _package.GetServiceAsync(typeof(DTE)) as DTE2
                ?? throw new OutputWindowProviderException();
            var text = ReadPaneOutput(dte, source);
            var returnedText = text.Length > maxChars ? text.Substring(text.Length - maxChars) : text;
            return new GetOutputWindowLogsResponse
            {
                VsInstanceId = _vsInstanceId,
                Source = source,
                CapturedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                TotalChars = text.Length,
                ReturnedChars = returnedText.Length,
                Truncated = returnedText.Length < text.Length,
                Text = returnedText
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OutputWindowProviderException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new OutputWindowProviderException(exception);
        }
    }

    private static string ReadPaneOutput(DTE2 dte, string source)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        OutputWindowPane? matchedPane = null;
        foreach (OutputWindowPane pane in dte.ToolWindows.OutputWindow.OutputWindowPanes)
        {
            if (string.Equals(source, "build", StringComparison.OrdinalIgnoreCase))
            {
                if (Guid.TryParse(pane.Guid, out var paneGuid) &&
                    paneGuid == VSConstants.OutputWindowPaneGuid.BuildOutputPane_guid)
                {
                    matchedPane = pane;
                    break;
                }
            }

            if (pane.Name.Equals(source, StringComparison.OrdinalIgnoreCase) ||
                pane.Name.IndexOf(source, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                matchedPane = pane;
                break;
            }
        }

        if (matchedPane == null)
        {
            return string.Empty;
        }

        var document = matchedPane.TextDocument;
        var editPoint = document.StartPoint.CreateEditPoint();
        return editPoint.GetText(document.EndPoint);
    }
}

internal sealed class OutputWindowProviderException : Exception
{
    public OutputWindowProviderException(Exception? innerException = null)
        : this(BridgeErrorCodes.OutputUnavailable, true, innerException)
    {
    }

    private OutputWindowProviderException(string code, bool retryable, Exception? innerException)
        : base("The Visual Studio output window is unavailable.", innerException)
    {
        Code = code;
        Retryable = retryable;
    }

    public string Code { get; }

    public bool Retryable { get; }

    public static OutputWindowProviderException InvalidRequest() =>
        new(BridgeErrorCodes.InvalidRequest, false, null);
}