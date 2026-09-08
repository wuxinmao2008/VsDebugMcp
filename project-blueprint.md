# Visual Studio 2026 MCP 项目整体蓝图

## 当前共识

- 目标：参考 Qt Creator MCP 插件，为 Visual Studio 2026 / VS 18.x 提供类似能力，将 IDE 的构建、调试、测试、输出、项目/文件和代码搜索能力通过 MCP 暴露给外部 agent。
- 阶段：Phase 0、Phase 1（最小 IDE 上下文与构建闭环）及 Phase 2 Debugger POC（只读调试观测原型）已全部完成开发并完成全链路在线实测验收。
- 核心设计原则：**不重复提供 Agent 宿主已有的通用能力**（通用全盘搜索、通用读盘、修改文件行等交由 Agent 原生处理），集中提供 Visual Studio 独有的 IDE 上下文（项目工程树、构建生命周期、构建日志输出、调试器状态诊断）。
- 推荐主线：`Hybrid：OOP MCP Host + VSIX/VSSDK Bridge`。
- 能力范围：构建/编译、断点设置、调试状态/调用栈/表达式求值诊断、输出窗口、项目工程与文件树。
- 运行边界：仅本机使用；VS Code 到共享 Host 使用固定 `http://127.0.0.1:43260` Streamable HTTP，Host 到 VSIX Bridge 使用当前用户 ACL 保护的实例级 Named Pipe RPC，不开放外部网卡或远程访问。
- 部署形态：发布 win-x64 框架依赖（Framework-Dependent）Host，优先复用 Visual Studio 2026 内置的 .NET 8 运行时（或系统 .NET 8 运行时），随 VSIX 安装并由 VSIX 自动拉起，VSIX 包体积 ~4 MB。
- 多实例：同一 Windows 用户共享一个 Host；每个 Visual Studio 实例拥有独立 Bridge pipe，通过显式 `vsInstanceId` 路由。

## 实施进度（2026-09-08 更新）

### Phase 4C：高级调试控制深化已完成开发 (v0.1.14.0)

- **线程精细化挂起与解冻 (`vs_debugger_freeze_thread` / `vs_debugger_thaw_thread`)**：
  - 基于 Visual Studio COM 原生 `EnvDTE.Thread.Freeze()` 与 `Thaw()`，支持按 `threadId` 挂起或恢复指定线程；
  - 解决多线程调试时无法重现或隔离竞态条件的痛点；
  - 在 `ThreadInfo` 与 `vs_debugger_get_threads` 中新增 `isFrozen` 属性回显。
- **设置下一语句 / 跳转执行点 (`vs_debugger_set_next_statement`)**：
  - 联动前台编辑器光标定位与 `EnvDTE.Debugger.SetNextStatement()`，在调试中断期间动态调整程序计数器（Instruction Pointer）；
  - 支持跳过崩溃代码行或重新执行上一语句；无缝享受 Phase 4B 的工作目录路径路由；
  - 成功调整后返回更新后的顶层堆栈栈帧（`topFrame`）。
- **健壮性防卫与错误映射**：
  - 新增 `thread_not_found`、`invalid_next_statement` 结构化错误码；
  - 严格拦截非调试/非中断态调用（`debugger_not_paused` / `debugger_not_debugging`）。
- **自动化测试验证**：
  - 单元测试：`VsDebugMcp.Protocol.Tests` (13/13 PASS) + `VsDebugMcp.Host.Tests` (105/105 PASS)，全套 118 个单元测试 100% 通过；
  - Bridge capability 注册总数扩充至 **38 个**。

### Phase 4B：工程防御加固与多实例智能路由已完成开发并通过全链路在线实测验收 (v0.1.13.0)

- **多实例工作目录智能路由 (`FindByWorkingDirectory`)**：
  - 扩展 `VisualStudioInstanceRegistry.Resolve(string? vsInstanceId, string? targetPath = null)`，支持将请求的目标文件/目录路径与各 VS 实例报告的解决方案物理路径及目录进行包含与前缀匹配；
  - 在省略 `vsInstanceId` 时自动路由到匹配的 Visual Studio 实例（`vs_get_files_in_project`, `vs_get_errors`, `vs_debugger_set_breakpoints`, `vs_navigate_to`），彻底消除多开 VS 时的 `ambiguous_instance` 阻断；
  - 增强 `vs_find_instances` 查询能力，支持按解决方案目录模糊匹配。
- **构建与调试互斥防死锁守卫 (`debugger_running_cannot_build`)**：
  - 在 `SolutionBuildProvider` 构建生命周期前置感知调试器模式（`dte.Debugger.CurrentMode != dbgDesignMode`）；
  - 若在 F5 调试运行（`dbgRunMode`）或断点中断（`dbgBreakMode`）期间收到 `vs_run_build` 请求，立即拒绝并返回结构化错误码 `debugger_running_cannot_build`（`Retryable = false`），规避 VS 原生阻塞式模态对话框引发 UI 挂起与 IPC 死锁。
- **Windows Job Object 进程生命周期内核兜底**：
  - 在 `SharedHostProcessManager` 中引入原生 P/Invoke Windows Job Object，配置 `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` 限制；
  - 在拉起 `VsDebugMcp.Host` 进程后将其挂载至 VS 进程所属 Job Object，确保即使 Visual Studio 强杀或崩溃，操作系统内核均保证回收 Host 孤儿进程，避免端口 `43260` 冲突。
- **自动化测试验证**：
  - 单元测试：`VsDebugMcp.Protocol.Tests` (12/12 PASS) + `VsDebugMcp.Host.Tests` (103/103 PASS)，全套 115 个单元测试 100% 通过。

### Phase 4A：活动上下文与编辑器协同导航已完成开发并通过全链路在线实测验收 (v0.1.12.0)

- **`vs_get_active_document` 活动文档与光标选区探测**：
  - 基于 `EnvDTE.DTE.ActiveDocument` 获取当前前台编辑器激活文档；
  - 提取文件物理全路径、文件名、是否未保存（脏状态）、只读标记、语言类型及总行数；
  - 提取当前光标 1-based 行号与列号；
  - 提取选中区域文本（限制最大 10,000 字符安全截断）及选区起止行列范围；
  - 优雅防卫：当前无打开文档或焦点不在代码编辑器时安全返回 `hasActiveDocument: false`，杜绝异常崩溃。
- **`vs_navigate_to` 编辑器前台打开与行列精准定位**：
  - 支持传入物理绝对路径或相对于解决方案目录的相对路径；
  - 自动校验文件物理存在性（`file_not_found`）；
  - 调用 `dte.ItemOperations.OpenFile` 并前台激活（`Activate()`）窗口；
  - 调度 `TextSelection.GotoLine` 与 `MoveToDisplayColumn` 将光标精准平滑停靠在目标行列。
- **`vs_get_solution_configurations` 解决方案构建配置与平台组合查询**：
  - 遍历 `dte.Solution.SolutionBuild.SolutionConfigurations` 提取方案中所有构建配置与平台组合（如 `Debug|x64`, `Release|ARM64` 等）；
  - 识别并标注当前激活的配置（`isActive: true`）；
  - 方案未打开时安全返回 `solution_not_open`。
- **自动化测试验证**：
  - 单元测试：`VsDebugMcp.Protocol.Tests` (12/12 PASS) + `VsDebugMcp.Host.Tests` (101/101 PASS)，全套 113 个单元测试 100% 通过；
  - Bridge capability 注册总数升至 **35 个**（MCP 工具总数 37 个）。

### Phase 3C：进程附加与模块符号诊断闭环已完成开发并通过全链路在线实测验收 (v0.1.11.0)

- **`vs_debugger_get_processes` 本地进程发现与过滤**：
  - 基于 `EnvDTE.Debugger.LocalProcesses` 与 `DebuggedProcesses` 实现运行中进程遍历；
  - 自动向下转型为 `EnvDTE80.Process2` 提取 `UserName`、`IsBeingDebugged` 与 `TransportQualifier`；
  - 支持进程名模糊/子串匹配（忽略大小写）、PID 精确查询与仅看被调试进程过滤。
- **`vs_debugger_attach_process` 附加到进程与智能着陆**：
  - 支持按 PID 或进程名锁定系统目标进程；
  - 接入 `_executionLock` 互斥保护，调用 `proc.Attach()` 调度至 UI 线程；
  - 支持 `waitForBreak` 中断等待探测与顶层栈帧即时回显。
- **`vs_debugger_detach` 安全无害分离**：
  - 区别于 `stop`（杀死目标进程），`detach` 解除调试器挂钩并恢复至设计模式，目标进程在操作系统中继续存活独立运行。
- **`vs_debugger_get_modules` 加载模块与 PDB 符号诊断**：
  - 基于 `EnvDTE90.Process3.Modules` 遍历已加载模块集合；
  - 提取模块名、物理路径、版本、格式化十六进制加载与结束地址（`0x...`）、PDB 符号加载状态（`symbolsLoaded`）、优化与用户代码标记；
  - 支持模块名过滤与 `userCodeOnly` 过滤。
- **自动化测试与全链路在线实测验证**：
  - 单元测试：`VsDebugMcp.Protocol.Tests` (11/11 PASS) + `VsDebugMcp.Host.Tests` (90/90 PASS)，全套 101 个单元测试 100% 通过；
  - 在 VS 18.x 实验实例通过 `scripts/test_acceptance_phase3c.py` 验证外部常驻靶标进程启动、PID 发现、附加调试、模块枚举、暂停排查、安全分离（验证外部进程存活）完整闭环；
  - Bridge capability 注册总数升至 **32 个**（MCP 工具总数 34 个）。

### Phase 3B：核心诊断与高级调试闭环已完成开发并通过全链路在线实测验收 (v0.1.10.0)

- **`vs_get_errors` 深度攻坚与双轨保底**：
  - 重构错误发现机制：第一轨直接查询活动 ErrorList 的 `TableControl.Entries`，规避底层事件订阅静默与超时；第二轨通过 `OutputWindowProvider.ReadPaneOutput` 自动提取 Build 窗格原始日志，内嵌 MSVC / MSBuild 双正则表达式语法分析器（`MsvcOrClangRegex` 与 `MsBuildGeneralRegex`），将编译报错与警告即时结构化转译为 `VisualStudioDiagnostic` 项；
  - 彻底终结 `diagnostics_unavailable` 报错，确保外部 Agent 编译失败时总能稳定获得行号、列号、错误码与描述。
- **`vs_debugger_get_exception_info` 异常现场深度诊断**：
  - 新增调试中断期异常信息提取工具：通过在 UI 线程求值 `$exception` 伪变量，提取 `exceptionType`（全限定 CLR 类名）、`message`、十六进制 `hresult`、`source` 模块名、原始 `stackTrace`、`innerException` 与 `rawDetails`；
  - 严格模式防卫：非中断模式拦截返回 `debugger_not_paused`；
  - 包含非托管/C++ SEH 异常的兜底分析。
- **`vs_debugger_set_breakpoints` 高级断点控制增强**：
  - 扩展输入参数：支持条件评估模式 `conditionType`（`whenTrue` / `whenChanged`）、目标命中次数 `hitCountTarget`（int）与命中条件模式 `hitCountType`（`equal` / `greaterOrEqual` / `multiple`）；
  - 将原生设置同步至 `EnvDTE.Breakpoint` 并实时回显 `currentHitCount`。
- **自动化测试与全链路在线实测验证**：
  - 单元测试：`VsDebugMcp.Protocol.Tests` (10/10 PASS) + `VsDebugMcp.Host.Tests` (68/68 PASS)，全套 78 个单元测试 100% 通过；
  - 在 VS 18.x 实验实例通过 `scripts/test_acceptance_phase3b.py` 验证通过健康检查、双轨错误提取、条件/命中计数断点设置、单测除零异常命中停靠、异常现场栈帧提取（`DivideByZeroException`）以及调试停止恢复完整闭环；
  - Bridge capability 注册总数升至 **28 个**（MCP 工具总数 30 个）。


### Phase 3A：测试资源管理器与测试联动调试 (v0.1.9.0)

- **Test Explorer / VSTest 混合链路集成**：
  - 基于 MEF `ITestsService`（反射 Invoker 封装 internal 访问）与 `IOperationState` 事件监听，实现轻量免引用的测试发现与执行；
  - 4 个核心 MCP 工具全部落地：
    - `vs_get_tests`：按项目/FullyQualifiedName/DisplayName 过滤发现方案单元测试；
    - `vs_run_tests`：异步调度指定 testId 或全方案测试，返回显式 `testRunId`；
    - `vs_get_test_run_status`：查询测试执行状态（`running`/`completed`/`cancelled`/`failed`）、执行时长、通过/失败/跳过计数及各项详细报错与堆栈；
    - `vs_cancel_test_run`：即时取消正在运行的测试；
  - 单测试运行防卫互斥（`test_run_busy`）与容错状态回退。
- **靶场与底层核心问题攻关**：
  - 新建真实测试靶场 `sample/SampleTests`（xUnit .NET 8，3 个测试样例），并入 `SampleSolution.sln` 与 `SampleSolution.slnx`；
  - **核心机制探明**：通过逆向分析 `Microsoft.VisualStudio.TestWindow.Core.dll`，探明 TestWindow 的 `OperationBroker.storeOpenTaskSource` 必须在 Test Explorer 工具窗口就绪后才会放行测试执行队列；在 `EnsureServicesAsync` 中引入 `IVsUIShell.FindToolWindow`（GUID: `E1B7D1F8-9B3C-49B1-8F4F-BFC63A88835D`）程序化唤醒底层管道；
  - **Host 启动与注册鲁棒性增强**：`HostOptions.InitialRegistrationTimeout` 提升至 60 秒，`SharedHostProcessManager` 等待重试增至 60 次，`HostRegistrationManager` 增加 5 次重试退避循环，彻底消除冷启动偶发超时。
- **自动化测试与实测进展**：
  - 单元测试全量通过：`VsDebugMcp.Protocol.Tests` (9/9 PASS) + `VsDebugMcp.Host.Tests` (59/59 PASS)；
  - 在 VS 18.9 实验实例完成 `vs_health`、`vs_capabilities`、`vs_get_tests`（精确发现 3 个测试）、过滤搜索及 `vs_cancel_test_run` 在线实测。

### Phase 2C 调试器启动与诊断增强已完成开发并通过全链路在线实测验收 (v0.1.7.0)

- **启动与批量诊断能力**：
  - `vs_debugger_start`：在设计模式下程序化触发 F5 启动调试，支持智能着陆探测（`waitForBreak`）并即时回显断点栈帧；
  - `vs_debugger_evaluate_expressions`：单次 RPC 批量求值多个表达式，具备单项错误隔离能力，消除反复网络往返延迟；
  - `vs_debugger_get_locals`：基于 `StackFrame.Locals` 与 `Arguments` 原生 COM 集合，自动列出活动栈帧的形参与局部变量名值清单；
  - 严格模式防卫：调试运行中禁止重复调用启动（`debugger_already_running`）。
- **工程与部署优化**：
  - 修复 VSIX .NET Framework 兼容性问题，改用 `Stopwatch.StartNew()` 计时；
  - 新增 `scripts/deploy-exp.ps1` 专用部署工具，保障实验实例文件覆盖与时间戳刷新。
- **自动化测试与实测验收**：
  - `VsDebugMcp.Protocol.Tests` (8/8 PASS) + `VsDebugMcp.Host.Tests` (47/47 PASS)，全套 55 个单元测试 100% 通过；
  - 在 VS 18.9 实验实例完成全链路闭环实测（0 手工按键启动调试、断点停靠、变量巡检、单步控制、批量求值与平稳退出）。

### Phase 2B 调试器执行控制闭环已完成开发并在线验收 (v0.1.6.0)

- **控制能力与并发防卫**：
  - 基于 `EnvDTE.Debugger` 实现完整的执行控制，调度至 Visual Studio 主 UI 线程；
  - 引入 `_executionLock` 互斥锁，并发冲突时快速返回结构化错误码 `debugger_busy`，杜绝 VS COM 重入崩溃；
  - 严格模式防卫：非暂停状态调用 `step_*` 拦截返回 `debugger_not_paused`；设计模式调用 `pause`/`stop` 安全拦截；
  - 单步执行与暂停成功后统一返回 `DebuggerExecutionResponse`，自动捕获并返回着陆顶层栈帧（`topFrame`），实现即时单步反馈。
- **6 个核心执行控制 MCP 工具落地**：
  - `vs_debugger_step_over`：单步步过；
  - `vs_debugger_step_into`：单步步入；
  - `vs_debugger_step_out`：单步步出；
  - `vs_debugger_continue`：继续执行（默认不阻塞等待）；
  - `vs_debugger_pause`：暂停运行中的调试目标；
  - `vs_debugger_stop`：终止当前调试会话，安全回到设计模式。
- **自动化测试**：
  - `VsDebugMcp.Protocol.Tests` (7/7 PASS) + `VsDebugMcp.Host.Tests` (37/37 PASS)，全套 44 个单元测试 100% 通过。

### Phase 2 Debugger POC（只读调试观测原型验证）已完成并在线验收 (v0.1.5.0)

- **能力与模式防卫**：
  - 基于 `EnvDTE.Debugger` 实现只读调试现场诊断，前置判定 `dbgDesignMode`、`dbgRunMode` 与 `dbgBreakMode`；
  - 非中断模式下读取调用栈或求值严格拦截并返回结构化错误码 `debugger_not_paused`，杜绝 COM 崩溃；
  - 所有调试器交互通过 `ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync` 调度至 Visual Studio 主 UI 线程；
  - 表达式求值限制 2000ms 超时并支持指定 `frameIndex` 进行跨栈帧上卷求值。
- **4 个核心调试 MCP 工具全部落地并通过在线实测**：
  - `vs_debugger_get_info`：在线验证设计模式（`design`）与断点中断模式（`break`），准确报告调试器模式、活动 PID/TID、断点数与中断原因；
  - `vs_debugger_set_breakpoints`：在线验证在源码物理路径与行号打断点（`sample/SampleApp/Services/Calculator.cs:5`），成功被 VS 采纳（`isBound=true`，断点数增至 1）；
  - `vs_debugger_get_call_stack`：在线验证在中断现场准确捕获线程调用栈帧（Frame 0: `Calculator.Add`，Frame 1: `Program.<Main>$`）；
  - `vs_debugger_evaluate_expr`：在线验证当前栈帧局部形参求值（`a=10`、`b=20`、`a+b=30`）以及调用者栈帧对象属性求值（`item.Name="Widget"`、`item.Price=9.99`）。
- **自动化测试**：
  - `VsDebugMcp.Protocol.Tests` (6/6 PASS) + `VsDebugMcp.Host.Tests` (25/25 PASS)，全套 31 个单元测试 100% 通过。

### Phase 1 最小 IDE 闭环与样本方案已完成并在线验收 (v0.1.4.0)

- **`vs_get_files_in_project`**：
  - 基于 `IVsHierarchy` + `IVsProject` 原生 COM 实现，高效遍历项目文件树；
  - 提取物理路径、相对根路径、C++ 筛选器（Filters）及后缀，自动过滤 `External Dependencies` 外部依赖干扰项；
  - 在 VS 18.9 实验实例上完成全案 5 个文件、单工程过滤与扩展名过滤实测。
- **开箱即用测试方案 (`sample/`)**：
  - 建立 `sample/SampleSolution.sln` / `.slnx`，包含 `SampleApp`（.NET 8 Console）与 `SampleLib`（.NET 8 ClassLib）。

- Host 已改为仅监听 IPv4 loopback `127.0.0.1:43260` 的无状态 Streamable HTTP MCP 服务。
- 已删除 MCP stdio、MCP Named Pipe HTTP、手工 smoke 启动入口和对应 VS Code 任务。
- 已增加当前用户 Host 单例、Host 控制 pipe、实例注册表、`vs_list_instances`、`vs_find_instances` 和按 `vsInstanceId` 路由。
- 每个 VS 实例使用 PID + process start time 派生会话 ID，并监听独立 Bridge pipe。
- VSIX 已实现 Host 探测、自动启动、注册、每 5 秒心跳和有界注销；15 秒无心跳清理僵尸实例。
- 最后一个实例注销或超时清理后 Host 立即退出。
- Host 已按 `win-x64` self-contained 发布并包含在 VSIX 的 `Host/` 目录。
- managed 与 VSIX Debug package 已构建通过；生成的 VSIX 已确认包含 `VsDebugMcp.Host.exe`。
- 首轮在线验收确认 VSIX 从 Experimental Instance 安装目录自动启动 Host，Host 监听 `127.0.0.1:43260`，MCP `tools/list` 返回 10 个工具。
- `vs_list_instances`、`vs_find_instances`、`vs_health`、`vs_capabilities`、单实例默认路由、显式 `vsInstanceId` 路由及 `instance_not_found` 已在线通过。
- 在线实例为 VS 18.9 Experimental Instance，solution 为 `OvalPrintSrv.sln`；`vs_get_projects_in_solution` 返回 2 个项目项。
- `Debug|x64` 构建在线返回 task handle，状态达到 `failed`，Build Output 成功返回 500 字符尾部并包含 MSVC `C3861` 与构建汇总。
- `vs_get_errors` 继续稳定返回 `diagnostics_unavailable`，与当前 Error Table 公共数据源限制一致。
- 最新部署已复验 MCP schema：`vs_run_build` 的 `configuration/platform/vsInstanceId` 以及 build status/cancel 的 `vsInstanceId` 均不再出现在 `required` 数组中。
- 省略全部可选参数调用 `vs_run_build` 已在线成功，自动使用唯一实例和活动 `Debug|x64`；省略 `vsInstanceId` 的 status/cancel 路由均通过。
- 构建取消在线状态为 `running → cancelling → cancelled`，`cancelRequested=true`；无效 build handle 稳定返回 `build_task_not_found`。

### 已完成并在线验收

- Phase 0A：Console Host ↔ Named Pipe ↔ VSIX Bridge。
- Phase 0B：标准 MCP Host 与工具发现，基于官方 C# MCP SDK。
- `vs_health`
- `vs_capabilities`
- `vs_get_projects_in_solution`
- `vs_run_build`
- `vs_get_build_status`
- `vs_cancel_build`
- `vs_get_output_window_logs`

上一次已完成在线验收的链路：

```text
MCP Client
  -> VsDebugMcp.Host (.NET 8)
  -> Named Pipe
  -> VSIX Bridge
  -> Visual Studio 18.9 Experimental Instance
```

当前已完成首轮在线验收的目标链路：

```text
VS Code / MCP Client
  -> Streamable HTTP at 127.0.0.1:43260
  -> shared VsDebugMcp.Host (OOP / win-x64 Framework-Dependent)
  -> instance registry and vsInstanceId routing
  -> per-instance Named Pipe RPC
  -> Visual Studio VSIX Bridge
  -> Visual Studio 18.x instance
```

VS Code 使用固定 URL，不启动 Host，也不需要知道 Host 安装路径。

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
3. `vs_get_files_in_project`
4. `vs_run_build`
5. `vs_get_build_status`
6. `vs_cancel_build`
7. `vs_get_errors`
8. `vs_get_output_window_logs`
9. `vs_debugger_get_info`
10. `vs_debugger_set_breakpoints`
11. `vs_debugger_get_call_stack`
12. `vs_debugger_evaluate_expr`
13. `vs_debugger_step_over`
14. `vs_debugger_step_into`
15. `vs_debugger_step_out`
16. `vs_debugger_continue`
17. `vs_debugger_pause`
18. `vs_debugger_stop`
19. `vs_debugger_start`
20. `vs_debugger_evaluate_expressions`
21. `vs_debugger_get_locals`
22. `vs_get_tests`
23. `vs_run_tests`
24. `vs_get_test_run_status`
25. `vs_cancel_test_run`
26. `vs_debug_test_by_id`
27. `vs_debugger_get_threads`
28. `vs_debugger_get_exception_info`
29. `vs_debugger_get_processes`
30. `vs_debugger_attach_process`
31. `vs_debugger_detach`
32. `vs_debugger_get_modules`
33. `vs_get_active_document`
34. `vs_navigate_to`
35. `vs_get_solution_configurations`

`vs_health` 作为 MCP tool 提供，但不重复列入 Bridge capability 数组。

### 自动化和部署状态

- Protocol tests 最近一次完整运行：`12/12` PASS。
- Host tests 最近一次完整运行：`101/101` PASS。
- 全套测试通过率：`113/113` PASS (100%)。
- 当前扩展与 Host 版本号：`0.1.12.0`。
- VSIX 已使用 VS 18 MSBuild 成功编译与打包。
- 部署流水线已完善：
  - `build: vsix`：编译 VSIX 包；
  - `deploy: vsix`：依赖 `build: vsix`，调用 `scripts/deploy-exp.ps1` 校验实验实例与 Host 状态并完成安全覆盖部署。
- 已在 Visual Studio 2026 (VS 18.x) 实验实例完成全链路实测验收（含工程、构建、高级调试、测试资源管理器及 Phase 3A 单测联动调试与多线程巡检）。

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
| MCP Client Transport | Streamable HTTP over IPv4 loopback | VS Code 只需固定 `http://127.0.0.1:43260`；不需要 command、Host 路径或动态端口发现。 |
| Host ↔ VSIX IPC | 每实例独立 Named Pipe RPC | 保留现有协议与进程隔离，同时支持多个 VS 实例。 |
| Host 部署 | VSIX 内置 `win-x64` self-contained | 用户无需单独安装 Host 或 .NET Runtime。 |
| VSIX 项目形态 | SDK-style VSIX | 适配 VS 18.x，项目结构更现代。 |
| 安全模型 | 工具分级 + 确认 + 审计 + 脱敏 | 覆盖表达式求值、attach、写文件、命令执行等风险。 |

## 总体架构

```text
MCP Client / Agent
  └─ Streamable HTTP at 127.0.0.1:43260
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
- VSIX 每 5 秒发送心跳；Host 在 15 秒无心跳后移除僵尸实例。
- 最后一个 VS 实例注销或超时清理后，Host 立即优雅退出；下次 VSIX 加载时重新启动。
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

目标：让 agent 完成“看项目结构/包含文件 → 构建 → 诊断输出”的 VS 独有上下文闭环。

设计原则（2026-09-05 共识）：
- **不重复造轮子**：通用全盘搜索（ripgrep）、常规磁盘文件读取、文件修改/打补丁均由 Agent 宿主（VS Code / Cursor / Claude 等）原生提供，VsDebugMcp 专注于提供 Visual Studio 独有的 IDE 上下文。
- 原路线图中的通用读盘（`vs_read_file`）、全盘搜索（`vs_file_search`）和文件编辑（`vs_edit_file` / `vs_create_patch`）予以裁剪，移出默认 MCP 工具面。

状态：**代码实现完成，待在线部署验收**。

任务：

1. ✅ `vs_get_projects_in_solution`
2. ✅ `vs_get_files_in_project`（基于 `IVsHierarchy` + `IVsProject` 原生 COM 遍历，支持 Filters 分类和后缀筛选）
3. ✅ `vs_run_build`
4. 🔶 `vs_get_errors`：已实现，Qt/C++ Error Table 在线验收未通过，公开 API 无法提供时稳定返回 `diagnostics_unavailable`。
5. ✅ `vs_get_output_window_logs`

验收：

- Agent 能列出解决方案项目。
- Agent 能查询指定项目或全部项目的源码/头文件清单（含相对路径与 C++ 筛选器结构）。
- Agent 能触发构建并追踪进度。
- 构建失败后能获取 Build Output 原始输出窗口文本。
- 所有只读工具不需要用户确认。

### Phase 2：基础调试与全生命周期闭环

目标：让 agent 能进入基础调试诊断、现场分析与单步执行控制。

状态：**已完成全部代码实现并通过全链路在线验收（v0.1.7.0）**。

POC 与控制核心原则：
- **只读观测先行**：优先提供状态探测（`get_info`）、断点管理（`set_breakpoints`）、中断现场栈帧提取（`get_call_stack`）与表达式探针（`evaluate_expr`）。
- **模式防卫（Mode Guard）**：未暂停时严格拦截并返回结构化错误码 `debugger_not_paused`；已调试时拦截重复启动并返回 `debugger_already_running`。
- **超时保护**：表达式求值设置有界超时（默认 2000ms），防止死循环或耗时 getter 冻结 VS UI。

已完成任务：

1. ✅ `vs_debugger_get_info`
2. ✅ `vs_debugger_set_breakpoints`
3. ✅ `vs_debugger_get_call_stack`
4. ✅ `vs_debugger_evaluate_expr`
5. ✅ `vs_debugger_start`（F5 原生启动，支持 `waitForBreak` 着陆）
6. ✅ `vs_debugger_step_over`
7. ✅ `vs_debugger_step_into`
8. ✅ `vs_debugger_step_out`
9. ✅ `vs_debugger_continue`
10. ✅ `vs_debugger_pause`
11. ✅ `vs_debugger_stop`
12. ✅ `vs_debugger_get_locals`
13. ✅ `vs_debugger_evaluate_expressions`（单次 RPC 批量求值）

验收：

- 能设置与清空断点。
- 能程序化拉起调试（F5）。
- 程序暂停后能提取线程、调用栈与当前栈帧局部变量。
- 能在当前帧安全求值单项或批量表达式。
- 能继续、暂停、单步步过/步入/步出，并安全返回设计模式。

### Phase 3：测试与调试联动（Test-Driven Debugging）

目标：覆盖 Test Explorer、单测联动调试与高级现场诊断。

状态：**方向 B（测试资源管理器集成 v0.1.8.0）与 Phase 3A（测试驱动调试与多线程巡检 v0.1.9.0）均已完成代码实现并全链路在线实测验收**。

已完成任务：

1. ✅ `vs_get_tests`：基于 MEF `ITestsService` 查询测试清单，支持显示名与 FQN 模糊过滤。
2. ✅ `vs_run_tests`：通过 `OperationBroker`（全量 `ExecuteAllTestsAsync`，单用例通过 `Microsoft.VisualStudio.TestWindow.Internal.Messages.SearchQuery` 过滤 `ExecuteTestsByFilterAsync`）异步触发测试运行。
3. ✅ `vs_get_test_run_status`：轮询异步测试运行生命周期（`starting → running → completed/failed/cancelled`），返回通过/失败统计、总耗时、以及各项单测的独立测试结果与执行耗时。
4. ✅ `vs_cancel_test_run`：通过 `OperationBroker.CancelAsync` 取消正在运行的测试任务。
5. ✅ `vs_debug_test_by_id`：基于 `OperationBroker.DebugTestsByFilterAsync` 异步发起指定单测调试，支持 `waitForBreak` 探测循环，命中时毫秒级捕获顶层栈帧 `topFrame` 与 `lastBreakReason`。
6. ✅ `vs_debugger_get_threads`：通过 `EnvDTE.Debugger` 提取调试目标多线程诊断快照，标明 `isCurrent`、线程名及状态。
7. ✅ 单实例并发互斥守卫（`test_run_busy`）、调试冲突守卫（`debugger_already_running`）与容错状态机。
8. ✅ 靶场单测工程：`sample/SampleTests`（xUnit .NET 8，包含 3 个单测，已挂载至 `SampleSolution.slnx`）。

后续进阶任务（Phase 3C）：

9. ⬜ 异常分析与高级调用栈摘要
10. ⬜ 错误列表（Error List）原生 COM 数据源深化
11. ⬜ 进程附加（Attach to Process）与高级条件/命中断点

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

## 下一步规划：Phase 4B 工程防御加固与多实例智能路由 (Hardening & Smart Routing)

当前状态：Phase 0、Phase 1、Phase 2 全部调试闭环、Phase 3 (3A/3B/3C) 测试与深层调试闭环、以及 Phase 4A 活动上下文与编辑器协同导航（v0.1.12.0，35 个 Bridge Capabilities / 37 个 MCP Tools）全部开发完成并通过 113 个单元测试 (100%) 与全链路在线实测验收。

下一次迭代确立目标：**Phase 4B 工程防御加固与多实例智能路由**。

### 1. 工作目录智能路由 (Smart Multi-Instance Routing via `FindByWorkingDirectory`)
- **痛点**：当开发者同时打开多个 Visual Studio 解决方案时，外部 Agent 若未显式传递 `vsInstanceId`，Host 会直接报错 `ambiguous_instance`，导致必须多轮往返调用 `vs_find_instances` 确认实例 ID。
- **方案**：
  - 在 `VisualStudioInstanceRegistry` 与 `BridgeRouter` 中引入工作目录/路径前缀匹配算法；
  - 当省略 `vsInstanceId` 且存在多个实例时，Host 优先将请求中的目标路径（如 `filePath`、`project`、或者客户端上下文 WorkingDirectory）与各 VS 实例报告的 `solutionDirectory` 进行不区分大小写的前缀匹配；
  - 若能唯一精准锁定目标实例，自动路由至该实例，实现无感平滑切换；仅在完全无法区分时才回退至 `ambiguous_instance`。

### 2. 构建与调试互斥防御 (Prevent Build While Debugging Deadlock)
- **痛点**：在 F5 调试状态（`dbgRunMode` 或 `dbgBreakMode`）下，Agent 若调用 `vs_run_build`，Visual Studio 底层会弹出阻塞式模态确认对话框（“项目正在运行，是否停止调试并重新生成？”），导致 devenv.exe UI 线程完全挂起，IPC 通道死锁。
- **方案**：
  - 在 `SolutionBuildProvider` 中前置注入调试器状态感知；
  - 在触发构建操作前，检查 `dte.Debugger.CurrentMode != dbgDesignMode`；
  - 若处于调试状态，立即提前拦截并返回结构化错误码 `debugger_running_cannot_build`（`Retryable = false`），明确提示 Agent 先调用 `vs_debugger_stop` 结束调试再行构建，彻底规避模态死锁。

### 3. Windows Job Object 进程生命周期兜底 (OS-Level Orphan Process Cleanup)
- **痛点**：当 Visual Studio 异常崩溃、被任务管理器强制杀死时，VSIX 在前台拉起的 OOP Host 进程可能无法收到正常注销信号，成为孤儿进程并继续占用端口 `43260` 或 Named Pipe，导致下次启动冲突。
- **方案**：
  - 在 `SharedHostProcessManager` 中引入 Windows Job Object 原生 P/Invoke 支持；
  - 配置 `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` 标志位，将 Host 进程加入当前 VS 进程守护的 Job Object 中；
  - 即使 Visual Studio 发生强杀或崩溃，操作系统内核层级会自动级联回收 Host 子进程，保障端口与环境的绝对干净。
