# Visual Studio 2026 MCP 项目整体蓝图

## 当前共识

- 目标：参考 Qt Creator MCP 插件，为 Visual Studio 2026 / VS 18.x 提供类似能力，将 IDE 的构建、调试、测试、输出、项目/文件和代码搜索能力通过 MCP 暴露给外部 agent。
- 阶段：Phase 0 已完成，Phase 1 最小 IDE 闭环实施中；项目、构建生命周期和 Build Output 已完成在线验收。
- 推荐主线：`Hybrid：OOP MCP Host + VSIX/VSSDK Bridge`。
- 能力范围：构建/编译、启动/附加调试与断点、错误列表/输出窗口、测试发现与测试运行、代码搜索/文件读取/补丁编辑。
- 运行边界：仅本机使用；VS Code 到共享 Host 使用当前用户 ACL 保护的 Windows Named Pipe HTTP，Host 到 VSIX Bridge 继续使用实例级 Named Pipe RPC，不开放 TCP 或远程访问。
- 部署形态：仅发布 `win-x64` self-contained Host，随 VSIX 安装并由 VSIX 确保启动，不依赖 Visual Studio 私有运行时或单独安装 Host。
- 多实例：同一 Windows 用户共享一个 Host；每个 Visual Studio 实例拥有独立 Bridge pipe，通过显式 `vsInstanceId` 路由。

## 实施进度（2026-08-17）

### 已完成并在线验收

- Phase 0A：Console Host ↔ Named Pipe ↔ VSIX Bridge。
- Phase 0B：标准 MCP stdio Host，基于官方 C# MCP SDK。
- `vs_health`
- `vs_capabilities`
- `vs_get_projects_in_solution`
- `vs_run_build`
- `vs_get_build_status`
- `vs_cancel_build`
- `vs_get_output_window_logs`

当前在线链路：

```text
MCP Client
  -> VsDebugMcp.Host (.NET 8 / stdio)
  -> Named Pipe
  -> VSIX Bridge
  -> Visual Studio 18.9 Experimental Instance
```

已批准的目标链路：

```text
VS Code / MCP Client
  -> Streamable HTTP over current-user Windows Named Pipe
  -> shared VsDebugMcp.Host (OOP / win-x64 self-contained)
  -> instance registry and vsInstanceId routing
  -> per-instance Named Pipe RPC
  -> Visual Studio VSIX Bridge
  -> Visual Studio 18.x instance
```

迁移期间保留 stdio 模式用于开发和回归测试，但安装后的默认接入不再要求 VS Code 直接启动 Host。

已验证的构建行为：

- `Debug|x64`、`Release|x64` solution build。
- 异步状态：`starting → running → succeeded/failed/cancelled`。
- 同一实例只允许一个活动构建。
- 支持取消、并发拒绝、旧 handle 拒绝和非法配置拒绝。
- Build task 状态由 VSIX 持有，不依赖短 Named Pipe 连接生命周期。

已验证的 Build Output 行为：

- 通过 EnvDTE 按 `BuildOutputPane_guid` 读取“输出 → 生成”窗格，不依赖本地化显示名称。
- `vs_get_output_window_logs(source="build")` 在线返回 Qt/MSBuild 和 MSVC 原始输出。
- 在线样本返回 `4428` 字符，包含 `C2039`、`C2065`、`C2355` 等编译错误和最终构建汇总。
- `maxChars=200` 时正确返回尾部文本，并设置 `truncated=true`。

### 已实现但尚未完成验收

- `vs_get_errors`
  - 已完成 Protocol、Bridge、Host、MCP tool 和基础过滤实现。
  - 语义保持为当前 Error Table、仅 `ErrorSource.Build`、忽略 UI 筛选、不保存历史。
  - 当前 Qt/C++ 工程的编译错误可见于 Build Output，但通过公开 Error Table source 路径仍返回空集合。
  - 不允许静默降级为解析 Build Output；公开 API 无法可靠提供数据时应返回 `diagnostics_unavailable`。
  - Build Output 已通过独立工具解决 agent 获取原始编译信息的当前需求，但 `vs_get_errors` 仍属于未完成项。

### 当前 MCP capability

1. `phase0.ipc`（保留的 stub capability）
2. `vs_get_projects_in_solution`
3. `vs_run_build`
4. `vs_get_build_status`
5. `vs_cancel_build`
6. `vs_get_errors`
7. `vs_get_output_window_logs`

`vs_health` 作为 MCP tool 提供，但不重复列入 Bridge capability 数组。

### 自动化和部署状态

- Protocol tests 最近一次完整运行：`8/8` 通过。
- Host tests 最近一次完整运行：`16/16` 通过。
- 后续已增加 Build Output DTO、tool forwarding 和 tool discovery 测试；最新完整 managed test run 待执行。
- VSIX 已使用 VS 18 MSBuild 成功编译。
- 已新增独立 VS Code 任务：
  - `build: vsix`：只构建，不部署。
  - `deploy: vsix`：执行 `Build;DeployVsixExtensionFiles` 并显式启用部署。
- 已在线确认编译 DLL 与 Exp 安装副本 SHA-256 一致，解决了“构建成功但安装目录未更新”的问题。

## 核心依据

- Qt Creator MCP 已验证 IDE 内嵌/桥接 MCP 的产品形态：插件启用后提供本机 MCP server，工具按 project/debugger/autotest/cppeditor 等子系统注册，并提供 inspector、settings、log capture。
- VS Copilot 日志显示 MS GHCP 已经采用 `devenv + DevHub/OOP` 混合架构：`devenv` 承载 IDE 状态集成，`DevHub`/ServiceHub 负责模型调用、MCP 聚合和 tool definitions。
- VS Copilot 日志显示内部已有类似能力：`get_errors`、`file_search`、`get_files_in_project`、`get_projects_in_solution`、`run_build`、`get_output_window_logs`、`read_file`、`get_tests`、`run_tests`。
- VS Copilot DebuggerAgent 日志显示调试能力被拆为 `DebuggerContextProvider`、`IssueContextProvider`、`DebuggerAgent`，并出现 `debugger_evaluate_expr`、`debugger_evaluate_expressions`、疑似 `get_debugging_info`。
- Visual Studio 官方扩展模型显示：`VisualStudio.Extensibility` 适合 OOP/现代 .NET 扩展，VSSDK/in-proc 适合访问调试器、输出窗口、错误列表等深层 IDE 服务，官方也支持 hybrid 模式补齐 API 缺口。

## 技术选型

| 层级 | 选型 | 理由 |
|---|---|---|
| MCP 协议 | 标准 MCP tools/resources | 保持外部 agent 兼容，不把接口锁死为自定义 REST。 |
| MCP SDK | .NET / C# MCP SDK | 与 VSIX、Visual Studio、ServiceHub 生态一致。 |
| MCP Host | OOP standalone .NET 进程 | 避免在 `devenv` 中承载长生命周期网络服务。 |
| VS Bridge | VSIX Bridge | 负责访问当前 VS 实例、解决方案、调试器、输出窗口等 IDE 状态。 |
| 高层 VS 能力 | `VisualStudio.Extensibility` | 适合项目、构建、启动等较现代 API。 |
| 深层 VS 能力 | VSSDK / COM services | 用于调试器、错误列表、输出窗口等深层能力。 |
| MCP Client Transport | Streamable HTTP over Windows Named Pipe | VS Code 只需固定 pipe URL；当前用户 ACL；无 TCP 端口冲突。 |
| Host ↔ VSIX IPC | 每实例独立 Named Pipe RPC | 保留现有协议与进程隔离，同时支持多个 VS 实例。 |
| Host 部署 | VSIX 内置 `win-x64` self-contained | 用户无需单独安装 Host 或 .NET Runtime。 |
| VSIX 项目形态 | SDK-style VSIX | 适配 VS 18.x，项目结构更现代。 |
| 安全模型 | 工具分级 + 确认 + 审计 + 脱敏 | 覆盖表达式求值、attach、写文件、命令执行等风险。 |

## 总体架构

```text
MCP Client / Agent
  └─ Streamable HTTP over user-scoped Windows Named Pipe
    └─ shared VsMcpHost (OOP / self-contained .NET)
          ├─ ToolRegistry
          ├─ CapabilityDiscovery
      ├─ VisualStudioInstanceRegistry
      ├─ BridgeRouter
          ├─ SessionHandleStore
          ├─ PolicyAndAudit
      └─ VsBridgeClient per vsInstanceId
        └─ per-instance Named Pipe RPC
          └─ Visual Studio VSIX Bridge instance
                      ├─ ExtensibilityProvider
                      ├─ VssdkProvider
                      ├─ BuildProvider
                      ├─ DebuggerProvider
                      ├─ OutputProvider
                      ├─ ErrorListProvider
                      ├─ TestExplorerProvider
                      ├─ ProjectProvider
                      └─ FileSearchProvider
```

## 共享 Host 与多实例规则

- Host 按当前 Windows 用户保持单例，由任意已启用的 VSIX 实例确保启动。
- 每个 VSIX 使用 `PID + process start time` 生成会话级 `vsInstanceId`，并注册实例级 Bridge pipe。
- 新增 `vs_list_instances` 与 `vs_find_instances`；查找支持实例 ID、PID、solution 名称和完整路径。
- 只有一个活动实例时，实例绑定工具允许省略 `vsInstanceId`；存在多个实例时必须显式指定，禁止依赖 MCP transport session 保存默认实例。
- 最后一个 VS 实例注销后，Host 等待 30 秒并再次确认无实例，再优雅退出。
- VSIX 配置页首版提供启用、自动启动和日志级别设置，并显示 Host、pipe URL、实例列表和连接诊断；不允许修改 Host 路径、pipe 名或 ACL。

## Provider 分层

### Function Providers

- `BuildFunctions`
  - `vs_run_build`
  - `vs_get_build_status`
  - `vs_get_errors`
- `ProjectFunctions`
  - `vs_get_projects_in_solution`
  - `vs_get_files_in_project`
- `OutputFunctions`
  - `vs_get_output_window_logs`
  - `vs_get_build_output`
- `ContextFunctions`
  - `vs_read_file`
  - `vs_file_search`
- `TestExplorerFunctions`
  - `vs_get_tests`
  - `vs_run_tests`
  - 测试调试后置
- `DebuggerFunctions`
  - `vs_debugger_get_info`
  - `vs_debugger_evaluate_expr`
  - `vs_debugger_evaluate_expressions`
  - `vs_debugger_get_threads`
  - `vs_debugger_get_call_stack`
  - `vs_debugger_set_breakpoints`
- `FileEditFunctions`
  - `vs_create_patch`
  - `vs_apply_patch`
  - `vs_edit_file`
  - `vs_edit_files`
  - 高风险，后置

### Context Providers

- `DocumentContextProvider`
- `OutputContextProvider`
- `DebuggerContextProvider`
- `SymbolContextProvider`
- `DiagnosticContextProvider`
- `WorkspaceContextProvider`
- `DiagnosticsContextProvider`
  - 聚合 build errors、runtime exceptions、debug stopped reason、output logs。

### Safety / Policy

- `ToolAvailabilityPolicy`
- `DangerousOperationConfirmation`
- `AuditLog`
- `SensitiveValueRedaction`
- `SessionOwnership`

## 初版 MCP 工具清单

### Project / Build

- `vs_get_projects_in_solution`
- `vs_get_files_in_project`
- `vs_run_build`
- `vs_get_errors`
- `vs_get_build_status`

### Output / Context

- `vs_get_output_window_logs`
- `vs_read_file`
- `vs_file_search`

### Test

- `vs_get_tests`
- `vs_run_tests`

### Debugger

- `vs_debugger_get_info`
- `vs_debugger_set_breakpoints`
- `vs_debugger_get_threads`
- `vs_debugger_get_call_stack`
- `vs_debugger_evaluate_expr`
- `vs_debugger_evaluate_expressions`
- `vs_debugger_continue`
- `vs_debugger_pause`
- `vs_debugger_step_over`
- `vs_debugger_step_into`
- `vs_debugger_step_out`

### File Edit，高风险后置

- `vs_create_patch`
- `vs_apply_patch`
- `vs_edit_file`
- `vs_edit_files`
- `vs_remove_file`

## 分阶段路线图

### Phase 0：协议与桥接壳验证

目标：证明 MCP Host 能和 VSIX Bridge 通信。

状态：**已完成**。

任务：

1. 创建 OOP MCP Host，暴露 `vs_capabilities` 和 health/version 工具。
2. 创建 VSIX Bridge 空壳，验证 VS 启动、扩展加载、IPC 连接、生命周期关闭。
3. 加入工具注册表、能力发现、统一错误模型、审计日志。
4. 定义 capability 返回格式。
5. 定义 bridge IPC 请求/响应格式。

验收：

- 外部 MCP client 能调用 `vs_capabilities`。
- Host 能检测当前 VS 实例。
- VS 关闭时连接能优雅断开。
- Bridge 不阻塞 VS UI 线程。

### Phase 1：最小 IDE 闭环

目标：让 agent 完成“看项目 → 构建 → 看错误 → 读文件/搜索”的闭环。

状态：**进行中**。

任务：

1. ✅ `vs_get_projects_in_solution`
2. ⬜ `vs_get_files_in_project`
3. ✅ `vs_run_build`
4. 🔶 `vs_get_errors`：已实现，Qt/C++ Error Table 在线验收未通过。
5. ✅ `vs_get_output_window_logs`
6. ⬜ `vs_read_file`
7. ⬜ `vs_file_search`

验收：

- Agent 能列出解决方案项目。
- Agent 能触发构建。
- 构建失败后能获取错误列表和输出窗口。
- Agent 能读取相关源码文件。
- 所有只读工具不需要用户确认。

### Phase 2：基础调试闭环

目标：让 agent 能进入基础调试诊断。

任务：

1. `vs_debugger_get_info`
2. `vs_debugger_set_breakpoints`
3. `vs_debugger_launch_project`
4. `vs_debugger_attach_process`
5. `vs_debugger_get_threads`
6. `vs_debugger_get_call_stack`
7. `vs_debugger_evaluate_expr`
8. `vs_debugger_evaluate_expressions`
9. `vs_debugger_continue`
10. `vs_debugger_pause`
11. `vs_debugger_step_over`
12. `vs_debugger_step_into`
13. `vs_debugger_step_out`

验收：

- 能设置断点。
- 能启动或附加调试。
- 程序暂停后能拿线程、调用栈、当前帧。
- 能在当前帧求值表达式。
- 能继续、暂停、单步。
- 表达式求值必须明确是否允许副作用。

### Phase 3：测试与诊断增强

目标：覆盖 Test Explorer 和高级诊断。

任务：

1. `vs_get_tests`
2. `vs_run_tests`
3. 测试结果/失败详情。
4. `vs_debug_test_by_id`
5. 异常分析。
6. 并行栈摘要。
7. 输出窗口 + 错误列表 + 调试状态聚合。

特别约束：

- 测试调试必须走专用接口。
- 不允许用普通 `launch` 调试 Test Explorer 测试。

### Phase 4：高风险写操作和生态集成

目标：补齐代码修改和 MCP 生态体验。

任务：

1. `vs_create_patch`
2. `vs_apply_patch`
3. `vs_edit_file`
4. `vs_edit_files`
5. `vs_remove_file`
6. `vs_run_command_in_terminal`
7. VS 内置 `mcp.json` 发布方式研究。
8. Windows MCP Registry 机制研究。

默认策略：

- `apply_patch`、`edit_file`、`remove_file`、`run_command_in_terminal` 默认需要确认。
- 远程访问不进入第一版。

## 安全边界

P0 就要设计：

- 仅本机访问。
- 默认 `Named Pipe`。
- localhost 模式必须 token。
- 工具按风险分级：
  - read-only
  - state-changing
  - dangerous
- `evaluate` 默认标注可能有副作用。
- `attach` 需要确认。
- 文件写入需要确认。
- 命令执行默认禁用。
- 审计日志记录工具调用。
- 不记录 token、环境变量、完整日志敏感内容。
- 多 client 同时连接时，调试会话需要 session ownership 或互斥锁。

## 状态与 Handle 模型

MCP 2026 新规范弱化 transport session，因此 VS 调试状态必须显式管理：

- `vsInstanceId`
- `debugSessionId`
- `threadId`
- `frameId`
- `buildTaskId`
- `testRunId`

原则：

- 不依赖底层连接保存调试状态。
- 长任务返回 task/run handle。
- 后续调用显式传入 handle。
- handle 需要生命周期和过期策略。

## 主要风险

1. 调试器深层 API 可用性仍需 POC 验证。
2. Test Explorer 编程 API 需要专项验证。
3. VSSDK in-proc 代码容易影响 VS 稳定性。
4. 表达式求值可能修改程序状态。
5. 多 agent 控制同一 VS 实例会冲突。
6. MCP 新规范弱化 transport session，调试状态必须用显式 handle 管理。
7. VS 2026 / VS 18.x 文档仍在变化，可能有版本锁定风险。
8. Windows MCP Registry 机制当前不可作为 P0 依赖。

## 决策

- 采用 Hybrid 主线，不选纯 VSSDK 内嵌 server，也不选纯外部 DTE 自动化。
- P0 优先复刻 VS Copilot 日志中已证实的函数族：project/build/errors/output/read/search/test，再做 debugger POC。
- 命名优先贴近 VS Copilot 内部函数，但对外加 `vs_` 前缀避免冲突。
- 原始日志不提交仓库，只保留脱敏分析文档。
- 第一阶段不开放远程访问。
- 第一阶段不默认启用高风险写操作。

## 已落盘参考资料

- [visual-studio-2026-mcp-research.md](visual-studio-2026-mcp-research.md)
- [technical-route-comparison.md](technical-route-comparison.md)
- [vs-copilot-log-analysis.md](vs-copilot-log-analysis.md)
- [vs-copilot-debugger-log-analysis.md](vs-copilot-debugger-log-analysis.md)
- [vs2026_copilot.md](vs2026_copilot.md)

## 下一步

1. 由用户运行最新 managed tests，覆盖新增的 Build Output DTO、Bridge RPC 和 MCP tool schema。
2. 将 `vs_get_errors` 的空集合误判收紧为稳定的 `diagnostics_unavailable`，继续研究 Qt/C++ Error List 的公开数据源，不使用输出解析 fallback。
3. 继续 Phase 1 低风险工具，优先顺序：
  - `vs_get_files_in_project`
  - `vs_read_file`
  - `vs_file_search`
4. 为 Build Output 增加可选的 build handle/起始偏移关联，避免当前窗格包含多次历史构建内容。
5. Phase 1 最小闭环完成后，再进入 Debugger POC。
