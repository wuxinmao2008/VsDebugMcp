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

    [Fact]
    public void TestContractsRoundTripThroughSharedSerializer()
    {
        var getReq = new GetTestsRequest { ProjectName = "SampleApp", Filter = "Add" };
        var getReqCopy = BridgeJson.Deserialize<GetTestsRequest>(BridgeJson.Serialize(getReq));
        Assert.Equal("SampleApp", getReqCopy.ProjectName);
        Assert.Equal("Add", getReqCopy.Filter);

        var getResp = new GetTestsResponse
        {
            VsInstanceId = "vs-1",
            TotalCount = 1,
            Tests = new List<VsTestItem>
            {
                new()
                {
                    TestId = "d3b07384-d113-46fb-ba3a-ec4f67645d12",
                    DisplayName = "CalculatorTests.Add_TwoNumbers_ReturnsSum",
                    FullyQualifiedName = "SampleTests.CalculatorTests.Add_TwoNumbers_ReturnsSum",
                    FilePath = @"C:\Sample\CalculatorTests.cs",
                    LineNumber = 10,
                    State = "Passed",
                    DurationMs = 12.5
                }
            }
        };
        var getRespCopy = BridgeJson.Deserialize<GetTestsResponse>(BridgeJson.Serialize(getResp));
        Assert.Equal(1, getRespCopy.TotalCount);
        Assert.Equal("CalculatorTests.Add_TwoNumbers_ReturnsSum", getRespCopy.Tests[0].DisplayName);
        Assert.Equal(12.5, getRespCopy.Tests[0].DurationMs);

        var runReq = new RunTestsRequest
        {
            TestIds = new List<string> { "d3b07384-d113-46fb-ba3a-ec4f67645d12" }
        };
        var runReqCopy = BridgeJson.Deserialize<RunTestsRequest>(BridgeJson.Serialize(runReq));
        Assert.Single(runReqCopy.TestIds!);

        var runResp = new RunTestsResponse
        {
            VsInstanceId = "vs-1",
            TestRunId = "testrun-12345678",
            State = TestRunStates.Running,
            TotalCount = 1,
            StartedAt = "2026-09-05T12:00:00Z"
        };
        var runRespCopy = BridgeJson.Deserialize<RunTestsResponse>(BridgeJson.Serialize(runResp));
        Assert.Equal("testrun-12345678", runRespCopy.TestRunId);
        Assert.Equal(TestRunStates.Running, runRespCopy.State);

        var statusResp = new TestRunStatusResponse
        {
            VsInstanceId = "vs-1",
            TestRunId = "testrun-12345678",
            State = TestRunStates.Completed,
            TotalCount = 1,
            PassedCount = 1,
            FailedCount = 0,
            SkippedCount = 0,
            DurationMs = 25.0,
            Results = new List<VsTestResult>
            {
                new()
                {
                    TestId = "d3b07384-d113-46fb-ba3a-ec4f67645d12",
                    DisplayName = "CalculatorTests.Add_TwoNumbers_ReturnsSum",
                    Outcome = "Passed",
                    DurationMs = 25.0
                }
            }
        };
        var statusRespCopy = BridgeJson.Deserialize<TestRunStatusResponse>(BridgeJson.Serialize(statusResp));
        Assert.Equal(TestRunStates.Completed, statusRespCopy.State);
        Assert.Equal(1, statusRespCopy.PassedCount);
        Assert.Equal("Passed", statusRespCopy.Results[0].Outcome);

        var cancelReq = new CancelTestRunRequest { TestRunId = "testrun-12345678" };
        var cancelReqCopy = BridgeJson.Deserialize<CancelTestRunRequest>(BridgeJson.Serialize(cancelReq));
        Assert.Equal("testrun-12345678", cancelReqCopy.TestRunId);

        var cancelResp = new CancelTestRunResponse
        {
            VsInstanceId = "vs-1",
            TestRunId = "testrun-12345678",
            State = TestRunStates.Cancelled,
            CancelRequested = true
        };
        var cancelRespCopy = BridgeJson.Deserialize<CancelTestRunResponse>(BridgeJson.Serialize(cancelResp));
        Assert.True(cancelRespCopy.CancelRequested);
        Assert.Equal(TestRunStates.Cancelled, cancelRespCopy.State);
    }

    [Fact]
    public void DebugTestAndGetThreadsRoundTripThroughSharedSerializer()
    {
        var debugReq = new DebugTestRequest
        {
            VsInstanceId = "vs-1",
            TestId = "d3b07384-d113-46fb-ba3a-ec4f67645d12",
            WaitForBreak = true,
            TimeoutMs = 5000
        };
        var debugReqCopy = BridgeJson.Deserialize<DebugTestRequest>(BridgeJson.Serialize(debugReq));
        Assert.Equal("d3b07384-d113-46fb-ba3a-ec4f67645d12", debugReqCopy.TestId);
        Assert.True(debugReqCopy.WaitForBreak);
        Assert.Equal(5000, debugReqCopy.TimeoutMs);

        var debugResp = new DebugTestResponse
        {
            VsInstanceId = "vs-1",
            TestRunId = "testrun-1234",
            TestId = "d3b07384-d113-46fb-ba3a-ec4f67645d12",
            TestDisplayName = "CalculatorTests.Multiply",
            IsDebugging = true,
            DebuggerMode = "break",
            LastBreakReason = "breakpoint",
            CurrentProcessId = 1234,
            CurrentThreadId = 5678,
            TopFrame = new StackFrameInfo
            {
                FrameIndex = 0,
                FunctionName = "Calculator.Multiply",
                FileName = @"C:\Sample\Calculator.cs",
                LineNumber = 15
            }
        };
        var debugRespCopy = BridgeJson.Deserialize<DebugTestResponse>(BridgeJson.Serialize(debugResp));
        Assert.Equal("testrun-1234", debugRespCopy.TestRunId);
        Assert.Equal("break", debugRespCopy.DebuggerMode);
        Assert.NotNull(debugRespCopy.TopFrame);
        Assert.Equal(15, debugRespCopy.TopFrame!.LineNumber);

        var threadsReq = new DebuggerGetThreadsRequest { VsInstanceId = "vs-1" };
        var threadsReqCopy = BridgeJson.Deserialize<DebuggerGetThreadsRequest>(BridgeJson.Serialize(threadsReq));
        Assert.Equal("vs-1", threadsReqCopy.VsInstanceId);

        var threadsResp = new DebuggerGetThreadsResponse
        {
            VsInstanceId = "vs-1",
            CurrentThreadId = 5678,
            TotalCount = 2,
            Threads = new List<ThreadInfo>
            {
                new()
                {
                    Id = 5678,
                    Name = "Main Thread",
                    IsAlive = true,
                    IsCurrent = true,
                    SuspendedCount = 0,
                    Priority = "Normal",
                    TopFrame = new StackFrameInfo { FunctionName = "Calculator.Multiply" }
                },
                new()
                {
                    Id = 5679,
                    Name = "Worker Thread",
                    IsAlive = true,
                    IsCurrent = false,
                    SuspendedCount = 0
                }
            }
        };
        var threadsRespCopy = BridgeJson.Deserialize<DebuggerGetThreadsResponse>(BridgeJson.Serialize(threadsResp));
        Assert.Equal(2, threadsRespCopy.TotalCount);
        Assert.True(threadsRespCopy.Threads[0].IsCurrent);
        Assert.False(threadsRespCopy.Threads[1].IsCurrent);
        Assert.Equal("Main Thread", threadsRespCopy.Threads[0].Name);

        // Advanced Breakpoint contracts serialization
        var bpSpec = new BreakpointSpec
        {
            Line = 42,
            Column = 10,
            Condition = "x > 100",
            ConditionType = "whenTrue",
            HitCountTarget = 5,
            HitCountType = "greaterOrEqual",
            Enabled = true
        };
        var bpSpecCopy = BridgeJson.Deserialize<BreakpointSpec>(BridgeJson.Serialize(bpSpec));
        Assert.Equal("whenTrue", bpSpecCopy.ConditionType);
        Assert.Equal(5, bpSpecCopy.HitCountTarget);
        Assert.Equal("greaterOrEqual", bpSpecCopy.HitCountType);

        var bpInfo = new BreakpointInfo
        {
            Id = "test.cs:42",
            FilePath = "test.cs",
            Line = 42,
            Condition = "x > 100",
            ConditionType = "whenTrue",
            HitCountTarget = 5,
            HitCountType = "greaterOrEqual",
            CurrentHitCount = 3,
            IsBound = true
        };
        var bpInfoCopy = BridgeJson.Deserialize<BreakpointInfo>(BridgeJson.Serialize(bpInfo));
        Assert.Equal(3, bpInfoCopy.CurrentHitCount);
        Assert.Equal("whenTrue", bpInfoCopy.ConditionType);
        Assert.Equal(5, bpInfoCopy.HitCountTarget);

        // Exception Info contracts serialization
        var exReq = new DebuggerGetExceptionInfoRequest { VsInstanceId = "vs-1" };
        var exReqCopy = BridgeJson.Deserialize<DebuggerGetExceptionInfoRequest>(BridgeJson.Serialize(exReq));
        Assert.Equal("vs-1", exReqCopy.VsInstanceId);

        var exResp = new DebuggerGetExceptionInfoResponse
        {
            VsInstanceId = "vs-1",
            HasException = true,
            ExceptionType = "System.DivideByZeroException",
            Message = "Attempted to divide by zero.",
            HResult = "0x80020012",
            Source = "SampleApp",
            StackTrace = "   at Calculator.Divide(Int32 a, Int32 b) in Calculator.cs:line 20",
            InnerException = null,
            RawDetails = "System.DivideByZeroException: Attempted to divide by zero."
        };
        var exRespCopy = BridgeJson.Deserialize<DebuggerGetExceptionInfoResponse>(BridgeJson.Serialize(exResp));
        Assert.True(exRespCopy.HasException);
        Assert.Equal("System.DivideByZeroException", exRespCopy.ExceptionType);
        Assert.Equal("0x80020012", exRespCopy.HResult);
        Assert.Equal("Attempted to divide by zero.", exRespCopy.Message);
        Assert.Contains("Calculator.Divide", exRespCopy.StackTrace);
    }
}
