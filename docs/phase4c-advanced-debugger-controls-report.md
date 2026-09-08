# Visual Studio 2026 高级调试控制深化（Phase 4C）联调与在线验收报告

## 1. 概述与目标

本次迭代完成了 Visual Studio 2026 (VS 18.x) 扩展与 MCP Host 的 **Phase 4C（高级调试控制深化，Advanced Debugger Controls）** 研发与全链路实测。在原有调试断点、单步、调用栈、变量求值与异常探测的基础上，深度赋能外部 Agent 两大工业级底层调试控制能力：
1. **线程精细化挂起与解冻 (`vs_debugger_freeze_thread` / `vs_debugger_thaw_thread`)**：利用 Visual Studio COM 原生 `EnvDTE.Thread.Freeze()` 与 `Thaw()`，支持在调试中断期间挂起特定工作线程或恢复线程执行，并在 `vs_debugger_get_threads` 中增加 `isFrozen` 状态回显，解决多线程调试时无法重现或隔离竞态条件的痛点；
2. **设置下一语句 / 动态跳转执行点 (`vs_debugger_set_next_statement`)**：联动前台编辑器光标定位与 `EnvDTE.Debugger.SetNextStatement()`，在调试中断期间动态调整程序计数器（Instruction Pointer），支持跳过故障语句或重新执行上一语句，避免反复重新编译与启动调试。

---

## 2. 核心技术实现细节

1. **线程挂起与恢复控制**：
   - 调试状态检查：非调试（`dbgDesignMode`）状态下严密拦截并返回 `debugger_not_debugging`；
   - 线程精准锁定：实现 `FindExactThread` 遍历 `debugger.CurrentProgram.Threads` / `debugger.CurrentProcess.Programs` 匹配 `request.ThreadId`，未匹配到则返回 `thread_not_found`；
   - 状态联动与回显：调用 `thread.Freeze()` / `thread.Thaw()` 后，立即提取 `thread.IsFrozen` 与 `thread.SuspendCount` 返回给 Agent；
   - 同步升级 `vs_debugger_get_threads`，在 `ThreadInfo` 中追加 `isFrozen` 布尔字段。
2. **动态调整下一语句 (Set Next Statement)**：
   - 严格中断态防卫：非中断模式（`dbgBreakMode`）立即拦截并返回 `debugger_not_paused`；
   - 跨格式路径自适应与智能路由：结合 Phase 4B 的工作目录前缀匹配算法，支持绝对路径或相对路径，自动定位打开文档；
   - 指令指针重置：调用 `debugger.SetNextStatement()`；
   - 跨方法/非法指令异常捕获：当 CLR/调试引擎拒绝跨函数跳转（如 HRESULT 0x89710011）或无有效代码行时，严密捕获并结构化映射为 `invalid_next_statement`，保障 Visual Studio 进程与 IPC 稳定不崩溃；
   - 最新现场回传：成功调整后提取最新的顶层调用栈栈帧（`topFrame`）并同步返回。

---

## 3. 端到端在线实测验收（Online Acceptance）

在运行中的 Visual Studio 2026（VS 18.x）实验实例与部署好的 MCP Host 上，运行自动化验收套件 `scripts/test_acceptance_phase4c.py`，全套 6 项实测环节全部 100% 通过：

| 验收环节 | 调用的 MCP 工具 / 场景 | 预期结果 | 实测结果 | 状态 |
| :--- | :--- | :--- | :--- | :---: |
| **[1/6] 健康检查与能力发现** | `vs_health` & `vs_capabilities` | 状态 ok，总能力数扩充至 **38 个**，3 项新能力全激活（`isStub=false`） | `status=ok, vsInstanceId=vs-20932-08df0d4c330561d5`<br>总能力数达到 38，全部就绪 | **PASS** |
| **[2/6] 断点着陆与调试启动** | `vs_debugger_set_breakpoints` & `vs_debugger_start` | 在 `Program.cs:7` 命中断点，进入 `break` 状态 | 成功命中断点并暂停：`mode=break, breakReason=None` | **PASS** |
| **[3/6] 线程快照与冻结状态回显** | `vs_debugger_get_threads` | 枚举全部线程，回显 `isFrozen` 与 `suspendedCount` | 发现 10 个运行线程，目标线程 ID=18992 (`isFrozen: False, suspendedCount: 0`) | **PASS** |
| **[4/6] 线程挂起与解冻恢复** | `vs_debugger_freeze_thread` & `vs_debugger_thaw_thread` | 成功将目标线程挂起（`isFrozen=true`），随后成功解冻（`isFrozen=false`） | `freeze` 返回：`isFrozen: True, suspendedCount: 1, action: 'freeze'`<br>`thaw` 返回：`isFrozen: False, suspendedCount: 0, action: 'thaw'` | **PASS** |
| **[5/6] 动态跳转程序计数器** | `vs_debugger_set_next_statement(Program.cs:10:1)` | 将当前执行点从 Line 7 成功重置到 Line 10，顶层栈帧联动更新 | `success: True, line: 10, function: Program.<Main>$`<br>执行点成功跳转，无需重新编译！ | **PASS** |
| **[6/6] 逆向异常拦截防卫测试** | 1. 挂起无效线程 ID (9999999)<br>2. 跨方法非法跳转 (`Calculator.cs:7`)<br>3. 跳转至不存在文件 (`NoSuchFile.cs`) | 全部前置拦截并返回结构化错误码，IDE 保持稳定不崩溃 | 1. 拦截无效线程，返回 `thread_not_found`<br>2. 拦截跨方法跳转，返回 `invalid_next_statement` (无法设置下一条语句。源代码的此位置没有可执行代码)<br>3. 拦截不存在文件，返回 `file_not_found` | **PASS** |

---

## 4. 自动化测试与工程指标

- **`VsDebugMcp.Protocol.Tests`**：13 / 13 PASS (100%)。
  - 新增 `DebuggerAdvancedControlsContractsRoundTripThroughSharedSerializer` 覆盖 `ThreadInfo.IsFrozen`、`DebuggerThreadControlRequest/Response` 与 `DebuggerSetNextStatementRequest/Response` 序列化往返测试。
- **`VsDebugMcp.Host.Tests`**：105 / 105 PASS (100%)。
  - 新增 `ThreadNotFound` 与 `InvalidNextStatement` 异常映射单测；
  - 覆盖新工具元数据与 Schema 契约校验。
- **全项目自动化单元测试**：**118 / 118 PASS (100%)**。
- **版本号统一发布**：全组件（`source.extension.vsixmanifest`、各项目 csproj、`CHANGELOG.md`、`project-blueprint.md`）已统一提升至 **`0.1.14.0`**。
