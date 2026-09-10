using System.IO;
using VsDebugMcp.Protocol;
using Xunit;

namespace VsDebugMcp.Host.Tests;

public sealed class DiagnosticReportServiceTests : IDisposable
{
    private readonly string _tempReportsDir;
    private readonly VisualStudioInstanceRegistry _registry;
    private readonly BridgeService _bridgeService;

    public DiagnosticReportServiceTests()
    {
        _tempReportsDir = Path.Combine(Path.GetTempPath(), "VsDebugMcp_Tests_" + Guid.NewGuid().ToString("N"));
        var options = new VsHostOptions();
        _registry = new VisualStudioInstanceRegistry(options, () => { });
        _bridgeService = new BridgeService(options, _registry);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempReportsDir))
            {
                Directory.Delete(_tempReportsDir, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public async Task ReportMcpIssue_ValidInput_GeneratesShortUrlAndLocalDump()
    {
        var service = new DiagnosticReportService(_bridgeService, _registry, _tempReportsDir);

        var response = await service.ReportMcpIssueAsync(
            targetTool: "vs_debugger_evaluate_expr",
            issueType: "transport_timeout",
            agentSummary: "Named pipe timed out while evaluating lazy collection.",
            suggestedImprovement: "Implement progressive timeout.",
            vsInstanceId: null);

        Assert.Equal("ready_for_user_submission", response.Status);
        Assert.StartsWith("rpt_", response.ReportId);
        Assert.NotNull(response.GithubIssueUrl);
        Assert.Contains("template=tool-friction.yml", response.GithubIssueUrl);
        Assert.Contains("vs_debugger_evaluate_expr", response.GithubIssueUrl);
        Assert.Contains("transport_timeout", response.GithubIssueUrl);
        Assert.StartsWith("%LOCALAPPDATA%", response.LocalReportPath);

        // Verify local report was written to disk
        var files = Directory.GetFiles(_tempReportsDir, "*.md");
        Assert.Single(files);
        var content = await File.ReadAllTextAsync(files[0]);
        Assert.Contains("vs_debugger_evaluate_expr", content);
        Assert.Contains("transport_timeout", content);
        Assert.Contains("Named pipe timed out", content);
        Assert.Contains("Implement progressive timeout", content);
    }

    [Theory]
    [InlineData("My secret key is -----BEGIN RSA PRIVATE KEY----- abc12345 -----END RSA PRIVATE KEY-----")]
    [InlineData("Using token ghp_123456789012345678901234567890123456 in header")]
    [InlineData("Found JWT token eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.doNotLeakThis")]
    [InlineData("password = 'MySecretPassword123'")]
    [InlineData(@"Crash occurred at C:\Users\SecretUser\Project\Test.cs")]
    public async Task ReportMcpIssue_PrivacyRiskDetected_AbortsAndReturnsPrivacyRiskAborted(string highRiskText)
    {
        var service = new DiagnosticReportService(_bridgeService, _registry, _tempReportsDir);

        var response = await service.ReportMcpIssueAsync(
            targetTool: "vs_debugger_get_info",
            issueType: "crash_or_exception",
            agentSummary: highRiskText);

        Assert.Equal("privacy_risk_aborted", response.Status);
        Assert.Null(response.GithubIssueUrl);
        Assert.Null(response.LocalReportPath);
        Assert.Contains("privacy circuit breaker", response.InstructionsForAgent, StringComparison.OrdinalIgnoreCase);
        Assert.Single(response.Warnings);
        Assert.Equal(BridgeErrorCodes.PrivacyRiskAborted, response.Warnings[0].Code);
    }

    [Fact]
    public async Task ReportMcpIssue_OverLengthInput_TruncatesTo512()
    {
        var service = new DiagnosticReportService(_bridgeService, _registry, _tempReportsDir);

        var longSummary = new string('A', 800);
        var longImprovement = new string('B', 600);

        var response = await service.ReportMcpIssueAsync(
            targetTool: "vs_run_build",
            issueType: "output_too_large",
            agentSummary: longSummary,
            suggestedImprovement: longImprovement);

        Assert.Equal("ready_for_user_submission", response.Status);
        Assert.NotNull(response.GithubIssueUrl);

        // Check local report content has truncated text
        var files = Directory.GetFiles(_tempReportsDir, "*.md");
        Assert.Single(files);
        var content = await File.ReadAllTextAsync(files[0]);
        Assert.DoesNotContain(longSummary, content);
        Assert.Contains(new string('A', 512), content);
    }

    [Fact]
    public async Task ReportMcpIssue_UnknownIssueType_NormalizesToUnknown()
    {
        var service = new DiagnosticReportService(_bridgeService, _registry, _tempReportsDir);

        var response = await service.ReportMcpIssueAsync(
            targetTool: "vs_get_errors",
            issueType: "unrecognized_custom_type",
            agentSummary: "Something weird happened.");

        Assert.Equal("ready_for_user_submission", response.Status);
        Assert.Contains("category=unknown", response.GithubIssueUrl);
    }
}
