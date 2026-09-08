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

    [DataMember(Name = "conditionType", Order = 5, EmitDefaultValue = false)]
    public string? ConditionType { get; set; }

    [DataMember(Name = "hitCountTarget", Order = 6, EmitDefaultValue = false)]
    public int? HitCountTarget { get; set; }

    [DataMember(Name = "hitCountType", Order = 7, EmitDefaultValue = false)]
    public string? HitCountType { get; set; }
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

    [DataMember(Name = "conditionType", Order = 8, EmitDefaultValue = false)]
    public string? ConditionType { get; set; }

    [DataMember(Name = "hitCountTarget", Order = 9, EmitDefaultValue = false)]
    public int? HitCountTarget { get; set; }

    [DataMember(Name = "hitCountType", Order = 10, EmitDefaultValue = false)]
    public string? HitCountType { get; set; }

    [DataMember(Name = "currentHitCount", Order = 11, EmitDefaultValue = false)]
    public int? CurrentHitCount { get; set; }
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

    [DataMember(Name = "columnNumber", Order = 7, EmitDefaultValue = false)]
    public int? ColumnNumber { get; set; }

    [DataMember(Name = "userCode", Order = 8, EmitDefaultValue = false)]
    public bool? UserCode { get; set; }
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
public sealed class DebuggerStartRequest
{
    [DataMember(Name = "waitForBreak", Order = 1)]
    public bool WaitForBreak { get; set; } = true;

    [DataMember(Name = "timeoutMs", Order = 2, EmitDefaultValue = false)]
    public int? TimeoutMs { get; set; }
}

[DataContract]
public sealed class DebuggerEvaluateExpressionsRequest
{
    [DataMember(Name = "expressions", Order = 1)]
    public List<string> Expressions { get; set; } = new();

    [DataMember(Name = "frameIndex", Order = 2, EmitDefaultValue = false)]
    public int? FrameIndex { get; set; }

    [DataMember(Name = "timeoutMs", Order = 3, EmitDefaultValue = false)]
    public int? TimeoutMs { get; set; }

    [DataMember(Name = "allowSideEffects", Order = 4)]
    public bool AllowSideEffects { get; set; }
}

[DataContract]
public sealed class DebuggerExpressionItemResult
{
    [DataMember(Name = "expression", Order = 1)]
    public string Expression { get; set; } = string.Empty;

    [DataMember(Name = "value", Order = 2)]
    public string Value { get; set; } = string.Empty;

    [DataMember(Name = "type", Order = 3)]
    public string Type { get; set; } = string.Empty;

    [DataMember(Name = "isValid", Order = 4)]
    public bool IsValid { get; set; }

    [DataMember(Name = "error", Order = 5, EmitDefaultValue = false)]
    public string? Error { get; set; }
}

[DataContract]
public sealed class DebuggerEvaluateExpressionsResponse
{
    [DataMember(Name = "vsInstanceId", Order = 1)]
    public string VsInstanceId { get; set; } = string.Empty;

    [DataMember(Name = "frameIndex", Order = 2)]
    public int FrameIndex { get; set; }

    [DataMember(Name = "results", Order = 3)]
    public List<DebuggerExpressionItemResult> Results { get; set; } = new();

    [DataMember(Name = "warnings", Order = 4)]
    public List<BridgeWarning> Warnings { get; set; } = new();
}

[DataContract]
public sealed class DebuggerGetLocalsRequest
{
    [DataMember(Name = "frameIndex", Order = 1, EmitDefaultValue = false)]
    public int? FrameIndex { get; set; }

    [DataMember(Name = "maxCount", Order = 2, EmitDefaultValue = false)]
    public int? MaxCount { get; set; }
}

[DataContract]
public sealed class DebuggerVariableInfo
{
    [DataMember(Name = "name", Order = 1)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Name = "value", Order = 2)]
    public string Value { get; set; } = string.Empty;

    [DataMember(Name = "type", Order = 3)]
    public string Type { get; set; } = string.Empty;

    [DataMember(Name = "isArgument", Order = 4)]
    public bool IsArgument { get; set; }
}

[DataContract]
public sealed class DebuggerGetLocalsResponse
{
    [DataMember(Name = "vsInstanceId", Order = 1)]
    public string VsInstanceId { get; set; } = string.Empty;

    [DataMember(Name = "frameIndex", Order = 2)]
    public int FrameIndex { get; set; }

    [DataMember(Name = "variables", Order = 3)]
    public List<DebuggerVariableInfo> Variables { get; set; } = new();

    [DataMember(Name = "totalCount", Order = 4)]
    public int TotalCount { get; set; }

    [DataMember(Name = "truncated", Order = 5)]
    public bool Truncated { get; set; }

    [DataMember(Name = "warnings", Order = 6)]
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

[DataContract]
public sealed class GetTestsRequest
{
    [DataMember(Name = "vsInstanceId", Order = 1, EmitDefaultValue = false)]
    public string? VsInstanceId { get; set; }

    [DataMember(Name = "projectName", Order = 2, EmitDefaultValue = false)]
    public string? ProjectName { get; set; }

    [DataMember(Name = "filter", Order = 3, EmitDefaultValue = false)]
    public string? Filter { get; set; }
}

[DataContract]
public sealed class VsTestItem
{
    [DataMember(Name = "testId", Order = 1)]
    public string TestId { get; set; } = string.Empty;

    [DataMember(Name = "displayName", Order = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [DataMember(Name = "fullyQualifiedName", Order = 3)]
    public string FullyQualifiedName { get; set; } = string.Empty;

    [DataMember(Name = "filePath", Order = 4, EmitDefaultValue = false)]
    public string? FilePath { get; set; }

    [DataMember(Name = "lineNumber", Order = 5, EmitDefaultValue = false)]
    public int? LineNumber { get; set; }

    [DataMember(Name = "projectId", Order = 6, EmitDefaultValue = false)]
    public string? ProjectId { get; set; }

    [DataMember(Name = "source", Order = 7, EmitDefaultValue = false)]
    public string? Source { get; set; }

    [DataMember(Name = "state", Order = 8)]
    public string State { get; set; } = "NotRun";

    [DataMember(Name = "durationMs", Order = 9, EmitDefaultValue = false)]
    public double? DurationMs { get; set; }

    [DataMember(Name = "lastErrorMessage", Order = 10, EmitDefaultValue = false)]
    public string? LastErrorMessage { get; set; }
}

[DataContract]
public sealed class GetTestsResponse
{
    [DataMember(Name = "vsInstanceId", Order = 1)]
    public string VsInstanceId { get; set; } = string.Empty;

    [DataMember(Name = "tests", Order = 2)]
    public List<VsTestItem> Tests { get; set; } = new();

    [DataMember(Name = "totalCount", Order = 3)]
    public int TotalCount { get; set; }
}

[DataContract]
public sealed class RunTestsRequest
{
    [DataMember(Name = "vsInstanceId", Order = 1, EmitDefaultValue = false)]
    public string? VsInstanceId { get; set; }

    [DataMember(Name = "testIds", Order = 2, EmitDefaultValue = false)]
    public List<string>? TestIds { get; set; }
}

[DataContract]
public sealed class RunTestsResponse
{
    [DataMember(Name = "vsInstanceId", Order = 1)]
    public string VsInstanceId { get; set; } = string.Empty;

    [DataMember(Name = "testRunId", Order = 2)]
    public string TestRunId { get; set; } = string.Empty;

    [DataMember(Name = "state", Order = 3)]
    public string State { get; set; } = TestRunStates.Starting;

    [DataMember(Name = "totalCount", Order = 4)]
    public int TotalCount { get; set; }

    [DataMember(Name = "startedAt", Order = 5)]
    public string StartedAt { get; set; } = string.Empty;
}

[DataContract]
public sealed class GetTestRunStatusRequest
{
    [DataMember(Name = "vsInstanceId", Order = 1, EmitDefaultValue = false)]
    public string? VsInstanceId { get; set; }

    [DataMember(Name = "testRunId", Order = 2, EmitDefaultValue = false)]
    public string? TestRunId { get; set; }
}

[DataContract]
public sealed class VsTestResult
{
    [DataMember(Name = "testId", Order = 1)]
    public string TestId { get; set; } = string.Empty;

    [DataMember(Name = "displayName", Order = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [DataMember(Name = "outcome", Order = 3)]
    public string Outcome { get; set; } = "None";

    [DataMember(Name = "durationMs", Order = 4)]
    public double DurationMs { get; set; }

    [DataMember(Name = "errorMessage", Order = 5, EmitDefaultValue = false)]
    public string? ErrorMessage { get; set; }

    [DataMember(Name = "stackTrace", Order = 6, EmitDefaultValue = false)]
    public string? StackTrace { get; set; }

    [DataMember(Name = "standardOutput", Order = 7, EmitDefaultValue = false)]
    public string? StandardOutput { get; set; }

    [DataMember(Name = "standardError", Order = 8, EmitDefaultValue = false)]
    public string? StandardError { get; set; }
}

[DataContract]
public sealed class TestRunStatusResponse
{
    [DataMember(Name = "vsInstanceId", Order = 1)]
    public string VsInstanceId { get; set; } = string.Empty;

    [DataMember(Name = "testRunId", Order = 2)]
    public string TestRunId { get; set; } = string.Empty;

    [DataMember(Name = "state", Order = 3)]
    public string State { get; set; } = TestRunStates.Running;

    [DataMember(Name = "totalCount", Order = 4)]
    public int TotalCount { get; set; }

    [DataMember(Name = "passedCount", Order = 5)]
    public int PassedCount { get; set; }

    [DataMember(Name = "failedCount", Order = 6)]
    public int FailedCount { get; set; }

    [DataMember(Name = "skippedCount", Order = 7)]
    public int SkippedCount { get; set; }

    [DataMember(Name = "durationMs", Order = 8)]
    public double DurationMs { get; set; }

    [DataMember(Name = "results", Order = 9)]
    public List<VsTestResult> Results { get; set; } = new();
}

[DataContract]
public sealed class CancelTestRunRequest
{
    [DataMember(Name = "vsInstanceId", Order = 1, EmitDefaultValue = false)]
    public string? VsInstanceId { get; set; }

    [DataMember(Name = "testRunId", Order = 2, EmitDefaultValue = false)]
    public string? TestRunId { get; set; }
}

[DataContract]
public sealed class CancelTestRunResponse
{
    [DataMember(Name = "vsInstanceId", Order = 1)]
    public string VsInstanceId { get; set; } = string.Empty;

    [DataMember(Name = "testRunId", Order = 2)]
    public string TestRunId { get; set; } = string.Empty;

    [DataMember(Name = "state", Order = 3)]
    public string State { get; set; } = TestRunStates.Cancelled;

    [DataMember(Name = "cancelRequested", Order = 4)]
    public bool CancelRequested { get; set; }
}

[DataContract]
public sealed class DebugTestRequest
{
    [DataMember(Name = "vsInstanceId", Order = 1, EmitDefaultValue = false)]
    public string? VsInstanceId { get; set; }

    [DataMember(Name = "testId", Order = 2)]
    public string TestId { get; set; } = string.Empty;

    [DataMember(Name = "waitForBreak", Order = 3, EmitDefaultValue = false)]
    public bool? WaitForBreak { get; set; }

    [DataMember(Name = "timeoutMs", Order = 4, EmitDefaultValue = false)]
    public int? TimeoutMs { get; set; }
}

[DataContract]
public sealed class DebugTestResponse
{
    [DataMember(Name = "vsInstanceId", Order = 1)]
    public string VsInstanceId { get; set; } = string.Empty;

    [DataMember(Name = "testRunId", Order = 2)]
    public string TestRunId { get; set; } = string.Empty;

    [DataMember(Name = "testId", Order = 3)]
    public string TestId { get; set; } = string.Empty;

    [DataMember(Name = "testDisplayName", Order = 4)]
    public string TestDisplayName { get; set; } = string.Empty;

    [DataMember(Name = "isDebugging", Order = 5)]
    public bool IsDebugging { get; set; }

    [DataMember(Name = "debuggerMode", Order = 6)]
    public string DebuggerMode { get; set; } = "design";

    [DataMember(Name = "lastBreakReason", Order = 7, EmitDefaultValue = false)]
    public string? LastBreakReason { get; set; }

    [DataMember(Name = "topFrame", Order = 8, EmitDefaultValue = false)]
    public StackFrameInfo? TopFrame { get; set; }

    [DataMember(Name = "currentProcessId", Order = 9, EmitDefaultValue = false)]
    public int? CurrentProcessId { get; set; }

    [DataMember(Name = "currentThreadId", Order = 10, EmitDefaultValue = false)]
    public int? CurrentThreadId { get; set; }

    [DataMember(Name = "startedAt", Order = 11)]
    public string StartedAt { get; set; } = string.Empty;

    [DataMember(Name = "warnings", Order = 12, EmitDefaultValue = false)]
    public List<BridgeWarning> Warnings { get; set; } = new();
}

[DataContract]
public sealed class DebuggerGetThreadsRequest
{
    [DataMember(Name = "vsInstanceId", Order = 1, EmitDefaultValue = false)]
    public string? VsInstanceId { get; set; }
}

[DataContract]
public sealed class ThreadInfo
{
    [DataMember(Name = "id", Order = 1)]
    public int Id { get; set; }

    [DataMember(Name = "name", Order = 2)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Name = "isAlive", Order = 3)]
    public bool IsAlive { get; set; }

    [DataMember(Name = "isCurrent", Order = 4)]
    public bool IsCurrent { get; set; }

    [DataMember(Name = "suspendedCount", Order = 5)]
    public int SuspendedCount { get; set; }

    [DataMember(Name = "priority", Order = 6, EmitDefaultValue = false)]
    public string? Priority { get; set; }

    [DataMember(Name = "topFrame", Order = 7, EmitDefaultValue = false)]
    public StackFrameInfo? TopFrame { get; set; }

    [DataMember(Name = "isFrozen", Order = 8)]
    public bool IsFrozen { get; set; }
}

[DataContract]
public sealed class DebuggerGetThreadsResponse
{
    [DataMember(Name = "vsInstanceId", Order = 1)]
    public string VsInstanceId { get; set; } = string.Empty;

    [DataMember(Name = "currentThreadId", Order = 2, EmitDefaultValue = false)]
    public int? CurrentThreadId { get; set; }

    [DataMember(Name = "totalCount", Order = 3)]
    public int TotalCount { get; set; }

    [DataMember(Name = "threads", Order = 4)]
    public List<ThreadInfo> Threads { get; set; } = new();
}

[DataContract]
public sealed class DebuggerGetExceptionInfoRequest
{
    [DataMember(Name = "vsInstanceId", Order = 1, EmitDefaultValue = false)]
    public string? VsInstanceId { get; set; }
}

[DataContract]
public sealed class DebuggerGetExceptionInfoResponse
{
    [DataMember(Name = "vsInstanceId", Order = 1)]
    public string VsInstanceId { get; set; } = string.Empty;

    [DataMember(Name = "hasException", Order = 2)]
    public bool HasException { get; set; }

    [DataMember(Name = "exceptionType", Order = 3, EmitDefaultValue = false)]
    public string? ExceptionType { get; set; }

    [DataMember(Name = "message", Order = 4, EmitDefaultValue = false)]
    public string? Message { get; set; }

    [DataMember(Name = "hresult", Order = 5, EmitDefaultValue = false)]
    public string? HResult { get; set; }

    [DataMember(Name = "source", Order = 6, EmitDefaultValue = false)]
    public string? Source { get; set; }

    [DataMember(Name = "stackTrace", Order = 7, EmitDefaultValue = false)]
    public string? StackTrace { get; set; }

    [DataMember(Name = "innerException", Order = 8, EmitDefaultValue = false)]
    public string? InnerException { get; set; }

    [DataMember(Name = "rawDetails", Order = 9, EmitDefaultValue = false)]
    public string? RawDetails { get; set; }
}

[DataContract]
public sealed class ProcessInfo
{
    [DataMember(Name = "processId", Order = 1)]
    public int ProcessId { get; set; }

    [DataMember(Name = "name", Order = 2)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Name = "userName", Order = 3, EmitDefaultValue = false)]
    public string? UserName { get; set; }

    [DataMember(Name = "isBeingDebugged", Order = 4)]
    public bool IsBeingDebugged { get; set; }

    [DataMember(Name = "transportQualifier", Order = 5, EmitDefaultValue = false)]
    public string? TransportQualifier { get; set; }
}

[DataContract]
public sealed class DebuggerGetProcessesRequest
{
    [DataMember(Name = "vsInstanceId", Order = 1, EmitDefaultValue = false)]
    public string? VsInstanceId { get; set; }

    [DataMember(Name = "processName", Order = 2, EmitDefaultValue = false)]
    public string? ProcessName { get; set; }

    [DataMember(Name = "processId", Order = 3, EmitDefaultValue = false)]
    public int? ProcessId { get; set; }

    [DataMember(Name = "onlyDebugged", Order = 4, EmitDefaultValue = false)]
    public bool OnlyDebugged { get; set; }

    [DataMember(Name = "maxCount", Order = 5, EmitDefaultValue = false)]
    public int? MaxCount { get; set; }
}

[DataContract]
public sealed class DebuggerGetProcessesResponse
{
    [DataMember(Name = "vsInstanceId", Order = 1)]
    public string VsInstanceId { get; set; } = string.Empty;

    [DataMember(Name = "totalCount", Order = 2)]
    public int TotalCount { get; set; }

    [DataMember(Name = "returnedCount", Order = 3)]
    public int ReturnedCount { get; set; }

    [DataMember(Name = "processes", Order = 4)]
    public List<ProcessInfo> Processes { get; set; } = new();
}

[DataContract]
public sealed class DebuggerAttachRequest
{
    [DataMember(Name = "vsInstanceId", Order = 1, EmitDefaultValue = false)]
    public string? VsInstanceId { get; set; }

    [DataMember(Name = "processId", Order = 2, EmitDefaultValue = false)]
    public int? ProcessId { get; set; }

    [DataMember(Name = "processName", Order = 3, EmitDefaultValue = false)]
    public string? ProcessName { get; set; }

    [DataMember(Name = "waitForBreak", Order = 4, EmitDefaultValue = false)]
    public bool WaitForBreak { get; set; }

    [DataMember(Name = "breakTimeoutMs", Order = 5, EmitDefaultValue = false)]
    public int? BreakTimeoutMs { get; set; }
}

[DataContract]
public sealed class DebuggerAttachResponse
{
    [DataMember(Name = "vsInstanceId", Order = 1)]
    public string VsInstanceId { get; set; } = string.Empty;

    [DataMember(Name = "processId", Order = 2)]
    public int ProcessId { get; set; }

    [DataMember(Name = "processName", Order = 3)]
    public string ProcessName { get; set; } = string.Empty;

    [DataMember(Name = "currentMode", Order = 4)]
    public string CurrentMode { get; set; } = string.Empty;

    [DataMember(Name = "isDebugging", Order = 5)]
    public bool IsDebugging { get; set; }

    [DataMember(Name = "lastBreakReason", Order = 6, EmitDefaultValue = false)]
    public string? LastBreakReason { get; set; }

    [DataMember(Name = "topFrame", Order = 7, EmitDefaultValue = false)]
    public StackFrameInfo? TopFrame { get; set; }

    [DataMember(Name = "warnings", Order = 8, EmitDefaultValue = false)]
    public List<BridgeWarning> Warnings { get; set; } = new();
}

[DataContract]
public sealed class DebuggerDetachRequest
{
    [DataMember(Name = "vsInstanceId", Order = 1, EmitDefaultValue = false)]
    public string? VsInstanceId { get; set; }

    [DataMember(Name = "processId", Order = 2, EmitDefaultValue = false)]
    public int? ProcessId { get; set; }
}

[DataContract]
public sealed class DebuggerDetachResponse
{
    [DataMember(Name = "vsInstanceId", Order = 1)]
    public string VsInstanceId { get; set; } = string.Empty;

    [DataMember(Name = "detachedProcessId", Order = 2, EmitDefaultValue = false)]
    public int? DetachedProcessId { get; set; }

    [DataMember(Name = "currentMode", Order = 3)]
    public string CurrentMode { get; set; } = string.Empty;

    [DataMember(Name = "isDebugging", Order = 4)]
    public bool IsDebugging { get; set; }
}

[DataContract]
public sealed class ModuleInfo
{
    [DataMember(Name = "name", Order = 1)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Name = "path", Order = 2, EmitDefaultValue = false)]
    public string? Path { get; set; }

    [DataMember(Name = "order", Order = 3)]
    public uint Order { get; set; }

    [DataMember(Name = "version", Order = 4, EmitDefaultValue = false)]
    public string? Version { get; set; }

    [DataMember(Name = "loadAddress", Order = 5, EmitDefaultValue = false)]
    public string? LoadAddress { get; set; }

    [DataMember(Name = "endAddress", Order = 6, EmitDefaultValue = false)]
    public string? EndAddress { get; set; }

    [DataMember(Name = "symbolFile", Order = 7, EmitDefaultValue = false)]
    public string? SymbolFile { get; set; }

    [DataMember(Name = "symbolsLoaded", Order = 8)]
    public bool SymbolsLoaded { get; set; }

    [DataMember(Name = "optimized", Order = 9)]
    public bool Optimized { get; set; }

    [DataMember(Name = "userCode", Order = 10)]
    public bool UserCode { get; set; }

    [DataMember(Name = "is64Bit", Order = 11)]
    public bool Is64Bit { get; set; }
}

[DataContract]
public sealed class DebuggerGetModulesRequest
{
    [DataMember(Name = "vsInstanceId", Order = 1, EmitDefaultValue = false)]
    public string? VsInstanceId { get; set; }

    [DataMember(Name = "processId", Order = 2, EmitDefaultValue = false)]
    public int? ProcessId { get; set; }

    [DataMember(Name = "nameFilter", Order = 3, EmitDefaultValue = false)]
    public string? NameFilter { get; set; }

    [DataMember(Name = "userCodeOnly", Order = 4, EmitDefaultValue = false)]
    public bool UserCodeOnly { get; set; }

    [DataMember(Name = "maxCount", Order = 5, EmitDefaultValue = false)]
    public int? MaxCount { get; set; }
}

[DataContract]
public sealed class DebuggerGetModulesResponse
{
    [DataMember(Name = "vsInstanceId", Order = 1)]
    public string VsInstanceId { get; set; } = string.Empty;

    [DataMember(Name = "processId", Order = 2)]
    public int ProcessId { get; set; }

    [DataMember(Name = "processName", Order = 3, EmitDefaultValue = false)]
    public string? ProcessName { get; set; }

    [DataMember(Name = "totalCount", Order = 4)]
    public int TotalCount { get; set; }

    [DataMember(Name = "returnedCount", Order = 5)]
    public int ReturnedCount { get; set; }

    [DataMember(Name = "modules", Order = 6)]
    public List<ModuleInfo> Modules { get; set; } = new();
}

[DataContract]
public sealed class GetActiveDocumentRequest
{
    [DataMember(Name = "vsInstanceId", Order = 1, EmitDefaultValue = false)]
    public string? VsInstanceId { get; set; }
}

[DataContract]
public sealed class GetActiveDocumentResponse
{
    [DataMember(Name = "vsInstanceId", Order = 1)]
    public string VsInstanceId { get; set; } = string.Empty;

    [DataMember(Name = "hasActiveDocument", Order = 2)]
    public bool HasActiveDocument { get; set; }

    [DataMember(Name = "filePath", Order = 3, EmitDefaultValue = false)]
    public string? FilePath { get; set; }

    [DataMember(Name = "fileName", Order = 4, EmitDefaultValue = false)]
    public string? FileName { get; set; }

    [DataMember(Name = "isDirty", Order = 5)]
    public bool IsDirty { get; set; }

    [DataMember(Name = "isReadOnly", Order = 6)]
    public bool IsReadOnly { get; set; }

    [DataMember(Name = "language", Order = 7, EmitDefaultValue = false)]
    public string? Language { get; set; }

    [DataMember(Name = "cursorLine", Order = 8, EmitDefaultValue = false)]
    public int? CursorLine { get; set; }

    [DataMember(Name = "cursorColumn", Order = 9, EmitDefaultValue = false)]
    public int? CursorColumn { get; set; }

    [DataMember(Name = "lineCount", Order = 10, EmitDefaultValue = false)]
    public int? LineCount { get; set; }

    [DataMember(Name = "hasSelection", Order = 11)]
    public bool HasSelection { get; set; }

    [DataMember(Name = "selectionStartLine", Order = 12, EmitDefaultValue = false)]
    public int? SelectionStartLine { get; set; }

    [DataMember(Name = "selectionStartColumn", Order = 13, EmitDefaultValue = false)]
    public int? SelectionStartColumn { get; set; }

    [DataMember(Name = "selectionEndLine", Order = 14, EmitDefaultValue = false)]
    public int? SelectionEndLine { get; set; }

    [DataMember(Name = "selectionEndColumn", Order = 15, EmitDefaultValue = false)]
    public int? SelectionEndColumn { get; set; }

    [DataMember(Name = "selectedText", Order = 16, EmitDefaultValue = false)]
    public string? SelectedText { get; set; }
}

[DataContract]
public sealed class NavigateToRequest
{
    [DataMember(Name = "vsInstanceId", Order = 1, EmitDefaultValue = false)]
    public string? VsInstanceId { get; set; }

    [DataMember(Name = "filePath", Order = 2)]
    public string FilePath { get; set; } = string.Empty;

    [DataMember(Name = "line", Order = 3, EmitDefaultValue = false)]
    public int? Line { get; set; }

    [DataMember(Name = "column", Order = 4, EmitDefaultValue = false)]
    public int? Column { get; set; }

    [DataMember(Name = "preview", Order = 5, EmitDefaultValue = false)]
    public bool Preview { get; set; }
}

[DataContract]
public sealed class NavigateToResponse
{
    [DataMember(Name = "vsInstanceId", Order = 1)]
    public string VsInstanceId { get; set; } = string.Empty;

    [DataMember(Name = "filePath", Order = 2)]
    public string FilePath { get; set; } = string.Empty;

    [DataMember(Name = "line", Order = 3)]
    public int Line { get; set; } = 1;

    [DataMember(Name = "column", Order = 4)]
    public int Column { get; set; } = 1;

    [DataMember(Name = "success", Order = 5)]
    public bool Success { get; set; }
}

[DataContract]
public sealed class SolutionConfigurationInfo
{
    [DataMember(Name = "name", Order = 1)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Name = "platformName", Order = 2)]
    public string PlatformName { get; set; } = string.Empty;

    [DataMember(Name = "fullName", Order = 3)]
    public string FullName { get; set; } = string.Empty;

    [DataMember(Name = "isActive", Order = 4)]
    public bool IsActive { get; set; }
}

[DataContract]
public sealed class GetSolutionConfigurationsRequest
{
    [DataMember(Name = "vsInstanceId", Order = 1, EmitDefaultValue = false)]
    public string? VsInstanceId { get; set; }
}

[DataContract]
public sealed class GetSolutionConfigurationsResponse
{
    [DataMember(Name = "vsInstanceId", Order = 1)]
    public string VsInstanceId { get; set; } = string.Empty;

    [DataMember(Name = "solutionName", Order = 2)]
    public string SolutionName { get; set; } = string.Empty;

    [DataMember(Name = "solutionPath", Order = 3)]
    public string SolutionPath { get; set; } = string.Empty;

    [DataMember(Name = "activeConfigurationName", Order = 4)]
    public string ActiveConfigurationName { get; set; } = string.Empty;

    [DataMember(Name = "activePlatformName", Order = 5)]
    public string ActivePlatformName { get; set; } = string.Empty;

    [DataMember(Name = "configurations", Order = 6)]
    public List<SolutionConfigurationInfo> Configurations { get; set; } = new();
}

[DataContract]
public sealed class DebuggerThreadControlRequest
{
    [DataMember(Name = "threadId", Order = 1)]
    public int ThreadId { get; set; }

    [DataMember(Name = "vsInstanceId", Order = 2, EmitDefaultValue = false)]
    public string? VsInstanceId { get; set; }
}

[DataContract]
public sealed class DebuggerThreadControlResponse
{
    [DataMember(Name = "vsInstanceId", Order = 1)]
    public string VsInstanceId { get; set; } = string.Empty;

    [DataMember(Name = "threadId", Order = 2)]
    public int ThreadId { get; set; }

    [DataMember(Name = "action", Order = 3)]
    public string Action { get; set; } = string.Empty;

    [DataMember(Name = "isFrozen", Order = 4)]
    public bool IsFrozen { get; set; }

    [DataMember(Name = "suspendedCount", Order = 5)]
    public int SuspendedCount { get; set; }

    [DataMember(Name = "success", Order = 6)]
    public bool Success { get; set; }
}

[DataContract]
public sealed class DebuggerSetNextStatementRequest
{
    [DataMember(Name = "filePath", Order = 1, EmitDefaultValue = false)]
    public string? FilePath { get; set; }

    [DataMember(Name = "line", Order = 2, EmitDefaultValue = false)]
    public int Line { get; set; }

    [DataMember(Name = "column", Order = 3, EmitDefaultValue = false)]
    public int? Column { get; set; }

    [DataMember(Name = "vsInstanceId", Order = 4, EmitDefaultValue = false)]
    public string? VsInstanceId { get; set; }
}

[DataContract]
public sealed class DebuggerSetNextStatementResponse
{
    [DataMember(Name = "vsInstanceId", Order = 1)]
    public string VsInstanceId { get; set; } = string.Empty;

    [DataMember(Name = "filePath", Order = 2)]
    public string FilePath { get; set; } = string.Empty;

    [DataMember(Name = "line", Order = 3)]
    public int Line { get; set; }

    [DataMember(Name = "column", Order = 4)]
    public int Column { get; set; }

    [DataMember(Name = "success", Order = 5)]
    public bool Success { get; set; }

    [DataMember(Name = "topFrame", Order = 6, EmitDefaultValue = false)]
    public StackFrameInfo? TopFrame { get; set; }
}

[DataContract]
public sealed class DebuggerListBreakpointsRequest
{
    [DataMember(Name = "filePath", Order = 1, EmitDefaultValue = false)]
    public string? FilePath { get; set; }

    [DataMember(Name = "enabledOnly", Order = 2)]
    public bool EnabledOnly { get; set; }

    [DataMember(Name = "vsInstanceId", Order = 3, EmitDefaultValue = false)]
    public string? VsInstanceId { get; set; }
}

[DataContract]
public sealed class DebuggerListBreakpointsResponse
{
    [DataMember(Name = "vsInstanceId", Order = 1)]
    public string VsInstanceId { get; set; } = string.Empty;

    [DataMember(Name = "totalCount", Order = 2)]
    public int TotalCount { get; set; }

    [DataMember(Name = "breakpoints", Order = 3)]
    public List<BreakpointInfo> Breakpoints { get; set; } = new();
}

[DataContract]
public sealed class DebuggerClearBreakpointsRequest
{
    [DataMember(Name = "clearAll", Order = 1)]
    public bool ClearAll { get; set; }

    [DataMember(Name = "filePath", Order = 2, EmitDefaultValue = false)]
    public string? FilePath { get; set; }

    [DataMember(Name = "line", Order = 3, EmitDefaultValue = false)]
    public int? Line { get; set; }

    [DataMember(Name = "breakpointId", Order = 4, EmitDefaultValue = false)]
    public string? BreakpointId { get; set; }

    [DataMember(Name = "vsInstanceId", Order = 5, EmitDefaultValue = false)]
    public string? VsInstanceId { get; set; }
}

[DataContract]
public sealed class DebuggerClearBreakpointsResponse
{
    [DataMember(Name = "vsInstanceId", Order = 1)]
    public string VsInstanceId { get; set; } = string.Empty;

    [DataMember(Name = "clearedCount", Order = 2)]
    public int ClearedCount { get; set; }

    [DataMember(Name = "remainingCount", Order = 3)]
    public int RemainingCount { get; set; }

    [DataMember(Name = "warnings", Order = 4)]
    public List<BridgeWarning> Warnings { get; set; } = new();
}

[DataContract]
public sealed class DebuggerToggleBreakpointRequest
{
    [DataMember(Name = "breakpointId", Order = 1, EmitDefaultValue = false)]
    public string? BreakpointId { get; set; }

    [DataMember(Name = "filePath", Order = 2, EmitDefaultValue = false)]
    public string? FilePath { get; set; }

    [DataMember(Name = "line", Order = 3, EmitDefaultValue = false)]
    public int? Line { get; set; }

    [DataMember(Name = "enabled", Order = 4, EmitDefaultValue = false)]
    public bool? Enabled { get; set; }

    [DataMember(Name = "vsInstanceId", Order = 5, EmitDefaultValue = false)]
    public string? VsInstanceId { get; set; }
}

[DataContract]
public sealed class DebuggerToggleBreakpointResponse
{
    [DataMember(Name = "vsInstanceId", Order = 1)]
    public string VsInstanceId { get; set; } = string.Empty;

    [DataMember(Name = "matchedCount", Order = 2)]
    public int MatchedCount { get; set; }

    [DataMember(Name = "breakpoints", Order = 3)]
    public List<BreakpointInfo> Breakpoints { get; set; } = new();

    [DataMember(Name = "warnings", Order = 4)]
    public List<BridgeWarning> Warnings { get; set; } = new();
}