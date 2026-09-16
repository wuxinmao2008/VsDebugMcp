using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TestWindow.Extensibility;
using VsDebugMcp.Protocol;
using VsDebugMcp_Vsix.Diagnostics;

namespace VsDebugMcp_Vsix;

internal sealed class TestExplorerProvider : IDisposable
{
    private static readonly Guid TestWindowPackageGuid = new("BFC24BF4-B994-4757-BCDC-1D5D2768BF29");
    private readonly AsyncPackage _package;
    private readonly string _vsInstanceId;
    private readonly VsDiagnosticService? _diagnostics;
    private readonly object _sync = new();

    private IComponentModel? _componentModel;
    private TestsServiceInvoker? _testsService;
    private IOperationState? _operationState;
    private OperationBrokerInvoker? _operationBroker;
    private bool _subscribedToOperationState;
    private bool _disposed;

    private readonly DebuggerProvider _debuggerProvider;
    private ActiveTestRunContext? _activeRun;
    private ActiveTestRunContext? _lastRun;
    private System.Diagnostics.Process? _activeCTestProcess;

    public TestExplorerProvider(
        AsyncPackage package,
        string vsInstanceId,
        DebuggerProvider debuggerProvider,
        VsDiagnosticService? diagnostics = null)
    {
        _package = package;
        _vsInstanceId = vsInstanceId;
        _debuggerProvider = debuggerProvider;
        _diagnostics = diagnostics;
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
                _operationBroker = new OperationBrokerInvoker(_operationState, _diagnostics);
                _operationState.StateChanged += OnOperationStateChanged;
                _subscribedToOperationState = true;
            }
        }

        return _testsService;
    }

    private void OnOperationStateChanged(object? sender, OperationStateChangedEventArgs e)
    {
        _diagnostics?.LogInfo($"[TestExplorer] OperationStateChanged: {e.State} (0x{(int)e.State:X})");

        // Specific test execution finished masks
        var finished = e.State.HasFlag(TestOperationStates.TestExecutionFinished)
            || e.State.HasFlag(TestOperationStates.TestExecutionCancelAndFinished)
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
                            // Brief delay to allow TestStore to commit completed test results
                            await Task.Delay(300);
                            await FinalizeRunAsync(run);
                        }
                        catch (Exception ex)
                        {
                            _diagnostics?.LogError($"[TestExplorer] FinalizeRunAsync error: {ex.Message}");
                        }
                    });
                }
            }
        }
    }

    public async Task<GetTestsResponse> GetTestsAsync(GetTestsRequest request, CancellationToken cancellationToken)
    {
        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        var dte = await _package.GetServiceAsync(typeof(DTE)) as DTE2;

        var items = new List<VsTestItem>();

        // 1. If CMake workspace, discover CTest tests
        if (dte != null && CMakeWorkspaceState.TryGetCMakeWorkspaceRoot(dte, out var cmakeRoot))
        {
            var ctestItems = await GetCTestTestsAsync(cmakeRoot, request.ProjectName, request.Filter, cancellationToken);
            items.AddRange(ctestItems);
        }

        // 2. Try MEF TestWindow for managed/MSBuild tests
        try
        {
            var testsService = await EnsureServicesAsync(cancellationToken);
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            var rawTests = await testsService.GetTestsAsync();
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
        }
        catch (TestExplorerProviderException ex) when (items.Count > 0)
        {
            _diagnostics?.LogInfo($"[TestExplorer] MEF TestWindow unavailable in CMake workspace: {ex.Message}");
        }

        return new GetTestsResponse
        {
            VsInstanceId = _vsInstanceId,
            Tests = items,
            TotalCount = items.Count
        };
    }

    private static string ResolveCTestExe()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrEmpty(baseDir))
            {
                var bundledCTest = Path.GetFullPath(Path.Combine(baseDir, @"CommonExtensions\Microsoft\CMake\CMake\bin\ctest.exe"));
                if (File.Exists(bundledCTest))
                {
                    return bundledCTest;
                }
            }
        }
        catch { }

        try
        {
            var procPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(procPath))
            {
                var ideDir = Path.GetDirectoryName(procPath);
                if (!string.IsNullOrEmpty(ideDir))
                {
                    var bundledCTest = Path.GetFullPath(Path.Combine(ideDir, @"CommonExtensions\Microsoft\CMake\CMake\bin\ctest.exe"));
                    if (File.Exists(bundledCTest))
                    {
                        return bundledCTest;
                    }
                }
            }
        }
        catch { }

        var standardCandidates = new[]
        {
            @"C:\Program Files\Microsoft Visual Studio\18\Professional\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\ctest.exe",
            @"C:\Program Files\Microsoft Visual Studio\18\Enterprise\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\ctest.exe",
            @"C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\ctest.exe",
            @"C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\ctest.exe",
            @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\ctest.exe",
            @"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\ctest.exe"
        };
        foreach (var c in standardCandidates)
        {
            if (File.Exists(c)) return c;
        }

        return "ctest.exe";
    }

    private async Task<List<VsTestItem>> GetCTestTestsAsync(
        string cmakeRoot,
        string? projectNameFilter,
        string? filter,
        CancellationToken cancellationToken)
    {
        var result = new List<VsTestItem>();
        try
        {
            var presets = CMakePresetsParser.LoadWorkspacePresets(cmakeRoot);
            if (presets.Count == 0) return result;

            var activePresetName = CMakeWorkspaceState.GetActivePreset(cmakeRoot, presets);
            var activePreset = presets.Find(p => string.Equals(p.Name, activePresetName, StringComparison.OrdinalIgnoreCase)) ?? presets[0];

            var binaryDir = activePreset.BinaryDir;
            if (string.IsNullOrWhiteSpace(binaryDir) || !Directory.Exists(binaryDir))
            {
                _diagnostics?.LogWarning($"[TestExplorer] CMake binaryDir does not exist: {binaryDir}");
                return result;
            }

            var ctestExe = ResolveCTestExe();
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = ctestExe,
                Arguments = $"--test-dir \"{binaryDir}\" --show-only=json-v1",
                WorkingDirectory = binaryDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            string jsonOutput;
            using (var proc = new System.Diagnostics.Process { StartInfo = psi })
            {
                proc.Start();
                jsonOutput = await proc.StandardOutput.ReadToEndAsync();
                proc.WaitForExit(5000);
            }

            var ctestItems = CTestParser.ParseDiscoveryJson(jsonOutput);
            foreach (var item in ctestItems)
            {
                if (!string.IsNullOrWhiteSpace(projectNameFilter))
                {
                    if (item.Name.IndexOf(projectNameFilter, StringComparison.OrdinalIgnoreCase) < 0 &&
                        item.Command.IndexOf(projectNameFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                }

                if (!string.IsNullOrWhiteSpace(filter))
                {
                    if (item.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                        item.Command.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                }

                result.Add(new VsTestItem
                {
                    TestId = $"ctest:{item.Name}",
                    DisplayName = item.Name,
                    FullyQualifiedName = $"ctest:{item.Name}",
                    FilePath = !string.IsNullOrEmpty(item.FilePath) ? item.FilePath : null,
                    LineNumber = item.LineNumber > 0 ? item.LineNumber : null,
                    ProjectId = cmakeRoot,
                    Source = !string.IsNullOrEmpty(item.Command) ? item.Command : null,
                    State = "NotRun",
                    DurationMs = null
                });
            }
        }
        catch (Exception ex)
        {
            _diagnostics?.LogError($"[TestExplorer] GetCTestTestsAsync error: {ex.Message}");
        }

        return result;
    }

    public async Task<RunTestsResponse> RunTestsAsync(RunTestsRequest request, CancellationToken cancellationToken)
    {
        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        var dte = await _package.GetServiceAsync(typeof(DTE)) as DTE2;

        bool hasCTestIds = request.TestIds != null && request.TestIds.Any(id => id.StartsWith("ctest:", StringComparison.OrdinalIgnoreCase));
        string cmakeRoot = string.Empty;
        bool isCMakeWorkspace = dte != null && CMakeWorkspaceState.TryGetCMakeWorkspaceRoot(dte, out cmakeRoot);

        if (isCMakeWorkspace && (hasCTestIds || (request.TestIds == null || request.TestIds.Count == 0)))
        {
            return await RunCTestTestsAsync(dte!, cmakeRoot, request, cancellationToken);
        }

        var testsService = await EnsureServicesAsync(cancellationToken);
        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        bool isAllTests = request.TestIds == null || request.TestIds.Count == 0;
        List<Guid> targetGuids = new();
        List<string> targetFqns = new();

        var allTests = await testsService.GetTestsAsync();
        if (allTests != null)
        {
            if (isAllTests)
            {
                foreach (var t in allTests)
                {
                    targetGuids.Add(TestAccessor.GetId(t));
                    targetFqns.Add(TestAccessor.GetFullyQualifiedName(t));
                }
            }
            else
            {
                var requestedIdSet = new HashSet<string>(request.TestIds!, StringComparer.OrdinalIgnoreCase);
                foreach (var t in allTests)
                {
                    var idStr = TestAccessor.GetId(t).ToString();
                    if (requestedIdSet.Contains(idStr))
                    {
                        targetGuids.Add(TestAccessor.GetId(t));
                        targetFqns.Add(TestAccessor.GetFullyQualifiedName(t));
                    }
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
                bool started = false;
                if (isAllTests)
                {
                    if (_operationBroker != null)
                    {
                        started = await _operationBroker.ExecuteAllTestsAsync(CancellationToken.None);
                    }
                    if (!started)
                    {
                        started = await testsService.RunTestsAsync(targetGuids);
                    }
                }
                else
                {
                    if (_operationBroker != null && targetFqns.Count > 0)
                    {
                        started = await _operationBroker.ExecuteTestsByFilterAsync(targetFqns, CancellationToken.None);
                    }
                    if (!started)
                    {
                        started = await testsService.RunTestsAsync(targetGuids);
                    }
                }

                _diagnostics?.LogInfo($"[TestExplorer] Test execution initiated for run {runId}, started={started}");
                if (!started)
                {
                    lock (_sync)
                    {
                        runContext.State = TestRunStates.Failed;
                        runContext.CompletedAt = DateTimeOffset.UtcNow;
                        if (_activeRun == runContext)
                        {
                            _activeRun = null;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _diagnostics?.LogError($"[TestExplorer] Error starting run {runId}: {ex.Message}");
                lock (_sync)
                {
                    runContext.State = TestRunStates.Failed;
                    runContext.CompletedAt = DateTimeOffset.UtcNow;
                    if (_activeRun == runContext)
                    {
                        _activeRun = null;
                    }
                }
            }
        });

        // Safety watchdog: finalize after 120s if event is never received
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(120));
            lock (_sync)
            {
                if (_activeRun == runContext && runContext.State == TestRunStates.Running)
                {
                    _diagnostics?.LogWarning($"[TestExplorer] Safety watchdog reached for run {runId}; finalizing.");
                    _ = FinalizeRunAsync(runContext);
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

    private async Task<RunTestsResponse> RunCTestTestsAsync(
        DTE2 dte,
        string cmakeRoot,
        RunTestsRequest request,
        CancellationToken cancellationToken)
    {
        var presets = CMakePresetsParser.LoadWorkspacePresets(cmakeRoot);
        if (presets.Count == 0)
        {
            throw new TestExplorerProviderException(
                BridgeErrorCodes.BuildStateUnavailable,
                "No CMake presets found in workspace.",
                false);
        }

        var activePresetName = CMakeWorkspaceState.GetActivePreset(cmakeRoot, presets);
        var activePreset = presets.Find(p => string.Equals(p.Name, activePresetName, StringComparison.OrdinalIgnoreCase)) ?? presets[0];

        var binaryDir = activePreset.BinaryDir;
        if (string.IsNullOrWhiteSpace(binaryDir) || !Directory.Exists(binaryDir))
        {
            throw new TestExplorerProviderException(
                BridgeErrorCodes.BuildStateUnavailable,
                $"CMake binary directory does not exist: '{binaryDir}'. Please build the project first.",
                false);
        }

        var runId = "testrun-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var now = DateTimeOffset.UtcNow;

        var targetTestNames = new List<string>();
        if (request.TestIds != null && request.TestIds.Count > 0)
        {
            foreach (var id in request.TestIds)
            {
                if (id.StartsWith("ctest:", StringComparison.OrdinalIgnoreCase))
                {
                    targetTestNames.Add(id.Substring(6).Trim());
                }
                else
                {
                    targetTestNames.Add(id.Trim());
                }
            }
        }

        var runContext = new ActiveTestRunContext(runId, new List<Guid>(), now)
        {
            IsCTest = true,
            TargetCTestNames = targetTestNames,
            TotalCount = targetTestNames.Count > 0 ? targetTestNames.Count : 1
        };

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

        runContext.State = TestRunStates.Running;

        // Output Window Pane for Test streaming
        OutputWindowPane? testPane = null;
        try
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            foreach (OutputWindowPane pane in dte.ToolWindows.OutputWindow.OutputWindowPanes)
            {
                if (pane.Name.Equals("Test", StringComparison.OrdinalIgnoreCase) ||
                    pane.Name.Equals("测试", StringComparison.OrdinalIgnoreCase))
                {
                    testPane = pane;
                    break;
                }
            }
            if (testPane == null)
            {
                testPane = dte.ToolWindows.OutputWindow.OutputWindowPanes.Add("测试");
            }
            testPane?.Activate();
            testPane?.Clear();
        }
        catch { }

        void WriteOutput(string line)
        {
            try
            {
                _ = _package.JoinableTaskFactory.RunAsync(async () =>
                {
                    await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                    testPane?.OutputString(line + Environment.NewLine);
                });
            }
            catch { }
        }

        WriteOutput($"------ 已启动 CTest 测试: 配置: {activePreset.Name} ------");

        var junitPath = Path.Combine(Path.GetTempPath(), $"ctest_junit_{Guid.NewGuid():N}.xml");
        var ctestExe = ResolveCTestExe();

        string args = $"--test-dir \"{binaryDir}\" --output-on-failure --output-junit \"{junitPath}\"";
        if (targetTestNames.Count > 0)
        {
            string filterRegex = "^(" + string.Join("|", targetTestNames.Select(Regex.Escape)) + ")$";
            args += $" -R \"{filterRegex}\"";
        }

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = ctestExe,
            Arguments = args,
            WorkingDirectory = binaryDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        _ = Task.Run(async () =>
        {
            System.Diagnostics.Process? proc = null;
            try
            {
                proc = new System.Diagnostics.Process { StartInfo = psi };
                proc.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null) WriteOutput(e.Data);
                };
                proc.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null) WriteOutput(e.Data);
                };

                lock (_sync)
                {
                    _activeCTestProcess = proc;
                }

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                while (!proc.HasExited)
                {
                    await Task.Delay(100);
                }

                WriteOutput($"------ CTest 已完成，退出代码: {proc.ExitCode} ------");

                CTestRunResult? parsedResult = null;
                if (File.Exists(junitPath))
                {
                    try
                    {
                        string xml = File.ReadAllText(junitPath);
                        parsedResult = CTestParser.ParseJUnitXml(xml);
                    }
                    catch (Exception ex)
                    {
                        _diagnostics?.LogError($"[TestExplorer] Error reading/parsing JUnit XML: {ex.Message}");
                    }
                }

                lock (_sync)
                {
                    if (runContext.State == TestRunStates.Running)
                    {
                        var resultsList = new List<VsTestResult>();
                        int passed = 0, failed = 0, skipped = 0;

                        if (parsedResult != null)
                        {
                            foreach (var kvp in parsedResult.TestOutcomes)
                            {
                                var tc = kvp.Value;
                                if (string.Equals(tc.State, "Passed", StringComparison.OrdinalIgnoreCase)) passed++;
                                else if (string.Equals(tc.State, "Failed", StringComparison.OrdinalIgnoreCase)) failed++;
                                else if (string.Equals(tc.State, "Skipped", StringComparison.OrdinalIgnoreCase)) skipped++;

                                resultsList.Add(new VsTestResult
                                {
                                    TestId = $"ctest:{tc.Name}",
                                    DisplayName = tc.Name,
                                    Outcome = tc.State,
                                    DurationMs = tc.DurationMs,
                                    ErrorMessage = tc.ErrorMessage,
                                    StandardOutput = tc.SystemOut
                                });
                            }
                        }

                        runContext.UpdateResults(resultsList, passed, failed, skipped);
                        runContext.TotalCount = resultsList.Count;
                        runContext.State = (failed > 0 || proc.ExitCode != 0) ? TestRunStates.Failed : TestRunStates.Completed;
                        runContext.CompletedAt = DateTimeOffset.UtcNow;
                    }

                    if (_activeRun == runContext)
                    {
                        _activeRun = null;
                    }
                    _activeCTestProcess = null;
                }
            }
            catch (Exception ex)
            {
                _diagnostics?.LogError($"[TestExplorer] CTest run failed: {ex.Message}");
                lock (_sync)
                {
                    runContext.State = TestRunStates.Failed;
                    runContext.CompletedAt = DateTimeOffset.UtcNow;
                    if (_activeRun == runContext)
                    {
                        _activeRun = null;
                    }
                    _activeCTestProcess = null;
                }
            }
            finally
            {
                proc?.Dispose();
                try
                {
                    if (File.Exists(junitPath)) File.Delete(junitPath);
                }
                catch { }
            }
        });

        return new RunTestsResponse
        {
            VsInstanceId = _vsInstanceId,
            TestRunId = runId,
            State = TestRunStates.Running,
            TotalCount = targetTestNames.Count,
            StartedAt = now.ToString("O")
        };
    }

    public async Task<DebugTestResponse> DebugTestAsync(DebugTestRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TestId))
        {
            throw new TestExplorerProviderException(
                BridgeErrorCodes.InvalidRequest,
                "The 'testId' parameter is required.",
                false);
        }

        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var debugger = await _debuggerProvider.GetDebuggerAsync(cancellationToken);
        if (debugger.CurrentMode != dbgDebugMode.dbgDesignMode)
        {
            throw new TestExplorerProviderException(
                BridgeErrorCodes.DebuggerAlreadyRunning,
                $"The debugger is already running (current mode: {debugger.CurrentMode}). Stop or finish the current session before debugging tests.",
                false);
        }

        lock (_sync)
        {
            if (_activeRun != null && (_activeRun.State == TestRunStates.Starting || _activeRun.State == TestRunStates.Running))
            {
                throw new TestExplorerProviderException(
                    BridgeErrorCodes.TestRunBusy,
                    $"A test run '{_activeRun.TestRunId}' is already in progress.",
                    false);
            }
        }

        // CTest test debugging branch
        if (request.TestId.StartsWith("ctest:", StringComparison.OrdinalIgnoreCase))
        {
            var testName = request.TestId.Substring(6).Trim();
            var dte = await _package.GetServiceAsync(typeof(DTE)) as DTE2;
            if (dte == null || !CMakeWorkspaceState.TryGetCMakeWorkspaceRoot(dte, out var cmakeRoot))
            {
                throw new TestExplorerProviderException(
                    BridgeErrorCodes.SolutionStateUnavailable,
                    "CMake workspace is not available.",
                    false);
            }

            var ctestItems = await GetCTestTestsAsync(cmakeRoot, null, testName, cancellationToken);
            var matchedTest = ctestItems.FirstOrDefault(t =>
                string.Equals(t.DisplayName, testName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.TestId, request.TestId, StringComparison.OrdinalIgnoreCase));

            if (matchedTest == null || string.IsNullOrWhiteSpace(matchedTest.Source))
            {
                throw new TestExplorerProviderException(
                    BridgeErrorCodes.TestNotFound,
                    $"CTest '{request.TestId}' was not found or has no executable target command.",
                    false);
            }

            var exePath = matchedTest.Source!;
            var workDir = Path.GetDirectoryName(exePath);

            var execResult = await _debuggerProvider.LaunchTargetAsync(
                exePath,
                null,
                workDir,
                request.WaitForBreak ?? true,
                request.TimeoutMs,
                cancellationToken);

            var runId = "testrun-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var now = DateTimeOffset.UtcNow;

            return new DebugTestResponse
            {
                VsInstanceId = _vsInstanceId,
                TestRunId = runId,
                TestId = request.TestId,
                TestDisplayName = testName,
                IsDebugging = execResult.IsDebugging,
                DebuggerMode = execResult.CurrentMode,
                LastBreakReason = execResult.LastBreakReason,
                TopFrame = execResult.TopFrame,
                CurrentProcessId = execResult.CurrentProcessId,
                CurrentThreadId = execResult.CurrentThreadId,
                StartedAt = now.ToString("O"),
                Warnings = execResult.Warnings
            };
        }

        var testsService = await EnsureServicesAsync(cancellationToken);
        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var allTests = await testsService.GetTestsAsync();
        object? targetTest = null;
        if (allTests != null)
        {
            foreach (var t in allTests)
            {
                if (string.Equals(TestAccessor.GetId(t).ToString(), request.TestId, StringComparison.OrdinalIgnoreCase))
                {
                    targetTest = t;
                    break;
                }
            }
        }

        if (targetTest == null)
        {
            throw new TestExplorerProviderException(
                BridgeErrorCodes.TestNotFound,
                $"Test with ID '{request.TestId}' was not found.",
                false);
        }

        var testGuid = TestAccessor.GetId(targetTest);
        var testFqn = TestAccessor.GetFullyQualifiedName(targetTest);
        var testDisplayName = TestAccessor.GetDisplayName(targetTest);

        var runIdMef = "testrun-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var nowMef = DateTimeOffset.UtcNow;
        var runContextMef = new ActiveTestRunContext(runIdMef, new List<Guid> { testGuid }, nowMef);

        lock (_sync)
        {
            _activeRun = runContextMef;
            _lastRun = runContextMef;
        }

        runContextMef.State = TestRunStates.Running;

        if (_operationBroker == null)
        {
            throw new TestExplorerProviderException(
                BridgeErrorCodes.TestWindowUnavailable,
                "Test OperationBroker service is not available.",
                true);
        }

        // Trigger test debugging asynchronously on UI thread
        _ = _package.JoinableTaskFactory.RunAsync(async () =>
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
            try
            {
                bool started = false;
                if (_operationBroker != null)
                {
                    started = await _operationBroker.DebugTestsByFilterAsync(new List<string> { testFqn }, CancellationToken.None);
                }
                _diagnostics?.LogInfo($"[TestExplorer] DebugTestsByFilterAsync finished for {testFqn}, started={started}");
                if (!started)
                {
                    lock (_sync)
                    {
                        runContextMef.State = TestRunStates.Failed;
                        runContextMef.CompletedAt = DateTimeOffset.UtcNow;
                        if (_activeRun == runContextMef)
                        {
                            _activeRun = null;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _diagnostics?.LogError($"[TestExplorer] Error in DebugTestsByFilterAsync for {testFqn}: {ex.Message}");
                lock (_sync)
                {
                    runContextMef.State = TestRunStates.Failed;
                    runContextMef.CompletedAt = DateTimeOffset.UtcNow;
                    if (_activeRun == runContextMef)
                    {
                        _activeRun = null;
                    }
                }
            }
        });

        // Safety watchdog: finalize after 120s if event is never received
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(120));
            lock (_sync)
            {
                if (_activeRun == runContextMef && runContextMef.State == TestRunStates.Running)
                {
                    _diagnostics?.LogWarning($"[TestExplorer] Safety watchdog reached for debug run {runIdMef}; finalizing.");
                    _ = FinalizeRunAsync(runContextMef);
                }
            }
        });

        bool waitForBreak = request.WaitForBreak ?? true;
        int timeoutMs = Clamp(request.TimeoutMs ?? 10000, 500, 60000);
        var warnings = new List<BridgeWarning>();

        if (waitForBreak)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(100, cancellationToken);
                await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                if (debugger.CurrentMode == dbgDebugMode.dbgBreakMode)
                {
                    break;
                }

                if (debugger.CurrentMode == dbgDebugMode.dbgDesignMode && runContextMef.State != TestRunStates.Running)
                {
                    warnings.Add(new BridgeWarning
                    {
                        Code = "test_completed_without_break",
                        Message = $"The test '{testDisplayName}' executed and completed without triggering a breakpoint or exception."
                    });
                    break;
                }
            }

            if (debugger.CurrentMode == dbgDebugMode.dbgRunMode && sw.ElapsedMilliseconds >= timeoutMs)
            {
                warnings.Add(new BridgeWarning
                {
                    Code = "break_wait_timeout",
                    Message = $"Timed out after {timeoutMs}ms waiting for the debugger to enter break mode. The test process may still be running."
                });
            }
        }

        var executionResult = await _debuggerProvider.CaptureCurrentStateAsync("debug_test", cancellationToken);

        return new DebugTestResponse
        {
            VsInstanceId = _vsInstanceId,
            TestRunId = runIdMef,
            TestId = testGuid.ToString(),
            TestDisplayName = testDisplayName,
            IsDebugging = executionResult.IsDebugging,
            DebuggerMode = executionResult.CurrentMode,
            LastBreakReason = executionResult.LastBreakReason,
            TopFrame = executionResult.TopFrame,
            CurrentProcessId = executionResult.CurrentProcessId,
            CurrentThreadId = executionResult.CurrentThreadId,
            StartedAt = nowMef.ToString("O"),
            Warnings = warnings
        };
    }

    private static int Clamp(int value, int min, int max) =>
        value < min ? min : (value > max ? max : value);

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

        // If currently running, do a live refresh of results (MEF only)
        if (run.State == TestRunStates.Running && !run.IsCTest)
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
        System.Diagnostics.Process? ctestProc = null;
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

            ctestProc = _activeCTestProcess;
            _activeCTestProcess = null;
        }

        if (ctestProc != null && !ctestProc.HasExited)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    Arguments = $"/F /T /PID {ctestProc.Id}",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                System.Diagnostics.Process.Start(psi)?.WaitForExit(3000);
            }
            catch (Exception ex)
            {
                _diagnostics?.LogWarning($"[TestExplorer] Error cancelling CTest process: {ex.Message}");
            }
        }

        if (_operationBroker != null && !run.IsCTest)
        {
            try
            {
                await _operationBroker.CancelAsync(cancellationToken);
            }
            catch
            {
            }
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

        var tests = await _testsService.GetTestsAsync();
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
        public List<string>? TargetCTestNames { get; set; }
        public bool IsCTest { get; set; }
        public DateTimeOffset StartedAt { get; }
        public DateTimeOffset? CompletedAt { get; set; }
        public int TotalCount { get; set; }
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

    private sealed class OperationBrokerInvoker
    {
        private readonly object _broker;
        private readonly VsDiagnosticService? _diagnostics;
        private readonly MethodInfo? _executeAllTestsMethod;
        private readonly MethodInfo? _executeTestsByFilterMethod;
        private readonly MethodInfo? _debugTestsByFilterMethod;
        private readonly MethodInfo? _cancelAsyncMethod;

        public OperationBrokerInvoker(object broker, VsDiagnosticService? diagnostics = null)
        {
            _broker = broker;
            _diagnostics = diagnostics;
            var type = broker.GetType();
            _executeAllTestsMethod = type.GetMethod("ExecuteAllTestsAsync", new[] { typeof(int?), typeof(CancellationToken) });
            _executeTestsByFilterMethod = type.GetMethod("ExecuteTestsByFilterAsync", BindingFlags.Public | BindingFlags.Instance);
            _debugTestsByFilterMethod = type.GetMethod("DebugTestsByFilterAsync", BindingFlags.Public | BindingFlags.Instance);
            _cancelAsyncMethod = type.GetMethod("CancelAsync", new[] { typeof(CancellationToken) });
        }

        public async Task<bool> ExecuteAllTestsAsync(CancellationToken cancellationToken)
        {
            if (_executeAllTestsMethod == null) return false;
            try
            {
                var task = (Task)_executeAllTestsMethod.Invoke(_broker, new object?[] { null, cancellationToken })!;
                await task.ConfigureAwait(false);
                var prop = task.GetType().GetProperty("Result");
                if (prop != null && prop.GetValue(task) is bool b) return b;
                return true;
            }
            catch (Exception ex)
            {
                _diagnostics?.LogError($"[TestExplorer] ExecuteAllTestsAsync error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ExecuteTestsByFilterAsync(List<string> fqns, CancellationToken cancellationToken)
        {
            if (_executeTestsByFilterMethod == null)
            {
                _diagnostics?.LogWarning("[TestExplorer] ExecuteTestsByFilterAsync method not found on broker.");
                return false;
            }

            try
            {
                var messagesAsm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Microsoft.VisualStudio.TestWindow.Internal")
                    ?? Assembly.Load("Microsoft.VisualStudio.TestWindow.Internal");
                var searchQueryType = messagesAsm?.GetType("Microsoft.VisualStudio.TestWindow.Messages.SearchQuery");
                var filterMatchKindType = typeof(IOperationState).Assembly.GetType("Microsoft.VisualStudio.TestWindow.Extensibility.FilterMatchKind");

                if (searchQueryType == null || filterMatchKindType == null)
                {
                    _diagnostics?.LogWarning($"[TestExplorer] Cannot resolve SearchQuery ({searchQueryType != null}) or FilterMatchKind ({filterMatchKindType != null}).");
                    return false;
                }

                var exactMatchVal = Enum.Parse(filterMatchKindType, "ExactMatch");
                var listType = typeof(List<>).MakeGenericType(searchQueryType);
                var filterList = (System.Collections.IList)Activator.CreateInstance(listType)!;

                var ctors = searchQueryType.GetConstructors();
                if (ctors.Length > 0 && fqns != null && fqns.Count > 0)
                {
                    var ctor = ctors[0];
                    // SearchQuery ctor: (property, value, values, subQueries, matchKind)
                    var queryObj = ctor.Invoke(new object?[] { "TestWindow_FullyQualifiedName", null, fqns, null, exactMatchVal });
                    filterList.Add(queryObj);
                }

                var task = (Task)_executeTestsByFilterMethod.Invoke(_broker, new object[] { filterList, cancellationToken })!;
                await task.ConfigureAwait(false);
                var prop = task.GetType().GetProperty("Result");
                if (prop != null && prop.GetValue(task) is bool b) return b;
                return true;
            }
            catch (Exception ex)
            {
                _diagnostics?.LogError($"[TestExplorer] ExecuteTestsByFilterAsync error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DebugTestsByFilterAsync(List<string> fqns, CancellationToken cancellationToken)
        {
            if (_debugTestsByFilterMethod == null)
            {
                _diagnostics?.LogWarning("[TestExplorer] DebugTestsByFilterAsync method not found on broker.");
                return false;
            }

            try
            {
                var messagesAsm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Microsoft.VisualStudio.TestWindow.Internal")
                    ?? Assembly.Load("Microsoft.VisualStudio.TestWindow.Internal");
                var searchQueryType = messagesAsm?.GetType("Microsoft.VisualStudio.TestWindow.Messages.SearchQuery");
                var filterMatchKindType = typeof(IOperationState).Assembly.GetType("Microsoft.VisualStudio.TestWindow.Extensibility.FilterMatchKind");

                if (searchQueryType == null || filterMatchKindType == null)
                {
                    _diagnostics?.LogWarning($"[TestExplorer] Cannot resolve SearchQuery ({searchQueryType != null}) or FilterMatchKind ({filterMatchKindType != null}).");
                    return false;
                }

                var exactMatchVal = Enum.Parse(filterMatchKindType, "ExactMatch");
                var listType = typeof(List<>).MakeGenericType(searchQueryType);
                var filterList = (System.Collections.IList)Activator.CreateInstance(listType)!;

                var ctors = searchQueryType.GetConstructors();
                if (ctors.Length > 0 && fqns != null && fqns.Count > 0)
                {
                    var ctor = ctors[0];
                    var queryObj = ctor.Invoke(new object?[] { "TestWindow_FullyQualifiedName", null, fqns, null, exactMatchVal });
                    filterList.Add(queryObj);
                }

                var task = (Task)_debugTestsByFilterMethod.Invoke(_broker, new object[] { filterList, cancellationToken })!;
                await task.ConfigureAwait(false);
                var prop = task.GetType().GetProperty("Result");
                if (prop != null && prop.GetValue(task) is bool b) return b;
                return true;
            }
            catch (Exception ex)
            {
                _diagnostics?.LogError($"[TestExplorer] DebugTestsByFilterAsync error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> CancelAsync(CancellationToken cancellationToken)
        {
            if (_cancelAsyncMethod == null) return false;
            try
            {
                var task = (Task)_cancelAsyncMethod.Invoke(_broker, new object[] { cancellationToken })!;
                await task.ConfigureAwait(false);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private sealed class TestsServiceInvoker
    {
        private readonly object _service;
        private readonly MethodInfo _getTestsMethod;
        private readonly MethodInfo _runTestsMethod;

        public TestsServiceInvoker(object service)
        {
            _service = service;
            var type = service.GetType();
            _getTestsMethod = type.GetMethod("GetTestsAsEnumerableAsync", Type.EmptyTypes)
                ?? throw new InvalidOperationException("GetTestsAsEnumerableAsync not found.");
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
