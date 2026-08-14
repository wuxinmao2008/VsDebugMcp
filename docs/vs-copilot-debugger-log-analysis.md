# VS Copilot DebuggerAgent 日志分析

## 分析目标

重点日志：`C:\Users\wuxin\AppData\Local\Temp\VSGitHubCopilotLogs\20260813_072145.947_VSGitHubCopilot.chat.log`

关联 trace：`C:\Users\wuxin\AppData\Local\Temp\VSGitHubCopilotLogs\traces\4bf1a5ba_VSGitHubCopilot_traces.jsonl`

目标：从 MS GHCP / VS Copilot 日志中解析与调试器有关的 provider、agent、feature flag、trace span、tool/function 线索。

## 直接可见的调试相关组件

### Debugger context provider

日志注册了：

- `Microsoft.VisualStudio.Copilot.DebuggerContextProvider Version=0.3`

含义推断：

- 这是 VS Copilot 获取当前调试上下文的 provider。
- 它很可能负责收集当前 debug session、当前线程、当前栈帧、当前异常/停止点等上下文。
- 它不是 function provider，而是 context provider；也就是说，它可能用于 prompt/context 注入，不一定直接暴露成可调用工具。

### Debugging issue context provider

日志注册了：

- `namespace Microsoft.VisualStudio.Copilot.Debugging.IssueContextProvider Version=0.3`

注意日志里带有 `namespace` 字样，可能是注册名格式异常或内部类型名输出。

含义推断：

- 该 provider 可能用于调试/诊断问题上下文。
- 结合 trace 中的 `Copilot ErrorList Fixer` client id，可能与“从错误列表/异常/诊断问题发起 Copilot 分析”有关。

### DebuggerAgent

日志注册了：

- `Microsoft.VisualStudio.Copilot.Debugging.DebuggerAgent Version=0.3`

trace 中也出现：

- `copilot_chat.entry_point = Microsoft.VisualStudio.Copilot.Debugging.DebuggerAgent`
- `gen_ai.agent.name = 调试程序`
- `copilot_chat.mode = Installed`
- `copilot_chat.client_id = Copilot ErrorList Fixer`

含义推断：

- DebuggerAgent 是一个已安装 agent/responder，不只是普通函数集合。
- VS UI 里选择“调试程序”agent 后，会进入这个 responder。
- 它可调用调试相关工具，并使用 debugger context provider 注入当前 IDE/debugger 状态。

## 调试相关 feature flags

重点日志的 Settings report 中有以下调试相关开关：

- `EnableBreakpointSuggestions=True`
- `EnableWatchSuggestions=True`
- `EnableDebugErrorCodeService=True`
- `EnableParallelStacksSummarizationService=True`
- `ExceptionAnalysisThinking=Medium`
- `EnableDebuggerSubAgents=False`
- `EnableTestFailureAnalysis=True`
- `EnableDiagnosticsHubSuggestions=True`
- `EnableDiagnosticsHubProfilerAgent=True`
- `AnalyzeTestWithCopilot=False`
- `EnableOutputWindowContext=True`
- `EnableErrorContext=True`
- `ErrorDiagnosticsEnabled=True`
- `EnableWorkspaceErrorContext=False`
- `DisabledFunctionsInCopilotEdits=lookup_vs;get_debugging_info`

设计启发：

- VS Copilot 调试能力不是单点能力，而是由断点建议、Watch 建议、错误码服务、并行堆栈总结、异常分析、测试失败分析、DiagnosticsHub/Profiler 等子能力组成。
- `EnableDebuggerSubAgents=False` 表示当前版本可能预留了 debugger sub-agent 架构，但该环境未开启。
- `get_debugging_info` 在 Copilot Edits 中被禁用，说明它可能是一个默认存在但不允许在 Edits 场景使用的内部函数。

## Trace span 结构

关联 trace 中多次出现类似 span 组合：

1. `devenv` 进程 span：
   - `name = invoke_agent 调试程序`
   - `process.runtime.name = devenv`
   - `process.runtime.version = .NET Framework 4.8.9337.0`
   - `gen_ai.agent.name = 调试程序`
   - `copilot_chat.entry_point = Microsoft.VisualStudio.Copilot.Debugging.DebuggerAgent`

2. `DevHub` 进程 span：
   - `name = chat claude-haiku-4.5` 或 `chat gpt-5-mini`
   - `process.runtime.name = DevHub`
   - `process.runtime.version = .NET 10.0.11`
   - `server.address = api.githubcopilot.com`
   - 包含 `gen_ai.tool.definitions`

结论：

- `devenv` 负责触发/承载 IDE agent entry point。
- `DevHub` 负责模型调用与 tool definitions 传输。
- DebuggerAgent 是 VS 内部 responder，模型调用发生在 OOP 服务进程。
- 对本项目来说，`VSIX bridge + OOP MCP host` 的混合架构与此高度一致。

## 可见的调试工具/function 线索

trace 中 `gen_ai.tool.definitions` 字段被读取工具截断，但可见以下函数名：

- `debugger_evaluate_expr`
- `debugger_evaluate_expressions`

其中一个完整可见的描述片段：

- `debugger_evaluate_expressions`：`This function retrieves the implementations of the specified list of expressions to improve the accuracy of the analysis requested by the user. All arguments must be provided.`

注意：该描述里的 `implementations` 用词有点奇怪，可能是通用模板或内部语义；从函数名看，它应是批量表达式求值。

推断工具分层：

- `debugger_evaluate_expr`：单表达式求值。
- `debugger_evaluate_expressions`：批量表达式求值。
- 可能还有未完整显示的调试信息函数，例如 Settings report 中出现的 `get_debugging_info`。

## 模型与 DebuggerAgent

trace 中 DebuggerAgent 使用过：

- `claude-haiku-4.5`
- `gpt-5-mini`

日志中模型刷新场景：

- `SelectedMode(Installed,Microsoft.VisualStudio.Copilot.Debugging.DebuggerAgent)`
- `responderService=Microsoft.VisualStudio.Copilot.Debugging.DebuggerAgent`

这说明：

- DebuggerAgent 是一个可被 UI mode selection 选中的 responder service。
- 选中 DebuggerAgent 后，模型列表会以该 responder 作为上下文刷新。

## 与普通 Copilot tools 的区别

普通 function provider 中没有直接列出调试工具：

- `EditsFunctions` 有 `get_errors`、`run_build`、`file_search` 等。
- `TestExplorerFunctions` 有 `get_tests`、`run_tests`。
- 但 `debugger_evaluate_expr` 没有在普通 `RegisterLazyFunctionProviderAsync` 列表中出现。

推断：

- 调试工具可能不是全局 lazy function provider，而是 DebuggerAgent responder 专属工具。
- 它们只在选中 DebuggerAgent 或特定调试场景时传给模型。
- 这解释了为什么普通日志里 function registry 不显示完整 debugger function provider。

## DebuggerAgent 与 ErrorList Fixer

trace 中 `copilot_chat.client_id = Copilot ErrorList Fixer`，但 entry point 是 DebuggerAgent。

可能解释：

1. VS 的错误列表修复入口会复用 DebuggerAgent。
2. 用户从错误/异常/调试问题入口发起后，内部 client id 标记为 ErrorList Fixer。
3. DebuggerAgent 可能能同时处理编译错误、运行时异常、断点上下文等诊断问题。

对本项目启发：

- 不应把 `ErrorList`、`Diagnostics`、`Debugger` 完全割裂。
- 可以设计一个 `DiagnosticsContextProvider`，同时聚合 build errors、runtime exceptions、debug stopped reason、output window 日志。

## 对本项目的设计启发

### 推荐调试 provider 拆分

```text
DebuggerCapabilityProvider
  ├─ DebuggerContextProvider
  │   ├─ 当前调试会话
  │   ├─ 当前停止原因
  │   ├─ 当前线程
  │   ├─ 当前栈帧
  │   └─ 当前异常/错误码
  ├─ DebuggerFunctions
  │   ├─ debugger_get_info
  │   ├─ debugger_get_threads
  │   ├─ debugger_get_call_stack
  │   ├─ debugger_evaluate_expr
  │   ├─ debugger_evaluate_expressions
  │   ├─ debugger_set_breakpoints
  │   ├─ debugger_continue
  │   ├─ debugger_pause
  │   └─ debugger_step_*
  ├─ DebuggerSuggestions
  │   ├─ breakpoint suggestions
  │   └─ watch suggestions
  └─ DebuggerSummaries
      ├─ exception analysis
      └─ parallel stacks summary
```

### P0 调试能力建议

基于日志和 VS Copilot 内部线索，P0 可优先验证：

1. `debugger_get_info`，对应内部疑似 `get_debugging_info`。
2. `debugger_evaluate_expr`。
3. `debugger_evaluate_expressions`。
4. `debugger_get_threads`。
5. `debugger_get_call_stack`。
6. `debugger_get_current_exception` 或合并进 `debugger_get_info`。
7. `debugger_set_breakpoints`。

### 安全注意

- 表达式求值可能有副作用，必须支持 `safe` / `allowSideEffects` 参数或明确标注。
- Watch 建议、表达式求值、异常分析都依赖“当前暂停帧”；如果程序未暂停，应返回结构化错误。
- 并行堆栈总结可能产生大量数据，应设置 frame/thread 数量限制。
- 不要默认暴露任意进程 attach；attach 应独立高风险确认。

## 当前无法从日志确认的内容

由于 trace 中 `gen_ai.tool.definitions` 被读取工具截断，暂时不能完整还原：

- `debugger_evaluate_expr` 的完整 JSON schema。
- `debugger_evaluate_expressions` 的完整参数 schema。
- 是否存在 `debugger_get_threads`、`debugger_get_call_stack`、`debugger_get_task_info` 等完整工具。
- 工具实际调用结果和返回 schema。

后续可行办法：

- 用脚本直接读取 jsonl，提取 `gen_ai.tool.definitions` 字段并做 JSON unescape。
- 搜索 trace 中 `gen_ai.tool.calls`、`tool.name`、`debugger_`，还原实际调用序列。
- 读取持久化 session 文件，查看 DebuggerAgent 会话是否保存了工具调用内容。
