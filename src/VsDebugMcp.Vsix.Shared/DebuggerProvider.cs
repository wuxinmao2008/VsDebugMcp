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
using Microsoft.VisualStudio.Shell.Interop;
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

	[StructLayout(LayoutKind.Sequential)]
	private struct MEMORY_BASIC_INFORMATION
	{
		public IntPtr BaseAddress;
		public IntPtr AllocationBase;
		public uint AllocationProtect;
		public IntPtr RegionSize;
		public uint State;
		public uint Protect;
		public uint Type;
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern int VirtualQueryEx(IntPtr hProcess, IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, uint dwLength);

	private readonly AsyncPackage _package;
	private readonly string _vsInstanceId;
	private readonly OutputWindowProvider? _outputWindowProvider;
	private readonly SemaphoreSlim _executionLock = new(1, 1);
	private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _sessionBreakpointIds = new(StringComparer.OrdinalIgnoreCase);

	public bool SuppressCrtDialogOnStart { get; set; } = ReadBoolEnv("VSDEBUGMCP_SUPPRESS_CRT_DIALOG_ON_START", true);
	public bool SuppressCrtDialogOnAttach { get; set; } = ReadBoolEnv("VSDEBUGMCP_SUPPRESS_CRT_DIALOG_ON_ATTACH", false);

	private static bool ReadBoolEnv(string name, bool defaultValue)
	{
		try
		{
			var val = Environment.GetEnvironmentVariable(name);
			if (string.IsNullOrWhiteSpace(val)) return defaultValue;
			if (bool.TryParse(val, out var b)) return b;
			if (val == "1") return true;
			if (val == "0") return false;
		}
		catch { }
		return defaultValue;
	}

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
		DebuggerGetExceptionInfoResponse? exceptionInfo = null;
		int? totalThreads = null;
		List<ThreadInfo>? threadList = null;

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
				EnvDTE.Threads? ths = debugger.CurrentProgram?.Threads;
				if (ths == null && debugger.CurrentProcess?.Programs != null)
				{
					foreach (EnvDTE.Program prog in debugger.CurrentProcess.Programs)
					{
						if (prog.Threads != null)
						{
							ths = prog.Threads;
							break;
						}
					}
				}

				if (ths != null)
				{
					totalThreads = ths.Count;
					if (request.IncludeThreads)
					{
						threadList = new List<ThreadInfo>();
						var maxTh = Clamp(request.MaxThreads ?? 20, 1, 100);
						var idx = 0;
						foreach (EnvDTE.Thread t in ths)
						{
							if (idx >= maxTh) break;
							int tid = 0;
							string tname = string.Empty;
							bool isAlive = true;
							bool isFrozen = false;
							string? priority = null;
							try { tid = t.ID; } catch { }
							try { tname = t.Name ?? string.Empty; } catch { }
							try { isAlive = t.IsAlive; } catch { }
							try { isFrozen = t.IsFrozen; } catch { }
							try { priority = t.Priority; } catch { }

							threadList.Add(new ThreadInfo
							{
								Id = tid,
								Name = tname,
								IsCurrent = threadId.HasValue && tid == threadId.Value,
								IsAlive = isAlive,
								IsFrozen = isFrozen,
								Priority = priority
							});
							idx++;
						}
					}
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

			if (request.IncludeExceptionInfo)
			{
				try
				{
					var excResp = await GetExceptionInfoAsync(new DebuggerGetExceptionInfoRequest(), cancellationToken);
					if (excResp != null && excResp.HasException)
					{
						exceptionInfo = excResp;
					}
				}
				catch { }
			}

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
				var source = string.IsNullOrWhiteSpace(request.LogSource) ? "debug" : request.LogSource!.Trim();
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
			ExceptionInfo = exceptionInfo,
			TotalThreadCount = totalThreads,
			Threads = threadList,
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

							var bpId = $"{fullPath}:{bp.FileLine}";
							_sessionBreakpointIds.TryAdd(bpId, 0);
							try
							{
								var rawFile = bp.File;
								if (!string.IsNullOrEmpty(rawFile))
								{
									_sessionBreakpointIds.TryAdd($"{rawFile}:{bp.FileLine}", 0);
								}
							}
							catch { }

							response.Breakpoints.Add(new BreakpointInfo
							{
								Id = bpId,
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
		    request.SessionOnly != true &&
		    string.IsNullOrWhiteSpace(request.FilePath) &&
		    string.IsNullOrWhiteSpace(request.BreakpointId))
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.InvalidRequest,
				"Must specify 'clearAll: true', 'sessionOnly: true', 'filePath', or 'breakpointId' to clear breakpoints. Preventing accidental full deletion.");
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

				if (request.SessionOnly == true)
				{
					var inSession = _sessionBreakpointIds.ContainsKey(bpId) ||
					                (!string.IsNullOrEmpty(bpFile) && _sessionBreakpointIds.ContainsKey($"{bpFile}:{bpLine}"));
					if (!inSession)
					{
						continue;
					}
				}

				if (request.ClearAll || request.SessionOnly == true)
				{
					toDelete.Add(bp);
				}
				else if (!string.IsNullOrEmpty(request.BreakpointId))
				{
					if (string.Equals(bpId, request.BreakpointId!.Trim(), StringComparison.OrdinalIgnoreCase))
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
					string bpFile = string.Empty;
					int bpLine = 0;
					try { bpFile = bp.File ?? string.Empty; } catch { }
					try { bpLine = bp.FileLine; } catch { }
					var bpId = !string.IsNullOrEmpty(bpFile) ? $"{bpFile}:{bpLine}" : $"bp:{bpLine}";
					_sessionBreakpointIds.TryRemove(bpId, out _);
					if (!string.IsNullOrEmpty(bpFile))
					{
						_sessionBreakpointIds.TryRemove($"{bpFile}:{bpLine}", out _);
					}

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

			if (request.ClearAll && request.SessionOnly != true)
			{
				_sessionBreakpointIds.Clear();
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
		var threadName = thread.Name ?? string.Empty;
		var threadId = thread.ID;

		var totalFrames = 0;
		int? firstUserFrameIndex = null;
		var userCodeFramesCount = 0;
		var rawFrames = new List<StackFrameInfo>();

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
					if (frameIndex >= MaxAllowedFrames)
					{
						break;
					}

					var frameInfo = ReadStackFrame(frame, frameIndex);
					var isExt = IsExternalFrame(frameInfo);
					if (!isExt)
					{
						if (firstUserFrameIndex == null)
						{
							firstUserFrameIndex = frameIndex;
						}
						userCodeFramesCount++;
					}

					rawFrames.Add(frameInfo);
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

		var frames = new List<StackFrameInfo>();
		if (request.UserCodeOnly == true)
		{
			foreach (var f in rawFrames)
			{
				if (!IsExternalFrame(f))
				{
					frames.Add(f);
					if (frames.Count >= maxFrames)
					{
						break;
					}
				}
			}
		}
		else if (request.CollapseExternal == true)
		{
			var i = 0;
			while (i < rawFrames.Count && frames.Count < maxFrames)
			{
				if (!IsExternalFrame(rawFrames[i]))
				{
					frames.Add(rawFrames[i]);
					i++;
				}
				else
				{
					var startIdx = i;
					var distinctModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					while (i < rawFrames.Count && IsExternalFrame(rawFrames[i]))
					{
						var mod = rawFrames[i].Module;
						if (!string.IsNullOrWhiteSpace(mod))
						{
							distinctModules.Add(Path.GetFileName(mod));
						}
						i++;
					}

					var count = i - startIdx;
					if (count == 1)
					{
						frames.Add(rawFrames[startIdx]);
					}
					else
					{
						var modSummary = distinctModules.Count > 0 ? string.Join(", ", distinctModules.Take(3)) : "System & Runtime";
						frames.Add(new StackFrameInfo
						{
							FrameIndex = startIdx,
							FunctionName = $"[External Code: {modSummary} - {count} frames]",
							UserCode = false
						});
					}
				}
			}
		}
		else
		{
			for (var i = 0; i < rawFrames.Count && i < maxFrames; i++)
			{
				frames.Add(rawFrames[i]);
			}
		}

		return new DebuggerGetCallStackResponse
		{
			VsInstanceId = _vsInstanceId,
			ThreadId = threadId,
			ThreadName = string.IsNullOrWhiteSpace(threadName) ? null : threadName,
			Frames = frames,
			TotalFrames = totalFrames,
			Truncated = totalFrames > frames.Count,
			FirstUserFrameIndex = firstUserFrameIndex,
			UserCodeFramesCount = userCodeFramesCount
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

		EnvDTE.Thread? originalThread = null;
		var switchedThread = false;
		if (request.ThreadId.HasValue)
		{
			var targetTh = FindThread(debugger, request.ThreadId.Value);
			if (targetTh != null && targetTh.ID != debugger.CurrentThread?.ID)
			{
				originalThread = debugger.CurrentThread;
				debugger.CurrentThread = targetTh;
				switchedThread = true;
			}
		}

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

			if (isValid)
			{
				val = EnrichCharPointerValue(dte, debugger, request.Expression, val, type, timeoutMs);
			}

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
			if (switchedThread && originalThread != null)
			{
				try
				{
					debugger.CurrentThread = originalThread;
				}
				catch
				{
				}
			}

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
			ThreadHelper.ThrowIfNotOnUIThread();
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
			ThreadHelper.ThrowIfNotOnUIThread();
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
			ThreadHelper.ThrowIfNotOnUIThread();
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
			ThreadHelper.ThrowIfNotOnUIThread();
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
			ThreadHelper.ThrowIfNotOnUIThread();
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
			ThreadHelper.ThrowIfNotOnUIThread();
			if (debugger.CurrentMode == dbgDebugMode.dbgDesignMode)
			{
				return;
			}

			debugger.Stop(request.WaitForStop);
		});
	}

	internal async Task<DebuggerExecutionResponse> LaunchTargetAsync(
		string exePath,
		string? arguments,
		string? workingDir,
		bool waitForBreak,
		int? timeoutMs,
		CancellationToken cancellationToken)
	{
		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var (dte, debugger) = await GetDteAndDebuggerAsync(cancellationToken);

		if (debugger.CurrentMode != dbgDebugMode.dbgDesignMode)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.DebuggerAlreadyRunning,
				$"The debugger is already running (current mode: {GetModeString(debugger.CurrentMode)}). Use continue or step instead.");
		}

		var previousMode = GetModeString(debugger.CurrentMode);

		var targetInfo = new VsDebugTargetInfo
		{
			cbSize = (uint)Marshal.SizeOf(typeof(VsDebugTargetInfo)),
			dlo = DEBUG_LAUNCH_OPERATION.DLO_CreateProcess,
			bstrExe = exePath,
			bstrCurDir = !string.IsNullOrWhiteSpace(workingDir) ? workingDir : Path.GetDirectoryName(exePath),
			bstrArg = arguments ?? string.Empty,
			clsidCustom = Microsoft.VisualStudio.VSConstants.DebugEnginesGuids.NativeOnly_guid,
			fSendStdoutToOutputWindow = 0
		};

		VsShellUtilities.LaunchDebugger(_package, targetInfo);

		if (waitForBreak)
		{
			var waitTimeout = Clamp(timeoutMs ?? 5000, 500, 30000);
			var sw = System.Diagnostics.Stopwatch.StartNew();
			while (sw.ElapsedMilliseconds < waitTimeout)
			{
				await Task.Delay(100, cancellationToken);
				await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
				if (debugger.CurrentMode == dbgDebugMode.dbgBreakMode || debugger.CurrentMode == dbgDebugMode.dbgDesignMode)
				{
					break;
				}
			}
		}

		await TrySuppressCrtDialogAsync(isAttach: false, cancellationToken);
		return CaptureExecutionResult(debugger, "start", previousMode);
	}

	public async Task<DebuggerExecutionResponse> StartAsync(
		DebuggerStartRequest request,
		CancellationToken cancellationToken)
	{
		if (!await _executionLock.WaitAsync(0, cancellationToken))
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

			if (!string.IsNullOrWhiteSpace(request.Target))
			{
				string target = request.Target!.Trim();
				string? resolvedExe = null;
				if (Path.IsPathRooted(target) && File.Exists(target))
				{
					resolvedExe = target;
				}
				else if (CMakeWorkspaceState.TryGetCMakeWorkspaceRoot(dte, out var cmakeRoot))
				{
					var presets = CMakePresetsParser.LoadWorkspacePresets(cmakeRoot);
					var activePresetName = CMakeWorkspaceState.GetActivePreset(cmakeRoot, presets);
					var activePreset = presets.Find(p => string.Equals(p.Name, activePresetName, StringComparison.OrdinalIgnoreCase)) ?? presets.FirstOrDefault();
					var binaryDir = activePreset?.BinaryDir;

					string targetFileName = target;
					if (!targetFileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
					{
						targetFileName += ".exe";
					}

					if (!string.IsNullOrEmpty(binaryDir) && Directory.Exists(binaryDir))
					{
						var found = Directory.GetFiles(binaryDir, targetFileName, SearchOption.AllDirectories);
						if (found.Length > 0)
						{
							resolvedExe = found[0];
						}
					}
				}

				if (string.IsNullOrEmpty(resolvedExe) || !File.Exists(resolvedExe))
				{
					throw new DebuggerProviderException(
						BridgeErrorCodes.FileNotFound,
						$"Target executable '{request.Target}' was not found. Please build the project first (vs_run_build).");
				}

				return await LaunchTargetAsync(
					resolvedExe!,
					request.Arguments,
					request.WorkingDirectory,
					request.WaitForBreak,
					request.TimeoutMs,
					cancellationToken);
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

			await TrySuppressCrtDialogAsync(isAttach: false, cancellationToken);
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

		EnvDTE.Thread? originalThread = null;
		var switchedThread = false;
		if (request.ThreadId.HasValue)
		{
			var targetTh = FindThread(debugger, request.ThreadId.Value);
			if (targetTh != null && targetTh.ID != debugger.CurrentThread?.ID)
			{
				originalThread = debugger.CurrentThread;
				debugger.CurrentThread = targetTh;
				switchedThread = true;
			}
		}

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
						var val = expr.Value ?? string.Empty;
						var type = expr.Type ?? string.Empty;
						var isValid = expr.IsValidValue;
						if (isValid)
						{
							val = EnrichCharPointerValue(dte, debugger, exprStr, val, type, timeoutMs);
						}

						results.Add(new DebuggerExpressionItemResult
						{
							Expression = exprStr,
							Value = val,
							Type = type,
							IsValid = isValid
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
			if (switchedThread && originalThread != null)
			{
				try
				{
					debugger.CurrentThread = originalThread;
				}
				catch
				{
				}
			}

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

		var targetThread = FindThread(debugger, request.ThreadId);
		if (targetThread == null)
		{
			throw new DebuggerProviderException(BridgeErrorCodes.DebuggerUnavailable, "No active debug thread available.");
		}

		StackFrame? targetFrame = null;
		if (targetThread.StackFrames != null)
		{
			var currentIndex = 0;
			foreach (StackFrame frame in targetThread.StackFrames)
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
		var (dte, debugger) = await GetDteAndDebuggerAsync(cancellationToken);

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

		var pid = 0;
		try { pid = currentProcess.ProcessID; } catch { }
		var rawAddress = request.Address.Trim();
		var resolvedAddr = TryResolveAddressExpression(debugger, rawAddress);

		if (resolvedAddr == 0)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.InvalidMemoryAddress,
				$"Unable to resolve memory address from '{request.Address}'. Ensure the variable, register, or address is accessible in the current scope.");
		}

		var byteCount = Clamp(request.ByteCount, 1, 4096);
		var warnings = new List<BridgeWarning>();
		byte[]? buffer = null;
		int bytesRead = 0;
		string? readSource = null;
		int lastWin32Error = 0;
		var dumpPath = GetActiveDumpFilePath(dte, debugger);

		// Tier 1: Live OS Process ReadProcessMemory (when PID > 0 and not a Minidump file)
		if (pid > 0 && string.IsNullOrEmpty(dumpPath))
		{
			var hProcess = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, pid);
			if (hProcess != IntPtr.Zero)
			{
				try
				{
					var tempBuf = new byte[byteCount];
					var success = ReadProcessMemory(hProcess, (IntPtr)(long)resolvedAddr, tempBuf, byteCount, out var bytesReadPtr);
					bytesRead = (int)bytesReadPtr;
					if (success || bytesRead > 0)
					{
						if (bytesRead < byteCount)
						{
							Array.Resize(ref tempBuf, bytesRead);
							warnings.Add(new BridgeWarning
							{
								Code = "partial_memory_read",
								Message = $"Requested {byteCount} bytes, but only {bytesRead} bytes could be read (likely reached end of valid virtual memory page)."
							});
						}
						buffer = tempBuf;
						readSource = "win32_read_process_memory";
					}
					else
					{
						lastWin32Error = Marshal.GetLastWin32Error();
					}
				}
				finally
				{
					CloseHandle(hProcess);
				}
			}
			else
			{
				lastWin32Error = Marshal.GetLastWin32Error();
			}
		}

		// Tier 2: Direct Minidump (.dmp) Memory64ListStream / MemoryListStream read
		if (buffer == null && !string.IsNullOrEmpty(dumpPath))
		{
			if (MinidumpParser.TryReadMemory(dumpPath!, resolvedAddr, byteCount, out var dmpBytes) && dmpBytes.Length > 0)
			{
				buffer = dmpBytes;
				bytesRead = dmpBytes.Length;
				readSource = "minidump_stream";
				if (bytesRead < byteCount)
				{
					warnings.Add(new BridgeWarning
					{
						Code = "partial_memory_read",
						Message = $"Requested {byteCount} bytes, read {bytesRead} bytes from Minidump memory stream."
					});
				}
			}
		}

		// Tier 3: Debugger Expression Evaluator fallback (reads 8-byte chunks via *(unsigned __int64*)0xADDR,x)
		if (buffer == null && debugger.CurrentMode == dbgDebugMode.dbgBreakMode)
		{
			if (TryReadMemoryViaExpressionEvaluator(debugger, resolvedAddr, byteCount, out var evalBytes))
			{
				buffer = evalBytes;
				bytesRead = evalBytes.Length;
				readSource = "debugger_eval";
				if (bytesRead < byteCount)
				{
					warnings.Add(new BridgeWarning
					{
						Code = "partial_memory_read",
						Message = $"Requested {byteCount} bytes, read {bytesRead} bytes via debugger expression evaluator."
					});
				}
			}
		}

		if (buffer == null || bytesRead == 0)
		{
			var reason = !string.IsNullOrEmpty(dumpPath)
				? $"Address 0x{resolvedAddr:X16} is not present in the captured memory ranges of Minidump '{Path.GetFileName(dumpPath)}'."
				: $"ReadProcessMemory failed at address 0x{resolvedAddr:X16} (PID: {pid}, size: {byteCount}). Win32 error code: {lastWin32Error}";
			throw new DebuggerProviderException(BridgeErrorCodes.MemoryReadFailed, reason);
		}

		// Format representations
		var hexBytes = string.Join(" ", buffer.Select(b => b.ToString("X2")));
		var base64Data = Convert.ToBase64String(buffer);
		var asciiRep = new string(buffer.Select(b => b >= 32 && b <= 126 ? (char)b : '.').ToArray());
		var utf16Preview = MinidumpParser.ExtractUtf16Preview(buffer);

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
			Utf16Preview = utf16Preview,
			ReadSource = readSource,
			Base64Data = base64Data,
			Warnings = warnings
		};
	}

	private static byte[]? ReadRawMemoryBytesInternal(DTE2 dte, Debugger debugger, ulong address, int byteCount, out string? readSource)
	{
		ThreadHelper.ThrowIfNotOnUIThread();
		readSource = null;
		if (byteCount <= 0 || address == 0) return null;

		var currentProcess = debugger.CurrentProcess;
		var pid = 0;
		try { pid = currentProcess?.ProcessID ?? 0; } catch { }
		var dumpPath = GetActiveDumpFilePath(dte, debugger);

		// Tier 1: Live OS Process ReadProcessMemory (when PID > 0 and not a Minidump file)
		if (pid > 0 && string.IsNullOrEmpty(dumpPath))
		{
			var hProcess = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, pid);
			if (hProcess != IntPtr.Zero)
			{
				try
				{
					var tempBuf = new byte[byteCount];
					var success = ReadProcessMemory(hProcess, (IntPtr)(long)address, tempBuf, byteCount, out var bytesReadPtr);
					var bytesRead = (int)bytesReadPtr;
					if (success || bytesRead > 0)
					{
						if (bytesRead < byteCount) Array.Resize(ref tempBuf, bytesRead);
						readSource = "win32_read_process_memory";
						return tempBuf;
					}
				}
				finally
				{
					CloseHandle(hProcess);
				}
			}
		}

		// Tier 2: Direct Minidump (.dmp) Memory Stream read
		if (!string.IsNullOrEmpty(dumpPath))
		{
			if (MinidumpParser.TryReadMemory(dumpPath!, address, byteCount, out var dmpBytes) && dmpBytes.Length > 0)
			{
				readSource = "minidump_stream";
				return dmpBytes;
			}
		}

		// Tier 3: Debugger Expression Evaluator fallback
		if (debugger.CurrentMode == dbgDebugMode.dbgBreakMode)
		{
			if (TryReadMemoryViaExpressionEvaluator(debugger, address, byteCount, out var evalBytes) && evalBytes.Length > 0)
			{
				readSource = "debugger_eval";
				return evalBytes;
			}
		}

		return null;
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

			var navTarget = request.FilePath!.Trim();
			if (!Path.IsPathRooted(navTarget) && !string.IsNullOrEmpty(dte.Solution?.FullName))
			{
				var slnDir = Path.GetDirectoryName(dte.Solution!.FullName);
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
		var (dte, debugger) = await GetDteAndDebuggerAsync(cancellationToken);

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

				// Do not return early if $exception succeeded; continue to check if assertion or additional context exists in logs
			}
		}
		catch { }

		// 2. Scan recent Debug Output Window logs for assertion or native crash details
		if (_outputWindowProvider != null)
		{
			try
			{
				var logResp = await _outputWindowProvider.GetLogsAsync(new GetOutputWindowLogsRequest
				{
					Source = "debug",
					MaxChars = 8000
				}, cancellationToken);

				if (!string.IsNullOrEmpty(logResp.Text))
				{
					AssertionLogParser.ScanLogsForAssertionOrException(logResp.Text, response);
				}
			}
			catch { }
		}

		// 3. Check if active target is a Minidump (.dmp) file and parse ExceptionStream (StreamType 6)
		var dumpPath = GetActiveDumpFilePath(dte, debugger);
		if (!string.IsNullOrEmpty(dumpPath) && MinidumpParser.TryReadExceptionInfo(dumpPath, out var dmpExc) && dmpExc != null)
		{
			response.HasException = true;
			response.ExceptionCode ??= dmpExc.ExceptionCodeHex;
			response.ExceptionType ??= dmpExc.ExceptionName;
			response.ExceptionAddress ??= dmpExc.ExceptionAddressHex;
			response.FaultingThreadId ??= dmpExc.ThreadId;
			response.AccessOperation ??= dmpExc.AccessOperation;
			response.FaultingAddress ??= dmpExc.FaultingAddressHex;
			response.HResult ??= dmpExc.ExceptionCodeHex;
			if (string.IsNullOrWhiteSpace(response.Message))
			{
				response.Message = dmpExc.FormattedMessage;
			}
			response.RawDetails ??= dmpExc.FormattedMessage;
		}

		// 4. Fallback: Check if LastBreakReason was an exception (e.g. C++ or native unhandled)
		if (debugger.LastBreakReason == dbgEventReason.dbgEventReasonExceptionThrown ||
			debugger.LastBreakReason == dbgEventReason.dbgEventReasonExceptionNotHandled)
		{
			response.HasException = true;
			if (string.IsNullOrEmpty(response.Message))
			{
				response.Message = "Debugger paused due to an exception (native/unmanaged or CLR first-chance).";
			}
			try
			{
				var errExpr = debugger.GetExpression("$err,hr", UseAutoExpandRules: false, Timeout: 1000);
				if (errExpr != null && errExpr.IsValidValue)
				{
					response.HResult ??= errExpr.Value;
					response.RawDetails ??= errExpr.Value;
				}
			}
			catch { }
		}

		// Enrich FaultingThreadId and ExceptionAddress if HasException is true
		if (response.HasException)
		{
			if (!response.FaultingThreadId.HasValue)
			{
				try { response.FaultingThreadId = debugger.CurrentThread?.ID; } catch { }
			}
			if (string.IsNullOrEmpty(response.ExceptionAddress))
			{
				var ip = TryResolveAddressExpression(debugger, "rip");
				if (ip == 0)
				{
					ip = TryResolveAddressExpression(debugger, "eip");
				}
				if (ip != 0)
				{
					response.ExceptionAddress = $"0x{ip:X16}";
				}
			}
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
		if (!await _executionLock.WaitAsync(0, cancellationToken))
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
		if (!await _executionLock.WaitAsync(0, cancellationToken))
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
					var availableEngines = new List<(EnvDTE80.Engine Engine, string Name, string Id)>();
					if (proc2Engine.Transport?.Engines != null)
					{
						foreach (EnvDTE80.Engine eng in proc2Engine.Transport.Engines)
						{
							availableEngines.Add((eng, eng.Name ?? string.Empty, eng.ID ?? string.Empty));
						}
					}

					var matchedEngines = new List<EnvDTE80.Engine>();
					foreach (var reqEngine in engines)
					{
						if (string.IsNullOrWhiteSpace(reqEngine)) continue;
						var trimmed = reqEngine.Trim();
						var match = availableEngines.Find(e =>
							string.Equals(e.Name, trimmed, StringComparison.OrdinalIgnoreCase) ||
							string.Equals(e.Id, trimmed, StringComparison.OrdinalIgnoreCase));

						if (match.Engine == null)
						{
							match = availableEngines.Find(e =>
								!string.IsNullOrEmpty(e.Name) && e.Name.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase) >= 0);
						}

						if (match.Engine == null)
						{
							var availableNames = availableEngines.Select(e => e.Name).ToList();
							throw new DebuggerProviderException(
								BridgeErrorCodes.EngineNotFound,
								$"Debugger engine '{trimmed}' not found for process {targetProc.Name} (PID: {targetProc.ProcessID}). Available engines: {string.Join(", ", availableNames)}");
						}

						if (!matchedEngines.Contains(match.Engine))
						{
							matchedEngines.Add(match.Engine);
							attachedEngines.Add(match.Name);
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

		await TrySuppressCrtDialogAsync(isAttach: true, cancellationToken);

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
						outputNames.Add(asmName!);
					}
				}
			}
			catch { }

			if (!string.IsNullOrWhiteSpace(name))
			{
				var isStartup = startupNames.Contains(name)
					|| (!string.IsNullOrWhiteSpace(filePath) && startupNames.Contains(filePath!))
					|| (!string.IsNullOrWhiteSpace(filePath) && startupNames.Contains(Path.GetFileName(filePath!)!))
					|| (!string.IsNullOrWhiteSpace(filePath) && startupNames.Contains(Path.GetFileNameWithoutExtension(filePath!)!));

				result.Add((name, filePath, isStartup, outputNames));
			}
		}
		catch { }
	}

	public async Task<DebuggerDetachResponse> DetachAsync(
		DebuggerDetachRequest request,
		CancellationToken cancellationToken)
	{
		if (!await _executionLock.WaitAsync(0, cancellationToken))
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
		if (!await _executionLock.WaitAsync(0, cancellationToken))
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

	private static readonly HashSet<string> ExternalModulePrefixes = new(StringComparer.OrdinalIgnoreCase)
	{
		"ntdll", "kernel32", "kernelbase", "user32", "gdi32", "combase", "ole32", "shell32",
		"ucrtbase", "ucrtbased", "msvcp", "msvcr", "vcruntime", "vcamp", "clr", "mscoree",
		"qt5core", "qt5gui", "qt5widgets", "qt5network", "qt6core", "qt6gui", "qt6widgets", "qwindows"
	};

	private static bool IsExternalFrame(StackFrameInfo frame)
	{
		if (frame.UserCode.HasValue)
		{
			return !frame.UserCode.Value;
		}

		if (!string.IsNullOrWhiteSpace(frame.Module))
		{
			var mod = Path.GetFileNameWithoutExtension(frame.Module);
			foreach (var prefix in ExternalModulePrefixes)
			{
				if (mod.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
		}

		if (!string.IsNullOrWhiteSpace(frame.FileName))
		{
			var fn = frame.FileName!;
			if (fn.IndexOf("\\include\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
			    fn.IndexOf("/include/", StringComparison.OrdinalIgnoreCase) >= 0 ||
			    fn.IndexOf("\\ucrt\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
			    fn.IndexOf("\\Qt\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
			    fn.IndexOf("/Qt/", StringComparison.OrdinalIgnoreCase) >= 0 ||
			    fn.IndexOf("\\Program Files", StringComparison.OrdinalIgnoreCase) >= 0 ||
			    fn.IndexOf("/Program Files", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
		}

		if (string.IsNullOrWhiteSpace(frame.FileName))
		{
			var fn = frame.FunctionName ?? string.Empty;
			if (fn.StartsWith("std::", StringComparison.Ordinal) ||
			    fn.StartsWith("QCoreApplication", StringComparison.Ordinal) ||
			    fn.StartsWith("QApplication", StringComparison.Ordinal) ||
			    fn.StartsWith("QWidget", StringComparison.Ordinal) ||
			    fn.StartsWith("operator delete", StringComparison.Ordinal) ||
			    fn.StartsWith("operator new", StringComparison.Ordinal) ||
			    fn.StartsWith("_Crt", StringComparison.Ordinal) ||
			    fn.StartsWith("__scrt", StringComparison.Ordinal) ||
			    fn.StartsWith("qDeleteInEventHandler", StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}

	private async Task TrySuppressCrtDialogAsync(bool isAttach, CancellationToken cancellationToken)
	{
		try
		{
			var enabled = isAttach ? SuppressCrtDialogOnAttach : SuppressCrtDialogOnStart;
			if (!enabled)
			{
				return;
			}

			await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
			var debugger = await GetDebuggerAsync(cancellationToken);

			if (debugger.CurrentMode == dbgDebugMode.dbgDesignMode)
			{
				return;
			}

			var wasBreakMode = debugger.CurrentMode == dbgDebugMode.dbgBreakMode;
			if (!wasBreakMode)
			{
				await Task.Delay(100, cancellationToken);
				await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

				if (debugger.CurrentMode == dbgDebugMode.dbgRunMode)
				{
					try
					{
						debugger.Break(false);
						var sw = System.Diagnostics.Stopwatch.StartNew();
						while (sw.ElapsedMilliseconds < 1000 && debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
						{
							await Task.Delay(50, cancellationToken);
							await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
							if (debugger.CurrentMode == dbgDebugMode.dbgDesignMode)
							{
								return;
							}
						}
					}
					catch { }
				}
			}

			if (debugger.CurrentMode == dbgDebugMode.dbgBreakMode)
			{
				try
				{
					// _CRT_ERROR = 2, _CRTDBG_MODE_DEBUG = 4
					debugger.GetExpression("_CrtSetReportMode(2, 4)", UseAutoExpandRules: false, Timeout: 1000);
					// _CRT_ASSERT = 1, _CRTDBG_MODE_DEBUG = 4
					debugger.GetExpression("_CrtSetReportMode(1, 4)", UseAutoExpandRules: false, Timeout: 1000);
				}
				catch
				{
					// Safe ignore: Release builds or non-CRT modules don't export _CrtSetReportMode
				}

				if (!wasBreakMode && debugger.CurrentMode == dbgDebugMode.dbgBreakMode)
				{
					try
					{
						debugger.Go(false);
					}
					catch { }
				}
			}
		}
		catch
		{
			// Safe fallback: never block or fail debugger start/attach
		}
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

	public async Task<DebuggerGetDisassemblyResponse> GetDisassemblyAsync(
		DebuggerGetDisassemblyRequest request,
		CancellationToken cancellationToken)
	{
		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var (dte, debugger) = await GetDteAndDebuggerAsync(cancellationToken);

		if (debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.DebuggerNotPaused,
				$"The debugger must be paused in break mode to retrieve disassembly (current mode: {GetModeString(debugger.CurrentMode)}).");
		}

		var instructionCount = Clamp(request.InstructionCount ?? 20, 1, 200);
		var instructionsBefore = Clamp(request.InstructionsBefore ?? 8, 0, Math.Min(100, instructionCount - 1));
		var targetFrameIndex = request.FrameIndex ?? 0;

		EnvDTE.Thread? originalThread = null;
		var switchedThread = false;
		if (request.ThreadId.HasValue)
		{
			var targetTh = FindThread(debugger, request.ThreadId.Value);
			if (targetTh != null && targetTh.ID != debugger.CurrentThread?.ID)
			{
				originalThread = debugger.CurrentThread;
				debugger.CurrentThread = targetTh;
				switchedThread = true;
			}
		}

		StackFrame? originalFrame = null;
		var switchedFrame = false;

		try
		{
			StackFrame? selectedFrame = debugger.CurrentStackFrame;
			if (targetFrameIndex > 0 && debugger.CurrentThread?.StackFrames != null)
			{
				originalFrame = debugger.CurrentStackFrame;
				var currentIndex = 0;
				foreach (StackFrame frame in debugger.CurrentThread.StackFrames)
				{
					if (currentIndex == targetFrameIndex)
					{
						debugger.CurrentStackFrame = frame;
						selectedFrame = frame;
						switchedFrame = true;
						break;
					}
					currentIndex++;
				}
			}

			ulong targetAddr = 0;
			if (!string.IsNullOrWhiteSpace(request.Address))
			{
				targetAddr = TryResolveAddressExpression(debugger, request.Address!.Trim());
				if (targetAddr == 0)
				{
					throw new DebuggerProviderException(
						BridgeErrorCodes.InvalidMemoryAddress,
						$"Unable to resolve disassembly target address from '{request.Address}'.");
				}
			}
			else
			{
				targetAddr = TryResolveAddressExpression(debugger, "rip");
				if (targetAddr == 0)
				{
					targetAddr = TryResolveAddressExpression(debugger, "eip");
				}
				if (targetAddr == 0 && selectedFrame != null)
				{
					try
					{
						var fn = selectedFrame.FunctionName ?? string.Empty;
						var hexMatch = Regex.Match(fn, @"0x[0-9a-fA-F]{6,16}");
						if (hexMatch.Success)
						{
							ulong.TryParse(hexMatch.Value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out targetAddr);
						}
					}
					catch { }
				}

				if (targetAddr == 0)
				{
					throw new DebuggerProviderException(
						BridgeErrorCodes.DisassemblyUnavailable,
						"Could not determine the instruction pointer (RIP/EIP) for the selected stack frame.");
				}
			}

			string? frameFunctionName = null;
			string? frameModuleName = null;
			if (selectedFrame != null)
			{
				try { frameFunctionName = selectedFrame.FunctionName; } catch { }
				try { frameModuleName = selectedFrame.Module; } catch { }
			}

			// Find containing module from Process3.Modules
			string? moduleName = null;
			string? modulePath = null;
			ulong moduleLoadAddr = 0;
			ulong moduleEndAddr = 0;

			if (debugger.CurrentProcess is Process3 proc3 && proc3.Modules != null)
			{
				foreach (EnvDTE90.Module mod in proc3.Modules)
				{
					try
					{
						var start = mod.LoadAddress;
						var end = mod.EndAddress;
						if (start > 0 && end > start && targetAddr >= start && targetAddr < end)
						{
							moduleName = mod.Name;
							modulePath = mod.Path;
							moduleLoadAddr = start;
							moduleEndAddr = end;
							break;
						}
					}
					catch { }
				}

				if (moduleLoadAddr == 0 && !string.IsNullOrWhiteSpace(frameModuleName))
				{
					var frameModShort = Path.GetFileName(frameModuleName);
					foreach (EnvDTE90.Module mod in proc3.Modules)
					{
						try
						{
							var mName = mod.Name ?? string.Empty;
							if (string.Equals(mName, frameModuleName, StringComparison.OrdinalIgnoreCase) ||
							    string.Equals(Path.GetFileName(mName), frameModShort, StringComparison.OrdinalIgnoreCase))
							{
								moduleName = mod.Name;
								modulePath = mod.Path;
								moduleLoadAddr = mod.LoadAddress;
								moduleEndAddr = mod.EndAddress;
								break;
							}
						}
						catch { }
					}
				}
			}

			var warnings = new List<BridgeWarning>();
			var dumpPath = GetActiveDumpFilePath(dte, debugger);
			var localPePath = ResolveLocalModuleBinaryPath(dte, modulePath, moduleName ?? frameModuleName, dumpPath);

			if (!string.IsNullOrEmpty(localPePath) &&
			    moduleLoadAddr > 0 &&
			    MinidumpParser.TryReadPeImageBase(localPePath!, out var preferredImageBase))
			{
				var dumpbinExe = FindDumpbinExecutable();
				if (!string.IsNullOrEmpty(dumpbinExe))
				{
					var instructions = await Task.Run(() =>
						RunDumpbinDisassembly(
							dumpbinExe!,
							localPePath!,
							preferredImageBase,
							moduleLoadAddr,
							targetAddr,
							instructionCount,
							instructionsBefore),
						cancellationToken);

					if (instructions.Count > 0)
					{
						var resolvedFn = instructions.FirstOrDefault(i => i.IsCurrentInstruction)?.Symbol
						                 ?? instructions.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.Symbol))?.Symbol
						                 ?? frameFunctionName;

						return new DebuggerGetDisassemblyResponse
						{
							VsInstanceId = _vsInstanceId,
							TargetAddress = $"0x{targetAddr:X16}",
							FrameIndex = targetFrameIndex,
							ModuleName = !string.IsNullOrWhiteSpace(moduleName) ? Path.GetFileName(moduleName) : Path.GetFileName(localPePath),
							ModulePath = localPePath,
							FunctionName = resolvedFn,
							FormattedText = MinidumpParser.FormatDisassemblyText(instructions),
							Instructions = instructions,
							Warnings = warnings
						};
					}
				}
				else
				{
					warnings.Add(new BridgeWarning
					{
						Code = "dumpbin_not_found",
						Message = "MSVC dumpbin.exe was not found in the Visual Studio installation."
					});
				}
			}

			// Tier 2 Fallback: Direct memory disassembly via Iced engine (works for both Minidumps and live processes)
			var memBytes = ReadRawMemoryBytesInternal(dte, debugger, targetAddr, instructionCount * 10, out _);
			if (memBytes != null && memBytes.Length > 0)
			{
				var icedInstructions = MinidumpParser.DisassembleMemoryBytes(memBytes, targetAddr, 64, targetAddr, instructionCount);
				if (icedInstructions.Count > 0)
				{
					return new DebuggerGetDisassemblyResponse
					{
						VsInstanceId = _vsInstanceId,
						TargetAddress = $"0x{targetAddr:X16}",
						FrameIndex = targetFrameIndex,
						ModuleName = !string.IsNullOrWhiteSpace(moduleName) ? Path.GetFileName(moduleName) : Path.GetFileName(localPePath ?? frameModuleName),
						ModulePath = localPePath ?? modulePath,
						FunctionName = frameFunctionName,
						FormattedText = MinidumpParser.FormatDisassemblyText(icedInstructions),
						Instructions = icedInstructions,
						Warnings = warnings
					};
				}
			}

			throw new DebuggerProviderException(
				BridgeErrorCodes.DisassemblyUnavailable,
				$"Disassembly is unavailable at address 0x{targetAddr:X16} (module: {moduleName ?? frameModuleName ?? "unknown"}). Ensure the module binary or memory is accessible.");
		}
		finally
		{
			if (switchedThread && originalThread != null)
			{
				try
				{
					debugger.CurrentThread = originalThread;
				}
				catch { }
			}

			if (switchedFrame && originalFrame != null)
			{
				try
				{
					debugger.CurrentStackFrame = originalFrame;
				}
				catch { }
			}
		}
	}

	public async Task<DebuggerLoadSymbolsResponse> LoadSymbolsAsync(
		DebuggerLoadSymbolsRequest request,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(request.SymbolPath))
		{
			throw new DebuggerProviderException(BridgeErrorCodes.InvalidRequest, "SymbolPath is required.");
		}

		var rawPath = request.SymbolPath.Trim();
		bool isFile = File.Exists(rawPath);
		bool isDir = !isFile && Directory.Exists(rawPath);

		if (!isFile && !isDir)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.FileNotFound,
				$"The specified symbol path '{rawPath}' does not exist on disk.");
		}

		var fullPath = Path.GetFullPath(rawPath);
		var symbolDirectory = isFile
			? (Path.GetDirectoryName(fullPath) ?? fullPath)
			: fullPath;

		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var debugger = await GetDebuggerAsync(cancellationToken);

		if (debugger.CurrentMode == dbgDebugMode.dbgDesignMode)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.DebuggerNotRunning,
				"Cannot load symbols when the debugger is in design mode.");
		}

		var warnings = new List<BridgeWarning>();
		var addedPaths = new List<string>();
		var allPaths = new List<string>();
		var symbolPathRegistered = false;

		try
		{
			var shellDebugger = await _package.GetServiceAsync(typeof(SVsShellDebugger));
			if (shellDebugger is Microsoft.VisualStudio.Debugger.Interop.IVsDebuggerSymbolSettingsManager120A symMgr)
			{
				var settings = symMgr.GetCurrentSymbolSettings();
				if (settings?.SymbolPaths != null)
				{
					var normalizedDir = symbolDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
					var alreadyPresent = false;
					for (int i = 0; i < settings.SymbolPaths.Count; i++)
					{
						var existing = settings.SymbolPaths[i];
						if (!string.IsNullOrWhiteSpace(existing.Name))
						{
							if (string.Equals(
								existing.Name.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
								normalizedDir,
								StringComparison.OrdinalIgnoreCase))
							{
								alreadyPresent = true;
							}
						}
					}

					if (!alreadyPresent)
					{
						settings.SymbolPaths.Add(symbolDirectory, true);
						addedPaths.Add(symbolDirectory);
					}

					symMgr.SaveSymbolSettings(settings);
					symbolPathRegistered = true;

					for (int i = 0; i < settings.SymbolPaths.Count; i++)
					{
						var p = settings.SymbolPaths[i];
						if (!string.IsNullOrWhiteSpace(p.Name))
						{
							allPaths.Add(p.Name);
						}
					}
				}
			}
			else
			{
				warnings.Add(new BridgeWarning
				{
					Code = "symbol_settings_manager_unavailable",
					Message = "IVsDebuggerSymbolSettingsManager120A is unavailable on SVsShellDebugger."
				});
			}
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not OperationCanceledException)
		{
			warnings.Add(new BridgeWarning
			{
				Code = "symbol_settings_update_failed",
				Message = $"Failed to update Visual Studio symbol settings: {ex.Message}"
			});
		}

		// Give Visual Studio a brief moment to reload symbols after SaveSymbolSettings
		if (symbolPathRegistered)
		{
			await Task.Delay(200, cancellationToken);
			await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		}

		var moduleFilter = !string.IsNullOrWhiteSpace(request.ModuleName)
			? request.ModuleName!.Trim()
			: (isFile && fullPath.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)
				? Path.GetFileNameWithoutExtension(fullPath)
				: null);

		var modulesReq = new DebuggerGetModulesRequest
		{
			NameFilter = moduleFilter,
			MaxCount = 200
		};
		var modulesResp = await GetModulesAsync(modulesReq, cancellationToken);

		// If inferred filter from .pdb stem matched nothing, return all modules
		if (modulesResp.Modules.Count == 0 && string.IsNullOrWhiteSpace(request.ModuleName) && !string.IsNullOrWhiteSpace(moduleFilter))
		{
			modulesResp = await GetModulesAsync(new DebuggerGetModulesRequest { MaxCount = 200 }, cancellationToken);
		}

		var loadedCount = modulesResp.Modules.Count(m => m.SymbolsLoaded);

		return new DebuggerLoadSymbolsResponse
		{
			VsInstanceId = _vsInstanceId,
			AddedSymbolPaths = addedPaths,
			AllSymbolPaths = allPaths,
			LoadedModulesCount = loadedCount,
			Modules = modulesResp.Modules,
			Warnings = warnings
		};
	}

	private static string? GetActiveDumpFilePath(DTE2 dte, Debugger debugger)
	{
		ThreadHelper.ThrowIfNotOnUIThread();
		try
		{
			var procName = debugger.CurrentProcess?.Name;
			if (MinidumpParser.IsMinidumpFile(procName))
			{
				return procName;
			}
		}
		catch { }

		try
		{
			var slnName = dte.Solution?.FullName;
			if (MinidumpParser.IsMinidumpFile(slnName))
			{
				return slnName;
			}
		}
		catch { }

		return null;
	}

	private static ulong TryResolveModuleOffsetOrSymbol(Debugger debugger, string expr)
	{
		ThreadHelper.ThrowIfNotOnUIThread();
		if (string.IsNullOrWhiteSpace(expr)) return 0;
		var trimmed = expr.Trim();

		// Match: module!func+offset OR module!func OR module!+offset
		// Match: module+offset
		var bangMatch = Regex.Match(trimmed, @"^([a-zA-Z0-9_\.-]+)!(?:([a-zA-Z0-9_:]+))?(?:\+(0x[0-9a-fA-F]+|[0-9a-fA-F]+))?$");
		var plusMatch = !bangMatch.Success ? Regex.Match(trimmed, @"^([a-zA-Z0-9_\.-]+)\+(0x[0-9a-fA-F]+|[0-9a-fA-F]+)$") : null;

		if (!bangMatch.Success && (plusMatch == null || !plusMatch.Success))
		{
			return 0;
		}

		string targetMod;
		string? targetSymbol = null;
		string? targetOffsetStr;

		if (bangMatch.Success)
		{
			targetMod = bangMatch.Groups[1].Value;
			if (bangMatch.Groups[2].Success && !string.IsNullOrEmpty(bangMatch.Groups[2].Value))
			{
				targetSymbol = bangMatch.Groups[2].Value;
			}
			targetOffsetStr = bangMatch.Groups[3].Success ? bangMatch.Groups[3].Value : null;
		}
		else
		{
			targetMod = plusMatch!.Groups[1].Value;
			targetOffsetStr = plusMatch.Groups[2].Value;
		}

		ulong offsetVal = 0;
		if (!string.IsNullOrEmpty(targetOffsetStr))
		{
			if (targetOffsetStr!.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			{
				ulong.TryParse(targetOffsetStr.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out offsetVal);
			}
			else
			{
				if (!ulong.TryParse(targetOffsetStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out offsetVal))
				{
					ulong.TryParse(targetOffsetStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out offsetVal);
				}
			}
		}

		// Find module in debugger.CurrentProcess
		EnvDTE90.Module? matchedMod = null;
		if (debugger.CurrentProcess is Process3 proc3 && proc3.Modules != null)
		{
			var modBase = Path.GetFileNameWithoutExtension(targetMod);
			var modFull = Path.GetFileName(targetMod);
			foreach (EnvDTE90.Module mod in proc3.Modules)
			{
				try
				{
					var mName = mod.Name ?? string.Empty;
					if (string.Equals(mName, targetMod, StringComparison.OrdinalIgnoreCase) ||
					    string.Equals(Path.GetFileName(mName), modFull, StringComparison.OrdinalIgnoreCase) ||
					    string.Equals(Path.GetFileNameWithoutExtension(mName), modBase, StringComparison.OrdinalIgnoreCase))
					{
						matchedMod = mod;
						break;
					}
				}
				catch { }
			}
		}

		if (matchedMod != null && matchedMod.LoadAddress > 0)
		{
			// If symbol is specified, try evaluating {,,mod}symbol in VS EE
			if (!string.IsNullOrEmpty(targetSymbol))
			{
				try
				{
					var modNameForEE = Path.GetFileName(matchedMod.Name);
					var eeExpr = $"{{,,{modNameForEE}}}{targetSymbol}";
					var res = debugger.GetExpression(eeExpr, UseAutoExpandRules: false, Timeout: 1000);
					if (res != null && res.IsValidValue && !string.IsNullOrWhiteSpace(res.Value))
					{
						var match = Regex.Match(res.Value.Trim(), @"0x[0-9a-fA-F]+");
						if (match.Success && ulong.TryParse(match.Value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var symAddr))
						{
							return symAddr + offsetVal;
						}
					}
				}
				catch { }
			}

			// If no symbol or EE failed to resolve symbol (e.g. no PDB), use module base + offset!
			if (offsetVal > 0 || string.IsNullOrEmpty(targetSymbol))
			{
				return matchedMod.LoadAddress + offsetVal;
			}
		}

		return 0;
	}

	private static ulong TryResolveAddressExpression(Debugger debugger, string rawAddress)
	{
		ThreadHelper.ThrowIfNotOnUIThread();
		if (string.IsNullOrWhiteSpace(rawAddress))
		{
			return 0;
		}

		var trimmed = rawAddress.Trim();
		if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
		    ulong.TryParse(trimmed.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var directHex))
		{
			return directHex;
		}

		if (trimmed.Length >= 4 &&
		    trimmed.All(Uri.IsHexDigit) &&
		    !IsCpuRegisterName(trimmed) &&
		    ulong.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var bareHex))
		{
			return bareHex;
		}

		// Try WinDbg / module offset syntax first (e.g. "PrintConfigDll!print+0x4aa2a", "PrintConfigDll+0x4aa2a")
		var modAddr = TryResolveModuleOffsetOrSymbol(debugger, trimmed);
		if (modAddr > 0)
		{
			return modAddr;
		}

		try
		{
			var expr = debugger.GetExpression(trimmed, UseAutoExpandRules: false, Timeout: 2000);
			if (expr != null && expr.IsValidValue && !string.IsNullOrWhiteSpace(expr.Value))
			{
				var val = expr.Value.Trim();
				var match = Regex.Match(val, @"0x[0-9a-fA-F]+");
				if (match.Success &&
				    ulong.TryParse(match.Value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var evalHex))
				{
					return evalHex;
				}
				if (ulong.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var evalDec))
				{
					return evalDec;
				}
				if (val.All(Uri.IsHexDigit) &&
				    ulong.TryParse(val, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rawEvalHex))
				{
					return rawEvalHex;
				}
			}
		}
		catch { }

		return 0;
	}

	private static bool IsCpuRegisterName(string name) =>
		string.Equals(name, "rip", StringComparison.OrdinalIgnoreCase) ||
		string.Equals(name, "eip", StringComparison.OrdinalIgnoreCase) ||
		string.Equals(name, "rax", StringComparison.OrdinalIgnoreCase) ||
		string.Equals(name, "rbx", StringComparison.OrdinalIgnoreCase) ||
		string.Equals(name, "rcx", StringComparison.OrdinalIgnoreCase) ||
		string.Equals(name, "rdx", StringComparison.OrdinalIgnoreCase) ||
		string.Equals(name, "rsi", StringComparison.OrdinalIgnoreCase) ||
		string.Equals(name, "rdi", StringComparison.OrdinalIgnoreCase) ||
		string.Equals(name, "rbp", StringComparison.OrdinalIgnoreCase) ||
		string.Equals(name, "rsp", StringComparison.OrdinalIgnoreCase);

	private static bool TryReadMemoryViaExpressionEvaluator(
		Debugger debugger,
		ulong startAddr,
		int byteCount,
		out byte[] bytes)
	{
		ThreadHelper.ThrowIfNotOnUIThread();
		bytes = Array.Empty<byte>();
		var maxBytes = Math.Min(byteCount, 512);
		var result = new List<byte>(maxBytes);

		for (int offset = 0; offset < maxBytes; offset += 8)
		{
			var addr = startAddr + (ulong)offset;
			try
			{
				var expr = debugger.GetExpression($"*(unsigned __int64*)0x{addr:X16},x", UseAutoExpandRules: false, Timeout: 500);
				if (expr == null || !expr.IsValidValue || string.IsNullOrWhiteSpace(expr.Value))
				{
					break;
				}

				var match = Regex.Match(expr.Value, @"0x([0-9a-fA-F]+)");
				if (!match.Success ||
				    !ulong.TryParse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var qword))
				{
					break;
				}

				var chunkBytes = BitConverter.GetBytes(qword);
				var needed = Math.Min(8, maxBytes - offset);
				for (int i = 0; i < needed; i++)
				{
					result.Add(chunkBytes[i]);
				}
			}
			catch
			{
				break;
			}
		}

		if (result.Count == 0)
		{
			return false;
		}

		bytes = result.ToArray();
		return true;
	}

	private static string EnrichCharPointerValue(
		DTE2 dte,
		Debugger debugger,
		string expression,
		string val,
		string type,
		int timeoutMs)
	{
		ThreadHelper.ThrowIfNotOnUIThread();
		if (string.IsNullOrWhiteSpace(val) || string.IsNullOrWhiteSpace(type))
		{
			return val;
		}

		// Only enrich if the expression doesn't already specify a format specifier and val is a bare hex pointer
		if (expression.IndexOf(',') >= 0)
		{
			return val;
		}

		var trimmedVal = val.Trim();
		if (!Regex.IsMatch(trimmedVal, @"^0x[0-9a-fA-F]{4,16}$"))
		{
			return val;
		}

		if (!ulong.TryParse(trimmedVal.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var ptrAddr) ||
		    ptrAddr == 0)
		{
			return val;
		}

		bool isWide = type.IndexOf("wchar_t", StringComparison.OrdinalIgnoreCase) >= 0 ||
		              type.IndexOf("char16_t", StringComparison.OrdinalIgnoreCase) >= 0 ||
		              type.IndexOf("WCHAR", StringComparison.Ordinal) >= 0 ||
		              type.IndexOf("LPWSTR", StringComparison.OrdinalIgnoreCase) >= 0 ||
		              type.IndexOf("LPCWSTR", StringComparison.OrdinalIgnoreCase) >= 0;

		bool isNarrow = !isWide && (
			type.IndexOf("char *", StringComparison.OrdinalIgnoreCase) >= 0 ||
			type.IndexOf("char*", StringComparison.OrdinalIgnoreCase) >= 0 ||
			type.IndexOf("LPSTR", StringComparison.OrdinalIgnoreCase) >= 0 ||
			type.IndexOf("LPCSTR", StringComparison.OrdinalIgnoreCase) >= 0);

		if (!isWide && !isNarrow)
		{
			return val;
		}

		// 1. Try MSVC format specifier (,su or ,s)
		var spec = isWide ? "su" : "s";
		try
		{
			var formattedExpr = debugger.GetExpression($"({expression}),{spec}", UseAutoExpandRules: false, Timeout: Math.Min(timeoutMs, 1000));
			if (formattedExpr != null && formattedExpr.IsValidValue && !string.IsNullOrWhiteSpace(formattedExpr.Value))
			{
				var fmtVal = formattedExpr.Value.Trim();
				if (fmtVal.IndexOf('"') >= 0 && !string.Equals(fmtVal, trimmedVal, StringComparison.OrdinalIgnoreCase))
				{
					return fmtVal.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
						? fmtVal
						: $"{trimmedVal} {fmtVal}";
				}
			}
		}
		catch { }

		// 2. Fallback: If debugging a Minidump (.dmp), read string directly from Minidump memory stream
		var dumpPath = GetActiveDumpFilePath(dte, debugger);
		if (!string.IsNullOrEmpty(dumpPath) &&
		    MinidumpParser.TryReadMemory(dumpPath!, ptrAddr, 256, out var dmpBytes) &&
		    dmpBytes.Length > 0)
		{
			if (isWide)
			{
				var preview = MinidumpParser.ExtractUtf16Preview(dmpBytes, 120);
				if (!string.IsNullOrEmpty(preview))
				{
					return $"{trimmedVal} L\"{preview}\"";
				}
			}
			else
			{
				var sb = new StringBuilder();
				for (int i = 0; i < dmpBytes.Length && i < 120; i++)
				{
					var b = dmpBytes[i];
					if (b == 0) break;
					if (b >= 32 && b <= 126) sb.Append((char)b);
					else break;
				}
				if (sb.Length > 0)
				{
					return $"{trimmedVal} \"{sb}\"";
				}
			}
		}

		return val;
	}

	private string? ResolveLocalModuleBinaryPath(
		DTE2 dte,
		string? modulePath,
		string? moduleName,
		string? dumpPath)
	{
		ThreadHelper.ThrowIfNotOnUIThread();
		if (!string.IsNullOrWhiteSpace(modulePath) && File.Exists(modulePath))
		{
			return modulePath;
		}

		var fileName = !string.IsNullOrWhiteSpace(modulePath)
			? Path.GetFileName(modulePath)
			: (!string.IsNullOrWhiteSpace(moduleName) ? Path.GetFileName(moduleName) : null);

		if (string.IsNullOrWhiteSpace(fileName))
		{
			return null;
		}

		// 1. Check directory of the active Minidump file
		if (!string.IsNullOrWhiteSpace(dumpPath))
		{
			try
			{
				var dumpDir = Path.GetDirectoryName(dumpPath);
				if (!string.IsNullOrWhiteSpace(dumpDir))
				{
					var candidate = Path.Combine(dumpDir, fileName);
					if (File.Exists(candidate))
					{
						return candidate;
					}
				}
			}
			catch { }
		}

		// 2. Check Visual Studio Debugger Symbol Paths
		try
		{
			var shellDebugger = Package.GetGlobalService(typeof(SVsShellDebugger));
			if (shellDebugger is Microsoft.VisualStudio.Debugger.Interop.IVsDebuggerSymbolSettingsManager120A symMgr)
			{
				var settings = symMgr.GetCurrentSymbolSettings();
				if (settings?.SymbolPaths != null)
				{
					for (int i = 0; i < settings.SymbolPaths.Count; i++)
					{
						var symDir = settings.SymbolPaths[i].Name;
						if (!string.IsNullOrWhiteSpace(symDir) && Directory.Exists(symDir))
						{
							var candidate = Path.Combine(symDir, fileName);
							if (File.Exists(candidate))
							{
								return candidate;
							}
						}
					}
				}
			}
		}
		catch { }

		// 3. Check open solution directory
		try
		{
			var slnFull = dte.Solution?.FullName;
			if (!string.IsNullOrWhiteSpace(slnFull))
			{
				var slnDir = Directory.Exists(slnFull) ? slnFull : Path.GetDirectoryName(slnFull);
				if (!string.IsNullOrWhiteSpace(slnDir) && Directory.Exists(slnDir))
				{
					var found = Directory.GetFiles(slnDir, fileName, SearchOption.AllDirectories);
					if (found.Length > 0)
					{
						return found[0];
					}
				}
			}
		}
		catch { }

		return null;
	}

	private static string? FindDumpbinExecutable()
	{
		try
		{
			var procPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
			if (!string.IsNullOrEmpty(procPath))
			{
				// devenv.exe is at <VsRoot>\Common7\IDE\devenv.exe
				var ideDir = Path.GetDirectoryName(procPath);
				if (!string.IsNullOrEmpty(ideDir))
				{
					var vsRoot = Path.GetFullPath(Path.Combine(ideDir, @"..\.."));
					var msvcDir = Path.Combine(vsRoot, "VC", "Tools", "MSVC");
					if (Directory.Exists(msvcDir))
					{
						var versions = Directory.GetDirectories(msvcDir);
						Array.Sort(versions, StringComparer.OrdinalIgnoreCase);
						for (int i = versions.Length - 1; i >= 0; i--)
						{
							var candidate = Path.Combine(versions[i], "bin", "Hostx64", "x64", "dumpbin.exe");
							if (File.Exists(candidate))
							{
								return candidate;
							}
						}
					}
				}
			}
		}
		catch { }

		return null;
	}

	private static List<DisassemblyInstruction> RunDumpbinDisassembly(
		string dumpbinExe,
		string modulePath,
		ulong preferredImageBase,
		ulong runtimeLoadAddr,
		ulong targetAddr,
		int instructionCount,
		int instructionsBefore)
	{
		var rva = targetAddr - runtimeLoadAddr;
		var targetFileVa = preferredImageBase + rva;
		var preBytes = (ulong)Math.Max(128, instructionsBefore * 16 + 64);
		var postBytes = (ulong)Math.Max(128, (instructionCount - instructionsBefore) * 16 + 64);
		var rangeStart = targetFileVa > preferredImageBase + preBytes ? targetFileVa - preBytes : preferredImageBase;
		var rangeEnd = targetFileVa + postBytes;

		// 1. Try fast /RANGE disassembly first (works when PDB or export symbols start within or before range)
		try
		{
			var psiRange = new System.Diagnostics.ProcessStartInfo
			{
				FileName = dumpbinExe,
				Arguments = $"/NOLOGO /DISASM:BYTES /RANGE:{rangeStart},{rangeEnd} \"{modulePath}\"",
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};

			using var proc = System.Diagnostics.Process.Start(psiRange);
			if (proc != null)
			{
				var output = proc.StandardOutput.ReadToEnd();
				proc.WaitForExit(3000);
				var rangedList = MinidumpParser.ParseAndRebaseDumpbinOutput(
					output,
					targetRuntimeAddress: targetAddr,
					moduleLoadAddress: runtimeLoadAddr,
					peImageBase: preferredImageBase,
					instructionCount: instructionCount,
					instructionsBefore: instructionsBefore);

				if (rangedList.Count > 0)
				{
					return rangedList;
				}
			}
		}
		catch { }

		// 2. Fallback for stripped DLLs where /RANGE outputs raw hex bytes if no symbol starts inside /RANGE:
		// Stream full /DISASM:BYTES output and stop as soon as we pass targetFileVa + postBytes.
		try
		{
			var psiFull = new System.Diagnostics.ProcessStartInfo
			{
				FileName = dumpbinExe,
				Arguments = $"/NOLOGO /DISASM:BYTES \"{modulePath}\"",
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};

			using var proc = System.Diagnostics.Process.Start(psiFull);
			if (proc != null)
			{
				var maxRollingLines = Math.Max(100, instructionsBefore * 3 + 50);
				var rollingLines = new Queue<string>(maxRollingLines);
				var postLinesCollected = new List<string>();
				bool reachedTarget = false;
				var maxPostLines = Math.Max(100, (instructionCount - instructionsBefore) * 3 + 50);

				string? line;
				while ((line = proc.StandardOutput.ReadLine()) != null)
				{
					if (!reachedTarget)
					{
						var trimmed = line.TrimStart();
						if (trimmed.Length >= 8)
						{
							int colonIdx = trimmed.IndexOf(':');
							if (colonIdx >= 8 && colonIdx <= 16 &&
							    ulong.TryParse(trimmed.Substring(0, colonIdx), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var lineVa))
							{
								if (lineVa >= targetFileVa)
								{
									reachedTarget = true;
								}
							}
						}

						if (!reachedTarget)
						{
							if (rollingLines.Count >= maxRollingLines)
							{
								rollingLines.Dequeue();
							}
							rollingLines.Enqueue(line);
							continue;
						}
					}

					postLinesCollected.Add(line);
					if (postLinesCollected.Count >= maxPostLines)
					{
						try { proc.Kill(); } catch { }
						break;
					}
				}

				var combined = new StringBuilder();
				foreach (var l in rollingLines)
				{
					combined.AppendLine(l);
				}
				foreach (var l in postLinesCollected)
				{
					combined.AppendLine(l);
				}

				return MinidumpParser.ParseAndRebaseDumpbinOutput(
					combined.ToString(),
					targetRuntimeAddress: targetAddr,
					moduleLoadAddress: runtimeLoadAddr,
					peImageBase: preferredImageBase,
					instructionCount: instructionCount,
					instructionsBefore: instructionsBefore);
			}
		}
		catch { }

		return new List<DisassemblyInstruction>();
	}

	public async Task<DebuggerGetRegistersResponse> GetRegistersAsync(
		DebuggerGetRegistersRequest request,
		CancellationToken cancellationToken)
	{
		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var (dte, debugger) = await GetDteAndDebuggerAsync(cancellationToken);

		if (debugger.CurrentMode == dbgDebugMode.dbgDesignMode)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.DebuggerNotDebugging,
				"Cannot retrieve registers when debugger is in design mode.");
		}

		var warnings = new List<BridgeWarning>();
		var dumpPath = GetActiveDumpFilePath(dte, debugger);

		// If dump is available, use MinidumpParser.TryReadThreadContext
		if (!string.IsNullOrEmpty(dumpPath))
		{
			if (MinidumpParser.TryReadThreadContext(dumpPath, request.ThreadId, out var ctx) && ctx != null)
			{
				return new DebuggerGetRegistersResponse
				{
					VsInstanceId = _vsInstanceId,
					ThreadId = ctx.ThreadId,
					Architecture = ctx.Architecture,
					Registers = ctx.Registers,
					DetailedRegisters = ctx.DetailedRegisters,
					InstructionPointer = ctx.InstructionPointer,
					StackPointer = ctx.StackPointer,
					FramePointer = ctx.FramePointer,
					FlagsHex = ctx.FlagsHex,
					FlagBits = ctx.FlagBits,
					Warnings = warnings
				};
			}
		}

		// Fallback for live debugger session: evaluate registers
		var currentThread = debugger.CurrentThread;
		var threadId = currentThread?.ID ?? 0;
		if (request.ThreadId.HasValue && request.ThreadId.Value > 0 && debugger.CurrentProgram?.Threads != null)
		{
			foreach (EnvDTE.Thread th in debugger.CurrentProgram.Threads)
			{
				if (th.ID == request.ThreadId.Value)
				{
					debugger.CurrentThread = th;
					currentThread = th;
					threadId = th.ID;
					break;
				}
			}
		}

		var regDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var detailed = new List<RegisterValueInfo>();
		var x64Names = new[] { "rax", "rbx", "rcx", "rdx", "rsi", "rdi", "rbp", "rsp", "r8", "r9", "r10", "r11", "r12", "r13", "r14", "r15", "rip", "eflags" };

		string ip = string.Empty;
		string sp = string.Empty;
		string? bp = null;
		string flagsHex = "0x0";

		foreach (var reg in x64Names)
		{
			var val = TryResolveAddressExpression(debugger, "@" + reg);
			if (val == 0) val = TryResolveAddressExpression(debugger, reg);
			var hexStr = $"0x{val:X16}";
			var upperName = reg.ToUpperInvariant();
			regDict[upperName] = hexStr;
			detailed.Add(new RegisterValueInfo { Name = upperName, ValueHex = hexStr, Value64 = val });

			if (reg == "rip") ip = hexStr;
			else if (reg == "rsp") sp = hexStr;
			else if (reg == "rbp") bp = hexStr;
			else if (reg == "eflags") flagsHex = $"0x{val:X8}";
		}

		return new DebuggerGetRegistersResponse
		{
			VsInstanceId = _vsInstanceId,
			ThreadId = threadId,
			Architecture = "x64",
			Registers = regDict,
			DetailedRegisters = detailed,
			InstructionPointer = ip,
			StackPointer = sp,
			FramePointer = bp,
			FlagsHex = flagsHex,
			FlagBits = new Dictionary<string, bool>(),
			Warnings = warnings
		};
	}

	public async Task<DebuggerGetCrashSummaryResponse> GetCrashSummaryAsync(
		DebuggerGetCrashSummaryRequest request,
		CancellationToken cancellationToken)
	{
		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var (dte, debugger) = await GetDteAndDebuggerAsync(cancellationToken);

		var dumpPath = !string.IsNullOrWhiteSpace(request.DumpPath)
			? request.DumpPath!.Trim()
			: GetActiveDumpFilePath(dte, debugger);

		if (string.IsNullOrEmpty(dumpPath) || !File.Exists(dumpPath))
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.FileNotFound,
				"No crash dump file is currently open or specified.");
		}

		if (!MinidumpParser.TryReadCrashSummary(dumpPath, out var summary) || summary == null)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.DiagnosticsUnavailable,
				$"Unable to extract crash summary from '{dumpPath}'. Ensure it is a valid Minidump with an exception stream.");
		}

		string? faultingModule = null;
		string? faultingOffset = null;

		if (debugger.CurrentProcess is Process3 proc3 && proc3.Modules != null)
		{
			foreach (EnvDTE90.Module mod in proc3.Modules)
			{
				try
				{
					if (mod.LoadAddress > 0 && summary.ExceptionAddress >= mod.LoadAddress && summary.ExceptionAddress < mod.EndAddress)
					{
						faultingModule = mod.Name;
						faultingOffset = $"0x{summary.ExceptionAddress - mod.LoadAddress:X}";
						break;
					}
				}
				catch { }
			}
		}

		return new DebuggerGetCrashSummaryResponse
		{
			VsInstanceId = _vsInstanceId,
			DumpPath = dumpPath!,
			ProcessName = Path.GetFileName(dumpPath),
			FaultingThreadId = summary.ThreadId,
			ExceptionCodeHex = summary.ExceptionCodeHex,
			ExceptionName = summary.ExceptionName,
			ExceptionAddressHex = summary.ExceptionAddressHex,
			AccessOperation = summary.AccessOperation,
			FaultingAddressHex = summary.FaultingAddressHex,
			FaultingModule = faultingModule,
			FaultingModuleOffsetHex = faultingOffset,
			FormattedSummary = summary.FormattedSummary,
			FaultingInstructionDisassembly = summary.FaultingInstructionDisassembly,
			Registers = summary.Registers,
			Warnings = new List<BridgeWarning>()
		};
	}

	public async Task<DebuggerOpenDumpResponse> OpenDumpAsync(
		DebuggerOpenDumpRequest request,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(request.DumpPath))
		{
			throw new DebuggerProviderException(BridgeErrorCodes.InvalidRequest, "DumpPath is required.");
		}

		var path = Path.GetFullPath(request.DumpPath.Trim());
		if (!File.Exists(path))
		{
			throw new DebuggerProviderException(BridgeErrorCodes.FileNotFound, $"The dump file '{path}' does not exist on disk.");
		}

		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var (dte, _) = await GetDteAndDebuggerAsync(cancellationToken);

		try
		{
			dte.ItemOperations.OpenFile(path);
			return new DebuggerOpenDumpResponse
			{
				VsInstanceId = _vsInstanceId,
				DumpPath = path,
				Success = true,
				Message = $"Opened dump file '{Path.GetFileName(path)}' successfully in Visual Studio.",
				Warnings = new List<BridgeWarning>()
			};
		}
		catch (Exception ex)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.DebuggerUnavailable,
				$"Failed to open dump file in Visual Studio: {ex.Message}",
				ex);
		}
	}

	public async Task<DebuggerDumpPointersResponse> DumpPointersAsync(
		DebuggerDumpPointersRequest request,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(request.Address))
		{
			throw new DebuggerProviderException(BridgeErrorCodes.InvalidRequest, "Address is required.");
		}

		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var (dte, debugger) = await GetDteAndDebuggerAsync(cancellationToken);

		var resolvedAddr = TryResolveAddressExpression(debugger, request.Address.Trim());
		if (resolvedAddr == 0)
		{
			throw new DebuggerProviderException(BridgeErrorCodes.InvalidMemoryAddress, $"Unable to resolve address '{request.Address}'.");
		}

		var count = Clamp(request.Count ?? 16, 1, 128);
		var ptrSize = request.PointerSize == 4 ? 4 : 8;
		var totalBytes = count * ptrSize;

		var rawBytes = ReadRawMemoryBytesInternal(dte, debugger, resolvedAddr, totalBytes, out _);
		if (rawBytes == null || rawBytes.Length < ptrSize)
		{
			throw new DebuggerProviderException(BridgeErrorCodes.MemoryReadFailed, $"Failed to read memory at address 0x{resolvedAddr:X16}.");
		}

		var pointers = new List<PointerDumpEntry>();
		var sb = new StringBuilder();
		var availableCount = rawBytes.Length / ptrSize;

		for (int i = 0; i < availableCount; i++)
		{
			var curAddr = resolvedAddr + (ulong)(i * ptrSize);
			ulong ptrVal = 0;
			if (ptrSize == 8)
			{
				ptrVal = BitConverter.ToUInt64(rawBytes, i * 8);
			}
			else
			{
				ptrVal = BitConverter.ToUInt32(rawBytes, i * 4);
			}

			string? module = null;
			string? resolvedSymbol = null;

			if (debugger.CurrentProcess is Process3 proc3 && proc3.Modules != null)
			{
				foreach (EnvDTE90.Module mod in proc3.Modules)
				{
					try
					{
						if (mod.LoadAddress > 0 && ptrVal >= mod.LoadAddress && ptrVal < mod.EndAddress)
						{
							module = mod.Name;
							resolvedSymbol = $"{Path.GetFileName(mod.Name)}+0x{ptrVal - mod.LoadAddress:X}";
							break;
						}
					}
					catch { }
				}
			}

			var entry = new PointerDumpEntry
			{
				Address = ptrSize == 8 ? $"0x{curAddr:X16}" : $"0x{curAddr:X8}",
				PointerValue = ptrSize == 8 ? $"0x{ptrVal:X16}" : $"0x{ptrVal:X8}",
				PointerValue64 = ptrVal,
				ModuleName = module,
				ResolvedSymbol = resolvedSymbol
			};
			pointers.Add(entry);

			var symbolCol = !string.IsNullOrEmpty(resolvedSymbol) ? $" ({resolvedSymbol})" : string.Empty;
			sb.AppendLine($"{entry.Address}:  {entry.PointerValue}{symbolCol}");
		}

		return new DebuggerDumpPointersResponse
		{
			VsInstanceId = _vsInstanceId,
			BaseAddress = ptrSize == 8 ? $"0x{resolvedAddr:X16}" : $"0x{resolvedAddr:X8}",
			Count = pointers.Count,
			PointerSize = ptrSize,
			Pointers = pointers,
			FormattedText = sb.ToString(),
			Warnings = new List<BridgeWarning>()
		};
	}

	public async Task<DebuggerQueryMemoryResponse> QueryMemoryAsync(
		DebuggerQueryMemoryRequest request,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(request.Address))
		{
			throw new DebuggerProviderException(BridgeErrorCodes.InvalidRequest, "Address is required.");
		}

		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var (dte, debugger) = await GetDteAndDebuggerAsync(cancellationToken);

		var resolvedAddr = TryResolveAddressExpression(debugger, request.Address.Trim());
		if (resolvedAddr == 0)
		{
			throw new DebuggerProviderException(BridgeErrorCodes.InvalidMemoryAddress, $"Unable to resolve address '{request.Address}'.");
		}

		var dumpPath = GetActiveDumpFilePath(dte, debugger);

		// If dump: use MinidumpParser.TryQueryMemoryInDump
		if (!string.IsNullOrEmpty(dumpPath))
		{
			if (MinidumpParser.TryQueryMemoryInDump(dumpPath, resolvedAddr, out var memInfo) && memInfo != null)
			{
				string? containingMod = null;
				string? modOffset = null;
				if (debugger.CurrentProcess is Process3 proc3 && proc3.Modules != null)
				{
					foreach (EnvDTE90.Module mod in proc3.Modules)
					{
						try
						{
							if (mod.LoadAddress > 0 && resolvedAddr >= mod.LoadAddress && resolvedAddr < mod.EndAddress)
							{
								containingMod = mod.Name;
								modOffset = $"0x{resolvedAddr - mod.LoadAddress:X}";
								break;
							}
						}
						catch { }
					}
				}

				return new DebuggerQueryMemoryResponse
				{
					VsInstanceId = _vsInstanceId,
					Address = $"0x{resolvedAddr:X16}",
					IsValid = memInfo.IsPresentInDump,
					State = memInfo.IsPresentInDump ? "MEM_COMMIT" : "NOT_IN_DUMP",
					Protect = memInfo.IsPresentInDump ? "PAGE_READONLY_OR_READWRITE" : "NONE",
					Type = memInfo.StreamSource,
					BaseAddress = memInfo.BaseAddressHex,
					RegionSizeHex = memInfo.RegionSizeHex,
					RegionSizeBytes = memInfo.RegionSize,
					ContainingModule = containingMod,
					ModuleOffsetHex = modOffset,
					Description = memInfo.IsPresentInDump
						? $"Address 0x{resolvedAddr:X16} is captured in Minidump {memInfo.StreamSource} (Range: {memInfo.BaseAddressHex}, Size: {memInfo.RegionSizeHex})."
						: $"Address 0x{resolvedAddr:X16} is NOT present in any memory stream of Minidump '{Path.GetFileName(dumpPath)}'."
				};
			}
		}

		// Live process VirtualQueryEx
		var pid = 0;
		try { pid = debugger.CurrentProcess?.ProcessID ?? 0; } catch { }
		if (pid > 0)
		{
			var hProcess = OpenProcess(PROCESS_QUERY_INFORMATION, false, pid);
			if (hProcess != IntPtr.Zero)
			{
				try
				{
					if (VirtualQueryEx(hProcess, (IntPtr)(long)resolvedAddr, out var mbi, (uint)Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION))) > 0)
					{
						var stateStr = mbi.State switch
						{
							0x1000 => "MEM_COMMIT",
							0x2000 => "MEM_RESERVE",
							0x10000 => "MEM_FREE",
							_ => $"0x{mbi.State:X}"
						};
						var protStr = mbi.Protect switch
						{
							0x01 => "PAGE_NOACCESS",
							0x02 => "PAGE_READONLY",
							0x04 => "PAGE_READWRITE",
							0x10 => "PAGE_EXECUTE",
							0x20 => "PAGE_EXECUTE_READ",
							0x40 => "PAGE_EXECUTE_READWRITE",
							_ => $"0x{mbi.Protect:X}"
						};
						var typeStr = mbi.Type switch
						{
							0x1000000 => "MEM_IMAGE",
							0x40000 => "MEM_MAPPED",
							0x20000 => "MEM_PRIVATE",
							_ => $"0x{mbi.Type:X}"
						};

						string? containingMod = null;
						string? modOffset = null;
						if (debugger.CurrentProcess is Process3 proc3 && proc3.Modules != null)
						{
							foreach (EnvDTE90.Module mod in proc3.Modules)
							{
								try
								{
									if (mod.LoadAddress > 0 && resolvedAddr >= mod.LoadAddress && resolvedAddr < mod.EndAddress)
									{
										containingMod = mod.Name;
										modOffset = $"0x{resolvedAddr - mod.LoadAddress:X}";
										break;
									}
								}
								catch { }
							}
						}

						return new DebuggerQueryMemoryResponse
						{
							VsInstanceId = _vsInstanceId,
							Address = $"0x{resolvedAddr:X16}",
							IsValid = mbi.State == 0x1000,
							State = stateStr,
							Protect = protStr,
							Type = typeStr,
							BaseAddress = $"0x{(ulong)(long)mbi.BaseAddress:X16}",
							RegionSizeHex = $"0x{(ulong)(long)mbi.RegionSize:X}",
							RegionSizeBytes = (ulong)(long)mbi.RegionSize,
							ContainingModule = containingMod,
							ModuleOffsetHex = modOffset,
							Description = $"VirtualQueryEx: {stateStr}, {protStr}, {typeStr}, Base: 0x{(ulong)(long)mbi.BaseAddress:X16}, Size: 0x{(ulong)(long)mbi.RegionSize:X}."
						};
					}
				}
				finally
				{
					CloseHandle(hProcess);
				}
			}
		}

		throw new DebuggerProviderException(BridgeErrorCodes.MemoryReadFailed, $"Failed to query memory at address 0x{resolvedAddr:X16}.");
	}

	public async Task<DebuggerSwitchThreadResponse> SwitchThreadAsync(
		DebuggerSwitchThreadRequest request,
		CancellationToken cancellationToken)
	{
		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var debugger = await GetDebuggerAsync(cancellationToken);

		if (debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.DebuggerNotPaused,
				$"The debugger is not currently in break mode (current mode: {GetModeString(debugger.CurrentMode)}). Switching threads is only available when paused.");
		}

		var targetThread = FindThread(debugger, request.ThreadId);
		if (targetThread == null)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.ThreadNotFound,
				$"Debug thread with ID {request.ThreadId} not found in the current process.");
		}

		var prevThreadId = debugger.CurrentThread?.ID ?? 0;
		debugger.CurrentThread = targetThread;

		var threadName = targetThread.Name ?? string.Empty;
		var isAlive = false;
		try { isAlive = targetThread.IsAlive; } catch { }
		var suspendCount = 0;
		try { suspendCount = targetThread.SuspendCount; } catch { }

		string? topFrame = null;
		try
		{
			if (targetThread.StackFrames != null && targetThread.StackFrames.Count > 0)
			{
				var firstFrame = targetThread.StackFrames.Item(1);
				if (firstFrame != null)
				{
					topFrame = FormatFrameSummary(firstFrame);
				}
			}
		}
		catch { }

		return new DebuggerSwitchThreadResponse
		{
			VsInstanceId = _vsInstanceId,
			PreviousThreadId = prevThreadId,
			CurrentThreadId = targetThread.ID,
			ThreadName = threadName,
			IsAlive = isAlive,
			SuspendCount = suspendCount,
			TopFrame = topFrame,
			Warnings = new List<BridgeWarning>()
		};
	}

	public async Task<DebuggerGetAllThreadsCallStacksResponse> GetAllThreadsCallStacksAsync(
		DebuggerGetAllThreadsCallStacksRequest request,
		CancellationToken cancellationToken)
	{
		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var debugger = await GetDebuggerAsync(cancellationToken);

		if (debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.DebuggerNotPaused,
				$"The debugger is not currently in break mode (current mode: {GetModeString(debugger.CurrentMode)}). All-thread call stacks are only available when paused.");
		}

		var maxFramesPerThread = Clamp(request.MaxFramesPerThread ?? 5, 1, 50);
		var userCodeOnly = request.UserCodeOnly ?? false;

		var summaries = new List<ThreadCallStackSummary>();
		var deadlockWarnings = new List<string>();
		var currentThreadId = debugger.CurrentThread?.ID ?? 0;

		var lockContentionThreads = new List<int>();
		var nestedEventLoopThreads = new List<int>();
		var kernelWaitThreads = new List<int>();

		if (debugger.CurrentProgram?.Threads != null)
		{
			foreach (EnvDTE.Thread th in debugger.CurrentProgram.Threads)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var thId = th.ID;
				var thName = th.Name ?? string.Empty;
				var isAlive = false;
				try { isAlive = th.IsAlive; } catch { }
				var isFrozen = false;
				if (th is EnvDTE90.Thread2 th2)
				{
					try { isFrozen = th2.IsFrozen; } catch { }
				}
				var priority = string.Empty;
				try { priority = th.Priority ?? string.Empty; } catch { }
				var suspendCount = 0;
				try { suspendCount = th.SuspendCount; } catch { }

				string? topFrameName = null;
				string? firstUserFrameName = null;
				string? waitingHeuristic = null;
				var frameList = new List<StackFrameInfo>();

				try
				{
					if (th.StackFrames != null)
					{
						int frameIdx = 0;
						foreach (StackFrame sf in th.StackFrames)
						{
							if (frameIdx >= maxFramesPerThread) break;
							var frameInfo = ReadStackFrame(sf, frameIdx);
							if (topFrameName == null)
							{
								topFrameName = frameInfo.FunctionName;
							}
							if (firstUserFrameName == null && !IsExternalFrame(frameInfo))
							{
								firstUserFrameName = frameInfo.FunctionName;
							}

							if (!userCodeOnly || !IsExternalFrame(frameInfo))
							{
								frameList.Add(frameInfo);
							}
							frameIdx++;
						}
					}
				}
				catch { }

				if (!string.IsNullOrEmpty(topFrameName))
				{
					var topStr = topFrameName!;
					if (topStr.IndexOf("WaitForMultipleObjects", StringComparison.OrdinalIgnoreCase) >= 0 ||
					    topStr.IndexOf("WaitForSingleObject", StringComparison.OrdinalIgnoreCase) >= 0 ||
					    topStr.IndexOf("NtWaitFor", StringComparison.OrdinalIgnoreCase) >= 0 ||
					    topStr.IndexOf("ZwWaitFor", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						waitingHeuristic = "Kernel Synchronization Wait (Event / Semaphore / Mutex)";
						kernelWaitThreads.Add(thId);
					}
					else if (topStr.IndexOf("EnterCriticalSection", StringComparison.OrdinalIgnoreCase) >= 0 ||
					         topStr.IndexOf("RtlpWaitOnCriticalSection", StringComparison.OrdinalIgnoreCase) >= 0 ||
					         topStr.IndexOf("AcquireSRWLock", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						waitingHeuristic = "Lock Contention (CriticalSection / SRWLock)";
						lockContentionThreads.Add(thId);
					}
					else if (topStr.IndexOf("QEventLoop::exec", StringComparison.OrdinalIgnoreCase) >= 0 ||
					         topStr.IndexOf("QDialog::exec", StringComparison.OrdinalIgnoreCase) >= 0 ||
					         topStr.IndexOf("QCoreApplication::exec", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						waitingHeuristic = "Nested Qt Modal / Event Loop (Hang / Modal Deadlock Risk)";
						nestedEventLoopThreads.Add(thId);
					}
					else if (topStr.IndexOf("GetMessage", StringComparison.OrdinalIgnoreCase) >= 0 ||
					         topStr.IndexOf("PeekMessage", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						waitingHeuristic = "UI Message Pump Idle / Wait";
					}
					else if (topStr.IndexOf("clr!CallDescr", StringComparison.OrdinalIgnoreCase) >= 0 ||
					         topStr.IndexOf("coreclr!", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						waitingHeuristic = "Managed CLR Interop Boundary";
					}
				}

				summaries.Add(new ThreadCallStackSummary
				{
					ThreadId = thId,
					Name = thName,
					IsAlive = isAlive,
					IsFrozen = isFrozen,
					Priority = string.IsNullOrEmpty(priority) ? null : priority,
					SuspendCount = suspendCount,
					TopFrame = topFrameName,
					FirstUserFrame = firstUserFrameName,
					WaitingHeuristic = waitingHeuristic,
					Frames = frameList
				});
			}
		}

		if (nestedEventLoopThreads.Count > 0 && (kernelWaitThreads.Count > 0 || lockContentionThreads.Count > 0))
		{
			deadlockWarnings.Add($"Potential cross-framework modal event loop hang detected: Thread(s) [{string.Join(", ", nestedEventLoopThreads)}] running nested Qt exec() loop while {kernelWaitThreads.Count + lockContentionThreads.Count} thread(s) wait on synchronization primitives.");
		}
		if (lockContentionThreads.Count > 1)
		{
			deadlockWarnings.Add($"Lock contention detected across multiple threads [{string.Join(", ", lockContentionThreads)}]. Check for potential circular mutex/CriticalSection locking.");
		}

		return new DebuggerGetAllThreadsCallStacksResponse
		{
			VsInstanceId = _vsInstanceId,
			CurrentThreadId = currentThreadId,
			TotalThreads = summaries.Count,
			Threads = summaries,
			DeadlockWarnings = deadlockWarnings,
			Warnings = new List<BridgeWarning>()
		};
	}

	public async Task<DebuggerReadMemoryTypedResponse> ReadMemoryTypedAsync(
		DebuggerReadMemoryTypedRequest request,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(request.Address))
		{
			throw new DebuggerProviderException(BridgeErrorCodes.InvalidRequest, "Address is required.");
		}

		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var (dte, debugger) = await GetDteAndDebuggerAsync(cancellationToken);

		if (debugger.CurrentMode == dbgDebugMode.dbgDesignMode)
		{
			throw new DebuggerProviderException(
				BridgeErrorCodes.DebuggerNotDebugging,
				"Cannot read process memory when not debugging (debugger is in design mode).");
		}

		EnvDTE.Thread? originalThread = null;
		var switchedThread = false;
		if (request.ThreadId.HasValue)
		{
			var targetTh = FindThread(debugger, request.ThreadId.Value);
			if (targetTh != null && targetTh.ID != debugger.CurrentThread?.ID)
			{
				originalThread = debugger.CurrentThread;
				debugger.CurrentThread = targetTh;
				switchedThread = true;
			}
		}

		try
		{
			var requestedType = (request.Type ?? "ascii_string").Trim().ToLowerInvariant();
			var ptrSize = request.PointerSize == 4 ? 4 : 8;
			var warnings = new List<BridgeWarning>();
			var pointerChainValues = new List<string>();

			ulong resolvedAddr = 0;
			string? readSource = null;
			int bytesRead = 0;
			string? stringVal = null;
			string? numericVal = null;
			List<PointerDumpEntry>? pointersList = null;

			// Handle Pointer Chain syntax: e.g. "0x7ffb1234->+0x18->+0x8" or "PrintConfigDll!g_ptr->+0x20"
			if (request.Address.Contains("->"))
			{
				var steps = request.Address.Split(new[] { "->" }, StringSplitOptions.RemoveEmptyEntries);
				if (steps.Length > 0)
				{
					var baseStep = steps[0].Trim();
					resolvedAddr = TryResolveAddressExpression(debugger, baseStep);
					if (resolvedAddr == 0)
					{
						throw new DebuggerProviderException(
							BridgeErrorCodes.InvalidMemoryAddress,
							$"Unable to resolve base address from pointer chain step '{baseStep}'.");
					}
					pointerChainValues.Add($"[Base: {baseStep}] = 0x{resolvedAddr:X16}");

					for (int i = 1; i < steps.Length; i++)
					{
						var offsetStep = steps[i].Trim();
						long offset = 0;
						if (offsetStep.StartsWith("+")) offsetStep = offsetStep.Substring(1).Trim();
						if (offsetStep.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
						{
							long.TryParse(offsetStep.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out offset);
						}
						else
						{
							if (!long.TryParse(offsetStep, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out offset))
							{
								long.TryParse(offsetStep, NumberStyles.Integer, CultureInfo.InvariantCulture, out offset);
							}
						}

						var ptrBytes = ReadRawMemoryBytesInternal(dte, debugger, resolvedAddr, ptrSize, out readSource);
						if (ptrBytes == null || ptrBytes.Length < ptrSize)
						{
							throw new DebuggerProviderException(
								BridgeErrorCodes.MemoryReadFailed,
								$"Failed to read pointer at chain step {i} (address 0x{resolvedAddr:X16}).");
						}

						ulong nextPtr = ptrSize == 4 ? BitConverter.ToUInt32(ptrBytes, 0) : BitConverter.ToUInt64(ptrBytes, 0);
						if (nextPtr == 0)
						{
							pointerChainValues.Add($"-> [Null Pointer at 0x{resolvedAddr:X16}]");
							throw new DebuggerProviderException(
								BridgeErrorCodes.MemoryReadFailed,
								$"Encountered null pointer at chain step {i} (address 0x{resolvedAddr:X16}).");
						}

						var nextTarget = (ulong)((long)nextPtr + offset);
						pointerChainValues.Add($"-> *0x{resolvedAddr:X16} = 0x{nextPtr:X16} (+0x{offset:X}) => 0x{nextTarget:X16}");
						resolvedAddr = nextTarget;
					}
				}
			}
			else
			{
				resolvedAddr = TryResolveAddressExpression(debugger, request.Address.Trim());
				if (resolvedAddr == 0)
				{
					throw new DebuggerProviderException(
						BridgeErrorCodes.InvalidMemoryAddress,
						$"Unable to resolve memory address from '{request.Address}'.");
				}
			}

			// Format by requested type
			if (requestedType == "ascii_string" || requestedType == "da" || requestedType == "string")
			{
				var maxLen = Clamp(request.Length ?? 256, 1, 4096);
				var raw = ReadRawMemoryBytesInternal(dte, debugger, resolvedAddr, maxLen, out readSource);
				if (raw != null && raw.Length > 0)
				{
					bytesRead = raw.Length;
					var nullIdx = Array.IndexOf(raw, (byte)0);
					var len = nullIdx >= 0 ? nullIdx : raw.Length;
					try
					{
						stringVal = Encoding.Default.GetString(raw, 0, len);
					}
					catch
					{
						stringVal = Encoding.ASCII.GetString(raw, 0, len);
					}
				}
				else
				{
					throw new DebuggerProviderException(BridgeErrorCodes.MemoryReadFailed, $"Failed to read string memory at 0x{resolvedAddr:X16}.");
				}
			}
			else if (requestedType == "unicode_string" || requestedType == "du" || requestedType == "wstring" || requestedType == "utf16")
			{
				var maxChars = Clamp(request.Length ?? 256, 1, 2048);
				var maxBytes = maxChars * 2;
				var raw = ReadRawMemoryBytesInternal(dte, debugger, resolvedAddr, maxBytes, out readSource);
				if (raw != null && raw.Length >= 2)
				{
					bytesRead = raw.Length;
					var charCount = raw.Length / 2;
					var nullCharIdx = -1;
					for (int i = 0; i < charCount; i++)
					{
						if (raw[i * 2] == 0 && raw[i * 2 + 1] == 0)
						{
							nullCharIdx = i;
							break;
						}
					}
					var len = nullCharIdx >= 0 ? nullCharIdx * 2 : (charCount * 2);
					stringVal = Encoding.Unicode.GetString(raw, 0, len);
				}
				else
				{
					throw new DebuggerProviderException(BridgeErrorCodes.MemoryReadFailed, $"Failed to read unicode string memory at 0x{resolvedAddr:X16}.");
				}
			}
			else if (requestedType == "utf8_string" || requestedType == "utf8")
			{
				var maxLen = Clamp(request.Length ?? 256, 1, 4096);
				var raw = ReadRawMemoryBytesInternal(dte, debugger, resolvedAddr, maxLen, out readSource);
				if (raw != null && raw.Length > 0)
				{
					bytesRead = raw.Length;
					var nullIdx = Array.IndexOf(raw, (byte)0);
					var len = nullIdx >= 0 ? nullIdx : raw.Length;
					stringVal = Encoding.UTF8.GetString(raw, 0, len);
				}
				else
				{
					throw new DebuggerProviderException(BridgeErrorCodes.MemoryReadFailed, $"Failed to read UTF-8 memory at 0x{resolvedAddr:X16}.");
				}
			}
			else if (requestedType == "dword" || requestedType == "uint32" || requestedType == "int32")
			{
				var raw = ReadRawMemoryBytesInternal(dte, debugger, resolvedAddr, 4, out readSource);
				if (raw != null && raw.Length >= 4)
				{
					bytesRead = 4;
					uint u = BitConverter.ToUInt32(raw, 0);
					int s = BitConverter.ToInt32(raw, 0);
					numericVal = $"0x{u:X8} ({s})";
				}
				else
				{
					throw new DebuggerProviderException(BridgeErrorCodes.MemoryReadFailed, $"Failed to read DWORD at 0x{resolvedAddr:X16}.");
				}
			}
			else if (requestedType == "qword" || requestedType == "uint64" || requestedType == "int64" || requestedType == "pointer")
			{
				var raw = ReadRawMemoryBytesInternal(dte, debugger, resolvedAddr, 8, out readSource);
				if (raw != null && raw.Length >= 8)
				{
					bytesRead = 8;
					ulong u = BitConverter.ToUInt64(raw, 0);
					long s = BitConverter.ToInt64(raw, 0);
					numericVal = $"0x{u:X16} ({s})";
				}
				else
				{
					throw new DebuggerProviderException(BridgeErrorCodes.MemoryReadFailed, $"Failed to read QWORD at 0x{resolvedAddr:X16}.");
				}
			}
			else if (requestedType == "float")
			{
				var raw = ReadRawMemoryBytesInternal(dte, debugger, resolvedAddr, 4, out readSource);
				if (raw != null && raw.Length >= 4)
				{
					bytesRead = 4;
					float f = BitConverter.ToSingle(raw, 0);
					numericVal = f.ToString(CultureInfo.InvariantCulture);
				}
				else
				{
					throw new DebuggerProviderException(BridgeErrorCodes.MemoryReadFailed, $"Failed to read float at 0x{resolvedAddr:X16}.");
				}
			}
			else if (requestedType == "double")
			{
				var raw = ReadRawMemoryBytesInternal(dte, debugger, resolvedAddr, 8, out readSource);
				if (raw != null && raw.Length >= 8)
				{
					bytesRead = 8;
					double d = BitConverter.ToDouble(raw, 0);
					numericVal = d.ToString(CultureInfo.InvariantCulture);
				}
				else
				{
					throw new DebuggerProviderException(BridgeErrorCodes.MemoryReadFailed, $"Failed to read double at 0x{resolvedAddr:X16}.");
				}
			}
			else if (requestedType == "pointers" || requestedType == "dps")
			{
				var count = Clamp(request.Length ?? 16, 1, 128);
				var totalBytes = count * ptrSize;
				var raw = ReadRawMemoryBytesInternal(dte, debugger, resolvedAddr, totalBytes, out readSource);
				if (raw != null && raw.Length >= ptrSize)
				{
					bytesRead = raw.Length;
					pointersList = new List<PointerDumpEntry>();
					var avail = raw.Length / ptrSize;
					for (int i = 0; i < avail; i++)
					{
						var curAddr = resolvedAddr + (ulong)(i * ptrSize);
						ulong ptrVal = ptrSize == 8 ? BitConverter.ToUInt64(raw, i * 8) : BitConverter.ToUInt32(raw, i * 4);
						string? modName = null;
						string? sym = null;
						if (debugger.CurrentProcess is Process3 proc3 && proc3.Modules != null)
						{
							foreach (EnvDTE90.Module mod in proc3.Modules)
							{
								try
								{
									if (mod.LoadAddress > 0 && ptrVal >= mod.LoadAddress && ptrVal < mod.EndAddress)
									{
										modName = mod.Name;
										sym = $"{Path.GetFileName(mod.Name)}+0x{ptrVal - mod.LoadAddress:X}";
										break;
									}
								}
								catch { }
							}
						}
						pointersList.Add(new PointerDumpEntry
						{
							Address = $"0x{curAddr:X16}",
							PointerValue = $"0x{ptrVal:X16}",
							PointerValue64 = ptrVal,
							ModuleName = modName,
							ResolvedSymbol = sym
						});
					}
				}
				else
				{
					throw new DebuggerProviderException(BridgeErrorCodes.MemoryReadFailed, $"Failed to read pointers at 0x{resolvedAddr:X16}.");
				}
			}
			else
			{
				throw new DebuggerProviderException(BridgeErrorCodes.InvalidRequest, $"Unsupported memory type '{request.Type}'. Supported types: ascii_string, unicode_string, utf8_string, dword, qword, int32, int64, float, double, pointers, pointer_chain.");
			}

			return new DebuggerReadMemoryTypedResponse
			{
				VsInstanceId = _vsInstanceId,
				Address = request.Address,
				ResolvedAddress = $"0x{resolvedAddr:X16}",
				Type = requestedType,
				StringValue = stringVal,
				NumericValue = numericVal,
				PointerChainValues = pointerChainValues.Count > 0 ? pointerChainValues : null,
				Pointers = pointersList,
				BytesRead = bytesRead,
				ReadSource = readSource ?? "unknown",
				Warnings = warnings
			};
		}
		finally
		{
			if (switchedThread && originalThread != null)
			{
				try { debugger.CurrentThread = originalThread; } catch { }
			}
		}
	}

	private static string FormatFrameSummary(StackFrame frame)
	{
		ThreadHelper.ThrowIfNotOnUIThread();
		try
		{
			var fn = frame.FunctionName ?? "unknown";
			var mod = string.Empty;
			try { mod = frame.Module ?? string.Empty; } catch { }
			if (!string.IsNullOrEmpty(mod))
			{
				return $"{Path.GetFileName(mod)}!{fn}";
			}
			return fn;
		}
		catch
		{
			return "unknown";
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
