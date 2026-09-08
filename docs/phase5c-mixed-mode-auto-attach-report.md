# Phase 5C 验收报告：混合模式调试引擎支持与关联进程自动发现

## 1. 概述

本阶段落地并实测完成了 **Phase 5C（P1 优先级）** 功能，针对 2026-09-08 开发者与 `gpt-luna 5.6` 在工控 C# + Qt/C++ PLC 实测中反馈的 **“多进程架构下手工查找 PID 繁琐、缺乏混合模式多引擎（Native + Managed）支持”** 核心痛点，提供了完整解法。

- **版本号**：`0.1.18.0`
- **验收时间**：2026-09-08
- **运行环境**：Visual Studio 18.x (PID: 12460) 实验性实例 + SampleSolution (C# .NET 8)

---

## 2. 交付成果

### 2.1 混合模式调试引擎扩展 (`vs_debugger_attach_process`)
- **多引擎支持**：`vs_debugger_attach_process` 增加了可选 `engines: ["Native", "Managed"]`（或中文本地化 `["本机", "托管"]`）。
- **COM 原生 Attach2 绑定**：
  - 通过 `EnvDTE80.Process2.Attach2(object Engines)` 绑定目标进程的传输引擎；
  - 智能大小写不敏感与子字符串匹配：匹配 `proc2.Transport.Engines`，例如传入 `"本机"` 匹配原生引擎，传入 `"托管"` 匹配 `"托管(.NET Core、.NET 5+)"`；
  - 当传入不存在的调试引擎时，抛出结构化错误 `engine_not_found`，并在错误信息中列举该进程当前系统支持的全部调试引擎列表，便于 Agent 自我纠错。
- **状态回显**：`DebuggerAttachResponse` 包含 `attachedEngines` 列表，明确回显本次实际挂载的调试引擎。

### 2.2 解决方案关联进程自动发现 (`vs_debugger_find_solution_processes`)
- **进程与工程自动映射**：
  - 递归遍历当前打开解决方案的全部工程（包含嵌套 Solution Folder 与项目引用），收集项目名称、完整工程路径、程序集输出名（`OutputFileName` / `AssemblyName`）；
  - 从 `SolutionBuild.StartupProjects` 精确提取启动工程列表；
  - 与操作系统当前运行的 `debugger.LocalProcesses` 进行匹配，自动识别属于当前解决方案的活动进程；
  - 提取进程实时状态：`processId`、`processName`、`projectName`、`projectFilePath`、`isStartupProject`、`isBeingDebugged`（是否已被当前 VS 实例调试）、`userName`。
- **启动项过滤**：支持 `startupOnly: true`，仅检索启动项目对应的运行中进程。

### 2.3 一键批量自动附加 (`vs_debugger_auto_attach`)
- **无需 PID 的智能附加**：外部 Agent 无需事先通过任务管理器或外部命令查询 PID，直接调用 `vs_debugger_auto_attach` 即可一键将当前打开解决方案的项目进程附加到 Visual Studio 调试器。
- **自定义过滤与引擎配置**：
  - 支持 `startupOnly` 限制；
  - 支持 `processNames` 白名单子字符串过滤；
  - 支持传入 `engines` 同时以混合模式（Native + Managed）附加。
- **幂等与健壮性防卫**：
  - 若目标进程已被调试，自动记录 `already_debugged` 警告，不中断其余进程附加；
  - 若无匹配进程，抛出结构化错误 `no_solution_processes_found`；
  - 返回批量操作摘要：`attachedCount` 与每个进程的详细 `DebuggerAttachResponse`。

---

## 3. 在线联调与实测记录 (Live Acceptance)

在运行中的 Visual Studio 18.x Experimental Instance（PID: 12460）中完成 MCP 链路全通联调验证：

| 测试项 | 调用工具与入参 | 实际输出结果 | 结论 |
| :--- | :--- | :--- | :--- |
| **健康检查** | `vs_health` | `hostVersion: 0.1.18.0`, `PID: 12460`, `bridge: ok` | **PASS** |
| **能力清单** | `vs_capabilities` | `vs_debugger_find_solution_processes` 与 `vs_debugger_auto_attach` 均为 `isStub: false` | **PASS** |
| **无进程时进程发现** | `vs_debugger_find_solution_processes` | `processes: []`, `totalCount: 0` | **PASS** |
| **活动进程精准发现** | `SampleApp.exe` 启动后调用 `vs_debugger_find_solution_processes` | 成功发现 PID: 13060，匹配 `projectName: "SampleApp"`, `isStartupProject: true`, `isBeingDebugged: false` | **PASS** |
| **启动工程过滤** | `vs_debugger_find_solution_processes(startupOnly: true)` | 正确保留 `SampleApp.exe` | **PASS** |
| **未知引擎防卫校验** | `vs_debugger_attach_process(engines: ["NonExistentEngine123"])` | 拦截并抛出 `engine_not_found`，列出可用引擎：`托管(.NET Core、.NET 5+)`, `本机` 等 | **PASS** |
| **混合模式双引擎附加** | `vs_debugger_attach_process(engines: ["本机", "托管"])` | `isDebugging: true`, `attachedEngines: ["本机", "托管(.NET Core、.NET 5+)"]` | **PASS** |
| **调试中状态实时同步** | 附加后再次调用 `vs_debugger_find_solution_processes` | PID: 13060 的 `isBeingDebugged` 实时更新为 `true` | **PASS** |
| **空进程自动附加防卫** | 目标进程退出后调用 `vs_debugger_auto_attach` | 抛出 `no_solution_processes_found: No running solution processes were found to attach.` | **PASS** |
| **批量一键自动附加** | 重新拉起进程后调用 `vs_debugger_auto_attach(engines: ["本机", "托管"], startupOnly: true)` | `attachedCount: 1`, 自动附加 `SampleApp.exe`，引擎为 `本机` + `托管(.NET Core、.NET 5+)` | **PASS** |
| **重复附加幂等警告** | 进程调试中再次调用 `vs_debugger_auto_attach` | 返回 `already_debugged` 警告，无异常崩溃 | **PASS** |

---

## 4. 自动化测试统计

- **Protocol 测试套件**：20/20 PASS (`VsDebugMcp.Protocol.Tests`)
- **Host 测试套件**：145/145 PASS (`VsDebugMcp.Host.Tests`)
- **测试总用例数**：165 个（100% 通过）
- **注册 MCP 工具总数**：从 46 个扩充至 **48 个**