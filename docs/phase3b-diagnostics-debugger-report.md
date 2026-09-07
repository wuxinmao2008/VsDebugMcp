# Visual Studio 2026 核心诊断与高级调试闭环（Phase 3B）联调与在线验收报告

## 1. 概述与目标

本次迭代完成了 Visual Studio 2026 (VS 18.x) 扩展与 MCP Host 的 **Phase 3B（核心诊断与高级调试闭环）** 研发，重点攻坚结构化错误列表提取稳定性、调试中断期异常现场深度提取以及高级断点条件/命中计数控制，并针对 `sample/SampleSolution.slnx` 靶场完成了全链路端到端在线联机验收（Online Acceptance）。

本次新增与增强的核心 MCP 工具如下：
- **`vs_get_errors`（重构增强）**：重构为双轨保底诊断提取引擎（主轨 `TableControl.Entries` 原生快照 + 次轨 Build Output 正则解析），彻底消除了 `diagnostics_unavailable` 报错；
- **`vs_debugger_get_exception_info`（新增）**：在调试断点暂停或异常停靠模式下，深度提取当前异常信息（包含 CLR 完整类名、Message、十六进制 HResult、StackTrace 源码栈帧、InnerException 及原始详情）；
- **`vs_debugger_set_breakpoints`（增强）**：扩展支持高级断点控制参数，包含条件断点类型 `conditionType`（`whenTrue` / `whenChanged`）、目标命中次数 `hitCountTarget` 及命中比较条件 `hitCountType`（`equal` / `greaterOrEqual` / `multiple`），并在设置结果中完整回显 `currentHitCount`。

至此，外部 Agent 具备了从 **“编译失败精准结构化定位错误 -> 设定高级条件与命中计数断点 -> 驱动单测调试停靠 -> 异常现场深度诊断提取 -> 恢复/停止调试”** 的高质量自愈调试闭环能力。

---

## 2. 核心技术难点攻关与问题解决

在研发与端到端实测过程中，攻克了五处核心技术难点与 API 兼容问题：

1. **`vs_get_errors` 底层事件订阅超时与 `diagnostics_unavailable` 根治**：
   - **现象**：通过 MEF `IErrorList.TableControl` 监听诊断事件时，后台分析器往往处于静默或延迟就绪状态，原有的订阅等待逻辑易超时触发 `diagnostics_unavailable`。
   - **解决方案**：重构为**双轨制提取引擎**：
     - **第一轨**：直接同步遍历 `tableControl.Entries` 的不可变快照，若已包含错误或警告则直接返回；
     - **第二轨（保底）**：若表格为空且处于编译后阶段，自动调取 Output 窗口 Build 窗格日志，通过内置双正则引擎（`MsvcOrClangRegex` 与 `MsBuildGeneralRegex`）解析形如 `1>Program.cs(10,11,10,27): error CS0103: ...` 的行，精准提取文件名、行号、列号、严重级别与错误代码，彻底保证诊断信息的稳定返回。

2. **`EnvDTE.Breakpoint` 属性只读约束**：
   - **现象**：在设置断点后，尝试修改 `Breakpoint.ConditionType`、`Breakpoint.HitCountTarget` 或 `Breakpoint.HitCountType` 会抛出 `CS0200`（属性为只读）。
   - **解决方案**：重构 `DebuggerProvider.SetBreakpointsAsync` 逻辑，在调用 `debugger.Breakpoints.Add` 时通过具名参数（`ConditionType: condType, HitCount: hitTarget, HitCountType: hitType`）在断点创建时即一次性注入高级配置。

3. **`EnvDTE.Debugger.GetExpression` 参数签名兼容**：
   - **现象**：传参 `UseHierarchy: false` 会导致编译报错 `CS1739: “GetExpression”的最佳重载没有名为“UseHierarchy”的参数`。
   - **解决方案**：修正形参名称为 `UseAutoExpandRules: false`，遵循 EnvDTE 原生调试接口规范。

4. **COM 枚举命名规范匹配**：
   - **现象**：无命中计数限制的枚举误写为 `dbgHitCountTypeNoHitCount`，调试器中断原因误用了不存在的枚举。
   - **解决方案**：修正命中类型枚举为 `dbgHitCountType.dbgHitCountTypeNone`；修正中断原因类型为 `dbgEventReason`，并分别支持 `dbgEventReasonExceptionThrown` 与 `dbgEventReasonExceptionNotHandled`。

5. **MSVC/MSBuild 日志前缀及 4 元组行列号匹配**：
   - **现象**：Visual Studio 并发构建日志每行均带有 `1>` 前缀，且部分编译器报错的坐标格式为 4 元组 `(10,11,10,27)`，导致通用正则漏匹配。
   - **解决方案**：将匹配正则升级为：
     `^(?:\d+>\s*)?(?<file>[a-zA-Z]:[\\/][^:(]+|\S[^:(]+)\((?<line>\d+)(?:,(?<col>\d+))?(?:,\d+,\d+)?\)\s*:\s*(?<severity>fatal error|error|warning)\s+(?<code>[A-Za-z0-9_]+)\s*:\s*(?<msg>.*?)(?:\s*\[(?<proj>[^\]]+)\])?$`，并在提取后统一标准化清洗。

---

## 3. 端到端在线实测验收（Online Acceptance）

在运行中的 Visual Studio 2026（VS 18.x）实验实例与部署好的 MCP Host 上，运行自动化验收套件 `scripts/test_acceptance_phase3b.py`，全流程 7 个环节全部通过：

| 验收环节 | 调用的 MCP 工具 | 预期结果 | 实测结果 | 状态 |
| :--- | :--- | :--- | :--- | :---: |
| **[1/6] 健康与能力发现** | `vs_health` & `vs_capabilities` | 状态 ok，发现全部 28 个工具，新工具 `isStub=false` | `status=ok, vsInstanceId=vs-15416-08df0c937bcdd54b`<br>发现 28 个可用能力，`vs_debugger_get_exception_info` 激活 | **PASS** |
| **[2/6] 结构化错误提取** | `vs_get_errors` | 稳定返回诊断列表，无 `diagnostics_unavailable` 报错 | `totalCount=0, returnedCount=0`（正常返回，未抛出异常） | **PASS** |
| **[3/6] 高级断点控制** | `vs_debugger_set_breakpoints` | 在 `Calculator.cs:6` 设置条件 `a > 0` 与命中计数 `1` | 返回 1 个断点：`Condition: a > 0, ConditionType: whenTrue, HitTarget: 1, warnings: []` | **PASS** |
| **[4/6] 单元测试定位** | `vs_get_tests` | 发现全部单测用例，锁定除零异常测试 | 发现 4 个用例，成功锁定 `Divide_ByZero_ThrowsException`（ID: `fc596137-817c-e8b6-3436-8efd09a4ade0`） | **PASS** |
| **[5/6] 启动单测调试** | `vs_debug_test_by_id(waitForBreak=True)` | 成功启动调试并在异常触发时立即在 Break 模式着陆 | `mode: break, breakReason: exception_unhandled`<br>栈顶帧: `SampleApp.Services.Calculator.Divide` | **PASS** |
| **[6/6] 异常现场诊断** | `vs_debugger_get_exception_info` | 深度提取当前异常类型、描述、堆栈 | `HasException: True`<br>`Type: System.DivideByZeroException`<br>`Message: Attempted to divide by zero.`<br>`StackTrace:` 精准指向 `Calculator.cs:line 7` | **PASS** |
| **[收尾] 优雅终止与恢复** | `vs_debugger_stop` & `vs_debugger_get_info` | 终止调试，平稳回到设计模式 | `mode: design, isDebugging: False, breakpointCount: 3` | **PASS** |

---

## 4. 自动化单元测试与代码质量指标

- **`VsDebugMcp.Protocol.Tests`**：10 / 10 PASS (100%)
  - 高级断点协议扩展字段序列化与反序列化测试全部通过；
  - 异常现场诊断响应契约（`DebuggerGetExceptionInfoResponse`）结构验证通过。
- **`VsDebugMcp.Host.Tests`**：68 / 68 PASS (100%)
  - MCP 工具 Schema 注册（`vs_debugger_get_exception_info`）与参数默认值校验通过；
  - 高级断点参数（`conditionType`, `hitCountTarget`, `hitCountType`）Schema 校验通过。
- **项目总计自动化测试**：**78 / 78 PASS (100%)**。
- **构建状态**：MSBuild 0 警告，0 错误。
- **版本号同步**：全模块统一升级至 `0.1.10.0`。
