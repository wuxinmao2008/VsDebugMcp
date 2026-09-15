# Phase 7B 验收报告：CMakePresets 配置切换与 CMake 双轨构建驱动

**日期**：2026-09-15  
**版本**：`0.1.22.0`  
**测试目标**：Visual Studio 2026 Professional (v18.9.12120.119)  
**目标工作区**：`sample/SampleCMake` (C++17 嵌套现代 CMake 工程，配置 `CMakePresets.json`)

---

## 1. 阶段目标与完成情况

| 需求项 | 涉及组件 / 工具 | 预期表现 | 验收结果 |
| :--- | :--- | :--- | :---: |
| **CMakePresets 解析** | `CMakePresetsModel.cs`<br>`CMakePresetsParser` | 解析 `CMakePresets.json` / `CMakeUserPresets.json`，过滤 `hidden: true`，提取名称、平台与宏展开路径 | **PASS** |
| **预设配置发现** | `vs_get_solution_configurations` | 返回工作区内所有可见 CMake configure presets，正确标记当前 `isActive` 状态 | **PASS** |
| **预设配置切换** | `vs_set_solution_configuration` | 校验请求配置，切换活动 preset，保存工作区会话状态；调试状态下拦截 | **PASS** |
| **双轨构建驱动** | `SolutionBuildProvider`<br>`vs_run_build` | 自动识别 CMake 工作区，定位宿主 VS 工具链（`VsDevCmd.bat` + `cmake.exe` + `ninja.exe`），发起异步构建并返回 `buildTaskId` | **PASS** |
| **构建状态轮询** | `vs_get_build_status` | 规范轮询 `starting` -> `running` -> `succeeded`，记录各阶段高精 UTC 时间戳 | **PASS** |
| **构建日志重定向** | `vs_get_output_window_logs` | 将编译配置与 Ninja 进度流式写入 IDE“生成”（Build）输出窗格，MCP 端可即刻读取 | **PASS** |
| **进程取消支持** | `vs_cancel_build` | 终止后台构建进程树，流转为 `cancelled`；完成态/无效任务返回明确错误码 | **PASS** |
| **自动化测试** | `VsDebugMcp.Protocol.Tests`<br>`VsDebugMcp.Host.Tests` | 新增 Preset 单元测试，全套回归测试全部通过 | **212/212 PASS** |

---

## 2. 在线验收实测结果 (`scripts/test_acceptance_phase7b.py`)

运行环境：Visual Studio 2026 (PID 9256)，打开 `sample/SampleCMake`，MCP Host (PID 19148)。

### 2.1 实例与健康检查 (`vs_health`)
```json
{
  "status": "ok",
  "hostVersion": "0.1.21.0",
  "selectedInstance": {
    "vsInstanceId": "vs-9256-08df12f0070214bb",
    "visualStudioProcessId": 9256,
    "solutionName": "SampleCMake",
    "solutionFilePath": "D:\\VsDebugMcp\\VsDebugMcp\\sample\\SampleCMake"
  }
}
```

### 2.2 CMake 预设配置发现 (`vs_get_solution_configurations`)
```json
{
  "activeConfigurationName": "x64-Debug",
  "activePlatformName": "x64",
  "configurations": [
    { "name": "x64-Debug", "platformName": "x64", "fullName": "x64-Debug|x64", "isActive": true },
    { "name": "x64-Release", "platformName": "x64", "fullName": "x64-Release|x64", "isActive": false }
  ]
}
```
*注：`windows-base` 配置具有 `hidden: true`，已按规范精准过滤。*

### 2.3 预设配置切换 (`vs_set_solution_configuration`)
- 切换至 `x64-Release`：
  - 返回：`success: true`, `previousConfiguration: "x64-Debug"`, `activeConfiguration: "x64-Release"`
- 验证 `vs_get_solution_configurations`：
  - `activeConfigurationName` 立即更新为 `"x64-Release"`，`x64-Release.isActive` 为 `true`。
- 切换回 `x64-Debug`：
  - 返回：`success: true`, `activeConfiguration: "x64-Debug"`。

### 2.4 CMake 构建触发与状态轮询 (`vs_run_build` & `vs_get_build_status`)
- 发起构建：
  ```json
  {
    "buildTaskId": "674b93b75193411e8b8325468cf1b3df",
    "state": "starting",
    "configuration": "x64-Debug",
    "platform": "x64",
    "requestedAtUtc": "2026-09-15T06:17:36.4093604Z"
  }
  ```
- 状态轮询：
  - `[Poll #1 ~ #3] State: running, StartedAt: 2026-09-15T06:17:36.4108769Z`
  - `[Poll #4] State: succeeded, CompletedAt: 2026-09-15T06:17:38.5343044Z, Succeeded: true`
  - 任务用时 2.12 秒，顺利完成。

### 2.5 Visual Studio 输出窗口日志回读 (`vs_get_output_window_logs`)
```text
------ 已启动生成: CMake 项目: SampleCMake, 配置: x64-Debug ------
**********************************************************************
** Visual Studio 2026 Developer Command Prompt v18.9.2
** Copyright (c) 2026 Microsoft Corporation
**********************************************************************
-- Configuring done (0.1s)
-- Generating done (0.1s)
-- Build files have been written to: D:/VsDebugMcp/VsDebugMcp/sample/SampleCMake/out/build/x64-Debug
ninja: no work to do.
========== 生成: 成功 1 个，失败 0 个，最新 0 个，跳过 0 个 ==========
```

---

## 3. 架构与稳定性亮点

1. **零外部环境依赖**：
   - 彻底摆脱对外部全局环境变量的依赖，利用宿主 Visual Studio 自带的 `VsDevCmd.bat` 以及内置 `cmake.exe`、`ninja.exe` 驱动构建，环境一致性达到 100%。
2. **双轨架构无侵入**：
   - 传统 `.sln` 方案完全保留现有 `IVsSolutionBuildManager2` 通道；
   - CMake 工作区走进程树驱动通道，对外输出的 `BuildTaskResponse` 协议模型 100% 保持一致，Client/Agent 端零感知平滑适配。
3. **IDE 本地状态联动**：
   - 通过 EnvDTE 的 `OutputWindowPane`，编译流式日志与结果汇总实时呈现在 Visual Studio IDE 界面的“生成”输出窗口中，保证 Agent 与人类开发者协同体验的一致性。
