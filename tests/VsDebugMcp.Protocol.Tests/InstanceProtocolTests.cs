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

    [Fact]
    public void DebuggerExecutionContractsRoundTripThroughSharedSerializer()
    {
        var execResponse = new DebuggerExecutionResponse
        {
            VsInstanceId = "vs-1",
            Action = "step_over",
            PreviousMode = "break",
            CurrentMode = "break",
            IsDebugging = true,
            LastBreakReason = "step",
            CurrentProcessId = 1234,
            CurrentThreadId = 5678,
            TopFrame = new StackFrameInfo
            {
                FrameIndex = 0,
                FunctionName = "Calculator.Add",
                FileName = @"C:\src\Calculator.cs",
                LineNumber = 6,
                Language = "C#"
            }
        };

        var json = BridgeJson.Serialize(execResponse);
        var copy = BridgeJson.Deserialize<DebuggerExecutionResponse>(json);

        Assert.Equal(execResponse.VsInstanceId, copy.VsInstanceId);
        Assert.Equal("step_over", copy.Action);
        Assert.Equal("break", copy.CurrentMode);
        Assert.True(copy.IsDebugging);
        Assert.Equal("step", copy.LastBreakReason);
        Assert.NotNull(copy.TopFrame);
        Assert.Equal(6, copy.TopFrame.LineNumber);
        Assert.Equal("Calculator.Add", copy.TopFrame.FunctionName);

        var stepReq = new DebuggerStepRequest { WaitForBreak = true };
        var stepReqCopy = BridgeJson.Deserialize<DebuggerStepRequest>(BridgeJson.Serialize(stepReq));
        Assert.True(stepReqCopy.WaitForBreak);

        var contReq = new DebuggerContinueRequest { WaitForBreak = false };
        var contReqCopy = BridgeJson.Deserialize<DebuggerContinueRequest>(BridgeJson.Serialize(contReq));
        Assert.False(contReqCopy.WaitForBreak);
    }

    [Fact]
    public void DebuggerDiagnosticEnhancementsRoundTripThroughSharedSerializer()
    {
        var startReq = new DebuggerStartRequest { WaitForBreak = true, TimeoutMs = 3000 };
        var startReqCopy = BridgeJson.Deserialize<DebuggerStartRequest>(BridgeJson.Serialize(startReq));
        Assert.True(startReqCopy.WaitForBreak);
        Assert.Equal(3000, startReqCopy.TimeoutMs);

        var evalReq = new DebuggerEvaluateExpressionsRequest
        {
            Expressions = new List<string> { "item.Id", "item.Name" },
            FrameIndex = 1,
            TimeoutMs = 1500,
            AllowSideEffects = false
        };
        var evalReqCopy = BridgeJson.Deserialize<DebuggerEvaluateExpressionsRequest>(BridgeJson.Serialize(evalReq));
        Assert.Equal(2, evalReqCopy.Expressions.Count);
        Assert.Equal("item.Id", evalReqCopy.Expressions[0]);
        Assert.Equal(1, evalReqCopy.FrameIndex);

        var evalResp = new DebuggerEvaluateExpressionsResponse
        {
            VsInstanceId = "vs-1",
            FrameIndex = 1,
            Results = new List<DebuggerExpressionItemResult>
            {
                new() { Expression = "item.Id", Value = "1", Type = "int", IsValid = true },
                new() { Expression = "item.Name", Value = "\"Widget\"", Type = "string", IsValid = true }
            }
        };
        var evalRespCopy = BridgeJson.Deserialize<DebuggerEvaluateExpressionsResponse>(BridgeJson.Serialize(evalResp));
        Assert.Equal(2, evalRespCopy.Results.Count);
        Assert.Equal("\"Widget\"", evalRespCopy.Results[1].Value);

        var localsResp = new DebuggerGetLocalsResponse
        {
            VsInstanceId = "vs-1",
            FrameIndex = 0,
            TotalCount = 2,
            Truncated = false,
            Variables = new List<DebuggerVariableInfo>
            {
                new() { Name = "a", Value = "10", Type = "int", IsArgument = true },
                new() { Name = "sum", Value = "30", Type = "int", IsArgument = false }
            }
        };
        var localsRespCopy = BridgeJson.Deserialize<DebuggerGetLocalsResponse>(BridgeJson.Serialize(localsResp));
        Assert.Equal(2, localsRespCopy.Variables.Count);
        Assert.True(localsRespCopy.Variables[0].IsArgument);
        Assert.False(localsRespCopy.Variables[1].IsArgument);
        Assert.Equal("10", localsRespCopy.Variables[0].Value);
    }
}
