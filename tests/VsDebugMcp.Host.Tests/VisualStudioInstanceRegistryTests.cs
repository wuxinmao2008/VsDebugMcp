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

    [Fact]
    public void ResolveMatchesByWorkingDirectoryWhenMultipleInstancesExist()
    {
        var registry = new VisualStudioInstanceRegistry(new VsHostOptions(), () => { }, _ => { });

        var instanceA = new VisualStudioInstanceDescriptor
        {
            VsInstanceId = "vs-inst-a",
            VisualStudioProcessId = 1001,
            ProcessStartTimeUtcTicks = 12345,
            VisualStudioVersion = "test",
            SolutionName = "SolutionA",
            SolutionFilePath = @"C:\repos\ProjectA\SolutionA.sln",
            BridgePipeName = "pipeA"
        };
        var instanceB = new VisualStudioInstanceDescriptor
        {
            VsInstanceId = "vs-inst-b",
            VisualStudioProcessId = 1002,
            ProcessStartTimeUtcTicks = 12346,
            VisualStudioVersion = "test",
            SolutionName = "SolutionB",
            SolutionFilePath = @"C:\repos\ProjectB\SolutionB.sln",
            BridgePipeName = "pipeB"
        };

        registry.Register(instanceA);
        registry.Register(instanceB);

        // 1. Ambiguous when no targetPath is given
        var ex1 = Assert.Throws<BridgeServiceException>(() => registry.Resolve(null));
        Assert.Equal(BridgeErrorCodes.AmbiguousInstance, ex1.Code);

        // 2. Matches subpath of ProjectA
        var matchedA = registry.Resolve(null, @"C:\repos\ProjectA\src\Controllers\HomeController.cs");
        Assert.Equal("vs-inst-a", matchedA.VsInstanceId);

        // 3. Matches subpath of ProjectB
        var matchedB = registry.Resolve(null, @"C:\repos\ProjectB\src\Data\Model.cs");
        Assert.Equal("vs-inst-b", matchedB.VsInstanceId);

        // 4. Matches parent directory of ProjectA
        var matchedParentA = registry.Resolve(null, @"C:\repos\ProjectA");
        Assert.Equal("vs-inst-a", matchedParentA.VsInstanceId);

        // 5. Unrelated path still throws AmbiguousInstance
        var ex2 = Assert.Throws<BridgeServiceException>(() => registry.Resolve(null, @"D:\OtherRepos\SomethingElse.cs"));
        Assert.Equal(BridgeErrorCodes.AmbiguousInstance, ex2.Code);

        // 6. Explicit vsInstanceId bypasses path matching
        var explicitA = registry.Resolve("vs-inst-a", @"C:\repos\ProjectB\file.cs");
        Assert.Equal("vs-inst-a", explicitA.VsInstanceId);
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
