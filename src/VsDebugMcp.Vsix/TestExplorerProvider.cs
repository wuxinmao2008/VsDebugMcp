using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TestWindow.Extensibility;
using VsDebugMcp.Protocol;

namespace VsDebugMcp_Vsix;

internal sealed class TestExplorerProvider : IDisposable
{
    private static readonly Guid TestWindowPackageGuid = new("BFC24BF4-B994-4757-BCDC-1D5D2768BF29");
    private readonly AsyncPackage _package;
    private readonly string _vsInstanceId;
    private readonly object _sync = new();

    private IComponentModel? _componentModel;
    private TestsServiceInvoker? _testsService;
    private IOperationState? _operationState;
    private bool _subscribedToOperationState;
    private bool _disposed;

    private ActiveTestRunContext? _activeRun;
    private ActiveTestRunContext? _lastRun;

    public TestExplorerProvider(AsyncPackage package, string vsInstanceId)
    {
        _package = package;
        _vsInstanceId = vsInstanceId;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        await EnsureServicesAsync(cancellationToken);
    }

    private async Task<TestsServiceInvoker> EnsureServicesAsync(CancellationToken cancellationToken)
    {
        if (_testsService != null)
        {
            return _testsService;
        }

        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        // Preload TestWindowPackage if needed
        var shell = await _package.GetServiceAsync(typeof(SVsShell)) as IVsShell7;
        if (shell != null)
        {
            try
            {
                await shell.LoadPackageAsync(TestWindowPackageGuid);
            }
            catch
            {
                // Best-effort package preload
            }
        }

        var uiShell = await _package.GetServiceAsync(typeof(SVsUIShell)) as IVsUIShell;
        if (uiShell != null)
        {
            try
            {
                var toolWindowGuid = new Guid("E1B7D1F8-9B3C-49B1-8F4F-BFC63A88835D");
                uiShell.FindToolWindow((uint)__VSFINDTOOLWIN.FTW_fForceCreate, ref toolWindowGuid, out var windowFrame);
                if (windowFrame != null)
                {
                    windowFrame.ShowNoActivate();
                }
            }
            catch
            {
            }
        }

        _componentModel = await _package.GetServiceAsync(typeof(SComponentModel)) as IComponentModel;
        if (_componentModel == null)
        {
            throw new TestExplorerProviderException(
                BridgeErrorCodes.TestWindowUnavailable,
                "Visual Studio component model is unavailable.",
                true);
        }

        var testServiceType = typeof(IOperationState).Assembly.GetType("Microsoft.VisualStudio.TestWindow.Extensibility.ITestsService");
        if (testServiceType == null)
        {
            throw new TestExplorerProviderException(
                BridgeErrorCodes.TestWindowUnavailable,
                "Test Explorer service type (ITestsService) could not be resolved.",
                true);
        }

        object? rawService = null;
        try
        {
            rawService = _componentModel.DefaultExportProvider.GetExportedValue<object>("Microsoft.VisualStudio.TestWindow.Extensibility.ITestsService");
        }
        catch
        {
            var getServiceMethod = typeof(IComponentModel).GetMethod("GetService", Type.EmptyTypes)?.MakeGenericMethod(testServiceType);
            rawService = getServiceMethod?.Invoke(_componentModel, null);
        }

        if (rawService == null)
        {
            throw new TestExplorerProviderException(
                BridgeErrorCodes.TestWindowUnavailable,
                "Test Explorer service (ITestsService) is not available from MEF.",
                true);
        }

        _testsService = new TestsServiceInvoker(rawService);

        if (!_subscribedToOperationState)
        {
            _operationState = _componentModel.GetService<IOperationState>();
            if (_operationState != null)
            {
                _operationState.StateChanged += OnOperationStateChanged;
                _subscribedToOperationState = true;
            }
        }

        return _testsService;
    }

    private void OnOperationStateChanged(object? sender, OperationStateChangedEventArgs e)
    {
        var finished = e.State.HasFlag(TestOperationStates.TestExecutionFinished)
            || e.State.HasFlag(TestOperationStates.TestExecutionCancelAndFinished)
            || (e.State & TestOperationStates.Finished) != 0
            || (int)e.State == (int)TestOperationStates.OperationSetFinished;

        if (finished)
        {
            lock (_sync)
            {
                if (_activeRun != null && _activeRun.State == TestRunStates.Running)
                {
                    var run = _activeRun;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(500);
                            await FinalizeRunAsync(run);
                        }
                        catch
                        {
                        }
                    });
                }
            }
        }
    }

    public async Task<GetTestsResponse> GetTestsAsync(GetTestsRequest request, CancellationToken cancellationToken)
    {
        var testsService = await EnsureServicesAsync(cancellationToken);
        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var rawTests = await testsService.GetTestsAsync();

        var items = new List<VsTestItem>();
        if (rawTests != null)
        {
            foreach (var test in rawTests)
            {
                var source = TestAccessor.GetSource(test) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(request.ProjectName))
                {
                    if (source.IndexOf(request.ProjectName, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                }

                var displayName = TestAccessor.GetDisplayName(test);
                var fqn = TestAccessor.GetFullyQualifiedName(test);
                if (!string.IsNullOrWhiteSpace(request.Filter))
                {
                    var matchDisplay = displayName.IndexOf(request.Filter, StringComparison.OrdinalIgnoreCase) >= 0;
                    var matchFqn = fqn.IndexOf(request.Filter, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!matchDisplay && !matchFqn)
                    {
                        continue;
                    }
                }

                string? lastError = null;
                var results = TestAccessor.GetResults(test);
                if (results != null)
                {
                    foreach (var r in results)
                    {
                        var msg = ResultAccessor.GetErrorMessage(r);
                        if (!string.IsNullOrWhiteSpace(msg))
                        {
                            lastError = msg;
                            break;
                        }
                    }
                }

                items.Add(new VsTestItem
                {
                    TestId = TestAccessor.GetId(test).ToString(),
                    DisplayName = displayName,
                    FullyQualifiedName = fqn,
                    FilePath = TestAccessor.GetFilePath(test),
                    LineNumber = TestAccessor.GetLineNumber(test),
                    ProjectId = TestAccessor.GetProjectId(test),
                    Source = string.IsNullOrEmpty(source) ? null : source,
                    State = TestAccessor.GetState(test),
                    DurationMs = TestAccessor.GetDurationMs(test),
                    LastErrorMessage = lastError
                });
            }
        }

        return new GetTestsResponse
        {
            VsInstanceId = _vsInstanceId,
            Tests = items,
            TotalCount = items.Count
        };
    }

    public async Task<RunTestsResponse> RunTestsAsync(RunTestsRequest request, CancellationToken cancellationToken)
    {
        var testsService = await EnsureServicesAsync(cancellationToken);
        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        List<Guid> targetGuids = new();
        if (request.TestIds != null && request.TestIds.Count > 0)
        {
            foreach (var idStr in request.TestIds)
            {
                if (Guid.TryParse(idStr, out var g))
                {
                    targetGuids.Add(g);
                }
            }
        }
        else
        {
            var allTests = await testsService.GetTestsAsync();
            if (allTests != null)
            {
                foreach (var t in allTests)
                {
                    targetGuids.Add(TestAccessor.GetId(t));
                }
            }
        }

        var runId = "testrun-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var now = DateTimeOffset.UtcNow;

        var runContext = new ActiveTestRunContext(runId, targetGuids, now);

        lock (_sync)
        {
            if (_activeRun != null && (_activeRun.State == TestRunStates.Starting || _activeRun.State == TestRunStates.Running))
            {
                throw new TestExplorerProviderException(
                    BridgeErrorCodes.TestRunBusy,
                    $"A test run '{_activeRun.TestRunId}' is already in progress.",
                    false);
            }

            _activeRun = runContext;
            _lastRun = runContext;
        }

        if (targetGuids.Count == 0)
        {
            runContext.State = TestRunStates.Completed;
            runContext.CompletedAt = DateTimeOffset.UtcNow;
            return new RunTestsResponse
            {
                VsInstanceId = _vsInstanceId,
                TestRunId = runId,
                State = TestRunStates.Completed,
                TotalCount = 0,
                StartedAt = now.ToString("O")
            };
        }

        runContext.State = TestRunStates.Running;

        // Trigger test execution asynchronously on UI thread
        _ = _package.JoinableTaskFactory.RunAsync(async () =>
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
            try
            {
                await testsService.RunTestsAsync(targetGuids);
                await Task.Delay(1000);
                await FinalizeRunAsync(runContext);
            }
            catch (Exception)
            {
                lock (_sync)
                {
                    runContext.State = TestRunStates.Failed;
                    runContext.CompletedAt = DateTimeOffset.UtcNow;
                }
            }
        });

        return new RunTestsResponse
        {
            VsInstanceId = _vsInstanceId,
            TestRunId = runId,
            State = TestRunStates.Running,
            TotalCount = targetGuids.Count,
            StartedAt = now.ToString("O")
        };
    }

    public async Task<TestRunStatusResponse> GetTestRunStatusAsync(GetTestRunStatusRequest request, CancellationToken cancellationToken)
    {
        ActiveTestRunContext? run;
        lock (_sync)
        {
            if (!string.IsNullOrWhiteSpace(request.TestRunId))
            {
                if (_activeRun != null && string.Equals(_activeRun.TestRunId, request.TestRunId, StringComparison.OrdinalIgnoreCase))
                {
                    run = _activeRun;
                }
                else if (_lastRun != null && string.Equals(_lastRun.TestRunId, request.TestRunId, StringComparison.OrdinalIgnoreCase))
                {
                    run = _lastRun;
                }
                else
                {
                    throw new TestExplorerProviderException(
                        BridgeErrorCodes.TestRunNotFound,
                        $"Test run '{request.TestRunId}' was not found.",
                        false);
                }
            }
            else
            {
                run = _activeRun ?? _lastRun;
                if (run == null)
                {
                    throw new TestExplorerProviderException(
                        BridgeErrorCodes.TestRunNotFound,
                        "No active or previous test run was found.",
                        false);
                }
            }
        }

        // If currently running, do a live refresh of results
        if (run.State == TestRunStates.Running)
        {
            try
            {
                await RefreshRunResultsAsync(run, cancellationToken);
            }
            catch
            {
            }
        }

        var durationMs = ((run.CompletedAt ?? DateTimeOffset.UtcNow) - run.StartedAt).TotalMilliseconds;
        if (durationMs < 0) durationMs = 0;

        return new TestRunStatusResponse
        {
            VsInstanceId = _vsInstanceId,
            TestRunId = run.TestRunId,
            State = run.State,
            TotalCount = run.TotalCount,
            PassedCount = run.PassedCount,
            FailedCount = run.FailedCount,
            SkippedCount = run.SkippedCount,
            DurationMs = durationMs,
            Results = run.GetResultsSnapshot()
        };
    }

    public async Task<CancelTestRunResponse> CancelTestRunAsync(CancelTestRunRequest request, CancellationToken cancellationToken)
    {
        ActiveTestRunContext? run;
        lock (_sync)
        {
            if (!string.IsNullOrWhiteSpace(request.TestRunId))
            {
                if (_activeRun != null && string.Equals(_activeRun.TestRunId, request.TestRunId, StringComparison.OrdinalIgnoreCase))
                {
                    run = _activeRun;
                }
                else
                {
                    throw new TestExplorerProviderException(
                        BridgeErrorCodes.TestRunNotFound,
                        $"Active test run '{request.TestRunId}' was not found.",
                        false);
                }
            }
            else
            {
                run = _activeRun;
                if (run == null || run.State != TestRunStates.Running)
                {
                    throw new TestExplorerProviderException(
                        BridgeErrorCodes.TestRunNotFound,
                        "No active test run is currently running to cancel.",
                        false);
                }
            }

            run.State = TestRunStates.Cancelled;
            run.CompletedAt = DateTimeOffset.UtcNow;
            _activeRun = null;
        }

        return new CancelTestRunResponse
        {
            VsInstanceId = _vsInstanceId,
            TestRunId = run.TestRunId,
            State = TestRunStates.Cancelled,
            CancelRequested = true
        };
    }

    private async Task FinalizeRunAsync(ActiveTestRunContext run)
    {
        await RefreshRunResultsAsync(run, CancellationToken.None);
        lock (_sync)
        {
            if (run.State == TestRunStates.Running)
            {
                run.State = run.FailedCount > 0 ? TestRunStates.Failed : TestRunStates.Completed;
                run.CompletedAt = DateTimeOffset.UtcNow;
                if (_activeRun == run)
                {
                    _activeRun = null;
                }
            }
        }
    }

    private async Task RefreshRunResultsAsync(ActiveTestRunContext run, CancellationToken cancellationToken)
    {
        if (_testsService == null) return;
        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        System.Collections.IEnumerable? tests = null;
        if (run.TargetGuids != null && run.TargetGuids.Count > 0)
        {
            try
            {
                tests = await _testsService.GetTestsAsync(run.TargetGuids);
            }
            catch
            {
            }
        }

        if (tests == null)
        {
            tests = await _testsService.GetTestsAsync();
        }

        if (tests == null) return;

        var results = new List<VsTestResult>();
        int passed = 0;
        int failed = 0;
        int skipped = 0;

        foreach (var test in tests)
        {
            var testId = TestAccessor.GetId(test);
            if (run.TargetGuids != null && run.TargetGuids.Count > 0 && !run.TargetGuids.Contains(testId))
            {
                continue;
            }

            var outcome = TestAccessor.GetState(test);
            if (string.Equals(outcome, "Passed", StringComparison.OrdinalIgnoreCase)) passed++;
            else if (string.Equals(outcome, "Failed", StringComparison.OrdinalIgnoreCase)) failed++;
            else if (string.Equals(outcome, "Skipped", StringComparison.OrdinalIgnoreCase)) skipped++;

            string? err = null;
            string? stack = null;
            string? stdOut = null;
            string? stdErr = null;
            double dur = TestAccessor.GetDurationMs(test) ?? 0;

            var testResults = TestAccessor.GetResults(test);
            if (testResults != null)
            {
                foreach (var r in testResults)
                {
                    var msg = ResultAccessor.GetErrorMessage(r);
                    if (!string.IsNullOrWhiteSpace(msg)) err = msg;

                    var st = ResultAccessor.GetErrorStackTrace(r);
                    if (!string.IsNullOrWhiteSpace(st)) stack = st;

                    var so = ResultAccessor.GetStandardOutput(r);
                    if (!string.IsNullOrWhiteSpace(so)) stdOut = so;

                    var se = ResultAccessor.GetStandardError(r);
                    if (!string.IsNullOrWhiteSpace(se)) stdErr = se;

                    var d = ResultAccessor.GetDurationMs(r);
                    if (d > 0) dur = d;
                }
            }

            results.Add(new VsTestResult
            {
                TestId = TestAccessor.GetId(test).ToString(),
                DisplayName = TestAccessor.GetDisplayName(test),
                Outcome = outcome,
                DurationMs = dur,
                ErrorMessage = err,
                StackTrace = stack,
                StandardOutput = stdOut,
                StandardError = stdErr
            });
        }

        run.UpdateResults(results, passed, failed, skipped);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_subscribedToOperationState && _operationState != null)
        {
            try
            {
                _operationState.StateChanged -= OnOperationStateChanged;
            }
            catch
            {
            }
        }
    }

    private sealed class ActiveTestRunContext
    {
        private readonly object _lock = new();

        public ActiveTestRunContext(string testRunId, List<Guid> targetGuids, DateTimeOffset startedAt)
        {
            TestRunId = testRunId;
            TargetGuids = targetGuids;
            StartedAt = startedAt;
            TotalCount = targetGuids.Count;
            State = TestRunStates.Starting;
        }

        public string TestRunId { get; }
        public List<Guid> TargetGuids { get; }
        public DateTimeOffset StartedAt { get; }
        public DateTimeOffset? CompletedAt { get; set; }
        public int TotalCount { get; }
        public string State { get; set; }
        public int PassedCount { get; private set; }
        public int FailedCount { get; private set; }
        public int SkippedCount { get; private set; }

        private List<VsTestResult> _results = new();

        public void UpdateResults(List<VsTestResult> results, int passed, int failed, int skipped)
        {
            lock (_lock)
            {
                _results = results;
                PassedCount = passed;
                FailedCount = failed;
                SkippedCount = skipped;
            }
        }

        public List<VsTestResult> GetResultsSnapshot()
        {
            lock (_lock)
            {
                return new List<VsTestResult>(_results);
            }
        }
    }

    private sealed class TestsServiceInvoker
    {
        private readonly object _service;
        private readonly MethodInfo _getTestsMethod;
        private readonly MethodInfo _getTestsWithIdsMethod;
        private readonly MethodInfo _runTestsMethod;

        public TestsServiceInvoker(object service)
        {
            _service = service;
            var type = service.GetType();
            _getTestsMethod = type.GetMethod("GetTestsAsEnumerableAsync", Type.EmptyTypes)
                ?? throw new InvalidOperationException("GetTestsAsEnumerableAsync not found.");
            _getTestsWithIdsMethod = type.GetMethod("GetTestsAsEnumerableAsync", new[] { typeof(IEnumerable<Guid>) })
                ?? throw new InvalidOperationException("GetTestsAsEnumerableAsync(ids) not found.");
            _runTestsMethod = type.GetMethod("RunTestsAsync", new[] { typeof(IEnumerable<Guid>) })
                ?? throw new InvalidOperationException("RunTestsAsync not found.");
        }

        public async Task<System.Collections.IEnumerable?> GetTestsAsync()
        {
            var task = (Task)_getTestsMethod.Invoke(_service, null)!;
            await task.ConfigureAwait(false);
            var prop = task.GetType().GetProperty("Result");
            return (System.Collections.IEnumerable?)prop?.GetValue(task);
        }

        public async Task<System.Collections.IEnumerable?> GetTestsAsync(IEnumerable<Guid> ids)
        {
            var task = (Task)_getTestsWithIdsMethod.Invoke(_service, new object[] { ids })!;
            await task.ConfigureAwait(false);
            var prop = task.GetType().GetProperty("Result");
            return (System.Collections.IEnumerable?)prop?.GetValue(task);
        }

        public async Task<bool> RunTestsAsync(IEnumerable<Guid> ids)
        {
            var task = (Task)_runTestsMethod.Invoke(_service, new object[] { ids })!;
            await task.ConfigureAwait(false);
            var prop = task.GetType().GetProperty("Result");
            if (prop != null && prop.GetValue(task) is bool b)
            {
                return b;
            }
            return true;
        }
    }

    private static class TestAccessor
    {
        public static Guid GetId(object test) => (Guid)test.GetType().GetProperty("Id")!.GetValue(test)!;
        public static string GetDisplayName(object test) => (string?)test.GetType().GetProperty("DisplayName")?.GetValue(test) ?? string.Empty;
        public static string GetFullyQualifiedName(object test) => (string?)test.GetType().GetProperty("FullyQualifiedName")?.GetValue(test) ?? string.Empty;
        public static string? GetFilePath(object test) => (string?)test.GetType().GetProperty("FilePath")?.GetValue(test);
        public static int? GetLineNumber(object test)
        {
            var val = (int?)test.GetType().GetProperty("LineNumber")?.GetValue(test);
            return val > 0 ? val : null;
        }
        public static string? GetProjectId(object test)
        {
            var val = test.GetType().GetProperty("ProjectId")?.GetValue(test);
            if (val is Guid g && g != Guid.Empty) return g.ToString();
            return null;
        }
        public static string? GetSource(object test) => (string?)test.GetType().GetProperty("Source")?.GetValue(test);
        public static string GetState(object test) => test.GetType().GetProperty("State")?.GetValue(test)?.ToString() ?? "NotRun";
        public static double? GetDurationMs(object test)
        {
            var val = test.GetType().GetProperty("Duration")?.GetValue(test);
            if (val is TimeSpan ts && ts.TotalMilliseconds > 0) return ts.TotalMilliseconds;
            return null;
        }
        public static System.Collections.IEnumerable? GetResults(object test) =>
            (System.Collections.IEnumerable?)test.GetType().GetProperty("Results")?.GetValue(test);
    }

    private static class ResultAccessor
    {
        public static string? GetErrorMessage(object result) => (string?)result.GetType().GetProperty("ErrorMessage")?.GetValue(result);
        public static string? GetErrorStackTrace(object result) => (string?)result.GetType().GetProperty("ErrorStackTrace")?.GetValue(result);
        public static string? GetStandardOutput(object result) => (string?)result.GetType().GetProperty("StandardOutput")?.GetValue(result);
        public static string? GetStandardError(object result) => (string?)result.GetType().GetProperty("StandardError")?.GetValue(result);
        public static double GetDurationMs(object result)
        {
            var val = result.GetType().GetProperty("Duration")?.GetValue(result);
            if (val is TimeSpan ts) return ts.TotalMilliseconds;
            return 0;
        }
    }
}

internal sealed class TestExplorerProviderException : Exception
{
    public TestExplorerProviderException(string code, string message, bool retryable)
        : base(message)
    {
        Code = code;
        Retryable = retryable;
    }

    public string Code { get; }
    public bool Retryable { get; }
}
