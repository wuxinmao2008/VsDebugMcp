# Visual Studio 2026 工程防御加固与多实例智能路由（Phase 4B）联调与在线验收报告

## 1. 概述与目标

本次迭代完成了 Visual Studio 2026 (VS 18.x) 扩展与 MCP Host 的 **Phase 4B（工程防御加固与多实例智能路由）** 研发与全链路实测。重点攻克了三大长期痛点：
1. **多实例智能路径路由（`FindByWorkingDirectory`）**：解决多开 VS 时未带 `vsInstanceId` 频繁报 `ambiguous_instance` 的痛点，通过自动将请求中的目标路径与各 VS 实例打开的解决方案目录进行前缀/包含匹配，实现智能锁定与平滑调用；
2. **构建与调试互斥防死锁守卫（Build Deadlock Guard）**：解决调试运行/断点中断期间触发构建会导致 VS 弹出底层模态对话框并彻底卡死 UI 线程与 IPC 管道的严重隐患，前置拦截并返回结构化错误码 `debugger_running_cannot_build`；
3. **Windows Job Object 进程生命周期内核兜底（Host Lifecycle Hardening）**：通过 Win32 原生 Job Object (`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`) 将 VSIX 拉起的 OOP MCP Host 进程与 Visual Studio 强力绑定，确保 VS 崩溃或被强杀时操作系统内核 100% 回收 Host，杜绝孤儿进程占用端口与管道。

---

## 2. 核心技术实现细节

1. **智能多实例路径与目录匹配算法**：
   - 在 `VisualStudioInstanceRegistry.Resolve(string? vsInstanceId, string? targetPath = null)` 中引入 `MatchByPath` 校验逻辑；
   - 提取各注册实例的 `SolutionFilePath` 及其目录，与传入的 `targetPath` 执行双向路径前缀与包含匹配；
   - 在 `BridgeService` 的核心文件操作工具（`vs_get_files_in_project`、`vs_get_errors`、`vs_debugger_set_breakpoints`、`vs_navigate_to`）中注入目标路径传递；
   - 同步增强 `vs_find_instances`，支持按解决方案所在文件夹模糊匹配实例。
2. **调试构建前置互斥状态守卫**：
   - 在 `SolutionBuildProvider.RunBuildAsync` 执行实际构建前，切入主线程探测 `dte.Debugger.CurrentMode`；
   - 若当前模式处于运行态（`dbgRunMode`）或断点中断态（`dbgBreakMode`），立即抛出 `SolutionBuildProviderException(BridgeErrorCodes.DebuggerRunningCannotBuild, ...)`；
   - 在 `BridgeErrorCodes`、`BridgeService`、`BridgeServer` 中完整打通统一错误码映射，`Retryable = false`，避免重复盲目重试。
3. **Win32 Job Object 内核级级联回收**：
   - 在 `SharedHostProcessManager` 中引入 P/Invoke `CreateJobObject`、`SetInformationJobObject`（`JobObjectExtendedLimitInformation`）与 `AssignProcessToJobObject`；
   - 设置 `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000`；
   - 在启动 Host 进程后立即分配至 Job Object，生命周期完全受操作系统内核托管。

---

## 3. 端到端在线实测验收（Online Acceptance）

在运行中的 Visual Studio 2026（VS 18.x）实验实例与部署好的 MCP Host 上，运行自动化验收套件 `scripts/test_acceptance_phase4b.py`，全套 5 个实测环节均 100% 通过：

| 验收环节 | 调用的 MCP 工具 / 场景 | 预期结果 | 实测结果 | 状态 |
| :--- | :--- | :--- | :--- | :---: |
| **[1/5] 健康检查与实例校验** | `vs_health` | 状态 ok，成功连接实验实例与 SampleSolution | `status=ok, vsInstanceId=vs-14240-08df0d4a5dc019ed`<br>解决方案：`SampleSolution.slnx` | **PASS** |
| **[2/5] 智能目录检索** | `vs_find_instances(query="sample")` | 按工程所在文件夹名精准匹配出实例 | 成功匹配 1 个实例，准确锁定 `SampleSolution.slnx` | **PASS** |
| **[3/5] 路径智能路由** | `vs_navigate_to(filePath="SampleApp/Program.cs", vsInstanceId=None)` | 省略 `vsInstanceId`，依靠相对路径自动路由 | `success: True, line: 10, column: 1`<br>自动路由至当前方案，无需显式实例 ID | **PASS** |
| **[4/5] 构建防死锁守卫** | `vs_debugger_start` 暂停后调用 `vs_run_build` | 拦截构建，立即返回 `debugger_running_cannot_build`，无弹窗死锁 | 抛出结构化错误：`debugger_running_cannot_build: Cannot build the solution while debugging is in progress. Stop debugging first.`<br>VS UI 未挂起，IPC 管道畅通！ | **PASS** |
| **[5/5] 恢复设计模式构建** | `vs_debugger_stop` 后调用 `vs_run_build` & 轮询状态 | 停止调试回到设计模式后，构建可正常调度并成功完成 | 构建任务 `013bf2e34296469db5960d7a53b94c78` 状态流转最终到达 `succeeded` | **PASS** |

---

## 4. 自动化测试与工程指标

- **`VsDebugMcp.Protocol.Tests`**：12 / 12 PASS (100%)。
- **`VsDebugMcp.Host.Tests`**：103 / 103 PASS (100%)。
  - 覆盖 `BridgeErrorCodes.DebuggerRunningCannotBuild` 映射测试；
  - 覆盖 `ResolveMatchesByWorkingDirectoryWhenMultipleInstancesExist` 多实例工作目录匹配、子路径、父目录与歧义防卫测试。
- **全项目自动化单元测试**：**115 / 115 PASS (100%)**。
- **版本号统一发布**：全组件（`source.extension.vsixmanifest`、各项目 csproj、`CHANGELOG.md`）已统一提升至 **`0.1.13.0`**。
