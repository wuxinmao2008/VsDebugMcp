# Visual Studio 2026 MCP 项目资料收集

## 目标背景

- 用户希望参考 Qt Creator MCP 插件，为 Visual Studio 2026 提供类似能力：把 IDE 的编译、调试、测试、输出、项目/文件等功能通过 MCP 暴露给外部 agent。
- 当前阶段只做整体规划与设计，不编码。
- 用户是第一次做 VS 插件和 MCP 开发，需要资料调研、路线比较、架构规划。
- 运行边界优先限定本机：localhost 或 Named Pipe。

## 工作区已有资料

- [vs2026_copilot.md](vs2026_copilot.md)：已有一份 VS MCP 能力清单草案。
- [LICENSE](LICENSE)：MIT License。

## [vs2026_copilot.md](vs2026_copilot.md) 摘要

### 总体建议

- 传输：JSON-RPC over WebSocket、HTTP(S)、Named Pipe。
- 授权：本机默认允许；生产建议短期 token + TLS，或基于 OS ACL 的 Named Pipe。
- 能力发现：实现 `/capabilities`，返回能力列表、版本和支持选项。
- 数据格式：统一 JSON，错误格式为 `{ code, message, details }`。

### P0 能力

- `launch`
- `attach`
- `continue` / `pause` / `stepOver` / `stepInto` / `stepOut`
- `setBreakpoints`
- `waitForBreak` / `waitForPause`
- `getCallStack`
- `getFrameSource`
- `evaluateExpression`
- `getThreads`
- `capabilities`
- `enumerateProcesses`

### P1 能力

- `getOutputWindowText`
- `readFile`
- `grepSearch`
- `getProjectsInSolution`
- `apply_patch`，初期建议只读或 diff 支持。

### P2 能力

- `getTests`
- `launchUnitTestById`
- `runTests`
- `.NET Task wait chain`
- work item / remote repository context 集成。

### 关键约束

- 单元测试调试不要用普通 `launch`，应提供专门测试调试接口。
- `evaluateExpression` 可能产生副作用，应提供 `safeEvaluate` 或明确副作用策略。
- 多客户端连接需要会话隔离、锁或排队策略。
- 优先使用公开 VS SDK；内部 API 需要标记风险和版本锁定。
- VS 退出或扩展卸载时需要优雅关闭服务。

## MCP 官方资料

### 协议基础

- MCP 基于 JSON-RPC 2.0。
- 协议包含 capability/version discovery。
- Server 可暴露 tools、resources、prompts。
- 通信需要支持请求、响应、通知和初始化握手。

### 传输

- 官方传输包括 stdio 和 Streamable HTTP。
- 规范允许自定义传输。
- 对本项目而言：
  - 若面向通用 MCP client，优先兼容标准 MCP transport。
  - 若只做本机 VS bridge，可评估 Named Pipe，但需要额外适配 MCP framing。

### 来源

- https://modelcontextprotocol.io/specification/2025-03-26/basic/index
- https://modelcontextprotocol.io/specification/2025-03-26/basic/transports
- https://modelcontextprotocol.io/specification/2025-06-18/server/index
- https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/docs/specification/2026-07-28/server/tools.mdx
- https://blog.modelcontextprotocol.io/posts/2026-07-28/

## Qt Creator MCP 资料

### 已知能力

- Qt Creator 官方 MCP Server 插件可让 AI assistant 控制 IDE 的调试、构建和项目管理。
- 插件启用后在本机启动服务。
- 可配置监听地址、端口、CORS。
- 支持自动分配端口。
- 提供 MCP 通信检查器，用于检查 client/server 消息。
- Qt Creator 19 发布说明提到其基础 MCP server 支持打开文件/项目、构建、运行、调试等工具。

### 对 VS 项目的启发

- IDE 内嵌 MCP server 是可行产品形态。
- 第一版应以本机能力暴露为主，避免远程开放。
- 需要能力发现和通信诊断能力。
- 工具应按项目、构建、运行、调试、文件/搜索、测试分组。
- 调试相关工具需要有状态 session 模型。

### 来源

- https://doc.qt.io/qtcreator/creator-how-to-mcp-server.html
- https://doc.qt.io/qtcreator/qtcreator-attribution-davecotter-mcp.html
- https://github.com/qt-creator/qt-creator/releases
- https://www.qt.io/blog/qt-creator-19-released

## Visual Studio 扩展资料

### VisualStudio.Extensibility / Out-of-proc

- `VisualStudio.Extensibility` 是较新的扩展模型，主推 out-of-process。
- Out-of-proc 扩展通过 RPC/ServiceHub 与 VS 交互，稳定性和性能边界更好。
- 支持 SDK-style VSIX 项目。
- 可与传统 VSSDK in-proc 扩展混用，以补齐 API 缺口。

### 相关能力

- Project Query API 提供项目查询能力。
- 文档中出现 `BuildAsync`、`RebuildAsync`、`CleanAsync`、`DebugLaunchAsync`、`LaunchAsync` 等项目操作。
- 适合优先验证构建、启动、项目枚举等能力。

### 来源

- https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility/visualstudio-extensibility?view=visualstudio
- https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility/get-started/oop-extensibility-model-overview?view=visualstudio
- https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility/get-started/create-your-first-extension?view=visualstudio
- https://learn.microsoft.com/visualstudio/extensibility/project-visual-studio-sdk?view=vs-2022

## 传统 VSSDK / In-proc 资料

### 构建

- `IVsSolutionBuildManager` 管理解决方案构建、构建顺序、依赖等。
- 可作为完整构建能力的底层入口候选。

### 调试

- `SVsShellDebugger` / `IVsDebugger` 是 VS 调试相关公开服务入口。
- `IVsDebugger2.LaunchDebugTargets2` 可用于启动调试目标。
- 调试器 launch 过程涉及 `VsDebugTargetInfo2` 和项目 `DebugLaunch`。
- 更底层的调试引擎 API 复杂度较高，可能需要逐项 POC 验证。

### 输出/错误

- `SVsOutputWindow` / `IVsOutputWindow` 可访问 Output Window。
- `SVsGeneralOutputWindowPane`、Build pane 等是内置输出 pane。
- `SVsErrorList` / `IVsErrorList` 是错误列表相关服务入口。

### 来源

- https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualstudio.shell.interop.ivssolutionbuildmanager?view=visualstudiosdk-2022
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualstudio.shell.interop.ivsdebugger?view=visualstudiosdk-2022
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualstudio.shell.interop.ivsdebugger2.launchdebugtargets2?view=visualstudiosdk-2022
- https://learn.microsoft.com/en-us/visualstudio/extensibility/debugger/launching-a-program?view=visualstudio
- https://learn.microsoft.com/en-us/visualstudio/extensibility/extending-the-output-window

## Test Explorer 资料

- Visual Studio Test Explorer 支持发现、运行、调试单元测试。
- 支持 .NET 多语言和 native C/C++ 测试框架/适配器。
- 扩展侧是否存在稳定公开 API 可用于枚举、运行和调试测试仍需进一步验证。
- 测试能力应列为独立子系统，不应简单复用普通 debug launch。

### 来源

- https://learn.microsoft.com/en-us/visualstudio/test/run-unit-tests-with-test-explorer?view=visualstudio

## 初步架构判断

### 方案 A：VSIX 内嵌 MCP Server

优点：

- 最接近 Qt Creator 模式。
- 对 IDE 当前状态、解决方案、调试器、输出窗口访问更直接。
- 用户体验集中：安装 VSIX 即可启用。

风险：

- in-proc 服务监听、线程模型、生命周期和安全边界复杂。
- 若扩展崩溃可能影响 VS。
- 调试器深度 API 复杂。

### 方案 B：独立 MCP Server + VS 自动化/SDK Bridge

优点：

- MCP server 与 VS 解耦，稳定性更好。
- 可以使用 stdio/HTTP 等标准 MCP server 形态。
- 更容易测试和独立发布。

风险：

- 外部进程访问 VS 状态可能受限。
- 仍可能需要一个轻量 VSIX bridge。
- 调试器、输出窗口、测试等能力可能无法纯外部实现。

### 方案 C：混合架构

推荐初步方向：

- 对外是独立或半独立 MCP Server。
- VSIX 提供 VS Capability Provider。
- 内部抽象 `BuildProvider`、`DebugProvider`、`TestProvider`、`ProjectProvider`、`FileProvider`。
- 能用 VisualStudio.Extensibility 的能力优先使用 OOP API。
- 必须访问传统服务时，通过 VSSDK in-proc bridge 实现。

## 初步推荐能力模型

### Build tools

- `vs_get_solution_info`
- `vs_build_solution`
- `vs_build_project`
- `vs_clean_solution`
- `vs_get_build_output`
- `vs_get_error_list`

### Debug tools

- `vs_debug_launch_project`
- `vs_debug_launch_exe`
- `vs_debug_attach_process`
- `vs_debug_set_breakpoints`
- `vs_debug_continue`
- `vs_debug_pause`
- `vs_debug_step_over`
- `vs_debug_step_into`
- `vs_debug_step_out`
- `vs_debug_get_threads`
- `vs_debug_get_call_stack`
- `vs_debug_evaluate_expression`
- `vs_debug_wait_for_break`

### Project/code tools

- `vs_get_projects`
- `vs_get_project_files`
- `vs_read_file`
- `vs_grep_search`
- `vs_code_search`
- `vs_get_symbols_by_name`
- `vs_create_patch`
- `vs_apply_patch`，建议后置且需要确认。

### Test tools

- `vs_get_tests`
- `vs_run_tests`
- `vs_debug_test_by_id`

## 主要风险

- VS 2026 文档和 VS SDK 2022/预览文档之间存在版本差异，需要后续用实际 SDK 验证。
- VisualStudio.Extensibility 是否覆盖完整调试/测试能力不确定。
- 传统 VSSDK 能力强但复杂度和稳定性风险较高。
- 表达式求值、文件写入、进程 attach 都属于高风险能力，需要权限控制。
- 多 agent/多客户端控制同一 VS 实例可能导致状态竞争。
- Test Explorer API 的公开可编程能力仍需专项验证。

## 待确认问题

- 最终文档受众：个人学习、开源 README、还是团队评审设计文档。
- 是否要兼容 Qt Creator MCP 的工具命名/行为，还是只借鉴架构。
- 是否必须严格遵循 MCP 官方 tool schema，还是允许内部 REST/JSON-RPC bridge。
- 第一版是否允许只规划文件写入，不实现自动 apply。
