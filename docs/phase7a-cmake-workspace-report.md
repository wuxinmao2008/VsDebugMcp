# Phase 7A 验收报告：CMake 与 Open Folder 工作区生命周期与文件树适配

> **报告版本**: v0.1.21.0  
> **验收日期**: 2026-09-15  
> **状态**: 已完成开发，并通过单元测试与 Visual Studio 2026 真实实例在线实测验收  

---

## 1. 目标与背景

在 C++ 跨平台开发、工控嵌入式及高性能计算等场景中，越来越多的项目放弃了传统的 `.sln` + `.vcxproj` 单体绑定模式，全面转向现代 CMake 与 Open Folder（打开文件夹）工作区体系。

在此前版本中，VsDebugMcp 在处理 VS 纯文件夹模式时存在以下摩擦点：
1. **启动时未自动激活**：VSIX 仅监听了 `NoSolution` 与 `SolutionExists`，若直接通过“文件 -> 打开 -> 文件夹”启动 VS，扩展无法自动加载；
2. **项目被标记为不支持的杂项**：VS Open Folder 会生成一个临时的虚拟解决方案及“杂项文件（Miscellaneous Files）”层级，由于没有物理工程文件，导致 `projectFilePath` 为空并输出 `isUnsupported: true` 与 `project_path_unavailable` 警告；
3. **无法枚举工作区文件树**：传统的 `IVsHierarchy` 遍历只能枚举当前在 VS 临时打开的文件，无法索引整个项目的多级嵌套目录源码。

**Phase 7A** 的核心目标是彻底打通 CMake 与 Open Folder 工作区的基础底座，使 Agent 能够像处理常规 `.sln` 一样平滑地发现 CMake 工程、递归遍历嵌套源码文件。

---

## 2. 架构设计与核心实现

### 2.1 扩展生命周期自动加载 (AutoLoad Lifecycle)
- **文件**: `src/VsDebugMcp.Vsix.Shared/VsDebugMcp.VsixPackage.cs`
- 补充注册了 Visual Studio 专有的 Open Folder 激活上下文：
  ```csharp
  [ProvideAutoLoad(VSConstants.UICONTEXT.FolderOpened_string, PackageAutoLoadFlags.BackgroundLoad)]
  [ProvideAutoLoad(VSConstants.UICONTEXT.EmptySolution_string, PackageAutoLoadFlags.BackgroundLoad)]
  ```
- 保证用户无论是直接从 VS 启动窗口打开文件夹、从命令行传入路径，还是打开空工作区，扩展包都会在后台自动拉起并完成 Host 握手。

### 2.2 CMake 工作区规范工程识别 (`vs_get_projects_in_solution`)
- **文件**: `src/VsDebugMcp.Vsix.Shared/SolutionProjectProvider.cs`
- **实现策略**:
  1. 读取当前工作区物理根目录，探测是否存在 `CMakeLists.txt`；
  2. 若检测到当前为 CMake 工作区且没有常规 `.vcxproj`/`.csproj`，自动过滤 VS 虚拟生成的“杂项文件”（GUID: `6bb5f8f0-4483-11d3-8bcf-00c04f8ec28c`），消除假警告；
  3. 合成并注入一等公民的 CMake 工程项：
     - `Id`: `"cmake:root"`
     - `Name`: 目录名或解决方案基础名（如 `SampleCMake`）
     - `ProjectFilePath`: 绝对路径指向根入口 `CMakeLists.txt`
     - `ProjectDirectory`: 工作区绝对根目录
     - `Kind`: `"cmake"`
     - `TypeGuid`: `"cmake"`
     - `IsLoaded`: `true`
     - `IsUnsupported`: `false`
  4. 宿主诊断服务 `DiagnosticReportService` 联动识别项目范式为 `CMake`。

### 2.3 工作区源码文件树智能检索 (`vs_get_files_in_project`)
- **文件**: `src/VsDebugMcp.Vsix.Shared/SolutionFileProvider.cs`
- **实现策略**:
  1. 新增 `TraverseCMakeWorkspace` 深度递归遍历器；
  2. **严密黑名单目录过滤**：自动排除 `.vs/`、`.git/`、`.svn/`、`build/`、`out/`、`bin/`、`obj/`、`CMakeFiles/` 及带有 `build-`/`out-` 前缀的临时生成目录；
  3. **精确扩展名过滤**：支持通过 `extensionFilter` 精确匹配 `.cpp`、`.h`、`.txt`、`.cmake` 等；
  4. **大工程安全限长**：设定 5000 个文件的单次扫描硬防护上限，超限输出 `file_count_truncated` 结构化警告；
  5. 统一计算规范的 `RelativePath`，便于 Agent 构建代码全景图。

### 2.4 标准化 CMake 多层级靶场工程 (`sample/SampleCMake`)
- 包含 3 级目录嵌套与完整构建依赖链：
  - `src/core`（静态库 `sample_core`，提供 logger 接口）
  - `src/modules/calculator`（静态库 `sample_calculator`，依赖 core）
  - `src/app`（可执行程序 `SampleCMakeApp`，依赖 calculator 和 core）
  - `tests`（CTest 单元测试程序 `sample_tests`）
- 原生包含 VS 标准 `CMakePresets.json`（`x64-Debug` 与 `x64-Release`）。

---

## 3. 自动化测试套件验证

全套自动化测试用例规模扩充至 **208 项（100% PASS）**：

```text
1. Protocol 测试套件:
   - Command: dotnet test tests/VsDebugMcp.Protocol.Tests/VsDebugMcp.Protocol.Tests.csproj --nologo
   - Result: 已通过! 失败: 0，通过: 27，总计: 27 (100% 通过)
   - 新增覆盖:
     * CMakeProjectAndFilesRoundTripThroughSharedSerializer (CMake 项目与文件结构序列化往返测试)

2. Host 测试套件:
   - Command: dotnet test tests/VsDebugMcp.Host.Tests/VsDebugMcp.Host.Tests.csproj --nologo
   - Result: 已通过! 失败: 0，通过: 181，总计: 181 (100% 通过)
   - 覆盖:
     * DiagnosticReportService 中的 CMake Archetype 识别
     * McpTools 全部 51 个工具 Schema 契约校验
```

---

## 4. 全链路在线实测验收（基于 Visual Studio 2026 Experimental Instance）

在 Visual Studio 2026 实验实例中通过“文件 -> 打开 -> 文件夹”打开 `sample/SampleCMake`，运行端到端验收脚本 `scripts/test_acceptance_phase7a.py` 以及原生 MCP 工具调用：

```text
==================================================
   VsDebugMcp Phase 7A: CMake Workspace Acceptance
==================================================

[Step 1] Health and Instance Check
  Health status: ok
  Instance ID:   vs-18712-08df12dda5f5986a
  Solution Name: SampleCMake
  Solution Path: D:\VsDebugMcp\VsDebugMcp\sample\SampleCMake

[Step 2] CMake Workspace Project Discovery
  Total Projects: 1
  Warnings Count: 0
    - ID: cmake:root, Name: SampleCMake, Kind: cmake, Unsupported: False, Path: D:\VsDebugMcp\VsDebugMcp\sample\SampleCMake\CMakeLists.txt
  --> PASS: CMake project recognized with Kind='cmake', isUnsupported=False

[Step 3] Workspace Source File Traversal (Unfiltered)
  Total Files Found: 15
  Project: SampleCMake (15 files)
    - .gitignore
    - CMakeLists.txt
    - CMakePresets.json
    - README.md
    - src\app\CMakeLists.txt
    - src\app\main.cpp
    - src\CMakeLists.txt
    - src\core\CMakeLists.txt
    - src\core\include\core\logger.h
    - src\core\src\logger.cpp
    - src\modules\calculator\CMakeLists.txt
    - src\modules\calculator\include\calculator\math_ops.h
    - src\modules\calculator\src\math_ops.cpp
    - tests\CMakeLists.txt
    - tests\test_math.cpp

[Step 4] Extension Filtered Traversal (.cpp)
  C++ Source Files (.cpp): 4
    - src\app\main.cpp
    - src\core\src\logger.cpp
    - src\modules\calculator\src\math_ops.cpp
    - tests\test_math.cpp

[Step 5] Extension Filtered Traversal (.h)
  Header Files (.h): 2
    - src\core\include\core\logger.h
    - src\modules\calculator\include\calculator\math_ops.h

==================================================
       Phase 7A Acceptance Completed Successfully! 
==================================================
```

### 调试器断点交互实测验证
- `vs_debugger_set_breakpoints`: 成功在 `src\modules\calculator\src\math_ops.cpp` 第 10 行打上断点，回显 `isBound: true`；
- `vs_debugger_clear_breakpoints`: `clearedCount: 1` 成功清除。

---

## 5. 结论与下阶段演进

Phase 7A 成功解决了 CMake 项目在 VS 打开时的生命周期激活、工程结构一等公民化与源码树遍历难题。

**下一步演进（Phase 7B）规划**：
- **CMake Presets 感知与切换**：解析并映射 `CMakePresets.json`（`x64-Debug` / `x64-Release`），扩展 `vs_get_solution_configurations` 与 `vs_set_solution_configuration`；
- **双轨一键构建驱动**：攻克 `vs_run_build` 在 CMake 模式下的 `build_state_unavailable` 报错，通过 DTE 命令驱动 CMake 构建并捕获生命周期日志与错误。
