using VsDebugMcp.Protocol;
using Xunit;

namespace VsDebugMcp.Protocol.Tests;

public sealed class InstanceProtocolTests
{
    [Fact]
    public void InstanceIdIncludesProcessAndStartTime()
    {
        Assert.Equal("vs-42-00000000000004d2", VisualStudioInstanceIds.Create(42, 1234));
    }

    [Fact]
    public void InstancePipeIsDeterministicAndSanitized()
    {
        var first = PipeNames.ForVisualStudioInstance("vs-42-abc:def");
        var second = PipeNames.ForVisualStudioInstance("vs-42-abc:def");

        Assert.Equal(first, second);
        Assert.Contains("vs-42-abcdef", first, StringComparison.Ordinal);
        Assert.DoesNotContain(':', first);
    }

    [Fact]
    public void HostControlPipeIsUserScoped()
    {
        Assert.StartsWith("VsDebugMcp.Host.Control.v2.", PipeNames.ForHostControl(), StringComparison.Ordinal);
    }

    [Fact]
    public void RegistrationRoundTripsThroughSharedSerializer()
    {
        var request = new RegisterInstanceRequest
        {
            Instance = new VisualStudioInstanceDescriptor
            {
                VsInstanceId = "vs-42-00000000000004d2",
                VisualStudioProcessId = 42,
                ProcessStartTimeUtcTicks = 1234,
                BridgePipeName = "bridge",
                SolutionName = "Sample"
            }
        };

        var copy = BridgeJson.Deserialize<RegisterInstanceRequest>(BridgeJson.Serialize(request));

        Assert.Equal(request.Instance.VsInstanceId, copy.Instance.VsInstanceId);
        Assert.Equal(request.Instance.SolutionName, copy.Instance.SolutionName);
    }

    [Fact]
    public void GetFilesInProjectRoundTripsThroughSharedSerializer()
    {
        var response = new GetFilesInProjectResponse
        {
            VsInstanceId = "vs-42-00000000000004d2",
            TotalFileCount = 2,
            Projects = new List<ProjectFilesGroup>
            {
                new()
                {
                    ProjectId = "proj-1",
                    ProjectName = "SampleApp",
                    ProjectFilePath = @"C:\src\SampleApp.vcxproj",
                    FileCount = 2,
                    Files = new List<ProjectFileInfo>
                    {
                        new()
                        {
                            FilePath = @"C:\src\main.cpp",
                            RelativePath = "main.cpp",
                            FilterPath = "Source Files",
                            Extension = ".cpp"
                        },
                        new()
                        {
                            FilePath = @"C:\src\main.h",
                            RelativePath = "main.h",
                            FilterPath = "Header Files",
                            Extension = ".h"
                        }
                    }
                }
            }
        };

        var json = BridgeJson.Serialize(response);
        var copy = BridgeJson.Deserialize<GetFilesInProjectResponse>(json);

        Assert.Equal(response.VsInstanceId, copy.VsInstanceId);
        Assert.Equal(2, copy.TotalFileCount);
        Assert.Single(copy.Projects);
        Assert.Equal("SampleApp", copy.Projects[0].ProjectName);
        Assert.Equal(2, copy.Projects[0].Files.Count);
        Assert.Equal(@"C:\src\main.cpp", copy.Projects[0].Files[0].FilePath);
        Assert.Equal("Source Files", copy.Projects[0].Files[0].FilterPath);
    }

    [Fact]
    public void DebuggerContractsRoundTripThroughSharedSerializer()
    {
        var info = new DebuggerGetInfoResponse
        {
            VsInstanceId = "vs-1",
            Mode = "break",
            IsDebugging = true,
            CurrentProcessId = 1234,
            CurrentProcessName = "TestApp.exe",
            CurrentThreadId = 5678,
            CurrentThreadName = "Main Thread",
            BreakpointCount = 3,
            LastBreakReason = "breakpoint"
        };
        var infoCopy = BridgeJson.Deserialize<DebuggerGetInfoResponse>(BridgeJson.Serialize(info));
        Assert.Equal(info.Mode, infoCopy.Mode);
        Assert.Equal(1234, infoCopy.CurrentProcessId);
        Assert.Equal("breakpoint", infoCopy.LastBreakReason);

        var stack = new DebuggerGetCallStackResponse
        {
            VsInstanceId = "vs-1",
            ThreadId = 5678,
            ThreadName = "Main",
            TotalFrames = 1,
            Frames = new List<StackFrameInfo>
            {
                new()
                {
                    FrameIndex = 0,
                    FunctionName = "Calculator.Add",
                    FileName = @"C:\src\Calculator.cs",
                    LineNumber = 10,
                    Language = "C#"
                }
            }
        };
        var stackCopy = BridgeJson.Deserialize<DebuggerGetCallStackResponse>(BridgeJson.Serialize(stack));
        Assert.Single(stackCopy.Frames);
        Assert.Equal("Calculator.Add", stackCopy.Frames[0].FunctionName);
        Assert.Equal(10, stackCopy.Frames[0].LineNumber);

        var expr = new DebuggerEvaluateExprResponse
        {
            VsInstanceId = "vs-1",
            Expression = "a + b",
            Value = "30",
            Type = "int",
            IsValid = true,
            FrameIndex = 0
        };
        var exprCopy = BridgeJson.Deserialize<DebuggerEvaluateExprResponse>(BridgeJson.Serialize(expr));
        Assert.Equal("30", exprCopy.Value);
        Assert.Equal("int", exprCopy.Type);
        Assert.True(exprCopy.IsValid);
    }
}
