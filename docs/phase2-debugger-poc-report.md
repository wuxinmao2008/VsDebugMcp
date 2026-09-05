# Phase 2 Debugger POC（只读调试观测原型验证）实施与验收报告

**归档日期**：2026-09-05  
**版本基线**：0.1.5.0  
**验证环境**：Visual Studio 2026 (VS 18.9.12120.119 Experimental Instance) + .NET 8 SDK + Windows 11

---

## 一、 背景与设计原则

根据架构讨论共识，Phase 1 聚焦于 **“IDE 上下文与构建闭环”**（不重复提供 Agent 已有的全盘搜索/读盘能力，仅提供 `vs_get_files_in_project` 等原生 IDE 上下文）；Phase 2 开启独立的 **调试器验证轨道（Debugger POC）**。

首期定位为 **“只读现场诊断专家”**，即开发者在 VS 中正常调试启动（F5）或命中断点后，外部 Agent 介入案发现场进行只读结构化排查。核心设计原则包括：
1. **安全与模式防卫（Mode Guard）**：
   - 调试器存在 `dbgDesignMode`（设计模式）、`dbgRunMode`（运行模式）、`dbgBreakMode`（中断模式）。
   - 调用栈获取与表达式求值强依赖 `dbgBreakMode`。若处于非中断状态，通过预检查严格拦截并返回结构化错误码 `debugger_not_paused`，杜绝未经处理的 COM 异常。
2. **UI 线程亲和调度**：
   - 所有与 `EnvDTE.Debugger` 相关的操作均严格通过 `ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync` 调度至 Visual Studio 主 UI 线程。
3. **求值超时与防死锁保护**：
   - 表达式求值限制最长超时（默认 2000ms），避免复杂 getter 或死循环冻结 VS 界面。

---

## 二、 交付物清单

### 1. 协议层（VsDebugMcp.Protocol）
- 新增契约：
  - `DebuggerGetInfoRequest` / `DebuggerGetInfoResponse`
  - `DebuggerSetBreakpointsRequest` / `DebuggerSetBreakpointsResponse`（含 `BreakpointSpec`、`BreakpointInfo`）
  - `DebuggerGetCallStackRequest` / `DebuggerGetCallStackResponse`（含 `StackFrameInfo`）
  - `DebuggerEvaluateExprRequest` / `DebuggerEvaluateExprResponse`
- 新增错误码：
  - `debugger_not_paused`
  - `debugger_unavailable`
  - `debugger_evaluation_failed`

### 2. VSIX 桥接层（VsDebugMcp.Vsix）
- `DebuggerProvider.cs`：基于 `EnvDTE.Debugger` 实现断点预设/清除、状态识别、主线程栈帧遍历以及带超时与副作用控制的表达式求值。
- `BridgeServer.cs`：派发 4 个 `debugger/*` 协议方法，并在 `capabilities` 注册 4 个 `isStub=false` 能力。

### 3. OOP MCP 宿主层（VsDebugMcp.Host）
- `McpTools.cs`：对外注册 4 个标准 MCP 工具：
  - `vs_debugger_get_info`（只读，幂等）
  - `vs_debugger_set_breakpoints`（状态变更）
  - `vs_debugger_get_call_stack`（只读，幂等）
  - `vs_debugger_evaluate_expr`（只读）
- `BridgeService.cs` & `BridgeClient.cs`：实现 Named Pipe RPC 请求转发与调试器特定错误码翻译。

### 4. 自动化测试套件
- `VsDebugMcp.Protocol.Tests`（6/6 PASS）
- `VsDebugMcp.Host.Tests`（25/25 PASS）
- 全套测试 **31/31 PASS (100%)**。

---

## 三、 在线实测验收证据（Live Acceptance Evidence）

在线联调基于测试工程 `sample/SampleSolution.sln`（包含 `SampleApp` 与 `SampleLib`），VS 实验实例 PID `10940`。

### 1. 实例发现与健康状态 (`vs_health` & `vs_capabilities`)
- `vs_health`：成功识别 `vs-10940-08df0b200408b0cf`，版本为 `0.1.5.0`。
- `vs_capabilities`：确认全部 4 项调试器能力均已生效：
  - `vs_debugger_get_info` (v0.1, isStub=false)
  - `vs_debugger_set_breakpoints` (v0.1, isStub=false)
  - `vs_debugger_get_call_stack` (v0.1, isStub=false)
  - `vs_debugger_evaluate_expr` (v0.1, isStub=false)

### 2. 设计时断点设置 (`vs_debugger_set_breakpoints`)
- 调用工具在 `sample/SampleApp/Services/Calculator.cs:5`（`Calculator.Add`）设置断点。
- Visual Studio 成功绑定断点：
  ```json
  {
    "filePath": "D:\\VsDebugMcp\\VsDebugMcp\\sample\\SampleApp\\Services\\Calculator.cs",
    "breakpoints": [
      {
        "id": "D:\\VsDebugMcp\\VsDebugMcp\\sample\\SampleApp\\Services\\Calculator.cs:5",
        "line": 5,
        "column": 44,
        "enabled": true,
        "isBound": true
      }
    ]
  }
  ```
- `vs_debugger_get_info` 反映 `breakpointCount: 1`。

### 3. 中断模式现场观测 (`dbgBreakMode`)
用户在 VS 实验实例中按 F5 启动 `SampleApp`，程序命中断点暂停：
- `vs_debugger_get_info` 实测输出：
  ```json
  {
    "mode": "break",
    "isDebugging": true,
    "currentProcessId": 6108,
    "currentProcessName": "...\\SampleApp.exe",
    "currentThreadId": 8168,
    "breakpointCount": 1,
    "lastBreakReason": "breakpoint"
  }
  ```

### 4. 调用栈提取 (`vs_debugger_get_call_stack`)
实测成功提取完整调用栈帧（含源码函数与模块）：
```json
{
  "threadId": 8168,
  "totalFrames": 2,
  "truncated": false,
  "frames": [
    {
      "frameIndex": 0,
      "functionName": "SampleApp.Services.Calculator.Add",
      "language": "C#",
      "module": "...\\SampleApp.dll"
    },
    {
      "frameIndex": 1,
      "functionName": "Program.<Main>$",
      "language": "C#",
      "module": "...\\SampleApp.dll"
    }
  ]
}
```

### 5. 跨栈帧变量求值 (`vs_debugger_evaluate_expr`)
- **顶层栈帧 (Frame 0: `Calculator.Add`)**：
  - `a` ➔ `value: "10"`, `type: "int"`, `isValid: true`
  - `b` ➔ `value: "20"`, `type: "int"`, `isValid: true`
  - `a + b` ➔ `value: "30"`, `type: "int"`, `isValid: true`
- **调用者栈帧 (Frame 1: `Program.<Main>$`, 指定 `frameIndex: 1`)**：
  - `item.Name` ➔ `value: "\"Widget\""`, `type: "string"`, `isValid: true`
  - `item.Price` ➔ `value: "9.99"`, `type: "double"`, `isValid: true`

---

## 四、 下一步工作候选（面向新会话）

1. **方向 A：调试器控制闭环（Debugger Execution Control）**
   - 实现单步控制与继续/停止命令：
     - `vs_debugger_step_over`（单步跳过）
     - `vs_debugger_step_into`（单步进入）
     - `vs_debugger_step_out`（单步跳出）
     - `vs_debugger_continue`（继续执行）
     - `vs_debugger_stop`（终止调试）
   - 需实现状态互斥与防并发保护。
2. **方向 B：测试资源管理器集成（Test Explorer / VSTest）**
   - 探索单元测试发现与执行闭环：`vs_get_tests`、`vs_run_tests`、`vs_get_test_run_status`。
3. **方向 C：错误列表深化（Error List Deepening）**
   - 深入 VS 18.x `ITableManager` / `IVsErrorList` 原生 COM 接口，解决未托管 C++ 错误快照获取。
