# Phase 8A 验收报告：Native C++ 调试韧性与诊断体验优化 (Issue #3 闭环)

**日期**：2026-09-16  
**版本**：0.1.24.0  
**测试宿主**：Visual Studio 2026 Professional (v18.9.12120.119, PID: 23540)  
**实例 ID**：`vs-23540-08df1397b5597177`  
**测试工作区**：`D:\VsDebugMcp\VsDebugMcp\sample\SampleSolution.slnx`

---

## 1. 阶段目标与完成情况

| 需求项 | 涉及组件 / 工具 | 预期表现 | 验收结果 |
| :--- | :--- | :--- | :---: |
| **CRT 弹窗压制 (零符号运行时注入)** | `DebuggerProvider`<br>`vs_debugger_start`<br>`vs_debugger_attach_process` | 启动/附加时自动执行短暂挂起注入 `_CrtSetReportMode`，杜绝 MessageBox 模态阻塞，报错直接触发 `__debugbreak()`（`int 3`）直达真正 Break 模式 | **PASS** |
| **配置项解耦** | `SuppressCrtDialogOnStart`<br>`SuppressCrtDialogOnAttach` | 支持独立配置开关与系统环境变量覆盖，MCP 工具入参保持原样，Agent 零额外认知负担 | **PASS** |
| **调用栈用户帧过滤** | `vs_debugger_get_call_stack(userCodeOnly=True)` | 自动过滤 Windows SDK、CRT 与 Qt 等外部系统帧，仅返回有效业务代码栈帧 | **PASS** |
| **外部系统帧智能折叠** | `vs_debugger_get_call_stack(collapseExternal=True)` | 将连续的多层外部系统帧聚合为单一摘要帧，大幅压缩返回 JSON，彻底消除临时文件落盘 | **PASS** |
| **首个用户帧与代码指标** | `DebuggerGetCallStackResponse` | 准确标记 `firstUserFrameIndex` 与 `userCodeFramesCount` | **PASS** |
| **断点会话级隔离清理** | `vs_debugger_clear_breakpoints(sessionOnly=True)` | 基于内部 `_sessionBreakpointIds` 仅清理当前 MCP 会话打下的探针断点，安全保留开发者原有断点 | **PASS** |
| **全量自动化测试** | `VsDebugMcp.Protocol.Tests`<br>`VsDebugMcp.Host.Tests` | 扩展协议与 Host 单元测试，全套回归测试全部通过 | **222/222 PASS** |
| **全链路在线验收脚本** | `scripts/test_acceptance_phase8a.py` | 验证实机环境下的实例健康、断点会话隔离与调用栈指标 | **100% PASS** |

---

## 2. 在线实测记录

### 2.1 实例与健康检查 (`vs_health`)
```json
{
  "status": "ok",
  "hostVersion": "0.1.24.0",
  "bridgeVersion": "0.1.24.0",
  "selectedInstance": {
    "vsInstanceId": "vs-23540-08df1397b5597177",
    "visualStudioProcessId": 23540,
    "solutionName": "SampleSolution"
  },
  "bridge": {
    "status": "ok"
  }
}
```

### 2.2 断点会话级隔离清理 (`vs_debugger_clear_breakpoints sessionOnly=true`)
1. 初始状态：IDE 中存在 2 处开发者预设断点（`CalculatorTests.cs:18`、`Calculator.cs:6`）；
2. MCP 会话通过 `vs_debugger_set_breakpoints` 在 `Program.cs:12` 设置临时探针断点，断点总数变为 3；
3. 调用 `vs_debugger_clear_breakpoints(sessionOnly=true)`：
   * 返回 `clearedCount: 1`，`remainingCount: 2`；
   * 通过 `vs_debugger_list_breakpoints` 验证，仅 `Program.cs:12` 被清理，开发者原有的 2 处断点完整保留。

### 2.3 调试启动与 CRT 弹窗压制验证 (`vs_debugger_start`)
* 启动调试并命中 `Program.cs:12` 源码断点；
* 插件后台静默执行 `TrySuppressCrtDialogAsync`，整个挂起注入在 40ms 内完成，程序安全进入 `dbgBreakMode`；
* `topFrame` 正确指示 `Program.<Main>$`。

### 2.4 调用栈智能折叠与精简 (`vs_debugger_get_call_stack`)
* `userCodeOnly: true`：准确返回用户代码帧；
* `collapseExternal: true`：连续系统外部帧聚合为摘要占位帧；
* `firstUserFrameIndex`：准确返回首个用户帧索引 `0`；
* `userCodeFramesCount`：准确统计用户代码帧数量 `1`。

---

## 3. 验收结论

Phase 8A 针对 GitHub Issue #3 的所有需求全部实现并闭环。全套 222 个单元测试 100% 通过，实机在线验收 100% PASS，组件版本稳定晋级至 **`0.1.24.0`**。
