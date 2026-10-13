# Phase 8B 验收报告：Minidump 深度诊断、原生反汇编、符号热加载与单工程构建 (Issue #4 闭环)

**日期**：2026-10-13  
**版本**：0.1.25.0  
**测试宿主**：Visual Studio 2026 Professional (v18.9.12120.119, PID: 6600)  
**实例 ID**：`vs-6600-08df28ceb57b9b4e`  
**测试工作区**：`D:\VsDebugMcp\VsDebugMcp\sample\SampleCMake\out\build\x64-Debug\crash_sample.dmp`

---

## 1. 阶段目标与完成情况

| 需求项 | 涉及组件 / 工具 | 预期表现 | 验收结果 |
| :--- | :--- | :--- | :---: |
| **Minidump 异常流解析 (`ExceptionStream` Type 6)** | `MinidumpParser`<br>`vs_debugger_get_exception_info`<br>`vs_debugger_get_snapshot` | 在事后转储 (`.dmp`) 调试下自动回退解析 `MINIDUMP_EXCEPTION_STREAM`，补齐 `exceptionCode` (`0xC0000005`)、`exceptionType` (`EXCEPTION_ACCESS_VIOLATION`)、`exceptionAddress`、`faultingThreadId`、`accessOperation` (`read`/`write`/`execute`) 与 `faultingAddress` | **PASS** |
| **Minidump 内存读取与三级回退** | `MinidumpParser`<br>`DebuggerProvider`<br>`vs_debugger_read_memory` | 修复 DMP 下 `PID: 0` 导致 `Win32 error code: 87` 的问题，实现 `win32_read_process_memory` ➔ `minidump_stream` (Type 9/5) ➔ `debugger_eval` 三级级联回退，并新增 `readSource` 与 `utf16Preview` | **PASS** |
| **原生反汇编引擎 (#52)** | `DebuggerProvider`<br>`vs_debugger_get_disassembly` | 支持按实时地址 (`address`) 或调用栈帧 (`frameIndex`) 提取原生汇编指令，自动根据模块运行时 `LoadAddress` 与 PE 静态 `ImageBase` 计算重定位偏移，并利用 `dumpbin.exe /DISASM:BYTES /RANGE` 极速切片并标记 `[FAULT/IP]` | **PASS** |
| **动态 PDB 符号加载 (#53)** | `DebuggerProvider`<br>`vs_debugger_load_symbols` | 通过 `IVsDebuggerSymbolSettingsManager120A` 动态向 VS 调试器符号路径集合注入 `.pdb` 文件或目录、`SaveSymbolSettings()` 触发符号重载，并返回更新后的 `ModuleInfo` 列表 | **PASS** |
| **单工程/单 Target 定向构建与 Clean/Rebuild** | `SolutionBuildProvider`<br>`vs_run_build` | 新增 `projectName` 与 `action` (`build` \| `rebuild` \| `clean`) 参数，同时覆盖 `.sln/.slnx` (`StartSimpleUpdateProjectConfiguration`) 与 CMake (`--target` / `--clean-first`) 双轨工程 | **PASS** |
| **表达式求值字符串预览与格式化指南** | `DebuggerProvider`<br>`vs_debugger_evaluate_expr`<br>`vs_debugger_evaluate_expressions` | 针对返回裸地址的 `char*` / `wchar_t*` 指针自动执行二级修饰符求值并拼接文本预览；在工具 Schema 中明确 MSVC 格式化修饰符（`,s`、`,su`、`,s8`、`,x`、`ptr,N`） | **PASS** |
| **全量自动化单元测试** | `VsDebugMcp.Protocol.Tests`<br>`VsDebugMcp.Host.Tests` | 新增 `MinidumpParserTests`（覆盖 StreamType 5/6/9、AV 读写冲突及 UTF-16 预览）及 Schema/快照回归测试 | **235/235 PASS** |
| **实机 DMP 全链路在线验收** | `SampleCMake --generate-dmp`<br>`crash_sample.dmp` | 通过内置 `MiniDumpWriteDump` 生成真实违例转储，在 VS 2026 实验实例中完成全部 6 项能力端到端验证 | **100% PASS** |

---

## 2. 在线实测记录

### 2.1 实例健康与 53 项工具发现 (`vs_health` & `vs_capabilities`)
```json
{
  "status": "ok",
  "hostVersion": "0.1.25.0",
  "bridgeVersion": "0.1.25.0",
  "selectedInstance": {
    "vsInstanceId": "vs-6600-08df28ceb57b9b4e",
    "visualStudioProcessId": 6600,
    "solutionName": "crash_sample"
  },
  "totalTools": 53,
  "stubTools": 0
}
```

### 2.2 Minidump 异常流深度提取 (`vs_debugger_get_exception_info` & `vs_debugger_get_snapshot`)
在 VS 2026 中打开 `crash_sample.dmp` 并进入事后调试（`dbgBreakMode`，`processId: 0`）后调用 `vs_debugger_get_exception_info`：
```json
{
  "hasException": true,
  "exceptionType": "EXCEPTION_ACCESS_VIOLATION",
  "exceptionCode": "0xC0000005",
  "exceptionMessage": "Exception 0xC0000005 (EXCEPTION_ACCESS_VIOLATION) at 0x00007FF75E531DF0: Access violation reading location 0x0000000000000018.",
  "exceptionAddress": "0x00007FF75E531DF0",
  "faultingThreadId": 39188,
  "accessOperation": "read",
  "faultingAddress": "0x0000000000000018",
  "topFrame": "SampleApp.exe!TriggerCrashForDump(const wchar_t * label, const char * tag)",
  "sourceFile": "D:\\VsDebugMcp\\VsDebugMcp\\sample\\SampleCMake\\src\\app\\main.cpp",
  "sourceLine": 35
}
```
* `vs_debugger_get_snapshot` 同步内联返回上述完整 `exceptionInfo` 结构，实现单次调用定位崩溃根因。

### 2.3 Minidump 内存流读取与双编码预览 (`vs_debugger_read_memory`)
读取故障栈帧上的寬字符指针 `label` (`0x00007ff75e535dd8`)：
* 自动跳过 `PID: 0` 不可用的 Win32 API，命中最优二级回退 `readSource: "minidump_stream"`；
* 准确返回 `bytesRead: 64`、十六进制 `hexDump`、`asciiPreview` 以及新增的宽字符预览 `utf16Preview: "OnePass_Dmp_Test_UTF16_String\u0000\u0000\u0000..."`。

### 2.4 原生反汇编与 ASLR 地址重定位 (`vs_debugger_get_disassembly`)
调用 `vs_debugger_get_disassembly(frameIndex=0, instructionCount=12, instructionsBefore=5)`：
* 自动识别模块 `SampleApp.exe` 运行时基址 `0x00007FF75E530000` 并换算 PE 静态偏移；
* 准确提取崩溃点前后 12 条 x64 汇编指令及机器码字节，并在崩溃指令处标记 `isCurrentInstruction: true`：
  * `0x00007FF75E531DEC` (`48 8B 44 24 28`)：`mov rax,qword ptr [rsp+28h]`
  * `0x00007FF75E531DF0` (`8B 40 18`)：`mov eax,dword ptr [rax+18h]` **`<-- [FAULT/IP]`**（`rax=0` 解引用偏移 `+0x18` 触发 `0xC0000005`）

### 2.5 动态符号路径注册 (`vs_debugger_load_symbols`)
调用 `vs_debugger_load_symbols(symbolPath="D:\\VsDebugMcp\\VsDebugMcp\\sample\\SampleCMake\\out\\build\\x64-Debug\\bin", moduleName="SampleApp.exe")`：
* 返回 `appliedSymbolPath` 与 `matchedModulesCount: 1`，确认 `SampleApp.exe` 符号状态 `symbolsLoaded: true`。

### 2.6 字符串指针自动文本预览 (`vs_debugger_evaluate_expr`)
* 在 DMP 栈帧中直接求值 `label`（未带 `,su` 修饰符）：自动识别 `const wchar_t *` 裸地址并追加宽字符串预览，返回 `"0x00007ff75e535dd8 \"OnePass_Dmp_Test_UTF16_String\""`；
* 求值 `label,su` 与 `tag,s` 均原生返回完整字符串内容。

---

## 3. 验收结论

Phase 8B 针对 GitHub Issue #4 暴露的所有事后转储（`.dmp`）与原生 C++ 调试痛点全部实现并验证闭环。全套 **235 个单元测试 100% 通过**，实机在线验收 **100% PASS**，工具总数扩展至 **53 项**，版本正式发布为 **`0.1.25.0`**。
