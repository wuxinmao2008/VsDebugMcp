# VS GitHub Copilot 日志分析

## 日志目录

扫描目录：`C:\Users\wuxin\AppData\Local\Temp\VSGitHubCopilotLogs`

包含的主要文件：

- `20260812_070442.083_VSGitHubCopilot.chat.log`
- `20260812_083833.553_VSGitHubCopilot.chat.log`
- `20260813_003819.338_VSGitHubCopilot.chat.log`
- `20260813_054835.395_VSGitHubCopilot.chat.log`
- `20260813_072145.947_VSGitHubCopilot.chat.log`
- `20260813_083929.849_VSGitHubCopilot.chat.log`
- `20260813_084011.003_VSGitHubCopilot.chat.log`
- `traces/4bf1a5ba_VSGitHubCopilot_traces.jsonl`
- `traces/a227a8e0_VSGitHubCopilot_traces.jsonl`

> 日志可能包含 session id、quota、路径、response id 等隐私信息；本文件只保存脱敏后的结构化结论。

## 版本与运行架构

日志多次出现：

- Visual Studio：`VisualStudio.18.Release/18.9.0+12105.275`
- Copilot Chat：`18.9.1302+bef5266ec0` / `18.9.1302.48885`
- `devenv` 侧运行时：`.NET Framework 4.8.9337.0`
- OOP/DevHub 侧运行时：`.NET 10.0.11`
- OOP 服务进程名：`DevHub`
- ServiceHub 相关目录：`ServiceHub.Host.Extensibility.amd64\net10.0-windows10.0.19041.0`

推断：VS 2026 / VS 18.x 的 Copilot 架构已经是明显的混合模式：

- `devenv` 进程内部分负责 VS 传统集成。
- `DevHub` / ServiceHub out-of-process 进程负责 Copilot 服务、模型调用、MCP 聚合等。
- 这与本项目推荐的 Hybrid 方向一致。

## Copilot Interaction / Context Provider

日志中注册了大量 `CopilotInteractionManager` provider：

- `Microsoft.VisualStudio.Copilot.McpResourceContextProvider`
- `Microsoft.VisualStudio.Copilot.McpResourceContextTypeParser`
- `Microsoft.VisualStudio.Copilot.OutputContextProvider`
- `Microsoft.VisualStudio.Copilot.OutputContextTypeParser`
- `Microsoft.VisualStudio.Copilot.DocumentContextProvider`
- `Microsoft.VisualStudio.Copilot.ImageContextProvider`
- `Microsoft.VisualStudio.Copilot.DebuggerContextProvider`
- `Microsoft.VisualStudio.Copilot.Debugging.IssueContextProvider`
- `Microsoft.VisualStudio.Copilot.SymbolContextProvider`
- `Microsoft.VisualStudio.Copilot.SymbolContextTypesHandler`
- `Microsoft.VisualStudio.Copilot.DiagnosticContextProvider`
- `Microsoft.Copilot.RemoteAgentReferenceContextProvider`
- `Microsoft.VisualStudio.Copilot.ErrorContextProvider`
- `Microsoft.VisualStudio.Copilot.CopilotWorkspaceContextProvider`
- `Microsoft.VisualStudio.Copilot.WebPageContextProvider`
- `Microsoft.VisualStudio.Copilot.DiagnosticsScope`
- `Microsoft.VisualStudio.Copilot.SemanticContextScope`
- `Microsoft.VisualStudio.Copilot.WorkspaceTraitsScope`
- `Microsoft.VisualStudio.Copilot.CSharpProjectTraitProvider`
- `Microsoft.VisualStudio.Copilot.CppTraitProvider`
- `Microsoft.VisualStudio.Copilot.WorkerServiceTraitProvider`
- `Microsoft.VisualStudio.Copilot.MauiTraitProvider`
- `Microsoft.VisualStudio.Copilot.WpfTraitProvider`
- `Microsoft.VisualStudio.Copilot.WorkspaceSummaryTraitProvider`
- `Microsoft.VisualStudio.Copilot.BlazorTraitProvider`
- `Microsoft.VisualStudio.Copilot.RazorPagesTraitProvider`
- `Microsoft.VisualStudio.Copilot.TypeScriptTraitProvider`
- `Microsoft.VisualStudio.Copilot.CSharpTypeSignatureContextProvider`

设计启发：VS Copilot 内部不是只靠一组函数，而是拆为：

- Context Provider
- Context Type Parser
- Trait Provider
- Function Provider
- Agent Responder

本项目也应避免把全部能力堆在单个 MCP server 类里，而应设计 provider 分层。

## Copilot Function Provider

### EditsFunctions

日志显示：

`Microsoft.VisualStudio.Copilot.EditsFunctions Version=0.1` 注册 10 个函数：

- `get_errors`
- `file_search`
- `get_files_in_project`
- `get_projects_in_solution`
- `run_build`
- `remove_file`
- `create_file`
- `run_command_in_terminal`
- `edit_files`
- `edit_file`

对应本项目能力：

| VS Copilot 函数 | 本项目规划能力 |
|---|---|
| `get_errors` | 获取 Error List / diagnostics |
| `file_search` | 文件搜索 |
| `get_files_in_project` | 项目文件列表 |
| `get_projects_in_solution` | 解决方案项目列表 |
| `run_build` | 构建解决方案/项目 |
| `remove_file` | 删除文件，需高风险确认 |
| `create_file` | 创建文件 |
| `run_command_in_terminal` | 终端命令执行，需沙箱/确认策略 |
| `edit_files` | 批量编辑 |
| `edit_file` | 单文件编辑 |

### OutputFunctions

`Microsoft.VisualStudio.Copilot.OutputFunctions Version=0.1` 注册 1 个函数：

- `get_output_window_logs`

对应本项目：

- `getOutputWindowText`
- `vs_get_output_window_logs`
- `vs_get_build_output`

### ContextFunctions

`Microsoft.VisualStudio.Copilot.ContextFunctions Version=0.1` 注册 1 个函数：

- `read_file`

对应本项目：

- `readFile`
- `vs_read_file`

### TestExplorerFunctions

`Microsoft.VisualStudio.Copilot.TestExplorerFunctions Version=0.1` 注册 2 个函数：

- `get_tests`
- `run_tests`

对应本项目：

- `getTests`
- `runTests`
- 后续可扩展 `debugTestById`。

### Planning / AskQuestion

- `Microsoft.VisualStudio.Copilot.PlanningFunctions Version=0.1`
  - `plan`
- `Microsoft.VisualStudio.Copilot.AskQuestionFunctions Version=0.1`
  - `ask_question`

对应本项目：

- `askQuestion`
- 交互式确认、能力选择、危险操作确认。

### C++ 与性能相关

日志还出现：

- `Microsoft.VisualStudio.VC.Copilot.CppLanguageFunctions Version=0.1`
  - `FunctionsCount=2`，日志片段未展开具体函数名。
- `PerformanceProfilerActivationFunctionsService Version=0.3`
  - `FunctionsCount=1`
- `DiagSessionContextProviderService`
- `ProfilerActionContextProviderService`
- `UnitTestProfilingContextProviderService`
- `DiagSessionContextTypeProviderService`

设计启发：C++ 语言能力和 profiler 能力是单独 provider，不应混入基础 build/debug provider。

## MCP 源加载机制

日志显示 VS Copilot 会从多个位置加载 MCP server 配置。

### 工作区级位置

- `<workspace>\.vscode\mcp.json`
- `<workspace>\.mcp.json`
- `<workspace>\.vs\mcp.json`
- `<workspace>\.cursor\mcp.json`

### 用户级位置

- `C:\Users\wuxin\.mcp.json`

### VS 内置位置

- `C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\Microsoft\Copilot\Mcp\mcp.json`
- `C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\NuGet\MCP\mcp.json`

### 内置 MCP Server

日志显示：

- `Microsoft Learn`
  - 来源：VS Copilot 扩展内置 `mcp.json`
  - 缓存状态：3 tools，0 prompts，0 resources
- `NuGet`
  - 来源：NuGet 扩展内置 `mcp.json`
  - 缓存状态：6 tools，0 prompts，0 resources

### Qt Creator MCP

日志中识别到：

- server 名称：`qt_creater_mcp`
- 来源：项目 `.vscode\mcp.json`
- 状态：`UserDisabled`

设计启发：

- VS Copilot 已经支持读取 VS Code / Cursor 风格 MCP 配置。
- 本项目如果提供 MCP server，可以优先支持用户通过 `.vscode/mcp.json` 或 `.mcp.json` 接入。
- 后续也可研究 VS 扩展内置 `mcp.json` 的发布方式。

## Windows MCP Registry 机制

日志显示 VS Copilot 会尝试读取 Windows 级 MCP registry：

- Registry key：`SOFTWARE\Microsoft\Windows\CurrentVersion\Mcp`
- 命令：`odr.exe list`
- 工作目录：`C:\Program Files\Microsoft Visual Studio\18\Community\Common7\ServiceHub\Hosts\ServiceHub.Host.Extensibility.amd64\net10.0-windows10.0.19041.0`

当前机器失败原因：

- `odr.exe` 找不到。
- 因此 `WindowsManifestProvider MCP feature is disabled`。
- 日志结论：`Found 0 servers in manifest data`。

设计启发：

- VS Copilot 可能预留了 Windows 级 MCP server 发现机制。
- 本项目初期不依赖该机制；优先使用 workspace/user `mcp.json`。
- 后续可作为集成增强方向。

## DebuggerAgent 与调试函数线索

trace 文件中出现：

- Agent 名：`调试程序`
- Entry point：`Microsoft.VisualStudio.Copilot.Debugging.DebuggerAgent`
- Client id：`Copilot ErrorList Fixer`
- 进程：`devenv` 与 `DevHub`
- 使用模型：`claude-haiku-4.5`、`gpt-5-mini`

trace 的 tool definitions 字段被工具截断，但可见以下调试函数线索：

- `debugger_evaluate_expr`
- `debugger_evaluate_expressions`

设计启发：

- VS Copilot DebuggerAgent 内部确实有表达式求值类能力。
- 我们规划的 `evaluateExpression`、`getCallStack`、`DebuggerContextProvider` 方向与内部实现思路一致。
- 后续可以继续从 trace 文件中提取完整 `gen_ai.tool.definitions`，以还原 DebuggerAgent 的函数 schema。

## Custom Agent / Instructions 机制

日志中出现：

- `CustomAgentManager.InitializeAsync called with 2 repository(ies)`
- `ExtensionCustomAgentRepository`
- `CopilotCustomAgentsRepository`
- 用户级 agent 路径：`C:\Users\wuxin\.github\agents`
- 用户级监听：`C:\Users\wuxin\.github`
- 工作区 `.github` 未找到时，不加载 workspace agent files。

设计启发：

- VS Copilot 的 agent 自定义路径与 GitHub/Copilot 生态有关。
- 如果本项目后续要提供自定义 agent，可考虑 `.github/agents` 或 workspace 配置方式，但这不是 P0。

## Git/worktree 路径问题

日志中多次出现类似：

- 发现 `.git` 文件。
- 尝试解析 worktree 真实 git 目录。
- 解析到 `D:/Repository/PrintSystem.git/worktrees/...`。
- 但该路径不存在或无效。
- 因此停止查找 `.github` 自定义文件。

设计启发：

- 对 workspace root / git root / solution root 的解析要有容错。
- 不应假设 `.git` 一定是目录；worktree 下 `.git` 可能是文件。
- Provider 需要明确区分：
  - solution root
  - workspace root
  - repository root
  - project root

## 对本项目的建议调整

### 推荐抽象结构

```text
VsMcpServer
  ├─ McpSourceLoader
  │   ├─ WorkspaceMcpJsonProvider
  │   ├─ UserMcpJsonProvider
  │   └─ BuiltInMcpJsonProvider
  ├─ FunctionProviders
  │   ├─ EditsFunctions
  │   ├─ OutputFunctions
  │   ├─ ContextFunctions
  │   ├─ TestExplorerFunctions
  │   ├─ DebuggerFunctions
  │   ├─ CppLanguageFunctions
  │   └─ ProfilingFunctions
  ├─ ContextProviders
  │   ├─ DocumentContextProvider
  │   ├─ OutputContextProvider
  │   ├─ DebuggerContextProvider
  │   ├─ SymbolContextProvider
  │   ├─ DiagnosticContextProvider
  │   └─ WorkspaceContextProvider
  └─ SafetyAndPolicy
      ├─ ToolAvailabilityPolicy
      ├─ DangerousOperationConfirmation
      ├─ AuditLog
      └─ SensitiveValueRedaction
```

### 能力命名建议

尽量贴近 VS Copilot 已有函数名，便于理解与对照：

- `get_errors`
- `file_search`
- `get_files_in_project`
- `get_projects_in_solution`
- `run_build`
- `get_output_window_logs`
- `read_file`
- `get_tests`
- `run_tests`
- `debugger_evaluate_expr`
- `debugger_evaluate_expressions`

如果对外作为 MCP server，可以加前缀防冲突：

- `vs_get_errors`
- `vs_file_search`
- `vs_get_projects_in_solution`
- `vs_run_build`
- `vs_debugger_evaluate_expr`

### 第一阶段建议优先验证

1. `get_projects_in_solution`
2. `get_files_in_project`
3. `run_build`
4. `get_errors`
5. `get_output_window_logs`
6. `read_file`
7. `file_search`
8. `get_tests`
9. `run_tests`
10. `debugger_evaluate_expr` 的可行性验证

### 风险与注意事项

- 不要提交原始 Copilot 日志，里面包含路径、session id、response id、quota 等信息。
- `remove_file`、`edit_file`、`run_command_in_terminal` 属于高风险能力，应默认禁用或要求确认。
- MCP server 聚合后 `FunctionsCount` 可能受用户启用/禁用策略影响，不能只看配置文件判断可用工具。
- Windows MCP Registry 机制当前不可用，不应作为 P0 依赖。
