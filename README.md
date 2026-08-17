# VsDebugMcp

Visual Studio 2026 / VS 18.x MCP bridge prototype. Phase 0B exposes the validated local VSIX Bridge through a standard MCP stdio Host.

## Current scope

- `handshake`
- `health`
- `capabilities`
- `shutdown`
- Length-prefixed JSON framing
- Current-user Named Pipe ACL
- Structured bridge errors
- Single Visual Studio instance
- MCP stdio transport
- `vs_health`
- `vs_capabilities`
- `--smoke` diagnostics mode

Project APIs, build control, test control, debugger control and file editing are not implemented yet.

## Projects

- `src/VsDebugMcp.Protocol` — shared IPC contracts, framing and error model; targets `net8.0` and `netstandard2.0`.
- `src/VsDebugMcp.Host` — .NET 8 MCP stdio Host, smoke runner and Named Pipe client.
- `src/VsDebugMcp.Vsix` — SDK-style VSSDK Bridge and Named Pipe server for VS 18.5+.
- `tests/VsDebugMcp.Protocol.Tests` — protocol and framing tests.
- `tests/VsDebugMcp.Host.Tests` — Host client, MCP tools and stdio integration tests.

## Build and test

Use the VS Code tasks:

- `build: all`
- `test: Phase 0B`
- `build: vsix`
- `run: host smoke`
- `run: host mcp`

The VSIX project must be built with the Visual Studio 18 MSBuild installation because `Microsoft.VisualStudio.SDK.Build` is installed under Visual Studio rather than the standalone .NET SDK.

The generated package is located under `src/VsDebugMcp.Vsix/bin/Debug/vs2026_5/`.

## Experimental instance validation

1. Open `VsDebugMcp.slnx` in Visual Studio 2026.
2. Set `VsDebugMcp.Vsix` as the startup project.
3. Start debugging to launch the Visual Studio experimental instance.
4. Wait for the experimental instance to finish loading the extension.
5. Run the `run: host smoke` task from this workspace.
6. Confirm that the Host reports the Visual Studio version, Bridge health and the `phase0.ipc` stub capability.
7. Close the experimental instance and confirm that a later Host invocation reports `bridge_unavailable` instead of hanging.

## VS Code MCP validation

The workspace MCP configuration is stored in `.vscode/mcp.json` under the server name `vs-debug-mcp`.

1. Build the managed projects.
2. Start the Visual Studio experimental instance with the Bridge enabled.
3. Start `vs-debug-mcp` from the VS Code MCP server view.
4. Confirm that `vs_health` and `vs_capabilities` are listed.
5. Call both tools and verify the connected Visual Studio metadata.

The project targets `vs2026_5`. The VSSDK project currently pins the public `Microsoft.VisualStudio.Sdk` package to `17.14.40265` because the VS 18 template's default preview package is not available from the configured public NuGet sources.
