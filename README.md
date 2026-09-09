# VsDebugMcp

Visual Studio 2026 / VS 18.x MCP integration using a shared out-of-process Host and an in-process VSIX Bridge.

## Architecture

```text
VS Code / MCP client
	-> Streamable HTTP at http://127.0.0.1:43260
	-> one shared VsDebugMcp.Host per Windows user
	-> vsInstanceId registry and router
	-> per-instance current-user Named Pipe RPC
	-> Visual Studio VSIX Bridge
```

The VSIX packages a `win-x64` Framework-Dependent Host (reusing Visual Studio 2026's bundled .NET 8 runtime or system .NET 8) and ensures that it is running when Visual Studio loads. MCP clients do not launch the Host or need its installation path.

Each Visual Studio process registers a session identity derived from its PID and process start time. Tools may omit `vsInstanceId` when one instance is registered; when multiple instances are registered, callers must select one explicitly.

## Current capabilities

### Solution, Project & Build Context
- `vs_health` — MCP server and Visual Studio bridge connectivity check
- `vs_capabilities` — Active IDE capability discovery
- `vs_list_instances` / `vs_find_instances` — Multi-instance Visual Studio management
- `vs_get_projects_in_solution` — Solution structure and project discovery
- `vs_get_files_in_project` — Comprehensive project source file tree and C++ filters
- `vs_run_build` — Asynchronous IDE build execution
- `vs_get_build_status` — Build task state polling
- `vs_cancel_build` — Active build cancellation
- `vs_get_errors` — Error list diagnostics extraction with resilient dual-track Build Output fallback
- `vs_get_output_window_logs` — Raw Build Output and IDE pane logs

### Debugger Launch, Control & Diagnostics
- `vs_debugger_start` — Programmatic F5 launch with smart landing break detection
- `vs_debugger_get_info` — Debugger mode, active process, thread, and break reason
- `vs_debugger_set_breakpoints` — Source line breakpoint management with conditional (`whenTrue`/`whenChanged`) and hit count filtering
- `vs_debugger_get_threads` — Multi-thread diagnostic snapshot inspection
- `vs_debugger_get_call_stack` — Call stack capture upon pause/breakpoint
- `vs_debugger_get_locals` — Arguments and local variables inspection
- `vs_debugger_get_exception_info` — Deep exception inspection on break mode (CLR type, message, HResult, and stack trace)
- `vs_debugger_evaluate_expr` — Single expression evaluation with timeout protection
- `vs_debugger_evaluate_expressions` — Single-RPC batch expressions evaluation
- `vs_debugger_step_over` / `step_into` / `step_out` — Stepping execution control
- `vs_debugger_continue` / `pause` / `stop` — Session continuation, pause, and termination
- `vs_debugger_freeze_thread` / `thaw_thread` — Precise thread suspension and resumption during pause
- `vs_debugger_set_next_statement` — Dynamic instruction pointer repositioning without recompilation

### Process Attach & Module Diagnostics (Phase 3C)
- `vs_debugger_get_processes` — Local process enumeration with debugger attachment eligibility
- `vs_debugger_attach_process` — Programmatic debugger attach to running processes by PID
- `vs_debugger_detach` — Graceful debugger detachment leaving processes running
- `vs_debugger_get_modules` — Loaded module enumeration with symbol load status (PDB), file paths, and memory addresses

### Active Context & Editor Navigation (Phase 4A & 4B)
- `vs_get_active_document` — Foreground active editor document inspection with dirty flag, language, line count, cursor position, and text selection
- `vs_navigate_to` — Smooth navigation to files with precise line/column positioning
- `vs_get_solution_configurations` — Solution configuration and platform discovery
- `vs_set_solution_configuration` — Solution configuration and platform activation (Debug <-> Release, x64, etc.)

### Advanced Debugger Controls & Breakpoint Lifecycle (Phase 4C & 5A)
- `vs_debugger_freeze_thread` / `vs_debugger_thaw_thread` — Precise thread suspension and resumption
- `vs_debugger_set_next_statement` — Instruction pointer repositioning during break mode
- `vs_debugger_list_breakpoints` — Structured inspection of all breakpoints in the solution
- `vs_debugger_clear_breakpoints` — Guarded breakpoint removal (all, per-file, or by ID)
- `vs_debugger_toggle_breakpoint` — Independent breakpoint enable/disable toggling

### Multi-Pane Output & Diagnostics (Phase 5B)
- `vs_get_output_panes` — Enumeration of all Output Window panes (Build, Debug, custom panes)
- Enhanced `vs_get_output_window_logs` with standard Debug pane support (`qDebug()`, `OutputDebugString`)
- Smart optimization warnings on Release variables (`variable_optimized_in_release`)

### Mixed-Mode Engines & Solution Process Auto-Attach (Phase 5C)
- `vs_debugger_find_solution_processes` — Auto-discovery of running processes matching solution projects
- `vs_debugger_auto_attach` — One-click batch auto-attachment for solution processes
- `vs_debugger_attach_process` with explicit mixed-mode `engines` parameter (`Native`, `Managed`)

### Aggregated Diagnostic Snapshot & Memory Reading (Phase 5D)
- `vs_debugger_get_snapshot` — Atomic, single-call capture of full debugging context (process, thread, stack, locals, debug logs)
- `vs_debugger_read_memory` — Raw virtual memory byte inspection via Win32 `ReadProcessMemory` (HexDump and Base64)

### Client Onboarding & Ecosystem Guide (Phase 4D)
- Native Visual Studio top-level menu: **`Extensions (扩展) -> VsDebugMcp`**
- Interactive WPF Configuration Guide with presets for **VS Code**, **Cursor**, **Claude Desktop**, **Antigravity**, and **Codex/Windsurf**
- Clear **Global vs. Local (Workspace)** scope differentiation with one-click clipboard copying
- Strictly zero-intrusion: 100% human-controlled, never modifies user configuration files automatically

### In action

![VS Code Agent using VsDebugMcp](assets/screenshot_01.png)

## Projects

- `src/VsDebugMcp.Protocol` — shared IPC contracts, framing, instance identity and error model; targets `net8.0` and `netstandard2.0`.
- `src/VsDebugMcp.Host` — framework-dependent .NET 8 Streamable HTTP MCP Host, instance registry and Named Pipe Bridge client.
- `src/VsDebugMcp.Vsix` — SDK-style VSSDK Bridge, Host launcher and Visual Studio instance registrar.

## VS Code configuration

```json
{
	"servers": {
		"vs-debug-mcp": {
			"type": "http",
			"url": "http://127.0.0.1:43260"
		}
	},
	"inputs": []
}
```

The Host listens only on IPv4 loopback. If port `43260` is occupied, startup fails safely and does not select another port or terminate the occupying process.

## Lifecycle

- A loaded VSIX probes the current-user Host control pipe.
- If no compatible Host is running, the VSIX starts the packaged Host.
- Every Visual Studio instance has an independent Bridge pipe.
- The VSIX sends a heartbeat every 5 seconds.
- The Host removes an instance after 15 seconds without a heartbeat.
- The Host exits immediately after the final registered instance is removed.

## Build

Use the VS Code tasks:

- `build: managed`
- `build: vsix`
- `build: vsix: release`
- `build: all`
- `deploy: vsix`

The VSIX project must be built with the Visual Studio 18 MSBuild installation. Its build publishes the Host as `win-x64` framework-dependent and embeds it under `Host/` in the VSIX package.

Ordinary builds do not deploy the extension. Deployment requires closing the relevant Visual Studio instance and running `deploy: vsix` explicitly.

## Validation status

- Automated unit tests: 180/180 PASS (100% across Protocol and Host test suites: 21 Protocol, 159 Host).
- End-to-end online acceptance: Verified in Visual Studio 2026 (VS 18.x) Experimental Instance across the full MCP client → HTTP Host (`127.0.0.1:43260`) → instance router → Named Pipe → VSIX Bridge path.
- Verified capability domains: Solution structure & files context, IDE build lifecycle & raw output capture, Debugger F5 launch / break detection / stepping / locals / multi-thread inspection / expression evaluation, and Test Explorer test discovery / execution / status polling / cancellation / test-driven debugging with smart break landing.

## Security and privacy

- MCP HTTP is bound only to `127.0.0.1:43260`.
- Host control and Bridge pipes are restricted to the current Windows user.
- Remote access is not supported.
- The Host does not terminate unknown processes during port conflicts.
- Logs must not include request payloads, credentials, environment variables or raw Visual Studio Copilot logs.

The project targets `vs2026_5`. The VSIX currently pins `Microsoft.VisualStudio.Sdk` to `17.14.40265` while building with Visual Studio 18 MSBuild.

## Release notes

See [CHANGELOG.md](CHANGELOG.md) for detailed version history and release notes.

## Acknowledgements & Ecosystem References

We would like to express our gratitude to the open-source community and pioneering IDE integrations, especially to:

- **[GitHub Copilot for Visual Studio](https://learn.microsoft.com/en-us/visualstudio/ide/visual-studio-github-copilot-extension)** by [GitHub](https://github.com/) & [Microsoft](https://www.microsoft.com/) — whose native in-IDE agent workflows (including the DebuggerAgent, test runner, and solution diagnostic flows) provided foundational capability taxonomy, domain interaction models, and interface design references for standardizing Visual Studio capabilities into open MCP tools.
- **[Qt Creator MCP Server](https://doc.qt.io/qtcreator/creator-how-to-mcp-server.html)** by [The Qt Company](https://www.qt.io/) — a pioneering IDE-native MCP implementation. Its modular subsystem provider architecture (organizing project, debugger, test, and editor capabilities into dedicated providers) and communication inspection patterns served as an essential architectural inspiration for designing VsDebugMcp.
- **[CodingWithCalvin/VS-MCPServer](https://github.com/CodingWithCalvin/VS-MCPServer)** by [Calvin Allen](https://github.com/CalvinAllen) and contributors — an excellent Visual Studio MCP extension for VS 2022/2026. Their transparent issue discussions, architecture evolutions, and battle-tested solutions for IDE concurrency, UI-thread deadlock prevention, and MSBuild/debugger edge cases provided valuable insights for this project. See [docs/ecosystem-reference-vs-mcpserver.md](docs/ecosystem-reference-vs-mcpserver.md) for our detailed ecosystem analysis and lessons learned.



