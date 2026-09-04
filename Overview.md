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

## MCP 工具列表

插件向 AI Agent 暴露以下标准 MCP 工具：

| 工具名称 | 功能描述 |
|---|---|
| `vs_health` | 检查 MCP 服务与 Visual Studio 的连接健康状态 |
| `vs_capabilities` | 查询当前支持的 IDE 工具能力集 |
| `vs_list_instances` | 列出本机当前运行的所有 Visual Studio 实例 |
| `vs_find_instances` | 根据解决方案路径查找匹配的 Visual Studio 实例 |
| `vs_get_projects_in_solution` | 获取当前解决方案下的所有项目信息 |
| `vs_run_build` | 触发当前解决方案或指定项目的构建 |
| `vs_get_build_status` | 根据构建任务句柄查询当前构建状态（运行中、成功、失败、已取消） |
| `vs_cancel_build` | 取消正在执行的构建任务 |
| `vs_get_output_window_logs` | 获取 Visual Studio “输出”窗口指定窗格（如生成日志）的文本 |
| `vs_get_errors` | 查询 Visual Studio 错误列表中由构建产生的诊断信息 |

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

- **当前支持（v0.1.x）**：工程项目发现、解决方案构建控制、输出日志读取、多实例路由。
- **后续规划**：
  - 调试器接入（断点管理、线程与调用栈查询、表达式求值）
  - 单元测试集成（测试用例发现与运行）
- **安全边界**：服务仅监听本机回环地址（`127.0.0.1`），不开放远程网络访问，不执行非受控的外部系统命令。

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

## Available MCP Tools

The extension exposes the following tools to MCP-compatible AI clients:

| Tool Name | Description |
|---|---|
| `vs_health` | Check connection and health status of the bridge |
| `vs_capabilities` | Discover available IDE capabilities |
| `vs_list_instances` | List all running Visual Studio instances |
| `vs_find_instances` | Find running instances matching a solution path |
| `vs_get_projects_in_solution` | Get all projects in the current solution |
| `vs_run_build` | Start building the solution or a specified project |
| `vs_get_build_status` | Poll asynchronous build progress (running, succeeded, failed, cancelled) |
| `vs_cancel_build` | Cancel an active build task |
| `vs_get_output_window_logs` | Retrieve text from the Output Window (e.g. Build pane) |
| `vs_get_errors` | Retrieve diagnostics from the Visual Studio Error List |

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

- **Supported Now (v0.1.x)**: Solution & project discovery, build lifecycle control, output log retrieval, multi-instance routing.
- **Roadmap**:
  - Debugger integration (breakpoints, threads, call stacks, expression evaluation)
  - Unit test discovery and execution
- **Security**: Bound strictly to local loopback (`127.0.0.1`), with no remote access and no arbitrary process execution.