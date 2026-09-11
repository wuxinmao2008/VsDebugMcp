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

    [Fact]
    public void Phase5BSolutionConfigurationAndOutputPanesRoundTripThroughSharedSerializer()
    {
        var setCfgReq = new SetSolutionConfigurationRequest
        {
            Configuration = "Release",
            Platform = "x64",
            VsInstanceId = "vs-1"
        };
        var setCfgReqCopy = BridgeJson.Deserialize<SetSolutionConfigurationRequest>(BridgeJson.Serialize(setCfgReq));
        Assert.Equal("Release", setCfgReqCopy.Configuration);
        Assert.Equal("x64", setCfgReqCopy.Platform);
        Assert.Equal("vs-1", setCfgReqCopy.VsInstanceId);

        var setCfgResp = new SetSolutionConfigurationResponse
        {
            VsInstanceId = "vs-1",
            Success = true,
            PreviousConfiguration = "Debug",
            PreviousPlatform = "x64",
            ActiveConfiguration = "Release",
            ActivePlatform = "x64"
        };
        var setCfgRespCopy = BridgeJson.Deserialize<SetSolutionConfigurationResponse>(BridgeJson.Serialize(setCfgResp));
        Assert.True(setCfgRespCopy.Success);
        Assert.Equal("Debug", setCfgRespCopy.PreviousConfiguration);
        Assert.Equal("Release", setCfgRespCopy.ActiveConfiguration);

        var panesReq = new GetOutputPanesRequest { VsInstanceId = "vs-1" };
        var panesReqCopy = BridgeJson.Deserialize<GetOutputPanesRequest>(BridgeJson.Serialize(panesReq));
        Assert.Equal("vs-1", panesReqCopy.VsInstanceId);

        var panesResp = new GetOutputPanesResponse
        {
            VsInstanceId = "vs-1",
            TotalCount = 3,
            Panes = new List<OutputPaneInfo>
            {
                new() { Name = "Build", Guid = "1bd8a850-02d1-11d1-bee7-00a0c913d1f8", IsBuiltIn = true },
                new() { Name = "Debug", Guid = "fc076020-078a-11d1-a7df-00a0c9110051", IsBuiltIn = true },
                new() { Name = "UILOG", Guid = "c4b4d682-1678-4eb1-9988-66236bdf483b", IsBuiltIn = false }
            }
        };
        var panesRespCopy = BridgeJson.Deserialize<GetOutputPanesResponse>(BridgeJson.Serialize(panesResp));
        Assert.Equal(3, panesRespCopy.TotalCount);
        Assert.Equal("Build", panesRespCopy.Panes[0].Name);
        Assert.True(panesRespCopy.Panes[1].IsBuiltIn);
        Assert.False(panesRespCopy.Panes[2].IsBuiltIn);

        var evalResp = new DebuggerEvaluateExprResponse
        {
            VsInstanceId = "vs-1",
            Expression = "m_plcDriver",
            Value = "<optimized away>",
            Type = "QModbusRtuSerialMaster*",
            IsValid = false,
            FrameIndex = 0,
            Warnings = new List<BridgeWarning>
            {
                new()
                {
                    Code = "variable_optimized_in_release",
                    Message = "Consider switching to Debug configuration."
                }
            }
        };
        var evalRespCopy = BridgeJson.Deserialize<DebuggerEvaluateExprResponse>(BridgeJson.Serialize(evalResp));
        Assert.Equal("m_plcDriver", evalRespCopy.Expression);
        Assert.Equal("<optimized away>", evalRespCopy.Value);
        Assert.False(evalRespCopy.IsValid);
        Assert.Single(evalRespCopy.Warnings);
        Assert.Equal("variable_optimized_in_release", evalRespCopy.Warnings[0].Code);
    }

    [Fact]
    public void Phase5CContractsRoundTripThroughSharedSerializer()
    {
        var attachReq = new DebuggerAttachRequest
        {
            ProcessId = 12345,
            ProcessName = "IndustrialService.exe",
            Engines = new List<string> { "Native", "Managed" },
            WaitForBreak = true,
            BreakTimeoutMs = 5000
        };
        var attachReqCopy = BridgeJson.Deserialize<DebuggerAttachRequest>(BridgeJson.Serialize(attachReq));
        Assert.Equal(12345, attachReqCopy.ProcessId);
        Assert.Equal("IndustrialService.exe", attachReqCopy.ProcessName);
        Assert.NotNull(attachReqCopy.Engines);
        Assert.Equal(2, attachReqCopy.Engines.Count);
        Assert.Equal("Native", attachReqCopy.Engines[0]);
        Assert.Equal("Managed", attachReqCopy.Engines[1]);
        Assert.True(attachReqCopy.WaitForBreak);
        Assert.Equal(5000, attachReqCopy.BreakTimeoutMs);

        var attachResp = new DebuggerAttachResponse
        {
            VsInstanceId = "vs-1",
            ProcessId = 12345,
            ProcessName = "IndustrialService.exe",
            CurrentMode = "running",
            IsDebugging = true,
            AttachedEngines = new List<string> { "Native", "Managed (CoreCLR)" }
        };
        var attachRespCopy = BridgeJson.Deserialize<DebuggerAttachResponse>(BridgeJson.Serialize(attachResp));
        Assert.Equal(12345, attachRespCopy.ProcessId);
        Assert.Equal(2, attachRespCopy.AttachedEngines.Count);
        Assert.Equal("Native", attachRespCopy.AttachedEngines[0]);
        Assert.Equal("Managed (CoreCLR)", attachRespCopy.AttachedEngines[1]);

        var findReq = new DebuggerFindSolutionProcessesRequest
        {
            StartupOnly = true,
            VsInstanceId = "vs-1"
        };
        var findReqCopy = BridgeJson.Deserialize<DebuggerFindSolutionProcessesRequest>(BridgeJson.Serialize(findReq));
        Assert.True(findReqCopy.StartupOnly);
        Assert.Equal("vs-1", findReqCopy.VsInstanceId);

        var findResp = new DebuggerFindSolutionProcessesResponse
        {
            VsInstanceId = "vs-1",
            TotalCount = 2,
            Processes = new List<SolutionProcessInfo>
            {
                new()
                {
                    ProcessId = 1001,
                    ProcessName = "PlcMaster.exe",
                    ProjectName = "PlcMaster",
                    ProjectFilePath = @"C:\src\PlcMaster\PlcMaster.csproj",
                    IsStartupProject = true,
                    IsBeingDebugged = false,
                    UserName = @"WORKGROUP\Operator"
                },
                new()
                {
                    ProcessId = 1002,
                    ProcessName = "NativeDriver.exe",
                    ProjectName = "NativeDriver",
                    ProjectFilePath = @"C:\src\NativeDriver\NativeDriver.vcxproj",
                    IsStartupProject = false,
                    IsBeingDebugged = true,
                    UserName = @"WORKGROUP\Operator"
                }
            }
        };
        var findRespCopy = BridgeJson.Deserialize<DebuggerFindSolutionProcessesResponse>(BridgeJson.Serialize(findResp));
        Assert.Equal(2, findRespCopy.TotalCount);
        Assert.Equal("PlcMaster.exe", findRespCopy.Processes[0].ProcessName);
        Assert.True(findRespCopy.Processes[0].IsStartupProject);
        Assert.False(findRespCopy.Processes[0].IsBeingDebugged);
        Assert.Equal("NativeDriver.exe", findRespCopy.Processes[1].ProcessName);
        Assert.False(findRespCopy.Processes[1].IsStartupProject);
        Assert.True(findRespCopy.Processes[1].IsBeingDebugged);

        var autoReq = new DebuggerAutoAttachRequest
        {
            StartupOnly = false,
            ProcessNames = new List<string> { "PlcMaster", "NativeDriver" },
            Engines = new List<string> { "Native", "Managed" },
            WaitForBreak = false,
            BreakTimeoutMs = 3000,
            VsInstanceId = "vs-1"
        };
        var autoReqCopy = BridgeJson.Deserialize<DebuggerAutoAttachRequest>(BridgeJson.Serialize(autoReq));
        Assert.False(autoReqCopy.StartupOnly);
        Assert.NotNull(autoReqCopy.ProcessNames);
        Assert.Equal(2, autoReqCopy.ProcessNames.Count);
        Assert.NotNull(autoReqCopy.Engines);
        Assert.Equal(2, autoReqCopy.Engines.Count);

        var autoResp = new DebuggerAutoAttachResponse
        {
            VsInstanceId = "vs-1",
            AttachedCount = 1,
            Processes = new List<DebuggerAttachResponse> { attachResp }
        };
        var autoRespCopy = BridgeJson.Deserialize<DebuggerAutoAttachResponse>(BridgeJson.Serialize(autoResp));
        Assert.Equal(1, autoRespCopy.AttachedCount);
        Assert.Single(autoRespCopy.Processes);
        Assert.Equal("IndustrialService.exe", autoRespCopy.Processes[0].ProcessName);

        Assert.Equal("engine_not_found", BridgeErrorCodes.EngineNotFound);
        Assert.Equal("no_solution_processes_found", BridgeErrorCodes.NoSolutionProcessesFound);
    }

    [Fact]
    public void Phase5DContractsRoundTripThroughSharedSerializer()
    {
        var snapReq = new DebuggerGetSnapshotRequest
        {
            IncludeCallStack = true,
            MaxFrames = 5,
            IncludeLocals = true,
            MaxLocals = 20,
            IncludeRecentLogs = true,
            RecentLogLines = 25,
            LogSource = "debug",
            VsInstanceId = "vs-1"
        };
        var snapReqCopy = BridgeJson.Deserialize<DebuggerGetSnapshotRequest>(BridgeJson.Serialize(snapReq));
        Assert.True(snapReqCopy.IncludeCallStack);
        Assert.Equal(5, snapReqCopy.MaxFrames);
        Assert.True(snapReqCopy.IncludeLocals);
        Assert.Equal(20, snapReqCopy.MaxLocals);
        Assert.True(snapReqCopy.IncludeRecentLogs);
        Assert.Equal(25, snapReqCopy.RecentLogLines);
        Assert.Equal("debug", snapReqCopy.LogSource);
        Assert.Equal("vs-1", snapReqCopy.VsInstanceId);

        var snapResp = new DebuggerGetSnapshotResponse
        {
            VsInstanceId = "vs-1",
            Mode = "break",
            IsDebugging = true,
            CurrentProcessId = 5555,
            CurrentProcessName = "PlcWorker.exe",
            CurrentThreadId = 1,
            CurrentThreadName = "Main Thread",
            LastBreakReason = "breakpoint",
            TopFrame = new StackFrameInfo
            {
                FrameIndex = 0,
                FunctionName = "ModbusHandler.ProcessPacket",
                FileName = @"C:\src\ModbusHandler.cpp",
                LineNumber = 128
            },
            CallStack = new List<StackFrameInfo>
            {
                new() { FrameIndex = 0, FunctionName = "ModbusHandler.ProcessPacket", LineNumber = 128 }
            },
            Locals = new List<DebuggerVariableInfo>
            {
                new() { Name = "pBuffer", Value = "0x00007FFE12345678", Type = "uint8_t*", IsArgument = false }
            },
            RecentLogs = "[INFO] Modbus RX 16 bytes: 01 03 00 00 00 02 C4 0B\n",
            LogSource = "debug",
            Warnings = new List<BridgeWarning>
            {
                new() { Code = "test_warning", Message = "Snapshot OK" }
            }
        };
        var snapRespCopy = BridgeJson.Deserialize<DebuggerGetSnapshotResponse>(BridgeJson.Serialize(snapResp));
        Assert.Equal("vs-1", snapRespCopy.VsInstanceId);
        Assert.Equal("break", snapRespCopy.Mode);
        Assert.True(snapRespCopy.IsDebugging);
        Assert.Equal(5555, snapRespCopy.CurrentProcessId);
        Assert.NotNull(snapRespCopy.TopFrame);
        Assert.Equal("ModbusHandler.ProcessPacket", snapRespCopy.TopFrame.FunctionName);
        Assert.Single(snapRespCopy.CallStack);
        Assert.Single(snapRespCopy.Locals);
        Assert.NotNull(snapRespCopy.RecentLogs);
        Assert.Contains("Modbus RX", snapRespCopy.RecentLogs);
        Assert.Single(snapRespCopy.Warnings);

        var memReq = new DebuggerReadMemoryRequest
        {
            Address = "0x00007FFE12345678",
            ByteCount = 16,
            VsInstanceId = "vs-1"
        };
        var memReqCopy = BridgeJson.Deserialize<DebuggerReadMemoryRequest>(BridgeJson.Serialize(memReq));
        Assert.Equal("0x00007FFE12345678", memReqCopy.Address);
        Assert.Equal(16, memReqCopy.ByteCount);
        Assert.Equal("vs-1", memReqCopy.VsInstanceId);

        var memResp = new DebuggerReadMemoryResponse
        {
            VsInstanceId = "vs-1",
            ProcessId = 5555,
            ResolvedAddress = "0x00007FFE12345678",
            ByteCount = 4,
            HexBytes = "01 03 00 00",
            HexDump = "00007FFE12345678  01 03 00 00                                      |....|",
            AsciiRepresentation = "....",
            Base64Data = "AQMAAA==",
            Warnings = new List<BridgeWarning>()
        };
        var memRespCopy = BridgeJson.Deserialize<DebuggerReadMemoryResponse>(BridgeJson.Serialize(memResp));
        Assert.Equal(5555, memRespCopy.ProcessId);
        Assert.Equal("0x00007FFE12345678", memRespCopy.ResolvedAddress);
        Assert.Equal(4, memRespCopy.ByteCount);
        Assert.Equal("01 03 00 00", memRespCopy.HexBytes);
        Assert.Equal("AQMAAA==", memRespCopy.Base64Data);

        Assert.Equal("invalid_memory_address", BridgeErrorCodes.InvalidMemoryAddress);
        Assert.Equal("memory_read_failed", BridgeErrorCodes.MemoryReadFailed);
        Assert.Equal("privacy_risk_aborted", BridgeErrorCodes.PrivacyRiskAborted);

        var issueReq = new ReportMcpIssueRequest
        {
            TargetTool = "vs_debugger_evaluate_expr",
            IssueType = McpIssueTypes.TransportTimeout,
            AgentSummary = "Named pipe timed out while evaluating a deep collection.",
            SuggestedImprovement = "Introduce progressive evaluation.",
            VsInstanceId = "vs-1"
        };
        var issueReqCopy = BridgeJson.Deserialize<ReportMcpIssueRequest>(BridgeJson.Serialize(issueReq));
        Assert.Equal("vs_debugger_evaluate_expr", issueReqCopy.TargetTool);
        Assert.Equal(McpIssueTypes.TransportTimeout, issueReqCopy.IssueType);
        Assert.Equal("Named pipe timed out while evaluating a deep collection.", issueReqCopy.AgentSummary);
        Assert.Equal("Introduce progressive evaluation.", issueReqCopy.SuggestedImprovement);
        Assert.Equal("vs-1", issueReqCopy.VsInstanceId);

        var issueResp = new ReportMcpIssueResponse
        {
            Status = "ready_for_user_submission",
            ReportId = "rpt_20260910_01",
            GithubIssueUrl = "https://github.com/wuxinmao2008/VsDebugMcp/issues/new?template=tool-friction.yml",
            LocalReportPath = @"%LOCALAPPDATA%\VsDebugMcp\reports\rpt_20260910_01.md",
            InstructionsForAgent = "Present the github_issue_url as a clickable link.",
            EnvironmentSummary = new Dictionary<string, string>
            {
                ["OS"] = "Windows 11",
                ["VS"] = "18.2.0"
            },
            Warnings = new List<BridgeWarning>()
        };
        var issueRespCopy = BridgeJson.Deserialize<ReportMcpIssueResponse>(BridgeJson.Serialize(issueResp));
        Assert.Equal("ready_for_user_submission", issueRespCopy.Status);
        Assert.Equal("rpt_20260910_01", issueRespCopy.ReportId);
        Assert.NotNull(issueRespCopy.GithubIssueUrl);
        Assert.Equal(@"%LOCALAPPDATA%\VsDebugMcp\reports\rpt_20260910_01.md", issueRespCopy.LocalReportPath);
        Assert.Equal("Windows 11", issueRespCopy.EnvironmentSummary["OS"]);

        Assert.Equal("internal_exception", McpIssueTypes.Normalize("INTERNAL_EXCEPTION"));
        Assert.Equal("transport_timeout", McpIssueTypes.Normalize(" transport_timeout "));
        Assert.Equal("unknown", McpIssueTypes.Normalize("some_random_invalid_type"));
        Assert.Equal("unknown", McpIssueTypes.Normalize(null));
        Assert.Equal(11, McpIssueTypes.All.Count);
    }

    [Fact]
    public void Phase6AContracts_SerializeAndDeserialize_Correctly()
    {
        // 1. BuildStates and TestRunStates IsTerminal
        Assert.True(BuildStates.IsTerminal(BuildStates.Succeeded));
        Assert.True(BuildStates.IsTerminal(BuildStates.Failed));
        Assert.True(BuildStates.IsTerminal(BuildStates.Cancelled));
        Assert.False(BuildStates.IsTerminal(BuildStates.Starting));
        Assert.False(BuildStates.IsTerminal(BuildStates.Running));
        Assert.False(BuildStates.IsTerminal(null));

        Assert.True(TestRunStates.IsTerminal(TestRunStates.Completed));
        Assert.True(TestRunStates.IsTerminal(TestRunStates.Failed));
        Assert.True(TestRunStates.IsTerminal(TestRunStates.Cancelled));
        Assert.False(TestRunStates.IsTerminal(TestRunStates.Starting));
        Assert.False(TestRunStates.IsTerminal(TestRunStates.Running));
        Assert.False(TestRunStates.IsTerminal(null));

        // 2. BuildTaskResponse with wait fields
        var buildResp = new BuildTaskResponse
        {
            BuildTaskId = "bld_1",
            VsInstanceId = "vs-1",
            State = BuildStates.Failed,
            Succeeded = false,
            DurationSeconds = 12.34,
            ErrorCount = 2,
            TopErrors = new List<string> { "foo.cpp(42): C2065 undeclared identifier", "bar.cpp(10): C2143 syntax error" }
        };
        var buildRespCopy = BridgeJson.Deserialize<BuildTaskResponse>(BridgeJson.Serialize(buildResp));
        Assert.Equal("bld_1", buildRespCopy.BuildTaskId);
        Assert.Equal(12.34, buildRespCopy.DurationSeconds);
        Assert.Equal(2, buildRespCopy.ErrorCount);
        Assert.NotNull(buildRespCopy.TopErrors);
        Assert.Equal(2, buildRespCopy.TopErrors.Count);
        Assert.Contains("foo.cpp", buildRespCopy.TopErrors[0]);

        // 3. RunTestsResponse with wait fields
        var testResp = new RunTestsResponse
        {
            VsInstanceId = "vs-1",
            TestRunId = "run_1",
            State = TestRunStates.Completed,
            TotalCount = 10,
            PassedCount = 8,
            FailedCount = 2,
            SkippedCount = 0,
            DurationSeconds = 5.67,
            FailedTestNames = new List<string> { "TestA", "TestB" }
        };
        var testRespCopy = BridgeJson.Deserialize<RunTestsResponse>(BridgeJson.Serialize(testResp));
        Assert.Equal(8, testRespCopy.PassedCount);
        Assert.Equal(2, testRespCopy.FailedCount);
        Assert.Equal(5.67, testRespCopy.DurationSeconds);
        Assert.Equal(2, testRespCopy.FailedTestNames?.Count);

        // 4. DebuggerGetExceptionInfoResponse with assertion fields
        var excResp = new DebuggerGetExceptionInfoResponse
        {
            VsInstanceId = "vs-1",
            HasException = true,
            ExceptionType = "AssertionFailure",
            Message = "Assertion failed: ptr != nullptr",
            AssertionFailed = true,
            AssertionExpression = "ptr != nullptr",
            AssertionFile = @"D:\Project\main.cpp",
            AssertionLine = 128
        };
        var excRespCopy = BridgeJson.Deserialize<DebuggerGetExceptionInfoResponse>(BridgeJson.Serialize(excResp));
        Assert.True(excRespCopy.HasException);
        Assert.True(excRespCopy.AssertionFailed);
        Assert.Equal("ptr != nullptr", excRespCopy.AssertionExpression);
        Assert.Equal(@"D:\Project\main.cpp", excRespCopy.AssertionFile);
        Assert.Equal(128, excRespCopy.AssertionLine);

        // 5. DebuggerGetSnapshotResponse with aggregated exception and thread fields
        var snapReq = new DebuggerGetSnapshotRequest
        {
            IncludeExceptionInfo = true,
            IncludeThreads = true,
            MaxThreads = 15
        };
        var snapReqCopy = BridgeJson.Deserialize<DebuggerGetSnapshotRequest>(BridgeJson.Serialize(snapReq));
        Assert.True(snapReqCopy.IncludeExceptionInfo);
        Assert.True(snapReqCopy.IncludeThreads);
        Assert.Equal(15, snapReqCopy.MaxThreads);

        var snapResp = new DebuggerGetSnapshotResponse
        {
            VsInstanceId = "vs-1",
            Mode = "break",
            IsDebugging = true,
            ExceptionInfo = excResp,
            TotalThreadCount = 4,
            Threads = new List<ThreadInfo>
            {
                new() { Id = 100, Name = "Main Thread", IsCurrent = true, IsAlive = true },
                new() { Id = 101, Name = "Worker Thread", IsCurrent = false, IsAlive = true }
            }
        };
        var snapRespCopy = BridgeJson.Deserialize<DebuggerGetSnapshotResponse>(BridgeJson.Serialize(snapResp));
        Assert.NotNull(snapRespCopy.ExceptionInfo);
        Assert.True(snapRespCopy.ExceptionInfo.AssertionFailed);
        Assert.Equal(4, snapRespCopy.TotalThreadCount);
        Assert.NotNull(snapRespCopy.Threads);
        Assert.Equal(2, snapRespCopy.Threads.Count);
        Assert.Equal("Main Thread", snapRespCopy.Threads[0].Name);
    }
}
