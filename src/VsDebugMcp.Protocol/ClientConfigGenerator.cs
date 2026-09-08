using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;

namespace VsDebugMcp.Protocol;

[DataContract]
public sealed class ClientConfigItem
{
    [DataMember(Name = "clientId", Order = 1)]
    public string ClientId { get; set; } = string.Empty;

    [DataMember(Name = "displayName", Order = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [DataMember(Name = "scope", Order = 3)]
    public string Scope { get; set; } = string.Empty;

    [DataMember(Name = "scopeDisplayName", Order = 4)]
    public string ScopeDisplayName { get; set; } = string.Empty;

    [DataMember(Name = "recommendedPath", Order = 5)]
    public string RecommendedPath { get; set; } = string.Empty;

    [DataMember(Name = "sampleJson", Order = 6)]
    public string SampleJson { get; set; } = string.Empty;

    [DataMember(Name = "notes", Order = 7)]
    public string Notes { get; set; } = string.Empty;

    [DataMember(Name = "isSupported", Order = 8)]
    public bool IsSupported { get; set; } = true;
}

public static class ClientConfigGenerator
{
    public const int DefaultPort = 43260;

    public const string ScopeGlobal = "global";
    public const string ScopeLocal = "local";

    public const string ClientVsCode = "vscode";
    public const string ClientCursor = "cursor";
    public const string ClientClaude = "claude";
    public const string ClientAntigravity = "antigravity";
    public const string ClientCodex = "codex";

    public static readonly string[] SupportedClients = new[]
    {
        ClientVsCode,
        ClientCursor,
        ClientClaude,
        ClientAntigravity,
        ClientCodex
    };

    public static string GetServiceEndpoint(int port = DefaultPort) => $"http://127.0.0.1:{port}";

    public static string BuildSampleJson(int port = DefaultPort)
    {
        var endpoint = GetServiceEndpoint(port);
        return
$@"{{
  ""mcpServers"": {{
    ""vs-debug-mcp"": {{
      ""url"": ""{endpoint}""
    }}
  }}
}}";
    }

    public static string BuildInnerSampleJson(int port = DefaultPort)
    {
        var endpoint = GetServiceEndpoint(port);
        return
$@"    ""vs-debug-mcp"": {{
      ""url"": ""{endpoint}""
    }}";
    }

    public static List<ClientConfigItem> Generate(string? clientFilter = null, string? scopeFilter = null, int port = DefaultPort)
    {
        var result = new List<ClientConfigItem>();
        var sampleJson = BuildSampleJson(port);

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var requestedClient = clientFilter?.Trim().ToLowerInvariant();
        var requestedScope = scopeFilter?.Trim().ToLowerInvariant();

        bool MatchClient(string id)
        {
            if (string.IsNullOrEmpty(requestedClient) || requestedClient == "all")
                return true;

            if (id == requestedClient)
                return true;

            if (requestedClient == "claude_desktop" && id == ClientClaude)
                return true;

            if (requestedClient == "windsurf" && id == ClientCodex)
                return true;

            return false;
        }

        bool MatchScope(string scope)
        {
            if (string.IsNullOrEmpty(requestedScope) || requestedScope == "both" || requestedScope == "all")
                return true;

            return scope == requestedScope;
        }

        // 1. VS Code
        if (MatchClient(ClientVsCode))
        {
            if (MatchScope(ScopeGlobal))
            {
                result.Add(new ClientConfigItem
                {
                    ClientId = ClientVsCode,
                    DisplayName = "VS Code",
                    Scope = ScopeGlobal,
                    ScopeDisplayName = "全局配置 (Global / User)",
                    RecommendedPath = Path.Combine(appData, @"Code\User\globalStorage\rooveterinaryinc.roo-cline\settings\cline_mcp_settings.json"),
                    SampleJson = sampleJson,
                    Notes = "适用于 VS Code MCP 插件全局配置（如 Roo-Code、Cline 等）。若已有 mcpServers 节点，仅需将内部 vs-debug-mcp 块复制合并进去。",
                    IsSupported = true
                });
            }

            if (MatchScope(ScopeLocal))
            {
                result.Add(new ClientConfigItem
                {
                    ClientId = ClientVsCode,
                    DisplayName = "VS Code",
                    Scope = ScopeLocal,
                    ScopeDisplayName = "本地/项目配置 (Local / Workspace)",
                    RecommendedPath = ".vscode/mcp.json",
                    SampleJson = sampleJson,
                    Notes = "在您的代码仓库或工作区根目录下创建该文件，仅对当前项目生效，便于随 Git 提交与团队共享。",
                    IsSupported = true
                });
            }
        }

        // 2. Cursor
        if (MatchClient(ClientCursor))
        {
            if (MatchScope(ScopeGlobal))
            {
                result.Add(new ClientConfigItem
                {
                    ClientId = ClientCursor,
                    DisplayName = "Cursor",
                    Scope = ScopeGlobal,
                    ScopeDisplayName = "全局配置 (Global / User)",
                    RecommendedPath = Path.Combine(userProfile, @".cursor\mcp.json"),
                    SampleJson = sampleJson,
                    Notes = "全局生效。保存在当前用户目录下的 .cursor 文件夹内，所有在 Cursor 中打开的窗口均可使用。",
                    IsSupported = true
                });
            }

            if (MatchScope(ScopeLocal))
            {
                result.Add(new ClientConfigItem
                {
                    ClientId = ClientCursor,
                    DisplayName = "Cursor",
                    Scope = ScopeLocal,
                    ScopeDisplayName = "本地/项目配置 (Local / Workspace)",
                    RecommendedPath = ".cursor/mcp.json",
                    SampleJson = sampleJson,
                    Notes = "在当前工作区根目录下创建 .cursor/mcp.json 文件，仅对当前项目生效。",
                    IsSupported = true
                });
            }
        }

        // 3. Claude Desktop
        if (MatchClient(ClientClaude))
        {
            if (MatchScope(ScopeGlobal))
            {
                result.Add(new ClientConfigItem
                {
                    ClientId = ClientClaude,
                    DisplayName = "Claude Desktop",
                    Scope = ScopeGlobal,
                    ScopeDisplayName = "全局配置 (Global / User)",
                    RecommendedPath = Path.Combine(appData, @"Claude\claude_desktop_config.json"),
                    SampleJson = sampleJson,
                    Notes = "Claude Desktop 官方配置文件。修改保存后，需要完全退出并重新启动 Claude 客户端以生效。",
                    IsSupported = true
                });
            }

            if (MatchScope(ScopeLocal))
            {
                result.Add(new ClientConfigItem
                {
                    ClientId = ClientClaude,
                    DisplayName = "Claude Desktop",
                    Scope = ScopeLocal,
                    ScopeDisplayName = "本地/项目配置 (Local / Workspace)",
                    RecommendedPath = "N/A",
                    SampleJson = "// Claude Desktop 官方目前仅支持全局用户配置，暂不支持本地工作区配置。",
                    Notes = "Claude Desktop 客户端目前仅支持全局用户级配置文件，请切换至【全局配置】选项卡使用。",
                    IsSupported = false
                });
            }
        }

        // 4. Antigravity
        if (MatchClient(ClientAntigravity))
        {
            if (MatchScope(ScopeGlobal))
            {
                result.Add(new ClientConfigItem
                {
                    ClientId = ClientAntigravity,
                    DisplayName = "Antigravity",
                    Scope = ScopeGlobal,
                    ScopeDisplayName = "全局配置 (Global / User)",
                    RecommendedPath = Path.Combine(userProfile, @".gemini\antigravity\mcp_config.json"),
                    SampleJson = sampleJson,
                    Notes = "Antigravity 全局配置。对当前用户下的所有会话生效。",
                    IsSupported = true
                });
            }

            if (MatchScope(ScopeLocal))
            {
                result.Add(new ClientConfigItem
                {
                    ClientId = ClientAntigravity,
                    DisplayName = "Antigravity",
                    Scope = ScopeLocal,
                    ScopeDisplayName = "本地/项目配置 (Local / Workspace)",
                    RecommendedPath = ".gemini/antigravity/mcp_config.json",
                    SampleJson = sampleJson,
                    Notes = "在当前工作区根目录下创建该文件，仅对本工作区独立生效。",
                    IsSupported = true
                });
            }
        }

        // 5. Codex / Windsurf
        if (MatchClient(ClientCodex))
        {
            if (MatchScope(ScopeGlobal))
            {
                result.Add(new ClientConfigItem
                {
                    ClientId = ClientCodex,
                    DisplayName = "Codex / Windsurf",
                    Scope = ScopeGlobal,
                    ScopeDisplayName = "全局配置 (Global / User)",
                    RecommendedPath = Path.Combine(userProfile, @".codeium\windsurf\mcp_config.json"),
                    SampleJson = sampleJson,
                    Notes = "Codex / Windsurf 全局配置文件。",
                    IsSupported = true
                });
            }

            if (MatchScope(ScopeLocal))
            {
                result.Add(new ClientConfigItem
                {
                    ClientId = ClientCodex,
                    DisplayName = "Codex / Windsurf",
                    Scope = ScopeLocal,
                    ScopeDisplayName = "本地/项目配置 (Local / Workspace)",
                    RecommendedPath = ".codeium/windsurf/mcp_config.json",
                    SampleJson = sampleJson,
                    Notes = "在当前工作区根目录下创建该文件，仅对当前项目生效。",
                    IsSupported = true
                });
            }
        }

        return result;
    }
}
