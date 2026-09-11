using System.Reflection;
using ModelContextProtocol.Server;
using VsDebugMcp.Host;
using VsDebugMcp.Protocol;
using Xunit;

namespace VsDebugMcp.Host.Tests;

public sealed class DiagnosticSnapshotEnhancementTests
{
    [Fact]
    public void McpTools_RunBuild_HasWaitForCompletionParameter()
    {
        var method = typeof(McpTools).GetMethod(nameof(McpTools.RunBuildAsync));
        Assert.NotNull(method);

        var waitParam = method.GetParameters().SingleOrDefault(p => p.Name == "waitForCompletion");
        Assert.NotNull(waitParam);
        Assert.True(waitParam.HasDefaultValue);
        Assert.Null(waitParam.DefaultValue);

        var timeoutParam = method.GetParameters().SingleOrDefault(p => p.Name == "timeoutSeconds");
        Assert.NotNull(timeoutParam);
        Assert.True(timeoutParam.HasDefaultValue);
        Assert.Null(timeoutParam.DefaultValue);
    }

    [Fact]
    public void McpTools_RunTests_HasWaitForCompletionParameter()
    {
        var method = typeof(McpTools).GetMethod(nameof(McpTools.RunTestsAsync));
        Assert.NotNull(method);

        var waitParam = method.GetParameters().SingleOrDefault(p => p.Name == "waitForCompletion");
        Assert.NotNull(waitParam);
        Assert.True(waitParam.HasDefaultValue);
        Assert.Null(waitParam.DefaultValue);

        var timeoutParam = method.GetParameters().SingleOrDefault(p => p.Name == "timeoutSeconds");
        Assert.NotNull(timeoutParam);
        Assert.True(timeoutParam.HasDefaultValue);
        Assert.Null(timeoutParam.DefaultValue);
    }

    [Fact]
    public void McpTools_DebuggerGetSnapshot_HasExceptionAndThreadParameters()
    {
        var method = typeof(McpTools).GetMethod(nameof(McpTools.DebuggerGetSnapshotAsync));
        Assert.NotNull(method);

        var excParam = method.GetParameters().SingleOrDefault(p => p.Name == "includeExceptionInfo");
        Assert.NotNull(excParam);
        Assert.True(excParam.HasDefaultValue);

        var threadParam = method.GetParameters().SingleOrDefault(p => p.Name == "includeThreads");
        Assert.NotNull(threadParam);
        Assert.True(threadParam.HasDefaultValue);

        var maxThreadParam = method.GetParameters().SingleOrDefault(p => p.Name == "maxThreads");
        Assert.NotNull(maxThreadParam);
        Assert.True(maxThreadParam.HasDefaultValue);
    }

    [Fact]
    public void AssertionLogParser_CrtAssertion_SetsAllStructuredFields()
    {
        var rawLog = "Loaded symbols.\nAssertion failed: size > 0, file C:\\Sources\\App\\Vector.h, line 256\nThread stopped.";
        var response = new DebuggerGetExceptionInfoResponse();

        AssertionLogParser.ScanLogsForAssertionOrException(rawLog, response);

        Assert.True(response.HasException);
        Assert.True(response.AssertionFailed);
        Assert.Equal("size > 0", response.AssertionExpression);
        Assert.Equal(@"C:\Sources\App\Vector.h", response.AssertionFile);
        Assert.Equal(256, response.AssertionLine);
        Assert.Equal("AssertionFailure", response.ExceptionType);
        Assert.Equal("Assertion failed: size > 0", response.Message);
        Assert.Equal(@"C:\Sources\App\Vector.h", response.Source);
    }
}
