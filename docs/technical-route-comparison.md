# 技术调研与路线比较

## 说明

本资料用于规划“为 Visual Studio 2026 暴露 MCP 能力”的技术路线。目标是参考 Qt Creator MCP 插件，同时结合 Visual Studio 扩展模型、MCP 新规范和 VS 调试/构建/测试 API，比较可选实现路线。

## MCP 2026-07-28 新规范影响

- 2026-07-28 MCP 规范重点变化：协议核心转向无状态、引入 Multi Round-Trip Requests、基于 header 的路由、list 结果可缓存、授权加固、正式 extensions framework。
- 对本项目关键影响：不要依赖 MCP transport session 保存调试上下文；如果需要跨调用状态，应由工具显式返回 `debugSessionId`、`buildTaskId`、`testRunId` 等 handle，并要求后续 tool call 传回。
- Streamable HTTP 在 2026-07-28 下要求请求携带 `MCP-Protocol-Version`、`Mcp-Method`，调用工具时还应有 `Mcp-Name`，方便网关/安全策略按工具名路由和审计。
- 旧 HTTP+SSE 已被标记 deprecated，新实现不建议采用。
- C# SDK 是官方 Tier 1 SDK；`ModelContextProtocol.AspNetCore` 支持 HTTP-based MCP server，适合 .NET/VSIX 生态验证。

### 来源

- https://blog.modelcontextprotocol.io/posts/2026-07-28/
- https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/docs/specification/2026-07-28/basic/transports/streamable-http.mdx
- https://github.com/modelcontextprotocol/csharp-sdk
- https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/transports/transports.md

## Qt Creator MCP 代码结构观察

- Qt Creator MCP 不是单一大工具，而是由多个 IDE 子系统各自注册工具：`mcpserver`、`projectexplorer`、`debugger`、`autotest`、`cppeditor`、`cmakeprojectmanager`、`qtsupport` 等。
- 核心抽象是 `ToolRegistry::registerTool(...)`，每个工具声明 name/title/description/inputSchema/outputSchema/annotations，再绑定 callback。
- `AutoRegisteringServer` 会自动收集已注册工具，server info 名称为 `qt-creator-mcp-server`。
- Qt Creator 插件提供 inspector、settings page、log capture；这提示 VS 项目也应设计“通信检查器/日志窗口/工具开关”。
- 已观察到的工具方向包括：
  - 文件/目录：`file_info`、`list_directory`、`save_file`、`list_open_files`、`list_visible_files`、`read_pane`。
  - 项目/构建：`build`、`get_build_status`、`list_issues`、`list_file_issues`、`run`、`debug`、`search_in_files`、`replace_in_files`、`find_files_in_projects`、`list_projects`、`switch_build_config`、`add_build_config`、`get_current_project`。
  - C++ 代码模型：`get_file_symbols`、`find_references`、`get_symbol_info`、文件 diagnostics。
  - 测试：`run_tests`、`get_last_test_results`、`get_test_details`、`list_tests`。
  - 调试：`get_breakpoints`、`get_threads`、`select_thread` 等。
- 设计启发：VS 项目也应采用“核心 MCP server + 各 capability provider 插件化注册”的结构，而不是把全部工具堆在一个类中。

### 来源

- https://github.com/qt-creator/qt-creator/tree/master/src/plugins/mcpserver
- https://github.com/qt-creator/qt-creator/tree/master/src/libs/mcp/server
- https://github.com/qt-creator/qt-creator/tree/master/src/plugins/projectexplorer/mcpsupport.cpp
- https://github.com/qt-creator/qt-creator/tree/master/src/plugins/debugger/mcpsupport.cpp
- https://github.com/qt-creator/qt-creator/tree/master/src/plugins/autotest/mcptools.cpp
- https://github.com/qt-creator/qt-creator/tree/master/src/plugins/cppeditor/mcpsupport.cpp

## Visual Studio 扩展模型比较

### VSSDK

- 微软文档明确：VSSDK 是最完整、最强大、覆盖面最广的模型，但最复杂。
- VSSDK 扩展运行在 Visual Studio 进程内，因此扩展 bug、死循环、UI 线程阻塞可能直接影响 VS。
- VSSDK 只能使用 .NET Framework 环境；需要处理 COM、DTE、MEF、线程切换、`JoinableTaskFactory` 等复杂概念。
- 对本项目意义：调试器深度控制、Output Window、Error List、传统服务访问大概率需要 VSSDK。

### Community Toolkit

- Community Toolkit 是 VSSDK 的易用封装，API 更 .NET-friendly。
- 但它仍然受 VSSDK 限制：进程内、.NET Framework、线程模型复杂、能力边界与 VSSDK 一致。
- 对本项目意义：可作为降低开发门槛的辅助层，但不应作为核心架构依赖；关键底层能力仍需理解 VSSDK。

### VisualStudio.Extensibility / OOP

- 新模型主推 out-of-process，扩展运行在 `Microsoft.ServiceHub.Host.Extensibility` 等外部进程，经 RPC/brokered services 与 VS 通信。
- 优点：隔离性好、async API、一致性好、可用现代 .NET、安装体验更现代。
- 缺点：VS scenario breadth 仍不如 VSSDK；某些深度调试/测试/输出能力可能缺 API。
- 对本项目意义：适合作为新项目起点，承载 MCP server/bridge、项目查询、构建、启动等高层能力。

### VSSDK-compatible VisualStudio.Extensibility / Hybrid

- 微软官方提供在进程内使用 VisualStudio.Extensibility，同时访问 VSSDK/MEF 服务的模式。
- 文档目标就是让新模型早期采用者在遇到 API 缺口时回退到 `Microsoft.VisualStudio.Sdk`。
- 对本项目意义：这是当前最接近需求的官方路线：用新模型组织扩展与 DI，用 VSSDK 补齐调试器/输出/错误/测试等缺口。

### 来源

- https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility/extensibility-models?view=visualstudio
- https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility/get-started/oop-extensibility-model-overview?view=visualstudio
- https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility/get-started/in-proc-extensions?view=visualstudio
- https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility/get-started/debug-extensions?view=visualstudio

## VSIX SDK-style 项目资料

- 2026 年 4 月 Visual Studio Blog：从 Visual Studio 18.5 开始，VSSDK-based VSIX 支持官方 SDK-style project。
- 这将传统 VSIX 项目带入更现代的构建/部署/调试流程，改善增量构建和 F5 调试可靠性。
- 对本项目意义：即使采用 VSSDK/Hybrid，也应优先评估 SDK-style VSIX 项目结构，降低项目文件复杂度。

### 来源

- https://devblogs.microsoft.com/visualstudio/sdk-style-support-for-extension-projects/

## Visual Studio 构建/项目能力

- Project Query API 可查询项目系统信息、理解项目文件、包引用、添加文件、调整项目属性等。
- VisualStudio.Extensibility 示例中可对 solution/project 调用 `BuildAsync`、`RebuildAsync`、`CleanAsync`、`DebugLaunchAsync`、`LaunchAsync`。
- 传统 VSSDK 的 `IVsSolutionBuildManager` 用于解决方案构建、构建顺序和依赖管理。
- 对本项目建议：构建能力优先从 Project Query / Extensibility API 开始验证；若需更完整构建控制再降级到 `IVsSolutionBuildManager`。

### 来源

- https://learn.microsoft.com/visualstudio/extensibility/project-visual-studio-sdk?view=vs-2022
- https://github.com/microsoft/VSExtensibility/tree/main/New_Extensibility_Model/Samples/VSProjectQueryAPISample
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualstudio.shell.interop.ivssolutionbuildmanager?view=visualstudiosdk-2022

## Visual Studio 调试能力

- `IVsDebugger2.LaunchDebugTargets2` 文档说明可在调试器控制下 launch 或 attach 指定进程。
- Visual Studio Debugger Extensibility 文档更偏向“实现/扩展调试引擎”，不是简单控制现有 IDE 调试器；但它明确了执行控制、断点、表达式求值、stack frame 等架构概念。
- 表达式求值依赖程序停止在断点处；stack frame 提供 expression evaluation context；通过 `IDebugExpressionContext2.ParseText` 生成 `IDebugExpression2`，再 `EvaluateSync/EvaluateAsync` 得到 `IDebugProperty2`。
- 这说明 `evaluateExpression(frameId, expression)` 工具在 VS 中理论上可映射，但实际要获取当前 stack frame/context 的 API 路径需要 POC 验证。
- 断点功能方面，VS 用户文档确认支持 conditional breakpoint、tracepoint/logpoint、data breakpoint、function breakpoint 等；MCP 第一版可先支持 source line + condition + logMessage。

### 来源

- https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualstudio.shell.interop.ivsdebugger2.launchdebugtargets2?view=visualstudiosdk-2022
- https://learn.microsoft.com/en-us/visualstudio/extensibility/debugger/launching-a-program?view=visualstudio
- https://learn.microsoft.com/en-us/visualstudio/extensibility/debugger/evaluating-expressions?view=visualstudio
- https://learn.microsoft.com/en-us/visualstudio/extensibility/debugger/expression-evaluation-context?view=vs-2022
- https://learn.microsoft.com/en-us/visualstudio/extensibility/debugger/stack-frames?view=visualstudio
- https://learn.microsoft.com/en-us/visualstudio/debugger/using-breakpoints?view=visualstudio

## 独立 MCP Server + VS 自动化路线

- EnvDTE/DTE 自动化可以从外部进程获取运行中的 VS 实例并执行部分自动化，如 SolutionBuild、Debugger attach、break、LocalProcesses 等。
- 但 DTE 是较老 COM 自动化模型，文档和实践资料较分散；外部自动化对当前调试 session 深度状态、表达式求值、错误列表、测试发现的访问可能不足。
- 对本项目意义：独立 MCP Server 可作为快速验证或 fallback，但若目标是完整 IDE 能力，仍可能需要 VSIX bridge。
- 推荐把独立 MCP server 视为“host/transport 层”，而不是唯一访问 VS 的实现层。

## 已批准的安装与传输演进（2026-08-18）

保留 Hybrid 的进程边界，但改变 Host 的分发和客户端 transport：

```text
VS Code
  -> Streamable HTTP over current-user Windows Named Pipe
  -> one shared OOP Host per Windows user
  -> vsInstanceId router
  -> per-instance Named Pipe RPC
  -> VSIX Bridge in each Visual Studio instance
```

- Host 仍是独立进程，不在 `devenv.exe` 中承载 MCP server。
- Host 以 `win-x64` self-contained 产物随 VSIX 安装，由 VSIX 确保启动；不依赖 Visual Studio 私有 runtime。
- VS Code 使用固定 `pipe:///pipe/...` MCP 配置，不需要单独安装或定位 Host，也不需要开放 localhost TCP 端口。
- Host 到 VSIX 的现有自定义 RPC 保留，但 pipe 名改为实例级；同一用户的多个 VS 实例由共享 Host 发现和路由。
- 单实例时工具可省略 `vsInstanceId`，多实例时必须显式指定；新增实例列表和条件查找工具。
- stdio transport 仅作为迁移期开发和自动化回归入口保留。

实施前置闸门是验证 VS Code MCP client、C# MCP SDK Streamable HTTP 与 ASP.NET Core Kestrel Named Pipe transport 的完整往返；闸门未通过前不展开多实例和打包改造。

### 来源

- https://learn.microsoft.com/en-us/dotnet/api/envdte.debugger.break?view=visualstudiosdk-2022
- https://learn.microsoft.com/en-us/dotnet/api/envdte80.debugger2?view=visualstudiosdk-2022
- https://learn.microsoft.com/en-us/dotnet/api/envdte.solutionbuild?view=visualstudiosdk-2022

## 路线比较初步结论

1. 纯独立 MCP Server：适合快速验证 build/search/read 等外围能力，但难以完整覆盖 VS 调试器/测试/输出窗口状态。
2. 纯 VSSDK VSIX 内嵌 MCP Server：能力覆盖最强，但稳定性、安全、线程和协议服务承载风险最大。
3. 纯 VisualStudio.Extensibility OOP：架构最现代、安全边界好，但 API 覆盖可能不足。
4. Hybrid 推荐路线：`MCP Server/Transport` 尽量放到 out-of-proc 或独立进程；`VS Capability Bridge` 通过 VisualStudio.Extensibility + VSSDK-compatible in-proc provider 暴露必要 VS 服务；对外只暴露标准 MCP tools。

## 新增规划约束

- MCP 2026-07-28 无协议 session 后，VS 调试状态必须作为显式 application handle 管理。
- 长任务如 build/test/debug wait 应考虑 MCP Tasks extension 或项目自定义 `taskId` + polling 工具。
- 每个工具应标注 read-only/idempotent/destructive 等 annotations，并在 server 侧执行安全策略，而不是只依赖模型自觉。
- 第一版设计应包含“工具注册表 + provider 插件模型 + 能力探测 + 工具开关 + 审计日志”。
