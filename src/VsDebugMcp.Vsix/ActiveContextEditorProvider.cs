using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using VsDebugMcp.Protocol;

namespace VsDebugMcp_Vsix;

internal sealed class ActiveContextEditorProvider
{
    private readonly AsyncPackage _package;
    private readonly string _vsInstanceId;

    public ActiveContextEditorProvider(AsyncPackage package, string vsInstanceId)
    {
        _package = package;
        _vsInstanceId = vsInstanceId;
    }

    public async Task<GetActiveDocumentResponse> GetActiveDocumentAsync(
        GetActiveDocumentRequest request,
        CancellationToken cancellationToken)
    {
        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        var dte = await _package.GetServiceAsync(typeof(DTE)) as DTE2
            ?? throw new ActiveContextEditorException(BridgeErrorCodes.SolutionStateUnavailable, "Visual Studio DTE is unavailable.", true);

        var response = new GetActiveDocumentResponse
        {
            VsInstanceId = _vsInstanceId
        };

        Document? activeDoc = null;
        try
        {
            activeDoc = dte.ActiveDocument;
        }
        catch
        {
            // ActiveDocument can throw when no document is active or during transition
        }

        if (activeDoc == null)
        {
            response.HasActiveDocument = false;
            return response;
        }

        response.HasActiveDocument = true;
        try
        {
            response.FilePath = activeDoc.FullName;
            response.FileName = activeDoc.Name;
            response.IsDirty = !activeDoc.Saved;
            response.IsReadOnly = activeDoc.ReadOnly;
            response.Language = activeDoc.Language;
        }
        catch
        {
            // Document might be closing
        }

        try
        {
            if (activeDoc.Selection is TextSelection textSel)
            {
                response.CursorLine = textSel.CurrentLine;
                response.CursorColumn = textSel.CurrentColumn;

                var selected = textSel.Text;
                if (!string.IsNullOrEmpty(selected))
                {
                    response.HasSelection = true;
                    response.SelectionStartLine = textSel.TopPoint?.Line ?? textSel.TopLine;
                    response.SelectionStartColumn = textSel.TopPoint?.DisplayColumn ?? 1;
                    response.SelectionEndLine = textSel.BottomPoint?.Line ?? textSel.BottomLine;
                    response.SelectionEndColumn = textSel.BottomPoint?.DisplayColumn ?? 1;

                    const int maxSelectedChars = 10000;
                    if (selected.Length > maxSelectedChars)
                    {
                        response.SelectedText = selected.Substring(0, maxSelectedChars) + "... [truncated]";
                    }
                    else
                    {
                        response.SelectedText = selected;
                    }
                }
            }

            if (activeDoc.Object("TextDocument") is TextDocument textDoc)
            {
                response.LineCount = textDoc.EndPoint?.Line;
            }
        }
        catch
        {
            // Ignore non-text or binary editor differences
        }

        return response;
    }

    public async Task<NavigateToResponse> NavigateToAsync(
        NavigateToRequest request,
        CancellationToken cancellationToken)
    {
        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        var dte = await _package.GetServiceAsync(typeof(DTE)) as DTE2
            ?? throw new ActiveContextEditorException(BridgeErrorCodes.SolutionStateUnavailable, "Visual Studio DTE is unavailable.", true);

        if (string.IsNullOrWhiteSpace(request.FilePath))
        {
            throw new ActiveContextEditorException(BridgeErrorCodes.InvalidRequest, "File path cannot be empty.", false);
        }

        string targetPath = request.FilePath.Trim();
        if (!Path.IsPathRooted(targetPath))
        {
            string? slnPath = dte.Solution?.FullName;
            if (!string.IsNullOrEmpty(slnPath))
            {
                string? slnDir = Path.GetDirectoryName(slnPath);
                if (!string.IsNullOrEmpty(slnDir))
                {
                    targetPath = Path.GetFullPath(Path.Combine(slnDir, targetPath));
                }
            }
        }

        if (!File.Exists(targetPath))
        {
            throw new ActiveContextEditorException(BridgeErrorCodes.FileNotFound, $"File not found: {targetPath}", false);
        }

        int targetLine = request.Line.GetValueOrDefault(1);
        int targetColumn = request.Column.GetValueOrDefault(1);
        if (targetLine < 1) targetLine = 1;
        if (targetColumn < 1) targetColumn = 1;

        Window? window = null;
        try
        {
            window = dte.ItemOperations.OpenFile(targetPath, EnvDTE.Constants.vsViewKindTextView);
            window?.Activate();
        }
        catch (Exception ex)
        {
            throw new ActiveContextEditorException(BridgeErrorCodes.InvalidNavigationTarget, $"Failed to open file: {ex.Message}", false, ex);
        }

        if (window?.Document?.Selection is TextSelection sel)
        {
            try
            {
                sel.GotoLine(targetLine, true);
                if (targetColumn > 1)
                {
                    sel.MoveToDisplayColumn(targetLine, targetColumn);
                }
            }
            catch
            {
                // Fallback if targetLine exceeds file length
            }
        }

        return new NavigateToResponse
        {
            VsInstanceId = _vsInstanceId,
            FilePath = targetPath,
            Line = targetLine,
            Column = targetColumn,
            Success = true
        };
    }

    public async Task<GetSolutionConfigurationsResponse> GetSolutionConfigurationsAsync(
        GetSolutionConfigurationsRequest request,
        CancellationToken cancellationToken)
    {
        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        var dte = await _package.GetServiceAsync(typeof(DTE)) as DTE2
            ?? throw new ActiveContextEditorException(BridgeErrorCodes.SolutionStateUnavailable, "Visual Studio DTE is unavailable.", true);

        if (dte.Solution == null || !dte.Solution.IsOpen)
        {
            throw new ActiveContextEditorException(BridgeErrorCodes.SolutionNotOpen, "No solution is currently open in Visual Studio.", false);
        }

        string slnPath = dte.Solution.FullName ?? string.Empty;
        string slnName = string.IsNullOrEmpty(slnPath) ? string.Empty : Path.GetFileNameWithoutExtension(slnPath);

        var response = new GetSolutionConfigurationsResponse
        {
            VsInstanceId = _vsInstanceId,
            SolutionName = slnName,
            SolutionPath = slnPath
        };

        var solutionBuild = dte.Solution.SolutionBuild;
        if (solutionBuild != null)
        {
            try
            {
                if (solutionBuild.ActiveConfiguration is SolutionConfiguration2 activeCfg2)
                {
                    response.ActiveConfigurationName = activeCfg2.Name ?? string.Empty;
                    response.ActivePlatformName = activeCfg2.PlatformName ?? string.Empty;
                }
                else if (solutionBuild.ActiveConfiguration is SolutionConfiguration activeCfg)
                {
                    response.ActiveConfigurationName = activeCfg.Name ?? string.Empty;
                }
            }
            catch
            {
            }

            try
            {
                if (solutionBuild.SolutionConfigurations is SolutionConfigurations cfgs)
                {
                    foreach (SolutionConfiguration cfg in cfgs)
                    {
                        string name = cfg.Name ?? string.Empty;
                        string platform = string.Empty;
                        if (cfg is SolutionConfiguration2 cfg2)
                        {
                            platform = cfg2.PlatformName ?? string.Empty;
                        }
                        string fullName = string.IsNullOrEmpty(platform) ? name : $"{name}|{platform}";
                        bool isActive = string.Equals(name, response.ActiveConfigurationName, StringComparison.OrdinalIgnoreCase) &&
                                        (string.IsNullOrEmpty(platform) || string.Equals(platform, response.ActivePlatformName, StringComparison.OrdinalIgnoreCase));

                        response.Configurations.Add(new SolutionConfigurationInfo
                        {
                            Name = name,
                            PlatformName = platform,
                            FullName = fullName,
                            IsActive = isActive
                        });
                    }
                }
            }
            catch
            {
            }
        }

        return response;
    }

    public async Task<SetSolutionConfigurationResponse> SetSolutionConfigurationAsync(
        SetSolutionConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Configuration))
        {
            throw new ActiveContextEditorException(BridgeErrorCodes.InvalidRequest, "Configuration name must be specified.", false);
        }

        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        var dte = await _package.GetServiceAsync(typeof(DTE)) as DTE2
            ?? throw new ActiveContextEditorException(BridgeErrorCodes.SolutionStateUnavailable, "Visual Studio DTE is unavailable.", true);

        if (dte.Solution == null || !dte.Solution.IsOpen)
        {
            throw new ActiveContextEditorException(BridgeErrorCodes.SolutionNotOpen, "No solution is currently open in Visual Studio.", false);
        }

        if (dte.Debugger != null && dte.Debugger.CurrentMode != dbgDebugMode.dbgDesignMode)
        {
            throw new ActiveContextEditorException(BridgeErrorCodes.CannotSwitchConfigurationWhileDebugging, "Cannot switch solution configuration while debugging is active.", false);
        }

        var solutionBuild = dte.Solution.SolutionBuild
            ?? throw new ActiveContextEditorException(BridgeErrorCodes.SolutionStateUnavailable, "Solution build manager is unavailable.", true);

        string prevCfg = string.Empty;
        string prevPlat = string.Empty;
        if (solutionBuild.ActiveConfiguration is SolutionConfiguration2 activeCfg2)
        {
            prevCfg = activeCfg2.Name ?? string.Empty;
            prevPlat = activeCfg2.PlatformName ?? string.Empty;
        }
        else if (solutionBuild.ActiveConfiguration is SolutionConfiguration activeCfg)
        {
            prevCfg = activeCfg.Name ?? string.Empty;
        }

        SolutionConfiguration? matchedCfg = null;
        if (solutionBuild.SolutionConfigurations is SolutionConfigurations cfgs)
        {
            foreach (SolutionConfiguration cfg in cfgs)
            {
                string name = cfg.Name ?? string.Empty;
                string platform = string.Empty;
                if (cfg is SolutionConfiguration2 c2)
                {
                    platform = c2.PlatformName ?? string.Empty;
                }

                if (string.Equals(name, request.Configuration.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(request.Platform) ||
                        string.Equals(platform, request.Platform.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        matchedCfg = cfg;
                        break;
                    }
                }
            }
        }

        if (matchedCfg == null)
        {
            string msg = string.IsNullOrWhiteSpace(request.Platform)
                ? $"Configuration '{request.Configuration}' was not found in the solution."
                : $"Configuration '{request.Configuration}' with platform '{request.Platform}' was not found in the solution.";
            throw new ActiveContextEditorException(BridgeErrorCodes.ConfigurationNotFound, msg, false);
        }

        try
        {
            matchedCfg.Activate();
        }
        catch (Exception ex)
        {
            throw new ActiveContextEditorException(BridgeErrorCodes.InternalError, $"Failed to activate solution configuration: {ex.Message}", false, ex);
        }

        string newCfg = string.Empty;
        string newPlat = string.Empty;
        if (solutionBuild.ActiveConfiguration is SolutionConfiguration2 newActive2)
        {
            newCfg = newActive2.Name ?? string.Empty;
            newPlat = newActive2.PlatformName ?? string.Empty;
        }
        else if (solutionBuild.ActiveConfiguration is SolutionConfiguration newActive)
        {
            newCfg = newActive.Name ?? string.Empty;
        }

        return new SetSolutionConfigurationResponse
        {
            VsInstanceId = _vsInstanceId,
            Success = true,
            PreviousConfiguration = prevCfg,
            PreviousPlatform = prevPlat,
            ActiveConfiguration = newCfg,
            ActivePlatform = newPlat
        };
    }
}

internal sealed class ActiveContextEditorException : Exception
{
    public ActiveContextEditorException(string code, string message, bool retryable = false, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Retryable = retryable;
    }

    public string Code { get; }
    public bool Retryable { get; }
}
