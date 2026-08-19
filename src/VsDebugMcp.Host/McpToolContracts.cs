using VsDebugMcp.Protocol;

namespace VsDebugMcp.Host;

public sealed class VsHealthResult
{
    public string Status { get; init; } = "ok";

    public string HostVersion { get; init; } = string.Empty;

    public string UtcTimestamp { get; init; } = string.Empty;

    public int InstanceCount { get; init; }

    public VisualStudioInstanceDescriptor? SelectedInstance { get; init; }

    public HealthResponse? Bridge { get; init; }
}

public sealed class VsInstancesResult
{
    public IReadOnlyList<VisualStudioInstanceDescriptor> Instances { get; init; } = [];
}

public sealed class VsCapabilitiesResult
{
    public string HostVersion { get; init; } = string.Empty;

    public string BridgeVersion { get; init; } = string.Empty;

    public string VisualStudioVersion { get; init; } = string.Empty;

    public int VisualStudioProcessId { get; init; }

    public string VsInstanceId { get; init; } = string.Empty;

    public string ProtocolVersion { get; init; } = BridgeProtocol.Version;

    public IReadOnlyList<CapabilityDescriptor> Capabilities { get; init; } = [];
}