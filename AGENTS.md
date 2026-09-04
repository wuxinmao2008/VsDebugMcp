# AGENTS.md

## Scope

These instructions apply to the entire repository.

## Project Goal

This repository is for planning and implementing a Visual Studio 2026 / VS 18.x MCP integration that exposes IDE capabilities such as build, diagnostics, project context, output, tests, and debugging to external agents.

## Primary Architecture Direction

Use the agreed hybrid architecture as the default direction:

```text
MCP Client / Agent
  -> VsMcpHost (OOP / Framework-Dependent .NET 8, reusing VS-bundled runtime)
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
- **Build and Deployment Execution**: When building or packaging the VSIX, or when running commands that may hang or take long in background tasks, the agent must not wait indefinitely in background. Instead, pause, display the exact MSBuild/VS Code task command to the user, prompt them to run it locally, and resume only after the user reports the result.
- **Packaging and Runtime Constraint**: The MCP Host must be published as framework-dependent (`win-x64`, `--self-contained false`), relying on the host Visual Studio 2026's bundled .NET 8 runtime (or system .NET 8) to keep VSIX package size under ~5 MB. Do not revert to self-contained bundling without explicit user approval.
- **Version and Changelog Maintenance**: Keep `source.extension.vsixmanifest`, csproj `<Version>`, and `CHANGELOG.md` in sync whenever incrementing plugin versions.

## VSIX Deployment and Online Acceptance Workflow

Use the following workflow whenever a change affects the VSIX Bridge, MCP tool definitions, shared Protocol assembly, or any code loaded by a running Visual Studio instance:

1. The agent reads the current implementation, modifies the code, and runs non-deployment diagnostics and automated tests where possible.
2. Treat compilation, VSIX deployment, Visual Studio startup, and MCP tool discovery as separate states. A successful compile does not prove that the running Visual Studio instance loaded the new extension.
  - `build: vsix` compiles and packages only.
  - `deploy: vsix` runs `Build;DeployVsixExtensionFiles` and is the task used for manual Experimental Instance deployment.
3. Before a manual reload is required, use a user-facing question popup. State exactly which actions are needed:
  - close the relevant Visual Studio or experimental instance;
  - rebuild and deploy the VSIX;
  - restart the intended instance;
  - open the solution required for acceptance;
  - reload the MCP Host/client when tool definitions changed.
4. Do not autonomously close or restart Visual Studio, deploy the VSIX, or launch the experimental instance when the user is expected to perform those steps. Wait for explicit confirmation that deployment and startup are complete.
5. Visual Studio must be closed before replacing deployed extension assemblies. File-lock deployment failures can leave zero-byte or partially extracted manifests in the experimental extension directory.
6. If deployment reports `VSSDK1081`, `FindInstalledExtension`, an invalid manifest, or a locked extension assembly, distinguish source/build output from the installed copy. Inspect the experimental extension directory, identify the damaged deployment, and ask the user to clean and redeploy it rather than repeatedly rebuilding.
7. After the user confirms deployment, perform online acceptance through the registered MCP tools:
  - call `vs_health`;
  - call `vs_capabilities` and confirm the expected capabilities are present with `isStub=false`;
  - verify the intended `vsInstanceId` and Visual Studio process;
  - call the newly implemented tools against the solution opened by the user;
  - verify success, failure, cancellation, concurrency, handle retention, and stable error paths that belong to the current phase.
8. Prefer the registered MCP tools over ad hoc stdio scripts once the client has discovered the new tool definitions.
9. Report automated-test results separately from live acceptance results. Include the actual configuration/platform, task handles, terminal state, and any acceptance gap that still requires user setup.
10. Do not mark a live capability complete solely because unit tests or VSIX packaging passed. Completion requires evidence from the full path: MCP client → Host → Named Pipe → deployed VSIX → Visual Studio service.

## User Workflow Preference

The user prefers planning and design to be preserved in repository Markdown files before major implementation steps. Keep project decisions traceable in existing design documents when the user requests documentation updates.
