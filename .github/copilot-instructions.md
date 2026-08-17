# VsDebugMcp workspace instructions

- Follow the repository-wide requirements in `AGENTS.md`.
- Preserve the Hybrid architecture: OOP Host, local IPC and VSIX/VSSDK Bridge.
- Phase 0A is limited to `handshake`, `health`, `capabilities` and `shutdown` over Named Pipe.
- Do not introduce MCP stdio, real IDE providers, debugger control, file editing or remote access before Phase 0A is accepted.
- Build managed projects with the .NET SDK.
- Build `src/VsDebugMcp.Vsix/VsDebugMcp.Vsix.csproj` with the Visual Studio 18 MSBuild installation.
- Keep the shared protocol compatible with both `net8.0` and `netstandard2.0`.
- Do not log request payloads, credentials, environment variables or raw Visual Studio Copilot logs.
- Run `test: Phase 0A` and `build: all` after meaningful IPC changes.
- Keep changes focused and avoid unrelated refactoring.
