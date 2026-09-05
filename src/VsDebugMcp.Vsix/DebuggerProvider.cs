using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using VsDebugMcp.Protocol;

namespace VsDebugMcp_Vsix;

internal sealed class DebuggerProvider
{
	private const int DefaultMaxFrames = 50;
	private const int MaxAllowedFrames = 200;
	private const int DefaultTimeoutMs = 2000;
	private const int MaxAllowedTimeoutMs = 10000;

	private readonly AsyncPackage _package;
	private readonly string _vsInstanceId;

	public DebuggerProvider(AsyncPackage package, string vsInstanceId)
	{
		_package = package;
		_vsInstanceId = vsInstanceId;
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
					var addedBreakpoints = debugger.Breakpoints.Add(
						"",
						fullPath,
						spec.Line,
						col,
						condition);

					if (addedBreakpoints != null)
					{
						foreach (Breakpoint bp in addedBreakpoints)
						{
							bp.Enabled = spec.Enabled;
							response.Breakpoints.Add(new BreakpointInfo
							{
								Id = $"{fullPath}:{bp.FileLine}",
								FilePath = fullPath,
								Line = bp.FileLine,
								Column = bp.FileColumn,
								Condition = string.IsNullOrEmpty(bp.Condition) ? null : bp.Condition,
								Enabled = bp.Enabled,
								IsBound = true
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
		var debugger = await GetDebuggerAsync(cancellationToken);

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
				return new DebuggerEvaluateExprResponse
				{
					VsInstanceId = _vsInstanceId,
					Expression = request.Expression,
					Value = "<evaluation produced no result>",
					Type = "unknown",
					IsValid = false,
					FrameIndex = targetFrameIndex
				};
			}

			string val = expr.Value ?? string.Empty;
			string type = expr.Type ?? string.Empty;
			bool isValid = expr.IsValidValue;

			return new DebuggerEvaluateExprResponse
			{
				VsInstanceId = _vsInstanceId,
				Expression = request.Expression,
				Value = val,
				Type = type,
				IsValid = isValid,
				FrameIndex = targetFrameIndex
			};
		}
		catch (Exception ex) when (ex is not OutOfMemoryException && ex is not OperationCanceledException)
		{
			return new DebuggerEvaluateExprResponse
			{
				VsInstanceId = _vsInstanceId,
				Expression = request.Expression,
				Value = $"<Evaluation error: {ex.Message}>",
				Type = "error",
				IsValid = false,
				FrameIndex = targetFrameIndex
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

	private async Task<Debugger> GetDebuggerAsync(CancellationToken cancellationToken)
	{
		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var dte = await _package.GetServiceAsync(typeof(DTE)) as DTE2
			?? throw new DebuggerProviderException(BridgeErrorCodes.DebuggerUnavailable, "The Visual Studio DTE service is unavailable.");
		return dte.Debugger
			?? throw new DebuggerProviderException(BridgeErrorCodes.DebuggerUnavailable, "The Visual Studio debugger is unavailable.");
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

	private static StackFrameInfo ReadStackFrame(StackFrame frame, int index)
	{
		ThreadHelper.ThrowIfNotOnUIThread();
		var functionName = "unknown";
		string? language = null;
		string? module = null;

		try { functionName = frame.FunctionName ?? "unknown"; } catch { }
		try { language = frame.Language; } catch { }
		try { module = frame.Module; } catch { }

		string? fileName = null;
		int? lineNumber = null;

		try
		{
			var inIdx = functionName.LastIndexOf(" in ", StringComparison.OrdinalIgnoreCase);
			var lineIdx = functionName.LastIndexOf(":line ", StringComparison.OrdinalIgnoreCase);
			if (inIdx >= 0 && lineIdx > inIdx)
			{
				fileName = functionName.Substring(inIdx + 4, lineIdx - (inIdx + 4)).Trim();
				var lineStr = functionName.Substring(lineIdx + 6).Trim();
				if (int.TryParse(lineStr, out var parsedLine))
				{
					lineNumber = parsedLine;
				}
			}
			else
			{
				var altLineIdx = functionName.LastIndexOf(" Line ", StringComparison.OrdinalIgnoreCase);
				if (altLineIdx >= 0)
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

		return new StackFrameInfo
		{
			FrameIndex = index,
			FunctionName = functionName,
			FileName = string.IsNullOrWhiteSpace(fileName) ? null : fileName,
			LineNumber = lineNumber > 0 ? lineNumber : null,
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
