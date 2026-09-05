using System.Reflection;
using Xunit;

namespace VsDebugMcp.Host.Tests;

public sealed class McpToolSchemaTests
{
    [Theory]
    [InlineData(nameof(McpTools.RunBuildAsync), "configuration")]
    [InlineData(nameof(McpTools.RunBuildAsync), "platform")]
    [InlineData(nameof(McpTools.RunBuildAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.GetBuildStatusAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.CancelBuildAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.GetFilesInProjectAsync), "projectId")]
    [InlineData(nameof(McpTools.GetFilesInProjectAsync), "extensionFilter")]
    [InlineData(nameof(McpTools.GetFilesInProjectAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.DebuggerGetInfoAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.DebuggerSetBreakpointsAsync), "column")]
    [InlineData(nameof(McpTools.DebuggerSetBreakpointsAsync), "condition")]
    [InlineData(nameof(McpTools.DebuggerSetBreakpointsAsync), "enabled")]
    [InlineData(nameof(McpTools.DebuggerSetBreakpointsAsync), "clearExisting")]
    [InlineData(nameof(McpTools.DebuggerSetBreakpointsAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.DebuggerGetCallStackAsync), "threadId")]
    [InlineData(nameof(McpTools.DebuggerGetCallStackAsync), "maxFrames")]
    [InlineData(nameof(McpTools.DebuggerGetCallStackAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.DebuggerEvaluateExprAsync), "frameIndex")]
    [InlineData(nameof(McpTools.DebuggerEvaluateExprAsync), "timeoutMs")]
    [InlineData(nameof(McpTools.DebuggerEvaluateExprAsync), "allowSideEffects")]
    [InlineData(nameof(McpTools.DebuggerEvaluateExprAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.DebuggerStepOverAsync), "waitForBreak")]
    [InlineData(nameof(McpTools.DebuggerStepOverAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.DebuggerStepIntoAsync), "waitForBreak")]
    [InlineData(nameof(McpTools.DebuggerStepIntoAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.DebuggerStepOutAsync), "waitForBreak")]
    [InlineData(nameof(McpTools.DebuggerStepOutAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.DebuggerContinueAsync), "waitForBreak")]
    [InlineData(nameof(McpTools.DebuggerContinueAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.DebuggerPauseAsync), "waitForBreak")]
    [InlineData(nameof(McpTools.DebuggerPauseAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.DebuggerStopAsync), "waitForStop")]
    [InlineData(nameof(McpTools.DebuggerStopAsync), "vsInstanceId")]
    public void OptionalToolParametersHaveDefaultValues(string methodName, string parameterName)
    {
        var method = typeof(McpTools).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        var parameter = method?.GetParameters().Single(item => item.Name == parameterName);

        Assert.NotNull(parameter);
        Assert.True(parameter.HasDefaultValue);
        Assert.Null(parameter.DefaultValue);
    }
}