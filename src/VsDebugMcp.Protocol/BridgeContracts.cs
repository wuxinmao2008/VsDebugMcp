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