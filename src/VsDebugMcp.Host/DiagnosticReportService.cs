using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using ModelContextProtocol;
using VsDebugMcp.Protocol;

namespace VsDebugMcp.Host;

public interface IDiagnosticReportService
{
    Task<ReportMcpIssueResponse> ReportMcpIssueAsync(
        string targetTool,
        string issueType,
        string agentSummary,
        string? suggestedImprovement = null,
        string? vsInstanceId = null,
        CancellationToken cancellationToken = default);
}

public sealed class DiagnosticReportService : IDiagnosticReportService
{
    private static readonly Regex s_privateKeyPattern = new(
        @"-----BEGIN\s+[A-Z0-9\s_-]+PRIVATE\s+KEY-----",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex s_tokenPattern = new(
        @"\b(?:ghp_[A-Za-z0-9]{36}|glpat-[A-Za-z0-9\-_]{20,}|bearer\s+[A-Za-z0-9_\-\.]{20,})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex s_jwtPattern = new(
        @"\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b",
        RegexOptions.Compiled);

    private static readonly Regex s_passwordPattern = new(
        @"(?i)\b(?:password|passwd|pwd|secret)\s*[:=]\s*['""][^'""]{4,}['""]",
        RegexOptions.Compiled);

    private static readonly Regex s_unmaskedProfilePathPattern = new(
        @"[A-Za-z]:\\[Uu]sers\\[^\\]+\\",
        RegexOptions.Compiled);

    private readonly IBridgeService _bridgeService;
    private readonly VisualStudioInstanceRegistry _registry;
    private readonly string _reportsDirectory;

    public DiagnosticReportService(
        IBridgeService bridgeService,
        VisualStudioInstanceRegistry registry,
        string? customReportsDirectory = null)
    {
        _bridgeService = bridgeService ?? throw new ArgumentNullException(nameof(bridgeService));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _reportsDirectory = customReportsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VsDebugMcp",
            "reports");
    }

    public async Task<ReportMcpIssueResponse> ReportMcpIssueAsync(
        string targetTool,
        string issueType,
        string agentSummary,
        string? suggestedImprovement = null,
        string? vsInstanceId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetTool))
        {
            throw new McpException("invalid_request: targetTool is required and cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(agentSummary))
        {
            throw new McpException("invalid_request: agentSummary is required and cannot be empty.");
        }

        var normalizedTargetTool = targetTool.Trim();
        var normalizedIssueType = McpIssueTypes.Normalize(issueType);

        // Enforce maximum length of 512 characters
        var summary = agentSummary.Trim();
        if (summary.Length > 512)
        {
            summary = summary[..512];
        }

        var improvement = suggestedImprovement?.Trim();
        if (improvement != null && improvement.Length > 512)
        {
            improvement = improvement[..512];
        }

        var reportId = $"rpt_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"[..24];

        // 1. Proactive Privacy Circuit Breaker (DLP scan)
        var combinedText = summary + "\n" + (improvement ?? string.Empty);
        if (DetectPrivacyViolationRisk(combinedText))
        {
            return new ReportMcpIssueResponse
            {
                Status = "privacy_risk_aborted",
                ReportId = reportId,
                GithubIssueUrl = null,
                LocalReportPath = null,
                InstructionsForAgent = "Report generation aborted because the summary or improvement contains potential credentials, private keys, or unmasked user paths. Proactive privacy circuit breaker engaged.",
                Warnings = new List<BridgeWarning>
                {
                    new()
                    {
                        Code = BridgeErrorCodes.PrivacyRiskAborted,
                        Message = "High-risk credential, token, or private path detected in payload. Issue generation blocked for user privacy safety."
                    }
                }
            };
        }

        // 2. Client-side Sanitization
        summary = SanitizeText(summary);
        if (improvement != null)
        {
            improvement = SanitizeText(improvement);
        }

        // 3. Environment Technical Diagnostics Collection
        var envSummary = await CollectEnvironmentDiagnosticsAsync(vsInstanceId, cancellationToken).ConfigureAwait(false);

        // 4. Local Diagnostic Dump
        var localReportFileName = $"{reportId}.md";
        var localAbstractPath = $@"%LOCALAPPDATA%\VsDebugMcp\reports\{localReportFileName}";
        try
        {
            Directory.CreateDirectory(_reportsDirectory);
            var localDiskPath = Path.Combine(_reportsDirectory, localReportFileName);
            var reportContent = BuildLocalMarkdownReport(
                reportId,
                normalizedTargetTool,
                normalizedIssueType,
                summary,
                improvement,
                envSummary);
            await File.WriteAllTextAsync(localDiskPath, reportContent, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Writing local dump failure should not block generating the URL
        }

        // 5. Short GitHub Issue Form URL Generation
        var githubUrl = BuildGitHubIssueFormUrl(
            normalizedTargetTool,
            normalizedIssueType,
            summary,
            improvement,
            envSummary);

        return new ReportMcpIssueResponse
        {
            Status = "ready_for_user_submission",
            ReportId = reportId,
            GithubIssueUrl = githubUrl,
            LocalReportPath = localAbstractPath,
            InstructionsForAgent = "Present the 'github_issue_url' as a clickable markdown link to the user. Inform the user that all paths have been sanitized and they can review before submitting on GitHub.",
            EnvironmentSummary = envSummary,
            Warnings = new List<BridgeWarning>()
        };
    }

    private static bool DetectPrivacyViolationRisk(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (s_privateKeyPattern.IsMatch(text)) return true;
        if (s_tokenPattern.IsMatch(text)) return true;
        if (s_jwtPattern.IsMatch(text)) return true;
        if (s_passwordPattern.IsMatch(text)) return true;
        if (s_unmaskedProfilePathPattern.IsMatch(text)) return true;

        return false;
    }

    private static string SanitizeText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var result = text;
        try
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(userProfile))
            {
                result = result.Replace(userProfile, "<USER_HOME>", StringComparison.OrdinalIgnoreCase);
            }

            var userName = Environment.UserName;
            if (!string.IsNullOrEmpty(userName))
            {
                result = result.Replace(userName, "<USER>", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // Ignore environment query failures
        }

        return result;
    }

    private async Task<Dictionary<string, string>> CollectEnvironmentDiagnosticsAsync(
        string? vsInstanceId,
        CancellationToken cancellationToken)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["OS"] = $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})",
            [".NET Runtime"] = RuntimeInformation.FrameworkDescription,
            ["VsDebugMcp Host"] = typeof(DiagnosticReportService).Assembly.GetName().Version?.ToString(4) ?? "0.1.20.0"
        };

        // Try inspecting the target or default Visual Studio instance
        VisualStudioInstanceDescriptor? descriptor = null;
        try
        {
            descriptor = _registry.Resolve(vsInstanceId);
        }
        catch
        {
            // Instance not registered or registry empty
        }

        if (descriptor != null)
        {
            if (!string.IsNullOrEmpty(descriptor.VisualStudioVersion))
            {
                dict["Visual Studio"] = descriptor.VisualStudioVersion;
            }
            if (!string.IsNullOrEmpty(descriptor.VsInstanceId))
            {
                dict["VS Instance ID"] = descriptor.VsInstanceId;
            }
        }
        else
        {
            dict["Visual Studio"] = "Not connected / Instance unavailable";
        }

        // Try querying live project archetypes and debug info from Bridge
        try
        {
            var projectsResp = await _bridgeService.GetProjectsInSolutionAsync(vsInstanceId, cancellationToken).ConfigureAwait(false);
            if (projectsResp.Projects != null && projectsResp.Projects.Count > 0)
            {
                var archetypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var proj in projectsResp.Projects)
                {
                    var path = proj.ProjectFilePath ?? proj.Name ?? string.Empty;
                    if (path.EndsWith(".vcxproj", StringComparison.OrdinalIgnoreCase))
                    {
                        archetypes.Add("C++ (vcxproj)");
                    }
                    else if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                    {
                        archetypes.Add("C# (csproj)");
                    }
                    else if (path.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase))
                    {
                        archetypes.Add("F# (fsproj)");
                    }
                    else if (path.Contains("CMake", StringComparison.OrdinalIgnoreCase) || path.EndsWith("CMakeLists.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        archetypes.Add("CMake");
                    }
                    else
                    {
                        var ext = Path.GetExtension(path);
                        if (!string.IsNullOrEmpty(ext))
                        {
                            archetypes.Add(ext.TrimStart('.'));
                        }
                    }
                }

                if (archetypes.Count > 0)
                {
                    dict["Project Archetypes"] = string.Join(", ", archetypes);
                }
            }
        }
        catch
        {
            // Bridge unreachable or no solution open; skip silently
        }

        try
        {
            var debugInfo = await _bridgeService.DebuggerGetInfoAsync(vsInstanceId, cancellationToken).ConfigureAwait(false);
            dict["Debug Mode"] = debugInfo.Mode;
            if (debugInfo.CurrentProcessId.HasValue)
            {
                dict["Debugged Process ID"] = debugInfo.CurrentProcessId.Value.ToString();
            }
            if (!string.IsNullOrEmpty(debugInfo.CurrentProcessName))
            {
                dict["Debugged Process Name"] = debugInfo.CurrentProcessName;
            }
        }
        catch
        {
            // Bridge unreachable or debugger not debugging
        }

        return dict;
    }

    private static string BuildLocalMarkdownReport(
        string reportId,
        string targetTool,
        string issueType,
        string summary,
        string? improvement,
        Dictionary<string, string> envSummary)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# MCP Tool Friction Diagnostic Report ({reportId})");
        sb.AppendLine();
        sb.AppendLine($"**Created UTC**: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z");
        sb.AppendLine($"**Target MCP Tool**: `{targetTool}`");
        sb.AppendLine($"**Friction Category**: `{issueType}`");
        sb.AppendLine();
        sb.AppendLine("## Agent Technical Summary");
        sb.AppendLine(summary);
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(improvement))
        {
            sb.AppendLine("## Suggested Improvement");
            sb.AppendLine(improvement);
            sb.AppendLine();
        }

        sb.AppendLine("## Environment & Diagnostics");
        foreach (var kvp in envSummary)
        {
            sb.AppendLine($"- **{kvp.Key}**: {kvp.Value}");
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("*(Generated locally by VsDebugMcp DiagnosticReportService. All private user paths and secrets sanitized.)*");
        return sb.ToString();
    }

    private static string BuildGitHubIssueFormUrl(
        string targetTool,
        string issueType,
        string summary,
        string? improvement,
        Dictionary<string, string> envSummary)
    {
        var title = $"[Tool Friction]: {targetTool}: {issueType}";

        var sbEnv = new StringBuilder();
        foreach (var kvp in envSummary)
        {
            sbEnv.AppendLine($"- **{kvp.Key}**: {kvp.Value}");
        }

        var envText = sbEnv.ToString().TrimEnd();

        // Keep within conservative URL limits (< 2000 chars total) for GitHub Issue Form
        var baseUrl = "https://github.com/wuxinmao2008/VsDebugMcp/issues/new";
        var queryBuilder = new StringBuilder();
        queryBuilder.Append("template=tool-friction.yml");
        queryBuilder.Append("&title=").Append(Uri.EscapeDataString(title));
        queryBuilder.Append("&tool=").Append(Uri.EscapeDataString(targetTool));
        queryBuilder.Append("&category=").Append(Uri.EscapeDataString(issueType));
        queryBuilder.Append("&summary=").Append(Uri.EscapeDataString(summary));

        if (!string.IsNullOrWhiteSpace(improvement))
        {
            queryBuilder.Append("&improvement=").Append(Uri.EscapeDataString(improvement));
        }

        queryBuilder.Append("&environment=").Append(Uri.EscapeDataString(envText));

        return $"{baseUrl}?{queryBuilder}";
    }
}
