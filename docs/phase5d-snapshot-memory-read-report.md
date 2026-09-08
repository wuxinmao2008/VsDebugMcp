# Phase 5D 验收报告：聚合调试快照与进程虚拟内存透视

> **报告版本**: v0.1.19.0  
> **验收日期**: 2026-09-08  
> **工具总数**: 50 个 MCP Tools (新增 2 个，全部 `isStub: false`)  
> **状态**: 已完成并在 VS 2026 Experimental Instance 真实环境验证通过

---

## 1. 目标与价值

在工业自动化控制（PLC/Modbus 通信）以及复杂图形/多线程系统（如 Qt 桌面应用）中，AI 协同调试面临两大效率瓶颈：
1. **多轮往返高延迟**：获取完整的上下文以往需要分别调用 `vs_debugger_get_info`、`vs_debugger_get_threads`、`vs_debugger_get_call_stack`、`vs_debugger_get_locals` 及 `vs_get_output_window_logs`，至少 5 次 IPC 往返，极易遇到状态漂移与超时。
2. **底层报文与缓冲区黑盒**：工业通信以字节流（字节数组、原生指针、环形缓冲区）为主，调试器符号求值经常因为编译器优化、内联、缺少类型定义或复杂指针运算而无法直接展示连续内存。

Phase 5D 针对性提供两个高价值原生调试原语：
- `vs_debugger_get_snapshot`：单次调用原子聚合模式、进程/线程信息、中断原因、顶层栈帧、完整调用栈、局部变量（含 Release 优化警告检测）及最近 Output 窗口输出日志（如 qDebug、OutputDebugString、CLR 模块日志）。
- `vs_debugger_read_memory`：基于 Windows 原生 `ReadProcessMemory` API，安全透视运行中进程的虚拟内存空间，支持绝对 16 进制地址与指针表达式解析，生成标准 HexDump 与 Base64 二进制数据。

---

## 2. 工具定义与设计规范

### 2.1 `vs_debugger_get_snapshot`
- **功能**: 一站式捕获调试器运行期完整诊断切片。
- **协议特征**: ReadOnly=true, Idempotent=true。
- **输入参数**:
  - `includeCallStack` (bool?, 默认 true): 是否包含调用栈帧。
  - `maxFrames` (int?, 默认 10): 最大调用栈帧数。
  - `includeLocals` (bool?, 默认 true): 是否包含局部变量。
  - `maxLocals` (int?, 默认 50): 最大局部变量数。
  - `includeRecentLogs` (bool?, 默认 true): 是否附带输出窗口日志。
  - `recentLogLines` (int?, 默认 30): 输出日志最大行数。
  - `logSource` (string?, 默认 "debug"): 日志输出窗格（"debug" 或 "build"）。
  - `vsInstanceId` (string?): 目标 Visual Studio 实例。
- **输出载荷**: `mode`, `isDebugging`, `currentProcessId`, `currentProcessName`, `currentThreadId`, `lastBreakReason`, `topFrame`, `callStack`, `locals`, `recentLogs`, `logSource`, `warnings`。

### 2.2 `vs_debugger_read_memory`
- **功能**: 安全读取目标调试进程的裸内存数据。
- **协议特征**: ReadOnly=true, Idempotent=true。
- **输入参数**:
  - `address` (string, 必需): 目标 16 进制内存地址（如 `"0x00007FFE354A0000"`）或指针表达式/变量名（如 `"pBuffer"`, `"&myStruct"`）。
  - `byteCount` (int?, 默认 64): 读取字节数（限制 1-4096 字节）。
  - `vsInstanceId` (string?): 目标 Visual Studio 实例。
- **输出载荷**:
  - `resolvedAddress`: 解析后的绝对 64 位/32 位 16 进制地址。
  - `hexBytes`: 空格分隔的 16 进制字节序列（如 `4D 5A 90 00 ...`）。
  - `hexDump`: 经典格式化转储（含偏移量、16 字节分栏与 ASCII 可读侧边栏）。
  - `asciiRepresentation`: ASCII 可打印字符流（不可打印以 `.` 替代）。
  - `base64Data`: 标准 Base64 编码二进制流。

---

## 3. 自动化测试套件验证

```text
1. Protocol 测试套件:
   - Command: dotnet test tests/VsDebugMcp.Protocol.Tests/VsDebugMcp.Protocol.Tests.csproj
   - Result: 已通过! 失败: 0，通过: 21，总计: 21 (100% 通过)
   - 覆盖: Phase5DContractsRoundTripThroughSharedSerializer (序列化/反序列化兼容性验证)

2. Host 测试套件:
   - Command: dotnet test tests/VsDebugMcp.Host.Tests/VsDebugMcp.Host.Tests.csproj
   - Result: 已通过! 失败: 0，通过: 159，总计: 159 (100% 通过)
   - 覆盖:
     * AllPhase5DToolsAreRegisteredWithCorrectMetadata (vs_debugger_get_snapshot, vs_debugger_read_memory)
     * OptionalToolParametersHaveDefaultValues (全量参数默认值校验)
     * BridgeServiceExceptionTests (InvalidMemoryAddress, MemoryReadFailed 异常错误码映射)
     * Exactly50ToolsAreRegisteredOnMcpTools (断言 50 个工具全量注册)
```

---

## 4. Visual Studio 2026 在线联调与真实环境验收记录

- **环境宿主**: Visual Studio 2026 (18.9.12120.119), Experimental Instance (PID: 23228).
- **测试解决方案**: `SampleSolution.slnx` (`SampleApp`, `SampleLib`, `SampleTests`).
- **MCP 插件版本**: `0.1.19.0` (Host & Bridge 均升级为 0.1.19.0).

### 4.1 健康与能力集检查
- `vs_health`: 返回 `hostVersion: 0.1.19.0`, `bridge: { status: "ok" }`。
- `vs_capabilities`: 返回全部 50 个工具，`vs_debugger_get_snapshot` 与 `vs_debugger_read_memory` 均为 `isStub: false`。

### 4.2 非调试 (Design) 模式测试
- 调用 `vs_debugger_get_snapshot`：返回 `{ "mode": "design", "isDebugging": false, "callStack": [], "locals": [] }`，无报错安全退出。
- 调用 `vs_debugger_read_memory(address="0x12345678")`：正确捕获并返回 `debugger_not_debugging: Cannot read process memory when not debugging (debugger is in design mode).`。

### 4.3 调试中断 (Break) 模式全功能快照测试
1. 在 `SampleApp\Program.cs:10` 命中测试断点，单步初始化对象。
2. 调用 `vs_debugger_get_snapshot(recentLogLines=10)`：
   - 一次性成功返回当前调试进程 `SampleApp.exe` (PID: 3024) 与主线程 (TID: 2060)。
   - 完整调用栈（顶层帧 `Program.<Main>$`）。
   - 局部变量集：`item`, `args`, `sum` 以及属性值 `Widget`, `9.99`。
   - 最近 10 条 Debug 输出窗口日志（含 CoreCLR 模块加载及符号状态信息）。

### 4.4 真实物理进程虚拟内存读取测试
1. 目标地址：调用 `vs_debugger_get_modules` 获取目标进程已加载的 `System.Private.CoreLib.dll` 模块加载基址：`0x00007FFE354A0000`。
2. 调用 `vs_debugger_read_memory(address="0x00007FFE354A0000", byteCount=64)`：
   - 成功读取 64 字节 PE 文件头数据！
   - `hexBytes`: `4D 5A 90 00 03 00 00 00 04 00 00 00 FF FF 00 00 ...`
   - `hexDump`:
     ```text
     7FFE354A0000  4D 5A 90 00 03 00 00 00  04 00 00 00 FF FF 00 00  |MZ..............|
     7FFE354A0010  B8 00 00 00 00 00 00 00  40 00 00 00 00 00 00 00  |........@.......|
     7FFE354A0020  00 00 00 00 00 00 00 00  00 00 00 00 00 00 00 00  |................|
     7FFE354A0030  00 00 00 00 00 00 00 00  00 00 00 00 80 00 00 00  |................|
     ```
   - `asciiRepresentation`: `MZ......................@.......................................`
   - `base64Data`: `TVqQAAMAAAAEAAAA//8AALgAAAAAAAAAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAgAAAAA==`
3. 验证了 Win32 原生 `ReadProcessMemory` 在 64 位宿主目标进程上的安全稳定读取。

---

## 5. 结论

Phase 5D 成功交付并通过全部严苛的工程约束与在线验收要求。VsDebugMcp 当前已具备 50 个高可靠性 MCP 调试与构建工具，涵盖断点、堆栈、表达式、多窗格日志、混合模式进程发现、聚合调试快照与裸内存透视，为复杂工控协议逆向与跨线程桌面系统诊断提供了坚实的底层技术支撑。
