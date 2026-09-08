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
    [InlineData(nameof(McpTools.DebuggerSetBreakpointsAsync), "conditionType")]
    [InlineData(nameof(McpTools.DebuggerSetBreakpointsAsync), "hitCountTarget")]
    [InlineData(nameof(McpTools.DebuggerSetBreakpointsAsync), "hitCountType")]
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
    [InlineData(nameof(McpTools.DebuggerStartAsync), "waitForBreak")]
    [InlineData(nameof(McpTools.DebuggerStartAsync), "timeoutMs")]
    [InlineData(nameof(McpTools.DebuggerStartAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.DebuggerEvaluateExpressionsAsync), "frameIndex")]
    [InlineData(nameof(McpTools.DebuggerEvaluateExpressionsAsync), "timeoutMs")]
    [InlineData(nameof(McpTools.DebuggerEvaluateExpressionsAsync), "allowSideEffects")]
    [InlineData(nameof(McpTools.DebuggerEvaluateExpressionsAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.DebuggerGetLocalsAsync), "frameIndex")]
    [InlineData(nameof(McpTools.DebuggerGetLocalsAsync), "maxCount")]
    [InlineData(nameof(McpTools.DebuggerGetLocalsAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.GetTestsAsync), "projectName")]
    [InlineData(nameof(McpTools.GetTestsAsync), "filter")]
    [InlineData(nameof(McpTools.GetTestsAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.RunTestsAsync), "testIds")]
    [InlineData(nameof(McpTools.RunTestsAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.GetTestRunStatusAsync), "testRunId")]
    [InlineData(nameof(McpTools.GetTestRunStatusAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.CancelTestRunAsync), "testRunId")]
    [InlineData(nameof(McpTools.CancelTestRunAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.DebugTestByIdAsync), "waitForBreak")]
    [InlineData(nameof(McpTools.DebugTestByIdAsync), "timeoutMs")]
    [InlineData(nameof(McpTools.DebugTestByIdAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.DebuggerGetThreadsAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.DebuggerGetExceptionInfoAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.DebuggerGetProcessesAsync), "processName")]
    [InlineData(nameof(McpTools.DebuggerGetProcessesAsync), "processId")]
    [InlineData(nameof(McpTools.DebuggerGetProcessesAsync), "onlyDebugged")]
    [InlineData(nameof(McpTools.DebuggerGetProcessesAsync), "maxCount")]
    [InlineData(nameof(McpTools.DebuggerGetProcessesAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.DebuggerAttachProcessAsync), "processId")]
    [InlineData(nameof(McpTools.DebuggerAttachProcessAsync), "processName")]
    [InlineData(nameof(McpTools.DebuggerAttachProcessAsync), "engines")]
    [InlineData(nameof(McpTools.DebuggerAttachProcessAsync), "waitForBreak")]
    [InlineData(nameof(McpTools.DebuggerAttachProcessAsync), "breakTimeoutMs")]
    [InlineData(nameof(McpTools.DebuggerAttachProcessAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.DebuggerDetachAsync), "processId")]
    [InlineData(nameof(McpTools.DebuggerDetachAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.DebuggerGetModulesAsync), "processId")]
    [InlineData(nameof(McpTools.DebuggerGetModulesAsync), "nameFilter")]
    [InlineData(nameof(McpTools.DebuggerGetModulesAsync), "userCodeOnly")]
    [InlineData(nameof(McpTools.DebuggerGetModulesAsync), "maxCount")]
    [InlineData(nameof(McpTools.DebuggerGetModulesAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.GetActiveDocumentAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.NavigateToAsync), "line")]
    [InlineData(nameof(McpTools.NavigateToAsync), "column")]
    [InlineData(nameof(McpTools.NavigateToAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.GetSolutionConfigurationsAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.DebuggerListBreakpointsAsync), "filePath")]
    [InlineData(nameof(McpTools.DebuggerListBreakpointsAsync), "enabledOnly")]
    [InlineData(nameof(McpTools.DebuggerListBreakpointsAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.DebuggerClearBreakpointsAsync), "clearAll")]
    [InlineData(nameof(McpTools.DebuggerClearBreakpointsAsync), "filePath")]
    [InlineData(nameof(McpTools.DebuggerClearBreakpointsAsync), "line")]
    [InlineData(nameof(McpTools.DebuggerClearBreakpointsAsync), "breakpointId")]
    [InlineData(nameof(McpTools.DebuggerClearBreakpointsAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.DebuggerToggleBreakpointAsync), "breakpointId")]
    [InlineData(nameof(McpTools.DebuggerToggleBreakpointAsync), "filePath")]
    [InlineData(nameof(McpTools.DebuggerToggleBreakpointAsync), "line")]
    [InlineData(nameof(McpTools.DebuggerToggleBreakpointAsync), "enabled")]
    [InlineData(nameof(McpTools.DebuggerToggleBreakpointAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.SetSolutionConfigurationAsync), "platform")]
    [InlineData(nameof(McpTools.SetSolutionConfigurationAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.GetOutputPanesAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.DebuggerFindSolutionProcessesAsync), "startupOnly")]
    [InlineData(nameof(McpTools.DebuggerFindSolutionProcessesAsync), "vsInstanceId")]
    [InlineData(nameof(McpTools.DebuggerAutoAttachAsync), "startupOnly")]
    [InlineData(nameof(McpTools.DebuggerAutoAttachAsync), "processNames")]
    [InlineData(nameof(McpTools.DebuggerAutoAttachAsync), "engines")]
    [InlineData(nameof(McpTools.DebuggerAutoAttachAsync), "waitForBreak")]
    [InlineData(nameof(McpTools.DebuggerAutoAttachAsync), "breakTimeoutMs")]
    [InlineData(nameof(McpTools.DebuggerAutoAttachAsync), "vsInstanceId")]
    public void OptionalToolParametersHaveDefaultValues(string methodName, string parameterName)
    {
        var method = typeof(McpTools).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        var parameter = method?.GetParameters().Single(item => item.Name == parameterName);

        Assert.NotNull(parameter);
        Assert.True(parameter.HasDefaultValue);
        Assert.Null(parameter.DefaultValue);
    }

    [Theory]
    [InlineData("vs_debugger_get_processes", nameof(McpTools.DebuggerGetProcessesAsync), true)]
    [InlineData("vs_debugger_attach_process", nameof(McpTools.DebuggerAttachProcessAsync), false)]
    [InlineData("vs_debugger_detach", nameof(McpTools.DebuggerDetachAsync), false)]
    [InlineData("vs_debugger_get_modules", nameof(McpTools.DebuggerGetModulesAsync), true)]
    public void AllPhase3CToolsAreRegisteredWithCorrectMetadata(string expectedToolName, string methodName, bool expectedReadOnly)
    {
        var method = typeof(McpTools).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);

        var attr = method.GetCustomAttribute<ModelContextProtocol.Server.McpServerToolAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(expectedToolName, attr.Name);
        Assert.Equal(expectedReadOnly, attr.ReadOnly);
        Assert.True(attr.UseStructuredContent);
    }

    [Theory]
    [InlineData("vs_get_active_document", nameof(McpTools.GetActiveDocumentAsync), true)]
    [InlineData("vs_navigate_to", nameof(McpTools.NavigateToAsync), false)]
    [InlineData("vs_get_solution_configurations", nameof(McpTools.GetSolutionConfigurationsAsync), true)]
    public void AllPhase4AToolsAreRegisteredWithCorrectMetadata(string expectedToolName, string methodName, bool expectedReadOnly)
    {
        var method = typeof(McpTools).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);

        var attr = method.GetCustomAttribute<ModelContextProtocol.Server.McpServerToolAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(expectedToolName, attr.Name);
        Assert.Equal(expectedReadOnly, attr.ReadOnly);
        Assert.True(attr.UseStructuredContent);
    }

    [Theory]
    [InlineData("vs_debugger_list_breakpoints", nameof(McpTools.DebuggerListBreakpointsAsync), true)]
    [InlineData("vs_debugger_clear_breakpoints", nameof(McpTools.DebuggerClearBreakpointsAsync), false)]
    [InlineData("vs_debugger_toggle_breakpoint", nameof(McpTools.DebuggerToggleBreakpointAsync), false)]
    public void AllPhase5AToolsAreRegisteredWithCorrectMetadata(string expectedToolName, string methodName, bool expectedReadOnly)
    {
        var method = typeof(McpTools).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);

        var attr = method.GetCustomAttribute<ModelContextProtocol.Server.McpServerToolAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(expectedToolName, attr.Name);
        Assert.Equal(expectedReadOnly, attr.ReadOnly);
        Assert.True(attr.UseStructuredContent);
    }

    [Theory]
    [InlineData("vs_set_solution_configuration", nameof(McpTools.SetSolutionConfigurationAsync), false)]
    [InlineData("vs_get_output_panes", nameof(McpTools.GetOutputPanesAsync), true)]
    public void AllPhase5BToolsAreRegisteredWithCorrectMetadata(string expectedToolName, string methodName, bool expectedReadOnly)
    {
        var method = typeof(McpTools).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);

        var attr = method.GetCustomAttribute<ModelContextProtocol.Server.McpServerToolAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(expectedToolName, attr.Name);
        Assert.Equal(expectedReadOnly, attr.ReadOnly);
        Assert.True(attr.UseStructuredContent);
    }

    [Theory]
    [InlineData("vs_debugger_find_solution_processes", nameof(McpTools.DebuggerFindSolutionProcessesAsync), true)]
    [InlineData("vs_debugger_auto_attach", nameof(McpTools.DebuggerAutoAttachAsync), false)]
    public void AllPhase5CToolsAreRegisteredWithCorrectMetadata(string expectedToolName, string methodName, bool expectedReadOnly)
    {
        var method = typeof(McpTools).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);

        var attr = method.GetCustomAttribute<ModelContextProtocol.Server.McpServerToolAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(expectedToolName, attr.Name);
        Assert.Equal(expectedReadOnly, attr.ReadOnly);
        Assert.True(attr.UseStructuredContent);
    }

    [Fact]
    public void Exactly48ToolsAreRegisteredOnMcpTools()
    {
        var toolMethods = typeof(McpTools)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.GetCustomAttribute<ModelContextProtocol.Server.McpServerToolAttribute>() != null)
            .ToList();

        Assert.Equal(48, toolMethods.Count);
    }
}