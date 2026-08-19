# VsDebugMcp Bridge

VsDebugMcp Bridge connects MCP-compatible clients to local Visual Studio 2026 / Visual Studio 18.x instances through a shared out-of-process Host.

## Features

- Report Visual Studio bridge health and instance metadata
- Discover available bridge capabilities
- List projects in the currently open solution
- Start, monitor, and cancel Visual Studio builds
- Read Visual Studio Error Table diagnostics
- Read build output from the Visual Studio Output window
- Communicate locally through an authenticated Named Pipe
- Serve MCP clients at the fixed local URL `http://127.0.0.1:43259`
- Automatically start the packaged self-contained Host when the VSIX loads
- Route tools to multiple Visual Studio instances through `vsInstanceId`

## Architecture

MCP Client → Loopback HTTP Host → Instance Router → Per-instance Named Pipe → Visual Studio Bridge

The VSIX provides the Visual Studio-side bridge and packages the Host. The MCP client uses a fixed HTTP URL and does not launch or locate the Host executable.

## Installation

1. Install the VsDebugMcp Bridge VSIX.
2. Restart Visual Studio.
3. Configure the MCP client with `http://127.0.0.1:43259` and `type: http`.
4. Open a Visual Studio solution and verify `vs_health`, `vs_list_instances` and `vs_capabilities`.

## Requirements

- Visual Studio 2026 / Visual Studio 18.x
- An MCP-compatible client
- IPv4 loopback and local Named Pipe communication enabled

## Security and Privacy

VsDebugMcp Bridge is intended for local-only use. MCP HTTP listens only on `127.0.0.1:43259`; Host control and Visual Studio Bridge communication use Named Pipes protected by current-user access control. The extension does not provide remote Visual Studio access.

Each VSIX sends a heartbeat every 5 seconds. The Host removes an instance after 15 seconds without a heartbeat and exits immediately when no registered Visual Studio instances remain.

## Current Limitations

This release focuses on solution context, build operations, diagnostics, and build output. Debugger control, file editing, remote access, and general process control are not included.

This extension is an early access release and its capabilities may change in future versions.