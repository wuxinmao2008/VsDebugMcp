# AGENTS.md

## Scope

These instructions apply to the entire repository.

## Project Goal

This repository is for planning and implementing a Visual Studio 2026 / VS 18.x MCP integration that exposes IDE capabilities such as build, diagnostics, project context, output, tests, and debugging to external agents.

## Primary Architecture Direction

Use the agreed hybrid architecture as the default direction:

```text
MCP Client / Agent
  -> VsMcpHost (OOP / standalone .NET)
  -> Named Pipe or authenticated localhost IPC
  -> Visual Studio VSIX Bridge
  -> VisualStudio.Extensibility + VSSDK/COM providers
```

Do not pivot to a pure in-process VSIX MCP server or a pure external DTE automation server unless the user explicitly asks to revisit the architecture.

## Required References

Before changing design or implementation direction, consult the existing repository documents:

- `project-blueprint.md` — current architecture, roadmap, provider split, safety model.
- `technical-route-comparison.md` — route comparison and selected hybrid approach.
- `visual-studio-2026-mcp-research.md` — first-round research notes.
- `vs-copilot-log-analysis.md` —脱敏 VS Copilot 日志能力分析。
- `vs-copilot-debugger-log-analysis.md` — DebuggerAgent / debugger capability analysis.
- `vs2026_copilot.md` — initial capability inventory and API notes.

Prefer these documents over reconstructing prior context from memory.

## Implementation Priorities

### Phase 0

Start with a minimal proof of communication and capability discovery:

1. `vs_capabilities`
2. host/bridge health and version metadata
3. OOP MCP host skeleton
4. VSIX bridge skeleton
5. Named Pipe IPC request/response shape
6. shared error model
7. basic audit logging

### Phase 1

After Phase 0 works, implement the low-risk IDE loop:

1. `vs_get_projects_in_solution`
2. `vs_get_files_in_project`
3. `vs_run_build`
4. `vs_get_errors`
5. `vs_get_output_window_logs`
6. `vs_read_file`
7. `vs_file_search`

### Debugger POC

Treat debugger support as a separate validation track. Do not assume APIs are available until verified in a POC.

Initial debugger candidates:

- `vs_debugger_get_info`
- `vs_debugger_set_breakpoints`
- `vs_debugger_get_threads`
- `vs_debugger_get_call_stack`
- `vs_debugger_evaluate_expr`
- `vs_debugger_evaluate_expressions`

## Safety Rules

- Default to local-only operation.
- Prefer Named Pipe IPC with OS ACLs.
- If localhost transport is used, require a short-lived token.
- Do not expose remote access in early phases.
- Treat attach, debugger control, expression evaluation, file edits, process control, and command execution as risky.
- Ask for confirmation before destructive or hard-to-reverse actions.
- Never bypass safety checks such as Git hooks or validation gates.
- Do not run high-risk commands unless explicitly requested and confirmed.
- Do not store or print secrets, tokens, credentials, or private environment values.

## Log and Privacy Rules

- Do not commit raw Visual Studio Copilot logs.
- Do not copy raw log payloads into tracked files.
- Use only the existing脱敏 analysis documents unless the user explicitly asks for local log re-analysis.
- If local logs are analyzed, summarize and redact sensitive paths, IDs, tokens, quotas, and response identifiers.

## Design Constraints

- Use standard MCP tools/resources as the external protocol surface.
- Use explicit handles for stateful operations: `vsInstanceId`, `buildTaskId`, `debugSessionId`, `threadId`, `frameId`, `testRunId`.
- Do not rely on MCP transport sessions to preserve debugger state.
- Keep providers modular and aligned with IDE subsystems: project, build, output, diagnostics, tests, debugger, file/search.
- Prefer public Visual Studio APIs. If an internal API is considered, document the version-lock risk first.
- Avoid over-engineering; implement only the current phase requirements.

## Coding Guidelines

- Read relevant files before modifying them.
- Prefer editing existing files over creating new ones unless a new project artifact is clearly needed.
- Keep comments short and only explain what code cannot show by itself.
- Keep changes focused; do not refactor unrelated code.
- Validate builds/tests after meaningful code changes when a runnable project exists.

## User Workflow Preference

The user prefers planning and design to be preserved in repository Markdown files before major implementation steps. Keep project decisions traceable in existing design documents when the user requests documentation updates.
