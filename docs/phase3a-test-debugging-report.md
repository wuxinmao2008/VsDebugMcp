# Visual Studio 2026 测试与调试联动（Phase 3A）联调与在线验收报告

## 1. 概述与目标

本次工作完成了 Visual Studio 2026 (VS 18.x) 扩展与 MCP Host 的 **Test-Driven Debugging（测试驱动联动调试 / Phase 3A）** 集成，并针对 `sample/SampleSolution.slnx` 靶场进行了全链路端到端在线验收（Online Acceptance）。

本次新增并暴露的核心 MCP 工具如下：
- `vs_debug_test_by_id`: 指定测试用例 ID 触发原生单测调试，支持 `waitForBreak` 等待断点命中，自动捕获断点现场顶层栈帧（`topFrame`）与暂停原因；
- `vs_debugger_get_threads`: 在调试暂停或命中断点时，获取当前调试会话的多线程快照（包含线程 ID、友好名称、`isCurrent`、存活状态及优先级）。

至此，外部 Agent 具备了从 **“发现单测 -> 下发业务断点 -> 精确调试单个测试 -> 捕获命中断点现场 -> 巡检多线程与局部变量 -> 恢复/停止调试”** 的闭环自愈调试能力。

---

## 2. 关键攻关与问题解决

在集成与端到端实测过程中，攻克了三处关键核心技术难点：

1. **`OperationBroker.DebugTestsByFilterAsync` 阻塞机制攻关**：
   - **现象**：`OperationBroker.DebugTestsByFilterAsync` 返回的 `Task` 会一直持续到测试执行或调试完全结束才返回。若在 `DebugTestAsync` 中直接 `await` 该 Task，当测试执行命中断点时调试器挂起，该 Task 永久等待继续运行，导致外层 MCP/HTTP 请求在断点期间发生连接超时。
   - **解决方案**：在 `TestExplorerProvider.DebugTestAsync` 中，利用 `_package.JoinableTaskFactory.RunAsync` 将调试测试任务放入后台异步执行；主线程立即进入带超时控制（默认 15s）的 100ms 探测循环，检测 `debugger.CurrentMode == dbgBreakMode`。一旦断点命中，立即捕获当前的 `topFrame`、`lastBreakReason` 并向 MCP Client 快速响应，彻底解决长连接挂死问题。

2. **EnvDTE 线程对象模型结构兼容**：
   - **现象**：`debugger.CurrentProcess` 属于 `EnvDTE.Process` 接口，并不直接提供 `Threads` 属性（`Threads` 存在于 `EnvDTE.Program` 或 `EnvDTE80.Process2` 中），直接访问会导致运行时异常或空指针。
   - **解决方案**：在 `DebuggerProvider.GetThreadsAsync` 中优先通过 `debugger.CurrentProgram?.Threads` 获取线程集合；若为空则遍历 `debugger.CurrentProcess.Programs` 级联获取，并安全提取 `thread.ID`、`thread.Name`、`thread.IsAlive` 及 `isCurrent` 标记。

3. **.NET Framework 4.7.2 运行时 API 约束**：
   - **现象**：Visual Studio 2026 扩展进程仍运行在 .NET Framework 兼容层上，诸如 `Math.Clamp` 等现代 .NET API 缺失，会导致编译失败。
   - **解决方案**：提供自实现的兼容工具函数，严格遵循 Framework 4.7.2 语法标准。

---

## 3. 端到端在线验收验证结果

在运行中的 Visual Studio 实验实例与已部署的 MCP Host 上，运行端到端验证套件 `scripts/test_acceptance_phase3a.py`，6 大核心流程全部通过：

| 验收环节 | 执行动作 | 预期结果 | 实测结果 | 状态 |
| :--- | :--- | :--- | :--- | :--- |
| **[1/6] 健康与能力发现** | 调用 `vs_health` 与 `vs_capabilities` | 状态 ok，27 个工具全部暴露，两个新工具 `isStub=false` | `status=ok`, `vs_debug_test_by_id: isStub=False`, `vs_debugger_get_threads: isStub=False` | **PASS** |
| **[2/6] 测试用例定位** | 调用 `vs_get_tests` 发现用例 | 发现 3 个测试用例，精确提取 `Multiply` 用例 ID | 提取到 `SampleTests.CalculatorTests.Multiply_TwoNumbers_ReturnsProduct` (ID: `c0c810fb-5a88-294e-d684-89f323ff8bf7`) | **PASS** |
| **[3/6] 设置断点** | 调用 `vs_debugger_set_breakpoints` | 在业务代码 `Calculator.cs:6` 及测试代码 `CalculatorTests.cs:18` 下断点 | 断点数量=1，无任何 warning | **PASS** |
| **[4/6] 联动单测调试** | 调用 `vs_debug_test_by_id(waitForBreak=True)` | 成功启动调试并在断点命中时立刻返回 | 1.8s 内返回 `debuggerMode: "break"`, `lastBreakReason: "breakpoint"`, `topFrame: SampleTests.CalculatorTests.Multiply_TwoNumbers_ReturnsProduct` | **PASS** |
| **[5/6] 现场多线程与变量巡检** | 调用 `vs_debugger_get_threads` & `vs_debugger_get_locals` | 捕获所有线程及当前断点栈帧局部变量 | 线程总数 32，活动线程 TID=9608；成功读取局部变量 `this`, `result = 0` | **PASS** |
| **[6/6] 恢复与收尾** | 调用 `vs_debugger_continue` 与 `vs_debugger_stop` | 退出断点暂停，安全恢复到设计模式 | 成功恢复执行并由设计模式收尾，调试会话正确清理 | **PASS** |

---

## 4. 自动化单元测试与代码健康度

- **`VsDebugMcp.Protocol.Tests`**：10/10 PASS (100%)
- **`VsDebugMcp.Host.Tests`**：64/64 PASS (100%)
- **项目总计自动化测试**：74/74 PASS (100%)
- **版本号同步**：`0.1.9.0`（已同步 `source.extension.vsixmanifest`, 各 csproj `<Version>`, `CHANGELOG.md`）
- **MSBuild 编译状态**：0 警告，0 错误
