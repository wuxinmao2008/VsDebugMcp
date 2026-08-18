# VsDebugMcp Bridge

VsDebugMcp Bridge connects MCP-compatible clients to a local Visual Studio instance through a secure local bridge.

It is designed for Visual Studio 2026 / Visual Studio 18.x and works together with the standalone VsDebugMcp .NET MCP Host.

## Features

- Report Visual Studio bridge health and instance metadata
- Discover available bridge capabilities
- List projects in the currently open solution
- Start, monitor, and cancel Visual Studio builds
- Read Visual Studio Error Table diagnostics
- Read build output from the Visual Studio Output window
- Communicate locally through an authenticated Named Pipe
- Support MCP clients through a standard MCP stdio Host

## Architecture

MCP Client → VsDebugMcp MCP Host → Local Named Pipe → Visual Studio Bridge

The VSIX provides the Visual Studio-side bridge. The MCP Host is a separate .NET application and must be configured independently in your MCP client.

## Installation

1. Install the VsDebugMcp Bridge VSIX.
2. Restart Visual Studio.
3. Start the VsDebugMcp .NET MCP Host.
4. Configure your MCP client to launch the Host.
5. Open a Visual Studio solution and verify the `vs_health` and `vs_capabilities` tools.

## Requirements

- Visual Studio 2026 / Visual Studio 18.x
- The VsDebugMcp .NET MCP Host
- An MCP-compatible client
- Local Named Pipe communication enabled

## Security and Privacy

VsDebugMcp Bridge is intended for local-only use. Communication between the MCP Host and Visual Studio uses a local Named Pipe protected by current-user access control. The extension does not provide remote Visual Studio access.

## Current Limitations

This release focuses on solution context, build operations, diagnostics, and build output. Debugger control, file editing, remote access, and general process control are not included.

This extension is an early access release and its capabilities may change in future versions.