# VsDebugMcp

**Connect Visual Studio 2026 to AI Agents via Model Context Protocol (MCP).**

通过 MCP 协议将 Visual Studio 2026 / 18.x 连接到外部 AI Agent，让 Agent 能够感知打开的解决方案、控制 IDE 构建，并直接读取编译输出与诊断信息。

[简体中文](#简体中文) | [English](#english)

---

<a id="简体中文"></a>

## 这是你需要的插件吗？

如果你在使用支持 MCP 的 AI Agent（如 Claude Desktop、Cursor、VS Code、Cline 等），并且在 Visual Studio 中开发 C++ 或 .NET 项目：

- **普通 Agent 的局限**：只能查看磁盘上的静态文件，或在终端盲目运行 `dotnet build` / `msbuild`，无法获知 IDE 当前加载的解决方案配置，也经常因为缺少环境上下文导致命令行构建失败。
- **VsDebugMcp 的作用**：在 Visual Studio 与 AI Agent 之间搭建桥梁，让 Agent 可以直接调用 Visual Studio 内部的能力——读取当前工程结构、触发真实的 IDE 构建、获取详细的生成日志并分析编译错误。

## 核心功能

- **解决方案与项目感知**：让 Agent 查询 Visual Studio 当前打开的解决方案、包含的项目列表及活动配置（Debug/Release、x64 等）。
- **IDE 构建控制**：支持通过活动配置或指定配置发起构建、异步轮询构建进度、随时取消正在执行的构建。
- **输出与错误诊断**：直接读取 Visual Studio “输出”窗口中的构建日志（Build Output），方便 Agent 准确定位编译报错并提供修改建议。
- **多实例支持**：同时打开多个 Visual Studio 窗口时，支持按实例进行明确路由，防止 Agent 误操作其他工程。

## 实际运行效果

下图展示了 AI Agent（以 VS Code Agent 为例）在执行代码变更后，自主调用 `vs_run_build` 触发构建，并通过 `vs_get_build_status` 轮询进度，结合 `vs_get_errors` 与 `vs_get_output_window_logs` 读取构建日志与诊断信息闭环：

![AI Agent 工作流截图](assets/screenshot_01.png)

## MCP 工具列表

插件向 AI Agent 暴露以下标准 MCP 工具（共 24 个）：

| 分类 | 工具名称 | 功能描述 |
|---|---|---|
| **服务与实例** | `vs_health` | 检查 MCP 服务与 Visual Studio 的连接健康状态与实例信息 |
| | `vs_capabilities` | 查询当前已加载并激活的 IDE 工具能力集 |
| | `vs_list_instances` | 列出本机当前运行的所有 Visual Studio 实例 |
| | `vs_find_instances` | 根据解决方案路径查找匹配的 Visual Studio 实例 |
| **工程与构建** | `vs_get_projects_in_solution` | 获取当前解决方案下的所有项目信息 |
| | `vs_get_files_in_project` | 获取指定项目或全解决方案的源码文件树及 C++ 筛选器目录 |
| | `vs_run_build` | 触发当前解决方案或指定项目的构建 |
| | `vs_get_build_status` | 根据构建任务句柄查询当前构建状态（运行中、成功、失败、已取消） |
| | `vs_cancel_build` | 取消正在执行的构建任务 |
| | `vs_get_output_window_logs` | 获取 Visual Studio “输出”窗口指定窗格（如生成日志）的文本 |
| | `vs_get_errors` | 查询 Visual Studio 错误列表中由构建产生的诊断信息 |
| **调试启动与控制** | `vs_debugger_start` | 程序化启动调试（F5），支持自动着陆探测（`waitForBreak`）与顶层栈帧即时回显 |
| | `vs_debugger_step_over` | 单步步过（Step Over）当前语句 |
| | `vs_debugger_step_into` | 单步步入（Step Into）目标函数内部 |
| | `vs_debugger_step_out` | 单步步出（Step Out）到当前函数的调用方 |
| | `vs_debugger_continue` | 继续运行（Continue）直到下一断点或退出 |
| | `vs_debugger_pause` | 暂停（Pause/Break）当前执行中的目标进程 |
| | `vs_debugger_stop` | 停止调试（Stop Debugging），平稳返回设计模式 |
| **调试诊断与求值** | `vs_debugger_get_info` | 查询调试器运行模式（design/run/break）、活动 PID/TID 及断点数 |
| | `vs_debugger_set_breakpoints` | 在指定源码文件与行号设置、切换或清空断点 |
| | `vs_debugger_get_call_stack` | 在断点停靠时捕获当前活动线程的调用栈帧列表 |
| | `vs_debugger_get_locals` | 自动识别当前栈帧的全部入参（Arguments）与局部变量（Locals） |
| | `vs_debugger_evaluate_expr` | 对单项表达式在指定栈帧执行安全求值（支持超时与副作用控制） |
| | `vs_debugger_evaluate_expressions` | 单次 RPC 批量求值多个表达式，支持单项错误隔离 |

## 快速上手

### 1. 安装插件
安装 `VsDebugMcp` VSIX 扩展，并重启 Visual Studio。

### 2. 打开工程
在 Visual Studio 2026 中打开你的解决方案。扩展加载后会在后台自动启动本地服务，无需手动运行任何后台程序。

### 3. 配置 MCP 客户端
在你的 MCP 客户端（例如 Claude Desktop 或 VS Code 配置文件）中添加本地服务地址：

```json
{
  "mcpServers": {
    "vs-debug-mcp": {
      "url": "http://127.0.0.1:43260"
    }
  }
}
```

配置完成后，你的 AI Agent 即可自动发现并使用 Visual Studio 工具。

## 当前状态与后续规划

- **当前支持（v0.1.7.0）**：
  - 工程结构与文件树发现、IDE 构建控制与 Build Output 原始日志提取；
  - 调试器全链路闭环：设计模式下自动 F5 启动、断点智能着陆、局部变量全景探测、单步步过/步入/步出、批量表达式求值、会话终止与并发模式守卫；
  - 全套 55/55 自动化单元测试覆盖，经由 Visual Studio 2026 实验实例全链路在线实测验收。
- **后续规划**：
  - 单元测试集成（Test Explorer / VSTest 发现与异步运行）；
  - 错误列表（Error List）公开数据源原生 COM 深化；
  - 调试器进程附加（Attach to Process）与高级条件/计数断点。
- **安全边界**：服务仅监听本机回环地址（`127.0.0.1:43260`），不开放远程网络访问，不执行非受控的外部系统命令。

---

<a id="english"></a>

## Is this extension for you?

If you use MCP-compatible AI agents (such as Claude Desktop, Cursor, VS Code, Cline, etc.) while developing C++ or .NET projects in Visual Studio:

- **The Problem**: General AI tools only see static files on disk or try to run terminal builds blindly. They cannot know which solution configuration is active in the IDE, and command-line builds often fail due to missing IDE environment context.
- **The Solution**: VsDebugMcp bridges Visual Studio and your AI Agent, enabling the agent to inspect the loaded solution, trigger real IDE builds, and read build logs and diagnostics directly from Visual Studio.

## Key Features

- **Solution & Project Awareness**: Query the currently opened solution, project list, and active configurations (Debug/Release, x64, etc.).
- **IDE Build Control**: Start solution builds using active or specified configurations, track build progress asynchronously, or cancel ongoing builds.
- **Output & Diagnostics**: Retrieve raw text from the Visual Studio Output Window (Build pane) so your agent can diagnose compiler errors accurately.
- **Multi-Instance Support**: When multiple Visual Studio windows are open, tools can target specific instances to avoid conflicting operations.

## Usage in Action

The screenshot below demonstrates an AI agent (e.g., VS Code Agent) automatically triggering `vs_run_build`, tracking status with `vs_get_build_status`, and reading diagnostic messages and output logs via `vs_get_errors` and `vs_get_output_window_logs` to complete an edit-build-diagnose loop:

![AI Agent in Action](assets/screenshot_01.png)

## Available MCP Tools

The extension exposes the following standard MCP tools (24 tools total):

| Category | Tool Name | Description |
|---|---|---|
| **Service & Instances** | `vs_health` | Check connection and health status of the bridge and running instance |
| | `vs_capabilities` | Discover available and active IDE capabilities |
| | `vs_list_instances` | List all running Visual Studio instances on the machine |
| | `vs_find_instances` | Find running instances matching a solution path |
| **Project & Build** | `vs_get_projects_in_solution` | Get all projects in the current solution |
| | `vs_get_files_in_project` | Get source file hierarchy and C++ filter directories for projects |
| | `vs_run_build` | Start building the solution or a specified project |
| | `vs_get_build_status` | Poll asynchronous build progress (running, succeeded, failed, cancelled) |
| | `vs_cancel_build` | Cancel an active build task |
| | `vs_get_output_window_logs` | Retrieve text from the Output Window (e.g. Build pane) |
| | `vs_get_errors` | Retrieve diagnostics from the Visual Studio Error List |
| **Debugger Launch & Stepping** | `vs_debugger_start` | Programmatically start debugging (F5) with smart break landing and top frame return |
| | `vs_debugger_step_over` | Step over the current statement in break mode |
| | `vs_debugger_step_into` | Step into target functions in break mode |
| | `vs_debugger_step_out` | Step out of current function to caller |
| | `vs_debugger_continue` | Resume debuggee execution until next break or exit |
| | `vs_debugger_pause` | Pause/break running debuggee process |
| | `vs_debugger_stop` | Stop debugging and return to design mode |
| **Debugger Diagnostics & Eval** | `vs_debugger_get_info` | Query debugger mode (design/run/break), active PID/TID, and breakpoints |
| | `vs_debugger_set_breakpoints` | Set, toggle, or clear breakpoints in source files |
| | `vs_debugger_get_call_stack` | Capture call stack frames for the active thread |
| | `vs_debugger_get_locals` | Automatically inspect arguments and local variables on current stack frame |
| | `vs_debugger_evaluate_expr` | Safely evaluate an expression with timeout and side-effect control |
| | `vs_debugger_evaluate_expressions` | Single-RPC batch expression evaluation with per-item error isolation |

## Quick Start

### 1. Install Extension
Install the `VsDebugMcp` VSIX package and restart Visual Studio.

### 2. Open a Solution
Open your solution in Visual Studio 2026. The extension automatically launches the local service in the background. No manual setup required.

### 3. Configure Your MCP Client
Add the local endpoint to your MCP client configuration (e.g. Claude Desktop or VS Code):

```json
{
  "mcpServers": {
    "vs-debug-mcp": {
      "url": "http://127.0.0.1:43260"
    }
  }
}
```

Your AI agent will automatically detect and start using Visual Studio tools.

## Current Status & Roadmap

- **Supported Now (v0.1.7.0)**:
  - Solution and project file discovery, build control and Build Output retrieval;
  - Full debugger automation loop: programmatic F5 start, smart break detection, local variables inspection, stepping control, batch expression evaluation, session termination, and concurrency mode guards;
  - Complete 55/55 automated unit tests pass rate, live accepted against Visual Studio 2026 Experimental Instance.
- **Roadmap**:
  - Test Explorer / VSTest integration (test discovery, asynchronous test runs);
  - Error List COM provider deepening;
  - Attach to process and advanced conditional/hit-count breakpoints.
- **Security**: Bound strictly to local loopback (`127.0.0.1:43260`), with no remote access and no arbitrary process execution.