using System.Collections.Generic;
using System.Runtime.Serialization;

namespace VsDebugMcp.Protocol;

[DataContract]
public sealed class HandshakeRequest
{
    [DataMember(Name = "hostVersion", Order = 1)]
    public string HostVersion { get; set; } = string.Empty;

    [DataMember(Name = "hostProcessId", Order = 2)]
    public int HostProcessId { get; set; }
}

[DataContract]
public sealed class HandshakeResponse
{
    [DataMember(Name = "protocolVersion", Order = 1)]
    public string ProtocolVersion { get; set; } = BridgeProtocol.Version;

    [DataMember(Name = "bridgeVersion", Order = 2)]
    public string BridgeVersion { get; set; } = string.Empty;

    [DataMember(Name = "visualStudioVersion", Order = 3)]
    public string VisualStudioVersion { get; set; } = string.Empty;

    [DataMember(Name = "visualStudioProcessId", Order = 4)]
    public int VisualStudioProcessId { get; set; }

    [DataMember(Name = "vsInstanceId", Order = 5)]
    public string VsInstanceId { get; set; } = string.Empty;
}

[DataContract]
public sealed class HealthResponse
{
    [DataMember(Name = "status", Order = 1)]
    public string Status { get; set; } = "ok";

    [DataMember(Name = "utcTimestamp", Order = 2)]
    public string UtcTimestamp { get; set; } = string.Empty;
}

[DataContract]
public sealed class CapabilityDescriptor
{
    [DataMember(Name = "name", Order = 1)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Name = "version", Order = 2)]
    public string Version { get; set; } = string.Empty;

    [DataMember(Name = "isStub", Order = 3)]
    public bool IsStub { get; set; }
}

[DataContract]
public sealed class CapabilitiesResponse
{
    [DataMember(Name = "bridgeVersion", Order = 1)]
    public string BridgeVersion { get; set; } = string.Empty;

    [DataMember(Name = "visualStudioVersion", Order = 2)]
    public string VisualStudioVersion { get; set; } = string.Empty;

    [DataMember(Name = "protocolVersion", Order = 3)]
    public string ProtocolVersion { get; set; } = BridgeProtocol.Version;

    [DataMember(Name = "capabilities", Order = 4)]
    public List<CapabilityDescriptor> Capabilities { get; set; } = new();
}

[DataContract]
public sealed class ShutdownResponse
{
    [DataMember(Name = "accepted", Order = 1)]
    public bool Accepted { get; set; }
}

[DataContract]
public sealed class GetProjectsInSolutionResponse
{
    [DataMember(Name = "vsInstanceId", Order = 1)]
    public string VsInstanceId { get; set; } = string.Empty;

    [DataMember(Name = "solution", Order = 2)]
    public SolutionInfo Solution { get; set; } = new();

    [DataMember(Name = "projects", Order = 3)]
    public List<SolutionProjectInfo> Projects { get; set; } = new();

    [DataMember(Name = "warnings", Order = 4)]
    public List<BridgeWarning> Warnings { get; set; } = new();
}

[DataContract]
public sealed class SolutionInfo
{
    [DataMember(Name = "isOpen", Order = 1)]
    public bool IsOpen { get; set; }

    [DataMember(Name = "name", Order = 2)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Name = "filePath", Order = 3)]
    public string FilePath { get; set; } = string.Empty;

    [DataMember(Name = "directory", Order = 4)]
    public string Directory { get; set; } = string.Empty;

    [DataMember(Name = "projectCount", Order = 5)]
    public int ProjectCount { get; set; }
}

[DataContract]
public sealed class SolutionProjectInfo
{
    [DataMember(Name = "id", Order = 1)]
    public string Id { get; set; } = string.Empty;

    [DataMember(Name = "name", Order = 2)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Name = "projectFilePath", Order = 3)]
    public string ProjectFilePath { get; set; } = string.Empty;

    [DataMember(Name = "projectDirectory", Order = 4)]
    public string ProjectDirectory { get; set; } = string.Empty;

    [DataMember(Name = "projectGuid", Order = 5)]
    public string ProjectGuid { get; set; } = string.Empty;

    [DataMember(Name = "typeGuid", Order = 6)]
    public string TypeGuid { get; set; } = string.Empty;

    [DataMember(Name = "kind", Order = 7)]
    public string Kind { get; set; } = "project";

    [DataMember(Name = "isLoaded", Order = 8)]
    public bool IsLoaded { get; set; } = true;

    [DataMember(Name = "isUnsupported", Order = 9)]
    public bool IsUnsupported { get; set; }
}

[DataContract]
public sealed class BridgeWarning
{
    [DataMember(Name = "code", Order = 1)]
    public string Code { get; set; } = string.Empty;

    [DataMember(Name = "message", Order = 2)]
    public string Message { get; set; } = string.Empty;

    [DataMember(Name = "projectId", Order = 3, EmitDefaultValue = false)]
    public string? ProjectId { get; set; }
}

[DataContract]
public sealed class GetFilesInProjectRequest
{
    [DataMember(Name = "projectId", Order = 1, EmitDefaultValue = false)]
    public string? ProjectId { get; set; }

    [DataMember(Name = "extensionFilter", Order = 2, EmitDefaultValue = false)]
    public string? ExtensionFilter { get; set; }
}

[DataContract]
public sealed class ProjectFileInfo
{
    [DataMember(Name = "filePath", Order = 1)]
    public string FilePath { get; set; } = string.Empty;

    [DataMember(Name = "relativePath", Order = 2)]
    public string RelativePath { get; set; } = string.Empty;

    [DataMember(Name = "filterPath", Order = 3, EmitDefaultValue = false)]
    public string? FilterPath { get; set; }

    [DataMember(Name = "extension", Order = 4)]
    public string Extension { get; set; } = string.Empty;
}

[DataContract]
public sealed class ProjectFilesGroup
{
    [DataMember(Name = "projectId", Order = 1)]
    public string ProjectId { get; set; } = string.Empty;

    [DataMember(Name = "projectName", Order = 2)]
    public string ProjectName { get; set; } = string.Empty;

    [DataMember(Name = "projectFilePath", Order = 3)]
    public string ProjectFilePath { get; set; } = string.Empty;

    [DataMember(Name = "files", Order = 4)]
    public List<ProjectFileInfo> Files { get; set; } = new();

    [DataMember(Name = "fileCount", Order = 5)]
    public int FileCount { get; set; }
}

[DataContract]
public sealed class GetFilesInProjectResponse
{
    [DataMember(Name = "vsInstanceId", Order = 1)]
    public string VsInstanceId { get; set; } = string.Empty;

    [DataMember(Name = "projects", Order = 2)]
    public List<ProjectFilesGroup> Projects { get; set; } = new();

    [DataMember(Name = "totalFileCount", Order = 3)]
    public int TotalFileCount { get; set; }

    [DataMember(Name = "warnings", Order = 4)]
    public List<BridgeWarning> Warnings { get; set; } = new();
}

[DataContract]
public sealed class DebuggerGetInfoRequest
{
}

[DataContract]
public sealed class DebuggerGetInfoResponse
{
    [DataMember(Name = "vsInstanceId", Order = 1)]
    public string VsInstanceId { get; set; } = string.Empty;

    [DataMember(Name = "mode", Order = 2)]
    public string Mode { get; set; } = "design";

    [DataMember(Name = "isDebugging", Order = 3)]
    public bool IsDebugging { get; set; }

    [DataMember(Name = "currentProcessId", Order = 4, EmitDefaultValue = false)]
    public int? CurrentProcessId { get; set; }

    [DataMember(Name = "currentProcessName", Order = 5, EmitDefaultValue = false)]
    public string? CurrentProcessName { get; set; }

    [DataMember(Name = "currentThreadId", Order = 6, EmitDefaultValue = false)]
    public int? CurrentThreadId { get; set; }

    [DataMember(Name = "currentThreadName", Order = 7, EmitDefaultValue = false)]
    public string? CurrentThreadName { get; set; }

    [DataMember(Name = "breakpointCount", Order = 8)]
    public int BreakpointCount { get; set; }

    [DataMember(Name = "lastBreakReason", Order = 9, EmitDefaultValue = false)]
    public string? LastBreakReason { get; set; }
}

[DataContract]
public sealed class BreakpointSpec
{
    [DataMember(Name = "line", Order = 1)]
    public int Line { get; set; }

    [DataMember(Name = "column", Order = 2, EmitDefaultValue = false)]
    public int? Column { get; set; }

    [DataMember(Name = "condition", Order = 3, EmitDefaultValue = false)]
    public string? Condition { get; set; }

    [DataMember(Name = "enabled", Order = 4)]
    public bool Enabled { get; set; } = true;
}

[DataContract]
public sealed class BreakpointInfo
{
    [DataMember(Name = "id", Order = 1)]
    public string Id { get; set; } = string.Empty;

    [DataMember(Name = "filePath", Order = 2)]
    public string FilePath { get; set; } = string.Empty;

    [DataMember(Name = "line", Order = 3)]
    public int Line { get; set; }

    [DataMember(Name = "column", Order = 4)]
    public int Column { get; set; } = 1;

    [DataMember(Name = "condition", Order = 5, EmitDefaultValue = false)]
    public string? Condition { get; set; }

    [DataMember(Name = "enabled", Order = 6)]
    public bool Enabled { get; set; } = true;

    [DataMember(Name = "isBound", Order = 7)]
    public bool IsBound { get; set; }
}

[DataContract]
public sealed class DebuggerSetBreakpointsRequest
{
    [DataMember(Name = "filePath", Order = 1)]
    public string FilePath { get; set; } = string.Empty;

    [DataMember(Name = "breakpoints", Order = 2)]
    public List<BreakpointSpec> Breakpoints { get; set; } = new();

    [DataMember(Name = "clearExisting", Order = 3)]
    public bool ClearExisting { get; set; }
}

[DataContract]
public sealed class DebuggerSetBreakpointsResponse
{
    [DataMember(Name = "vsInstanceId", Order = 1)]
    public string VsInstanceId { get; set; } = string.Empty;

    [DataMember(Name = "filePath", Order = 2)]
    public string FilePath { get; set; } = string.Empty;

    [DataMember(Name = "breakpoints", Order = 3)]
    public List<BreakpointInfo> Breakpoints { get; set; } = new();

    [DataMember(Name = "warnings", Order = 4)]
    public List<BridgeWarning> Warnings { get; set; } = new();
}

[DataContract]
public sealed class StackFrameInfo
{
    [DataMember(Name = "frameIndex", Order = 1)]
    public int FrameIndex { get; set; }

    [DataMember(Name = "functionName", Order = 2)]
    public string FunctionName { get; set; } = string.Empty;

    [DataMember(Name = "fileName", Order = 3, EmitDefaultValue = false)]
    public string? FileName { get; set; }

    [DataMember(Name = "lineNumber", Order = 4, EmitDefaultValue = false)]
    public int? LineNumber { get; set; }

    [DataMember(Name = "language", Order = 5, EmitDefaultValue = false)]
    public string? Language { get; set; }

    [DataMember(Name = "module", Order = 6, EmitDefaultValue = false)]
    public string? Module { get; set; }
}

[DataContract]
public sealed class DebuggerGetCallStackRequest
{
    [DataMember(Name = "threadId", Order = 1, EmitDefaultValue = false)]
    public int? ThreadId { get; set; }

    [DataMember(Name = "maxFrames", Order = 2, EmitDefaultValue = false)]
    public int? MaxFrames { get; set; }
}

[DataContract]
public sealed class DebuggerGetCallStackResponse
{
    [DataMember(Name = "vsInstanceId", Order = 1)]
    public string VsInstanceId { get; set; } = string.Empty;

    [DataMember(Name = "threadId", Order = 2)]
    public int ThreadId { get; set; }

    [DataMember(Name = "threadName", Order = 3, EmitDefaultValue = false)]
    public string? ThreadName { get; set; }

    [DataMember(Name = "frames", Order = 4)]
    public List<StackFrameInfo> Frames { get; set; } = new();

    [DataMember(Name = "totalFrames", Order = 5)]
    public int TotalFrames { get; set; }

    [DataMember(Name = "truncated", Order = 6)]
    public bool Truncated { get; set; }
}

[DataContract]
public sealed class DebuggerEvaluateExprRequest
{
    [DataMember(Name = "expression", Order = 1)]
    public string Expression { get; set; } = string.Empty;

    [DataMember(Name = "frameIndex", Order = 2, EmitDefaultValue = false)]
    public int? FrameIndex { get; set; }

    [DataMember(Name = "timeoutMs", Order = 3, EmitDefaultValue = false)]
    public int? TimeoutMs { get; set; }

    [DataMember(Name = "allowSideEffects", Order = 4)]
    public bool AllowSideEffects { get; set; }
}

[DataContract]
public sealed class DebuggerEvaluateExprResponse
{
    [DataMember(Name = "vsInstanceId", Order = 1)]
    public string VsInstanceId { get; set; } = string.Empty;

    [DataMember(Name = "expression", Order = 2)]
    public string Expression { get; set; } = string.Empty;

    [DataMember(Name = "value", Order = 3)]
    public string Value { get; set; } = string.Empty;

    [DataMember(Name = "type", Order = 4)]
    public string Type { get; set; } = string.Empty;

    [DataMember(Name = "isValid", Order = 5)]
    public bool IsValid { get; set; }

    [DataMember(Name = "frameIndex", Order = 6)]
    public int FrameIndex { get; set; }
}

[DataContract]
public sealed class DebuggerStepRequest
{
    [DataMember(Name = "waitForBreak", Order = 1)]
    public bool WaitForBreak { get; set; } = true;
}

[DataContract]
public sealed class DebuggerContinueRequest
{
    [DataMember(Name = "waitForBreak", Order = 1)]
    public bool WaitForBreak { get; set; }
}

[DataContract]
public sealed class DebuggerPauseRequest
{
    [DataMember(Name = "waitForBreak", Order = 1)]
    public bool WaitForBreak { get; set; } = true;
}

[DataContract]
public sealed class DebuggerStopRequest
{
    [DataMember(Name = "waitForStop", Order = 1)]
    public bool WaitForStop { get; set; }
}

[DataContract]
public sealed class DebuggerExecutionResponse
{
    [DataMember(Name = "vsInstanceId", Order = 1)]
    public string VsInstanceId { get; set; } = string.Empty;

    [DataMember(Name = "action", Order = 2)]
    public string Action { get; set; } = string.Empty;

    [DataMember(Name = "previousMode", Order = 3)]
    public string PreviousMode { get; set; } = string.Empty;

    [DataMember(Name = "currentMode", Order = 4)]
    public string CurrentMode { get; set; } = string.Empty;

    [DataMember(Name = "isDebugging", Order = 5)]
    public bool IsDebugging { get; set; }

    [DataMember(Name = "lastBreakReason", Order = 6, EmitDefaultValue = false)]
    public string? LastBreakReason { get; set; }

    [DataMember(Name = "currentProcessId", Order = 7, EmitDefaultValue = false)]
    public int? CurrentProcessId { get; set; }

    [DataMember(Name = "currentThreadId", Order = 8, EmitDefaultValue = false)]
    public int? CurrentThreadId { get; set; }

    [DataMember(Name = "topFrame", Order = 9, EmitDefaultValue = false)]
    public StackFrameInfo? TopFrame { get; set; }

    [DataMember(Name = "warnings", Order = 10)]
    public List<BridgeWarning> Warnings { get; set; } = new();
}

[DataContract]
public sealed class RunBuildRequest
{
    [DataMember(Name = "configuration", Order = 1, EmitDefaultValue = false)]
    public string? Configuration { get; set; }

    [DataMember(Name = "platform", Order = 2, EmitDefaultValue = false)]
    public string? Platform { get; set; }
}

[DataContract]
public sealed class GetBuildStatusRequest
{
    [DataMember(Name = "buildTaskId", Order = 1)]
    public string BuildTaskId { get; set; } = string.Empty;
}

[DataContract]
public sealed class CancelBuildRequest
{
    [DataMember(Name = "buildTaskId", Order = 1)]
    public string BuildTaskId { get; set; } = string.Empty;
}

[DataContract]
public sealed class BuildTaskResponse
{
    [DataMember(Name = "buildTaskId", Order = 1)]
    public string BuildTaskId { get; set; } = string.Empty;

    [DataMember(Name = "vsInstanceId", Order = 2)]
    public string VsInstanceId { get; set; } = string.Empty;

    [DataMember(Name = "state", Order = 3)]
    public string State { get; set; } = BuildStates.Starting;

    [DataMember(Name = "configuration", Order = 4)]
    public string Configuration { get; set; } = string.Empty;

    [DataMember(Name = "platform", Order = 5)]
    public string Platform { get; set; } = string.Empty;

    [DataMember(Name = "requestedAtUtc", Order = 6)]
    public string RequestedAtUtc { get; set; } = string.Empty;

    [DataMember(Name = "startedAtUtc", Order = 7, EmitDefaultValue = false)]
    public string? StartedAtUtc { get; set; }

    [DataMember(Name = "completedAtUtc", Order = 8, EmitDefaultValue = false)]
    public string? CompletedAtUtc { get; set; }

    [DataMember(Name = "succeeded", Order = 9, EmitDefaultValue = false)]
    public bool? Succeeded { get; set; }

    [DataMember(Name = "cancelRequested", Order = 10)]
    public bool CancelRequested { get; set; }
}

[DataContract]
public sealed class CancelBuildResponse
{
    [DataMember(Name = "accepted", Order = 1)]
    public bool Accepted { get; set; }

    [DataMember(Name = "build", Order = 2)]
    public BuildTaskResponse Build { get; set; } = new();
}

[DataContract]
public sealed class GetErrorsRequest
{
    [DataMember(Name = "buildTaskId", Order = 1, EmitDefaultValue = false)]
    public string? BuildTaskId { get; set; }

    [DataMember(Name = "severities", Order = 2, EmitDefaultValue = false)]
    public List<string>? Severities { get; set; }

    [DataMember(Name = "project", Order = 3, EmitDefaultValue = false)]
    public string? Project { get; set; }

    [DataMember(Name = "file", Order = 4, EmitDefaultValue = false)]
    public string? File { get; set; }

    [DataMember(Name = "maxCount", Order = 5, EmitDefaultValue = false)]
    public int? MaxCount { get; set; }
}

[DataContract]
public sealed class GetErrorsResponse
{
    [DataMember(Name = "vsInstanceId", Order = 1)]
    public string VsInstanceId { get; set; } = string.Empty;

    [DataMember(Name = "buildTaskId", Order = 2, EmitDefaultValue = false)]
    public string? BuildTaskId { get; set; }

    [DataMember(Name = "snapshotAtUtc", Order = 3)]
    public string SnapshotAtUtc { get; set; } = string.Empty;

    [DataMember(Name = "totalCount", Order = 4)]
    public int TotalCount { get; set; }

    [DataMember(Name = "returnedCount", Order = 5)]
    public int ReturnedCount { get; set; }

    [DataMember(Name = "truncated", Order = 6)]
    public bool Truncated { get; set; }

    [DataMember(Name = "items", Order = 7)]
    public List<VisualStudioDiagnostic> Items { get; set; } = new();
}

[DataContract]
public sealed class VisualStudioDiagnostic
{
    [DataMember(Name = "severity", Order = 1)]
    public string Severity { get; set; } = string.Empty;

    [DataMember(Name = "code", Order = 2)]
    public string Code { get; set; } = string.Empty;

    [DataMember(Name = "message", Order = 3)]
    public string Message { get; set; } = string.Empty;

    [DataMember(Name = "project", Order = 4)]
    public string Project { get; set; } = string.Empty;

    [DataMember(Name = "filePath", Order = 5)]
    public string FilePath { get; set; } = string.Empty;

    [DataMember(Name = "line", Order = 6, EmitDefaultValue = false)]
    public int? Line { get; set; }

    [DataMember(Name = "column", Order = 7, EmitDefaultValue = false)]
    public int? Column { get; set; }

    [DataMember(Name = "buildTool", Order = 8)]
    public string BuildTool { get; set; } = string.Empty;
}

[DataContract]
public sealed class GetOutputWindowLogsRequest
{
    [DataMember(Name = "source", Order = 1, EmitDefaultValue = false)]
    public string? Source { get; set; }

    [DataMember(Name = "maxChars", Order = 2, EmitDefaultValue = false)]
    public int? MaxChars { get; set; }
}

[DataContract]
public sealed class GetOutputWindowLogsResponse
{
    [DataMember(Name = "vsInstanceId", Order = 1)]
    public string VsInstanceId { get; set; } = string.Empty;

    [DataMember(Name = "source", Order = 2)]
    public string Source { get; set; } = string.Empty;

    [DataMember(Name = "capturedAtUtc", Order = 3)]
    public string CapturedAtUtc { get; set; } = string.Empty;

    [DataMember(Name = "totalChars", Order = 4)]
    public int TotalChars { get; set; }

    [DataMember(Name = "returnedChars", Order = 5)]
    public int ReturnedChars { get; set; }

    [DataMember(Name = "truncated", Order = 6)]
    public bool Truncated { get; set; }

    [DataMember(Name = "text", Order = 7)]
    public string Text { get; set; } = string.Empty;
}

[DataContract]
public sealed class VisualStudioInstanceDescriptor
{
    [DataMember(Name = "vsInstanceId", Order = 1)]
    public string VsInstanceId { get; set; } = string.Empty;

    [DataMember(Name = "visualStudioProcessId", Order = 2)]
    public int VisualStudioProcessId { get; set; }

    [DataMember(Name = "processStartTimeUtcTicks", Order = 3)]
    public long ProcessStartTimeUtcTicks { get; set; }

    [DataMember(Name = "visualStudioVersion", Order = 4)]
    public string VisualStudioVersion { get; set; } = string.Empty;

    [DataMember(Name = "solutionName", Order = 5)]
    public string SolutionName { get; set; } = string.Empty;

    [DataMember(Name = "solutionFilePath", Order = 6)]
    public string SolutionFilePath { get; set; } = string.Empty;

    [DataMember(Name = "bridgePipeName", Order = 7)]
    public string BridgePipeName { get; set; } = string.Empty;

    [DataMember(Name = "registeredAtUtc", Order = 8)]
    public string RegisteredAtUtc { get; set; } = string.Empty;

    [DataMember(Name = "lastHeartbeatUtc", Order = 9)]
    public string LastHeartbeatUtc { get; set; } = string.Empty;
}

[DataContract]
public sealed class HostStatusResponse
{
    [DataMember(Name = "hostVersion", Order = 1)]
    public string HostVersion { get; set; } = string.Empty;

    [DataMember(Name = "protocolVersion", Order = 2)]
    public string ProtocolVersion { get; set; } = BridgeProtocol.Version;

    [DataMember(Name = "instances", Order = 3)]
    public List<VisualStudioInstanceDescriptor> Instances { get; set; } = new();
}

[DataContract]
public sealed class RegisterInstanceRequest
{
    [DataMember(Name = "instance", Order = 1)]
    public VisualStudioInstanceDescriptor Instance { get; set; } = new();
}

[DataContract]
public sealed class RegisterInstanceResponse
{
    [DataMember(Name = "accepted", Order = 1)]
    public bool Accepted { get; set; }

    [DataMember(Name = "heartbeatIntervalSeconds", Order = 2)]
    public int HeartbeatIntervalSeconds { get; set; }
}

[DataContract]
public sealed class HeartbeatInstanceRequest
{
    [DataMember(Name = "instance", Order = 1)]
    public VisualStudioInstanceDescriptor Instance { get; set; } = new();
}

[DataContract]
public sealed class HeartbeatInstanceResponse
{
    [DataMember(Name = "accepted", Order = 1)]
    public bool Accepted { get; set; }
}

[DataContract]
public sealed class UnregisterInstanceRequest
{
    [DataMember(Name = "vsInstanceId", Order = 1)]
    public string VsInstanceId { get; set; } = string.Empty;
}

[DataContract]
public sealed class UnregisterInstanceResponse
{
    [DataMember(Name = "removed", Order = 1)]
    public bool Removed { get; set; }
}