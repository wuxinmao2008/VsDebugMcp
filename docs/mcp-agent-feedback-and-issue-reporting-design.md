# VsDebugMcp Agent 交互式工具反馈与诊断报告方案设计

> **文档版本**: v1.1.0  
> **更新日期**: 2026-09-10  
> **文档状态**: 架构定稿（Approved / Official Architecture Specification）  
> **适用范围**: `VsDebugMcp` 全仓库生态（Host / VSIX Bridge / MCP Protocol / Agent 交互规范）

---

## 1. 背景与核心问题

### 1.1 现状与痛点
`VsDebugMcp` 已演进至 Phase 5D，暴露出 50 个涉及 Visual Studio 2026 深度交互的 MCP 工具（覆盖构建、诊断日志、单元测试、混合模式调试、虚拟内存读取等）。在 Agent 实际调用过程中，存在以下现实摩擦：
1. **工具可用性摩擦（Tool Friction）**：某些工具可能存在参数模式（Schema）语义模糊、返回数据体积过大导致上下文窗口溢出、IPC 管道瞬时超时、或底层 COM 接口未捕获异常。
2. **传统遥测的失真与违规困境**：
   - **硬编码遥测（Hardcoded Telemetry）无法区分是非**：静态的错误码（如 `ExitCode != 0`）无法判断是“用户业务代码编译少写了分号”，还是“插件自身的 MSBuild 管道超时”；
   - **静默后台上传存在严重的合规与法律风险**：开发者本地 IDE 运行着企业核心资产、源码、环境变量、调试内存数据，任何静默回传行为都极易触发数据泄露、违反 PIPL/GDPR、触犯企业信息安全红线，甚至导致插件被各大扩展商店下架。
3. **Agent 自动发帖的灾难（Bot Spam & Public Leak）**：若让 Agent 拥有凭据直接通过 GitHub API 自动提 Issue，极易造成上下文中的敏感信息（凭据、内部类名、私人绝对路径）被公开发布至互联网，且极易引发 Issue 刷屏和 GitHub API 封禁。

### 1.2 方案愿景与核心设计目标
基于“**以人为本（Human-in-the-Loop）**”、“**Agentic 自省（Agent Self-Reflection）**”与“**主动隐私熔断（Proactive Privacy Circuit Breaker）**”原则，构建一套**默认零后台外发、最小化披露风险、极低基础设施成本、体验优雅**的 MCP 使用反馈与诊断闭环：
- **Agent 负责“感知与归纳”**：利用大模型的上下文理解力，智能区分用户代码 Bug 与插件缺陷，并提炼技术原因；
- **Host 负责“白名单脱敏与诊断封装”**：自动注入精确的宿主/IDE/OS/SDK/项目范式等环境元数据，通过严格白名单和字段限长进行防御，构造精简的 GitHub Issue Form 预填链接；
- **用户负责“审查与最终决策”**：Agent 友好询问用户，用户拥有最终知情权与提交权，在浏览器内一键完成提单；
- **隐私第一兜底**：**当 Agent 或 Host 判定反馈可能侵犯用户隐私时，必须主动熔断并中断反馈流程**。

---

## 2. 法律、隐私与安全合规边界规范

### 2.1 网络外发承诺与准确法律语义
- **核心承诺**：未经用户主动操作，`VsDebugMcp` 不向任何外部服务传输诊断数据。
- **外发语义明确**：当用户在聊天中同意并点击浏览器链接时，URL Query 参数作为 HTTP GET 请求发送给 GitHub，**这属于第一次网络外发**。因此，**所有数据必须在生成 URL 之前就在本地达到 100% Public-Safe 标准**。Issue 的最终公开发布仍须由用户在 GitHub 页面显式点击 `Submit new issue` 提交。

### 2.2 数据分类与处置矩阵

```text
┌────────────────────────────────────────────────────────────────────────┐
│                        数据分类与合规红线                               │
├───────────────────┬──────────────────────────────────┬─────────────────┤
│ 数据分类           │ 具体内容                          │ 处置策略        │
├───────────────────┼──────────────────────────────────┼─────────────────┤
│ 严禁收集 (红线)   │ 源码内容、变量值、内存 Dump、凭据 │ 绝不出域 / 本地 │
│                   │ 用户私有 Prompt、完整调用栈代码   │ 严格过滤截断    │
├───────────────────┼──────────────────────────────────┼─────────────────┤
│ 敏感/需脱敏数据   │ 系统路径、用户名、GUID、机器环境  │ 抽象化替换      │
│                   │                                  │ (%LOCALAPPDATA%)│
├───────────────────┼──────────────────────────────────┼─────────────────┤
│ 允许收集的环境    │ VsDebugMcp 版本、VS 版本、       │ Host 自动注入   │
│ 诊断元数据        │ .NET Runtime、OS 构建版本、错误码 │ (仅用于排障)    │
│                   │ WinSDK、工具集、项目抽象范式     │                 │
├───────────────────┼──────────────────────────────────┼─────────────────┤
│ 反馈主体          │ Agent 抽象归纳的问题类型与总结    │ 严格 Schema 限制│
│                   │                                  │ (限长 512 字符) │
└───────────────────┴──────────────────────────────────┴─────────────────┘
```

### 2.3 主动隐私熔断机制（Proactive Privacy Circuit Breaker）
**核心铁律：宁可丢失一次反馈，绝不冒任何隐私泄露风险。**
1. **Agent 端主动熔断**：
   - 若 Agent 评估该工具故障的解释**高度依赖用户的私有业务逻辑、特定私有类名、敏感 SQL/网络报文，或无法在 512 字符内抽象说明**，Agent **严禁向用户提出反馈建议**，必须主动放弃提单。
   - 若用户主动要求提反馈，但内容涉及敏感数据，Agent 必须主动拒绝并解释：“由于该问题的复现涉及您的核心业务代码与私有数据，为保障您的隐私安全，我不建议且已中止生成公开反馈。”
2. **Host 端确定性硬熔断（Deterministic Circuit Breaker）**：
   - 即使 Agent 发起了调用，Host 的 Sanitizer 只要在参数中检测到高危模式（如正则匹配到私钥格式、JWT、数据库密码特征、未脱敏的绝对路径），**直接拒绝生成 GitHub URL**，状态返回 `privacy_risk_aborted`。

---

## 3. 总体架构与端到端链路设计

### 3.1 端到端交互时序图

```text
    [VS / Host]            [Agent (LLM)]             [人类开发者]           [GitHub]
         │                       │                        │                    │
  1. 出错│ 携带 JIT Hint 报错    │                        │                    │
         ├──────────────────────►│                        │                    │
         │                       │ 2. 友好协商            │                    │
         │                       ├───────────────────────►│                    │
         │                       │                        │ 3. 授权同意        │
         │                       │◄───────────────────────┤ ("可以，帮我提一个")│
         │ 4. vs_report_mcp_issue│                        │                    │
         │    (严格 Schema/限长) │                        │                    │
         │◄──────────────────────┤                        │                    │
  5. 构造│ PublicSafeReport      │                        │                    │
     脱敏│ 生成短 Form URL       │                        │                    │
         │ (隐藏真实本地路径)     │                        │                    │
         ├──────────────────────►│                        │                    │
         │                       │ 6. 呈现脱敏链接        │                    │
         │                       ├───────────────────────►│                    │
         │                       │                        │ 7. 点击链接 (第一次外发)
         │                       │                        ├───────────────────►│ (浏览器导航)
         │                       │                        │                    │ 8. Issue Form
         │                       │                        │◄───────────────────┤    自动预填就绪
         │                       │                        │ 9. 用户复核点 Submit│
         │                       │                        ├───────────────────►│ (公开发布成功)
```

---

## 4. 详细模块设计与协议规范

### 4.1 运行时即时提示引导（JIT Error Enveloping）
当 `VsMcpHost` 或 `VSIX Bridge` 内部发生不可恢复的通信超时（`NamedPipeTimeout`）、COM 内部故障（`COMException`）、序列化异常或协议不匹配时，返回统一的错误封包，附带专供 Agent 理解的指导信息：

```json
{
  "isError": true,
  "content": [
    {
      "type": "text",
      "text": "Internal Error: Named pipe connection to Visual Studio timed out after 10000ms."
    },
    {
      "type": "text",
      "text": "[System Guidance for Assistant]: This is an internal IPC timeout of the VsDebugMcp infrastructure, NOT a bug in user code. If this tool limitation blocks your debugging goal, explain the technical obstacle to the user and politely ask if they would like you to draft a GitHub issue report via 'vs_report_mcp_issue'. PRIVACY RULE: If describing this problem requires leaking user code or private context, DO NOT suggest feedback."
    }
  ]
}
```

### 4.2 工具定义：`vs_report_mcp_issue` (精细化 Schema)

#### Tool Schema
```json
{
  "name": "vs_report_mcp_issue",
  "description": "Packs a standardized diagnostic report and pre-fills a GitHub Issue Form URL when an MCP tool suffers from infrastructure failures, unclear schema, or performance bottlenecks. MUST obtain explicit user confirmation before calling. DO NOT call if privacy leakage is possible.",
  "parameters": {
    "type": "object",
    "properties": {
      "target_tool": {
        "type": "string",
        "description": "The exact MCP tool name that caused friction (e.g. 'vs_debugger_evaluate_expr')"
      },
      "issue_type": {
        "type": "string",
        "enum": [
          "internal_exception",
          "transport_timeout",
          "protocol_mismatch",
          "serialization_failure",
          "schema_ambiguous",
          "invalid_tool_result",
          "output_too_large",
          "performance_degradation",
          "unsupported_state",
          "feature_gap",
          "unknown"
        ],
        "description": "Standardized category of the friction."
      },
      "agent_summary": {
        "type": "string",
        "maxLength": 512,
        "description": "A concise technical summary (<=512 chars) of what the tool failed to deliver from the agent's perspective. STRICTLY FORBIDDEN to include proprietary code, variable contents, credentials, or private file paths."
      },
      "suggested_improvement": {
        "type": "string",
        "maxLength": 512,
        "description": "Optional constructive suggestions (<=512 chars) to improve the tool."
      }
    },
    "required": ["target_tool", "issue_type", "agent_summary"]
  }
}
```

#### 返回值 Payload（Host -> Agent）（外部信任域防护）
> **注意**：绝对不在 Tool Result 中包含本地真实用户名路径，将路径抽象为环境变量形式或使用 Report ID。

```json
{
  "status": "ready_for_user_submission",
  "report_id": "rpt_20260910_094512",
  "github_issue_url": "https://github.com/wuxinmao2008/VsDebugMcp/issues/new?template=tool-friction.yml&tool=vs_debugger_evaluate_expr&category=transport_timeout&version=0.1.19.0&vs=18.2.0&summary=...",
  "local_report_path": "%LOCALAPPDATA%\\VsDebugMcp\\reports\\rpt_20260910_094512.md",
  "instructions_for_agent": "Present the 'github_issue_url' as a clickable markdown link to the user. Inform them that all paths have been sanitized."
}
```

### 4.3 环境技术元数据自动提取规范 (Environment Diagnostic Metadata)

宿主（Host）在生成报告时，**全自动通过本地 API 提取技术特征，严禁依赖 Agent 编造，严禁包含任何私有路径与业务名称**：

| 维度 | 指标项 | 提取方式 | 安全与脱敏规则 |
| :--- | :--- | :--- | :--- |
| **操作系统** | `Windows 11 Pro 23H2 (Build 22631) x64` | `RuntimeInformation.OSDescription` | 仅保留系统版本与架构，不采机器名/MAC |
| **Visual Studio** | `VS 2026 Enterprise (18.2.0 Preview 1.0)` | `DTE.Version`, `DTE.Edition` | 确认 COM 宿主兼容性 |
| **VsDebugMcp** | `Host: 0.1.19.0 / Bridge: 0.1.19.0` | `AssemblyInformationalVersion` | 确认跨进程组件版本匹配 |
| **.NET 运行时** | `.NET 8.0.8 (win-x64, Framework-Dependent)` | `RuntimeInformation.FrameworkDescription` | 宿主运行时诊断 |
| **项目范式 (Archetype)**| `C++ Native (vcxproj)` / `C# (.NET SDK-style)` | 遍历活动工程的 Project GUID 与属性 | **强制抽象**：仅输出类型，严禁泄漏工程名与绝对路径 |
| **工具集与 SDK** | `MSVC v144 / Windows SDK 10.0.22621.0` | 读取工程 `PlatformToolset`, `TargetPlatformVersion` | 原生 C++ 符号与调试引擎排障 |
| **目标框架** | `net8.0-windows` / `net48` | 读取工程 `TargetFramework` | CoreCLR / 托管调试支持判定 |
| **活动调试引擎** | `Native` / `Managed` / `Mixed` | 调试器会话绑定引擎列表 | 混合模式调试状态验证 |

### 4.4 宿主端白名单脱敏与 GitHub Issue Form Prefill

1. **白名单模式（Allowlist Construction）**：
   - 绝不透传不可信的大段文本，仅允许 Host 生成的固定结构和 Agent 严格限长的 512 字符进入 URL；
   - 扫描并阻断（Drop/Abort）任何匹配到 `(?i)(token|bearer|secret|password|ghp_)` 或私有绝对路径的输入。
2. **短 URL 方案（基于 GitHub Issue Forms）**：
   - 在仓库配置 `.github/ISSUE_TEMPLATE/tool-friction.yml`；
   - 利用官方支持的表单 `id` 进行字段映射预填：
     ```text
     https://github.com/wuxinmao2008/VsDebugMcp/issues/new?template=tool-friction.yml&tool=vs_debugger_evaluate_expr&category=transport_timeout&version=0.1.19.0&vs=18.2.0&summary=Named+pipe+timed+out+during+evaluation
     ```
   - 彻底避免 `414 URI Too Long`，URL 控制在 200~400 字符内。
3. **本地审计日志存档**：
   - 将组装好的完整 Markdown 保存至 `%LOCALAPPDATA%\VsDebugMcp\reports\rpt_{timestamp}.md`，供离线审计查阅。

---

## 5. 全链路引导触点规范（The 4 Critical Touchpoints）

为了确保用户与 Agent 在全过程中保持完全知情、互相理解，必须在以下 4 个触点注入标准引导：

```text
┌────────────────────────────────────────────────────────────────────────┐
│                        四大引导触点架构                                │
├────────────────────────────┬───────────────────────────────────────────┤
│ 触点 1: MCP Server         │ 在 InitializeResult.instructions 中立规矩  │
│         Initialize         │ (明确区分用户代码错误 vs 插件缺陷)         │
├────────────────────────────┼───────────────────────────────────────────┤
│ 触点 2: 运行时 JIT 引导     │ 在异常 Payload 的 [System Guidance] 递话条│
│                            │ (指导 Agent 礼貌询问，禁止骚扰)           │
├────────────────────────────┼───────────────────────────────────────────┤
│ 触点 3: 用户配置与文档     │ Tools->Options 设置开关 + README 隐私章节 │
│                            │ (给用户一票否决权与完全知情权)             │
├────────────────────────────┼───────────────────────────────────────────┤
│ 触点 4: GitHub 表单告示    │ Issue Form 顶部的 [!IMPORTANT] 安全提示   │
│                            │ (用户最后点击 Submit 前的安心定心丸)       │
└────────────────────────────┴───────────────────────────────────────────┘
```

### 触点 1：MCP Server `InitializeResult.instructions`
```markdown
### Visual Studio 2026 MCP (VsDebugMcp) 协作准则
1. **问题归因原则**：
   - 若编译报错、单元测试失败、业务逻辑异常，属于用户的业务代码，请专注协助用户修复代码，严禁归咎于工具。
   - 只有当工具返回 transport_timeout、internal_exception、协议异常或输出过大导致阻塞时，才属于 VsDebugMcp 插件的基础设施问题。
2. **用户反馈协商**：
   - 严禁擅自假定用户想要反馈。在工具发生故障时，先向用户简述技术阻塞，并友善询问用户是否需要协助生成 GitHub Issue。
   - 严禁在反馈中包含用户的业务源码、文件绝对路径或变量值。若反馈不可避免地触及用户隐私，必须主动放弃提单。
```

### 触点 2：工具 Schema 与运行时 JIT 引导
在工具报错时注入系统指导语，防止 Agent 产生幻觉或过度自作主张。

### 触点 3：面向人类用户的设置开关与文档
- **README.md 专章**：《隐私承诺与 Local-First 架构说明》；
- **Visual Studio 选项（Tools -> Options -> VsDebugMcp）**：
  `[勾选]` **允许 Agent 在工具遇到故障时向我提示反馈 (Allow Agent Feedback Inquiries)**（默认开启，取消后隐藏该反馈机制）。

### 触点 4：GitHub Issue Form 顶部安全告示
```yaml
# .github/ISSUE_TEMPLATE/tool-friction.yml
name: "🛠️ VsDebugMcp 工具可用性与故障反馈"
description: "由 Agent 协助生成的 MCP 工具调用摩擦报告"
body:
  - type: markdown
    attributes:
      value: |
        > [!IMPORTANT]
        > **安全提醒**：此表单由您的本地 Agent 协助预填。在点击底部的 "Submit new issue" 前，**请务必快速浏览下方内容，确保不包含您的企业机密、业务源码或内部 Token**。
  - type: input
    id: tool
    attributes:
      label: "涉及工具"
  - type: dropdown
    id: category
    attributes:
      label: "故障分类"
      options:
        - internal_exception
        - transport_timeout
        - schema_ambiguous
        - output_too_large
        - performance_degradation
        - unknown
  - type: textarea
    id: summary
    attributes:
      label: "Agent 技术总结"
```

---

## 6. 与备选技术路线的深度对比

| 评估维度 | 方案 A: 静默后台遥测 (Sentry/SaaS) | 方案 B: Agent 自动 GitHub API 提单 | 方案 C: 本方案 (交互式预填 Issue 生成) |
| :--- | :--- | :--- | :--- |
| **隐私合规风险** | ⚠️ **高**（极易误传代码/变量，违反企业 DLP） | 🚨 **极高**（直接将私有信息公开发布全网） | 🟢 **最小化披露风险**（默认零外发，双防线脱敏） |
| **技术实现成本** | 需维护云端 Ingestion API / Sentry Token | 需在客户端分发 GitHub Token（极易泄露） | 🟢 **零额外成本**（无需云端后端，仅靠 GitHub 原生能力）|
| **Issue 质量** | 仅有抽象堆栈，缺乏调用意图上下文 | 极易高频重复刷屏，触发 GitHub Rate Limit | 🟢 **极高**（结构化技术归纳 + 自动注入完整环境） |
| **开发者心智** | 易被误判为“流氓间谍插件” | 容易引起仓库被污染和用户反感 | 🟢 **良好**（以助手身份协商，专业、克制） |

---

## 7. 实施计划与里程碑 (Phase 5E 候选)

1. **Protocol 层更新 (`VsDebugMcp.Protocol`)**：
   - 增加 `ReportMcpIssueRequest` 与 `ReportMcpIssueResponse` 模型；
   - 引入规范的 `IssueType` 强类型枚举；
   - 增加协议层单元测试（JSON 序列化、字段限长及边界校验）。
2. **Host 层实现 (`VsDebugMcp.Host`)**：
   - 注册 `vs_report_mcp_issue` 工具；
   - 实现 `DiagnosticReportService`，集成白名单生成、环境技术元数据提取（VS/OS/SDK/项目范式）、环境变量抽象（`%LOCALAPPDATA%`）和 GitHub Issue Form 短 URL 生成；
   - 完善单元测试（验证路径脱敏、隐私熔断机制与 URL 长度预算）。
3. **仓库 GitHub 模板与文档配套**：
   - 创建 `.github/ISSUE_TEMPLATE/tool-friction.yml`；
   - 在 `README.md` 中增加隐私承诺专章。
