# Visual Studio 2026 Test Explorer 集成（Phase 3 方向 B）联调与在线验收报告

## 1. 概述与目标

本次工作完成了 Visual Studio 2026 (VS 18.x) 扩展与 MCP Host 的 **Test Explorer（测试资源管理器 / VSTest）** 集成，并针对 `sample/SampleSolution.slnx` 靶场进行了全链路端到端在线验收（Online Acceptance）。

暴露的核心 MCP 工具如下：
- `vs_get_tests`: 发现解决方案中的测试用例，支持按名称/类名/命名空间过滤
- `vs_run_tests`: 发起测试运行（支持全量运行或指定 `testIds` 过滤运行）
- `vs_get_test_run_status`: 轮询当前或历史测试运行的状态、结果、耗时与错误信息
- `vs_cancel_test_run`: 取消正在进行的测试运行

---

## 2. 关键排查与问题修复

在联调过程中，我们通过逆向反射分析与深层代码审查，解决了两处关键阻塞问题：

1. **测试运行触发与底层 API 缺陷绕行**：
   - **现象**：`Microsoft.VisualStudio.TestWindow.Extensibility.ITestsService.RunTestsAsync(IEnumerable<Guid> testIds)` 内部将传入的 ID 当作底层 `TestCaseId` 精确比对，而 `ITest.Id` 返回的实际是 `TestCaseRecord.Id`，导致传参后永远匹配不到用例。
   - **修复**：引入 `OperationBrokerInvoker`，通过 MEF 获取 `IOperationState`（其运行时实现为 `Microsoft.VisualStudio.TestWindow.Controller.OperationBroker`）：
     - 全量运行：调用 `ExecuteAllTestsAsync` 原生触发；
     - 单用例过滤运行：使用 `SearchQuery`（程序集位于 `Microsoft.VisualStudio.TestWindow.Internal.dll`）精确构造 5 参数过滤条件，通过 `ExecuteTestsByFilterAsync` 原生触发；
     - 取消运行：调用 `CancelAsync`。

2. **测试完成事件掩码过早触发修复**：
   - **现象**：初始实现中监听 `(e.State & TestOperationStates.Finished) != 0`，但构建阶段的 `TestOperationStates.ChangeDetectionFinished (0x40004)` 复合了 `Finished (0x4)` 标志位，导致在编译完成时就提前结束了测试状态轮询。
   - **修复**：精确限制仅在 `TestExecutionFinished (0x20004)`、`TestExecutionCancelAndFinished (0x20005)` 或 `OperationSetFinished (0x200000)` 时触发完成回调。

3. **客户端异常解析与互斥断言**：
   - **修复**：安全解析 MCP Tool 异常响应文本，支持捕获并发互斥 `test_run_busy` 及取消回调。

---

## 3. 端到端在线验收验证结果

在运行中的 Visual Studio 实验实例（PID: 5484，实例 ID: `vs-5484-08df0c7e597587fa`）与 MCP Host（PID: 18024，监听 `http://127.0.0.1:43260/`）上，端到端执行了 `scripts/test_acceptance.py`：

| 验收项 | 测试动作 | 预期结果 | 实际结果 | 状态 |
| :--- | :--- | :--- | :--- | :--- |
| **[1/5] 健康与能力发现** | 调用 `vs_health` 与 `vs_capabilities` | 状态 ok，4 个测试能力 `isStub=false` | `vsInstanceId=vs-5484-08df0c7e597587fa`，4 项均为 `isStub=False` | **PASS** |
| **[2/5] 测试发现与过滤** | 调用 `vs_get_tests`，及 `filter: "Multiply"` | 全量发现 3 个单测，过滤精准匹配 1 个 | 全量 3 个用例全部发现，过滤 `Multiply` 结果数=1 | **PASS** |
| **[3/5] 全量测试执行** | 调用 `vs_run_tests`，轮询 `vs_get_test_run_status` | 状态流转 `running -> completed`，全部通过 | 耗时 2859.2ms，3 个用例全部 Passed，带独立耗时 | **PASS** |
| **[4/5] 单用例测试执行** | 指定 `testIds: [Multiply_Guid]` 调用 `vs_run_tests` | 仅执行指定的 `Multiply` 测试用例 | 轮询 2s 完成，Passed: 1, Total: 1 | **PASS** |
| **[5/5] 并发互斥与取消** | 连续快速发起两个测试任务，随后调用取消 | 任务 2 报 `test_run_busy`，任务 1 被成功取消 | 任务 2 拦截并报错 `test_run_busy`；任务 1 状态变为 `cancelled` | **PASS** |

---

## 4. 自动化测试与代码健康度

- **`VsDebugMcp.Protocol.Tests`**：9/9 PASS
- **`VsDebugMcp.Host.Tests`**：59/59 PASS
- **总计自动化测试**：68/68 PASS
- **MSBuild 编译状态**：0 警告，0 错误
