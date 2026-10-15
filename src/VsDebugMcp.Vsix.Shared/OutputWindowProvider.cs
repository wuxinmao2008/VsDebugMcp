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
    private static readonly Guid TestOutputPaneGuid1 = new("B85579AA-8BE0-4C4F-A850-90902B317581");
    private static readonly Guid TestOutputPaneGuid2 = new("E8365304-9908-466D-9B5D-4C1F671C401B");
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

    public async Task<GetOutputPanesResponse> GetPanesAsync(
        GetOutputPanesRequest request,
        CancellationToken cancellationToken)
    {
        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        try
        {
            var dte = await _package.GetServiceAsync(typeof(DTE)) as DTE2
                ?? throw new OutputWindowProviderException();

            var response = new GetOutputPanesResponse
            {
                VsInstanceId = _vsInstanceId
            };

            foreach (OutputWindowPane pane in dte.ToolWindows.OutputWindow.OutputWindowPanes)
            {
                string paneGuidStr = pane.Guid ?? string.Empty;
                bool isBuiltIn = false;
                if (Guid.TryParse(paneGuidStr, out var paneGuid))
                {
                    if (paneGuid == VSConstants.OutputWindowPaneGuid.BuildOutputPane_guid ||
                        paneGuid == VSConstants.OutputWindowPaneGuid.DebugPane_guid ||
                        paneGuid == VSConstants.OutputWindowPaneGuid.GeneralPane_guid ||
                        paneGuid == VSConstants.OutputWindowPaneGuid.SortedBuildOutputPane_guid ||
                        paneGuid == TestOutputPaneGuid1 ||
                        paneGuid == TestOutputPaneGuid2)
                    {
                        isBuiltIn = true;
                    }
                }

                response.Panes.Add(new OutputPaneInfo
                {
                    Name = pane.Name ?? string.Empty,
                    Guid = paneGuidStr,
                    IsBuiltIn = isBuiltIn
                });
            }

            response.TotalCount = response.Panes.Count;
            return response;
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

    internal static string ReadPaneOutput(DTE2 dte, string source)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        OutputWindowPane? matchedPane = null;
        var trimmedSource = (source ?? string.Empty).Trim();

        foreach (OutputWindowPane pane in dte.ToolWindows.OutputWindow.OutputWindowPanes)
        {
            var paneName = pane.Name ?? string.Empty;
            var hasGuid = Guid.TryParse(pane.Guid, out var paneGuid);

            if (string.Equals(trimmedSource, "build", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmedSource, "生成", StringComparison.OrdinalIgnoreCase))
            {
                if (hasGuid && (paneGuid == VSConstants.OutputWindowPaneGuid.BuildOutputPane_guid ||
                                paneGuid == VSConstants.OutputWindowPaneGuid.SortedBuildOutputPane_guid))
                {
                    matchedPane = pane;
                    break;
                }
                if (paneName.IndexOf("build", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    paneName.IndexOf("生成", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    matchedPane = pane;
                    break;
                }
            }
            else if (string.Equals(trimmedSource, "debug", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(trimmedSource, "调试", StringComparison.OrdinalIgnoreCase))
            {
                if (hasGuid && paneGuid == VSConstants.OutputWindowPaneGuid.DebugPane_guid)
                {
                    matchedPane = pane;
                    break;
                }
                if (paneName.IndexOf("debug", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    paneName.IndexOf("调试", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    matchedPane = pane;
                    break;
                }
            }
            else if (string.Equals(trimmedSource, "tests", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(trimmedSource, "test", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(trimmedSource, "测试", StringComparison.OrdinalIgnoreCase))
            {
                if (hasGuid && (paneGuid == TestOutputPaneGuid1 || paneGuid == TestOutputPaneGuid2))
                {
                    matchedPane = pane;
                    break;
                }
                if (paneName.IndexOf("test", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    paneName.IndexOf("测试", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    matchedPane = pane;
                    break;
                }
            }

            if (paneName.Equals(trimmedSource, StringComparison.OrdinalIgnoreCase) ||
                paneName.IndexOf(trimmedSource, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                matchedPane = pane;
                break;
            }
        }

        if (matchedPane == null)
        {
            return string.Empty;
        }

        try
        {
            var document = matchedPane.TextDocument;
            var editPoint = document.StartPoint.CreateEditPoint();
            return editPoint.GetText(document.EndPoint);
        }
        catch
        {
            return string.Empty;
        }
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