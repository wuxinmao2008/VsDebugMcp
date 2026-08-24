# VsDebugMcp Bridge

**让 AI Agent 看见并使用你正在运行的 Visual Studio。**

VsDebugMcp Bridge 通过 MCP 将本地 Visual Studio 2026 / Visual Studio 18.x 的解决方案上下文、构建流程、错误信息和输出窗口连接到外部 AI Agent。

[简体中文](#简体中文) | [English](#english)

> **Early Access** — 项目、构建生命周期和 Build Output 已完成端到端验证；Error Table 诊断仍受 Visual Studio 公共数据源限制。当前版本不包含调试器控制、测试运行、文件编辑或远程访问。

---

<a id="简体中文"></a>

## 不再让 Agent 猜测 Visual Studio 的状态

当代码真正由 Visual Studio、MSBuild、MSVC 或解决方案配置驱动时，仅访问工作区文件并不足以回答这些问题：

- 当前打开的是哪个解决方案、包含哪些项目？
- Visual Studio 实际使用哪个配置和平台进行构建？
- 构建仍在运行、已经成功，还是已经失败？
- 编译器在 Visual Studio 的 Build Output 中报告了什么？
- 同时打开多个 Visual Studio 实例时，Agent 应该操作哪一个？

VsDebugMcp Bridge 为这些问题提供一条本机 MCP 通道，让 Agent 使用 Visual Studio 的真实状态，而不是根据文件和命令行结果进行猜测。

> **演示素材占位**  
> 建议 GIF：Agent 发现 Visual Studio 实例 → 列出解决方案项目 → 启动构建 → 返回失败状态和 Build Output。

## 你可以做什么

### 发现 Visual Studio 上下文

- 检查 Bridge 和 Host 的健康状态
- 查询当前可用的 MCP 能力
- 列出或筛选正在运行的 Visual Studio 实例
- 获取当前解决方案中的项目

### 控制真实的 Visual Studio 构建

- 使用活动配置，或指定 configuration/platform 启动解决方案构建
- 通过稳定的 build task handle 查询异步构建状态
- 取消正在运行的构建
- 识别成功、失败、取消、并发冲突和无效 handle

### 把构建结果交给 Agent

- 读取 Visual Studio **Output → Build** 窗格中的原始输出
- 获取输出尾部并限制最大字符数，避免返回不受控的大段日志
- 查询 Error Table 中由 Build 来源提供的诊断信息*

\* `vs_get_errors` 已实现，但部分项目系统不会通过 Visual Studio 公共 Error Table 数据源公开编译诊断。此时工具会明确返回 `diagnostics_unavailable`，不会把 Build Output 解析结果伪装成 Error Table 数据。

### 安全地处理多个实例

每个 Visual Studio 进程都会注册独立的 `vsInstanceId`。只有一个实例时可以自动路由；同时运行多个实例时，Agent 必须明确选择目标，避免误操作其他解决方案。

## 当前 MCP 工具

| 场景 | 工具 |
|---|---|
| 健康与能力 | `vs_health`, `vs_capabilities` |
| 实例发现 | `vs_list_instances`, `vs_find_instances` |
| 解决方案上下文 | `vs_get_projects_in_solution` |
| 构建生命周期 | `vs_run_build`, `vs_get_build_status`, `vs_cancel_build` |
| 诊断与输出 | `vs_get_errors`, `vs_get_output_window_logs` |

## 五分钟开始使用

### 1. 安装扩展

安装 **VsDebugMcp Bridge** VSIX，然后重启 Visual Studio。

### 2. 打开解决方案

在 Visual Studio 中打开需要交给 Agent 使用的解决方案。VSIX 加载后会自动启动随扩展打包的 self-contained Host，无需单独安装或手动运行 Host。

### 3. 配置 MCP 客户端

```json
{
	"servers": {
		"vs-debug-mcp": {
			"type": "http",
			"url": "http://127.0.0.1:43260"
		}
	},
	"inputs": []
}
```

### 4. 验证连接

让 Agent 依次调用：

1. `vs_health`
2. `vs_list_instances`
3. `vs_capabilities`
4. `vs_get_projects_in_solution`

如果工具定义刚刚发生变化，请重新加载 MCP 客户端，使其重新发现工具。

## 一个典型工作流

```text
发现 Visual Studio 实例
	→ 选择目标 vsInstanceId
	→ 获取解决方案项目
	→ 使用活动配置启动构建
	→ 通过 buildTaskId 查询状态
	→ 读取 Build Output
	→ Agent 根据真实编译结果继续分析
```

Build task 状态保存在 Visual Studio Bridge 中，不依赖单次 MCP 请求或 Named Pipe 连接的生命周期。

## 工作原理

```text
MCP Client / AI Agent
	→ Streamable HTTP on 127.0.0.1:43260
	→ Shared VsDebugMcp.Host
	→ vsInstanceId Registry and Router
	→ Per-instance Named Pipe RPC
	→ Visual Studio VSIX Bridge
	→ Visual Studio
```

- MCP 客户端只需要一个固定的本机 URL。
- 一个 Windows 用户共享一个 Host，每个 Visual Studio 实例拥有独立 Bridge pipe。
- VSIX 每 5 秒发送一次心跳；Host 在实例失联 15 秒后移除注册。
- 最后一个 Visual Studio 实例离开后，Host 自动退出。

## 本机优先的安全边界

- HTTP 仅绑定 IPv4 loopback `127.0.0.1:43260`
- Host control pipe 和 Bridge pipe 仅允许当前 Windows 用户访问
- 不开放外部网卡，不支持远程 Visual Studio 访问
- 当前版本不提供任意命令执行、进程控制或文件编辑
- 日志不得记录请求载荷、凭据、环境变量或原始 Visual Studio Copilot 日志

## 当前状态

| 状态 | 能力 |
|---|---|
| 已完成端到端验证 | Host 自动启动、MCP 工具发现、多实例路由、项目发现、构建启动/状态/取消、Build Output |
| 已实现，仍在验证 | Error Table build diagnostics |
| 尚未提供 | Debugger、tests、file read/search/edit、远程访问、通用进程控制 |

这是一个 Early Access 项目，工具接口和能力可能随着验证结果继续调整。

## 后续方向

在保持本机安全边界和显式状态 handle 的前提下，后续阶段计划验证：

- 测试发现与测试运行
- 调试会话、线程、调用栈、断点和表达式求值
- 更完整的 IDE diagnostics 和 output providers

暂不考虑：

- 文件读取和代码搜索(*使用Agent宿主的功能应该已经足够)

这些项目代表计划方向，不表示当前版本已经提供，也不承诺具体发布日期。


## 要求

- Visual Studio 2026 / Visual Studio 18.x
- 支持 Streamable HTTP 的 MCP 客户端
- Windows `win-x64`
- 可用的 IPv4 loopback 和本机 Named Pipe 通信

## 常见问题

### 是否需要单独安装 .NET 或启动 Host？

不需要。VSIX 包含 `win-x64` self-contained Host，并在 Visual Studio 加载扩展时确保它正在运行。

### 可以连接另一台计算机上的 Visual Studio 吗？

不可以。VsDebugMcp Bridge 有意限制为本机使用。

### 为什么 `vs_get_errors` 可能返回 `diagnostics_unavailable`？

某些项目系统会把编译错误显示在 Build Output 中，却不通过当前可用的 Visual Studio 公共 Error Table 数据源公开这些错误。请使用 `vs_get_output_window_logs` 获取原始构建输出。

### 端口 `43260` 被占用会怎样？

Host 会安全地启动失败，不会改用不可预测的端口，也不会终止占用端口的其他进程。

---

<a id="english"></a>

## Let your AI agent work with the Visual Studio you are actually running

VsDebugMcp Bridge connects MCP-compatible AI agents to the real state of a local Visual Studio 2026 / Visual Studio 18.x instance: its solution, projects, builds, diagnostics, and Build Output.

File access alone cannot reliably tell an agent:

- Which solution and projects are currently loaded
- Which configuration and platform Visual Studio is using
- Whether a build is running, succeeded, failed, or was cancelled
- What MSBuild or the compiler reported in Visual Studio
- Which Visual Studio instance should be targeted when several are open

VsDebugMcp Bridge provides a local MCP path for those answers, so agents can act on IDE state instead of guessing from workspace files and shell output.

> **Demo media placeholder**  
> Suggested GIF: discover an instance → list solution projects → start a build → return the failed status and Build Output.

## What you can do

### Discover Visual Studio context

- Check Bridge and Host health
- Discover the MCP capabilities currently available
- List or filter running Visual Studio instances
- Read the projects in the open solution

### Control real Visual Studio builds

- Start a solution build with the active configuration or an explicit configuration/platform
- Track asynchronous progress through a stable build task handle
- Cancel a running build
- Distinguish success, failure, cancellation, concurrency conflicts, and invalid handles

### Bring build results back to the agent

- Read raw text from Visual Studio's **Output → Build** pane
- Request only the output tail and set a maximum size
- Query diagnostics exposed by the Build source in the Error Table*

\* `vs_get_errors` is implemented, but some project systems do not expose compiler diagnostics through Visual Studio's public Error Table data source. In that case, the tool returns `diagnostics_unavailable` instead of presenting parsed Build Output as Error Table data.

### Target multiple instances safely

Each Visual Studio process registers its own `vsInstanceId`. Routing is automatic when exactly one instance is available. When several instances are running, the agent must explicitly select one to avoid acting on the wrong solution.

## Available MCP tools

| Workflow | Tools |
|---|---|
| Health and discovery | `vs_health`, `vs_capabilities` |
| Instance discovery | `vs_list_instances`, `vs_find_instances` |
| Solution context | `vs_get_projects_in_solution` |
| Build lifecycle | `vs_run_build`, `vs_get_build_status`, `vs_cancel_build` |
| Diagnostics and output | `vs_get_errors`, `vs_get_output_window_logs` |

## Get started in five minutes

### 1. Install the extension

Install the **VsDebugMcp Bridge** VSIX, then restart Visual Studio.

### 2. Open a solution

Open the solution you want the agent to use. When the VSIX loads, it automatically starts the packaged self-contained Host. There is no separate Host installation or manual startup step.

### 3. Configure your MCP client

```json
{
	"servers": {
		"vs-debug-mcp": {
			"type": "http",
			"url": "http://127.0.0.1:43260"
		}
	},
	"inputs": []
}
```

### 4. Verify the connection

Ask the agent to call:

1. `vs_health`
2. `vs_list_instances`
3. `vs_capabilities`
4. `vs_get_projects_in_solution`

Reload the MCP client after tool definitions change so it can discover the updated tools.

## A typical workflow

```text
Discover Visual Studio instances
	→ select a vsInstanceId
	→ read solution projects
	→ start a build with the active configuration
	→ poll status using buildTaskId
	→ read Build Output
	→ continue with the real compiler result
```

Build task state lives in the Visual Studio Bridge and does not depend on the lifetime of an individual MCP request or Named Pipe connection.

## How it works

```text
MCP Client / AI Agent
	→ Streamable HTTP on 127.0.0.1:43260
	→ Shared VsDebugMcp.Host
	→ vsInstanceId Registry and Router
	→ Per-instance Named Pipe RPC
	→ Visual Studio VSIX Bridge
	→ Visual Studio
```

- MCP clients use one stable local URL.
- One Host is shared per Windows user, while each Visual Studio instance has a separate Bridge pipe.
- The VSIX sends a heartbeat every 5 seconds; the Host removes an instance after 15 seconds without a heartbeat.
- The Host exits after the final Visual Studio instance is removed.

## Local-first security boundary

- HTTP binds only to IPv4 loopback `127.0.0.1:43260`
- Host control and Bridge pipes are restricted to the current Windows user
- No external network binding or remote Visual Studio access
- No arbitrary command execution, process control, or file editing in the current release
- Logs must not contain request payloads, credentials, environment variables, or raw Visual Studio Copilot logs

## Current status

| Status | Capabilities |
|---|---|
| End-to-end validated | Automatic Host startup, MCP tool discovery, multi-instance routing, project discovery, build start/status/cancel, Build Output |
| Implemented, still under validation | Error Table build diagnostics |
| Not available yet | Debugger, tests, file read/search/edit, remote access, general process control |

This is an Early Access project. Tool contracts and capabilities may evolve as validation continues.

## Roadmap

Future validation tracks include:

- File reading and code search
- Test discovery and test execution
- Debug sessions, threads, call stacks, breakpoints, and expression evaluation
- Broader IDE diagnostics and output providers

These are planned directions, not features available in the current release, and no delivery dates are promised.

## Requirements

- Visual Studio 2026 / Visual Studio 18.x
- An MCP client with Streamable HTTP support
- Windows `win-x64`
- IPv4 loopback and local Named Pipe communication

## FAQ

### Do I need to install .NET or start the Host separately?

No. The VSIX packages a `win-x64` self-contained Host and ensures that it is running when Visual Studio loads the extension.

### Can it connect to Visual Studio on another computer?

No. VsDebugMcp Bridge is intentionally local-only.

### Why can `vs_get_errors` return `diagnostics_unavailable`?

Some project systems display compiler errors in Build Output without exposing them through the public Visual Studio Error Table data source currently available to the extension. Use `vs_get_output_window_logs` to retrieve the raw build output.

### What happens if port `43260` is already in use?

The Host fails safely instead of selecting an unpredictable port or terminating the process that owns it.