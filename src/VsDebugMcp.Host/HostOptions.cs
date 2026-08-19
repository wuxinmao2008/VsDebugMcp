using VsDebugMcp.Protocol;

namespace VsDebugMcp.Host;

public sealed class VsHostOptions
{
    public const int HttpPort = 43260;

    public string ControlPipeName { get; init; } = PipeNames.ForHostControl();

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(3);

    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan StaleInstanceTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan InitialRegistrationTimeout { get; init; } = TimeSpan.FromSeconds(15);
}