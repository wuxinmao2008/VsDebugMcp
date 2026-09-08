# Changelog

All notable changes to the "VsDebugMcp" extension will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.19.0] - 2026-09-08

### Added
- **Aggregated Debugger Diagnostic Snapshot & Process Virtual Memory Read (Phase 5D)**:
  - **Aggregated Debugger Snapshot (`vs_debugger_get_snapshot`)**:
    - Single-call aggregation of full debugging diagnostics: execution mode, debugged process PID & path, thread ID, last break reason, top stack frame, full call stack, local variables/arguments, and recent Output window logs.
    - Integrates smart Release mode `<optimized away>` warning detection on local variables.
    - Gracefully handles design mode (`isDebugging: false`, empty collections, zero errors).
    - Captures language-independent Output window debug logs (`VSConstants.OutputWindowPaneGuid.DebugPane_guid`) including `qDebug()`, `OutputDebugString`, and CoreCLR runtime logs.
  - **Process Virtual Memory Read (`vs_debugger_read_memory`)**:
    - Safely inspects raw virtual memory bytes from the debugged target process via Win32 `ReadProcessMemory` with `PROCESS_VM_READ | PROCESS_QUERY_INFORMATION`.
    - Automatically resolves both direct hexadecimal addresses (`0x00007FFE354A0000`) and pointer/variable expressions (`pBuffer`, `&myStruct`, `m_buffer`).
    - Produces multiple consumable formats:
      - `hexBytes`: Space-separated uppercase hex byte string.
      - `hexDump`: Standard formatted hex dump with 16-byte aligned offsets, 8-byte spacer, and ASCII sidebar.
      - `asciiRepresentation`: Human-readable ASCII preview with unprintable characters replaced by dots.
      - `base64Data`: Raw bytes encoded in Base64 for binary data transfers.
    - Enforces safety limits: byte count restricted to 1 - 4096 bytes.
    - Structured error handling: `invalid_memory_address`, `memory_read_failed`, `debugger_not_debugging`.
  - **Industrial PLC & Qt Best Practices Guide (`docs/qt-plc-debugging-guide.md`)**:
    - Provides detailed Agent SOP for diagnosing Qt cross-thread ownership issues (`((QObject*)x)->thread()`) and industrial Modbus/PLC packet reverse debugging using memory primitives.
  - Added new error codes: `invalid_memory_address`, `memory_read_failed`.

### Verified
- Automated unit tests: 180/180 PASS (100% across Protocol and Host test suites: 21/21 Protocol tests, 159/159 Host tests).
- Total registered MCP tools expanded from 48 to 50 (all `isStub: false`).
- Live verified against Visual Studio 18 Experimental Instance:
  - `vs_debugger_get_snapshot`: Single-call capture in break mode returning process, thread, stack, locals (`item`, `sum`, `args`), and recent CoreCLR debug logs. Verified design mode safe return.
  - `vs_debugger_read_memory`: Read 64 bytes of PE header from `System.Private.CoreLib.dll` base address (`0x00007FFE354A0000`), verifying `MZ` header, formatted hex dump with ASCII bar, and base64 payload. Verified `debugger_not_debugging` guard in design mode.

## [0.1.18.0] - 2026-09-08

### Added
- **Mixed-Mode Debugging Engine Support & Solution Process Auto-Discovery (Phase 5C)**:
  - **Mixed-Mode Debugging Engines (`vs_debugger_attach_process`)**:
    - Upgraded `vs_debugger_attach_process` with optional `engines` parameter (e.g. `["Native", "Managed"]` or `["本机", "托管"]`).
    - Uses `EnvDTE80.Process2.Attach2(engines)` with substring-insensitive matching against `proc2.Transport.Engines`.
    - Returns attached engine list in `DebuggerAttachResponse.AttachedEngines`.
    - Throws structured error `engine_not_found` with complete list of available engines for the process when an invalid engine is requested.
  - **Solution Running Process Auto-Discovery (`vs_debugger_find_solution_processes`)**:
    - Scans active solution projects and discovers matching running OS processes by process name, project name, or assembly/output name.
    - Identifies startup projects (`isStartupProject: true`) via `SolutionBuild.StartupProjects`.
    - Detects whether processes are currently being debugged by this instance (`isBeingDebugged`).
    - Supports `startupOnly` filtering.
  - **One-Click Batch Auto-Attach (`vs_debugger_auto_attach`)**:
    - Automatically discovers and attaches to running solution processes without requiring manual PID lookup.
    - Supports filtering by `processNames`, `startupOnly`, and specifying custom mixed-mode `engines`.
    - Gracefully handles already debugged processes with `already_debugged` warning without failing entire batch.
    - Throws `no_solution_processes_found` when no target processes match.
  - Added new error codes: `engine_not_found`, `no_solution_processes_found`.

### Verified
- Automated unit tests: 165/165 PASS (100% across Protocol and Host test suites: 20/20 Protocol tests, 145/145 Host tests).
- Total registered MCP tools expanded from 46 to 48.
- Live verified against Visual Studio 18 Experimental Instance:
  - `vs_debugger_find_solution_processes`: Discovered running `SampleApp.exe` (PID: 13060), identified startup project status, and tracked live `isBeingDebugged` state.
  - `vs_debugger_attach_process`: Attached with mixed-mode engines `["本机", "托管"]`, correctly bound to `"本机"` and `"托管(.NET Core、.NET 5+)"`. Tested and verified `engine_not_found` exception with available engine listings.
  - `vs_debugger_auto_attach`: Successfully attached solution processes in batch, verified `no_solution_processes_found` error, and verified idempotent `already_debugged` warning.

## [0.1.17.0] - 2026-09-08

### Added
- **Solution Configuration Activation, Multi-Pane Output Logs & Smart Optimization Warnings (Phase 5B)**:
  - **Solution Configuration Switching (`vs_set_solution_configuration`)**:
    - Switches active build configuration and platform across the solution (e.g. `Debug` <-> `Release`, `x64` <-> `Any CPU`) via COM `cfg.Activate()`.
    - Enforces defensive safety checks: rejects configuration switching while debugging is active (`dbgRunMode` or `dbgBreakMode`) with `cannot_switch_configuration_while_debugging`.
  - **Output Window Multi-Pane Introspection (`vs_get_output_panes`)**:
    - Enumerates all available panes in the Visual Studio Output window (Name, GUID, isBuiltIn), allowing agents to discover both built-in panes and custom application panes (e.g. `UILOG`, `Database`, `VsDebugMcp`).
  - **Debug Output Window Pane Support (`vs_get_output_window_logs`)**:
    - Upgraded `ReadPaneOutput` with language-independent `VSConstants.OutputWindowPaneGuid.DebugPane_guid` (`{FC076020-078A-11D1-A7DF-00A0C9110051}`).
    - Allows agents to capture live debug output including Qt `qDebug()`, native `OutputDebugString`, and CLR trace messages on localized Visual Studio installations (e.g. Chinese `调试`).
    - Added resilient fallback for uninitialized/empty output window panes.
  - **Smart Optimization Warnings in Debugger**:
    - Automatically detects `<optimized away>` or unavailable evaluation results while the active solution configuration is `Release`.
    - Attaches structured `BridgeWarning` with guidance to switch to `Debug` configuration via `vs_set_solution_configuration(configuration: "Debug")`.
  - Added new error codes: `configuration_not_found`, `output_pane_not_found`, `cannot_switch_configuration_while_debugging`.

### Verified
- Automated unit tests: 151/151 PASS (100% across Protocol and Host test suites: 19/19 Protocol tests, 132/132 Host tests).
- Total registered MCP tools expanded from 44 to 46.
- Live verified against Visual Studio 18 Experimental Instance:
  - `vs_get_solution_configurations` / `vs_set_solution_configuration`: Switched `Debug` -> `Release` -> `Debug` with full state fidelity.
  - Guard verification: configuration switching successfully rejected while debugging with `cannot_switch_configuration_while_debugging`.
  - `vs_get_output_panes`: Discovered 11 panes (including Chinese `生成`, `调试`, `VsDebugMcp`, `GitHub Copilot`).
  - `vs_get_output_window_logs`: Successfully retrieved both build logs and live debug output (CoreCLR module load traces).

## [0.1.16.0] - 2026-09-08

### Added
- **Call Stack Strong-Typed Native Attribute Extraction & Breakpoint Full Lifecycle Management (Phase 5A)**:
  - **Native Call Stack Resolution (`vs_debugger_get_call_stack`)**:
    - Integrated with COM `EnvDTE90a.StackFrame2` to extract strongly-typed source file paths (`FileName`), line numbers (`LineNumber`), column numbers (`ColumnNumber`), and user code flags (`UserCode`).
    - Eliminates the limitation where C++/Qt non-managed stack frames only returned function names without source locations.
    - Preserves smart fallback text parsing for legacy and mixed formatting.
  - **Breakpoint Listing (`vs_debugger_list_breakpoints`)**:
    - Queries all breakpoints currently configured in the Visual Studio solution.
    - Exposes file paths, line/column coordinates, conditional expressions (`whenTrue`/`whenChanged`), hit count conditions (`equal`/`greaterOrEqual`/`multiple`), current hit count, enabled status, and binding state (`isBound`).
    - Supports filtering by file path and enabled-only status.
  - **Safe Breakpoint Deletion (`vs_debugger_clear_breakpoints`)**:
    - Enforces safety defenses: requires at least one of `clearAll: true`, `filePath`, or `breakpointId` to prevent accidental deletion.
    - Supports clearing all breakpoints solution-wide, clearing by file path (or file + line), or deleting by specific breakpoint ID (`path:line`).
  - **Breakpoint Enable/Disable Toggling (`vs_debugger_toggle_breakpoint`)**:
    - Dynamically toggles or explicitly sets breakpoint enabled state without losing conditional expressions or hit count configurations.
    - Supports targeting by `breakpointId` or `(filePath, line)`.
  - Added new bridge error codes: `breakpoint_not_found`, `invalid_breakpoint_target`.

### Verified
- Automated unit tests: 141/141 PASS (100% across Protocol and Host test suites: 18/18 Protocol tests, 123/123 Host tests).
- Total registered MCP tools expanded from 38 to 41.

## [0.1.15.0] - 2026-09-08

### Added
- **Ecosystem Integration & Client Onboarding (Phase 4D)**:
  - **IDE Native Configuration Guide**: Added a dedicated top-level entry in Visual Studio menu: `Extensions (扩展) -> VsDebugMcp` that opens a clean, modern WPF dialog (`ClientConfigWindow`).
  - **Zero-Intrusion Safety Principle**: Non-intrusive by design—strictly read-only presentation of ready-to-use configuration samples and recommended paths; never automatically modifies or overwrites user configuration files.
  - **Scope Differentiation (Global vs. Local)**: Supports both Global (User-level) and Local (Workspace-level) scope selection across major AI coding clients.
  - **Multi-Client Support**: Out-of-the-box presets for **VS Code**, **Cursor**, **Claude Desktop**, **Antigravity**, and **Codex/Windsurf**.
  - **One-Click Clipboard Copying**: High-visibility copy-to-clipboard functionality with live confirmation feedback ("已复制 ✓").
  - **Clean Protocol Surface**: Onboarding is purely IDE-facing; keeps the external MCP tool surface clean and unpolluted without injecting unnecessary configuration tools to connected agents.
  - **`ClientConfigGenerator` in Protocol**: Unified internal configuration engine powering the WPF window.

### Verified
- Automated unit tests: 122/122 PASS (100% across Protocol and Host test suites, 17/17 Protocol tests, 105/105 Host tests).

## [0.1.14.0] - 2026-09-08

### Added
- **Advanced Debugger Controls (Phase 4C)**:
  - `vs_debugger_freeze_thread`: Freezes (suspends) a specific thread by ID during debugging using native `EnvDTE.Thread.Freeze()`, allowing agents to isolate race conditions and lock interfering threads.
  - `vs_debugger_thaw_thread`: Thaws (resumes) a previously frozen thread during debugging via `EnvDTE.Thread.Thaw()`.
  - `vs_debugger_set_next_statement`: Sets the next instruction to be executed by the debugger (instruction pointer) to a designated source line/column or active document cursor position using `EnvDTE.Debugger.SetNextStatement()`, allowing agents to dynamically skip failing lines or re-execute statements without recompilation.
  - Added `isFrozen` property in `ThreadInfo` and `vs_debugger_get_threads` response.
  - Added structured bridge error codes: `thread_not_found`, `invalid_next_statement`.

### Verified
- Automated unit tests: 118/118 PASS (100% across Protocol and Host test suites, 13/13 Protocol tests, 105/105 Host tests).

## [0.1.13.0] - 2026-09-08

### Added
- **Multi-Instance Smart Working Directory Routing (Phase 4B)**:
  - Extended `VisualStudioInstanceRegistry.Resolve(string? vsInstanceId, string? targetPath = null)` to support prefix/containment matching of `targetPath` against open `SolutionFilePath` and directory.
  - Automatically routes MCP tool requests (`vs_get_files_in_project`, `vs_get_errors`, `vs_debugger_set_breakpoints`, `vs_navigate_to`) to the matching Visual Studio instance when `vsInstanceId` is omitted, eliminating ambiguity errors in multi-solution / multi-window workflows.
  - Enhanced `vs_find_instances` with full solution directory and project context search matching.
- **Build Deadlock Guard during Debugging (Phase 4B)**:
  - Added pre-build state guard in `SolutionBuildProvider`: prevents `vs_run_build` when the Visual Studio debugger is in run or break mode (`dbgRunMode` / `dbgBreakMode`).
  - Immediately rejects build requests with `BridgeErrorCodes.DebuggerRunningCannotBuild` (`debugger_running_cannot_build`), avoiding native Visual Studio modal blocking dialogs and IPC pipe hangs.
- **Host Process Lifecycle Job Object Hardening (Phase 4B)**:
  - Added Windows Kernel Job Object encapsulation (`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`) in `SharedHostProcessManager`.
  - Automatically associates child `VsDebugMcp.Host` process with the VSIX parent Visual Studio process, guaranteeing that the OOP MCP Host is terminated by the OS even if Visual Studio crashes or is killed ungracefully.

### Verified
- Automated unit tests: 115/115 PASS (100% across Protocol and Host test suites, 12/12 Protocol tests, 103/103 Host tests).

## [0.1.12.0] - 2026-09-08

### Added
- **Active Context & Editor Navigation (`vs_get_active_document`, `vs_navigate_to`, `vs_get_solution_configurations`) (Phase 4A)**:
  - `vs_get_active_document`: Inspects the active foreground document in Visual Studio, extracting full physical path, file name, unsaved dirty status, read-only flag, language, total line count, 1-based cursor line/column, and selection details (selection range and selected text with 10,000-char safety clamp). Returns `hasActiveDocument: false` gracefully when no editor is focused.
  - `vs_navigate_to`: Navigates Visual Studio to the specified file and moves the cursor to the target line and column. Supports absolute physical paths or solution-relative paths, brings the document window to the foreground, and validates file existence (`file_not_found`).
  - `vs_get_solution_configurations`: Queries all build configurations and platforms configured in the open solution (e.g. `Debug|x64`, `Release|ARM64`), identifying the currently active solution configuration and platform.
  - Added new bridge error codes: `file_not_found`, `invalid_navigation_target`, `active_document_unavailable`.

### Verified
- Automated unit tests: 113/113 PASS (100% across Protocol and Host test suites, 12/12 Protocol tests, 101/101 Host tests).

## [0.1.11.0] - 2026-09-07

### Added
- **Process Attachment & Detachment (`vs_debugger_attach_process`, `vs_debugger_detach`, `vs_debugger_get_processes`) (Phase 3C)**:
  - `vs_debugger_get_processes`: Discovers running OS processes via `EnvDTE.Debugger.LocalProcesses` and `DebuggedProcesses`, supporting substring name filtering, PID query, only-debugged filtering, and username/transport extraction.
  - `vs_debugger_attach_process`: Attaches the Visual Studio debugger to a target process by PID or process name with optional break landing probe (`waitForBreak`), returning current mode, break reason, and top stack frame.
  - `vs_debugger_detach`: Safely detaches the debugger from a specific process or all processes (`debugger.DetachAll()`), allowing target processes to remain alive while returning Visual Studio to design mode.
  - Added mode guard `debugger_not_running` and error code `process_not_found`.
- **Loaded Modules & Symbol Diagnostics (`vs_debugger_get_modules`) (Phase 3C)**:
  - Deep module and symbol inspection via `EnvDTE90.Process3.Modules`: extracts module name, disk path, load order, version, hex load/end addresses, symbol file (PDB) path, symbols loaded flag, optimization, and Just My Code (`userCode`) status.
  - Added name filter and `userCodeOnly` filters.

### Verified
- Automated unit tests: 101/101 PASS (100% across Protocol and Host test suites, 11/11 Protocol tests, 90/90 Host tests).

## [0.1.10.0] - 2026-09-07

### Added
- **Structured Diagnostics Dual-Track & Fallback (`vs_get_errors`) (Phase 3B)**: Refactored error extraction into a resilient dual-track engine:
  - Primary track: queries `IErrorList.TableControl.Entries` directly for active error snapshots across build sources, avoiding subscription quiescence timeouts.
  - Secondary track (Build Output fallback): automatically parses raw build output logs via built-in MSVC and MSBuild regex engines (`MsvcOrClangRegex` and `MsBuildGeneralRegex`) when the Error Table is unpopulated or in transit.
  - Completely eliminates `diagnostics_unavailable` errors, ensuring reliable structured diagnostic feedback.
- **Exception Diagnosis (`vs_debugger_get_exception_info`) (Phase 3B)**: Added deep exception inspection during break mode:
  - Evaluates `$exception` to capture `exceptionType` (full qualified CLR type), `message`, hex-formatted `hresult`, `source`, raw `stackTrace`, `innerException`, and `rawDetails`.
  - Added native/unmanaged break reason fallback for C++ and SEH exceptions.
  - Added mode guard `debugger_not_paused` when called outside break mode.
- **Advanced Breakpoints Control (`vs_debugger_set_breakpoints`) (Phase 3B)**:
  - Added `conditionType` parameter supporting `"whenTrue"` (default) and `"whenChanged"`.
  - Added hit count parameters `hitCountTarget` (int) and `hitCountType` (`"equal"`, `"greaterOrEqual"`, `"multiple"`).
  - Enriched `BreakpointInfo` with `currentHitCount`, `conditionType`, `hitCountTarget`, and `hitCountType`.

### Verified
- Automated unit tests: 78/78 PASS (100% across Protocol and Host test suites).
- Full end-to-end online acceptance in Visual Studio 2026 (VS 18.x) Experimental Instance against `sample/SampleSolution.slnx` via `scripts/test_acceptance_phase3b.py`:
  - `vs_health` & `vs_capabilities`: confirmed 28 capabilities active, `vs_debugger_get_exception_info` is active (`isStub=false`).
  - `vs_get_errors`: verified dual-track fallback, eliminated `diagnostics_unavailable` timeout exceptions.
  - `vs_debugger_set_breakpoints`: successfully placed conditional breakpoint `a > 0` with hit count target `1` (`whenTrue`, `equal`).
  - `vs_get_tests`: discovered 4 tests, locked target exception test `Divide_ByZero_ThrowsException`.
  - `vs_debug_test_by_id`: launched test debugging, landed in break mode on unhandled exception (`mode: break`, `breakReason: exception_unhandled`).
  - `vs_debugger_get_exception_info`: successfully inspected deep exception state (`System.DivideByZeroException`, stack trace at `Calculator.cs:line 7`).
  - `vs_debugger_stop`: cleanly terminated debugging and returned to design mode.


## [0.1.9.0] - 2026-09-07

### Added
- **Test-Driven Debugging & Thread Diagnostics (Phase 3A)**: Added integrated test debugging and multi-thread inspection:
  - `vs_debug_test_by_id`: Launches debugging for a specific unit test via `OperationBroker.DebugTestsByFilterAsync`, with smart breakpoint landing probe (`waitForBreak`). Automatically captures and returns the top stack frame and break reason upon pausing at a breakpoint or unhandled exception.
  - `vs_debugger_get_threads`: Inspects all threads in the active debugging target process via `EnvDTE.Debugger` (`CurrentProgram.Threads` / `CurrentProcess.Threads`), reporting Thread ID, name, alive status, suspend count, priority, current thread marker (`isCurrent`), and top frame.
  - Added mode guard `debugger_already_running` when attempting to debug a test while an active debug session exists.
  - Added test run guard `test_run_busy` and safe fallback `test_not_found`.

### Fixed
- Fixed blocking `OperationBroker.DebugTestsByFilterAsync` call: Invoking and directly awaiting the task blocked until the entire debugging session terminated; refactored to launch asynchronously via `JoinableTaskFactory.RunAsync` and perform non-blocking 100ms status polling to immediately respond when `debugger.CurrentMode == dbgBreakMode`.
- Fixed `EnvDTE.Process.Threads` missing property runtime exception by traversing `debugger.CurrentProgram.Threads` first, falling back to `debugger.CurrentProcess.Programs[].Threads`.
- Fixed .NET Framework 4.7.2 compilation compatibility by providing self-contained clamp utility functions.

### Verified
- Automated unit tests: 74/74 PASS (100% across Protocol and Host test suites).
- Full end-to-end online acceptance in Visual Studio 2026 (VS 18.x) Experimental Instance against `sample/SampleSolution.slnx`:
  - `vs_health` & `vs_capabilities`: confirmed all 27 capabilities discovered with `vs_debug_test_by_id` and `vs_debugger_get_threads` active (`isStub=false`).
  - `vs_get_tests`: discovered 3 tests, extracted target test `SampleTests.CalculatorTests.Multiply_TwoNumbers_ReturnsProduct`.
  - `vs_debugger_set_breakpoints`: set breakpoints in business service (`Calculator.cs:6`) and test code (`CalculatorTests.cs:18`).
  - `vs_debug_test_by_id` (`waitForBreak=True`): launched test debugging, hit breakpoint within ~1.8s, returning `testRunId`, `debuggerMode: "break"`, `lastBreakReason: "breakpoint"`, and top frame.
  - `vs_debugger_get_threads`: inspected all 32 active threads, correctly identified current thread (TID: 9608).
  - `vs_debugger_get_locals`: verified locals and arguments on current frame (`this`, `result = 0`).
  - `vs_debugger_continue` & `vs_debugger_stop`: resumed execution and cleanly returned to design mode.

## [0.1.8.0] - 2026-09-07

### Added
- **Test Explorer / VSTest Integration (Direction B / Route 2)**: Added full hybrid Visual Studio Test Explorer integration via MEF services (`ITestsService`, `IOperationState`) and package preload:
  - `vs_get_tests`: Discovers tests in the current solution with optional project and search term filters.
  - `vs_run_tests`: Triggers asynchronous test execution for specific test IDs or all discovered tests, returning a dedicated `testRunId`.
  - `vs_get_test_run_status`: Queries test execution status (`pending`, `running`, `completed`, `cancelled`, `failed`), summary counts (passed, failed, skipped), durations, and detailed per-test outcome records.
  - `vs_cancel_test_run`: Programmatically cancels an active test run via `IOperationState`.
  - Added single-run mutual exclusion guard (`test_run_busy`) and safe fallback error handling (`test_run_not_found`, `test_window_unavailable`).
  - Added sample test suite `sample/SampleTests` (xUnit .NET 8) with 3 unit tests integrated into `SampleSolution.sln` and `SampleSolution.slnx`.

### Fixed
- Fixed premature test run completion caused by `TestOperationStates.ChangeDetectionFinished (0x40004)` containing the `Finished (0x4)` bitmask flag during solution build phase; narrowed state filter to `TestExecutionFinished`, `TestExecutionCancelAndFinished`, and `OperationSetFinished`.
- Fixed `ITestsService.RunTestsAsync(targetGuids)` zero-match bug (due to `TestCaseId` vs `TestCaseRecord.Id` internal mismatch) by invoking native `OperationBroker.ExecuteAllTestsAsync` for full runs, and precisely constructing 5-parameter `SearchQuery` in `Microsoft.VisualStudio.TestWindow.Internal.dll` for `ExecuteTestsByFilterAsync` single-test runs.
- Fixed `OutputWindowProvider.cs` source pane resolution to support `"VsDebugMcp"`, `"tests"`, `"build"`, and general IDE panes.

### Verified
- Automated unit tests: 68/68 PASS (100% across Protocol and Host test suites).
- Full end-to-end online acceptance in Visual Studio 2026 (VS 18.x) Experimental Instance against `sample/SampleSolution.slnx`:
  - `vs_health` & `vs_capabilities`: confirmed all 4 test tools active with `isStub=false`.
  - `vs_get_tests`: discovered all 3 unit tests in `SampleTests`, verified filter matching (`Multiply`).
  - `vs_run_tests` (full suite): executed asynchronously, polled from `running` to `completed` in ~2.8s, all 3 tests passed with per-test duration metrics.
  - `vs_run_tests` (single test): successfully filtered and executed `Multiply_TwoNumbers_ReturnsProduct` independently.
  - Concurrency & cancellation: verified mutual exclusion (`test_run_busy`) on simultaneous runs and clean termination via `vs_cancel_test_run`.

## [0.1.7.0] - 2026-09-05

### Added
- **Debugger Launch & Diagnostic Enhancements (Route 1)**: Added programmatic debugger launch and batch diagnostics tools via `EnvDTE.Debugger` and `EnvDTE.StackFrame`:
  - `vs_debugger_start`: Starts debugging the active startup project in the open solution (equivalent to F5) with smart landing probe (`waitForBreak`).
  - `vs_debugger_evaluate_expressions`: Evaluates multiple expressions or variables in batch within the context of the current or specified stack frame, isolating per-item errors.
  - `vs_debugger_get_locals`: Inspects all arguments and local variables available in the active or specified stack frame, eliminating variable name guesswork.
  - Added mode guard `debugger_already_running` when attempting to start an active debug session.
  - Added deployment helper script `scripts/deploy-exp.ps1` and updated `deploy: vsix` task to ensure safe, file-lock-free updates.

### Fixed
- Fixed .NET Framework compatibility in `DebuggerProvider.cs` by replacing .NET Core-only `Environment.TickCount64` with `System.Diagnostics.Stopwatch.StartNew()`.

### Verified
- Automated unit tests: 55/55 PASS (100%).
- Full end-to-end online acceptance in Visual Studio 2026 (VS 18.9.12120.119) Experimental Instance against `sample/SampleSolution.slnx`: verified programmatic F5 start, break landing, locals inspection, step over, batch evaluation, step into, argument inspection, step out, and stop.

## [0.1.6.0] - 2026-09-05

### Added
- **Debugger Execution Control (Direction A)**: Added Visual Studio debugger execution control tools via `EnvDTE.Debugger` with UI thread synchronization, concurrency mutex locks, and mode guards:
  - `vs_debugger_step_over`: Steps over the next statement or function call while paused in break mode.
  - `vs_debugger_step_into`: Steps into the next statement or function call while paused in break mode.
  - `vs_debugger_step_out`: Steps out of the current function to its caller while paused in break mode.
  - `vs_debugger_continue`: Resumes execution until the next breakpoint or program termination.
  - `vs_debugger_pause`: Pauses (breaks) the currently executing debuggee.
  - `vs_debugger_stop`: Stops the active debugging session and returns Visual Studio to design mode.
  - **Immediate Landing Feedback**: Unified `DebuggerExecutionResponse` automatically extracts and returns the top stack frame (`topFrame`) upon entering break mode.
  - **Concurrency Guard**: Added `_executionLock` semaphore guard in `DebuggerProvider` to prevent re-entrant command corruption (`debugger_busy`).

## [0.1.5.0] - 2026-09-05

### Added
- **Debugger POC (Read-only observation track)**: Added core Visual Studio debugger inspection and breakpoint tools via `EnvDTE.Debugger` with UI thread synchronization and mode guards:
  - `vs_debugger_get_info`: Queries current debugger mode (design, running, break), active process, thread, and last break reason.
  - `vs_debugger_set_breakpoints`: Sets, toggles, or clears source line breakpoints in the solution.
  - `vs_debugger_get_call_stack`: Captures stack frames (function signatures, module, file/line heuristics) when paused at a breakpoint or exception.
  - `vs_debugger_evaluate_expr`: Evaluates expressions or variables at specified stack frames with timeout protection and side-effect control.

## [0.1.4.0] - 2026-09-05

### Added
- **Project Files Context (`vs_get_files_in_project`)**: Added MCP tool and bridge provider to query files belonging to one or all loaded projects in the Visual Studio solution.
  - Implemented via high-performance native COM hierarchy traversal (`IVsHierarchy` + `IVsProject`), avoiding UI thread blocking.
  - Preserves C++ virtual filter classifications (`FilterPath`), relative paths to project root, and physical file paths.
  - Supports filtering by project ID/name/path and optional extension filter (e.g. `.cpp;.h`).
  - Automatically filters out external SDK dependencies (`External Dependencies` / `外部依赖项`).

## [0.1.3.0] - 2026-09-04

### Changed
- **Package Size Optimization**: Switched `VsDebugMcp.Host` from self-contained to framework-dependent deployment (`--self-contained false`), reducing the VSIX extension package size from ~103 MB to ~5 MB.
- **Runtime Resolution**: Enhanced `SharedHostProcessManager` to automatically locate Visual Studio 2026's bundled .NET 8 runtime (`dotnet\net8.0\runtime`) and configure `DOTNET_ROOT` and `PATH` on launch, with fallback to system .NET 8.

### Fixed
- Fixed MSBuild/NuGet package readme analysis warning by setting `<IsPackable>false</IsPackable>` in the VSIX project.

### Added
- Associated `CHANGELOG.md` with `<ReleaseNotes>` in `source.extension.vsixmanifest` for Visual Studio Extension Manager and Marketplace update history.

## [0.1.2.0] - 2026-09-04

### Added
- **Diagnostic UX**: Added dedicated "VsDebugMcp" Output Window pane for host launch and bridge diagnostic logs.
- **InfoBar Notifications**: Added Visual Studio InfoBar banner alerts for connection failures, port conflicts, or startup timeouts.
- **Status Bar Integration**: Added IDE status bar indicator showing current MCP service and bridge connectivity state.

## [0.1.1.0] - 2026-08-20

### Added
- **Fixed Streamable HTTP Host**: Standardized Host to loopback `http://127.0.0.1:43260` Streamable HTTP MCP server.
- **Multi-Instance Routing**: Added instance registry and per-instance Named Pipe RPC routing by `vsInstanceId`.
- **Solution & Build Control**: Added MCP tools for solution project discovery (`vs_get_projects_in_solution`), build lifecycle control (`vs_run_build`, `vs_get_build_status`, `vs_cancel_build`), and build output retrieval (`vs_get_output_window_logs`).

## [0.1.0.0] - 2026-08-10

### Added
- Phase 0 initial prototype: proof-of-concept communication over Named Pipe IPC between Console Host and in-process VSIX Bridge.
