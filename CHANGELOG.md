# Changelog

All notable changes to the "VsDebugMcp" extension will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
