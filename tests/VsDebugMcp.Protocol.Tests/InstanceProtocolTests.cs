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

    [Fact]
    public void DebuggerProcessesAndModulesContractsRoundTripThroughSharedSerializer()
    {
        // 1. Get Processes
        var procReq = new DebuggerGetProcessesRequest
        {
            VsInstanceId = "vs-1",
            ProcessName = "Sample",
            ProcessId = 12345,
            OnlyDebugged = false,
            MaxCount = 50
        };
        var procReqCopy = BridgeJson.Deserialize<DebuggerGetProcessesRequest>(BridgeJson.Serialize(procReq));
        Assert.Equal("Sample", procReqCopy.ProcessName);
        Assert.Equal(12345, procReqCopy.ProcessId);
        Assert.False(procReqCopy.OnlyDebugged);
        Assert.Equal(50, procReqCopy.MaxCount);

        var procResp = new DebuggerGetProcessesResponse
        {
            VsInstanceId = "vs-1",
            TotalCount = 1,
            ReturnedCount = 1,
            Processes = new List<ProcessInfo>
            {
                new()
                {
                    ProcessId = 12345,
                    Name = "SampleApp.exe",
                    UserName = @"DOMAIN\user",
                    IsBeingDebugged = true,
                    TransportQualifier = "localhost"
                }
            }
        };
        var procRespCopy = BridgeJson.Deserialize<DebuggerGetProcessesResponse>(BridgeJson.Serialize(procResp));
        Assert.Equal(1, procRespCopy.TotalCount);
        Assert.Single(procRespCopy.Processes);
        Assert.Equal(12345, procRespCopy.Processes[0].ProcessId);
        Assert.Equal("SampleApp.exe", procRespCopy.Processes[0].Name);
        Assert.True(procRespCopy.Processes[0].IsBeingDebugged);

        // 2. Attach Process
        var attachReq = new DebuggerAttachRequest
        {
            VsInstanceId = "vs-1",
            ProcessId = 12345,
            ProcessName = "SampleApp.exe",
            WaitForBreak = true,
            BreakTimeoutMs = 5000
        };
        var attachReqCopy = BridgeJson.Deserialize<DebuggerAttachRequest>(BridgeJson.Serialize(attachReq));
        Assert.Equal(12345, attachReqCopy.ProcessId);
        Assert.True(attachReqCopy.WaitForBreak);
        Assert.Equal(5000, attachReqCopy.BreakTimeoutMs);

        var attachResp = new DebuggerAttachResponse
        {
            VsInstanceId = "vs-1",
            ProcessId = 12345,
            ProcessName = "SampleApp.exe",
            CurrentMode = "running",
            IsDebugging = true,
            LastBreakReason = null,
            TopFrame = null,
            Warnings = new List<BridgeWarning>()
        };
        var attachRespCopy = BridgeJson.Deserialize<DebuggerAttachResponse>(BridgeJson.Serialize(attachResp));
        Assert.Equal(12345, attachRespCopy.ProcessId);
        Assert.Equal("running", attachRespCopy.CurrentMode);
        Assert.True(attachRespCopy.IsDebugging);

        // 3. Detach
        var detachReq = new DebuggerDetachRequest
        {
            VsInstanceId = "vs-1",
            ProcessId = 12345
        };
        var detachReqCopy = BridgeJson.Deserialize<DebuggerDetachRequest>(BridgeJson.Serialize(detachReq));
        Assert.Equal(12345, detachReqCopy.ProcessId);

        var detachResp = new DebuggerDetachResponse
        {
            VsInstanceId = "vs-1",
            DetachedProcessId = 12345,
            CurrentMode = "design",
            IsDebugging = false
        };
        var detachRespCopy = BridgeJson.Deserialize<DebuggerDetachResponse>(BridgeJson.Serialize(detachResp));
        Assert.Equal(12345, detachRespCopy.DetachedProcessId);
        Assert.Equal("design", detachRespCopy.CurrentMode);
        Assert.False(detachRespCopy.IsDebugging);

        // 4. Get Modules
        var modReq = new DebuggerGetModulesRequest
        {
            VsInstanceId = "vs-1",
            ProcessId = 12345,
            NameFilter = "Sample",
            UserCodeOnly = true,
            MaxCount = 100
        };
        var modReqCopy = BridgeJson.Deserialize<DebuggerGetModulesRequest>(BridgeJson.Serialize(modReq));
        Assert.Equal("Sample", modReqCopy.NameFilter);
        Assert.True(modReqCopy.UserCodeOnly);

        var modResp = new DebuggerGetModulesResponse
        {
            VsInstanceId = "vs-1",
            ProcessId = 12345,
            ProcessName = "SampleApp.exe",
            TotalCount = 1,
            ReturnedCount = 1,
            Modules = new List<ModuleInfo>
            {
                new()
                {
                    Name = "SampleApp.dll",
                    Path = @"C:\app\SampleApp.dll",
                    Order = 1,
                    Version = "1.0.0.0",
                    LoadAddress = "0x00007FF7B1230000",
                    EndAddress = "0x00007FF7B1250000",
                    SymbolFile = @"C:\app\SampleApp.pdb",
                    SymbolsLoaded = true,
                    Optimized = false,
                    UserCode = true,
                    Is64Bit = true
                }
            }
        };
        var modRespCopy = BridgeJson.Deserialize<DebuggerGetModulesResponse>(BridgeJson.Serialize(modResp));
        Assert.Equal(1, modRespCopy.TotalCount);
        Assert.Single(modRespCopy.Modules);
        Assert.Equal("SampleApp.dll", modRespCopy.Modules[0].Name);
        Assert.True(modRespCopy.Modules[0].SymbolsLoaded);
        Assert.Equal("0x00007FF7B1230000", modRespCopy.Modules[0].LoadAddress);
    }

    [Fact]
    public void ActiveContextAndEditorContractsRoundTripThroughSharedSerializer()
    {
        // 1. GetActiveDocument
        var getDocReq = new GetActiveDocumentRequest
        {
            VsInstanceId = "vs-1"
        };
        var getDocReqCopy = BridgeJson.Deserialize<GetActiveDocumentRequest>(BridgeJson.Serialize(getDocReq));
        Assert.Equal("vs-1", getDocReqCopy.VsInstanceId);

        var getDocResp = new GetActiveDocumentResponse
        {
            VsInstanceId = "vs-1",
            HasActiveDocument = true,
            FilePath = @"C:\src\Program.cs",
            FileName = "Program.cs",
            IsDirty = true,
            IsReadOnly = false,
            Language = "CSharp",
            CursorLine = 42,
            CursorColumn = 10,
            LineCount = 100,
            HasSelection = true,
            SelectionStartLine = 40,
            SelectionStartColumn = 1,
            SelectionEndLine = 42,
            SelectionEndColumn = 10,
            SelectedText = "Console.WriteLine();"
        };
        var getDocRespCopy = BridgeJson.Deserialize<GetActiveDocumentResponse>(BridgeJson.Serialize(getDocResp));
        Assert.True(getDocRespCopy.HasActiveDocument);
        Assert.Equal(@"C:\src\Program.cs", getDocRespCopy.FilePath);
        Assert.True(getDocRespCopy.IsDirty);
        Assert.Equal(42, getDocRespCopy.CursorLine);
        Assert.True(getDocRespCopy.HasSelection);
        Assert.Equal("Console.WriteLine();", getDocRespCopy.SelectedText);

        // 2. NavigateTo
        var navReq = new NavigateToRequest
        {
            VsInstanceId = "vs-1",
            FilePath = @"C:\src\Program.cs",
            Line = 50,
            Column = 5,
            Preview = false
        };
        var navReqCopy = BridgeJson.Deserialize<NavigateToRequest>(BridgeJson.Serialize(navReq));
        Assert.Equal(@"C:\src\Program.cs", navReqCopy.FilePath);
        Assert.Equal(50, navReqCopy.Line);
        Assert.Equal(5, navReqCopy.Column);

        var navResp = new NavigateToResponse
        {
            VsInstanceId = "vs-1",
            FilePath = @"C:\src\Program.cs",
            Line = 50,
            Column = 5,
            Success = true
        };
        var navRespCopy = BridgeJson.Deserialize<NavigateToResponse>(BridgeJson.Serialize(navResp));
        Assert.True(navRespCopy.Success);
        Assert.Equal(50, navRespCopy.Line);

        // 3. GetSolutionConfigurations
        var slnCfgReq = new GetSolutionConfigurationsRequest
        {
            VsInstanceId = "vs-1"
        };
        var slnCfgReqCopy = BridgeJson.Deserialize<GetSolutionConfigurationsRequest>(BridgeJson.Serialize(slnCfgReq));
        Assert.Equal("vs-1", slnCfgReqCopy.VsInstanceId);

        var slnCfgResp = new GetSolutionConfigurationsResponse
        {
            VsInstanceId = "vs-1",
            SolutionName = "SampleApp",
            SolutionPath = @"C:\src\SampleApp.sln",
            ActiveConfigurationName = "Debug",
            ActivePlatformName = "x64",
            Configurations = new List<SolutionConfigurationInfo>
            {
                new()
                {
                    Name = "Debug",
                    PlatformName = "x64",
                    FullName = "Debug|x64",
                    IsActive = true
                },
                new()
                {
                    Name = "Release",
                    PlatformName = "x64",
                    FullName = "Release|x64",
                    IsActive = false
                }
            }
        };
        var slnCfgRespCopy = BridgeJson.Deserialize<GetSolutionConfigurationsResponse>(BridgeJson.Serialize(slnCfgResp));
        Assert.Equal("SampleApp", slnCfgRespCopy.SolutionName);
        Assert.Equal("Debug", slnCfgRespCopy.ActiveConfigurationName);
        Assert.Equal(2, slnCfgRespCopy.Configurations.Count);
        Assert.True(slnCfgRespCopy.Configurations[0].IsActive);
        Assert.False(slnCfgRespCopy.Configurations[1].IsActive);
    }

    [Fact]
    public void DebuggerAdvancedControlsContractsRoundTripThroughSharedSerializer()
    {
        var threadInfo = new ThreadInfo
        {
            Id = 1234,
            Name = "WorkerThread",
            IsAlive = true,
            IsCurrent = false,
            SuspendedCount = 1,
            Priority = "Normal",
            IsFrozen = true
        };
        var threadInfoCopy = BridgeJson.Deserialize<ThreadInfo>(BridgeJson.Serialize(threadInfo));
        Assert.Equal(1234, threadInfoCopy.Id);
        Assert.True(threadInfoCopy.IsFrozen);
        Assert.Equal(1, threadInfoCopy.SuspendedCount);

        var freezeReq = new DebuggerThreadControlRequest { ThreadId = 4321, VsInstanceId = "vs-1" };
        var freezeReqCopy = BridgeJson.Deserialize<DebuggerThreadControlRequest>(BridgeJson.Serialize(freezeReq));
        Assert.Equal(4321, freezeReqCopy.ThreadId);
        Assert.Equal("vs-1", freezeReqCopy.VsInstanceId);

        var freezeResp = new DebuggerThreadControlResponse
        {
            VsInstanceId = "vs-1",
            ThreadId = 4321,
            Action = "freeze",
            IsFrozen = true,
            SuspendedCount = 1,
            Success = true
        };
        var freezeRespCopy = BridgeJson.Deserialize<DebuggerThreadControlResponse>(BridgeJson.Serialize(freezeResp));
        Assert.Equal("freeze", freezeRespCopy.Action);
        Assert.True(freezeRespCopy.IsFrozen);
        Assert.True(freezeRespCopy.Success);

        var nextStmtReq = new DebuggerSetNextStatementRequest
        {
            FilePath = "C:\\src\\Test.cs",
            Line = 42,
            Column = 5,
            VsInstanceId = "vs-1"
        };
        var nextStmtReqCopy = BridgeJson.Deserialize<DebuggerSetNextStatementRequest>(BridgeJson.Serialize(nextStmtReq));
        Assert.Equal("C:\\src\\Test.cs", nextStmtReqCopy.FilePath);
        Assert.Equal(42, nextStmtReqCopy.Line);
        Assert.Equal(5, nextStmtReqCopy.Column);

        var nextStmtResp = new DebuggerSetNextStatementResponse
        {
            VsInstanceId = "vs-1",
            FilePath = "C:\\src\\Test.cs",
            Line = 42,
            Column = 5,
            Success = true,
            TopFrame = new StackFrameInfo
            {
                FrameIndex = 0,
                FunctionName = "Test.DoWork",
                LineNumber = 42
            }
        };
        var nextStmtRespCopy = BridgeJson.Deserialize<DebuggerSetNextStatementResponse>(BridgeJson.Serialize(nextStmtResp));
        Assert.Equal(42, nextStmtRespCopy.Line);
        Assert.True(nextStmtRespCopy.Success);
        Assert.NotNull(nextStmtRespCopy.TopFrame);
        Assert.Equal("Test.DoWork", nextStmtRespCopy.TopFrame.FunctionName);
    }

    [Fact]
    public void Phase5ABreakpointAndStackFrameProtocolRoundTripsThroughSharedSerializer()
    {
        var frame = new StackFrameInfo
        {
            FrameIndex = 0,
            FunctionName = "MyWidget::onDataReceived()",
            FileName = "D:\\src\\MyWidget.cpp",
            LineNumber = 120,
            ColumnNumber = 5,
            UserCode = true,
            Language = "C++",
            Module = "MyApp.exe"
        };
        var frameCopy = BridgeJson.Deserialize<StackFrameInfo>(BridgeJson.Serialize(frame));
        Assert.Equal(0, frameCopy.FrameIndex);
        Assert.Equal("MyWidget::onDataReceived()", frameCopy.FunctionName);
        Assert.Equal("D:\\src\\MyWidget.cpp", frameCopy.FileName);
        Assert.Equal(120, frameCopy.LineNumber);
        Assert.Equal(5, frameCopy.ColumnNumber);
        Assert.True(frameCopy.UserCode);
        Assert.Equal("C++", frameCopy.Language);
        Assert.Equal("MyApp.exe", frameCopy.Module);

        var listReq = new DebuggerListBreakpointsRequest
        {
            FilePath = "D:\\src\\MyWidget.cpp",
            EnabledOnly = true,
            VsInstanceId = "vs-1"
        };
        var listReqCopy = BridgeJson.Deserialize<DebuggerListBreakpointsRequest>(BridgeJson.Serialize(listReq));
        Assert.Equal("D:\\src\\MyWidget.cpp", listReqCopy.FilePath);
        Assert.True(listReqCopy.EnabledOnly);
        Assert.Equal("vs-1", listReqCopy.VsInstanceId);

        var listResp = new DebuggerListBreakpointsResponse
        {
            VsInstanceId = "vs-1",
            TotalCount = 1,
            Breakpoints = new List<BreakpointInfo>
            {
                new()
                {
                    Id = "D:\\src\\MyWidget.cpp:120",
                    FilePath = "D:\\src\\MyWidget.cpp",
                    Line = 120,
                    Column = 1,
                    Condition = "value > 0",
                    ConditionType = "whenTrue",
                    HitCountTarget = 5,
                    HitCountType = "equal",
                    CurrentHitCount = 2,
                    Enabled = true,
                    IsBound = true
                }
            }
        };
        var listRespCopy = BridgeJson.Deserialize<DebuggerListBreakpointsResponse>(BridgeJson.Serialize(listResp));
        Assert.Equal("vs-1", listRespCopy.VsInstanceId);
        Assert.Equal(1, listRespCopy.TotalCount);
        Assert.Single(listRespCopy.Breakpoints);
        Assert.Equal("D:\\src\\MyWidget.cpp:120", listRespCopy.Breakpoints[0].Id);
        Assert.Equal("value > 0", listRespCopy.Breakpoints[0].Condition);
        Assert.Equal(2, listRespCopy.Breakpoints[0].CurrentHitCount);
        Assert.True(listRespCopy.Breakpoints[0].IsBound);

        var clearReq = new DebuggerClearBreakpointsRequest
        {
            ClearAll = false,
            FilePath = "D:\\src\\MyWidget.cpp",
            Line = 120,
            BreakpointId = "D:\\src\\MyWidget.cpp:120",
            VsInstanceId = "vs-1"
        };
        var clearReqCopy = BridgeJson.Deserialize<DebuggerClearBreakpointsRequest>(BridgeJson.Serialize(clearReq));
        Assert.False(clearReqCopy.ClearAll);
        Assert.Equal("D:\\src\\MyWidget.cpp", clearReqCopy.FilePath);
        Assert.Equal(120, clearReqCopy.Line);
        Assert.Equal("D:\\src\\MyWidget.cpp:120", clearReqCopy.BreakpointId);

        var clearResp = new DebuggerClearBreakpointsResponse
        {
            VsInstanceId = "vs-1",
            ClearedCount = 1,
            RemainingCount = 0,
            Warnings = new List<BridgeWarning>
            {
                new() { Code = "warn1", Message = "test warning" }
            }
        };
        var clearRespCopy = BridgeJson.Deserialize<DebuggerClearBreakpointsResponse>(BridgeJson.Serialize(clearResp));
        Assert.Equal(1, clearRespCopy.ClearedCount);
        Assert.Equal(0, clearRespCopy.RemainingCount);
        Assert.Single(clearRespCopy.Warnings);

        var toggleReq = new DebuggerToggleBreakpointRequest
        {
            BreakpointId = "D:\\src\\MyWidget.cpp:120",
            FilePath = "D:\\src\\MyWidget.cpp",
            Line = 120,
            Enabled = false,
            VsInstanceId = "vs-1"
        };
        var toggleReqCopy = BridgeJson.Deserialize<DebuggerToggleBreakpointRequest>(BridgeJson.Serialize(toggleReq));
        Assert.Equal("D:\\src\\MyWidget.cpp:120", toggleReqCopy.BreakpointId);
        Assert.False(toggleReqCopy.Enabled);

        var toggleResp = new DebuggerToggleBreakpointResponse
        {
            VsInstanceId = "vs-1",
            MatchedCount = 1,
            Breakpoints = new List<BreakpointInfo>
            {
                new()
                {
                    Id = "D:\\src\\MyWidget.cpp:120",
                    FilePath = "D:\\src\\MyWidget.cpp",
                    Line = 120,
                    Enabled = false
                }
            }
        };
        var toggleRespCopy = BridgeJson.Deserialize<DebuggerToggleBreakpointResponse>(BridgeJson.Serialize(toggleResp));
        Assert.Equal(1, toggleRespCopy.MatchedCount);
        Assert.False(toggleRespCopy.Breakpoints[0].Enabled);
    }
}
