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
            : requestedSource!.Trim().ToLowerInvariant();
        var maxChars = request.MaxChars ?? DefaultMaxChars;
        if (source != "build" || maxChars < 1 || maxChars > MaximumMaxChars)
        {
            throw OutputWindowProviderException.InvalidRequest();
        }

        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        try
        {
            var dte = await _package.GetServiceAsync(typeof(DTE)) as DTE2
                ?? throw new OutputWindowProviderException();
            var text = ReadBuildOutput(dte);
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

    private static string ReadBuildOutput(DTE2 dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        foreach (OutputWindowPane pane in dte.ToolWindows.OutputWindow.OutputWindowPanes)
        {
            if (!Guid.TryParse(pane.Guid, out var paneGuid) ||
                paneGuid != VSConstants.OutputWindowPaneGuid.BuildOutputPane_guid)
            {
                continue;
            }

            var document = pane.TextDocument;
            var editPoint = document.StartPoint.CreateEditPoint();
            return editPoint.GetText(document.EndPoint);
        }

        throw new OutputWindowProviderException();
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