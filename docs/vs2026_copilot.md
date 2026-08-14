下面是基于当前 VS/工作区 环境（Visual Studio 2026 + 本工作区示例）可以作为 MCP 能力暴露的清单与实现细节建议。结构化为：能力名、简要描述、主要参数/返回、与 DAP/VS Code 对应、实现/限制要点、优先级。可直接作为你开发 VS 扩展（VSIX）时的能力定义草案。
总体说明
•	传输建议：JSON‑RPC over WebSocket 或 HTTP(S)（双向可推送）或 Named Pipe（仅本机更安全）。
•	授权建议：本机默认允许，生产使用短期 token + TLS 或基于 OS ACL 的 Named Pipe。
•	能力发现：实现 /capabilities 返回能力列表与版本、支持的选项（用于客户端自动适配）。
•	数据格式：统一 JSON，错误使用 { code, message, details }。
一、核心调试控制（高优先级）
1.	launch
•	描述：启动可调试进程并将其置于调试器下（与 VS 的启动行为一致）。
•	参数：{ exePath, args[], cwd, env?, debugConfig? }
•	返回：{ sessionId, processId }
•	DAP 对应：launchRequest
•	要点：区分普通程序 vs 单元测试（绝对不要用 launch 来启动 Test Explorer 可运行的单元测试，需提供单测专用接口）。
2.	attach
•	描述：附加到指定进程或通过进程名附加。
•	参数：{ pid | processName, debugEngine? }
•	返回：{ sessionId }
•	DAP 对应：attachRequest
•	要点：需支持 pauseWhenAttached 标志。
3.	continue / pause / stepOver / stepInto / stepOut
•	描述：线程/会话的执行控制。
•	参数：{ threadId? , sessionId? }
•	返回：操作结果/状态
•	DAP 对应：continue/pause/next/stepIn/stepOut
4.	setBreakpoints
•	描述：在文件行处设置/替换断点（支持条件/tracepoint）。
•	参数：{ source: { path }, breakpoints: [{ line, condition?, hitCondition?, logMessage? }] }
•	返回：{ breakpoints: [{ id, verified, message }] }
•	DAP 对应：setBreakpoints
•	要点：支持“追踪点（tracepoint）”即 logMessage，VS 有原生 tracepoint。
5.	waitForBreak / waitForPause
•	描述：等待调试器进入中断态并返回当前停止点信息（便于远端调试 UI）。
•	参数：{ sessionId, timeout? }
•	返回：{ threadId, reason, topFrame }
•	DAP 对应：线程/stopped 事件
二、堆栈 / 帧 / 表达式（高优先级） 6) getCallStack
•	描述：返回指定线程的调用栈，含帧 id，用于后续 evaluate/frameSource。
•	参数：{ threadId, start=0, count=... }
•	返回：{ frames: [{ id, name, source, line, column }] }
•	DAP 对应：stackTrace
7.	getFrameSource
•	描述：获取某帧对应的源代码片段（当堆栈帧没有直接源时）。
•	参数：{ threadId, frameId, contextLines? }
•	返回：{ sourceText, startLine, endLine }
•	要点：可直接映射到 VS 的符号/源服务。
8.	evaluateExpression
•	描述：在指定帧上下文评估表达式。
•	参数：{ expression, frameId?, context? ("watch"|"hover"|"repl") }
•	返回：{ result, type, variablesReference? }
•	DAP 对应：evaluateRequest
•	要点：支持 ShowChildren（展开子字段）与条件求值。注意安全（表达式可能有副作用）。
9.	getThreads
•	描述：列出调试会话中的线程及每个线程的 top frame。
•	参数：{ sessionId }
•	返回：{ threads: [{ id, name, topFrame }] }
•	DAP 对应：threadsRequest
10.	getTaskWaitChain（可选，仅托管/.NET）
•	描述：返回 .NET Task 等的等待链（debugger_get_task_info 对应）。
•	参数：{ threadId }
•	返回：树形任务链
•	限制：仅在托管调试可用时有意义。
三、IDE 输出 / 日志 / 捕获（中优先级） 11) getOutputWindowText
•	描述：获取 VS Debug Output 窗口文本（Debug.WriteLine、异常未抛断的日志等）。
•	参数：{ paneName? }
•	返回：{ text }
•	要点：有助于诊断运行时事件。
12.	enumerateProcesses
•	描述：列出可附加的本地进程（可带过滤）。
•	参数：{ nameFilter?, includeSystem? }
•	返回：[{ pid, exe, title }]
•	DAP 对应：无（辅助能力，用于 UI 列表）
四、工程/代码 文件操作（高优先级） 13) getProjectsInSolution
•	描述：列出解决方案中的项目（示例中返回 PrintSystem.vcxproj 等）。
•	参数：无
•	返回：[{ projectPath }]
•	用途：为 UI 提供上下文、限定文件查找范围。
14.	getFilesInProject
•	描述：返回某项目的文件路径列表。
•	参数：{ projectPath }
•	返回：[{ filePath }]
15.	grepSearch / codeSearch / fileSearch
•	描述：工作区文本/符号搜索（支持 regex、glob）。
•	参数：{ query, isRegexp?, includePattern?, maxResults? }
•	返回：匹配行列表（文件+行号）
•	要点：用于实现“全局查找引用/符号”能力。
16.	readFile / getSymbolsByName / fileSearch
•	描述：读取指定源码行；按符号名查找类/方法定义（get_symbols_by_name）。
•	参数：readFile { filename, startLine, endLine }
•	返回：文本片段（带行号）
•	限制：对大文件需分页。
17.	applyPatch / editFile / createFile / removeFile
•	描述：对代码文件进行结构化修改（apply_patch 遵循差异格式），或直接编辑/新建/删除文件。
•	参数：patch 文本 或 filePath + content
•	返回：操作结果、错误列表
•	要点：apply_patch 格式必须严格遵守；不要用 create_file 编辑已存在的文件；apply_patch 会在提交后触发语法/编译检查，须妥善映射错误给客户端。
五、单元测试（中优先级，需特殊处理） 18) getTests
•	描述：查询 Test Explorer 的测试集合（过滤器支持）。
•	参数：{ filterTypes[], filterValues[], forceBuild? }
•	返回：测试清单（含 testId）
•	要点：不要用 debugger_launch 启动单元测试；应使用 debugger_launch_unit_test_by_id。
19.	launchUnitTestById
•	描述：用调试器启动并调试指定 testId 的单元测试（遵循 VS 的规则）。
•	参数：{ testId, breakpoints? }
•	返回：{ sessionId, result }
•	要点：遵守“绝不用普通 launch 调试单测”的约束。
20.	runTests
•	描述：按过滤器运行测试（不一定进入调试）。
•	参数：{ filterTypes[], filterValues[] }
•	返回：运行结果/报告
六、调试会话管理与诊断流程（中优先级） 21) getCallStack + startIssueResolutionProcess / setIssueResolutionModeState
•	描述：集成问题解决流程（start_issue_resolution_process / set_issue_resolution_mode_state），可用于远端发起有状态的调试流程（涉及多步骤交互）。
•	用途：自动化/半自动化的逐步调试与验证工作流。
22.	getWorkItemInfo / getRemoteRepositoryContext
•	描述：从 Azure DevOps/GitHub Issue 中获取上下文（当用户传入 issue URL 时）。
•	参数：issueUrl 或 searchQueries
•	返回：工单/Issue 内容，支持在远端复现或关联代码位置
•	要点：需要网络访问权限与认证。
七、进程/系统操作（低优先级） 23) enumerateProcesses / attachToProcess
•	描述：列举系统进程并附加，（可选提升权限或提示 UAC）。
•	要点：安全与权限限制，需在本机模式下谨慎开放。
八、工具/协助类（低优先级） 24) askQuestion
•	描述：向用户以交互卡片收集选择（用于调试步骤选择或确认）。
•	用途：在远端 UI 中实现多选决策。
25.	debugger_get_output_window_text / debugger_get_threads / debugger_get_task_info 等（更多细粒度）
•	描述：用于收集更多运行时或托管特定信息。
映射建议（MCP 方法 -> JSON-RPC / HTTP 示例）
•	GET /capabilities
•	POST /debug/launch body: { "exePath":"C:\app.exe","args":["-f"],"cwd":"C:\proj" }
•	POST /debug/attach body: { "pid":1234 }
•	POST /debug/setBreakpoints body: { "source":{"path":"src/main.cpp"},"breakpoints":[{"line":42,"condition":"x>0"}] }
•	POST /debug/evaluate body: { "expression":"obj.Field","frameId":456 }
•	GET /projects returns project list
•	POST /code/search body: { "query":"QTabBar","isRegexp":false,"includePattern":".cpp;.h" }
•	POST /file/patch body: { "patch":"*** Begin Patch\n..." }
实现/限制注意事项（必须写入设计文档）
•	权限与安全：默认仅 localhost，优先使用 Named Pipe；必要时启用 TLS + Token。
•	生命周期：VS 退出或扩展卸载时优雅停止服务并断开所有会话。
•	兼容性：尽量调用公开 VS SDK。如果不得已要用内部 API，记录风险与版本锁定。
•	错误映射：将 VS 异常映射为可重试/不可重试错误码，便于客户端逻辑。
•	单元测试规则：遵守 VS 测试启动约束（不要用普通 launch 调试 test explorer 的测试）。
•	并发：多个客户端同时连接时需做会话隔离或排队策略（调试器通常是单用户会话资源）。
•	表达式求值副作用：明确标注 evaluate API 是否允许产生副作用（如调用属性/方法），并提供 safeEvaluate 选项。
优先级建议（开发阶段）
•	P0（先做）：launch、attach、setBreakpoints、getCallStack、evaluate、continue/pause、getThreads、capabilities、enumerateProcesses
•	P1（随后）：getOutputWindowText、readFile、grepSearch、getProjectsInSolution、apply_patch（只读/diff 支持）
•	P2（后续）：测试相关（getTests, launchUnitTestById）、task wait chain（托管特定）、workitem 集成
示例 capability 条目（返回示例）
•	capability: { name:"launch", description:"启动进程并调试", version:"1.0", options:{ supportsAttach:true, supportsTracepoint:true } }
•	capability: { name:"setBreakpoints", description:"设置断点", options:{ supportsCondition:true, supportsLogMessage:true } }
需要我为你进一步生成的内容
•	JSON‑RPC 接口草案（OpenAPI 风格的 JSON 定义）
•	VSIX/C# POC 骨架：在 Package 初始化时启动本地 HTTP/WebSocket/NamedPipe 服务，并实现 5 个核心方法（launch,setBreakpoints,getCallStack,evaluate,capabilities）
•	VS Code agent (TypeScript) POC：连接 localhost:port，调用 capabilities 并把部分能力映射为 VS Code 命令/调试适配器
请选择要先生成的工件（A: JSON‑RPC 接口定义, B: VSIX POC 源码骨架(C#), C: VS Code agent 示例 TypeScript）。