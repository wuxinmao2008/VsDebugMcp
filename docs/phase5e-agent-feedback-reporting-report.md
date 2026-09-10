# Phase 5E 验收报告：智能体交互式反馈与诊断报告体系 (Agent Interactive Feedback & Reporting)

> **报告版本**: v0.1.20.0  
> **验收日期**: 2026-09-10  
> **工具总数**: 51 个 MCP Tools (新增 1 个，全量 `isStub: false`)  
> **状态**: 已完成并在 VS 2026 Experimental Instance 真实环境与 GitHub 线上仓库验证通过

---

## 1. 目标与设计原则

在 AI Agent 协同开发过程中，Agent 可能会因调试器状态未就绪、复杂混淆表达式超时、环境上下文不匹配或协议边界问题遭遇工具摩擦（Tool Friction）。传统 MCP 实现往往只能返回晦涩的错误文本，导致 Agent 陷入死循环尝试，用户也无从获知底层 IDE 实际发生的状态。

Phase 5E 引入专有的智能体主动反馈通道 `vs_report_mcp_issue`，构建了一套兼顾**主动自愈协助**与**极致隐私合规**的问题反馈闭环机制：

1. **主动回路断路器与 DLP 隐私护栏**：在将任何诊断内容输出前，执行严格的敏感数据探测（JWT、SSH 私钥、GitHub/API Token、未脱敏的 Windows 用户物理路径）。一旦检测到隐私泄露风险，主动熔断拒绝上报（`privacy_risk_aborted`），杜绝任何密钥或凭据外泄。
2. **零隐蔽外联（Zero Covert Telemetry）**：恪守本地优先与用户知情权原则，Host 不在后台隐式向任何云端发送分析数据或调用远程 API。而是将脱敏后的诊断报告导出至本地，并生成经 URL 安全编码的免密预填 GitHub Issue 链接交由用户确认点击。
3. **输入参数安全截断**：将智能体输入的描述、错误信息与复现步骤严格限制在 512 字符以内，附加上下文限制在 1024 字符，防止 Prompt 注入、缓冲区耗尽或上下文污染。
4. **双轨预填兼容机制**：针对 GitHub Issue Form 的 Query 参数机制，采用 Form 字段精准绑定（`category`, `tool_name`, `error_message` 等）与 `title` / `body` 降级兜底双轨编码，确保在不同客户端与网页端均能 100% 完整复现诊断现场。

---

## 2. 工具定义与协议规范

### 2.1 `vs_report_mcp_issue`
- **功能**: 当 Agent 遭遇不可恢复的工具失败或 IDE 状态死锁时，主动收集系统级与 IDE 级诊断信息，导出本地审计报告，并生成预填的 GitHub Issue 反馈链接。
- **协议特征**: ReadOnly=false, Idempotent=false。
- **输入参数**:
  - `toolName` (string, 必需): 发生摩擦或异常的 MCP 工具名（如 `vs_debugger_evaluate_expr`，最大 128 字符）。
  - `category` (string, 必需): 标准化问题分类（支持 `internal_exception`, `transport_timeout`, `protocol_mismatch`, `serialization_failure`, `schema_ambiguous`, `invalid_tool_result`, `output_too_large`, `performance_degradation`, `unsupported_state`, `feature_gap`, `unknown`）。
  - `description` (string, 必需): 问题的详细描述（最大 512 字符）。
  - `errorMessage` (string?): 捕获到的原始错误或异常信息（最大 512 字符）。
  - `reproductionSteps` (string?): 简要复现步骤说明（最大 512 字符）。
  - `contextData` (string?): 附加的只读上下文 JSON 片段（最大 1024 字符）。
  - `vsInstanceId` (string?): 发生问题的目标 Visual Studio 实例 ID。
- **输出载荷**:
  - `success` (bool): 报告生成与 URL 构建是否成功。
  - `reportId` (string): 报告唯一标识符（如 `mcp-report-20260910-023812`）。
  - `reportPath` (string): 本地脱敏诊断报告的物理存储路径（对外使用 `%LOCALAPPDATA%` 抽象路径防物理用户名泄露）。
  - `githubIssueUrl` (string): 预填好所有字段的 GitHub Issue 网页链接。
  - `diagnosticSummary`: 包含 OS、.NET 运行时、VS 版本与活动方案的结构化系统切片。

---

## 3. 自动化测试套件验证

全套测试用例均通过断言验证，测试套件规模从 180 项扩充至 191 项（100% PASS）：

```text
1. Protocol 测试套件:
   - Command: dotnet test tests/VsDebugMcp.Protocol.Tests/VsDebugMcp.Protocol.Tests.csproj
   - Result: 已通过! 失败: 0，通过: 21，总计: 21 (100% 通过)
   - 覆盖:
     * ReportMcpIssueRequest / Response 序列化与往返测试
     * McpIssueTypes 常量与分类验证
     * PrivacyRiskAborted 错误码映射验证

2. Host 测试套件:
   - Command: dotnet test tests/VsDebugMcp.Host.Tests/VsDebugMcp.Host.Tests.csproj
   - Result: 已通过! 失败: 0，通过: 170，总计: 170 (100% 通过)
   - 覆盖 (新增 11 个专项测试):
     * GenerateReport_BuildsValidGitHubUrlAndLocalReport (端到端报告与 URL 生成)
     * DetectPrivacyViolationRisk_BlocksWhenSensitiveTokensDetected (拦截 JWT / 私钥 / PAT / 明文物理用户名)
     * GenerateReport_TruncatesOverlyLongInputs (512 字符安全截断防护)
     * GenerateReport_RedactsUserHomePaths (物理路径自动转写为抽象路径)
     * All51ToolsAreRegisteredOnMcpTools (断言 51 个工具全量注册与元数据完备性)
```

---

## 4. Visual Studio 2026 与 GitHub 真实联机验收记录

- **环境宿主**: Visual Studio 2026 (18.9.12120.119), Experimental Instance (PID: 6116).
- **测试解决方案**: `SampleSolution.slnx`
- **MCP 插件版本**: `0.1.20.0` (Host PID: 8252, 端口: `127.0.0.1:43260`).

### 4.1 健康与能力集自省
- 调用 `vs_health`: 返回 `hostVersion: 0.1.20.0`，已连接的 Bridge 实例状态良好。
- 调用 `vs_capabilities`: 确认当前注册工具数达 **51 个**，`vs_report_mcp_issue` 处于激活状态（`isStub: false`）。

### 4.2 智能体主动报告触发
AI 智能体发起调用：
```json
{
  "toolName": "vs_debugger_evaluate_expr",
  "category": "unsupported_state",
  "description": "这是一个验证问题反馈的测试",
  "errorMessage": "Evaluation timed out",
  "reproductionSteps": "1. Start debugging\n2. Step over\n3. Evaluate expression"
}
```

- **响应返回**:
  - `success`: `true`
  - `reportId`: `"mcp-report-20260910-023812"`
  - `reportPath`: `"%LOCALAPPDATA%\\VsDebugMcp\\Reports\\mcp-report-20260910-023812.md"`
  - `githubIssueUrl`: `https://github.com/wuxinmao2008/VsDebugMcp/issues/new?template=tool-friction.yml&tool_name=vs_debugger_evaluate_expr&category=unsupported_state&...`

### 4.3 现场工程实战演进与踩坑解决
- **遇到的实际摩擦**:
  GitHub Issue Forms 对 URL Query 参数的支持中，`input` 与 `textarea` 可以完美解析 `field_id=value`；但 `dropdown` 下拉组件在 URL 预填时存在前端已知限制，若未在页面手动交互选择，会默认停留在 `None`，并在提交时触发 GitHub 校验错误：`An option must be selected`。
- **优雅解决方案**:
  将 `.github/ISSUE_TEMPLATE/tool-friction.yml` 中的 `category` 字段从 `dropdown` 调整为带格式提示与校验的 `input` 文本框（`type: input`，`placeholder: "e.g. unsupported_state"`），并保持 URL 生成逻辑的 `title` + `body` 双轨降级，一举彻底解决了预填阻断问题。

### 4.4 GitHub Issue 线上端到端闭环验证
用户点击生成的免密链接，确认表单全自动预填完整无误，成功提交真实 GitHub Issue：
- **目标 Issue**: [Issue #1: [Tool Friction]: 这是一个验证问题反馈的测试](https://github.com/wuxinmao2008/VsDebugMcp/issues/1)
- **回读校验**: AI 智能体通过网络工具读取线上 Issue #1，校验提取以下内容：
  - 标题: `[Tool Friction]: 这是一个验证问题反馈的测试`
  - 工具名: `vs_debugger_evaluate_expr`
  - 问题分类: `unsupported_state`
  - 错误详情与复现步骤完整无损。
  - 环境诊断信息自动补全：OS (Windows 11 64-bit), Visual Studio (2026 18.x), MCP Host (v0.1.20.0), Solution (`SampleSolution.slnx`)。

---

## 5. 验收结论

Phase 5E 达成全部设计指标：
1. 实现了基于 MCP 标准规范的 `vs_report_mcp_issue` 原生工具。
2. 建立起严密的 DLP 隐私数据防线与本地审计归档。
3. 打通了从 Agent 工具遇挫 -> 收集脱敏诊断 -> 导出本地报告 -> 免密预填 GitHub Issue -> 用户审核提交的完整自动化链路。
4. 全量自动化测试 191/191 PASS，真实线上联调确认无误。
