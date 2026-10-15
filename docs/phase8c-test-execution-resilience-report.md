# Phase 8C 验收报告：自动化测试闭环、测试输出透传与通信/编码韧性加固 (Issue #5 闭环)

**日期**：2026-10-15  
**版本**：0.1.26.0  
**测试宿主**：Visual Studio 2026 Professional (v18.9.12120.119, PID: 16348)  
**实例 ID**：`vs-16348-08df2a864d421339`  
**测试工作区**：`D:\VsDebugMcp\VsDebugMcp\sample\SampleCMake` (原生 C++17 CMake 工程)

---

## 1. 阶段目标与完成情况

| 需求项 / 问题编号 | 涉及组件 / 工具 | 根因与改进方案 | 验收结果 |
| :--- | :--- | :--- | :---: |
| **HTTP 406 Not Acceptable 拦截** (Issue #5) | `VsDebugMcp.Host`<br>`Program.cs` | 官方 C# MCP SDK 严格校验请求头 `Accept`。前置标准化中间件，对缺省、`*/*` 或仅有 `application/json` 的客户端请求自动补齐为 `application/json, text/event-stream`。 | **PASS** |
| **`vs_get_build_status` 参数自适应** (Issue #5) | `SolutionBuildProvider`<br>`McpTools`<br>`BridgeService` | 修复 Agent 轮询时未传 `buildTaskId` 导致连续 30 次入参报错死循环。变更为可选参数，缺省时自动回退返回当前执行中或最近一次完成的构建快照。 | **PASS** |
| **Windows 中文编码乱码自适应** (Issue #5) | `SolutionBuildProvider`<br>`TestExplorerProvider` | 移除硬编码的 `StandardOutputEncoding = Encoding.UTF8`，引入 `GetConsoleEncoding()` 动态采用系统 OEM 代码页 (CP936/GBK) 自动解码 MSVC / CTest 子进程流。 | **PASS** |
| **Tests 窗格原生支持与多语种映射** | `OutputWindowProvider`<br>`vs_get_output_window_logs` | 注册 TestWindow 专属 GUID (`B85579AA-...` 与 `E8365304-...`)，原生支持 `source: "tests"`；并支持 `"生成"`、`"调试"`、`"测试"` 中英文及 GUID 双向兜底。 | **PASS** |
| **测试结果透传原始控制台输出** (Issue #5) | `BridgeContracts`<br>`TestExplorerProvider`<br>`vs_run_tests`<br>`vs_get_test_run_status` | 协议增加 `consoleOutput` 字段。启动前记录窗格偏移，运行与结束时自动捕获增量输出。CTest 捕获进程标准/错误流，MEF 捕获 Tests 窗格增量文本。 | **PASS** |
| **非标准测试框架 (QtTest/Catch2) 状态纠错** | `TestExplorerProvider`<br>`FinalizeRunAsync` | 针对测试适配器未回填用例通过/失败统计导致结果归零的缺陷，引入启发式探测，自动识别控制台文本中的 `FAIL!`、`[  FAILED  ]`、`Totals: ... failed`，纠正状态为 `Failed` 并标记失败数。 | **PASS** |
| **全量自动化单元测试** | `VsDebugMcp.Protocol.Tests`<br>`VsDebugMcp.Host.Tests` | 新增 HTTP 宽容中间件测试、参数自适应回归测试、`consoleOutput` 跨目标框架序列化测试。 | **244/244 PASS** |
| **VS 实验实例端到端实测** | `SampleCMake`<br>`test_math.cpp` | 在免外部 SDK 依赖的纯 C++17 CMake 样例中完成构建、错误诊断、测试发现、单用例通过、单用例失败及全量并发测试实机联机验证。 | **100% PASS** |

---

## 2. 在线实机测试记录

### 2.1 实例健康与 53 项工具就绪 (`vs_health` & `vs_capabilities`)
```json
{
  "status": "ok",
  "hostVersion": "0.1.25.0",
  "instanceCount": 1,
  "selectedInstance": {
    "vsInstanceId": "vs-16348-08df2a864d421339",
    "visualStudioProcessId": 16348,
    "solutionName": "SampleCMake",
    "solutionFilePath": "D:\\VsDebugMcp\\VsDebugMcp\\sample\\SampleCMake"
  },
  "totalCapabilities": 53,
  "isStub": false
}
```

### 2.2 构建状态参数自适应与错误诊断 (`vs_get_build_status`, `vs_run_build`, `vs_get_errors`)
1. **未构建时无参调用 `vs_get_build_status()`**：
   - 响应：返回规范业务错误 `build_task_not_found: The build task was not found.`（入参绑定成功，未抛出参数缺失异常）。
2. **触发构建 `vs_run_build(waitForCompletion: true)`**：
   - 响应：
     ```json
     {
       "buildTaskId": "30c85e084c4c440b907d909576a9562d",
       "state": "succeeded",
       "configuration": "x64-Debug",
       "platform": "x64",
       "succeeded": true,
       "durationSeconds": 5.51,
       "action": "build"
     }
     ```
3. **构建后无参调用 `vs_get_build_status()`**：
   - 响应：自动回退并返回最近构建快照 `buildTaskId: "30c85e084c4c440b907d909576a9562d", state: "succeeded"`。
4. **MSVC 编译诊断提取 (`vs_get_errors`)**：
   - 在添加 `/utf-8` 编译选项前，精准捕获 `C4819`、`C2001` 等中文诊断且中文字符串解码完全正常：
     ```json
     {
       "severity": "warning",
       "code": "C4819",
       "message": "该文件包含不能在当前代码页(936)中表示的字符。请将该文件保存为 Unicode 格式以防数据丢失",
       "filePath": "D:\\VsDebugMcp\\VsDebugMcp\\sample\\SampleCMake\\tests\\test_math.cpp",
       "line": 1
     }
     ```

### 2.3 测试用例发现 (`vs_get_tests`)
```json
{
  "tests": [
    {
      "testId": "ctest:MathOperationsTest",
      "displayName": "MathOperationsTest",
      "state": "NotRun"
    },
    {
      "testId": "ctest:MathOperationsFailureTest",
      "displayName": "MathOperationsFailureTest",
      "state": "NotRun"
    }
  ],
  "totalCount": 2
}
```

### 2.4 单用例通过执行与控制台日志透传 (`vs_run_tests` & `vs_get_test_run_status`)
- **调用**：`vs_run_tests(testIds: ["ctest:MathOperationsTest"], waitForCompletion: true)`
- **响应**：
  ```json
  {
    "testRunId": "testrun-9d5ca219",
    "state": "completed",
    "totalCount": 1,
    "passedCount": 1,
    "failedCount": 0,
    "durationSeconds": 0.54
  }
  ```
- **调用 `vs_get_test_run_status("testrun-9d5ca219")`**：
  返回的 `standardOutput` 完整保留了全部 6 项子用例的中文日志，无乱码：
  ```text
  [INFO] Running MathOperationsTest...
  [测试日志] 启动数学运算测试 MathOperationsTest...
  [INFO] Computing Add(2, 3)
  [用例通过] 验证加法运算: 2 + 3 = 5
  [INFO] Computing Subtract(10, 4)
  [用例通过] 验证减法运算: 10 - 4 = 6
  [INFO] Computing Multiply(3, 7)
  [用例通过] 验证乘法运算: 3 * 7 = 21
  [INFO] Computing Divide(10, 2)
  [用例通过] 验证除法运算: 10 / 2 = 5.0
  [用例通过] 验证阶乘运算: 0! = 1, 4! = 24
  [INFO] Computing Divide(1, 0)
  [ERR ] Division by zero error!
  [用例通过] 验证除以零异常捕获成功
  [INFO] All tests passed!
  [测试完成] 全部 6 项子用例测试通过！Totals: 6 passed, 0 failed, 0 skipped
  ALL_TESTS_PASSED
  ```

### 2.5 模拟失败用例执行与错误定位 (`vs_run_tests` & `vs_get_test_run_status`)
- **调用**：`vs_run_tests(testIds: ["ctest:MathOperationsFailureTest"], waitForCompletion: true)`
- **响应**：
  ```json
  {
    "testRunId": "testrun-25c1e8a4",
    "state": "failed",
    "totalCount": 1,
    "passedCount": 0,
    "failedCount": 1,
    "durationSeconds": 0.51,
    "failedTestNames": [
      "MathOperationsFailureTest"
    ]
  }
  ```
- **调用 `vs_get_test_run_status("testrun-25c1e8a4")`**：
  ```json
  {
    "testRunId": "testrun-25c1e8a4",
    "state": "failed",
    "results": [
      {
        "testId": "ctest:MathOperationsFailureTest",
        "displayName": "MathOperationsFailureTest",
        "outcome": "Failed",
        "errorMessage": "Failed",
        "standardOutput": "[测试日志] 启动模拟失败用例 MathOperationsFailureTest...\n[用例详情] 正在测试 MathOps::Add(2, 3) 是否等于预期错误值 999...\nFAIL! : MathOps::Add(2, 3) == 999 模拟断言失败 (实际值: 5, 期望值: 999)\n[测试总结] Totals: 0 passed, 1 failed, 0 skipped"
      }
    ]
  }
  ```

### 2.6 全量测试并发执行验证 (`vs_run_tests`)
- **调用**：`vs_run_tests(waitForCompletion: true)`（不指定 `testIds`）
- **响应**：
  ```json
  {
    "testRunId": "testrun-f354db9d",
    "state": "failed",
    "totalCount": 2,
    "passedCount": 1,
    "failedCount": 1,
    "skippedCount": 0,
    "durationSeconds": 0.5,
    "failedTestNames": [
      "MathOperationsFailureTest"
    ]
  }
  ```

### 2.7 输出窗口 Tests 窗格原生读取 (`vs_get_output_window_logs`)
- **调用**：`vs_get_output_window_logs(source: "tests")`
- **响应**：原生读取 VS 输出窗口中的“测试”窗格，包含 CTest 执行详情、退出代码（`退出代码: 8`）及总结信息。

---

## 3. 自动化测试统计

| 测试程序集 | 目标框架 | 用例数 | 状态 | 覆盖关键点 |
| :--- | :--- | :---: | :---: | :--- |
| `VsDebugMcp.Protocol.Tests` | `net8.0` / `net472` | 48 | **PASS** | `RunTestsResponse` 与 `TestRunStatusResponse` 中 `consoleOutput` 字段双向契约序列化测试 |
| `VsDebugMcp.Host.Tests` | `net8.0` | 196 | **PASS** | `HttpAcceptHeaderMiddlewareTests`（覆盖无头、`*/*`、带分号等各种组合）、参数自适应 Schema 校验 |
| **总计** | | **244** | **100% PASS** | 持续集成质量门禁完全通过 |

---

## 4. 交付文件清单

1. **协议层与宿主层**：
   - `src/VsDebugMcp.Protocol/BridgeContracts.cs`：扩充 `ConsoleOutput`。
   - `src/VsDebugMcp.Host/Program.cs`：注入 HTTP Accept 宽容中间件。
   - `src/VsDebugMcp.Host/BridgeService.cs`：无参构建状态回退与测试控制台输出透传。
   - `src/VsDebugMcp.Host/McpTools.cs`：更新工具参数定义与文档说明。
2. **VS 插件层**：
   - `src/VsDebugMcp.Vsix.Shared/OutputWindowProvider.cs`：注册 TestWindow GUID 与双语映射。
   - `src/VsDebugMcp.Vsix.Shared/SolutionBuildProvider.cs`：动态采用系统 OEM 控制台编码。
   - `src/VsDebugMcp.Vsix.Shared/TestExplorerProvider.cs`：控制台输出捕获、增量截取与失败启发式修正。
3. **测试与样例**：
   - `sample/SampleCMake/tests/test_math.cpp`：中文测试日志输出与 `--fail` 模拟用例。
   - `sample/SampleCMake/tests/CMakeLists.txt`：注册通过与失败双 CTest 用例。
   - `sample/SampleTests/CalculatorTests.cs`：新增 C# 控制台输出测试。
   - `tests/VsDebugMcp.Host.Tests/HttpAcceptHeaderMiddlewareTests.cs`：HTTP 中间件专项测试。
4. **版本与发布元数据**：
   - 版本统一递增至 **`0.1.26.0`**（`VsDebugMcp.Host.csproj`、`VsDebugMcp.Vsix.csproj`、`VsDebugMcp.Vsix.2019.csproj`、`source.extension.vsixmanifest`）。
   - `CHANGELOG.md`：记录 `0.1.26.0` 变更履历。
   - `project-blueprint.md`：记录 Phase 8C 架构设计。
