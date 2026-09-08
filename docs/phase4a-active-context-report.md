# Visual Studio 2026 活动上下文与编辑器协同导航（Phase 4A）联调与在线验收报告

## 1. 概述与目标

本次迭代完成了 Visual Studio 2026 (VS 18.x) 扩展与 MCP Host 的 **Phase 4A（活动上下文与编辑器协同导航）** 研发与全链路实测。通过打通 Visual Studio 前台活动文档状态感知、光标选区捕获、文件精准导航定位与解决方案构建配置枚举，外部 Agent 具备了与开发者同屏交互的“视线感知”与“焦点指引”能力。

本次新增的 3 个核心 MCP 工具如下：
- **`vs_get_active_document`**：探测当前前台活跃聚焦的代码文档，提取物理路径、文件名、脏标记（未保存修改）、只读属性、语言类型、总行数、光标 1-based 行列位置、以及选区范围和选中文本（施加 10,000 字符安全上限保护）；
- **`vs_navigate_to`**：根据物理绝对路径或解决方案相对路径在 Visual Studio 中打开并前台激活目标源码文件，并将光标精准停靠至指定的行与列；
- **`vs_get_solution_configurations`**：枚举当前打开解决方案的所有构建配置与平台组合（如 `Debug|Any CPU`、`Release|x64`），并识别当前 IDE 激活的活动配置组合。

---

## 2. 核心技术实现细节

1. **活动文档与选区安全探测**：
   - 基于 `EnvDTE.DTE.ActiveDocument` 与底层 `TextDocument` / `TextSelection`；
   - 提取行号 `selection.CurrentLine`、列号 `selection.CurrentColumn` 以及选中文本 `selection.Text`；
   - **防御式鲁棒性设计**：若当前未打开任何文档，或当前活动窗口为非文本编辑器（如设计视图、工具窗口），安全返回 `hasActiveDocument: false`，杜绝 COM 异常崩溃。
2. **跨格式路径自适应与平滑导航**：
   - 支持绝对路径与解决方案相对路径（自动拼合 `Path.GetDirectoryName(solution.FullName)`）；
   - 前置校验目标文件物理存在性，不存在时抛出结构化错误码 `file_not_found`；
   - 调用 `dte.ItemOperations.OpenFile` 并执行 `window.Activate()` 激活前台；
   - 调度 `selection.GotoLine(line, true)` 与 `selection.MoveToDisplayColumn(line, column, false)` 平滑落焦。
3. **解决方案构建配置与平台枚举**：
   - 遍历 `dte.Solution.SolutionBuild.SolutionConfigurations` 集合；
   - 提取配置名 `ConfigurationName`、平台名 `PlatformName` 与全名 `Name`；
   - 与 `dte.Solution.SolutionBuild.ActiveConfiguration` 比对，标记 `isActive: true`。

---

## 3. 端到端在线实测验收（Online Acceptance）

在运行中的 Visual Studio 2026（VS 18.x）实验实例与部署好的 MCP Host 上，运行自动化验收套件 `scripts/test_acceptance_phase4a.py`，全套 6 项实测环节全部 100% 通过：

| 验收环节 | 调用的 MCP 工具 / 场景 | 预期结果 | 实测结果 | 状态 |
| :--- | :--- | :--- | :--- | :---: |
| **[1/6] 健康检查与能力发现** | `vs_health` & `vs_capabilities` | 状态 ok，总能力数达到 35 个，3 个新能力全部 `isStub=false` | `status=ok, vsInstanceId=vs-22736-08df0d478be27233`<br>发现 35 个注册能力，3 项新能力全部就绪 | **PASS** |
| **[2/6] 构建配置与平台枚举** | `vs_get_solution_configurations` | 返回方案中所有构建配置与平台，准确标识激活项 | 发现 2 个配置组合：`Debug\|Any CPU (Active)` 与 `Release\|Any CPU` | **PASS** |
| **[3/6] 相对路径导航与定位** | `vs_navigate_to(filePath="SampleApp/Services/Calculator.cs", line=7, column=5)` | 文件成功打开并激活，光标移动到第 7 行第 5 列 | `success: True, path: ...Calculator.cs, line: 7, column: 5` | **PASS** |
| **[4/6] 活动文档与光标位置校验** | `vs_get_active_document` | 准确探测到当前活动文档为 Calculator.cs，光标在第 7 行 | `hasActiveDocument: True, fileName: Calculator.cs, cursorLine: 7` | **PASS** |
| **[5/6] 跨文档切换与绝对路径导航** | `vs_navigate_to(Program.cs:10:1)` & `vs_get_active_document` | 切换到 Program.cs，活动文档与光标即刻联动更新 | `success: True, line: 10`<br>校验活动文档切换至 `Program.cs`，光标落在第 10 行 | **PASS** |
| **[6/6] 逆向异常拦截防卫** | `vs_navigate_to(NonExistent.cs)` | 导航至不存在的文件，触发前置防御拦截 | 返回结构化错误：`file_not_found: Target file does not exist.` | **PASS** |

---

## 4. 自动化测试与工程指标

- **`VsDebugMcp.Protocol.Tests`**：12 / 12 PASS (100%)。
- **`VsDebugMcp.Host.Tests`**：101 / 101 PASS (100%)。
- **全项目自动化单元测试**：**113 / 113 PASS (100%)**。
- **版本发布**：全组件统一提升至 **`0.1.12.0`**。
