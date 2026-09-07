# Changelog

All notable changes to the "VsDebugMcp" extension will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
