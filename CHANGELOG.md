# Changelog

All notable changes to the "VsDebugMcp" extension will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
