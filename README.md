# VsDebugMcp

Visual Studio 2026 / VS 18.x MCP bridge prototype. Phase 0A validates local Named Pipe communication between an out-of-process Console Host and an SDK-style VSSDK extension.

## Current scope

- `handshake`
- `health`
- `capabilities`
- `shutdown`
- Length-prefixed JSON framing
- Current-user Named Pipe ACL
- Structured bridge errors
- Single Visual Studio instance

MCP stdio, project APIs, build, tests, debugger control and file editing are not implemented yet.

## Projects

- `src/VsDebugMcp.Protocol` — shared IPC contracts, framing and error model; targets `net8.0` and `netstandard2.0`.
- `src/VsDebugMcp.Host` — .NET 8 Console Host and Named Pipe client.
- `src/VsDebugMcp.Vsix` — SDK-style VSSDK Bridge and Named Pipe server for VS 18.5+.
- `tests/VsDebugMcp.Protocol.Tests` — protocol and framing tests.
- `tests/VsDebugMcp.Host.Tests` — Host client and Named Pipe tests.

## Build and test

Use the VS Code tasks:

- `build: all`
- `test: Phase 0A`
- `build: vsix`
- `run: host`

The VSIX project must be built with the Visual Studio 18 MSBuild installation because `Microsoft.VisualStudio.SDK.Build` is installed under Visual Studio rather than the standalone .NET SDK.

The generated package is located under `src/VsDebugMcp.Vsix/bin/Debug/vs2026_5/`.

## Experimental instance validation

1. Open `VsDebugMcp.slnx` in Visual Studio 2026.
2. Set `VsDebugMcp.Vsix` as the startup project.
3. Start debugging to launch the Visual Studio experimental instance.
4. Wait for the experimental instance to finish loading the extension.
5. Run the `run: host` task from this workspace.
6. Confirm that the Host reports the Visual Studio version, Bridge health and the `phase0.ipc` stub capability.
7. Close the experimental instance and confirm that a later Host invocation reports `bridge_unavailable` instead of hanging.

The project targets `vs2026_5`. The VSSDK project currently pins the public `Microsoft.VisualStudio.Sdk` package to `17.14.40265` because the VS 18 template's default preview package is not available from the configured public NuGet sources.
