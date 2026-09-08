# Phase 5：混合模式与 Qt/PLC 工控调试深度赋能规划蓝图

## 1. 真实业务场景实测背景

2026-09-08，开发者使用外部 Agent（`gpt-luna 5.6`）接入 `VsDebugMcp`，针对一套真实的 **C# + Qt/C++ 混合进程工业自动化（PLC 通信）程序** 进行深度调试排查。

### 实测表现与核心肯定
- **混合调试底座稳健**：成功附加到 C# CLR 与 Qt/C++ 原生混合进程；
- **全生命周期调试可用**：断点设置、调用栈读取、局部变量读取、线程枚举、模块加载诊断均正常运作；
- **关键问题定位有效**：在排查 Modbus/PLC 偶发通信异常与线程安全问题时，通过变量求值成功验证了 Qt 对象的线程归属（Thread Affinity），有效帮助定位了跨线程调用缺陷；
- **环境切换大幅减少**：构建、调试运行、错误排查闭环在 MCP 中完成，减少了 VS GUI 窗口与终端命令行的来回切换。

---

## 2. 核心不足与痛点根因深度剖析

结合当前仓库代码实现，对实测中暴露的 7 项不足进行技术剖析与根因定位：

### 痛点 1：Release 优化下局部变量不稳定
- **实测表现**：`QModbusReply*`、Qt 成员方法求值经常显示不可用或 `<optimized away>`。
- **底层根因**：
  - 在 Release 编译或 JIT 激进优化下，局部变量被优化至寄存器或已内联失效，DTE `GetExpression` 返回无效求值结果；
  - 当前系统虽然提供了 `vs_get_solution_configurations`，但**缺少一键切换配置的写工具**（无法命令 VS 切换为 Debug 并重构）；
  - 当前对求值失败并未向 Agent 提供“当前处于 Release 优化模式，请切换至 Debug 或检查 PDB 符号”的结构化诊断建议。

### 痛点 2：断点管理不够直观
- **实测表现**：清空断点、按文件清空、区分启用/禁用断点不够明确，容易误删或留下无效断点。
- **底层根因**：
  - 当前系统仅有单个 `vs_debugger_set_breakpoints` 工具；
  - 缺少 `vs_debugger_list_breakpoints`，外部 Agent 无法直观查询当前 VS 中已生效的断点列表及绑定有效性；
  - 清空机制仅依赖 `clearExisting: true`（必须指定文件且必须至少提供一个断点定义），无法无害清空全量断点或按断点 ID 删除；
  - 缺少独立的启用/禁用（Toggle）工具，临时跳过断点必须删除后重新配置。

### 痛点 3：调用栈缺少源文件和行号
- **实测表现**：调用栈很多结果仅有函数名，缺少源文件路径、行号与当前语句。
- **底层根因（已在代码中直接确认）**：
  - 查看 [DebuggerProvider.cs:1974-2002](file:///d:/VsDebugMcp/VsDebugMcp/src/VsDebugMcp.Vsix/DebuggerProvider.cs#L1974-L2002) 的 `ReadStackFrame`，发现当前是通过字符串正则匹配 `functionName` 中的 `" in "` 和 `":line "`；
  - 该纯文本解析仅对部分特定格式的托管堆栈有效；
  - 对 **C++ / Qt 原生帧、混合模式帧或非标准输出格式**，此文本匹配必然失效，导致文件名和行号为空；
  - **COM 原生接口支持缺失**：未向下转型 `EnvDTE80.StackFrame2` 获取强类型的 `FileName`、`LineNumber`、`ColumnNumber` 和 `UserCode`。

### 痛点 4 & 5：缺少 Qt 线程归属诊断与异步事件链追踪
- **实测表现**：
  - 工控/PLC 场景严重依赖 Qt 的事件循环与线程亲和性（Thread Affinity）；
  - 难以快速诊断 `QObject::thread()`、当前执行线程 ID、接收对象线程 ID、是否存在跨线程直接调用风险以及信号槽连接类型（Direct vs. Queued）；
  - `signal → slot → QModbusReply::finished` 异步链难以一次看完整。
- **底层根因**：
  - MCP 工具面均为通用调试命令，未封装针对 Qt 对象模型（`QObject`、`QThread`）的复合诊断能力；
  - 排查跨线程缺陷时需要外部 Agent 手动拼装多次低级 `vs_debugger_evaluate_expr` 往返。

### 痛点 6：缺少日志与断点联动（Output Window 仅限 Build）
- **实测表现**：无法将断点命中时刻与现场日志（如 `qDebug()`、`OutputDebugString`、C# `Debug.WriteLine` 或自定义 `UILOG`）自动关联。
- **底层根因**：
  - 查看 [OutputWindowProvider.cs](file:///d:/VsDebugMcp/VsDebugMcp/src/VsDebugMcp.Vsix/OutputWindowProvider.cs#L78-L86)，当前仅硬编码了 `BuildOutputPane_guid`；
  - 未接入 `DebugOutputPane_guid`（`{538080fc-3b28-4824-8b6b-8cb8c962b406}`）；
  - 未提供枚举 Output 窗格列表的工具，导致自定义窗格（如 `UILOG`）无法被 Agent 自省与读取。

### 痛点 7：混合进程自动附加与多进程发现
- **实测表现**：工控系统中主界面、后台通讯服务（PLC 采集服务）、原生驱动 DLL 可能分布于多个进程中，目前手动指定 PID/进程名较繁琐。
- **底层根因**：
  - `vs_debugger_attach_process` 仅支持单个目标精确定位，且未向 Agent 暴露调试引擎选项（原生 Native / 托管 CLR / 混合 Mixed）；
  - 缺乏基于当前已打开解决方案（启动工程、引用项目产物）的自动进程模式匹配。

---

## 3. Phase 5 演进方案设计（分阶段落地规划）

### Phase 5A（P0 优先级）：调用栈原生属性修复与断点全生命周期治理

#### 1. 调用栈原生强类型属性提取
- **技术落地**：
  在 `DebuggerProvider.ReadStackFrame` 中引入 `EnvDTE80.StackFrame2` 向下转型：
  ```csharp
  if (frame is EnvDTE80.StackFrame2 frame2)
  {
      try { fileName = frame2.FileName; } catch { }
      try { lineNumber = frame2.LineNumber; } catch { }
      try { columnNumber = frame2.ColumnNumber; } catch { }
      try { userCode = frame2.UserCode; } catch { }
  }
  ```
- **收益**：彻底解决 C++/Qt 原生帧文件名与行号丢失问题。

#### 2. 断点管理闭环工具链
- **`vs_debugger_list_breakpoints`**：
  - 遍历 `dte.Debugger.Breakpoints`；
  - 结构化返回断点 ID、文件绝对路径、行号、列号、条件表达式、命中计数模式与目标、当前命中次数、是否启用（`enabled`）、是否已成功绑定（`isBound`）。
- **`vs_debugger_clear_breakpoints`**：
  - 支持参数：
    - `clearAll: true`（清空当前方案中所有断点）；
    - `filePath: "..."`（精确清空指定源文件中的全部断点）；
    - `breakpointId: "..."`（精确删除单个断点）。
- **`vs_debugger_toggle_breakpoint`**：
  - 支持按 `breakpointId` 或 `(filePath, line)` 对断点设置 `enabled: true / false`，不破坏已有条件与计数配置。

---

### Phase 5B（P1 优先级）：解决方案配置切换与多窗格日志联动

#### 1. 解决方案配置一键切换 (`vs_set_solution_configuration`)
- **技术落地**：
  - 基于 `dte.Solution.SolutionBuild.SolutionConfigurations` 查找目标配置（如 `Debug`、`Release`）与平台（如 `x64`、`Any CPU`）；
  - 调用 `cfg.Activate()` 激活，使 Agent 发现优化问题时能自主切换至 Debug 并重新编译。

#### 2. 输出窗口多窗格与 Debug/UILOG 读取支持
- **技术落地**：
  - 在 `OutputWindowProvider` 中接入 `VSConstants.OutputWindowPaneGuid.DebugOutputPane_guid`；
  - 新增 `vs_get_output_panes`：枚举当前 VS 输出窗口中所有已创建的窗格（Name 与 GUID），包括外部程序或插件创建的 `UILOG`；
  - 扩展 `vs_get_output_window_logs`，支持 `source: "debug"` 或任意指定窗格名称。

#### 3. 变量优化态智能诊断建议（Smart Warnings）
- **技术落地**：
  - 在 `vs_debugger_evaluate_expr` 和 `vs_debugger_get_locals` 中，若返回包含 `optimized`、`unavailable` 或值为空，检测当前活动构建配置；
  - 若处于 `Release`，在响应体的 `warnings` 列表中附加指引：“变量可能由于 Release 优化无法查看，建议使用 `vs_set_solution_configuration` 切换为 Debug”。

---

### Phase 5C（P1 优先级）：混合模式调试与多进程自动关联

#### 1. 混合调试引擎显式支持
- **技术落地**：
  - 在 `vs_debugger_attach_process` 中支持传入 `engines: ["Native", "Managed"]`；
  - 通过 `EnvDTE80.Process2.Attach2(engines)` 实现原生 + 托管混合模式附加，确保 C# 与 Qt/C++ 在同一调试会话中均能正常断点和求值。

#### 2. 解决方案关联进程自动发现与附加 (`vs_debugger_auto_attach`)
- **技术落地**：
  - 从 `dte.Solution.Projects` 收集启动项目与输出文件名（`.exe`）；
  - 在系统进程列表中进行前缀与正则匹配；
  - 一键对所有相关进程执行附加，降低工控多进程调试的手动门槛。

---

### Phase 5D（P2 优先级）：Qt/工控线程安全与异步事件链诊断

#### 1. Qt 对象线程安全性智能诊断 (`vs_debugger_qt_diagnose_affinity`)
- **技术落地**：
  - Agent 传入 Qt 对象表达式（如 `pModbusClient` 或 `this`）；
  - 自动在当前中断帧中执行复合求值：
    1. `((QObject*)(expression))->thread()`（对象所属线程指针）；
    2. `QThread::currentThread()`（当前中断代码所在线程指针）；
    3. 获取当前 OS 线程 ID 与对象所属线程的 OS ID；
  - 自动比对并给出结构化诊断结论：
    - `isAffinityMatched: true/false`；
    - 风险等级提示：若在工作线程直接调用非所属 QObject 方法，提示潜在竞态、死锁或信号槽直连风险。

#### 2. 异步调用链追踪时序支持
- **技术落地**：
  - 调试中断停靠（断点/单步）返回结构中增加可选的 `recentDebugLogs` 上下文快照；
  - 串联断点时间戳与输出日志时间线，便于复现 `QModbusReply::finished` 异步回调延迟与死锁。

---

## 4. 实施规划总结

| 阶段 | 核心任务 | 预估交付内容 |
| :--- | :--- | :--- |
| **Phase 5A** | 调用栈强类型修复 + 断点生命周期管理 | 修复 `StackFrame2`；新增 `vs_debugger_list_breakpoints`、`vs_debugger_clear_breakpoints`、`vs_debugger_toggle_breakpoint` |
| **Phase 5B** | 编译配置切换 + 输出窗口多窗格与日志联动 | 新增 `vs_set_solution_configuration`、`vs_get_output_panes`；扩展 `vs_get_output_window_logs` 支持 Debug/UILOG 窗格 |
| **Phase 5C** | 混合调试模式 + 关联进程自动附加 | 扩展 `vs_debugger_attach_process` 支持混合引擎；新增 `vs_debugger_auto_attach` |
| **Phase 5D** | Qt 线程亲和性诊断 + 异步事件链辅助 | 复合诊断工具 `vs_debugger_qt_diagnose_affinity` 与断点日志时序关联 |
