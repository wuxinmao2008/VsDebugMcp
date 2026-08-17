using VsDebugMcp.Protocol;

namespace VsDebugMcp.Host;

public enum HostMode
{
    Mcp,
    Smoke,
    Help
}

public sealed class VsHostOptions
{
    public HostMode Mode { get; init; } = HostMode.Mcp;

    public string PipeName { get; init; } = PipeNames.ForCurrentUser();

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(3);
}

public static class HostCommandLine
{
    public static VsHostOptions Parse(string[] args)
    {
        var mode = HostMode.Mcp;
        var modeSpecified = false;
        var pipeName = PipeNames.ForCurrentUser();
        var connectTimeout = TimeSpan.FromSeconds(3);

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--mcp":
                    SetMode(HostMode.Mcp);
                    break;
                case "--smoke":
                    SetMode(HostMode.Smoke);
                    break;
                case "--help":
                case "-h":
                    SetMode(HostMode.Help);
                    break;
                case "--pipe":
                    pipeName = ReadValue(args, ref index, "--pipe");
                    break;
                case "--connect-timeout-seconds":
                    var value = ReadValue(args, ref index, "--connect-timeout-seconds");
                    if (!int.TryParse(value, out var seconds) || seconds is < 1 or > 30)
                    {
                        throw new ArgumentException("--connect-timeout-seconds must be between 1 and 30.");
                    }

                    connectTimeout = TimeSpan.FromSeconds(seconds);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {args[index]}");
            }
        }

        return new VsHostOptions
        {
            Mode = mode,
            PipeName = pipeName,
            ConnectTimeout = connectTimeout
        };

        void SetMode(HostMode requestedMode)
        {
            if (modeSpecified && mode != requestedMode)
            {
                throw new ArgumentException("Only one host mode can be selected.");
            }

            mode = requestedMode;
            modeSpecified = true;
        }
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        return args[index];
    }
}