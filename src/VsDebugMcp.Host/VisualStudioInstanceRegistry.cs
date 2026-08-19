using System.Diagnostics;
using System.Globalization;
using VsDebugMcp.Protocol;

namespace VsDebugMcp.Host;

public sealed class VisualStudioInstanceRegistry
{
    private readonly Dictionary<string, VisualStudioInstanceDescriptor> _instances =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private readonly DateTime _startedAtUtc = DateTime.UtcNow;
    private readonly VsHostOptions _options;
    private readonly Action _requestStop;
    private bool _hasRegisteredInstance;

    public VisualStudioInstanceRegistry(VsHostOptions options, Action requestStop)
    {
        _options = options;
        _requestStop = requestStop;
    }

    public RegisterInstanceResponse Register(VisualStudioInstanceDescriptor instance)
    {
        ValidateInstance(instance);
        var now = DateTime.UtcNow;
        var registered = Copy(instance);
        registered.RegisteredAtUtc = now.ToString("O", CultureInfo.InvariantCulture);
        registered.LastHeartbeatUtc = registered.RegisteredAtUtc;

        lock (_sync)
        {
            _instances[registered.VsInstanceId] = registered;
            _hasRegisteredInstance = true;
        }

        return new RegisterInstanceResponse
        {
            Accepted = true,
            HeartbeatIntervalSeconds = (int)_options.HeartbeatInterval.TotalSeconds
        };
    }

    public HeartbeatInstanceResponse Heartbeat(VisualStudioInstanceDescriptor instance)
    {
        ValidateInstance(instance);
        lock (_sync)
        {
            if (!_instances.TryGetValue(instance.VsInstanceId, out var registered))
            {
                return new HeartbeatInstanceResponse { Accepted = false };
            }

            registered.VisualStudioVersion = instance.VisualStudioVersion;
            registered.SolutionName = instance.SolutionName;
            registered.SolutionFilePath = instance.SolutionFilePath;
            registered.LastHeartbeatUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            return new HeartbeatInstanceResponse { Accepted = true };
        }
    }

    public (bool Removed, bool ShouldStop) Unregister(string vsInstanceId)
    {
        lock (_sync)
        {
            var removed = _instances.Remove(vsInstanceId ?? string.Empty);
            return (removed, removed && _hasRegisteredInstance && _instances.Count == 0);
        }
    }

    public VisualStudioInstanceDescriptor Resolve(string? vsInstanceId)
    {
        lock (_sync)
        {
            if (!string.IsNullOrWhiteSpace(vsInstanceId))
            {
                if (_instances.TryGetValue(vsInstanceId, out var selected))
                {
                    return Copy(selected);
                }

                throw new BridgeServiceException(
                    BridgeErrorCodes.InstanceNotFound,
                    "The requested Visual Studio instance is not registered.",
                    true);
            }

            if (_instances.Count == 0)
            {
                throw new BridgeServiceException(
                    BridgeErrorCodes.InstanceNotFound,
                    "No Visual Studio instance is registered.",
                    true);
            }

            if (_instances.Count > 1)
            {
                throw new BridgeServiceException(
                    BridgeErrorCodes.AmbiguousInstance,
                    "Multiple Visual Studio instances are registered; specify vsInstanceId.",
                    false);
            }

            return Copy(_instances.Values.Single());
        }
    }

    public IReadOnlyList<VisualStudioInstanceDescriptor> List()
    {
        lock (_sync)
        {
            return _instances.Values
                .OrderBy(instance => instance.VisualStudioProcessId)
                .Select(Copy)
                .ToArray();
        }
    }

    public IReadOnlyList<VisualStudioInstanceDescriptor> Find(string? query)
    {
        var normalized = query?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return List();
        }

        lock (_sync)
        {
            return _instances.Values
                .Where(instance =>
                    instance.VsInstanceId.IndexOf(normalized, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    instance.VisualStudioProcessId.ToString(CultureInfo.InvariantCulture) == normalized ||
                    instance.SolutionName.IndexOf(normalized, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    instance.SolutionFilePath.IndexOf(normalized, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(instance => instance.VisualStudioProcessId)
                .Select(Copy)
                .ToArray();
        }
    }

    public async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            var shouldStop = false;
            var now = DateTime.UtcNow;
            lock (_sync)
            {
                var staleIds = _instances.Values
                    .Where(instance => IsStale(instance, now))
                    .Select(instance => instance.VsInstanceId)
                    .ToArray();
                foreach (var staleId in staleIds)
                {
                    _instances.Remove(staleId);
                }

                shouldStop = (_hasRegisteredInstance && _instances.Count == 0) ||
                    (!_hasRegisteredInstance && now - _startedAtUtc >= _options.InitialRegistrationTimeout);
            }

            if (shouldStop)
            {
                _requestStop();
                return;
            }
        }
    }

    private bool IsStale(VisualStudioInstanceDescriptor instance, DateTime now)
    {
        if (!DateTime.TryParse(
                instance.LastHeartbeatUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var lastHeartbeatUtc))
        {
            return true;
        }

        return now - lastHeartbeatUtc.ToUniversalTime() >= _options.StaleInstanceTimeout;
    }

    private static void ValidateInstance(VisualStudioInstanceDescriptor instance)
    {
        if (instance is null || instance.VisualStudioProcessId <= 0 || instance.ProcessStartTimeUtcTicks <= 0)
        {
            throw new ArgumentException("The Visual Studio instance identity is invalid.");
        }

        var expectedId = VisualStudioInstanceIds.Create(
            instance.VisualStudioProcessId,
            instance.ProcessStartTimeUtcTicks);
        if (!string.Equals(instance.VsInstanceId, expectedId, StringComparison.Ordinal))
        {
            throw new ArgumentException("The Visual Studio instance ID is invalid.");
        }

        var expectedPipe = PipeNames.ForVisualStudioInstance(expectedId);
        if (!string.Equals(instance.BridgePipeName, expectedPipe, StringComparison.Ordinal))
        {
            throw new ArgumentException("The Visual Studio bridge pipe name is invalid.");
        }

        try
        {
            using var process = Process.GetProcessById(instance.VisualStudioProcessId);
            if (process.StartTime.ToUniversalTime().Ticks != instance.ProcessStartTimeUtcTicks)
            {
                throw new ArgumentException("The Visual Studio process identity is stale.");
            }
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new ArgumentException("The Visual Studio process cannot be validated.", exception);
        }
    }

    private static VisualStudioInstanceDescriptor Copy(VisualStudioInstanceDescriptor source) => new()
    {
        VsInstanceId = source.VsInstanceId,
        VisualStudioProcessId = source.VisualStudioProcessId,
        ProcessStartTimeUtcTicks = source.ProcessStartTimeUtcTicks,
        VisualStudioVersion = source.VisualStudioVersion,
        SolutionName = source.SolutionName,
        SolutionFilePath = source.SolutionFilePath,
        BridgePipeName = source.BridgePipeName,
        RegisteredAtUtc = source.RegisteredAtUtc,
        LastHeartbeatUtc = source.LastHeartbeatUtc
    };
}