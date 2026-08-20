using System.Diagnostics;
using VsDebugMcp.Protocol;
using Xunit;

namespace VsDebugMcp.Host.Tests;

public sealed class VisualStudioInstanceRegistryTests
{
    [Fact]
    public void RegisterResolveAndFindUseSessionIdentity()
    {
        var registry = CreateRegistry();
        var instance = CreateCurrentProcessInstance("Sample.sln");

        var registration = registry.Register(instance);
        var resolved = registry.Resolve(null);
        var found = registry.Find("Sample");

        Assert.True(registration.Accepted);
        Assert.Equal(5, registration.HeartbeatIntervalSeconds);
        Assert.Equal(instance.VsInstanceId, resolved.VsInstanceId);
        Assert.Single(found);
    }

    [Fact]
    public void ResolveRejectsMissingInstance()
    {
        var registry = CreateRegistry();

        var exception = Assert.Throws<BridgeServiceException>(() => registry.Resolve(null));

        Assert.Equal(BridgeErrorCodes.InstanceNotFound, exception.Code);
    }

    [Fact]
    public void HeartbeatDoesNotCreateUnknownInstance()
    {
        var registry = CreateRegistry();

        var response = registry.Heartbeat(CreateCurrentProcessInstance("Sample.sln"));

        Assert.False(response.Accepted);
        Assert.Empty(registry.List());
    }

    [Fact]
    public void RemovingFinalInstanceRequestsHostStop()
    {
        var registry = CreateRegistry();
        var instance = CreateCurrentProcessInstance("Sample.sln");
        registry.Register(instance);

        var result = registry.Unregister(instance.VsInstanceId);

        Assert.True(result.Removed);
        Assert.True(result.ShouldStop);
    }

    private static VisualStudioInstanceRegistry CreateRegistry() =>
        new(new VsHostOptions(), () => { });

    private static VisualStudioInstanceDescriptor CreateCurrentProcessInstance(string solutionName)
    {
        using var process = Process.GetCurrentProcess();
        var startTicks = process.StartTime.ToUniversalTime().Ticks;
        var id = VisualStudioInstanceIds.Create(process.Id, startTicks);
        return new VisualStudioInstanceDescriptor
        {
            VsInstanceId = id,
            VisualStudioProcessId = process.Id,
            ProcessStartTimeUtcTicks = startTicks,
            VisualStudioVersion = "test",
            SolutionName = solutionName,
            SolutionFilePath = $"C:\\src\\{solutionName}",
            BridgePipeName = PipeNames.ForVisualStudioInstance(id)
        };
    }
}
