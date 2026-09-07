# Visual Studio 2026 进程附加与模块符号诊断（Phase 3C）联调与在线验收报告

## 1. 概述与目标

本次迭代完成了 Visual Studio 2026 (VS 18.x) 扩展与 MCP Host 的 **Phase 3C（进程附加与模块符号诊断）** 研发，重点攻坚对系统正在运行的外部进程的检索发现、附加调试（Attach to Process）、安全无害分离（Detach），以及中断/运行期间模块加载基地址与 PDB 符号加载状态（Modules & Symbols）的深度诊断。

本次新增的 4 个核心 MCP 工具如下：
- **`vs_debugger_get_processes`**：枚举操作系统当前运行的本地进程或仅看被调试进程，支持进程名模糊过滤、PID 精确查询与只看已调试进程；
- **`vs_debugger_attach_process`**：将 Visual Studio 调试器附加至指定的运行中进程（支持 PID 与进程名匹配），支持断点中断智能着陆探测（`waitForBreak`）；
- **`vs_debugger_detach`**：安全分离调试器，使目标外部进程继续保持存活运行，VS 调试器干净恢复至设计模式；
- **`vs_debugger_get_modules`**：在调试运行态深度提取目标进程已加载模块（DLL/EXE）清单、物理路径、版本、十六进制加载/结束基地址、以及 PDB 符号文件状态（Symbols Loaded / No Symbols）。

至此，外部 Agent 具备了覆盖 **“外部常驻进程探测 -> 精准附加调试 -> 符号与模块状态诊断 -> 暂停/单步排查 -> 安全分离进程保持存活”** 的工业级调试生命周期全闭环。

---

## 2. 核心技术难点攻关与架构保证

1. **`EnvDTE.Process` 与 `EnvDTE80.Process2` / `EnvDTE90.Process3` 的向下兼容转型**：
   - Visual Studio 的底层 COM 对象在运行时提供多重接口。通过将 `EnvDTE.Process` 安全转型为 `EnvDTE80.Process2`，提取了 `UserName`、`IsBeingDebugged` 与 `TransportQualifier`；进一步转型为 `EnvDTE90.Process3`，成功提取底层 `Modules` 模块集合。
2. **多进程并发互斥防卫与锁模型**：
   - 附加与分离均纳管于 `_executionLock` 互斥量，杜绝与单步（Step）、启动（Start）或测试调试（TestDebug）发生并发 COM 重入崩溃。
3. **安全分离（Detach）与终止（Stop）的明确边界**：
   - 彻底理清语义：`vs_debugger_stop` 会终止杀死被调试进程，而 `vs_debugger_detach` 则解除调试挂钩并确保宿主进程继续独立运行，通过在线实测验证目标进程在分离后 `poll() is None` 完好存活。
4. **十六进制地址与符号标准化**：
   - 模块加载地址统一使用 `0x{mod.LoadAddress:X16}` 格式化为 64 位大端十六进制字符串，便于外部 Agent 直观判断内存段分布。

---

## 3. 端到端在线实测验收（Online Acceptance）

在运行中的 Visual Studio 2026（VS 18.x）实验实例与部署好的 MCP Host 上，运行自动化验收套件 `scripts/test_acceptance_phase3c.py`，全流程 6 个环节全部通过：

| 验收环节 | 调用的 MCP 工具 | 预期结果 | 实测结果 | 状态 |
| :--- | :--- | :--- | :--- | :---: |
| **[1/6] 健康与新能力发现** | `vs_health` & `vs_capabilities` | 状态 ok，总能力数达到 32 个，4 个新工具 `isStub=false` | `status=ok, vsInstanceId=vs-2116-08df0ca4a57bd836`<br>发现 32 个可用能力，4 项新能力全部激活 | **PASS** |
| **[2/6] 进程发现与查询** | `vs_debugger_get_processes` | 稳定返回操作系统进程清单与 PID | 成功返回前 20 个本地进程，提取样本 PID=4 (`System`) | **PASS** |
| **[3/6] 靶场常驻进程启动与锁定** | `vs_debugger_get_processes(processId)` | 启动外部靶场，按 PID 精确检索 | 启动外部常驻进程（PID=21520），精确检索返回 1 个匹配项 (`powershell.exe`) | **PASS** |
| **[4/6] 附加到外部进程** | `vs_debugger_attach_process` | 成功附加并进入调试模式 | `ProcessId: 21520, Mode: running, IsDebugging: True` | **PASS** |
| **[5/6] 模块与符号深度诊断** | `vs_debugger_get_modules` & `vs_debugger_pause` | 提取加载模块列表、基地址与符号状态并暂停 | 提取 20 个加载模块（`mscorlib.dll` 等），基地址精准格式化为十六进制；成功触发 `user_break` 暂停 | **PASS** |
| **[6/6] 安全分离并验证存活** | `vs_debugger_detach` | 分离调试器，VS 回到设计模式，外部进程保持存活 | `Mode: design, IsDebugging: False`<br>外部进程 `poll() is None`，继续健康存活！ | **PASS** |
| **[收尾] 靶标安全清理** | `subprocess.terminate()` | 外部靶标进程清理 | 进程资源完全释放 | **PASS** |

---

## 4. 自动化单元测试与代码质量指标

- **`VsDebugMcp.Protocol.Tests`**：11 / 11 PASS (100%)
  - 新增 `DebuggerProcessesAndModulesContractsRoundTripThroughSharedSerializer` 覆盖全部进程与模块 DTO 序列化/反序列化。
- **`VsDebugMcp.Host.Tests`**：90 / 90 PASS (100%)
  - 新增 4 个新工具全部入参的默认值/可选性校验与 Schema 元数据检查；
  - 新增 `ProcessNotFound` 错误码映射验证。
- **项目总计自动化测试**：**101 / 101 PASS (100%)**。
- **构建状态**：MSBuild 0 警告，0 错误。
- **版本号同步**：全模块统一升级至 `0.1.11.0`。
