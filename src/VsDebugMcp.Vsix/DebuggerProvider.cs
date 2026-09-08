using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using EnvDTE90;
using EnvDTE90a;
using Microsoft.VisualStudio.Shell;
using VsDebugMcp.Protocol;

namespace VsDebugMcp_Vsix;

internal sealed class DebuggerProvider
{
	private const int DefaultMaxFrames = 50;
	private const int MaxAllowedFrames = 200;
	private const int DefaultTimeoutMs = 2000;
	private const int MaxAllowedTimeoutMs = 10000;

	private const uint PROCESS_VM_READ = 0x0010;
	private const uint PROCESS_QUERY_INFORMATION = 0x0400;

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool CloseHandle(IntPtr hObject);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool ReadProcessMemory(
		IntPtr hProcess,
		IntPtr lpBaseAddress,
		[Out] byte[] lpBuffer,
		int dwSize,
		out IntPtr lpNumberOfBytesRead);

	private readonly AsyncPackage _package;
	private readonly string _vsInstanceId;
	private readonly OutputWindowProvider? _outputWindowProvider;
	private readonly SemaphoreSlim _executionLock = new(1, 1);

	public DebuggerProvider(
		AsyncPackage package,
		string vsInstanceId,
		OutputWindowProvider? outputWindowProvider = null)
	{
		_package = package;
		_vsInstanceId = vsInstanceId;
		_outputWindowProvider = outputWindowProvider;
	}

	public async Task<DebuggerGetInfoResponse> GetInfoAsync(
		DebuggerGetInfoRequest request,
		CancellationToken cancellationToken)
	{
		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var debugger = await GetDebuggerAsync(cancellationToken);

		var mode = GetModeString(debugger.CurrentMode);
		var isDebugging = debugger.CurrentMode != dbgDebugMode.dbgDesignMode;

		int? processId = null;
		string? processName = null;
		int? threadId = null;
		string? threadName = null;
		string? breakReason = null;
		var breakpointCount = 0;

		try
		{
			breakpointCount = debugger.Breakpoints?.Count ?? 0;
		}
		catch
		{
		}

		if (isDebugging)
		{
			try
			{
				var proc = debugger.CurrentProcess;
				if (proc != null)
				{
					processId = proc.ProcessID;
					processName = proc.Name;
				}
			}
			catch
			{
			}

			try
			{
				var thread = debugger.CurrentThread;
				if (thread != null)
				{
					threadId = thread.ID;
					threadName = thread.Name;
				}
			}
			catch
			{
			}

			try
			{
				breakReason = GetBreakReasonString(debugger.LastBreakReason);
			}
			catch
			{
			}
		}

		return new DebuggerGetInfoResponse
		{
			VsInstanceId = _vsInstanceId,
			Mode = mode,
			IsDebugging = isDebugging,
			CurrentProcessId = processId,
			CurrentProcessName = processName,
			CurrentThreadId = threadId,
			CurrentThreadName = threadName,
			BreakpointCount = breakpointCount,
			LastBreakReason = breakReason
		};
	}

	public async Task<DebuggerGetSnapshotResponse> GetSnapshotAsync(
		DebuggerGetSnapshotRequest request,
		CancellationToken cancellationToken)
	{
		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var (dte, debugger) = await GetDteAndDebuggerAsync(cancellationToken);

		var isDebugging = debugger.CurrentMode != dbgDebugMode.dbgDesignMode;
		var mode = GetModeString(debugger.CurrentMode);

		int? processId = null;
		string? processName = null;
		int? threadId = null;
		string? threadName = null;
		string? breakReason = null;
		StackFrameInfo? topFrame = null;
		var callStack = new List<StackFrameInfo>();
		var locals = new List<DebuggerVariableInfo>();
		var warnings = new List<BridgeWarning>();
		string? recentLogs = null;

		if (isDebugging)
		{
			try
			{
				var proc = debugger.CurrentProcess;
				if (proc != null)
				{
					processId = proc.ProcessID;
					processName = proc.Name;
				}
			}
			catch { }

			try
			{
				var thread = debugger.CurrentThread;
				if (thread != null)
				{
					threadId = thread.ID;
					threadName = thread.Name;
				}
			}
			catch { }

			try
			{
				breakReason = GetBreakReasonString(debugger.LastBreakReason);
			}
			catch { }
		}

		if (debugger.CurrentMode == dbgDebugMode.dbgBreakMode)
		{
			try
			{
				var frame = debugger.CurrentStackFrame;
				if (frame != null)
				{
					topFrame = ReadStackFrame(frame, 0);
				}
			}
			catch { }

			if (request.IncludeCallStack)
			{
				try
				{
					var maxFrames = Clamp(request.MaxFrames ?? 10, 1, MaxAllowedFrames);
					var th = debugger.CurrentThread;
					if (th?.StackFrames != null)
					{
						var idx = 0;
						foreach (StackFrame sf in th.StackFrames)
						{
							cancellationToken.ThrowIfCancellationRequested();
							if (idx >= maxFrames) break;
							callStack.Add(ReadStackFrame(sf, idx));
							idx++;
						}
					}
				}
				catch { }
			}

			if (request.IncludeLocals)
			{
				try
				{
					var maxLocals = Clamp(request.MaxLocals ?? 50, 1, 200);
					var frame = debugger.CurrentStackFrame;
					if (frame?.Locals != null)
					{
						var idx = 0;
						foreach (Expression expr in frame.Locals)
						{
							cancellationToken.ThrowIfCancellationRequested();
							if (idx >= maxLocals) break;
							var val = string.Empty;
							var type = string.Empty;
							var isValid = false;
							try { val = expr.Value ?? string.Empty; } catch { }
							try { type = expr.Type ?? string.Empty; } catch { }
							try { isValid = expr.IsValidValue; } catch { }

							CheckOptimizationWarning(dte, val, isValid, warnings);

							locals.Add(new DebuggerVariableInfo
							{
								Name = expr.Name ?? string.Empty,
								Value = val,
								Type = type,
								IsArgument = false
							});
							idx++;
						}
					}
				}
				catch { }
			}
		}

		if (request.IncludeRecentLogs && _outputWindowProvider != null)
		{
			try
			{
				var logLines = Clamp(request.RecentLogLines ?? 30, 1, 200);
				var source = string.IsNullOrWhiteSpace(request.LogSource) ? "debug" : request.LogSource.Trim();
				var logResp = await _outputWindowProvider.GetLogsAsync(new GetOutputWindowLogsRequest
				{
					Source = source,
					MaxChars = logLines * 200
				}, cancellationToken);

				if (!string.IsNullOrEmpty(logResp.Text))
				{
					var lines = logResp.Text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
					if (lines.Length > logLines)
					{
						recentLogs = string.Join(Environment.NewLine, lines.Skip(lines.Length - logLines));
					}
					else
					{
						recentLogs = logResp.Text;
					}
				}
			}
			catch (Exception ex)
			{
				warnings.Add(new BridgeWarning
				{
					Code = "recent_logs_unavailable",
					Message = $"Failed to retrieve recent logs: {ex.Message}"
				});
			}
		}

		return new DebuggerGetSnapshotResponse
		{
			VsInstanceId = _vsInstanceId,
			Mode = mode,
			IsDebugging = isDebugging,
			CurrentProcessId = processId,
			CurrentProcessName = processName,
			CurrentThreadId = threadId,
			CurrentThreadName = threadName,
			LastBreakReason = breakReason,
			TopFrame = topFrame,
			CallStack = callStack,
			Locals = locals,
			RecentLogs = recentLogs,
			LogSource = request.IncludeRecentLogs ? (request.LogSource ?? "debug") : null,
			Warnings = warnings
		};
	}

	public async Task<DebuggerSetBreakpointsResponse> SetBreakpointsAsync(
		DebuggerSetBreakpointsRequest request,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(request.FilePath))
		{
			throw new DebuggerProviderException(BridgeErrorCodes.InvalidRequest, "File path is required.");
		}

		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var debugger = await GetDebuggerAsync(cancellationToken);

		var fullPath = request.FilePath.Trim();
		if (!Path.IsPathRooted(fullPath))
		{
			try
			{
				fullPath = Path.GetFullPath(fullPath);
			}
			catch
			{
			}
		}

		var response = new DebuggerSetBreakpointsResponse
		{
			VsInstanceId = _vsInstanceId,
			FilePath = fullPath
		};

		if (request.ClearExisting && debugger.Breakpoints != null)
		{
			try
			{
				var toDelete = new List<Breakpoint>();
				foreach (Breakpoint bp in debugger.Breakpoints)
				{
					cancellationToken.ThrowIfCancellationRequested();
					if (string.Equals(bp.File, fullPath, StringComparison.OrdinalIgnoreCase))
					{
						toDelete.Add(bp);
					}
				}

				foreach (var bp in toDelete)
				{
					try
					{
						bp.Delete();
					}
					catch
					{
					}
				}
			}
			catch (Exception ex) when (ex is not OutOfMemoryException && ex is not OperationCanceledException)
			{
				response.Warnings.Add(new BridgeWarning
				{
					Code = "clear_breakpoints_failed",
					Message = $"Failed to clear existing breakpoints: {ex.Message}"
				});
			}
		}

		if (request.Breakpoints != null && debugger.Breakpoints != null)
		{
			foreach (var spec in request.Breakpoints)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (spec.Line < 1)
				{
					response.Warnings.Add(new BridgeWarning
					{
						Code = "invalid_breakpoint_line",
						Message = $"Invalid line number {spec.Line} for file '{fullPath}'."
					});
					continue;
				}

				try
				{
					var col = spec.Column ?? 1;
					var condition = spec.Condition ?? string.Empty;
					var condType = string.Equals(spec.ConditionType, "whenChanged", StringComparison.OrdinalIgnoreCase)
						? dbgBreakpointConditionType.dbgBreakpointConditionTypeWhenChanged
						: dbgBreakpointConditionType.dbgBreakpointConditionTypeWhenTrue;

					var hitType = dbgHitCountType.dbgHitCountTypeNone;
					var hitTarget = 0;
					if (spec.HitCountTarget.HasValue && spec.HitCountTarget.Value > 0)
					{
						hitTarget = spec.HitCountTarget.Value;
						hitType = string.Equals(spec.HitCountType, "greaterOrEqual", StringComparison.OrdinalIgnoreCase)
							? dbgHitCountType.dbgHitCountTypeGreaterOrEqual
							: string.Equals(spec.HitCountType, "multiple", StringComparison.OrdinalIgnoreCase)
								? dbgHitCountType.dbgHitCountTypeMultiple
								: dbgHitCountType.dbgHitCountTypeEqual;
					}

					var addedBreakpoints = debugger.Breakpoints.Add(
						"",
						fullPath,
						spec.Line,
						col,
						condition,
						condType,
						"",
						"",
						1,
						"",
						hitTarget,
						hitType);

					if (addedBreakpoints != null)
					{
						foreach (Breakpoint bp in addedBreakpoints)
						{
							bp.Enabled = spec.Enabled;

							string? resCondType = null;
							try
							{
								resCondType = bp.ConditionType == dbgBreakpointConditionType.dbgBreakpointConditionTypeWhenChanged ? "whenChanged" : "whenTrue";
							}
							catch { }

							int? resHitTarget = null;
							try
							{
								resHitTarget = bp.HitCountTarget;
							}
							catch { }

							string? resHitType = null;
							try
							{
								resHitType = bp.HitCountType switch
								{
									dbgHitCountType.dbgHitCountTypeEqual => "equal",
									dbgHitCountType.dbgHitCountTypeGreaterOrEqual => "greaterOrEqual",
									dbgHitCountType.dbgHitCountTypeMultiple => "multiple",
									_ => null
								};
							}
							catch { }

							int? currentHits = null;
							try
							{
								currentHits = bp.CurrentHits;
							}
							catch { }

							response.Breakpoints.Add(new BreakpointInfo
							{
								Id = $"{fullPath}:{bp.FileLine}",
								FilePath = fullPath,
								Line = bp.FileLine,
								Column = bp.FileColumn,
								Condition = string.IsNullOrEmpty(bp.Condition) ? null : bp.Condition,
								Enabled = bp.Enabled,
								IsBound = true,
								ConditionType = resCondType,
								HitCountTarget = resHitTarget,
								HitCountType = resHitType,
								CurrentHitCount = currentHits
							});
						}
					}
				}
				catch (Exception ex) when (ex is not OutOfMemoryException && ex is not OperationCanceledException)
				{
					response.Warnings.Add(new BridgeWarning
					{
						Code = "add_breakpoint_failed",
						Message = $"Failed to set breakpoint at line {spec.Line}: {ex.Message}"
					});
				}
			}
		}

		return response;
	}

	public async Task<DebuggerListBreakpointsResponse> ListBreakpointsAsync(
		DebuggerListBreakpointsRequest request,
		CancellationToken cancellationToken)
	{
		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var debugger = await GetDebuggerAsync(cancellationToken);

		var result = new List<BreakpointInfo>();
		var filterPath = request.FilePath?.Trim();
		if (!string.IsNullOrEmpty(filterPath) && !Path.IsPathRooted(filterPath))
		{
			try { filterPath = Path.GetFullPath(filterPath); } catch { }
		}

		if (debugger.Breakpoints != null)
		{
			foreach (Breakpoint bp in debugger.Breakpoints)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var info = MapBreakpoint(bp);

				if (request.EnabledOnly && !info.Enabled)
				{
					continue;
				}

				if (!string.IsNullOrEmpty(filterPath))
				{
					if (!string.Equals(info.FilePath, filterPath, StringComparison.OrdinalIgnoreCase) &&
					    !info.FilePath.EndsWith(filterPath, StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
				}

				result.Add(info);
			}
		}

		return new DebuggerListBreakpointsResponse
		{
			VsInstanceId = _vsInstanceId,
			TotalCount = result.Count,
			Breakpoints = result
		};
	}

	public async Task<DebuggerClearBreakpointsResponse> ClearBreakpointsAsync(
		DebuggerClearBreakpointsRequest request,
		CancellationToken cancellationToken)
	{
		if (!request.ClearAll &&
		    string.IsNullOrWhiteSpace(request.FilePath) &&
		    string.IsNullOrWhiteSpace(request.BreakpointId))
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.InvalidRequest,
				"Must specify 'clearAll: true', 'filePath', or 'breakpointId' to clear breakpoints. Preventing accidental full deletion.");
		}

		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var debugger = await GetDebuggerAsync(cancellationToken);

		var response = new DebuggerClearBreakpointsResponse
		{
			VsInstanceId = _vsInstanceId
		};

		if (debugger.Breakpoints == null || debugger.Breakpoints.Count == 0)
		{
			response.ClearedCount = 0;
			response.RemainingCount = 0;
			return response;
		}

		var targetFile = request.FilePath?.Trim();
		if (!string.IsNullOrEmpty(targetFile) && !Path.IsPathRooted(targetFile))
		{
			try { targetFile = Path.GetFullPath(targetFile); } catch { }
		}

		var toDelete = new List<Breakpoint>();
		try
		{
			foreach (Breakpoint bp in debugger.Breakpoints)
			{
				cancellationToken.ThrowIfCancellationRequested();
				string bpFile = string.Empty;
				int bpLine = 0;
				try { bpFile = bp.File ?? string.Empty; } catch { }
				try { bpLine = bp.FileLine; } catch { }

				var bpId = !string.IsNullOrEmpty(bpFile) ? $"{bpFile}:{bpLine}" : $"bp:{bpLine}";

				if (request.ClearAll)
				{
					toDelete.Add(bp);
				}
				else if (!string.IsNullOrEmpty(request.BreakpointId))
				{
					if (string.Equals(bpId, request.BreakpointId.Trim(), StringComparison.OrdinalIgnoreCase))
					{
						toDelete.Add(bp);
					}
				}
				else if (!string.IsNullOrEmpty(targetFile))
				{
					var fileMatch = string.Equals(bpFile, targetFile, StringComparison.OrdinalIgnoreCase) ||
					                bpFile.EndsWith(targetFile, StringComparison.OrdinalIgnoreCase);

					if (fileMatch)
					{
						if (request.Line.HasValue)
						{
							if (bpLine == request.Line.Value)
							{
								toDelete.Add(bp);
							}
						}
						else
						{
							toDelete.Add(bp);
						}
					}
				}
			}

			foreach (var bp in toDelete)
			{
				try
				{
					bp.Delete();
					response.ClearedCount++;
				}
				catch (Exception ex)
				{
					response.Warnings.Add(new BridgeWarning
					{
						Code = "delete_breakpoint_failed",
						Message = $"Failed to delete breakpoint: {ex.Message}"
					});
				}
			}

			response.RemainingCount = debugger.Breakpoints?.Count ?? 0;
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not OperationCanceledException)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.DebuggerUnavailable,
				$"Failed to clear breakpoints: {ex.Message}",
				ex);
		}

		return response;
	}

	public async Task<DebuggerToggleBreakpointResponse> ToggleBreakpointAsync(
		DebuggerToggleBreakpointRequest request,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(request.BreakpointId) &&
		    (string.IsNullOrWhiteSpace(request.FilePath) || !request.Line.HasValue))
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.InvalidRequest,
				"Must specify either 'breakpointId' or both 'filePath' and 'line' to toggle a breakpoint.");
		}

		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var debugger = await GetDebuggerAsync(cancellationToken);

		var response = new DebuggerToggleBreakpointResponse
		{
			VsInstanceId = _vsInstanceId
		};

		if (debugger.Breakpoints == null || debugger.Breakpoints.Count == 0)
		{
			throw new DebuggerProviderException(BridgeErrorCodes.BreakpointNotFound, "No breakpoints are set in the current solution.");
		}

		var targetFile = request.FilePath?.Trim();
		if (!string.IsNullOrEmpty(targetFile) && !Path.IsPathRooted(targetFile))
		{
			try { targetFile = Path.GetFullPath(targetFile); } catch { }
		}

		var targetId = request.BreakpointId?.Trim();
		var matched = new List<Breakpoint>();

		foreach (Breakpoint bp in debugger.Breakpoints)
		{
			cancellationToken.ThrowIfCancellationRequested();
			string bpFile = string.Empty;
			int bpLine = 0;
			try { bpFile = bp.File ?? string.Empty; } catch { }
			try { bpLine = bp.FileLine; } catch { }

			var bpId = !string.IsNullOrEmpty(bpFile) ? $"{bpFile}:{bpLine}" : $"bp:{bpLine}";

			if (!string.IsNullOrEmpty(targetId))
			{
				if (string.Equals(bpId, targetId, StringComparison.OrdinalIgnoreCase))
				{
					matched.Add(bp);
				}
			}
			else if (!string.IsNullOrEmpty(targetFile) && request.Line.HasValue)
			{
				var fileMatch = string.Equals(bpFile, targetFile, StringComparison.OrdinalIgnoreCase) ||
				                bpFile.EndsWith(targetFile, StringComparison.OrdinalIgnoreCase);
				if (fileMatch && bpLine == request.Line.Value)
				{
					matched.Add(bp);
				}
			}
		}

		if (matched.Count == 0)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.BreakpointNotFound,
				$"No breakpoint found matching target '{targetId ?? $"{targetFile}:{request.Line}"}'.");
		}

		foreach (var bp in matched)
		{
			try
			{
				var newEnabled = request.Enabled ?? !bp.Enabled;
				bp.Enabled = newEnabled;
				response.Breakpoints.Add(MapBreakpoint(bp));
				response.MatchedCount++;
			}
			catch (Exception ex)
			{
				response.Warnings.Add(new BridgeWarning
				{
					Code = "toggle_breakpoint_failed",
					Message = $"Failed to toggle breakpoint: {ex.Message}"
				});
			}
		}

		return response;
	}

	private static BreakpointInfo MapBreakpoint(Breakpoint bp)
	{
		ThreadHelper.ThrowIfNotOnUIThread();
		var file = string.Empty;
		var line = 1;
		var col = 1;
		var condition = string.Empty;
		var enabled = true;
		string? condType = null;
		int? hitTarget = null;
		string? hitType = null;
		int? currentHits = null;
		var isBound = false;

		try { file = bp.File ?? string.Empty; } catch { }
		try { line = bp.FileLine; } catch { }
		try { col = bp.FileColumn; } catch { }
		try { condition = bp.Condition; } catch { }
		try { enabled = bp.Enabled; } catch { }

		try
		{
			condType = bp.ConditionType == dbgBreakpointConditionType.dbgBreakpointConditionTypeWhenChanged
				? "whenChanged"
				: "whenTrue";
		}
		catch { }

		try { hitTarget = bp.HitCountTarget; } catch { }

		try
		{
			hitType = bp.HitCountType switch
			{
				dbgHitCountType.dbgHitCountTypeEqual => "equal",
				dbgHitCountType.dbgHitCountTypeGreaterOrEqual => "greaterOrEqual",
				dbgHitCountType.dbgHitCountTypeMultiple => "multiple",
				_ => null
			};
		}
		catch { }

		try { currentHits = bp.CurrentHits; } catch { }

		try
		{
			isBound = (bp.Children != null && bp.Children.Count > 0) || bp.FileLine > 0;
		}
		catch
		{
			isBound = bp.FileLine > 0;
		}

		var id = !string.IsNullOrWhiteSpace(file) ? $"{file}:{line}" : $"bp:{line}:{col}";

		return new BreakpointInfo
		{
			Id = id,
			FilePath = file,
			Line = line,
			Column = col,
			Condition = string.IsNullOrEmpty(condition) ? null : condition,
			Enabled = enabled,
			IsBound = isBound,
			ConditionType = condType,
			HitCountTarget = hitTarget,
			HitCountType = hitType,
			CurrentHitCount = currentHits
		};
	}

	public async Task<DebuggerGetCallStackResponse> GetCallStackAsync(
		DebuggerGetCallStackRequest request,
		CancellationToken cancellationToken)
	{
		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var debugger = await GetDebuggerAsync(cancellationToken);

		if (debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.DebuggerNotPaused,
				$"The debugger is not currently in break mode (current mode: {GetModeString(debugger.CurrentMode)}). Call stacks are only available when paused at a breakpoint or exception.");
		}

		var thread = FindThread(debugger, request.ThreadId);
		if (thread == null)
		{
			throw new DebuggerProviderException(BridgeErrorCodes.DebuggerUnavailable, "No active debug thread available.");
		}

		var maxFrames = Clamp(request.MaxFrames ?? DefaultMaxFrames, 1, MaxAllowedFrames);
		var frames = new List<StackFrameInfo>();
		var threadName = thread.Name ?? string.Empty;
		var threadId = thread.ID;

		var totalFrames = 0;
		try
		{
			var stackFrames = thread.StackFrames;
			if (stackFrames != null)
			{
				totalFrames = stackFrames.Count;
				var frameIndex = 0;
				foreach (StackFrame frame in stackFrames)
				{
					cancellationToken.ThrowIfCancellationRequested();
					if (frameIndex >= maxFrames)
					{
						break;
					}

					frames.Add(ReadStackFrame(frame, frameIndex));
					frameIndex++;
				}
			}
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not OperationCanceledException)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.DebuggerUnavailable,
				$"Failed to retrieve stack frames: {ex.Message}",
				ex);
		}

		return new DebuggerGetCallStackResponse
		{
			VsInstanceId = _vsInstanceId,
			ThreadId = threadId,
			ThreadName = string.IsNullOrWhiteSpace(threadName) ? null : threadName,
			Frames = frames,
			TotalFrames = totalFrames,
			Truncated = totalFrames > frames.Count
		};
	}

	public async Task<DebuggerEvaluateExprResponse> EvaluateExprAsync(
		DebuggerEvaluateExprRequest request,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(request.Expression))
		{
			throw new DebuggerProviderException(BridgeErrorCodes.InvalidRequest, "Expression is required.");
		}

		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var (dte, debugger) = await GetDteAndDebuggerAsync(cancellationToken);

		if (debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.DebuggerNotPaused,
				$"The debugger is not currently in break mode (current mode: {GetModeString(debugger.CurrentMode)}). Expression evaluation is only available when paused at a breakpoint or exception.");
		}

		var targetFrameIndex = request.FrameIndex ?? 0;
		var timeoutMs = Clamp(request.TimeoutMs ?? DefaultTimeoutMs, 100, MaxAllowedTimeoutMs);

		StackFrame? originalFrame = null;
		var switchedFrame = false;

		try
		{
			if (targetFrameIndex > 0 && debugger.CurrentThread?.StackFrames != null)
			{
				originalFrame = debugger.CurrentStackFrame;
				var currentIndex = 0;
				foreach (StackFrame frame in debugger.CurrentThread.StackFrames)
				{
					if (currentIndex == targetFrameIndex)
					{
						debugger.CurrentStackFrame = frame;
						switchedFrame = true;
						break;
					}

					currentIndex++;
				}
			}

			var expr = debugger.GetExpression(request.Expression, UseAutoExpandRules: false, Timeout: timeoutMs);
			if (expr == null)
			{
				var nullResp = new DebuggerEvaluateExprResponse
				{
					VsInstanceId = _vsInstanceId,
					Expression = request.Expression,
					Value = "<evaluation produced no result>",
					Type = "unknown",
					IsValid = false,
					FrameIndex = targetFrameIndex
				};
				CheckOptimizationWarning(dte, nullResp.Value, false, nullResp.Warnings);
				return nullResp;
			}

			string val = expr.Value ?? string.Empty;
			string type = expr.Type ?? string.Empty;
			bool isValid = expr.IsValidValue;

			var resp = new DebuggerEvaluateExprResponse
			{
				VsInstanceId = _vsInstanceId,
				Expression = request.Expression,
				Value = val,
				Type = type,
				IsValid = isValid,
				FrameIndex = targetFrameIndex
			};
			CheckOptimizationWarning(dte, val, isValid, resp.Warnings);
			return resp;
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not OperationCanceledException)
		{
			var errResp = new DebuggerEvaluateExprResponse
			{
				VsInstanceId = _vsInstanceId,
				Expression = request.Expression,
				Value = $"<Evaluation error: {ex.Message}>",
				Type = "error",
				IsValid = false,
				FrameIndex = targetFrameIndex
			};
			CheckOptimizationWarning(dte, ex.Message, false, errResp.Warnings);
			return errResp;
		}
		finally
		{
			if (switchedFrame && originalFrame != null)
			{
				try
				{
					debugger.CurrentStackFrame = originalFrame;
				}
				catch
				{
				}
			}
		}
	}

	public async Task<DebuggerExecutionResponse> StepOverAsync(
		DebuggerStepRequest request,
		CancellationToken cancellationToken)
	{
		return await ExecuteControlCommandAsync("step_over", cancellationToken, debugger =>
		{
			if (debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
			{
				throw new DebuggerProviderException(
					BridgeErrorCodes.DebuggerNotPaused,
					$"The debugger is not currently in break mode (current mode: {GetModeString(debugger.CurrentMode)}). StepOver is only valid when paused.");
			}

			debugger.StepOver(request.WaitForBreak);
		});
	}

	public async Task<DebuggerExecutionResponse> StepIntoAsync(
		DebuggerStepRequest request,
		CancellationToken cancellationToken)
	{
		return await ExecuteControlCommandAsync("step_into", cancellationToken, debugger =>
		{
			if (debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
			{
				throw new DebuggerProviderException(
					BridgeErrorCodes.DebuggerNotPaused,
					$"The debugger is not currently in break mode (current mode: {GetModeString(debugger.CurrentMode)}). StepInto is only valid when paused.");
			}

			debugger.StepInto(request.WaitForBreak);
		});
	}

	public async Task<DebuggerExecutionResponse> StepOutAsync(
		DebuggerStepRequest request,
		CancellationToken cancellationToken)
	{
		return await ExecuteControlCommandAsync("step_out", cancellationToken, debugger =>
		{
			if (debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
			{
				throw new DebuggerProviderException(
					BridgeErrorCodes.DebuggerNotPaused,
					$"The debugger is not currently in break mode (current mode: {GetModeString(debugger.CurrentMode)}). StepOut is only valid when paused.");
			}

			debugger.StepOut(request.WaitForBreak);
		});
	}

	public async Task<DebuggerExecutionResponse> ContinueAsync(
		DebuggerContinueRequest request,
		CancellationToken cancellationToken)
	{
		return await ExecuteControlCommandAsync("continue", cancellationToken, debugger =>
		{
			if (debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
			{
				if (debugger.CurrentMode == dbgDebugMode.dbgRunMode)
				{
					throw new DebuggerProviderException(
						BridgeErrorCodes.DebuggerNotPaused,
						"The debugger is already running.");
				}

				throw new DebuggerProviderException(
					BridgeErrorCodes.DebuggerNotDebugging,
					"The debugger is not active (design mode). Start debugging before calling continue.");
			}

			debugger.Go(request.WaitForBreak);
		});
	}

	public async Task<DebuggerExecutionResponse> PauseAsync(
		DebuggerPauseRequest request,
		CancellationToken cancellationToken)
	{
		return await ExecuteControlCommandAsync("pause", cancellationToken, debugger =>
		{
			if (debugger.CurrentMode == dbgDebugMode.dbgBreakMode)
			{
				return;
			}

			if (debugger.CurrentMode == dbgDebugMode.dbgDesignMode)
			{
				throw new DebuggerProviderException(
					BridgeErrorCodes.DebuggerNotDebugging,
					"The debugger is not active (design mode). Cannot pause.");
			}

			debugger.Break(request.WaitForBreak);
		});
	}

	public async Task<DebuggerExecutionResponse> StopAsync(
		DebuggerStopRequest request,
		CancellationToken cancellationToken)
	{
		return await ExecuteControlCommandAsync("stop", cancellationToken, debugger =>
		{
			if (debugger.CurrentMode == dbgDebugMode.dbgDesignMode)
			{
				return;
			}

			debugger.Stop(request.WaitForStop);
		});
	}

	public async Task<DebuggerExecutionResponse> StartAsync(
		DebuggerStartRequest request,
		CancellationToken cancellationToken)
	{
		if (!_executionLock.Wait(0))
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.DebuggerBusy,
				"A debugger execution control operation is already in progress.");
		}

		try
		{
			await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
			var (dte, debugger) = await GetDteAndDebuggerAsync(cancellationToken);

			if (!dte.Solution?.IsOpen ?? true)
			{
				throw new DebuggerProviderException(
					BridgeErrorCodes.SolutionNotOpen,
					"No Visual Studio solution is open. Open a solution before starting debugging.");
			}

			if (debugger.CurrentMode != dbgDebugMode.dbgDesignMode)
			{
				throw new DebuggerProviderException(
					BridgeErrorCodes.DebuggerAlreadyRunning,
					$"The debugger is already running (current mode: {GetModeString(debugger.CurrentMode)}). Use continue or step instead.");
			}

			var previousMode = GetModeString(debugger.CurrentMode);
			debugger.Go(WaitForBreakOrEnd: false);

			if (request.WaitForBreak)
			{
				var timeoutMs = Clamp(request.TimeoutMs ?? 5000, 500, 30000);
				var sw = System.Diagnostics.Stopwatch.StartNew();
				while (sw.ElapsedMilliseconds < timeoutMs)
				{
					await Task.Delay(100, cancellationToken);
					await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
					if (debugger.CurrentMode == dbgDebugMode.dbgBreakMode || debugger.CurrentMode == dbgDebugMode.dbgDesignMode)
					{
						break;
					}
				}
			}

			return CaptureExecutionResult(debugger, "start", previousMode);
		}
		finally
		{
			_executionLock.Release();
		}
	}

	public async Task<DebuggerEvaluateExpressionsResponse> EvaluateExpressionsAsync(
		DebuggerEvaluateExpressionsRequest request,
		CancellationToken cancellationToken)
	{
		if (request.Expressions == null || request.Expressions.Count == 0)
		{
			throw new DebuggerProviderException(BridgeErrorCodes.InvalidRequest, "Expressions list cannot be empty.");
		}

		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var (dte, debugger) = await GetDteAndDebuggerAsync(cancellationToken);

		if (debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.DebuggerNotPaused,
				$"The debugger is not currently in break mode (current mode: {GetModeString(debugger.CurrentMode)}). Expression evaluation is only available when paused at a breakpoint or exception.");
		}

		var targetFrameIndex = request.FrameIndex ?? 0;
		var timeoutMs = Clamp(request.TimeoutMs ?? DefaultTimeoutMs, 100, MaxAllowedTimeoutMs);

		StackFrame? originalFrame = null;
		var switchedFrame = false;

		try
		{
			if (targetFrameIndex > 0 && debugger.CurrentThread?.StackFrames != null)
			{
				originalFrame = debugger.CurrentStackFrame;
				var currentIndex = 0;
				foreach (StackFrame frame in debugger.CurrentThread.StackFrames)
				{
					if (currentIndex == targetFrameIndex)
					{
						debugger.CurrentStackFrame = frame;
						switchedFrame = true;
						break;
					}

					currentIndex++;
				}
			}

			var results = new List<DebuggerExpressionItemResult>();
			foreach (var exprStr in request.Expressions)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (string.IsNullOrWhiteSpace(exprStr))
				{
					continue;
				}

				try
				{
					var expr = debugger.GetExpression(exprStr, UseAutoExpandRules: false, Timeout: timeoutMs);
					if (expr == null)
					{
						results.Add(new DebuggerExpressionItemResult
						{
							Expression = exprStr,
							Value = "<evaluation produced no result>",
							Type = "unknown",
							IsValid = false
						});
					}
					else
					{
						results.Add(new DebuggerExpressionItemResult
						{
							Expression = exprStr,
							Value = expr.Value ?? string.Empty,
							Type = expr.Type ?? string.Empty,
							IsValid = expr.IsValidValue
						});
					}
				}
				catch (Exception ex) when (ex is not OutOfMemoryException && ex is not OperationCanceledException)
				{
					results.Add(new DebuggerExpressionItemResult
					{
						Expression = exprStr,
						Value = $"<Evaluation error: {ex.Message}>",
						Type = "error",
						IsValid = false,
						Error = ex.Message
					});
				}
			}

			var warnings = new List<BridgeWarning>();
			foreach (var res in results)
			{
				CheckOptimizationWarning(dte, res.Value, res.IsValid, warnings);
				if (warnings.Count > 0)
				{
					break;
				}
			}

			return new DebuggerEvaluateExpressionsResponse
			{
				VsInstanceId = _vsInstanceId,
				FrameIndex = targetFrameIndex,
				Results = results,
				Warnings = warnings
			};
		}
		finally
		{
			if (switchedFrame && originalFrame != null)
			{
				try
				{
					debugger.CurrentStackFrame = originalFrame;
				}
				catch
				{
				}
			}
		}
	}

	public async Task<DebuggerGetLocalsResponse> GetLocalsAsync(
		DebuggerGetLocalsRequest request,
		CancellationToken cancellationToken)
	{
		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var (dte, debugger) = await GetDteAndDebuggerAsync(cancellationToken);

		if (debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.DebuggerNotPaused,
				$"The debugger is not currently in break mode (current mode: {GetModeString(debugger.CurrentMode)}). Locals inspection is only available when paused.");
		}

		var targetFrameIndex = request.FrameIndex ?? 0;
		var maxCount = Clamp(request.MaxCount ?? 50, 1, 200);

		StackFrame? targetFrame = null;
		if (debugger.CurrentThread?.StackFrames != null)
		{
			var currentIndex = 0;
			foreach (StackFrame frame in debugger.CurrentThread.StackFrames)
			{
				if (currentIndex == targetFrameIndex)
				{
					targetFrame = frame;
					break;
				}
				currentIndex++;
			}
		}

		if (targetFrame == null)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.DebuggerUnavailable,
				$"Stack frame at index {targetFrameIndex} was not found.");
		}

		var variables = new List<DebuggerVariableInfo>();
		var totalCount = 0;
		var warnings = new List<BridgeWarning>();

		try
		{
			var args = targetFrame.Arguments;
			if (args != null)
			{
				foreach (Expression arg in args)
				{
					cancellationToken.ThrowIfCancellationRequested();
					totalCount++;
					if (variables.Count < maxCount)
					{
						variables.Add(new DebuggerVariableInfo
						{
							Name = arg.Name ?? string.Empty,
							Value = arg.Value ?? string.Empty,
							Type = arg.Type ?? string.Empty,
							IsArgument = true
						});
					}
				}
			}
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not OperationCanceledException)
		{
			warnings.Add(new BridgeWarning
			{
				Code = "arguments_read_failed",
				Message = $"Failed to read function arguments: {ex.Message}"
			});
		}

		try
		{
			var locals = targetFrame.Locals;
			if (locals != null)
			{
				foreach (Expression loc in locals)
				{
					cancellationToken.ThrowIfCancellationRequested();
					totalCount++;
					if (variables.Count < maxCount)
					{
						variables.Add(new DebuggerVariableInfo
						{
							Name = loc.Name ?? string.Empty,
							Value = loc.Value ?? string.Empty,
							Type = loc.Type ?? string.Empty,
							IsArgument = false
						});
					}
				}
			}
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not OperationCanceledException)
		{
			warnings.Add(new BridgeWarning
			{
				Code = "locals_read_failed",
				Message = $"Failed to read function locals: {ex.Message}"
			});
		}

		foreach (var v in variables)
		{
			CheckOptimizationWarning(dte, v.Value, true, warnings);
			if (warnings.Exists(w => w.Code == "variable_optimized_in_release"))
			{
				break;
			}
		}

		return new DebuggerGetLocalsResponse
		{
			VsInstanceId = _vsInstanceId,
			FrameIndex = targetFrameIndex,
			Variables = variables,
			TotalCount = totalCount,
			Truncated = totalCount > variables.Count,
			Warnings = warnings
		};
	}

	public async Task<DebuggerReadMemoryResponse> ReadMemoryAsync(
		DebuggerReadMemoryRequest request,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(request.Address))
		{
			throw new DebuggerProviderException(BridgeErrorCodes.InvalidRequest, "Address is required.");
		}

		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var debugger = await GetDebuggerAsync(cancellationToken);

		if (debugger.CurrentMode == dbgDebugMode.dbgDesignMode)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.DebuggerNotDebugging,
				"Cannot read process memory when not debugging (debugger is in design mode).");
		}

		var currentProcess = debugger.CurrentProcess;
		if (currentProcess == null)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.ProcessNotFound,
				"No debugged process is currently active.");
		}

		var pid = currentProcess.ProcessID;
		var rawAddress = request.Address.Trim();
		ulong resolvedAddr = 0;
		var parsed = false;

		// 1. Try parse direct hex number e.g. "0x00401000", "0x7FFE1234", "00401000"
		if (rawAddress.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			parsed = ulong.TryParse(rawAddress.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out resolvedAddr);
		}
		else if (rawAddress.All(c => Uri.IsHexDigit(c)) && rawAddress.Length >= 4)
		{
			parsed = ulong.TryParse(rawAddress, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out resolvedAddr);
		}

		// 2. If not parsed directly, try evaluate as expression (e.g. pointer variable "pBuffer" or "&variable")
		if (!parsed)
		{
			try
			{
				var expr = debugger.GetExpression(rawAddress, UseAutoExpandRules: false, Timeout: 2000);
				if (expr != null && expr.IsValidValue && !string.IsNullOrWhiteSpace(expr.Value))
				{
					var val = expr.Value.Trim();
					var match = Regex.Match(val, @"0x[0-9a-fA-F]+");
					if (match.Success)
					{
						parsed = ulong.TryParse(match.Value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out resolvedAddr);
					}
					else if (ulong.TryParse(val, out var decAddr))
					{
						resolvedAddr = decAddr;
						parsed = true;
					}
				}
			}
			catch { }
		}

		if (!parsed || resolvedAddr == 0)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.InvalidMemoryAddress,
				$"Unable to resolve memory address from '{request.Address}'. Ensure the variable or address is accessible in the current scope.");
		}

		var byteCount = Clamp(request.ByteCount, 1, 4096);
		var warnings = new List<BridgeWarning>();

		// 3. Read memory via Windows ReadProcessMemory
		var hProcess = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, pid);
		if (hProcess == IntPtr.Zero)
		{
			var lastError = Marshal.GetLastWin32Error();
			throw new DebuggerProviderException(
				BridgeErrorCodes.MemoryReadFailed,
				$"Failed to open process (PID: {pid}) for reading memory. Win32 error code: {lastError}");
		}

		byte[] buffer = new byte[byteCount];
		int bytesRead = 0;

		try
		{
			var success = ReadProcessMemory(hProcess, (IntPtr)(long)resolvedAddr, buffer, byteCount, out var bytesReadPtr);
			bytesRead = (int)bytesReadPtr;

			if (!success && bytesRead == 0)
			{
				var lastError = Marshal.GetLastWin32Error();
				throw new DebuggerProviderException(
					BridgeErrorCodes.MemoryReadFailed,
					$"ReadProcessMemory failed at address 0x{resolvedAddr:X16} (size: {byteCount}). Win32 error code: {lastError}");
			}

			if (bytesRead < byteCount)
			{
				Array.Resize(ref buffer, bytesRead);
				warnings.Add(new BridgeWarning
				{
					Code = "partial_memory_read",
					Message = $"Requested {byteCount} bytes, but only {bytesRead} bytes could be read (likely reached end of valid virtual memory page)."
				});
			}
		}
		finally
		{
			CloseHandle(hProcess);
		}

		// 4. Format representations
		var hexBytes = string.Join(" ", buffer.Select(b => b.ToString("X2")));
		var base64Data = Convert.ToBase64String(buffer);
		var asciiRep = new string(buffer.Select(b => b >= 32 && b <= 126 ? (char)b : '.').ToArray());

		var sb = new StringBuilder();
		for (int i = 0; i < buffer.Length; i += 16)
		{
			var lineAddr = resolvedAddr + (ulong)i;
			var lineBytesCount = Math.Min(16, buffer.Length - i);
			sb.AppendFormat("{0:X8}  ", lineAddr);

			for (int j = 0; j < 16; j++)
			{
				if (j == 8) sb.Append(' ');
				if (j < lineBytesCount)
				{
					sb.AppendFormat("{0:X2} ", buffer[i + j]);
				}
				else
				{
					sb.Append("   ");
				}
			}

			sb.Append(" |");
			for (int j = 0; j < lineBytesCount; j++)
			{
				var b = buffer[i + j];
				sb.Append(b >= 32 && b <= 126 ? (char)b : '.');
			}
			sb.Append('|');
			if (i + 16 < buffer.Length) sb.AppendLine();
		}

		return new DebuggerReadMemoryResponse
		{
			VsInstanceId = _vsInstanceId,
			ProcessId = pid,
			ResolvedAddress = $"0x{resolvedAddr:X16}",
			ByteCount = bytesRead,
			HexBytes = hexBytes,
			HexDump = sb.ToString(),
			AsciiRepresentation = asciiRep,
			Base64Data = base64Data,
			Warnings = warnings
		};
	}

	public async Task<DebuggerGetThreadsResponse> GetThreadsAsync(
		DebuggerGetThreadsRequest request,
		CancellationToken cancellationToken)
	{
		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var debugger = await GetDebuggerAsync(cancellationToken);

		if (debugger.CurrentMode == dbgDebugMode.dbgDesignMode)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.DebuggerNotDebugging,
				"The debugger is not active (design mode). Threads inspection is only available during debugging.");
		}

		int? currentThreadId = null;
		try
		{
			currentThreadId = debugger.CurrentThread?.ID;
		}
		catch
		{
		}

		var threadList = new List<ThreadInfo>();

		try
		{
			var threads = debugger.CurrentProgram?.Threads;
			if (threads == null && debugger.CurrentProcess?.Programs != null)
			{
				foreach (Program prog in debugger.CurrentProcess.Programs)
				{
					if (prog.Threads != null)
					{
						threads = prog.Threads;
						break;
					}
				}
			}

			if (threads != null)
			{
				foreach (EnvDTE.Thread th in threads)
				{
					cancellationToken.ThrowIfCancellationRequested();

					int id = 0;
					string name = string.Empty;
					bool isAlive = true;
					int suspendedCount = 0;
					string? priority = null;
					StackFrameInfo? topFrame = null;

					bool isFrozen = false;

					try { id = th.ID; } catch { }
					try { name = th.Name ?? string.Empty; } catch { }
					try { isAlive = th.IsAlive; } catch { }
					try { suspendedCount = th.SuspendCount; } catch { }
					try { priority = th.Priority; } catch { }
					try { isFrozen = th.IsFrozen; } catch { }

					if (debugger.CurrentMode == dbgDebugMode.dbgBreakMode && th.StackFrames != null)
					{
						try
						{
							foreach (StackFrame sf in th.StackFrames)
							{
								topFrame = ReadStackFrame(sf, 0);
								break;
							}
						}
						catch
						{
						}
					}

					threadList.Add(new ThreadInfo
					{
						Id = id,
						Name = name,
						IsAlive = isAlive,
						IsCurrent = currentThreadId.HasValue && id == currentThreadId.Value,
						SuspendedCount = suspendedCount,
						Priority = priority,
						TopFrame = topFrame,
						IsFrozen = isFrozen
					});
				}
			}
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not OperationCanceledException)
		{
			if (threadList.Count == 0 && debugger.CurrentThread != null)
			{
				try
				{
					var th = debugger.CurrentThread;
					bool fallbackFrozen = false;
					try { fallbackFrozen = th.IsFrozen; } catch { }
					threadList.Add(new ThreadInfo
					{
						Id = th.ID,
						Name = th.Name ?? string.Empty,
						IsAlive = th.IsAlive,
						IsCurrent = true,
						SuspendedCount = th.SuspendCount,
						Priority = th.Priority,
						IsFrozen = fallbackFrozen
					});
				}
				catch
				{
				}
			}
		}

		return new DebuggerGetThreadsResponse
		{
			VsInstanceId = _vsInstanceId,
			CurrentThreadId = currentThreadId,
			TotalCount = threadList.Count,
			Threads = threadList
		};
	}

	public async Task<DebuggerThreadControlResponse> FreezeThreadAsync(
		DebuggerThreadControlRequest request,
		CancellationToken cancellationToken)
	{
		if (request.ThreadId <= 0)
		{
			throw new DebuggerProviderException(BridgeErrorCodes.InvalidRequest, "A valid positive ThreadId is required.");
		}

		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var debugger = await GetDebuggerAsync(cancellationToken);

		if (debugger.CurrentMode == dbgDebugMode.dbgDesignMode)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.DebuggerNotDebugging,
				"The debugger is not active (design mode). Thread freezing is only available during debugging.");
		}

		var thread = FindExactThread(debugger, request.ThreadId)
			?? throw new DebuggerProviderException(
				BridgeErrorCodes.ThreadNotFound,
				$"Thread with ID {request.ThreadId} was not found.");

		try
		{
			thread.Freeze();
		}
		catch (Exception ex)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.InternalError,
				$"Failed to freeze thread {request.ThreadId}: {ex.Message}",
				ex);
		}

		bool isFrozen = false;
		int suspendCount = 0;
		try { isFrozen = thread.IsFrozen; } catch { }
		try { suspendCount = thread.SuspendCount; } catch { }

		return new DebuggerThreadControlResponse
		{
			VsInstanceId = _vsInstanceId,
			ThreadId = request.ThreadId,
			Action = "freeze",
			IsFrozen = isFrozen,
			SuspendedCount = suspendCount,
			Success = true
		};
	}

	public async Task<DebuggerThreadControlResponse> ThawThreadAsync(
		DebuggerThreadControlRequest request,
		CancellationToken cancellationToken)
	{
		if (request.ThreadId <= 0)
		{
			throw new DebuggerProviderException(BridgeErrorCodes.InvalidRequest, "A valid positive ThreadId is required.");
		}

		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var debugger = await GetDebuggerAsync(cancellationToken);

		if (debugger.CurrentMode == dbgDebugMode.dbgDesignMode)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.DebuggerNotDebugging,
				"The debugger is not active (design mode). Thread thawing is only available during debugging.");
		}

		var thread = FindExactThread(debugger, request.ThreadId)
			?? throw new DebuggerProviderException(
				BridgeErrorCodes.ThreadNotFound,
				$"Thread with ID {request.ThreadId} was not found.");

		try
		{
			thread.Thaw();
		}
		catch (Exception ex)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.InternalError,
				$"Failed to thaw thread {request.ThreadId}: {ex.Message}",
				ex);
		}

		bool isFrozen = false;
		int suspendCount = 0;
		try { isFrozen = thread.IsFrozen; } catch { }
		try { suspendCount = thread.SuspendCount; } catch { }

		return new DebuggerThreadControlResponse
		{
			VsInstanceId = _vsInstanceId,
			ThreadId = request.ThreadId,
			Action = "thaw",
			IsFrozen = isFrozen,
			SuspendedCount = suspendCount,
			Success = true
		};
	}

	public async Task<DebuggerSetNextStatementResponse> SetNextStatementAsync(
		DebuggerSetNextStatementRequest request,
		CancellationToken cancellationToken)
	{
		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var (dte, debugger) = await GetDteAndDebuggerAsync(cancellationToken);

		if (debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.DebuggerNotPaused,
				"The debugger must be paused (break mode) to set the next statement.");
		}

		string finalFilePath = string.Empty;
		int finalLine = request.Line;
		int finalColumn = request.Column ?? 1;

		if (!string.IsNullOrWhiteSpace(request.FilePath))
		{
			if (request.Line <= 0)
			{
				throw new DebuggerProviderException(BridgeErrorCodes.InvalidRequest, "A valid line number (>= 1) is required when specifying filePath.");
			}

			var navTarget = request.FilePath.Trim();
			if (!Path.IsPathRooted(navTarget) && !string.IsNullOrEmpty(dte.Solution?.FullName))
			{
				var slnDir = Path.GetDirectoryName(dte.Solution.FullName);
				if (!string.IsNullOrEmpty(slnDir))
				{
					navTarget = Path.Combine(slnDir, navTarget);
				}
			}

			if (!File.Exists(navTarget))
			{
				throw new DebuggerProviderException(
					BridgeErrorCodes.FileNotFound,
					$"Target file does not exist: {request.FilePath}");
			}

			finalFilePath = Path.GetFullPath(navTarget);
			var window = dte.ItemOperations.OpenFile(finalFilePath);
			window?.Activate();

			if (dte.ActiveDocument?.Selection is TextSelection selection)
			{
				selection.GotoLine(finalLine, true);
				selection.MoveToDisplayColumn(finalLine, finalColumn, false);
			}
		}
		else
		{
			if (dte.ActiveDocument?.Selection is TextSelection selection)
			{
				finalFilePath = dte.ActiveDocument.FullName ?? string.Empty;
				if (request.Line > 0)
				{
					selection.GotoLine(finalLine, true);
					selection.MoveToDisplayColumn(finalLine, finalColumn, false);
				}
				else
				{
					finalLine = selection.CurrentLine;
					finalColumn = selection.CurrentColumn;
				}
			}
			else
			{
				throw new DebuggerProviderException(
					BridgeErrorCodes.ActiveDocumentUnavailable,
					"No active document found to set next statement.");
			}
		}

		try
		{
			debugger.SetNextStatement();
		}
		catch (Exception ex)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.InvalidNextStatement,
				$"Cannot set next statement to line {finalLine}: {ex.Message}",
				ex);
		}

		StackFrameInfo? topFrame = null;
		try
		{
			if (debugger.CurrentStackFrame != null)
			{
				topFrame = ReadStackFrame(debugger.CurrentStackFrame, 0);
			}
		}
		catch
		{
		}

		return new DebuggerSetNextStatementResponse
		{
			VsInstanceId = _vsInstanceId,
			FilePath = finalFilePath,
			Line = finalLine,
			Column = finalColumn,
			Success = true,
			TopFrame = topFrame
		};
	}

	public async Task<DebuggerGetExceptionInfoResponse> GetExceptionInfoAsync(
		DebuggerGetExceptionInfoRequest request,
		CancellationToken cancellationToken)
	{
		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var debugger = await GetDebuggerAsync(cancellationToken);

		if (debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.DebuggerNotPaused,
				"Cannot get exception info while debugger is not paused in break mode.");
		}

		var response = new DebuggerGetExceptionInfoResponse
		{
			VsInstanceId = _vsInstanceId,
			HasException = false
		};

		// 1. Try evaluating $exception (standard for CLR / .NET debugging)
		try
		{
			var expr = debugger.GetExpression("$exception", UseAutoExpandRules: false, Timeout: 2000);
			if (expr != null && expr.IsValidValue && !string.IsNullOrEmpty(expr.Value) &&
				!string.Equals(expr.Value, "null", StringComparison.OrdinalIgnoreCase))
			{
				response.HasException = true;
				response.ExceptionType = expr.Type;
				response.RawDetails = expr.Value;

				// Extract Message
				try
				{
					var msgExpr = debugger.GetExpression("$exception.Message", UseAutoExpandRules: false, Timeout: 1000);
					if (msgExpr != null && msgExpr.IsValidValue)
					{
						var val = msgExpr.Value;
						if (val.StartsWith("\"") && val.EndsWith("\"") && val.Length >= 2)
						{
							val = val.Substring(1, val.Length - 2);
						}
						response.Message = val;
					}
				}
				catch { }

				// Extract Full Type Name
				try
				{
					var typeExpr = debugger.GetExpression("$exception.GetType().FullName", UseAutoExpandRules: false, Timeout: 1000);
					if (typeExpr != null && typeExpr.IsValidValue)
					{
						var val = typeExpr.Value;
						if (val.StartsWith("\"") && val.EndsWith("\"") && val.Length >= 2)
						{
							val = val.Substring(1, val.Length - 2);
						}
						if (!string.IsNullOrWhiteSpace(val))
						{
							response.ExceptionType = val;
						}
					}
				}
				catch { }

				// Extract HResult
				try
				{
					var hrExpr = debugger.GetExpression("$exception.HResult", UseAutoExpandRules: false, Timeout: 1000);
					if (hrExpr != null && hrExpr.IsValidValue)
					{
						if (int.TryParse(hrExpr.Value, out var hrInt))
						{
							response.HResult = string.Format("0x{0:X8}", hrInt);
						}
						else
						{
							response.HResult = hrExpr.Value;
						}
					}
				}
				catch { }

				// Extract Source
				try
				{
					var srcExpr = debugger.GetExpression("$exception.Source", UseAutoExpandRules: false, Timeout: 1000);
					if (srcExpr != null && srcExpr.IsValidValue)
					{
						var val = srcExpr.Value;
						if (val.StartsWith("\"") && val.EndsWith("\"") && val.Length >= 2)
						{
							val = val.Substring(1, val.Length - 2);
						}
						response.Source = val;
					}
				}
				catch { }

				// Extract StackTrace
				try
				{
					var stExpr = debugger.GetExpression("$exception.StackTrace", UseAutoExpandRules: false, Timeout: 1000);
					if (stExpr != null && stExpr.IsValidValue)
					{
						var val = stExpr.Value;
						if (val.StartsWith("\"") && val.EndsWith("\"") && val.Length >= 2)
						{
							val = System.Text.RegularExpressions.Regex.Unescape(val.Substring(1, val.Length - 2));
						}
						response.StackTrace = val;
					}
				}
				catch { }

				// Extract InnerException
				try
				{
					var innerExpr = debugger.GetExpression("$exception.InnerException", UseAutoExpandRules: false, Timeout: 1000);
					if (innerExpr != null && innerExpr.IsValidValue && !string.Equals(innerExpr.Value, "null", StringComparison.OrdinalIgnoreCase))
					{
						response.InnerException = $"{innerExpr.Type}: {innerExpr.Value}";
					}
				}
				catch { }

				return response;
			}
		}
		catch { }

		// 2. Fallback: Check if LastBreakReason was an exception (e.g. C++ or native unhandled)
		if (debugger.LastBreakReason == dbgEventReason.dbgEventReasonExceptionThrown ||
			debugger.LastBreakReason == dbgEventReason.dbgEventReasonExceptionNotHandled)
		{
			response.HasException = true;
			response.Message = "Debugger paused due to an exception (native/unmanaged or CLR first-chance).";
			try
			{
				var errExpr = debugger.GetExpression("$err,hr", UseAutoExpandRules: false, Timeout: 1000);
				if (errExpr != null && errExpr.IsValidValue)
				{
					response.HResult = errExpr.Value;
					response.RawDetails = errExpr.Value;
				}
			}
			catch { }
		}

		return response;
	}

	public async Task<DebuggerGetProcessesResponse> GetProcessesAsync(
		DebuggerGetProcessesRequest request,
		CancellationToken cancellationToken)
	{
		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var debugger = await GetDebuggerAsync(cancellationToken);

		var list = new List<ProcessInfo>();
		var source = request.OnlyDebugged ? debugger.DebuggedProcesses : debugger.LocalProcesses;
		var maxCount = Clamp(request.MaxCount ?? 50, 1, 200);

		if (source != null)
		{
			foreach (EnvDTE.Process proc in source)
			{
				try
				{
					var pid = proc.ProcessID;
					var name = proc.Name ?? string.Empty;

					if (request.ProcessId.HasValue && pid != request.ProcessId.Value)
					{
						continue;
					}

					if (!string.IsNullOrWhiteSpace(request.ProcessName))
					{
						var match = name.IndexOf(request.ProcessName, StringComparison.OrdinalIgnoreCase) >= 0;
						if (!match)
						{
							continue;
						}
					}

					string? userName = null;
					var isBeingDebugged = false;
					string? transport = null;

					if (proc is Process2 proc2)
					{
						try { userName = proc2.UserName; } catch { }
						try { isBeingDebugged = proc2.IsBeingDebugged; } catch { }
						try { transport = proc2.TransportQualifier; } catch { }
					}

					list.Add(new ProcessInfo
					{
						ProcessId = pid,
						Name = name,
						UserName = string.IsNullOrWhiteSpace(userName) ? null : userName,
						IsBeingDebugged = isBeingDebugged,
						TransportQualifier = string.IsNullOrWhiteSpace(transport) ? null : transport
					});

					if (list.Count >= maxCount)
					{
						break;
					}
				}
				catch
				{
					// Ignore individual processes that cannot be inspected due to permissions
				}
			}
		}

		return new DebuggerGetProcessesResponse
		{
			VsInstanceId = _vsInstanceId,
			TotalCount = list.Count,
			ReturnedCount = list.Count,
			Processes = list
		};
	}

	public async Task<DebuggerAttachResponse> AttachProcessAsync(
		DebuggerAttachRequest request,
		CancellationToken cancellationToken)
	{
		if (!_executionLock.Wait(0))
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.DebuggerBusy,
				"A debugger execution control operation is already in progress.");
		}

		try
		{
			await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
			var debugger = await GetDebuggerAsync(cancellationToken);

			EnvDTE.Process? targetProc = null;
			var localProcesses = debugger.LocalProcesses;
			if (localProcesses != null)
			{
				foreach (EnvDTE.Process proc in localProcesses)
				{
					try
					{
						if (request.ProcessId.HasValue && proc.ProcessID == request.ProcessId.Value)
						{
							targetProc = proc;
							break;
						}

						if (!request.ProcessId.HasValue && !string.IsNullOrWhiteSpace(request.ProcessName))
						{
							if (proc.Name != null && proc.Name.IndexOf(request.ProcessName, StringComparison.OrdinalIgnoreCase) >= 0)
							{
								targetProc = proc;
								break;
							}
						}
					}
					catch { }
				}
			}

			if (targetProc == null)
			{
				var identifier = request.ProcessId.HasValue ? $"PID {request.ProcessId.Value}" : $"Name '{request.ProcessName}'";
				throw new DebuggerProviderException(
					BridgeErrorCodes.ProcessNotFound,
					$"Target process ({identifier}) was not found in local running processes.");
			}

			return await AttachProcessCoreAsync(
				debugger,
				targetProc,
				request.Engines,
				request.WaitForBreak,
				request.BreakTimeoutMs,
				cancellationToken);
		}
		finally
		{
			_executionLock.Release();
		}
	}

	public async Task<DebuggerFindSolutionProcessesResponse> FindSolutionProcessesAsync(
		DebuggerFindSolutionProcessesRequest request,
		CancellationToken cancellationToken)
	{
		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var (dte, debugger) = await GetDteAndDebuggerAsync(cancellationToken);

		var solutionProjects = new List<(string Name, string? FilePath, bool IsStartup, List<string> OutputNames)>();
		var warnings = new List<BridgeWarning>();

		if (dte.Solution?.IsOpen != true)
		{
			warnings.Add(new BridgeWarning
			{
				Code = "no_solution_open",
				Message = "No solution is currently open in Visual Studio."
			});
			return new DebuggerFindSolutionProcessesResponse
			{
				VsInstanceId = _vsInstanceId,
				Processes = new List<SolutionProcessInfo>(),
				TotalCount = 0,
				Warnings = warnings
			};
		}

		var startupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			if (dte.Solution.SolutionBuild?.StartupProjects is Array startupArray)
			{
				foreach (var item in startupArray)
				{
					if (item is string startupStr && !string.IsNullOrWhiteSpace(startupStr))
					{
						startupNames.Add(startupStr);
						startupNames.Add(Path.GetFileName(startupStr));
						startupNames.Add(Path.GetFileNameWithoutExtension(startupStr));
					}
				}
			}
		}
		catch { }

		if (dte.Solution.Projects != null)
		{
			foreach (Project proj in dte.Solution.Projects)
			{
				CollectSolutionProjects(proj, startupNames, solutionProjects);
			}
		}

		var matchedProcesses = new List<SolutionProcessInfo>();
		var localProcesses = debugger.LocalProcesses;

		if (localProcesses != null)
		{
			foreach (EnvDTE.Process proc in localProcesses)
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					var procName = proc.Name;
					if (string.IsNullOrWhiteSpace(procName)) continue;

					var exeBaseName = Path.GetFileNameWithoutExtension(procName);

					var matchedProj = solutionProjects.Find(p =>
						string.Equals(p.Name, exeBaseName, StringComparison.OrdinalIgnoreCase) ||
						(!string.IsNullOrWhiteSpace(p.FilePath) && string.Equals(Path.GetFileNameWithoutExtension(p.FilePath), exeBaseName, StringComparison.OrdinalIgnoreCase)) ||
						p.OutputNames.Exists(on => string.Equals(on, exeBaseName, StringComparison.OrdinalIgnoreCase)));

					if (!string.IsNullOrEmpty(matchedProj.Name))
					{
						if (request.StartupOnly && !matchedProj.IsStartup)
						{
							continue;
						}

						bool isBeingDebugged = false;
						string? userName = null;
						if (proc is Process2 proc2)
						{
							try { isBeingDebugged = proc2.IsBeingDebugged; } catch { }
							try { userName = proc2.UserName; } catch { }
						}

						matchedProcesses.Add(new SolutionProcessInfo
						{
							ProcessId = proc.ProcessID,
							ProcessName = Path.GetFileName(procName),
							ProjectName = matchedProj.Name,
							ProjectFilePath = matchedProj.FilePath,
							IsStartupProject = matchedProj.IsStartup,
							IsBeingDebugged = isBeingDebugged,
							UserName = userName
						});
					}
				}
				catch { }
			}
		}

		return new DebuggerFindSolutionProcessesResponse
		{
			VsInstanceId = _vsInstanceId,
			Processes = matchedProcesses,
			TotalCount = matchedProcesses.Count,
			Warnings = warnings
		};
	}

	public async Task<DebuggerAutoAttachResponse> AutoAttachAsync(
		DebuggerAutoAttachRequest request,
		CancellationToken cancellationToken)
	{
		if (!_executionLock.Wait(0))
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.DebuggerBusy,
				"A debugger execution control operation is already in progress.");
		}

		try
		{
			await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
			var (dte, debugger) = await GetDteAndDebuggerAsync(cancellationToken);

			var findReq = new DebuggerFindSolutionProcessesRequest
			{
				StartupOnly = request.StartupOnly,
				VsInstanceId = request.VsInstanceId
			};

			var found = await FindSolutionProcessesAsync(findReq, cancellationToken);
			var candidates = found.Processes;

			if (request.ProcessNames != null && request.ProcessNames.Count > 0)
			{
				candidates = candidates.FindAll(p =>
					request.ProcessNames.Exists(filter =>
						p.ProcessName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
						p.ProjectName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0));
			}

			if (candidates.Count == 0)
			{
				var filterDesc = request.ProcessNames != null && request.ProcessNames.Count > 0
					? $" matching filters [{string.Join(", ", request.ProcessNames)}]"
					: string.Empty;
				throw new DebuggerProviderException(
					BridgeErrorCodes.NoSolutionProcessesFound,
					$"No running solution processes{filterDesc} were found to attach.");
			}

			var localProcesses = debugger.LocalProcesses;
			var attachedResults = new List<DebuggerAttachResponse>();
			var warnings = new List<BridgeWarning>();

			foreach (var candidate in candidates)
			{
				cancellationToken.ThrowIfCancellationRequested();

				EnvDTE.Process? targetProc = null;
				if (localProcesses != null)
				{
					foreach (EnvDTE.Process proc in localProcesses)
					{
						try
						{
							if (proc.ProcessID == candidate.ProcessId)
							{
								targetProc = proc;
								break;
							}
						}
						catch { }
					}
				}

				if (targetProc == null)
				{
					warnings.Add(new BridgeWarning
					{
						Code = "process_exited",
						Message = $"Candidate process {candidate.ProcessName} (PID: {candidate.ProcessId}) exited before attaching."
					});
					continue;
				}

				try
				{
					var attachResp = await AttachProcessCoreAsync(
						debugger,
						targetProc,
						request.Engines,
						waitForBreak: false,
						breakTimeoutMs: null,
						cancellationToken);

					attachedResults.Add(attachResp);
				}
				catch (Exception ex)
				{
					warnings.Add(new BridgeWarning
					{
						Code = "attach_failed",
						Message = $"Failed to attach to {candidate.ProcessName} (PID: {candidate.ProcessId}): {ex.Message}"
					});
				}
			}

			if (request.WaitForBreak)
			{
				var timeoutMs = Clamp(request.BreakTimeoutMs ?? 3000, 500, 30000);
				var sw = System.Diagnostics.Stopwatch.StartNew();
				while (sw.ElapsedMilliseconds < timeoutMs)
				{
					await Task.Delay(100, cancellationToken);
					await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
					if (debugger.CurrentMode == dbgDebugMode.dbgBreakMode || debugger.CurrentMode == dbgDebugMode.dbgDesignMode)
					{
						break;
					}
				}
			}

			return new DebuggerAutoAttachResponse
			{
				VsInstanceId = _vsInstanceId,
				AttachedCount = attachedResults.Count(p => p.IsDebugging),
				Processes = attachedResults,
				Warnings = warnings
			};
		}
		finally
		{
			_executionLock.Release();
		}
	}

	private async Task<DebuggerAttachResponse> AttachProcessCoreAsync(
		Debugger debugger,
		EnvDTE.Process targetProc,
		List<string>? engines,
		bool waitForBreak,
		int? breakTimeoutMs,
		CancellationToken cancellationToken)
	{
		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var warnings = new List<BridgeWarning>();
		var attachedEngines = new List<string>();

		if (targetProc is Process2 proc2 && proc2.IsBeingDebugged)
		{
			warnings.Add(new BridgeWarning
			{
				Code = "already_debugged",
				Message = $"Process {targetProc.Name} (PID: {targetProc.ProcessID}) is already being debugged by this instance."
			});
		}
		else
		{
			try
			{
				if (targetProc is Process2 proc2Engine && engines != null && engines.Count > 0)
				{
					var availableEngines = new List<EnvDTE80.Engine>();
					if (proc2Engine.Transport?.Engines != null)
					{
						foreach (EnvDTE80.Engine eng in proc2Engine.Transport.Engines)
						{
							availableEngines.Add(eng);
						}
					}

					var matchedEngines = new List<EnvDTE80.Engine>();
					foreach (var reqEngine in engines)
					{
						if (string.IsNullOrWhiteSpace(reqEngine)) continue;
						var trimmed = reqEngine.Trim();
						var match = availableEngines.Find(e =>
							string.Equals(e.Name, trimmed, StringComparison.OrdinalIgnoreCase) ||
							string.Equals(e.ID, trimmed, StringComparison.OrdinalIgnoreCase))
							?? availableEngines.Find(e =>
							e.Name != null && e.Name.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase) >= 0);

						if (match == null)
						{
							var availableNames = availableEngines.Select(e => e.Name).ToList();
							throw new DebuggerProviderException(
								BridgeErrorCodes.EngineNotFound,
								$"Debugger engine '{trimmed}' not found for process {targetProc.Name} (PID: {targetProc.ProcessID}). Available engines: {string.Join(", ", availableNames)}");
						}

						if (!matchedEngines.Contains(match))
						{
							matchedEngines.Add(match);
						}
					}

					if (matchedEngines.Count == 1)
					{
						proc2Engine.Attach2(matchedEngines[0]);
					}
					else
					{
						proc2Engine.Attach2(matchedEngines.ToArray());
					}

					attachedEngines = matchedEngines.Select(e => e.Name).ToList();
				}
				else
				{
					targetProc.Attach();
				}
			}
			catch (DebuggerProviderException)
			{
				throw;
			}
			catch (Exception ex)
			{
				throw new DebuggerProviderException(
					BridgeErrorCodes.DebuggerUnavailable,
					$"Failed to attach to process {targetProc.Name} (PID: {targetProc.ProcessID}): {ex.Message}",
					ex);
			}
		}

		if (waitForBreak)
		{
			var timeoutMs = Clamp(breakTimeoutMs ?? 3000, 500, 30000);
			var sw = System.Diagnostics.Stopwatch.StartNew();
			while (sw.ElapsedMilliseconds < timeoutMs)
			{
				await Task.Delay(100, cancellationToken);
				await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
				if (debugger.CurrentMode == dbgDebugMode.dbgBreakMode || debugger.CurrentMode == dbgDebugMode.dbgDesignMode)
				{
					break;
				}
			}
		}

		var isDebugging = debugger.CurrentMode != dbgDebugMode.dbgDesignMode;
		var currentMode = GetModeString(debugger.CurrentMode);
		string? breakReason = null;
		StackFrameInfo? topFrame = null;

		if (debugger.CurrentMode == dbgDebugMode.dbgBreakMode)
		{
			try
			{
				breakReason = GetBreakReasonString(debugger.LastBreakReason);
			}
			catch { }

			try
			{
				var frame = debugger.CurrentStackFrame;
				if (frame != null)
				{
					topFrame = ReadStackFrame(frame, 0);
				}
			}
			catch { }
		}

		return new DebuggerAttachResponse
		{
			VsInstanceId = _vsInstanceId,
			ProcessId = targetProc.ProcessID,
			ProcessName = targetProc.Name ?? string.Empty,
			CurrentMode = currentMode,
			IsDebugging = isDebugging,
			LastBreakReason = breakReason,
			TopFrame = topFrame,
			AttachedEngines = attachedEngines,
			Warnings = warnings
		};
	}

	private static void CollectSolutionProjects(
		Project proj,
		HashSet<string> startupNames,
		List<(string Name, string? FilePath, bool IsStartup, List<string> OutputNames)> result)
	{
		ThreadHelper.ThrowIfNotOnUIThread();
		if (proj == null) return;

		try
		{
			if (string.Equals(proj.Kind, "{66A26720-8FB5-11D2-AA7E-00C04F688DDE}", StringComparison.OrdinalIgnoreCase))
			{
				if (proj.ProjectItems != null)
				{
					foreach (ProjectItem item in proj.ProjectItems)
					{
						try
						{
							if (item.SubProject != null)
							{
								CollectSolutionProjects(item.SubProject, startupNames, result);
							}
						}
						catch { }
					}
				}
				return;
			}

			var name = string.Empty;
			string? filePath = null;
			var outputNames = new List<string>();

			try { name = proj.Name ?? string.Empty; } catch { }
			try { filePath = proj.FullName; } catch { }

			if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(filePath))
			{
				name = Path.GetFileNameWithoutExtension(filePath);
			}

			try
			{
				if (proj.Properties != null)
				{
					var outName = proj.Properties.Item("OutputFileName")?.Value?.ToString();
					if (!string.IsNullOrWhiteSpace(outName))
					{
						outputNames.Add(Path.GetFileNameWithoutExtension(outName));
					}
					var asmName = proj.Properties.Item("AssemblyName")?.Value?.ToString();
					if (!string.IsNullOrWhiteSpace(asmName))
					{
						outputNames.Add(asmName);
					}
				}
			}
			catch { }

			if (!string.IsNullOrWhiteSpace(name))
			{
				var isStartup = startupNames.Contains(name)
					|| (!string.IsNullOrWhiteSpace(filePath) && startupNames.Contains(filePath))
					|| (!string.IsNullOrWhiteSpace(filePath) && startupNames.Contains(Path.GetFileName(filePath)))
					|| (!string.IsNullOrWhiteSpace(filePath) && startupNames.Contains(Path.GetFileNameWithoutExtension(filePath)));

				result.Add((name, filePath, isStartup, outputNames));
			}
		}
		catch { }
	}

	public async Task<DebuggerDetachResponse> DetachAsync(
		DebuggerDetachRequest request,
		CancellationToken cancellationToken)
	{
		if (!_executionLock.Wait(0))
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.DebuggerBusy,
				"A debugger execution control operation is already in progress.");
		}

		try
		{
			await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
			var debugger = await GetDebuggerAsync(cancellationToken);

			if (debugger.CurrentMode == dbgDebugMode.dbgDesignMode)
			{
				throw new DebuggerProviderException(
					BridgeErrorCodes.DebuggerNotRunning,
					"The debugger is in design mode and is not currently debugging any process.");
			}

			if (request.ProcessId.HasValue)
			{
				EnvDTE.Process? foundProc = null;
				if (debugger.DebuggedProcesses != null)
				{
					foreach (EnvDTE.Process p in debugger.DebuggedProcesses)
					{
						try
						{
							if (p.ProcessID == request.ProcessId.Value)
							{
								foundProc = p;
								break;
							}
						}
						catch { }
					}
				}

				if (foundProc == null)
				{
					throw new DebuggerProviderException(
						BridgeErrorCodes.ProcessNotFound,
						$"Debugged process with PID {request.ProcessId.Value} was not found among active debugged processes.");
				}

				foundProc.Detach(WaitForBreakOrEnd: false);
			}
			else
			{
				debugger.DetachAll();
			}

			var currentMode = GetModeString(debugger.CurrentMode);
			var isDebugging = debugger.CurrentMode != dbgDebugMode.dbgDesignMode;

			return new DebuggerDetachResponse
			{
				VsInstanceId = _vsInstanceId,
				DetachedProcessId = request.ProcessId,
				CurrentMode = currentMode,
				IsDebugging = isDebugging
			};
		}
		finally
		{
			_executionLock.Release();
		}
	}

	public async Task<DebuggerGetModulesResponse> GetModulesAsync(
		DebuggerGetModulesRequest request,
		CancellationToken cancellationToken)
	{
		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var debugger = await GetDebuggerAsync(cancellationToken);

		if (debugger.CurrentMode == dbgDebugMode.dbgDesignMode)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.DebuggerNotRunning,
				"Cannot retrieve modules when the debugger is in design mode.");
		}

		EnvDTE.Process? targetProc = null;
		if (request.ProcessId.HasValue)
		{
			if (debugger.DebuggedProcesses != null)
			{
				foreach (EnvDTE.Process p in debugger.DebuggedProcesses)
				{
					try
					{
						if (p.ProcessID == request.ProcessId.Value)
						{
							targetProc = p;
							break;
						}
					}
					catch { }
				}
			}

			if (targetProc == null)
			{
				throw new DebuggerProviderException(
					BridgeErrorCodes.ProcessNotFound,
					$"Debugged process with PID {request.ProcessId.Value} was not found.");
			}
		}
		else
		{
			targetProc = debugger.CurrentProcess;
		}

		if (targetProc == null)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.DebuggerNotRunning,
				"No active debugged process is currently available.");
		}

		var pid = targetProc.ProcessID;
		var procName = targetProc.Name ?? string.Empty;
		var modulesList = new List<ModuleInfo>();
		var maxCount = Clamp(request.MaxCount ?? 100, 1, 500);

		if (targetProc is Process3 proc3 && proc3.Modules != null)
		{
			foreach (EnvDTE90.Module mod in proc3.Modules)
			{
				try
				{
					var modName = mod.Name ?? string.Empty;
					var userCode = false;
					try { userCode = mod.UserCode; } catch { }

					if (request.UserCodeOnly && !userCode)
					{
						continue;
					}

					if (!string.IsNullOrWhiteSpace(request.NameFilter))
					{
						if (modName.IndexOf(request.NameFilter, StringComparison.OrdinalIgnoreCase) < 0)
						{
							continue;
						}
					}

					string? path = null;
					try { path = mod.Path; } catch { }

					uint order = 0;
					try { order = mod.Order; } catch { }

					string? version = null;
					try { version = mod.Version; } catch { }

					string? loadAddr = null;
					try { loadAddr = string.Format(CultureInfo.InvariantCulture, "0x{0:X16}", mod.LoadAddress); } catch { }

					string? endAddr = null;
					try { endAddr = string.Format(CultureInfo.InvariantCulture, "0x{0:X16}", mod.EndAddress); } catch { }

					string? symFile = null;
					try { symFile = mod.SymbolFile; } catch { }

					var symLoaded = !string.IsNullOrWhiteSpace(symFile);

					var optimized = false;
					try { optimized = mod.Optimized; } catch { }

					var is64Bit = false;
					try { is64Bit = mod.Is64bit; } catch { }

					modulesList.Add(new ModuleInfo
					{
						Name = modName,
						Path = string.IsNullOrWhiteSpace(path) ? null : path,
						Order = order,
						Version = string.IsNullOrWhiteSpace(version) ? null : version,
						LoadAddress = loadAddr,
						EndAddress = endAddr,
						SymbolFile = string.IsNullOrWhiteSpace(symFile) ? null : symFile,
						SymbolsLoaded = symLoaded,
						Optimized = optimized,
						UserCode = userCode,
						Is64Bit = is64Bit
					});

					if (modulesList.Count >= maxCount)
					{
						break;
					}
				}
				catch
				{
				}
			}
		}

		return new DebuggerGetModulesResponse
		{
			VsInstanceId = _vsInstanceId,
			ProcessId = pid,
			ProcessName = procName,
			TotalCount = modulesList.Count,
			ReturnedCount = modulesList.Count,
			Modules = modulesList
		};
	}

	internal async Task<DebuggerExecutionResponse> CaptureCurrentStateAsync(
		string action,
		CancellationToken cancellationToken)
	{
		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var debugger = await GetDebuggerAsync(cancellationToken);
		return CaptureExecutionResult(debugger, action, GetModeString(debugger.CurrentMode));
	}

	private async Task<DebuggerExecutionResponse> ExecuteControlCommandAsync(
		string actionName,
		CancellationToken cancellationToken,
		Action<Debugger> executeAction)
	{
		if (!_executionLock.Wait(0))
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.DebuggerBusy,
				"A debugger execution control operation is already in progress.");
		}

		try
		{
			await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
			var debugger = await GetDebuggerAsync(cancellationToken);
			var previousMode = GetModeString(debugger.CurrentMode);

			executeAction(debugger);

			return CaptureExecutionResult(debugger, actionName, previousMode);
		}
		finally
		{
			_executionLock.Release();
		}
	}

	private DebuggerExecutionResponse CaptureExecutionResult(
		Debugger debugger,
		string action,
		string previousMode)
	{
		ThreadHelper.ThrowIfNotOnUIThread();
		var currentMode = GetModeString(debugger.CurrentMode);
		var isDebugging = debugger.CurrentMode != dbgDebugMode.dbgDesignMode;

		int? processId = null;
		int? threadId = null;
		string? breakReason = null;
		StackFrameInfo? topFrame = null;

		if (isDebugging)
		{
			try
			{
				var proc = debugger.CurrentProcess;
				if (proc != null)
				{
					processId = proc.ProcessID;
				}
			}
			catch
			{
			}

			try
			{
				var thread = debugger.CurrentThread;
				if (thread != null)
				{
					threadId = thread.ID;
					if (debugger.CurrentMode == dbgDebugMode.dbgBreakMode && thread.StackFrames != null)
					{
						foreach (StackFrame frame in thread.StackFrames)
						{
							topFrame = ReadStackFrame(frame, 0);
							break;
						}
					}
				}
			}
			catch
			{
			}

			try
			{
				breakReason = GetBreakReasonString(debugger.LastBreakReason);
			}
			catch
			{
			}
		}

		return new DebuggerExecutionResponse
		{
			VsInstanceId = _vsInstanceId,
			Action = action,
			PreviousMode = previousMode,
			CurrentMode = currentMode,
			IsDebugging = isDebugging,
			LastBreakReason = breakReason,
			CurrentProcessId = processId,
			CurrentThreadId = threadId,
			TopFrame = topFrame
		};
	}

	private async Task<(DTE2 Dte, Debugger Debugger)> GetDteAndDebuggerAsync(CancellationToken cancellationToken)
	{
		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var dte = await _package.GetServiceAsync(typeof(DTE)) as DTE2
			?? throw new DebuggerProviderException(BridgeErrorCodes.DebuggerUnavailable, "The Visual Studio DTE service is unavailable.");
		var debugger = dte.Debugger
			?? throw new DebuggerProviderException(BridgeErrorCodes.DebuggerUnavailable, "The Visual Studio debugger is unavailable.");
		return (dte, debugger);
	}

	internal async Task<Debugger> GetDebuggerAsync(CancellationToken cancellationToken)
	{
		var (_, debugger) = await GetDteAndDebuggerAsync(cancellationToken);
		return debugger;
	}

	private static EnvDTE.Thread? FindThread(Debugger debugger, int? requestedThreadId)
	{
		ThreadHelper.ThrowIfNotOnUIThread();
		if (!requestedThreadId.HasValue)
		{
			return debugger.CurrentThread;
		}

		try
		{
			if (debugger.CurrentProgram?.Threads != null)
			{
				foreach (EnvDTE.Thread th in debugger.CurrentProgram.Threads)
				{
					if (th.ID == requestedThreadId.Value)
					{
						return th;
					}
				}
			}
		}
		catch
		{
		}

		return debugger.CurrentThread;
	}

	private static EnvDTE.Thread? FindExactThread(Debugger debugger, int threadId)
	{
		ThreadHelper.ThrowIfNotOnUIThread();
		try
		{
			var threads = debugger.CurrentProgram?.Threads;
			if (threads == null && debugger.CurrentProcess?.Programs != null)
			{
				foreach (Program prog in debugger.CurrentProcess.Programs)
				{
					if (prog.Threads != null)
					{
						threads = prog.Threads;
						break;
					}
				}
			}

			if (threads != null)
			{
				foreach (EnvDTE.Thread th in threads)
				{
					int id = 0;
					try { id = th.ID; } catch { }
					if (id == threadId)
					{
						return th;
					}
				}
			}
		}
		catch
		{
		}

		return null;
	}

	private static StackFrameInfo ReadStackFrame(StackFrame frame, int index)
	{
		ThreadHelper.ThrowIfNotOnUIThread();
		var functionName = "unknown";
		string? language = null;
		string? module = null;
		string? fileName = null;
		int? lineNumber = null;
		int? columnNumber = null;
		bool? userCode = null;

		try { functionName = frame.FunctionName ?? "unknown"; } catch { }
		try { language = frame.Language; } catch { }
		try { module = frame.Module; } catch { }

		// Phase 5A: Try reading strong-typed properties via EnvDTE90a.StackFrame2 first
		if (frame is EnvDTE90a.StackFrame2 frame2)
		{
			try
			{
				var fn = frame2.FileName;
				if (!string.IsNullOrWhiteSpace(fn))
				{
					fileName = fn.Trim();
				}
			}
			catch { }

			try
			{
				var ln = frame2.LineNumber;
				if (ln > 0)
				{
					lineNumber = (int)ln;
				}
			}
			catch { }

			try
			{
				userCode = frame2.UserCode;
			}
			catch { }

			try
			{
				if (string.IsNullOrWhiteSpace(module))
				{
					module = frame2.Module;
				}
			}
			catch { }
		}

		// Fallback: If fileName or lineNumber couldn't be extracted via StackFrame2, try parsing functionName string
		if (string.IsNullOrWhiteSpace(fileName) || lineNumber == null)
		{
			try
			{
				var inIdx = functionName.LastIndexOf(" in ", StringComparison.OrdinalIgnoreCase);
				var lineIdx = functionName.LastIndexOf(":line ", StringComparison.OrdinalIgnoreCase);
				if (inIdx >= 0 && lineIdx > inIdx)
				{
					if (string.IsNullOrWhiteSpace(fileName))
					{
						fileName = functionName.Substring(inIdx + 4, lineIdx - (inIdx + 4)).Trim();
					}
					if (lineNumber == null)
					{
						var lineStr = functionName.Substring(lineIdx + 6).Trim();
						if (int.TryParse(lineStr, out var parsedLine))
						{
							lineNumber = parsedLine;
						}
					}
				}
				else
				{
					var altLineIdx = functionName.LastIndexOf(" Line ", StringComparison.OrdinalIgnoreCase);
					if (altLineIdx >= 0 && lineNumber == null)
					{
						var lineStr = functionName.Substring(altLineIdx + 6).Trim();
						if (int.TryParse(lineStr, out var parsedLine))
						{
							lineNumber = parsedLine;
						}
					}
				}
			}
			catch
			{
			}
		}

		return new StackFrameInfo
		{
			FrameIndex = index,
			FunctionName = functionName,
			FileName = string.IsNullOrWhiteSpace(fileName) ? null : fileName,
			LineNumber = lineNumber > 0 ? lineNumber : null,
			ColumnNumber = columnNumber > 0 ? columnNumber : null,
			UserCode = userCode,
			Language = string.IsNullOrWhiteSpace(language) ? null : language,
			Module = string.IsNullOrWhiteSpace(module) ? null : module
		};
	}

	private static string GetModeString(dbgDebugMode mode) => mode switch
	{
		dbgDebugMode.dbgDesignMode => "design",
		dbgDebugMode.dbgRunMode => "running",
		dbgDebugMode.dbgBreakMode => "break",
		_ => "unknown"
	};

	private static string GetBreakReasonString(dbgEventReason reason) => reason switch
	{
		dbgEventReason.dbgEventReasonBreakpoint => "breakpoint",
		dbgEventReason.dbgEventReasonExceptionThrown => "exception_thrown",
		dbgEventReason.dbgEventReasonExceptionNotHandled => "exception_unhandled",
		dbgEventReason.dbgEventReasonStep => "step",
		dbgEventReason.dbgEventReasonUserBreak => "user_break",
		dbgEventReason.dbgEventReasonNone => "none",
		_ => reason.ToString().ToLowerInvariant()
	};

	private static int Clamp(int value, int min, int max) =>
		value < min ? min : (value > max ? max : value);

	private static string GetActiveSolutionConfigurationName(DTE2 dte)
	{
		ThreadHelper.ThrowIfNotOnUIThread();
		try
		{
			if (dte.Solution?.SolutionBuild?.ActiveConfiguration is SolutionConfiguration2 sc2)
			{
				return sc2.Name ?? string.Empty;
			}
			if (dte.Solution?.SolutionBuild?.ActiveConfiguration is SolutionConfiguration sc)
			{
				return sc.Name ?? string.Empty;
			}
		}
		catch
		{
		}
		return string.Empty;
	}

	private static void CheckOptimizationWarning(DTE2 dte, string? value, bool isValid, List<BridgeWarning> warnings)
	{
		ThreadHelper.ThrowIfNotOnUIThread();
		var text = value ?? string.Empty;
		bool isOptimizedOrUnavailable =
			!isValid ||
			text.IndexOf("optimized away", StringComparison.OrdinalIgnoreCase) >= 0 ||
			text.IndexOf("not available", StringComparison.OrdinalIgnoreCase) >= 0 ||
			text.IndexOf("cannot be evaluated", StringComparison.OrdinalIgnoreCase) >= 0;

		if (isOptimizedOrUnavailable)
		{
			var activeConfig = GetActiveSolutionConfigurationName(dte);
			if (!string.IsNullOrEmpty(activeConfig) &&
			    activeConfig.IndexOf("Release", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				if (!warnings.Exists(w => w.Code == "variable_optimized_in_release"))
				{
					warnings.Add(new BridgeWarning
					{
						Code = "variable_optimized_in_release",
						Message = $"Evaluation yielded an unavailable or optimized result ('{text}'). Visual Studio is currently in '{activeConfig}' configuration. Consider switching to 'Debug' using 'vs_set_solution_configuration(configuration: \"Debug\")' and rebuilding to inspect unoptimized locals."
					});
				}
			}
		}
	}
}

internal sealed class DebuggerProviderException : Exception
{
	public DebuggerProviderException(string code, string message, Exception? innerException = null)
		: base(message, innerException)
	{
		Code = code;
	}

	public string Code { get; }
}
