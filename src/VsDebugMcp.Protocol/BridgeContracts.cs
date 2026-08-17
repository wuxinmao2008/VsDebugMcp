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