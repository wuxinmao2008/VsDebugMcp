# VsDebugMcp

**Connect Visual Studio (VS 2017 ~ VS 2026) to AI Agents via Model Context Protocol (MCP).**

通过 MCP 协议将 Visual Studio（全版本覆盖 VS 2017、VS 2019、VS 2022 及最新的 VS 2026 / 18.x）连接到外部 AI Agent，为智能助手提供 IDE 级的项目工程树、构建控制、双轨编译诊断、全生命周期混合调试、进程虚拟内存透视与测试资源管理器联动能力。

[简体中文](#简体中文) | [English](#english)

---

<a id="简体中文"></a>

## 这是你需要的扩展吗？

如果你在日常开发中使用支持 MCP 的 AI 编程助手（如 **Cursor**、**VS Code**、**Claude Desktop**、**Antigravity**、**Windsurf / Codex** 等），并且在 Visual Studio 中开发 C++、Qt、C# / .NET 或工控多进程项目：

- **传统 Agent 的严重局限**：
  - 只能看到磁盘上的静态文件，无法获知 Visual Studio 当前激活的构建配置（Debug/Release、x64 等）；
  - 经常在终端盲目运行 `dotnet build` 或 `msbuild`，因缺少 VS 专用环境与依赖上下文导致命令行构建失败；
  - 遇到崩溃只能反复猜测打印，无法下断点、读调用栈、单步步过或查看内存数据。
- **VsDebugMcp 的核心价值**：
  - 在 Visual Studio 与外部 AI Agent 之间建立高性能双向桥梁；
  - 让 Agent 能够直接掌控 Visual Studio 原生能力——读取工程树、触发 IDE 构建、捕获原始 Build 日志、自动下断调试、单步单帧排查、单次原子快照捕获，甚至直接读取目标进程的裸虚拟内存。

---

## 核心架构与安全边界

VsDebugMcp 采用工业级混合架构设计（**Hybrid OOP Host + VSIX Bridge**）：

```text
MCP 客户端 (Cursor / Claude / VS Code)
  └─ Streamable HTTP (仅限本机 127.0.0.1:43260)
       └─ VsDebugMcp.Host (.NET 8 框架依赖进程，复用 VS 内置运行时)
            ├─ 实例注册表与多实例智能路由 (FindByWorkingDirectory)
            ├─ 互斥并发保护与内核 Job Object 生命周期守护
            └─ 用户级安全 ACL 命名管道 RPC
                 └─ Visual Studio VSIX Bridge (运行在 devenv.exe 内部)
                      ├─ 构建与输出提供者 (Build / Output / ErrorList)
                      ├─ 调试器核心与内存透视 (Debugger / Win32 Virtual Memory)
                      └─ 测试资源管理器 (Test Explorer MEF Broker)
```

- **零云端外泄**：HTTP 严格绑定本机 IPv4 回环（`127.0.0.1:43260`），命名管道由当前 Windows 用户安全 ACL 保护，严禁任何远程与跨网卡访问；
- **防卡死与防死锁**：构建前自动阻断调试态模态弹窗；所有长耗时操作均为异步句柄轮询；子进程挂载 Windows Job Object 确保随 VS 退出自动级联回收；
- **克制边界**：不重复提供 Agent 原生已有的全盘检索与文本读写能力，专注提供 Visual Studio 独占的 IDE 状态。

---

## 实际运行效果

下图展示了 AI Agent（以 VS Code 为例）在执行代码变更后，自主调用 `vs_run_build` 触发构建，通过 `vs_get_build_status` 轮询进度，结合 `vs_get_errors` 与 `vs_get_output_window_logs` 读取构建日志并完成诊断闭环：

![AI Agent 工作流截图](assets/screenshot_01.png)

---

## MCP 工具全景清单 (51 个标准工具)

扩展向 AI Agent 完整暴露 **51 个标准 MCP 工具**（全量已实现并实测验证，无空壳）：

### 1. 服务与多实例管理 (4 个工具)
| 工具名称 | 只读 | 功能描述 |
|---|:---:|---|
| `vs_health` | 是 | 检查 MCP 服务健康状态、版本号及与 Visual Studio 实例的连接状态 |
| `vs_capabilities` | 是 | 查询当前已加载并激活的 IDE MCP 能力集清单（全部 `isStub: false`） |
| `vs_list_instances` | 是 | 列出本机当前运行的所有 Visual Studio 实例（PID、启动时间、解决方案路径） |
| `vs_find_instances` | 是 | 根据解决方案物理路径或目录模糊匹配定位目标 Visual Studio 实例 |

### 2. 解决方案与工程上下文 (4 个工具)
| 工具名称 | 只读 | 功能描述 |
|---|:---:|---|
| `vs_get_projects_in_solution` | 是 | 获取当前打开解决方案中的所有项目信息（项目名、路径、GUID） |
| `vs_get_files_in_project` | 是 | 高效提取工程源码文件树，支持 C++ 筛选器（Filters）结构与扩展名过滤 |
| `vs_get_solution_configurations` | 是 | 查询解决方案的所有配置与平台组合（Debug/Release、x64 等）及活动配置 |
| `vs_set_solution_configuration` | 否 | 动态激活并切换解决方案配置与平台（带调试态防死锁保护） |

### 3. 构建生命周期与输出日志 (6 个工具)
| 工具名称 | 只读 | 功能描述 |
|---|:---:|---|
| `vs_run_build` | 否 | 非阻塞触发全方案或单工程构建，返回异步跟踪句柄 `buildTaskId` |
| `vs_get_build_status` | 是 | 轮询构建任务进度（starting, running, succeeded, failed, cancelled） |
| `vs_cancel_build` | 否 | 及时取消正在执行中的后台构建任务 |
| `vs_get_output_window_logs` | 是 | 获取 Visual Studio 输出窗口指定窗格（生成、调试等）文本，支持截断控制 |
| `vs_get_output_panes` | 是 | 自省枚举输出窗口中所有活动窗格（含生成、调试及用户自定义 UILOG 等） |
| `vs_get_errors` | 是 | 查询错误列表诊断，支持 Error Table 原生提取与 Build Output 双正则双轨保底 |

### 4. 编辑器协同与代码导航 (2 个工具)
| 工具名称 | 只读 | 功能描述 |
|---|:---:|---|
| `vs_get_active_document` | 是 | 探测当前前台激活的代码文档、光标行列号与选中文本 |
| `vs_navigate_to` | 否 | 在 VS 编辑器前台打开指定源文件并平滑停靠光标至目标行列 |

### 5. 调试生命周期与进程附加 (12 个工具)
| 工具名称 | 只读 | 功能描述 |
|---|:---:|---|
| `vs_debugger_start` | 否 | 设计模式下程序化启动调试（F5），支持自动着陆探测（`waitForBreak`） |
| `vs_debugger_continue` | 否 | 恢复被调试进程执行（Continue），直至下一断点或退出 |
| `vs_debugger_pause` | 否 | 暂停正在运行中的被调试目标进程（进入中断模式） |
| `vs_debugger_stop` | 否 | 终止调试会话，平稳安全返回设计模式 |
| `vs_debugger_step_over` | 否 | 单步步过（Step Over）当前语句，即时回显着陆顶层栈帧 |
| `vs_debugger_step_into` | 否 | 单步步入（Step Into）目标函数内部 |
| `vs_debugger_step_out` | 否 | 单步步出（Step Out）至当前函数调用方 |
| `vs_debugger_get_processes` | 是 | 枚举本机运行中系统进程，支持模糊匹配与仅看被调试进程过滤 |
| `vs_debugger_find_solution_processes` | 是 | 智能扫描解决方案工程输出，自动匹配属于当前方案的运行进程 |
| `vs_debugger_attach_process` | 否 | 附加到指定进程，显式支持原生/托管/混合（Mixed-mode）引擎 |
| `vs_debugger_auto_attach` | 否 | 一键免 PID 自动发现并批量附加属于当前解决方案的运行进程 |
| `vs_debugger_detach` | 否 | 安全脱钩调试器，保留目标进程在操作系统中继续独立存活运行 |

### 6. 调试状态、断点与异常现场 (12 个工具)
| 工具名称 | 只读 | 功能描述 |
|---|:---:|---|
| `vs_debugger_get_info` | 是 | 查询调试器模式（design/run/break）、活动 PID/TID 及断点总数 |
| `vs_debugger_get_threads` | 是 | 捕获调试进程多线程快照（TID、线程名、存活状态及 `isFrozen` 标记） |
| `vs_debugger_freeze_thread` | 否 | 挂起（冻结）指定线程，用于排查或隔离多线程竞态条件 |
| `vs_debugger_thaw_thread` | 否 | 解冻并恢复指定工作线程的执行 |
| `vs_debugger_get_call_stack` | 是 | 捕获活动线程调用栈，强类型提取源文件名、行号、列号与用户代码标记 |
| `vs_debugger_set_next_statement` | 否 | 中断期间动态调整程序计数器（IP），跳过崩溃行或重执语句 |
| `vs_debugger_set_breakpoints` | 否 | 在源码精准下断，支持条件断点（whenTrue/whenChanged）与命中计数过滤 |
| `vs_debugger_list_breakpoints` | 是 | 结构化查询当前方案所有断点清单、条件配置、命中次数与绑定状态 |
| `vs_debugger_clear_breakpoints` | 否 | 具备安全参数防卫的断点删除（支持一键清空、按文件清空或按 ID 精确删除） |
| `vs_debugger_toggle_breakpoint` | 否 | 独立启停/反转指定断点的启用状态，保留原有条件与规则 |
| `vs_debugger_get_exception_info` | 是 | 中断现场深度提取 CLR/SEH 异常类型、Message、十六进制 HResult 与堆栈 |
| `vs_debugger_get_modules` | 是 | 枚举目标进程已加载模块清单（基址、大小、版本、路径及 PDB 加载状态） |

### 7. 深度诊断、内存透视与表达式求值 (5 个工具)
| 工具名称 | 只读 | 功能描述 |
|---|:---:|---|
| `vs_debugger_get_snapshot` | 是 | **单次原子聚合调试诊断现场**（进程/线程/顶层帧/完整调用栈/局部变量/最近日志） |
| `vs_debugger_read_memory` | 是 | **Win32 虚拟内存安全透视**，支持绝对地址与指针表达式，输出 HexDump 与 Base64 |
| `vs_debugger_evaluate_expr` | 是 | 在指定栈帧安全求值单个表达式，带超时控制与 Release 优化智能建议 |
| `vs_debugger_evaluate_expressions` | 是 | 单次 RPC 批量求值多个表达式，支持单项错误隔离 |
| `vs_debugger_get_locals` | 是 | 自动识别并提取当前栈帧所有形参（Arguments）与局部变量（Locals） |

### 8. 测试资源管理器与单测驱动调试 (5 个工具)
| 工具名称 | 只读 | 功能描述 |
|---|:---:|---|
| `vs_get_tests` | 是 | 发现解决方案内所有单元测试用例，支持按名称/类名/命名空间过滤 |
| `vs_run_tests` | 否 | 异步发起单测执行（支持全量运行或指定测试 ID 集合过滤执行） |
| `vs_get_test_run_status` | 是 | 轮询测试运行生命周期、通过/失败计数、总耗时及各测试项独立结果 |
| `vs_cancel_test_run` | 否 | 即时取消正在运行中的测试任务 |
| `vs_debug_test_by_id` | 否 | 专有单测调试通道，下断触发测试调试，支持自动着陆与栈顶帧即时返回 |

### 9. 智能体交互与问题反馈 (1 个工具)
| 工具名称 | 只读 | 功能描述 |
|---|:---:|---|
| `vs_report_mcp_issue` | 否 | 智能体交互式摩擦与缺陷报告，内置 DLP 隐私脱敏、本地诊断导出与免密预填 GitHub Issue |

---

## 快速上手与客户端配置指南

### 1. 安装插件
在 Visual Studio 市场中搜索并安装 `VsDebugMcp`（或双击下载的 `.vsix` 文件安装），并重启 Visual Studio。

### 2. 打开工程并验证服务
在 Visual Studio 2026 / 2022 中打开你的解决方案。扩展加载后会在后台自动拉起本地 Host 服务（监听 `127.0.0.1:43260`）。
在 Visual Studio 顶部菜单栏点击 **`扩展 (Extensions) -> VsDebugMcp`**，即可查看当前服务状态与一键复制配置。

### 3. 配置你的 MCP 客户端

#### 方案 A：VS Code
在工作区 `.vscode/mcp.json`（或全局配置）中添加：
```json
{
  "mcpServers": {
    "vs-debug-mcp": {
      "type": "http",
      "url": "http://127.0.0.1:43260"
    }
  }
}
```

#### 方案 B：Cursor
在项目根目录 `.cursor/mcp.json` 中配置：
```json
{
  "mcpServers": {
    "vs-debug-mcp": {
      "url": "http://127.0.0.1:43260"
    }
  }
}
```

#### 方案 C：Claude Desktop
编辑 `%APPDATA%\Claude\claude_desktop_config.json`：
```json
{
  "mcpServers": {
    "vs-debug-mcp": {
      "url": "http://127.0.0.1:43260"
    }
  }
}
```

#### 方案 D：Antigravity / Windsurf
直接配置本地 HTTP 接入点即可：
`http://127.0.0.1:43260`

---

## 工业工控与复杂桌面应用实战案例

### 案例 1：Qt 跨线程对象所有权与亲和性（Thread Affinity）排查
在多线程工控采集中，界面偶发卡死或报 `QObject::killTimer: Timers cannot be stopped from another thread`：
1. Agent 调用 `vs_debugger_get_snapshot()`：单次原子获取崩溃时的调用栈与最近 30 条 `qDebug` 日志；
2. Agent 执行 `vs_debugger_evaluate_expr(expr="((QObject*)pTarget)->thread()")` 获取对象归属 QThread 指针；
3. Agent 执行 `vs_debugger_evaluate_expr(expr="QThread::currentThread()")` 获取当前执行线程指针；
4. 比对二者地址，Agent 立即精确定位出非法跨线程直接调用缺陷。

### 案例 2：PLC / Modbus 通信接收缓冲区内存透视
在工业网络通信中，驱动层常以原生裸指针（如 `char* m_rxBuffer`）存储原始报文字节流，符号求值往往因内联或剥离无法展开：
1. Agent 调用 `vs_debugger_read_memory(address="m_rxBuffer", byteCount=64)`；
2. 工具直接通过 Win32 `ReadProcessMemory` 抓取裸内存，返回 16 字节对齐的经典 HexDump 及 ASCII 预览栏；
3. Agent 直接阅读十六进制报文（如从站地址、功能码、寄存器起始地址与 CRC 校验码），秒级完成通信时序逆向排查。

---

<a id="english"></a>

## Is this extension for you?

If you use MCP-compatible AI agents (such as **Cursor**, **VS Code**, **Claude Desktop**, **Antigravity**, **Windsurf / Codex**) while developing C++, Qt, C# / .NET, or multi-process industrial automation applications in Visual Studio:

- **The Problem with General Agents**:
  - Confined to static disk files, unable to perceive which build configuration is active in Visual Studio (Debug vs. Release, x64 vs. ARM64);
  - Blindly executing `dotnet build` or `msbuild` in the terminal frequently fails due to missing Visual Studio SDK toolchains and environmental context;
  - When encountering runtime crashes or deadlocks, general agents can only guess without breakpoint control, call stacks, stepping, or memory visibility.
- **The VsDebugMcp Solution**:
  - Constructs a high-performance, bidirectional bridge between Visual Studio and external AI agents;
  - Empowers your agent to inspect solutions, control IDE builds, capture Build/Debug output panes, set advanced breakpoints, step through code, take aggregated diagnostic snapshots, and inspect raw virtual memory spaces;
  - Offers full version coverage spanning **Visual Studio 2017, 2019, 2022, and 2026** with zero third-party DLL dependencies.

---

## Architecture & Security Model

VsDebugMcp follows an enterprise-grade **Hybrid Architecture (OOP Host + VSIX Bridge)**:

- **Local-Only & Zero Cloud Leaks**: Standard Streamable HTTP transport bound exclusively to loopback `127.0.0.1:43260`. The internal RPC uses Windows Named Pipes protected by current-user OS ACLs. No external or remote network traffic is accepted;
- **Deadlock & UI Freeze Prevention**: Automatically rejects build requests during debugging to prevent blocking modal dialogs. Long-running builds and test runs use asynchronous task handles. The Host process is bound to a Windows Job Object to guarantee automatic cleanup when Visual Studio exits;
- **Non-Redundant Tool Boundary**: Omits generic disk reading/writing and full-text grepping (delegated natively to client agents), concentrating on Visual Studio's exclusive IDE contexts.

---

## Available MCP Tools (51 Tools Total)

The extension exposes **51 production-ready MCP tools** across 9 core domains:

1. **Service & Multi-Instance Routing (4 tools)**: `vs_health`, `vs_capabilities`, `vs_list_instances`, `vs_find_instances`.
2. **Solution & Project Context (4 tools)**: `vs_get_projects_in_solution`, `vs_get_files_in_project`, `vs_get_solution_configurations`, `vs_set_solution_configuration`.
3. **Build Lifecycle & Logs (6 tools)**: `vs_run_build`, `vs_get_build_status`, `vs_cancel_build`, `vs_get_output_window_logs`, `vs_get_output_panes`, `vs_get_errors`.
4. **Editor Collaboration & Navigation (2 tools)**: `vs_get_active_document`, `vs_navigate_to`.
5. **Debugger Lifecycle & Process Attach (12 tools)**: `vs_debugger_start`, `vs_debugger_continue`, `vs_debugger_pause`, `vs_debugger_stop`, `vs_debugger_step_over`, `vs_debugger_step_into`, `vs_debugger_step_out`, `vs_debugger_get_processes`, `vs_debugger_find_solution_processes`, `vs_debugger_attach_process`, `vs_debugger_auto_attach`, `vs_debugger_detach`.
6. **Debugger Diagnostics & Breakpoints (12 tools)**: `vs_debugger_get_info`, `vs_debugger_get_threads`, `vs_debugger_freeze_thread`, `vs_debugger_thaw_thread`, `vs_debugger_get_call_stack`, `vs_debugger_set_next_statement`, `vs_debugger_set_breakpoints`, `vs_debugger_list_breakpoints`, `vs_debugger_clear_breakpoints`, `vs_debugger_toggle_breakpoint`, `vs_debugger_get_exception_info`, `vs_debugger_get_modules`.
7. **Deep Inspection, Memory & Evaluation (5 tools)**: `vs_debugger_get_snapshot`, `vs_debugger_read_memory`, `vs_debugger_evaluate_expr`, `vs_debugger_evaluate_expressions`, `vs_debugger_get_locals`.
8. **Test Explorer & Test-Driven Debugging (5 tools)**: `vs_get_tests`, `vs_run_tests`, `vs_get_test_run_status`, `vs_cancel_test_run`, `vs_debug_test_by_id`.
9. **Agent Feedback & Diagnostic Reporting (1 tool)**: `vs_report_mcp_issue`.

---

## Quick Start Configuration

Add the local endpoint to your MCP client configuration:

```json
{
  "mcpServers": {
    "vs-debug-mcp": {
      "url": "http://127.0.0.1:43260"
    }
  }
}
```

Once configured, your agent can discover and invoke Visual Studio capabilities immediately.