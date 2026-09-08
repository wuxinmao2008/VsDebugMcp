# Visual Studio 2026 调用栈强类型修复与断点全生命周期治理（Phase 5A）联调与在线验收报告

## 1. 概述与核心破局

针对 2026-09-08 开发者与 `gpt-luna 5.6` 在实际工控 C# + Qt/C++ 混合程序实测中暴露的 **“调用栈缺少源文件行号”** 与 **“断点管理不够直观易误删”** 两大核心痛点，本次迭代完成了 Visual Studio 2026 (VS 18.x) 扩展与 MCP Host 的 **Phase 5A（P0 优先级，调用栈强类型修复 + 断点全生命周期管理）**。

### 核心改进要点：
1. **调用栈强类型原生提取**：
   - 彻底摒弃以往仅依靠纯文本正则匹配 CLR 字符串的脆弱机制；
   - 引入 COM `EnvDTE90a.StackFrame2` 强类型接口，直接提取 `FileName`（物理绝对路径）、`LineNumber`（1-based 行号）与 `UserCode`（用户代码标记）；
   - 彻底解决 C++/Qt 原生帧、顶层语句帧或混合模式帧只有函数名而缺失源码路径行号的痛点。
2. **断点全生命周期管理三件套**：
   - **`vs_debugger_list_breakpoints`**：结构化自省当前解决方案中设置的所有断点及其详细属性（位置、条件、命中计数规则与当前命中次数、绑定状态 `isBound`）；支持文件过滤与仅看启用过滤；
   - **`vs_debugger_clear_breakpoints`**：支持一键清空全部、按文件清空（可选行号）、按断点 ID 精确删除；施加严格防卫拦截，杜绝全空参数误清空；
   - **`vs_debugger_toggle_breakpoint`**：独立启停指定断点（自动翻转或显式指定 `enabled`），完全保留已有条件与计数配置。
3. **保持完全向后兼容**：
   - `StackFrameInfo` 新增可选的 `columnNumber` 与 `userCode` 字段，既有字段契约不受影响。

---

## 2. 核心技术实现细节

1. **协议层契约升级 (`VsDebugMcp.Protocol`)**：
   - 在 `StackFrameInfo` 中扩展 `ColumnNumber` 与 `UserCode` 字段；
   - 在 `Class1.cs` 中新增 `BridgeMethods.DebuggerListBreakpoints`、`DebuggerClearBreakpoints`、`DebuggerToggleBreakpoint`；
   - 在 `BridgeErrorCodes` 中新增 `BreakpointNotFound = "breakpoint_not_found"` 与 `InvalidBreakpointTarget = "invalid_breakpoint_target"`；
   - 定义 `DebuggerListBreakpointsRequest/Response`、`DebuggerClearBreakpointsRequest/Response`、`DebuggerToggleBreakpointRequest/Response` 强类型数据模型。
2. **DTE 原生调试提供者重构 (`DebuggerProvider.cs`)**：
   - **`ReadStackFrame` 重构**：向下转型 `EnvDTE90a.StackFrame2` 优先读取强类型属性，保留智能字符串解析作为兜底机制；
   - **`ListBreakpointsAsync`**：遍历 `debugger.Breakpoints`，提取统一的 `BreakpointInfo` 模型并支持多维过滤；
   - **`ClearBreakpointsAsync`**：参数防卫（必须指定 `ClearAll`、`FilePath` 或 `BreakpointId` 之一），安全调用 `bp.Delete()` 并统计清除与剩余计数；
   - **`ToggleBreakpointAsync`**：精准定位断点并切换 `bp.Enabled`，VS 编辑器界面即时同步镂空/实心圆点状态。
3. **OOP MCP 宿主与工具面接入 (`VsDebugMcp.Host`)**：
   - 在 `BridgeClient` 与 `BridgeService` 中追加 3 个断点方法，接入 Phase 4B 的工作目录智能路由（`_registry.Resolve(vsInstanceId, filePath)`）；
   - 在 `McpTools.cs` 中注册 3 个全新的标准 MCP 工具，工具总数从 38 个扩充至 **41 个**。

---

## 3. 在线实测验收（Live Acceptance）

在运行中的 Visual Studio 2026（VS 18.x）实验实例（PID: 15376，实例 ID: `vs-15376-08df0d6e41b82557`，解决方案：`SampleSolution.slnx`）上通过注册的 MCP 工具面完成了完整的闭环在线实测验收：

### ① 实例状态与能力发现
- **`vs_health`**：成功连通，报告 `hostVersion: "0.1.16.0"`, `bridgeVersion: "0.1.16.0"`, `instanceCount: 1`, 状态正常；
- **`vs_capabilities`**：确认全量 41 个能力注册就绪，新工具 `vs_debugger_list_breakpoints`, `vs_debugger_clear_breakpoints`, `vs_debugger_toggle_breakpoint` 均为 `isStub: false`。

### ② 断点管理工具链在线实测
- **`vs_debugger_list_breakpoints`**：
  - 无参调用：精准返回当前方案中设置的 3 个断点，包含完整文件物理路径、行列号、条件表达式类型与当前命中次数；
  - 文件路径过滤：传入目标源文件全路径精准命中并只返回该文件中的断点项。
- **`vs_debugger_toggle_breakpoint`**：
  - 传入 `breakpointId: "D:\...\Program.cs:7"`，成功将 `enabled` 切换为 `false`（VS 编辑器中断点圆点变为镂空禁用）；
  - 调用 `vs_debugger_list_breakpoints(enabledOnly: true)` 验证：该禁用断点被正确过滤排除；
  - 再次调用 `toggle(enabled: true)` 成功恢复启用。
- **`vs_debugger_clear_breakpoints`**：
  - **安全防卫验证**：调用全空参数时，立即返回 `invalid_request` 拦截错误，有效防御误删；
  - **精准删除验证**：在 `Program.cs:10` 设置断点后，传入 `breakpointId` 调用清理，精准返回 `clearedCount: 1, remainingCount: 3`，仅删除目标断点，其他断点完好无损。

### ③ 调用栈强类型源文件/行号实测
- **`vs_debugger_start`**：程序化触发 F5 启动调试并自动在 `Program.cs:7` 断点停靠（`lastBreakReason: "breakpoint"`）。
- **`topFrame` 提取**：
  - 顶层栈帧直接结构化返回：
    ```json
    {
      "frameIndex": 0,
      "functionName": "Program.<Main>$",
      "fileName": "D:\\VsDebugMcp\\VsDebugMcp\\sample\\SampleApp\\Program.cs",
      "lineNumber": 7,
      "userCode": true,
      "module": "...\\SampleApp.dll"
    }
    ```
  - **关键突破**：此前版本由于针对没有 `" in ...:line ..."` 字符串后缀的栈帧无法解析导致文件名行号为空；现在通过 `EnvDTE90a.StackFrame2` **原生直接提取到物理绝对路径与精准行号**！
- **`vs_debugger_get_call_stack`（单帧与多帧验证）**：
  - 经 `step_over` 到第 10 行并 `step_into` 进入 `Calculator.Add` 方法后，调用 `vs_debugger_get_call_stack(maxFrames: 10)`：
    - 栈帧 0：`SampleApp.Services.Calculator.Add`，`Calculator.cs` 第 5 行，`userCode: true`；
    - 栈帧 1：`Program.<Main>$`，`Program.cs` 第 10 行，`userCode: true`；
  - 调用栈全部栈帧均完整附带强类型物理文件路径、准确行号与用户代码标记！
- **`vs_debugger_stop`**：平稳结束调试会话，VS 返回设计模式（`design`）。

---

## 4. 自动化测试与工程指标

- **`VsDebugMcp.Protocol.Tests`**：**18 / 18 PASS (100%)**。
  - 新增 `Phase5ABreakpointAndStackFrameProtocolRoundTripsThroughSharedSerializer` 测试。
- **`VsDebugMcp.Host.Tests`**：**123 / 123 PASS (100%)**。
  - 新增断点工具元数据、参数默认值、以及错误码映射测试。
- **全项目自动化单元测试**：**141 / 141 PASS (100%)**。
- **VSIX 打包产物**：`VsDebugMcp.Vsix.vsix` 编译成功，0 错误，框架依赖模式包体积保持合规。
- **工具总数扩充**：MCP 工具总数从 38 个增至 **41 个**。
- **版本号统一发布**：全项目（`source.extension.vsixmanifest`、各项目 csproj、`CHANGELOG.md`、`project-blueprint.md`）已统一提升至 **`0.1.16.0`**。
