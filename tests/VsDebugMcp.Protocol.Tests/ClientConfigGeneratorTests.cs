using System.Linq;
using VsDebugMcp.Protocol;
using Xunit;

namespace VsDebugMcp.Protocol.Tests;

public sealed class ClientConfigGeneratorTests
{
    [Fact]
    public void Generate_Default_ReturnsConfigsForAllClients()
    {
        var configs = ClientConfigGenerator.Generate();

        Assert.NotNull(configs);
        Assert.NotEmpty(configs);

        // 5 clients * 2 scopes = 10 items
        Assert.Equal(10, configs.Count);

        var clientIds = configs.Select(c => c.ClientId).Distinct().ToList();
        Assert.Contains(ClientConfigGenerator.ClientVsCode, clientIds);
        Assert.Contains(ClientConfigGenerator.ClientCursor, clientIds);
        Assert.Contains(ClientConfigGenerator.ClientClaude, clientIds);
        Assert.Contains(ClientConfigGenerator.ClientAntigravity, clientIds);
        Assert.Contains(ClientConfigGenerator.ClientCodex, clientIds);
    }

    [Fact]
    public void Generate_FilterByClient_ReturnsOnlyTargetClient()
    {
        var configs = ClientConfigGenerator.Generate(clientFilter: "cursor");

        Assert.Equal(2, configs.Count);
        Assert.All(configs, c => Assert.Equal("cursor", c.ClientId));

        var globalConfig = configs.Single(c => c.Scope == ClientConfigGenerator.ScopeGlobal);
        var localConfig = configs.Single(c => c.Scope == ClientConfigGenerator.ScopeLocal);

        Assert.Contains(".cursor", globalConfig.RecommendedPath);
        Assert.Equal(".cursor/mcp.json", localConfig.RecommendedPath);
        Assert.True(globalConfig.IsSupported);
        Assert.True(localConfig.IsSupported);
    }

    [Fact]
    public void Generate_ClaudeLocal_IsNotSupported()
    {
        var configs = ClientConfigGenerator.Generate(clientFilter: "claude", scopeFilter: "local");

        var claudeLocal = Assert.Single(configs);
        Assert.False(claudeLocal.IsSupported);
        Assert.Contains("暂不支持", claudeLocal.SampleJson);
    }

    [Fact]
    public void Generate_CustomPort_ReflectsInSampleJson()
    {
        const int customPort = 48888;
        var configs = ClientConfigGenerator.Generate(clientFilter: "vscode", scopeFilter: "global", port: customPort);

        var config = Assert.Single(configs);
        Assert.Contains("http://127.0.0.1:48888", config.SampleJson);
    }
}
