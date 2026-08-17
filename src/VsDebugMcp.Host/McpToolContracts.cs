using VsDebugMcp.Protocol;

namespace VsDebugMcp.Host;

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