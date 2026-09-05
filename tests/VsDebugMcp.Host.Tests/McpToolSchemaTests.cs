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
    public void OptionalToolParametersHaveDefaultValues(string methodName, string parameterName)
    {
        var method = typeof(McpTools).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        var parameter = method?.GetParameters().Single(item => item.Name == parameterName);

        Assert.NotNull(parameter);
        Assert.True(parameter.HasDefaultValue);
        Assert.Null(parameter.DefaultValue);
    }
}